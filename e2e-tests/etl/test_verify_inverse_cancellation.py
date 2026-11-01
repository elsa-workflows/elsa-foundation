"""Corrupt closed copies of actual result-first native/ETL cancellation journeys."""
import gzip,hashlib,importlib.util,json,os,shutil,sqlite3,tempfile,unittest
from contextlib import closing
from pathlib import Path
spec=importlib.util.spec_from_file_location('inverse_cancellation',Path(__file__).with_name('verify_inverse_cancellation.py'));v=importlib.util.module_from_spec(spec);spec.loader.exec_module(v)
EVIDENCE=Path(os.environ.get('ELSA_BPMN_INVERSE_EVIDENCE_DIRECTORY',str(v.common_verifier().q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/inverse-cancellation-order')))

class InverseCancellationControls(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory(prefix='inverse-cancel-controls-');self.addCleanup(self.temporary.cleanup);self.root=Path(self.temporary.name)/'case';shutil.copytree(EVIDENCE/'direct',self.root)
    def rehash(self,name):
        p=self.root/'manifest.json';x=json.loads(p.read_text());next(a for a in x['assets']if a['path']==name)['sha256']=hashlib.sha256((self.root/name).read_bytes()).hexdigest();p.write_text(json.dumps(x))
    def change(self,name,action):
        p=self.root/name;x=json.loads(p.read_text());action(x);p.write_text(json.dumps(x));self.rehash(name)
    def reject(self):
        with self.assertRaises(ValueError):v.verify_case(self.root)
    def test_all_five_actual_cases_verify_sixty_whole_stores(self):
        results=[v.verify_case(EVIDENCE/n)for n in ['direct','root','sequence','flowchart','compensationscope']];self.assertEqual(60,sum(x['wholeStores']for x in results))
    def test_an_unrelated_dependency_error_cannot_stand_in_for_pending_etl_cancellation(self):
        self.change('sdk-inverse-cancel.json',lambda x:x['prematureDrain'].update(safeSummary='Another unavailable dependency'));self.reject()
    def test_a_premature_accepted_receipt_cannot_be_hidden(self):
        self.change('sdk-inverse-cancel.json',lambda x:x.update(prematureReceipt={'status':'Accepted'}));self.reject()
    def test_the_original_queued_input_cannot_disappear(self):
        self.change('deferred-checked.json',lambda x:x.update(queuedModelInputs=[]));self.change('sdk-inverse-cancel.json',lambda x:x.update(deferredQueue=[]));self.reject()
    def test_a_replacement_result_address_cannot_pass_as_the_original(self):
        self.change('deferred-checked.json',lambda x:x['queuedModelInputs'][0]['payload'].update(inputId='replacement'));self.change('sdk-inverse-cancel.json',lambda x:x['deferredQueue'][0]['payload'].update(inputId='replacement'));self.reject()
    def test_a_premature_model_terminal_cannot_be_hidden(self):
        self.change('deferred-checked.json',lambda x:x['model'].update(revision=2,lifecycle=3,terminalResult={'status':'Cancelled'}));self.reject()
    def test_the_cancel_inventory_cannot_drop_its_actual_etl_source(self):
        self.change('sdk-inverse-cancel.json',lambda x:x['prematureSource']['intent']['payload'].update(cancellationSources=[]));self.reject()
    def test_a_delivered_cancel_cannot_claim_the_pending_cut(self):
        self.change('sdk-inverse-cancel.json',lambda x:x['pendingEtlCancel'].update(status=2));self.reject()
    def test_graceful_exit_cannot_claim_process_loss(self):
        self.change('process.json',lambda x:x.update(pendingPredecessorExitCode=0));self.reject()
    def test_a_pre_deadline_observation_cannot_prove_no_late_retry(self):
        self.change('process.json',lambda x:x.update(observedCancelledAfterRetryDeadlineAtUtc=x['cutAtUtc']));self.reject()
    def test_a_terminal_etl_store_cannot_be_substituted_into_the_pending_cut(self):
        name='deferred-etl-runtime.sqlite.gz';data=(self.root/'finished-etl-runtime.sqlite.gz').read_bytes();(self.root/name).write_bytes(data);self.rehash(name)
        def update(x):x['deferredStores']['etl-runtime'].update(databaseSha256=hashlib.sha256(gzip.decompress(data)).hexdigest(),gzipSha256=hashlib.sha256(data).hexdigest());x['deferredStores']['etl-runtime']['observer']['snapshotSha256']=hashlib.sha256(gzip.decompress(data)).hexdigest()
        self.change('process.json',update);self.reject()
    def test_physical_model_settlement_cannot_hide_behind_unchanged_checked_json(self):
        name='deferred-elsa.sqlite.gz';raw=Path(self.temporary.name)/'changed.sqlite';raw.write_bytes(gzip.decompress((self.root/name).read_bytes()))
        with closing(sqlite3.connect(raw))as db:
            row,=db.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchall();x=json.loads(row[0]);x['revision']=2;self.assertEqual(1,db.execute('UPDATE elsa_runtime_model_execution SET Revision=2,ContentJson=?',(json.dumps(x),)).rowcount);db.commit()
        data=gzip.compress(raw.read_bytes());(self.root/name).write_bytes(data);self.rehash(name)
        def update(x):s=x['deferredStores']['elsa'];s['databaseSha256']=s['observer']['snapshotSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=hashlib.sha256(data).hexdigest()
        self.change('process.json',update);self.reject()
    def test_a_foreign_native_registration_cannot_claim_parent_consumption(self):
        shutil.rmtree(self.root);shutil.copytree(EVIDENCE/'root',self.root)
        def change(x):
            for a in x['activities']:
                if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn':
                    for t in a['triggerDeliveries']:t['registrationId']='foreign'
        self.change('finished-checked-embedding.json',change);self.change('retained-checked-embedding.json',change);self.reject()

if __name__=='__main__':unittest.main()
