"""Synthetic failure controls for the acceptance gate, never product or screenshot evidence."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import threading
import unittest

spec = importlib.util.spec_from_file_location("standalone_acceptance", Path(__file__).with_name("standalone.py"))
acceptance = importlib.util.module_from_spec(spec)
spec.loader.exec_module(acceptance)


class StandaloneVerdictTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="synthetic-trigger-gate-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.journey = "standalone trigger management"
        self.sha = "synthetic-failure-control-only"
        self.report = {"suites": []}
        self.evidence = {}
        for project in sorted(acceptance.TRIGGER_PROJECTS):
            folder = self.root / project
            folder.mkdir()
            captures = acceptance.TRIGGER_CAPTURES[self.journey]
            attachments = []
            for name in ["trace"] + [f"{name}.{suffix}" for name in captures for suffix in ("png", "aria.yml")]:
                path = folder / name
                path.write_text("SYNTHETIC gate control; not a screenshot or product witness")
                attachments.append({"name": name, "path": str(path)})
            mode = project.removeprefix("desktop-").removeprefix("narrow-").removeprefix("compact-")
            facts = {"kind": "composed-source", "sourceSha": self.sha, "journey": self.journey, "project": project,
                "requests": [{"synthetic": True}], "observed": ["synthetic gate control"], "unverified": [],
                "captures": [{"name": name, "viewport": {"width": 320 if project == "compact-light" else 360 if project.startswith("narrow-") else 1440},
                    "palette": "synthetic", "appearance": mode, "mode": "dark" if mode in {"dim", "high-contrast"} else mode} for name in captures]}
            path = folder / "standalone-evidence.json"
            path.write_text(json.dumps(facts))
            self.evidence[project] = (path, facts)
            attachments.append({"name": path.name, "path": str(path)})
            self.report["suites"].append({"specs": [{"title": self.journey, "tests": [{"projectName": project,
                "expectedStatus": "passed", "status": "expected", "results": [{"status": "passed", "attachments": attachments}]}]}]})

    def verdict(self, report=None, selected=True):
        return acceptance.verdict(report or self.report, self.root, [self.journey] if selected else None,
            self.sha, suite="standalone-triggers")

    def test_selected_journey_requires_all_seven_appearances(self):
        self.assertEqual(7, self.verdict())
        for change in ("missing", "duplicate"):
            with self.subTest(change=change):
                report = copy.deepcopy(self.report)
                if change == "missing": report["suites"].pop()
                else: report["suites"].append(copy.deepcopy(report["suites"][0]))
                with self.assertRaisesRegex(ValueError, "inventory"): self.verdict(report)

    def test_selected_journey_cannot_certify_the_complete_feature(self):
        with self.assertRaisesRegex(ValueError, "inventory"): self.verdict(selected=False)
        self.assertEqual(49, len(acceptance.inventory("standalone-triggers")))

    def test_failed_skipped_expected_failure_or_retry_cannot_be_success(self):
        for change in ("failed", "skipped", "expected-failure", "retry", "runner-error"):
            with self.subTest(change=change):
                report = copy.deepcopy(self.report)
                row = report["suites"][0]["specs"][0]["tests"][0]
                if change in {"failed", "skipped"}: row["results"][0]["status"] = change
                elif change == "expected-failure": row["expectedStatus"] = "failed"
                elif change == "retry": row["results"].append(copy.deepcopy(row["results"][0]))
                else: report["errors"] = [{"message": "synthetic runner failure"}]
                with self.assertRaises(ValueError): self.verdict(report)

    def test_unverified_foreign_source_wrong_mode_or_missing_capture_is_rejected(self):
        project = sorted(self.evidence)[0]
        path, original = self.evidence[project]
        for change in ("unverified", "foreign-source", "wrong-mode", "missing-capture"):
            with self.subTest(change=change):
                facts = copy.deepcopy(original)
                if change == "unverified": facts["unverified"].append({"state": "not proved", "reason": "synthetic negative control"})
                elif change == "foreign-source": facts["sourceSha"] = "another-synthetic-source"
                elif change == "wrong-mode": facts["captures"][0]["mode"] = "wrong-mode"
                else: facts["captures"].pop()
                path.write_text(json.dumps(facts))
                try:
                    with self.assertRaises(ValueError): self.verdict()
                finally: path.write_text(json.dumps(original))

    def test_dark_base_does_not_substitute_for_exact_dim_appearance(self):
        path, original = self.evidence["desktop-dim"]
        facts = copy.deepcopy(original)
        facts["captures"][0]["appearance"] = "dark"
        path.write_text(json.dumps(facts))
        with self.assertRaises(ValueError): self.verdict()

    def test_external_or_empty_artifacts_are_rejected(self):
        report = copy.deepcopy(self.report)
        attachment = report["suites"][0]["specs"][0]["tests"][0]["results"][0]["attachments"][0]
        attachment["path"] = str(self.root.parent / "not-owned")
        with self.assertRaises(ValueError): self.verdict(report)
        attachment["path"] = str(self.root / "empty")
        Path(attachment["path"]).touch()
        with self.assertRaises(ValueError): self.verdict(report)


class OwnedConsumerPreparationTests(unittest.TestCase):
    def test_sdk_consumer_uses_declared_configuration_and_never_seeds_output_stores(self):
        with tempfile.TemporaryDirectory(prefix="synthetic-sdk-copy-control-") as temporary:
            root = Path(temporary)
            project = root / "consumer/Consumer.csproj"
            output = project.parent / "bin/Debug/net10.0"
            output.mkdir(parents=True)
            project.write_text("<Project />")
            (output / "Consumer.dll").write_text("synthetic assembly placeholder")
            (output / "appsettings.json").write_text("stale output configuration")
            (output / "not-owned.db").write_text("private store sentinel")
            (output / ".nuplane").mkdir()
            (output / ".nuplane/state.json").write_text("private package state sentinel")
            (output / "native-store").mkdir()
            (output / "native-store/state.json").write_text("foreign SDK enrollment sentinel")
            configuration = root / "application"
            configuration.mkdir()
            (configuration / "appsettings.json").write_text('{"source":"declared application"}')
            (configuration / "shells.json").write_text('{"source":"declared shells"}')
            destination = root / "owned"
            acceptance.prepare_host(project, destination, configuration_directory=configuration)
            self.assertEqual((configuration / "appsettings.json").read_bytes(), (destination / "appsettings.json").read_bytes())
            self.assertEqual((configuration / "shells.json").read_bytes(), (destination / "shells.json").read_bytes())
            self.assertFalse((destination / "not-owned.db").exists())
            self.assertFalse((destination / ".nuplane").exists())
            self.assertFalse((destination / "native-store").exists())
            self.assertTrue((destination / "Consumer.dll").is_file())


class OwnedLifecycleTests(unittest.TestCase):
    def test_cleanup_attempts_every_owned_host_after_join_and_first_stop_fail(self):
        calls = []
        owner = acceptance.OwnedBackend.__new__(acceptance.OwnedBackend)
        owner.closing = threading.Event()
        class HeldThread:
            def join(self, timeout=None): raise TimeoutError("controlled held join")
            def is_alive(self): return True
        owner.thread = HeldThread()
        def stop(name, fail=False):
            def apply():
                self.assertTrue(owner.closing.is_set())
                calls.append(name)
                if fail: raise RuntimeError("controlled first stop failure")
            return apply
        owner.stop_scope_peer = stop("peer", True)
        owner.stop_concurrent_node = stop("concurrent")
        owner.stop_studio = stop("studio")
        owner.stop = stop("backend")
        with self.assertRaises(TimeoutError): owner.__exit__(None, None, None)
        self.assertEqual(["peer", "concurrent", "studio", "backend"], calls)


if __name__ == "__main__":
    unittest.main()
