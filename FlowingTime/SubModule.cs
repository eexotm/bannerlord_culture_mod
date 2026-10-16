using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
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
        public static void Info(string message)
        {
            InformationManager.DisplayMessage(new InformationMessage("[Flowing Time] " + message, Colors.Cyan));
        }

        public static void Error(string message)
        {
            Debug.Print("[FlowingTime] " + message);
            InformationManager.DisplayMessage(new InformationMessage("[Flowing Time] " + message, Colors.Red));
        }
    }
}
