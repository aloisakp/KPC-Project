# KPC Launcher 0.5.3

Fixes character export reporting **“Character export failed”** on some Windows 10 PCs even though the character file had been saved. The launcher now starts KurtzPel through Steam without the Windows shell, which crashed the export helper as it closed, and an export whose file is already saved is no longer reported as failed.

Exports made with 0.5.2 that showed this error are complete and can be imported; find them with **Open exports folder**.

Export remains available without tester access. Existing character files remain compatible and are preserved.
