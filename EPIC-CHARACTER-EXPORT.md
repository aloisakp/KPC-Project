# Epic retail character export

Added as an experimental Windows export source in launcher 0.7.3.
Community accounts, preservation downloads, Merge and Play remain Steam based.

## Behavior

On Windows, Export character reads Steam library manifests and Epic `.item`
installation records (including the registry's custom Epic AppDataPath). It requires
the retail shipping executable to exist. Missing, incomplete, unrelated or malformed
Epic records are ignored. Duplicate Epic records do not create duplicate choices.

One detected copy starts directly. Multiple copies open a dialog with named Steam
and Epic Games buttons, installation paths, and Cancel. Closing the dialog cancels;
it never silently picks Steam. With no copy, the user is told to install retail
KurtzPel. Linux capture remains disabled; existing exports remain importable there.

The worker receives the selection and independently rediscovers it before launch.
It invokes Steam or Epic Games Launcher, with the selected game's URI. Epic's
registered handler supplies only a validated launcher executable and a recognized
URI argument form; arbitrary registry commands and manifest LaunchCommand strings
are never executed. Existing process-path/start-time checks prevent attaching to
or closing a different retail copy. Already-running game processes block export.

Community authorization still uses Steam. Steam retail capture keeps its desktop
account-match checks. Epic retail capture leaves authentication to Epic Games
Launcher and reads no Epic account IDs, passwords or tokens. The existing signed
listener and character file format are reused; this is appearance/outfit transfer,
not a transfer of retail progression or an Epic-to-Steam account link.

## Validation and remaining acceptance

- Windows launcher, Windows game worker and Linux launcher compile without warnings.
- 242 security checks pass, including 46 source-discovery/selection/launch checks.
- Source audit and `git diff --check` pass.
- Headless rendering exercises the actual Windows source dialog.
- Neither Epic Games Launcher nor an Epic KurtzPel installation was found on this
  machine. No retail game was launched, no account session copied, and no live
  server/export package was changed.

Real-game acceptance remains pending. Validate on a Windows machine with the Epic game installed:

1. Authorize the community Steam identity and close retail KurtzPel.
2. With both copies installed, check that both choices appear. Cancel must launch
   nothing. Steam must still capture normally.
3. Select Epic, sign in through Epic if required, choose the character and enter
   the lobby. Confirm the named export is produced and only that captured game closes.
4. Check Epic-only retail detection with Steam itself still configured. Confirm an
   exported character can be offered by the existing community import flow.

The passive listener observes Windows socket traffic and has no Steam executable
offset dependency, but matching Epic protocol behavior has not been demonstrated.
If Epic retail capture uses different framing/opcodes, update and validate the
private signed listener separately. Do not claim a successful Epic capture from
manifest fixtures alone.

## References

Epic manifest locations/fields and launch-URI forms were checked against the
[Vortex developer game-detection documentation](https://github.com/Nexus-Mods/Vortex/wiki/MODDINGWIKI-Developers-General-Game-detection).
No game IDs, keys or credentials are embedded in this feature.
