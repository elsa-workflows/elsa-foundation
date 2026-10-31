"""Corrupt copies of real retained-snapshot fault journeys; no live database writes."""
import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from contextlib import closing
from pathlib import Path
_spec=importlib.util.spec_from_file_location('technical_fault',Path(__file__).with_name('verify_http_recovery.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/technical-fault-native-recovery'


class ActualTechnicalFaultRecoveryControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-technical-fault-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def rehash(self,case,name):
        p=self.root/case/'manifest.json';m=json.loads(p.read_text());next(a for a in m['assets'] if a['path']=='capture/'+name)['sha256']=v.q.digest(p.parent/'capture'/name);v.q.write_json(p,m)
        p=self.root/'manifest.json';m=json.loads(p.read_text());next(c for c in m['cases'] if c['name']==case)['sha256']=v.q.digest(self.root/case/'manifest.json');v.q.write_json(p,m)

    def change(self,case,name,action):
        p=self.root/case/'capture'/name;value=json.loads(p.read_text());action(value);v.q.write_json(p,value);self.rehash(case,name)

    def store(self,case,action,phase='finished',store='elsa'):
        p=self.root/case/'capture'/(phase+'-'+store+'.sqlite.gz');raw=Path(self.temp.name)/'changed.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(case,p.name)
        def observer(value):
            s=value[phase+'Stores'][store];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=v.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change(case,'process.json',observer)

    def verify(self):return v.verify(self.root/'manifest.json')

    def test_real_technical_faults_recover_directly_and_under_every_native_parent(self):
        result=self.verify();self.assertEqual(5,result['actualTechnicalFinalFaultJourneys']);self.assertEqual(30,result['wholeStoresVerified'])
        self.assertTrue(result['technicalFinalFaultProved']);self.assertTrue(result['technicalFaultRefusesCodedBusinessCatch']);self.assertTrue(result['sameSourceInterpreterTechnicalFaultTracesCompared'])
        self.assertFalse(result['modelHeldFaultRetryProved']);self.assertFalse(result['finalFaultRetryRefusalProved']);self.assertFalse(result['nativeCompositeFaultStatusDecisionResolved'])

    def test_the_retained_snapshot_bytes_cannot_change_while_called_unavailable(self):
        p=self.root/'direct/capture/lookup-snapshot.csv';p.write_bytes(p.read_bytes()+b'5,changed\n');self.rehash('direct',p.name)
        with self.assertRaisesRegex(ValueError,'unchanged, temporarily unavailable retained lookup snapshot'):self.verify()

    def test_the_fault_must_refer_to_its_actual_prepared_lookup_dependency(self):
        def change(c):
            value=json.loads(c.execute('SELECT PreparedJson FROM elsa_etl_connector_runs').fetchone()[0]);value['lookup']['key']='u16:'+v.key('f'*64+'.csv');c.execute('UPDATE elsa_etl_connector_runs SET PreparedJson=?',(json.dumps(value),))
        self.store('root',change,phase='before',store='etl-runtime')
        with self.assertRaisesRegex(ValueError,'actual pinned and prepared lookup dependency'):self.verify()

    def test_the_native_root_must_retain_the_original_technical_fault_code(self):
        process=json.loads((self.root/'sequence/capture/process.json').read_text())
        def change(c):
            row,=c.execute('SELECT Id,ContentJson FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId=?',(v.key(process['workflowExecutionId']),)).fetchall();value=json.loads(row[1]);value['fault']['code']=v.key('foreign-fault');c.execute('UPDATE elsa_runtime_activity_execution_state SET ContentJson=? WHERE Id=?',(json.dumps(value),row[0]))
        self.store('sequence',change)
        with self.assertRaisesRegex(ValueError,'physical native root lost its actual technical fault'):self.verify()

    def test_a_technical_fault_cannot_be_assigned_the_business_error_catch_code(self):
        for phase in ('finished','retained'):
            self.change('direct',phase+'-checked-model.json',lambda value:value['model']['terminalResult'].update(errorCode='etl.rows-rejected'))
            self.change('direct',phase+'.json',lambda value:value['model']['result'].update(errorCode='etl.rows-rejected'))
        with self.assertRaisesRegex(ValueError,'technical worker fault was relabeled as a business error'):self.verify()

    def test_the_terminal_http_wait_cannot_resume_at_its_old_deadline(self):
        for phase in ('finished','retained'):
            self.change('flowchart',phase+'-wire.json',lambda value:value['requests'].append({'shard':'shard-b','status':200,'receivedAtMs':999999}))
        with self.assertRaisesRegex(ValueError,'faulted HTTP wait revived at its original retry deadline'):self.verify()

    def test_the_physical_native_fault_incident_cannot_disappear(self):
        self.store('direct',lambda c:c.execute('DELETE FROM elsa_runtime_incident_state'))
        with self.assertRaisesRegex(ValueError,'physical technical-fault incident inventory differs'):self.verify()

    def test_a_duplicated_parent_cannot_inflate_the_technical_fault_matrix(self):
        p=self.root/'manifest.json';value=json.loads(p.read_text());value['cases'][1]=value['cases'][0];v.q.write_json(p,value)
        with self.assertRaisesRegex(ValueError,'every unique direct/native parent'):self.verify()

    def test_the_fault_capture_still_requires_real_process_loss(self):
        self.change('compensationscope','process.json',lambda value:value.update(predecessorExitCode=0))
        with self.assertRaisesRegex(ValueError,'distinct process-loss/restart provenance'):self.verify()
