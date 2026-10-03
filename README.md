# Micro Assistant

An informational Slay the Spire 2 mod that shows how your relics and powers react to
what you are about to do. Small readouts appear over the card you are about to play, by
the End Turn button, and at the top of the screen. Nothing about the game's behavior
changes.

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
