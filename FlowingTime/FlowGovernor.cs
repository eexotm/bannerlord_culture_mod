using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FlowingTime
{
    /// <summary>
    /// Decides, once per campaign frame, whether the game's automatic pause should be undone, and
    /// restarts the clock at the player's chosen speed when it should.
    ///
    /// Vanilla pauses in three ways that matter here:
    ///  1. GameMenu.ActivateGameMenu / SwitchToMenu / ExitToLast, PlayerEncounter.Finish, GameMenu.EndWait
    ///     and a few others set TimeControlMode = Stop.
    ///  2. In "Stoppable" modes the clock gives no time while the main party is standing still
    ///     (Campaign.TickMapTime + MobileParty.ComputeIsWaiting), which is the arrival pause and also
    ///     what freezes time inside settlements.
    ///  3. Campaign.Tick zeroes the frame's time step at any menu that is not an active wait menu, which
    ///     skips AI thinking (patched separately in CampaignTickMenuPatch).
    /// Popups that lock the clock (TimeControlModeLock), the escape menu (the map state stops ticking),
    /// and the player's own pause are always respected.
    /// </summary>
    internal static class FlowGovernor
    {
        /// <summary>Called at the start of Campaign.RealTick, before the frame's time step is computed.</summary>
        public static void BeforeRealTick()
        {
            Campaign campaign = Campaign.Current;
            if (campaign == null || !(Game.Current?.GameStateManager?.ActiveState is MapState mapState)) return;
            if (!IsFlowAllowed(mapState)) return;

            // Vanilla wait menus ("wait here", sieges, captivity, ...) drive the clock themselves.
            GameMenu menu = mapState.MenuContext?.GameMenu;
            if (menu != null && menu.IsWaitActive) return;

            CampaignTimeControlMode mode = campaign.TimeControlMode;
            TimeIntent.SyncSpeed(mode);
            if (!TimeIntent.WantsRunning) return;

            switch (mode)
            {
                case CampaignTimeControlMode.Stop:
                case CampaignTimeControlMode.FastForwardStop:
                    campaign.TimeControlMode = TimeIntent.FastForward
                        ? CampaignTimeControlMode.UnstoppableFastForward
                        : CampaignTimeControlMode.UnstoppablePlay;
                    break;

                case CampaignTimeControlMode.StoppablePlay:
                    if (StandingStillWouldPause(mapState))
                        campaign.TimeControlMode = CampaignTimeControlMode.UnstoppablePlay;
                    break;

                case CampaignTimeControlMode.StoppableFastForward:
                    if (StandingStillWouldPause(mapState))
                        campaign.TimeControlMode = CampaignTimeControlMode.UnstoppableFastForward;
                    break;
            }
        }

        /// <summary>
        /// Replacement for GameMenu.IsWaitActive at the two places that use it to mean "time may run at
        /// this menu": Campaign.Tick (whether to zero the time step) and MapScreen's input handling
        /// (whether the pause/speed keys work).
        /// </summary>
        public static bool IsTimeFlowMenu(GameMenu menu)
        {
            if (menu == null) return false;
            if (menu.IsWaitActive) return true;
            return Game.Current?.GameStateManager?.ActiveState is MapState mapState
                && mapState.AtMenu
                && IsFlowAllowed(mapState);
        }

        /// <summary>Situations where the clock must stay as the game left it.</summary>
        public static bool IsFlowAllowed(MapState mapState)
        {
            Campaign campaign = Campaign.Current;
            if (campaign == null || campaign.TimeControlModeLock) return false;
            if (mapState.IsSimulationActive || mapState.MapConversationActive) return false;
            if (campaign.ConversationManager != null && campaign.ConversationManager.IsConversationFlowActive) return false;
            if (!Settings.FlowDuringPopups && InformationManager.IsAnyInquiryActive()) return false;

            if (mapState.AtMenu)
            {
                if (!Settings.FlowInMenus) return false;
                if (mapState.MenuContext?.GameMenu == null) return false;
                if (!Settings.FlowDuringEncounters && MobileParty.MainParty?.MapEvent != null) return false;
            }
            return true;
        }

        private static bool StandingStillWouldPause(MapState mapState)
        {
            // Stoppable modes give no time while the party stands still. Inside a menu the party is
            // always standing (in a settlement or an encounter), so that would freeze the menu.
            if (mapState.AtMenu) return true;
            // Computed fresh: Campaign.IsMainPartyWaiting is last frame's value, which would still say
            // "waiting" on the frame the player clicks a new destination.
            return Settings.NoArrivalPause && MobileParty.MainParty != null && MobileParty.MainParty.ComputeIsWaiting();
        }
    }
}
