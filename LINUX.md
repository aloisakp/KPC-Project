# Linux installation

The Linux installer and launcher are native Linux applications. They do not run
the Windows launcher under Wine, and require no terminal commands.

1. Download **[KPCLauncher-linux-Setup.run](https://github.com/aloisakp/KPC-Project/releases/latest/download/KPCLauncher-linux-Setup.run)**.
2. In your file manager, open the file's **Properties → Permissions** and enable
   **Allow executing as a program** if needed. Some desktops call this **Executable**.
   Browsers generally do not preserve Linux executable permissions on downloads.
3. Double-click it and choose **Run**. Click **Choose folder** or edit the destination,
   then **Install**. Choose a local folder you can write to.
4. Open **KPC Launcher** from your applications menu. Authorize Steam in your browser.
5. Choose your game storage in Settings. This is separate from the launcher location.

Requirements: x86-64 Linux with glibc, a desktop providing X11 or XWayland,
fontconfig and the usual .NET native dependencies. The release workflow tests
Ubuntu 24.04. ARM, Alpine/musl and a Wayland-only session without XWayland are not
currently supported. The .NET runtime is bundled.

## Steam and the game

Native Steam and Flatpak Steam data locations are detected automatically. If both
exist, the running installation is preferred when unambiguous. Otherwise select
the intended data folder in Settings. It contains `steamapps` and `ubuntu12_32`
or `ubuntu12_64`; it is not `/usr/bin`. Start Steam once after installing it.
Keep Steam online and signed in to the account authorized in your browser.

The launcher and depot downloads do not need Wine. The game and its signed
Windows tooling still do. The launcher looks for Proton in your Steam libraries,
then a system Wine installation. If needed, install **Proton Experimental** through
**Steam → Library → Tools** and retry. No console setup or Python helper is needed.
Game tooling uses its own environment under the launcher settings directory. With Proton,
the launcher translates folders with Proton's `getcompatpath` and starts its tools with
`runinprefix`. Proton's `run` starts a program through Proton's Steam helper, which did not
pass the program's output on in testing with a Proton 10 build, so earlier versions could
not read the translated storage folder or the tools' progress there.

Version 0.7.1 handles Steam's `ubuntu12_32` / `ubuntu12_64` download locations
and backslashes in completion messages. Version 0.7.2 also recovers completed
downloads saved in `.previous-...` folders when Steam immediately reports an empty
download as complete. Leave these folders in place, update and retry **Install**.
The launcher checks saved files against Steam's cached manifest before reusing them;
checking a large archive can take several minutes. No terminal commands or manual
file moves are needed. Incomplete or altered files are not adopted as complete.
If the saved files have been deleted or cannot be recovered, the launcher backs up
the matching Steam progress record and requests a fresh download. Other games'
download records are left alone.

Automated tests cover native detection,
account/log validation, installer behavior, both graphical windows, and Windows
worker startup under Wine. Real Steam depot downloads, Flatpak/Proton combinations
and community gameplay still need testing on players' machines.

## Character export

**Export character** uses the KurtzPel copy in your Steam library. Steam normally runs a
Proton game in a separate Wine environment that the character listener cannot reach, so the
launcher starts that copy in its own Proton environment instead, the way Steam starts a
Proton game: through Proton's Steam helper with KurtzPel's Steam app ID, from the game's
install folder. Keep the Steam client open and signed in to the authorized account, choose
the character to keep and enter the square. The launcher saves the export, then closes the game.

Export needs Proton installed through Steam (plain Wine cannot connect the game to Steam)
and the native Steam client; Flatpak Steam is not supported for export yet. Close KurtzPel
before starting. The game keeps its settings for this environment separately from the ones
Steam uses. During the export the launcher confirms that Steam is still signed in to the
authorized account, as on Windows. Cancel stops the listener and leaves the game open.

Linux export is experimental. An automated check (`tests/linux-export-check.sh`) runs the
whole export through a real Proton build with a stand-in game and listener; a capture from the
real game on Linux still needs confirmation. Existing exports can be imported through the
normal Play flow.

If an export fails, the error names the step it stopped at. The log
(`~/.local/share/KPCLauncher/preservation.log`; **Open log file** in the launcher) lists the
Proton build used, each export step, the reason, and any messages from Proton, Wine or the
listener. Include those lines when you report a problem.

## Updates and settings

The launcher checks for releases and offers the Linux installer. It verifies the
download checksum and preselects your existing installation directory. Install
the update and open the new launcher from the applications menu; close the old
window. Previous program versions are retained in the installation's `versions`
folder; game files and settings remain separate.

Settings/logs live in `$XDG_DATA_HOME/KPCLauncher`, normally
`~/.local/share/KPCLauncher`. If `secret-tool` and an unlocked desktop keyring are
available, sign-in is remembered there. Otherwise you sign in again after closing
the launcher; account tokens are not saved as plaintext files.

The Windows installer is exclusively for Windows. The former portable ZIP and
`linux-start.py` entry point have been retired.
