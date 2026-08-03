using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.addons.mega_text;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// Builds a centered horizontal row of relic badges: the relic's own atlas icon with
/// either its current count or an X over it.
///
/// The badge copies its geometry and label styling from the game's buff/debuff icon
/// (res://scenes/combat/power.tscn), the native "small icon with a count" in combat:
/// a 48x40 cell, a 40x40 icon, and a bottom-right Kreon count with the same cream
/// fill, outline, and shadow. The X has no native standalone texture, so it is the
/// same Kreon face in the game's red, sized to fill the icon.
/// </summary>
internal static class RelicBadgeStrip
{
    private const string CountFontPath = "res://themes/kreon_bold_glyph_space_one.tres";

    // From power.tscn: cell and %Icon sizes, and the %AmountLabel theme overrides.
    private static readonly Vector2 CellSize = new(48f, 40f);
    private const float IconSize = 40f;
    private const int CountFontSize = 18;
    private static readonly Color CountShadow = new(0f, 0f, 0f, 0.188235f);
    private static readonly Color CountOutline = new(0.12f, 0.10208f, 0.0816f);

    private const int BrokenFontSize = 34;
    private static readonly Color BrokenDim = new(0.5f, 0.5f, 0.5f);

    /// <summary>An empty strip; position and size are the caller's to set.</summary>
    public static HBoxContainer CreateContainer(string name)
    {
        var strip = new HBoxContainer
        {
            Name = name,
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        strip.AddThemeConstantOverride("separation", 4);
        return strip;
    }

    /// <summary>Replace the strip's badges. An empty list leaves the strip blank.</summary>
    public static void SetBadges(HBoxContainer strip, IReadOnlyList<RelicBadge> badges)
    {
        foreach (var child in strip.GetChildren())
        {
            // Removed before the deferred free so the outgoing badges stop taking
            // room in the row this frame, not next.
            strip.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var badge in badges)
            strip.AddChild(CreateBadge(badge));
    }

    private static Control CreateBadge(RelicBadge badge)
    {
        var cell = new Control
        {
            CustomMinimumSize = CellSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var icon = new TextureRect
        {
            Texture = badge.Relic.Icon,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            OffsetRight = IconSize,
            OffsetBottom = IconSize,
        };
        cell.AddChild(icon);

        if (badge.Broken)
        {
            icon.Modulate = BrokenDim;
            cell.AddChild(BrokenMark());
        }
        else
        {
            cell.AddChild(CountLabel(badge));
        }
        return cell;
    }

    private static MegaLabel CountLabel(RelicBadge badge)
    {
        var label = new MegaLabel
        {
            Text = badge.Count.ToString(),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutoSizeEnabled = false,
            // power.tscn's %AmountLabel offsets: a wide right-aligned band whose
            // right edge sits just off the icon's bottom-right corner.
            OffsetLeft = -56f,
            OffsetTop = 21f,
            OffsetRight = 44f,
            OffsetBottom = 44f,
        };
        Style(label, CountFontSize, badge.Primed ? StsColors.gold : StsColors.cream, outlineSize: 10);
        return label;
    }

    /// <summary>The X for a play that would break the relic, centered over the icon.</summary>
    private static MegaLabel BrokenMark()
    {
        var label = new MegaLabel
        {
            Text = "X",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutoSizeEnabled = false,
            OffsetRight = IconSize,
            OffsetBottom = IconSize,
        };
        Style(label, BrokenFontSize, StsColors.red, outlineSize: 12);
        return label;
    }

    private static void Style(MegaLabel label, int fontSize, Color color, int outlineSize)
    {
        // Loaded per badge rather than cached in a static: the resource cache frees a
        // font once nothing in the tree holds it, and a stale managed wrapper throws.
        label.AddThemeFontOverride("font", ResourceLoader.Load<Font>(CountFontPath));
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", CountShadow);
        label.AddThemeColorOverride("font_outline_color", CountOutline);
        label.AddThemeConstantOverride("shadow_offset_x", 3);
        label.AddThemeConstantOverride("shadow_offset_y", 3);
        label.AddThemeConstantOverride("outline_size", outlineSize);
    }
}
