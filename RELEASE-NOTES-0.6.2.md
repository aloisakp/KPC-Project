# KPC Launcher 0.6.2 — experimental native Linux Steam helper

- Add **KPCLauncher-Portable.zip**: extract the launcher anywhere, without a fixed
  installation folder. Includes `linux-start.py` and [Linux instructions](https://github.com/aloisakp/KPC-Project/blob/v0.6.2/LINUX.md).
- On Linux, run `python3 linux-start.py` from the extracted folder. The helper
  finds native or Flatpak Steam and bridges live account checks, depot commands,
  and Linux paths to the launcher running under Wine. Keep Steam signed in and
  the helper terminal open. Use `--steam native` or `--steam flatpak` if needed.
- The Windows Steam path picker remains available. Character export now respects
  its saved folder; native Steam export is explicitly disabled pending support
  for the separate Proton/Wine process boundary.
- Document the existing installer's `--installto` option for custom destinations.

Native Linux helper regression tests and Windows launcher tests run before
packaging. End-to-end Wine, real Steam downloads, and Linux gameplay still need
community validation. This is experimental support, not a native Linux launcher.
No Steam account checks are bypassed; no game files are bundled. The Windows
installer remains unsigned. Portable builds are updated by replacing the extracted
files while the launcher and helper are closed.
