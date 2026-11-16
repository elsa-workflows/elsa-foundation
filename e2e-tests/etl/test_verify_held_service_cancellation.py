"""Rehashed corrupt copies of actual held-service cancellation captures."""
from contextlib import closing
import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from pathlib import Path
_spec=importlib.util.spec_from_file_location('held_cancel',Path(__file__).with_name('verify_held_service_cancellation.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/held-service-cancellation'


class ActualHeldServiceCancellationControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-held-cancel-control-');self.addCleanup(self.temp.cleanup)
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

    def test_real_held_cancellation_retires_work_directly_and_under_all_native_parents(self):
        r=self.verify();self.assertEqual(5,len(r['actualHeldCancellationJourneys']));self.assertEqual(45,r['wholeStoresVerified'])
        self.assertTrue(r['heldCancellationProved']);self.assertTrue(r['retiredRetryRefusalProved']);self.assertTrue(r['sameSourceInterpreterTracesCompared'])
        for name in ('budgetExhaustionProved','timerInterruptionProved','obsoleteResultControlsProved','authorizationBeforeReplayProved','finalAffectedSourceUpgradeAndStudioProved'):self.assertFalse(r[name])

    def test_cancelled_held_work_cannot_remain_active(self):
        before=json.loads((self.root/'direct/before-checked-model.json').read_text())
        for name in ('finished-checked-model.json','retained-checked-model.json'):
            self.change('direct',name,lambda x:x['model']['payload'].update(work=before['model']['payload']['work']))
        with self.assertRaisesRegex(ValueError,'Held cancellation revived'):self.verify()

    def test_retired_incident_cannot_accept_another_retry(self):
        self.change('direct','retired-retry-refusal.json',lambda x:x.update(httpStatus=200))
        with self.assertRaisesRegex(ValueError,'terminal retry refusal'):self.verify()

    def test_the_original_failed_etl_truth_cannot_be_relabelled(self):
        for name in ('finished.json','retained.json'):self.change('direct',name,lambda x:x['run'].update(status='succeeded'))
        with self.assertRaisesRegex(ValueError,'original ETL truth'):self.verify()

    def test_the_original_physical_fault_cannot_change_during_cancellation(self):
        p=json.loads((self.root/'direct/process.json').read_text())
        def change(c):
            row=c.execute('SELECT Id,ContentJson FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId=?',(v.v.key(p['workflowExecutionId']),)).fetchone()
            value=json.loads(row[1]);value['fault']['code']=v.v.key('other-fault');c.execute('UPDATE elsa_runtime_activity_execution_state SET ContentJson=? WHERE Id=?',(json.dumps(value),row[0]))
        self.store('direct',change)
        with self.assertRaisesRegex(ValueError,'physical native root fault'):self.verify()

    def test_cancellation_cannot_delete_its_failed_etl_run(self):
        self.store('flowchart',lambda c:c.execute('DELETE FROM elsa_etl_runtime_runs'),store='etl-runtime')
        with self.assertRaisesRegex(ValueError,'lost the failed run'):self.verify()

    def test_retired_retry_receipt_must_retain_its_original_failed_attempt(self):
        def change(c):
            identity=v.v.key('retry-actual-retired-service');row=c.execute('SELECT ContentJson FROM elsa_runtime_model_input WHERE InputId=?',(identity,)).fetchone()
            value=json.loads(row[0]);value['attemptId']=v.v.key('other-attempt');c.execute('UPDATE elsa_runtime_model_input SET ContentJson=? WHERE InputId=?',(json.dumps(value),identity))
        self.store('root',change)
        with self.assertRaisesRegex(ValueError,'exact durable decision'):self.verify()

    def test_a_source_cannot_be_relabelled_as_cancellation_of_the_faulted_worker(self):
        p=json.loads((self.root/'sequence/finished-checked-model.json').read_text());identity=v.v.key(p['startSource']['outboxItemId'])
        def change(c):
            row=c.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(identity,)).fetchone();value=json.loads(row[0]);value['intent']['kind']=v.v.key('Elsa.Bpmn.CancelNativeWork.v1')
            c.execute('UPDATE elsa_runtime_post_commit_outbox SET ContentJson=? WHERE OutboxItemId=?',(json.dumps(value),identity))
        self.store('sequence',change)
        with self.assertRaisesRegex(ValueError,'physical cancelled worker source'):self.verify()

    def test_the_native_parent_successor_cannot_run_after_cancellation(self):
        for name in ('finished-checked-embedding.json','retained-checked-embedding.json'):
            self.change('compensationscope',name,lambda x:x['activities'].append({'execution':{'executableNodeId':'after','activityType':'Elsa.Activities.Primitives.Activities.WriteLine'}}))
        with self.assertRaisesRegex(ValueError,'native parent advanced'):self.verify()

    def test_the_parent_cancel_source_must_settle_from_real_terminal_responsibility(self):
        for name in ('finished-checked-embedding.json','retained-checked-embedding.json'):
            self.change('root',name,lambda x:next(s for s in x['sources'] if s['intent']['kind']=='Elsa.Bpmn.CancelEmbedded.v1').update(status=3,deliveredAt=None))
        with self.assertRaisesRegex(ValueError,'settlement/ACK'):self.verify()

    def test_changed_lookup_bytes_cannot_be_called_the_same_repair(self):
        p=self.root/'direct/lookup-snapshot.csv';p.write_bytes(p.read_bytes()+b'5,changed\n');self.rehash('direct',p.name)
        with self.assertRaisesRegex(ValueError,'immutable lookup bytes'):self.verify()

    def test_the_held_process_loss_cannot_be_a_graceful_stop(self):
        self.change('flowchart','process.json',lambda x:x.update(heldPredecessorExitCode=0))
        with self.assertRaisesRegex(ValueError,'real process-loss'):self.verify()
