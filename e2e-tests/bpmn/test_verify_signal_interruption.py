"""Rehashed captured-store controls for actual pending sibling cancellation recovery."""
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

spec = importlib.util.spec_from_file_location('pending_sibling', Path(__file__).with_name('verify_signal_interruption.py'))
s = importlib.util.module_from_spec(spec); spec.loader.exec_module(s); v = s.v
EVIDENCE = Path(__file__).resolve().parents[2] / 'specs/1017-direct-bpmn-hosting/evidence/events/pending-sibling-interruption'


class PendingSiblingControls(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix='elsa-pending-sibling-control-'); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name) / 'evidence'; shutil.copytree(EVIDENCE, self.root)

    def rehash(self, name):
        path = self.root / 'manifest.json'; body = json.loads(path.read_text())
        next(a for a in body['assets'] if a['path'] == name)['sha256'] = v.digest(self.root / name); path.write_text(json.dumps(body))

    def change(self, name, action):
        path = self.root / name; body = json.loads(path.read_text()); action(body); path.write_text(json.dumps(body)); self.rehash(name)

    def store(self, phase, action, parent='direct'):
        name = phase + '-runtime.sqlite.gz'; path = self.root / parent / name; raw = self.root.parent / 'control.sqlite'; raw.write_bytes(gzip.decompress(path.read_bytes()))
        with closing(sqlite3.connect(raw)) as connection: action(connection); connection.commit()
        path.write_bytes(gzip.compress(raw.read_bytes())); checksum = hashlib.sha256(raw.read_bytes()).hexdigest(); raw.unlink(); self.rehash(parent + '/' + name)
        def update(body):
            entry = body['snapshots'][phase]['runtime']; entry.update(databaseSha256=checksum, gzipSha256=v.digest(path)); entry['observer']['snapshotSha256'] = checksum
        self.change(parent + '/process.json', update)

    def verify(self): return s.verify(self.root / 'manifest.json')

    def test_all_actual_pending_cancellations_recover_with_standard_hosts(self):
        result = self.verify(); self.assertEqual(30, result['wholeStores']); self.assertEqual(15, result['ownedHosts'])
        self.assertTrue(result['pendingModelSettlementCrashProved']); self.assertFalse(result['finalUpgradeAndStudioProved'])

    def test_graceful_exit_cannot_replace_actual_process_loss(self):
        self.change('direct/process.json', lambda p: p['exits'][0].update(exitCode=0))
        with self.assertRaisesRegex(ValueError, 'process-loss'): self.verify()

    def test_other_source_products_cannot_qualify_the_journey(self):
        self.change('direct/process.json', lambda p: p['products']['standard'].update(sourceCommit='0' * 40))
        with self.assertRaisesRegex(ValueError, 'exact source'): self.verify()

    def test_the_physical_model_must_still_owe_settlement_at_the_crash(self):
        self.store('pending', lambda c: c.execute('UPDATE elsa_runtime_model_execution SET Lifecycle=3'))
        with self.assertRaisesRegex(ValueError, 'physical model'): self.verify()

    def test_the_sibling_must_still_be_running_before_dispatch(self):
        def change(c):
            model = json.loads(c.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchone()[0]); sibling = model['payload']['cancellations'][0]['origin']['workerExecutionId']
            c.execute('UPDATE elsa_runtime_workflow_execution_state SET Status=5 WHERE WorkflowExecutionId=?', (v.key(sibling),))
        self.store('pending', change)
        with self.assertRaisesRegex(ValueError, 'physical Event worker'): self.verify()

    def test_the_original_cancel_claim_must_be_fresh_at_the_crash(self):
        marker = json.loads((self.root / 'direct/pending-marker.json').read_text())
        self.change('direct/process.json', lambda p: p.update(pendingCapturedAtUtc=marker['visibleAfter']))
        with self.assertRaisesRegex(ValueError, 'original fresh cancellation claim'): self.verify()

    def test_recovery_must_use_a_higher_claim_fence(self):
        marker = json.loads((self.root / 'direct/pending-marker.json').read_text())
        self.change('direct/finished-source.json', lambda p: p['source'].update(deliveryFencingToken=marker['fencingToken']))
        with self.assertRaisesRegex(ValueError, 'higher fence'): self.verify()

    def test_the_physical_pending_claim_must_match_the_actual_marker(self):
        def change(c):
            model = json.loads(c.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchone()[0]); identity = v.key(model['payload']['cancellations'][0]['sourceOutboxItemId'])
            body = json.loads(c.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (identity,)).fetchone()[0]); body['deliveryVisibleAfter'] = '2000-01-01T00:00:00Z'
            c.execute('UPDATE elsa_runtime_post_commit_outbox SET ContentJson=? WHERE OutboxItemId=?', (json.dumps(body), identity))
        self.store('pending', change)
        with self.assertRaisesRegex(ValueError, 'physical original cancellation source'): self.verify()

    def test_original_worker_pins_cannot_be_replaced(self):
        def change(c):
            identity, raw = c.execute('SELECT WorkflowExecutionId,ContentJson FROM elsa_runtime_workflow_execution_state LIMIT 1').fetchone(); body = json.loads(raw)
            body['pinnedExecutable']['artifactHash'] = v.key('other'); c.execute('UPDATE elsa_runtime_workflow_execution_state SET ContentJson=? WHERE WorkflowExecutionId=?', (json.dumps(body), identity))
        self.store('retained', change)
        with self.assertRaisesRegex(ValueError, 'physical Event worker'): self.verify()

    def test_physical_native_ack_must_follow_exact_consumption(self):
        def change(c):
            model = json.loads(c.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchone()[0]); identity = v.key(model['payload']['nativeResults'][0]['sourceOutboxItemId'])
            body = json.loads(c.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (identity,)).fetchone()[0]); body['deliveredAt'] = '2000-01-01T00:00:00Z'
            c.execute('UPDATE elsa_runtime_post_commit_outbox SET ContentJson=? WHERE OutboxItemId=?', (json.dumps(body), identity))
        self.store('retained', change)
        with self.assertRaisesRegex(ValueError, 'physical native result lacks'): self.verify()

    def test_cancelled_native_parent_cannot_run_a_successor(self):
        for phase in ('finished', 'retained'):
            self.change('sequence/' + phase + '-parent.json', lambda p: p['activities'].append({'execution': {'executableNodeId': 'after'}}))
        with self.assertRaisesRegex(ValueError, 'advanced a successor'): self.verify()

    def test_native_parent_requires_typed_consumption(self):
        def change(p):
            run = next(a for a in p['activities'] if a['execution']['executableNodeId'] == 'run-bpmn')
            for item in run['triggerDeliveries']: item['payloadType']['alias'] = 'foreign'
        for phase in ('finished', 'retained'): self.change('root/' + phase + '-parent.json', change)
        with self.assertRaisesRegex(ValueError, 'typed consumption'): self.verify()

    def test_terminal_model_cannot_change_in_the_third_cold_host(self):
        self.change('direct/retained-model.json', lambda p: p['model'].update(revision=99))
        with self.assertRaisesRegex(ValueError, 'another cold host'): self.verify()


if __name__ == '__main__': unittest.main()
