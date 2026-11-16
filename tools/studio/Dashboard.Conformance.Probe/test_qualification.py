"""Evidence-gate controls; these do not replace the real package/native witness."""
from copy import deepcopy
import importlib.util
from pathlib import Path
import tempfile
import io
import subprocess
import tarfile
import unittest
from unittest import mock

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
    def test_public_receipt_never_contains_private_profiles_commands_or_response_payloads(self):
        import json
        report = complete()
        report.update(source="a" * 40, profiles={"token": "DO-NOT-DISCLOSE"}, ownedRoot="DO-NOT-DISCLOSE")
        report["cases"][0]["response"] = {"token": "DO-NOT-DISCLOSE"}
        report["stages"][0]["command"] = ["DO-NOT-DISCLOSE"]
        report["hostProcesses"][0]["log"] = "DO-NOT-DISCLOSE"
        report["packages"] = {"A": {"version": "4.0.0-probe", "sha256": "b" * 64, "path": "DO-NOT-DISCLOSE"}}
        receipt = PROBE.public_receipt(report)
        self.assertNotIn("DO-NOT-DISCLOSE", json.dumps(receipt))
        self.assertEqual(report["source"], receipt["source"])
        self.assertEqual(report["cases"][0]["status"], receipt["cases"][0]["status"])

    def test_consumer_config_uses_the_exact_committed_filename_on_case_sensitive_filesystems(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            config = root / "NuGet.config"
            config.write_text('<configuration><packageSources><add key="public" value="https://example.invalid/feed" /></packageSources></configuration>')
            subprocess.run(["git", "init", "-q", str(root)], check=True, capture_output=True)
            subprocess.run(["git", "add", "."], cwd=root, check=True, capture_output=True)
            subprocess.run(["git", "-c", "user.name=Dashboard test", "-c", "user.email=dashboard@example.invalid",
                            "commit", "-qm", "Owned config"], cwd=root, check=True, capture_output=True)
            probe = object.__new__(PROBE.Probe); probe.root = root; probe.feed = root / "feed"; probe.packages = {"Acme.Processing": {}}; probe.source = "HEAD"; probe.report = {"preliminary": True}
            with mock.patch.object(PROBE, "REPO", root), mock.patch.object(PROBE.ET, "parse", wraps=PROBE.ET.parse) as parse:
                probe.config(root / "consumer.config")
                # Exact spelling is asserted even on case-insensitive macOS; Linux cannot open an alias.
                self.assertEqual(config.name, Path(parse.call_args.args[0]).name)
            tree = PROBE.ET.parse(root / "consumer.config")
            self.assertEqual("https://example.invalid/feed", tree.find("./packageSources/add[@key='public']").attrib["value"])

    def test_checkout_edits_after_archive_cannot_change_final_consumer_inputs(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); repo = root / "repo"; repo.mkdir(); owned = root / "witness"; owned.mkdir()
            (repo / "NuGet.config").write_text('<configuration><packageSources><add key="public" value="https://frozen.invalid/feed" /></packageSources></configuration>')
            (repo / "Directory.Packages.props").write_text('<Project><ItemGroup><PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.1.0" /></ItemGroup></Project>')
            (repo / "global.json").write_text('{}')
            fixtures = repo / "tools/studio/Dashboard.Conformance.Probe/fixtures"; fixtures.mkdir(parents=True)
            (fixtures / "Consumer.csproj.in").write_text('<Project>__PACKAGE_REFERENCES__</Project>')
            (fixtures / "Program.cs").write_text('// frozen fixture')
            subprocess.run(["git", "init", "-q", str(repo)], check=True, capture_output=True)
            subprocess.run(["git", "add", "."], cwd=repo, check=True, capture_output=True)
            subprocess.run(["git", "-c", "user.name=Dashboard test", "-c", "user.email=dashboard@example.invalid", "commit", "-qm", "Frozen inputs"], cwd=repo, check=True, capture_output=True)
            producer = owned / "producer"; producer.mkdir()
            archive = subprocess.check_output(["git", "archive", "--format=tar", "HEAD"], cwd=repo)
            with tarfile.open(fileobj=io.BytesIO(archive)) as frozen: frozen.extractall(producer)
            (repo / "NuGet.config").write_text('<configuration><packageSources><add key="public" value="https://changed.invalid/feed" /></packageSources></configuration>')
            (repo / "Directory.Packages.props").write_text('<Project><ItemGroup><PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="99.0.0" /></ItemGroup></Project>')
            probe = object.__new__(PROBE.Probe); probe.root = owned; probe.feed = owned / "feed"; probe.packages = {"Acme.Processing": {}}
            probe.source = "HEAD"; probe.version = "4.0.0-probe"; probe.dotnet = "dotnet"; probe.report = {"preliminary": False}
            class Prepared(Exception): pass
            probe.run = lambda *args: (_ for _ in ()).throw(Prepared())
            with mock.patch.object(PROBE, "REPO", repo), self.assertRaises(Prepared): probe.consumer()
            project = (owned / "consumer/Consumer.csproj").read_text()
            self.assertIn('Include="Microsoft.EntityFrameworkCore.Sqlite" Version="[9.1.0]"', project)
            self.assertNotIn("99.0.0", project)
            config = PROBE.ET.parse(owned / "consumer/NuGet.online.config")
            self.assertEqual("https://frozen.invalid/feed", config.find("./packageSources/add[@key='public']").attrib["value"])

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
