from pathlib import Path
import hashlib
import importlib.util
import json
import unittest

spec = importlib.util.spec_from_file_location('s13_hashes', Path(__file__).with_name('generate-s13-integration-hash-vectors.py'))
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)


class S13IntegrationVectorsTests(unittest.TestCase):
    def test_independent_preimages_and_all_checked_in_pins_reproduce(self):
        rows = generator.generate()
        self.assertEqual(rows, json.loads(Path(__file__).with_name('s13-integration-hash-vectors.json').read_text()), 'First-stable hash freeze requires an explicit migration decision')
        for row in rows:
            self.assertEqual(row['sha256'], hashlib.sha256(row['canonical'].encode()).hexdigest())
            for component in ['contract', 'revision']:
                if component in row:
                    self.assertEqual(row[component]['sha256'], hashlib.sha256(row[component]['canonical'].encode()).hexdigest())
            if 'nodeFrame' in row:
                self.assertEqual(row['nodeSha256'], hashlib.sha256(row['nodeFrame'].encode()).hexdigest())

    def test_all_capability_combinations_and_each_ordinary_flag_branch_remain_distinct(self):
        rows = generator.generate()
        masks = [row for row in rows if row['family'] == 'capability']
        for selector in [lambda row: row['sha256'], lambda row: row['nodeSha256'], lambda row: row['contract']['sha256']]:
            self.assertEqual(8, len({selector(row) for row in masks}))
        for key in ['accessesWorkflowState', 'runtime.mayWriteEveryVisibleVariable']:
            flags = [row for row in rows if row['family'] == 'flag' and row['key'] == key]
            self.assertEqual(3, len({row['sha256'] for row in flags}))
            self.assertEqual(3, len({row['nodeSha256'] for row in flags}))
        self.assertEqual(8, len({row['sha256'] for row in rows if row['family'] == 'template-capability'}))

    def test_exact_json_and_opaque_UTF16_causal_controls(self):
        rows = generator.generate()
        durable = {row['name']: row['sha256'] for row in rows if row['family'] == 'durable-checkpoint'}
        self.assertEqual(durable['spelling-a'], durable['spelling-b'])
        for left, right in [('empty', 'present-null'), ('array-forward', 'array-reverse'), ('large-a', 'large-b'), ('none', 'inline-null'), ('inline-null', 'external'), ('opaque-a', 'opaque-b')]:
            self.assertNotEqual(durable[left], durable[right])
        metadata = {row['name']: row for row in rows if row['family'] == 'activity-metadata'}
        for selector in [lambda row: row['sha256'], lambda row: row['revision']['sha256']]:
            for left, right in [('effects-a', 'effects-b'), ('receipts-a', 'receipts-b')]:
                self.assertEqual(selector(metadata[left]), selector(metadata[right]))
            for left, right in [('effects-a', 'effects-order'), ('effects-null', 'effects-missing'), ('receipts-a', 'receipts-number'), ('receipts-null', 'receipts-missing'), ('ordinary-a', 'ordinary-b')]:
                self.assertNotEqual(selector(metadata[left]), selector(metadata[right]))

    def test_template_root_and_child_owner_protocols_leave_data_strings_exact(self):
        rows = generator.generate()
        for scope in ['root', 'child']:
            templates = {row['name']: row['sha256'] for row in rows if row['family'] == 'template-metadata' and row['scope'] == scope}
            for left, right in [('effects-a', 'effects-b'), ('variables-a', 'variables-b')]:
                self.assertEqual(templates[left], templates[right])
            for left, right in [('effects-a', 'effects-changed'), ('effects-null', 'effects-missing'), ('variables-order-a', 'variables-order-b'), ('ordinary-a', 'ordinary-b')]:
                self.assertNotEqual(templates[left], templates[right])
        self.assertEqual(2, len({row['sha256'] for row in rows if row['family'] == 'template-data-string'}))


if __name__ == '__main__':
    unittest.main()
