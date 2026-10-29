import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from contextlib import closing
from pathlib import Path
_spec=importlib.util.spec_from_file_location('success_parent',Path(__file__).with_name('verify_http_recovery.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/successful-native-parent-return'


class ActualSuccessfulNativeParentReturnControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-success-parent-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def rehash(self,case,name):
        p=self.root/case/'manifest.json';m=json.loads(p.read_text());next(a for a in m['assets'] if a['path']=='capture/'+name)['sha256']=v.q.digest(p.parent/'capture'/name);v.q.write_json(p,m)
        p=self.root/'manifest.json';m=json.loads(p.read_text());next(c for c in m['cases'] if c['name']==case)['sha256']=v.q.digest(self.root/case/'manifest.json');v.q.write_json(p,m)

    def change(self,case,name,action):
        p=self.root/case/'capture'/name;value=json.loads(p.read_text());action(value);v.q.write_json(p,value);self.rehash(case,name)

    def store(self,case,store,action):
        p=self.root/case/'capture'/('parent-pending-'+store+'.sqlite.gz');raw=Path(self.temp.name)/'changed.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(case,p.name)
        def observer(value):
            s=value['parentPendingStores'][store];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=v.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change(case,'process.json',observer)

    def verify(self):return v.verify(self.root/'manifest.json')

    def test_all_actual_parent_shapes_recover_each_successful_consumption_settlement_ack_boundary(self):
        result=self.verify();self.assertEqual(108,result['wholeStoresVerified']);self.assertEqual(12,len(result['actualSuccessfulNativeParentReturnJourneys']))
        self.assertTrue(result['allNativeParentSuccessConsumptionSettlementAndPreAckCrashProved']);self.assertFalse(result['modelHeldFaultRetryProved'])

    def test_a_pending_parent_cannot_claim_its_native_invocation_was_consumed(self):
        self.change('root-pending','parent-pending-checked-embedding.json',lambda value:value['nativeParent'].update(status=3))
        with self.assertRaisesRegex(ValueError,'actual native parent invocation, pin or consumption state'):self.verify()

    def test_native_parent_completion_requires_actual_typed_RunBpmn_consumption(self):
        def remove(value):next(a for a in value['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn')['triggerDeliveries']=[]
        self.change('sequence-consumed','parent-pending-checked-embedding.json',remove)
        self.change('sequence-consumed','sdk-consume.json',lambda value:value['parentActivity'].update(triggerDeliveries=[]))
        with self.assertRaisesRegex(ValueError,'actual typed RunBpmn consumption'):self.verify()

    def test_source_ack_must_still_be_owed_after_the_settlement_receipt(self):
        def ack(source):source.update(status=2,deliveredAt='2026-10-29T00:00:00+00:00')
        self.change('flowchart-settled','parent-pending-checked-embedding.json',lambda value:ack(next(s for s in value['sources'] if s['intent']['kind']=='Elsa.Bpmn.ResumeNativeParent.v1')))
        self.change('flowchart-settled','sdk-consume.json',lambda value:ack(value['parentResultSource']))
        with self.assertRaisesRegex(ValueError,'original unacknowledged source or positive delivery fence'):self.verify()

    def test_inspected_model_settlement_cannot_hide_a_physically_pending_model(self):
        self.store('compensationscope-settled','elsa',lambda c:c.execute('UPDATE elsa_runtime_model_execution SET Lifecycle=2,Revision=2'))
        with self.assertRaisesRegex(ValueError,'physical model state differs'):self.verify()

    def test_queued_settlement_cannot_be_rebound_to_a_foreign_source(self):
        def rebound(value):
            work,=value['queuedModelInputs'];work['payload']['payload']['sourceOutboxItemId']='foreign-parent-result'
        self.change('root-consumed','parent-pending-checked-embedding.json',rebound)
        with self.assertRaisesRegex(ValueError,'original source, pins or captured authority'):self.verify()

    def test_native_consumption_must_keep_its_original_revision_and_exact_receipt(self):
        self.change('sequence-pending','parent-pending-checked-model.json',lambda value:value['resultInput'].update(acceptedRevision=3))
        with self.assertRaisesRegex(ValueError,'exact original native consumption or selected model settlement stage'):self.verify()

    def test_physical_etl_terminal_truth_cannot_be_replaced_by_model_status(self):
        self.store('flowchart-consumed','etl-runtime',lambda c:c.execute('UPDATE elsa_etl_runtime_runs SET Status=5'))
        with self.assertRaisesRegex(ValueError,'real terminal, output accounting or original result address'):self.verify()

    def test_the_actual_parent_source_must_retain_its_positive_physical_fence(self):
        def remove(c):
            rows=c.execute('SELECT Id,ContentJson FROM elsa_runtime_post_commit_outbox').fetchall();row,=[r for r in rows if json.loads(r[1])['intent']['kind']==v.key('Elsa.Bpmn.ResumeNativeParent.v1')];value=json.loads(row[1]);value['deliveryFencingToken']=0;c.execute('UPDATE elsa_runtime_post_commit_outbox SET ContentJson=? WHERE Id=?',(json.dumps(value),row[0]))
        self.store('root-settled','elsa',remove)
        with self.assertRaisesRegex(ValueError,'physical successful parent source disagrees'):self.verify()

    def test_one_case_cannot_inflate_the_full_successful_parent_stage_matrix(self):
        p=self.root/'manifest.json';value=json.loads(p.read_text());value['cases'][1]=value['cases'][0];v.q.write_json(p,value)
        with self.assertRaisesRegex(ValueError,'every unique actual parent and consumption/settlement/ACK stage'):self.verify()

    def test_swapping_shape_labels_cannot_fake_two_distinct_native_composite_journeys(self):
        self.change('sequence-consumed','process.json',lambda value:value.update(embeddedParent='Flowchart'))
        self.change('flowchart-consumed','process.json',lambda value:value.update(embeddedParent='Sequence'))
        p=self.root/'manifest.json';value=json.loads(p.read_text())
        for case in value['cases']:
            if case['name']=='sequence-consumed':case['parent']='Flowchart'
            elif case['name']=='flowchart-consumed':case['parent']='Sequence'
        v.q.write_json(p,value)
        with self.assertRaisesRegex(ValueError,'physical native parent root differs'):self.verify()

    def test_observed_typed_consumption_cannot_hide_its_missing_physical_receipt(self):
        observed=json.loads((self.root/'root-consumed/capture/parent-pending-checked-embedding.json').read_text());activity=next(a for a in observed['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn')
        def remove(c):
            identity=v.key(activity['invocationId']);row=c.execute('SELECT ContentJson FROM elsa_runtime_activity_execution_state WHERE ActivityExecutionId=?',(identity,)).fetchone();value=json.loads(row[0]);value['triggerDeliveries']=[];c.execute('UPDATE elsa_runtime_activity_execution_state SET ContentJson=? WHERE ActivityExecutionId=?',(json.dumps(value),identity))
        self.store('root-consumed','elsa',remove)
        with self.assertRaisesRegex(ValueError,'physical successful parent lost its exact typed consumption'):self.verify()
