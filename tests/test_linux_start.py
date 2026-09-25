import datetime as dt
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch
from urllib.request import Request, urlopen
from urllib.error import HTTPError

spec = importlib.util.spec_from_file_location("linux_start", Path(__file__).resolve().parents[1] / "linux-start.py")
kpc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(kpc)


class BridgeTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="kpc-linux-test-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name) / "Steam with spaces"
        (self.root / "steamapps").mkdir(parents=True)
        (self.root / "ubuntu12_32").mkdir()
        (self.root / "ubuntu12_32/steam").write_text("fixture - not run")
        (self.root / "logs").mkdir()
        self.now = time.time()
        self.account = str(kpc.INDIVIDUAL_BASE + 42)
        self.connected = self.line("[Logged On, 3, 0] [U:1:42]")
        (self.root / "logs/connection_log.txt").write_text(self.connected)
        self.bridge = kpc.SteamBridge("native", "winepath")
        self.bridge.root = self.root
        self.bridge.windows_root = "Z:\\test\\Steam with spaces"
        self.bridge.command = ["steam"]
        self.process = patch.object(kpc, "native_process", return_value=(1234, self.now - 1))
        self.process.start()
        self.addCleanup(self.process.stop)

    def line(self, text):
        return f"[{dt.datetime.fromtimestamp(self.now):%Y-%m-%d %H:%M:%S}] {text}\n"

    def test_identity_requires_current_connected_process(self):
        self.assertEqual(self.bridge.status()["steamId"], self.account)
        self.assertIsNone(kpc.connected_identity(self.connected, self.now + 60))
        self.assertIsNone(kpc.connected_identity(self.connected + "ConnectionDisconnected(\n", self.now - 1))
        self.assertIsNone(kpc.connected_identity(self.connected + self.line("[Logged Off, 0] [U:1:42]"), self.now - 1))
        self.assertIsNone(kpc.connected_identity(self.line("[Logged On, 0] [U:1:0]"), self.now - 1))
        with patch.object(kpc, "native_process", side_effect=[(1234, self.now - 1), None]):
            self.assertIsNone(self.bridge.status()["steamId"])
        with patch.object(kpc, "native_process", return_value=None):
            self.assertIsNone(self.bridge.status()["steamId"])

    def test_data_folder_not_command_path(self):
        self.assertEqual(kpc.data_root(self.root), self.root.resolve())
        for path in (self.root / "ubuntu12_32/steam", self.root / "steamapps", self.root.parent):
            with self.assertRaises(ValueError):
                kpc.data_root(path)

    def test_linux_completion_path_and_idle_guard(self):
        stage = self.root / f"steamapps/content/app_{kpc.APP}/depot_{kpc.DEPOT}"
        # POSIX behavior is separately exercised by the Linux CI job.
        if sys.platform != "linux":
            self.skipTest("POSIX path comparisons")
        self.assertTrue(self.bridge.matches_staging(str(stage)))
        self.assertFalse(self.bridge.matches_staging(str(stage) + "-other"))
        self.assertFalse(self.bridge.matches_staging("relative/path"))
        console = self.root / "logs/console_log.txt"
        request = self.line(f"ExecCommandLine: +download_depot {kpc.APP} {kpc.DEPOT} {kpc.MANIFESTS[0]}")
        begun = self.line(f"Downloading depot {kpc.DEPOT} (1 files, 1 MB)")
        console.write_text(request + begun)
        with self.assertRaises(ValueError):
            self.bridge.require_idle(self.now - 1)
        console.write_text(request + begun + self.line(f'Depot download complete : "{stage}" (manifest {kpc.MANIFESTS[0]})'))
        self.bridge.require_idle(self.now - 1)
        console.write_text(request + self.line('Depot download complete : "/wrong" (manifest 1)'))
        with self.assertRaises(ValueError):
            self.bridge.require_idle(self.now - 1)
        self.bridge.require_idle(self.now + 60)

    def test_commands_are_pinned_argument_arrays(self):
        request = {"appId": kpc.APP, "depotId": kpc.DEPOT, "manifestId": str(kpc.MANIFESTS[0]), "steamId": self.account}
        with patch.object(kpc.subprocess, "Popen") as launch:
            self.bridge.download(request)
            self.assertEqual(launch.call_args.args[0], ["steam", "+download_depot", str(kpc.APP), str(kpc.DEPOT), str(kpc.MANIFESTS[0])])
            self.assertNotIn("shell", launch.call_args.kwargs)
            for field, value in (("appId", 1), ("depotId", 1), ("manifestId", "1; touch /tmp/unsafe"), ("steamId", "wrong")):
                with self.assertRaises(ValueError):
                    self.bridge.download(dict(request, **{field: value}))
            self.assertEqual(launch.call_count, 1)

    def test_native_and_flatpak_detection(self):
        for mode, expected in (("native", ["/usr/bin/steam"]), ("flatpak", ["/usr/bin/flatpak", "run", "com.valvesoftware.Steam"])):
            bridge = kpc.SteamBridge(mode, "winepath", str(self.root))
            with patch.object(bridge, "candidates", return_value={self.root.resolve(): mode}), \
                    patch.object(bridge, "convert", return_value="Z:\\Steam"), \
                    patch.object(kpc.shutil, "which", side_effect=lambda name: "/usr/bin/" + name), \
                    patch.object(kpc.subprocess, "run"):
                bridge.select()
                self.assertEqual(bridge.command, expected)
        bridge = kpc.SteamBridge("auto", "winepath")
        with patch.object(bridge, "candidates", return_value={self.root: "native", self.root.parent: "flatpak"}):
            with self.assertRaisesRegex(ValueError, "Both native and Flatpak"):
                bridge.select()

    def test_loopback_requires_capability_and_rejects_browser_origins(self):
        token = "a" * 64
        server = kpc.BridgeServer(self.bridge, token)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            def request(path, extra=None):
                headers = {"Authorization": "Bearer " + token, "Content-Type": "application/json"}
                headers.update(extra or {})
                return urlopen(Request(f"http://127.0.0.1:{server.server_port}/{path}", b"{}", headers), timeout=5)
            with request("status") as response:
                self.assertEqual(json.load(response)["steamId"], self.account)
            for headers in ({"Authorization": "Bearer wrong"}, {"Origin": "https://example.com"}, {"Host": "evil.example"}):
                with self.assertRaises(HTTPError) as error:
                    request("status", headers)
                self.assertEqual(error.exception.code, 403)
            with self.assertRaises(HTTPError) as error:
                request("run-any-command")
            self.assertEqual(error.exception.code, 400)
        finally:
            server.shutdown()
            server.server_close()
            thread.join()

    @unittest.skipUnless(sys.platform == "linux", "requires a real Linux /proc filesystem")
    def test_real_linux_process_identity_and_symlink_alias(self):
        self.process.stop()
        binary = self.root / "ubuntu12_32/steam"
        shutil.copyfile(shutil.which("sleep"), binary)
        binary.chmod(0o700)
        started = time.time()
        child = subprocess.Popen([str(binary), "30"])
        try:
            result = kpc.native_process(self.root)
            self.assertEqual(result[0], child.pid)
            self.assertLess(abs(result[1] - started), 2)
            alias = self.root.parent / "alias"
            alias.symlink_to(self.root, target_is_directory=True)
            self.assertEqual(kpc.data_root(alias), self.root.resolve())
            self.assertEqual(kpc.native_process(alias)[0], child.pid)
            self.assertEqual(self.bridge.status()["steamId"], self.account)
            duplicate = subprocess.Popen([str(binary), "30"])
            try:
                with self.assertRaisesRegex(ValueError, "More than one"):
                    kpc.native_process(self.root)
            finally:
                duplicate.terminate()
                duplicate.wait(timeout=5)
        finally:
            child.terminate()
            child.wait(timeout=5)
        self.assertIsNone(kpc.native_process(self.root))
        self.assertIsNone(self.bridge.status()["steamId"])


if __name__ == "__main__":
    unittest.main()
