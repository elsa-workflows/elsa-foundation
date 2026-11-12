"""Capture-file controls; these do not establish runtime recovery semantics."""
import hashlib
import importlib.util
from pathlib import Path
import sqlite3
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('broadcast_capture', Path(__file__).with_name('inspect_broadcast_recovery.py'))
capture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(capture)


class RetainedSnapshotTests(unittest.TestCase):
    def fixture(self, root):
        source = root / 'source.sqlite'
        with sqlite3.connect(source) as database:
            database.execute('create table facts(value text)')
            database.execute("insert into facts values ('committed')")
        return source

    def test_retains_exact_checked_reader_bytes_and_relative_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); source = self.fixture(root); target = root / 'retained.sqlite'
            observed = {}
            def read(path, *_args):
                observed['bytes'] = path.read_bytes()
                return {'providerChecked': True}
            with patch.object(capture, 'read_snapshot', side_effect=read):
                result = capture.observe(source, Path('reader.dll'), {}, [], retain=target)
            self.assertEqual(observed['bytes'], target.read_bytes())
            self.assertEqual('retained.sqlite', result['retainedSnapshot']['file'])
            self.assertEqual(hashlib.sha256(observed['bytes']).hexdigest(), result['retainedSnapshot']['sha256'])

    def test_never_overwrites_or_removes_an_existing_capture(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); source = self.fixture(root); target = root / 'retained.sqlite'
            target.write_bytes(b'previous capture')
            with self.assertRaises(FileExistsError):
                capture.retain_snapshot(source, target)
            self.assertEqual(b'previous capture', target.read_bytes())

    def test_refused_provider_input_is_not_retained(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); source = self.fixture(root); target = root / 'retained.sqlite'
            with patch.object(capture, 'read_snapshot', side_effect=ValueError('provider refused')):
                with self.assertRaisesRegex(ValueError, 'provider refused'):
                    capture.observe(source, Path('reader.dll'), {}, [], retain=target)
            self.assertFalse(target.exists())

    def test_interrupted_copy_does_not_leave_a_positive_snapshot(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); source = self.fixture(root); target = root / 'retained.sqlite'
            with patch.object(capture.shutil, 'copyfileobj', side_effect=OSError('interrupted')):
                with self.assertRaisesRegex(OSError, 'interrupted'):
                    capture.retain_snapshot(source, target)
            self.assertFalse(target.exists())

    def test_authentication_and_secret_tables_are_refused_without_sanitization(self):
        for name in ['identity_users', 'identity_credentials', 'OpenIddictTokens', 'elsa_secrets']:
            with self.subTest(table=name), tempfile.TemporaryDirectory() as directory:
                root = Path(directory); source = self.fixture(root); target = root / 'retained.sqlite'
                with sqlite3.connect(source) as database:
                    database.execute(f'create table {name}(value text)')
                original = source.read_bytes()
                with self.assertRaisesRegex(ValueError, 'Isolate authentication'):
                    capture.retain_snapshot(source, target)
                self.assertFalse(target.exists())
                self.assertEqual(original, source.read_bytes())


if __name__ == '__main__':
    unittest.main()
