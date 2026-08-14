using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.addons.mega_text;

namespace MicroAssistant.MicroAssistantCode;

/// <summary>
/// Builds a centered horizontal row of source badges: the source's own atlas icon
/// with either a count or an X over it.
///
/// The badge copies its geometry and label styling from the game's buff/debuff icon
/// (res://scenes/combat/power.tscn), the native "small icon with a count" in combat:
/// a 48x40 cell, a 40x40 icon, and a bottom-right Kreon count with the same cream
/// fill, outline, and shadow. The X has no native standalone texture, so it is the
/// same Kreon face in the game's red, sized to fill the icon.
/// </summary>
internal static class BadgeStrip
{
    private const string CountFontPath = "res://themes/kreon_bold_glyph_space_one.tres";

    // From power.tscn: cell and %Icon sizes, and the %AmountLabel theme overrides.
    private static readonly Vector2 CellSize = new(48f, 40f);
    private const float IconSize = 40f;
    private const int CountFontSize = 18;
    private static readonly Color CountShadow = new(0f, 0f, 0f, 0.188235f);
    private static readonly Color CountOutline = new(0.12f, 0.10208f, 0.0816f);

    // The broken mark is the game's own "can't play this" slash roundel — the one
    // the cost gem wears at Velvet Choker's limit — slightly translucent so the
    // source icon stays readable under it.
    private const string BrokenTexturePath =
        "res://images/atlases/ui_atlas.sprites/card/card_unplayable_icon.tres";
    private static readonly Color BrokenModulate = new(1f, 1f, 1f, 0.85f);

    // HoverTip's Title and Description only have private setters; the seed LocString
    // is discarded once both are overwritten. Same approach Hypergeo ships.
    private static readonly PropertyInfo TipTitle =
        typeof(HoverTip).GetProperty(nameof(HoverTip.Title)) ??
        throw new MissingMemberException(typeof(HoverTip).FullName, nameof(HoverTip.Title));
    private static readonly PropertyInfo TipDescription =
        typeof(HoverTip).GetProperty(nameof(HoverTip.Description)) ??
        throw new MissingMemberException(
            typeof(HoverTip).FullName, nameof(HoverTip.Description));

    private const float BannerIconSize = 40f;

    // A panel's caption: the same Kreon face as the counts, in the game's gold, on
    // its own line above the icons and inside the same backing. Deliberately well
    // under the icon row's height — it labels the panel, it is not its headline.
    // TitleRowHeight is the height it reserves at scale 1; callers sizing a titled
    // panel must add TitleRowHeight * scale to the band, or the backing clips the
    // icons.
    private const int TitleFontSize = 12;
    private const int TitleOutlineSize = 6;
    public const float TitleRowHeight = 18f;

    // A captioned panel is a billboard at the top of the screen, not a badge
    // cluster pinned to a card, and it reads better with room around a short row.
    private const float TitledMinWidth = 292f;

    // The native tooltip backing: images/ui/hover_tip.png as a nine-patch, with a
    // 25% black copy offset behind it as the shadow, inside a MarginContainer whose
    // content margins match the art's borders. Straight from ui/hover_tip.tscn.
    private const string BackingTexturePath = "res://images/ui/hover_tip.png";
    private static readonly Rect2 BackingRegion = new(0f, 0f, 339f, 107f);
    private static readonly Rect2 BackingShadowRegion = new(-8f, -8f, 339f, 107f);

    /// <summary>
    /// The trigger banner row: the icons of the sources this play sets off, on a
    /// tooltip backing. Content is filled by SetTriggerIcons; empty hides it.
    /// </summary>
    public static HBoxContainer CreateTriggerBanner(string name)
    {
        var banner = CreateContainer(name);
        banner.Visible = false;
        return banner;
    }

    /// <summary>Rebuild the banner for these badges; hidden when none trigger.</summary>
    public static void SetTriggerIcons(
        HBoxContainer banner, IReadOnlyList<SourceBadge> triggering, float scale = 1f,
        string? title = null)
    {
        foreach (var child in banner.GetChildren())
        {
            banner.RemoveChild(child);
            child.QueueFree();
        }
        banner.Visible = triggering.Count > 0;
        if (triggering.Count == 0)
            return;

        var icons = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        icons.AddThemeConstantOverride("separation", (int)(8 * scale));
        foreach (var badge in triggering)
            icons.AddChild(new TextureRect
            {
                Texture = badge.Icon,
                CustomMinimumSize = new Vector2(BannerIconSize, BannerIconSize) * scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        banner.AddChild(title == null
            ? BackedPanel(icons)
            : BackedPanel(Titled(title, icons, scale), TitledMinWidth));
    }

    /// <summary>
    /// A caption stacked over a row, both inside one backing, so the panel says
    /// what it is. Separation is zero: TitleRowHeight is the whole line, which is
    /// what the caller's band arithmetic assumes.
    /// </summary>
    private static Control Titled(string title, Control row, float scale)
    {
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 0);
        var caption = new MegaLabel
        {
            Text = title,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutoSizeEnabled = false,
            CustomMinimumSize = new Vector2(0f, TitleRowHeight * scale),
        };
        Style(caption, (int)(TitleFontSize * scale), StsColors.gold,
            (int)(TitleOutlineSize * scale));
        column.AddChild(caption);
        column.AddChild(row);
        return column;
    }

    /// <summary>
    /// The native tooltip's backing sandwich around some content: shadow and
    /// nine-patch filling a MarginContainer, content inset by the art's borders.
    /// <paramref name="minWidth" /> raises the floor for panels that should read as
    /// a billboard rather than hug a short row.
    /// </summary>
    public static Control BackedPanel(Control content, float minWidth = 0f)
    {
        var panel = new MarginContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            // The nine-patch corners need this much room to render undistorted.
            CustomMinimumSize = new Vector2(Math.Max(146f, minWidth), 75f),
        };
        // hover_tip.tscn lets the art overhang the layout box a little.
        panel.AddThemeConstantOverride("margin_right", -8);
        panel.AddThemeConstantOverride("margin_bottom", -8);
        panel.AddChild(BackingPatch(BackingShadowRegion, new Color(0f, 0f, 0f, 0.25098f)));
        panel.AddChild(BackingPatch(BackingRegion, Colors.White));

        var inset = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        // hover_tip.tscn's own left inset is 22, against a right of 45 — asymmetric
        // because the art carries transparent padding down that edge and the native
        // tip's text is left aligned anyway. Every row here is centered, so the two
        // sides have to match or the content sits left of the frame it is centered
        // in: 22 against 45 (less the panel's own -8 overhang) put it 11px off, far
        // enough to read as a mistake.
        inset.AddThemeConstantOverride("margin_left", 45);
        inset.AddThemeConstantOverride("margin_top", 16);
        inset.AddThemeConstantOverride("margin_right", 45);
        inset.AddThemeConstantOverride("margin_bottom", 28);
        inset.AddChild(content);
        panel.AddChild(inset);
        return panel;
    }

    private static NinePatchRect BackingPatch(Rect2 region, Color modulate) => new()
    {
        // Loaded per build rather than cached in a static; see Style's note.
        Texture = ResourceLoader.Load<Texture2D>(BackingTexturePath),
        RegionRect = region,
        Modulate = modulate,
        PatchMarginLeft = 55,
        PatchMarginTop = 43,
        PatchMarginRight = 91,
        PatchMarginBottom = 32,
        AxisStretchHorizontal = NinePatchRect.AxisStretchMode.Tile,
        AxisStretchVertical = NinePatchRect.AxisStretchMode.Tile,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

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

    /// <summary>
    /// Replace the strip's badges. An empty list leaves the strip blank. With
    /// <paramref name="backed" /> the row sits on the native tooltip backing — for
    /// strips that float over busy art rather than on an existing panel. Scale
    /// multiplies every badge metric; a prefix becomes a leading label, used to
    /// absorb the native hand-count digit; a title captions the panel on its own
    /// line and needs <paramref name="backed" /> to have a panel to caption.
    /// </summary>
    public static void SetBadges(
        HBoxContainer strip, IReadOnlyList<SourceBadge> badges, bool backed = false,
        float scale = 1f, string? prefix = null, string? title = null)
    {
        foreach (var child in strip.GetChildren())
        {
            // Removed before the deferred free so the outgoing badges stop taking
            // room in the row this frame, not next.
            strip.RemoveChild(child);
            child.QueueFree();
        }
        if (badges.Count == 0)
            return;

        var row = backed
            ? new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            }
            : strip;
        if (!ReferenceEquals(row, strip))
            row.AddThemeConstantOverride("separation", (int)(4 * scale));

        if (prefix != null)
        {
            var lead = new MegaLabel
            {
                Text = prefix,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AutoSizeEnabled = false,
            };
            Style(lead, (int)(26 * scale), StsColors.cream, (int)(8 * scale));
            row.AddChild(lead);
        }
        foreach (var badge in badges)
            row.AddChild(CreateBadge(badge, scale));
        if (!ReferenceEquals(row, strip))
            strip.AddChild(title == null
                ? BackedPanel(row)
                : BackedPanel(Titled(title, row, scale), TitledMinWidth));
    }

    private static Control CreateBadge(SourceBadge badge, float scale = 1f)
    {
        var cell = new Control
        {
            CustomMinimumSize = CellSize * scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var icon = new TextureRect
        {
            Texture = badge.Icon,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            OffsetRight = IconSize * scale,
            OffsetBottom = IconSize * scale,
        };
        cell.AddChild(icon);

        if (badge.Broken)
            cell.AddChild(BrokenMark(scale));
        else if (badge.CountText != null)
            cell.AddChild(CountLabel(badge, scale));
        // Projection (the value a counter is about to become) outranks primed gold;
        // both are decided in CountLabel.

        if (badge.Tip is { } tip)
            AttachHoverTip(cell, badge, tip);
        return cell;
    }

    /// <summary>
    /// A native hover tip on the badge. The cell starts catching the mouse for it,
    /// which is safe only where the strip floats over empty background — never on a
    /// badge above a hand card, where it would steal the card's own hover.
    /// </summary>
    private static void AttachHoverTip(Control cell, SourceBadge badge, BadgeTip tip)
    {
        cell.MouseFilter = Control.MouseFilterEnum.Stop;
        cell.MouseEntered += () => Guard.Run("Showing a badge hover tip", () =>
        {
            object boxed = new HoverTip(new LocString("gameplay_ui", "DRAW_PILE_INFO"));
            TipTitle.SetValue(boxed, tip.Title);
            TipDescription.SetValue(boxed, tip.Body);
            var hoverTip = (HoverTip)boxed;
            hoverTip.Id = $"{MainFile.ModId}.{badge.Id}";
            NHoverTipSet.CreateAndShow(cell, hoverTip, HoverTipAlignment.Left);
        });
        cell.MouseExited += () => Guard.Run("Hiding a badge hover tip", () =>
            NHoverTipSet.Remove(cell));
    }

    private static MegaLabel CountLabel(SourceBadge badge, float scale)
    {
        var label = new MegaLabel
        {
            Text = badge.CountText,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutoSizeEnabled = false,
            // power.tscn's %AmountLabel offsets: a wide right-aligned band whose
            // right edge sits just off the icon's bottom-right corner.
            OffsetLeft = -56f * scale,
            OffsetTop = 21f * scale,
            OffsetRight = 44f * scale,
            OffsetBottom = 44f * scale,
        };
        var color = badge.Projected ? StsColors.aqua
            : badge.Primed ? StsColors.gold
            : StsColors.cream;
        Style(label, (int)(CountFontSize * scale), color, (int)(10 * scale));
        return label;
    }

    /// <summary>The slash for a play that would break the source, over the icon.</summary>
    private static TextureRect BrokenMark(float scale) => new()
    {
        // Loaded per badge rather than cached in a static; see Style's note.
        Texture = ResourceLoader.Load<Texture2D>(BrokenTexturePath),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        Modulate = BrokenModulate,
        OffsetRight = IconSize * scale,
        OffsetBottom = IconSize * scale,
    };

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
