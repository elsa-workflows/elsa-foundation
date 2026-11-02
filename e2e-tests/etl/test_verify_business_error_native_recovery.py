"""Rehashed controls over real coded-error recovery captures, never live stores."""
import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from contextlib import closing
from pathlib import Path
_spec=importlib.util.spec_from_file_location('business_error',Path(__file__).with_name('verify_http_recovery.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/business-error-native-recovery'


class ActualBusinessErrorRecoveryControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-business-error-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def rehash(self,case,name):
        p=self.root/case/'manifest.json';m=json.loads(p.read_text());next(a for a in m['assets'] if a['path']=='capture/'+name)['sha256']=v.q.digest(p.parent/'capture'/name);v.q.write_json(p,m)
        p=self.root/'manifest.json';m=json.loads(p.read_text());next(c for c in m['cases'] if c['name']==case)['sha256']=v.q.digest(self.root/case/'manifest.json');v.q.write_json(p,m)

    def change(self,case,name,action):
        p=self.root/case/'capture'/name;value=json.loads(p.read_text());action(value);v.q.write_json(p,value);self.rehash(case,name)

    def store(self,case,action):
        p=self.root/case/'capture/finished-elsa.sqlite.gz';raw=Path(self.temp.name)/'changed.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(case,p.name)
        def observer(value):
            s=value['finishedStores']['elsa'];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=v.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change(case,'process.json',observer)

    def verify(self):return v.verify(self.root/'manifest.json')

    def test_real_direct_and_four_native_parents_preserve_both_business_error_dispositions(self):
        result=self.verify();self.assertEqual(10,result['actualMatchedAndUnmatchedBusinessErrorJourneys']);self.assertEqual(60,result['wholeStoresVerified'])
        self.assertTrue(result['directAndAllNativeParentBusinessErrorDispositionProved']);self.assertTrue(result['sameSourceInterpreterBusinessErrorTracesCompared'])
        self.assertFalse(result['modelHeldFaultRetryProved']);self.assertFalse(result['nativeCompositeFaultStatusDecisionResolved'])

    def test_a_physical_catch_code_cannot_be_replaced_by_a_label(self):
        def change(c):
            rows=c.execute('SELECT Id,ContentJson FROM elsa_runtime_workflow_executable').fetchall();row,=[r for r in rows if json.loads(r[1]).get('material')];value=json.loads(row[1]);value['material']['definition']['process']['elements'][3]['eventDefinitions'][0]['properties']['code']='etl.other-error';c.execute('UPDATE elsa_runtime_workflow_executable SET ContentJson=? WHERE Id=?',(json.dumps(value),row[0]))
        self.store('direct-matching',change)
        with self.assertRaisesRegex(ValueError,'physical model lost its declared coded business-error boundary'):self.verify()

    def test_matching_requires_the_actual_interpreter_error_caught_diagnostic(self):
        def remove(value):value['model']['payload']['state']['diagnostics']=[d for d in value['model']['payload']['state']['diagnostics'] if not (d['elementId']=='error' and d['details'].get('match')=='exact')]
        for phase in ('finished','retained'):self.change('direct-matching',phase+'-checked-model.json',remove)
        def physical(c):
            row,=c.execute('SELECT Id,ContentJson FROM elsa_runtime_model_execution').fetchall();value=json.loads(row[1]);value['payload']['state']['diagnostics']=[d for d in value['payload']['state']['diagnostics'] if not (d['elementId']=='error' and d['details'].get('match')=='exact')];c.execute('UPDATE elsa_runtime_model_execution SET ContentJson=? WHERE Id=?',(json.dumps(value),row[0]))
        self.store('direct-matching',physical)
        with self.assertRaisesRegex(ValueError,'exact matched or unmatched business-error disposition'):self.verify()

    def test_an_unmatched_error_must_keep_the_native_default_fault_incident(self):
        for phase in ('finished','retained'):self.change('sequence-different',phase+'.json',lambda value:value['embedding'].update(incidents=[]))
        with self.assertRaisesRegex(ValueError,'default fault incident or terminal API truth'):self.verify()

    def test_the_default_fault_incident_must_exist_in_the_physical_store(self):
        self.store('root-different',lambda c:c.execute('DELETE FROM elsa_runtime_incident_state'))
        with self.assertRaisesRegex(ValueError,'physical parent lost its default fault incident inventory'):self.verify()

    def test_a_faulted_parent_still_requires_exact_typed_RunBpmn_consumption(self):
        for phase in ('finished','retained'):
            def remove(value):next(a for a in value['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn')['triggerDeliveries']=[]
            self.change('flowchart-different',phase+'-checked-embedding.json',remove)
        with self.assertRaisesRegex(ValueError,'exact typed RunBpmn consumption proof'):self.verify()

    def test_a_faulted_RunBpmn_cannot_execute_the_outer_successor(self):
        for phase in ('finished','retained'):
            self.change('compensationscope-different',phase+'-checked-embedding.json',lambda value:value['activities'].append({'execution':{'executableNodeId':'after','activityType':'Elsa.Activities.Primitives.Activities.WriteLine'},'status':4}))
        with self.assertRaisesRegex(ValueError,'successor did not preserve completion/fault/cancellation'):self.verify()

    def test_fault_result_ack_must_follow_the_original_model_parent_settlement(self):
        for phase in ('finished','retained'):
            def early(value):next(s for s in value['sources'] if s['intent']['kind']=='Elsa.Bpmn.ResumeNativeParent.v1')['deliveredAt']='2026-01-01T00:00:00+00:00'
            self.change('root-different',phase+'-checked-embedding.json',early)
        with self.assertRaisesRegex(ValueError,'exact native parent settlement proof'):self.verify()

    def test_a_duplicate_case_cannot_replace_one_of_the_real_parent_code_combinations(self):
        p=self.root/'manifest.json';value=json.loads(p.read_text());value['cases'][1]=value['cases'][0];v.q.write_json(p,value)
        with self.assertRaisesRegex(ValueError,'every unique direct/native-parent and matching-code case'):self.verify()

    def test_product_hashes_must_match_before_comparing_interpreter_traces(self):
        self.change('sequence-matching','process.json',lambda value:value['products']['actualProductVersions'][0].update(sha256='0'*64))
        with self.assertRaisesRegex(ValueError,'differ in captured first-party host products'):self.verify()

    def test_a_completed_worker_cannot_hide_its_typed_BusinessError(self):
        def hide(value):
            for result in (value['model']['payload']['nativeResults'][0],value['resultSource']['intent']['payload']):result['completion']['outcomeKey']='Done'
        for phase in ('finished','retained'):self.change('direct-matching',phase+'-checked-model.json',hide)
        with self.assertRaisesRegex(ValueError,'actual typed RunEtl result'):self.verify()

    def test_the_physical_compensation_outcome_cannot_be_relabeled_as_Done(self):
        def change(c):
            rows=c.execute('SELECT Id,ContentJson FROM elsa_runtime_activity_execution_state').fetchall();row,=[r for r in rows if json.loads(r[1])['execution']['activityType']==v.key('Elsa.Activities.Compensation.Activities.CompensationScope')];value=json.loads(row[1]);value['completion']['outcomeKey']=v.key('Done');c.execute('UPDATE elsa_runtime_activity_execution_state SET ContentJson=? WHERE Id=?',(json.dumps(value),row[0]))
        self.store('compensationscope-different',change)
        with self.assertRaisesRegex(ValueError,'physical compensation scope lost its default Compensated outcome'):self.verify()

    def test_compensation_fault_absorption_cannot_be_relabeled_as_a_blocking_fault(self):
        def block(value):
            incident,=value['embedding']['incidents'];incident['status']='Blocking';incident['resolutionOutcome']['actionKind']='FaultWorkflow';incident['isBlocking']=True
        for phase in ('finished','retained'):self.change('compensationscope-different',phase+'.json',block)
        with self.assertRaisesRegex(ValueError,'default fault incident or terminal API truth'):self.verify()
