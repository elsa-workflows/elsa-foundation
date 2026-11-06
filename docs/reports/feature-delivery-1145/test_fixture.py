"""Regression coverage for the review finding: padded base64 keys must be redacted."""
import base64
import json
import pathlib
import secrets
import tempfile
import unittest
from unittest.mock import patch
from exercise import Proof
from run import check_remote_inventory, stop_owned_host
from types import SimpleNamespace


class SnapshotRedactionTests(unittest.TestCase):
    def test_padded_key_and_unpadded_credential_are_removed_from_saved_log(self):
        padded_key = base64.b64encode(secrets.token_bytes(32)).decode()
        credential = secrets.token_urlsafe(32)
        self.assertTrue(padded_key.endswith('='))
        state = dict(Status='running', Running=True, Pid=123, StartedAt='fixture', ExitCode=0)
        with tempfile.TemporaryDirectory(prefix='fr2-redaction-') as directory:
            proof = Proof.__new__(Proof)
            proof.root = pathlib.Path(directory)
            (proof.root / 'receipts').mkdir()
            proof.host = dict(name='owned-fixture')
            proof.env = dict(key=padded_key, credential=credential)
            with patch('exercise.subprocess.check_output', side_effect=[json.dumps(state), f'key={padded_key}; credential={credential}']):
                proof.snapshot()
            saved = (proof.root / 'receipts/host.log').read_text()
            self.assertNotIn(padded_key, saved)
            self.assertNotIn(credential, saved)
            self.assertEqual('key=<fixture-secret>; credential=<fixture-secret>', saved)


class CleanupTests(unittest.TestCase):
    def test_owned_container_is_stopped_when_diagnostic_snapshot_fails(self):
        with tempfile.TemporaryDirectory(prefix='fr2-cleanup-') as directory:
            proof = SimpleNamespace(host=dict(name='owned-fixture'))
            with patch.object(proof, 'snapshot', side_effect=OSError('injected disk full'), create=True), patch('run.subprocess.run') as stop:
                with self.assertRaisesRegex(OSError, 'injected disk full'):
                    stop_owned_host(proof, pathlib.Path(directory))
                self.assertEqual(['docker', 'stop', 'owned-fixture'], stop.call_args.args[0])
                self.assertTrue(stop.call_args.kwargs['check'])


class RemotePinTests(unittest.TestCase):
    def test_extra_runtime_dependency_is_refused_until_explicitly_pinned(self):
        packages = [dict(id='Root', version='1.0.0', feed='elsa-4'), dict(id='System.Text.Json', version='8.0.5', feed='nuget.org')]
        pins = {'root': dict(version='1.0.0', feed='elsa-4')}
        with self.assertRaises(AssertionError):
            check_remote_inventory(packages, pins)
        pins['system.text.json'] = dict(version='8.0.5', feed='nuget.org')
        check_remote_inventory(packages, pins)
        pins['system.text.json'] = dict(version='8.0.4', feed='nuget.org')
        with self.assertRaises(AssertionError):
            check_remote_inventory(packages, pins)


if __name__ == '__main__':
    unittest.main()
