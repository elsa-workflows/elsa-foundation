import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from contextlib import closing
from pathlib import Path
_spec=importlib.util.spec_from_file_location('queued_retirement',Path(__file__).with_name('verify_native_start_retirement.py'))
r=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(r)
EVIDENCE=r.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/queued-native-retirement'


class ActualQueuedNativeRetirementControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-queued-native-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def rehash(self,stage,name):
        p=self.root/stage/'manifest.json';m=json.loads(p.read_text());next(a for a in m['assets'] if a['path']==name)['sha256']=r.q.digest(p.parent/name);r.q.write_json(p,m)
        p=self.root/'manifest.json';m=json.loads(p.read_text());next(a for a in m['cases'] if a['stage']==stage)['sha256']=r.q.digest(self.root/stage/'manifest.json');r.q.write_json(p,m)

    def change(self,stage,name,action):
        p=self.root/stage/name;v=json.loads(p.read_text());action(v);r.q.write_json(p,v);self.rehash(stage,name)

    def store(self,stage,phase,action):
        p=self.root/stage/(phase+'-elsa.sqlite.gz');raw=Path(self.temp.name)/'changed.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(stage,p.name)
        def observed(v):
            s=v[phase+'Stores']['elsa'];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=r.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change(stage,'process.json',observed)

    def verify(self):return r.verify(self.root/'manifest.json')

    def test_both_real_queued_stages_recover_without_claiming_the_simultaneous_race(self):
        v=self.verify();self.assertEqual(['start','checkpoint'],v['actualQueuedNativeStages']);self.assertEqual(18,v['wholeStoresVerified'])
        self.assertTrue(v['queuedNativeStartRetirementProved']);self.assertFalse(v['admissionRaceProved']);self.assertEqual(0,v['nativeWorkflowsAdmitted'])

    def test_an_anonymous_flush_cannot_replace_the_original_retirement_marker(self):
        def anonymous(v):
            m=v['nativeMarkers'][0];m['commitId']=r.v.key('anonymous-flush');body=json.loads(m['contentJson']);body['commitId']='anonymous-flush';m['contentJson']=json.dumps(body)
        for phase in ('finished','retained'):self.change('checkpoint',phase+'-checked.json',anonymous)
        def physical(c):
            row=c.execute('SELECT Id,ContentJson FROM elsa_runtime_checkpoint_commit WHERE WorkflowExecutionId=?',(r.v.key(json.loads((self.root/'checkpoint/process.json').read_text())['origin']['workerExecutionId']),)).fetchone()
            body=json.loads(row[1]);body['commitId']='anonymous-flush';c.execute('UPDATE elsa_runtime_checkpoint_commit SET CommitId=?,ContentJson=? WHERE Id=?',(r.v.key('anonymous-flush'),json.dumps(body),row[0]))
        self.store('checkpoint','finished',physical)
        with self.assertRaisesRegex(ValueError,'folded away or acquired root-work'):self.verify()

    def test_missing_physical_retirement_marker_cannot_be_hidden_by_checked_receipts(self):
        origin=json.loads((self.root/'start/process.json').read_text())['origin']
        self.store('start','finished',lambda c:c.execute('DELETE FROM elsa_runtime_checkpoint_commit WHERE WorkflowExecutionId=?',(r.v.key(origin['workerExecutionId']),)))
        with self.assertRaisesRegex(ValueError,'physical native retirement checkpoint is absent'):self.verify()

    def test_the_native_checkpoint_must_still_be_queued_at_the_crash_cut(self):
        self.change('checkpoint','pending-checked.json',lambda v:v.update(queuedNativeWork=[]))
        with self.assertRaisesRegex(ValueError,'actual SDK/native queued stage lost'):self.verify()

    def test_rehashed_native_queue_cannot_target_a_different_attempt(self):
        def redirect(v):v['queuedNativeWork'][0]['payload']['ModelWorkOrigin']['AttemptId']='foreign-attempt'
        for phase in ('before','pending'):self.change('checkpoint',phase+'-checked.json',redirect)
        self.change('checkpoint','sdk-dispatch.json',lambda v:v['queuedWork'][0]['payload']['ModelWorkOrigin'].update(AttemptId='foreign-attempt'))
        with self.assertRaisesRegex(ValueError,'changed the frozen model origin'):self.verify()

    def test_the_sdk_cannot_have_already_admitted_the_worker(self):
        self.change('start','sdk-dispatch.json',lambda v:v.update(nativeWorkerAdmitted=True))
        with self.assertRaisesRegex(ValueError,'actual SDK/native queued stage lost'):self.verify()

    def test_a_different_sdk_binary_source_cannot_be_substituted(self):
        self.change('checkpoint','sdk-dispatch.json',lambda v:v['products'][0].update(informationalVersion='other-source'))
        with self.assertRaisesRegex(ValueError,'actual SDK/native queued stage lost'):self.verify()


if __name__=='__main__':unittest.main()
