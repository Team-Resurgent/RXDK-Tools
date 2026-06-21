#!/usr/bin/env bash
set -euo pipefail

runtime="${1:?Usage: $0 <runtime> [output-dir]}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
dotnet_root="$repo_root/src"
tools_dir="${2:-"$repo_root/out/publish/managed-cli-tools-$runtime"}"
temp_root="$(mktemp -d)"

cli_tools=(
  "Rxdk.XbSet/Rxdk.XbSet.csproj:xbset"
  "Rxdk.XbCp/Rxdk.XbCp.csproj:xbcp"
  "Rxdk.XbDir/Rxdk.XbDir.csproj:xbdir"
  "Rxdk.XbMkdir/Rxdk.XbMkdir.csproj:xbmkdir"
  "Rxdk.XbeCopy/Rxdk.XbeCopy.csproj:xbecopy"
  "Rxdk.ImageBld/Rxdk.ImageBld.csproj:imagebld"
  "Rxdk.XboxLaunch.Cli/Rxdk.XboxLaunch.Cli.csproj:xbox-launch"
  "Rxdk.XboxDbgBridge.Cli/Rxdk.XboxDbgBridge.Cli.csproj:xboxdbg-bridge"
  "Rxdk.XbWatson/Rxdk.XbWatson.csproj:xbwatson"
)

mkdir -p "$tools_dir"

cleanup() { rm -rf "$temp_root"; }
trap cleanup EXIT

for entry in "${cli_tools[@]}"; do
  project_rel="${entry%%:*}"
  tool_name="${entry##*:}"
  project="$dotnet_root/$project_rel"
  staging="$temp_root/$tool_name"
  mkdir -p "$staging"

  echo "Publishing $tool_name (single-file, $runtime)..."
  dotnet publish "$project" -c Release -r "$runtime" -o "$staging"

  published_file="$(find "$staging" -maxdepth 1 -type f -name "$tool_name*" ! -name "*.pdb" | head -n 1)"
  if [[ -z "$published_file" ]]; then
    echo "Expected published executable '$tool_name' in $staging:" >&2
    ls -la "$staging" >&2
    exit 1
  fi

  cp -f "$published_file" "$tools_dir/"
  echo "  -> $tools_dir/$(basename "$published_file")"
done

echo "Published ${#cli_tools[@]} single-file tools to: $tools_dir"
