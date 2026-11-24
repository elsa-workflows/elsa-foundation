"""Synthetic receipt controls; none of these inputs represents an execution capture."""
from pathlib import Path
import copy
import importlib.util
import unittest

spec = importlib.util.spec_from_file_location('fault_packager', Path(__file__).with_name('record-current-bpmn-fault-fixtures.py'))
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)


class FaultCaptureBindingTests(unittest.TestCase):
    def history(self):
        origins = [dict(invocationId='call-' + name, attemptId='attempt-' + name, workerExecutionId='worker-' + name,
                        sourceOutboxItemId='start-' + name, resultInputId='result-' + name) for name in ['a', 'b']]
        fault = dict(origin=origins[0], status=4)
        debt = dict(origin=origins[1], sourceOutboxItemId='cancel-b', settled=False)
        state = dict(executionId='model', scope='scope', protocol='bpmn', executable='pin', materialIncarnationId='incarnation', authority='authority')
        workers = [dict(workflowExecutionId=origin['workerExecutionId'], status=4) for origin in origins]
        reports = [dict(state=dict(state, lifecycle=0, revision=1, terminalResult=None, payload=dict(work=[dict(nativeWork=origin) for origin in origins])), native=[]),
                   dict(state=dict(state, lifecycle=2, revision=2, terminalResult=dict(status='Faulted'), payload=dict(state={'semantic': 1}, work=[], nativeResults=[fault], cancellations=[debt])), native=workers),
                   dict(state=dict(state, lifecycle=3, revision=3, terminalResult=dict(status='Faulted'), payload=dict(state={'semantic': 1}, work=[], nativeResults=[fault, dict(origin=origins[1], status=4)], cancellations=[dict(debt, settled=True)])), native=workers)]
        stores = [dict(outbox={name: dict(status='Pending') for name in ['start-a', 'start-b']}, inputs={}),
                  dict(outbox={'cancel-b': dict(status='Pending')}, inputs={}),
                  dict(outbox={name: dict(status='Delivered', deliveredAt='now') for name in ['start-a', 'start-b', 'cancel-b']}, inputs={'result-b': dict(result=dict(status='Accepted'))})]
        return copy.deepcopy(reports), copy.deepcopy(stores)

    def test_rehashed_reports_cannot_replace_physical_fault_debt_or_settlement_histories(self):
        reports, stores = self.history()
        packager.verify_history('concurrent-admitted', reports, stores)
        changes = [
            lambda r, s: r[1]['state']['terminalResult'].update(status='Completed'),
            lambda r, s: r[0]['state']['payload']['work'][1]['nativeWork'].update(attemptId='attempt-a'),
            lambda r, s: r[1]['state']['payload'].update(cancellations=[]),
            lambda r, s: r[2]['state']['payload']['cancellations'][0].update(sourceOutboxItemId='other-source'),
            lambda r, s: r[1].update(native=[]),
            lambda r, s: s[1]['outbox']['cancel-b'].update(status='Delivered', deliveredAt='early'),
            lambda r, s: s[2]['outbox']['start-b'].update(status='Pending', deliveredAt=None),
            lambda r, s: s[2].update(inputs={}),
        ]
        for index, change in enumerate(changes):
            reports, stores = self.history()
            change(reports, stores)
            # Byte/report receipts can be freshly recomputed; they cannot establish the missing history.
            with self.subTest(change=index), self.assertRaises(ValueError):
                packager.verify_history('concurrent-admitted', reports, stores)

    def receipt(self):
        return dict(captureProvenance=dict(version=1, sourceSha='a' * 40, sourceDirty=False,
            databaseSha256=packager.sha(b'fixture'), databaseByteLength=7,
            binaryHashes={name: '0' * 64 for name in packager.WRITERS},
            binaryVersions={name: '4.0.0+' + 'a' * 40 if name.startswith('Elsa.') else '1.0.0' for name in packager.WRITERS}))

    def test_actual_hash_writers_are_required_and_must_identify_the_capture_source(self):
        packager.verify(b'fixture', self.receipt())
        for name in ['Elsa.Serialization.Core.dll', 'Elsa.Activities.Runtime.Core.dll']:
            missing = self.receipt()
            del missing['captureProvenance']['binaryHashes'][name]
            with self.subTest(name=name, condition='missing'), self.assertRaises(ValueError):
                packager.verify(b'fixture', missing)
            stale = self.receipt()
            stale['captureProvenance']['binaryVersions'][name] = '4.0.0+' + 'b' * 40
            with self.subTest(name=name, condition='stale'), self.assertRaises(ValueError):
                packager.verify(b'fixture', stale)

    def test_changed_store_dirty_source_or_invalid_binary_digest_cannot_borrow_a_receipt(self):
        with self.assertRaises(ValueError):
            packager.verify(b'changed', self.receipt())
        for field, value in [('sourceDirty', True), ('sourceSha', 'not-a-commit'), ('databaseByteLength', 8)]:
            receipt = self.receipt()
            receipt['captureProvenance'][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                packager.verify(b'fixture', receipt)
        receipt = copy.deepcopy(self.receipt())
        receipt['captureProvenance']['binaryHashes']['Elsa.Serialization.Core.dll'] = 'wrong'
        with self.assertRaises(ValueError):
            packager.verify(b'fixture', receipt)


if __name__ == '__main__':
    unittest.main()
