#!/usr/bin/env bash
set -euo pipefail
# An anonymous Valve SDK depot, isolated on an ephemeral runner. No player login or game files.
root="$RUNNER_TEMP/kpc-steam-recovery-check"
mkdir "$root"
cd "$root"
curl -fsSL --retry 2 https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz -o steamcmd.tar.gz
tar -xzf steamcmd.tar.gz
timeout 180 ./steamcmd.sh +login anonymous +download_depot 1007 1006 +quit > first.log 2>&1 || { cat first.log; exit 1; }
cat first.log
depot="$(find "$root" -type d -name depot_1006 -print -quit)"
test -n "$depot"
test -n "$(find "$depot" -type f -print -quit)"
manifest="$(sed -nE 's/.*Depot download complete.*manifest ([0-9]+).*/\1/p' first.log | tail -n 1)"
[[ "$manifest" =~ ^[0-9]+$ ]]
echo 'State files after initial download:'
find "$root" -name 'state_*.patch' -printf '%P %s bytes\n'
(cd "$depot" && find . -type f -print0 | sort -z | xargs -0 sha256sum) > first.sha256
mv "$depot" "$depot.previous-first"
timeout 180 ./steamcmd.sh +login anonymous +download_depot 1007 1006 "$manifest" +quit > second.log 2>&1 || { cat second.log; exit 1; }
cat second.log
echo "Files after removing the completed directory: $(find "$depot" -type f | wc -l)"
echo 'Retiring only this depot and manifest resume records:'
find "$root" -type f \( -name "state_1007_1006_$manifest.patch" -o -name 'state_1007_1006.patch' \) -print -exec mv -- '{}' '{}.previous-test' \;
mv "$depot" "$depot.previous-second"
timeout 180 ./steamcmd.sh +login anonymous +download_depot 1007 1006 "$manifest" +quit > third.log 2>&1 || { cat third.log; exit 1; }
cat third.log
(cd "$depot" && sha256sum -c "$root/first.sha256")
echo 'PASS: retiring the matching Steam resume record restores a complete fresh download.'
