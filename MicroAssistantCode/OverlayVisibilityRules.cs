namespace MicroAssistant.MicroAssistantCode;

/// <summary>Pure policy for when the combat display yields to native modal UI.</summary>
internal static class OverlayVisibilityRules
{
    public static bool ShouldStandDown(
        bool mapOpen,
        bool capstoneInUse,
        bool overlayOpen,
        bool handSelection) =>
        mapOpen || capstoneInUse || overlayOpen || handSelection;
}
