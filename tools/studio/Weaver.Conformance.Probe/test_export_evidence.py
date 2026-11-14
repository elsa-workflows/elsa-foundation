import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from autonomy_journeys import MATRIX_QUALIFICATION
from restart_journeys import RESTART_QUALIFICATION
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

    def protocol(self, name="first", *, earlier=(), observed=None, later=()):
        observed = observed or [{"kind": "copilot.request", "data": {"commandId": name}}]
        encode = lambda rows: b"".join((json.dumps(row) + "\n").encode() for row in rows)
        prefix, window, tail = encode(earlier), encode(observed), encode(later)
        path = self.root / "protocol.jsonl"
        path.write_bytes(prefix + window + tail)
        captured = self.root / (name + "-window.jsonl")
        captured.write_bytes(window)
        raw = {"sourcePath": str(path), "offset": len(prefix), "nextOffset": len(prefix + window),
               "incompleteTailBytes": 0, "observedBytesSha256": hashlib.sha256(window).hexdigest(), "records": observed}
        identity = path.stat()
        trace = dict(raw, commandId=name, rawTrace=self.ref(name + "-raw.json", raw),
                     window={"path": str(captured), "sha256": hashlib.sha256(window).hexdigest()},
                     sourceIdentity={"device": identity.st_dev, "inode": identity.st_ino},
                     sourceSizeAtObservation=raw["nextOffset"], capturedAt=1.0)
        return trace, self.ref(name + "-trace.json", trace), prefix + window

    def test_exports_actual_protocol_prefix_window_and_receipts_without_rewriting_native_identity(self):
        trace, reference, expected = self.protocol(earlier=[{"kind": "factory.loaded"}], later=[{"kind": "later.append"}])
        self.report({"status": "failed", "trace": reference})
        index = export_evidence(self.root, self.destination)
        self.assertEqual({"report.json", "first-trace.json", "first-raw.json", "first-window.jsonl", "protocol.jsonl"}, set(index["files"]))
        self.assertEqual(expected, (self.destination / "protocol.jsonl").read_bytes())
        self.assertEqual(trace, json.loads((self.destination / "first-trace.json").read_text()))
        self.assertEqual("failed", index["qualificationStatus"])
        self.assertFalse(index["privateRuntimeStateIncluded"])
        self.assertEqual(hashlib.sha256(expected).hexdigest(), index["files"]["protocol.jsonl"])

    def test_exports_longest_observed_prefix_of_a_shared_actual_source(self):
        first = {"kind": "copilot.request", "data": {"commandId": "first"}}
        second = {"kind": "copilot.request", "data": {"commandId": "second"}}
        _, one, _ = self.protocol(observed=[first])
        _, two, expected = self.protocol("second", earlier=[first], observed=[second], later=[{"kind": "later.append"}])
        self.report({"traces": [one, two]})
        index = export_evidence(self.root, self.destination)
        self.assertEqual(expected, (self.destination / "protocol.jsonl").read_bytes())
        self.assertIn("first-window.jsonl", index["files"])
        self.assertIn("second-window.jsonl", index["files"])

    def test_refuses_changed_or_substituted_actual_protocol_evidence(self):
        for fault in ("source-bytes", "source-truncated", "source-replaced", "source-symlink", "source-parent-symlink",
                      "window-bytes", "raw-receipt-bytes", "raw-records", "command-records", "identity", "foreign-source", "missing-window"):
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as directory:
                original_root, original_destination = self.root, self.destination
                self.root, self.destination = Path(directory) / "run", Path(directory) / "export"
                self.root.mkdir()
                try:
                    trace, _, _ = self.protocol()
                    path = self.root / "protocol.jsonl"
                    if fault == "source-bytes": path.write_bytes(path.read_bytes().replace(b"copilot.request", b"copilot.changed"))
                    elif fault == "source-truncated": path.write_bytes(b"")
                    elif fault == "source-replaced":
                        replacement = self.root / "replacement.jsonl"
                        replacement.write_bytes(path.read_bytes()); replacement.replace(path)
                    elif fault == "source-symlink":
                        original = self.root / "original.jsonl"
                        path.rename(original); path.symlink_to(original)
                    elif fault == "source-parent-symlink":
                        alias = self.root / "alias"
                        alias.symlink_to(self.root, target_is_directory=True)
                        trace["sourcePath"] = str(alias / path.name)
                        raw = json.loads(Path(trace["rawTrace"]["path"]).read_text())
                        raw["sourcePath"] = trace["sourcePath"]
                        trace["rawTrace"] = self.ref("first-raw.json", raw)
                    elif fault == "window-bytes": Path(trace["window"]["path"]).write_bytes(b"changed")
                    elif fault == "raw-receipt-bytes": Path(trace["rawTrace"]["path"]).write_bytes(b"{}")
                    elif fault == "raw-records":
                        raw = json.loads(Path(trace["rawTrace"]["path"]).read_text())
                        raw["records"] = []
                        trace["rawTrace"] = self.ref("first-raw.json", raw)
                    elif fault == "command-records": trace["records"] = []
                    elif fault == "identity": trace["sourceIdentity"]["inode"] += 1
                    elif fault == "foreign-source":
                        foreign = self.root.parent / "foreign.jsonl"
                        foreign.write_bytes(path.read_bytes())
                        trace["sourcePath"] = str(foreign)
                        raw = json.loads(Path(trace["rawTrace"]["path"]).read_text())
                        raw["sourcePath"] = str(foreign)
                        trace["rawTrace"] = self.ref("first-raw.json", raw)
                    elif fault == "missing-window": trace.pop("window")
                    reference = self.ref("first-trace.json", trace)
                    self.report({"trace": reference})
                    with self.assertRaises(ValueError):
                        export_evidence(self.root, self.destination)
                    self.assertFalse(self.destination.exists())
                finally:
                    self.root, self.destination = original_root, original_destination

    def test_refuses_credentials_in_source_prefix_or_captured_jsonl_before_writing_any_export(self):
        for channel in ("prefix", "window", "nested-body", "jwt"):
            with self.subTest(channel=channel), tempfile.TemporaryDirectory() as directory:
                original_root, original_destination = self.root, self.destination
                self.root, self.destination = Path(directory) / "run", Path(directory) / "export"
                self.root.mkdir()
                try:
                    private = {"kind": "factory.observation", "data": {"accessToken": "generated-own-credential"}}
                    if channel == "nested-body": private["data"] = {"rawBody": json.dumps({"password": "generated-own-credential"})}
                    elif channel == "jwt": private["data"] = {"text": "eyJabcdefghijk.eyJabcdefghijk.abcdefghijk"}
                    trace, reference, _ = self.protocol(earlier=[private] if channel == "prefix" else (),
                                                        observed=None if channel == "prefix" else [dict(private, data=dict(private["data"], commandId="first"))])
                    self.report({"trace": reference})
                    with self.assertRaisesRegex(ValueError, "Credential"):
                        export_evidence(self.root, self.destination)
                    self.assertFalse(self.destination.exists())
                finally:
                    self.root, self.destination = original_root, original_destination

    def test_refuses_credentials_in_a_direct_jsonl_reference(self):
        path = self.root / "protocol.jsonl"
        path.write_text(json.dumps({"data": {"accessToken": "generated-own-credential"}}) + "\n")
        self.report({"trace": {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}})
        with self.assertRaisesRegex(ValueError, "Credential"):
            export_evidence(self.root, self.destination)
        self.assertFalse(self.destination.exists())

    def test_a_module_payload_cannot_select_a_protocol_source(self):
        trace, _, _ = self.protocol()
        body = self.ref("module.json", {"payload": trace})
        self.report({"owner": body})
        index = export_evidence(self.root, self.destination)
        self.assertEqual({"report.json", "module.json"}, set(index["files"]))

    def test_duplicate_sensitive_keys_cannot_hide_credentials_in_protocol_or_nested_body(self):
        duplicate = '{"accessToken":"synthetic-test-only","accessToken":"[redacted]"}'
        for channel in ("source-prefix", "nested-body", "direct-reference"):
            with self.subTest(channel=channel), tempfile.TemporaryDirectory() as directory:
                original_root, original_destination = self.root, self.destination
                self.root, self.destination = Path(directory) / "run", Path(directory) / "export"
                self.root.mkdir()
                try:
                    if channel == "direct-reference":
                        path = self.root / "response.json"
                        path.write_text(duplicate)
                        reference = {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
                    elif channel == "nested-body":
                        _, reference, _ = self.protocol(observed=[{"kind": "copilot.request", "data": {"commandId": "first", "rawBody": duplicate}}])
                    else:
                        trace, _, _ = self.protocol()
                        path = self.root / "protocol.jsonl"
                        prefix = (duplicate + "\n").encode()
                        path.write_bytes(prefix + path.read_bytes())
                        raw = json.loads(Path(trace["rawTrace"]["path"]).read_text())
                        raw["offset"] += len(prefix); raw["nextOffset"] += len(prefix)
                        trace.update(offset=raw["offset"], nextOffset=raw["nextOffset"], sourceSizeAtObservation=raw["nextOffset"],
                                     rawTrace=self.ref("first-raw.json", raw))
                        reference = self.ref("first-trace.json", trace)
                    self.report({"trace": reference})
                    with self.assertRaisesRegex(ValueError, "Duplicate JSON keys"):
                        export_evidence(self.root, self.destination)
                    self.assertFalse(self.destination.exists())
                finally:
                    self.root, self.destination = original_root, original_destination

    def test_private_typed_canary_binding_prevents_export_of_an_earlier_source_prefix(self):
        canary = "a1" * 24
        _, reference, _ = self.protocol(earlier=[{"kind": "factory.observation", "data": {"message": "Unsupported diagnostic " + canary}}])
        self.report({"status": "failed", "canarySearch": {"found": 1, "sha256": hashlib.sha256(canary.encode()).hexdigest()}, "trace": reference})
        with self.assertRaisesRegex(ValueError, "Private typed canary"):
            export_evidence(self.root, self.destination)
        self.assertFalse(self.destination.exists())

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
    """Original107 export behavior in isolation; actual new gates are tested in test_effects_journeys.

    Scope only the new gates out of these synthetic legacy records, so their own
    missing-file and wrong-cause tests still exercise the intended export boundary.
    """
    def setUp(self):
        for name in ("manifest_gaps", "restart_manifest_gaps"):
            mocked = patch("effects_journeys." + name, return_value=[])
            mocked.start(); self.addCleanup(mocked.stop)
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
        self.assertEqual({"complete": True, "expectedCaseCount": report["witnessManifest"]["expectedCaseCount"], "gaps": []}, index["witnessCompleteness"])
        self.assertEqual("passed", index["qualificationStatus"]); self.assertFalse(index["qualificationStatusChanged"])
        root = self.base.root.resolve()
        for row in report[MATRIX_QUALIFICATION]["cases"]:
            for ref in [row["ownerBefore"], row["ownerAfter"], *row["evidence"]]:
                self.assertIn(Path(ref["path"]).resolve().relative_to(root).as_posix(), index["files"])
        for row in report["contextDisclosureQualification"]["records"]:
            self.assertIn(Path(row["record"]["path"]).resolve().relative_to(root).as_posix(), index["files"])
        for row in report["workflowAuthorizationQualification"]["records"]:
            self.assertIn(Path(row["record"]["path"]).resolve().relative_to(root).as_posix(), index["files"])
        for row in report["workflowTruthSeamQualification"]["records"]:
            self.assertIn(Path(row["record"]["path"]).resolve().relative_to(root).as_posix(), index["files"])
        for row in report[RESTART_QUALIFICATION]["cases"]:
            for ref in [row["ownerBefore"], row["ownerAfter"], row["correlation"]["restartReceipt"], *row["evidence"]]:
                self.assertIn(Path(ref["path"]).resolve().relative_to(root).as_posix(), index["files"])

    def test_promoted_report_without_the_complete_inventory_is_refused(self):
        def missing_case(r): r[MATRIX_QUALIFICATION]["cases"].pop()
        def duplicate_case(r): r[MATRIX_QUALIFICATION]["cases"].append(r[MATRIX_QUALIFICATION]["cases"][0])
        def wrong_cause(r): next(row for row in r[MATRIX_QUALIFICATION]["cases"] if "outcome" in row)["outcome"].update(effects=2)
        def legacy(r): r.pop(MATRIX_QUALIFICATION); r.pop("witnessManifest")
        def without_restarts(r): r.pop(RESTART_QUALIFICATION)
        for index, change in enumerate((missing_case, duplicate_case, wrong_cause, legacy, without_restarts)):
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
