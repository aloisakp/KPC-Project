# KPC Launcher 0.3.0

Public beta baseline for the tested Normal Match (1 vs 1) game update.

- Two authorized testers can queue for Normal 1v1, accept Ready and play against
  each other using the current Road to Grand Chase arena and rules.
- The server randomly selects one player's local host. Encrypted Cloudflare TURN
  carries the other player's game traffic; no NPC opponent is started.
- The current private runtime includes peer round-trip reporting, opponent
  appearance corrections, mission cleanup fixes and reduced Karma UI work during
  combat. Gameplay smoothness is still being improved.
- Game behavior comes from the current signed server update on Play. The public
  launcher keeps the existing Steam browser authorization, tester access checks,
  protected sessions and verified in-memory runtime delivery.
- 2v2, 4v4 and other modes still require server/private runtime development and
  testing. They are not enabled by this release. The existing delivery interface
  supports those future runtime updates without a launcher release per mode.

Existing 0.2.0 installations can use the current PvP runtime too; 0.3.0 establishes
the documented beta baseline and updated release package. Installed launchers
using this repository's update feed can update normally. Restart Play to receive
the latest private runtime. Existing current merges do not need rebuilding for
this release; a future game-file update will explicitly require Merge again.

The installer is unsigned. SHA256SUMS.txt accompanies the release assets.
Tester access and connectivity to the configured IPv6 server are required.
Private game instructions and credentials are not included in the public package.
