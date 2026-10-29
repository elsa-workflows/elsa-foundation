"""Mechanical capture/upgrade evidence safeguards; host journeys supply recovery proof."""
import importlib.util
import base64
from contextlib import closing
from datetime import datetime, timezone
import json
import gzip
import os
from pathlib import Path
import signal
import shlex
import shutil
import sqlite3
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[2] / "tools/bpmn/qualify-hosting.py"
spec = importlib.util.spec_from_file_location("qualify_hosting", SCRIPT)
capture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(capture)


class HostingCaptureTests(unittest.TestCase):
    def test_owner_paths_allow_canonical_aliases_but_refuse_a_different_root(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            actual = root / "actual"; actual.mkdir()
            alias = root / "alias"; alias.symlink_to(actual, target_is_directory=True)
            assembly = actual / "host.dll"; assembly.write_bytes(b"owned-host")
            manifest = actual / "owner.json"; manifest.write_text("{}")
            start = "Wed Oct 28 04:41:48 2026"
            epoch = int(datetime.strptime(start, "%a %b %d %H:%M:%S %Y").astimezone(timezone.utc).timestamp())
            fields = {(42, "ppid"): "41", (42, "lstart"): start,
                (42, "command"): shlex.join(["dotnet", str(alias / "host.dll"), "--contentRoot", str(alias)]),
                (41, "command"): shlex.join(["python3", "run_owned_broadcast_probe.py", "--manifest", str(alias / "owner.json"),
                    "--content-root", str(alias), "--base-url", "http://127.0.0.1:1"])}
            args = SimpleNamespace(assembly=assembly.resolve(), content_root=actual.resolve(), owner_manifest=manifest.resolve(), base_url="http://127.0.0.1:1")
            owner = {"processId": 42, "controllerProcessId": 41, "startEpochSeconds": epoch}
            with patch.object(capture, "process_field", side_effect=lambda pid, field: fields[(pid, field)]):
                self.assertEqual(42, capture.verify_owned(args, owner)["processId"])
                args.content_root = root
                with self.assertRaisesRegex(ValueError, "content root differs"):
                    capture.verify_owned(args, owner)

    def test_snapshot_includes_committed_wal_and_excludes_uncommitted_work(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "source.sqlite"
            writer = sqlite3.connect(source)
            try:
                writer.execute("pragma journal_mode=WAL")
                writer.execute("create table results(value text)")
                writer.execute("insert into results values ('accepted')")
                writer.commit()
                writer.execute("insert into results values ('not-committed')")
                target = root / "snapshot.sqlite"
                capture.backup_sqlite(source, target)
                with sqlite3.connect(target) as copied:
                    self.assertEqual([("accepted",)], copied.execute("select value from results").fetchall())
                self.assertEqual(2, writer.execute("select count(*) from results").fetchone()[0])
                with self.assertRaises(ValueError):
                    capture.backup_sqlite(source, target)
            finally:
                writer.close()

    def test_capture_resumes_its_exact_stopped_host_when_snapshot_fails(self):
        with patch.object(capture, "verify_owned", return_value={"processId": 4242}) as verify, \
                patch.object(capture, "process_field", return_value="T"), patch.object(capture.os, "kill") as kill:
            with self.assertRaisesRegex(OSError, "snapshot unavailable"):
                with capture.quiesce_owned(SimpleNamespace(), {}):
                    raise OSError("snapshot unavailable")
            self.assertEqual(2, verify.call_count)
            self.assertEqual([(4242, signal.SIGSTOP), (4242, signal.SIGCONT)], [c.args for c in kill.call_args_list])

    def test_refused_owner_is_never_stopped(self):
        with patch.object(capture, "verify_owned", side_effect=ValueError("foreign process")), patch.object(capture.os, "kill") as kill:
            with self.assertRaisesRegex(ValueError, "foreign process"):
                with capture.quiesce_owned(SimpleNamespace(), {}):
                    self.fail("A refused process reached the capture cut.")
            kill.assert_not_called()

    def test_bundle_path_cannot_escape_via_parent_or_symlink(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            bundle = root / "bundle"; bundle.mkdir()
            outside = root / "outside.json"; outside.write_text("{}")
            (bundle / "link.json").symlink_to(outside)
            for name in ("../outside.json", str(outside), "link.json"):
                with self.subTest(name=name), self.assertRaises(ValueError):
                    capture.evidence_file(bundle, name)

    def test_api_view_cannot_substitute_another_execution_pin_or_mode(self):
        case = {"workflowExecutionId": "expected", "publication": {"artifactId": "pin"}, "persistenceMode": "Durable"}
        view = {"instance": {"workflowExecutionId": "expected", "artifactId": "pin", "status": "Running"},
                "persistenceMode": "Durable", "incidents": [], "activities": [
                    {"executableNodeId": "wait-a", "activityExecutionId": "a", "status": "Completed"},
                    {"executableNodeId": "wait-b", "activityExecutionId": "b", "status": "Suspended"}]}
        self.assertEqual("expected", capture.observe_case(case, view)["workflowExecutionId"])
        for key in ("workflowExecutionId", "artifactId"):
            wrong = json.loads(json.dumps(view)); wrong["instance"][key] = "foreign"
            with self.subTest(key=key), self.assertRaises(ValueError):
                capture.observe_case(case, wrong)
        view["persistenceMode"] = "Balanced"
        with self.assertRaises(ValueError):
            capture.observe_case(case, view)

    def test_missing_hashed_snapshot_cannot_be_reported_as_verified(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            path.write_text(json.dumps({"schemaVersion": 1, "kind": "actual-legacy-host-capture",
                "executingSourceCommit": "a" * 40, "assets": [], "caseReceipt": "cases.json", "frozenContractReceipt": "frozen.json"}))
            with self.assertRaisesRegex(ValueError, "required hashed asset"):
                capture.verify_capture(path)

    def test_capture_url_refuses_remote_or_credential_bearing_target(self):
        for base in ("https://example.com", "http://127.0.0.1@elsewhere:53186", "http://admin:secret@127.0.0.1:53186", "http://127.0.0.1:53186?redirect=elsewhere"):
            with self.subTest(base=base), self.assertRaises(ValueError):
                capture.Api(base)


@unittest.skipUnless(os.environ.get("ELSA_BPMN_CAPTURE_MANIFEST"), "Actual captured bundle must be supplied explicitly")
class ActualCaptureControls(unittest.TestCase):
    def setUp(self):
        self.original = Path(os.environ["ELSA_BPMN_CAPTURE_MANIFEST"]).resolve(strict=True)
        capture.verify_capture(self.original)
        self.before = capture.digest(self.original)
        self.directory = tempfile.TemporaryDirectory()
        self.bundle = Path(self.directory.name) / "capture"
        shutil.copytree(self.original.parent, self.bundle)
        self.path = self.bundle / self.original.name
        self.manifest = json.loads(self.path.read_text())

    def tearDown(self):
        self.assertEqual(self.before, capture.digest(self.original))
        capture.verify_capture(self.original)
        self.directory.cleanup()

    def test_changed_actual_database_is_refused(self):
        database = self.bundle / "runtime.sqlite"
        with database.open("r+b") as stream:
            stream.seek(100); byte = stream.read(1); stream.seek(100); stream.write(bytes([byte[0] ^ 1]))
        with self.assertRaisesRegex(ValueError, "retained hash"):
            capture.verify_capture(self.path)

    def test_wrong_case_execution_is_refused_even_after_rehashing_the_receipt(self):
        receipt = self.bundle / self.manifest["caseReceipt"]
        cases = json.loads(receipt.read_text())
        cases[0]["workflowExecutionId"] = "different-execution"
        capture.write_json(receipt, cases)
        next(a for a in self.manifest["assets"] if a["path"] == receipt.name)["sha256"] = capture.digest(receipt)
        capture.write_json(self.path, self.manifest)
        with self.assertRaisesRegex(ValueError, "execution, immutable pin or persistence mode"):
            capture.verify_capture(self.path)

    def test_unbound_actual_database_cannot_be_omitted_from_the_manifest(self):
        self.manifest["assets"] = [a for a in self.manifest["assets"] if a["path"] != "runtime.sqlite"]
        capture.write_json(self.path, self.manifest)
        with self.assertRaisesRegex(ValueError, "required hashed asset"):
            capture.verify_capture(self.path)

    def test_frozen_receipt_from_another_snapshot_is_refused_after_rehashing(self):
        receipt = self.bundle / self.manifest["frozenContractReceipt"]
        frozen = json.loads(receipt.read_text()); frozen["closedSnapshotSha256"] = "0" * 64
        capture.write_json(receipt, frozen)
        next(a for a in self.manifest["assets"] if a["path"] == receipt.name)["sha256"] = capture.digest(receipt)
        capture.write_json(self.path, self.manifest)
        with self.assertRaisesRegex(ValueError, "frozen authoring verification"):
            capture.verify_capture(self.path)


class ActualUpgradeControls(unittest.TestCase):
    def setUp(self):
        default = SCRIPT.parents[2] / "specs/1017-direct-bpmn-hosting/evidence/upgrade/partial-forks/manifest.json"
        self.original = Path(os.environ.get("ELSA_BPMN_UPGRADE_MANIFEST", default)).resolve(strict=True)
        capture.verify_upgrade(self.original)
        self.before = capture.digest(self.original)
        self.directory = tempfile.TemporaryDirectory()
        self.bundle = Path(self.directory.name) / "upgrade"
        shutil.copytree(self.original.parent, self.bundle)
        self.path = self.bundle / self.original.name
        self.manifest = json.loads(self.path.read_text())
        self.process_path = self.bundle / "legacy-partial-forks/process.json"
        self.process = json.loads(self.process_path.read_text())

    def tearDown(self):
        self.assertEqual(self.before, capture.digest(self.original))
        capture.verify_upgrade(self.original)
        self.directory.cleanup()

    def rehash(self, path):
        name = path.relative_to(self.bundle).as_posix()
        next(asset for asset in self.manifest["assets"] if asset["path"] == name)["sha256"] = capture.digest(path)
        capture.write_json(self.path, self.manifest)

    def change_runtime(self, sql):
        snapshot = self.bundle / "legacy-partial-forks/upgraded-runtime.sqlite.gz"
        database = Path(self.directory.name) / "changed.sqlite"
        database.write_bytes(gzip.decompress(snapshot.read_bytes()))
        with closing(sqlite3.connect(database)) as connection:
            with connection:
                connection.execute(sql)
            connection.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        snapshot.write_bytes(gzip.compress(database.read_bytes(), mtime=0))
        self.process["snapshot"]["databaseSha256"] = capture.digest(database)
        self.process["snapshot"]["gzipSha256"] = capture.digest(snapshot)
        capture.write_json(self.process_path, self.process)
        self.rehash(snapshot); self.rehash(self.process_path)

    def test_physical_nonterminal_state_refuses_completed_api_evidence_after_rehashing(self):
        self.change_runtime("UPDATE elsa_runtime_workflow_execution_state SET Status = 1")
        with self.assertRaisesRegex(ValueError, "physical workflow row"):
            capture.verify_upgrade(self.path)

    def test_changed_original_executable_refuses_success_evidence_after_rehashing(self):
        self.change_runtime("UPDATE elsa_runtime_workflow_executable SET ContentJson = ContentJson || ' '")
        with self.assertRaisesRegex(ValueError, "original executable"):
            capture.verify_upgrade(self.path)

    def test_replaced_wait_identity_is_refused_after_rehashing(self):
        activities = self.process["observations"][0]["completed"]["activities"]
        next(activity for activity in activities if activity["executableNodeId"] == "wait-b")["activityExecutionId"] = "repeated-wait"
        capture.write_json(self.process_path, self.process); self.rehash(self.process_path)
        with self.assertRaisesRegex(ValueError, "exact continuation"):
            capture.verify_upgrade(self.path)

    def test_unrelated_baseline_is_refused_after_rehashing(self):
        self.process["baselineSourceRevision"] = "b" * 40
        capture.write_json(self.process_path, self.process); self.rehash(self.process_path)
        with self.assertRaisesRegex(ValueError, "declared baseline"):
            capture.verify_upgrade(self.path)

    def test_frozen_receipt_from_another_upgraded_store_is_refused_after_rehashing(self):
        path = self.bundle / "frozen-contract.json"
        frozen = json.loads(path.read_text()); frozen["closedSnapshotSha256"] = "0" * 64
        capture.write_json(path, frozen); self.rehash(path)
        with self.assertRaisesRegex(ValueError, "frozen authoring verification"):
            capture.verify_upgrade(self.path)


class ActualNativeParentUpgradeControls(unittest.TestCase):
    def setUp(self):
        default = SCRIPT.parents[2] / "specs/1017-direct-bpmn-hosting/evidence/upgrade/native-parents/manifest.json"
        self.original = Path(os.environ.get("ELSA_BPMN_NATIVE_UPGRADE_MANIFEST", default)).resolve(strict=True)
        capture.verify_upgrade(self.original)
        self.before = capture.digest(self.original)
        self.directory = tempfile.TemporaryDirectory()
        self.bundle = Path(self.directory.name) / "upgrade"
        shutil.copytree(self.original.parent, self.bundle)
        self.path = self.bundle / "manifest.json"
        self.manifest = json.loads(self.path.read_text())
        self.pending_path = self.bundle / "legacy-pending-parent/process.json"
        self.pending = json.loads(self.pending_path.read_text())

    def tearDown(self):
        self.assertEqual(self.before, capture.digest(self.original))
        capture.verify_upgrade(self.original)
        self.directory.cleanup()

    def rehash(self, path):
        name = path.relative_to(self.bundle).as_posix()
        next(asset for asset in self.manifest["assets"] if asset["path"] == name)["sha256"] = capture.digest(path)
        capture.write_json(self.path, self.manifest)

    def save_pending(self):
        capture.write_json(self.pending_path, self.pending); self.rehash(self.pending_path)

    def test_sequence_terminal_disposition_cannot_be_called_parent_consumption(self):
        case = next(case for case in self.pending["observations"] if case["parent"] == "Sequence")
        case["parentOutcomeApplied"] = True
        self.save_pending()
        with self.assertRaisesRegex(ValueError, "cannot be called parent consumption"):
            capture.verify_upgrade(self.path)

    def test_saved_callback_must_be_physically_delivered_even_after_rehashing(self):
        case = self.pending["observations"][0]
        snapshot = self.bundle / "legacy-pending-parent" / case["snapshot"]["file"]
        database = Path(self.directory.name) / "changed.sqlite"
        database.write_bytes(gzip.decompress(snapshot.read_bytes()))
        with closing(sqlite3.connect(database)) as connection:
            with connection:
                connection.execute("UPDATE elsa_runtime_post_commit_outbox SET Status = 0 WHERE OutboxItemId = ?",
                    (base64.b64encode(case["sourceOutboxItemId"].encode("utf-16-le")).decode(),))
            connection.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        snapshot.write_bytes(gzip.compress(database.read_bytes(), mtime=0))
        case["snapshot"]["databaseSha256"] = capture.digest(database)
        case["snapshot"]["gzipSha256"] = capture.digest(snapshot)
        self.rehash(snapshot); self.save_pending()
        with self.assertRaisesRegex(ValueError, "physical original callback"):
            capture.verify_upgrade(self.path)

    def test_changed_committed_callback_intent_is_refused_after_rehashing(self):
        case = self.pending["observations"][0]
        case["acceptedSource"]["document"]["intent"]["payload"]["Payload"]["ActivityExecutionId"] = "foreign-parent"
        self.save_pending()
        with self.assertRaisesRegex(ValueError, "original callback changed"):
            capture.verify_upgrade(self.path)

    def test_foreign_complete_pin_is_refused_even_when_all_callback_copies_match(self):
        case = self.pending["observations"][0]
        for source in ("originalSource", "acceptedSource", "replayedSource"):
            case[source]["document"]["intent"]["payload"]["Payload"]["PinnedExecutable"]["DefinitionVersionId"] = "foreign-version"
        self.save_pending()
        with self.assertRaisesRegex(ValueError, "complete executable pin differs"):
            capture.verify_upgrade(self.path)

    def test_compensated_parent_cannot_be_relabelled_done_after_rehashing(self):
        case = next(case for case in self.pending["observations"] if case["parent"] == "CompensationScope")
        parent = next(activity for activity in case["completed"]["activities"] if activity["executableNodeId"] == "native-parent")
        parent["outcomeNames"] = ["Done"]
        self.save_pending()
        with self.assertRaisesRegex(ValueError, "original composite projection"):
            capture.verify_upgrade(self.path)

    def test_retained_terminal_cannot_be_revived_after_rehashing(self):
        path = self.bundle / "legacy-native-terminals/process.json"
        process = json.loads(path.read_text()); process["observations"][0]["recovered"]["instance"]["status"] = "Running"
        capture.write_json(path, process); self.rehash(path)
        with self.assertRaisesRegex(ValueError, "existing native terminal changed"):
            capture.verify_upgrade(self.path)


class ActualCalledAcknowledgmentUpgradeControls(unittest.TestCase):
    def setUp(self):
        self.original = SCRIPT.parents[2] / "specs/1017-direct-bpmn-hosting/evidence/upgrade/called-ack/manifest.json"
        capture.verify_upgrade(self.original); self.before = capture.digest(self.original)
        self.directory = tempfile.TemporaryDirectory(); self.bundle = Path(self.directory.name) / "upgrade"
        shutil.copytree(self.original.parent, self.bundle); self.path = self.bundle / "manifest.json"
        self.manifest = json.loads(self.path.read_text()); self.process_path = self.bundle / "legacy-called-ack/process.json"
        self.process = json.loads(self.process_path.read_text()); self.case = self.process["observations"][0]

    def tearDown(self):
        self.assertEqual(self.before, capture.digest(self.original)); capture.verify_upgrade(self.original)
        self.directory.cleanup()

    def rehash(self, path):
        name = path.relative_to(self.bundle).as_posix()
        next(asset for asset in self.manifest["assets"] if asset["path"] == name)["sha256"] = capture.digest(path)
        capture.write_json(self.path, self.manifest)

    def save_process(self):
        capture.write_json(self.process_path, self.process); self.rehash(self.process_path)

    def mutate_snapshot(self, mutate):
        snapshot = self.case["recoveredSnapshot"]; path = self.bundle / "legacy-called-ack" / snapshot["file"]
        database = Path(self.directory.name) / "changed.sqlite"; database.write_bytes(gzip.decompress(path.read_bytes()))
        with closing(sqlite3.connect(database)) as connection:
            with connection: mutate(connection)
            connection.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        path.write_bytes(gzip.compress(database.read_bytes(), mtime=0)); snapshot["databaseSha256"] = capture.digest(database)
        snapshot["gzipSha256"] = capture.digest(path); self.case["recoveredProof"]["snapshotSha256"] = capture.digest(database)
        self.rehash(path); self.save_process()

    def test_acknowledged_source_must_be_physically_delivered_after_rehashing(self):
        identity = base64.b64encode(self.case["marker"]["sourceOutboxItemId"].encode("utf-16-le")).decode()
        self.mutate_snapshot(lambda connection: connection.execute("UPDATE elsa_runtime_post_commit_outbox SET Status = 0 WHERE OutboxItemId = ?", (identity,)))
        with self.assertRaisesRegex(ValueError, "physical original acknowledgment source"):
            capture.verify_upgrade(self.path)

    def test_dispatch_ack_cannot_replace_committed_caller_consumption(self):
        def lose_acceptance(connection):
            identity = self.case["marker"]["transport"]["callingActivityExecutionId"]
            identity = base64.b64encode(identity.encode("utf-16-le")).decode()
            content = json.loads(connection.execute("SELECT ContentJson FROM elsa_runtime_activity_execution_state WHERE ActivityExecutionId = ?", (identity,)).fetchone()[0])
            content["childControlInbox"]["nextOrdinal"] = 0
            connection.execute("UPDATE elsa_runtime_activity_execution_state SET ContentJson = ? WHERE ActivityExecutionId = ?", (json.dumps(content), identity))
        self.mutate_snapshot(lose_acceptance)
        with self.assertRaisesRegex(ValueError, "physical caller inbox"):
            capture.verify_upgrade(self.path)

    def test_repeated_normal_successor_is_refused_after_rehashing(self):
        activities = self.case["finishedProof"]["calledWorkflow"]["facts"]["parentActivities"]
        activities.append(next(activity for activity in activities if activity["execution"]["executableNodeId"] == "node-normal"))
        self.save_process()
        with self.assertRaisesRegex(ValueError, "normal successor was lost or repeated"):
            capture.verify_upgrade(self.path)

    def test_foreign_full_parent_pin_is_refused_after_rehashing(self):
        self.case["recoveredProof"]["calledWorkflow"]["facts"]["parent"]["pinnedExecutable"]["definitionVersionId"] = "foreign"
        self.save_process()
        with self.assertRaisesRegex(ValueError, "complete pin"):
            capture.verify_upgrade(self.path)

    def test_sender_success_without_checked_acceptance_is_refused(self):
        self.case["recoveredProof"]["calledWorkflow"]["exactCommittedAcceptanceVerified"] = False
        self.save_process()
        with self.assertRaisesRegex(ValueError, "exact checked acceptance/source proof"):
            capture.verify_upgrade(self.path)

    def test_changed_original_transport_is_refused_after_rehashing(self):
        self.case["recoveredProof"]["calledWorkflow"]["source"]["intent"]["payload"]["callingActivityExecutionId"] = "foreign"
        self.save_process()
        with self.assertRaisesRegex(ValueError, "original called transport changed"):
            capture.verify_upgrade(self.path)


if __name__ == "__main__":
    unittest.main()
