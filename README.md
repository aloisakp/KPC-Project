# KPC Launcher

A Windows launcher for preserving two pinned KurtzPel depot versions and accessing
community testing. **Steam itself downloads the game files**, using the account
signed in to your installed Steam client. Testers can then merge and play using
private, signed instructions supplied by the community server.

## Download

**[Download the latest installer](https://github.com/aloisakp/KPC-Project/releases/latest/download/KPCLauncher-win-Setup.exe)**

[Latest release notes](https://github.com/aloisakp/KPC-Project/releases/latest) ·
[All releases](https://github.com/aloisakp/KPC-Project/releases)

To review the code and build the launcher yourself, follow the
[build instructions](BUILDING.md).

Windows 10 or later, an installed Steam client, an internet connection, and an
account entitled to download KurtzPel are required. Allow around 60 GB for the two
archives and additional space for the merged game, temporary work and retained
previous installations. Community connections use the public VPS relay.

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
   builds `KurtzPel-Tester` in your storage folder and verifies the result.
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

**Export character** is available to everyone in version 0.5.1: no tester code,
archive downloads or merge are needed. Authorize Steam first. It opens the normal Steam game. Select the character to keep
and enter the square. When capture finishes, the game closes and the launcher
confirms the character's name. **Open exports folder** shows the character-named
file. Delete an unwanted export there, then capture another character.

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

### Steam folder selection and Wine

Version **0.6.1** adds Wine support through manual Steam folder selection. If
Steam is not found, click **Select Steam folder** on the home screen, or open
**Settings → Steam → Select Steam folder**. Choose the Steam installation folder
containing **steam.exe**, not `steamapps` or the KurtzPel game folder.

The launcher remembers this location and uses it immediately, including after
restart. Use **Use automatic detection** in Settings to clear the saved choice.
If the selected installation moves or disappears, select its new folder.

Under Wine, choose the Windows Steam installation accessible in the launcher's
Wine environment. This fixes missing registry-based detection; it does not add
integration with the native Linux Steam client. Linux/Wine compatibility is
community-tested and depends on your setup; this release has not been validated
end to end under Wine. Steam sign-in and matching-account checks still apply.

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

## Steam authorization

Browser authorization uses [Steam OpenID 2.0](https://partner.steamgames.com/doc/features/auth).
The community server validates Steam's signed response directly with Valve over
HTTPS. The launcher remembers the public Steam ID, verification date and an opaque
community-server session, protected with Windows DPAPI for the current user.
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

Settings, remembered identity and logs are in %LOCALAPPDATA%/KPCLauncher:
preservation-settings.json, steam-identity.dat, tester-session.dat and
preservation.log. Generic runtime tools are cached in tester-tools. Private recipe
packages, scripts and hooks are received into memory, used by temporary worker
processes and released when those workers exit. Game output, verification receipts
and runtime state remain on disk. This creates redistribution friction; it cannot
prevent a tester or software running as that user from capturing memory, traffic
after decryption, or game output. Windows may also page memory or create dumps.
Old versions' Steam token files are never read or reused by this version.

After Archive A is downloaded and verified, an authorized tester receives a signed
request to prepare local game data. The private worker acquires the game key from
that player's own pinned executable and validates it against a local encrypted
PAK index. It does not start the game, save a key file, or upload the key. Neither
the launcher nor the server's private update includes the game key. Normal users
skip this preparation. Merging acquires the key again locally in a temporary worker.

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
