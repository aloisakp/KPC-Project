# KPC Launcher 0.4.0

Public beta release of the launcher used with the current signed community runtime.

- Keeps server-verified Steam authorization and tester checks for private
  downloads and new game-launch tickets.
- Keeps the pinned tester certificate, RSA-signed private releases, SHA-256
  package/tool checks, per-user protected sessions and in-memory runtime delivery.
- Continues receiving mission, menu-performance and game-reliability updates
  through the community server when you press Play. Available missions and maps
  are configured by the server, rather than selected by the launcher version.
- Includes the normal installer and update feed for existing installations.

The public executable's functional code is unchanged from 0.3.0; this release
updates its version and beta documentation. Current game behavior is supplied
separately by the signed private runtime. Existing 0.3.0 installations remain
compatible with that runtime. A public launcher update alone does not require
downloading the game again or remerging a current installation.

Testing continues: menu stalls are not fully eliminated, and Road mission loading
and additional mode/map/player combinations still need gameplay validation.
This release does not certify every configurable mission combination as playable.

The installer remains unsigned by Windows Authenticode. SHA256SUMS.txt accompanies
the release assets; build provenance is added by the public release workflow.
Tester access and connectivity to the configured IPv6 server are required.
No game files, private runtime instructions, credentials or signing private keys
are included in the public package. See SECURITY.md for the trust boundaries.
