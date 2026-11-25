"""Independent retained-content pins and canonical equivalence/negative controls."""
from pathlib import Path
import hashlib
import importlib.util
import json
import unittest

spec = importlib.util.spec_from_file_location('import_content', Path(__file__).with_name('generate-elsa3-import-content-hash-vectors.py'))
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)
FREEZE = "JSON hashes freeze at Elsa 4's first stable release; before then regenerate goldens and fixtures together, never add an old-hash verifier."


class ImportContentHashVectorsTests(unittest.TestCase):
    def test_pins_and_exact_preimages_reproduce(self):
        generated = generator.generate()
        self.assertEqual(generated, json.loads(Path(__file__).with_name('elsa3-import-content-hash-vectors.json').read_text()), FREEZE)
        self.assertEqual(22, len(generated['vectors']))
        for vector in generated['vectors']:
            with self.subTest(name=vector['name']):
                self.assertEqual(vector['canonical'], generator.helper.canonical(generator.helper.load_json(vector['material'])), FREEZE)
                self.assertEqual(vector['sha256'], hashlib.sha256(vector['canonical'].encode()).hexdigest(), FREEZE)

    def test_only_equal_JSON_is_equal_in_both_retained_document_kinds(self):
        pins = {vector['name']: vector for vector in generator.generate()['vectors']}
        for kind in ['collection', 'receipt']:
            with self.subTest(kind=kind):
                self.assertNotEqual(pins[kind + '-spelling-a']['material'], pins[kind + '-spelling-b']['material'])
                self.assertEqual(pins[kind + '-spelling-a']['sha256'], pins[kind + '-spelling-b']['sha256'])
                for left, right in [('missing', 'null'), ('forward', 'reverse'), ('large-a', 'large-b'), ('text-a', 'text-b')]:
                    self.assertNotEqual(pins[kind + '-' + left]['sha256'], pins[kind + '-' + right]['sha256'])


if __name__ == '__main__':
    unittest.main()
