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


class ActualNativeEtlRecoveryControls(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory(prefix="elsa-etl-reference-negative-");self.addCleanup(self.temporary.cleanup)
        self.root=Path(self.temporary.name)/"evidence";shutil.copytree(EVIDENCE,self.root)

    def rehash(self,relative):
        path=self.root/"manifest.json";manifest=json.loads(path.read_text())
        next(item for item in manifest["assets"] if item["path"]==relative)["sha256"]=etl.q.digest(self.root/relative)
        etl.q.write_json(path,manifest)

    def change(self,relative,action):
        path=self.root/relative;value=json.loads(path.read_text());action(value);etl.q.write_json(path,value);self.rehash(relative)

    def verify(self):return etl.verify(self.root/"manifest.json")

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


if __name__=="__main__":unittest.main()
