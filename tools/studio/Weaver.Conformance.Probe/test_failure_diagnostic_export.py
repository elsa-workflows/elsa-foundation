"""Failure diagnostics remain bounded, private, and independent of mandatory full export."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from export_evidence import export_evidence
from export_failure_diagnostic import export_failure_diagnostic


class FailureDiagnosticExportTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.source = self.root / "run"
        self.source.mkdir()
        self.canary = "Bearer private-value-must-not-export"
        self.jwt = "eyJ" + "a" * 12 + ".eyJ" + "b" * 12 + "." + "c" * 12
        self.report = {"status": "failed", "source": "a" * 40, "stage": "browser-sdk-incompatible",
                       "cases": [{"id": "observed"} for _ in range(32)],
                       "browserFailureDiagnostic": {"kind": "AssertionError", "frames": [
                           {"file": "browser.mjs", "line": 111, "column": 2}]}}

    def write_report(self):
        data = json.dumps(self.report).encode()
        (self.source / "report.json").write_bytes(data)
        return data

    def test_projects_only_safe_failed_fields_and_never_qualifies(self):
        self.report.update(password=self.canary, failure={"message": self.jwt}, path="/private/secret")
        self.report["cases"][0]["rawBody"] = self.canary
        diagnostic = self.report["browserFailureDiagnostic"]
        diagnostic.update(password=self.canary, message=self.jwt)
        diagnostic["frames"] += [diagnostic["frames"][0],
                                 {"file": "/private/browser.mjs", "line": 1, "column": 2},
                                 {"file": "browser.mjs", "line": True, "column": 2},
                                 {"file": "browser.mjs", "line": self.canary, "column": 2}]
        diagnostic["frames"] += [{"file": "browser.mjs", "line": 200 + i, "column": 1} for i in range(20)]
        original = self.write_report()
        destination = self.root / "diagnostic"
        index = export_failure_diagnostic(self.source, destination)
        exported = json.loads((destination / "report.json").read_text())
        self.assertEqual({"status", "diagnosticOnly", "qualificationComplete", "independentPackageWitness", "source",
                          "stage", "originalCaseCount", "expectedCaseCount", "browserFailureDiagnostic"}, set(exported))
        self.assertEqual("failed", exported["status"])
        self.assertTrue(exported["diagnosticOnly"])
        self.assertFalse(exported["qualificationComplete"])
        self.assertFalse(exported["independentPackageWitness"])
        self.assertEqual("a" * 40, exported["source"])
        self.assertEqual("browser-sdk-incompatible", exported["stage"])
        self.assertEqual(32, exported["originalCaseCount"])
        self.assertEqual(107, exported["expectedCaseCount"])
        frames = exported["browserFailureDiagnostic"]["frames"]
        self.assertLessEqual(len(frames), 8)
        self.assertEqual(len(frames), len({(f["line"], f["column"]) for f in frames}))
        self.assertEqual({"file": "browser.mjs", "line": 111, "column": 2}, frames[0])
        self.assertEqual("failed", index["qualificationStatus"])
        self.assertFalse(index["witnessCompleteness"]["complete"])
        self.assertEqual({"report.json"}, set(index["files"]))
        for path in destination.iterdir():
            data = path.read_text()
            self.assertNotIn(self.canary, data)
            self.assertNotIn(self.jwt, data)
            self.assertNotIn("/private/secret", data)
        self.assertEqual(original, (self.source / "report.json").read_bytes())

    def test_early_missing_primary_and_unknown_stage_stay_unavailable(self):
        self.report.pop("browserFailureDiagnostic")
        self.report.pop("cases")
        self.report["stage"] = self.canary
        self.write_report()
        destination = self.root / "diagnostic"
        export_failure_diagnostic(self.source, destination)
        exported = json.loads((destination / "report.json").read_text())
        self.assertEqual({"kind": "Unavailable", "frames": []}, exported["browserFailureDiagnostic"])
        self.assertEqual("Unavailable", exported["stage"])
        self.assertIsNone(exported["originalCaseCount"])
        self.assertEqual("failed", exported["status"])
        self.assertNotIn(self.canary, (destination / "report.json").read_text())

    def test_preserves_only_the_two_exact_manifest_run_browser_stages(self):
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                self.report["stage"] = provider + "-workflow-run-browser"
                self.write_report()
                destination = self.root / provider
                export_failure_diagnostic(self.source, destination)
                selected = json.loads((destination / "report.json").read_text())
                self.assertEqual(self.report["stage"], selected["stage"])
                self.assertFalse(selected["independentPackageWitness"])
        for i, stage in enumerate(("other-workflow-run-browser", "copilot-workflow-run-browser-private", self.jwt)):
            with self.subTest(stage=stage):
                self.report["stage"] = stage
                self.write_report()
                destination = self.root / f"unknown-{i}"
                export_failure_diagnostic(self.source, destination)
                selected = json.loads((destination / "report.json").read_text())
                self.assertEqual("Unavailable", selected["stage"])

    def test_full_export_still_refuses_bad_references_while_safe_diagnostic_survives(self):
        outside = self.root / "foreign.txt"
        outside.write_text("outside owning run")
        symlink = self.source / "symlink.txt"
        symlink.symlink_to(outside)
        changed = self.source / "changed.txt"
        changed.write_text("changed bytes")
        private = self.source / "config-private.json"
        private.write_text("{}")
        jwt = self.source / "capture.txt"
        jwt.write_text(self.jwt)
        credential = self.source / "capture.body"
        credential.write_text(json.dumps({"response": json.dumps({"accessToken": self.canary})}))
        refs = [(self.source / "missing.txt", "0" * 64, "escaped"),
                (outside, hashlib.sha256(outside.read_bytes()).hexdigest(), "escaped"),
                (Path("relative.txt"), "0" * 64, "escaped"),
                (symlink, hashlib.sha256(outside.read_bytes()).hexdigest(), "escaped"),
                (changed, "0" * 64, "changed before export"),
                (private, hashlib.sha256(private.read_bytes()).hexdigest(), "Private runtime"),
                (jwt, hashlib.sha256(jwt.read_bytes()).hexdigest(), "Credential material"),
                (credential, hashlib.sha256(credential.read_bytes()).hexdigest(), "Credential material")]
        for i, (path, digest, cause) in enumerate(refs):
            with self.subTest(path=path.name, cause=cause):
                self.report["evidence"] = [{"path": str(path), "sha256": digest}]
                original = self.write_report()
                diagnostic = self.root / f"diagnostic-{i}"
                export_failure_diagnostic(self.source, diagnostic)
                full = self.root / f"full-{i}"
                with self.assertRaisesRegex(ValueError, cause):
                    export_evidence(self.source, full)
                self.assertFalse(full.exists())
                self.assertEqual({"report.json", "export-index.json"}, {p.name for p in diagnostic.iterdir()})
                self.assertEqual("failed", json.loads((diagnostic / "report.json").read_text())["status"])
                self.assertEqual(original, (self.source / "report.json").read_bytes())

    def test_refuses_nonfailed_or_unbound_source_without_artifact(self):
        for i, (status, commit) in enumerate((("passed", "a" * 40), ("running", "a" * 40),
                                            ("failed", self.jwt), ("failed", None))):
            with self.subTest(status=status, commit=commit):
                self.report.update(status=status, source=commit)
                self.write_report()
                destination = self.root / f"diagnostic-{i}"
                with self.assertRaises(ValueError):
                    export_failure_diagnostic(self.source, destination)
                self.assertFalse(destination.exists())

    def test_rejects_relative_nested_and_existing_destinations(self):
        self.write_report()
        existing = self.root / "existing"
        existing.mkdir()
        for destination in (Path("relative"), self.source / "nested", existing):
            with self.subTest(destination=destination), self.assertRaisesRegex(ValueError, "new absolute"):
                export_failure_diagnostic(self.source, destination)
        self.assertEqual([], list(existing.iterdir()))
        self.assertFalse((self.source / "nested").exists())

    def test_diagnostic_cli_succeeds_independently_of_original_full_refusal(self):
        self.report["evidence"] = [{"path": str(self.source / "missing.txt"), "sha256": "0" * 64}]
        original = self.write_report()
        tool = Path(__file__).parent
        full = subprocess.run([sys.executable, str(tool / "export_evidence.py"), "--source", str(self.source),
                               "--destination", str(self.root / "full")], capture_output=True, text=True)
        self.assertEqual(1, full.returncode)
        self.assertIn("Evidence reference escaped its owning run", full.stderr)
        diagnostic = subprocess.run([sys.executable, str(tool / "export_failure_diagnostic.py"), "--source", str(self.source),
                                     "--destination", str(self.root / "diagnostic")], capture_output=True, text=True)
        self.assertEqual(0, diagnostic.returncode, diagnostic.stderr)
        self.assertEqual({"status": "failure-diagnostic-exported", "qualificationStatus": "failed",
                          "independentPackageWitness": False, "files": 1}, json.loads(diagnostic.stdout))
        self.assertFalse((self.root / "full").exists())
        self.assertEqual(original, (self.source / "report.json").read_bytes())


if __name__ == "__main__":
    unittest.main()
