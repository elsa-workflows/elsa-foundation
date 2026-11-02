"""Real owned-process observation controls; this fixture is not Studio qualification."""
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import runtime


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


if __name__ == "__main__":
    unittest.main()
