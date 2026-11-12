"""Rehashed corrupt-copy controls for actual competing held retry/cancel recovery."""
from contextlib import closing
import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from pathlib import Path
_spec=importlib.util.spec_from_file_location('held_command_race',Path(__file__).with_name('verify_held_command_race.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/races/held-command-race'

class ActualHeldCommandRaceControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-held-race-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)
    def rehash(self,case,name):
        p=self.root/'manifest.json';m=json.loads(p.read_text());row=next(c for c in m['cases'] if c['name']==case)
        next(a for a in row['assets'] if a['path']==case+'/'+name)['sha256']=v.q.digest(self.root/case/name);v.q.write_json(p,m)
    def change(self,case,name,action):
        p=self.root/case/name;s=json.loads(p.read_text());action(s);v.q.write_json(p,s);self.rehash(case,name)
    def store(self,case,phase,store,action):
        p=self.root/case/(phase+'-'+store+'.sqlite.gz');raw=Path(self.temp.name)/'db';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(case,p.name)
        def update(s):
            a=s[phase+'Stores'][store];a['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();a['gzipSha256']=v.q.digest(p);a['observer']['snapshotSha256']=a['databaseSha256']
        self.change(case,'process.json',update)
    def verify(self):return v.verify(self.root/'manifest.json')
    def test_actual_both_orders_survive_stale_ownership_and_cold_recovery_for_every_parent(self):
        r=self.verify();self.assertEqual(10,len(r['actualConcurrentSubmissionJourneys']));self.assertEqual(120,r['wholeStoresVerified'])
        self.assertTrue(r['bothFifoDecisionOrdersProved']);self.assertFalse(r['twoSimultaneousProviderProposalsManufactured']);self.assertFalse(r['finalUpgradeAndStudioProved'])
    def test_an_order_or_parent_cannot_be_omitted(self):
        p=self.root/'manifest.json';s=json.loads(p.read_text());s['cases'].pop();v.q.write_json(p,s)
        with self.assertRaisesRegex(ValueError,'both unique orders'):self.verify()
    def test_an_authoritative_proposal_cannot_be_omitted(self):
        p=self.root/'manifest.json';s=json.loads(p.read_text());s['cases'][0]['assets']=[a for a in s['cases'][0]['assets'] if not a['path'].endswith('/race-proposal.json')];v.q.write_json(p,s)
        with self.assertRaisesRegex(ValueError,'authoritative raw asset'):self.verify()
    def test_a_graceful_exit_cannot_replace_original_process_loss(self):
        self.change('retry-first-direct','process.json',lambda s:s.update(heldPredecessorExitCode=0))
        with self.assertRaisesRegex(ValueError,'process-loss cuts'):self.verify()
    def test_the_sdk_cannot_run_products_from_a_foreign_source(self):
        self.change('retry-first-direct','race-competitor.json',lambda s:s['products'][0].update(informationalVersion='1.0.0+'+'0'*40))
        with self.assertRaisesRegex(ValueError,'source identity'):self.verify()
    def test_the_hook_cannot_change_the_actual_proposal(self):
        self.change('cancel-first-direct','race-release.json',lambda s:s.update(proposalUnchanged=False))
        with self.assertRaisesRegex(ValueError,'unchanged actual proposal'):self.verify()
    def test_the_competitor_cannot_be_reported_consumed_before_recovery(self):
        self.change('retry-first-direct','race-competitor.json',lambda s:s['outcome'].update(pending=False,result={'status':'Accepted'}))
        with self.assertRaisesRegex(ValueError,'durable pending admission'):self.verify()
    def test_the_original_fifo_cannot_be_reversed(self):
        self.change('cancel-first-direct','raced-checked-model.json',lambda s:s['queuedModelInputs'].reverse())
        with self.assertRaisesRegex(ValueError,'protected FIFO'):self.verify()
    def test_the_cancel_first_retry_cannot_be_relabelled_accepted(self):
        for name in ('finished.json','retained.json'):
            self.change('cancel-first-direct',name,lambda s:s['retry']['result'].update(status='Accepted',code=None))
        with self.assertRaisesRegex(ValueError,'exact accepted/rejected'):self.verify()
    def test_start_ack_must_follow_positive_retirement(self):
        for name in ('finished-checked-inputs.json','retained-checked-inputs.json'):
            self.change('retry-first-direct',name,lambda s:next(x for x in s['sources'] if x['intent']['kind']=='Elsa.Bpmn.CancelNativeWork.v1').update(deliveredAt='2000-01-01T00:00:00Z'))
        with self.assertRaisesRegex(ValueError,'acknowledged without'):self.verify()
    def test_the_physical_original_worker_must_keep_its_pin(self):
        p=json.loads((self.root/'retry-first-direct/process.json').read_text());key=v.v.key(p['workflowExecutionId'])
        def change(c):
            s=json.loads(c.execute('SELECT ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(key,)).fetchone()[0]);s['pinnedExecutable']['artifactHash']=v.v.key('other')
            c.execute('UPDATE elsa_runtime_workflow_execution_state SET ContentJson=? WHERE WorkflowExecutionId=?',(json.dumps(s),key))
        self.store('retry-first-direct','finished','elsa',change)
        with self.assertRaisesRegex(ValueError,'physical original worker'):self.verify()
    def test_the_actual_competitor_must_advance_the_ownership_fence(self):
        p=json.loads((self.root/'retry-first-direct/process.json').read_text());proposal=json.loads((self.root/'retry-first-direct/race-proposal.json').read_text());key=v.v.key(p['modelExecutionId'])
        def change(c):
            s=json.loads(c.execute('SELECT ContentJson FROM elsa_runtime_execution_liveness_state WHERE WorkflowExecutionId=?',(key,)).fetchone()[0]);s['metadata'][v.v.key('runtime.ownership.fencingToken')]=v.v.key(str(proposal['ownership']['fencingToken']))
            c.execute('UPDATE elsa_runtime_execution_liveness_state SET ContentJson=? WHERE WorkflowExecutionId=?',(json.dumps(s),key))
        self.store('retry-first-direct','raced','elsa',change)
        with self.assertRaisesRegex(ValueError,'supersede and release'):self.verify()
    def test_the_physical_failed_etl_cannot_be_relabelled_success(self):
        self.store('cancel-first-direct','finished','etl-runtime',lambda c:c.execute('UPDATE elsa_etl_runtime_runs SET Status=2'))
        with self.assertRaisesRegex(ValueError,'physical old/new ETL terminal'):self.verify()
    def test_the_original_result_receipt_cannot_disappear_physically(self):
        receipt=json.loads((self.root/'retry-first-direct/held-checked-model.json').read_text())['resultInput']
        self.store('retry-first-direct','raced','elsa',lambda c:c.execute('DELETE FROM elsa_runtime_model_input WHERE InputId=?',(v.v.key(receipt['inputId']),)))
        with self.assertRaisesRegex(ValueError,'lost or invented an input receipt'):self.verify()
    def test_the_native_parent_must_remain_physically_cancelled(self):
        p=json.loads((self.root/'cancel-first-root/process.json').read_text())
        self.store('cancel-first-root','finished','elsa',lambda c:c.execute('UPDATE elsa_runtime_workflow_execution_state SET Status=1 WHERE WorkflowExecutionId=?',(v.v.key(p['nativeParentExecutionId']),)))
        with self.assertRaisesRegex(ValueError,'physical native parent'):self.verify()
    def test_the_physical_model_cannot_change_its_pinned_attempt_budget(self):
        def change(c):
            for key,raw in c.execute('SELECT ArtifactId,ContentJson FROM elsa_runtime_workflow_executable').fetchall():
                s=json.loads(raw)
                if s.get('material'):
                    s['material']['definition']['serviceBindings'][0]['faultPolicy']['maxAttempts']=3
                    c.execute('UPDATE elsa_runtime_workflow_executable SET ContentJson=? WHERE ArtifactId=?',(json.dumps(s),key))
        self.store('retry-first-direct','before','elsa',change)
        with self.assertRaisesRegex(ValueError,'pinned attempt policy'):self.verify()
