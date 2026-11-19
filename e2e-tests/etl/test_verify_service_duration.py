"""Corrupt-copy controls over actual native ETL/timer decisions and their coupled stores."""
from contextlib import closing
import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from pathlib import Path
_spec=importlib.util.spec_from_file_location('service_duration',Path(__file__).with_name('verify_service_duration.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/races/service-duration'

class ActualServiceDurationControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-service-duration-control-');self.addCleanup(self.temp.cleanup)
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
    def test_actual_worker_timer_and_held_interruption_orders_preserve_all_original_parents(self):
        r=self.verify();self.assertEqual(15,len(r['actualServiceDurationJourneys']));self.assertEqual(135,r['wholeStoresVerified'])
        self.assertTrue(r['actualBothWorkerTimerOrdersProved']);self.assertTrue(r['actualHeldFailureTimerInterruptionProved']);self.assertFalse(r['simultaneousCommitOrdersProved']);self.assertFalse(r['finalUpgradeAndStudioProved'])
    def test_a_real_parent_order_cannot_be_omitted(self):
        p=self.root/'manifest.json';s=json.loads(p.read_text());s['cases'].pop();v.q.write_json(p,s)
        with self.assertRaisesRegex(ValueError,'all real direct/native parent orders'):self.verify()
    def test_an_authoritative_timer_observation_cannot_be_omitted(self):
        p=self.root/'manifest.json';s=json.loads(p.read_text());s['cases'][0]['assets']=[a for a in s['cases'][0]['assets'] if not a['path'].endswith('/before-checked-timers.json')];v.q.write_json(p,s)
        with self.assertRaisesRegex(ValueError,'exact raw assets'):self.verify()
    def test_a_graceful_exit_cannot_replace_second_process_loss(self):
        self.change('timer-first-direct','process.json',lambda s:s.update(middleExitCode=0))
        with self.assertRaisesRegex(ValueError,'process-loss cuts'):self.verify()
    def test_a_captured_product_cannot_come_from_another_source(self):
        self.change('worker-first-direct','process.json',lambda s:s['products']['actualProductVersions'][0].update(productVersion='0.0.0+'+'0'*40))
        with self.assertRaisesRegex(ValueError,'products differ'):self.verify()
    def test_timer_acceptance_cannot_precede_its_actual_deadline(self):
        for name in ('finished-checked-timers.json','retained-checked-timers.json'):
            self.change('timer-first-direct',name,lambda s:next(r for r in s['inputs'] if r['inputKind']=='timer-elapsed').update(acceptedAt='2000-01-01T00:00:00Z'))
        with self.assertRaisesRegex(ValueError,'acceptance order'):self.verify()
    def test_worker_first_cannot_be_relabelled_as_a_second_timer_winner(self):
        for name in ('finished-checked-timers.json','retained-checked-timers.json'):
            self.change('worker-first-direct',name,lambda s:next(r for r in s['inputs'] if r['inputKind']=='timer-elapsed')['result'].update(status='Accepted',code=None))
        with self.assertRaisesRegex(ValueError,'acceptance order'):self.verify()
    def test_retired_fault_cannot_lose_its_original_semantic_snapshot(self):
        for name in ('finished-checked-timers.json','retained-checked-timers.json','finished-checked-model.json','retained-checked-model.json'):
            self.change('held-interruption-direct',name,lambda s:s['model']['payload']['failures'][0].update(semanticState={}))
        with self.assertRaisesRegex(ValueError,'held semantic snapshot'):self.verify()
    def test_timer_retired_failure_cannot_reopen_through_retry(self):
        self.change('held-interruption-root','retry-refusal.json',lambda s:s['outcome']['result'].update(status='Accepted',code=None))
        with self.assertRaisesRegex(ValueError,'reopened through retry'):self.verify()
    def test_original_parent_must_consume_the_typed_result(self):
        for name in ('finished-checked-embedding.json','retained-checked-embedding.json'):
            self.change('timer-first-sequence',name,lambda s:next(a for a in s['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn')['completion'].update(outcomeKey='Other'))
        with self.assertRaisesRegex(ValueError,'typed outcome consumption'):self.verify()
    def test_timer_ack_cannot_precede_its_exact_elapsed_receipt(self):
        for name in ('finished-checked-timers.json','retained-checked-timers.json'):
            self.change('worker-first-direct',name,lambda s:next(x for x in s['sources'] if x['intent']['kind']=='Elsa.Bpmn.DurationTimer.v1').update(deliveredAt='2000-01-01T00:00:00Z'))
        with self.assertRaisesRegex(ValueError,'ACKed before'):self.verify()
    def test_original_worker_pin_must_survive_in_the_physical_header(self):
        p=json.loads((self.root/'timer-first-direct/process.json').read_text());key=v.v.key(p['workflowExecutionId'])
        def change(c):
            s=json.loads(c.execute('SELECT ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(key,)).fetchone()[0]);s['pinnedExecutable']['artifactHash']=v.v.key('other')
            c.execute('UPDATE elsa_runtime_workflow_execution_state SET ContentJson=? WHERE WorkflowExecutionId=?',(json.dumps(s),key))
        self.store('timer-first-direct','finished','elsa',change)
        with self.assertRaisesRegex(ValueError,'complete pin'):self.verify()
    def test_the_physical_cancelled_etl_run_cannot_be_relabelled_as_success(self):
        self.store('timer-first-direct','finished','etl-runtime',lambda c:c.execute('UPDATE elsa_etl_runtime_runs SET Status=2'))
        with self.assertRaisesRegex(ValueError,'physical timed ETL invocation'):self.verify()
    def test_the_physical_timer_cannot_lose_its_immutable_not_before(self):
        p=json.loads((self.root/'worker-first-direct/process.json').read_text());key=v.v.key(p['timer']['sourceOutboxItemId'])
        def change(c):
            s=json.loads(c.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(key,)).fetchone()[0]);s['intent']['notBefore']=None
            c.execute('UPDATE elsa_runtime_post_commit_outbox SET ContentJson=? WHERE OutboxItemId=?',(json.dumps(s),key))
        self.store('worker-first-direct','before','elsa',change)
        with self.assertRaisesRegex(ValueError,'physical duration source'):self.verify()
    def test_a_physical_native_parent_cannot_lose_completion(self):
        p=json.loads((self.root/'worker-first-root/process.json').read_text())
        self.store('worker-first-root','finished','elsa',lambda c:c.execute('UPDATE elsa_runtime_workflow_execution_state SET Status=1 WHERE WorkflowExecutionId=?',(v.v.key(p['nativeParentExecutionId']),)))
        with self.assertRaisesRegex(ValueError,'physical timed native parent'):self.verify()
    def test_a_physical_model_cannot_change_its_pinned_attempt_budget(self):
        def change(c):
            for key,raw in c.execute('SELECT ArtifactId,ContentJson FROM elsa_runtime_workflow_executable').fetchall():
                s=json.loads(raw)
                if s.get('material'):
                    s['material']['definition']['serviceBindings'][0]['faultPolicy']['maxAttempts']=32
                    c.execute('UPDATE elsa_runtime_workflow_executable SET ContentJson=? WHERE ArtifactId=?',(json.dumps(s),key))
        self.store('held-interruption-direct','before','elsa',change)
        with self.assertRaisesRegex(ValueError,'complete explicit policy'):self.verify()
