"""Mechanical ownership/cleanup controls; no workflow or lease qualification."""
import json
import os
from pathlib import Path
import shlex
import signal
import subprocess
import sys
import tempfile
import time
import unittest


@unittest.skipUnless(hasattr(signal, 'SIGSTOP'), 'Unix process cut')
class OwnedProbeBrokerTests(unittest.TestCase):
    def run_case(self, suspend, stop_by_signal):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            executable = root / 'dotnet'
            executable.write_text('#!/bin/sh\nexec ' + shlex.quote(sys.executable) + ' -c ' +
                                  shlex.quote('import time; time.sleep(120)') + ' "$@"\n')
            executable.chmod(0o700)
            environment = root / 'environment.json'
            environment.write_text(json.dumps({'PATH': str(root) + os.pathsep + os.environ['PATH']}))
            manifest = root / 'owned.json'
            stop_file = root / 'stop'
            broker = subprocess.Popen([sys.executable, str(Path(__file__).with_name('run_owned_broadcast_probe.py')),
                '--assembly', str(root / 'probe.dll'), '--content-root', str(root), '--base-url', 'http://127.0.0.1:1',
                '--environment-file', str(environment), '--manifest', str(manifest), '--stop-file', str(stop_file)],
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            bystander = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(120)'])
            child_id = None
            try:
                deadline = time.monotonic() + 10
                while not manifest.exists() and broker.poll() is None and time.monotonic() < deadline:
                    time.sleep(0.025)
                self.assertTrue(manifest.exists(), 'The broker failed to register its owned executable.')
                owned = json.loads(manifest.read_text())
                self.assertEqual(broker.pid, owned['controllerProcessId'])
                child_id = owned['processId']
                if suspend:
                    os.kill(child_id, signal.SIGSTOP)
                if stop_by_signal:
                    broker.terminate()
                else:
                    stop_file.touch()
                stdout, stderr = broker.communicate(timeout=10)
                self.assertEqual(0, broker.returncode, stdout + stderr)
                with self.assertRaises(ProcessLookupError):
                    os.kill(child_id, 0)
                self.assertIsNone(bystander.poll(), 'Cleanup touched an unowned process.')
            finally:
                if broker.poll() is None:
                    broker.terminate()
                    broker.communicate(timeout=10)
                bystander.terminate()
                bystander.wait(timeout=5)

    def test_stop_file_reaps_owned_running_child(self):
        self.run_case(suspend=False, stop_by_signal=False)

    def test_termination_reaps_owned_stopped_child(self):
        self.run_case(suspend=True, stop_by_signal=True)


if __name__ == '__main__':
    unittest.main()
