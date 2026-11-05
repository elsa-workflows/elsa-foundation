"""Regression coverage for the review finding: padded base64 keys must be redacted."""
import base64
import json
import pathlib
import secrets
import tempfile
import unittest
from unittest.mock import patch
from exercise import Proof


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


if __name__ == '__main__':
    unittest.main()
