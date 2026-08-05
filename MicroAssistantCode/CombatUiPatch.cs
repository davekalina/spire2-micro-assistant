using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// The mod's Harmony surface: a view per combat UI, created when it readies and
/// disposed when it leaves the tree, plus one transition below that no game event
/// covers. Everything else runs on the game's own events.
/// </summary>
[HarmonyPatch(typeof(NCombatUi))]
internal static class CombatUiPatch
{
    private static readonly Dictionary<NCombatUi, CombatOverlayView> Views = [];

    /// <summary>Resync every live view's hand strip.</summary>
    internal static void SyncViews()
    {
        foreach (var view in Views.Values)
            view.SyncHandStripNow();
    }

    /// <summary>Re-assert a hidden hand digit after the game re-shows it.</summary>
    internal static void ReassertIndexHidden(NHandCardHolder holder)
    {
        foreach (var view in Views.Values)
            view.ReassertIndexHidden(holder);
    }

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
            Diag.Once("attach", () => "Combat overlay attached to the combat UI.");
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

/// <summary>
/// The moment a card is taken up for play — mouse drag or controller select, both
/// funnel through this one private method — nothing observable fires: the hover
/// tracker's model does not change and no combat state moves. This postfix is what
/// flips the hand strip into projected counts right away. The end of the play needs
/// no patch; deselection raises HoverChanged.
/// </summary>
[HarmonyPatch(typeof(NPlayerHand))]
internal static class CardPlayStartPatch
{
    [HarmonyPostfix]
    [HarmonyPatch("StartCardPlay")]
    private static void AfterStartCardPlay() =>
        Guard.Run("Projecting badges as a card enters play", () =>
        {
            Diag.Once("cardplay", () => "Card play start observed.");
            CombatUiPatch.SyncViews();
        });
}

/// <summary>
/// The game's SetIndexLabel re-shows the hand digit from the player's settings on
/// every layout pass, silently undoing the Replace Card Count hide; this postfix
/// puts the hide back the moment that happens.
/// </summary>
[HarmonyPatch(typeof(NHandCardHolder))]
internal static class HandIndexPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NHandCardHolder.SetIndexLabel))]
    private static void AfterSetIndexLabel(NHandCardHolder __instance) =>
        Guard.Run("Re-hiding an absorbed hand digit", () =>
            CombatUiPatch.ReassertIndexHidden(__instance));
}
