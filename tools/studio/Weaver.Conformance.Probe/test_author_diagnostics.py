"""Recorded VSTest-shaped synthetic observations; these controls do not qualify native authors."""
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import uuid
import xml.etree.ElementTree as ET

import authors
import author_diagnostics as diagnostics
from export_evidence import export_evidence
from export_failure_diagnostic import export_failure_diagnostic


class AuthorDiagnosticTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / "owned"
        self.root.mkdir()
        self.source = self.root / "author-backend-tests"
        self.source.mkdir()
        self.canary = "synthetic-private-" + uuid.uuid4().hex
        self.file = self.source / "DeclaredTests.cs"
        self.file.write_text('''namespace Observed.Tests;

public sealed class DeclaredTests
{
    [Theory]
    [InlineData("''' + self.canary + '''")]
    public void Fails(string value)
    {
        Assert.Equal("expected", value);
    }

    [Fact]
    public void Works() { }
}
''')
        self.stage = "authors-backend-tests-test"
        self.log = self.root / (self.stage + ".log")
        self.log.write_text(f'{self.file}(9,4): error CS0123: Bearer {self.canary}\n'
                            f'{self.source}/Tests.csproj : warning NU1603: {self.canary}\n'
                            f'Assert.Equal() Failure: expected CS9999 {self.canary}\n')
        self.trx = self.root / "author-evidence/authors-backend-tests-results/contracts.trx"
        self.trx.parent.mkdir(parents=True)
        # Three execution rows, including two InlineData rows of the same declared method.
        self.document = ET.fromstring('''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult executionId="e1" testId="t1" outcome="Failed" testName="display values never identify a method"><Output><ErrorInfo><Message/><StackTrace/></ErrorInfo></Output></UnitTestResult>
    <UnitTestResult executionId="e2" testId="t2" outcome="Failed" testName="another theory display"><Output><ErrorInfo><Message/><StackTrace/></ErrorInfo></Output></UnitTestResult>
    <UnitTestResult executionId="e3" testId="t3" outcome="Passed" testName="passed display"/>
  </Results>
  <TestDefinitions>
    <UnitTest id="t1" name="display"><TestMethod className="Observed.Tests.DeclaredTests" name="Fails" codeBase="private assembly path"/></UnitTest>
    <UnitTest id="t2" name="display"><TestMethod className="Observed.Tests.DeclaredTests" name="Fails"/></UnitTest>
    <UnitTest id="t3" name="display"><TestMethod className="Observed.Tests.DeclaredTests" name="Works"/></UnitTest>
  </TestDefinitions>
  <ResultSummary outcome="Failed"><Counters total="3" executed="3" passed="1" failed="2" error="0" timeout="0" aborted="0" notExecuted="0"/></ResultSummary>
</TestRun>''')
        for row in self.document.findall("{*}Results/{*}UnitTestResult")[:2]:
            row.set("testName", f'Fails(value: "{self.canary}")')
            row.find("{*}Output/{*}ErrorInfo/{*}Message").text = 'Assert.Equal() Failure: ' + self.canary
            row.find("{*}Output/{*}ErrorInfo/{*}StackTrace").text = (
                f'   at Observed.Tests.DeclaredTests.Fails(value: "{self.canary}") in {self.file}:line 9\n'
                f'   at Foreign.Private({self.canary}) in /elsewhere/DeclaredTests.cs:line 17')
        self.write_trx()
        self.report = {"status": "failed", "source": "a" * 40, "stage": self.stage, "cases": [],
                       "stages": [{"name": self.stage, "exitCode": 1, "log": str(self.log)}],
                       "authors": {"backend": {"status": "building"}}}
        self.run = SimpleNamespace(root=self.root, source="a" * 40, report=self.report, dotnet="owned-dotnet")
        self.run.save = lambda: (self.root / "report.json").write_text(json.dumps(self.report))

    def write_trx(self):
        ET.ElementTree(self.document).write(self.trx, encoding="utf-8", xml_declaration=True)

    def retain(self, error=None):
        diagnostics.retain_test_failure(self.run, "authors-backend-tests", self.source, error or RuntimeError(self.canary))
        return json.loads(Path(self.report["authorTestFailureDiagnostic"]["path"]).read_text())

    def test_nonzero_exit_retains_counts_and_declared_method_without_display_inline_data_messages_or_paths(self):
        value = self.retain()
        self.assertEqual({"termination": "nonzero-exit", "exitCode": 1}, value["command"])
        self.assertEqual("valid", value["trx"]["status"])
        self.assertEqual({"total": 3, "executed": 3, "passed": 1, "failed": 2, "error": 0, "timeout": 0, "aborted": 0, "notExecuted": 0}, value["trx"]["counts"])
        self.assertEqual([{"className": "Observed.Tests.DeclaredTests", "method": "Fails", "executions": 2,
                           "assertionKinds": ["Equal"], "frames": [{"file": "DeclaredTests.cs", "line": 9}]}], value["trx"]["failedMethods"])
        self.assertEqual(["CS0123"], value["compiler"]["errors"])
        self.assertEqual(["NU1603"], value["compiler"]["warnings"])
        self.assertEqual("failed", self.report["authors"]["backend"]["status"])
        self.assertFalse(value["qualificationComplete"])
        self.assertFalse(value["rawTrxExported"])
        text = json.dumps(value)
        for private in (self.canary, str(self.root), "Bearer ", "value:", "private assembly path", "/elsewhere/"):
            self.assertNotIn(private, text)

    def test_full_and_fallback_exports_select_only_new_safe_metadata_without_raw_log_or_trx_permissions(self):
        value = self.retain()
        self.run.save()
        full, fallback = self.root.parent / "full", self.root.parent / "fallback"
        full_index = export_evidence(self.root, full)
        fallback_index = export_failure_diagnostic(self.root, fallback)
        self.assertEqual({"report.json", "author-evidence/authors-backend-tests-failure.json"}, set(full_index["files"]))
        self.assertEqual({"report.json"}, set(fallback_index["files"]))
        projected = json.loads((fallback / "report.json").read_text())
        self.assertEqual(self.stage, projected["stage"])
        self.assertEqual(value, projected["authorTestFailureDiagnostic"])
        self.assertEqual(0, projected["originalCaseCount"])
        for index in (full_index, fallback_index):
            self.assertEqual("failed", index["qualificationStatus"])
            self.assertFalse(index["witnessCompleteness"]["complete"])
        for directory in (full, fallback):
            for path in directory.rglob("*"):
                if path.is_file(): self.assertNotIn(self.canary.encode(), path.read_bytes())

    def test_original_command_exception_survives_retention_write_and_save_failures(self):
        for broken in ("none", "write", "save"):
            with self.subTest(broken=broken):
                original = RuntimeError("original native failure " + self.canary)
                def command(stage, *_args):
                    self.report["stage"] = stage
                    raise original
                self.run.run = command
                target = Path if broken == "write" else self.run
                name = "open" if broken == "write" else "save"
                if broken == "none":
                    replacement = self.run.save
                elif broken == "write":
                    ordinary_open = Path.open
                    def replacement(path, *args, **kwargs):
                        if path.name == "authors-backend-tests-failure.json": raise OSError(self.canary)
                        return ordinary_open(path, *args, **kwargs)
                else:
                    def replacement(*_args): raise OSError(self.canary)
                with patch.object(authors, "_configure"), patch.object(authors, "_environment", return_value={}), \
                     patch.object(authors, "_restore", return_value={}), patch.object(authors, "_project", return_value=self.source / "Tests.csproj"), \
                     patch.object(target, name, autospec=True, side_effect=replacement):
                    with self.assertRaises(RuntimeError) as caught:
                        authors._tests(self.run, "authors-backend-tests", self.source, "module", {}, "1", 3)
                self.assertIs(original, caught.exception)
                if broken != "none":
                    self.assertEqual("unavailable", self.report["authorTestFailureDiagnostic"]["retention"])
                    unavailable = diagnostics.load_test_failure(self.root, self.report["authorTestFailureDiagnostic"], "a" * 40)
                    self.assertEqual({"termination": "nonzero-exit", "exitCode": 1}, unavailable["command"])
                    self.assertEqual("unavailable", unavailable["retention"])
                path = self.root / "author-evidence/authors-backend-tests-failure.json"
                if path.exists(): path.unlink()

    def test_missing_malformed_inconsistent_dtd_and_oversized_trx_are_explicit_and_never_guessed_assertions(self):
        original = self.trx.read_bytes()
        cases = [(None, "missing"), (b'<TestRun', "malformed"), (original.replace(b'failed="2"', b'failed="1"'), "inconsistent"),
                 (original.replace(b'executionId="e2"', b'executionId="e1"'), "inconsistent"),
                 (b'<!DOCTYPE TestRun [<!ENTITY private "' + self.canary.encode() + b'">]><TestRun/>', "malformed"),
                 (original.decode().encode("utf-16"), "unsupported-encoding"),
                 (b'x' * (diagnostics.TRX_LIMIT + 1), "over-limit")]
        for data, status in cases:
            with self.subTest(status=status):
                if data is None: self.trx.unlink()
                else: self.trx.write_bytes(data)
                value = diagnostics._trx(self.root, self.source, self.trx)
                self.assertEqual(status, value["status"])
                self.assertIsNone(value["counts"])
                self.assertEqual([], value["failedMethods"])

    def test_timeout_and_compile_failure_do_not_claim_an_assertion_when_no_trx_exists(self):
        self.trx.unlink()
        self.report["stages"] = []
        value = self.retain(subprocess.TimeoutExpired([self.canary], 10))
        self.assertEqual({"termination": "timeout", "exitCode": None}, value["command"])
        self.assertEqual("missing", value["trx"]["status"])
        self.assertEqual(["CS0123"], value["compiler"]["errors"])
        self.assertEqual([], value["trx"]["failedMethods"])

    def test_foreign_symlink_and_undeclared_identities_never_become_named_failed_tests(self):
        outside = self.root.parent / "foreign.trx"
        outside.write_bytes(self.trx.read_bytes())
        self.trx.unlink(); self.trx.symlink_to(outside)
        self.assertEqual("unsafe", diagnostics._trx(self.root, self.source, self.trx)["status"])
        self.trx.unlink(); self.write_trx()
        for method in self.document.findall("{*}TestDefinitions/{*}UnitTest/{*}TestMethod")[:2]:
            method.set("name", "InlineData_" + self.canary.replace("-", "_"))
        self.write_trx()
        value = diagnostics._trx(self.root, self.source, self.trx)
        self.assertEqual("valid", value["status"])
        self.assertEqual(2, value["unavailableFailedIdentities"])
        self.assertEqual([], value["failedMethods"])
        self.assertNotIn(self.canary.replace("-", "_"), json.dumps(value))

    def test_compiler_codes_and_input_bytes_have_explicit_bounds(self):
        self.log.write_text(''.join(f'{self.file}(1,2): error CS{1000 + i}: {self.canary}\n' for i in range(20)))
        value = diagnostics._compiler(self.root, self.log)
        self.assertEqual([f'CS{1000 + i}' for i in range(8)], value["errors"])
        self.assertTrue(value["truncated"])
        self.log.write_bytes(b'x' * (diagnostics.LOG_LIMIT + 1))
        self.assertEqual("over-limit", diagnostics._compiler(self.root, self.log)["status"])

    def test_failed_method_limit_preserves_observed_omission_count_without_display_name_fallback(self):
        (self.source / "MoreTests.cs").write_text('namespace Observed.Tests;\n\npublic sealed class MoreTests\n{\n'
            + ''.join(f'\n    [Fact]\n    public void Case{i}() {{ }}\n' for i in range(17)) + '}\n')
        rows, definitions = self.document.find("{*}Results"), self.document.find("{*}TestDefinitions")
        rows.clear(); definitions.clear()
        for i in range(17):
            ET.SubElement(rows, diagnostics.NAMESPACE + "UnitTestResult", executionId=f'e{i}', testId=f't{i}', outcome="Failed", testName=self.canary)
            unit = ET.SubElement(definitions, diagnostics.NAMESPACE + "UnitTest", id=f't{i}')
            ET.SubElement(unit, diagnostics.NAMESPACE + "TestMethod", className="Observed.Tests.MoreTests", name=f'Case{i}')
        self.document.find("{*}ResultSummary/{*}Counters").attrib.update(total="17", executed="17", passed="0", failed="17")
        self.write_trx()
        value = diagnostics._trx(self.root, self.source, self.trx)
        self.assertEqual(17, value["counts"]["failed"])
        self.assertEqual(16, len(value["failedMethods"]))
        self.assertEqual(1, value["omittedFailedMethods"])
        self.assertEqual(0, value["unavailableFailedIdentities"])
        self.assertNotIn(self.canary, json.dumps(value))

    def test_valid_zero_execution_counts_are_retained_as_failure_and_never_promoted_to_qualification(self):
        self.document.find("{*}Results").clear()
        self.document.find("{*}TestDefinitions").clear()
        self.document.find("{*}ResultSummary/{*}Counters").attrib.update(total="0", executed="0", passed="0", failed="0")
        self.write_trx()
        value = self.retain()
        self.assertEqual("valid", value["trx"]["status"])
        self.assertEqual(0, value["trx"]["counts"]["executed"])
        self.assertEqual("failed", value["status"])
        self.assertFalse(value["qualificationComplete"])

    def test_fallback_refuses_foreign_relative_symlink_changed_and_malformed_references_without_private_text(self):
        self.retain()
        reference = self.report["authorTestFailureDiagnostic"]
        path = Path(reference["path"])
        original = path.read_bytes()
        outside = self.root.parent / "foreign.json"; outside.write_bytes(original)
        cases = [{"path": str(outside), "sha256": reference["sha256"]}, {"path": path.name, "sha256": reference["sha256"]},
                 {"path": str(path), "sha256": "0" * 64}, {"kind": self.canary}]
        for item in cases:
            value = diagnostics.load_test_failure(self.root, item, "a" * 40)
            self.assertEqual("unavailable", value["retention"])
            self.assertNotIn(self.canary, json.dumps(value))
        path.unlink(); path.symlink_to(outside)
        self.assertEqual("unavailable", diagnostics.load_test_failure(self.root, reference, "a" * 40)["retention"])
        path.unlink(); path.write_bytes(original.replace(b'"source": "' + b'a' * 40 + b'"', b'"source": "' + b'a' * 40 + b'", "source": "' + b'a' * 40 + b'"'))
        forged = {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
        self.assertEqual("unavailable", diagnostics.load_test_failure(self.root, forged, "a" * 40)["retention"])

    def test_fallback_projects_away_unknown_fields_display_data_bad_frames_and_forged_failed_names(self):
        value = self.retain()
        value.update(message=self.canary, password=self.canary, content={"accessToken": self.canary})
        row = value["trx"]["failedMethods"][0]
        row.update(testName=self.canary, inlineData=[self.canary])
        row["frames"] += [{"file": "/private/" + self.canary, "line": 3}, {"file": self.file.name, "line": True}]
        value["trx"]["failedMethods"].append(dict(row, method="Undeclared_" + self.canary.replace("-", "_")))
        reference = self.report["authorTestFailureDiagnostic"]
        path = Path(reference["path"]); path.write_text(json.dumps(value))
        reference["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
        safe = diagnostics.load_test_failure(self.root, reference, "a" * 40)
        self.assertEqual("available", safe["retention"])
        self.assertEqual(1, len(safe["trx"]["failedMethods"]))
        self.assertEqual([{"file": self.file.name, "line": 9}], safe["trx"]["failedMethods"][0]["frames"])
        self.assertNotIn(self.canary, json.dumps(safe))
        self.assertNotIn(self.canary.replace("-", "_"), json.dumps(safe))


if __name__ == "__main__":
    unittest.main()
