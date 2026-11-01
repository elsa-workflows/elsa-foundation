"""Exercise corrupt closed copies of actual RunBpmn recovery captures, never live stores."""
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

_spec=importlib.util.spec_from_file_location("runbpmn_recovery",Path(__file__).with_name("verify_runbpmn_recovery.py"))
runbpmn=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(runbpmn)
EVIDENCE=runbpmn.q.REPO/"specs/1017-direct-bpmn-hosting/evidence/embedding/human-native-recovery"


class ActualRunBpmnRecoveryControls(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory(prefix="elsa-runbpmn-controls-");self.addCleanup(self.temporary.cleanup)
        self.root=Path(self.temporary.name)/"evidence";shutil.copytree(EVIDENCE,self.root)

    def rehash(self,relative):
        path=self.root/"manifest.json";manifest=json.loads(path.read_text())
        next(item for item in manifest["assets"] if item["path"]==relative)["sha256"]=runbpmn.q.digest(self.root/relative)
        runbpmn.q.write_json(path,manifest)

    def change(self,relative,action):
        path=self.root/relative;value=json.loads(path.read_text());action(value);runbpmn.q.write_json(path,value);self.rehash(relative)

    def change_terminal(self,case,action):
        for phase in ("completed","retained"):self.change(case+"/"+phase+"-checked.json",action)

    def change_store(self,case,action):
        relative=case+"/completed-runtime.sqlite.gz";path=self.root/relative;raw=Path(self.temporary.name)/"changed.sqlite"
        raw.write_bytes(gzip.decompress(path.read_bytes()))
        with closing(sqlite3.connect(raw)) as connection:action(connection);connection.commit()
        path.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(relative)
        def observed(value):
            snapshot=next(item for item in value["snapshots"] if item["file"]=="completed-runtime.sqlite.gz")
            snapshot["databaseSha256"]=hashlib.sha256(raw.read_bytes()).hexdigest();snapshot["gzipSha256"]=runbpmn.q.digest(path)
        self.change(case+"/case.json",observed)

    def verify(self):return runbpmn.verify(self.root/"manifest.json")

    def test_eight_actual_three_process_journeys_keep_bounded_claims(self):
        result=self.verify()
        self.assertEqual(8,len(result["actualJourneys"]));self.assertEqual(32,result["wholeStoresVerified"])
        self.assertEqual(24,result["nativeProcesses"]);self.assertTrue(result["humanModelEmbeddingProved"])
        self.assertEqual(4,sum(item["directTraceCompared"] for item in result["actualJourneys"]))
        for field in ("etlEmbeddingProved","pendingParentResultCrashProved","cancellationBeforeAdmissionProved","upgradedHostOrStudioProved"):
            self.assertFalse(result[field])

    def test_native_completion_cannot_borrow_another_model_result(self):
        def foreign(value):
            activity=next(item for item in value["activities"] if item["execution"]["activityType"]==runbpmn.ACTIVITY)
            activity["completion"]["result"]["inlineValue"]["executionId"]="foreign-model"
        self.change_terminal("root-complete",foreign)
        with self.assertRaisesRegex(ValueError,"exact typed consumed registration"):self.verify()

    def test_native_ack_requires_the_exact_model_settlement_receipt(self):
        self.change_terminal("root-complete",lambda value:value.update(inputs=[item for item in value["inputs"] if item["inputKind"]!="native-parent-settled"]))
        with self.assertRaisesRegex(ValueError,"exact model settlement receipt"):self.verify()

    def test_physical_consumption_receipt_cannot_borrow_a_fingerprint(self):
        def corrupt(connection):
            for identity,raw in connection.execute("SELECT Id,ContentJson FROM elsa_runtime_model_input").fetchall():
                value=json.loads(raw)
                if value["inputKind"]==runbpmn.key("native-parent-settled"):
                    value["contentFingerprint"]=runbpmn.key("foreign-fingerprint")
                    connection.execute("UPDATE elsa_runtime_model_input SET ContentJson=? WHERE Id=?",(json.dumps(value),identity))
        self.change_store("root-complete",corrupt)
        with self.assertRaisesRegex(ValueError,"physical model consumption receipt lost its exact identity"):self.verify()

    def test_physical_native_publication_cannot_lose_its_model_dependency(self):
        def corrupt(connection):
            for identity,raw in connection.execute("SELECT ArtifactId,ContentJson FROM elsa_runtime_workflow_executable").fetchall():
                value=json.loads(raw)
                if value.get("dependencies"):
                    value["dependencies"][0]["artifactHash"]=runbpmn.key("foreign-model-hash")
                    connection.execute("UPDATE elsa_runtime_workflow_executable SET ContentJson=? WHERE ArtifactId=?",(json.dumps(value),identity))
        self.change_store("root-complete",corrupt)
        with self.assertRaisesRegex(ValueError,"native publication lost its model dependency"):self.verify()

    def test_trace_comparison_cannot_normalize_a_changed_diagnostic(self):
        self.change_terminal("root-complete",lambda value:value["directCounterpart"]["payload"]["state"]["diagnostics"][0].update(message="Changed semantic decision"))
        with self.assertRaisesRegex(ValueError,"semantic states differ beyond the declared host handle"):self.verify()

    def test_graceful_exit_cannot_be_called_process_loss(self):
        self.change("root-complete/case.json",lambda value:value.update(predecessorExit=0))
        with self.assertRaisesRegex(ValueError,"three-process loss/recovery provenance"):self.verify()

    def test_late_human_completion_cannot_advance_a_cancelled_model(self):
        def advance(value):
            receipt=next(item for item in value["inputs"] if item["inputKind"]=="complete-human")
            receipt["acceptedRevision"]=4;receipt["result"]={"status":"Accepted","code":None}
        self.change_terminal("root-cancel",advance)
        with self.assertRaisesRegex(ValueError,"Late human completion advanced cancelled BPMN"):self.verify()


if __name__=="__main__":unittest.main()
