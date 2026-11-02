"""Mutation tests use synthetic retained files, never native-journey qualification claims."""
import copy
import hashlib
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
import zipfile

from autonomy_journeys import MATRIX_QUALIFICATION, MODE_QUALIFICATION, autonomy_stages
from disclosure_journeys import EXPECTED_CASES as AUTHORITY_CASES
from evidence import (CONTROL_IDS, REQUIRED_CASES, UNCHANGED_CONTROLS, EvidenceError, validate_complete_witness, validate_report,
                      witness_gaps, witness_manifest)
from test_autonomy_journeys import complete_autonomy_section


class QualificationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.sequence = 0
        self.source, self.version = "a" * 40, "4.0.0-witness"
        manifest = json.dumps({"name": "@elsa-workflows/studio-sdk", "version": self.version}).encode()
        sdk_path = self.root / "sdk.tgz"
        with tarfile.open(sdk_path, "w:gz") as archive:
            entry = tarfile.TarInfo("package/package.json")
            entry.size = len(manifest)
            archive.addfile(entry, io.BytesIO(manifest))
        package_path = self.root / "product.nupkg"
        with zipfile.ZipFile(package_path, "w") as archive:
            archive.writestr("product.nuspec", f"<package><metadata><id>Elsa.Test</id><version>{self.version}</version></metadata></package>")
        package = dict(self.ref(package_path), id="Elsa.Test", version=self.version, dependencies=[])
        sdk = self.ref(sdk_path)
        self.report = {"status": "verifying", "source": self.source, "version": self.version,
                       "evidenceCategory": "deterministic-provider-protocol", "liveProvider": False,
                       "producer": {"source": self.source, "sdk": sdk}, "packages": {"Elsa.Test": package},
                       "consumers": {}, "authors": {}, "cases": []}
        for kind in ("backend", "studio"):
            record = self.closure()
            published = self.root / (kind + "-published")
            published.mkdir()
            baseline = {}
            for name in ("common.dll", "another.dll"):
                path = published / name; path.write_bytes(b"unchanged compiled bytes")
                baseline[name] = self.ref(path)
            record.update(frontendInvocations=0, frontendAudit=self.write(b""),
                          publishedDirectory=str(published), baseline=baseline, after=copy.deepcopy(baseline))
            self.report["consumers"][kind] = record
        self.report["authors"]["backend"] = self.closure()
        audit = {"sdkSha256": sdk["sha256"], "sdkTgz": sdk["path"], "sourceReferences": [], "workspaceImports": [],
                 "installedSdk": self.write(manifest)}
        self.report["authors"]["studio"] = {"source": self.source, "status": "passed", "sdkSha256": sdk["sha256"], "graphAudit": self.write(audit)}
        self.controls = {name: {"stage": "execution", "diagnostic": "fixture.expected." + name} for name in CONTROL_IDS}
        for name, observations in REQUIRED_CASES.items():
            status = "failed" if name in CONTROL_IDS else "passed"
            before = self.write({"owningState": "before"})
            after = before if name in UNCHANGED_CONTROLS else self.write({"owningState": "after"})
            receipt = {"id": name, "source": self.source, "status": status,
                       "observations": dict.fromkeys(observations, True), "ownerBefore": before, "ownerAfter": after,
                       "evidence": [self.write({"syntheticSelfTestObservation": name})]}
            case = {"id": name, "source": self.source, "status": status}
            if name in CONTROL_IDS:
                receipt.update(self.controls[name], expectedFailureObserved=True)
                case["expectedFailureObserved"] = True
            case["receipt"] = self.write(receipt)
            self.report["cases"].append(case)

    def tearDown(self):
        self.temporary.cleanup()

    def ref(self, path):
        return {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}

    def write(self, value):
        self.sequence += 1
        path = self.root / (str(self.sequence) + ".retained")
        path.write_bytes(value if isinstance(value, bytes) else json.dumps(value).encode())
        return self.ref(path)

    def closure(self):
        return {"source": self.source, "status": "passed", "sourceReferences": [],
                "assets": self.write({"libraries": {"Elsa.Test/" + self.version: {"type": "package"}}}),
                "lock": self.write({"version": 1})}

    def change_receipt(self, report, name, change):
        case = next(case for case in report["cases"] if case["id"] == name)
        receipt = json.loads(Path(case["receipt"]["path"]).read_text())
        change(receipt)
        case["receipt"] = self.write(receipt)

    def rejected(self, change):
        report = copy.deepcopy(self.report)
        change(report)
        with self.assertRaises(EvidenceError):
            validate_report(report, self.root, self.controls)

    def test_complete_synthetic_receipts_validate_without_status_or_file_mutation(self):
        before, files = copy.deepcopy(self.report), set(self.root.iterdir())
        self.assertIsNone(validate_report(self.report, self.root, self.controls))
        self.assertEqual(before, self.report)
        self.assertEqual(files, set(self.root.iterdir()))

    def test_empty_missing_duplicate_and_skipped_cases_are_rejected(self):
        for change in (lambda r: r.update(cases=[]), lambda r: r["cases"].pop(),
                       lambda r: r["cases"].append(r["cases"][0]), lambda r: r["cases"][0].update(status="skipped")):
            with self.subTest(change=change): self.rejected(change)

    def test_source_package_and_retained_hash_mismatches_are_rejected(self):
        for change in (lambda r: r.update(source="not-a-commit"), lambda r: r["producer"].update(source="b" * 40),
                       lambda r: r["cases"][0].update(source="b" * 40),
                       lambda r: r["packages"]["Elsa.Test"].update(version="99.0.0"),
                       lambda r: r["packages"]["Elsa.Test"].update(dependencies=[{"id": "invented"}]),
                       lambda r: r["producer"]["sdk"].update(sha256="0" * 64)):
            with self.subTest(change=change): self.rejected(change)

    def test_missing_files_and_source_linked_closure_are_rejected(self):
        for change in (lambda r: r["consumers"]["backend"]["lock"].update(path=str(self.root / "missing")),
                       lambda r: r["consumers"]["backend"].update(sourceReferences=["product.csproj"]),
                       lambda r: r["consumers"]["backend"].update(assets=self.write({"libraries": {"Elsa.Test/" + self.version: {"type": "project"}}})),
                       lambda r: r["consumers"]["backend"].update(assets=self.write({"libraries": {"Elsa.Test/99": {"type": "package"}}}))):
            with self.subTest(change=change): self.rejected(change)

    def test_frontend_invocations_and_changed_common_payload_are_rejected(self):
        for change in (lambda r: r["consumers"]["studio"].update(frontendInvocations=1),
                       lambda r: r["consumers"]["studio"].update(frontendAudit=self.write(b"node invoked\n")),
                       lambda r: r["consumers"]["backend"]["after"].update({"common.dll": self.write(b"changed compiled bytes")}),
                       lambda r: r["consumers"]["backend"].update(after={})):
            with self.subTest(change=change): self.rejected(change)
        def omit_from_both(report):
            for field in ("baseline", "after"): report["consumers"]["backend"][field].pop("another.dll")
        self.rejected(omit_from_both)

    def test_wrong_cause_and_relabelled_control_failures_are_rejected(self):
        name = "sdk-incompatible"
        for change in (lambda r: self.change_receipt(r, name, lambda x: x.update(stage="restore", diagnostic="network-timeout")),
                       lambda r: self.change_receipt(r, name, lambda x: x.update(diagnostic="unrelated-error")),
                       lambda r: self.change_receipt(r, name, lambda x: x.update(expectedFailureObserved=False)),
                       lambda r: next(x for x in r["cases"] if x["id"] == name).update(status="passed")):
            with self.subTest(change=change): self.rejected(change)
        with self.assertRaises(EvidenceError): validate_report(self.report, self.root, {})

    def test_owner_markers_without_retained_observations_do_not_qualify(self):
        for change in (lambda x: x.update(evidence=[]), lambda x: x.update(observations={}),
                       lambda x: x.pop("ownerAfter"), lambda x: x.update(status="pending")):
            with self.subTest(change=change):
                self.rejected(lambda r: self.change_receipt(r, "resource-copilot", change))
        self.rejected(lambda r: self.change_receipt(r, "read-denied", lambda x: x.update(ownerAfter=self.write({"changed": True}))))

    def test_sdk_author_graph_and_installed_manifest_must_match_retained_tgz(self):
        for change in (lambda r: r.pop("authors"), lambda r: r["authors"]["studio"].update(sdkSha256="0" * 64),
                       lambda r: r["authors"]["studio"].update(graphAudit=self.write({"sdkSha256": "0" * 64}))):
            with self.subTest(change=change): self.rejected(change)
        audit = json.loads(Path(self.report["authors"]["studio"]["graphAudit"]["path"]).read_text())
        for change in (lambda x: x.update(workspaceImports=["workspace:*"]), lambda x: x.update(installedSdk=self.write(b"wrong SDK manifest"))):
            bad = copy.deepcopy(audit); change(bad)
            self.rejected(lambda r: r["authors"]["studio"].update(graphAudit=self.write(bad)))


def complete_witness_report(base):
    """Extend the synthetic original38 report with the four current-authority and 26 autonomy records."""
    report = copy.deepcopy(base.report)
    report["witnessManifest"] = witness_manifest()
    names = sorted(AUTHORITY_CASES)
    report["contextDisclosureQualification"] = {"status": "passed", "evidenceCategory": "deterministic-provider-protocol", "liveProvider": False,
        "source": base.source, "caseIDs": names, "stages": [],
        "records": [{"id": name, "status": "passed", "record": base.write({"id": name, "status": "passed"})} for name in names]}
    report[MATRIX_QUALIFICATION] = complete_autonomy_section(base.write({"syntheticSelfTestObservation": "owner"}), base.source)
    report["stages"] = [{"name": "archive", "exitCode": 0}] + [{"name": name, "exitCode": 0} for name in sorted(autonomy_stages())]
    return report


class CompleteWitnessTests(unittest.TestCase):
    """The extended inventory gate; synthetic receipts only, never a native qualification claim."""
    def setUp(self):
        self.base = QualificationTests("test_complete_synthetic_receipts_validate_without_status_or_file_mutation")
        self.base.setUp()
        self.addCleanup(self.base.tearDown)
        self.report = complete_witness_report(self.base)

    def rejected(self, change):
        report = copy.deepcopy(self.report)
        change(report)
        with self.assertRaises(EvidenceError):
            validate_complete_witness(report, self.base.root, self.base.controls)

    def test_manifest_is_original38_plus_four_plus_thirteen_per_actual_adapter(self):
        manifest = witness_manifest()
        self.assertEqual((38, 4, 26, 68), (len(manifest["originalCases"]), len(manifest["currentAuthorityCases"]),
                                           len(manifest["autonomyCases"]), manifest["expectedCaseCount"]))
        self.assertTrue(all(name.split("-", 1)[0] in ("copilot", "anthropic") for name in manifest["autonomyCases"]))
        self.assertEqual(13, len({name.split("-", 1)[1] for name in manifest["autonomyCases"]}))

    def test_complete_inventory_validates_without_mutation(self):
        before = copy.deepcopy(self.report)
        self.assertEqual([], witness_gaps(self.report))
        self.assertIsNone(validate_complete_witness(self.report, self.base.root, self.base.controls))
        self.assertEqual(before, self.report)

    def test_no_total_stage_count_is_required(self):
        manifest_stages = [stage for stage in self.report["stages"] if stage["name"] in autonomy_stages()]
        for label, stages in (("matrix-only", manifest_stages),
                              ("legacy-135-plus-matrix", [{"name": "stage-" + str(i), "exitCode": 0} for i in range(135)] + manifest_stages),
                              ("other-production-shape", [{"name": "stage-" + str(i), "exitCode": 0} for i in range(7)] + manifest_stages)):
            with self.subTest(label=label):
                report = copy.deepcopy(self.report); report["stages"] = stages
                validate_complete_witness(report, self.base.root, self.base.controls)
        legacy = copy.deepcopy(self.report)
        legacy["stages"] = [{"name": "stage-" + str(i), "exitCode": 0} for i in range(135)]
        with self.assertRaises(EvidenceError): validate_complete_witness(legacy, self.base.root, self.base.controls)

    def test_legacy_original_witness_without_the_inventory_is_incomplete(self):
        legacy = copy.deepcopy(self.report)
        legacy.pop(MATRIX_QUALIFICATION); legacy.pop("witnessManifest")
        legacy["stages"] = [{"name": "stage-" + str(i), "exitCode": 0} for i in range(135)]
        self.assertIsNone(validate_report(legacy, self.base.root, self.base.controls))
        self.assertTrue(witness_gaps(legacy))
        with self.assertRaises(EvidenceError): validate_complete_witness(legacy, self.base.root, self.base.controls)

    def test_missing_duplicate_wrong_adapter_wrong_cause_or_missing_evidence_fails(self):
        def autonomy(report, key="denied-result-continuation", provider="anthropic"):
            return next(row for row in report[MATRIX_QUALIFICATION]["cases"] if row["id"] == provider + "-" + key)
        changes = {
            "missing": lambda r: r[MATRIX_QUALIFICATION]["cases"].remove(autonomy(r)),
            "duplicate": lambda r: r[MATRIX_QUALIFICATION]["cases"].append(copy.deepcopy(autonomy(r))),
            "replaced-by-other-adapter": lambda r: r[MATRIX_QUALIFICATION]["cases"].__setitem__(
                r[MATRIX_QUALIFICATION]["cases"].index(autonomy(r)), copy.deepcopy(autonomy(r, provider="copilot"))),
            "wrong-adapter": lambda r: autonomy(r).update(adapter="copilot"),
            "wrong-cause": lambda r: autonomy(r)["outcome"].update(execute="agent.proposal.forbidden"),
            "wrong-mode-cause": lambda r: autonomy(r, "automatic-to-manual").update(outcomeAtDecision="automatic"),
            "failed-case": lambda r: autonomy(r).update(status="failed", cause="lifecycle.refusal_cause"),
            "no-evidence": lambda r: autonomy(r).update(evidence=[]),
            "changed-evidence": lambda r: autonomy(r).update(evidence=[{**autonomy(r)["evidence"][0], "sha256": "0" * 64}]),
            "foreign-owner": lambda r: autonomy(r).update(ownerAfter={"path": "/elsewhere/owner.json", "sha256": "0" * 64}),
            "no-correlation": lambda r: autonomy(r)["correlation"].pop("proposalId"),
            "section-absent": lambda r: r.pop(MATRIX_QUALIFICATION),
            "section-failed": lambda r: r[MATRIX_QUALIFICATION].update(status="failed"),
            "section-foreign-source": lambda r: r[MATRIX_QUALIFICATION].update(source="b" * 40),
            "diagnostic-mode-section": lambda r: r.update({MODE_QUALIFICATION: {"status": "passed"}}),
        }
        for name, change in changes.items():
            with self.subTest(change=name): self.rejected(change)

    def test_current_authority_original_manifest_and_stage_gaps_fail(self):
        disclosure = "contextDisclosureQualification"
        changes = {
            "authority-missing": lambda r: (r[disclosure]["caseIDs"].pop(), r[disclosure]["records"].pop()),
            "authority-duplicate": lambda r: (r[disclosure]["caseIDs"].append(r[disclosure]["caseIDs"][0]), r[disclosure]["records"].append(r[disclosure]["records"][0])),
            "authority-failed": lambda r: r[disclosure].update(status="failed"),
            "authority-record-failed": lambda r: r[disclosure]["records"][0].update(record=self.base.write({"id": r[disclosure]["records"][0]["id"], "status": "failed"})),
            "authority-record-changed": lambda r: r[disclosure]["records"][0]["record"].update(sha256="0" * 64),
            "original-missing": lambda r: r["cases"].pop(),
            "manifest-absent": lambda r: r.pop("witnessManifest"),
            "manifest-shrunk": lambda r: r["witnessManifest"].update(autonomyCases=r["witnessManifest"]["autonomyCases"][:8]),
            "matrix-stage-missing": lambda r: r["stages"].pop(),
            "matrix-stage-repeated": lambda r: r["stages"].append(copy.deepcopy(r["stages"][-1])),
            "failed-stage": lambda r: r["stages"][0].update(exitCode=1),
        }
        for name, change in changes.items():
            with self.subTest(change=name): self.rejected(change)


if __name__ == "__main__":
    unittest.main()
