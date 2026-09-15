# Building KPC Launcher

Use Windows and .NET SDK 8.0.423 or later. The app targets .NET 8; global.json
also permits newer SDKs. NuGet access is needed for restore. No private server,
game assets, or Steam credentials are needed to compile or run the tests.

```powershell
dotnet restore --configfile NuGet.Config
dotnet build -c Release --no-restore
dotnet run --project tests/SecurityTests.csproj -c Release
./Audit-Source.ps1
dotnet list package --vulnerable --include-transitive
```

Tests exercise the actual loopback HTTP listener with fragmented requests and a
simulated Valve verification service, account mismatches/switches at the downloader
boundary, completion paths, and archive integrity. They do not authenticate a real
Steam user or prove a real depot download.
Progress tests also exercise timed speed samples, pauses, stale logs, overlapping
transfers, preallocated files, and cancellation partway through copying a file.

Run bin/Release/net8.0-windows/win-x64/KpcLauncher.exe for a live check. On a new
installation, the browser must open Steam automatically. Complete authorization on
Valve's page, confirm the account matches, and check cancellation/retry/disconnect.
A real transfer additionally requires a Steam entitlement and disk space.

## Packaging

```powershell
./publish.ps1 -Version 0.5.2
```

The self-contained installer, update package and feed appear in artifacts/releases.
Use -ArtifactDirectory artifacts/local-check for a separate local build. Packaging
cleans only its publish, releases and build subdirectories; it keeps other artifacts.
For a shareable local test executable without creating installer/update packages:

```powershell
./publish.ps1 -Version 0.5.2 -ArtifactDirectory artifacts/pvp-local -ExecutableOnly
```

The result is `artifacts/pvp-local/publish/KpcLauncher.exe`. This mode leaves
existing release packages alone. It retains the public tester gate and signed
in-memory package host; PvP runtime changes belong in the private tester package.

Code signing is optional through -SignParams; never commit certificates, private
keys or credentials. Without a signing identity the output is unsigned, as stated
in the README and release notes.

The tester signing **public** key in Assets/tester-signing-public.pem must be
included. The launcher contains the generic signed-package host only. Private
recipes, builder assemblies, hooks, databases, code registers and signing private
keys belong outside this repository. Do not copy private service artifacts into
public release assets. Changing the trust key or endpoint requires a launcher
release; routine private instruction updates do not.

Matchmaking, player counts, host selection, TURN connections and game hooks live
in the server and signed private runtime. The public package host loads the current
managed worker and assets without interpreting a mission or peer roster. Extending
1v1 to team modes therefore belongs in those private components, within the existing
package format and worker entry point. It does not require embedding each mode in
the public launcher. Team modes still require implementation and live validation.

Keep the public worker interface, trust configuration and supported .NET runtime
compatible when publishing private updates. If one must change, use the signed
minimum-launcher-version requirement and publish a new launcher. A changed game
file layout uses a new merge version; runtime-only changes are received on Play.

The application ID remains KPCLauncher for updater compatibility. Development
builds do not apply Velopack updates; use an installed build to test that path.

## Release

Update RELEASE_VERSION to a new three-part version and push to main. Pushing
source does not build or publish a release. When a release is ready, select
**Actions → Build release → Run workflow** on the intended branch.
The manual workflow runs tests and the source audit, builds the installer, checks
NuGet advisories, creates checksums, and publishes the tag/assets from that exact
workflow commit. Existing published versions are never overwritten.

Public repository runs attach GitHub provenance; private runs skip it because
availability depends on the account plan. Release notes state which occurred.
Private repository runs create a draft for review. Private releases are available
only to people with the required GitHub access. The launcher uses an unauthenticated
update feed, so automatic updates require a public repository and a published release.

The updater and release links now target aloisakp/KPC-Project. Previously built
installers still contain their original updater URL; rebuilding this source creates
an installer that targets the new repository. Changing source does not migrate an
already installed executable or the old repository's release assets.

Downloads use steam.exe +download_depot and follow Steam's console/content logs.
Identity comes from Steam's active process and current connection log, cross-checked
with ActiveUser when present. Changes to Steam's formats require a new live check;
do not bypass the gate.
