using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FlowingTime
{
    /// <summary>
    /// While time flows at the player's own battle menu, parties that arrive nearby join it, on either
    /// side, using vanilla's own rules: PlayerEncounter.CheckNearbyPartiesToJoinPlayerMapEvent, which the
    /// game runs once when the encounter starts (joining radius, war/alliance checks, not already busy
    /// in another battle, siege or settlement). Enemies that march into the player join on their own
    /// through vanilla's encounter handling. When the sides change, the menu is rebuilt so its text and
    /// options match the new battle.
    /// </summary>
    internal static class EncounterJoining
    {
        private static readonly Action<PlayerEncounter> CheckNearbyParties = CreateCheckNearbyParties();

        private static MapEvent _battle;
        private static int _playerSideParties;
        private static int _enemySideParties;
        private static CampaignTime _nextCheck;

        public static void Reset()
        {
            _battle = null;
        }

        /// <summary>Called every map frame, before the menu and campaign ticks (MapState.OnTick prefix).</summary>
        public static void Tick(MapState mapState)
        {
            MapEvent battle = MobileParty.MainParty?.MapEvent;
            if (battle == null)
            {
                _battle = null;
                return;
            }
            if (Game.Current?.GameStateManager?.ActiveState != mapState) return;
            if (mapState.MenuContext?.GameMenu == null || mapState.MenuContext.GameMenu.IsWaitActive) return;
            if (!FlowGovernor.IsOwnBattleAwaitingDecision(mapState, battle) || !FlowGovernor.CanFlow(mapState)) return;

            if (battle != _battle)
            {
                // New battle: vanilla already pulled in nearby parties when it started.
                _battle = battle;
                Count(battle, out _playerSideParties, out _enemySideParties);
                _nextCheck = CampaignTime.HoursFromNow(Settings.JoinCheckIntervalHours);
                return;
            }

            // Only while time actually passes; pulling parties in while paused would skip their march.
            if (!TimeIntent.IsRunning(Campaign.Current.TimeControlMode)) return;

            if (_nextCheck.IsPast)
            {
                _nextCheck = CampaignTime.HoursFromNow(Settings.JoinCheckIntervalHours);
                if (CheckNearbyParties != null && PlayerEncounter.Current != null) CheckNearbyParties(PlayerEncounter.Current);
            }

            Count(battle, out int playerSide, out int enemySide);
            if (playerSide == _playerSideParties && enemySide == _enemySideParties) return;

            int alliesJoined = playerSide - _playerSideParties;
            int enemiesJoined = enemySide - _enemySideParties;
            _playerSideParties = playerSide;
            _enemySideParties = enemySide;

            if (alliesJoined > 0)
                InformationManager.DisplayMessage(new InformationMessage(Plural(alliesJoined, "party has", "parties have") + " joined your side of the battle.", Colors.Green));
            if (enemiesJoined > 0)
                InformationManager.DisplayMessage(new InformationMessage(Plural(enemiesJoined, "party has", "parties have") + " joined the enemy.", Colors.Red));

            // Rebuild the encounter menu for the new sides, as vanilla does when returning from another screen.
            mapState.MenuContext.Refresh();
        }

        private static void Count(MapEvent battle, out int playerSide, out int enemySide)
        {
            BattleSideEnum side = PartyBase.MainParty.Side;
            playerSide = battle.PartiesOnSide(side).Count;
            enemySide = battle.PartiesOnSide(side == BattleSideEnum.Attacker ? BattleSideEnum.Defender : BattleSideEnum.Attacker).Count;
        }

        private static string Plural(int n, string one, string many)
        {
            return n == 1 ? "1 " + one : n + " " + many;
        }

        private static Action<PlayerEncounter> CreateCheckNearbyParties()
        {
            MethodInfo method = AccessTools.Method(typeof(PlayerEncounter), "CheckNearbyPartiesToJoinPlayerMapEvent");
            if (method == null || method.GetParameters().Length != 0) return null;
            return AccessTools.MethodDelegate<Action<PlayerEncounter>>(method);
        }

        public static bool Available => CheckNearbyParties != null;
    }
}
