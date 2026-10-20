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

# The CMake toolchain (cmake/RxdkToolchain.cmake finds Rxdk.Cli one folder up, in tools/).
rm -rf "$tools_dir/cmake"
mkdir -p "$tools_dir/cmake/Platform"
cp "$repo_root"/cmake/*.cmake "$tools_dir/cmake/"
cp "$repo_root"/cmake/Platform/*.cmake "$tools_dir/cmake/Platform/"

cp "$script_dir/install-dotnet-runtime.ps1" "$package_dir/"
cp "$script_dir/install-dotnet-runtime.cmd" "$package_dir/"
cp "$script_dir/install-dotnet-runtime.sh" "$package_dir/"
chmod +x "$package_dir/install-dotnet-runtime.sh"

echo "Staged package: $package_dir"
echo "  tools/    — single-file executables, and cmake/ (the CMake toolchain)"
echo "  runtime/  — bundled .NET 8 runtime installer"
echo "  install-dotnet-runtime.* — run before first use if .NET 8 is not installed"
