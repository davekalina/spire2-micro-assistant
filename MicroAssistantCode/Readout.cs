using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.addons.mega_text;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>Which of the design's three readout shapes a panel takes.</summary>
internal enum ReadoutStyle
{
    /// <summary>The badge row over a hand card (and its Show Up Top copy).</summary>
    Card,

    /// <summary>The setup bar hanging under the relic bar: plate, "SETUP", divider.</summary>
    SetupBand,

    /// <summary>The setup bar centered on screen: a fading strip under a gold rule.</summary>
    SetupDock,
}

/// <summary>
/// The card readout and the setup bar, built to the "Micro Assistant Final" design
/// (Claude Design, spire-mod design system). Every number below is that spec at
/// 1920x1080 and scale 1; the scale slider multiplies all of them together, so a
/// panel keeps its proportions at any size.
///
/// The plate is the design system's popup surface: a square, near-opaque slate
/// band with a steel hairline and a deep drop shadow. Counts are Kreon bold in
/// cream with a one-pixel ink outline, hung off the icon's bottom-right corner;
/// Block counts take the design system's data blue, projections this mod's aqua,
/// primed counts gold. A negated source is its icon dimmed and desaturated under a
/// loss-red slash — the design's own mark, which replaces the game's roundel here
/// (the End Turn badges keep the roundel).
/// </summary>
internal static class Readout
{
    private const string FontPath = "res://themes/kreon_bold_glyph_space_one.tres";

    private static readonly Color Cream = new("FFF8E2");
    private static readonly Color Gold = new("EFC851");
    private static readonly Color BlockBlue = new("8CD9EC");
    private static readonly Color Ink = new("14100C");
    private static readonly Color InkShadow = new(0f, 0f, 0f, 0.9f);

    private static readonly Color PlateFill = new(20 / 255f, 32 / 255f, 39 / 255f, 0.92f);
    private static readonly Color PlateBorder = new(120 / 255f, 150 / 255f, 166 / 255f, 0.4f);
    private static readonly Color PlateShadow = new(0f, 0f, 0f, 0.6f);
    private const float PlateBorderWidth = 2f;

    private static readonly Color DockFill = new(18 / 255f, 27 / 255f, 30 / 255f, 0.92f);
    private static readonly Color DockFillEnd = new(18 / 255f, 27 / 255f, 30 / 255f, 0.55f);
    private static readonly Color DockRule = new(239 / 255f, 200 / 255f, 81 / 255f, 0.55f);
    private const float DockRuleHeight = 2f;

    private static readonly Color Divider = new(1f, 248 / 255f, 226 / 255f, 0.14f);

    private static readonly Color Slash = new("E08D6E");
    private static readonly Color SlashKeyline = new(8 / 255f, 12 / 255f, 14 / 255f, 0.9f);
    private const float NegatedAlpha = 0.74f;
    private const float NegatedGrayscale = 0.2f;

    private const string SetupLabel = "SETUP";

    private sealed record Metrics(
        float Icon, float Gap, int FontSize, float BadgeRight, float BadgeRightWide,
        float BadgeBottom, float PadLeft, float PadTop, float PadRight, float PadBottom,
        int LabelFontSize = 0, float LabelGap = 0f);

    private static readonly Metrics CardMetrics = new(
        Icon: 52f, Gap: 28f, FontSize: 24, BadgeRight: -8f, BadgeRightWide: -10f,
        BadgeBottom: -6f, PadLeft: 24f, PadTop: 11f, PadRight: 24f, PadBottom: 13f);

    private static readonly Metrics BandMetrics = new(
        Icon: 44f, Gap: 24f, FontSize: 20, BadgeRight: -7f, BadgeRightWide: -9f,
        BadgeBottom: -5f, PadLeft: 22f, PadTop: 10f, PadRight: 22f, PadBottom: 12f,
        LabelFontSize: 20, LabelGap: 20f);

    private static readonly Metrics DockMetrics = new(
        Icon: 40f, Gap: 18f, FontSize: 18, BadgeRight: -6f, BadgeRightWide: -8f,
        BadgeBottom: -5f, PadLeft: 12f, PadTop: 7f, PadRight: 18f, PadBottom: 9f,
        LabelFontSize: 17, LabelGap: 16f);

    private static Metrics For(ReadoutStyle style) => style switch
    {
        ReadoutStyle.SetupBand => BandMetrics,
        ReadoutStyle.SetupDock => DockMetrics,
        _ => CardMetrics,
    };

    /// <summary>
    /// The panel's full height at this scale: icon row, padding, and the frame (the
    /// plate's border top and bottom, or the dock's rule). Callers sizing a band
    /// use this, so the panel never clips or floats in slack.
    /// </summary>
    public static float Height(ReadoutStyle style, float scale)
    {
        var m = For(style);
        var frame = style == ReadoutStyle.SetupDock ? DockRuleHeight : 2f * PlateBorderWidth;
        return (m.Icon + m.PadTop + m.PadBottom + frame) * scale;
    }

    /// <summary>
    /// Replace the strip's content with a readout of these badges; empty draws
    /// nothing (the design: a card that affects nothing tracked gets no band). A
    /// prefix is the absorbed hand digit, as a leading label on the card plate.
    /// </summary>
    public static void Set(
        HBoxContainer strip, IReadOnlyList<SourceBadge> badges, ReadoutStyle style,
        float scale, string? prefix = null)
    {
        foreach (var child in strip.GetChildren())
        {
            strip.RemoveChild(child);
            child.QueueFree();
        }
        if (badges.Count == 0)
            return;

        var m = For(style);
        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        // Label, divider, and icon row sit LabelGap apart; the card plate has no label,
        // only the absorbed hand digit, which takes half an icon gap.
        row.AddThemeConstantOverride("separation",
            (int)((m.LabelGap > 0f ? m.LabelGap : m.Gap / 2f) * scale));

        if (prefix != null)
            row.AddChild(TextLabel(prefix, (int)(m.FontSize * scale), Cream, scale));
        if (style != ReadoutStyle.Card)
            row.AddChild(SetupCaption(m, scale));
        if (style == ReadoutStyle.SetupBand)
            row.AddChild(new ColorRect
            {
                Color = Divider,
                CustomMinimumSize = new Vector2(2f * scale, 0f),
                SizeFlagsVertical = Control.SizeFlags.Fill,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });

        var icons = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        icons.AddThemeConstantOverride("separation", (int)(m.Gap * scale));
        foreach (var badge in badges)
            icons.AddChild(Slot(badge, m, scale));
        row.AddChild(icons);

        strip.AddChild(style == ReadoutStyle.SetupDock ? Dock(row, m, scale) : Plate(row, m, scale));
    }

    /// <summary>The square slate plate shared by the card readout and the setup band.</summary>
    private static Control Plate(Control content, Metrics m, float scale)
    {
        var border = PlateBorderWidth * scale;
        var box = new StyleBoxFlat
        {
            BgColor = PlateFill,
            BorderColor = PlateBorder,
            ShadowColor = PlateShadow,
            ShadowSize = (int)(17 * scale),
            ShadowOffset = new Vector2(0f, 14f * scale),
            ContentMarginLeft = m.PadLeft * scale + border,
            ContentMarginTop = m.PadTop * scale + border,
            ContentMarginRight = m.PadRight * scale + border,
            ContentMarginBottom = m.PadBottom * scale + border,
        };
        box.SetBorderWidthAll((int)Math.Max(1f, Math.Round(border)));
        var plate = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        plate.AddThemeStyleboxOverride("panel", box);
        plate.AddChild(content);
        return plate;
    }

    /// <summary>The centered dock: a left-to-right fade under a thin gold rule, no sides.</summary>
    private static Control Dock(Control content, Metrics m, float scale)
    {
        var gradient = new Gradient();
        gradient.SetColor(0, DockFill);
        gradient.SetColor(1, DockFillEnd);
        var box = new StyleBoxTexture
        {
            Texture = new GradientTexture2D
            {
                Gradient = gradient,
                Width = 64,
                Height = 4,
                FillFrom = new Vector2(0f, 0.5f),
                FillTo = new Vector2(1f, 0.5f),
            },
            ContentMarginLeft = m.PadLeft * scale,
            ContentMarginTop = m.PadTop * scale,
            ContentMarginRight = m.PadRight * scale,
            ContentMarginBottom = m.PadBottom * scale,
        };
        var strip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        strip.AddThemeStyleboxOverride("panel", box);
        strip.AddChild(content);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 0);
        column.AddChild(new ColorRect
        {
            Color = DockRule,
            CustomMinimumSize = new Vector2(0f, Math.Max(1f, DockRuleHeight * scale)),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        column.AddChild(strip);
        return column;
    }

    private static MegaLabel SetupCaption(Metrics m, float scale)
    {
        var caption = TextLabel(SetupLabel, (int)(m.LabelFontSize * scale), Gold, scale);
        // The design's 1.5px tracking on uppercase Kreon.
        caption.AddThemeConstantOverride("letter_spacing", (int)Math.Round(1.5f * scale));
        return caption;
    }

    /// <summary>One icon with its count, or dimmed under the slash when negated.</summary>
    private static Control Slot(SourceBadge badge, Metrics m, float scale)
    {
        var size = m.Icon * scale;
        var slot = new Control
        {
            CustomMinimumSize = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var icon = new TextureRect
        {
            Texture = badge.Icon,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            OffsetRight = size,
            OffsetBottom = size,
        };
        slot.AddChild(icon);

        if (badge.Broken)
        {
            icon.Modulate = new Color(1f, 1f, 1f, NegatedAlpha);
            icon.Material = Desaturate();
            // The slash's box, in design units on a 52px icon: 58x7 at (-2, 22),
            // turned -36 degrees about its centre, with a 2px ink keyline around it.
            var unit = size / CardMetrics.Icon;
            slot.AddChild(SlashBar(new Vector2(-2f - 2f, 22f - 2f) * unit,
                new Vector2(58f + 4f, 7f + 4f) * unit, SlashKeyline));
            slot.AddChild(SlashBar(new Vector2(-2f, 22f) * unit,
                new Vector2(58f, 7f) * unit, Slash));
        }
        else if (badge.CountText is { } text)
        {
            var color = badge.Projected ? StsColors.aqua
                : badge.Primed ? StsColors.gold
                : badge.BlockValue ? BlockBlue
                : Cream;
            var fontSize = (int)(m.FontSize * scale);
            var right = size - (text.Length > 1 ? m.BadgeRightWide : m.BadgeRight) * scale;
            // CSS line-height 1: the glyph box is fontSize tall and its bottom sits
            // BadgeBottom below the icon; centre a roomier label on that box.
            var centre = size - m.BadgeBottom * scale - fontSize / 2f;
            var label = TextLabel(text, fontSize, color, scale);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.OffsetLeft = right - 80f * scale;
            label.OffsetRight = right;
            label.OffsetTop = centre - fontSize * 0.75f;
            label.OffsetBottom = centre + fontSize * 0.75f;
            slot.AddChild(label);
        }
        return slot;
    }

    private static ColorRect SlashBar(Vector2 position, Vector2 size, Color color) => new()
    {
        Color = color,
        Position = position,
        Size = size,
        PivotOffset = size / 2f,
        Rotation = Mathf.DegToRad(-36f),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    private static MegaLabel TextLabel(string text, int fontSize, Color color, float scale)
    {
        var label = new MegaLabel
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutoSizeEnabled = false,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        // Loaded per label rather than cached; see BadgeStrip.Style's note.
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>(FontPath));
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        // CSS: a 1px ink outline (four offset copies) and a soft 0 2px 4px shadow.
        label.AddThemeColorOverride("font_outline_color", Ink);
        label.AddThemeConstantOverride("outline_size", Math.Max(2, (int)Math.Round(3f * scale)));
        label.AddThemeColorOverride("font_shadow_color", InkShadow);
        label.AddThemeConstantOverride("shadow_offset_x", 0);
        label.AddThemeConstantOverride("shadow_offset_y", (int)Math.Round(2f * scale));
        label.AddThemeConstantOverride("shadow_outline_size", (int)Math.Round(4f * scale));
        return label;
    }

    private static Shader? _desaturate;

    /// <summary>The design's grayscale(.2): a mix toward luminance, alpha kept.</summary>
    private static ShaderMaterial Desaturate()
    {
        _desaturate ??= new Shader
        {
            Code = """
                shader_type canvas_item;
                uniform float amount = 0.2;
                void fragment() {
                    vec4 c = texture(TEXTURE, UV) * COLOR;
                    float g = dot(c.rgb, vec3(0.2126, 0.7152, 0.0722));
                    COLOR = vec4(mix(c.rgb, vec3(g), amount), c.a);
                }
                """,
        };
        var material = new ShaderMaterial { Shader = _desaturate };
        material.SetShaderParameter("amount", NegatedGrayscale);
        return material;
    }
}
