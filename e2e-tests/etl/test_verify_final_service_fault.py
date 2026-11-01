"""Actual final fault and rehashed controls for its terminal retry refusal."""
from contextlib import closing
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sqlite3
import tempfile
import unittest

_spec=importlib.util.spec_from_file_location('final_fault',Path(__file__).with_name('verify_http_recovery.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/final-service-fault'


class ActualFinalServiceFaultControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-final-service-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def rehash(self,name):
        p=self.root/'manifest.json';m=json.loads(p.read_text());next(a for a in m['assets'] if a['path']=='capture/'+name)['sha256']=v.q.digest(self.root/'capture'/name);v.q.write_json(p,m)

    def change(self,name,action):
        p=self.root/'capture'/name;value=json.loads(p.read_text());action(value);v.q.write_json(p,value);self.rehash(name)

    def store(self,action):
        p=self.root/'capture/finished-elsa.sqlite.gz';raw=Path(self.temp.name)/'changed.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(p.name)
        def observer(value):
            s=value['finishedStores']['elsa'];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=v.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change('process.json',observer)

    def verify(self):return v.verify(self.root/'manifest.json')

    def test_actual_final_fault_refuses_retry_without_new_semantic_progress(self):
        result=self.verify();self.assertTrue(result['technicalFinalFaultProved']);self.assertTrue(result['finalFaultRetryRefusalProved']);self.assertEqual(6,result['wholeStoresVerified'])
        self.assertFalse(result['modelHeldFaultRetryProved'])

    def test_the_terminal_conflict_cannot_be_reported_as_accepted(self):
        self.change('final-retry-refusal.json',lambda x:x.update(httpStatus=200))
        with self.assertRaisesRegex(ValueError,'terminal retry refusal'):self.verify()

    def test_a_final_incident_cannot_be_relabelled_as_open(self):
        for name in ('finished-checked-model.json','retained-checked-model.json'):
            self.change(name,lambda x:x['model']['payload']['failures'][0].update(status='Open'))
        with self.assertRaisesRegex(ValueError,'terminal retry refusal'):self.verify()

    def test_the_physical_rejected_retry_receipt_cannot_change(self):
        def change(c):
            identity=v.key('retry-actual-final-service');row=c.execute('SELECT ContentJson FROM elsa_runtime_model_input WHERE InputId=?',(identity,)).fetchone()
            value=json.loads(row[0]);value['result']['status']='Accepted';c.execute('UPDATE elsa_runtime_model_input SET ContentJson=? WHERE InputId=?',(json.dumps(value),identity))
        self.store(change)
        with self.assertRaisesRegex(ValueError,'physical final-fault retry refusal'):self.verify()

    def test_final_policy_cannot_acquire_another_attempt_after_publication(self):
        def change(c):
            for identity,content in c.execute('SELECT ArtifactId,ContentJson FROM elsa_runtime_workflow_executable').fetchall():
                value=json.loads(content)
                if value.get('material'):
                    value['material']['definition']['serviceBindings'][0]['faultPolicy']['maxAttempts']=2
                    c.execute('UPDATE elsa_runtime_workflow_executable SET ContentJson=? WHERE ArtifactId=?',(json.dumps(value),identity))
        self.store(change)
        with self.assertRaisesRegex(ValueError,'declared coded business-error boundary or service binding'):self.verify()
