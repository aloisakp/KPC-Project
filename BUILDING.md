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

Windows:

```powershell
./publish.ps1 -Version 0.7.0
```

This builds the Windows launcher, Velopack update package/feed, and a graphical
folder-selecting setup program. The setup embeds the Windows installer and passes
the chosen destination as an argument. It does not include Linux support or a
portable ZIP. The application ID remains KPCLauncher for existing updater users.
`-ExecutableOnly` produces a local development executable, not a public download.

Linux (on Linux, with .NET 8 SDK, Python 3 for packaging, and desktop libraries):

```sh
dotnet run --project tests/Native/NativeTests.csproj -c Release
bash publish-linux.sh 0.7.0
```

The native Avalonia UI links the shared preservation, authorization and signed
package code, and supplies Linux Steam/process/keyring integration. `Worker/`
builds a separate self-contained Windows console worker used only for game tools
under Proton/Wine. `Installer/Linux/` embeds the native app and worker and installs
them into a user-selected directory with an applications-menu shortcut. No Python
or .NET installation is required by end users. Linux updates download the matching
Linux installer, verify GitHub's SHA-256 asset digest, and preselect the install root.

Both packaging scripts write the public assets to `artifacts/releases`. Linux
keeps isolated intermediate builds under `artifacts-linux-*`; Windows limits
cleanup to its validated build-output directories. Signing parameters apply to
Velopack's payload; the new setup wrapper is unsigned unless separately signed.
Never commit certificates, private keys, or credentials.

The release workflow runs both operating systems' tests, audits dependencies,
tests an actual Linux install into a path containing spaces/non-ASCII characters,
opens both Linux windows under Xvfb, and smoke-tests the game worker under Wine.
UI screenshots are retained as workflow artifacts. Neither synthetic tests nor
worker startup establish real Steam download or gameplay compatibility.

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
**Actions â†’ Build release â†’ Run workflow** on the intended branch.
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

Windows downloads use steam.exe +download_depot and follow Steam's console/content logs.
Identity comes from Steam's active process and current connection log, cross-checked
with ActiveUser when present. Changes to Steam's formats require a new live check;
do not bypass the gate.
