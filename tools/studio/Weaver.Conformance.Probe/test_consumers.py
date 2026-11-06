"""Runtime package closure checks use retained synthetic archives, never host starts."""
import copy
import hashlib
import json
from pathlib import Path
import secrets
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile

from artifacts import BACKEND_PACKAGES
from consumers import (COMMON_EFFECTS_FEATURE, DURABLE_EFFECTS_FEATURE, DURABLE_STORE_FEATURE, backend_features,
                       configure_agent_privileges, configure_runtime, enable_durable_agent_store, references,
                       select_native_archives, stage_native_archives)

DI = "Microsoft.Extensions.DependencyInjection.Abstractions"


class RuntimeClosureTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        feed = self.root / "runtime-feed"; feed.mkdir()
        self.run = SimpleNamespace(root=self.root, source="a" * 40,
            profiles={"backend": {"runtimeFeed": feed, "hostProvidedPackages": {"Provider.Adapter": "4.0.0"},
                                   "frameworkSharedAssemblies": [DI]}})
        self.root_archive = self.archive("Fixture.Protocol", "1.0.0", {"net10.0": {"Provider.Adapter": "4.0.0", DI: "10.0.10"}})
        self.di_archive = self.archive(DI, "10.0.10", {"net10.0": {}, ".NETStandard2.0": {"Unused.Legacy": "1.0.0"}})
        self.graph = {"libraries": {"Provider.Adapter/4.0.0": {"type": "package"}, DI + "/10.0.10": {"type": "package"}},
                      "targets": {"net10.0": {"Provider.Adapter/4.0.0": {}, DI + "/10.0.10": {}}}}
        self.assets = self.root / "project.assets.json"
        self.author = {"source": self.run.source, "status": "passed", "cachedArchives": [
            {"identity": DI + "/10.0.10", "archive": self.di_archive}]}
        self.save_graph()

    def tearDown(self):
        self.temporary.cleanup()

    def ref(self, path):
        return {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}

    def archive(self, package_id, version, groups):
        path = self.root / (package_id + "." + version + ".nupkg")
        document = ET.Element("package"); metadata = ET.SubElement(document, "metadata")
        ET.SubElement(metadata, "id").text = package_id; ET.SubElement(metadata, "version").text = version
        dependencies = ET.SubElement(metadata, "dependencies")
        for framework, items in groups.items():
            group = ET.SubElement(dependencies, "group", targetFramework=framework)
            for name, requested in items.items(): ET.SubElement(group, "dependency", id=name, version=requested)
        with zipfile.ZipFile(path, "w") as archive: archive.writestr(package_id + ".nuspec", ET.tostring(document))
        return dict(self.ref(path), identity=package_id + "/" + version)

    def save_graph(self):
        self.assets.write_text(json.dumps(self.graph)); self.author["assets"] = self.ref(self.assets)

    def select(self):
        return select_native_archives(self.run, "backend", [self.root_archive], self.author)

    def test_runtime_configuration_keeps_the_private_artifact_key_stable_and_enrolls_only_studio(self):
        backend, studio = self.root / "backend", self.root / "studio"
        for directory in (backend, studio):
            directory.mkdir()
            (directory / "appsettings.json").write_text(json.dumps({"Qualification": {}, "Studio": {}}))
        (backend / "shells.json").write_text(json.dumps({"CShells": {"Shells": {"Authoring": {"Features": backend_features(backend)}}}}))
        (studio / "shells.json").write_text("{}")
        run = SimpleNamespace(root=self.root, profiles={
            "backend": {"published": backend, "commandFile": backend / "command.json",
                        "traceFiles": {"copilot": backend / "copilot.jsonl", "anthropic": backend / "anthropic.jsonl"}},
            "studio": {"published": studio}})
        options = dict(backend_url="http://127.0.0.1:18001", studio_url="http://127.0.0.1:18002",
                       admin_username="private-fixture", admin_password=secrets.token_urlsafe(24) + "!aA1", enroll=True)
        configure_runtime(run, **options)
        def signing_key():
            return json.loads((backend / "shells.json").read_text())["CShells"]["Shells"]["Authoring"]["Features"]["WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence"]["RecoveryContinuationSigningKey"]
        first = signing_key()
        self.assertTrue(len(first.encode()) >= 32)
        configure_runtime(run, **options, provider="anthropic")
        self.assertTrue(first == signing_key())  # Never put private key material in an assertion message.
        self.assertNotIn("EnrollOwnedStore", json.loads((backend / "appsettings.json").read_text())["Qualification"])
        self.assertEqual({"Administrative": True, "Irrecoverable": True, "Activation": True},
                         json.loads((backend / "appsettings.json").read_text())["Elsa"]["Agent"]["Privileges"])
        self.assertIs(True, json.loads((studio / "appsettings.json").read_text())["Qualification"]["EnrollOwnedStore"])
        for directory in (backend, studio):
            for name in ("appsettings.json", "shells.json"):
                self.assertEqual(0, (directory / name).stat().st_mode & 0o077)

    def test_framework_assembly_sharing_does_not_satisfy_the_nuget_dependency(self):
        before = copy.deepcopy(self.author)
        result = self.select()
        self.assertEqual(["Fixture.Protocol/1.0.0", DI + "/10.0.10"], [item["identity"] for item in result])
        self.assertEqual(before, self.author)
        self.assertNotIn("runtimeArchiveInputs", self.run.profiles["backend"])
        self.assertEqual([], list(self.run.profiles["backend"]["runtimeFeed"].iterdir()))

    def test_exact_host_provided_package_stops_dependency_traversal(self):
        self.run.profiles["backend"]["hostProvidedPackages"][DI] = "10.0.10"
        self.author["cachedArchives"] = []
        self.assertEqual(["Fixture.Protocol/1.0.0"], [item["identity"] for item in self.select()])

    def test_genuine_archive_staging_is_explicit_hash_checked_and_idempotent(self):
        inputs = self.select()
        result = stage_native_archives(self.run, "backend", inputs)
        self.assertEqual(2, len(result)); self.assertEqual(result, stage_native_archives(self.run, "backend", inputs))
        for item in result:
            self.assertEqual(item["sha256"], self.ref(Path(item["path"]))["sha256"])

    def test_missing_retained_archive_does_not_trigger_cache_discovery(self):
        self.author["cachedArchives"] = []  # Archive still exists, but is not a declared input.
        with self.assertRaisesRegex(RuntimeError, "explicitly retained"): self.select()

    def test_wrong_archive_hash_or_claimed_identity_is_refused(self):
        for change in (lambda entry: entry["archive"].update(sha256="0" * 64),
                       lambda entry: entry.update(identity=DI + "/99.0.0")):
            with self.subTest(change=change):
                original = copy.deepcopy(self.author["cachedArchives"])
                change(self.author["cachedArchives"][0])
                with self.assertRaises(RuntimeError): self.select()
                self.author["cachedArchives"] = original

    def test_host_provided_version_mismatch_is_not_silently_filled_from_feed(self):
        self.run.profiles["backend"]["hostProvidedPackages"][DI] = "10.0.9"
        with self.assertRaisesRegex(RuntimeError, "version differs"): self.select()

    def test_author_source_or_assets_hash_mismatch_is_refused(self):
        for field, value in (("source", "b" * 40), ("status", "pending")):
            with self.subTest(field=field):
                original = self.author[field]; self.author[field] = value
                with self.assertRaises(RuntimeError): self.select()
                self.author[field] = original
        self.author["assets"]["sha256"] = "0" * 64
        with self.assertRaisesRegex(RuntimeError, "assets"): self.select()

    def test_nuspec_edges_must_match_actual_selected_target(self):
        self.graph["targets"]["net10.0"][DI + "/10.0.10"]["dependencies"] = {"Undeclared": "1.0.0"}
        self.save_graph()
        with self.assertRaisesRegex(RuntimeError, "Nuspec dependency IDs"): self.select()

    def test_unselected_framework_dependencies_are_not_staged(self):
        self.assertEqual(2, len(self.select()))  # netstandard-only Unused.Legacy has no retained archive.

    def test_fallback_framework_uses_actual_locked_target_instead_of_other_groups(self):
        self.di_archive = self.archive(DI, "10.0.10", {".NETStandard2.0": {"Current.Leaf": "1.0.0"},
                                                        ".NETFramework4.6.2": {"Unselected.Leaf": "2.0.0"}})
        leaf = self.archive("Current.Leaf", "1.0.0", {"net10.0": {}})
        self.author["cachedArchives"] = [{"identity": DI + "/10.0.10", "archive": self.di_archive},
                                          {"identity": "Current.Leaf/1.0.0", "archive": leaf}]
        self.graph["libraries"]["Current.Leaf/1.0.0"] = {"type": "package"}
        self.graph["targets"]["net10.0"]["Current.Leaf/1.0.0"] = {}
        self.graph["targets"]["net10.0"][DI + "/10.0.10"]["dependencies"] = {"Current.Leaf": "1.0.0"}
        self.save_graph()
        self.assertEqual(["Fixture.Protocol/1.0.0", DI + "/10.0.10", "Current.Leaf/1.0.0"], [item["identity"] for item in self.select()])

    def test_conflicting_root_archive_bytes_are_refused(self):
        alternate = self.root / "alternate.nupkg"
        alternate.write_bytes(Path(self.root_archive["path"]).read_bytes())
        with zipfile.ZipFile(alternate, "a") as archive: archive.writestr("different-bytes", "retained")
        with self.assertRaisesRegex(RuntimeError, "Conflicting selected"):
            select_native_archives(self.run, "backend", [self.root_archive, self.ref(alternate)], self.author)

    def test_dependency_must_exist_in_selected_net10_target(self):
        del self.graph["targets"]["net10.0"][DI + "/10.0.10"]; self.save_graph()
        with self.assertRaisesRegex(RuntimeError, "selected net10.0 target"): self.select()


class AgentConsumerConfigurationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.backend, self.studio = self.root / "backend", self.root / "studio"
        self.backend.mkdir(); self.studio.mkdir()
        self.settings = {"Qualification": {"StudioOrigin": "http://127.0.0.1:18002"},
                         "Elsa": {"Agent": {"RetainOption": True}}}
        self.path = self.backend / "appsettings.json"
        self.path.write_text(json.dumps(self.settings))
        self.studio_settings = self.studio / "appsettings.json"
        self.studio_settings.write_text("{}")
        self.run = SimpleNamespace(root=self.root, profiles={"backend": {"published": self.backend},
                                                           "studio": {"published": self.studio}})

    def test_ordinary_consumer_selects_only_memory_effects(self):
        features = backend_features(self.backend)
        self.assertIn(COMMON_EFFECTS_FEATURE, features)
        self.assertNotIn(DURABLE_EFFECTS_FEATURE, features)
        self.assertNotIn(DURABLE_STORE_FEATURE, features)
        self.assertIn("FoundationAgentApi", features)
        self.assertIn("FoundationWorkflowsAgent", features)

    def test_attention_composition_requires_its_exact_product_candidate(self):
        products = {name: {"version": "4.0.0-fixture.1"} for name in BACKEND_PACKAGES}
        run = SimpleNamespace(packages=products)
        document = ET.fromstring("<Project>" + references(run, BACKEND_PACKAGES, ()) + "</Project>")
        attention = [reference for reference in document if reference.attrib["Include"] == "Elsa.Attention.Api"]
        self.assertEqual(1, len(attention))
        self.assertEqual("[4.0.0-fixture.1]", attention[0].attrib["Version"])
        features = backend_features(self.backend)
        self.assertTrue({"ApiCapabilities", "ModularityApi", "AttentionApi", "ModularityAttention"}.issubset(features))
        self.assertTrue(all(features[name] == {} for name in ("AttentionApi", "ModularityAttention")))
        del products["Elsa.Attention.Api"]
        with self.assertRaisesRegex(RuntimeError, "Missing genuine consumer candidate: Elsa.Attention.Api"):
            references(run, BACKEND_PACKAGES, ())

    def test_runtime_configuration_and_policy_target_fresh_content_without_editing_publishes(self):
        content = self.root / "fresh-runtime-content"
        content.mkdir(mode=0o700)
        settings = dict(self.settings, Qualification={"StudioOrigin": "http://127.0.0.1:18002"})
        (content / "appsettings.json").write_text(json.dumps(settings))
        (content / "shells.json").write_text(json.dumps({"CShells": {"Shells": {"Authoring": {"Features": backend_features(content)}}}}))
        protocol = content / "protocol"
        protocol.mkdir()
        profile = self.run.profiles["backend"]
        profile.update(contentRoot=content, commandFile=protocol / "command.json",
                       traceFiles={"copilot": protocol / "copilot.jsonl", "anthropic": protocol / "anthropic.jsonl"})
        (self.studio / "shells.json").write_text("{}")
        (self.studio / "appsettings.json").write_text(json.dumps({"Studio": {}, "Qualification": {}}))
        before = {path.name: path.read_bytes() for path in self.backend.iterdir()}
        configure_runtime(self.run, backend_url="http://127.0.0.1:18001", studio_url="http://127.0.0.1:18002",
                          admin_username="private-fixture", admin_password=secrets.token_urlsafe(24) + "!aA1")
        configure_agent_privileges(self.run, administrative=False, irrecoverable=True, activation=False)
        self.assertTrue(before == {path.name: path.read_bytes() for path in self.backend.iterdir()})
        observed = json.loads((content / "appsettings.json").read_text())
        self.assertEqual({"Administrative": False, "Irrecoverable": True, "Activation": False}, observed["Elsa"]["Agent"]["Privileges"])
        features = json.loads((content / "shells.json").read_text())["CShells"]["Shells"]["Authoring"]["Features"]
        self.assertEqual(str(protocol / "command.json"), features["AcmeCopilotProtocolFixture"]["CommandFile"])
        selected = enable_durable_agent_store(self.run, content / "agent.db", secrets.token_urlsafe(48))
        features = json.loads((content / "shells.json").read_text())["CShells"]["Shells"]["Authoring"]["Features"]
        self.assertEqual(str(content / "agent.db"), selected["database"])
        self.assertIn(DURABLE_EFFECTS_FEATURE, features)
        self.assertNotIn(COMMON_EFFECTS_FEATURE, features)
        self.assertTrue(before == {path.name: path.read_bytes() for path in self.backend.iterdir()})
        for name in ("appsettings.json", "shells.json"):
            self.assertEqual(0, (content / name).stat().st_mode & 0o077)

    def test_separate_content_configuration_symlink_cannot_rewrite_a_binary_publish(self):
        content = self.root / "fresh-runtime-content"
        content.mkdir()
        (content / "appsettings.json").symlink_to(self.path)
        self.run.profiles["backend"]["contentRoot"] = content
        before = self.path.read_bytes()
        with self.assertRaisesRegex(RuntimeError, "ordinary file"):
            configure_agent_privileges(self.run, administrative=True, irrecoverable=True, activation=True)
        self.assertEqual(before, self.path.read_bytes())

    def test_private_policy_profiles_preserve_other_configuration_and_studio(self):
        private_value = secrets.token_urlsafe(48)
        self.settings["PrivateFixtureValue"] = private_value
        self.path.write_text(json.dumps(self.settings))
        for enabled in ((False, False, False), (True, False, False), (True, True, True)):
            with self.subTest(enabled=enabled):
                selected = configure_agent_privileges(self.run, administrative=enabled[0], irrecoverable=enabled[1], activation=enabled[2])
                settings = json.loads(self.path.read_text())
                self.assertEqual(dict(zip(("Administrative", "Irrecoverable", "Activation"), enabled)), selected)
                self.assertEqual(selected, settings["Elsa"]["Agent"].pop("Privileges"))
                self.assertTrue(settings == self.settings)  # Do not include the private value in a failed assertion message.
                self.assertTrue(private_value not in json.dumps(selected))
                self.assertEqual("{}", self.studio_settings.read_text())
                self.assertEqual(0, self.path.stat().st_mode & 0o077)
                self.assertEqual([], list(self.backend.glob(".agent-privileges-*")))

    def test_invalid_privilege_values_leave_the_original_file_unchanged(self):
        before = self.path.read_bytes()
        for invalid in (None, 0, 1, "true", [], {}):
            for member in ("administrative", "irrecoverable", "activation"):
                with self.subTest(member=member, invalid=invalid):
                    options = dict(administrative=True, irrecoverable=True, activation=True)
                    options[member] = invalid
                    with self.assertRaisesRegex(RuntimeError, "explicit boolean"):
                        configure_agent_privileges(self.run, **options)
                    self.assertEqual(before, self.path.read_bytes())

    def test_failed_atomic_replacement_preserves_the_original_and_removes_private_temporary_file(self):
        before = self.path.read_bytes()
        with patch.object(Path, "replace", side_effect=OSError("fixture replacement failure")):
            with self.assertRaises(OSError):
                configure_agent_privileges(self.run, administrative=True, irrecoverable=False, activation=False)
        self.assertEqual(before, self.path.read_bytes())
        self.assertEqual([], list(self.backend.glob(".agent-privileges-*")))

    def test_policy_writer_refuses_foreign_and_symlinked_configuration_without_writing(self):
        outside = tempfile.TemporaryDirectory()
        self.addCleanup(outside.cleanup)
        foreign = Path(outside.name).resolve()
        foreign_path = foreign / "appsettings.json"
        foreign_path.write_text("{}")
        self.run.profiles["backend"]["published"] = foreign
        with self.assertRaisesRegex(RuntimeError, "owned"):
            configure_agent_privileges(self.run, administrative=True, irrecoverable=True, activation=True)
        self.assertEqual("{}", foreign_path.read_text())
        self.run.profiles["backend"]["published"] = self.backend
        self.path.unlink()
        self.path.symlink_to(foreign_path)
        with self.assertRaisesRegex(RuntimeError, "owned backend"):
            configure_agent_privileges(self.run, administrative=True, irrecoverable=True, activation=True)
        self.assertTrue(self.path.is_symlink())
        self.assertEqual("{}", foreign_path.read_text())


if __name__ == "__main__":
    unittest.main()
