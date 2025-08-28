using FFTriadBuddy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TriadBuddyPlugin
{
    public class EnhancedCardDatabase
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string GITHUB_CARDS_URL = "https://raw.githubusercontent.com/[YOUR_REPO]/main/cardlist.json";
        private const string GITHUB_RULES_URL = "https://raw.githubusercontent.com/[YOUR_REPO]/main/ruleset.json";

        private Dictionary<int, EnhancedCardData> enhancedCards = new();
        private Dictionary<string, RuleSetData> ruleSets = new();
        private DateTime lastUpdate = DateTime.MinValue;

        public class EnhancedCardData
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int[] Sides { get; set; } = new int[4];
            public int Rarity { get; set; }
            public int Type { get; set; }
            public float WinRate { get; set; }
            public float PvPEffectiveness { get; set; }
            public Dictionary<string, float> RuleModifiers { get; set; } = new();
            public List<int> CounterCards { get; set; } = new();
            public List<int> SynergyCards { get; set; } = new();
            public DateTime LastUpdated { get; set; }
        }

        public class RuleSetData
        {
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public Dictionary<string, float> CardTypeModifiers { get; set; } = new();
            public Dictionary<string, float> PositionalModifiers { get; set; } = new();
            public bool IsPvPOptimal { get; set; }
            public float AggressionMultiplier { get; set; } = 1.0f;
        }

        public async Task<bool> UpdateFromGitHub()
        {
            try
            {
                Service.logger.Info("Updating card database from GitHub...");

                // Download card data
                var cardJson = await httpClient.GetStringAsync(GITHUB_CARDS_URL);
                var cardData = JsonConvert.DeserializeObject<List<EnhancedCardData>>(cardJson);

                // Download rule data
                var ruleJson = await httpClient.GetStringAsync(GITHUB_RULES_URL);
                var ruleData = JsonConvert.DeserializeObject<List<RuleSetData>>(ruleJson);

                // Update local data
                enhancedCards.Clear();
                if (cardData != null)
                {
                    foreach (var card in cardData)
                    {
                        enhancedCards[card.Id] = card;
                    }
                }

                ruleSets.Clear();
                if (ruleData != null)
                {
                    foreach (var rule in ruleData)
                    {
                        ruleSets[rule.Name] = rule;
                    }
                }

                lastUpdate = DateTime.Now;
                Service.logger.Info($"Successfully updated database with {enhancedCards.Count} cards and {ruleSets.Count} rule sets");
                return true;
            }
            catch (Exception ex)
            {
                Service.logger.Error($"Failed to update database from GitHub: {ex.Message}");
                return false;
            }
        }

        public EnhancedCardData? GetEnhancedCardData(int cardId)
        {
            return enhancedCards.TryGetValue(cardId, out var data) ? data : null;
        }

        public RuleSetData? GetRuleSetData(string ruleName)
        {
            return ruleSets.TryGetValue(ruleName, out var data) ? data : null;
        }

        public void UpdateCardPerformance(int cardId, float winRate, float pvpEffectiveness)
        {
            if (enhancedCards.TryGetValue(cardId, out var card))
            {
                card.WinRate = (card.WinRate * 0.9f) + (winRate * 0.1f);
                card.PvPEffectiveness = (card.PvPEffectiveness * 0.9f) + (pvpEffectiveness * 0.1f);
                card.LastUpdated = DateTime.Now;
            }
        }

        public List<int> GetOptimalCardsForRules(List<string> activeRules)
        {
            var optimalCards = new List<int>();
            var ruleModifiers = new Dictionary<int, float>();

            foreach (var cardId in enhancedCards.Keys)
            {
                float totalModifier = 1.0f;
                var cardData = enhancedCards[cardId];

                foreach (var rule in activeRules)
                {
                    if (cardData.RuleModifiers.TryGetValue(rule, out var modifier))
                    {
                        totalModifier *= modifier;
                    }
                }

                ruleModifiers[cardId] = totalModifier * cardData.PvPEffectiveness;
            }

            var sortedCards = ruleModifiers.OrderByDescending(x => x.Value).Take(15).Select(x => x.Key);
            optimalCards.AddRange(sortedCards);

            return optimalCards;
        }

        public bool ShouldUpdate()
        {
            return DateTime.Now.Subtract(lastUpdate).TotalHours >= 1; // Update every hour
        }

        public async Task<string> ExportToJson()
        {
            var exportData = new
            {
                cards = enhancedCards.Values,
                rules = ruleSets.Values,
                lastUpdate = lastUpdate,
                version = "2.0"
            };

            return JsonConvert.SerializeObject(exportData, Formatting.Indented);
        }
    }

    public class AggressiveDeckBuilder
    {
        private readonly EnhancedCardDatabase database;
        private readonly TriadCardDB cardDB;

        public AggressiveDeckBuilder(EnhancedCardDatabase database)
        {
            this.database = database;
            this.cardDB = TriadCardDB.Get();
        }

        public TriadDeck BuildOptimalPvPDeck(List<string> activeRules, List<TriadCard> availableCards)
        {
            var optimalCardIds = database.GetOptimalCardsForRules(activeRules);
            var selectedCards = new List<TriadCard>();

            // Prioritize high PvP effectiveness cards
            foreach (var cardId in optimalCardIds)
            {
                var card = availableCards.FirstOrDefault(c => c.Id == cardId);
                if (card != null)
                {
                    selectedCards.Add(card);
                    if (selectedCards.Count >= 5) break;
                }
            }

            // Fill remaining slots with highest value cards
            while (selectedCards.Count < 5)
            {
                var remainingCards = availableCards.Except(selectedCards).ToList();
                if (remainingCards.Count == 0) break;

                var bestCard = remainingCards.OrderByDescending(c => CalculateCardValue(c, activeRules)).First();
                selectedCards.Add(bestCard);
            }

            return new TriadDeck(selectedCards);
        }

        private float CalculateCardValue(TriadCard card, List<string> activeRules)
        {
            float baseValue = (card.Sides[0] + card.Sides[1] + card.Sides[2] + card.Sides[3]) / 4.0f;
            var enhancedData = database.GetEnhancedCardData(card.Id);
            
            if (enhancedData != null)
            {
                baseValue *= enhancedData.PvPEffectiveness;
                
                foreach (var rule in activeRules)
                {
                    if (enhancedData.RuleModifiers.TryGetValue(rule, out var modifier))
                    {
                        baseValue *= modifier;
                    }
                }
            }

            return baseValue;
        }
    }
}