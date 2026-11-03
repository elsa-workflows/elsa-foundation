"""Exercise actual author templates; these checks do not claim package/runtime proof."""
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

from artifacts import ConformanceRun, sha
from authors import build_authors


class MaterializationTests(unittest.TestCase):
    def test_commit_derived_author_version_has_no_zero_prefixed_numeric_identifier(self):
        for prefix in ("02848461", "00000000", "12345678", "deadbeef"):
            with self.subTest(prefix=prefix), tempfile.TemporaryDirectory() as directory:
                repo, run = self.real_pin_run(directory, ["10.0.12"], ["10.0.8"])
                run.source = prefix + "a" * 32
                run.sdk.write_bytes(b"synthetic SDK identity for template-only testing")
                run.packages = {"Elsa.Test": {"version": run.version}}
                run.report = {"producer": {"source": run.source, "sdk": {"sha256": sha(run.sdk)}}}
                run.save = lambda: None
                # Inspect the actual generated author before any restore/build/network work.
                with patch("artifacts.REPO", repo), patch("authors._configure", side_effect=RuntimeError("template boundary")):
                    with self.assertRaisesRegex(RuntimeError, "template boundary"):
                        build_authors(run)
                project = run.root / "author-protocol/Acme.Weaver.ProtocolFixtures.csproj"
                version = next(ET.parse(project).iter("Version")).text
                self.assertIn(prefix, version)
                prerelease = version.split("-", 1)[1]
                for identifier in prerelease.split("."):
                    self.assertRegex(identifier, r"^[0-9A-Za-z-]+$")
                    self.assertFalse(identifier.isdigit() and len(identifier) > 1 and identifier.startswith("0"), version)

    @staticmethod
    def real_pin_run(directory, backend_di, studio_di):
        root = Path(directory)
        repo = root / "repo"
        (repo / "Studio").mkdir(parents=True)
        common = '<PackageVersion Include="CShells.Abstractions" Version="1.0.0"/><PackageVersion Include="Microsoft.Extensions.AI.Abstractions" Version="10.10.1"/>'
        def declaration(versions):
            return ''.join(f'<PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="{version}"/>' for version in versions)
        (repo / "Directory.Packages.props").write_text('<Project><ItemGroup>' + common + declaration(backend_di) + '</ItemGroup></Project>')
        (repo / "Studio/Directory.Packages.props").write_text('<Project><ItemGroup>' + declaration(studio_di) + '</ItemGroup></Project>')
        run = ConformanceRun.__new__(ConformanceRun)
        run.root = root / "output"
        run.root.mkdir()
        run.version = "4.0.0-witness"
        run.sdk = root / "actual-sdk.tgz"
        run.config = lambda source: None
        run.assert_immutable_inputs = lambda: None  # Exercise real pin/template paths without building packages.
        return repo, run

    def test_protocol_author_selects_backend_pin_when_studio_has_a_different_version(self):
        with tempfile.TemporaryDirectory() as directory:
            repo, run = self.real_pin_run(directory, ["10.0.12"], ["10.0.8"])
            with patch("artifacts.REPO", repo):
                author = run.materialize("protocol", "ProviderProtocolFixtures")
            project = (author / "Acme.Weaver.ProtocolFixtures.csproj").read_text()
            self.assertIn('Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.12"', project)

    def test_protocol_author_refuses_missing_or_ambiguous_backend_pin(self):
        for versions in ([], ["10.0.12", "10.0.13"]):
            with self.subTest(versions=versions), tempfile.TemporaryDirectory() as directory:
                repo, run = self.real_pin_run(directory, versions, ["10.0.8"])
                with patch("artifacts.REPO", repo), self.assertRaisesRegex(RuntimeError, "Missing/ambiguous central pin"):
                    run.materialize("protocol", "ProviderProtocolFixtures")

    def test_unscoped_pin_keeps_rejecting_conflicting_central_files(self):
        with tempfile.TemporaryDirectory() as directory:
            repo, run = self.real_pin_run(directory, ["10.0.12"], ["10.0.8"])
            with patch("artifacts.REPO", repo), self.assertRaisesRegex(RuntimeError, "Missing/ambiguous central pin"):
                run.pin("Microsoft.Extensions.DependencyInjection.Abstractions")

    def test_protocol_dependencies_follow_selected_product_pins(self):
        with tempfile.TemporaryDirectory() as directory:
            run = ConformanceRun.__new__(ConformanceRun)
            run.root = Path(directory)
            run.version = "4.0.0-witness"
            run.sdk = run.root / "actual-sdk.tgz"
            pins = {"Microsoft.Extensions.AI.Abstractions": "10.10.1", "Microsoft.Extensions.DependencyInjection.Abstractions": "10.0.12"}
            run.pin = lambda name, **_: pins.get(name, "1.0.0")
            run.config = lambda source: None
            run.assert_immutable_inputs = lambda: None
            author = run.materialize("protocol", "ProviderProtocolFixtures")
            project = (author / "Acme.Weaver.ProtocolFixtures.csproj").read_text()
            self.assertIn('Include="Microsoft.Extensions.AI.Abstractions" Version="10.10.1"', project)
            self.assertIn('Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.12"', project)
            self.assertNotIn("__DI_ABSTRACTIONS_VERSION__", project)

    def test_studio_author_has_explicit_admission_input_and_can_build_a_real_incompatible_variant(self):
        with tempfile.TemporaryDirectory() as directory:
            run = ConformanceRun.__new__(ConformanceRun)
            run.root = Path(directory)
            run.version = "4.0.0-witness"
            run.sdk = run.root / "actual-sdk.tgz"
            run.pin = lambda name, **_: "1.0.0"
            run.config = lambda source: None
            run.assert_immutable_inputs = lambda: None  # This unit only checks template materialization.
            author = run.materialize("author", "Resource.Studio")
            self.assertIn('RequiredSdkVersion = "*"', (author / "AcmeHomeStudioFeature.cs").read_text())
            incompatible = run.materialize("incompatible", "Resource.Studio", {"__REQUIRED_SDK_VERSION__": "999.0.0"})
            self.assertIn('RequiredSdkVersion = "999.0.0"', (incompatible / "AcmeHomeStudioFeature.cs").read_text())
            self.assertEqual((author / "Client/src/module.tsx").read_bytes(), (incompatible / "Client/src/module.tsx").read_bytes())


if __name__ == "__main__":
    unittest.main()
