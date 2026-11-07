"""Evidence-gate controls; these do not replace the real package/native witness."""
from copy import deepcopy
import importlib.util
from pathlib import Path
import tempfile
import subprocess
import tarfile
import unittest

SPEC = importlib.util.spec_from_file_location("dashboard_probe", Path(__file__).with_name("qualify.py"))
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)


def complete():
    return {"preliminary": True, "packages": {"A": {}, "B": {}},
            "cases": [{"id": name, "status": "passed"} for name in PROBE.CASE_IDS],
            "stages": [{"id": name, "status": "passed"} for name in (
                "producer-build", "producer-pack-0", "producer-pack-1", "consumer-online-restore", "consumer-offline-restore", "consumer-publish")],
            "hostProcesses": [{"label": label, "status": "stopped", "exitCode": 0} for label in ("enabled", "disabled", "restored")],
            "consumer": {"packageOnly": True, "offlineRestore": True, "frontendInvocations": 0},
            "nativeProfileCommonBytesUnchanged": True}


class EvidenceControls(unittest.TestCase):
    def test_source_archive_uses_the_committed_configuration_filename_casing(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "NuGet.config").write_text("<configuration />")
            (root / "VersionLines.props").write_text("<Project />")
            subprocess.run(["git", "init", "-q", str(root)], check=True, capture_output=True)
            subprocess.run(["git", "add", "."], cwd=root, check=True, capture_output=True)
            subprocess.run(["git", "-c", "user.name=Dashboard test", "-c", "user.email=dashboard@example.invalid",
                            "commit", "-qm", "Owned source inputs"], cwd=root, check=True, capture_output=True)
            paths = [path.relative_to(root).as_posix() for path in PROBE.root_input_files(root, "HEAD")]
            self.assertEqual({"NuGet.config", "VersionLines.props"}, set(paths))
            archive = root / "source.tar"
            subprocess.run(["git", "archive", "--format=tar", "--output=" + str(archive), "HEAD", *paths],
                           cwd=root, check=True, capture_output=True)
            with tarfile.open(archive) as source:
                self.assertEqual(set(paths), set(source.getnames()))
                self.assertEqual(b"<configuration />", source.extractfile("NuGet.config").read())

    def test_complete_native_inventory_is_admitted(self):
        PROBE.validate_complete(complete())

    def test_missing_or_duplicated_native_case_cannot_qualify(self):
        for mutate in (lambda report: report["cases"].pop(), lambda report: report["cases"].append(deepcopy(report["cases"][0]))):
            report = complete(); mutate(report)
            with self.assertRaises(RuntimeError): PROBE.validate_complete(report)

    def test_failed_or_not_run_case_cannot_qualify(self):
        for state in ("failed", "not-run", "cancelled", "unavailable", "running"):
            report = complete(); report["cases"][0]["status"] = state
            with self.assertRaises(RuntimeError): PROBE.validate_complete(report)

    def test_missing_or_failed_distribution_stage_cannot_qualify(self):
        report = complete(); report["stages"].pop()
        with self.assertRaises(RuntimeError): PROBE.validate_complete(report)
        report = complete(); report["stages"][0]["status"] = "failed"
        with self.assertRaises(RuntimeError): PROBE.validate_complete(report)

    def test_final_commit_proof_requires_actual_committed_source_archive(self):
        report = complete(); report["preliminary"] = False
        with self.assertRaises(RuntimeError): PROBE.validate_complete(report)
        report["stages"].append({"id": "source-archive", "status": "passed"})
        PROBE.validate_complete(report)

    def test_owned_host_leak_or_failed_shutdown_cannot_qualify(self):
        report = complete(); report["hostProcesses"][0]["status"] = "ready"
        with self.assertRaises(RuntimeError): PROBE.validate_complete(report)
        report = complete(); report["hostProcesses"][0]["exitCode"] = -9
        with self.assertRaises(RuntimeError): PROBE.validate_complete(report)

    def test_project_reference_frontend_call_or_changed_common_bytes_cannot_qualify(self):
        for mutate in (lambda report: report["consumer"].update(packageOnly=False),
                       lambda report: report["consumer"].update(frontendInvocations=1),
                       lambda report: report.update(nativeProfileCommonBytesUnchanged=False)):
            report = complete(); mutate(report)
            with self.assertRaises(RuntimeError): PROBE.validate_complete(report)

    def test_failed_oracle_is_retained_as_failed_rather_than_passed_http(self):
        with tempfile.TemporaryDirectory() as directory:
            probe = object.__new__(PROBE.Probe); probe.root = Path(directory); probe.report = {"cases": []}
            def wrong_number(): PROBE.require(False, "Native HTTP succeeded but the numerical oracle differs")
            with self.assertRaises(RuntimeError): probe.case("author-numerical-oracle", wrong_number)
            self.assertEqual("failed", probe.report["cases"][0]["status"])
            self.assertIn("numerical oracle", probe.report["cases"][0]["cause"])


if __name__ == "__main__": unittest.main()
