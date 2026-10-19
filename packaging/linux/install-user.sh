#!/usr/bin/env bash
set -euo pipefail
if [[ ${EUID:-$(id -u)} == 0 ]]; then
  printf '%s\n' 'Run this installer as your desktop user, without sudo.' >&2
  exit 1
fi
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
base="$HOME/.local/opt/NekoProxy"
target="$base/0.6.0"
if [[ -e "$target" ]]; then
  printf 'Existing app directory preserved: %s\nUse a new version directory or back it up before replacing it.\n' "$target" >&2
  exit 1
fi
mkdir -p -- "$base" "$HOME/.local/bin"
stage=$(mktemp -d "$base/.install-XXXXXXXX")
cp -R -- "$root/." "$stage/"
chmod +x "$stage/NekoProxy.Desktop"
mv -- "$stage" "$target"
if [[ -e "$HOME/.local/bin/nekoproxy" || -L "$HOME/.local/bin/nekoproxy" ]]; then
  printf 'Existing launcher preserved: %s\n' "$HOME/.local/bin/nekoproxy"
else
  ln -s -- "$target/NekoProxy.Desktop" "$HOME/.local/bin/nekoproxy"
fi
data="${XDG_DATA_HOME:-$HOME/.local/share}"
[[ "$data" == /* ]] || data="$HOME/.local/share"
mkdir -p -- "$data/applications" "$data/icons/hicolor/512x512/apps"
cp -- "$target/NekoProxy.png" "$data/icons/hicolor/512x512/apps/org.nekoproxy.desktop.png"
# Escape Desktop Entry Exec reserved characters, without evaluating shell input.
exe="$target/NekoProxy.Desktop"
exe=${exe//\\/\\\\}; exe=${exe//\"/\\\"}; exe=${exe//\$/\\\$}; exe=${exe//\`/\\\`}; exe=${exe//%/%%}
{
  printf '[Desktop Entry]\nType=Application\nName=NekoProxy\n'
  printf 'Exec="%s"\nIcon=org.nekoproxy.desktop\n' "$exe"
  printf 'Terminal=false\nCategories=Network;Utility;\nStartupWMClass=NekoProxy\n'
} > "$data/applications/org.nekoproxy.desktop.desktop"
printf 'Installed NekoProxy to %s\nLaunch from your applications menu. User data and network settings were not changed.\n' "$target"
