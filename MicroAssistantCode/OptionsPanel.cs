using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.addons.mega_text;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// The experimental view switches, folded behind the game's own settings gear so
/// they take no screen space until asked for. The gear sits just left of the
/// version/seed block (whose box spans right-edge offsets -170..-11 from y 93);
/// clicking it drops a tooltip-backed list of toggle rows beneath. Opening also
/// moves the panel to the front of GlobalUi — Godot routes mouse picking by tree
/// order, not z, and other overlays were eating the bare rows' clicks.
/// </summary>
internal sealed class OptionsPanel : IDisposable
{
    private const string GearTexturePath =
        "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_settings.tres";
    private const string FontPath = "res://themes/kreon_bold_glyph_space_one.tres";
    private const int FontSize = 18;
    private static readonly Color GearIdle = new(1f, 1f, 1f, 0.6f);
    private static readonly Color GearHover = Colors.White;

    private Control? _root;
    private Control? _dropdown;

    public void Attach(Control parent)
    {
        _root = new Control
        {
            Name = "MicroAssistantOptions",
            AnchorRight = 1f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            // Drawn above other overlays; input priority is handled by MoveToFront.
            ZAsRelative = false,
            ZIndex = 60,
        };

        var gear = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>(GearTexturePath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Modulate = GearIdle,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -216f,
            OffsetRight = -180f,
            OffsetTop = 92f,
            OffsetBottom = 128f,
        };
        gear.MouseEntered += () => Guard.Run("Highlighting the options gear", () =>
            gear.Modulate = GearHover);
        gear.MouseExited += () => Guard.Run("Unhighlighting the options gear", () =>
            gear.Modulate = GearIdle);
        gear.GuiInput += @event => Guard.Run("Toggling the options dropdown", () =>
        {
            if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                return;
            if (_dropdown == null || !GodotObject.IsInstanceValid(_dropdown) ||
                _root == null || !GodotObject.IsInstanceValid(_root))
                return;
            _dropdown.Visible = !_dropdown.Visible;
            // Last sibling wins Godot's input picking; whatever overlay sat on this
            // spot loses the contest once we are in front.
            _root.MoveToFront();
        });
        _root.AddChild(gear);

        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 2);
        AddToggle(rows, "Big & Bold", () => ViewOptions.BigAndBold, v => ViewOptions.BigAndBold = v);
        AddToggle(rows, "Always On", () => ViewOptions.AlwaysOn, v => ViewOptions.AlwaysOn = v);
        AddToggle(rows, "Card Overlay",
            () => ViewOptions.CardOverlay, v => ViewOptions.CardOverlay = v);
        AddToggle(rows, "Replace Card Count",
            () => ViewOptions.ReplaceCardCount, v => ViewOptions.ReplaceCardCount = v);
        AddToggle(rows, "Replace End Turn Button",
            () => ViewOptions.ReplaceEndTurn, v => ViewOptions.ReplaceEndTurn = v);

        _dropdown = BadgeStrip.BackedPanel(rows);
        _dropdown.Visible = false;
        // Block click-through to whatever sits behind the open list.
        _dropdown.MouseFilter = Control.MouseFilterEnum.Stop;
        _dropdown.AnchorLeft = 1f;
        _dropdown.AnchorRight = 1f;
        _dropdown.OffsetLeft = -352f;
        _dropdown.OffsetRight = -12f;
        _dropdown.OffsetTop = 134f;
        _dropdown.OffsetBottom = 330f;
        _root.AddChild(_dropdown);

        parent.AddChild(_root);
    }

    public void Dispose()
    {
        if (_root != null && GodotObject.IsInstanceValid(_root))
            _root.QueueFree();
        _root = null;
        _dropdown = null;
    }

    private static void AddToggle(Container into, string name, Func<bool> get, Action<bool> set)
    {
        var row = new MegaLabel
        {
            Text = Render(name, get()),
            HorizontalAlignment = HorizontalAlignment.Left,
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
        into.AddChild(row);
    }

    private static string Render(string name, bool on) => $"[{(on ? "X" : "  ")}] {name}";
}
