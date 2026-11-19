import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from contextlib import closing
from pathlib import Path
_spec=importlib.util.spec_from_file_location('pending_parent',Path(__file__).with_name('verify_http_recovery.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/pending-native-parent-return'


class ActualPendingNativeParentReturnControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-pending-parent-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def rehash(self,case,name):
        p=self.root/case/'manifest.json';m=json.loads(p.read_text());next(a for a in m['assets'] if a['path']=='capture/'+name)['sha256']=v.q.digest(p.parent/'capture'/name);v.q.write_json(p,m)
        p=self.root/'manifest.json';m=json.loads(p.read_text());next(c for c in m['cases'] if c['name']==case)['sha256']=v.q.digest(self.root/case/'manifest.json');v.q.write_json(p,m)

    def change(self,case,name,action):
        p=self.root/case/'capture'/name;value=json.loads(p.read_text());action(value);v.q.write_json(p,value);self.rehash(case,name)

    def store(self,case,action):
        p=self.root/case/'capture/parent-pending-elsa.sqlite.gz';raw=Path(self.temp.name)/'changed.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(case,p.name)
        def observer(value):
            s=value['parentPendingStores']['elsa'];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=v.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change(case,'process.json',observer)

    def verify(self):return v.verify(self.root/'manifest.json')

    def test_all_actual_cancelled_native_parents_recover_the_original_owed_return(self):
        result=self.verify();self.assertEqual(36,result['wholeStoresVerified']);self.assertTrue(result['pendingParentResultCrashProved'])
        self.assertTrue(result['allNativeParentCancellationPendingReturnProved']);self.assertFalse(result['successParentResultCrashProved']);self.assertFalse(result['modelHeldFaultRetryProved'])
        self.assertEqual(['Root','Sequence','Flowchart','CompensationScope'],result['actualCancelledNativeParentShapes'])

    def test_the_parent_result_must_still_be_unacknowledged_at_the_second_crash(self):
        def ack(value):next(s for s in value['sources'] if s['intent']['kind']=='Elsa.Bpmn.ResumeNativeParent.v1').update(status=2,deliveredAt='2026-10-29T00:00:00+00:00')
        self.change('root','parent-pending-checked-embedding.json',ack)
        with self.assertRaisesRegex(ValueError,'original unacknowledged result sources'):self.verify()

    def test_the_native_result_source_cannot_already_be_acknowledged(self):
        self.change('sequence','parent-pending-checked-model.json',lambda value:value['resultSource'].update(status=2,deliveredAt='2026-10-29T00:00:00+00:00'))
        with self.assertRaisesRegex(ValueError,'SDK receipt differs|original unacknowledged result sources'):self.verify()

    def test_a_preexisting_parent_settlement_receipt_invalidates_the_cut(self):
        final=json.loads((self.root/'flowchart/capture/finished-checked-embedding.json').read_text());settled=next(r for r in final['inputs'] if r['inputKind']=='native-parent-settled')
        self.change('flowchart','parent-pending-checked-embedding.json',lambda value:value['inputs'].append(settled))
        with self.assertRaisesRegex(ValueError,'original unacknowledged result sources'):self.verify()

    def test_owed_worker_cancellation_cannot_claim_a_settled_downstream_cut(self):
        self.change('root','parent-pending-checked-model.json',lambda value:value['model']['payload']['cancellations'][0].update(settled=False))
        with self.assertRaisesRegex(ValueError,'terminal model, exact worker consumption or settled downstream responsibility'):self.verify()

    def test_the_actual_pending_etl_store_cannot_be_omitted_from_verification(self):
        self.change('sequence','process.json',lambda value:value['parentPendingStores'].pop('etl-runtime'))
        with self.assertRaisesRegex(ValueError,'all three coupled stores'):self.verify()

    def test_inspected_pending_state_cannot_hide_physical_model_settlement(self):
        self.store('flowchart',lambda c:c.execute('UPDATE elsa_runtime_model_execution SET Lifecycle=3'))
        with self.assertRaisesRegex(ValueError,'physical model state differs'):self.verify()

    def test_the_physical_native_parent_must_keep_its_original_pin(self):
        process=json.loads((self.root/'compensationscope/capture/process.json').read_text())
        def rebound(c):
            identity=v.key(process['nativeParentExecutionId']);row=c.execute('SELECT ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(identity,)).fetchone();value=json.loads(row[0]);value['pinnedExecutable']['artifactHash']=v.key('different-hash');c.execute('UPDATE elsa_runtime_workflow_execution_state SET ContentJson=? WHERE WorkflowExecutionId=?',(json.dumps(value),identity))
        self.store('compensationscope',rebound)
        with self.assertRaisesRegex(ValueError,'physical pending native parent lost'):self.verify()

    def test_an_old_sdk_cannot_claim_source_matched_consumption(self):
        self.change('root','sdk-consume.json',lambda value:value['products'][0].update(informationalVersion='old-build'))
        with self.assertRaisesRegex(ValueError,'SDK receipt differs'):self.verify()

    def test_one_parent_cannot_be_counted_twice(self):
        p=self.root/'manifest.json';value=json.loads(p.read_text());value['cases'][1]=value['cases'][0];v.q.write_json(p,value)
        with self.assertRaisesRegex(ValueError,'all unique native parent shapes'):self.verify()
