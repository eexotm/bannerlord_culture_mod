using System;
using System.Collections.Generic;
using System.Linq;
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
            // New or loaded campaign: start paused, like vanilla, until the player starts time.
            if (game.GameType is Campaign) TimeIntent.Reset();
        }

        /// <summary>
        /// Applies each [HarmonyPatch] class on its own, so a patch that conflicts with another mod
        /// (or a game update) only disables that one feature instead of the whole mod.
        /// </summary>
        private static void ApplyPatches()
        {
            var harmony = new Harmony(HarmonyId);
            var patchTypes = typeof(SubModule).Assembly.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0);

            foreach (Type type in patchTypes)
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    Log.Error("Patch " + type.Name + " disabled: " + (e.InnerException ?? e).Message);
                }
            }
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
