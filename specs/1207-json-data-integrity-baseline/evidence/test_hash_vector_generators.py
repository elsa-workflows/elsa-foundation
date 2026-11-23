"""Negative controls for the independent evidence generators: python3 -m unittest discover -s <this directory>."""
from pathlib import Path
import importlib.util
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


if __name__ == '__main__':
    unittest.main()
