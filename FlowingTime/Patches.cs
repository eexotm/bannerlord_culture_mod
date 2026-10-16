using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;

namespace FlowingTime.Patches
{
    /// <summary>Runs the governor every campaign frame, before the frame's time step is computed.</summary>
    [HarmonyPatch(typeof(Campaign), "RealTick")]
    internal static class CampaignRealTickPatch
    {
        private static bool _failed;

        private static void Prefix()
        {
            if (_failed) return;
            try
            {
                FlowGovernor.BeforeRealTick();
            }
            catch (Exception e)
            {
                // Never let a per-frame error spam or break the game: switch the governor off once.
                _failed = true;
                Log.Error("Time flow stopped working and was switched off: " + e.Message);
            }
        }
    }

    /// <summary>
    /// Campaign.Tick sets the frame's time step to 0 at any menu that is not an active wait menu, which
    /// skips AI thinking for that frame. Let it run at menus where time is allowed to flow.
    /// </summary>
    [HarmonyPatch(typeof(Campaign), "Tick")]
    internal static class CampaignTickMenuPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return IsWaitActiveRewriter.Rewrite(instructions, "Campaign.Tick");
        }
    }

    /// <summary>
    /// MapScreen only handles the pause and speed keys outside menus or in active wait menus. Allow them
    /// in menus where time flows, so the player can still pause in a town.
    /// </summary>
    [HarmonyPatch]
    internal static class MapScreenTimeKeysPatch
    {
        private static MethodBase TargetMethod()
        {
            // Explicit interface implementation: its metadata name is "<namespace>.IMapStateHandler.BeforeTick".
            return AccessTools.GetDeclaredMethods(typeof(MapScreen))
                .First(m => m.Name.EndsWith("IMapStateHandler.BeforeTick", StringComparison.Ordinal));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return IsWaitActiveRewriter.Rewrite(instructions, "MapScreen.BeforeTick");
        }
    }

    internal static class IsWaitActiveRewriter
    {
        /// <summary>Swaps the single GameMenu.IsWaitActive read for FlowGovernor.IsTimeFlowMenu(menu).</summary>
        public static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, string where)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(GameMenu), nameof(GameMenu.IsWaitActive));
            MethodInfo replacement = AccessTools.Method(typeof(FlowGovernor), nameof(FlowGovernor.IsTimeFlowMenu));

            List<CodeInstruction> codes = instructions.ToList();
            int replaced = 0;
            foreach (CodeInstruction code in codes)
            {
                if (code.Calls(getter))
                {
                    // Same stack shape (GameMenu in, bool out); changing in place keeps labels and blocks.
                    code.opcode = OpCodes.Call;
                    code.operand = replacement;
                    replaced++;
                }
            }

            // The game code differs from what this patch was written for (game update or another mod).
            if (replaced != 1)
                throw new InvalidOperationException(where + ": expected 1 IsWaitActive check, found " + replaced);

            return codes;
        }
    }

    /// <summary>Pause/play/fast-forward keys go through Campaign.SetTimeSpeed.</summary>
    [HarmonyPatch(typeof(Campaign), nameof(Campaign.SetTimeSpeed))]
    internal static class SetTimeSpeedPatch
    {
        private static void Postfix(int speed)
        {
            if (CameraAnimationPatch.Active) return; // the game pausing for a camera move, not the player
            TimeIntent.FromSpeed(speed);
        }
    }

    /// <summary>The map camera animation pauses via SetTimeSpeed(0); don't mistake that for the player.</summary>
    [HarmonyPatch(typeof(MapCameraView), nameof(MapCameraView.StartCameraAnimation))]
    internal static class CameraAnimationPatch
    {
        public static bool Active;

        private static void Prefix() => Active = true;

        private static Exception Finalizer(Exception __exception)
        {
            Active = false;
            return __exception;
        }
    }

    /// <summary>Clicking a destination starts the clock (StoppablePlay, or fast forward on double click).</summary>
    [HarmonyPatch(typeof(MapScreen), "HandleLeftMouseButtonClick")]
    internal static class MapClickPatch
    {
        private static void Prefix(out CampaignTimeControlMode __state)
        {
            __state = Campaign.Current?.TimeControlMode ?? CampaignTimeControlMode.Stop;
        }

        private static void Postfix(CampaignTimeControlMode __state)
        {
            if (Campaign.Current == null) return;
            CampaignTimeControlMode mode = Campaign.Current.TimeControlMode;
            if (mode != __state && (mode == CampaignTimeControlMode.StoppablePlay || mode == CampaignTimeControlMode.StoppableFastForward))
                TimeIntent.FromMode(mode);
        }
    }

    /// <summary>The pause/play/fast-forward buttons on the map bar.</summary>
    [HarmonyPatch(typeof(MapTimeControlVM), nameof(MapTimeControlVM.ExecuteTimeControlChange))]
    internal static class TimeControlButtonsPatch
    {
        private static void Prefix(out CampaignTimeControlMode __state)
        {
            __state = Campaign.Current?.TimeControlMode ?? CampaignTimeControlMode.Stop;
        }

        private static void Postfix(CampaignTimeControlMode __state)
        {
            if (Campaign.Current == null) return;
            CampaignTimeControlMode mode = Campaign.Current.TimeControlMode;
            if (mode != __state)
                TimeIntent.FromMode(mode);
        }
    }
}
