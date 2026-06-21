#!/usr/bin/env bash
# Downloads .NET 8 runtime installers into runtime/ for offline bundling with RXDK tool packages.
set -euo pipefail

runtime="${1:?Usage: $0 <win-x64|linux-x64|linux-arm64|osx-x64|osx-arm64> [output-dir]}"
output_dir="${2:-}"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
out_dir="${output_dir:-$repo_root/runtime}"
channel="${DOTNET_CHANNEL_VERSION:-8.0}"

mkdir -p "$out_dir"

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required to parse .NET release metadata." >&2
  exit 1
fi

metadata_url="https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/${channel}/releases.json"
echo "Fetching release metadata from $metadata_url ..."
metadata="$(curl -fsSL "$metadata_url")"

latest_release="$(echo "$metadata" | jq -r '."latest-release"')"
if [[ -z "$latest_release" || "$latest_release" == "null" ]]; then
  echo "Could not read latest-release from .NET release metadata." >&2
  exit 1
fi

echo "Latest stable release: $latest_release"

find_file_url() {
  local section="$1"
  local rid="$2"
  local pattern="$3"
  echo "$metadata" | jq -r --arg ver "$latest_release" --arg section "$section" --arg rid "$rid" --arg re "$pattern" '
    .releases[] | select(."release-version" == $ver) |
    .[$section].files[]? | select(.rid == $rid and (.name | test($re))) | .url' | head -n 1
}

case "$runtime" in
  win-x64)
    url="$(find_file_url windowsdesktop win-x64 'windowsdesktop-runtime-win-x64\.exe$')"
    dest="$out_dir/windowsdesktop-runtime-win-x64.exe"
    ;;
  linux-x64)
    url="$(find_file_url runtime linux-x64 'dotnet-runtime-linux-x64\.tar\.gz$')"
    dest="$out_dir/dotnet-runtime-linux-x64.tar.gz"
    ;;
  linux-arm64)
    url="$(find_file_url runtime linux-arm64 'dotnet-runtime-linux-arm64\.tar\.gz$')"
    dest="$out_dir/dotnet-runtime-linux-arm64.tar.gz"
    ;;
  osx-x64)
    url="$(find_file_url runtime osx-x64 'dotnet-runtime-osx-x64\.tar\.gz$')"
    dest="$out_dir/dotnet-runtime-osx-x64.tar.gz"
    ;;
  osx-arm64)
    url="$(find_file_url runtime osx-arm64 'dotnet-runtime-osx-arm64\.tar\.gz$')"
    dest="$out_dir/dotnet-runtime-osx-arm64.tar.gz"
    ;;
  *)
    echo "Unsupported runtime: $runtime" >&2
    exit 1
    ;;
esac

if [[ -z "$url" || "$url" == "null" ]]; then
  echo "Could not resolve download URL for $runtime." >&2
  exit 1
fi

echo "Downloading $url ..."
curl -fsSL "$url" -o "$dest"
echo "Saved: $dest"
