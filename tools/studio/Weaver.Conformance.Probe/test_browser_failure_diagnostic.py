"""A failed journey stays failed; its public receipt cannot carry private payloads."""
import json
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from export_evidence import export_evidence
from journey_runtime import JourneyRuntime, safe_browser_failure


class BrowserFailureDiagnosticTests(unittest.TestCase):
    def test_actual_browser_frame_parser_exports_only_bounded_source_coordinates(self):
        source = (Path(__file__).parent / "browser.mjs").read_text()
        function = source[source.index("function safeFailureFrames("):source.index("function diagnosticUrl(")]
        script = function + r'''
const secret="Bearer private-value-must-not-export";
const stack="Error: "+secret+" browser.mjs:98:2\n"
  +"    at caller (file:///private/"+secret+"/browser.mjs:1300:9)\n"
  +"    at caller (file:///private/browser.mjs:1300:9)\n"
  +"    at foreign (file:///private/browser-extra.mjs:7:8)\n"
  +"    at huge (file:///private/browser.mjs:1000001:1)\n"
  +Array.from({length:20},(_,i)=>"    at caller (file:///private/browser.mjs:"+(1400+i)+":3)").join("\n");
console.log(JSON.stringify(safeFailureFrames({stack})));
'''
        result = subprocess.run(["node", "-e", script], capture_output=True, text=True, check=True)
        frames = json.loads(result.stdout)
        self.assertEqual(8, len(frames))
        self.assertEqual({"file": "browser.mjs", "line": 1300, "column": 9}, frames[0])
        self.assertEqual(1406, frames[-1]["line"])
        self.assertNotIn("private", result.stdout)
        self.assertNotIn("Bearer", result.stdout)

    def test_public_projection_rejects_credentials_foreign_files_and_nonnumeric_positions(self):
        observation = {"failure": {"message": "Bearer private-value"}, "failureDiagnostic": {
            "kind": "TimeoutError", "password": "private-value", "frames": [
                {"file": "browser.mjs", "line": 1330, "column": 4, "accessToken": "private-value"},
                {"file": "/private/browser.mjs", "line": 1, "column": 2},
                {"file": "browser.mjs", "line": True, "column": 2},
                {"file": "browser.mjs", "line": "Bearer private-value", "column": 2}]}}
        diagnostic = safe_browser_failure(observation)
        self.assertEqual({"kind": "TimeoutError", "frames": [{"file": "browser.mjs", "line": 1330, "column": 4}]}, diagnostic)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "run"; root.mkdir()
            (root / "report.json").write_text(json.dumps({"status": "failed", "browserFailureDiagnostic": diagnostic}))
            (root / "observation-private.json").write_text(json.dumps(observation))
            destination = root.parent / "safe"
            index = export_evidence(root, destination)
            self.assertEqual({"report.json"}, set(index["files"]))
            self.assertEqual("failed", json.loads((destination / "report.json").read_text())["status"])
            self.assertNotIn("private-value", (destination / "report.json").read_text())
        self.assertEqual({"kind": "Unavailable", "frames": []}, safe_browser_failure({"failureDiagnostic": {"kind": "Bearer private-value"}}))

    def test_fatal_exception_or_rejection_retains_only_safe_receipt_and_still_exits_failed(self):
        source = (Path(__file__).parent / "browser.mjs").read_text()
        functions = source[source.index("function safeFailureFrames("):source.index("function diagnosticUrl(")]
        monitor = next(line for line in source.splitlines() if line.startswith('process.on("uncaughtExceptionMonitor"'))
        for terminal in ('throw new Error("Bearer private-value");', 'void Promise.reject(new Error("Bearer private-value"));'):
            with self.subTest(terminal=terminal), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                script = 'import fs from "node:fs"; import path from "node:path";\nconst config={output:' + json.dumps(str(root)) + '};\n' + functions + monitor + '\n' + terminal
                file = root / "browser.mjs"; file.write_text(script)
                result = subprocess.run(["node", str(file)], capture_output=True, text=True)
                self.assertEqual(1, result.returncode)
                self.assertIn("private-value", result.stderr)  # Normal fatal behavior stays intact.
                text = (root / "failure-diagnostic.json").read_text()
                diagnostic = json.loads(text)
                self.assertEqual("Error", diagnostic["kind"])
                self.assertTrue(diagnostic["frames"])
                self.assertNotIn("private-value", text)
                self.assertNotIn(str(root), text)

    def test_first_primary_survives_later_fatal_cleanup_and_process_still_fails(self):
        source = (Path(__file__).parent / "browser.mjs").read_text()
        functions = source[source.index("function safeFailureFrames("):source.index("function diagnosticUrl(")]
        monitor = next(line for line in source.splitlines() if line.startswith('process.on("uncaughtExceptionMonitor"'))
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            script = 'import fs from "node:fs"; import path from "node:path";\nconst config={output:' + json.dumps(str(root)) + '};\n'
            script += functions + monitor + r'''
const primary = new Error("Bearer primary-private-canary");
primary.stack = "Error: private\n    at primary (file:///private/browser.mjs:111:2)";
retainFailureDiagnostic(primary);
const cleanup = new Error("Bearer cleanup-private-canary");
cleanup.stack = "Error: private\n    at cleanup (file:///private/browser.mjs:222:3)";
throw cleanup;
'''
            file = root / "browser.mjs"; file.write_text(script)
            result = subprocess.run(["node", str(file)], capture_output=True, text=True)
            self.assertEqual(1, result.returncode)
            text = (root / "failure-diagnostic.json").read_text()
            self.assertEqual({"kind": "Error", "frames": [{"file": "browser.mjs", "line": 111, "column": 2}]}, json.loads(text))
            self.assertNotIn("private", text)
            self.assertNotIn(str(root), text)

    def test_failed_process_exports_safe_location_and_reraises_original_failure(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            output = root / "observation"; output.mkdir()
            (output / "failure-diagnostic.json").write_text(json.dumps({
                "kind": "AssertionError", "frames": [{"file": "browser.mjs", "line": 1300, "column": 9}],
                "message": "Bearer private-value"}))
            original = RuntimeError("original browser failure")
            runtime = JourneyRuntime.__new__(JourneyRuntime)
            runtime.sign_in = Mock(); runtime.provider = "copilot"; runtime.output = Mock(return_value=root)
            runtime.studio_origin = "http://127.0.0.1:41000"; runtime.backend_origin = "http://127.0.0.1:41001"
            runtime.active_identity = {"storageState": str(root / "storage-state.json")}
            runtime.command_file = root / "command.json"
            runtime.transport = Mock(return_value=SimpleNamespace(trace_file=root / "trace.jsonl"))
            runtime.run = SimpleNamespace(run=Mock(side_effect=original), report={}, save=Mock())
            records = Mock()
            with patch("journey_runtime.shutil.which", return_value="node"):
                with self.assertRaises(RuntimeError) as caught:
                    runtime.browser(records, "resource", "resource")
            self.assertIs(original, caught.exception)
            records.add.assert_not_called()
            runtime.run.save.assert_called_once()
            self.assertEqual({"browserFailureDiagnostic": {"kind": "AssertionError", "frames": [{"file": "browser.mjs", "line": 1300, "column": 9}]}}, runtime.run.report)


if __name__ == "__main__":
    unittest.main()
