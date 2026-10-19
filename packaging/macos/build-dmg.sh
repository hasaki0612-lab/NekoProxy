#!/usr/bin/env bash
set -euo pipefail
[[ $(uname -s) == Darwin ]] || { printf '%s\n' 'A real macOS hdiutil environment is required; no DMG was generated.' >&2; exit 1; }
[[ $# == 3 ]] || { printf '%s\n' 'Usage: build-dmg.sh /absolute/NekoProxy.app x64|arm64 /absolute/new-output-directory' >&2; exit 1; }
app=$1
arch=$2
out=$3
[[ "$app" == /* && "$out" == /* ]] || { printf '%s\n' 'Use absolute paths.' >&2; exit 1; }
[[ "$arch" == x64 || "$arch" == arm64 ]] || exit 1
[[ -d "$app/Contents/MacOS" && ! -e "$out" ]] || { printf '%s\n' 'App missing, or output already exists; preserving existing files.' >&2; exit 1; }
plutil -lint "$app/Contents/Info.plist"
[[ $(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$app/Contents/Info.plist") == org.nekoproxy.desktop ]] || exit 1
[[ $(/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' "$app/Contents/Info.plist") == NekoProxy.icns ]] || exit 1
[[ -x "$app/Contents/MacOS/NekoProxy.Desktop" && -f "$app/Contents/Resources/NekoProxy.icns" ]] || exit 1
expected=x86_64
[[ "$arch" == arm64 ]] && expected=arm64
[[ $(lipo -archs "$app/Contents/MacOS/NekoProxy.Desktop") == "$expected" ]] || { printf '%s\n' 'Apphost architecture mismatch.' >&2; exit 1; }
mkdir -p -- "$out/stage"
ditto "$app" "$out/stage/NekoProxy.app"
# Seal a staging copy only; leave the input app and existing candidates intact.
python3 "$(dirname -- "${BASH_SOURCE[0]}")/seal-bundle.py" "$out/stage/NekoProxy.app" "$out"
codesign --verify --deep --strict --verbose=2 "$out/stage/NekoProxy.app" > "$out/codesign-after-sealing.log" 2>&1
ln -s /Applications "$out/stage/Applications"
image="$out/NekoProxy-$arch.dmg"
hdiutil create -volname NekoProxy -srcfolder "$out/stage" -format UDZO "$image"
hdiutil verify "$image" > "$out/hdiutil-verify.log"
shasum -a 256 "$image" > "$image.sha256"
if codesign --verify --deep --strict "$out/stage/NekoProxy.app" > "$out/codesign-verify.log" 2>&1; then
  printf '%s\n' 'Existing code signature verification passed; notarization and GUI install are not implied.'
else
  printf '%s\n' 'Unsigned or unverified candidate. No signing, notarization, or Gatekeeper bypass was performed.'
fi
printf '%s\n' 'Native DMG created and hdiutil verified. Installation and GUI acceptance remain separate.'
