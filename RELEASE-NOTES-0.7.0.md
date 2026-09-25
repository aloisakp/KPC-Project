# KPC Launcher 0.7.0

- Separate **Windows** and **native Linux** graphical installers, each with a folder chooser.
- Windows retains its installed update mechanism and Windows Steam support.
- Linux has a native interface, native/Flatpak Steam detection, manual folder selection,
  applications-menu shortcut, and a dedicated Proton/Wine worker for the Windows game tools.
- Linux updates use the Linux installer and preserve the chosen installation location.
- The portable ZIP and Python launcher wrapper are removed. The README offers the two installers.
- Contact and rights-holder request information remains at **Aloisa.froyard@gmail.com**.

The launcher is native on Linux; the Windows game still requires Proton or Wine.
Retail character capture remains Windows-only. Linux can import existing exports.
This first Linux release has automated installation, UI, native Steam fixture and
Wine worker startup coverage; real depot downloads and gameplay are not yet
validated across Linux distributions and Steam/Proton configurations.

Both installers include checksums and GitHub build provenance. Windows installers
are currently unsigned with Authenticode. See [Linux installation](https://github.com/aloisakp/KPC-Project/blob/main/LINUX.md).
