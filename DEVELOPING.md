# Developing

## Build

```bash
dotnet build MicroAssistant.csproj
dotnet test MicroAssistant.Tests/MicroAssistant.Tests.csproj
```

`Sts2PathDiscovery.props` finds the game through the Steam registry keys on Windows
and the default Steam library on macOS. If it cannot, copy
`Directory.Build.props.example` to `Directory.Build.props` and set `Sts2Path`. On
macOS the .NET 9 SDK may live in `~/.dotnet`; put it on `PATH` (and set
`DOTNET_ROOT`) if `dotnet` is not found.

Building copies `MicroAssistant.json`, `MicroAssistant.dll`, and `MicroAssistant.pdb`
into `<game>/mods/MicroAssistant/` (on macOS,
`SlayTheSpire2.app/Contents/MacOS/mods/MicroAssistant/`). Pass
`-p:SkipModInstall=true` to build without installing. Close the game first: on
Windows the DLL is locked, and on either platform a running game keeps the build it
loaded until restarted.

A local copy and a Workshop copy of the same mod id conflict, and the loader
disables one. It compares semantic versions and prefers the local copy when they
are equal, so a development build shadows the published one without renaming
anything.

Runtime diagnostics are in the game's `godot.log` (`%APPDATA%\SlayTheSpire2\logs\`
on Windows, `~/Library/Application Support/SlayTheSpire2/logs/` on macOS). A
successful start logs `Micro Assistant <version> initialized.`

## Publish to the Steam Workshop

```bash
scripts/package-workshop.sh
```

That stages `workshop/content/` and prints the `ModUploader upload -w …` command to
run next (set `MOD_UPLOADER` to the uploader's path to have it printed in full). Get
the uploader for your platform from
<https://github.com/megacrit/sts2-mod-uploader/releases>. On Windows, run the script
from Git Bash or WSL. Steam must be running for the upload.

`workshop/mod_id.txt` appears after the first upload. **Commit it** — it is the only
link between this repository and the published Workshop item.

The page description's source of truth is `workshop/workshop-description.txt`; keep
`workshop.json`'s `description` in sync with it. Gallery images live in `images/`
and are copied to `workshop/previews/` (each under 1 MB). The gallery shows in
upload order, not filename order: to reorder, upload once with an empty
`previews/` directory, then again with the full set. A missing directory leaves the
gallery untouched.

See `docs/sts2-modding.md` for the full pipeline and `workshop/README.md` for the
`workshop.json` field reference.
