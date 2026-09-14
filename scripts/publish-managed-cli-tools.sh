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
  "Rxdk.XbDel/Rxdk.XbDel.csproj:xbdel"
  "Rxdk.XbeCopy/Rxdk.XbeCopy.csproj:xbecopy"
  "Rxdk.ImageBld/Rxdk.ImageBld.csproj:imagebld"
  "Rxdk.Bundler/Rxdk.Bundler.csproj:bundler"
  "Rxdk.XactBld/Rxdk.XactBld.csproj:xactbld"
  "Rxdk.SkinBld/Rxdk.SkinBld.csproj:skinbld"
  "Rxdk.Xsasm/Rxdk.Xsasm.csproj:xsasm"
  "Rxdk.XboxLaunch.Cli/Rxdk.XboxLaunch.Cli.csproj:xbox-launch"
  "Rxdk.XboxDbgBridge.Cli/Rxdk.XboxDbgBridge.Cli.csproj:xboxdbg-bridge"
  # The shared build engine + debug adapter (moved here from RXDK-VS20XX). Both IDEs consume these
  # from the tools bundle: Rxdk.Cli builds/links/packs a title; Rxdk.Dap is the debug adapter.
  "Rxdk.Cli/Rxdk.Cli.csproj:Rxdk.Cli"
  "Rxdk.Dap/Rxdk.Dap.csproj:Rxdk.Dap"
  "Rxdk.XbWatson/Rxdk.XbWatson.csproj:xbwatson"
  # Avalonia GUI app, same single-file publish shape as xbwatson (SingleFilePublish.props
  # embeds the Avalonia native libs), so it ships as one flat exe in tools/ too.
  "Rxdk.XbNeighborhood/Rxdk.XbNeighborhood.csproj:xbneighborhood"
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

echo "Publishing $tool_name (single-file framework-dependent, $runtime)..."
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

echo "Published ${#cli_tools[@]} single-file framework-dependent tools to: $tools_dir"
