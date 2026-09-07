# KPC Launcher 0.2.0

- Tester codes in Settings, verified and bound to a Steam account by the server.
- Normal Steam downloads and verification remain available without a tester code.
- The tester merge and Play require access and recheck it before starting.
- Tester status points to Settings for code entry.
- Merge and Play use the current server-authorized private update.
- Signed private instructions, builders and hooks load from memory. The public
  launcher contains the generic host and verification public key only.
- Hook updates can be delivered without a launcher release; changed merge versions
  require rebuilding the game. Both downloaded originals are preserved.
- Server-side Steam verification and a DPAPI-protected community session.
- Tester data preparation acquires the game key from the player's own verified
  Archive A without starting the game. No game key is bundled or sent by the server.
- 96 launcher security checks passed; complete merge, real Steam authorization,
  private delivery and launch were checked, with lobby arrival confirmed by the owner.

The installer is unsigned. SHA256SUMS.txt accompanies the release assets.
Private updates require the configured IPv6 server.
See README.md and SECURITY.md for the workflow and trust boundaries.
