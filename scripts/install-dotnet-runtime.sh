#!/usr/bin/env bash
# Installs the .NET 8 runtime required by RXDK managed tools.
# Prefers a bundled archive under <package>/runtime/ (offline). Falls back to Microsoft's dotnet-install.sh.
set -euo pipefail

major_version="${DOTNET_MAJOR_VERSION:-8}"
runtime_kind="${DOTNET_RUNTIME_KIND:-dotnet}"
force=false
package_root=""
runtime_dir=""

usage() {
  echo "Usage: $0 [--force] [--package-root DIR] [--runtime-dir DIR]"
  echo "  Installs .NET ${major_version} (${runtime_kind}) for RXDK managed CLI tools."
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --force) force=true; shift ;;
    --package-root) package_root="${2:-}"; shift 2 ;;
    --runtime-dir) runtime_dir="${2:-}"; shift 2 ;;
    -h|--help) usage ;;
    *) echo "Unknown option: $1" >&2; usage ;;
  esac
done

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ -z "$package_root" ]]; then
  if [[ -d "$script_dir/tools" ]]; then
    package_root="$script_dir"
  elif [[ -d "$(dirname "$script_dir")/tools" ]]; then
    package_root="$(cd "$(dirname "$script_dir")" && pwd)"
  else
    package_root="$script_dir"
  fi
fi

if [[ -z "$runtime_dir" ]]; then
  runtime_dir="$package_root/runtime"
fi

detect_rid() {
  local os arch
  os="$(uname -s | tr '[:upper:]' '[:lower:]')"
  arch="$(uname -m)"
  case "$os" in
    linux)
      case "$arch" in
        x86_64|amd64) echo "linux-x64" ;;
        aarch64|arm64) echo "linux-arm64" ;;
        *) echo "Unsupported Linux architecture: $arch" >&2; exit 1 ;;
      esac
      ;;
    darwin)
      case "$arch" in
        arm64) echo "osx-arm64" ;;
        x86_64) echo "osx-x64" ;;
        *) echo "Unsupported macOS architecture: $arch" >&2; exit 1 ;;
      esac
      ;;
    *)
      echo "Unsupported OS: $os" >&2
      exit 1
      ;;
  esac
}

dotnet8_installed() {
  if ! command -v dotnet >/dev/null 2>&1; then
    return 1
  fi
  dotnet --list-runtimes 2>/dev/null | grep -q "^Microsoft\.NETCore\.App ${major_version}\."
}

find_bundled_archive() {
  local rid="$1"
  local preferred="$runtime_dir/dotnet-runtime-${rid}.tar.gz"
  if [[ -f "$preferred" ]]; then
    echo "$preferred"
    return 0
  fi
  local match
  match="$(find "$runtime_dir" -maxdepth 1 -type f -name "dotnet-runtime-*-${rid}.tar.gz" 2>/dev/null | sort -r | head -n 1 || true)"
  if [[ -n "$match" ]]; then
    echo "$match"
    return 0
  fi
  return 1
}

install_from_archive() {
  local archive="$1"
  local install_dir="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
  mkdir -p "$install_dir"
  echo "Installing from bundled runtime: $archive"
  tar -xzf "$archive" -C "$install_dir"
  export DOTNET_ROOT="$install_dir"
  export PATH="$install_dir:$PATH"

  if ! grep -qs 'DOTNET_ROOT=' "$HOME/.profile" 2>/dev/null; then
    {
      echo ''
      echo '# Added by RXDK Tools install-dotnet-runtime.sh'
      echo "export DOTNET_ROOT=\"$install_dir\""
      echo 'export PATH="$DOTNET_ROOT:$PATH"'
    } >> "$HOME/.profile"
    echo "Added DOTNET_ROOT to ~/.profile. Run: source ~/.profile"
  fi
}

install_from_dotnet_install_script() {
  local rid="$1"
  local install_dir="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
  local tmp
  tmp="$(mktemp)"
  echo "Downloading Microsoft dotnet-install.sh..."
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$tmp"
  chmod +x "$tmp"
  echo "Installing .NET ${major_version} runtime to ${install_dir} ..."
  "$tmp" --runtime "$runtime_kind" --channel "$major_version" --install-dir "$install_dir" --quality GA
  rm -f "$tmp"

  export DOTNET_ROOT="$install_dir"
  export PATH="$install_dir:$PATH"
  if ! grep -qs 'DOTNET_ROOT=' "$HOME/.profile" 2>/dev/null; then
    {
      echo ''
      echo '# Added by RXDK Tools install-dotnet-runtime.sh'
      echo "export DOTNET_ROOT=\"$install_dir\""
      echo 'export PATH="$DOTNET_ROOT:$PATH"'
    } >> "$HOME/.profile"
    echo "Added DOTNET_ROOT to ~/.profile. Run: source ~/.profile"
  fi
}

rid="$(detect_rid)"

echo "RXDK Tools — .NET ${major_version} runtime installer"
echo "Package root: $package_root"
echo "Runtime identifier: $rid"

if [[ "$force" != true ]] && dotnet8_installed; then
  echo ".NET ${major_version} runtime is already installed."
  exit 0
fi

if archive="$(find_bundled_archive "$rid")"; then
  install_from_archive "$archive"
else
  echo "No bundled runtime found under: $runtime_dir"
  install_from_dotnet_install_script "$rid"
fi

if ! dotnet8_installed; then
  echo "ERROR: .NET ${major_version} runtime was not detected after installation." >&2
  exit 1
fi

echo 'Done.'
