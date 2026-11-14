"""Authored T038 prerequisite controls; source checkpoint only, all UNRUN.

These controls exercise owned configuration and fail-closed inputs, not native
upgrade semantics. Passing them could never promote an independent witness.
"""
import copy
import hashlib
import json
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import Mock, patch
import zipfile

from consumers import (COMMON_EFFECTS_FEATURE, DURABLE_EFFECTS_FEATURE, DURABLE_STORE_FEATURE, backend_features,
                       consumer_binary_provenance)
from upgrade_distribution import (BASELINE_SOURCE, BASELINE_TREE, JOURNAL_HEADER, RESOURCE_FEATURE,
                                  OPERATOR_ASSEMBLY, SOURCE_FETCH_TIMEOUT_SECONDS, _ensure_product_source, _source_identity,
                                  prepare_upgrade_content_root, prepare_upgrade_distributions, select_upgrade_generation,
                                  upgrade_operator_command)


class PinnedSourceAcquisitionTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.distribution = SimpleNamespace(root=self.root, assert_immutable_inputs=Mock(), run=Mock(side_effect=self.stage))

    def stage(self, name, command, cwd, **kwargs):
        (self.root / (name + ".log")).write_text("retained metadata")

    def test_existing_pinned_commit_does_not_fetch_or_move_refs(self):
        with patch("upgrade_distribution.subprocess.run", return_value=SimpleNamespace(returncode=0)):
            result = _ensure_product_source(self.distribution, BASELINE_SOURCE)
        self.assertFalse(result["acquired"])
        self.distribution.run.assert_not_called()
        self.assertEqual(2, self.distribution.assert_immutable_inputs.call_count)

    def test_missing_commit_in_shallow_checkout_fetches_only_exact_pinned_object_with_timeout(self):
        with patch("upgrade_distribution.subprocess.run", side_effect=[SimpleNamespace(returncode=1), SimpleNamespace(returncode=0)]):
            result = _ensure_product_source(self.distribution, BASELINE_SOURCE)
        self.assertTrue(result["acquired"])
        args, kwargs = self.distribution.run.call_args
        self.assertEqual(["git", "-c", "core.fsmonitor=false", "fetch", "--no-tags", "--no-write-fetch-head", "--depth=1", "origin", BASELINE_SOURCE], args[1])
        self.assertEqual(SOURCE_FETCH_TIMEOUT_SECONDS, kwargs["timeout_seconds"])
        self.assertEqual(2, self.distribution.assert_immutable_inputs.call_count)
        self.assertTrue(Path(result["fetchLog"]["path"]).is_file())

    def test_failed_acquisition_never_allows_archive_production(self):
        with patch("upgrade_distribution.subprocess.run", return_value=SimpleNamespace(returncode=1)):
            with self.assertRaisesRegex(RuntimeError, "still absent"):
                _ensure_product_source(self.distribution, BASELINE_SOURCE)
        self.assertEqual(1, self.distribution.run.call_count)

    def test_nonbaseline_missing_revision_cannot_trigger_fetch(self):
        with patch("upgrade_distribution.subprocess.run", return_value=SimpleNamespace(returncode=1)):
            with self.assertRaisesRegex(RuntimeError, "exact pinned"):
                _ensure_product_source(self.distribution, "a" * 40)
        self.distribution.run.assert_not_called()

    def test_fetched_commit_still_requires_exact_baseline_tree(self):
        def identity_stage(name, command, cwd):
            (self.root / (name + ".log")).write_text(BASELINE_SOURCE + "\n" + "0" * 40 + "\n")
        self.distribution.run.side_effect = identity_stage
        with patch("upgrade_distribution._ensure_product_source", return_value={"acquired": True}):
            with self.assertRaisesRegex(RuntimeError, "tree differs"):
                _source_identity(self.distribution, BASELINE_SOURCE)
        self.assertNotEqual("0" * 40, BASELINE_TREE)


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

    def operator(self, name="old"):
        self.run.dotnet = "/explicit-fixture-dotnet"
        published = self.root / (name + "-operator-published"); published.mkdir()
        record = {"assembly": OPERATOR_ASSEMBLY, "published": str(published),
                  "commandPrefix": [self.run.dotnet, str(published / OPERATOR_ASSEMBLY)], "publishedFiles": {"test": name}}
        self.prepared[name]["operator"] = record
        return record

    def test_operator_command_selects_its_own_generation_without_launch_or_database_access(self):
        selected = self.prepare()
        database = Path(selected["database"]); database.write_bytes(b"not a SQLite database; command construction must not open it")
        for name in ("old", "current"):
            record = self.operator(name)
            with patch("runtime.published_files", side_effect=self.manifest, create=True), patch("subprocess.Popen", side_effect=AssertionError("No operator launch during preparation")):
                command = upgrade_operator_command(self.run, self.prepared, name, database)
            self.assertEqual([*record["commandPrefix"], "--owned-root", str(self.content), "--database", str(database)], command)

    def test_operator_command_refuses_foreign_database_and_symlink(self):
        self.prepare(); self.operator()
        foreign = self.root / "foreign.db"; foreign.write_bytes(b"outside shared runtime")
        with self.assertRaisesRegex(RuntimeError, "owned runtime database"):
            upgrade_operator_command(self.run, self.prepared, "old", foreign)
        target = self.content / "state/real.db"; target.write_bytes(b"metadata only")
        alias = self.content / "state/alias.db"; alias.symlink_to(target)
        with self.assertRaisesRegex(RuntimeError, "owned runtime database"):
            upgrade_operator_command(self.run, self.prepared, "old", alias)

    def test_operator_command_refuses_changed_binary_manifest_or_substituted_command(self):
        selected = self.prepare(); record = self.operator()
        database = Path(selected["database"]); database.write_bytes(b"metadata only")
        with patch("runtime.published_files", return_value={"different": "bytes"}, create=True):
            with self.assertRaisesRegex(RuntimeError, "published inputs changed"):
                upgrade_operator_command(self.run, self.prepared, "old", database)
        record["commandPrefix"] = ["unowned-command"]
        with self.assertRaisesRegex(RuntimeError, "exact owned prepared"):
            upgrade_operator_command(self.run, self.prepared, "old", database)

    def test_operator_publish_cannot_overlap_runtime_content(self):
        selected = self.prepare(); record = self.operator()
        database = Path(selected["database"]); database.write_bytes(b"metadata only")
        record["published"] = str(self.content)
        record["commandPrefix"] = [self.run.dotnet, str(self.content / OPERATOR_ASSEMBLY)]
        with self.assertRaisesRegex(RuntimeError, "must not overlap"):
            upgrade_operator_command(self.run, self.prepared, "old", database)


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
        self.archive = self.cache / "fixture.dependency/1.0.0/fixture.dependency.1.0.0.nupkg"
        self.closure = {"assets": {"path": str(self.assets)}, "packageFolders": [str(self.cache)],
                        "cachedArchives": [{"identity": self.identity, "archive": {"path": str(self.archive), "sha256": ""}}]}
        self.retain_archive()

    def retain_archive(self, identity=None):
        package_id, version = (identity or self.identity).rsplit("/", 1)
        with zipfile.ZipFile(self.archive, "w") as archive:
            archive.writestr(package_id + ".nuspec", '<package><metadata><id>' + package_id + '</id><version>' + version + '</version></metadata></package>')
            for path in self.archive.parent.rglob("*"):
                if path.is_file() and path != self.archive:
                    archive.write(path, path.relative_to(self.archive.parent).as_posix())
        self.closure["cachedArchives"][0]["archive"]["sha256"] = hashlib.sha256(self.archive.read_bytes()).hexdigest()

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
        self.retain_archive()
        record = self.observe()
        self.assertEqual({"runtime", "native"}, {item["category"] for item in record["files"]})
        (self.published / "fixture.dylib").write_bytes(b"different-native-binary")
        with self.assertRaisesRegex(RuntimeError, "bytes differ"):
            self.observe()

    def test_matching_tampered_cache_and_publish_cannot_override_original_archive(self):
        original = self.archive.read_bytes()
        restored = self.cache / "fixture.dependency/1.0.0/lib/net10.0/Dependency.dll"
        restored.write_bytes(b"same-forged-bytes")
        (self.published / "Dependency.dll").write_bytes(restored.read_bytes())
        with self.assertRaisesRegex(RuntimeError, "retained archive ZIP entry"):
            self.observe()
        self.assertEqual(original, self.archive.read_bytes())

    def test_native_and_runtime_targets_require_the_exact_original_zip_bytes(self):
        for category in ("native", "runtimeTargets"):
            with self.subTest(category=category):
                native = self.cache / "fixture.dependency/1.0.0/runtimes/osx-arm64/native/fixture.dylib"
                native.parent.mkdir(parents=True, exist_ok=True); native.write_bytes(b"original-native")
                self.retain_archive()
                (self.published / "fixture.dylib").write_bytes(b"forged-native")
                native.write_bytes(b"forged-native")
                self.deps["targets"]["net10.0"][self.identity][category] = {"runtimes/osx-arm64/native/fixture.dylib": {}}
                with self.assertRaisesRegex(RuntimeError, "retained archive ZIP entry"):
                    self.observe()
                del self.deps["targets"]["net10.0"][self.identity][category]

    def test_changed_archive_hash_and_wrong_nuspec_identity_are_refused(self):
        original_hash = self.closure["cachedArchives"][0]["archive"]["sha256"]
        with zipfile.ZipFile(self.archive, "a") as archive:
            archive.writestr("additional-file", b"changed")
        with self.assertRaisesRegex(RuntimeError, "archive is missing, changed"):
            self.observe()
        self.assertEqual(original_hash, self.closure["cachedArchives"][0]["archive"]["sha256"])
        self.retain_archive("Wrong.Dependency/1.0.0")
        with self.assertRaisesRegex(RuntimeError, "identity differs"):
            self.observe()

    def test_missing_exact_zip_member_is_refused_even_when_cache_and_publish_match(self):
        with zipfile.ZipFile(self.archive, "w") as archive:
            archive.writestr("Fixture.Dependency.nuspec", '<package><metadata><id>Fixture.Dependency</id><version>1.0.0</version></metadata></package>')
        self.closure["cachedArchives"][0]["archive"]["sha256"] = hashlib.sha256(self.archive.read_bytes()).hexdigest()
        with self.assertRaisesRegex(RuntimeError, "unique exact asset"):
            self.observe()


if __name__ == "__main__":
    unittest.main()
