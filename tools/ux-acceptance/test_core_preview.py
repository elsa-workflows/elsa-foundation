"""Synthetic controls for the Core Preview acceptance runner, never product or screenshot evidence."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import MagicMock
import urllib.request

spec = importlib.util.spec_from_file_location("core_preview_acceptance", Path(__file__).with_name("core_preview.py"))
acceptance = importlib.util.module_from_spec(spec)
spec.loader.exec_module(acceptance)


class CorePreviewVerdictTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="synthetic-core-preview-gate-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.journey = "core preview serves kernel publications"
        attachments = []
        for name in ["trace", "reference.png", "reference.aria.yml", "core-preview-smoke.json"]:
            path = self.root / name
            path.write_text("SYNTHETIC gate control; not a screenshot or product witness")
            attachments.append({"name": name, "path": str(path)})
        self.test = {"projectName": acceptance.PROJECT, "expectedStatus": "passed", "status": "expected",
                     "results": [{"status": "passed", "attachments": attachments}]}

    def report(self, *tests):
        return {"suites": [{"specs": [{"title": self.journey, "tests": list(tests)}]}]}

    def test_a_complete_single_desktop_execution_passes(self):
        self.assertEqual(1, acceptance.verdict(self.report(self.test), self.root))

    def test_another_project_or_a_second_execution_is_refused(self):
        other = dict(self.test, projectName="desktop-light")
        with self.assertRaises(ValueError):
            acceptance.verdict(self.report(other), self.root)
        with self.assertRaises(ValueError):
            acceptance.verdict(self.report(self.test, self.test), self.root)

    def test_a_missing_receipt_is_refused(self):
        stripped = json.loads(json.dumps(self.test))
        stripped["results"][0]["attachments"] = [item for item in stripped["results"][0]["attachments"] if item["name"] != "core-preview-smoke.json"]
        with self.assertRaises(ValueError):
            acceptance.verdict(self.report(stripped), self.root)

    def test_an_artifact_outside_the_evidence_directory_is_refused(self):
        outside = tempfile.NamedTemporaryFile(delete=False)
        self.addCleanup(Path(outside.name).unlink)
        outside.write(b"outside"); outside.close()
        moved = json.loads(json.dumps(self.test))
        moved["results"][0]["attachments"][-1]["path"] = outside.name
        with self.assertRaises(ValueError):
            acceptance.verdict(self.report(moved), self.root)

    def test_unknown_journeys_are_refused(self):
        with self.assertRaises(ValueError):
            acceptance.inventory(["not a core preview journey"])


class CorePreviewCompositionTests(unittest.TestCase):
    def test_the_kernel_switches_and_store_reach_only_this_composition(self):
        env = acceptance.kernel_environment("Host=127.0.0.1;Port=1;Database=x")
        self.assertEqual({"Elsa__CorePreview__Enabled": "true", "Elsa__CorePreview__Publications": "true",
                          "Elsa__CorePreview__Tenant": acceptance.TENANT,
                          "ConnectionStrings__KernelCorePreview": "Host=127.0.0.1;Port=1;Database=x"}, env)

    def test_kernel_serving_is_the_serving_line_and_its_failures_are_named(self):
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "backend.log"
            process = MagicMock(); process.poll.return_value = None
            log.write_text("info: x[0]\n      The Core Preview serves the publications of shell default on the kernel: 0 of 0 active slots serve.\n")
            self.assertIn(acceptance.SERVING_LINE, acceptance.wait_for_kernel_serving(log, process, timeout=1))
            log.write_text("fail: x[0]\n      The Core Preview could not serve the publications of shell default on the kernel.\n")
            with self.assertRaises(RuntimeError):
                acceptance.wait_for_kernel_serving(log, process, timeout=1)
            log.write_text("info: starting\n")
            process.poll.return_value = 1
            with self.assertRaises(RuntimeError):
                acceptance.wait_for_kernel_serving(log, process, timeout=1)

    def test_the_stub_answers_with_what_it_got_and_records_it(self):
        with acceptance.Stub() as stub:
            sent = urllib.request.Request(f"{stub.url}/orders/7", data=b'{"id":7}', method="POST",
                                          headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(sent, timeout=5) as response:
                answer = json.loads(response.read())
        self.assertEqual({"ok": True, "method": "POST", "path": "/orders/7", "body": '{"id":7}', "received": 1}, answer)
        self.assertEqual([{"method": "POST", "path": "/orders/7", "body": '{"id":7}'}], stub.recorded())
        self.assertTrue(stub.url.startswith("http://127.0.0.1:"))


if __name__ == "__main__":
    unittest.main()
