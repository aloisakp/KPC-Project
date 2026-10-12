#!/usr/bin/env bash
set -euo pipefail
# Linux character export through a real Proton session: the launcher's runtime, worker host and game
# worker start a stand-in game through Proton's Steam helper, and a stand-in listener attaches to it.
# No Steam account, retail game, community server or private package is involved.
#
# Usage: tests/linux-export-check.sh [Proton directory] [steamclient.so]
# Without arguments it downloads a pinned GE-Proton build and SteamCMD's native Steam client library.
# Needs the .NET 8 SDK, x86_64-w64-mingw32-gcc, openssl, curl and xvfb-run.
cd -- "$(dirname -- "$0")/.."
work="$(mktemp -d "${TMPDIR:-/tmp}/kpc-export-check-XXXXXX")"
trap 'echo "Files kept for inspection: $work"' ERR
proton="${1:-}"
client="${2:-}"
home="$work/home"
if [ -z "$proton" ]; then
  tag=GE-Proton10-34
  sha512=9fd0b2cfbd501c0b5c892239c392c7283a029b5e5d5a77d3f85b0ce190d555456241a18eebca16b53f094b403499201c13550a3f0b9b365e1a5eb5737cbb7303
  curl -fsSL --retry 2 "https://github.com/GloriousEggroll/proton-ge-custom/releases/download/$tag/$tag.tar.gz" -o "$work/proton.tar.gz"
  echo "$sha512  $work/proton.tar.gz" | sha512sum -c -
  tar -xzf "$work/proton.tar.gz" -C "$work"
  rm "$work/proton.tar.gz"
  proton="$work/$tag"
fi
test -x "$proton/proton"
if [ -z "$client" ]; then
  mkdir "$work/steamcmd"
  curl -fsSL --retry 2 https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz | tar -xz -C "$work/steamcmd"
  HOME="$home" timeout 300 "$work/steamcmd/steamcmd.sh" +quit > "$work/steamcmd.log" 2>&1 || { cat "$work/steamcmd.log"; exit 1; }
  client="$work/steamcmd/linux64/steamclient.so"
fi
test -f "$client"
mkdir -p "$home/.steam/sdk64"
ln -sf "$(realpath "$client")" "$home/.steam/sdk64/steamclient.so"

fixtures="$work/fixtures"
mkdir "$fixtures"
x86_64-w64-mingw32-gcc -municode -mwindows -O2 -o "$fixtures/TheChase-Win64-Shipping.exe" tests/LinuxExport/game.c
x86_64-w64-mingw32-gcc -municode -mwindows -O2 -o "$fixtures/TheChase.exe" tests/LinuxExport/bootstrap.c
x86_64-w64-mingw32-gcc -municode -O2 -o "$fixtures/node.exe" tests/LinuxExport/listener.c

# The worker under test trusts only this run's key.
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$work/test-signing.pem" 2>/dev/null
openssl pkey -in "$work/test-signing.pem" -pubout -out "$work/test-signing-public.pem"
dotnet publish tests/LinuxExport/TestWorker.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None \
  -p:TestSigningKey="$work/test-signing-public.pem" -o "$work/worker" > "$work/worker-build.log" || { cat "$work/worker-build.log"; exit 1; }
dotnet build tests/Native/NativeTests.csproj -c Release > "$work/tests-build.log" || { cat "$work/tests-build.log"; exit 1; }

# A Steam data folder with spaces and non-ASCII characters, Proton in its library and the stand-in game.
steam="$work/Steam library é"
game="$steam/steamapps/common/KurtzPel"
mkdir -p "$steam/ubuntu12_32" "$steam/logs" "$game/TheChase/Binaries/Win64"
cp /bin/sleep "$steam/ubuntu12_32/steam"
ln -s "$(realpath "$proton")" "$steam/steamapps/common/Proton - Export check"
cp "$fixtures/TheChase.exe" "$game/"
cp "$fixtures/TheChase-Win64-Shipping.exe" "$game/TheChase/Binaries/Win64/"
printf '"AppState"\n{\n\t"appid"\t\t"844870"\n\t"installdir"\t\t"KurtzPel"\n}\n' > "$steam/steamapps/appmanifest_844870.acf"

HOME="$home" timeout 1500 xvfb-run -a dotnet tests/Native/bin/Release/net8.0/NativeTests.dll --proton-export \
  "$work" "$steam" "$work/worker/KpcGameWorker.exe" "$fixtures/node.exe" "$work/test-signing.pem"
rm -rf -- "$work"
