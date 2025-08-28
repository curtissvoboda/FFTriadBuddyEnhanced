using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using System.Linq;
using System.Numerics;

namespace TriadBuddyPlugin
{
    public class PluginWindowPvPStats : Window, IDisposable
    {
        private readonly PvPMatchTracker pvpTracker;
        private string selectedOpponent = "";
        private bool showDetailedStats = false;

        public PluginWindowPvPStats(PvPMatchTracker pvpTracker) : base("PvP Statistics")
        {
            this.pvpTracker = pvpTracker;
            
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(600, 400),
                MaximumSize = new Vector2(1200, 800)
            };
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        public override void Draw()
        {
            var matchHistory = pvpTracker.GetMatchHistory();
            var opponentProfiles = pvpTracker.GetOpponentProfiles();

            ImGui.Text($"Total PvP Matches: {matchHistory.Count}");
            
            if (matchHistory.Count > 0)
            {
                var wins = matchHistory.Count(m => m.Won);
                var winRate = (float)wins / matchHistory.Count * 100f;
                ImGui.Text($"Overall Win Rate: {winRate:F1}%");
            }

            ImGui.Separator();

            if (ImGui.BeginTabBar("PvPStatsTabs"))
            {
                if (ImGui.BeginTabItem("Match History"))
                {
                    DrawMatchHistory(matchHistory);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Opponent Analysis"))
                {
                    DrawOpponentAnalysis(opponentProfiles);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Card Performance"))
                {
                    DrawCardPerformance();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawMatchHistory(System.Collections.Generic.List<PvPMatchTracker.PvPMatchData> matches)
        {
            if (ImGui.BeginTable("MatchHistoryTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Date");
                ImGui.TableSetupColumn("Opponent");
                ImGui.TableSetupColumn("Result");
                ImGui.TableSetupColumn("Duration");
                ImGui.TableSetupColumn("Rules");
                ImGui.TableHeadersRow();

                foreach (var match in matches.TakeLast(50)) // Show last 50 matches
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(match.MatchTime.ToString("MM/dd HH:mm"));
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(match.OpponentName);
                    
                    ImGui.TableNextColumn();
                    ImGui.TextColored(match.Won ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 0, 1), 
                                     match.Won ? "WIN" : "LOSS");
                    
                    ImGui.TableNextColumn();
                    ImGui.Text($"{match.MatchDuration:F1}s");
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(string.Join(", ", match.Rules));
                }

                ImGui.EndTable();
            }
        }

        private void DrawOpponentAnalysis(System.Collections.Generic.Dictionary<string, PvPMatchTracker.OpponentProfile> profiles)
        {
            if (ImGui.BeginCombo("Select Opponent", selectedOpponent))
            {
                foreach (var opponent in profiles.Keys)
                {
                    if (ImGui.Selectable(opponent, selectedOpponent == opponent))
                    {
                        selectedOpponent = opponent;
                    }
                }
                ImGui.EndCombo();
            }

            if (!string.IsNullOrEmpty(selectedOpponent) && profiles.TryGetValue(selectedOpponent, out var profile))
            {
                ImGui.Text($"Win Rate Against: {profile.WinRateAgainst * 100:F1}%");
                ImGui.Text($"Average Move Time: {profile.AverageMovetime:F1}s");
                ImGui.Text($"Last Encounter: {profile.LastEncounter:MM/dd/yyyy}");

                ImGui.Separator();
                ImGui.Text("Preferred Cards:");
                foreach (var cardId in profile.PreferredCards.Take(10))
                {
                    var card = TriadCardDB.Get().FindById(cardId);
                    if (card != null)
                    {
                        ImGui.BulletText(card.Name.GetLocalized());
                    }
                }

                ImGui.Separator();
                ImGui.Text("Position Preferences:");
                foreach (var pos in profile.PositionPreferences.OrderByDescending(x => x.Value))
                {
                    ImGui.Text($"Position {pos.Key}: {pos.Value * 100:F1}%");
                }
            }
        }

        private void DrawCardPerformance()
        {
            ImGui.Text("Card performance data would be displayed here");
            ImGui.Text("This would show win rates and effectiveness for each card in PvP");
        }
    }
}