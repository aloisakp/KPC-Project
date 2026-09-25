#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
version="${1:-$(cat RELEASE_VERSION)}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Invalid release version'; exit 1; }
build="$(mktemp -d "$PWD/artifacts-linux-XXXXXX")"
dotnet publish Linux/KpcLauncher.Linux.csproj -c Release -r linux-x64 --self-contained true \
  -p:Version="$version" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "$build/app"
dotnet publish Worker/KpcGameWorker.csproj -c Release -r win-x64 --self-contained true \
  -p:Version="$version" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "$build/app/game-worker"
python3 - "$build" <<'PY'
from pathlib import Path
import sys, zipfile
root=Path(sys.argv[1])
with zipfile.ZipFile(root/'payload.zip','w',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
    for file in sorted((root/'app').rglob('*')):
        if file.is_file(): archive.write(file,file.relative_to(root/'app').as_posix())
PY
dotnet publish Installer/Linux/KpcLinuxSetup.csproj -c Release -r linux-x64 --self-contained true \
  -p:Version="$version" -p:InstallerPayload="$build/payload.zip" -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "$build/setup"
mkdir -p artifacts/releases
cp -- "$build/setup/KPCLauncher-linux-Setup" artifacts/releases/KPCLauncher-linux-Setup.run
chmod 755 artifacts/releases/KPCLauncher-linux-Setup.run
printf 'Linux installer built: %s\n' "$PWD/artifacts/releases/KPCLauncher-linux-Setup.run"
