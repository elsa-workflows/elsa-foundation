import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from contextlib import closing
from pathlib import Path
_spec=importlib.util.spec_from_file_location('admission_race',Path(__file__).with_name('verify_native_admission_race.py'))
r=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(r)
EVIDENCE=r.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/native-admission-race'


class ActualNativeAdmissionRaceControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-native-race-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def rehash(self,order,name):
        p=self.root/order/'manifest.json';m=json.loads(p.read_text());next(a for a in m['assets'] if a['path']==name)['sha256']=r.q.digest(p.parent/name);r.q.write_json(p,m)
        p=self.root/'manifest.json';m=json.loads(p.read_text());next(a for a in m['cases'] if a['order']==order)['sha256']=r.q.digest(self.root/order/'manifest.json');r.q.write_json(p,m)

    def change(self,order,name,action):
        p=self.root/order/name;value=json.loads(p.read_text());action(value);r.q.write_json(p,value);self.rehash(order,name)

    def store(self,order,phase,action):
        p=self.root/order/(phase+'-elsa.sqlite.gz');raw=Path(self.temp.name)/'changed.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(order,p.name)
        def observed(value):
            s=value[phase+'Stores']['elsa'];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=r.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change(order,'process.json',observed)

    def verify(self):return r.verify(self.root/'manifest.json')

    def test_both_actual_commit_orders_recover_without_claiming_later_integration(self):
        value=self.verify();self.assertTrue(value['admissionRaceProved']);self.assertEqual(['cancel-first','admit-first'],value['actualCommitOrders'])
        self.assertEqual(18,value['wholeStoresVerified']);self.assertEqual(1,value['nativeWorkflowsAdmitted']);self.assertEqual(0,value['etlRunsAdmitted'])
        self.assertFalse(value['pendingResultCrashProved']);self.assertFalse(value['runBpmnEtlIntegrationProved']);self.assertFalse(value['modelHeldFaultRetryProved'])

    def test_a_removed_atomic_native_model_guard_cannot_count_as_a_race(self):
        self.change('cancel-first','race-proposal.json',lambda value:value['proposal'].pop('expectedModelExecution'))
        with self.assertRaisesRegex(ValueError,'revision-1 admission guard'):self.verify()

    def test_a_proposal_observed_after_the_competitor_cannot_count_as_concurrency(self):
        competitor=json.loads((self.root/'cancel-first/race-competitor.json').read_text())
        self.change('cancel-first','race-proposal.json',lambda value:value.update(observedAtUtc=competitor['completedAtUtc']))
        with self.assertRaisesRegex(ValueError,'observed before the competitor'):self.verify()

    def test_releasing_a_rewritten_proposal_is_not_the_observed_race(self):
        self.change('admit-first','race-release.json',lambda value:value.update(proposalFingerprint='f'*64))
        with self.assertRaisesRegex(ValueError,'unchanged proposal'):self.verify()

    def test_native_admission_must_have_won_before_the_cancellation_released(self):
        self.change('admit-first','race-competing-checked.json',lambda value:value['model'].update(revision=2))
        with self.assertRaisesRegex(ValueError,'still observed revision 1'):self.verify()

    def test_an_unbounded_sdk_drain_or_fault_cannot_count_as_admission_only(self):
        self.change('admit-first','race-competitor.json',lambda value:value.update(outcome={'stoppedOnFault':True,'drainedCount':4}))
        with self.assertRaisesRegex(ValueError,'still observed revision 1'):self.verify()

    def test_an_admitted_worker_cannot_be_relabelled_not_admitted(self):
        for phase in ('finished','retained'):
            self.change('admit-first',phase+'-checked.json',lambda value:value['model']['payload']['cancellations'][0].update(notAdmitted=True))
        with self.assertRaisesRegex(ValueError,'fabricated retirement'):self.verify()

    def test_checked_worker_truth_cannot_hide_a_missing_physical_header(self):
        self.store('admit-first','pending',lambda c:c.execute('DELETE FROM elsa_runtime_workflow_execution_state'))
        with self.assertRaisesRegex(ValueError,'physical native admission contradicts'):self.verify()

    def test_an_admission_marker_cannot_be_deleted_from_the_real_store(self):
        self.store('admit-first','pending',lambda c:c.execute('DELETE FROM elsa_runtime_checkpoint_commit WHERE WorkflowExecutionId=?',(r.v.key(json.loads((self.root/'admit-first/process.json').read_text())['origin']['workerExecutionId']),)))
        with self.assertRaisesRegex(ValueError,'lost an original native checkpoint'):self.verify()

    def test_cancel_ack_cannot_precede_the_exact_native_consumption_receipt(self):
        for phase in ('finished','retained'):
            def early(value):
                next(s for s in value['sources'] if s['intent']['kind']==r.CANCEL)['deliveredAt']='2026-01-01T00:00:00+00:00'
            self.change('admit-first',phase+'-checked.json',early)
        with self.assertRaisesRegex(ValueError,'exact retained settlement receipt'):self.verify()

    def test_rehashed_queue_cannot_point_to_another_native_invocation(self):
        def redirect(c):
            row=c.execute('SELECT Id,ContentJson FROM elsa_runtime_scheduler_work_item').fetchone();value=json.loads(row[1]);value['payload']['ModelWorkOrigin']['AttemptId']='another-attempt';c.execute('UPDATE elsa_runtime_scheduler_work_item SET ContentJson=? WHERE Id=?',(json.dumps(value),row[0]))
        self.store('cancel-first','pending',redirect)
        with self.assertRaisesRegex(ValueError,'protected queued native command'):self.verify()
