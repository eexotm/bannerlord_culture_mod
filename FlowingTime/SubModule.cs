using System;
using System.Collections.Generic;
using System.Linq;
using FlowingTime.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace FlowingTime
{
    public class SubModule : MBSubModuleBase
    {
        internal const string HarmonyId = "flowingtime.bannerlord";

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            ApplyPatches();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            Log.FlushPending();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            if (!(game.GameType is Campaign)) return;

            // New or loaded campaign: start paused, like vanilla, until the player starts time.
            TimeIntent.Reset();
            FlowGovernor.Reset();
            if (gameStarterObject is CampaignGameStarter starter)
                starter.AddBehavior(new SettlementWatchBehavior());
        }

        /// <summary>
        /// Applies each [HarmonyPatch] class on its own. The required ones only work together: if any of
        /// them fails (another mod changed the same code, or a game update did), every patch is removed
        /// and the game runs as vanilla. Optional ones just switch off their own small feature.
        /// </summary>
        private static void ApplyPatches()
        {
            var harmony = new Harmony(HarmonyId);
            var patchTypes = typeof(SubModule).Assembly.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0);

            string requiredFailure = null;
            foreach (Type type in patchTypes)
            {
                bool required = type.GetCustomAttributes(typeof(RequiredPatchAttribute), false).Length > 0;
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    string reason = type.Name + ": " + (e.InnerException ?? e).Message;
                    if (required)
                    {
                        requiredFailure = reason;
                        break;
                    }
                    Log.Error("Optional patch disabled: " + reason);
                }
            }

            if (requiredFailure == null && !PatchStateFlags.TickRewritten)
                requiredFailure = "Campaign.Tick is not the code this version was written for";
            if (requiredFailure == null && !PatchStateFlags.KeysRewritten)
                requiredFailure = "MapScreen input is not the code this version was written for";

            if (requiredFailure != null)
            {
                harmony.UnpatchAll(HarmonyId);
                Log.Error("Switched off, the game runs as vanilla. Reason: " + requiredFailure);
                return;
            }
            PatchStateFlags.CoreApplied = true;
        }
    }

    internal static class Log
    {
        // Messages raised before the UI exists are shown once the main menu is up.
        private static List<string> _pending = new List<string>();

        public static void Error(string message)
        {
            Debug.Print("[FlowingTime] " + message);
            if (_pending != null)
                _pending.Add(message);
            else
                InformationManager.DisplayMessage(new InformationMessage("[Flowing Time] " + message, Colors.Red));
        }

        public static void FlushPending()
        {
            List<string> pending = _pending;
            _pending = null;
            if (pending == null) return;
            foreach (string message in pending)
                InformationManager.DisplayMessage(new InformationMessage("[Flowing Time] " + message, Colors.Red));
        }
    }
}
