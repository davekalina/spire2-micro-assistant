using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// The mod's only Harmony surface: a view per combat UI, created when it readies and
/// disposed when it leaves the tree. Everything else runs on the game's own events.
/// </summary>
[HarmonyPatch(typeof(NCombatUi))]
internal static class CombatUiPatch
{
    private static readonly Dictionary<NCombatUi, CombatOverlayView> Views = [];

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NCombatUi._Ready))]
    private static void AfterReady(NCombatUi __instance) =>
        Guard.Run("Attaching the counting-relic overlay", () =>
        {
            if (Views.ContainsKey(__instance))
                return;
            var view = new CombatOverlayView(__instance);
            Views.Add(__instance, view);
            view.Attach();
        });

    [HarmonyPrefix]
    [HarmonyPatch(nameof(NCombatUi._ExitTree))]
    private static void BeforeExitTree(NCombatUi __instance) =>
        Guard.Run("Detaching the counting-relic overlay", () =>
        {
            if (Views.Remove(__instance, out var view))
                view.Dispose();
        });
}
