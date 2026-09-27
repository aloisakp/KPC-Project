# KPC Launcher 0.7.3

This update adds an experimental Epic Games source for **Export character** on Windows, while retaining the native Linux support introduced in 0.7.0 and download recovery improvements from 0.7.2.

- Detect installed retail copies of KurtzPel from Steam and Epic Games.
- When both are installed, **Export character** offers **Steam**, **Epic Games**, or **Cancel**, with each installation's location shown.
- With one detected copy, export starts that copy directly. With neither, the launcher explains how to install the retail game.
- Epic Games Launcher handles its own sign-in. The community launcher does not store an Epic account ID, password or login token.
- Exports use the existing character file format and import flow. Steam remains the provider for preservation downloads and community account authorization.
- Recheck the selected installation before launch and retain process identity checks so capture completion closes only the selected game process.

**Epic export is experimental:** automated tests cover detection, selection, cancellation, launch arguments and account/process checks. A successful character capture from a real Epic Games installation has not yet been verified. Windows users can try it by authorizing Steam in KPC Launcher, closing KurtzPel, choosing **Export character**, and selecting Epic Games when prompted. Select the desired character and enter the lobby.

Character capture remains Windows-only. Linux users retain native launcher support and can import existing character exports. Retail progression and Epic account linking are not transferred.

Separate Windows and Linux installers, SHA-256 checksums, and build provenance are included. Install the update in your existing launcher location.
