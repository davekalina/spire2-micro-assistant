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
    private MegaLabel? _statusLine;
    private Func<string?>? _status;

    public void Attach(Control parent, Func<string?> status)
    {
        _status = status;
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
            if (_dropdown.Visible && _statusLine != null &&
                GodotObject.IsInstanceValid(_statusLine))
                _statusLine.Text = _status?.Invoke() ?? "";
            // Last sibling wins Godot's input picking; whatever overlay sat on this
            // spot loses the contest once we are in front.
            _root.MoveToFront();
        });
        _root.AddChild(gear);

        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 2);

        // Whether the quiet is correct: how many of the run's relics and powers the
        // mod actually understands right now. Refreshed each time the menu opens.
        _statusLine = new MegaLabel
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutoSizeEnabled = false,
            Modulate = new Color(1f, 1f, 1f, 0.75f),
        };
        _statusLine.AddThemeFontOverride("font", ResourceLoader.Load<Font>(FontPath));
        _statusLine.AddThemeFontSizeOverride("font_size", 15);
        _statusLine.AddThemeColorOverride("font_color", StsColors.gold);
        _statusLine.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.10208f, 0.0816f));
        _statusLine.AddThemeConstantOverride("outline_size", 6);
        rows.AddChild(_statusLine);

        AddScaleSlider(rows);
        AddToggle(rows, "Scale Always On",
            () => ViewOptions.ScaleAlwaysOn, v => ViewOptions.ScaleAlwaysOn = v);
        AddToggle(rows, "Show Up Top", () => ViewOptions.ShowUpTop, v => ViewOptions.ShowUpTop = v);
        AddToggle(rows, "Setup Relics Bar",
            () => ViewOptions.ShowSetupRelics, v => ViewOptions.ShowSetupRelics = v);
        AddToggle(rows, "Setup Relics Top Left",
            () => ViewOptions.SetupTopLeft, v => ViewOptions.SetupTopLeft = v);
        AddToggle(rows, "Pending Block",
            () => ViewOptions.ShowPendingBlock, v => ViewOptions.ShowPendingBlock = v);
        AddToggle(rows, "Always On", () => ViewOptions.AlwaysOn, v => ViewOptions.AlwaysOn = v);
        AddToggle(rows, "Card Overlay",
            () => ViewOptions.CardOverlay, v => ViewOptions.CardOverlay = v);
        AddToggle(rows, "Replace Card Count",
            () => ViewOptions.ReplaceCardCount, v => ViewOptions.ReplaceCardCount = v);
        AddToggle(rows, "Replace End Turn Button",
            () => ViewOptions.ReplaceEndTurn, v => ViewOptions.ReplaceEndTurn = v);
        AddToggle(rows, "Hide Negated Payoffs",
            () => ViewOptions.HideNegatedPayoffs, v => ViewOptions.HideNegatedPayoffs = v);

        // The byline closes the menu, as in the other mods' panels.
        rows.AddChild(new Control
        {
            CustomMinimumSize = new Vector2(0f, 6f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        var byline = new MegaLabel
        {
            Text = $"{MainFile.ModName} {MainFile.Version} by {MainFile.Author}",
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutoSizeEnabled = false,
            Modulate = new Color(1f, 1f, 1f, 0.75f),
        };
        byline.AddThemeFontOverride("font", ResourceLoader.Load<Font>(FontPath));
        byline.AddThemeFontSizeOverride("font_size", 15);
        byline.AddThemeColorOverride("font_color", StsColors.gold);
        byline.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.10208f, 0.0816f));
        byline.AddThemeConstantOverride("outline_size", 6);
        rows.AddChild(byline);

        _dropdown = BadgeStrip.BackedPanel(rows);
        _dropdown.Visible = false;
        // Block click-through to whatever sits behind the open list.
        _dropdown.MouseFilter = Control.MouseFilterEnum.Stop;
        // Pinned by its top-right corner and sized to its rows: a zero-size box that
        // grows left and down, so a longer row (the byline) widens it into the
        // screen instead of pushing its right edge off it.
        _dropdown.AnchorLeft = 1f;
        _dropdown.AnchorRight = 1f;
        _dropdown.OffsetLeft = -12f;
        _dropdown.OffsetRight = -12f;
        _dropdown.OffsetTop = 134f;
        _dropdown.OffsetBottom = 134f;
        _dropdown.GrowHorizontal = Control.GrowDirection.Begin;
        _dropdown.GrowVertical = Control.GrowDirection.End;
        _root.AddChild(_dropdown);

        parent.AddChild(_root);
    }

    public void Dispose()
    {
        if (_root != null && GodotObject.IsInstanceValid(_root))
            _root.QueueFree();
        _root = null;
        _dropdown = null;
        _statusLine = null;
    }

    /// <summary>
    /// The badge-scale slider that replaced the Big &amp; Bold toggle: 0.5..3.0 in
    /// 0.05 steps (the slider's Step does the rounding), applied to the focused
    /// card's display wherever it is placed.
    /// </summary>
    private static void AddScaleSlider(Container into)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);

        var label = new MegaLabel
        {
            Text = ScaleText(),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutoSizeEnabled = false,
            CustomMinimumSize = new Vector2(104f, 0f),
        };
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>(FontPath));
        label.AddThemeFontSizeOverride("font_size", FontSize);
        label.AddThemeColorOverride("font_color", StsColors.cream);
        label.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.10208f, 0.0816f));
        label.AddThemeConstantOverride("outline_size", 8);
        row.AddChild(label);

        var slider = new HSlider
        {
            MinValue = 0.5,
            MaxValue = 3.0,
            Step = 0.05,
            Value = ViewOptions.Scale,
            CustomMinimumSize = new Vector2(130f, 22f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        slider.ValueChanged += value => Guard.Run("Adjusting the badge scale", () =>
        {
            // Step already quantizes to 0.05; the rounding here just kills float noise.
            ViewOptions.Scale = (float)(Math.Round(value * 20.0) / 20.0);
            label.Text = ScaleText();
            ViewOptions.Notify();
        });
        row.AddChild(slider);
        into.AddChild(row);
    }

    private static string ScaleText() => $"Scale: {ViewOptions.Scale:0.00}";

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
