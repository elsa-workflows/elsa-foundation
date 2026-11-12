"""Rehashed corrupt copies of actual two-attempt fault/budget recovery evidence."""
from contextlib import closing
import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from pathlib import Path
_spec=importlib.util.spec_from_file_location('budget',Path(__file__).with_name('verify_service_budget_exhaustion.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/service-budget-exhaustion'


class ActualServiceBudgetExhaustionControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-budget-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def rehash(self,case,name):
        p=self.root/'manifest.json';m=json.loads(p.read_text());item=next(c for c in m['cases'] if c['name']==case)
        next(a for a in item['assets'] if a['path']==case+'/'+name)['sha256']=v.q.digest(self.root/case/name);v.q.write_json(p,m)

    def change(self,case,name,action):
        p=self.root/case/name;value=json.loads(p.read_text());action(value);v.q.write_json(p,value);self.rehash(case,name)

    def store(self,case,action,store='elsa'):
        p=self.root/case/('finished-'+store+'.sqlite.gz');raw=Path(self.temp.name)/'changed.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(case,p.name)
        def observer(value):
            s=value['finishedStores'][store];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=v.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change(case,'process.json',observer)

    def verify(self):return v.verify(self.root/'manifest.json')

    def test_real_budget_exhaustion_is_final_directly_and_under_every_native_parent(self):
        r=self.verify();self.assertEqual(5,len(r['actualBudgetExhaustionJourneys']));self.assertEqual(45,r['wholeStoresVerified']);self.assertTrue(r['budgetExhaustionProved'])
        self.assertTrue(r['bothFailedAttemptsPreserved']);self.assertTrue(r['furtherRetryRefusedWithoutNewAttempt']);self.assertTrue(r['sameSourceInterpreterTracesCompared'])
        for name in ('nativeCompositeFaultStatusDecisionResolved','timerInterruptionProved','competingRetryCancelCommitOrdersProved','obsoleteResultControlsProved','authorizationBeforeReplayProved','finalAffectedSourceUpgradeAndStudioProved'):self.assertFalse(r[name])

    def test_the_immutable_budget_cannot_be_increased_after_publication(self):
        def change(c):
            for identity,content in c.execute('SELECT ArtifactId,ContentJson FROM elsa_runtime_workflow_executable').fetchall():
                value=json.loads(content)
                if value.get('material'):
                    value['material']['definition']['serviceBindings'][0]['faultPolicy']['maxAttempts']=3
                    c.execute('UPDATE elsa_runtime_workflow_executable SET ContentJson=? WHERE ArtifactId=?',(json.dumps(value),identity))
        self.store('direct',change)
        with self.assertRaisesRegex(ValueError,'pinned budget'):self.verify()

    def test_the_exhausted_failure_cannot_remain_open(self):
        for name in ('finished-checked-model.json','retained-checked-model.json'):
            self.change('direct',name,lambda x:x['model']['payload']['failures'][1].update(status='Open'))
        with self.assertRaisesRegex(ValueError,'second technical fault'):self.verify()

    def test_the_original_fault_cannot_be_replaced_by_the_new_failure(self):
        for name in ('finished-checked-model.json','retained-checked-model.json'):
            self.change('direct',name,lambda x:x['model']['payload']['nativeResults'][0].update(faultCode='etl.binding-read-failed'))
        with self.assertRaisesRegex(ValueError,'preserve both original failed attempts'):self.verify()

    def test_the_new_physical_native_fault_must_be_the_actual_preparation_fault(self):
        p=json.loads((self.root/'root/process.json').read_text())
        def change(c):
            row=c.execute('SELECT Id,ContentJson FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId=?',(v.v.key(p['replacementWorkerExecutionId']),)).fetchone()
            value=json.loads(row[1]);value['fault']['code']=v.v.key('other-fault');c.execute('UPDATE elsa_runtime_activity_execution_state SET ContentJson=? WHERE Id=?',(json.dumps(value),row[0]))
        self.store('root',change)
        with self.assertRaisesRegex(ValueError,'physical worker fault'):self.verify()

    def test_a_genuine_second_etl_fault_cannot_be_relabelled_as_business_error(self):
        def change(c):
            for run,content in c.execute('SELECT RunKey,ResultJson FROM elsa_etl_runtime_terminals').fetchall():
                value=json.loads(content)
                if value['acceptedRecords']==0:
                    value['outcome']='businessError';c.execute('UPDATE elsa_etl_runtime_terminals SET ResultJson=? WHERE RunKey=?',(json.dumps(value),run))
        self.store('direct',change,store='etl-runtime')
        with self.assertRaisesRegex(ValueError,'relabeled as another classifier'):self.verify()

    def test_both_original_physical_etl_invocations_must_remain(self):
        p=json.loads((self.root/'sequence/process.json').read_text())
        self.store('sequence',lambda c:c.execute('DELETE FROM elsa_etl_runtime_runs WHERE RunId=?',(v.v.key(p['runId']),)),store='etl-runtime')
        with self.assertRaisesRegex(ValueError,'lost a failed invocation'):self.verify()

    def test_preparation_failure_cannot_be_called_a_runnable_prepared_pipeline(self):
        self.store('flowchart',lambda c:c.execute("UPDATE elsa_etl_connector_runs SET PreparedJson='{}' WHERE PreparedJson IS NULL"),store='etl-runtime')
        with self.assertRaisesRegex(ValueError,'preparation fault incorrectly created runnable'):self.verify()

    def test_retry_cannot_reuse_the_failed_physical_attempt_identity(self):
        original=json.loads((self.root/'direct/before-checked-model.json').read_text())['worker']['modelWorkOrigin']['attemptId']
        for name in ('finished-checked-replacement.json','retained-checked-replacement.json'):
            self.change('direct',name,lambda x:x['worker']['modelWorkOrigin'].update(attemptId=original))
        with self.assertRaisesRegex(ValueError,'immutable full origin'):self.verify()

    def test_a_third_attempt_cannot_be_reported_as_accepted(self):
        self.change('direct','exhausted-retry-refusal.json',lambda x:x.update(httpStatus=200))
        with self.assertRaisesRegex(ValueError,'exact terminal retry refusal'):self.verify()

    def test_the_physical_third_retry_refusal_must_retain_the_final_failed_attempt(self):
        def change(c):
            identity=v.v.key('retry-actual-exhausted-service');row=c.execute('SELECT ContentJson FROM elsa_runtime_model_input WHERE InputId=?',(identity,)).fetchone()
            value=json.loads(row[0]);value['attemptId']=v.v.key('other-attempt');c.execute('UPDATE elsa_runtime_model_input SET ContentJson=? WHERE InputId=?',(json.dumps(value),identity))
        self.store('root',change)
        with self.assertRaisesRegex(ValueError,'accepted/rejected decisions'):self.verify()

    def test_the_sequence_native_root_cannot_be_normalized_into_a_new_fault_policy(self):
        for name in ('finished-checked-embedding.json','retained-checked-embedding.json'):
            self.change('sequence',name,lambda x:next(a for a in x['activities'] if a['execution']['activityType']=='Elsa.Activities.Sequence.Activities.Sequence').update(status=5))
        with self.assertRaisesRegex(ValueError,'root fault disposition was normalized'):self.verify()

    def test_compensation_must_preserve_its_actual_compensated_outcome(self):
        for name in ('finished-checked-embedding.json','retained-checked-embedding.json'):
            self.change('compensationscope',name,lambda x:next(a for a in x['activities'] if a['execution']['activityType']=='Elsa.Activities.Compensation.Activities.CompensationScope')['completion'].update(outcomeKey='Done'))
        with self.assertRaisesRegex(ValueError,'Compensated/AbsorbFault truth'):self.verify()

    def test_repaired_bound_input_must_keep_its_original_bytes(self):
        self.change('flowchart','retry-input-unavailability.json',lambda x:x.update(restoredSha256='f'*64))
        with self.assertRaisesRegex(ValueError,'immutable lookup bytes'):self.verify()

    def test_the_held_failure_requires_an_actual_second_process_loss(self):
        self.change('direct','process.json',lambda x:x.update(heldPredecessorExitCode=0))
        with self.assertRaisesRegex(ValueError,'two real process-loss cuts'):self.verify()
