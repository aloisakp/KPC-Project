# KPC Launcher 0.8.1

This update makes **Export character** available on Linux and fixes the Linux game tools under Proton.

- **Export character on Linux.** The launcher exports from the KurtzPel copy in your Steam library. It starts that copy with Proton in the launcher's own game environment, the way Steam starts a Proton game, so the character listener can reach it. Keep Steam open and signed in to the authorized account, choose the character to keep and enter the square. The export is saved under the character's name and the game closes.
- Linux export needs Proton installed through Steam and the native Steam client. Flatpak Steam is not supported for export yet. Close KurtzPel before you start; the launcher explains what is missing if it cannot export.
- As on Windows, the launcher confirms that Steam is still signed in to the authorized account before the game starts, while it starts and before the export is saved. **Cancel** stops the listener and leaves the game open.
- If KurtzPel is installed in more than one Steam library, Linux asks which copy to export from.
- **Fixed: Linux game tools with Proton.** Proton's `run` command did not pass the tools' output on, so the launcher could not read the translated storage folder or the tools' progress. The launcher now uses Proton's `getcompatpath` and `runinprefix`.
- **Fixed:** inside the game tools, folder checks no longer treat Wine's Z: drive itself as a link.

**Linux export is experimental.** An automated check exports a stand-in character through a real Proton build with a stand-in game and listener, including an account switch and cancellation. A capture from the real game on Linux has not been confirmed yet. Windows export is unchanged.

Separate Windows and Linux installers, SHA-256 checksums, and build provenance are included. Install the update in your existing launcher location.
