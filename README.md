# KPC Launcher

A Windows utility for preserving two pinned KurtzPel depot versions in separate
folders. **Steam itself downloads the game files**, using the account signed in to
your installed Steam client. The launcher does not combine files or launch the game.

## Download

**[Download the latest installer](https://github.com/aloisakp/KPC-Project/releases/latest/download/KPCLauncher-win-Setup.exe)**

[Latest release notes](https://github.com/aloisakp/KPC-Project/releases/latest) ·
[All releases](https://github.com/aloisakp/KPC-Project/releases)

The latest links become available once the first release is published. Until then,
use the [build instructions](BUILDING.md) to build locally.
The installer is KPCLauncher-win-Setup.exe; the .nupkg and
releases.win.json assets are for the automatic updater. Private repositories and
draft releases require GitHub access and are not a public update channel.

Windows 10 or later, an installed Steam client, an internet connection, and an
account entitled to download KurtzPel are required. Allow around 60 GB for the two
archives, plus temporary space for cross-drive copies or retained old files.

## Use

1. Run the installer and open KPC Launcher.
2. On first launch, your browser opens **steamcommunity.com**. Authorize the Steam
   account you use in the desktop Steam client, then return to the launcher.
3. Keep Steam online and signed in to that same account. Press **Install**.
4. Steam downloads each pinned version; the launcher files it into its own folder.
5. Use **Verify downloads** to compare files with the SHA-256 receipt recorded when
   they were downloaded. Modified or older archives without a hash receipt are
   requested from Steam again. Previous folders are retained with a .previous-…
   suffix for manual review/removal when no download is active.

Choose storage on Steam's drive for fast moves instead of cross-drive copies.
Storage must be separate from Steam and launcher directories. Links and junctions
are rejected. Downloads remain subject to Steam's availability and entitlement
checks; the launcher cannot grant access to unavailable content.

The download bar is labeled **Estimated**. It uses the requested compressed size
and Steam's periodic speed readings, stops advancing when readings go stale, and
waits below 100% until Steam confirms completion. Detected overlapping downloads
disable the estimate because Steam reports their combined speed. File preallocation is
never counted as downloaded data. Copying and SHA-256 verification use measured
byte progress instead. The window and Cancel remain responsive during these steps.

Steam downloads compressed chunks and expands them into the game files. Archive A,
for example, transfers about 10.94 GB of compressed data and occupies 26.88 GB on
disk. Download status therefore says **compressed**, while verification says
**on disk**. This is [how SteamPipe delivers content](https://partner.steamgames.com/doc/sdk/uploading).

The log panel starts closed and opens or closes only when you press **Log**.
Starting downloads, verifying files, and errors do not change that choice. Logging
to the local file continues while the panel is closed.

## Steam authorization

Browser authorization uses [Steam OpenID 2.0](https://partner.steamgames.com/doc/features/auth).
KPC Launcher validates Steam's signed response directly with Valve over HTTPS, then
remembers only the public Steam ID and verification date, protected with Windows
DPAPI for the current user. Authorization is renewed after 30 days.
**Settings → Authorize Steam** switches the linked account; **Disconnect account**
removes that remembered identity.

The launcher has no password field, QR renderer, Steam Guard prompt, SteamKit
connection, or Steam refresh/access-token store. Valve controls its own browser
and client sign-in pages, including which sign-in options they display. The OpenID
assertion is handled transiently and never logged or saved. It cannot be used as a
Steam download session.

Before requesting a download, while monitoring it, and before filing its result,
the launcher compares the authorized ID with the connected desktop Steam account.
Missing, expired, disconnected, or mismatched identity blocks further requests.
This prevents accidental mismatches; Steam remains responsible for access control.
Software modified by its own user cannot be constrained by a local launcher check.

Cancelling stops launcher work, including an in-progress local copy or verification.
Steam may continue a transfer already requested; let it finish or close Steam
before starting another transfer or switching accounts. The launcher blocks a retry
while its current-process Steam log still shows a pending depot request. If Steam
reported an ambiguous failure, restart Steam before retrying. Browser cancellation and timeout
leave **Authorize Steam** available. If the desktop identity cannot be read,
restart Steam, sign in online, and retry. The launcher fails closed if a Steam
update changes its identity/log format.

Settings, remembered identity and logs are in %LOCALAPPDATA%/KPCLauncher:
preservation-settings.json, steam-identity.dat and preservation.log. Old versions'
Steam token files are never read or reused by this version.

## Verify the installer

Releases are built from the published commit by
[GitHub Actions](.github/workflows/release.yml) and include SHA256SUMS.txt.
Compare it with the output of:

```powershell
Get-FileHash .\KPCLauncher-win-Setup.exe -Algorithm SHA256
```

Public-repository release builds also produce GitHub build-provenance attestations:

```powershell
gh attestation verify KPCLauncher-win-Setup.exe --repo aloisakp/KPC-Project
```

An attestation binds the artifact to its workflow and source commit. It does not
prove the source has no vulnerabilities. The installer currently has no Windows
Authenticode signature, so Windows may show an unknown-publisher warning. Velopack
packaging includes timestamps, so local rebuilds need not be byte-identical.

The launcher checks for updates at startup and asks before installing them.
See [BUILDING.md](BUILDING.md), [SECURITY.md](SECURITY.md), [source review](REVIEW.md), and
[third-party notices](THIRD-PARTY-NOTICES.md).

This unofficial, non-commercial community project is not affiliated with or
endorsed by Valve, KOG, or the relevant rights holders. No game files are distributed.
