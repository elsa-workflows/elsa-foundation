"""Rehashed captured-store controls for actual material retention and admission-off recovery."""
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

spec = importlib.util.spec_from_file_location('rollback', Path(__file__).with_name('verify_model_rollback.py'))
r = importlib.util.module_from_spec(spec); spec.loader.exec_module(r); v = r.v
EVIDENCE = Path(__file__).resolve().parents[2] / 'specs/1017-direct-bpmn-hosting/evidence/retention/runtime-only-rollback'


class ModelRollbackControls(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(prefix='elsa-model-rollback-control-'); self.addCleanup(temp.cleanup)
        self.root = Path(temp.name) / 'evidence'; shutil.copytree(EVIDENCE, self.root)

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

    def verify(self): return r.verify(self.root / 'manifest.json')

    def test_actual_retirement_and_admission_off_runtime_only_matrix(self):
        result = self.verify(); self.assertEqual(25, result['wholeStores']); self.assertEqual(15, result['ownedHosts'])
        self.assertTrue(result['retiredPublicationRecoveryProved']); self.assertTrue(result['admissionOffAndRuntimeOnlyRecoveryProved'])
        self.assertFalse(result['nonSqlitePhysicalProviderQualificationProved']); self.assertFalse(result['finalUpgradeAndStudioProved'])

    def test_graceful_exit_cannot_replace_owned_process_loss(self):
        self.change('direct/process.json', lambda p: p['exits'][0].update(exitCode=0))
        with self.assertRaisesRegex(ValueError, 'owned process losses'): self.verify()

    def test_admission_must_remain_disabled_during_recovery(self):
        self.change('direct/runtime-only-features.json', lambda p: p['ActivitiesBpmnHosting'].update(AdmitNewExecutions=True))
        with self.assertRaisesRegex(ValueError, 'still enables admission'): self.verify()

    def test_design_storage_cannot_be_reenabled(self):
        self.change('direct/runtime-only-features.json', lambda p: p.update(WorkflowsDesignEntityFrameworkCore={}))
        with self.assertRaisesRegex(ValueError, 'Design/Publishing storage'): self.verify()

    def test_the_rollback_host_must_refuse_a_new_model_start(self):
        self.change('direct/new-admission-refused.json', lambda p: p.update(status=200))
        with self.assertRaisesRegex(ValueError, 'accepted a new model execution'): self.verify()

    def test_collector_must_actually_collect_the_unreferenced_control(self):
        self.change('direct/collection.json', lambda p: p.update(orphanCollected=False))
        with self.assertRaisesRegex(ValueError, 'positive orphan control'): self.verify()

    def test_owed_native_material_cannot_be_collected_before_admission(self):
        p = json.loads((self.root / 'direct/process.json').read_text()); identity = p['workerPublication']['artifactId']
        self.store('pending', lambda c: c.execute('DELETE FROM elsa_runtime_workflow_executable WHERE ArtifactId=?', (v.key(identity),)))
        with self.assertRaisesRegex(ValueError, 'removed an owed pinned artifact'): self.verify()

    def test_retired_publication_cannot_silently_become_live(self):
        p = json.loads((self.root / 'direct/process.json').read_text()); identity = p['published']['sourceReferenceId']
        self.store('retained', lambda c: c.execute('UPDATE elsa_runtime_workflow_executable_source_reference SET IsRetired=0 WHERE SourceReferenceId=?', (v.key(identity),)))
        with self.assertRaisesRegex(ValueError, 'physical retired publication became live'): self.verify()

    def test_held_source_must_be_fresh_at_the_actual_crash(self):
        marker = json.loads((self.root / 'direct/pending-marker.json').read_text())
        self.change('direct/process.json', lambda p: p.update(pendingCapturedAtUtc=marker['visibleAfter']))
        with self.assertRaisesRegex(ValueError, 'original fresh native-start claim'): self.verify()

    def test_recovery_requires_a_higher_fence(self):
        marker = json.loads((self.root / 'direct/pending-marker.json').read_text())
        self.change('direct/recovered-source.json', lambda p: p['source'].update(deliveryFencingToken=marker['fencingToken']))
        with self.assertRaisesRegex(ValueError, 'higher fence'): self.verify()

    def test_sdk_must_use_the_exact_committed_source(self):
        self.change('direct/collection.json', lambda p: p['products'][0].update(informationalVersion='1.0.0+' + '0' * 40))
        with self.assertRaisesRegex(ValueError, 'SDK lost its source'): self.verify()

    def test_native_result_ack_must_follow_physical_consumption(self):
        def change(c):
            model = json.loads(c.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchone()[0]); identity = v.key(model['payload']['nativeResults'][0]['sourceOutboxItemId'])
            body = json.loads(c.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (identity,)).fetchone()[0]); body['deliveredAt'] = '2000-01-01T00:00:00Z'
            c.execute('UPDATE elsa_runtime_post_commit_outbox SET ContentJson=? WHERE OutboxItemId=?', (json.dumps(body), identity))
        self.store('retained', change)
        with self.assertRaisesRegex(ValueError, 'physical rollback result lacks'): self.verify()

    def test_native_parent_cannot_lose_its_typed_result(self):
        def change(p):
            run = next(a for a in p['activities'] if a['execution']['executableNodeId'] == 'run-bpmn')
            run['completion']['result']['inlineValue']['executionId'] = 'foreign'
        for phase in ('finished', 'retained'): self.change('root/' + phase + '-parent.json', change)
        with self.assertRaisesRegex(ValueError, 'exact typed result consumption'): self.verify()

    def test_native_successor_cannot_complete_twice(self):
        def change(p): p['activities'].append(next(a for a in p['activities'] if a['execution']['executableNodeId'] == 'after'))
        for phase in ('finished', 'retained'): self.change('sequence/' + phase + '-parent.json', change)
        with self.assertRaisesRegex(ValueError, 'continue exactly once'): self.verify()

    def test_later_design_snapshot_cannot_replace_runtime_only_recovery(self):
        self.change('direct/process.json', lambda p: p['snapshots']['recovered'].update(design=p['snapshots']['pending']['design']))
        with self.assertRaisesRegex(ValueError, 'fabricated later Design availability'): self.verify()


if __name__ == '__main__': unittest.main()
