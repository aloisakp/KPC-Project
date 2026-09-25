#!/usr/bin/env python3
"""Run the Windows launcher under Wine with a local native-Steam bridge (Python 3.9+)."""
import argparse
import datetime as dt
import hmac
import http.server
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import sys
import threading

APP = 844870
DEPOT = 844871
MANIFESTS = (4819182874103212568, 6221929141711975568)
INDIVIDUAL_BASE = 76561197960265728
STATE_LINE = re.compile(r"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\] "
                        r"\[(Logged On|Logged Off|Logging On|Connecting|Connected),[^\]\r\n]*\] \[U:1:(\d+)\]", re.M)


def connected_identity(log, started):
    matches = list(STATE_LINE.finditer(log))
    if not matches:
        return None
    last = matches[-1]
    when, state, account = last.groups()
    account = int(account)
    if (state != "Logged On" or not 0 < account <= 0xffffffff
            or dt.datetime.strptime(when, "%Y-%m-%d %H:%M:%S").timestamp() < started - 2
            or "ConnectionDisconnected(" in log[last.end():]):
        return None
    return str(INDIVIDUAL_BASE + account)


def native_process(root, proc=Path("/proc")):
    """Bind the account log to exactly one live Steam binary from this data root."""
    binaries = [root / arch / "steam" for arch in ("ubuntu12_32", "ubuntu12_64")]
    ticks = os.sysconf("SC_CLK_TCK")
    boot = int(next(line.split()[1] for line in (proc / "stat").read_text().splitlines()
                    if line.startswith("btime ")))
    found = []
    for entry in proc.iterdir():
        if not entry.name.isdigit():
            continue
        try:
            if entry.stat().st_uid != os.getuid():
                continue
            if not any(binary.exists() and (entry / "exe").samefile(binary) for binary in binaries):
                continue
            if b"-child-update-ui" in (entry / "cmdline").read_bytes().split(b"\0"):
                continue
            stat = (entry / "stat").read_text().rsplit(")", 1)[1].split()
            if stat[0] == "Z":
                continue
            found.append((int(entry.name), boot + int(stat[19]) / ticks))
        except (OSError, ValueError, IndexError):
            continue
    if len(found) > 1:
        raise ValueError("More than one Steam process uses this folder. Restart Steam and try again.")
    return found[0] if found else None


def data_root(path):
    root = Path(path).expanduser().resolve(strict=True)
    if not root.is_dir() or not (root / "steamapps").is_dir() or not any(
            (root / arch / "steam").is_file() for arch in ("ubuntu12_32", "ubuntu12_64")):
        raise ValueError("Select Steam's data folder containing steamapps and ubuntu12_32/steam, not /usr/bin.")
    # Root aliases such as ~/.steam/steam are resolved first; writable descendants
    # must remain ordinary directories, matching the launcher's download policy.
    for part in (root / "steamapps", root / "steamapps" / "content"):
        if part.is_symlink():
            raise ValueError("Steam's steamapps/content directories must not be symbolic links.")
    return root


class SteamBridge:
    def __init__(self, mode, winepath, requested_root=None):
        self.mode = mode
        self.winepath = winepath
        self.requested_root = requested_root
        self.root = None
        self.windows_root = None
        self.command = None
        self.children = []

    def convert(self, flag, path):
        result = subprocess.run([self.winepath, flag, str(path)], capture_output=True,
                                text=True, timeout=15, check=True)
        value = result.stdout.strip()
        if not value or "\n" in value or "\r" in value:
            raise ValueError("winepath could not translate the selected path.")
        return value

    def candidates(self, all_modes=False):
        home = Path.home()
        normal = [home / ".local/share/Steam", home / ".steam/steam", home / ".steam/root"]
        flat = [home / ".var/app/com.valvesoftware.Steam/.local/share/Steam",
                home / ".var/app/com.valvesoftware.Steam/data/Steam"]
        result = {}
        for kind, paths in (("native", normal), ("flatpak", flat)):
            if not all_modes and self.mode not in ("auto", kind):
                continue
            for path in paths:
                try:
                    result[data_root(path)] = kind
                except (OSError, ValueError):
                    pass
        return result

    def select(self, windows_path=None):
        options = self.candidates()
        chosen = self.convert("-u", windows_path) if windows_path else self.requested_root
        if chosen:
            root = data_root(chosen)
            kind = self.mode if self.mode != "auto" else options.get(root)
            if kind is None:
                raise ValueError("For a custom Steam data folder, specify --steam native or --steam flatpak.")
            known_kind = self.candidates(all_modes=True).get(root)
            if known_kind is not None and known_kind != kind:
                raise ValueError("The selected data folder belongs to the other Steam edition. Choose the matching --steam mode.")
        elif len(options) == 1:
            root, kind = next(iter(options.items()))
        elif not options:
            raise ValueError("Steam data was not found. Start Steam once, or pass --steam-root /path/to/Steam.")
        else:
            raise ValueError("Both native and Flatpak Steam data were found. Choose --steam native or --steam flatpak.")
        if kind == "flatpak":
            executable = shutil.which("flatpak")
            if not executable:
                raise ValueError("flatpak is not available on PATH.")
            subprocess.run([executable, "info", "com.valvesoftware.Steam"], check=True,
                           capture_output=True, timeout=10)
            command = [executable, "run", "com.valvesoftware.Steam"]
        else:
            executable = shutil.which("steam")
            if not executable:
                raise ValueError("steam is not available on PATH (the equivalent of command -v steam).")
            command = [executable]
        mapped = self.convert("-w", root)
        self.root, self.windows_root, self.command = root, mapped, command
        return self.status()

    def status(self):
        if self.root is None:
            raise ValueError("Select a Steam data folder first.")
        process = native_process(self.root)
        identity = None
        if process:
            try:
                with (self.root / "logs/connection_log.txt").open("rb") as log:
                    log.seek(0, 2)
                    log.seek(max(0, log.tell() - 512 * 1024))
                    identity = connected_identity(log.read().decode("utf-8", errors="replace"), process[1])
            except OSError:
                pass
            if native_process(self.root) != process:
                process, identity = None, None
        return {"schema": 1, "root": self.windows_root, "unixRoot": str(self.root),
                "pid": process[0] if process else None, "started": process[1] if process else None,
                "steamId": identity}

    def matches_staging(self, reported):
        if not isinstance(reported, str) or not reported.startswith("/"):
            return False
        stage = self.root / f"steamapps/content/app_{APP}/depot_{DEPOT}"
        # Do not follow redirects inside mutable staging.
        for part in (stage.parent.parent, stage.parent, stage):
            if part.is_symlink():
                return False
        return Path(reported).resolve() == stage.resolve()

    def require_idle(self, started):
        console = self.root / "logs/console_log.txt"
        if not console.exists():
            return
        pending, awaiting = 0, False
        with console.open(encoding="utf-8", errors="replace") as log:
            for line in log:
                if len(line) < 22 or line[0] != "[" or line[20] != "]":
                    continue
                try:
                    if dt.datetime.strptime(line[1:20], "%Y-%m-%d %H:%M:%S").timestamp() < started - 2:
                        continue
                except ValueError:
                    continue
                message = line[22:]
                if message.startswith("ExecCommandLine:") and re.search(
                        rf'\+download_depot\s+{APP}\s+{DEPOT}\s+\d+(?=\s|"|$)', message):
                    pending, awaiting = pending + 1, True
                elif message.startswith(f"Downloading depot {DEPOT} ("):
                    pending += not awaiting
                    awaiting = False
                elif (complete := re.match(r'^Depot download complete : "(.*)" \(manifest [0-9]+\)', message)):
                    if self.matches_staging(complete[1]):
                        pending, awaiting = max(0, pending - 1), False
        if pending:
            raise ValueError("Steam has an unfinished depot request. Let it finish or restart Steam before retrying.")

    def download(self, request):
        if (request.get("appId") != APP or request.get("depotId") != DEPOT
                or request.get("manifestId") not in [str(value) for value in MANIFESTS]):
            raise ValueError("Unsupported depot request.")
        state = self.status()
        if not state["steamId"] or request.get("steamId") != state["steamId"]:
            raise ValueError("Keep native Steam signed in to the account authorized in the launcher.")
        self.require_idle(state["started"])
        if self.status() != state:
            raise ValueError("Steam changed while preparing the download. Retry.")
        # Argument arrays only: no shell, eval, or user-supplied command strings.
        self.children = [child for child in self.children if child.poll() is None]
        self.children.append(subprocess.Popen(self.command + ["+download_depot", str(APP), str(DEPOT), request["manifestId"]],
                            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                            close_fds=True, start_new_session=True))
        return {"ok": True}

    def idle(self, request):
        if request.get("appId") != APP or request.get("depotId") != DEPOT:
            raise ValueError("Unsupported depot.")
        state = self.status()
        if not state["pid"]:
            raise ValueError("Start native Steam and sign in before pressing Install.")
        self.require_idle(state["started"])
        if self.status() != state:
            raise ValueError("Steam changed during the idle check. Retry.")
        return {"ok": True}


class BridgeServer(http.server.HTTPServer):
    def __init__(self, bridge, token):
        self.bridge, self.token = bridge, token
        super().__init__(("127.0.0.1", 0), BridgeHandler)

    def get_request(self):
        connection, address = super().get_request()
        connection.settimeout(3)
        return connection, address


class BridgeHandler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass  # Never log the bearer token or account data.

    def do_POST(self):
        self.connection.settimeout(3)
        expected_host = f"127.0.0.1:{self.server.server_port}"
        if (self.headers.get("Host") != expected_host or self.headers.get("Origin")
                or not hmac.compare_digest(self.headers.get("Authorization", ""), "Bearer " + self.server.token)):
            self.send_error(403)
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 < length <= 8192 or self.headers.get("Transfer-Encoding"):
                raise ValueError("Invalid request length.")
            request = json.loads(self.rfile.read(length))
            if not isinstance(request, dict):
                raise ValueError("Invalid request.")
            bridge = self.server.bridge
            if self.path == "/select":
                response = bridge.select(request.get("root"))
            elif self.path == "/status":
                response = bridge.status()
            elif self.path == "/path-match":
                response = {"matches": bridge.matches_staging(request.get("path"))}
            elif self.path == "/download":
                response = bridge.download(request)
            elif self.path == "/idle":
                response = bridge.idle(request)
            else:
                raise ValueError("Unsupported operation.")
            code = 200
        except (OSError, ValueError, TypeError, subprocess.SubprocessError) as error:
            response, code = {"error": str(error)}, 400
        data = json.dumps(response).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("launcher", nargs="?", default=str(Path(__file__).with_name("KpcLauncher.exe")))
    parser.add_argument("--steam", choices=("auto", "native", "flatpak"), default="auto")
    parser.add_argument("--steam-root", help="Steam data folder, not /usr/bin/steam")
    parser.add_argument("--wine", default="wine", help="Wine executable; respects WINEPREFIX")
    parser.add_argument("--winepath", default="winepath", help="Matching Wine path conversion executable")
    args = parser.parse_args()
    if sys.platform != "linux":
        parser.error("Run this helper on Linux, outside Wine and outside Flatpak/Bottles sandboxes.")
    launcher = Path(args.launcher).expanduser().resolve(strict=True)
    if not launcher.is_file() or launcher.suffix.lower() != ".exe":
        parser.error("Choose the extracted KpcLauncher.exe.")
    wine, winepath = shutil.which(args.wine), shutil.which(args.winepath)
    if not wine or not winepath:
        parser.error("Install Wine, or specify matching --wine and --winepath executables.")
    bridge = SteamBridge(args.steam, winepath, args.steam_root)
    bridge.select()
    token = secrets.token_hex(32)
    server = BridgeServer(bridge, token)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    environment = os.environ.copy()
    environment["KPC_LINUX_BRIDGE"] = f"http://127.0.0.1:{server.server_port}/"
    environment["KPC_LINUX_BRIDGE_TOKEN"] = token
    if args.steam_root:
        environment["KPC_LINUX_STEAM_ROOT"] = bridge.windows_root
    else:
        environment.pop("KPC_LINUX_STEAM_ROOT", None)
    print("Native Steam data:", bridge.root)
    print("Keep this terminal open while using the launcher. Start Steam and sign in before Install.")
    try:
        return subprocess.call([wine, str(launcher)], env=environment, cwd=launcher.parent)
    finally:
        server.shutdown()
        server.server_close()
        thread.join()


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"KPC Linux: {error}", file=sys.stderr)
        sys.exit(1)
