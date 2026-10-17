using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;

namespace FlowingTime
{
    /// <summary>
    /// With time flowing at settlement menus, the settlement can change under an open menu, which never
    /// happens in vanilla (no time passes at those menus). When it does, the menu is rebuilt with vanilla's
    /// own MenuHelper.DecideMenuState on the next menu tick (see MenuRefreshPatch).
    /// </summary>
    internal class SettlementWatchBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnSiegeEventStartedEvent.AddNonSerializedListener(this, OnSiegeStarted);
            CampaignEvents.TournamentFinished.AddNonSerializedListener(this, (winner, participants, town, prize) => OnTournamentGone(town));
            CampaignEvents.TournamentCancelled.AddNonSerializedListener(this, OnTournamentGone);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Nothing is saved.
        }

        private static void OnSiegeStarted(SiegeEvent siegeEvent)
        {
            // Vanilla notices a siege on the player's settlement only from wait menus; an ordinary town menu
            // would otherwise stay open (and its Leave would walk out through the siege).
            if (siegeEvent != null && siegeEvent.BesiegedSettlement == MobileParty.MainParty?.CurrentSettlement)
                FlowGovernor.MenuNeedsRefresh = true;
        }

        private static void OnTournamentGone(Town town)
        {
            // Tournament menus keep options that dereference the tournament after it is gone.
            if (town != null && town == MobileParty.MainParty?.CurrentSettlement?.Town)
                FlowGovernor.MenuNeedsRefresh = true;
        }
    }
}
