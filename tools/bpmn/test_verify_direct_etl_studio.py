"""Mutation controls against actual closed-store ETL recovery evidence supplied explicitly."""
import json
import os
from pathlib import Path
import shutil
import tempfile
import unittest
import verify_direct_etl_studio as verifier


@unittest.skipUnless(os.environ.get('ELSA_DIRECT_ETL_EVIDENCE'), 'Supply the actual owned recovery evidence directory.')
class DirectEtlPhysicalEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.original = Path(os.environ['ELSA_DIRECT_ETL_EVIDENCE'])
        self.temporary = tempfile.TemporaryDirectory(prefix='elsa-etl-evidence-control-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / 'case'
        shutil.copytree(self.original, self.root)

    def test_actual_evidence_passes(self):
        result = verifier.verify(self.root)
        self.assertEqual(result['wholeStoresVerified'], 24)
        self.assertEqual(result['retryAcceptedInputs'], 1)

    def test_refuses_different_cut_request_identity(self):
        path = self.root / 'accepted-retry-response-cut.json'
        value = json.loads(path.read_text()); value['request']['requestId'] = 'another-request'
        path.write_text(json.dumps(value))
        with self.assertRaisesRegex(ValueError, 'real accepted response'):
            verifier.verify(self.root)

    def test_refuses_a_changed_closed_store(self):
        path = self.root / 'held-before-restart-etl-runtime.sqlite.gz'
        path.write_bytes(path.read_bytes() + b'changed')
        with self.assertRaisesRegex(ValueError, 'captured store differs'):
            verifier.verify(self.root)

    def test_refuses_changed_physical_output(self):
        manifest = json.loads((self.root / 'finished-etl-files.json').read_text())
        item = next(item for item in manifest['files'] if item['file'].startswith('main-output/'))
        (self.root / 'finished-etl-files' / item['file']).write_text('{"name":"Changed"}\n')
        with self.assertRaisesRegex(ValueError, 'physical destination'):
            verifier.verify(self.root)
