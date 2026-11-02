import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from autonomy_journeys import MATRIX_QUALIFICATION
from export_evidence import export_evidence
import test_qualification  # module import: do not collect its TestCase classes twice


class ExportEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / "run"
        self.root.mkdir()
        self.destination = self.root.parent / "export"

    def ref(self, name, value):
        path = self.root / name
        path.write_text(json.dumps(value))
        return {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}

    def report(self, value):
        (self.root / "report.json").write_text(json.dumps(value))

    def test_exports_nested_actual_refs_and_preserves_failed_child(self):
        body = self.ref("owner.json", {"revision": "r1"})
        receipt = self.ref("case.json", {"status": "failed", "ownerAfter": body})
        self.report({"status": "passed", "cases": [{"status": "failed", "receipt": receipt}]})
        index = export_evidence(self.root, self.destination)
        self.assertEqual({"report.json", "case.json", "owner.json"}, set(index["files"]))
        self.assertEqual("failed", json.loads((self.destination / "case.json").read_text())["status"])

    def test_does_not_copy_unreferenced_runtime_credentials(self):
        self.ref("bearer-private.json", {"accessToken": "generated-own-credential"})
        self.report({"status": "failed"})
        index = export_evidence(self.root, self.destination)
        self.assertEqual({"report.json"}, set(index["files"]))
        self.assertEqual("failed", index["qualificationStatus"])

    def test_refuses_referenced_credentials_even_with_innocent_filename(self):
        ref = self.ref("response.json", {"accessToken": "generated-own-credential"})
        self.report({"evidence": ref})
        with self.assertRaisesRegex(ValueError, "Credential"):
            export_evidence(self.root, self.destination)
        self.assertFalse(self.destination.exists())

    def test_refuses_private_auth_file_and_changed_evidence(self):
        for name, change in (("storage-state.json", False), ("owner.json", True)):
            with self.subTest(name=name):
                ref = self.ref(name, {"value": "public"})
                self.report({"evidence": ref})
                if change:
                    (self.root / name).write_text("changed")
                with self.assertRaises(ValueError):
                    export_evidence(self.root, self.destination)
                self.assertFalse(self.destination.exists())

    def test_refuses_foreign_or_symlinked_reference(self):
        path = self.root.parent / "foreign.json"
        path.write_text("{}")
        link = self.root / "alias.json"
        link.symlink_to(path)
        for selected in (path, link):
            with self.subTest(path=selected):
                self.report({"evidence": {"path": str(selected), "sha256": hashlib.sha256(b"{}").hexdigest()}})
                with self.assertRaisesRegex(ValueError, "escaped"):
                    export_evidence(self.root, self.destination)
                self.assertFalse(self.destination.exists())

    def test_refuses_token_inside_raw_text_evidence(self):
        path = self.root / "response.body"
        path.write_text("eyJabcdefghijk.eyJabcdefghijk.abcdefghijk")
        self.report({"evidence": {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}})
        with self.assertRaisesRegex(ValueError, "Credential"):
            export_evidence(self.root, self.destination)
        self.assertFalse(self.destination.exists())

    def test_treats_module_payload_as_opaque_and_checks_nested_http_credentials(self):
        unrelated = self.ref("unrelated.json", {"message": "must not be selected"})
        payload = self.ref("owner.json", {"value": {"opaqueModuleData": unrelated}})
        self.report({"evidence": payload})
        index = export_evidence(self.root, self.destination)
        self.assertNotIn("unrelated.json", index["files"])
        other_destination = self.root.parent / "unsafe-export"
        response = self.ref("http.json", {"rawBody": json.dumps({"accessToken": "generated-own-credential"})})
        self.report({"evidence": response})
        with self.assertRaisesRegex(ValueError, "Credential"):
            export_evidence(self.root, other_destination)
        self.assertFalse(other_destination.exists())


class WitnessExportTests(unittest.TestCase):
    """Export records completeness against the current manifest and never promotes or relabels."""
    def setUp(self):
        self.base = test_qualification.QualificationTests("test_complete_synthetic_receipts_validate_without_status_or_file_mutation")
        self.base.setUp()
        self.addCleanup(self.base.tearDown)
        self.complete = test_qualification.complete_witness_report(self.base)
        self.outside = tempfile.TemporaryDirectory()
        self.addCleanup(self.outside.cleanup)

    def export(self, report, name="export"):
        (self.base.root / "report.json").write_text(json.dumps(report))
        return export_evidence(self.base.root, Path(self.outside.name) / name)

    def test_complete_promoted_witness_exports_every_autonomy_reference(self):
        report = dict(self.complete, status="passed", independentPackageWitness=True)
        index = self.export(report)
        self.assertEqual({"complete": True, "expectedCaseCount": 68, "gaps": []}, index["witnessCompleteness"])
        self.assertEqual("passed", index["qualificationStatus"]); self.assertFalse(index["qualificationStatusChanged"])
        root = self.base.root.resolve()
        for row in report[MATRIX_QUALIFICATION]["cases"]:
            for ref in [row["ownerBefore"], row["ownerAfter"], *row["evidence"]]:
                self.assertIn(Path(ref["path"]).resolve().relative_to(root).as_posix(), index["files"])
        for row in report["contextDisclosureQualification"]["records"]:
            self.assertIn(Path(row["record"]["path"]).resolve().relative_to(root).as_posix(), index["files"])

    def test_promoted_report_without_the_complete_inventory_is_refused(self):
        def missing_case(r): r[MATRIX_QUALIFICATION]["cases"].pop()
        def duplicate_case(r): r[MATRIX_QUALIFICATION]["cases"].append(r[MATRIX_QUALIFICATION]["cases"][0])
        def wrong_cause(r): next(row for row in r[MATRIX_QUALIFICATION]["cases"] if "outcome" in row)["outcome"].update(effects=2)
        def legacy(r): r.pop(MATRIX_QUALIFICATION); r.pop("witnessManifest")
        for index, change in enumerate((missing_case, duplicate_case, wrong_cause, legacy)):
            with self.subTest(change=change.__name__):
                report = json.loads(json.dumps(dict(self.complete, status="passed", independentPackageWitness=True)))
                change(report)
                with self.assertRaisesRegex(ValueError, "complete current inventory"):
                    self.export(report, "refused-" + str(index))
                self.assertFalse((Path(self.outside.name) / ("refused-" + str(index))).exists())

    def test_unpromoted_failed_or_partial_report_still_exports_with_its_gaps(self):
        report = json.loads(json.dumps(dict(self.complete, status="failed", independentPackageWitness=False)))
        report[MATRIX_QUALIFICATION]["cases"][0].update(status="failed", cause="lifecycle.refusal_cause")
        index = self.export(report)
        self.assertFalse(index["witnessCompleteness"]["complete"])
        self.assertTrue(any("lifecycle.refusal_cause" in gap for gap in index["witnessCompleteness"]["gaps"]))
        self.assertEqual("failed", index["qualificationStatus"])


if __name__ == "__main__":
    unittest.main()
