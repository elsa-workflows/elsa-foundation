"""Exercise retained real cuts and causal negative controls without editing a captured original."""
import copy
from contextlib import closing
import gzip
import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest

_spec = importlib.util.spec_from_file_location('prior_capture', Path(__file__).with_name('record-current-bpmn-prior-cancellation.py'))
packager = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(packager)


class PriorCaptureBindingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)
        fixtures = Path(__file__).parent / 'fixtures/bpmn/current-prior-cancellation'
        self.reports, self.stores, self.databases = [], [], []
        for stage in packager.STAGES:
            compressed = (fixtures / (stage + '.sqlite.gz')).read_bytes()
            manifest = json.loads((fixtures / (stage + '.json')).read_bytes())
            data = gzip.decompress(compressed)
            self.assertEqual(manifest['compressedSha256'], packager.sha(compressed))
            self.assertEqual(manifest['databaseSha256'], packager.sha(data))
            report_bytes = (fixtures / (stage + '.capture.json')).read_bytes()
            self.assertEqual(manifest['captureReportSha256'], packager.sha(report_bytes))
            report = json.loads(report_bytes)
            packager.fault.verify(data, report, packager.WRITERS)
            database = self.folder / (stage + '.sqlite')
            database.write_bytes(data)
            store, counts = packager.inspect(database, report)
            self.assertEqual(manifest['rowCounts'], counts)
            self.reports.append(report)
            self.stores.append(store)
            self.databases.append(database)

    def test_actual_current_cuts_keep_physical_custody_and_claim_expiry(self):
        packager.verify_history(self.reports, self.stores)

    def test_rehashed_histories_cannot_replace_original_debt_result_or_claim(self):
        mutations = [
            lambda r, s: r[1]['observations'].update(source='replacement'),
            lambda r, s: r[1]['state'].update(revision=99),
            lambda r, s: r[1]['modelQueue'][0].update(commandId='replacement'),
            lambda r, s: s[1]['inputs'].update({r[0]['modelQueue'][0]['commandId']: {'result': {'status': 'Accepted'}}}),
            lambda r, s: s[1]['outbox'][r[0]['observations']['source']].update(status='Delivered', deliveredAt='2026-01-01T00:00:00Z'),
            lambda r, s: s[1]['queue'][0].update(ClaimToken=0),
            lambda r, s: s[1]['queue'][0].update(VisibleAfterUtcTicks=s[1]['queue'][0]['ClaimedAtUtcTicks'] + 1),
            lambda r, s: s[2]['inputs'][r[0]['modelQueue'][0]['commandId']].update(acceptedAt='2026-01-01T00:00:00Z'),
            lambda r, s: r[2]['observations']['innerState']['payload']['cancellations'][0].update(settled=False),
            lambda r, s: r[2]['state']['payload']['nativeResults'][0]['origin'].update(attemptId='replacement'),
            lambda r, s: s[0]['outbox'][r[0]['observations']['source']]['intent'].update(kind=packager.encoded('Test.NotCancellation')),
            lambda r, s: s[1]['outbox'][r[0]['observations']['source']]['intent'].update(workflowExecutionId=packager.encoded('unrelated-native')),
            lambda r, s: s[2]['outbox'][r[0]['observations']['source']]['intent']['payload'].update(executionId='u16:' + packager.encoded('unrelated-model')),
            lambda r, s: s[2]['inputs'][r[0]['modelQueue'][0]['commandId']].update(invocationId=packager.encoded('unrelated')),
            lambda r, s: s[2]['inputs'][r[0]['modelQueue'][0]['commandId']].update(attemptId=packager.encoded('unrelated')),
            lambda r, s: s[2]['inputs'][r[0]['modelQueue'][0]['commandId']].update(inputKind=packager.encoded('start')),
            lambda r, s: s[2]['inputs'][r[0]['modelQueue'][0]['commandId']].update(acceptedRevision=99),
            lambda r, s: s[2]['inputs'][r[0]['modelQueue'][0]['commandId']].update(contentFingerprint=packager.encoded('unrelated')),
        ]
        for index, mutation in enumerate(mutations):
            reports, stores = copy.deepcopy(self.reports), copy.deepcopy(self.stores)
            mutation(reports, stores)
            with self.subTest(mutation=index), self.assertRaises(ValueError):
                packager.verify_history(reports, stores)

    def test_rehashed_report_cannot_rebind_cancellation_kind_or_owner(self):
        for field, value in [('kind', 'Test.NotCancellation'), ('workflowExecutionId', 'unrelated-native')]:
            report = copy.deepcopy(self.reports[0])
            report['observations']['cancellation']['intent'][field] = value
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, 'Observed cancellation intent differs'):
                packager.inspect(self.databases[0], report)

    def test_rehashed_physical_payload_cannot_borrow_the_report(self):
        database = self.databases[0]
        with closing(sqlite3.connect(database)) as connection, connection:
            row_id, payload = connection.execute('SELECT Id, ContentJson FROM elsa_runtime_model_execution LIMIT 1').fetchone()
            state = json.loads(payload)
            state['payload']['formatVersion'] = 999
            connection.execute('UPDATE elsa_runtime_model_execution SET ContentJson=? WHERE Id=?', (json.dumps(state), row_id))
        report = copy.deepcopy(self.reports[0])
        data = database.read_bytes()
        report['captureProvenance'].update(databaseSha256=packager.sha(data), databaseByteLength=len(data))
        packager.fault.verify(data, report, packager.WRITERS)
        with self.assertRaisesRegex(ValueError, 'Physical model state differs'):
            packager.inspect(database, report)


if __name__ == '__main__':
    unittest.main()
