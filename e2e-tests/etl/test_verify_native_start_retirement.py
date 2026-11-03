import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from pathlib import Path
from contextlib import closing
_spec=importlib.util.spec_from_file_location('retirement',Path(__file__).with_name('verify_native_start_retirement.py'))
r=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(r)
EVIDENCE=r.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/native-start-retirement'


class ActualNativeStartRetirementControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-start-retirement-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def rehash(self,name):
        p=self.root/'manifest.json';m=json.loads(p.read_text());next(a for a in m['assets'] if a['path']==name)['sha256']=r.q.digest(self.root/name)
        r.q.write_json(p,m)

    def change(self,name,action):
        p=self.root/name;v=json.loads(p.read_text());action(v);r.q.write_json(p,v);self.rehash(name)

    def store(self,action):
        p=self.root/'finished-elsa.sqlite.gz';raw=Path(self.temp.name)/'control.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(p.name)
        def observed(v):
            s=v['finishedStores']['elsa'];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=r.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change('process.json',observed)

    def verify(self):return r.verify(self.root/'manifest.json')

    def test_actual_owed_start_retires_without_claiming_the_queued_start_race(self):
        v=self.verify();self.assertTrue(v['originalUndeliveredStartRetirementProved']);self.assertEqual(9,v['wholeStoresVerified'])
        self.assertEqual(0,v['nativeWorkflowsAdmitted']);self.assertFalse(v['queuedNativeStartRetirementProved']);self.assertFalse(v['admissionRaceProved'])

    def test_a_graceful_exit_cannot_replace_the_real_process_loss(self):
        self.change('process.json',lambda v:v.update(predecessorExitCode=0))
        with self.assertRaisesRegex(ValueError,'process-loss/restart provenance'):self.verify()

    def test_an_absent_worker_cannot_replace_the_positive_retirement_receipt(self):
        self.store(lambda c:c.execute('DELETE FROM elsa_runtime_model_input WHERE ContentJson LIKE ?',('%'+r.v.key('native-start-retired')+'%',)))
        with self.assertRaisesRegex(ValueError,'actual model receipt count'):self.verify()

    def test_start_acknowledgment_cannot_precede_retirement_consumption(self):
        def early(v):
            source=next(s for s in v['sources'] if s['intent']['kind']=='Elsa.Bpmn.StartNativeWork.v1');source['deliveredAt']=v['model']['createdAt']
        for phase in ('finished','retained'):self.change(phase+'-checked.json',early)
        with self.assertRaisesRegex(ValueError,'exact positive retirement consumption receipt'):self.verify()

    def test_acceptance_cannot_be_reported_as_already_settled(self):
        self.change('cancel-response.json',lambda v:v['result'].update(lifecycle='TerminalSettled'))
        with self.assertRaisesRegex(ValueError,'relabeled as already settled'):self.verify()

    def test_never_admitted_cannot_be_relabelled_as_a_cancelled_child(self):
        for phase in ('finished','retained'):self.change(phase+'-checked.json',lambda v:v['model']['payload']['cancellations'][0].pop('notAdmitted'))
        with self.assertRaisesRegex(ValueError,'fabricated an admitted-worker result'):self.verify()

    def test_a_physical_carrier_cannot_hide_behind_an_empty_public_list(self):
        source=EVIDENCE.parent/'model-cancellation/model-cancel/finished-elsa.sqlite.gz';other=Path(self.temp.name)/'admitted.sqlite';other.write_bytes(gzip.decompress(source.read_bytes()))
        def carrier(c):
            c.execute('ATTACH DATABASE ? AS admitted',(str(other),))
            c.execute('INSERT INTO main.elsa_runtime_workflow_execution_state SELECT * FROM admitted.elsa_runtime_workflow_execution_state')
        self.store(carrier)
        with self.assertRaisesRegex(ValueError,'physical carrier or work'):self.verify()


if __name__=='__main__':unittest.main()
