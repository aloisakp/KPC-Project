# Relay development launcher 0.6.0

## Explicit split-server test (dev.2)

`Start-RelayDevelopment.ps1 -SplitServer` starts the separate dev.2 executable.
It uses only https://127.0.0.1:11107 through the owned SSH endpoint session,
with separate `split-launcher` settings/auth and `split-storage` game files.
No certificate pin, signing key, Steam check, or tester grant is relaxed.
154 security checks and standalone publish passed. The first live local-world /
remote-menu run is documented in sibling Game/relay-development/server/tools/
relay-split/ACCEPTANCE-2026-09-22.md, including remaining limits.

```powershell
./publish.ps1 -Version 0.6.0-dev.2 -ArtifactDirectory artifacts/relay-split-next -ExecutableOnly
./Start-RelayDevelopment.ps1 -SplitServer
```

Baseline SHA: 11d4ec0. Owner: RelayDevelopment's explicit mode selector; existing
TesterClient consumes the fixed endpoint and LauncherConfig consumes isolated
paths. Changes are those three files, startup script, version and focused tests.
Rollback: revert the dev.2 commit; dev.1 executable and state are retained.

The first live dev.2 executable in artifacts/relay-split had the old hard-coded
dev.1 title. The next build in artifacts/relay-split-next takes its window title
from assembly metadata; it does not replace the still-running accepted binary.

## Retained dev.1 history

Based on current public launcher 0.5.3 source
`8495cbfe02ec1cb16d1eddc07e33e14ccf9df917`, not a recovered backup or an older launcher.
Private runtime source is in sibling `KPC-Relay-Tester-Service`; game-server source
is in `Game/relay-development`. None of the original working trees are modified.

This first change creates the isolated launcher shell needed for the avatar test.
It does **not** claim that a relay or a replicated additional avatar works yet.
The local host/remote-appearance test must pass before economy forwarding is enabled.

```powershell
dotnet run --project tests/SecurityTests.csproj -c Release
./publish.ps1 -Version 0.6.0-dev.1 -ArtifactDirectory artifacts/relay-dev -ExecutableOnly
./Start-RelayDevelopment.ps1
```

The build is standalone; do not produce/install a Velopack package or publish a
public update. Both updater entry points are disabled, and the UI is labelled
Relay Dev. Opening it does not automatically start a browser login. Explicit
Steam/tester authorization and signed-package verification are unchanged.

`KP_RELAY_SANDBOX_ROOT` must identify an existing directory with a matching
`.relay-sandbox-owner.json` marker. The startup script uses the registered
`Game/runtime-state/relay-lobby-sandbox` directory, sets only its child process's
environment, and records the launched PID/start time/executable/argument marker.
Launcher settings, session/cache files and default downloads use that root;
The shell has no fallback to `%LOCALAPPDATA%/KPCLauncher`. Its original signed
production worker still used the installed worker state directory; dev.2's
signed split worker corrects that separate boundary. Use a separate
prepared-client copy for game tests.

Baseline: all 141 security checks passed before edits. New checks cover unowned
roots, isolated settings/default storage and the disabled update path. This single
isolation boundary crosses startup, paths, updater and identity/UI files: all are
needed to avoid running the experimental launcher as the installed production one.

Verification: all **150** checks passed after the change; standalone publish and
the source-publication audit passed. This does not yet certify an in-game relay.

Live shell observation (2026-09-22): Relay Dev title/version, isolated settings and
disabled updates were visible. The owner manually authorized Steam and selected
the existing `G:\KPC Preservation` storage. Their normal Play reached character
selection on the existing live gateway, **not a relay**. That rendered client,
its workers/host and the development launcher were then closed for local-only
testing. Identities are retained in the sandbox process registry. No release was
installed, no public updater was changed, and no original game code was edited.

Important: this version's normal Play still uses the signed production runtime.
Use the explicit `Game/relay-development/server/tools/relay-avatar/probe.mjs`
experiment for native research; it is not yet a launcher-integrated relay route.
The probe uses a verified disposable copy of the actual preservation installation.

Rollback: close only the recorded development process and follow workspace
`REVERT-RELAY.md`. This worktree and its build directories are experiment-owned.
Restore no player database and do not replace the original public launcher.

## Independent portable device enrollment (baseline61df439)

Root limitation: this PC's protected transport key must never be distributed.
RelayDeviceIdentity owns adjacent RelayState initialization and generates a new
seven-day client certificate/key per device, under current-user/SYSTEM-only ACLs.
The public enrollment request contains only schema, root, fingerprint and expiry.
Existing unowned roots, incomplete identity, changed key pairs and expiry fail
closed; startup never overwrites them or copies another PC's authentication.
Program's explicit --relay-device-init entry performs this without browser login.
Files: that owner, Program, focused checks and test registration. The160 security
checks passed before and after key-pair validation. Standalone publish is required
before delivery; no public release or installed updater is changed here.
Rollback: revert the enrollment commit; remove only its registered disposable
test state. This change does not activate two-player gateway/native support.

## Portable development transport package

Baseline4f8b3f7. Portable scripts verify their explicit file manifest, initialize
that device's fresh key, reject occupied loopback ports without stopping their
owners, start one fixed-route TLS helper, require pinned HTTPS health, and launch
the split worker with isolated adjacent state. The helper's exact OS handle and
process receipt are retained; closing this launcher closes only that helper.
The key is not distributed. First enrollment requires the operator to approve
the public request fingerprint; account authentication is still separate.
Files: five portable startup/health/instruction files and this record. PowerShell
AST and Node syntax pass; actual portable binary initialization and occupied-port
rejection pass against a separate disposable validation copy. Existing operator
helper36028 remained unchanged. Published0.6.0-dev.3 is a single self-contained
executable, source audit passed,160 security checks passed. Generic transport core
has its independent two-key TLS tests. Second-PC rendered verification is pending.
No public update feed or stable release was published. Rollback: revert the portable
scripts commit and remove only ledger-owned package/test roots; keys in test state
must never enter the clean distributable ZIP.

Release requirement confirmed by the owner: device enrollment must be automatic
in the final launcher after account authorization. The manual public-request JSON
exchange is only an isolated two-device development gate, not an acceptable final
installation step. Automatic registration/revocation is still unimplemented; do
not call this portable build a finished public launcher. Never replace automatic
enrollment with a shared bundled private key or unauthenticated device admission.
