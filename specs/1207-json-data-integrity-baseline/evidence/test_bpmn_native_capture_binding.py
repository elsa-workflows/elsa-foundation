"""Negative controls for actual current native capture provenance."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('native_capture', Path(__file__).with_name('record-current-bpmn-native-fixtures.py'))
capture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(capture)


class NativeCaptureBindingTests(unittest.TestCase):
    source = '1' * 40

    def process(self):
        return {'products': {'sourceCommit': self.source, 'actualProductVersions': [
            {'name': name, 'productVersion': '1.0.0+' + self.source, 'sha256': '2' * 64} for name in sorted(capture.WRITERS)]}}

    def test_complete_pinned_inventory_is_required(self):
        self.assertEqual(14, len(capture.writers(self.process(), self.source)))
        for name in capture.WRITERS:
            process = self.process()
            process['products']['actualProductVersions'] = [row for row in process['products']['actualProductVersions'] if row['name'] != name]
            with self.subTest(name=name), self.assertRaises(ValueError):
                capture.writers(process, self.source)

    def test_each_writer_and_source_must_be_exact(self):
        for member, value in [('productVersion', '1.0.0+' + '3' * 40), ('sha256', 'not-a-digest')]:
            process = self.process()
            process['products']['actualProductVersions'][0][member] = value
            with self.subTest(member=member), self.assertRaises(ValueError):
                capture.writers(process, self.source)
        with self.assertRaises(ValueError):
            capture.writers(self.process(), '4' * 40)


if __name__ == '__main__':
    unittest.main()
