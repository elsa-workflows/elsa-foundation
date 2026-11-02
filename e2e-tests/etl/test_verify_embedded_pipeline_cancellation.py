"""The normal ETL-cancel API inside RunBpmn, plus corrupt closed copies."""
import importlib.util,json,shutil,tempfile,unittest
from pathlib import Path
_spec=importlib.util.spec_from_file_location('embedded_pipeline_cancel',Path(__file__).with_name('verify_http_recovery.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/reviews/round-3/etl-cancellation-tooling/actual-root'

class ActualEmbeddedPipelineCancellationControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-embedded-pipeline-cancel-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def change(self,name,action):
        path=self.root/'capture'/name;value=json.loads(path.read_text());action(value);v.q.write_json(path,value)
        manifest=self.root/'manifest.json';data=json.loads(manifest.read_text());next(a for a in data['assets'] if a['path']=='capture/'+name)['sha256']=v.q.digest(path);v.q.write_json(manifest,data)

    def verify(self):return v.verify(self.root/'manifest.json')

    def test_the_actual_pipeline_cancel_reaches_its_original_native_parent(self):
        result=self.verify();self.assertTrue(result['pipelineOriginatedCancellationProved']);self.assertTrue(result['runBpmnEtlIntegrationProved'])
        self.assertFalse(result['nativeParentToPipelineCancellationProved']);self.assertFalse(result['nativeWorkerOriginatedCancellationProved']);self.assertEqual('Root',result['embeddedParent'])
        self.assertEqual(6,result['wholeStoresVerified'])

    def test_native_origin_cancellation_cannot_be_relabelled_pipeline_origin(self):
        self.change('process.json',lambda p:p.update(pipelineCancel=False,nativeCancel=True))
        with self.assertRaises(ValueError):self.verify()

    def test_a_second_cancellation_origin_is_not_pipeline_origin_proof(self):
        self.change('process.json',lambda p:p.update(nativeCancel=True))
        with self.assertRaises(ValueError):self.verify()

    def test_the_original_RunBpmn_registration_must_consume_the_result(self):
        for phase in ('finished','retained'):
            self.change(phase+'-checked-embedding.json',lambda p:p['activities'][0].update(triggerDeliveries=[]))
        with self.assertRaises(ValueError):self.verify()

    def test_parent_source_ack_cannot_precede_exact_settlement(self):
        for phase in ('finished','retained'):
            def early(p):next(s for s in p['sources'] if s['intent']['kind']=='Elsa.Bpmn.ResumeNativeParent.v1')['deliveredAt']='2026-01-01T00:00:00+00:00'
            self.change(phase+'-checked-embedding.json',early)
        with self.assertRaises(ValueError):self.verify()

    def test_a_cancelled_label_cannot_hide_a_missing_cancel_API_receipt(self):
        self.change('cancel-response.json',lambda p:p.update(cancellationAccepted=False))
        with self.assertRaises(ValueError):self.verify()

if __name__=='__main__':unittest.main()
