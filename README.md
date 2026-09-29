# KPC Launcher

A launcher for Windows and Linux for preserving two pinned KurtzPel depot versions and accessing
community testing. **Steam itself downloads the game files**, using the account
signed in to your installed Steam client. Testers can then merge and play using
private, signed instructions supplied by the community server.

## Download

| Windows (x64) | Linux (x64) |
| --- | --- |
| **[Download Windows installer](https://github.com/aloisakp/KPC-Project/releases/latest/download/KPCLauncher-win-Setup.exe)** | **[Download Linux installer](https://github.com/aloisakp/KPC-Project/releases/latest/download/KPCLauncher-linux-Setup.run)** |

Both installers have a graphical **Choose folder** option. Select a writable
location for the launcher; select your game storage separately in Settings.
The Linux launcher runs natively. The Windows game still uses Proton or Wine.
There is no portable ZIP. See [Linux installation](LINUX.md) for the graphical
steps and current compatibility limits.

[Latest release notes](https://github.com/aloisakp/KPC-Project/releases/latest) ·
[All releases](https://github.com/aloisakp/KPC-Project/releases)

To review the code and build the launcher yourself, follow the
[build instructions](BUILDING.md).

Windows 10 or later, or a supported Linux desktop (see [LINUX.md](LINUX.md)),
an installed Steam client, an internet connection, and an
account entitled to download KurtzPel are required. Allow around 60 GB for the two
archives and additional space for retained previous downloads. The merged game links
to the downloaded files and needs little extra space; a storage drive that cannot
link files (for example FAT or exFAT) needs room for a full copy. Community
connections use the public VPS relay.

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
6. For community testing, enter your tester code in **Settings → Redeem code**.
   The server binds it to your verified Steam account. Normal Steam downloads and
   verification do not require a code; the tester merge and Play do.
7. Press **Merge**. The launcher receives the current signed private instructions,
   prepares `KurtzPel-Tester` in your storage folder from the two downloads and
   verifies the result (see [how the tester game is prepared](#how-the-tester-game-is-prepared)).
8. Press **Play**. The server checks access and the required update again before
   issuing a single-use game ticket. Revoked accounts cannot obtain new tickets.

**Check access** refreshes tester status. Merge and Play recheck tester access and
updates. A changed merge version requires another merge; hook-only updates are
received on Play. Private updates do not require another GitHub launcher release.

## PvP beta

The current private game update supports **Normal Match (1 vs 1)** for authorized
testers. Both players queue for the same mode and accept the Ready screen. The
server selects one player's local host at random; the other player connects through
an encrypted Cloudflare TURN relay. Available missions, maps and rules are
configured by the server. Two-player combat and returning from the match have
been tested; additional configurations and gameplay smoothness remain under testing.

Version 0.5.0 adds character export/import, export-folder access and account
deletion. Community sign-in, private downloads and gameplay use the VPS relay.
The endpoint change requires authorizing Steam again after upgrading. Game
updates continue to come from the signed private runtime when you press Play.

## Character transfer and account deletion

**Export character** is currently a Windows feature (Linux can import existing exports): no tester code,
archive downloads or merge are needed. Authorize Steam first. The Windows launcher detects
retail KurtzPel installations from Steam and Epic Games. If both are present, choose
**Steam**, **Epic Games**, or **Cancel**; with one detected copy, it opens that copy directly.
Epic Games handles its own retail login. Epic export does not link an Epic account to
your community account or provide Epic downloads for the preservation archives.
Select the character to keep and enter the square. When capture finishes, the game closes and the launcher
confirms the character's name. **Open exports folder** shows the character-named
file. Delete an unwanted export there, then capture another character.
Epic export (added in 0.7.3) is experimental: detection, source selection and process
routing have automated checks; a real Epic character capture still needs validation.
See [Epic export validation](EPIC-CHARACTER-EXPORT.md).

When your community account has no character, Play offers to use your latest
export. Accepting opens an unnamed character in rebirth for editing and naming;
completion continues into the starting story. Declining starts normal creation.
Appearance, colours, sliders and the supported equipped outfit are transferred;
weapon skins, aura and floating accessories are excluded. Imported clothing has
its default stats, an **[Imported]** name, no trading and a 1 GP resale value.

**Settings → Delete account** permanently removes your community game account,
characters, inventory, currencies, levels and progress after confirmation. Close
the game first. Your Steam account, tester access and local export files remain,
so you can reuse an export when starting again.

**A launcher release does not enable or validate every team mode.** The server's
mission catalog supplies supported modes, maps, team sizes and goals; additional
combinations still require gameplay testing. Server and private runtime components
can be updated through the existing signed delivery mechanism without a public
launcher release for each mode. Launcher UI, trust configuration or worker
interface changes can still require a new executable. Tester access remains required.

## Storage and downloads

### Steam detection and installation folders

The Windows launcher detects Windows Steam and accepts a folder containing
`steam.exe`. The Linux launcher detects native and Flatpak Steam directly and
accepts Steam's **data folder** containing `steamapps` and `ubuntu12_32` or
`ubuntu12_64`. `/usr/bin` contains commands, not Steam's game data.

If Steam is missing or there are multiple installations, use **Settings → Steam →
Choose/Select Steam folder**. The choice is remembered. Automatic detection clears
that override. Start Steam once after installing it so its data folders exist.

The launcher and game files have separate destinations. The installer chooses
where the program lives; **Settings → Choose storage folder** chooses where the
archives and merged game live. Use a writable folder separate from Steam and the
launcher. Symlinked download/staging folders are rejected. Steam controls content
availability and account entitlement.

The download bar is labeled **Estimated**. It uses the requested compressed size
and Steam's periodic speed readings, stops advancing when readings go stale, and
waits below 100% until Steam confirms completion. Detected overlapping downloads
disable the estimate because Steam reports their combined speed. File preallocation is
never counted as downloaded data. Copying and SHA-256 verification use measured
byte progress instead. The window and Cancel remain responsive during these steps.

## Steam authorization

Browser authorization uses [Steam OpenID 2.0](https://partner.steamgames.com/doc/features/auth).
The community server validates Steam's signed response directly with Valve over
HTTPS. The launcher remembers the public Steam ID, verification date and an opaque
community-server session. Windows protects them with per-user DPAPI. Linux uses
the desktop keyring through `secret-tool` when available; otherwise sign-in lasts
for the current launcher session and no session token is written to disk.
Authorization is renewed after 30 days. The community session is not a Steam login
or download credential.
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

Settings and logs are in `%LOCALAPPDATA%/KPCLauncher` on Windows and
`$XDG_DATA_HOME/KPCLauncher` (normally `~/.local/share/KPCLauncher`) on Linux:
preservation-settings.json, steam-identity.dat, tester-session.dat and
preservation.log. Generic runtime tools are cached in tester-tools. Private runtime
packages and hooks are received into memory, used by temporary worker
processes and released when those workers exit. Linux runs the signed Windows
game tooling in a dedicated Proton/Wine environment through an authenticated,
short-lived loopback connection; the interface and Steam integration remain native. Game output, verification receipts
and runtime state remain on disk. This creates redistribution friction; it cannot
prevent a tester or software running as that user from capturing memory, traffic
after decryption, or game output. Windows may also page memory or create dumps.
Old versions' Steam token files are never read or reused by this version.

## How the tester game is prepared

Since 0.8.0 no game key is read, stored or sent, no game package is built, and no
byte of the game or its checks is changed. **Merge** stacks the two pinned Steam
downloads in `KurtzPel-Tester`:

- the 2026 version exactly as downloaded, including its unmodified game executable;
- every signed package of the July 2021 version (each `.pak` with its `.sig`),
  unchanged and under its original name, in `TheChase/Content/Paks/2021`;
- the 2021 loose files, such as movies, that the 2026 version does not have;
- `steam_appid.txt` and the community intro video.

Where the storage drive supports it, these are hard links to the downloaded files.
The game verifies every package signature itself and uses the 2026 file wherever both
versions have one. A receipt records the prepared files. Before each Play the launcher
checks the unmodified executable, the complete 2021 package set and that no other
package was added. Game folders prepared by 0.7 or older, which held generated packages
and a changed executable, must be merged again. Private updates that still ask for the
retired key preparation are refused.

## Verify the installer

Releases are built from the published commit by
[GitHub Actions](.github/workflows/release.yml) and include SHA256SUMS.txt.
Compare it with the output of:

```powershell
Get-FileHash .\KPCLauncher-win-Setup.exe -Algorithm SHA256
```

You can also verify the release's build provenance:

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

## Contact and rights-holder requests

Contact: **[Aloisa.froyard@gmail.com](mailto:Aloisa.froyard@gmail.com)**.

KOG, its representatives, and any third-party rights holders whose resources are
used in the game, by KOG, or in this project may send copyright concerns or
takedown requests to this address. Please identify the affected content, the
rights involved, and how we can contact you.

We aim to respond in good faith and reach reasonable terms with the relevant
rights holders. Where possible and acceptable to them, we hope to resolve
concerns by removing or replacing only the affected content. This is a request
for dialogue, not a condition on submitting a takedown request or a guarantee
that the project can continue unchanged.
