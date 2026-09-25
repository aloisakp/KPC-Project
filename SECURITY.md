# Security boundaries

- Steam owns credentials, download sessions and entitlement checks.
- Browser identity requires server-side HTTPS verification with Valve, exact
  provider/identity/return URL checks, signed-field checks and nonce freshness.
  Each authorization has a random callback path on an ephemeral IPv4 loopback port.
  The listener enforces bounded headers, per-request deadlines, exact Host, GET,
  and unique parameters, and ignores unrelated browser requests.
- The public ID, verification date and opaque community-server session are
  persisted with per-user Windows DPAPI, or Linux desktop keyring storage. Without
  a Linux keyring, sign-in is retained only in memory for that launcher session. No assertion, browser cookie, Steam login
  token, password or Guard code is saved. Codes are bound atomically to the identity
  derived from the server session, never a caller-supplied Steam ID. Server access
  is checked for private downloads and every new launch ticket.
- The tester endpoint uses an exact certificate pin. Private packages require an
  RSA signature rooted in the embedded public key and SHA-256 file validation.
  The signing private key stays on the server. Signed code executes with the
  launcher's normal user privileges; the private update administrator is trusted
  to supply code. Generic tool caches are checked against a signed file list.
- Private instructions are loaded from RAM through same-user named pipes on Windows
  or an ephemeral IPv4 loopback connection with a random 256-bit capability for
  the Linux game worker, and
  temporary workers. Scripts are passed through stdin. The launcher does not save
  instruction archives, recipe scripts or hook scripts to the tester filesystem.
  This is redistribution friction, not DRM: memory, local IPC, dumps, paging,
  decrypted responses and resulting game files can be captured by the local user.
- The signed private update requests local data preparation only after tester
  authorization and a completed Archive A receipt. The worker checks the pinned
  executable hash and authenticates the acquired key against an encrypted PAK
  index. No game key is embedded in either distributed package or uploaded to the
  service. Local workers/builders use it transiently; operating-system memory,
  helper-process arguments, paging and diagnostic capture remain outside this guarantee.
- Revocation prevents subsequent protected API calls. It does not terminate a
  running game or invalidate an already issued gateway ticket before its existing
  five-minute expiry. A changed merge version requires rebuilding the game; a
  runtime version change can update hooks without a merge.
- Account matching is checked before a command, during monitoring and before
  accepting files. It cannot atomically control a separately running Steam client
  or stop a transfer already handed to Steam. Steam remains the access-control owner.
- Completion must name the expected staging directory and manifest. Storage links
  and junctions are rejected. Previous folders are kept instead of recursively
  removed. A file lock prevents simultaneous launcher operations. Steam's current
  console log is checked for an earlier pending request before staging changes or
  retries. Steam itself is an independent process, so this is not an atomic lock
  against manual commands or other software controlling Steam.
- Download percentage is a display-only estimate from Steam's rate logs. It cannot
  authorize an account, accept a directory, or mark an archive complete. Local
  verification/copy progress is measured. Stale or ambiguous speed data is not
  treated as evidence that a download finished.
- SHA-256 receipts detect content changes after download, including changes that
  preserve file counts/sizes. They supplement Steam's validation and are not a trust
  anchor against a malicious program running as the same Windows user.
- Update trust depends on GitHub, repository maintainers, TLS, Velopack and build
  dependencies. Provenance identifies origin, not absence of bugs.

The release tree removes SteamSession, Secrets, UiAuthenticator, CmServers, the
direct CDN downloader, QR dependency and associated UI. This repository starts
from the reviewed current source without importing the earlier repository's history.
Old session-*.dat files are neither read nor packaged;
users who no longer use an older launcher can remove those files after closing it.
Other local launcher variants may use them.

Run the tests and dependency audit in BUILDING.md before release. A clean scan
does not guarantee that all security risks have been eliminated.
