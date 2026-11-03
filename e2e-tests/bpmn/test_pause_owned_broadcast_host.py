"""Process/SQLite pause-helper safety controls only; not source-lease or workflow recovery evidence."""
from datetime import datetime, timezone
import hashlib
import os
from pathlib import Path
import signal
import sqlite3
import subprocess
import sys
import tempfile
import time
import unittest

HELPER = Path(__file__).with_name('pause_owned_broadcast_host.py')


@unittest.skipUnless(hasattr(signal, 'SIGSTOP'), 'Unix signal cut')
class OwnedPauseTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(); self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.databases = [self.root / 'runtime.sqlite', self.root / 'membership.sqlite']
        for path in self.databases:
            with sqlite3.connect(path) as connection:
                connection.execute('create table unchanged(value integer)')
                connection.execute('insert into unchanged values(1)')
        self.process = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)', str(self.root)])
        self.addCleanup(self.stop_owned)
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            self.assembly = subprocess.check_output(['ps', '-p', str(self.process.pid), '-o', 'comm='], text=True).strip()
            command = subprocess.check_output(['ps', '-p', str(self.process.pid), '-o', 'command='], text=True)
            if str(self.root) in command and self.assembly in command: break
            time.sleep(0.025)
        else: self.fail('The owned fixture process did not finish executable startup.')
        birth = subprocess.check_output(['ps', '-p', str(self.process.pid), '-o', 'lstart='], text=True).strip()
        self.start = int(datetime.strptime(birth, '%a %b %d %H:%M:%S %Y').astimezone(timezone.utc).timestamp())

    def stop_owned(self):
        if self.process.poll() is None:
            os.kill(self.process.pid, signal.SIGCONT)
            self.process.terminate(); self.process.wait(timeout=5)

    def arguments(self, assembly=None, root=None, pid=None, expected_start=None, identity_marker=None):
        command = [sys.executable, str(HELPER), '--pid', str(pid or self.process.pid), '--expected-start', str(self.start if expected_start is None else expected_start), '--assembly', assembly or self.assembly,
                   '--content-root', str(root or self.root), '--output', str(self.root / 'pause.json')]
        for database in self.databases: command += ['--database', str(database)]
        if identity_marker: command += ['--identity-marker', str(identity_marker)]
        return command

    def invoke(self, **kwargs):
        return subprocess.run(self.arguments(**kwargs), capture_output=True, text=True, timeout=20)

    def test_owned_pause_releases_both_reservations_without_data_changes(self):
        before = [hashlib.sha256(path.read_bytes()).hexdigest() for path in self.databases]
        result = self.invoke(); self.assertEqual(0, result.returncode, result.stderr)
        state = subprocess.check_output(['ps', '-p', str(self.process.pid), '-o', 'state='], text=True)
        self.assertIn('T', state)
        self.assertEqual(before, [hashlib.sha256(path.read_bytes()).hexdigest() for path in self.databases])
        for database in self.databases:
            with sqlite3.connect(database, timeout=1) as connection:
                connection.execute('begin immediate'); connection.rollback()
                self.assertEqual([(1,)], connection.execute('select value from unchanged').fetchall())

    def test_wrong_content_root_refuses_before_signal(self):
        other = self.root / 'other'; other.mkdir()
        self.assertNotEqual(0, self.invoke(root=other).returncode)
        self.assertIsNone(self.process.poll())
        self.assertNotIn('T', subprocess.check_output(['ps', '-p', str(self.process.pid), '-o', 'state='], text=True))

    def test_wrong_assembly_refuses_before_signal(self):
        self.assertNotEqual(0, self.invoke(assembly='/bin/echo').returncode)
        self.assertIsNone(self.process.poll())
        self.assertNotIn('T', subprocess.check_output(['ps', '-p', str(self.process.pid), '-o', 'state='], text=True))

    def test_invalid_pid_refuses_before_signal(self):
        self.assertNotEqual(0, self.invoke(pid=1).returncode)
        self.assertIsNone(self.process.poll())

    def test_root_path_prefix_is_not_an_exact_owned_argument(self):
        self.assertNotEqual(0, self.invoke(root=self.root.parent).returncode)
        self.assertIsNone(self.process.poll())

    def test_assembly_path_prefix_is_not_an_exact_owned_argument(self):
        self.assertNotEqual(0, self.invoke(assembly=str(Path(self.assembly).parent)).returncode)
        self.assertIsNone(self.process.poll())

    def test_wrong_registered_process_birth_is_refused(self):
        self.assertNotEqual(0, self.invoke(expected_start=self.start - 1).returncode)
        self.assertIsNone(self.process.poll())

    def test_exit_during_reservation_is_rechecked_before_signal(self):
        with sqlite3.connect(self.databases[0]) as locked:
            locked.execute('begin immediate')
            observed = self.root / 'identity-observed.json'
            helper = subprocess.Popen(self.arguments(identity_marker=observed), stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            try:
                deadline = time.monotonic() + 5
                while not observed.exists() and time.monotonic() < deadline: time.sleep(0.01)
                self.assertTrue(observed.exists(), 'The helper did not validate the owned identity before its reservation wait.')
                self.process.terminate(); self.process.wait(timeout=5)
                locked.rollback()
                stdout, stderr = helper.communicate(timeout=20)
                self.assertNotEqual(0, helper.returncode, stdout + stderr)
                self.assertFalse((self.root / 'pause.json').exists())
            finally:
                if helper.poll() is None: helper.kill(); helper.wait(timeout=5)


if __name__ == '__main__':
    unittest.main()
