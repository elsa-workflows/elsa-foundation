"""Synthetic controls for the Core Preview acceptance runner, never product or screenshot evidence."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import MagicMock, patch
import urllib.request

spec = importlib.util.spec_from_file_location("core_preview_acceptance", Path(__file__).with_name("core_preview.py"))
acceptance = importlib.util.module_from_spec(spec)
spec.loader.exec_module(acceptance)


class CorePreviewVerdictTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="synthetic-core-preview-gate-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.journey = "core preview stack is ready"
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
        other = dict(self.test, projectName="desktop-dark")
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

    def test_the_password_stays_out_of_argv_and_an_interrupted_run_still_stops_its_container(self):
        calls = []
        def check_output(args, **kwargs):
            calls.append((args, kwargs))
            raise KeyboardInterrupt  # interrupted before docker run printed an id
        def run(args, **kwargs):
            calls.append((args, kwargs))
            return MagicMock(returncode=0)
        with tempfile.TemporaryDirectory() as directory, \
                patch.object(acceptance.subprocess, "check_output", side_effect=check_output), \
                patch.object(acceptance.subprocess, "run", side_effect=run):
            with self.assertRaises(KeyboardInterrupt):
                with acceptance.postgres(Path(directory) / "postgres.log"):
                    self.fail("No container was started")
        (started, options), (stopped, _) = calls
        name = started[started.index("--name") + 1]
        self.assertIn("POSTGRES_PASSWORD", started)
        self.assertFalse([argument for argument in started if argument.startswith("POSTGRES_PASSWORD=")], "The password is in argv")
        self.assertEqual(48, len(options["env"]["POSTGRES_PASSWORD"]))
        self.assertIn(acceptance.LABEL, started)
        self.assertIn(f"{acceptance.OWNER_LABEL}={acceptance.OWNER}", started)
        self.assertEqual(["docker", "stop", "--time", "3", name], stopped)

    def test_only_containers_whose_run_has_ended_are_swept(self):
        host = acceptance.socket.gethostname()
        live = f"{host}:{acceptance.os.getpid()}:1"
        gone = f"{host}:999999:1"
        listing = f"exited1 exited {live}\nlive1 running {live}\ngone1 running {gone}\nother1 running elsewhere:1:1\n"
        with patch.object(acceptance.subprocess, "check_output", return_value=listing) as listed, \
                patch.object(acceptance.subprocess, "run") as removed, \
                patch.object(acceptance.os, "kill", side_effect=lambda pid, _: (_ for _ in ()).throw(ProcessLookupError()) if pid == 999999 else None):
            swept = acceptance.sweep_stale_containers()
        self.assertEqual({"removed": ["exited1", "gone1"], "keptRunning": ["live1", "other1"]}, swept)
        self.assertIn(f"label={acceptance.LABEL}", listed.call_args.args[0])
        self.assertEqual(["docker", "rm", "-f", "exited1", "gone1"], removed.call_args.args[0])

    def test_each_container_is_labelled_with_its_owner(self):
        host, pid, started = acceptance.OWNER.split(":")
        self.assertEqual((acceptance.socket.gethostname(), str(acceptance.os.getpid())), (host, pid))
        self.assertTrue(started.isdigit())

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
