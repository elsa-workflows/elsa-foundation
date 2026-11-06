"""Real owned-process observation controls; this fixture is not Studio qualification."""
import json
import os
from pathlib import Path
import signal
import sys
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import runtime


class RuntimeContentRootTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        self.published = self.root / "published"
        self.published.mkdir()
        (self.published / "host.py").write_text("immutable host input")
        self.content = self.root / "runtime-content"
        self.content.mkdir(mode=0o700)
        self.profile = {"published": self.published, "assembly": "host.py", "env": {}}
        self.run = SimpleNamespace(root=self.root, dotnet=sys.executable, profiles={"backend": self.profile})

    def test_default_and_separate_content_keep_binary_command_and_working_directory(self):
        for selected in (None, self.content):
            with self.subTest(selected=selected):
                if selected is not None:
                    self.profile["contentRoot"] = selected
                expected = self.published if selected is None else selected
                with patch.object(runtime.subprocess, "Popen") as launch:
                    launch.return_value.pid = 123
                    owned = runtime._OwnedProcess(self.run, "backend", "http://127.0.0.1:18001")
                    receipt = owned.spawn()
                    command = launch.call_args.args[0]
                    self.assertEqual([sys.executable, str(self.published / "host.py"), "--contentRoot", str(expected),
                                      "--urls", "http://127.0.0.1:18001"], command)
                    self.assertEqual(self.published, launch.call_args.kwargs["cwd"])
                    self.assertEqual(str(self.published), receipt["publishedDirectory"])
                    self.assertEqual(str(expected), receipt["contentRoot"])
                    self.assertEqual({"device": expected.stat().st_dev, "inode": expected.stat().st_ino}, receipt["contentRootIdentity"])
                    self.assertEqual(runtime.sha(self.published / "host.py"), receipt["assemblySha256"])
                    self.assertEqual("immutable host input", (self.published / "host.py").read_text())

    def test_invalid_or_overlapping_content_root_refuses_before_process_or_receipt(self):
        outside = tempfile.TemporaryDirectory()
        self.addCleanup(outside.cleanup)
        nested = self.published / "data"
        nested.mkdir()
        invalid = (Path(outside.name).resolve(), self.root / "missing", self.published / "host.py", Path("relative"),
                   self.root, nested, None, 1)
        for selected in invalid:
            with self.subTest(selected=selected), patch.object(runtime.subprocess, "Popen") as launch:
                self.profile["contentRoot"] = selected
                with self.assertRaisesRegex(RuntimeError, "content root"):
                    runtime._OwnedProcess(self.run, "backend", "http://127.0.0.1:18001")
                launch.assert_not_called()
                self.assertFalse((self.root / "backend-process.json").exists())

    def test_symlink_leaf_and_parent_are_rejected_even_when_the_target_is_owned(self):
        link = self.root / "content-link"
        link.symlink_to(self.content, target_is_directory=True)
        child = self.content / "nested"
        child.mkdir()
        for selected in (link, link / "nested"):
            with self.subTest(selected=selected), patch.object(runtime.subprocess, "Popen") as launch:
                self.profile["contentRoot"] = selected
                with self.assertRaisesRegex(RuntimeError, "symlink"):
                    runtime._OwnedProcess(self.run, "backend", "http://127.0.0.1:18001")
                launch.assert_not_called()

    def test_custom_content_cannot_overlap_another_profile_binary_publish(self):
        other = self.root / "other-published"
        other.mkdir()
        self.run.profiles["other"] = {"published": other}
        self.profile["contentRoot"] = other
        with self.assertRaisesRegex(RuntimeError, "overlap"):
            runtime.owned_content_root(self.run, self.profile)

    def test_replaced_data_directory_or_changed_binary_refuses_generation_provenance(self):
        self.profile["contentRoot"] = self.content
        with patch.object(runtime.subprocess, "Popen") as launch:
            launch.return_value.pid = 123
            owned = runtime._OwnedProcess(self.run, "backend", "http://127.0.0.1:18001")
            owned.spawn()
        self.content.rename(self.root / "retained-original-content")
        self.content.mkdir()
        with self.assertRaisesRegex(RuntimeError, "directory was replaced"):
            owned.require_unchanged_inputs()
        self.content.rmdir()
        (self.root / "retained-original-content").rename(self.content)
        (self.published / "host.py").write_text("changed binary input")
        with self.assertRaisesRegex(RuntimeError, "assembly changed"):
            owned.require_unchanged_inputs()


class RuntimeObservationTests(unittest.TestCase):
    @unittest.skipUnless(os.name == "posix", "Requires a real POSIX SIGTERM handler")
    def test_pending_shutdown_keeps_the_signalled_process_until_it_exits(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            published = root / "published"
            published.mkdir()
            release = published / "release"
            signalled = published / "signalled"
            (published / "host.py").write_text('''import argparse, signal, time
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--contentRoot');p.add_argument('--urls');a=p.parse_args()
root=Path(a.contentRoot)
signal_count=0
def terminate(signum, frame):
 global signal_count
 signal_count+=1
 (root/'signalled').write_text(str(signal_count))
signal.signal(signal.SIGTERM,terminate)
print('Now listening on: '+a.urls,flush=True)
print('WEAVER_BACKEND_CONSUMER_READY',flush=True)
while not (signal_count and (root/'release').exists()): time.sleep(.01)
''')
            run = SimpleNamespace(root=root, dotnet=sys.executable, profiles={"backend": {
                "published": published, "assembly": "host.py", "env": dict(os.environ)}})
            original = RuntimeError("Original qualification failure")
            failures = []

            def launch():
                try:
                    with runtime.server(run, "backend", runtime.owned_origin()):
                        raise original
                except Exception as error:
                    failures.append(error)

            with patch.object(runtime, "SHUTDOWN_OBSERVATION_SECONDS", .03):
                worker = threading.Thread(target=launch)
                worker.start()
                try:
                    pending = None
                    deadline = time.monotonic() + 5
                    while time.monotonic() < deadline and worker.is_alive():
                        path = root / "backend-process.json"
                        if path.exists():
                            try:
                                item = json.loads(path.read_text())
                                if len(item.get("shutdownObservations", [])) >= 2:
                                    pending = item
                                    break
                            except json.JSONDecodeError:
                                pass  # Receipt writer may be between truncate and flush.
                        time.sleep(.01)
                    self.assertIsNotNone(pending, "The signalled child must remain observable during shutdown: " + repr(failures))
                    self.assertTrue(worker.is_alive())
                    os.kill(pending["pid"], 0)
                    self.assertEqual(signalled.read_text(), "1")
                    self.assertTrue(all(item["status"] == "pending" and item["pid"] == pending["pid"]
                                        for item in pending["shutdownObservations"]))
                    self.assertNotIn("exitCode", pending)
                finally:
                    release.touch()
                    worker.join(10)
                self.assertFalse(worker.is_alive(), "The same child did not finish after its private release")
                self.assertEqual(failures, [original])
                final = json.loads((root / "backend-process.json").read_text())
                self.assertEqual(final["pid"], pending["pid"])
                self.assertEqual(final["exitCode"], 0)
                self.assertEqual(final["runtimeCacheFiles"], {"NUGET_PACKAGES": [], "NUGET_HTTP_CACHE_PATH": []})
                self.assertGreaterEqual(final["stoppedAt"], final["shutdownObservations"][-1]["at"])
                self.assertEqual(signalled.read_text(), "1")

    def test_pending_startup_keeps_the_same_process_until_it_can_confirm_readiness(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            published = root / "published"
            published.mkdir()
            release = published / "release"
            (published / "host.py").write_text('''import argparse, time, os, signal, sys
from pathlib import Path
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse
p=argparse.ArgumentParser();p.add_argument('--contentRoot');p.add_argument('--urls');a=p.parse_args()
signal.signal(signal.SIGTERM,lambda signum,frame:sys.exit(0))
if any(key in os.environ for key in ('ANTHROPIC_API_KEY','GITHUB_TOKEN','DOTNET_ADDITIONAL_DEPS')):
 raise RuntimeError('Ambient credential or dependency input reached the runtime fixture')
class Handler(BaseHTTPRequestHandler):
 def do_GET(self): self.send_response(200);self.end_headers();self.wfile.write(b'live')
 def log_message(self,*args): pass
server=HTTPServer(('127.0.0.1',urlparse(a.urls).port),Handler)
print('Now listening on: '+a.urls,flush=True)
while not (Path(a.contentRoot)/'release').exists(): time.sleep(.01)
print('WEAVER_BACKEND_CONSUMER_READY',flush=True)
server.serve_forever()
''')
            environment = dict(os.environ)
            environment.update(ANTHROPIC_API_KEY="synthetic-fixture-canary", GITHUB_TOKEN="synthetic-fixture-canary",
                               DOTNET_ADDITIONAL_DEPS="synthetic-undeclared-dependency")
            run = SimpleNamespace(root=root, dotnet=sys.executable, profiles={"backend": {
                "published": published, "assembly": "host.py", "env": environment}})
            origin = runtime.owned_origin()
            reached = threading.Event()
            finish = threading.Event()
            failures, observed = [], []

            def launch():
                try:
                    with runtime.server(run, "backend", origin) as started:
                        observed.append(started.copy())
                        reached.set()
                        finish.wait(5)
                except Exception as error:
                    failures.append(error)

            with patch.object(runtime, "STARTUP_OBSERVATION_SECONDS", .03):
                worker = threading.Thread(target=launch)
                worker.start()
                try:
                    pending = None
                    deadline = time.monotonic() + 5
                    while time.monotonic() < deadline and worker.is_alive():
                        path = root / "backend-process.json"
                        if path.exists():
                            try:
                                item = json.loads(path.read_text())
                                if item.get("startupObservations"):
                                    pending = item
                                    break
                            except json.JSONDecodeError:
                                pass  # Receipt writer may be between truncate and flush.
                        time.sleep(.01)
                    self.assertIsNotNone(pending, "The live host must remain observable after a pending window: " + repr(failures))
                    os.kill(pending["pid"], 0)
                    self.assertFalse(reached.is_set())
                    release.touch()
                    self.assertTrue(reached.wait(5), "The same host did not reach readiness")
                    self.assertEqual(observed[0]["pid"], pending["pid"])
                    self.assertEqual(runtime.Http(origin).request("/health/live")[0], 200)
                finally:
                    release.touch()
                    finish.set()
                    worker.join(10)
                self.assertFalse(worker.is_alive())
                self.assertEqual(failures, [])
                self.assertIsNotNone(json.loads((root / "backend-process.json").read_text())["exitCode"])


@unittest.skipUnless(os.name == "posix", "Requires a real POSIX SIGTERM handler")
class RuntimeShutdownTests(unittest.TestCase):
    def launch(self, root, *, exit_code=0, cache_key=None, bad_listener=False):
        published = root / "published"
        published.mkdir()
        (published / "host.py").write_text('''import argparse, os, signal, sys
from pathlib import Path
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse
p=argparse.ArgumentParser();p.add_argument('--contentRoot');p.add_argument('--urls');a=p.parse_args()
def terminate(signum, frame):
 cache_key=CACHE_KEY
 if cache_key: (Path(os.environ[cache_key])/'shutdown-cache-entry').write_text('undeclared runtime input')
 sys.exit(EXIT_CODE)
signal.signal(signal.SIGTERM,terminate)
class Handler(BaseHTTPRequestHandler):
 def do_GET(self): self.send_response(200);self.end_headers();self.wfile.write(b'live')
 def log_message(self,*args): pass
server=HTTPServer(('127.0.0.1',urlparse(a.urls).port),Handler)
print('Now listening on: '+('http://127.0.0.1:1' if BAD_LISTENER else a.urls),flush=True)
print('WEAVER_BACKEND_CONSUMER_READY',flush=True)
server.serve_forever()
'''.replace("CACHE_KEY", repr(cache_key)).replace("EXIT_CODE", repr(exit_code))
            .replace("BAD_LISTENER", repr(bad_listener)))
        run = SimpleNamespace(root=root, dotnet=sys.executable, profiles={"backend": {
            "published": published, "assembly": "host.py", "env": dict(os.environ)}})
        return runtime.server(run, "backend", runtime.owned_origin())

    def assert_final_receipt(self, root, *, exit_code, cache_key=None):
        receipt = json.loads((root / "backend-process.json").read_text())
        self.assertEqual(receipt["exitCode"], exit_code)
        self.assertGreaterEqual(receipt["stoppedAt"], receipt["startedAt"])
        expected = {"NUGET_PACKAGES": [], "NUGET_HTTP_CACHE_PATH": []}
        if cache_key:
            expected[cache_key] = ["shutdown-cache-entry"]
        self.assertEqual(receipt["runtimeCacheFiles"], expected)
        with self.assertRaises(ProcessLookupError):
            os.kill(receipt["pid"], 0)

    def test_clean_shutdown_accepts_qualification(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            with self.launch(root) as started:
                self.assertEqual(runtime.Http(started["origin"]).request("/health/live")[0], 200)
            self.assert_final_receipt(root, exit_code=0)

    def test_nonzero_shutdown_rejects_qualification_after_retaining_receipt(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            with self.assertRaisesRegex(RuntimeError, "Owned consumer exited unsuccessfully during shutdown"):
                with self.launch(root, exit_code=1) as started:
                    self.assertEqual(runtime.Http(started["origin"]).request("/health/live")[0], 200)
            self.assert_final_receipt(root, exit_code=1)

    def test_shutdown_cache_write_rejects_qualification_after_retaining_receipt(self):
        for cache_key in ("NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH"):
            with self.subTest(cache_key=cache_key), tempfile.TemporaryDirectory() as directory:
                root = Path(directory).resolve()
                with self.assertRaisesRegex(RuntimeError, "Native runtime populated an undeclared NuGet cache"):
                    with self.launch(root, cache_key=cache_key) as started:
                        self.assertEqual(runtime.Http(started["origin"]).request("/health/live")[0], 200)
                self.assert_final_receipt(root, exit_code=0, cache_key=cache_key)

    def test_original_body_failure_survives_nonzero_shutdown_and_cache_write(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            original = RuntimeError("Original qualification failure")
            with self.assertRaises(RuntimeError) as observed:
                with self.launch(root, exit_code=1, cache_key="NUGET_PACKAGES") as started:
                    self.assertEqual(runtime.Http(started["origin"]).request("/health/live")[0], 200)
                    raise original
            self.assertIs(observed.exception, original)
            self.assert_final_receipt(root, exit_code=1, cache_key="NUGET_PACKAGES")

    def test_original_startup_failure_survives_nonzero_shutdown_and_cache_write(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            with self.assertRaisesRegex(RuntimeError, "Ready marker did not match actual Kestrel listener"):
                with self.launch(root, exit_code=1, cache_key="NUGET_HTTP_CACHE_PATH", bad_listener=True):
                    self.fail("An invalid ready marker must not enter the qualification body")
            self.assert_final_receipt(root, exit_code=1, cache_key="NUGET_HTTP_CACHE_PATH")


@unittest.skipUnless(os.name == "posix", "Requires real POSIX SIGTERM/SIGKILL handling")
class RestartableServerTests(unittest.TestCase):
    """The driver's own host restarts (#3441): one owned origin, a separate receipt per generation, no overwritten evidence."""
    HOST = '''import argparse, os, signal, sys
from pathlib import Path
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse
p=argparse.ArgumentParser();p.add_argument('--contentRoot');p.add_argument('--urls');a=p.parse_args()
root=Path(a.contentRoot)
signal.signal(signal.SIGTERM,lambda signum,frame:sys.exit(int((root/'exit-code').read_text()) if (root/'exit-code').exists() else 0))
class Handler(BaseHTTPRequestHandler):
 def do_GET(self): self.send_response(200);self.end_headers();self.wfile.write(str(os.getpid()).encode())
 def log_message(self,*args): pass
server=HTTPServer(('127.0.0.1',urlparse(a.urls).port),Handler)
print('Now listening on: '+a.urls,flush=True)
print('WEAVER_BACKEND_CONSUMER_READY',flush=True)
server.serve_forever()
'''

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.published = self.root / "published"
        self.published.mkdir()
        (self.published / "host.py").write_text(self.HOST)
        self.run = SimpleNamespace(root=self.root, dotnet=sys.executable, profiles={"backend": {
            "published": self.published, "assembly": "host.py", "env": dict(os.environ)}})
        self.origin = runtime.owned_origin()

    def pid(self):
        status, body, _ = runtime.Http(self.origin).request("/health/live")
        self.assertEqual(200, status)
        return int(body)

    def test_graceful_and_abrupt_restarts_keep_the_origin_and_retain_every_generation(self):
        observed = []
        def stopped():
            with self.assertRaises(OSError):
                runtime.Http(self.origin).request("/health/live")  # No generation runs while configuration changes.
            observed.append("stopped")
            return {"configured": True}
        with runtime.RestartableServer(self.run, "backend", self.origin) as host:
            first = self.pid()
            graceful = host.restart("configure", while_stopped=stopped)
            second = self.pid()
            abrupt = host.restart("crash", abrupt=True)
            third = self.pid()
        self.assertEqual(["stopped"], observed)
        self.assertEqual(3, len({first, second, third}))
        self.assertEqual(("graceful", 1, 2, 0, {"configured": True}), (graceful["kind"], graceful["fromGeneration"], graceful["toGeneration"],
                                                                     graceful["stoppedProcess"]["exitCode"], graceful["whileStopped"]))
        self.assertEqual(("abrupt", 2, 3, -signal.SIGKILL), (abrupt["kind"], abrupt["fromGeneration"], abrupt["toGeneration"],
                                                            abrupt["stoppedProcess"]["exitCode"]))
        self.assertEqual([graceful, abrupt], host.restarts)
        receipts = [json.loads((self.root / name).read_text()) for name in ("backend-process.json", "backend-process-g2.json", "backend-process-g3.json")]
        self.assertEqual([first, second, third], [receipt["pid"] for receipt in receipts])
        self.assertEqual([0, -signal.SIGKILL, 0], [receipt["exitCode"] for receipt in receipts])
        self.assertTrue(all(receipt["origin"] == self.origin and receipt["runtimeCacheFiles"] == {"NUGET_PACKAGES": [], "NUGET_HTTP_CACHE_PATH": []}
                            for receipt in receipts))
        self.assertTrue(all((self.root / name).is_file() for name in ("backend-runtime.log", "backend-runtime-g2.log", "backend-runtime-g3.log")))
        for receipt in receipts:
            with self.assertRaises(ProcessLookupError):
                os.kill(receipt["pid"], 0)

    def test_an_unclean_graceful_stop_refuses_the_restart_and_starts_no_new_generation(self):
        with self.assertRaisesRegex(RuntimeError, "exited unsuccessfully"):
            with runtime.RestartableServer(self.run, "backend", self.origin) as host:
                (self.published / "exit-code").write_text("3")
                host.restart("unclean")
        self.assertFalse((self.root / "backend-process-g2.json").exists())
        self.assertEqual(3, json.loads((self.root / "backend-process.json").read_text())["exitCode"])

    def test_two_immutable_publishes_share_content_only_across_a_closed_generation(self):
        content = self.root / "fresh-runtime-content"
        content.mkdir(mode=0o700)
        (content / "fixture-state").write_text("fresh owned state")
        second = self.root / "second-published"
        second.mkdir()
        (second / "host.py").write_text(self.HOST + "\n# independently published second generation\n")
        before = {directory: {path.name: runtime.sha(path) for path in directory.iterdir()} for directory in (self.published, second)}
        self.run.profiles["backend"]["contentRoot"] = content

        def switch():
            with self.assertRaises(OSError):
                runtime.Http(self.origin).request("/health/live")
            self.run.profiles["backend"]["published"] = second
            return {"publishedDirectory": str(second)}

        with runtime.RestartableServer(self.run, "backend", self.origin) as host:
            first_pid = self.pid()
            transition = host.restart("switch normal binary publish", while_stopped=switch)
            self.assertNotEqual(first_pid, self.pid())
        self.assertEqual("fresh owned state", (content / "fixture-state").read_text())
        self.assertEqual(before, {directory: {path.name: runtime.sha(path) for path in directory.iterdir()} for directory in before})
        old, new = transition["stoppedProcess"], transition["startedProcess"]
        self.assertEqual((str(self.published), str(second)), (old["publishedDirectory"], new["publishedDirectory"]))
        self.assertEqual(str(content), old["contentRoot"])
        self.assertEqual((old["contentRoot"], old["contentRootIdentity"]), (new["contentRoot"], new["contentRootIdentity"]))
        self.assertNotEqual(old["assemblySha256"], new["assemblySha256"])
        self.assertEqual(0, old["exitCode"])
        self.assertIs(True, old["inputsUnchangedAtCloseout"])
        final = json.loads((self.root / "backend-process-g2.json").read_text())
        self.assertEqual((0, new["publishedDirectory"], new["contentRootIdentity"]),
                         (final["exitCode"], final["publishedDirectory"], final["contentRootIdentity"]))
        self.assertIs(True, final["inputsUnchangedAtCloseout"])

    def test_a_failure_inside_a_phase_keeps_its_cause_and_still_stops_the_running_generation(self):
        original = RuntimeError("Original restart-phase failure")
        with self.assertRaises(RuntimeError) as raised:
            with runtime.RestartableServer(self.run, "backend", self.origin) as host:
                host.restart("phase")
                raise original
        self.assertIs(original, raised.exception)
        final = json.loads((self.root / "backend-process-g2.json").read_text())
        self.assertEqual(0, final["exitCode"])
        with self.assertRaises(ProcessLookupError):
            os.kill(final["pid"], 0)


if __name__ == "__main__":
    unittest.main()
