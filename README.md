# Micro Assistant

An informational Slay the Spire 2 mod that shows how your counting relics react to
what you are about to do.

- Hover or controller-focus a card in hand, and the relics that care about playing it
  appear above the card — with their current count, or X-ed out if playing the card
  would break the relic's condition (a fourth card under Pocketwatch).
- Above the End Turn button, turn-scoped relics show whether ending the turn keeps
  their payoff: Pocketwatch while its condition still holds (X-ed out once it is
  lost), Pendulum only on the turn before it fires. Hovering a badge shows a native
  tooltip spelling out what will happen — "will draw 3 extra cards next turn" or
  "will NOT".

Counts are read from the same values the relic bar prints, so the two never
disagree. Nothing about the game's behavior changes.

Covered so far: **Pocketwatch**, **Pendulum**, **Pen Nib**. Adding another counting
relic is one registry entry in `MicroAssistantCode/CountingRelicTracker.cs`.

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
