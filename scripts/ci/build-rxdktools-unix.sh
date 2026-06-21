#!/usr/bin/env bash
# Publish cross-platform managed RXDK tools (xbset, Rxdk.XbWatson) for CI.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

export ARTIFACT_DIR="${ARTIFACT_DIR:-artifacts/managed}"

case "$(uname -s)" in
    Linux)
        export RUNTIME="${RUNTIME:-linux-x64}"
        ;;
    Darwin)
        export RUNTIME="${RUNTIME:-osx-arm64}"
        ;;
    *)
        echo "Unsupported platform for managed tools: $(uname -s)" >&2
        exit 1
        ;;
esac

"$SCRIPT_DIR/publish-managed-tools.sh"
