import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from contextlib import closing
from pathlib import Path
_spec=importlib.util.spec_from_file_location('pending_cancellation',Path(__file__).with_name('verify_http_recovery.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/pending-cancellation'


class ActualPendingCancellationControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-pending-cancel-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def case(self,stage):return 'model-cancel' if stage=='model-command' else 'native-cancel'

    def rehash(self,stage,name):
        p=self.root/stage/'manifest.json';m=json.loads(p.read_text());relative=self.case(stage)+'/'+name
        next(a for a in m['assets'] if a['path']==relative)['sha256']=v.q.digest(p.parent/relative);v.q.write_json(p,m)
        p=self.root/'manifest.json';m=json.loads(p.read_text());next(c for c in m['cases'] if c['stage']==stage)['sha256']=v.q.digest(self.root/stage/'manifest.json');v.q.write_json(p,m)

    def change(self,stage,name,action):
        p=self.root/stage/self.case(stage)/name;value=json.loads(p.read_text());action(value);v.q.write_json(p,value);self.rehash(stage,name)

    def store(self,stage,action,store='elsa'):
        p=self.root/stage/self.case(stage)/('pending-'+store+'.sqlite.gz');raw=Path(self.temp.name)/'changed.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(stage,p.name)
        def observer(value):
            s=value['pendingStores'][store];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=v.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change(stage,'process.json',observer)

    def verify(self):return v.verify(self.root/'manifest.json')

    def test_both_real_pending_deliveries_survive_second_process_loss(self):
        result=self.verify();self.assertTrue(result['pendingCancellationCrashProved']);self.assertEqual(['model-command','native-terminal'],result['actualPendingCancellationStages'])
        self.assertEqual(18,result['wholeStoresVerified']);self.assertEqual(8,result['actualWorkbenchProcesses'])
        self.assertFalse(result['postConsumptionPreAckCrashProved']);self.assertFalse(result['runBpmnEtlIntegrationProved']);self.assertFalse(result['modelHeldFaultRetryProved'])

    def test_a_second_clean_shutdown_cannot_count_as_second_process_loss(self):
        self.change('model-command','process.json',lambda value:value.update(pendingPredecessorExitCode=0))
        with self.assertRaisesRegex(ValueError,'second crash lost'):self.verify()

    def test_model_cancel_cannot_already_be_acknowledged_before_worker_delivery(self):
        def early(value):
            next(s for s in value['sources'] if s['intent']['kind']=='Elsa.Bpmn.CancelNativeWork.v1').update(status=2,deliveredAt='2026-01-01T00:00:00+00:00')
        self.change('model-command','pending-checked.json',early)
        with self.assertRaisesRegex(ValueError,'before worker delivery'):self.verify()

    def test_native_result_cannot_be_acknowledged_before_model_consumption(self):
        def early(value):
            next(s for s in value['sources'] if s['intent']['kind']=='Elsa.Bpmn.NativeWorkResult.v1').update(status=2,deliveredAt='2026-01-01T00:00:00+00:00')
        self.change('native-terminal','pending-checked.json',early)
        with self.assertRaisesRegex(ValueError,'same-checkpoint ETL cancellation'):self.verify()

    def test_pending_result_must_still_precede_its_exact_consumption(self):
        self.change('native-terminal','pending-checked.json',lambda value:value['inputs'].append({'inputKind':'native-result'}))
        with self.assertRaisesRegex(ValueError,'already consumed before'):self.verify()

    def test_native_result_cannot_drop_its_actual_etl_cancellation_inventory(self):
        for phase in ('finished','retained'):
            def drop(value):
                value['model']['payload']['nativeResults'][0]['cancellationSources']=[]
                value['resultSource']['intent']['payload']['cancellationSources']=[]
            self.change('native-terminal',phase+'-checked-model.json',drop)
        def drop_pending(value):
            next(s for s in value['sources'] if s['intent']['kind']=='Elsa.Bpmn.NativeWorkResult.v1')['intent']['payload']['cancellationSources']=[]
        self.change('native-terminal','pending-checked.json',drop_pending)
        with self.assertRaisesRegex(ValueError,'complete cancellation inventory'):self.verify()

    def test_physical_pending_etl_cannot_have_already_cancelled(self):
        self.store('native-terminal',lambda c:c.execute('UPDATE elsa_etl_runtime_runs SET Status=5'),store='etl-runtime')
        with self.assertRaisesRegex(ValueError,'original active invocation'):self.verify()

    def test_pending_native_result_must_retain_its_actual_terminal_checkpoint(self):
        p=self.root/'native-terminal/native-cancel/pending-checked.json';observed=json.loads(p.read_text());native=next(s for s in observed['sources'] if s['intent']['kind']=='Elsa.Bpmn.NativeWorkResult.v1')
        suffix=':'+native['intent']['intentId'];commit=native['outboxItemId'][:-len(suffix)]
        self.store('native-terminal',lambda c:c.execute('DELETE FROM elsa_runtime_checkpoint_commit WHERE CommitId=?',(v.key(commit),)))
        with self.assertRaisesRegex(ValueError,'original pending cancel/result responsibility'):self.verify()

    def test_checked_pending_sources_cannot_hide_a_physical_premature_ack(self):
        def early(c):
            rows=c.execute('SELECT Id,ContentJson FROM elsa_runtime_post_commit_outbox').fetchall()
            row,=[x for x in rows if json.loads(x[1])['intent']['kind']==v.key('Elsa.Bpmn.CancelNativeWork.v1')]
            value=json.loads(row[1]);value['status']='Delivered';c.execute('UPDATE elsa_runtime_post_commit_outbox SET ContentJson=?,Status=2 WHERE Id=?',(json.dumps(value),row[0]))
        self.store('model-command',early)
        with self.assertRaisesRegex(ValueError,'owed acknowledgment state'):self.verify()

    def test_frozen_retry_cannot_be_replaced_for_an_easier_pending_cut(self):
        def reset(value):
            next(p['http']['fetch'] for p in value['partitions']['items'] if p['http']['fetch'] and p['http']['fetch']['lastStatusCode']==429)['notBefore']='2027-01-01T00:00:00+00:00'
        self.change('native-terminal','pending.json',reset)
        with self.assertRaisesRegex(ValueError,'frozen HTTP retry'):self.verify()
