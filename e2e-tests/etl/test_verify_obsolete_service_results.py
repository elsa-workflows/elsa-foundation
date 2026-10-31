"""Corrupt-copy controls over actual obsolete native-result transport and live replacement stores."""
from contextlib import closing
import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from pathlib import Path
_spec=importlib.util.spec_from_file_location('obsolete_held',Path(__file__).with_name('verify_held_service_retry.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/obsolete-service-results'

class ActualObsoleteResultControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-obsolete-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)
    def rehash(self,case,name):
        p=self.root/'manifest.json';m=json.loads(p.read_text());row=next(c for c in m['cases'] if c['name']==case)
        next(a for a in row['assets'] if a['path']==case+'/'+name)['sha256']=v.q.digest(self.root/case/name);v.q.write_json(p,m)
    def change(self,case,name,action):
        p=self.root/case/name;value=json.loads(p.read_text());action(value);v.q.write_json(p,value);self.rehash(case,name)
    def store(self,case,store,action):
        p=self.root/case/('replacement-wait-'+store+'.sqlite.gz');raw=Path(self.temp.name)/'db';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(case,p.name)
        def update(value):
            s=value['replacementWaitStores'][store];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=v.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change(case,'process.json',update)
    def verify(self):return v.verify(self.root/'manifest.json')
    def test_actual_old_result_replay_and_refusals_preserve_every_live_replacement(self):
        r=self.verify();self.assertEqual(5,len(r['actualHeldRetryJourneys']));self.assertEqual(60,r['wholeStoresVerified'])
        self.assertEqual(15,r['actualObsoleteResultTransportControls']);self.assertTrue(r['obsoleteResultControlsProved']);self.assertFalse(r['finalUpgradeAndStudioProved'])
    def test_obsolete_result_qualification_cannot_be_removed(self):
        p=self.root/'manifest.json';m=json.loads(p.read_text());m['obsoleteResultControls']=False;v.q.write_json(p,m)
        with self.assertRaisesRegex(ValueError,'exact raw assets'):self.verify()
    def test_original_accepted_receipt_cannot_be_replaced(self):
        self.change('direct','obsolete-sdk.json',lambda s:s['controls'][0]['originalReceipt'].update(acceptedRevision=3))
        with self.assertRaisesRegex(ValueError,'changed the retained input'):self.verify()
    def test_conflicting_old_content_cannot_be_accepted(self):
        self.change('root','obsolete-sdk.json',lambda s:s['controls'][1].update(outcome=s['controls'][0]['outcome'],refusalType=None,refusalMessage=None))
        with self.assertRaisesRegex(ValueError,'replay/refusal'):self.verify()
    def test_a_new_old_result_address_cannot_acquire_a_receipt(self):
        self.change('sequence','obsolete-sdk.json',lambda s:s['controls'][2].update(addressedReceipt=s['originalReceipt']))
        with self.assertRaisesRegex(ValueError,'changed the retained input'):self.verify()
    def test_exact_old_replay_cannot_advance_the_new_attempt(self):
        self.change('flowchart','obsolete-sdk.json',lambda s:s['controls'][0]['model'].update(revision=4))
        with self.assertRaisesRegex(ValueError,'changed the retained input'):self.verify()
    def test_old_source_ack_cannot_be_rewritten_by_replay(self):
        self.change('compensationscope','obsolete-sdk.json',lambda s:s['controls'][0]['originalSource'].update(deliveredAt='2000-01-01T00:00:00Z'))
        with self.assertRaisesRegex(ValueError,'changed the retained input'):self.verify()
    def test_replayed_bytes_must_be_the_actual_failed_native_result(self):
        self.change('direct','obsolete-sdk.json',lambda s:s['oldResult'].update(faultCode='invented'))
        with self.assertRaisesRegex(ValueError,'unchanged real old failure'):self.verify()
    def test_current_attempt_cannot_be_relabelled_as_the_original(self):
        self.change('root','obsolete-sdk.json',lambda s:s['currentOrigin'].update(attemptId=s['oldResult']['origin']['attemptId']))
        with self.assertRaisesRegex(ValueError,'unchanged real old failure'):self.verify()
    def test_sdk_products_must_match_executing_source(self):
        self.change('direct','obsolete-sdk.json',lambda s:s['products'][0].update(informationalVersion='0.0.0+'+'0'*40))
        with self.assertRaisesRegex(ValueError,'source-matched transport'):self.verify()
    def test_probe_cannot_be_relabelled_as_execution_state_seeding(self):
        self.change('direct','obsolete-sdk.json',lambda s:s.update(executionStateSeeded=True))
        with self.assertRaisesRegex(ValueError,'source-matched transport'):self.verify()
    def test_probe_must_finish_while_the_replacement_retry_is_future(self):
        sdk=json.loads((self.root/'direct/obsolete-sdk.json').read_text())
        for name in ('replacement-wait-before.json','replacement-wait-after.json'):
            self.change('direct',name,lambda s:next(x['http']['fetch'] for x in s['replacement']['partitions']['items'] if x['partitionOrdinal']==1).update(notBefore=sdk['startedAtUtc']))
        with self.assertRaisesRegex(ValueError,'retry deadline'):self.verify()
    def test_negative_transport_cannot_be_omitted(self):
        self.change('direct','obsolete-sdk.json',lambda s:s['controls'].pop())
        with self.assertRaisesRegex(ValueError,'exact replay and negative controls'):self.verify()
    def test_physical_replacement_must_still_be_running(self):
        p=json.loads((self.root/'direct/process.json').read_text())
        self.store('direct','etl-runtime',lambda c:c.execute('UPDATE elsa_etl_runtime_runs SET Status=2 WHERE RunId=?',(v.v.key(p['replacementRunId']),)))
        with self.assertRaisesRegex(ValueError,'physical ETL replacement'):self.verify()
    def test_physical_original_receipt_cannot_change(self):
        sdk=json.loads((self.root/'direct/obsolete-sdk.json').read_text());input_key=v.v.key(sdk['originalReceipt']['inputId'])
        def change(c):
            key,raw=c.execute('SELECT InputId,ContentJson FROM elsa_runtime_model_input WHERE InputId=?',(input_key,)).fetchone();value=json.loads(raw);value['contentFingerprint']=v.v.key('other')
            c.execute('UPDATE elsa_runtime_model_input SET ContentJson=? WHERE InputId=?',(json.dumps(value),key))
        self.store('direct','elsa',change)
        with self.assertRaisesRegex(ValueError,'physical obsolete-result probe'):self.verify()
    def test_physical_original_source_payload_cannot_change(self):
        sdk=json.loads((self.root/'direct/obsolete-sdk.json').read_text());key=v.v.key(sdk['originalSource']['outboxItemId'])
        def change(c):
            value=json.loads(c.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(key,)).fetchone()[0]);value['intent']['payload']['faultCode']='other'
            c.execute('UPDATE elsa_runtime_post_commit_outbox SET ContentJson=? WHERE OutboxItemId=?',(json.dumps(value),key))
        self.store('direct','elsa',change)
        with self.assertRaisesRegex(ValueError,'physical original result source'):self.verify()
