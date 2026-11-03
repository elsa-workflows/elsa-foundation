import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from contextlib import closing
from pathlib import Path
_spec=importlib.util.spec_from_file_location('consumed_before_ack',Path(__file__).with_name('verify_http_recovery.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/consumed-before-ack'


class ActualConsumedBeforeAckControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-consumed-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def case(self,stage):return 'model-cancel' if stage=='model-command' else 'native-cancel'

    def rehash(self,stage,name):
        p=self.root/stage/'manifest.json';m=json.loads(p.read_text());relative=self.case(stage)+'/'+name
        next(a for a in m['assets'] if a['path']==relative)['sha256']=v.q.digest(p.parent/relative);v.q.write_json(p,m)
        p=self.root/'manifest.json';m=json.loads(p.read_text());next(c for c in m['cases'] if c['stage']==stage)['sha256']=v.q.digest(self.root/stage/'manifest.json');v.q.write_json(p,m)

    def change(self,stage,name,action):
        p=self.root/stage/self.case(stage)/name;value=json.loads(p.read_text());action(value);v.q.write_json(p,value);self.rehash(stage,name)

    def store(self,stage,action,phase='consumed',store='elsa'):
        p=self.root/stage/self.case(stage)/(phase+'-'+store+'.sqlite.gz');raw=Path(self.temp.name)/'changed.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(stage,p.name)
        def observer(value):
            s=value[phase+'Stores'][store];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=v.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change(stage,'process.json',observer)

    def verify(self):return v.verify(self.root/'manifest.json')

    def test_both_actual_ack_losses_preserve_exact_consumption_after_restart(self):
        result=self.verify();self.assertTrue(result['postConsumptionPreAckCrashProved']);self.assertFalse(result['pendingCancellationCrashProved'])
        self.assertEqual(24,result['wholeStoresVerified']);self.assertEqual(8,result['actualWorkbenchProcesses']);self.assertEqual(2,result['actualSdkProcesses'])
        self.assertFalse(result['runBpmnEtlIntegrationProved']);self.assertFalse(result['modelHeldFaultRetryProved'])

    def test_an_already_acknowledged_result_cannot_count_as_ack_loss(self):
        def acknowledged(value):
            next(s for s in value['sources'] if s['intent']['kind']=='Elsa.Bpmn.NativeWorkResult.v1').update(status=2,deliveredAt='2026-01-01T00:00:00+00:00')
        self.change('native-terminal','consumed-checked.json',acknowledged)
        with self.assertRaisesRegex(ValueError,'original result source still owed'):self.verify()

    def test_checked_consumption_cannot_hide_a_missing_physical_model_receipt(self):
        observed=json.loads((self.root/'model-command/model-cancel/consumed-checked.json').read_text())
        self.store('model-command',lambda c:c.execute('DELETE FROM elsa_runtime_model_input WHERE InputId=?',(v.key(observed['origin']['resultInputId']),)))
        with self.assertRaisesRegex(ValueError,'physical model accepted an unexpected input'):self.verify()

    def test_physical_source_ack_cannot_be_hidden_by_the_sdk_receipt(self):
        def acknowledged(c):
            rows=c.execute('SELECT Id,ContentJson FROM elsa_runtime_post_commit_outbox').fetchall();row,=[r for r in rows if json.loads(r[1])['intent']['kind']==v.key('Elsa.Bpmn.NativeWorkResult.v1')]
            value=json.loads(row[1]);value['status']='Delivered';c.execute('UPDATE elsa_runtime_post_commit_outbox SET Status=2,ContentJson=? WHERE Id=?',(json.dumps(value),row[0]))
        self.store('native-terminal',acknowledged)
        with self.assertRaisesRegex(ValueError,'physical native/model delivery source differs'):self.verify()

    def test_model_cancellation_ack_must_also_remain_owed_at_the_cut(self):
        def acknowledged(value):
            next(s for s in value['sources'] if s['intent']['kind']=='Elsa.Bpmn.CancelNativeWork.v1').update(status=2,deliveredAt='2026-01-01T00:00:00+00:00')
        self.change('model-command','consumed-checked.json',acknowledged)
        with self.assertRaisesRegex(ValueError,'still-owed original acknowledgment'):self.verify()

    def test_consumption_cannot_leave_the_actual_pipeline_running(self):
        self.store('model-command',lambda c:c.execute('UPDATE elsa_etl_runtime_runs SET Status=1'),store='etl-runtime')
        with self.assertRaisesRegex(ValueError,'left its original ETL invocation running'):self.verify()

    def test_a_different_sdk_product_cannot_claim_this_consumption(self):
        self.change('native-terminal','sdk-consume.json',lambda value:value['products'][4].update(informationalVersion='0.0.0+foreign-source'))
        with self.assertRaisesRegex(ValueError,'production operations, exact source'):self.verify()

    def test_a_faulted_or_wrong_protocol_dispatch_cannot_claim_consumption(self):
        self.change('native-terminal','sdk-consume.json',lambda value:value['steps'][-1]['drain'].update(status=3))
        with self.assertRaisesRegex(ValueError,'protocol-aware recovery mailbox'):self.verify()

    def test_the_crash_cut_must_follow_actual_sdk_consumption(self):
        self.change('model-command','process.json',lambda value:value.update(consumedCutAtUtc=value['pendingCutAtUtc']))
        with self.assertRaisesRegex(ValueError,'pre-ACK ordering'):self.verify()

    def test_a_remaining_recovery_wake_cannot_be_relabelled_settled(self):
        p=self.root/'model-command/model-cancel/consumed-elsa.sqlite.gz';raw=Path(self.temp.name)/'pending-wake.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:
            columns=[x[1] for x in c.execute('PRAGMA table_info(elsa_runtime_scheduler_work_item)')];rows=c.execute('SELECT * FROM elsa_runtime_scheduler_work_item').fetchall();self.assertEqual(1,len(rows))
        def retained(c):c.execute('INSERT INTO elsa_runtime_scheduler_work_item('+','.join(columns)+') VALUES('+','.join('?' for _ in columns)+')',rows[0])
        self.store('model-command',retained,phase='finished')
        with self.assertRaisesRegex(ValueError,'retained native/model scheduler work'):self.verify()
