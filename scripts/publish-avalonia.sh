#!/usr/bin/env bash
# Build and publish the Avalonia RXDK Neighborhood app for Linux/macOS.
set -euo pipefail

MODE="${1:-framework}"
RUNTIME="${2:-linux-x64}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$REPO_ROOT/src/Rxdk.XbNeighborhood/Rxdk.XbNeighborhood.csproj"
PUBLISH_DIR="$REPO_ROOT/out/publish/Rxdk.XbNeighborhood-$RUNTIME"

PUBLISH_ARGS=(
    publish "$PROJECT"
    -c Release
    -r "$RUNTIME"
    -o "$PUBLISH_DIR"
)

if [[ "$MODE" == "self-contained" ]]; then
    PUBLISH_ARGS+=(-p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true)
else
    PUBLISH_ARGS+=(--self-contained false)
fi

echo "Publishing Avalonia Rxdk.XbNeighborhood ($MODE, $RUNTIME)..."
dotnet "${PUBLISH_ARGS[@]}"
echo "Published to: $PUBLISH_DIR"
