"""Paired controls for capture-time provenance verification; these are synthetic validator inputs, not captures."""
from pathlib import Path
import copy
import importlib.util
import unittest

spec = importlib.util.spec_from_file_location('capture_packager', Path(__file__).with_name('record-current-bpmn-fixture.py'))
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)


class CaptureBindingTests(unittest.TestCase):
    def receipt(self):
        names = ['Elsa.Activities.Bpmn.Tests.dll', 'Elsa.Activities.Bpmn.dll', 'Elsa.Workflows.Runtime.Core.dll',
                 'Elsa.Workflows.Runtime.dll', 'Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll', 'Bpmn.Model.dll', 'Bpmn.Semantics.dll']
        versions = {name: '2.0.0' for name in names}
        versions[names[0]] = '1.0.0+' + 'a' * 40
        return dict(captureProvenance=dict(version=1, sourceSha='a' * 40, sourceDirty=False,
            databaseSha256=packager.sha(b'fixture'), databaseByteLength=7,
            binaryHashes={name: '0' * 64 for name in names}, binaryVersions=versions))

    def test_matching_capture_receipt_passes_but_changed_bytes_or_source_cannot_borrow_it(self):
        receipt = self.receipt()
        packager.verify_capture_binding(b'fixture', receipt)
        with self.assertRaises(ValueError):
            packager.verify_capture_binding(b'changed', receipt)
        changed = copy.deepcopy(receipt)
        changed['captureProvenance']['sourceSha'] = 'b' * 40
        with self.assertRaises(ValueError):
            packager.verify_capture_binding(b'fixture', changed)

    def test_missing_dirty_or_incomplete_provenance_is_refused(self):
        with self.assertRaises(ValueError):
            packager.verify_capture_binding(b'fixture', {})
        for field, value in [('sourceDirty', True), ('databaseByteLength', 8), ('binaryHashes', {})]:
            changed = self.receipt()
            changed['captureProvenance'][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                packager.verify_capture_binding(b'fixture', changed)


if __name__ == '__main__':
    unittest.main()
