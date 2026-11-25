"""Positive and corruption controls for the separate current-writer format-2 material golden."""
from pathlib import Path
import copy
import importlib.util
import json
import unittest

directory = Path(__file__).parent
spec = importlib.util.spec_from_file_location('v2_packager', directory / 'record-current-bpmn-v2.py')
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)


class MaterialBindingTests(unittest.TestCase):
    def setUp(self):
        self.data = (directory / 'fixtures/bpmn/current-model-v2.json').read_bytes()

    def test_retained_publication_matches_the_independent_full_preimage_and_pin(self):
        report, canonical, expected = packager.verify_capture(self.data)
        golden = json.loads((directory / 'fixtures/bpmn/current-model-v2.golden.json').read_bytes())
        self.assertEqual(golden['canonical'], canonical)
        self.assertEqual(golden['expectedContentHash'], expected)
        self.assertEqual(golden['writerSourceSha'], report['captureProvenance']['sourceSha'])

    def test_changed_behavior_dependency_or_stale_source_cannot_borrow_the_capture_receipt(self):
        original = json.loads(self.data)
        changes = [
            lambda value: value['definition']['process'].update(processId='different'),
            lambda value: value['dependencies'][0]['executable'].update(artifactHash='sha256:' + '0' * 64),
            lambda value: value['captureProvenance'].update(sourceSha='0' * 40),
            lambda value: value['captureProvenance'].update(sourceDirty=True),
            lambda value: value['definition']['serviceBindings'][0].update(faultPolicy=None),
            lambda value: value.update(inputContract=dict(version=1, inputs=[dict(name='uncovered')])),
        ]
        for mutate in changes:
            changed = copy.deepcopy(original)
            mutate(changed)
            with self.subTest(changed=changed), self.assertRaises(ValueError):
                packager.verify_capture(json.dumps(changed).encode())


if __name__ == '__main__':
    unittest.main()
