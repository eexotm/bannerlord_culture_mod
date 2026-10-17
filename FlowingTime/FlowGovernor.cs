using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FlowingTime
{
    /// <summary>
    /// Keeps the campaign clock running where vanilla would pause it for no reason other than UI.
    ///
    /// Vanilla pauses in these ways:
    ///  1. Generic transitions write TimeControlMode = Stop: GameMenu.ActivateGameMenu / SwitchToMenu /
    ///     ExitToLast, PlayerEncounter.Finish and GameMenu.EndWait. These are undone (see Transition*).
    ///  2. Game logic writes Stop on purpose: quest and issue menus, popups, sieges, deaths. These are
    ///     never undone. To tell them apart from (1), the clock is restarted right before a new menu's
    ///     init code runs, so a Stop written by that init stays visible and is respected.
    ///  3. "Stoppable" modes give no time while the party stands still (Campaign.TickMapTime +
    ///     MobileParty.ComputeIsWaiting): the arrival pause, and what freezes settlement menus.
    ///  4. Campaign.Tick zeroes the time step at non-wait menus (patched in CampaignTickMenuPatch).
    ///
    /// Time only flows at ordinary settlement menus (no battle, raid, siege or captivity involved) and
    /// on the map. Encounter, join-battle, captivity and siege menus pause exactly as in vanilla.
    /// The player's own pause, clock locks (popups, army management, incidents) and the escape menu are
    /// always respected.
    /// </summary>
    internal static class FlowGovernor
    {
        /// <summary>The clock is running because this mod restarted it.</summary>
        private static bool _runningByUs;

        /// <summary>The clock is stopped, and it is this mod's job to restart it once flow is allowed again.</summary>
        private static bool _suspendedByUs;

        // Nesting depth of generic transitions, and what they need to remember.
        private static int _transitionDepth;
        private static CampaignTimeControlMode _modeBeforeTransition;
        private static bool _restartedBeforeInit;

        // What the previous governed frame looked like, and whether this mod restarted the clock since.
        // Used to catch generic stops whose hook did not run (see BeforeTickMapTime).
        private static bool _lastFrameValid;
        private static bool _lastFrameAtMenu;
        private static bool _lastFrameWaitActive;
        private static bool _lastFrameRunning;
        private static bool _restartedSinceLastFrame;

        /// <summary>Set by SettlementWatchBehavior when the settlement the player is in changed under an open menu.</summary>
        public static bool MenuNeedsRefresh;

        public static void Reset()
        {
            _runningByUs = false;
            _suspendedByUs = false;
            _transitionDepth = 0;
            _restartedBeforeInit = false;
            _lastFrameValid = false;
            _restartedSinceLastFrame = false;
            MenuNeedsRefresh = false;
        }

        // ------------------------------------------------------------------
        // Per frame
        // ------------------------------------------------------------------

        /// <summary>Called right before Campaign.TickMapTime turns the time mode into this frame's time step.</summary>
        public static void BeforeTickMapTime()
        {
            Campaign campaign = Campaign.Current;
            if (campaign == null || !(Game.Current?.GameStateManager?.ActiveState is MapState mapState)) return;
            try
            {
                Govern(campaign, mapState);
            }
            finally
            {
                GameMenu menu = mapState.MenuContext?.GameMenu;
                _lastFrameValid = true;
                _lastFrameAtMenu = mapState.AtMenu;
                _lastFrameWaitActive = menu != null && menu.IsWaitActive;
                _lastFrameRunning = TimeIntent.IsRunning(campaign.TimeControlMode);
                _restartedSinceLastFrame = false;
            }
        }

        private static void Govern(Campaign campaign, MapState mapState)
        {
            CampaignTimeControlMode mode = campaign.TimeControlMode;

            if (!PatchState.AllActive)
            {
                // A required patch stopped working (e.g. another mod re-patched the same method). Hand the
                // clock back to vanilla, leaving it stopped if this mod was running it at a menu.
                if (_runningByUs && mapState.AtMenu && TimeIntent.IsRunning(mode)) campaign.TimeControlMode = CampaignTimeControlMode.Stop;
                Reset();
                return;
            }

            // Vanilla wait menus ("wait here", sieges, captivity, armies) drive the clock themselves.
            GameMenu menu = mapState.MenuContext?.GameMenu;
            if (menu != null && menu.IsWaitActive)
            {
                _runningByUs = false;
                _suspendedByUs = false;
                return;
            }

            // Fallback for generic stops whose hook did not run. GameMenu.EndWait and GameMenu.ExitToLast are
            // tiny methods the JIT may inline into their callers, which bypasses a Harmony patch. Since the
            // last frame the clock went from running to stopped, nothing restarted it, and either a wait menu
            // stopped waiting or a menu closed: treat it like the hooked transition would have.
            if (_lastFrameValid && _lastFrameRunning && !_restartedSinceLastFrame && !TimeIntent.IsRunning(mode)
                && ((_lastFrameWaitActive) || (_lastFrameAtMenu && !mapState.AtMenu)))
            {
                if (TimeIntent.WantsRunning) _suspendedByUs = true; // restarted below once flow is allowed
            }

            if (!TimeIntent.IsRunning(mode) && !_suspendedByUs) _runningByUs = false; // stopped by the game or the player

            if (!CanFlow(mapState))
            {
                // Flow is not allowed here right now (a popup opened, a siege started, ...). If this mod is
                // the reason the clock runs, stop it and remember to restart it later.
                if (_runningByUs && TimeIntent.IsRunning(mode) && !campaign.TimeControlModeLock)
                {
                    campaign.TimeControlMode = CampaignTimeControlMode.Stop;
                    _runningByUs = false;
                    _suspendedByUs = true;
                }
                return;
            }

            if (!TimeIntent.WantsRunning)
            {
                // Moving with the movement keys starts the clock (StoppablePlay) without SetTimeSpeed:
                // count that as the player pressing play.
                if (!mapState.AtMenu && (mode == CampaignTimeControlMode.StoppablePlay || mode == CampaignTimeControlMode.StoppableFastForward))
                    TimeIntent.FromMode(mode);
                else
                {
                    _suspendedByUs = false; // the player paused: nothing for this mod to restart
                    return;
                }
            }

            if (_suspendedByUs && !TimeIntent.IsRunning(mode))
            {
                Restart(campaign);
                return;
            }
            _suspendedByUs = false;

            // Vanilla never runs the clock at a settlement menu, so a running clock here (restarted by this
            // mod, or by the player's keys at the menu) is this mod's to stop if a popup or siege appears.
            if (mapState.AtMenu && TimeIntent.IsRunning(mode)) _runningByUs = true;

            // Stoppable modes give no time while the party stands still: always the case at a menu, and on
            // the map once the party arrives. Keep the clock going at normal speed: travel fast-forward
            // (double click) ends on arrival as in vanilla, instead of running the world at 4x indefinitely.
            if ((mode == CampaignTimeControlMode.StoppablePlay || mode == CampaignTimeControlMode.StoppableFastForward)
                && (mapState.AtMenu || (Settings.NoArrivalPause && MobileParty.MainParty.ComputeIsWaiting())))
            {
                campaign.TimeControlMode = CampaignTimeControlMode.UnstoppablePlay;
                _runningByUs = true;
            }
        }

        /// <summary>The map became the active screen again (back from a scene or another screen).</summary>
        public static void OnMapStateActivated()
        {
            _lastFrameValid = false; // the previous frame's snapshot is from before the other screen
        }

        // ------------------------------------------------------------------
        // Generic transitions
        // ------------------------------------------------------------------

        public static void TransitionStart()
        {
            if (_transitionDepth++ > 0) return;
            _modeBeforeTransition = Campaign.Current?.TimeControlMode ?? CampaignTimeControlMode.Stop;
            _restartedBeforeInit = false;
        }

        /// <summary>
        /// Called right before a menu's init code runs. If the transition just stopped the clock, restart
        /// it now, so a Stop written by the menu's own init is seen as deliberate.
        /// </summary>
        public static void BeforeMenuInit(GameMenu menu)
        {
            if (_transitionDepth == 0 || menu == null || menu.IsWaitMenu) return; // wait menus start their own wait
            Campaign campaign = Campaign.Current;
            if (campaign == null || TimeIntent.IsRunning(campaign.TimeControlMode)) return;
            if (!ShouldRestartAfterTransition()) return;
            if (!(Game.Current?.GameStateManager?.ActiveState is MapState mapState) || !CanFlow(mapState)) return;

            Restart(campaign);
            _restartedBeforeInit = true;
        }

        public static void TransitionEnd()
        {
            if (_transitionDepth == 0 || --_transitionDepth > 0) return;
            Campaign campaign = Campaign.Current;
            if (campaign == null || TimeIntent.IsRunning(campaign.TimeControlMode)) return;

            // Restarted before init but stopped again: the new menu (or a consequence) stopped it on purpose.
            if (_restartedBeforeInit) return;
            if (!ShouldRestartAfterTransition()) return;

            if (Game.Current?.GameStateManager?.ActiveState is MapState mapState && CanFlow(mapState)
                && !(mapState.MenuContext?.GameMenu?.IsWaitActive ?? false))
            {
                Restart(campaign);
            }
            else
            {
                // E.g. an encounter menu: stay paused like vanilla, restart once the player is back
                // somewhere time may flow.
                _suspendedByUs = true;
            }
        }

        private static bool ShouldRestartAfterTransition()
        {
            // Only undo a stop that the transition itself caused, from a running clock, while the player
            // wants time to run.
            return PatchState.AllActive && TimeIntent.WantsRunning && TimeIntent.IsRunning(_modeBeforeTransition);
        }

        // ------------------------------------------------------------------
        // Rules
        // ------------------------------------------------------------------

        /// <summary>
        /// Replacement for GameMenu.IsWaitActive at the two places that use it to mean "time may run at
        /// this menu": Campaign.Tick (whether to zero the time step) and MapScreen's input handling
        /// (whether the pause and speed keys work).
        /// </summary>
        public static bool IsTimeFlowMenu(GameMenu menu)
        {
            if (menu == null) return false;
            if (menu.IsWaitActive) return true;
            long t = PerfStats.Begin();
            try
            {
                return PatchState.AllActive
                    && Game.Current?.GameStateManager?.ActiveState is MapState mapState
                    && mapState.AtMenu
                    && CanFlow(mapState);
            }
            catch
            {
                return false; // called from inside game code: never throw, fall back to vanilla
            }
            finally
            {
                PerfStats.End(t);
            }
        }

        /// <summary>Whether campaign time may run in the current situation.</summary>
        public static bool CanFlow(MapState mapState)
        {
            Campaign campaign = Campaign.Current;
            MobileParty mainParty = MobileParty.MainParty;
            if (campaign == null || mainParty == null || campaign.TimeControlModeLock) return false;
            if (mapState.IsSimulationActive || mapState.MapConversationActive) return false;
            if (campaign.ConversationManager != null && campaign.ConversationManager.IsConversationFlowActive) return false;
            if (!Settings.FlowDuringPopups && InformationManager.IsAnyInquiryActive()) return false;

            // Battles: only the player's own field battle, at its decision menu, may flow. Raids, sieges,
            // sally-outs, hideouts, a battle the player is about to join, and captivity stay paused like
            // vanilla (their menus go stale or the battle resolves without the player when time passes).
            MapEvent battle = mainParty.MapEvent;
            if (battle != null && !IsOwnBattleAwaitingDecision(mapState, battle)) return false;
            if (PlayerCaptivity.IsCaptive) return false;
            if (PlayerEncounter.EncounteredParty != null)
            {
                MapEvent encounteredBattle = PlayerEncounter.EncounteredBattle;
                if (encounteredBattle != null && encounteredBattle != battle) return false;
            }

            if (!mapState.AtMenu) return battle == null;
            if (!Settings.FlowInMenus || mapState.MenuContext?.GameMenu == null) return false;
            if (battle != null) return true;

            // Other menus: only inside a settlement that is not being fought over.
            Settlement settlement = mainParty.CurrentSettlement;
            if (settlement == null || settlement.SiegeEvent != null || settlement.Party?.MapEvent != null) return false;
            if (settlement.Town?.GarrisonParty?.MapEvent != null) return false; // sally out
            return true;
        }

        /// <summary>
        /// The main party's own field battle, waiting at its menu for the player's decision. The game skips
        /// the player's own non-raid battle when it simulates battles (MapEventManager.Tick), so the enemy
        /// waits; parties arriving meanwhile join (EncounterJoining).
        /// </summary>
        public static bool IsOwnBattleAwaitingDecision(MapState mapState, MapEvent battle)
        {
            if (!Settings.FlowInOwnBattles || !mapState.AtMenu) return false;
            if (!battle.IsFieldBattle || battle.IsNavalMapEvent || battle.MapEventSettlement != null) return false;
            if (battle.IsFinalized || battle.HasWinner || battle.BattleState != BattleState.None) return false;
            if (PlayerEncounter.Current == null || PlayerEncounter.Battle != battle || PlayerEncounter.CurrentBattleSimulation != null) return false;

            // In someone else's army the army leader decides, as in vanilla.
            Army army = MobileParty.MainParty.Army;
            if (army != null && army.LeaderParty != MobileParty.MainParty) return false;

            // A party removed while in the battle (e.g. a quest party timing out) would leave the menu
            // pointing at it: stop rather than let the player act on it.
            return AllPartiesActive(battle, BattleSideEnum.Attacker) && AllPartiesActive(battle, BattleSideEnum.Defender);
        }

        private static bool AllPartiesActive(MapEvent battle, BattleSideEnum side)
        {
            var parties = battle.PartiesOnSide(side);
            for (int i = 0; i < parties.Count; i++)
            {
                PartyBase party = parties[i].Party;
                if (party == null || !party.IsActive || (party.IsMobile && !party.MobileParty.IsActive)) return false;
            }
            return true;
        }

        private static void Restart(Campaign campaign)
        {
            // Always normal speed: the mod never fast-forwards on its own (the player can, with the keys or
            // buttons). Running the whole campaign at 4x behind every menu is heavy on big overhaul maps.
            campaign.TimeControlMode = CampaignTimeControlMode.UnstoppablePlay;
            _runningByUs = true;
            _suspendedByUs = false;
            _restartedSinceLastFrame = true;
        }
    }
}
