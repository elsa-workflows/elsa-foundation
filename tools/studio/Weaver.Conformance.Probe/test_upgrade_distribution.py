"""Authored T038 prerequisite controls; source checkpoint only, all UNRUN.

These controls exercise owned configuration and fail-closed inputs, not native
upgrade semantics. Passing them could never promote an independent witness.
"""
import copy
import json
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import patch

from consumers import (COMMON_EFFECTS_FEATURE, DURABLE_EFFECTS_FEATURE, DURABLE_STORE_FEATURE, backend_features,
                       consumer_binary_provenance)
from upgrade_distribution import (BASELINE_SOURCE, JOURNAL_HEADER, RESOURCE_FEATURE, prepare_upgrade_content_root,
                                  prepare_upgrade_distributions, select_upgrade_generation)


class UpgradeConfigurationTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        self.root.chmod(0o700)
        self.prepared = {}
        for name in ("old", "current"):
            binary = self.root / (name + "-published")
            binary.mkdir()
            settings = {"ConnectionStrings": {"Tokens": "Data Source=" + str(binary / "state/tokens.db")},
                        "Nuplane": {"HostProvidedPackages": []}, "Qualification": {"StudioOrigin": ""}}
            (binary / "appsettings.json").write_text(json.dumps(settings))
            (binary / "shells.json").write_text(json.dumps({"CShells": {"Shells": {"Authoring": {"Features": backend_features(binary, effects=False)}}}}))
            self.prepared[name] = {"profile": {"published": binary, "runtimeArchiveInputs": [],
                "hostProvidedPackages": {"Elsa.Weaver.Core": name + "-version"}, "publishedFiles": {"test": name},
                "productSource": {"commit": BASELINE_SOURCE if name == "old" else "a" * 40}}}
        self.original = {name: {file: (item["profile"]["published"] / file).read_bytes()
                               for file in ("appsettings.json", "shells.json")} for name, item in self.prepared.items()}
        self.previous = {"published": self.root / "ordinary-backend"}
        self.previous["published"].mkdir()
        self.run = SimpleNamespace(root=self.root, profiles={"backend": self.previous})
        self.content = self.root / "upgrade-content"

    def prepare(self):
        return prepare_upgrade_content_root(self.run, self.prepared, self.content, encryption_key="x" * 48, studio_host_id="upgrade-host")

    def features(self):
        return json.loads((self.content / "shells.json").read_text())["CShells"]["Shells"]["Authoring"]["Features"]

    def manifest(self, path):
        return {"test": "old" if path.name.startswith("old") else "current"}

    def test_legacy_overlay_has_no_effects_feature_and_preserves_ordinary_selection(self):
        result = self.prepare()
        self.assertIs(self.previous, self.run.profiles["backend"])
        self.assertIn(DURABLE_STORE_FEATURE, self.features())
        self.assertNotIn(COMMON_EFFECTS_FEATURE, self.features())
        self.assertNotIn(DURABLE_EFFECTS_FEATURE, self.features())
        self.assertIsNone(result["effectsFeature"])
        self.assertFalse(Path(result["database"]).exists())

    def test_shared_runtime_paths_are_rebased_without_changing_any_published_config(self):
        self.prepare()
        settings = json.loads((self.content / "appsettings.json").read_text())
        self.assertEqual("Data Source=" + str(self.content / "state/tokens.db"), settings["ConnectionStrings"]["Tokens"])
        for name, item in self.prepared.items():
            self.assertEqual(self.content, item["profile"]["contentRoot"])
            for file, value in self.original[name].items():
                self.assertEqual(value, (item["profile"]["published"] / file).read_bytes())
        for feature in ("FoundationIdentityAspNetCoreIdentityEntityFrameworkCore", "StudioPreferencesEntityFrameworkCore"):
            self.assertTrue(self.features()[feature]["ConnectionString"].startswith("Data Source=" + str(self.content / "state/")))

    def test_observer_is_explicit_private_and_preprovisioned(self):
        result = self.prepare()
        journal = Path(result["executionObservationFile"])
        self.assertEqual(JOURNAL_HEADER + "\n", journal.read_text())
        self.assertEqual(0, journal.stat().st_mode & 0o077)
        self.assertEqual(str(journal), self.features()[RESOURCE_FEATURE]["ExecutionObservationFile"])
        self.assertEqual("upgrade-host", self.features()[RESOURCE_FEATURE]["StudioHostId"])

    def test_existing_root_and_binary_overlap_are_refused(self):
        self.content.mkdir()
        with self.assertRaisesRegex(RuntimeError, "new owned"):
            self.prepare()
        self.content.rmdir()
        self.content = self.prepared["old"]["profile"]["published"] / "content"
        with self.assertRaisesRegex(RuntimeError, "overlap"):
            self.prepare()
        self.assertFalse(self.content.exists())

    def test_symlink_ancestor_is_refused_before_writing(self):
        real = self.root / "real"; real.mkdir()
        alias = self.root / "alias"; alias.symlink_to(real, target_is_directory=True)
        self.content = alias / "content"
        with self.assertRaisesRegex(RuntimeError, "symlink"):
            self.prepare()
        self.assertFalse((real / "content").exists())

    def test_invalid_key_is_refused_before_creating_runtime_root(self):
        with self.assertRaisesRegex(RuntimeError, "private key"):
            prepare_upgrade_content_root(self.run, self.prepared, self.content, encryption_key="short", studio_host_id="upgrade-host")
        self.assertFalse(self.content.exists())

    def test_current_generation_selects_effects_only_when_requested(self):
        self.prepare()
        with patch("runtime.published_files", side_effect=self.manifest, create=True):
            old = select_upgrade_generation(self.run, self.prepared, "old")
            self.assertFalse(old["effectsSelected"])
            current = select_upgrade_generation(self.run, self.prepared, "current", effects=True)
        self.assertFalse(current["qualified"])
        self.assertFalse(current["runtimeObserved"])
        self.assertIn(DURABLE_EFFECTS_FEATURE, self.features())
        self.assertEqual(["Elsa.Weaver.Core"], json.loads((self.content / "appsettings.json").read_text())["Nuplane"]["HostProvidedPackages"])
        self.assertEqual(DURABLE_EFFECTS_FEATURE, self.run.profiles["backend"]["durableWeaverStore"]["effectsFeature"])

    def test_old_generation_cannot_enable_effects(self):
        self.prepare()
        with self.assertRaisesRegex(RuntimeError, "cannot select effects"):
            select_upgrade_generation(self.run, self.prepared, "old", effects=True)

    def test_unknown_feed_input_is_refused_and_retained(self):
        self.prepare()
        unexpected = self.content / "runtime-feed/unowned.nupkg"; unexpected.write_bytes(b"undeclared")
        with patch("runtime.published_files", side_effect=self.manifest, create=True):
            with self.assertRaisesRegex(RuntimeError, "undeclared"):
                select_upgrade_generation(self.run, self.prepared, "old")
        self.assertEqual(b"undeclared", unexpected.read_bytes())

    def test_manifest_mismatch_is_refused_before_feed_or_config_changes(self):
        self.prepare()
        before = (self.content / "shells.json").read_bytes()
        with patch("runtime.published_files", return_value={"changed": True}, create=True):
            with self.assertRaisesRegex(RuntimeError, "published inputs changed"):
                select_upgrade_generation(self.run, self.prepared, "old")
        self.assertEqual(before, (self.content / "shells.json").read_bytes())

    def test_distribution_requires_genuine_current_producer_before_any_preparation(self):
        run = SimpleNamespace(assert_immutable_inputs=lambda: None, packages={"current": {}}, source="a" * 40, report={})
        before = copy.deepcopy(run.packages)
        with self.assertRaisesRegex(RuntimeError, "genuine committed current"):
            prepare_upgrade_distributions(run)
        self.assertEqual(before, run.packages)

    def test_original_default_feature_selection_remains_memory_effects(self):
        self.assertIn(COMMON_EFFECTS_FEATURE, backend_features(self.root))
        self.assertNotIn(COMMON_EFFECTS_FEATURE, backend_features(self.root, effects=False))


class PublishedDependencyProvenanceTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        self.published = self.root / "published"; self.published.mkdir()
        self.cache = self.root / "cache"; self.cache.mkdir()
        self.identity = "Fixture.Dependency/1.0.0"
        restored = self.cache / "fixture.dependency/1.0.0/lib/net10.0/Dependency.dll"
        restored.parent.mkdir(parents=True); restored.write_bytes(b"actual-archive-asset")
        (self.published / "Dependency.dll").write_bytes(restored.read_bytes())
        self.assets = self.root / "project.assets.json"
        self.assets.write_text(json.dumps({"libraries": {self.identity: {"type": "package", "path": "fixture.dependency/1.0.0"}}}))
        self.deps = {"runtimeTarget": {"name": "net10.0"}, "targets": {"net10.0": {
            "Consumer/1.0.0": {"runtime": {"Consumer.dll": {}}},
            self.identity: {"runtime": {"lib/net10.0/Dependency.dll": {}}}}}}
        self.profile = {"published": self.published, "assembly": "Consumer.dll"}
        self.closure = {"assets": {"path": str(self.assets)}, "packageFolders": [str(self.cache)],
                        "cachedArchives": [{"identity": self.identity, "archive": {"path": "explicitly-retained-by-closure", "sha256": "a" * 64}}]}

    def observe(self):
        (self.published / "Consumer.deps.json").write_text(json.dumps(self.deps))
        return consumer_binary_provenance(self.profile, self.closure)

    def test_exact_restored_dependency_bytes_are_bound_to_the_published_deps_target(self):
        record = self.observe()
        self.assertEqual([self.identity], [item["identity"] for item in record["files"]])
        self.assertEqual(record["files"][0]["published"]["sha256"], record["files"][0]["restored"]["sha256"])

    def test_replaced_published_dependency_is_refused(self):
        (self.published / "Dependency.dll").write_bytes(b"unrelated-current-dll")
        with self.assertRaisesRegex(RuntimeError, "bytes differ"):
            self.observe()

    def test_unretained_or_unknown_dependency_is_refused(self):
        self.closure["cachedArchives"] = []
        with self.assertRaisesRegex(RuntimeError, "retained archive"):
            self.observe()
        self.deps["targets"]["net10.0"] = {"Unrestored.Dependency/9.0.0": {}}
        with self.assertRaisesRegex(RuntimeError, "absent from the package-only"):
            self.observe()

    def test_native_dependency_bytes_are_checked_too(self):
        native = self.cache / "fixture.dependency/1.0.0/runtimes/osx-arm64/native/fixture.dylib"
        native.parent.mkdir(parents=True); native.write_bytes(b"native-asset")
        (self.published / "fixture.dylib").write_bytes(native.read_bytes())
        self.deps["targets"]["net10.0"][self.identity]["native"] = {"runtimes/osx-arm64/native/fixture.dylib": {}}
        record = self.observe()
        self.assertEqual({"runtime", "native"}, {item["category"] for item in record["files"]})
        (self.published / "fixture.dylib").write_bytes(b"different-native-binary")
        with self.assertRaisesRegex(RuntimeError, "bytes differ"):
            self.observe()


if __name__ == "__main__":
    unittest.main()
