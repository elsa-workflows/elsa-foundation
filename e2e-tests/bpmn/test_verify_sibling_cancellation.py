"""Rehashed actual-store controls for Event sibling cancellation and source ACK recovery."""
from contextlib import closing
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sqlite3
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('sibling', Path(__file__).with_name('verify_sibling_cancellation.py'))
s = importlib.util.module_from_spec(spec); spec.loader.exec_module(s); v = s.v
EVIDENCE = Path(__file__).resolve().parents[2] / 'specs/1017-direct-bpmn-hosting/evidence/events'


class SiblingCancellationControls(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='elsa-sibling-control-'); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / 'sibling-cancellation'; shutil.copytree(EVIDENCE / 'sibling-cancellation', self.root)
        shutil.copytree(EVIDENCE / 'model-broadcast/empty-cohort', Path(self.temp.name) / 'model-broadcast/empty-cohort')

    def rehash(self, name):
        path = self.root / 'manifest.json'; body = json.loads(path.read_text())
        next(a for a in body['assets'] if a['path'] == name)['sha256'] = v.digest(self.root / name); path.write_text(json.dumps(body))

    def change(self, name, action):
        path = self.root / name; body = json.loads(path.read_text()); action(body); path.write_text(json.dumps(body)); self.rehash(name)

    def store(self, phase, action):
        name = phase + '-runtime.sqlite.gz'; path = self.root / 'fixed' / name; raw = Path(self.temp.name) / 'db'; raw.write_bytes(gzip.decompress(path.read_bytes()))
        with closing(sqlite3.connect(raw)) as connection: action(connection); connection.commit()
        path.write_bytes(gzip.compress(raw.read_bytes())); self.rehash('fixed/' + name)
        def update(body):
            entry = next(e for e in body['stores'] if e['file'] == name)
            entry.update(databaseSha256=hashlib.sha256(raw.read_bytes()).hexdigest(), gzipSha256=v.digest(path))
        self.change('fixed/process.json', update)

    def verify(self): return s.verify(self.root / 'manifest.json')

    def test_actual_orphan_fixed_with_original_source_ack_recovery(self):
        result = self.verify(); self.assertEqual(6, result['wholeStores']); self.assertEqual(3, result['ownedHosts'])
        self.assertTrue(result['originalOrphanReproduced']); self.assertTrue(result['postConsumptionPreAckRecoveryProved'])
        self.assertFalse(result['pendingModelSettlementCrashProved']); self.assertFalse(result['finalUpgradeAndStudioProved'])

    def test_the_original_orphan_cannot_be_hidden(self):
        self.change('red/after.json', lambda p: p['native'][1]['instance'].update(status='Cancelled'))
        with self.assertRaisesRegex(ValueError, 'original confirmed orphan'): self.verify()

    def test_graceful_exit_cannot_replace_process_loss(self):
        self.change('fixed/process.json', lambda p: p['exits'].__setitem__(0, 0))
        with self.assertRaisesRegex(ValueError, 'process-loss'): self.verify()

    def test_foreign_products_cannot_qualify_the_fix(self):
        self.change('fixed/process.json', lambda p: p['binaries'][0].update(productVersion='1.0.0+' + '0' * 40))
        with self.assertRaisesRegex(ValueError, 'source or process-loss'): self.verify()

    def test_the_model_must_enter_pending_sibling_settlement(self):
        def change(connection):
            for identity, raw in connection.execute('SELECT InputId,ContentJson FROM elsa_runtime_model_input'):
                body = json.loads(raw)
                if body['acceptedRevision'] != 2: continue
                body['result']['lifecycle'] = 'TerminalSettled'
                connection.execute('UPDATE elsa_runtime_model_input SET ContentJson=? WHERE InputId=?', (json.dumps(body), identity))
        self.store('finished', change)
        with self.assertRaisesRegex(ValueError, 'skipped durable pending'): self.verify()

    def test_a_sibling_obligation_cannot_remain_unsettled(self):
        def change(connection):
            body = json.loads(connection.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchone()[0]); body['payload']['cancellations'][0]['settled'] = False
            connection.execute('UPDATE elsa_runtime_model_execution SET ContentJson=?', (json.dumps(body),))
        self.store('finished', change)
        with self.assertRaisesRegex(ValueError, 'sibling cancellation obligation'): self.verify()

    def test_the_real_pre_ack_cut_cannot_be_relabelled_delivered(self):
        def change(connection):
            model = json.loads(connection.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchone()[0]); identity = v.key(model['payload']['cancellations'][0]['sourceOutboxItemId'])
            connection.execute('UPDATE elsa_runtime_post_commit_outbox SET Status=2 WHERE OutboxItemId=?', (identity,))
        self.store('finished', change)
        with self.assertRaisesRegex(ValueError, 'pre-ACK cut'): self.verify()

    def test_final_ack_must_follow_exact_consumption(self):
        def change(connection):
            model = json.loads(connection.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchone()[0]); identity = v.key(model['payload']['cancellations'][0]['sourceOutboxItemId'])
            body = json.loads(connection.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (identity,)).fetchone()[0]); body['deliveredAt'] = '2000-01-01T00:00:00Z'
            connection.execute('UPDATE elsa_runtime_post_commit_outbox SET ContentJson=? WHERE OutboxItemId=?', (json.dumps(body), identity))
        self.store('retained', change)
        with self.assertRaisesRegex(ValueError, 'final acknowledgment'): self.verify()

    def test_a_sibling_cannot_return_to_running(self):
        def change(connection):
            model = json.loads(connection.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchone()[0]); identity = v.key(model['payload']['cancellations'][0]['origin']['workerExecutionId'])
            connection.execute('UPDATE elsa_runtime_workflow_execution_state SET Status=1 WHERE WorkflowExecutionId=?', (identity,))
        self.store('retained', change)
        with self.assertRaisesRegex(ValueError, 'native worker lost'): self.verify()

    def test_the_exact_original_native_pin_must_survive(self):
        def change(connection):
            for identity, raw in connection.execute('SELECT WorkflowExecutionId,ContentJson FROM elsa_runtime_workflow_execution_state'):
                body = json.loads(raw)
                if body.get('modelWorkOrigin') is None: continue
                body['pinnedExecutable']['artifactHash'] = v.key('other')
                connection.execute('UPDATE elsa_runtime_workflow_execution_state SET ContentJson=? WHERE WorkflowExecutionId=?', (json.dumps(body), identity)); break
        self.store('waiting', change)
        with self.assertRaisesRegex(ValueError, 'native worker lost'): self.verify()


if __name__ == '__main__': unittest.main()
