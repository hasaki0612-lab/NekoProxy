#!/usr/bin/env bash
set -euo pipefail
[[ $(uname -s) == Linux && $(uname -m) == x86_64 ]] || { printf '%s\n' 'Build on native Arch Linux x86_64.' >&2; exit 1; }
[[ ${EUID:-$(id -u)} != 0 ]] || { printf '%s\n' 'Do not run makepkg as root.' >&2; exit 1; }
command -v makepkg >/dev/null || { printf '%s\n' 'Install the standard Arch base-devel tools first.' >&2; exit 1; }
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
[[ -f PKGBUILD ]] || { printf '%s\n' 'Prepare this directory with scripts/prepare-arch.py first.' >&2; exit 1; }
# No -s/-i/--force: do not install dependencies, install the app, or overwrite packages.
PKGEXT='.pkg.tar.zst' makepkg --noconfirm
actual=nekoproxy-0.6.0-1-x86_64.pkg.tar.zst
target=NekoProxy-0.6.0-1-x86_64.pkg.tar.zst
[[ -f "$actual" && ! -e "$target" ]] || { printf '%s\n' 'Expected makepkg output missing, or destination already exists.' >&2; exit 1; }
cp -- "$actual" "$target"
sha256sum "$target" > "$target.sha256"
printf '%s\n' 'Package built by makepkg. Installation, GUI and Secret Service still require native acceptance.'
