"""Mutate only private closed copies of the actual pre-admission cancellation receipt."""
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

_spec=importlib.util.spec_from_file_location("admission_cancel",Path(__file__).with_name("verify_runbpmn_admission_cancel.py"))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.b.q.REPO/"specs/1017-direct-bpmn-hosting/evidence/embedding/cancel-before-admission"

class ActualAdmissionCancellationControls(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory(prefix="elsa-admission-cancel-controls-");self.addCleanup(self.temporary.cleanup)
        self.root=Path(self.temporary.name)/"evidence";shutil.copytree(EVIDENCE,self.root)

    def rehash(self,name):
        path=self.root/"manifest.json";value=json.loads(path.read_text())
        next(item for item in value["assets"] if item["path"]==name)["sha256"]=v.b.q.digest(self.root/name)
        v.b.q.write_json(path,value)

    def change(self,name,action):
        path=self.root/name;value=json.loads(path.read_text());action(value);v.b.q.write_json(path,value);self.rehash(name)

    def change_store(self,action):
        path=self.root/"cancelled-runtime.sqlite.gz";raw=Path(self.temporary.name)/"changed.sqlite"
        raw.write_bytes(gzip.decompress(path.read_bytes()))
        with closing(sqlite3.connect(raw)) as connection:action(connection);connection.commit()
        path.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(path.name)
        def observed(value):
            snapshot=next(item for item in value["snapshots"] if item["file"]==path.name)
            snapshot["gzipSha256"]=v.b.q.digest(path);snapshot["databaseSha256"]=hashlib.sha256(raw.read_bytes()).hexdigest()
        self.change("case.json",observed)

    def verify(self):return v.verify(self.root/"manifest.json")

    def test_actual_three_process_root_cut_verifies_with_bounded_claims(self):
        result=self.verify();self.assertEqual(4,result["wholeStoresVerified"])
        self.assertEqual(3,result["workbenchProcesses"]);self.assertTrue(result["rootCancellationBeforeFirstModelAdmissionProved"])
        for field in ("interpreterStarted","pendingParentResultCrashProved","etlEmbeddingProved","upgradedHostOrStudioProved"):
            self.assertFalse(result[field])

    def test_an_existing_model_cannot_be_called_pre_admission(self):
        model=json.loads((self.root/"completed-checked.json").read_text())["model"]
        self.change("cancelled-checked.json",lambda value:value.update(model=model))
        with self.assertRaisesRegex(ValueError,"model already existed"):self.verify()

    def test_started_semantics_cannot_be_called_an_unstarted_cancellation(self):
        for name in ("completed-checked.json","retained-checked.json"):
            self.change(name,lambda value:value["model"]["payload"]["state"].update(sequence=1))
        with self.assertRaisesRegex(ValueError,"started semantics or lost its terminal origin"):self.verify()

    def test_a_physically_running_parent_cannot_be_called_cancelled(self):
        self.change_store(lambda connection:connection.execute("UPDATE elsa_runtime_workflow_execution_state SET Status=1"))
        with self.assertRaisesRegex(ValueError,"physical parent was not cancelled"):self.verify()

    def test_a_physical_source_cannot_borrow_another_wait(self):
        def corrupt(connection):
            for identity,raw in connection.execute("SELECT OutboxItemId,ContentJson FROM elsa_runtime_post_commit_outbox").fetchall():
                value=json.loads(raw)
                if value["intent"]["kind"]==v.b.key(v.b.START):
                    value["intent"]["payload"]["registrationId"]="u16:"+v.b.key("foreign-wait")
                    connection.execute("UPDATE elsa_runtime_post_commit_outbox SET ContentJson=? WHERE OutboxItemId=?",(json.dumps(value),identity))
        self.change_store(corrupt)
        with self.assertRaisesRegex(ValueError,"physical pre-admission source lost its owed immutable address"):self.verify()

    def test_result_ack_cannot_precede_its_exact_settlement(self):
        def early(value):
            source=next(item for item in value["sources"] if item["intent"]["kind"]==v.b.RESULT)
            source["deliveredAt"]="2026-01-01T00:00:00+00:00"
        for name in ("completed-checked.json","retained-checked.json"):self.change(name,early)
        with self.assertRaisesRegex(ValueError,"acknowledged without exact settlement"):self.verify()

if __name__=="__main__":unittest.main()
