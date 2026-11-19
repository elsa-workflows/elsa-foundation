"""Rehashed corrupt controls over actual paired partial-fork recovery captures."""
from copy import deepcopy
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

spec = importlib.util.spec_from_file_location('parallel_embedding', Path(__file__).with_name('verify_parallel_embedding.py'))
v = importlib.util.module_from_spec(spec); spec.loader.exec_module(v)
EVIDENCE = v.q.REPO / 'specs/1017-direct-bpmn-hosting/evidence/embedding/parallel-native-recovery'


class ParallelEmbeddingEvidenceControls(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='elsa-parallel-controls-'); self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / 'evidence'; shutil.copytree(EVIDENCE, self.root)

    def rehash(self, relative):
        path = self.root / 'manifest.json'; value = json.loads(path.read_text())
        next(a for a in value['assets'] if a['path'] == relative)['sha256'] = v.q.digest(self.root / relative)
        v.q.write_json(path, value)

    def change(self, relative, action):
        path = self.root / relative; value = json.loads(path.read_text()); action(value); v.q.write_json(path, value); self.rehash(relative)

    def verify(self): return v.verify(self.root / 'manifest.json')

    def test_actual_four_parent_pairs_verify_with_twenty_four_whole_stores(self):
        result = self.verify()
        self.assertEqual(8, result['modelExecutionsCompleted']); self.assertEqual(24, result['wholeStoresVerified'])
        self.assertTrue(result['directEmbeddedTracesEqual']); self.assertFalse(result['etlOrStudioAcceptanceProved'])

    def test_rehashed_semantic_token_change_is_not_normalized(self):
        self.change('parallel-root/partial-checked.json', lambda x: x['model']['payload']['state']['tokens'][0].update(tokenId='foreign-token'))
        with self.assertRaisesRegex(ValueError, 'traces differ'): self.verify()

    def test_rehashed_token_order_change_is_not_normalized(self):
        self.change('parallel-root/partial-checked.json', lambda x: x['model']['payload']['state']['tokens'].reverse())
        with self.assertRaisesRegex(ValueError, 'traces differ'): self.verify()

    def test_a_foreign_model_work_handle_is_not_normalized(self):
        case = json.loads((self.root / 'parallel-root/case.json').read_text())
        foreign = case['initial'][case['directCounterpartId']][0]['invocationId']
        self.change('parallel-root/partial-checked.json', lambda x: x['model']['payload']['state']['tokens'][1].update(producingWorkHandle=foreign))
        with self.assertRaisesRegex(ValueError, 'traces differ'): self.verify()

    def test_a_different_local_work_handle_is_not_collapsed_to_one_placeholder(self):
        case = json.loads((self.root / 'parallel-root/case.json').read_text())
        wrong = case['initial'][case['modelExecutionId']][1]['invocationId']
        self.change('parallel-root/partial-checked.json', lambda x: x['model']['payload']['state']['tokens'][1].update(producingWorkHandle=wrong))
        with self.assertRaisesRegex(ValueError, 'traces differ'): self.verify()

    def test_rehashed_invented_work_token_in_the_ledger_is_rejected(self):
        def changed(case): case['initial'][case['modelExecutionId']][0]['tokenId'] = 'foreign-token'
        self.change('parallel-root/case.json', changed)
        with self.assertRaisesRegex(ValueError, 'traces differ'): self.verify()

    def test_two_matching_but_rehashed_false_traces_do_not_replace_physical_state(self):
        def changed(value):
            for name in ('model', 'directCounterpart'): value[name]['payload']['state']['tokens'][0]['tokenId'] = 'foreign-token'
        self.change('parallel-root/partial-checked.json', changed)
        self.change('parallel-root/recovered-checked.json', changed)
        with self.assertRaisesRegex(ValueError, 'physical embedded model'): self.verify()

    def test_rehashed_deleted_physical_left_receipt_is_rejected(self):
        relative = 'parallel-root/partial-runtime.sqlite.gz'; path = self.root / relative
        raw = Path(self.temporary.name) / 'changed.sqlite'; raw.write_bytes(gzip.decompress(path.read_bytes()))
        case = json.loads((self.root / 'parallel-root/case.json').read_text())
        with closing(sqlite3.connect(raw)) as connection:
            deleted = connection.execute('DELETE FROM elsa_runtime_model_input WHERE ExecutionId = ? AND InputId = ?', (v.v.key(case['modelExecutionId']), v.v.key('left')))
            self.assertEqual(1, deleted.rowcount)
            connection.commit()
        path.write_bytes(gzip.compress(raw.read_bytes())); self.rehash(relative)
        def changed(value):
            item = next(s for s in value['snapshots'] if s['file'] == 'partial-runtime.sqlite.gz')
            item.update(databaseSha256=hashlib.sha256(raw.read_bytes()).hexdigest(), gzipSha256=v.q.digest(path))
        self.change('parallel-root/case.json', changed)
        with self.assertRaisesRegex(ValueError, 'physical model input set'): self.verify()

    def test_rehashed_premature_parent_ack_is_rejected(self):
        def changed(value):
            next(s for s in value['sources'] if s['intent']['kind'] == v.v.RESULT)['deliveredAt'] = '2000-01-01T00:00:00Z'
        self.change('parallel-root/final-checked.json', changed); self.change('parallel-root/retained-checked.json', changed)
        with self.assertRaisesRegex(ValueError, 'acknowledged before'): self.verify()

    def test_rehashed_duplicate_process_identity_cannot_prove_restart(self):
        def changed(case):
            case['processes'][1] = case['processes'][0]; case['ownedProcesses'][1] = deepcopy(case['ownedProcesses'][0])
        self.change('parallel-root/case.json', changed)
        with self.assertRaisesRegex(ValueError, 'owned process losses'): self.verify()

    def test_rehashed_native_parent_relabel_is_rejected(self):
        self.change('parallel-root/case.json', lambda x: x.update(parent='Sequence'))
        with self.assertRaisesRegex(ValueError, 'owned process losses'): self.verify()

    def test_rehashed_different_library_is_not_qualified_as_released_zero_five(self):
        def changed(case): next(p for p in case['binaries'] if p['file'] == 'Bpmn.Semantics.dll')['productVersion'] = '0.4.0+foreign'
        self.change('parallel-root/case.json', changed)
        with self.assertRaisesRegex(ValueError, 'exact-source identities'): self.verify()

    def test_an_omitted_parent_case_is_rejected(self):
        path = self.root / 'manifest.json'; value = json.loads(path.read_text())
        value['assets'] = [a for a in value['assets'] if not a['path'].startswith('parallel-flowchart/')]
        v.q.write_json(path, value)
        with self.assertRaisesRegex(ValueError, 'four-parent assets'): self.verify()


if __name__ == '__main__': unittest.main()
