"""Independent controls for workflow-design marker pins; no production hash code is loaded."""
from pathlib import Path
import hashlib
import importlib.util
import json
import unittest

spec = importlib.util.spec_from_file_location('design_vectors', Path(__file__).with_name('generate-design-operation-hash-vectors.py'))
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)


class DesignOperationHashVectorsTests(unittest.TestCase):
    def setUp(self):
        self.generated = generator.generate()
        self.vectors = {vector['name']: vector for vector in self.generated['vectors']}

    def test_checked_in_pins_and_preimages_match_the_independent_generator(self):
        self.assertEqual(self.generated, json.loads(Path(__file__).with_name('design-operation-hash-vectors.json').read_text()))
        self.assertEqual(23, len(self.vectors))
        for vector in self.vectors.values():
            with self.subTest(name=vector['name']):
                self.assertEqual(vector['canonical'], generator.helper.canonical(generator.helper.load_json(vector['material'])))
                self.assertEqual(vector['frame'], generator.framed(vector['operationKind'], vector['canonical']))
                self.assertEqual(vector['sha256'], 'sha256:' + hashlib.sha256(vector['frame'].encode()).hexdigest())

    def test_all_seven_state_owners_ignore_only_semantically_equivalent_json_spelling(self):
        for kind, _, _ in generator.projections({}):
            with self.subTest(kind=kind):
                left, right = self.vectors[kind + '-spelling-a'], self.vectors[kind + '-spelling-b']
                self.assertNotEqual(left['material'], right['material'])
                self.assertEqual(left['sha256'], right['sha256'])
        self.assertEqual(7, len({self.vectors[kind + '-spelling-a']['sha256'] for kind, _, _ in generator.projections({})}))

    def test_null_array_order_large_integer_and_ordinary_text_differences_remain_distinct(self):
        for left, right in [('missing', 'null'), ('forward', 'reverse'), ('large-a', 'large-b'), ('text-a', 'text-b')]:
            with self.subTest(left=left, right=right):
                self.assertNotEqual(self.vectors[left]['sha256'], self.vectors[right]['sha256'])


if __name__ == '__main__':
    unittest.main()
