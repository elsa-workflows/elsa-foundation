"""Corrupt copies of the actual ETL/native recovery reference, never live stores."""
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest
from contextlib import closing
import sqlite3

_spec=importlib.util.spec_from_file_location("etl_recovery_verifier",Path(__file__).with_name("verify_http_recovery.py"))
etl=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(etl)
EVIDENCE=etl.q.REPO/"specs/1017-direct-bpmn-hosting/evidence/pipeline/native-http-reference"


class _ActualRecoveryControls(unittest.TestCase):
    evidence=EVIDENCE

    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory(prefix="elsa-etl-reference-negative-");self.addCleanup(self.temporary.cleanup)
        self.root=Path(self.temporary.name)/"evidence";shutil.copytree(self.evidence,self.root)

    def rehash(self,relative):
        path=self.root/"manifest.json";manifest=json.loads(path.read_text())
        next(item for item in manifest["assets"] if item["path"]==relative)["sha256"]=etl.q.digest(self.root/relative)
        etl.q.write_json(path,manifest)

    def change(self,relative,action):
        path=self.root/relative;value=json.loads(path.read_text());action(value);etl.q.write_json(path,value);self.rehash(relative)

    def verify(self):return etl.verify(self.root/"manifest.json")

    def change_store(self,case,action):
        path=self.root/case/"finished-elsa.sqlite.gz";raw=Path(self.temporary.name)/"changed.sqlite"
        raw.write_bytes(gzip.decompress(path.read_bytes()))
        with closing(sqlite3.connect(raw)) as connection:action(connection);connection.commit()
        path.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(case+"/finished-elsa.sqlite.gz")
        def observed(process):
            snapshot=process["finishedStores"]["elsa"];snapshot["databaseSha256"]=hashlib.sha256(raw.read_bytes()).hexdigest()
            snapshot["gzipSha256"]=etl.q.digest(path);snapshot["observer"]["snapshotSha256"]=snapshot["databaseSha256"]
        self.change(case+"/process.json",observed)


class ActualNativeEtlRecoveryControls(_ActualRecoveryControls):
    def test_all_three_actual_journeys_verify_without_claiming_bpmn_integration(self):
        result=self.verify()
        self.assertEqual(18,result["wholeStoresVerified"])
        self.assertEqual([4,4,3],[case["acceptedRecords"] for case in result["actualReferenceJourneys"]])
        self.assertFalse(result["directOrRunBpmnEtlIntegrationProved"])
        self.assertFalse(result["modelHeldFaultRetryProved"])

    def test_completed_native_worker_cannot_hide_the_etl_business_error(self):
        self.change("worker-business/finished.json",lambda value:value["run"].update(status="succeeded"))
        self.change("worker-business/retained.json",lambda value:value["run"].update(status="succeeded"))
        with self.assertRaisesRegex(ValueError,"terminal or partition accounting"):self.verify()

    def test_rehashed_early_wire_retry_is_rejected(self):
        def early(value):
            attempts=[item for item in value["requests"] if item.get("shard")=="shard-b"]
            attempts[1]["receivedAtMs"]=attempts[0]["receivedAtMs"]+1000
        self.change("sequence/finished-wire.json",early);self.change("sequence/retained-wire.json",early)
        with self.assertRaisesRegex(ValueError,"source retried early"):self.verify()

    def test_graceful_shutdown_cannot_be_called_process_loss(self):
        self.change("flowchart/process.json",lambda value:value.update(predecessorExitCode=0))
        with self.assertRaisesRegex(ValueError,"process-loss/restart provenance"):self.verify()

    def test_physical_result_address_cannot_borrow_a_native_invocation(self):
        path=self.root/"worker-business/before-etl-runtime.sqlite.gz";raw=Path(self.temporary.name)/"changed.sqlite"
        raw.write_bytes(gzip.decompress(path.read_bytes()))
        with closing(sqlite3.connect(raw)) as connection:
            value=json.loads(connection.execute("SELECT InvocationJson FROM elsa_etl_runtime_runs").fetchone()[0])
            value["resultAddress"]["activityExecutionId"]="u16:"+etl.key("foreign-native-invocation")
            connection.execute("UPDATE elsa_etl_runtime_runs SET InvocationJson=?",(json.dumps(value),));connection.commit()
        path.write_bytes(gzip.compress(raw.read_bytes()));self.rehash("worker-business/before-etl-runtime.sqlite.gz")
        def observed(process):
            snapshot=process["beforeStores"]["etl-runtime"];snapshot["databaseSha256"]=hashlib.sha256(raw.read_bytes()).hexdigest()
            snapshot["gzipSha256"]=etl.q.digest(path);snapshot["observer"]["snapshotSha256"]=snapshot["databaseSha256"]
        self.change("worker-business/process.json",observed)
        with self.assertRaisesRegex(ValueError,"result address does not name its real native invocation"):self.verify()


class ActualDirectBpmnEtlRecoveryControls(_ActualRecoveryControls):
    evidence=EVIDENCE.parent/"direct-http-recovery"

    def test_both_actual_model_worker_journeys_verify_with_bounded_claims(self):
        result=self.verify()
        self.assertEqual(12,result["wholeStoresVerified"])
        self.assertTrue(result["directBpmnClosedWorkerIntegrationProved"])
        self.assertFalse(result["runBpmnEtlIntegrationProved"])
        self.assertFalse(result["modelHeldFaultRetryProved"])
        self.assertEqual([4,3],[case["acceptedRecords"] for case in result["actualReferenceJourneys"]])

    def test_completed_worker_cannot_relabel_its_typed_business_error(self):
        def hide(value):
            for result in (value["model"]["payload"]["nativeResults"][0],value["resultSource"]["intent"]["payload"]):
                result["completion"]["outcomeKey"]="Done";result["completion"]["result"]["outcome"]=0
        for phase in ("finished","retained"):self.change("direct-business/"+phase+"-checked-model.json",hide)
        with self.assertRaisesRegex(ValueError,"typed RunEtl result"):self.verify()

    def test_acknowledgement_requires_the_exact_model_receipt(self):
        for phase in ("finished","retained"):self.change("direct-success/"+phase+"-checked-model.json",lambda value:value.update(resultInput=None))
        with self.assertRaisesRegex(ValueError,"acknowledged without its exact model consumption"):self.verify()

    def test_rehashed_physical_model_receipt_cannot_borrow_a_fingerprint(self):
        def corrupt(connection):
            for identity,raw in connection.execute("SELECT Id,ContentJson FROM elsa_runtime_model_input"):
                value=json.loads(raw)
                if value["inputKind"]==etl.key("native-result"):
                    value["contentFingerprint"]=etl.key("foreign-fingerprint")
                    connection.execute("UPDATE elsa_runtime_model_input SET ContentJson=? WHERE Id=?",(json.dumps(value),identity))
        self.change_store("direct-success",corrupt)
        with self.assertRaisesRegex(ValueError,"physical model receipt lost its exact"):self.verify()

    def test_rehashed_physical_worker_cannot_redirect_its_frozen_return(self):
        def corrupt(connection):
            value=json.loads(connection.execute("SELECT ContentJson FROM elsa_runtime_workflow_execution_state").fetchone()[0])
            value["modelWorkOrigin"]["resultInputId"]=etl.key("foreign-model-return")
            connection.execute("UPDATE elsa_runtime_workflow_execution_state SET ContentJson=?",(json.dumps(value),))
        self.change_store("direct-success",corrupt)
        with self.assertRaisesRegex(ValueError,"physical worker lost its protected exact model origin"):self.verify()


class ActualDirectPipelineCancellationControls(_ActualRecoveryControls):
    evidence=EVIDENCE.parent/"direct-cancellation"

    def test_actual_cancelled_pipeline_worker_and_model_keep_bounded_claims(self):
        result=self.verify();self.assertEqual(6,result["wholeStoresVerified"])
        self.assertTrue(result["pipelineOriginatedCancellationProved"])
        self.assertFalse(result["modelToWorkerCancellationProved"]);self.assertFalse(result["runBpmnEtlIntegrationProved"])
        self.assertFalse(result["modelHeldFaultRetryProved"])
        self.assertEqual(2,result["actualReferenceJourneys"][0]["physicalOutputRecords"])

    def test_cancelled_native_work_cannot_be_relabeled_as_completed(self):
        def completed(value):
            value["worker"]["status"]=3;value["model"]["payload"]["nativeResults"][0]["status"]=3
        for phase in ("finished","retained"):self.change("direct-cancel/"+phase+"-checked-model.json",completed)
        with self.assertRaisesRegex(ValueError,"typed terminal outcome"):self.verify()

    def test_cancellation_cannot_erase_already_delivered_record_accounting(self):
        for phase in ("finished","retained"):
            self.change("direct-cancel/"+phase+".json",lambda value:value["run"].update(acceptedRecords=0))
        with self.assertRaisesRegex(ValueError,"terminal or partition accounting"):self.verify()

    def test_an_early_read_cannot_prove_stability_after_the_retry_deadline(self):
        self.change("direct-cancel/process.json",lambda value:value.update(observedCancelledAfterRetryDeadlineAtUtc=value["cutAtUtc"]))
        with self.assertRaisesRegex(ValueError,"accepted request and post-deadline observation"):self.verify()

    def test_cancelled_http_work_cannot_resume_its_saved_retry(self):
        def retry(value):
            original=next(item for item in value["requests"] if item.get("shard")=="shard-b")
            value["requests"].append({**original,"status":200,"receivedAtMs":original["receivedAtMs"]+60000})
        for phase in ("finished","retained"):self.change("direct-cancel/"+phase+"-wire.json",retry)
        with self.assertRaisesRegex(ValueError,"cancelled HTTP shard resumed"):self.verify()

    def test_physical_native_cancellation_cannot_borrow_a_completed_header(self):
        self.change_store("direct-cancel",lambda connection:connection.execute("UPDATE elsa_runtime_workflow_execution_state SET Status=3"))
        with self.assertRaisesRegex(ValueError,"physical native ETL workflow lost its exact pin or state"):self.verify()

    def test_cancelled_header_cannot_replace_the_typed_etl_consumption_receipt(self):
        def unconsumed(connection):
            value=json.loads(connection.execute("SELECT ContentJson FROM elsa_runtime_activity_execution_state").fetchone()[0])
            value["triggerDeliveries"][0]["status"]="Recorded"
            connection.execute("UPDATE elsa_runtime_activity_execution_state SET ContentJson=?",(json.dumps(value),))
        self.change_store("direct-cancel",unconsumed)
        with self.assertRaisesRegex(ValueError,"exact typed ETL consumption receipt"):self.verify()


if __name__=="__main__":unittest.main()
