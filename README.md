# Micro Assistant

An informational Slay the Spire 2 mod that shows how your counting relics and
powers react to what you are about to do.

- Hover or controller-focus a card in hand, and the sources that care about playing
  it appear above the card — with their current count, or X-ed out if playing the
  card would break their condition (a fourth card under Pocketwatch, an attack
  under Art of War) or miss their window (a card past Echo Form's doublings). The
  row stays with the card while you drag it out and play it — and once the card is
  held for play, counts turn aqua and show what each counter is **about to
  become** (Tuning Fork at 5 reads 6; Pen Nib at 9 reads 0 as it fires), and if
  the play will set something off, a bold **"Triggers"** banner with the icons
  appears near the top of the screen.
- Above the End Turn button, sources for which ending the turn is the decision show
  a will/will-NOT verdict: Pocketwatch, Art of War, Ripple Basin, Orichalcum,
  Pendulum and the other turn tickers, Pael's Eye, Pale Blue Dot. Hovering a badge
  shows a native tooltip spelling it out — "Will draw 3 extra cards next turn." or
  "Will NOT".

An upper-right panel on the combat screen (under the version/seed block) offers
live experimental view switches: Big & Bold, Always On, Card Overlay, Replace
Card Count, and Replace End Turn Button.

Counts are read from the same values the game's own UI prints, so the two never
disagree. Nothing about the game's behavior changes.

Covers 31 relics and 10 powers; the full list is in `AGENTS.md`. Adding another
source is one registry entry in `MicroAssistantCode/CountingTracker.cs`. The gear
menu reports how many of the current run's relics and powers are being tracked.

## Build

```powershell
dotnet build .\MicroAssistant.csproj
dotnet test .\MicroAssistant.Tests\MicroAssistant.Tests.csproj
```

`Sts2PathDiscovery.props` finds the game through the Steam registry keys. If it cannot,
copy `Directory.Build.props.example` to `Directory.Build.props` and set `Sts2Path`.

Building copies `MicroAssistant.json`, `MicroAssistant.dll`, and `MicroAssistant.pdb` into
`<game>/mods/MicroAssistant/`. Pass `-p:SkipModInstall=true` to build without installing.
Close the game first, or the DLL will be locked.

Runtime diagnostics are in `%APPDATA%\SlayTheSpire2\logs\godot.log`. A successful start
logs `Micro Assistant v0.1.0 initialized`.

## Publish to the Steam Workshop

```powershell
.\scripts\package-workshop.ps1
```

That stages `workshop/content/` and prints the `ModUploader.exe upload -w …` command to
run next. Get the uploader from
<https://github.com/megacrit/sts2-mod-uploader/releases>.

`workshop/mod_id.txt` appears after the first upload. **Commit it** — it is the only link
between this repository and the published Workshop item.

See `docs/sts2-modding.md` for the full pipeline and `workshop/README.md` for the
`workshop.json` field reference.
