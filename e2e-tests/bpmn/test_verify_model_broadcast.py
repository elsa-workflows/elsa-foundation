"""Rehashed corrupt-copy controls over actual model broadcast checkpoints."""
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

spec = importlib.util.spec_from_file_location('model_broadcast', Path(__file__).with_name('verify_model_broadcast.py'))
b = importlib.util.module_from_spec(spec); spec.loader.exec_module(b); v = b.v
EVIDENCE = Path(__file__).resolve().parents[2] / 'specs/1017-direct-bpmn-hosting/evidence/events/model-broadcast'


class ModelBroadcastControls(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='elsa-model-broadcast-control-'); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / 'evidence'; shutil.copytree(EVIDENCE, self.root)

    def rehash(self, cut, name):
        path = self.root / 'manifest.json'; body = json.loads(path.read_text()); case = next(c for c in body['cases'] if c['cut'] == cut)
        next(a for a in case['assets'] if a['path'] == name)['sha256'] = v.digest(self.root / cut / name); path.write_text(json.dumps(body))

    def change(self, cut, name, action):
        path = self.root / cut / name; value = json.loads(path.read_text()); action(value); path.write_text(json.dumps(value)); self.rehash(cut, name)

    def store(self, cut, phase, action):
        name = phase + '-runtime.sqlite.gz'; path = self.root / cut / name; raw = Path(self.temp.name) / 'store'
        raw.write_bytes(gzip.decompress(path.read_bytes()))
        with closing(sqlite3.connect(raw)) as connection: action(connection); connection.commit()
        path.write_bytes(gzip.compress(raw.read_bytes())); self.rehash(cut, name)
        self.change(cut, 'process.json', lambda p: p['snapshots'][phase]['runtime'].update(databaseSha256=hashlib.sha256(raw.read_bytes()).hexdigest(),
            gzipSha256=v.digest(path), observer={**p['snapshots'][phase]['runtime']['observer'], 'snapshotSha256': hashlib.sha256(raw.read_bytes()).hexdigest()}))

    def verify(self): return b.verify(self.root / 'manifest.json')

    def test_actual_all_three_cuts_and_source_specific_whole_stores(self):
        result = self.verify(); self.assertEqual(18, result['wholeStores']); self.assertEqual(12, result['ownedHosts']); self.assertFalse(result['finalUpgradeAndStudioProved'])

    def test_a_required_cut_cannot_be_omitted(self):
        path = self.root / 'manifest.json'; body = json.loads(path.read_text()); body['cases'].pop(); path.write_text(json.dumps(body))
        with self.assertRaisesRegex(ValueError, 'required cut'): self.verify()

    def test_a_graceful_exit_cannot_replace_process_loss(self):
        self.change('after-resume', 'process.json', lambda p: p['exits'][0].update(exitCode=0))
        with self.assertRaisesRegex(ValueError, 'process-loss'): self.verify()

    def test_a_foreign_product_source_cannot_qualify(self):
        self.change('after-resume', 'process.json', lambda p: p['products']['standard'].update(sourceCommit='0' * 40))
        with self.assertRaisesRegex(ValueError, 'products differ'): self.verify()

    def test_rearm_must_precede_original_claim_expiry(self):
        self.change('after-resume', 'process.json', lambda p: p.update(pendingCapturedAtUtc='2099-01-01T00:00:00Z'))
        with self.assertRaisesRegex(ValueError, 'fresh pre-ACK claim'): self.verify()

    def test_retry_cannot_replace_a_saved_recipient(self):
        for name in ('finished-source.json', 'retained-source.json'):
            self.change('after-resume', name, lambda p: p['source']['snapshot']['resumes'][0].update(workflowExecutionId='later-worker'))
        with self.assertRaisesRegex(ValueError, 'wrong actual model recipients'): self.verify()

    def test_the_later_listener_must_exist_after_publish_commit(self):
        late = json.loads((self.root / 'before-selection/late-before-crash.json').read_text())
        identity = late['model']['payload']['work'][0]['nativeWork']['workerExecutionId']
        for name in ('finished-source.json', 'retained-source.json'):
            self.change('before-selection', name, lambda p: next(r for r in p['source']['snapshot']['resumes'] if r['workflowExecutionId'] == identity).update(createdAt='2000-01-01T00:00:00Z'))
        with self.assertRaisesRegex(ValueError, 'between publisher commit'): self.verify()

    def test_an_original_retry_cannot_consume_rearmed_work(self):
        rearmed = json.loads((self.root / 'after-resume/rearmed-before-crash.json').read_text()); initial = json.loads((self.root / 'after-resume/initial.json').read_text())
        old = {w['nativeWork']['workerExecutionId'] for w in initial['model']['payload']['work']}; new, = [w for w in rearmed['model']['payload']['work'] if w['nativeWork']['workerExecutionId'] not in old]
        for name in ('finished-models.json', 'retained-models.json'):
            self.change('after-resume', name, lambda p: next(w for w in p[initial['model']['executionId']]['model']['payload']['work'] if w['nativeWork']['workerExecutionId'] == new['nativeWork']['workerExecutionId']).update(attemptId='other-attempt'))
        with self.assertRaisesRegex(ValueError, 'consumed a rearmed listener'): self.verify()

    def test_the_physical_worker_must_keep_its_model_pin(self):
        def change(connection):
            for identity, raw in connection.execute('SELECT WorkflowExecutionId,ContentJson FROM elsa_runtime_workflow_execution_state'):
                body = json.loads(raw)
                if body.get('modelWorkOrigin') is None: continue
                body['pinnedExecutable']['artifactHash'] = v.key('other')
                connection.execute('UPDATE elsa_runtime_workflow_execution_state SET ContentJson=? WHERE WorkflowExecutionId=?', (json.dumps(body), identity)); break
        self.store('after-resume', 'pending', change)
        with self.assertRaisesRegex(ValueError, 'physical Event worker'): self.verify()

    def test_the_physical_registration_generation_cannot_change(self):
        def change(connection):
            for identity, raw in connection.execute('SELECT OutboxItemId,ContentJson FROM elsa_runtime_post_commit_outbox'):
                body = json.loads(raw)
                if body.get('recipientSnapshot') is None: continue
                body['recipientSnapshot']['resumes'][0]['registrationCapture']['generation'] += 1
                connection.execute('UPDATE elsa_runtime_post_commit_outbox SET ContentJson=? WHERE OutboxItemId=?', (json.dumps(body), identity))
        self.store('after-resume', 'finished', change)
        with self.assertRaisesRegex(ValueError, 'registration generation'): self.verify()

    def test_a_real_current_native_wait_cannot_disappear(self):
        self.store('after-resume', 'pending', lambda c: c.execute('DELETE FROM elsa_runtime_bookmark_state'))
        with self.assertRaisesRegex(ValueError, 'exact native Event registration'): self.verify()

    def test_original_result_consumption_cannot_disappear(self):
        def change(connection):
            for identity, raw in connection.execute('SELECT InputId,ContentJson FROM elsa_runtime_model_input'):
                if json.loads(raw)['inputKind'] == v.key('native-result'): connection.execute('DELETE FROM elsa_runtime_model_input WHERE InputId=?', (identity,))
        self.store('after-resume', 'finished', change)
        with self.assertRaises(ValueError): self.verify()

    def test_the_source_cannot_be_acknowledged_in_the_pending_cut(self):
        identity = json.loads((self.root / 'after-resume/before-source.json').read_text())['source']['outboxItemId']
        self.store('after-resume', 'pending', lambda c: c.execute('UPDATE elsa_runtime_post_commit_outbox SET Status=2 WHERE OutboxItemId=?', (v.key(identity),)))
        with self.assertRaisesRegex(ValueError, 'physical publisher'): self.verify()

    def test_a_native_carrier_cannot_replace_the_model(self):
        identity = json.loads((self.root / 'empty-cohort/late-before-crash.json').read_text())['model']['executionId']
        self.store('empty-cohort', 'pending', lambda c: c.execute('UPDATE elsa_runtime_workflow_execution_state SET WorkflowExecutionId=? WHERE Id=(SELECT MIN(Id) FROM elsa_runtime_workflow_execution_state)', (v.key(identity),)))
        with self.assertRaisesRegex(ValueError, 'fabricated native model root'): self.verify()

    def test_the_host_must_retain_the_released_library(self):
        self.change('after-resume', 'host-library-products.json', lambda p: p['releasedLibraryProducts'][0].update(productVersion='0.4.0'))
        with self.assertRaisesRegex(ValueError, 'released BPMN0.5.0'): self.verify()

    def test_a_failed_prototype_cannot_be_promoted(self):
        path = self.root / 'manifest.json'; body = json.loads(path.read_text()); body['diagnostics'][0]['qualifiesJourney'] = True; path.write_text(json.dumps(body))
        with self.assertRaisesRegex(ValueError, 'failed prototype'): self.verify()


if __name__ == '__main__': unittest.main()
