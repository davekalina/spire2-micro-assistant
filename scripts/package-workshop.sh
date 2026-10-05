#!/usr/bin/env bash
#
# Stages this mod's Steam Workshop upload payload into workshop/content/.
#
# Builds the mod in Release without installing it into the local game folder,
# then copies exactly the files the Workshop item should contain:
#
#     <ModId>.json   the manifest
#     <ModId>.dll    the compiled mod (when has_dll)
#     <ModId>.pck    Godot resources (when has_pck)
#
# The .pdb is deliberately excluded: it is a local debugging aid, not something
# subscribers need.
#
# Nothing is uploaded. The command to run afterwards is printed at the end.
#
# Usage: scripts/package-workshop.sh [--configuration <Release|Debug>] [--skip-build]
#
#   --configuration  Build configuration. Defaults to Release; Workshop builds
#                    should stay Release.
#   --skip-build     Package whatever is already built instead of rebuilding.
#
# Set MOD_UPLOADER to the uploader executable to have the printed command use it.

set -euo pipefail

configuration=Release
skip_build=false
while [[ $# -gt 0 ]]; do
    case "$1" in
        -c|--configuration) configuration="$2"; shift 2 ;;
        --skip-build) skip_build=true; shift ;;
        -h|--help) sed -n '2,23p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done

cyan=$'\e[36m'; yellow=$'\e[33m'; green=$'\e[32m'; reset=$'\e[0m'
die() { echo "error: $*" >&2; exit 1; }
warn() { echo "${yellow}WARNING: $*${reset}" >&2; }

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"
fi
command -v dotnet >/dev/null 2>&1 || die "dotnet not found. Install the .NET 9 SDK."

project=""
for candidate in "$root"/*.csproj; do
    [[ -e "$candidate" && "$candidate" != *.Tests.csproj ]] && { project="$candidate"; break; }
done
[[ -n "$project" ]] || die "No mod .csproj found in $root"

mod_id="$(basename "$project" .csproj)"
manifest="$root/$mod_id.json"
[[ -f "$manifest" ]] || die "Manifest not found at $manifest. The manifest must be named after the mod id."

# Reads a top-level string or boolean field from the manifest.
manifest_field() {
    sed -n "s/^[[:space:]]*\"$1\"[[:space:]]*:[[:space:]]*\"\{0,1\}\([^\",]*\)\"\{0,1\}[[:space:]]*,\{0,1\}[[:space:]]*$/\1/p" \
        "$manifest" | head -n 1
}

manifest_id="$(manifest_field id)"
[[ "$manifest_id" == "$mod_id" ]] ||
    die "Manifest id '$manifest_id' does not match project name '$mod_id'. The loader requires <id>.dll, so they must match."

echo "${cyan}Packaging $(manifest_field name) ($mod_id) $(manifest_field version)${reset}"

if [[ "$skip_build" != true ]]; then
    dotnet build "$project" -c "$configuration" -p:SkipModInstall=true
fi

msbuild_property() {
    dotnet msbuild "$project" "-getProperty:$1" "-p:Configuration=$configuration" -p:SkipModInstall=true | tr -d '\r' | xargs
}
target_path="$(msbuild_property TargetPath)"
mods_path="$(msbuild_property ModsPath)"

content="$root/workshop/content"
rm -rf "$content"
mkdir -p "$content"

cp "$manifest" "$content/"
echo "  + $mod_id.json"

if [[ "$(manifest_field has_dll)" == true ]]; then
    [[ -f "$target_path" ]] || die "has_dll is true but no assembly at $target_path"
    cp "$target_path" "$content/$mod_id.dll"
    echo "  + $mod_id.dll"
fi

if [[ "$(manifest_field has_pck)" == true ]]; then
    pck="${mods_path%/}/$mod_id/$mod_id.pck"
    [[ -f "$pck" ]] ||
        die "has_pck is true but no .pck at $pck. Run 'dotnet publish' with GodotPath set in Directory.Build.props first."
    cp "$pck" "$content/"
    echo "  + $mod_id.pck"
fi

image="$root/workshop/image.png"
if [[ ! -f "$image" ]]; then
    warn "workshop/image.png is missing. The uploader requires it."
else
    image_bytes=$(wc -c < "$image" | tr -d ' ')
    if (( image_bytes >= 1048576 )); then
        warn "workshop/image.png is $(awk "BEGIN { printf \"%.2f\", $image_bytes / 1048576 }") MB. Steam rejects preview images of 1 MB or more."
    fi
fi

mod_id_file="$root/workshop/mod_id.txt"
if [[ -f "$mod_id_file" ]]; then
    echo
    echo "${yellow}Updating published item $(tr -d '[:space:]' < "$mod_id_file")${reset}"
else
    echo
    echo "${yellow}No workshop/mod_id.txt yet: this will create a NEW Workshop item.${reset}"
    echo "${yellow}Commit mod_id.txt immediately after the first upload.${reset}"
fi

echo
echo "${green}Staged in $content${reset}"
echo "Check workshop/workshop.json (especially changeNote), then run:"
echo
echo "    \"${MOD_UPLOADER:-ModUploader}\" upload -w \"$root/workshop\""
echo
