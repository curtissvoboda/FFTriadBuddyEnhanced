using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFTriadBuddy;

namespace TriadBuddyPlugin
{
    public class PluginWindowPvPDeckBuilder : Window, IDisposable
    {
        private readonly AggressiveDeckBuilder deckBuilder;
        private readonly EnhancedCardDatabase database;
        private List<string> selectedRules = new();
        private List<TriadCard> recommendedCards = new();
        private TriadDeck? builtDeck;

        public PluginWindowPvPDeckBuilder(AggressiveDeckBuilder deckBuilder, EnhancedCardDatabase database) 
            : base("PvP Deck Builder")
        {
            this.deckBuilder = deckBuilder;
            this.database = database;
            
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(800, 600),
                MaximumSize = new Vector2(1400, 1000)
            };
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        public override void Draw()
        {
            ImGui.Text("Aggressive PvP Deck Builder");
            ImGui.Separator();

            // Rule Selection
            if (ImGui.CollapsingHeader("Active Rules", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawRuleSelection();
            }

            ImGui.Separator();

            // Deck Building
            if (ImGui.Button("Build Optimal Deck"))
            {
                BuildOptimalDeck();
            }

            ImGui.SameLine();
            if (ImGui.Button("Refresh Database"))
            {
                RefreshDatabase();
            }

            ImGui.Separator();

            // Display recommended deck
            if (builtDeck != null)
            {
                DrawRecommendedDeck();
            }

            ImGui.Separator();

            // Card recommendations
            if (ImGui.CollapsingHeader("Card Analysis"))
            {
                DrawCardAnalysis();
            }
        }

        private void DrawRuleSelection()
        {
            // Common Triple Triad rules
            string[] availableRules = {
                "Open", "All Open", "Three Open", "Same", "Plus", "Combo", 
                "Sudden Death", "Random", "Order", "Chaos", "Reverse", 
                "Fallen Ace", "Ascension", "Descension", "Swap", "Draft"
            };

            ImGui.Text("Select Active Rules:");
            
            for (int i = 0; i < availableRules.Length; i += 3)
            {
                for (int j = 0; j < 3 && i + j < availableRules.Length; j++)
                {
                    if (j > 0) ImGui.SameLine();
                    
                    string rule = availableRules[i + j];
                    bool isSelected = selectedRules.Contains(rule);
                    
                    if (ImGui.Checkbox(rule, ref isSelected))
                    {
                        if (isSelected)
                        {
                            if (!selectedRules.Contains(rule))
                                selectedRules.Add(rule);
                        }
                        else
                        {
                            selectedRules.Remove(rule);
                        }
                    }
                }
            }
        }

        private void BuildOptimalDeck()
        {
            try
            {
                var ownedCards = PlayerSettingsDB.Get().ownedCards;
                var availableCards = ownedCards.Select(id => TriadCardDB.Get().FindById(id))
                                               .Where(card => card != null)
                                               .Cast<TriadCard>()
                                               .ToList();

                if (availableCards.Count >= 5)
                {
                    builtDeck = deckBuilder.BuildOptimalPvPDeck(selectedRules, availableCards);
                    Service.logger.Info($"Built optimal PvP deck with {builtDeck.knownCards.Count} cards");
                }
                else
                {
                    Service.logger.Warning("Not enough cards available to build deck");
                }
            }
            catch (Exception ex)
            {
                Service.logger.Error($"Failed to build deck: {ex.Message}");
            }
        }

        private void RefreshDatabase()
        {
            Task.Run(async () =>
            {
                var success = await database.UpdateFromGitHub();
                if (success)
                {
                    Service.logger.Info("Database updated successfully");
                }
            });
        }

        private void DrawRecommendedDeck()
        {
            ImGui.Text("Recommended Deck:");
            
            if (builtDeck?.knownCards != null)
            {
                foreach (var card in builtDeck.knownCards)
                {
                    if (card != null)
                    {
                        var enhancedData = database.GetEnhancedCardData(card.Id);
                        var effectiveness = enhancedData?.PvPEffectiveness ?? 0.5f;
                        
                        ImGui.Text($"• {card.Name.GetLocalized()} - Effectiveness: {effectiveness * 100:F0}%");
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), 
                                         $"[{card.Sides[0]}-{card.Sides[1]}-{card.Sides[2]}-{card.Sides[3]}]");
                    }
                }
            }
        }

        private void DrawCardAnalysis()
        {
            ImGui.Text("Top performing cards for selected rules:");
            
            var optimalCards = database.GetOptimalCardsForRules(selectedRules);
            var cardDB = TriadCardDB.Get();
            
            foreach (var cardId in optimalCards.Take(10))
            {
                var card = cardDB.FindById(cardId);
                var enhancedData = database.GetEnhancedCardData(cardId);
                
                if (card != null && enhancedData != null)
                {
                    ImGui.Text($"• {card.Name.GetLocalized()}");
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), $"{enhancedData.PvPEffectiveness * 100:F0}%");
                }
            }
        }
    }
}