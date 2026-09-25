#!/usr/bin/env bash
set -euo pipefail
testroot="$(mktemp -d)"
export XDG_DATA_HOME="$testroot/data"
export HOME="$testroot/home"
mkdir -p "$HOME" "$XDG_DATA_HOME" artifacts/ui
setup="$PWD/artifacts/releases/KPCLauncher-linux-Setup.run"
KPC_SMOKE_SCREENSHOT="$PWD/artifacts/ui/linux-installer.png" timeout 30 xvfb-run -a "$setup" --smoke-test
"$setup" --install-to "$testroot/Custom location é" > "$testroot/install-output"
app="$(tail -n 1 "$testroot/install-output")"
test -x "$app"
test -f "$testroot/Custom location é/.kpc-install.json"
KPC_SMOKE_SCREENSHOT="$PWD/artifacts/ui/linux-launcher.png" timeout 30 xvfb-run -a "$app" --smoke-test
# No game, server, private package, Steam command or account is involved.
export WINEPREFIX="$testroot/wine"
export WINEDEBUG=-all
set +e
timeout 120 xvfb-run -a wine "$(dirname "$app")/game-worker/KpcGameWorker.exe" --tester-worker > artifacts/ui/worker-smoke.log 2>&1
result=$?
set -e
cat artifacts/ui/worker-smoke.log
test "$result" -eq 1
grep -F 'KPC_ERROR Invalid game worker connection.' artifacts/ui/worker-smoke.log
printf 'Native installer, custom installation, launcher UI and Wine worker smoke checks passed.\n'
