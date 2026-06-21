#!/usr/bin/env bash
# Publishes managed CLI tools and stages a release folder with tools/, runtime/, and install scripts.
set -euo pipefail

runtime="${1:?Usage: $0 <runtime> [output-dir]}"
output_dir="${2:-}"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
package_dir="${output_dir:-$repo_root/out/publish/managed/$runtime}"
tools_dir="$package_dir/tools"
runtime_dir="$package_dir/runtime"

mkdir -p "$tools_dir" "$runtime_dir"

bash "$script_dir/publish-managed-cli-tools.sh" "$runtime" "$tools_dir"
bash "$script_dir/download-dotnet-runtime.sh" "$runtime" "$runtime_dir"

cp "$script_dir/install-dotnet-runtime.ps1" "$package_dir/"
cp "$script_dir/install-dotnet-runtime.cmd" "$package_dir/"
cp "$script_dir/install-dotnet-runtime.sh" "$package_dir/"
chmod +x "$package_dir/install-dotnet-runtime.sh"

echo "Staged package: $package_dir"
echo "  tools/    — single-file executables"
echo "  runtime/  — bundled .NET 8 runtime installer"
echo "  install-dotnet-runtime.* — run before first use if .NET 8 is not installed"
