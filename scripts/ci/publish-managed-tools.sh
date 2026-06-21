#!/usr/bin/env bash
# Publish single-file xbset and xbWatson for CI / local staging on Linux/macOS.
set -euo pipefail

RUNTIME="${RUNTIME:-linux-x64}"
ARTIFACT_DIR="${ARTIFACT_DIR:-artifacts/managed}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
OUT_DIR="$REPO_ROOT/$ARTIFACT_DIR/$RUNTIME"

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"

publish_tool() {
    local project="$1"
    local subdir="$2"
    local publish_dir="$OUT_DIR/$subdir"

    echo "Publishing $subdir (single-file, $RUNTIME)..."
    dotnet publish "$project" -c Release -r "$RUNTIME" -o "$publish_dir"
}

publish_tool "$REPO_ROOT/src-dotnet/Rxdk.XbSet/Rxdk.XbSet.csproj" "xbset"
publish_tool "$REPO_ROOT/src-dotnet/Rxdk.XbWatson/Rxdk.XbWatson.csproj" "xbwatson"

find "$OUT_DIR" -name '*.pdb' -type f -delete 2>/dev/null || true

echo "Managed tools staged in $OUT_DIR"
