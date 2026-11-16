"""Rehashed corrupt controls over copies of the actual upgraded Studio evidence."""
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

SCRIPT = Path(__file__).resolve().parents[2] / "tools/bpmn/verify_studio_upgrade.py"
spec = importlib.util.spec_from_file_location("verify_studio_upgrade", SCRIPT)
studio = importlib.util.module_from_spec(spec); spec.loader.exec_module(studio)
EVIDENCE = studio.q.REPO / "specs/1017-direct-bpmn-hosting/evidence/studio"


class ActualStudioUpgradeControls(unittest.TestCase):
    evidence = EVIDENCE

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="elsa-studio-negative-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / "evidence"
        shutil.copytree(self.evidence, self.root)

    def rehash(self, relative):
        path = self.root / "manifest.json"; manifest = json.loads(path.read_text())
        asset = next(asset for asset in manifest["assets"] if asset["path"] == relative)
        asset["sha256"] = studio.q.digest(self.root / relative)
        if relative.endswith(".json.gz"):
            raw = gzip.decompress((self.root / relative).read_bytes())
            asset.update(decodedSha256=hashlib.sha256(raw).hexdigest(), decodedBytes=len(raw))
        studio.q.write_json(path, manifest)

    def change(self, relative, action):
        if relative.endswith("process.json"): relative += ".gz"
        path = self.root / relative
        value = json.loads(gzip.decompress(path.read_bytes()) if relative.endswith(".gz") else path.read_text()); action(value)
        if relative.endswith(".gz"): path.write_bytes(gzip.compress((json.dumps(value, indent=2) + "\n").encode()))
        else: studio.q.write_json(path, value)
        self.rehash(relative)

    def verify(self):
        return studio.verify(self.root / "manifest.json")

    def test_both_actual_studio_journeys_and_all_twelve_stores_verify(self):
        result = self.verify()
        self.assertEqual(4, result["completedNewRuns"])
        self.assertEqual([84, 92], result["retainedNativeMaterialCounts"])
        self.assertTrue(result["existingStudioCompatibilityProved"])
        self.assertFalse(result["directModelStudioAcceptanceProved"])

    def test_rehashed_saved_layout_change_is_rejected(self):
        self.change("layout/open-export.json", lambda value: value["layout"][0].update(x=9000))
        with self.assertRaisesRegex(ValueError, "changed its authoring"): self.verify()

    def test_rehashed_native_binding_change_is_rejected(self):
        self.change("original/edited-export.json", lambda value: value["state"]["rootActivity"].update(activityVersionId="foreign-binding"))
        with self.assertRaisesRegex(ValueError, "more than"): self.verify()

    def test_rehashed_xml_semantic_change_is_rejected(self):
        path = self.root / "layout/edited-export.bpmn"
        self.assertIn('default="default-flow"', path.read_text())
        path.write_text(path.read_text().replace('default="default-flow"', 'default="other-flow"'))
        self.rehash("layout/edited-export.bpmn")
        with self.assertRaisesRegex(ValueError, "XML edit changed"): self.verify()

    def test_rehashed_test_run_cannot_be_called_a_published_run(self):
        self.change("layout/test-run-api.json", lambda value: value["instance"].update(runKind="PublishedRun"))
        with self.assertRaisesRegex(ValueError, "truthful completed kind"): self.verify()

    def test_rehashed_original_run_projection_change_is_rejected(self):
        self.change("original/restarted-legacy-views.json", lambda value: value["14iZvo3Gbql"]["instance"].update(artifactVersion="foreign-version"))
        with self.assertRaisesRegex(ValueError, "original Studio run changed"): self.verify()

    def test_rehashed_physical_full_pin_change_is_rejected(self):
        path = self.root / "original/finished-runtime.sqlite.gz"; raw = self.root / "changed.sqlite"
        raw.write_bytes(gzip.decompress(path.read_bytes()))
        with closing(sqlite3.connect(raw)) as connection:
            key = studio.encoded("14iZvo3Gbql")
            content = json.loads(connection.execute("SELECT ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId = ?", (key,)).fetchone()[0])
            content["pinnedExecutable"]["artifactVersion"] = studio.encoded("foreign-version")
            connection.execute("UPDATE elsa_runtime_workflow_execution_state SET ContentJson = ? WHERE WorkflowExecutionId = ?", (json.dumps(content), key))
            connection.commit()
        path.write_bytes(gzip.compress(raw.read_bytes()))
        def changed(process):
            snapshot = process["stages"]["finished"]["runtime"]
            snapshot.update(databaseSha256=hashlib.sha256(raw.read_bytes()).hexdigest(), gzipSha256=studio.q.digest(path))
            if "observer" in snapshot: snapshot["observer"]["snapshotSha256"] = snapshot["databaseSha256"]
        self.rehash("original/finished-runtime.sqlite.gz"); self.change("original/process.json", changed)
        with self.assertRaisesRegex(ValueError, "physical Studio run lost"): self.verify()

    def test_rehashed_model_reclassification_of_native_material_is_rejected(self):
        path = self.root / "layout/finished-runtime.sqlite.gz"; raw = self.root / "changed.sqlite"
        raw.write_bytes(gzip.decompress(path.read_bytes()))
        with studio.original_store("multi-instance-and-studio/runtime.sqlite.gz") as original:
            original_id = next(iter(studio.materials(original)))
        with closing(sqlite3.connect(raw)) as connection:
            connection.execute("UPDATE elsa_runtime_workflow_executable SET IsModelMaterial = 1 WHERE Id = ?", (original_id,))
            connection.commit()
        path.write_bytes(gzip.compress(raw.read_bytes()))
        self.rehash("layout/finished-runtime.sqlite.gz")
        def changed(process):
            snapshot = process["stages"]["finished"]["runtime"]
            snapshot.update(databaseSha256=hashlib.sha256(raw.read_bytes()).hexdigest(), gzipSha256=studio.q.digest(path))
            if "observer" in snapshot: snapshot["observer"]["snapshotSha256"] = snapshot["databaseSha256"]
        self.change("layout/process.json", changed)
        with self.assertRaisesRegex(ValueError, "original material"): self.verify()

    def test_an_existing_but_undeclared_screenshot_is_rejected(self):
        path = self.root / "manifest.json"; value = json.loads(path.read_text())
        value["assets"] = [asset for asset in value["assets"] if asset["path"] != "layout/published-run.jpg"]
        studio.q.write_json(path, value)
        with self.assertRaisesRegex(ValueError, "required assets"): self.verify()

    def test_compressed_process_receipt_requires_its_original_decoded_hash(self):
        path = self.root / "manifest.json"; value = json.loads(path.read_text())
        next(asset for asset in value["assets"] if asset["path"] == "layout/process.json.gz")["decodedSha256"] = "0" * 64
        studio.q.write_json(path, value)
        with self.assertRaisesRegex(ValueError, "decoded Studio process"): self.verify()


class FinalActualStudioUpgradeControls(ActualStudioUpgradeControls):
    evidence = EVIDENCE.parent / "final-compatibility/studio"

    def test_rehashed_provider_snapshot_without_readonly_receipt_is_rejected(self):
        self.change("original/process.json", lambda process: process["stages"]["recovered"]["runtime"]["observer"].update(readOnly=False))
        with self.assertRaisesRegex(ValueError, "read-only provider receipt"): self.verify()

    def test_rehashed_provider_snapshot_with_another_hash_is_rejected(self):
        self.change("layout/process.json", lambda process: process["stages"]["finished"]["design"]["observer"].update(snapshotSha256="0" * 64))
        with self.assertRaisesRegex(ValueError, "read-only provider receipt"): self.verify()

    def test_rehashed_provider_snapshot_without_coupled_cut_is_rejected(self):
        self.change("layout/process.json", lambda process: process["stages"]["restarted"]["runtime"].update(captureCut="unpaused"))
        with self.assertRaisesRegex(ValueError, "read-only provider receipt"): self.verify()


if __name__ == "__main__": unittest.main()
