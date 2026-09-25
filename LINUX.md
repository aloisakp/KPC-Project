# Native Linux Steam with the launcher under Wine (experimental)

Version 0.6.2 includes a native Python helper for **Linux Steam**, including the
Flatpak edition. The launcher itself is still a Windows WPF application run by
Wine. A folder picker alone cannot make Windows process APIs see native Steam.

## Quick start

1. Download **KPCLauncher-Portable.zip** from the release and extract it into any
   folder you choose. Keep `KpcLauncher.exe`, `linux-start.py`, and this document
   together. No launcher installation or fixed destination is required.
2. Use a working Wine installation with matching `wine` and `winepath` commands,
   and Python **3.9 or newer**. Start native Steam once and sign in online.
3. Open a terminal in the extracted folder and run:

   ```sh
   python3 linux-start.py
   ```

4. Keep that terminal open while using the launcher. Authorize the same Steam
   account in the browser, choose separate download storage, then press Install.

If both Steam editions are installed, explicitly choose one:

```sh
python3 linux-start.py --steam native
python3 linux-start.py --steam flatpak
```

Native Steam's launch command is found on PATH (like `command -v steam`). Flatpak
is checked using `flatpak info com.valvesoftware.Steam` and invoked with
`flatpak run com.valvesoftware.Steam`. The executable and the **data folder** are
different: `/usr/bin/steam` is not where Steam's logs or downloaded depots live.

The helper searches `~/.local/share/Steam`, `~/.steam/steam`, `~/.steam/root`, and
Flatpak's Steam folders under `~/.var/app/com.valvesoftware.Steam`. Aliases are
resolved to their real locations. For a custom data folder:

```sh
python3 linux-start.py --steam native --steam-root '/path/to/Steam'
```

You can also select the data folder under **Settings → Steam** while the helper
is running. Choose the folder containing `steamapps` and `ubuntu12_32`, not
`/usr/bin`, a game folder, or `steamapps` itself. The selection is remembered;
an explicit `--steam-root` takes precedence on the next start. Do not use symlinks
inside download staging or storage. Root aliases such as `~/.steam/steam` are
resolved before the launcher receives the path.

For an existing Wine prefix or a custom Wine build:

```sh
WINEPREFIX='/path/to/prefix' python3 linux-start.py \
  --wine '/path/to/bin/wine' --winepath '/path/to/bin/winepath'
```

Run the Python helper on the host Linux system. Running it inside Wine or a
Flatpak/Bottles sandbox is not supported. Bottles, Lutris, Proton, and Steam Deck
integration have not been validated; do not assume their runtime isolation or
Wine binaries can be substituted without configuration.

## What this version covers

- Native/Flatpak Steam discovery, current-process account verification, and
  fixed depot download commands without `steam.exe`.
- Validation of Steam's Linux completion paths against the selected data folder.
- Download progress, archive preservation, and the existing signed community
  build/play flow in Wine. Community gameplay still depends on the private
  runtime's own Wine compatibility and has **not** been validated by these tests.

**Character export from native Steam is disabled in this version.** The retail
game can run in a separate Proton/Wine environment; safely finding and attaching
the Windows exporter across that boundary needs additional work. Windows Steam
export remains available and now uses the saved Steam folder.

The helper reads only live Steam process identity and connection/download logs.
It does not read Steam passwords or saved login tokens. It accepts only the two
pinned depot requests, checks the authorized account again before launching a
command, and uses argument arrays rather than shell command strings. Its API
binds only to `127.0.0.1` on a random port and requires a random, per-launch bearer
token passed through the child environment. No firewall change or public port is
required. The helper stops when its Wine launcher process exits; closing its
terminal also makes subsequent launcher checks fail closed. It never stops Steam.

The portable build has no automatic installer updates. To upgrade, close the
launcher and helper and extract the new portable release. Launcher settings remain
in the Wine prefix; preserved archives and game output stay in your selected storage.

## Testing and troubleshooting

This release has automated Windows launcher tests and native Linux helper tests,
including a real Linux process fixture, account switch/logout checks, stale-log
rejection, command validation, and loopback authentication. These are **not** an
end-to-end test with Wine, Steam downloads, or gameplay on your distribution.

If detection fails, report your distribution, Wine version, Steam edition, the
helper's terminal error, and the launcher log. Do not send passwords, session
files, or environment dumps. A missing/ambiguous process or unreadable account
log blocks downloads rather than trusting a saved Steam login.

## Custom installer destination

For the normal Windows installer, Velopack supports an explicit destination:

```powershell
.\KPCLauncher-win-Setup.exe --installto 'D:\Apps\KPCLauncher'
```

Use a dedicated empty folder for a new installation. This does not move an
already installed launcher. Under Wine the destination is a Wine/Windows path,
for example `wine KPCLauncher-win-Setup.exe --installto 'C:\Apps\KPCLauncher'`.
For native Linux Steam, the portable helper workflow above is the supported
experimental entry point. There is no new graphical directory-selection wizard.

References: [Velopack installer options](https://docs.velopack.io/reference/cli/content/setup-windows),
[Valve's Steam for Linux tracker](https://github.com/ValveSoftware/steam-for-linux),
[Flatpak Steam project](https://github.com/flathub/com.valvesoftware.Steam).
