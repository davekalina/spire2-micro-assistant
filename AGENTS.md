# Micro Assistant — repository instructions

This repository is one Slay the Spire 2 mod. These instructions apply throughout it.
A more specific `AGENTS.md` may add to or override them for its directory.

`docs/sts2-modding.md` describes how the game's mod loader, manifest, and Steam Workshop
pipeline work. Those are platform facts. This file is how I want the work done.

## This mod

| | |
| --- | --- |
| Mod id | `MicroAssistant` |
| Display name | Micro Assistant |
| Installs to | `<game>/mods/MicroAssistant/` |
| Manifest | `MicroAssistant.json` |
| Version is also printed in | `MicroAssistantCode/MainFile.cs` |
| Gameplay | Informational only; `affects_gameplay` is `false` |
| Dependencies | None yet |

The mod shows how counting sources — relics and powers — react to what the player
is about to do, on two surfaces during combat:

- **Above the focused hand card** (mouse hover and controller focus both count): a
  centered row of badges — the source's atlas icon with its current count, or X-ed
  out when playing that card would break its condition or miss its window. The
  strip stays with the card while it is dragged out and played, and from the
  moment the card is held for play the counts switch to **projected values** —
  what each counter is about to become — in the aqua projection color.
- **Above the End Turn button**: badges for sources where ending the turn is the
  decision — will/will-NOT verdicts with native hover tips.

Covered sources (one registry entry each in `CountingTracker`): relics —
Pocketwatch, Pendulum, Pen Nib, Nunchaku, Kunai, Shuriken, Ornamental Fan,
Kusarigama, Letter Opener, Tuning Fork, Iron Club, Brilliant Scarf, Velvet Choker,
Art of War, Ripple Basin, Pael's Eye, Rainbow Ring, Happy Flower, Fake Happy
Flower, Pollinous Core, Stone Calendar, Music Box, Vambrace, Permafrost, Pael's
Legion, Orichalcum, Fake Orichalcum, Joss Paper (Exhaust cards advance it;
Ethereal cards carry the badge as a reminder without advancing or projecting);
powers — Echo Form, Void Form, Nostalgia,
Unmovable, Lethality, Phantom Blades, Juggling, Pale Blue Dot, Panache.
Deliberately excluded: Unsettling Lamp (a card's "applies a debuff to an enemy" is
only decidable at play time), locks and end-turn reversals (the game communicates
them), Make It So / Normality / FTL.

The layering that must not regress silently:

- `CountingRules` is the pure decision logic (game-independent, linked into the test
  project) — what a card play does to a source, what belongs above End Turn.
- `CountingTracker` maps rules onto live models, one registry entry per source.
  Counts come from the members the native UI prints (`DisplayAmount`, `TurnsSeen`,
  DynamicVars) or from `CombatQueries`, which mirrors each source's own combat-
  history predicate verbatim — never from independent bookkeeping.
- `CombatOverlayView` is event-driven off the signals the native UI repaints on
  (`DisplayAmountChanged`/`StatusChanged`, `HoveredModelTracker.HoverChanged`,
  `CombatManager` lifecycle, plus the coalesced `StateTracker.CombatStateChanged`
  as the catch-all for power and history changes). No polling, no per-frame work.
- The Harmony patches are `NCombatUi._Ready`/`_ExitTree` in `CombatUiPatch` and a
  postfix on the private `NPlayerHand.StartCardPlay` (the one transition no game
  event covers: hover→play raises no HoverChanged and moves no combat state);
  every game→mod boundary is wrapped in `Guard`. The one private read is the
  hand's `_currentCardPlay` Godot property (with a child-scan fallback) for the
  follow-the-played-card behavior.

### Surfaces to audit

The gear menu's first row is a status line — "Tracking X of N relics, Y of M
powers" — refreshed each open, so a badge-less run is diagnosable at a glance.
Six experimental view switches (session-only, behind the settings-gear icon left
of the version text, `ViewOptions` + `OptionsPanel`): **Big & Bold**
(focused card's badges doubled at the top-center panel instead of above the card),
**Always On** (every hand card carries its strip), **Card Overlay** (strip on the
card art, unbacked, at Hypergeo's proven on-art band), **Replace Card Count** (the
native hand digit hides and the strip absorbs it as a "N." prefix, dropping onto
the digit's spot), **Replace End Turn Button** (End Turn badges render as a flush
backed extension on the button's top edge instead of floating above it).

Any directional change must be applied across all of these:

- the badge strip above the focused hand card (mouse hover and controller focus;
  count badge, X-ed-out badge, bare-icon badge, primed gold count, multi-source
  rows mixing relics and powers);
- the strip riding a card that is being dragged, targeted, and played (mouse and
  controller; cancel returns it to hand cleanly), including the hover→play flip
  to aqua projected counts and the flip back on cancel, and the in-play absolute
  z that keeps badges above the targeting arrow (relative z on plain hover so
  overlay screens still cover the strip);
- the "Triggers [icons]" banner (top-center at 20% down the screen; visible only
  while a play in progress will fire a source: primed counters, armed first-X
  sources, active allowances);
- the badge strip above the End Turn button (active, X-ed out, hidden states; enemy
  turn, when the strip rides the button's slide-out tween);
- the hover tips on End Turn badges (will / will NOT wording, counts read from the
  source's DynamicVars; hand badges must never catch the mouse and get no tips);
- badge visuals shared by both strips (`BadgeStrip`: icon, count label, X mark);
- combat lifecycle edges: combat start and end, relic gained or lost mid-combat,
  power applied or removed mid-combat, relic melted, instanced powers appearing
  more than once, card played while another card is focused;
- every view switch in every combination (the options compose: placement comes
  from Card Overlay > Replace Card Count > default; Big & Bold moves the focused
  display; Always On multiplies strips), plus flipping a switch mid-combat and
  restoring the hidden hand digits when Replace Card Count turns off.

## Mod UI: match the game

Treat the game's existing UI as the design system for this mod.

- Prefer duplicating or instantiating native game scenes, controls, textures,
  frames, hover tips, labels, buttons, selection outlines, and animations.
- Use the game's `MegaLabel`/`MegaRichTextLabel` fonts and theme values. Do not
  introduce generic Godot controls, arbitrary fonts, improvised colors, or
  custom panel styling when an equivalent game element exists.
- Before creating a UI element, inspect the installed game/decompiled source
  and find the closest native screen or widget to use as the reference.
- Preserve native interaction behavior: hover animation, click handling,
  focus behavior, tooltips, dismissal, input capture, and selection feedback.
- If a native element is duplicated outside its original container, reset all
  inherited anchors, offsets, scale, rotation, minimum size, and mouse filters,
  then explicitly center its label after it has a real layout size.
- Selection state must be visually unmistakable and should reuse the relevant
  native selection outline or reticle. Do not layer multiple independent
  selection indicators.
- Prefer native hover tips for contextual details instead of persistent tiny
  overlays. Persistent information belongs in the screen's native information
  strip or another established game surface.

UI work is not complete until it has been inspected in-game at the target
resolution and checked for overlap, centering, clipping, input leakage, hover
behavior, and consistency with adjacent native UI.

## Dense information: use structure and alignment

When presenting several related values, optimize for scanning.

- Prefer tables or explicit columns over prose-like runs of labels and values.
- Keep row labels left-aligned and numeric values right-aligned.
- Give numeric columns enough fixed or proportional width that values line up
  against the right side of their containing panel.
- Center controls within their rows and center text within button bounds.
- Use concise visible labels when space is limited and put the longer
  explanation in a native tooltip.
- Avoid placing important text over detailed card art. Use native dark
  backgrounds, information strips, or tooltip frames for legibility.

## Directional changes apply across the whole mod

A product or visual direction is a mod-wide rule unless the request explicitly
limits it to one screen.

Do not fix only the screenshot or screen where the inconsistency was reported.
Search for every implementation of the old wording, style, calculation, or
interaction and update or intentionally exempt each one. Keep shared behavior
in shared helpers where practical so screens cannot silently diverge.

For every directional change, audit all equivalent surfaces of this mod — see
**Surfaces to audit** above — including buttons, labels, selection state, hover
tips, persistent overlays, bottom information text, empty states, and
keyboard/controller dismissal.

## Version, verification, and GitHub

Every new mod version must be committed and pushed to this repository during the
same task.

1. Keep the manifest version and any displayed/logged version in sync.
2. Update user-facing documentation when behavior or controls change.
3. Build the mod and run its test suite before committing.
4. Check `git diff --check` and review the complete scoped diff.
5. Commit only the intended project files; preserve unrelated and untracked
   user files.
6. Push the active branch to GitHub and report the commit hash.

Do not describe a version as complete if it exists only in the working tree.
A local game deployment is separate from a GitHub release. Always deploy a
verified mod build when the game is not running, even if the user did not
separately request deployment. Verify the installed manifest and DLL after
copying. If the game is running, do not terminate it to replace a locked DLL;
report that deployment is pending instead.

## Steam Workshop

The `workshop/` directory is this mod's uploader workspace. See
`docs/sts2-modding.md` for what each file means.

- Never change the manifest `id` once the mod has been published. It is the
  Workshop item's identity, the install folder name, and the DLL filename.
- `workshop/mod_id.txt` is created by the uploader on the first publish and must
  be committed. Losing it orphans the published item and the next upload creates
  a duplicate.
- Stage uploads with `scripts/package-workshop.ps1`. It rebuilds Release and
  populates `workshop/content/`. Do not hand-copy files, and do not ship the
  `.pdb`.
- Fill in `changeNote` in `workshop/workshop.json` before every update; it is
  the changelog subscribers see.
- Bump the manifest version and push to GitHub before uploading, so the
  published version always corresponds to a commit.
