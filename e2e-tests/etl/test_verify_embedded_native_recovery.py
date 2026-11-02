import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from contextlib import closing
from pathlib import Path
_spec=importlib.util.spec_from_file_location('embedded_etl',Path(__file__).with_name('verify_http_recovery.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/embedded-native-recovery'


class ActualEmbeddedNativeRecoveryControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-embedded-etl-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def rehash(self,case,name):
        p=self.root/case/'manifest.json';m=json.loads(p.read_text());next(a for a in m['assets'] if a['path']=='capture/'+name)['sha256']=v.q.digest(p.parent/'capture'/name);v.q.write_json(p,m)
        p=self.root/'manifest.json';m=json.loads(p.read_text());next(c for c in m['cases'] if c['name']==case)['sha256']=v.q.digest(self.root/case/'manifest.json');v.q.write_json(p,m)

    def change(self,case,name,action):
        p=self.root/case/'capture'/name;value=json.loads(p.read_text());action(value);v.q.write_json(p,value);self.rehash(case,name)

    def store(self,case,phase,action):
        p=self.root/case/'capture'/(phase+'-elsa.sqlite.gz');raw=Path(self.temp.name)/'changed.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(case,p.name)
        def observer(value):
            s=value[phase+'Stores']['elsa'];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=v.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change(case,'process.json',observer)

    def verify(self):return v.verify(self.root/'manifest.json')

    def test_all_actual_native_parents_complete_and_cancel_the_same_service_lifecycle(self):
        result=self.verify();self.assertTrue(result['runBpmnEtlIntegrationProved']);self.assertTrue(result['allNativeParentCompletionAndCancellationProved'])
        self.assertEqual(8,len(result['actualRunBpmnEtlJourneys']));self.assertEqual(54,result['wholeStoresVerified'])
        self.assertTrue(result['actualDirectSuccessCounterpartVerified'])
        self.assertEqual(['Root','Sequence','Flowchart','CompensationScope'],result['sameSourceInterpreterSuccessTraceComparedParents'])
        self.assertFalse(result['pendingParentResultCrashProved']);self.assertFalse(result['modelHeldFaultRetryProved'])

    def test_inspected_parent_truth_cannot_hide_an_absent_physical_native_parent(self):
        p=json.loads((self.root/'sequence-success/capture/process.json').read_text())
        self.store('sequence-success','before',lambda c:c.execute('DELETE FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(v.key(p['nativeParentExecutionId']),)))
        with self.assertRaisesRegex(ValueError,'physical RunBpmn parent lost'):self.verify()

    def test_the_model_must_address_the_actual_native_registration(self):
        self.change('root-success','before-checked-embedding.json',lambda value:value['bookmarks'][0].update(stimulusHash='foreign-model'))
        with self.assertRaisesRegex(ValueError,'actual native registration'):self.verify()

    def test_parent_ack_must_follow_its_exact_model_settlement(self):
        for phase in ('finished','retained'):
            def early(value):next(s for s in value['sources'] if s['intent']['kind']=='Elsa.Bpmn.ResumeNativeParent.v1')['deliveredAt']='2026-01-01T00:00:00+00:00'
            self.change('flowchart-success',phase+'-checked-embedding.json',early)
        with self.assertRaisesRegex(ValueError,'exact native parent settlement proof'):self.verify()

    def test_the_native_parent_must_retain_the_full_model_dependency(self):
        process=json.loads((self.root/'sequence-success/capture/process.json').read_text())
        def remove(c):
            identity=v.key(process['nativeParentPublication']['artifactId']);row=c.execute('SELECT Id,ContentJson FROM elsa_runtime_workflow_executable WHERE ArtifactId=?',(identity,)).fetchone();value=json.loads(row[1]);value['dependencies']=[];c.execute('UPDATE elsa_runtime_workflow_executable SET ContentJson=? WHERE Id=?',(json.dumps(value),row[0]))
        self.store('sequence-success','before',remove)
        with self.assertRaisesRegex(ValueError,'retained model dependency closure'):self.verify()

    def test_completed_RunBpmn_must_have_its_actual_typed_consumption_receipt(self):
        for phase in ('finished','retained'):
            def remove(value):next(a for a in value['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn')['triggerDeliveries']=[]
            self.change('root-success',phase+'-checked-embedding.json',remove)
        with self.assertRaisesRegex(ValueError,'exact typed RunBpmn consumption proof'):self.verify()

    def test_an_old_human_frame_cannot_claim_native_service_embedding(self):
        for name in ('before-checked-model.json','before-checked-embedding.json'):
            self.change('sequence-success',name,lambda value:value['model']['payload'].update(formatVersion=3))
        with self.assertRaisesRegex(ValueError,'pins, format or retained invocation'):self.verify()

    def test_model_settlement_cannot_precede_downstream_etl_cancellation(self):
        def late(c):
            rows=c.execute('SELECT Id,ContentJson FROM elsa_runtime_post_commit_outbox').fetchall();row,=[r for r in rows if json.loads(r[1])['intent']['kind']==v.key('Elsa.Etl.Cancel.v1')];value=json.loads(row[1]);value['deliveredAt']='2027-01-01T00:00:00+00:00';c.execute('UPDATE elsa_runtime_post_commit_outbox SET ContentJson=? WHERE Id=?',(json.dumps(value),row[0]))
        self.store('compensationscope-parent-cancel','finished',late)
        with self.assertRaisesRegex(ValueError,'settled before its exact downstream ETL cancellation'):self.verify()

    def test_model_parent_settlement_must_exist_in_the_physical_store(self):
        observed=json.loads((self.root/'flowchart-success/capture/finished-checked-embedding.json').read_text());receipt,=[r for r in observed['inputs'] if r['inputKind']=='native-parent-settled']
        self.store('flowchart-success','finished',lambda c:c.execute('DELETE FROM elsa_runtime_model_input WHERE InputId=?',(v.key(receipt['inputId']),)))
        with self.assertRaisesRegex(ValueError,'physical embedded ETL model receipt count'):self.verify()

    def test_a_duplicated_case_cannot_inflate_the_parent_matrix(self):
        p=self.root/'manifest.json';m=json.loads(p.read_text());m['cases'].append(m['cases'][0]);v.q.write_json(p,m)
        with self.assertRaisesRegex(ValueError,'all unique parent/completion/cancellation cases'):self.verify()

    def test_a_rehashed_interpreter_trace_change_is_not_normalized_away(self):
        case='root-success'
        for name in ('before-checked-model.json','before-checked-embedding.json'):
            self.change(case,name,lambda value:value['model']['payload']['state']['diagnostics'][0].update(message='different interpreter diagnostic'))
        def alter(c):
            row,=c.execute('SELECT ExecutionId,ContentJson FROM elsa_runtime_model_execution').fetchall();value=json.loads(row[1]);value['payload']['state']['diagnostics'][0]['message']='different interpreter diagnostic';c.execute('UPDATE elsa_runtime_model_execution SET ContentJson=? WHERE ExecutionId=?',(json.dumps(value),row[0]))
        self.store(case,'before',alter)
        with self.assertRaisesRegex(ValueError,'interpreter state differs from the direct success trace'):self.verify()

    def test_equal_source_labels_cannot_hide_a_different_captured_build(self):
        self.change('sequence-success','process.json',lambda value:value['products']['actualProductVersions'][0].update(sha256='0'*64))
        with self.assertRaisesRegex(ValueError,'product hashes differ from the direct success build'):self.verify()

    def test_a_rehashed_different_process_cannot_claim_direct_trace_parity(self):
        def alter(c):
            rows=c.execute('SELECT Id,ContentJson FROM elsa_runtime_workflow_executable').fetchall();row,=[r for r in rows if json.loads(r[1]).get('material') is not None];value=json.loads(row[1]);value['material']['definition']['process']['sequenceFlows'][0]['flowId']='different-flow';c.execute('UPDATE elsa_runtime_workflow_executable SET ContentJson=? WHERE Id=?',(json.dumps(value),row[0]))
        self.store('flowchart-success','before',alter)
        with self.assertRaisesRegex(ValueError,'process or binding contract differs from the direct success definition'):self.verify()
