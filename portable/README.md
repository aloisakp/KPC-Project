# KurtzPel 0.6.0 two-player development test

This is an unsigned standalone test build, not a public stable update.
It does not replace your installed launcher. Two devices are admitted explicitly;
the server still verifies each player's separate Steam account and tester grant.

1. Copy/extract this entire folder from USB to a regular folder on the second PC.
2. Run `Enroll-Device.cmd`. Send only `RelayState/device-request.json` to the operator.
   The operator enrolls this public fingerprint. Do not send/copy `device-identity`,
   private keys, Steam sessions or the other PC's RelayState. Identity lasts7 days.
3. Copy your complete already-merged `KurtzPel-Tester` folder, including hidden
   `.tester-build.json` and `kp-client-receipt.json`, into
   `RelayState/split-storage/KurtzPel-Tester`. Archives need not be downloaded again.
   Keep the original installation. Incomplete or changed copies fail verification.
4. After the operator confirms enrollment, run `Start-Relay.cmd`; keep its window
   open. Authorize the second Steam account in the launcher, with that same account
   signed into desktop Steam. Keep the default test-storage location. Press Play.

Use different Steam accounts on the two PCs. No Merge is required for a valid
current prepared installation. Do not move this folder after enrollment: its
identity marker binds the state location. For a new location or expired identity,
extract a fresh package and enroll again; existing state is never silently erased.

The data route is local helper → VPS TCP11109 → isolated logic endpoint, with
certificate pins and separate device keys. There is no automatic LAN/SSH fallback.
Port conflicts preserve the existing process. Close the game and launcher normally
to stop this package's helper. The frozen stable backups are never part of this ZIP.
