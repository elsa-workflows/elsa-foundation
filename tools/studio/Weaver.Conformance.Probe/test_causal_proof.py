"""Synthetic negative controls for the bounded driver; these are not product bites."""
from dataclasses import replace
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch
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
        "copilot-external-constructor-auth": "System.ArgumentException : GitHubToken and UseLoggedInUser cannot be combined with RuntimeConnection.ForUri (the existing runtime manages its own auth). (Parameter 'options')",
        "active-route-admission": "System.Collections.Generic.KeyNotFoundException : The given key was not present in the dictionary.",
        "copilot-runtime-url-redaction": 'Assert.DoesNotContain() Failure: Sub-string found\nString: "unredacted endpoint remains"\nFound:  "endpoint-' + "a" * 32 + '"',
        "copilot-runtime-url-validation": "Assert.False() Failure\nExpected: False\nActual: True",
    }
    def message(name):
        if recipe.id == "copilot-external-auth-mode":
            actual = "environment:ELSA_WEAVER_3831_RUNTIME_AUTH_" + "a" * 32 if 'providerAuth: "environment"' in name else \
                "logged-in-user" if 'providerAuth: "logged-in"' in name else "configured-token"
            return f'Assert.Equal() Failure: Strings differ\nExpected: "external-runtime"\nActual: "{actual}"'
        return messages[recipe.id]
    failed = recipe.failed_names() if phase == "mutated" else ()
    return [{"name": name, "status": "failed" if name in failed else "passed",
        "message": message(name) if name in failed else "",
        "stack": f"at {recipe.test}() in /synthetic/{Path(recipe.assertion_file).name}:line {recipe.assertion_line}" if name in failed else ""}
        for name in recipe.names()]


class VerdictControls(unittest.TestCase):
    def test_the_fourteen_concrete_contracts_accept_only_their_declared_triplet(self):
        self.assertEqual(len(proof.RECIPES), 14)
        self.assertEqual(len({recipe.id for recipe in proof.RECIPES}), 14)
        for recipe in proof.RECIPES:
            with self.subTest(recipe=recipe.id):
                proof.assess(recipe, "baseline", 0, records(recipe, "baseline"))
                result = proof.assess(recipe, "mutated", 1, records(recipe))
                self.assertEqual(result["failed"], len(recipe.failed_names()))
                proof.assess(recipe, "restored", 0, records(recipe, "restored"))

    def test_constructor_auth_control_requires_the_exact_sdk_exception_at_create(self):
        recipe = next(recipe for recipe in proof.RECIPES if recipe.id == "copilot-external-constructor-auth")
        for change in (
                lambda row: row.update(message=row["message"].replace("System.ArgumentException", "System.InvalidOperationException")),
                lambda row: row.update(message="System.InvalidOperationException : wrapped\n---> " + row["message"]),
                lambda row: row.update(message="System.ArgumentException : ConnectionToken must be a non-empty string or null."),
                lambda row: row.update(stack=row["stack"].replace(":line 51", ":line 53")),
                lambda row: row.update(status="passed")):
            with self.subTest(change=change):
                rows = records(recipe)
                change(rows[0])
                with self.assertRaises(proof.ProofFailure): proof.assess(recipe, "mutated", 1, rows)

    def test_auth_mode_control_rejects_unrelated_modes_frames_and_local_failures(self):
        recipe = next(recipe for recipe in proof.RECIPES if recipe.id == "copilot-external-auth-mode")
        for change in (
                lambda rows: rows[0].update(message='Assert.Equal() Failure: Strings differ\nExpected: "external-runtime"\nActual: "unrelated-mode"'),
                lambda rows: rows[0].update(stack=rows[0]["stack"].replace(":line 131", ":line 132")),
                lambda rows: rows[0].update(message="System.ArgumentException : unrelated setup error"),
                lambda rows: rows[4].update(status="failed", message=rows[0]["message"], stack=rows[0]["stack"])):
            with self.subTest(change=change):
                rows = records(recipe)
                change(rows)
                with self.assertRaises(proof.ProofFailure): proof.assess(recipe, "mutated", 1, rows)

    def test_auth_mode_control_allows_xunit_to_abbreviate_the_generated_environment_name(self):
        recipe = next(recipe for recipe in proof.RECIPES if recipe.id == "copilot-external-auth-mode")
        rows = records(recipe)
        rows[1]["message"] = 'Assert.Equal() Failure: Strings differ\nExpected: "external-runtime"\nActual: "environment:ELSA_WEAVER_"···'
        proof.assess(recipe, "mutated", 1, rows)

    def test_copilot_auth_controls_reject_zero_missing_or_foreign_selected_rows(self):
        for recipe in proof.RECIPES:
            if not recipe.id.startswith("copilot-external-"): continue
            for rows in ([], records(recipe)[:-1], [{**row, "name": "Foreign.test"} for row in records(recipe)]):
                with self.subTest(recipe=recipe.id, rows=len(rows)), self.assertRaises(proof.ProofFailure):
                    proof.assess(recipe, "mutated", 1, rows)

    def test_active_route_control_requires_the_missing_json_property_at_the_mapped_lookup(self):
        recipe = next(recipe for recipe in proof.RECIPES if recipe.id == "active-route-admission")
        for change in (
                lambda row: row.update(message="Assert.Equal() Failure: Values differ\nExpected: 200\nActual: 400"),
                lambda row: row.update(message="System.InvalidOperationException : The given key was not present in the dictionary."),
                lambda row: row.update(message="System.Collections.Generic.KeyNotFoundException : An unrelated lookup failed."),
                lambda row: row.update(message="System.InvalidOperationException : wrapped\n---> " + row["message"]),
                lambda row: row.update(stack=row["stack"].replace(":line 86", ":line 85")),
                lambda row: row.update(stack=row["stack"].replace(":line 86", ":line 860")),
                lambda row: row.update(status="passed")):
            with self.subTest(change=change):
                rows = records(recipe)
                change(rows[0])
                with self.assertRaises(proof.ProofFailure): proof.assess(recipe, "mutated", 1, rows)

    def test_active_route_control_rejects_incomplete_foreign_duplicate_or_skipped_encoding_rows_and_setup_exits(self):
        recipe = next(recipe for recipe in proof.RECIPES if recipe.id == "active-route-admission")
        for rows in ([], records(recipe)[:-1], records(recipe) + [records(recipe)[0]],
                [{**row, "name": "Foreign.test"} for row in records(recipe)],
                [{**row, "name": recipe.test + '(encoding: "unknown")'} for row in records(recipe)],
                [{**row, "status": "skipped"} for row in records(recipe)]):
            with self.subTest(rows=len(rows)), self.assertRaises(proof.ProofFailure):
                proof.assess(recipe, "mutated", 1, rows)
        for code in (0, 2):
            with self.subTest(exit=code), self.assertRaises(proof.ProofFailure):
                proof.assess(recipe, "mutated", code, records(recipe))

    def test_url_redaction_control_requires_the_endpoint_canary_failure_after_the_full_url_check(self):
        recipe = next(recipe for recipe in proof.RECIPES if recipe.id == "copilot-runtime-url-redaction")
        for change in (
                lambda row: row.update(message=row["message"].replace('Found:  "endpoint-', 'Found:  "runtime-')),
                lambda row: row.update(message=row["message"].replace("Sub-string found", "Filter matched in collection")),
                lambda row: row.update(message='Assert.Equal() Failure\nExpected: "sdk-unavailable"\nActual: "misconfigured"'),
                lambda row: row.update(message="System.InvalidOperationException : wrapped\n---> " + row["message"]),
                lambda row: row.update(stack=row["stack"].replace(":line 169", ":line 168")),
                lambda row: row.update(stack=row["stack"].replace(":line 169", ":line 1690"))):
            with self.subTest(change=change):
                rows = records(recipe)
                change(rows[0])
                with self.assertRaises(proof.ProofFailure): proof.assess(recipe, "mutated", 1, rows)

    def test_url_validation_control_requires_exactly_the_six_canonicalized_rows_at_availability(self):
        recipe = next(recipe for recipe in proof.RECIPES if recipe.id == "copilot-runtime-url-validation")
        self.assertEqual(len(recipe.names()), 20)
        self.assertEqual(len(recipe.failed_names()), 6)
        backslash_name = recipe.test + '(component: "backslash-path", absolute: True)'
        self.assertEqual("passed", next(row for row in records(recipe) if row["name"] == backslash_name)["status"])
        def swap_failure(rows):
            failed = next(row for row in rows if row["status"] == "failed")
            passed = next(row for row in rows if row["status"] == "passed")
            passed.update(status="failed", message=failed["message"], stack=failed["stack"])
            failed["status"] = "passed"
        def unexpected_backslash_failure(rows):
            failed = next(row for row in rows if row["status"] == "failed")
            next(row for row in rows if row["name"] == backslash_name).update(status="failed", message=failed["message"], stack=failed["stack"])
        for change in (
                swap_failure,
                unexpected_backslash_failure,
                lambda rows: next(row for row in rows if row["status"] == "failed").update(status="passed"),
                lambda rows: next(row for row in rows if row["status"] == "failed").update(message="Assert.Equal() Failure\nExpected: 0\nActual: 1"),
                lambda rows: next(row for row in rows if row["status"] == "failed").update(message="System.ArgumentException : invalid URI"),
                lambda rows: next(row for row in rows if row["status"] == "failed").update(stack="at WeaverProviderLogRedactionTests.cs:line 257"),
                lambda rows: next(row for row in rows if row["status"] == "failed").update(stack="at WeaverProviderLogRedactionTests.cs:line 2560")):
            with self.subTest(change=change):
                rows = records(recipe)
                change(rows)
                with self.assertRaises(proof.ProofFailure): proof.assess(recipe, "mutated", 1, rows)

    def test_url_controls_refuse_incomplete_foreign_duplicate_skipped_rows_and_wrong_command_exits(self):
        for recipe in proof.RECIPES:
            if not recipe.id.startswith("copilot-runtime-url-"): continue
            for rows in ([], records(recipe)[:-1], records(recipe) + [records(recipe)[0]],
                    [{**row, "name": "Foreign.test"} for row in records(recipe)],
                    [{**row, "status": "skipped"} for row in records(recipe)]):
                with self.subTest(recipe=recipe.id, rows=len(rows)), self.assertRaises(proof.ProofFailure):
                    proof.assess(recipe, "mutated", 1, rows)
            for code in (0, 2):
                with self.subTest(recipe=recipe.id, exit=code), self.assertRaises(proof.ProofFailure):
                    proof.assess(recipe, "mutated", code, records(recipe))

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

    def test_preflight_refuses_changed_or_ambiguous_synthetic_source_before_commands(self):
        cases = ((b"original guard guard bytes\n", b"original guard guard bytes\n", "assertion\n"),
            (self.original, b"different committed guard\n", "assertion\n"),
            (self.original, self.original, "changed assertion\n"))
        for index, (actual, committed, assertion) in enumerate(cases):
            with self.subTest(index=index):
                (self.root / "source.cs").write_bytes(actual)
                (self.root / "test.cs").write_text(assertion)
                with patch.object(proof, "git", self.fake_git), patch.object(proof.subprocess, "check_output", return_value=committed), patch.object(proof, "execute") as execute:
                    with self.assertRaises(proof.ProofFailure):
                        proof.run_recipe(self.root, "a" * 40, self.recipe, self.root / str(index), float("inf"))
                execute.assert_not_called()
                self.assertEqual((self.root / "source.cs").read_bytes(), actual)

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

    def test_post_popen_metadata_failure_cleans_up_before_restoration_and_lock_release(self):
        for failure in (OSError("Synthetic metadata failure"), KeyboardInterrupt()):
            with self.subTest(failure=type(failure).__name__), tempfile.TemporaryDirectory() as output_parent:
                root = self.root.resolve()
                lock = root / "causal.lock"
                events = []
                process = Mock(pid=12345, returncode=None)
                original_json, original_bytes = proof.write_json, Path.write_bytes
                real_execute = proof.execute
                failed = False

                def git(_root, *arguments):
                    if arguments == ("rev-parse", "--git-path", "weaver-causal-proof.lock"): return str(lock)
                    return self.fake_git(_root, *arguments)

                def metadata(path, value):
                    nonlocal failed
                    if path.name == "command.json" and "pid" in value and not failed:
                        failed = True
                        events.append("post-popen-metadata")
                        raise failure
                    original_json(path, value)

                def cleanup(owned):
                    self.assertIs(owned, process)
                    self.assertTrue(lock.exists())
                    self.assertEqual((root / "source.cs").read_bytes(), b"original disabled bytes\n")
                    events.append("owned-process-cleanup")
                    process.returncode = -15
                    return {"groupStillObserved": False}

                def source_write(path, value):
                    if path == root / "source.cs" and value == self.original:
                        self.assertTrue(lock.exists())
                        self.assertIn("owned-process-cleanup", events)
                        events.append("source-restored")
                    return original_bytes(path, value)

                def execute(checkout, recipe, phase, output, deadline):
                    if phase == "mutated": return real_execute(checkout, recipe, phase, output, deadline)
                    return {"phase": phase, "status": "passed"}

                argv = ["causal_proof.py", "--scope", "dotnet", "--output", str(Path(output_parent).resolve() / "evidence")]
                with patch.object(proof, "__file__", str(root / "tools/studio/Probe/causal_proof.py")), patch.object(proof, "RECIPES", (self.recipe,)), \
                        patch.object(proof, "git", git), patch.object(proof.subprocess, "check_output", return_value=self.original), \
                        patch.object(proof.subprocess, "Popen", return_value=process), patch.object(proof, "terminate_group", cleanup), \
                        patch.object(proof, "write_json", metadata), patch.object(Path, "write_bytes", source_write), \
                        patch.object(proof, "execute", execute), patch.dict(os.environ, {"WEAVER_CAUSAL_EXPECTED_HEAD": "a" * 40}), \
                        patch("sys.argv", argv), patch("sys.stdout", new_callable=io.StringIO):
                    self.assertEqual(proof.main(), 1)
                self.assertEqual(events, ["post-popen-metadata", "owned-process-cleanup", "source-restored"])
                process.wait.assert_not_called()
                self.assertFalse(lock.exists())
                self.assertEqual((root / "source.cs").read_bytes(), self.original)


if __name__ == "__main__":
    unittest.main()
