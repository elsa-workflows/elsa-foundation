"""Rehashed corrupt-copy controls against actual model signal recovery stores."""
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

spec = importlib.util.spec_from_file_location('signal_recovery', Path(__file__).with_name('verify_signal_recovery.py'))
v = importlib.util.module_from_spec(spec); spec.loader.exec_module(v)
EVIDENCE = Path(__file__).resolve().parents[2] / 'specs/1017-direct-bpmn-hosting/evidence/events/signal-recovery'


class SignalRecoveryControls(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='elsa-signal-control-'); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / 'evidence'; shutil.copytree(EVIDENCE, self.root)

    def change(self, parent, name, action):
        path = self.root / parent / name; value = json.loads(path.read_text()); action(value)
        path.write_text(json.dumps(value) + '\n'); self.rehash(parent, name)

    def rehash(self, parent, name):
        path = self.root / 'manifest.json'; manifest = json.loads(path.read_text())
        case = next(c for c in manifest['cases'] if c['parent'] == parent)
        next(a for a in case['assets'] if a['path'] == name)['sha256'] = v.digest(self.root / parent / name)
        path.write_text(json.dumps(manifest) + '\n')

    def store(self, phase, action):
        name = phase + '-runtime.sqlite.gz'; path = self.root / 'direct' / name; raw = Path(self.temp.name) / 'db'
        raw.write_bytes(gzip.decompress(path.read_bytes()))
        with closing(sqlite3.connect(raw)) as connection: action(connection); connection.commit()
        path.write_bytes(gzip.compress(raw.read_bytes())); self.rehash('direct', name)
        def update(process):
            entry = next(s for s in process['stores'] if s['file'] == name)
            entry.update(databaseSha256=hashlib.sha256(raw.read_bytes()).hexdigest(), gzipSha256=v.digest(path))
        self.change('direct', 'process.json', update)

    def verify(self): return v.verify(self.root / 'manifest.json')

    def test_actual_all_parent_journeys_and_whole_stores(self):
        result = self.verify(); self.assertEqual(30, result['wholeStores']); self.assertEqual(15, result['ownedProcesses'])
        self.assertFalse(result['laterListenerAndRearmRetryProved']); self.assertFalse(result['finalUpgradeAndStudioProved'])

    def test_graceful_exit_cannot_replace_process_loss(self):
        self.change('direct', 'process.json', lambda p: p['exits'].__setitem__(0, 0))
        with self.assertRaisesRegex(ValueError, 'process-loss'): self.verify()

    def test_foreign_product_source_is_refused(self):
        self.change('direct', 'process.json', lambda p: p['binaries'][0].update(productVersion='1.0.0+' + '0' * 40))
        with self.assertRaisesRegex(ValueError, 'products'): self.verify()

    def test_original_wait_cannot_change_after_restart(self):
        self.change('direct', 'recovered-checked.json', lambda p: p['model']['payload']['work'].pop())
        with self.assertRaisesRegex(ValueError, 'original signal wait'): self.verify()

    def test_both_exact_consumptions_are_required(self):
        for name in ('finished-checked.json', 'retained-checked.json'):
            self.change('direct', name, lambda p: p['inputs'].remove(next(r for r in p['inputs'] if r['inputKind'] == 'native-result')))
        with self.assertRaisesRegex(ValueError, 'consumption receipt'): self.verify()

    def test_result_ack_must_follow_consumption(self):
        self.change('direct', 'finished-workers.json', lambda p: p[0]['resultSource'].update(deliveredAt='2000-01-01T00:00:00Z'))
        with self.assertRaisesRegex(ValueError, 'acknowledged before'): self.verify()

    def test_later_worker_cannot_replace_a_selected_recipient(self):
        self.change('direct', 'broadcast-checked.json', lambda p: p['source']['snapshot']['resumes'][0].update(workflowExecutionId='later-worker'))
        with self.assertRaisesRegex(ValueError, 'original native Event recipients'): self.verify()

    def test_the_native_parent_must_keep_its_typed_result(self):
        self.change('sequence', 'finished-parent.json', lambda p: next(a for a in p['activities'] if a['execution']['executableNodeId'] == 'run-bpmn')['completion'].update(outcomeKey='Other'))
        with self.assertRaisesRegex(ValueError, 'native parent'): self.verify()

    def test_a_coupled_store_cannot_be_omitted(self):
        self.change('direct', 'process.json', lambda p: p['stores'].pop())
        with self.assertRaisesRegex(ValueError, 'coupled signal store'): self.verify()

    def test_the_physical_worker_cannot_change_its_pin(self):
        def change(connection):
            row = connection.execute('SELECT WorkflowExecutionId,ContentJson FROM elsa_runtime_workflow_execution_state').fetchone()
            body = json.loads(row[1]); body['pinnedExecutable']['artifactHash'] = v.key('other')
            connection.execute('UPDATE elsa_runtime_workflow_execution_state SET ContentJson=? WHERE WorkflowExecutionId=?', (json.dumps(body), row[0]))
        self.store('waiting', change)
        with self.assertRaisesRegex(ValueError, 'physical Event worker'): self.verify()

    def test_the_physical_model_cannot_lose_a_signal_result(self):
        def change(connection):
            body = json.loads(connection.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchone()[0]); body['payload']['nativeResults'].pop()
            connection.execute('UPDATE elsa_runtime_model_execution SET ContentJson=?', (json.dumps(body),))
        self.store('finished', change)
        with self.assertRaisesRegex(ValueError, 'physical model'): self.verify()

    def test_the_physical_consumption_cannot_disappear(self):
        def change(connection):
            ids = [identity for identity, raw in connection.execute('SELECT InputId,ContentJson FROM elsa_runtime_model_input') if json.loads(raw)['inputKind'] == v.key('native-result')]
            connection.executemany('DELETE FROM elsa_runtime_model_input WHERE InputId=?', [(identity,) for identity in ids])
        self.store('finished', change)
        with self.assertRaises(ValueError): self.verify()

    def test_the_durable_cohort_cannot_change_recipient(self):
        def change(connection):
            for identity, content in connection.execute('SELECT OutboxItemId,ContentJson FROM elsa_runtime_post_commit_outbox'):
                body = json.loads(content)
                if body.get('recipientSnapshot') is None: continue
                body['recipientSnapshot']['resumes'][0]['workflowExecutionId'] = v.key('later-worker')
                connection.execute('UPDATE elsa_runtime_post_commit_outbox SET ContentJson=? WHERE OutboxItemId=?', (json.dumps(body), identity))
        self.store('finished', change)
        with self.assertRaisesRegex(ValueError, 'physical durable broadcast cohort'): self.verify()

    def test_a_diagnostic_cannot_be_promoted_to_qualification(self):
        path = self.root / 'manifest.json'; body = json.loads(path.read_text()); body['diagnostics'][0]['qualifiesJourney'] = True; path.write_text(json.dumps(body))
        with self.assertRaisesRegex(ValueError, 'diagnostic cannot qualify'): self.verify()


if __name__ == '__main__': unittest.main()
