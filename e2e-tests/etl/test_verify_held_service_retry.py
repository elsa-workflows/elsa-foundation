"""Rehashed corrupt controls over real held-service recovery stores and receipts."""
import gzip
import hashlib
import importlib.util
import json
from contextlib import closing
from pathlib import Path
import shutil
import sqlite3
import tempfile
import unittest

_spec=importlib.util.spec_from_file_location('held_retry',Path(__file__).with_name('verify_held_service_retry.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/held-service-retry'


class ActualHeldServiceRetryControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-held-service-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def rehash(self,case,name):
        p=self.root/'manifest.json';value=json.loads(p.read_text());item=next(c for c in value['cases'] if c['name']==case)
        next(a for a in item['assets'] if a['path']==case+'/'+name)['sha256']=v.q.digest(self.root/case/name);v.q.write_json(p,value)

    def change(self,case,name,action):
        p=self.root/case/name;value=json.loads(p.read_text());action(value);v.q.write_json(p,value);self.rehash(case,name)

    def store(self,case,action,phase='finished',store='elsa'):
        p=self.root/case/(phase+'-'+store+'.sqlite.gz');raw=Path(self.temp.name)/'changed.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(case,p.name)
        def observer(value):
            s=value[phase+'Stores'][store];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=v.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change(case,'process.json',observer)

    def verify(self):return v.verify(self.root/'manifest.json')

    def test_real_held_retries_recover_directly_and_under_every_native_parent(self):
        result=self.verify();self.assertEqual(5,len(result['actualHeldRetryJourneys']));self.assertEqual(45,result['wholeStoresVerified'])
        self.assertTrue(result['oneAuthorizedPhysicalRetryProved']);self.assertTrue(result['oldFailurePreserved']);self.assertTrue(result['sameSourceInterpreterTracesCompared'])
        for name in ('budgetExhaustionProved','heldCancellationProved','obsoleteResultControlsProved','authorizationBeforeReplayProved','finalUpgradeAndStudioProved'):
            self.assertFalse(result[name])

    def test_holding_a_failure_cannot_rewind_the_semantic_token(self):
        self.change('direct','held-checked-model.json',lambda x:x['model']['payload']['state'].update(sequence=1))
        with self.assertRaisesRegex(ValueError,'active semantic token'):self.verify()

    def test_a_failed_attempt_cannot_be_erased_from_the_final_history(self):
        for name in ('finished-checked-model.json','retained-checked-model.json'):
            self.change('direct',name,lambda x:x['model']['payload']['nativeResults'][0].update(status=3))
        with self.assertRaisesRegex(ValueError,'original fault history'):self.verify()

    def test_an_identical_retry_must_replay_its_original_receipt(self):
        self.change('direct','retry-command.json',lambda x:x['duplicate'].update(replay=False))
        with self.assertRaisesRegex(ValueError,'idempotent replay'):self.verify()

    def test_a_result_source_cannot_be_called_acknowledged_while_still_owed(self):
        for name in ('finished-checked-replacement.json','retained-checked-replacement.json'):
            self.change('direct',name,lambda x:x['resultSource'].update(status=3,deliveredAt=None))
        with self.assertRaisesRegex(ValueError,'consumption receipt'):self.verify()

    def test_the_retry_receipt_must_retain_its_actual_failed_attempt(self):
        def change(c):
            identity=v.v.key('retry-actual-held-service');row=c.execute('SELECT ContentJson FROM elsa_runtime_model_input WHERE InputId=?',(identity,)).fetchone()
            value=json.loads(row[0]);value['attemptId']=v.v.key('other-attempt');c.execute('UPDATE elsa_runtime_model_input SET ContentJson=? WHERE InputId=?',(json.dumps(value),identity))
        self.store('root',change)
        with self.assertRaisesRegex(ValueError,'one exact decision'):self.verify()

    def test_the_original_physical_native_fault_cannot_be_rewritten(self):
        p=json.loads((self.root/'direct/process.json').read_text())
        def change(c):
            identity=v.v.key(p['workflowExecutionId']);row=c.execute('SELECT Id,ContentJson FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId=?',(identity,)).fetchone()
            value=json.loads(row[1]);value['fault']['code']=v.v.key('other-code');c.execute('UPDATE elsa_runtime_activity_execution_state SET ContentJson=? WHERE Id=?',(json.dumps(value),row[0]))
        self.store('direct',change)
        with self.assertRaisesRegex(ValueError,'original native root'):self.verify()

    def test_the_pinned_attempt_budget_cannot_change_after_publication(self):
        def change(c):
            for identity,content in c.execute('SELECT ArtifactId,ContentJson FROM elsa_runtime_workflow_executable').fetchall():
                value=json.loads(content)
                if value.get('material'):
                    value['material']['definition']['serviceBindings'][0]['faultPolicy']['maxAttempts']=3
                    c.execute('UPDATE elsa_runtime_workflow_executable SET ContentJson=? WHERE ArtifactId=?',(json.dumps(value),identity))
        self.store('sequence',change)
        with self.assertRaisesRegex(ValueError,'immutable per-service policy'):self.verify()

    def test_retry_cannot_remove_the_failed_etl_run(self):
        p=json.loads((self.root/'flowchart/process.json').read_text())
        self.store('flowchart',lambda c:c.execute('DELETE FROM elsa_etl_runtime_runs WHERE RunId=?',(v.v.key(p['runId']),)),store='etl-runtime')
        with self.assertRaisesRegex(ValueError,'lost the failed run'):self.verify()

    def test_retry_cannot_lose_the_original_etl_fault_terminal(self):
        self.store('direct',lambda c:c.execute('DELETE FROM elsa_etl_runtime_terminals WHERE Outcome=2'),store='etl-runtime')
        with self.assertRaisesRegex(ValueError,'ETL terminal'):self.verify()

    def test_a_different_lookup_cannot_be_used_to_claim_recovery(self):
        def change(c):
            row=c.execute('SELECT RunKey,PreparedJson FROM elsa_etl_connector_runs LIMIT 1').fetchone();value=json.loads(row[1]);value['lookup']['contentHash']='u16:'+v.v.key('f'*64)
            c.execute('UPDATE elsa_etl_connector_runs SET PreparedJson=? WHERE RunKey=?',(json.dumps(value),row[0]))
        self.store('compensationscope',change,store='etl-runtime')
        with self.assertRaisesRegex(ValueError,'pinned and prepared lookup'):self.verify()

    def test_the_native_parent_must_consume_the_original_typed_registration(self):
        for name in ('finished-checked-embedding.json','retained-checked-embedding.json'):
            self.change('root',name,lambda x:next(a for a in x['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn').update(triggerDeliveries=[]))
        with self.assertRaisesRegex(ValueError,'typed consumption receipt'):self.verify()

    def test_the_physical_parent_must_have_its_actual_declared_root_shape(self):
        p=json.loads((self.root/'sequence/process.json').read_text())
        def change(c):
            identity=v.v.key(p['nativeParentPublication']['artifactId']);row=c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable WHERE ArtifactId=?',(identity,)).fetchone()
            value=json.loads(row[0]);value['rootActivity']['activityType']=v.v.key('Elsa.Activities.Bpmn.Activities.RunBpmn');c.execute('UPDATE elsa_runtime_workflow_executable SET ContentJson=? WHERE ArtifactId=?',(json.dumps(value),identity))
        self.store('sequence',change)
        with self.assertRaisesRegex(ValueError,'native parent root shape'):self.verify()

    def test_repeated_external_effects_cannot_be_hidden_as_exactly_once(self):
        for name in ('finished-wire.json','retained-wire.json'):
            self.change('direct',name,lambda x:x.update(outputPosts=x['outputPosts'][2:]))
        with self.assertRaisesRegex(ValueError,'at-least-once physical output'):self.verify()

    def test_real_process_loss_cannot_be_replaced_by_a_graceful_stop(self):
        self.change('flowchart','process.json',lambda x:x.update(heldPredecessorExitCode=0))
        with self.assertRaisesRegex(ValueError,'two real process-loss cuts'):self.verify()

    def test_a_duplicate_parent_cannot_inflate_the_recovery_matrix(self):
        p=self.root/'manifest.json';value=json.loads(p.read_text());value['cases'][1]=value['cases'][0];v.q.write_json(p,value)
        with self.assertRaisesRegex(ValueError,'every unique direct/native parent'):self.verify()
