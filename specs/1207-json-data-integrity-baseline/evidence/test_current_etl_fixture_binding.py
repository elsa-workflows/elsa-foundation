"""Causal controls for the complete current-writer ETL fixture witness."""
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest

HERE = Path(__file__).parent
TEMP_ROOT = HERE.parents[2] / '.agent-prefs'
TEMP_ROOT.mkdir(exist_ok=True)
spec = importlib.util.spec_from_file_location('current_etl_fixture', HERE / 'verify-current-etl-fixtures.py')
verifier = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verifier)


class CurrentEtlFixtureBindingTests(unittest.TestCase):
    def test_complete_current_captures_bind_all_stores_and_receiver_effects(self):
        report = verifier.verify(HERE / 'fixtures/etl')
        self.assertEqual(3, len(report['current-v1']['databases']))
        self.assertEqual(1, len(report['current-continuous']['databases']))
        self.assertEqual(30, sum(len(database['hashes']) for family in report.values()
                                 for database in family['databases'].values()))

    def test_a_store_cannot_be_removed_with_its_manifest_entry(self):
        for family, path in [('current-continuous', 'runtime.db'), ('current-v1', 'published/workflow.db')]:
            with self.subTest(family=family), tempfile.TemporaryDirectory(prefix='s9-etl-omit-', dir=TEMP_ROOT) as temporary:
                root = Path(temporary) / 'fixtures'
                shutil.copytree(HERE / 'fixtures/etl', root)
                folder = root / family
                (folder / path).unlink()
                manifest_path = folder / 'manifest.json'
                manifest = json.loads(manifest_path.read_bytes())
                manifest['files'] = [item for item in manifest['files'] if item['path'] != path]
                manifest_path.write_text(json.dumps(manifest), encoding='utf-8')
                with self.assertRaisesRegex(ValueError, 'complete-store database'):
                    verifier.verify(root)

    def test_an_unlisted_physical_file_cannot_escape_the_capture(self):
        with tempfile.TemporaryDirectory(prefix='s9-etl-extra-', dir=TEMP_ROOT) as temporary:
            root = Path(temporary) / 'fixtures'
            shutil.copytree(HERE / 'fixtures/etl', root)
            (root / 'current-continuous/extra-output.json').write_text('{}', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'file inventory'):
                verifier.verify(root)

    def test_rehashed_receiver_body_still_has_to_match_the_retained_effect(self):
        with tempfile.TemporaryDirectory(prefix='s9-etl-body-', dir=TEMP_ROOT) as temporary:
            root = Path(temporary) / 'fixtures'
            shutil.copytree(HERE / 'fixtures/etl', root)
            folder = root / 'current-continuous'
            path = folder / 'deliveries.json'
            deliveries = json.loads(path.read_bytes())
            import base64
            body = base64.b64decode(deliveries[0]['body']).replace(b'Ada', b'Zoe')
            deliveries[0]['body'] = base64.b64encode(body).decode('ascii')
            path.write_text(json.dumps(deliveries), encoding='utf-8')
            manifest_path = folder / 'manifest.json'
            manifest = json.loads(manifest_path.read_bytes())
            for item in manifest['files']:
                if item['path'] == 'deliveries.json':
                    item['byteLength'] = path.stat().st_size
                    item['sha256'] = verifier.sha(path.read_bytes())
            manifest_path.write_text(json.dumps(manifest), encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'business rows'):
                verifier.verify(root)


if __name__ == '__main__':
    unittest.main()
