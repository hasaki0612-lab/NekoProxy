#!/usr/bin/env bash
set -euo pipefail
# Run inside dbus-run-session on a fresh hosted runner. This is never invoked on
# the user's Windows machine or a VPS. Test keyring/private keys are not uploaded.
keyring_password=$(python3 -c 'import secrets; print(secrets.token_urlsafe(32))')
printf '%s\n' "::add-mask::$keyring_password"
printf '%s' "$keyring_password" | gnome-keyring-daemon --unlock --components=secrets
unset keyring_password
pwsh -NoProfile -File scripts/Test-CI.ps1 -OutputDirectory artifacts/verification -CoreRoot artifacts/core-tools -Python python3
xvfb-run -a python3 scripts/gui-smoke.py --exe artifacts/packages/publish-linux-x64/NekoProxy.Desktop --output artifacts/gui-startup
