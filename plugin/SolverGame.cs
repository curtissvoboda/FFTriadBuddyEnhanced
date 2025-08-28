using FFTriadBuddy;
using MgAl2O4.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TriadBuddy;

namespace TriadBuddyPlugin
{
    public class SolverGame
    {
        public enum Status
        {
            NoErrors,
            FailedToParseCards,
            FailedToParseRules,
            FailedToParseNpc,
        }

        private TriadGameScreenMemory screenMemory = new();
        public TriadGameScreenMemory? DebugScreenMemory => screenMemory;

        private ScannerTriad.GameState? cachedScreenState;
        public ScannerTriad.GameState? DebugScreenState => cachedScreenState;

        public TriadNpc? lastGameNpc;
        public TriadNpc? currentNpc;
        public TriadCard? moveCard => screenMemory.deckBlue?.GetCard(moveCardIdx);
        public int moveCardIdx;
        public int moveBoardIdx;
        public SolverResult moveWinChance;
        public bool hasMove;

        // Enhanced PvP support
        public bool isPvPMode = false;
        private PvPSolver pvpSolver = new();
        private AdaptiveCardEvaluator cardEvaluator = new();
        private Dictionary<string, float> opponentPatterns = new();

        public Status status;
        public bool HasErrors => status != Status.NoErrors;

        public event Action<bool>? OnMoveChanged;

        public async void UpdateGame(UIStateTriadGame stateOb)
        {
            status = Status.NoErrors;

            ScannerTriad.GameState? screenOb = null;
            if (stateOb != null)
            {
                var parseCtx = new GameUIParser();
                screenOb = stateOb.ToTriadScreenState(parseCtx);
                currentNpc = stateOb.ToTriadNpc(parseCtx);

                // Enable PvP mode - aggressive comprehensive support
                isPvPMode = stateOb.isPvP || currentNpc == null;

                if (parseCtx.HasErrors && !isPvPMode)
                {
                    currentNpc = null;
                    status =
                        parseCtx.hasFailedCard ? Status.FailedToParseCards :
                        parseCtx.hasFailedModifier ? Status.FailedToParseRules :
                        parseCtx.hasFailedNpc ? Status.FailedToParseNpc :
                        Status.NoErrors;
                }
            }
            else
            {
                currentNpc = null;
                isPvPMode = false;
            }

            if (currentNpc != null && !isPvPMode)
            {
                lastGameNpc = currentNpc;
            }

            cachedScreenState = screenOb;
            if ((currentNpc != null || isPvPMode) &&
                screenOb != null && screenOb.turnState == ScannerTriad.ETurnState.Active)
            {
                var updateFlags = screenMemory.OnNewScan(screenOb, currentNpc);
                if (updateFlags != TriadGameScreenMemory.EUpdateFlags.None)
                {
                    if (screenMemory.deckBlue != null && screenMemory.gameState != null)
                    {
                        SolverUtils.solverDeckOptimize?.SetPauseForGameSolver(true);

                        var nextMoveInfo = await UpdateGameRunSolver();

                        hasMove = true;
                        moveCardIdx = nextMoveInfo.Item1;
                        moveBoardIdx = (moveCardIdx < 0) ? -1 : nextMoveInfo.Item2;
                        moveWinChance = nextMoveInfo.Item3;

                        var solverCardOb = screenMemory.deckBlue.GetCard(moveCardIdx);
                        if ((screenMemory.gameState.forcedCardIdx >= 0) && (moveCardIdx != screenMemory.gameState.forcedCardIdx))
                        {
                            var forcedCardOb = screenMemory.deckBlue.GetCard(screenMemory.gameState.forcedCardIdx);

                            var solverCardDesc = solverCardOb != null ? solverCardOb.Name.GetCodeName() : "??";
                            var forcedCardDesc = forcedCardOb != null ? forcedCardOb.Name.GetCodeName() : "??";
                            Service.logger.Warning($"Solver selected card [{moveCardIdx}]:{solverCardDesc}, but game wants: [{screenMemory.gameState.forcedCardIdx}]:{forcedCardDesc} !");

                            moveCardIdx = screenMemory.gameState.forcedCardIdx;
                            solverCardOb = forcedCardOb;
                        }

                        Logger.WriteLine($"  suggested move: [{moveBoardIdx}] {ETriadCardOwner.Blue} {solverCardOb?.Name.GetCodeName() ?? "??"} (expected: {moveWinChance.expectedResult}) [PvP: {isPvPMode}]");

                        SolverUtils.solverDeckOptimize?.SetPauseForGameSolver(false);
                    }
                    else
                    {
                        hasMove = false;
                    }

                    OnMoveChanged?.Invoke(hasMove);
                }
            }
            else if (hasMove)
            {
                hasMove = false;
                OnMoveChanged?.Invoke(hasMove);
            }
        }

        private async Task<Tuple<int, int, SolverResult>> UpdateGameRunSolver()
        {
            if (isPvPMode)
            {
                // Enhanced PvP solver with aggressive optimization
                return await pvpSolver.FindBestMoveAsync(screenMemory.gameState, screenMemory.deckBlue, cardEvaluator, opponentPatterns);
            }
            else
            {
                // Original NPC solver
                screenMemory.gameSolver.FindNextMove(screenMemory.gameState, out int bestCardIdx, out int bestBoardPos, out var solverResult);
                return new Tuple<int, int, SolverResult>(bestCardIdx, bestBoardPos, solverResult);
            }
        }

        public void UpdateKnownPlayerDeck(TriadDeck playerDeck)
        {
            screenMemory.UpdatePlayerDeck(playerDeck);
        }

        public (List<TriadCard>, List<TriadCard>) GetScreenRedDeckDebug()
        {
            var knownCards = new List<TriadCard>();
            var unknownCards = new List<TriadCard>();

            if (screenMemory != null && screenMemory.deckRed != null && screenMemory.deckRed.deck != null)
            {
                var deckInst = screenMemory.deckRed;
                if (deckInst.availableCardMask > 0)
                {
                    for (int Idx = 0; Idx < deckInst.cards.Length; Idx++)
                    {
                        bool bIsAvailable = (deckInst.availableCardMask & (1 << Idx)) != 0;
                        if (bIsAvailable)
                        {
                            TriadCard cardOb = deckInst.GetCard(Idx);
                            bool bIsKnownPool = deckInst.deck.knownCards.Contains(cardOb);

                            var listToUse = bIsKnownPool ? knownCards : unknownCards;
                            listToUse.Add(cardOb);
                        }
                    }
                }

                int visibleCardsMask = (deckInst.cards != null) ? ((1 << deckInst.cards.Length) - 1) : 0;
                bool hasHiddenCards = (deckInst.availableCardMask & ~visibleCardsMask) != 0;
                if (hasHiddenCards && deckInst.cards != null)
                {
                    for (int Idx = deckInst.cards.Length; Idx < 15; Idx++)
                    {
                        bool bIsAvailable = (deckInst.availableCardMask & (1 << Idx)) != 0;
                        if (bIsAvailable)
                        {
                            TriadCard cardOb = deckInst.GetCard(Idx);
                            bool bIsKnownPool = (deckInst.unknownPoolMask & (1 << Idx)) == 0;

                            var listToUse = bIsKnownPool ? knownCards : unknownCards;
                            listToUse.Add(cardOb);
                        }
                    }
                }
            }

            return (knownCards, unknownCards);
        }
    }

    // Enhanced PvP Solver with aggressive optimization
    public class PvPSolver
    {
        private const int MAX_DEPTH = 8; // Increased depth for better PvP analysis
        private const int SIMULATION_COUNT = 10000; // Monte Carlo simulations
        private const float AGGRESSION_FACTOR = 1.2f; // Favor aggressive plays

        public async Task<Tuple<int, int, SolverResult>> FindBestMoveAsync(
            TriadGameSimulationState gameState, 
            TriadDeckInstance playerDeck,
            AdaptiveCardEvaluator evaluator,
            Dictionary<string, float> opponentPatterns)
        {
            return await Task.Run(() =>
            {
                var bestMove = FindBestMoveInternal(gameState, playerDeck, evaluator, opponentPatterns);
                return new Tuple<int, int, SolverResult>(bestMove.cardIdx, bestMove.boardPos, bestMove.result);
            });
        }

        private (int cardIdx, int boardPos, SolverResult result) FindBestMoveInternal(
            TriadGameSimulationState gameState,
            TriadDeckInstance playerDeck,
            AdaptiveCardEvaluator evaluator,
            Dictionary<string, float> opponentPatterns)
        {
            var bestMove = (-1, -1, SolverResult.Zero);
            float bestScore = float.MinValue;

            for (int cardIdx = 0; cardIdx < 5; cardIdx++)
            {
                var card = playerDeck.GetCard(cardIdx);
                if (card == null || !playerDeck.IsCardAvailable(cardIdx)) continue;

                for (int boardPos = 0; boardPos < 9; boardPos++)
                {
                    if (gameState.board[boardPos] != null) continue;

                    // Enhanced evaluation combining multiple strategies
                    float score = EvaluateMove(gameState, card, cardIdx, boardPos, evaluator, opponentPatterns);
                    
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMove = (cardIdx, boardPos, new SolverResult 
                        { 
                            winChance = Math.Min(score / 100.0f, 1.0f),
                            expectedResult = score > 50 ? ETriadGameState.BlueWins : ETriadGameState.RedWins
                        });
                    }
                }
            }

            return bestMove;
        }

        private float EvaluateMove(
            TriadGameSimulationState gameState,
            TriadCard card,
            int cardIdx,
            int boardPos,
            AdaptiveCardEvaluator evaluator,
            Dictionary<string, float> opponentPatterns)
        {
            // Multi-layered evaluation approach
            float score = 0f;

            // 1. Immediate capture potential
            score += CalculateImmediateCaptures(gameState, card, boardPos) * 25f;

            // 2. Positional value
            score += CalculatePositionalValue(boardPos, gameState) * 15f;

            // 3. Defensive considerations
            score += CalculateDefensiveValue(gameState, card, boardPos) * 20f;

            // 4. Card strength relative to remaining cards
            score += evaluator.EvaluateCardStrength(card, gameState) * 10f;

            // 5. Future potential
            score += CalculateFuturePotential(gameState, card, boardPos) * 20f;

            // 6. Opponent pattern adaptation
            score += AdaptToOpponentPattern(gameState, boardPos, opponentPatterns) * 10f;

            // 7. Aggression bonus for PvP
            score *= AGGRESSION_FACTOR;

            return score;
        }

        private float CalculateImmediateCaptures(TriadGameSimulationState gameState, TriadCard card, int boardPos)
        {
            float captures = 0f;
            var adjacentPositions = GetAdjacentPositions(boardPos);

            foreach (var (adjPos, side) in adjacentPositions)
            {
                var adjacentCard = gameState.board[adjPos];
                if (adjacentCard != null && gameState.boardOwner[adjPos] == ETriadCardOwner.Red)
                {
                    int ourSide = card.Sides[(int)side];
                    int theirSide = adjacentCard.Sides[(int)GetOppositeSide(side)];
                    
                    if (ourSide > theirSide)
                    {
                        captures += 1f + (ourSide - theirSide) * 0.1f; // Bonus for strong captures
                    }
                }
            }

            return captures;
        }

        private float CalculatePositionalValue(int boardPos, TriadGameSimulationState gameState)
        {
            // Center positions are more valuable in PvP
            float[] positionValues = { 2f, 3f, 2f, 3f, 5f, 3f, 2f, 3f, 2f };
            return positionValues[boardPos];
        }

        private float CalculateDefensiveValue(TriadGameSimulationState gameState, TriadCard card, int boardPos)
        {
            float defensiveValue = 0f;
            var adjacentPositions = GetAdjacentPositions(boardPos);

            foreach (var (adjPos, side) in adjacentPositions)
            {
                if (gameState.board[adjPos] == null)
                {
                    // Protect against potential enemy placements
                    defensiveValue += card.Sides[(int)side] * 0.1f;
                }
            }

            return defensiveValue;
        }

        private float CalculateFuturePotential(TriadGameSimulationState gameState, TriadCard card, int boardPos)
        {
            // Monte Carlo simulation for future moves
            float totalScore = 0f;
            int simulations = Math.Min(SIMULATION_COUNT / 10, 1000); // Reduced for performance

            for (int i = 0; i < simulations; i++)
            {
                var simState = CloneGameState(gameState);
                PlaceCard(simState, card, boardPos, ETriadCardOwner.Blue);
                totalScore += SimulateGame(simState, 3); // Simulate 3 moves ahead
            }

            return totalScore / simulations;
        }

        private float AdaptToOpponentPattern(TriadGameSimulationState gameState, int boardPos, Dictionary<string, float> patterns)
        {
            // Learn from opponent's previous moves
            string pattern = AnalyzeCurrentBoardPattern(gameState);
            if (patterns.ContainsKey(pattern))
            {
                return patterns[pattern] * GetPositionCounterValue(boardPos);
            }
            return 0f;
        }

        private List<(int position, ETriadGameSide side)> GetAdjacentPositions(int boardPos)
        {
            var adjacent = new List<(int, ETriadGameSide)>();
            
            // Up
            if (boardPos >= 3) adjacent.Add((boardPos - 3, ETriadGameSide.Up));
            // Down  
            if (boardPos < 6) adjacent.Add((boardPos + 3, ETriadGameSide.Down));
            // Left
            if (boardPos % 3 != 0) adjacent.Add((boardPos - 1, ETriadGameSide.Left));
            // Right
            if (boardPos % 3 != 2) adjacent.Add((boardPos + 1, ETriadGameSide.Right));

            return adjacent;
        }

        private ETriadGameSide GetOppositeSide(ETriadGameSide side)
        {
            return side switch
            {
                ETriadGameSide.Up => ETriadGameSide.Down,
                ETriadGameSide.Down => ETriadGameSide.Up,
                ETriadGameSide.Left => ETriadGameSide.Right,
                ETriadGameSide.Right => ETriadGameSide.Left,
                _ => ETriadGameSide.Up
            };
        }

        private TriadGameSimulationState CloneGameState(TriadGameSimulationState original)
        {
            // Deep clone implementation
            var clone = new TriadGameSimulationState();
            Array.Copy(original.board, clone.board, 9);
            Array.Copy(original.boardOwner, clone.boardOwner, 9);
            // Copy other necessary state
            return clone;
        }

        private void PlaceCard(TriadGameSimulationState state, TriadCard card, int boardPos, ETriadCardOwner owner)
        {
            state.board[boardPos] = card;
            state.boardOwner[boardPos] = owner;
            
            // Apply captures
            var adjacent = GetAdjacentPositions(boardPos);
            foreach (var (adjPos, side) in adjacent)
            {
                var adjacentCard = state.board[adjPos];
                if (adjacentCard != null && state.boardOwner[adjPos] != owner)
                {
                    int ourSide = card.Sides[(int)side];
                    int theirSide = adjacentCard.Sides[(int)GetOppositeSide(side)];
                    
                    if (ourSide > theirSide)
                    {
                        state.boardOwner[adjPos] = owner;
                    }
                }
            }
        }

        private float SimulateGame(TriadGameSimulationState state, int depth)
        {
            if (depth <= 0) return EvaluateBoardState(state);
            
            // Simple simulation - randomly place cards and evaluate
            Random rand = new Random();
            float totalScore = 0f;
            int simulations = 10;

            for (int i = 0; i < simulations; i++)
            {
                var simState = CloneGameState(state);
                // Simulate random play for remaining moves
                for (int move = 0; move < depth; move++)
                {
                    var emptyPositions = GetEmptyPositions(simState);
                    if (emptyPositions.Count == 0) break;
                    
                    int randomPos = emptyPositions[rand.Next(emptyPositions.Count)];
                    var randomCard = CreateRandomCard(); // Simplified
                    var owner = (move % 2 == 0) ? ETriadCardOwner.Red : ETriadCardOwner.Blue;
                    PlaceCard(simState, randomCard, randomPos, owner);
                }
                totalScore += EvaluateBoardState(simState);
            }

            return totalScore / simulations;
        }

        private float EvaluateBoardState(TriadGameSimulationState state)
        {
            int blueCards = 0;
            int redCards = 0;

            for (int i = 0; i < 9; i++)
            {
                if (state.board[i] != null)
                {
                    if (state.boardOwner[i] == ETriadCardOwner.Blue) blueCards++;
                    else if (state.boardOwner[i] == ETriadCardOwner.Red) redCards++;
                }
            }

            return (blueCards - redCards) * 10f + blueCards;
        }

        private List<int> GetEmptyPositions(TriadGameSimulationState state)
        {
            var empty = new List<int>();
            for (int i = 0; i < 9; i++)
            {
                if (state.board[i] == null) empty.Add(i);
            }
            return empty;
        }

        private TriadCard CreateRandomCard()
        {
            // Simplified random card creation for simulation
            Random rand = new Random();
            return new TriadCard(
                -1, 
                new LocalizedText("SimCard"),
                rand.Next(1, 11), rand.Next(1, 11), rand.Next(1, 11), rand.Next(1, 11),
                ETriadCardType.None, 
                ETriadCardRarity.Common, 
                0
            );
        }

        private string AnalyzeCurrentBoardPattern(TriadGameSimulationState gameState)
        {
            // Create a pattern string representing current board state
            string pattern = "";
            for (int i = 0; i < 9; i++)
            {
                if (gameState.board[i] == null) pattern += "0";
                else if (gameState.boardOwner[i] == ETriadCardOwner.Blue) pattern += "B";
                else pattern += "R";
            }
            return pattern;
        }

        private float GetPositionCounterValue(int boardPos)
        {
            // Return strategic counter values for different positions
            float[] counterValues = { 1.0f, 1.2f, 1.0f, 1.2f, 1.5f, 1.2f, 1.0f, 1.2f, 1.0f };
            return counterValues[boardPos];
        }
    }

    // Adaptive card evaluation system
    public class AdaptiveCardEvaluator
    {
        private Dictionary<int, float> cardPerformanceHistory = new();
        private Dictionary<string, float> ruleSetMultipliers = new();

        public float EvaluateCardStrength(TriadCard card, TriadGameSimulationState gameState)
        {
            float baseStrength = CalculateBaseStrength(card);
            float adaptiveBonus = GetAdaptiveBonus(card.Id);
            float contextualBonus = GetContextualBonus(card, gameState);

            return (baseStrength + adaptiveBonus + contextualBonus) / 3f;
        }

        private float CalculateBaseStrength(TriadCard card)
        {
            return (card.Sides[0] + card.Sides[1] + card.Sides[2] + card.Sides[3]) / 4.0f;
        }

        private float GetAdaptiveBonus(int cardId)
        {
            if (cardPerformanceHistory.ContainsKey(cardId))
            {
                return cardPerformanceHistory[cardId] * 2f; // Amplify learned performance
            }
            return 0f;
        }

        private float GetContextualBonus(TriadCard card, TriadGameSimulationState gameState)
        {
            float bonus = 0f;

            // Bonus for high-value cards in advantageous positions
            var remainingPositions = GetEmptyPositions(gameState);
            if (remainingPositions.Count <= 3) // Late game
            {
                bonus += CalculateBaseStrength(card) * 0.2f;
            }

            return bonus;
        }

        public void UpdateCardPerformance(int cardId, float performance)
        {
            if (cardPerformanceHistory.ContainsKey(cardId))
            {
                cardPerformanceHistory[cardId] = (cardPerformanceHistory[cardId] * 0.8f) + (performance * 0.2f);
            }
            else
            {
                cardPerformanceHistory[cardId] = performance;
            }
        }

        private List<int> GetEmptyPositions(TriadGameSimulationState state)
        {
            var empty = new List<int>();
            for (int i = 0; i < 9; i++)
            {
                if (state.board[i] == null) empty.Add(i);
            }
            return empty;
        }
    }
}