"""Synthetic negative controls for the bounded driver; these are not product bites."""
from dataclasses import replace
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

import causal_proof as proof


def records(recipe, phase="mutated"):
    messages = {
        "policy": "Assert.Equal() Failure: Values differ\nExpected: Denied\nActual: ProposalCreated",
        "receipt-integrity": 'Assert.Equal() Failure: Strings differ\nExpected: "uncertain"\nActual: "performed"',
        "module-code-normalization": 'Assert.Equal() Failure: Strings differ\nExpected: "module.code.unsupported"\nActual: "native.ok"',
        "write-admission": "Assert.Throws() Failure: No exception was thrown\nExpected: typeof(Elsa.Persistence.EntityFramework.EfSchemaWriteRefusedException)",
        "support-retirement": 'AssertionError: expected "spy" to not be called at all, but actually been called 1 times',
        "retired-origin": 'AssertionError: expected "spy" to not be called at all, but actually been called 1 times',
        "r15-refusal-admission": 'Assert.Equal() Failure: Strings differ\nExpected: "schema-version-not-finalized"\nActual: "test.refused"',
        "r15-bare-disclosure": "Assert.Single() Failure: The collection did not contain a matching item",
        "r15-optional-representation": 'Assert.Null() Failure: Value is not null\nValue: WeaverError { Code = "weaver.proposal.expired" }',
    }
    failed = recipe.failed_names() if phase == "mutated" else ()
    return [{"name": name, "status": "failed" if name in failed else "passed",
        "message": messages[recipe.id] if name in failed else "",
        "stack": f"at {recipe.test}() in /synthetic/{Path(recipe.assertion_file).name}:line {recipe.assertion_line}" if name in failed else ""}
        for name in recipe.names()]


class VerdictControls(unittest.TestCase):
    def test_the_nine_concrete_contracts_accept_only_their_declared_triplet(self):
        self.assertEqual(len(proof.RECIPES), 9)
        self.assertEqual(len({recipe.id for recipe in proof.RECIPES}), 9)
        for recipe in proof.RECIPES:
            with self.subTest(recipe=recipe.id):
                proof.assess(recipe, "baseline", 0, records(recipe, "baseline"))
                result = proof.assess(recipe, "mutated", 1, records(recipe))
                self.assertEqual(result["failed"], len(recipe.failed_names()))
                proof.assess(recipe, "restored", 0, records(recipe, "restored"))

    def test_wrong_cause_wrong_frame_and_wrong_theory_row_never_count_as_a_bite(self):
        recipe = proof.RECIPES[0]
        for change in (lambda row: row.update(message="System.InvalidOperationException: setup failed"),
                       lambda row: row.update(stack="at other.cs:line 32"),
                       lambda row: row.update(stack=row["stack"].replace(":line 32", ":line 320")),
                       lambda row: row.update(name=recipe.test + "(required: Administrative, enabled: False)")):
            with self.subTest(change=change):
                rows = records(recipe)
                change(next(row for row in rows if row["status"] == "failed"))
                with self.assertRaises(proof.ProofFailure):
                    proof.assess(recipe, "mutated", 1, rows)

    def test_zero_foreign_duplicate_skipped_and_compile_exit_are_not_bites(self):
        recipe = proof.RECIPES[0]
        scenarios = [[], records(recipe)[:-1], records(recipe) + [records(recipe)[0]],
            [{**row, "name": "Foreign.test"} for row in records(recipe)],
            [{**row, "status": "skipped"} for row in records(recipe)]]
        for rows in scenarios:
            with self.subTest(rows=len(rows)), self.assertRaises(proof.ProofFailure):
                proof.assess(recipe, "mutated", 1, rows)
        for phase, code, rows in (("mutated", 2, records(recipe)), ("mutated", 0, records(recipe)),
                ("baseline", 1, records(recipe, "baseline")), ("restored", 1, records(recipe))):
            with self.subTest(phase=phase, code=code), self.assertRaises(proof.ProofFailure):
                proof.assess(recipe, phase, code, rows)

    def test_recipe_anchors_match_reviewed_source_without_importing_or_executing_it(self):
        root = Path(__file__).resolve().parents[3]
        for recipe in proof.RECIPES:
            with self.subTest(recipe=recipe.id):
                self.assertEqual((root / recipe.source).read_bytes().count(recipe.before.encode()), 1)
                line = (root / recipe.assertion_file).read_text().splitlines()[recipe.assertion_line - 1].strip()
                self.assertEqual(line, recipe.assertion_text)

    def test_expected_head_rejects_invalid_and_different_input_without_echoing_it(self):
        proof.require_expected_head("a" * 40, "a" * 40)
        proof.require_expected_head("a" * 40, None)
        for expected in ("b" * 40, "private-input\ncanary", "", "a" * 41):
            with self.subTest(length=len(expected)), self.assertRaises(proof.ProofFailure) as caught:
                proof.require_expected_head("a" * 40, expected)
            if expected:
                self.assertNotIn(expected, str(caught.exception))

    def test_dotnet_command_keeps_only_the_existing_nu1603_warning_exception(self):
        for recipe in proof.RECIPES:
            if recipe.scope != "dotnet": continue
            with self.subTest(recipe=recipe.id):
                command, _, _ = proof.command(recipe, Path("/synthetic/evidence"))
                self.assertEqual([argument for argument in command if "Warning" in argument],
                    ["-p:WarningsNotAsErrors=NU1603"])
                self.assertIn("--no-restore", command)
                self.assertNotIn("--no-build", command)


class ReportControls(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)

    def tearDown(self):
        self.temporary.cleanup()

    def trx(self, recipe, rows):
        tree = ET.Element("TestRun")
        definitions = ET.SubElement(tree, "TestDefinitions")
        results = ET.SubElement(tree, "Results")
        owner, method = recipe.test.rsplit(".", 1)
        for index, row in enumerate(rows):
            unit = ET.SubElement(definitions, "UnitTest", id=str(index))
            ET.SubElement(unit, "TestMethod", className=owner, name=method, codeBase="/synthetic/Tests.dll")
            result = ET.SubElement(results, "UnitTestResult", testId=str(index), executionId="execution-" + str(index),
                testName=row["name"], outcome=row["status"].capitalize())
            error = ET.SubElement(ET.SubElement(result, "Output"), "ErrorInfo")
            ET.SubElement(error, "Message").text = row["message"]
            ET.SubElement(error, "StackTrace").text = row["stack"]
        summary = ET.SubElement(tree, "ResultSummary")
        ET.SubElement(summary, "Counters", total=str(len(rows)), executed=str(len(rows)),
            passed=str(sum(row["status"] == "passed" for row in rows)), failed=str(sum(row["status"] == "failed" for row in rows)))
        path = self.root / "results.trx"
        ET.ElementTree(tree).write(path)
        return path, tree

    def test_trx_preserves_observed_rows_and_rejects_foreign_methods_and_wrong_counts(self):
        recipe = proof.RECIPES[0]
        expected = records(recipe)
        path, tree = self.trx(recipe, expected)
        actual, metadata = proof.parse_trx(path, recipe)
        self.assertEqual(actual, expected)
        self.assertEqual(metadata["rawCounters"]["executed"], "6")
        tree.find("./TestDefinitions/UnitTest/TestMethod").set("name", "Foreign")
        ET.ElementTree(tree).write(path)
        with self.assertRaises(proof.ProofFailure): proof.parse_trx(path, recipe)
        path, tree = self.trx(recipe, expected)
        tree.find("./ResultSummary/Counters").set("aborted", "1")
        ET.ElementTree(tree).write(path)
        with self.assertRaises(proof.ProofFailure): proof.parse_trx(path, recipe)
        path, tree = self.trx(recipe, expected)
        tree.find("./ResultSummary/Counters").set("executed", "0")
        ET.ElementTree(tree).write(path)
        with self.assertRaises(proof.ProofFailure): proof.parse_trx(path, recipe)

    def test_vitest_ignores_only_pending_foreign_cases_and_never_runtime_errors(self):
        recipe = proof.RECIPES[4]
        rows = records(recipe)
        report = {"numPassedTests": 0, "numFailedTests": 4, "numRuntimeErrorTestSuites": 0,
            "testResults": [{"name": recipe.assertion_file, "assertionResults": [
                {"fullName": row["name"], "status": row["status"], "failureMessages": [row["message"] + "\n" + row["stack"]]} for row in rows
            ] + [{"fullName": "Unselected test", "status": "pending"}]}]}
        path = self.root / "vitest.json"
        path.write_text(json.dumps(report))
        selected, _ = proof.parse_vitest(path, recipe)
        proof.assess(recipe, "mutated", 1, selected)
        report["testResults"][0]["assertionResults"][-1]["status"] = "failed"
        path.write_text(json.dumps(report))
        with self.assertRaises(proof.ProofFailure): proof.parse_vitest(path, recipe)
        report["testResults"][0]["assertionResults"].pop()
        report["numRuntimeErrorTestSuites"] = 1
        path.write_text(json.dumps(report))
        with self.assertRaises(proof.ProofFailure): proof.parse_vitest(path, recipe)


class RestorationControls(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.recipe = replace(proof.RECIPES[0], source="source.cs", before="guard", after="disabled",
            assertion_file="test.cs", assertion_line=1, assertion_text="assertion")
        self.original = b"original guard bytes\n"
        (self.root / "source.cs").write_bytes(self.original)
        (self.root / "test.cs").write_text("assertion\n")

    def tearDown(self):
        self.temporary.cleanup()

    def test_exception_and_interrupt_restore_exact_original_bytes(self):
        for error in (RuntimeError("synthetic command failure"), KeyboardInterrupt()):
            with self.subTest(error=type(error).__name__):
                state = {}
                with self.assertRaises(type(error)):
                    with proof.mutation(self.root, self.recipe, self.original, state):
                        self.assertEqual((self.root / "source.cs").read_bytes(), b"original disabled bytes\n")
                        raise error
                self.assertEqual((self.root / "source.cs").read_bytes(), self.original)
                self.assertTrue(state["originalBytesRestored"])

    def test_ambiguous_fragment_is_rejected_and_unowned_concurrent_change_is_not_overwritten(self):
        for value in (b"guard guard", b"another editor's guard"):
            (self.root / "source.cs").write_bytes(value)
            with self.assertRaises(proof.ProofFailure):
                with proof.mutation(self.root, self.recipe, self.original, {}):
                    self.fail("A foreign change cannot authorize mutation.")
            self.assertEqual((self.root / "source.cs").read_bytes(), value)

    def test_failed_restoration_propagates_instead_of_claiming_a_bite(self):
        original_writer = Path.write_bytes
        calls = 0
        def fail_restoration(path, value):
            nonlocal calls
            calls += 1
            if calls == 2: raise OSError("Synthetic restoration failure")
            return original_writer(path, value)
        state = {}
        with patch.object(Path, "write_bytes", fail_restoration), self.assertRaises(OSError):
            with proof.mutation(self.root, self.recipe, self.original, state): pass
        self.assertNotEqual((self.root / "source.cs").read_bytes(), self.original)
        self.assertNotEqual(state.get("originalBytesRestored"), True)

    def fake_git(self, _root, *arguments):
        if arguments == ("rev-parse", "HEAD"): return "a" * 40
        if arguments == ("diff", "--name-only"): return "source.cs"
        return ""

    def test_mutant_setup_failure_still_runs_restored_control_but_never_passes_recipe(self):
        phases = []
        def execute(_root, _recipe, phase, _output, _deadline):
            phases.append(phase)
            self.assertEqual((self.root / "source.cs").read_bytes(),
                b"original disabled bytes\n" if phase == "mutated" else self.original)
            return {"phase": phase, "status": "failed" if phase == "mutated" else "passed"}
        with patch.object(proof, "git", self.fake_git), patch.object(proof.subprocess, "check_output", return_value=self.original), patch.object(proof, "execute", execute):
            result = proof.run_recipe(self.root, "a" * 40, self.recipe, self.root / "evidence", float("inf"))
        self.assertEqual(phases, ["baseline", "mutated", "restored"])
        self.assertEqual(result["status"], "failed")
        self.assertTrue(result["sourceBytesRestored"])

    def test_failed_baseline_never_mutates_and_failed_restored_never_passes(self):
        for failed_phase in ("baseline", "restored"):
            with self.subTest(failed_phase=failed_phase):
                phases = []
                def execute(_root, _recipe, phase, _output, _deadline):
                    phases.append(phase)
                    return {"phase": phase, "status": "failed" if phase == failed_phase else "passed"}
                with patch.object(proof, "git", self.fake_git), patch.object(proof.subprocess, "check_output", return_value=self.original), patch.object(proof, "execute", execute):
                    result = proof.run_recipe(self.root, "a" * 40, self.recipe, self.root / failed_phase, float("inf"))
                self.assertEqual(result["status"], "failed")
                self.assertEqual(phases, ["baseline"] if failed_phase == "baseline" else ["baseline", "mutated", "restored"])
                self.assertEqual((self.root / "source.cs").read_bytes(), self.original)

    def test_abrupt_mutant_interruption_restores_and_keeps_a_failed_retained_recipe(self):
        def execute(_root, _recipe, phase, _output, _deadline):
            if phase == "mutated": raise KeyboardInterrupt()
            return {"phase": phase, "status": "passed"}
        output = self.root / "interrupted"
        with patch.object(proof, "git", self.fake_git), patch.object(proof.subprocess, "check_output", return_value=self.original), patch.object(proof, "execute", execute):
            with self.assertRaises(KeyboardInterrupt):
                proof.run_recipe(self.root, "a" * 40, self.recipe, output, float("inf"))
        self.assertEqual((self.root / "source.cs").read_bytes(), self.original)
        receipt = json.loads((output / "recipe.json").read_text())
        self.assertEqual(receipt["status"], "failed")
        self.assertTrue(receipt["sourceBytesRestored"])


if __name__ == "__main__":
    unittest.main()
