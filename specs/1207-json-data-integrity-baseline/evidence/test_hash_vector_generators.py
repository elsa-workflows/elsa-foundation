"""Negative controls for the independent evidence generators: python3 -m unittest discover -s <this directory>."""
from pathlib import Path
import hashlib
import importlib.util
import json
import unittest

spec = importlib.util.spec_from_file_location('canonical_fixture', Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


class CanonicalFixtureTests(unittest.TestCase):
    def test_unsupported_duplicate_names_and_unpaired_raw_surrogates_are_refused(self):
        for raw in ['{"x":1,"x":2}', '{"x":1,"\\u0078":2}', '"\\ud800"', '{"\\udfff":1}']:
            with self.subTest(raw=raw), self.assertRaises(ValueError):
                helper.load_json(raw)

    def test_valid_surrogate_pair_and_unbounded_exact_number_remain_supported(self):
        self.assertEqual('"\\uD800\\uDC00"', helper.canonical(helper.load_json('"\\ud800\\udc00"')))
        self.assertEqual('1e400', helper.canonical(helper.load_json('10e399')))

    def test_existing_withheld_plan_pin_keeps_explicit_null_schemas_and_absence_is_a_separate_case(self):
        rows = json.loads(Path(__file__).with_name('variable-value-hash-vectors.json').read_text(encoding='utf-8'))
        explicit = next(row for row in rows if row['name'] == 'withheld-secret-plan')
        absent = next(row for row in rows if row['name'] == 'withheld-secret-plan-absent-schema')
        self.assertEqual('d1ba68a7013509642f21e75cf4f7dffbc6a202366b41e35cb87f9775f33f2e9b', explicit['sha256'])
        self.assertNotEqual(explicit['sha256'], absent['sha256'])
        restored = helper.load_json(absent['canonical'])
        plan = restored['withheld']['conversionPlan']
        self.assertEqual('sha256:396d0ad07cbbf5b8074a8be3c6163e4db086b2b9445730c7d71794be59b22736', plan['fingerprint'])
        for descriptor in [plan['sourceType'], plan['targetType']]:
            self.assertNotIn('schema', descriptor)
            descriptor['schema'] = None
        self.assertEqual(explicit['canonical'], helper.canonical(restored))
        for row in [explicit, absent]:
            self.assertEqual(row['sha256'], hashlib.sha256(row['canonical'].encode()).hexdigest())

    def test_runtime_presence_recipes_reproduce_retained_compiler_preimages(self):
        spec = importlib.util.spec_from_file_location('runtime_presence', Path(__file__).with_name('generate-runtime-schema-presence-inputs.py'))
        generator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(generator)
        rows = json.loads(Path(__file__).with_name('compiler-hash-inputs.json').read_text())
        for expected in generator.generate():
            self.assertEqual(expected, next(row for row in rows if row['name'] == expected['name']))


if __name__ == '__main__':
    unittest.main()
