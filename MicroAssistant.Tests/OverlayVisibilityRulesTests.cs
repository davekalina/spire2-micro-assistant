using MicroAssistant.MicroAssistantCode;
using Xunit;

namespace MicroAssistant.Tests;

public class OverlayVisibilityRulesTests
{
    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void Modal_surface_makes_the_display_stand_down(
        bool mapOpen,
        bool capstoneInUse,
        bool overlayOpen,
        bool handSelection) =>
        Assert.True(OverlayVisibilityRules.ShouldStandDown(
            mapOpen, capstoneInUse, overlayOpen, handSelection));

    [Fact]
    public void Clear_board_keeps_the_display_visible() =>
        Assert.False(OverlayVisibilityRules.ShouldStandDown(
            mapOpen: false,
            capstoneInUse: false,
            overlayOpen: false,
            handSelection: false));
}
