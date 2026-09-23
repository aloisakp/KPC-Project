# KPC Launcher 0.6.0 — relay testing

- Update through the installed launcher, Build in your selected storage folder,
  then Play. Existing preserved base archives are reused; game files are not
  bundled in the launcher installer.
- Relay registration is automatic using your server-verified Steam/tester account.
  No USB enrollment JSON, manually installed device key or special game copy.
- Each player runs a local lobby world; inventory, equipment, currency and other
  account actions remain handled by the remote logic server through the VPS.
- Better diagnostics distinguish relay-controller termination from a game crash.

This is an experimental relay rollout. Intermittent startup/controller failures
remain under investigation; two-real-player visual/movement acceptance is pending.
Tester rights and a matching signed-in Steam account remain required. Close the
old standalone development launcher/relay before using this installed version.
The existing game files can be rebuilt from the two preserved archives. Historical
stable backups are retained; a public rollback will use a newer launcher version.
