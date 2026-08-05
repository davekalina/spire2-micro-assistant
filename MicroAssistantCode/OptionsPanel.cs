using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.addons.mega_text;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// The upper-right toggle list for the experimental view switches, sitting under
/// the game's version/seed block. Plain clickable text rows in the game's font —
/// deliberately austere; these are dev switches being auditioned, not shipped UI.
/// </summary>
internal sealed class OptionsPanel : IDisposable
{
    private const string FontPath = "res://themes/kreon_bold_glyph_space_one.tres";
    private const int FontSize = 18;

    private VBoxContainer? _root;

    public void Attach(Control parent)
    {
        _root = new VBoxContainer
        {
            Name = "MicroAssistantOptions",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -280f,
            OffsetRight = -12f,
            OffsetTop = 150f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddThemeConstantOverride("separation", 2);
        AddToggle("Big & Bold", () => ViewOptions.BigAndBold, v => ViewOptions.BigAndBold = v);
        AddToggle("Always On", () => ViewOptions.AlwaysOn, v => ViewOptions.AlwaysOn = v);
        AddToggle("Card Overlay", () => ViewOptions.CardOverlay, v => ViewOptions.CardOverlay = v);
        AddToggle("Replace Card Count",
            () => ViewOptions.ReplaceCardCount, v => ViewOptions.ReplaceCardCount = v);
        AddToggle("Replace End Turn Button",
            () => ViewOptions.ReplaceEndTurn, v => ViewOptions.ReplaceEndTurn = v);
        parent.AddChild(_root);
    }

    public void Dispose()
    {
        if (_root != null && GodotObject.IsInstanceValid(_root))
            _root.QueueFree();
        _root = null;
    }

    private void AddToggle(string name, Func<bool> get, Action<bool> set)
    {
        var row = new MegaLabel
        {
            Text = Render(name, get()),
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = Control.MouseFilterEnum.Stop,
            AutoSizeEnabled = false,
        };
        row.AddThemeFontOverride("font", ResourceLoader.Load<Font>(FontPath));
        row.AddThemeFontSizeOverride("font_size", FontSize);
        row.AddThemeColorOverride("font_color", StsColors.cream);
        row.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.10208f, 0.0816f));
        row.AddThemeConstantOverride("outline_size", 8);
        row.GuiInput += @event => Guard.Run("Toggling a view option", () =>
        {
            if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                return;
            set(!get());
            row.Text = Render(name, get());
            ViewOptions.Notify();
        });
        _root!.AddChild(row);
    }

    private static string Render(string name, bool on) => $"[{(on ? "X" : "  ")}] {name}";
}
