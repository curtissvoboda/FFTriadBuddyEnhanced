using Dalamud;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MgAl2O4.Utils;
using System;
using System.Threading.Tasks;

namespace TriadBuddyPlugin
{
    public class Plugin : IDalamudPlugin
    {
        public string Name => "Triad Buddy Enhanced";

        private readonly WindowSystem windowSystem = new("TriadBuddyEnhanced");

        private readonly PluginWindowStatus statusWindow;
        private readonly CommandInfo statusCommand;

        private readonly UIReaderTriadGame uiReaderGame;
        private readonly UIReaderTriadPrep uiReaderPrep;
        private readonly UIReaderTriadCardList uiReaderCardList;
        private readonly UIReaderTriadDeckEdit uiReaderDeckEdit;
        private readonly StatTracker statTracker;
        private readonly GameDataLoader dataLoader;
        private readonly UIReaderScheduler uiReaderScheduler;
        private readonly PluginOverlays overlays;
        private readonly Localization locManager;

        // Enhanced PvP components
        private readonly EnhancedCardDatabase enhancedDatabase;
        private readonly PvPMatchTracker pvpTracker;
        private readonly AggressiveDeckBuilder deckBuilder;

        public static Localization? CurrentLocManager;
        private string[] supportedLangCodes = { "de", "en", "es", "fr", "ja", "ko", "zh" };

        [PluginService] internal static IDalamudPluginInterface pluginInterface { get; private set; } = null!;

        public Plugin()
        {
            pluginInterface.Create<Service>();
#if DEBUG
            MgAl2O4.Utils.Logger.logger = Service.logger;
#endif // DEBUG

            Service.plugin = this;
            Service.pluginInterface = pluginInterface;
            Service.pluginConfig = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

            // Initialize enhanced PvP components
            enhancedDatabase = new EnhancedCardDatabase();
            pvpTracker = new PvPMatchTracker(enhancedDatabase);
            deckBuilder = new AggressiveDeckBuilder(enhancedDatabase);

            // Start background database update
            Task.Run(async () =>
            {
                if (enhancedDatabase.ShouldUpdate())
                {
                    await enhancedDatabase.UpdateFromGitHub();
                }
            });

            // prep utils
            var myAssemblyName = GetType().Assembly.GetName().Name;
            locManager = new Localization($"{myAssemblyName}.assets.loc.", "", true);
            locManager.SetupWithLangCode(pluginInterface.UiLanguage);
            CurrentLocManager = locManager;

            dataLoader = new GameDataLoader();
            dataLoader.StartAsyncWork();

            SolverUtils.CreateSolvers();
            if (Service.pluginConfig != null && Service.pluginConfig.CanUseProfileReader && SolverUtils.solverPreGameDecks != null)
            {
                SolverUtils.solverPreGameDecks.profileGS = new UnsafeReaderProfileGS();
            }

            statTracker = new StatTracker();

            // prep data scrapers
            uiReaderGame = new UIReaderTriadGame();
            uiReaderGame.OnUIStateChanged += (state) => 
            { 
                if (state != null) 
                { 
                    SolverUtils.solverGame?.UpdateGame(state);
                    
                    // Track PvP matches
                    if (state.isPvP)
                    {
                        TrackPvPMatch(state);
                    }
                } 
            };

            uiReaderPrep = new UIReaderTriadPrep();
            uiReaderPrep.shouldScanDeckData = (SolverUtils.solverPreGameDecks?.profileGS == null) || SolverUtils.solverPreGameDecks.profileGS.HasErrors;
            uiReaderPrep.OnUIStateChanged += (state) => SolverUtils.solverPreGameDecks?.UpdateDecks(state);

            uiReaderCardList = new UIReaderTriadCardList();
            uiReaderDeckEdit = new UIReaderTriadDeckEdit();

            var uiReaderMatchResults = new UIReaderTriadResults();
            uiReaderMatchResults.OnUpdated += (state) => 
            { 
                if (SolverUtils.solverGame != null) 
                { 
                    statTracker.OnMatchFinished(SolverUtils.solverGame, state);
                    
                    // Record PvP match completion
                    if (SolverUtils.solverGame.isPvPMode)
                    {
                        RecordPvPMatchCompletion(state);
                    }
                } 
            };

            uiReaderScheduler = new UIReaderScheduler(Service.gameGui);
            uiReaderScheduler.AddObservedAddon(uiReaderGame);
            uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderMatchRequest);
            uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderDeckSelect);
            uiReaderScheduler.AddObservedAddon(uiReaderCardList);
            uiReaderScheduler.AddObservedAddon(uiReaderDeckEdit);
            uiReaderScheduler.AddObservedAddon(uiReaderMatchResults);

            var memReaderTriadFunc = new UnsafeReaderTriadCards();
            GameCardDB.Get().memReader = memReaderTriadFunc;
            GameNpcDB.Get().memReader = memReaderTriadFunc;

            uiReaderDeckEdit.unsafeDeck = new UnsafeReaderTriadDeck();

            // prep UI
            overlays = new PluginOverlays(uiReaderGame, uiReaderPrep);
            statusWindow = new PluginWindowStatus(uiReaderGame, uiReaderPrep);
            windowSystem.AddWindow(statusWindow);

            var npcStatsWindow = new PluginWindowNpcStats(statTracker);
            var deckOptimizerWindow = new PluginWindowDeckOptimize(uiReaderDeckEdit);
            var deckEvalWindow = new PluginWindowDeckEval(uiReaderPrep, deckOptimizerWindow, npcStatsWindow);
            deckOptimizerWindow.OnConfigRequested += () => OnOpenConfig();
            windowSystem.AddWindow(deckEvalWindow);
            windowSystem.AddWindow(deckOptimizerWindow);
            windowSystem.AddWindow(npcStatsWindow);

            windowSystem.AddWindow(new PluginWindowCardInfo(uiReaderCardList));
            windowSystem.AddWindow(new PluginWindowCardSearch(uiReaderCardList, npcStatsWindow));
            windowSystem.AddWindow(new PluginWindowDeckSearch(uiReaderDeckEdit));

            // Add PvP-specific windows
            windowSystem.AddWindow(new PluginWindowPvPStats(pvpTracker));
            windowSystem.AddWindow(new PluginWindowPvPDeckBuilder(deckBuilder, enhancedDatabase));

            // prep plugin hooks
            statusCommand = new(OnCommand) { HelpMessage = string.Format(Localization.Localize("Cmd_Status", "Show state of {0} plugin"), Name) };
            Service.commandManager.AddHandler("/triadbuddy", statusCommand);

            // Enhanced PvP commands
            var pvpCommand = new CommandInfo(OnPvPCommand) { HelpMessage = "PvP Triple Triad commands" };
            Service.commandManager.AddHandler("/pvptriad", pvpCommand);

            pluginInterface.LanguageChanged += OnLanguageChanged;
            pluginInterface.UiBuilder.Draw += OnDraw;
            pluginInterface.UiBuilder.OpenConfigUi += OnOpenConfig;
            pluginInterface.UiBuilder.OpenMainUi += () => OnCommand("", "");

            Service.framework.Update += Framework_Update;

            Service.logger.Info("Triad Buddy Enhanced loaded with comprehensive PvP support!");
        }

        private void OnPvPCommand(string command, string args)
        {
            switch (args.ToLower())
            {
                case "stats":
                    // Open PvP stats window
                    var pvpStatsWindow = windowSystem.Windows.OfType<PluginWindowPvPStats>().FirstOrDefault();
                    if (pvpStatsWindow != null) pvpStatsWindow.IsOpen = true;
                    break;
                case "deck":
                    // Open PvP deck builder
                    var deckBuilderWindow = windowSystem.Windows.OfType<PluginWindowPvPDeckBuilder>().FirstOrDefault();
                    if (deckBuilderWindow != null) deckBuilderWindow.IsOpen = true;
                    break;
                case "update":
                    // Force database update
                    Task.Run(async () => await enhancedDatabase.UpdateFromGitHub());
                    Service.chatGui.Print("Updating card database from GitHub...");
                    break;
                case "export":
                    // Export match data
                    pvpTracker.ExportMatchData();
                    Service.chatGui.Print("Match data exported!");
                    break;
                default:
                    Service.chatGui.Print("PvP Commands: /pvptriad stats|deck|update|export");
                    break;
            }
        }

        private void TrackPvPMatch(UIStateTriadGame state)
        {
            // Implementation for tracking ongoing PvP match
            // This would record moves, timing, etc.
        }

        private void RecordPvPMatchCompletion(UIReaderTriadResults results)
        {
            // Implementation for recording completed PvP match
            // This would finalize the match record and update statistics
        }

        private void OnLanguageChanged(string langCode)
        {
            if (Array.Find(supportedLangCodes, x => x == langCode) != null)
            {
                locManager.SetupWithLangCode(langCode);
            }
            else
            {
                locManager.SetupWithFallbacks();
            }

            statusCommand.HelpMessage = string.Format(Localization.Localize("Cmd_Status", "Show state of {0} plugin"), Name);
        }

        public void Dispose()
        {
            Service.commandManager.RemoveHandler("/triadbuddy");
            Service.commandManager.RemoveHandler("/pvptriad");
            Service.framework.Update -= Framework_Update;
            windowSystem.RemoveAllWindows();
        }

        private void OnCommand(string command, string args)
        {
            statusWindow.showConfigs = false;
            statusWindow.IsOpen = true;
        }

        private void OnDraw()
        {
            windowSystem.Draw();
            overlays.OnDraw();
        }

        private void OnOpenConfig()
        {
            statusWindow.showConfigs = true;
            statusWindow.IsOpen = true;
        }

        private void Framework_Update(IFramework framework)
        {
            try
            {
                if (dataLoader.IsDataReady)
                {
                    float deltaSeconds = (float)framework.UpdateDelta.TotalSeconds;
                    uiReaderScheduler.Update(deltaSeconds);

                    // Periodic database updates
                    if (enhancedDatabase.ShouldUpdate())
                    {
                        Task.Run(async () => await enhancedDatabase.UpdateFromGitHub());
                    }
                }
            }
            catch (Exception ex)
            {
                Service.logger.Error(ex, "state update failed");
            }
        }
    }
}
