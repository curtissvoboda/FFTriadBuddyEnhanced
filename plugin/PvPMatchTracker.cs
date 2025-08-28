using System;
using System.Collections.Generic;
using System.Linq;

namespace TriadBuddyPlugin
{
    public class PvPMatchTracker
    {
        private List<PvPMatchData> matchHistory = new();
        private Dictionary<string, OpponentProfile> opponentProfiles = new();
        private EnhancedCardDatabase database;

        public class PvPMatchData
        {
            public DateTime MatchTime { get; set; }
            public string OpponentName { get; set; } = "";
            public List<int> PlayerCards { get; set; } = new();
            public List<int> OpponentCards { get; set; } = new();
            public List<string> Rules { get; set; } = new();
            public bool Won { get; set; }
            public List<MoveData> Moves { get; set; } = new();
            public float MatchDuration { get; set; }
        }

        public class MoveData
        {
            public int CardId { get; set; }
            public int Position { get; set; }
            public bool IsPlayerMove { get; set; }
            public float TimeToMove { get; set; }
            public int CapturedCards { get; set; }
        }

        public class OpponentProfile
        {
            public string Name { get; set; } = "";
            public float WinRateAgainst { get; set; }
            public List<int> PreferredCards { get; set; } = new();
            public Dictionary<int, float> PositionPreferences { get; set; } = new();
            public float AverageMovetime { get; set; }
            public List<string> StrategicPatterns { get; set; } = new();
            public DateTime LastEncounter { get; set; }
        }

        public PvPMatchTracker(EnhancedCardDatabase database)
        {
            this.database = database;
        }

        public void RecordMatch(PvPMatchData matchData)
        {
            matchHistory.Add(matchData);
            UpdateOpponentProfile(matchData);
            UpdateCardPerformance(matchData);
            
            // Keep only last 1000 matches for performance
            if (matchHistory.Count > 1000)
            {
                matchHistory.RemoveAt(0);
            }
        }

        // New methods for UI access
        public List<PvPMatchData> GetMatchHistory()
        {
            return matchHistory.ToList();
        }

        public Dictionary<string, OpponentProfile> GetOpponentProfiles()
        {
            return opponentProfiles.ToDictionary(x => x.Key, x => x.Value);
        }

        private void UpdateOpponentProfile(PvPMatchData match)
        {
            if (!opponentProfiles.TryGetValue(match.OpponentName, out var profile))
            {
                profile = new OpponentProfile { Name = match.OpponentName };
                opponentProfiles[match.OpponentName] = profile;
            }

            // Update win rate
            var matchesAgainstOpponent = matchHistory.Where(m => m.OpponentName == match.OpponentName).ToList();
            profile.WinRateAgainst = matchesAgainstOpponent.Count(m => m.Won) / (float)matchesAgainstOpponent.Count;

            // Update card preferences
            foreach (var cardId in match.OpponentCards)
            {
                if (!profile.PreferredCards.Contains(cardId))
                {
                    profile.PreferredCards.Add(cardId);
                }
            }

            // Update position preferences
            var opponentMoves = match.Moves.Where(m => !m.IsPlayerMove).ToList();
            foreach (var move in opponentMoves)
            {
                if (!profile.PositionPreferences.ContainsKey(move.Position))
                {
                    profile.PositionPreferences[move.Position] = 0;
                }
                profile.PositionPreferences[move.Position]++;
            }

            // Normalize position preferences
            var totalMoves = profile.PositionPreferences.Values.Sum();
            if (totalMoves > 0)
            {
                var keys = profile.PositionPreferences.Keys.ToList();
                foreach (var key in keys)
                {
                    profile.PositionPreferences[key] /= totalMoves;
                }
            }

            // Update average move time
            if (opponentMoves.Count > 0)
            {
                profile.AverageMovetime = opponentMoves.Average(m => m.TimeToMove);
            }

            profile.LastEncounter = match.MatchTime;
        }

        private void UpdateCardPerformance(PvPMatchData match)
        {
            foreach (var cardId in match.PlayerCards)
            {
                var cardMoves = match.Moves.Where(m => m.IsPlayerMove && m.CardId == cardId).ToList();
                if (cardMoves.Count > 0)
                {
                    float performance = cardMoves.Average(m => m.CapturedCards) / 3.0f; // Normalized to 0-1
                    if (match.Won) performance += 0.2f;
                    
                    database.UpdateCardPerformance(cardId, match.Won ? 1.0f : 0.0f, performance);
                }
            }
        }

        public OpponentProfile? GetOpponentProfile(string opponentName)
        {
            return opponentProfiles.TryGetValue(opponentName, out var profile) ? profile : null;
        }

        public List<int> GetRecommendedCounterCards(string opponentName)
        {
            var profile = GetOpponentProfile(opponentName);
            if (profile == null) return new List<int>();

            var counterCards = new List<int>();
            
            // Find cards that perform well against this opponent's preferred cards
            foreach (var opponentCardId in profile.PreferredCards)
            {
                var enhancedData = database.GetEnhancedCardData(opponentCardId);
                if (enhancedData != null)
                {
                    counterCards.AddRange(enhancedData.CounterCards);
                }
            }

            return counterCards.Distinct().ToList();
        }

        public Dictionary<int, float> GetPositionPredictions(string opponentName)
        {
            var profile = GetOpponentProfile(opponentName);
            return profile?.PositionPreferences ?? new Dictionary<int, float>();
        }

        public float GetWinPrediction(List<int> playerCards, List<string> rules, string opponentName)
        {
            var profile = GetOpponentProfile(opponentName);
            if (profile == null) return 0.5f;

            float basePrediction = profile.WinRateAgainst;
            
            // Adjust based on card matchups
            var counterCards = GetRecommendedCounterCards(opponentName);
            var countersInDeck = playerCards.Intersect(counterCards).Count();
            basePrediction += countersInDeck * 0.1f;

            // Adjust based on historical rule performance
            var ruleMatches = matchHistory.Where(m => 
                m.OpponentName == opponentName && 
                m.Rules.Intersect(rules).Count() == rules.Count).ToList();
            
            if (ruleMatches.Count > 0)
            {
                var ruleWinRate = ruleMatches.Count(m => m.Won) / (float)ruleMatches.Count;
                basePrediction = (basePrediction + ruleWinRate) / 2f;
            }

            return Math.Max(0f, Math.Min(1f, basePrediction));
        }

        public void ExportMatchData()
        {
            // Export match data for external analysis
            var exportData = new
            {
                matches = matchHistory,
                opponents = opponentProfiles,
                exportTime = DateTime.Now
            };

            // This could be saved to file or uploaded to GitHub
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(exportData, Newtonsoft.Json.Formatting.Indented);
            Service.logger.Info($"Match data exported: {matchHistory.Count} matches, {opponentProfiles.Count} opponents");
        }
    }
}