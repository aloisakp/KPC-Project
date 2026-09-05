# Security boundaries

- Steam owns credentials, download sessions and entitlement checks.
- Browser identity requires direct HTTPS verification with Valve, exact
  provider/identity/return URL checks, signed-field checks and nonce freshness.
  Each authorization has a random callback path on an ephemeral IPv4 loopback port.
  The listener enforces bounded headers, per-request deadlines, exact Host, GET,
  and unique parameters, and ignores unrelated browser requests.
- Only the public ID and verification date are persisted with per-user Windows
  DPAPI. No assertion, browser cookie, Steam login token, password or Guard code is
  saved. This is local convenience state, not a server authorization boundary.
- Account matching is checked before a command, during monitoring and before
  accepting files. It cannot atomically control a separately running Steam client
  or stop a transfer already handed to Steam. Steam remains the access-control owner.
- Completion must name the expected staging directory and manifest. Storage links
  and junctions are rejected. Previous folders are kept instead of recursively
  removed. A file lock prevents simultaneous launcher operations.
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
