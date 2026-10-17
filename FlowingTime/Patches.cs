using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Helpers;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Core;

namespace FlowingTime.Patches
{
    /// <summary>Marks a patch the mod cannot work without. If one fails, the whole mod switches off.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class RequiredPatchAttribute : Attribute
    {
    }

    /// <summary>Whether the patches the governor depends on are in place right now.</summary>
    internal static class PatchStateFlags
    {
        public static bool CoreApplied;
        public static bool TickRewritten;
        public static bool KeysRewritten;
        public static bool ButtonsRewritten;
        public static bool Faulted;
    }

    /// <summary>Runs patch bodies that are called from inside game code: an error switches the mod off instead of crashing.</summary>
    internal static class Guard
    {
        public static void Run(Action action, string where)
        {
            if (PatchStateFlags.Faulted) return;
            try
            {
                action();
            }
            catch (Exception e)
            {
                Fail(where, e);
            }
        }

        public static void Fail(string where, Exception e)
        {
            PatchStateFlags.Faulted = true;
            Log.Error("Switched off after an error in " + where + ": " + e.Message);
        }
    }

    /// <summary>Runs the governor every campaign frame, right before the frame's time step is computed.</summary>
    [RequiredPatch]
    [HarmonyPatch(typeof(Campaign), "TickMapTime")]
    internal static class TickMapTimePatch
    {
        // Runs every frame: plain try/catch, no delegate.
        private static void Prefix()
        {
            if (PatchStateFlags.Faulted) return;
            try { FlowGovernor.BeforeTickMapTime(); }
            catch (Exception e) { Guard.Fail("TickMapTime", e); }
        }
    }

    /// <summary>
    /// Campaign.Tick sets the frame's time step to 0 at any menu that is not an active wait menu, which
    /// skips AI thinking for that frame. Let it run at menus where time is allowed to flow.
    /// </summary>
    [RequiredPatch]
    [HarmonyPatch(typeof(Campaign), "Tick")]
    internal static class CampaignTickMenuPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return IsWaitActiveRewriter.Rewrite(instructions, out PatchStateFlags.TickRewritten);
        }
    }

    /// <summary>
    /// MapScreen only handles the pause and speed keys outside menus or in active wait menus. Allow them
    /// in menus where time flows, so the player can still pause in a town.
    /// </summary>
    [RequiredPatch]
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
            return IsWaitActiveRewriter.Rewrite(instructions, out PatchStateFlags.KeysRewritten);
        }
    }

    internal static class IsWaitActiveRewriter
    {
        /// <summary>
        /// Swaps the single GameMenu.IsWaitActive read for FlowGovernor.IsTimeFlowMenu(menu). If the code
        /// is not what this was written for (game update, or another mod's transpiler ran first), the
        /// method is left untouched and <paramref name="rewritten"/> is false. It never throws, because
        /// Harmony re-runs transpilers whenever any mod patches the same method.
        /// </summary>
        public static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, out bool rewritten)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(GameMenu), nameof(GameMenu.IsWaitActive));
            MethodInfo replacement = AccessTools.Method(typeof(FlowGovernor), nameof(FlowGovernor.IsTimeFlowMenu));

            List<CodeInstruction> codes = instructions.ToList();
            List<CodeInstruction> matches = codes.Where(c => c.Calls(getter)).ToList();
            rewritten = matches.Count == 1;
            if (!rewritten) return codes;

            // Same stack shape (GameMenu in, bool out); changing in place keeps labels and exception blocks.
            matches[0].opcode = OpCodes.Call;
            matches[0].operand = replacement;
            return codes;
        }
    }

    /// <summary>
    /// The generic transitions that stop the clock. The governor restarts it when the stop came from the
    /// transition itself, not from the menu or game logic around it.
    /// </summary>
    [RequiredPatch]
    [HarmonyPatch]
    internal static class TransitionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GameMenu), nameof(GameMenu.ActivateGameMenu), new[] { typeof(string) });
            yield return AccessTools.Method(typeof(GameMenu), nameof(GameMenu.SwitchToMenu), new[] { typeof(string) });
            yield return AccessTools.Method(typeof(GameMenu), nameof(GameMenu.ExitToLast), Type.EmptyTypes);
            yield return AccessTools.Method(typeof(GameMenu), nameof(GameMenu.EndWait), Type.EmptyTypes);
            yield return AccessTools.Method(typeof(PlayerEncounter), nameof(PlayerEncounter.Finish), new[] { typeof(bool) });
        }

        private static void Prefix()
        {
            if (PatchStateFlags.Faulted) return;
            try { FlowGovernor.TransitionStart(); }
            catch (Exception e) { Guard.Fail("menu transition", e); }
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (!PatchStateFlags.Faulted)
            {
                try { FlowGovernor.TransitionEnd(); }
                catch (Exception e) { Guard.Fail("menu transition", e); }
            }
            return __exception;
        }
    }

    /// <summary>Restarts the clock right before a menu's init code, so a Stop the init writes is seen as deliberate.</summary>
    [RequiredPatch]
    [HarmonyPatch(typeof(GameMenu), nameof(GameMenu.RunOnInit))]
    internal static class MenuInitPatch
    {
        private static void Prefix(GameMenu __instance) => Guard.Run(() => FlowGovernor.BeforeMenuInit(__instance), "menu init");
    }

    /// <summary>
    /// Rebuilds the open menu with vanilla's MenuHelper.DecideMenuState when the settlement changed under
    /// it (see SettlementWatchBehavior). Runs from the menu tick, where vanilla wait menus do the same.
    /// </summary>
    // Hooked on MapState.OnTick (virtual, so never inlined) rather than the tiny menu tick methods, and
    // run before the menu and map ticks of the frame, where vanilla wait menus make the same switch.
    [RequiredPatch]
    [HarmonyPatch(typeof(MapState), "OnTick")]
    internal static class MenuRefreshPatch
    {
        // Runs every frame: plain try/catch, no delegate or closure.
        private static void Prefix(MapState __instance)
        {
            if (!PatchState.AllActive) return;
            try
            {
                if (FlowGovernor.MenuNeedsRefresh) Refresh(__instance);
                EncounterJoining.Tick(__instance);
            }
            catch (Exception e)
            {
                Guard.Fail("menu tick", e);
            }
        }

        /// <summary>
        /// Rebuilds the open menu with vanilla's MenuHelper.DecideMenuState when the settlement changed
        /// under it (see SettlementWatchBehavior).
        /// </summary>
        private static void Refresh(MapState mapState)
        {
            if (Game.Current?.GameStateManager?.ActiveState != mapState || mapState.IsSimulationActive) return;
            if (mapState.MapConversationActive || (Campaign.Current?.ConversationManager?.IsConversationFlowActive ?? false)) return;
            if (Campaign.Current?.SaveHandler?.IsSaving ?? false) return;

            FlowGovernor.MenuNeedsRefresh = false;
            GameMenu menu = mapState.MenuContext?.GameMenu;
            if (menu == null || menu.IsWaitActive) return; // wait menus react on their own
            MenuHelper.DecideMenuState();
        }
    }

    [RequiredPatch]
    [HarmonyPatch(typeof(MapState), "OnActivate")]
    internal static class MapStateActivatePatch
    {
        private static void Postfix() => FlowGovernor.OnMapStateActivated();
    }

    /// <summary>The pause, play and fast-forward keys go through Campaign.SetTimeSpeed.</summary>
    [RequiredPatch]
    [HarmonyPatch(typeof(Campaign), nameof(Campaign.SetTimeSpeed))]
    internal static class SetTimeSpeedPatch
    {
        private static void Postfix(int speed)
        {
            if (CameraAnimationPatch.Active) return; // the game pausing for a camera move, not the player
            bool inWaitMenu = Campaign.Current?.CurrentMenuContext?.GameMenu?.IsWaitActive ?? false;
            TimeIntent.FromSpeed(speed, inWaitMenu);
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

    /// <summary>
    /// The pause/play/fast-forward buttons on the map bar only work outside menus or in active wait
    /// menus (MapTimeControlVM.ExecuteTimeControlChange). Allow them in menus where time flows. They go
    /// through Campaign.SetTimeSpeed, so SetTimeSpeedPatch records the player's choice.
    /// Optional: without it, only the keys work inside settlement menus.
    /// </summary>
    [HarmonyPatch(typeof(MapTimeControlVM), nameof(MapTimeControlVM.ExecuteTimeControlChange))]
    internal static class TimeControlButtonsPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return IsWaitActiveRewriter.Rewrite(instructions, out PatchStateFlags.ButtonsRewritten);
        }
    }
}

namespace FlowingTime
{
    using FlowingTime.Patches;

    internal static class PatchState
    {
        /// <summary>All required patches applied, both rewrites matched, and nothing has failed since.</summary>
        public static bool AllActive =>
            PatchStateFlags.CoreApplied && PatchStateFlags.TickRewritten && PatchStateFlags.KeysRewritten && !PatchStateFlags.Faulted;
    }
}
