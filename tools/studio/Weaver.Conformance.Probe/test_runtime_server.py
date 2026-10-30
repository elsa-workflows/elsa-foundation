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
    def test_pending_startup_keeps_the_same_process_until_it_can_confirm_readiness(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            published = root / "published"
            published.mkdir()
            release = published / "release"
            (published / "host.py").write_text('''import argparse, time, os
from pathlib import Path
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse
p=argparse.ArgumentParser();p.add_argument('--contentRoot');p.add_argument('--urls');a=p.parse_args()
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


if __name__ == "__main__":
    unittest.main()
