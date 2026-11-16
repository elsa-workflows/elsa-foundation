"""Owned feed controls; synthetic archives are not native lifecycle acceptance evidence."""
from pathlib import Path
import json
import tempfile
from types import SimpleNamespace
import unittest
import zipfile

from artifacts import sha
from native_lifecycle import (remove_resource_archives, restore_resource_archives, package_inventory,
                              require_installed_package, require_removed_package, NativeObservations)


class NativeMembershipTests(unittest.TestCase):
    """Descriptor fixtures test evidence rejection, not actual native package acceptance."""
    package = {"id": "Resource.Package", "version": "1.2.3"}

    def snapshot(self, *, version="1.2.3", studio=False, present=True):
        rows = [{"id" if studio else "packageId": "Resource.Package", "version": version, "installPath": "/owned/native/resource"}] if present else []
        return {"snapshotAtUtc": "2026-10-31T00:00:02Z", "persistedAtUtc": "2026-10-31T00:00:01Z", "correlationId": "native-read", "packages": rows}

    def test_active_catalog_must_observe_root_disappear_regardless_of_disable_or_serving_generation(self):
        for studio in (False, True):
            with self.subTest(studio=studio):
                before = package_inventory(self.snapshot(studio=studio), studio=studio)
                require_installed_package(before, self.package)
                # A new generation with no enabled contributions is not an active-package removal.
                after_disable = package_inventory(self.snapshot(studio=studio), studio=studio)
                with self.assertRaisesRegex(RuntimeError, "left the resource package installed"):
                    require_removed_package(before, after_disable, self.package)
                with self.assertRaises(RuntimeError):
                    require_removed_package(before, package_inventory(self.snapshot(studio=studio, version="9.0.0"), studio=studio), self.package)
                require_removed_package(before, package_inventory(self.snapshot(studio=studio, present=False), studio=studio), self.package)

    def test_empty_wrong_or_ambiguous_before_inventory_cannot_qualify_removal(self):
        empty = package_inventory(self.snapshot(present=False))
        with self.assertRaises(RuntimeError): require_removed_package(empty, empty, self.package)
        wrong_version = package_inventory(self.snapshot(version="9.0.0"))
        with self.assertRaises(RuntimeError): require_removed_package(wrong_version, empty, self.package)
        value = self.snapshot()
        value["packages"].append({"packageId": "RESOURCE.PACKAGE", "version": "2.0.0", "installPath": "/owned/other"})
        with self.assertRaises(RuntimeError): package_inventory(value)
        with self.assertRaises(RuntimeError): package_inventory({"packages": []})

    def test_membership_reads_the_native_api_and_retains_wire_before_rejecting_bad_snapshot(self):
        class Client:
            base = "http://127.0.0.1:51001"
            def __init__(self, response): self.response, self.paths = response, []
            def request(self, path, body=None, *, method="GET", headers=None):
                self.paths.append(path)
                return 200, json.dumps(self.response).encode(), {"content-type": "application/json"}
        with tempfile.TemporaryDirectory() as temporary:
            native = NativeObservations(Path(temporary) / "wire")
            client = Client(self.snapshot())
            inventory, observed = native.membership(client, "backend")
            require_installed_package(inventory, self.package)
            self.assertEqual(["/__qualification/packages"], client.paths)
            self.assertEqual(client.response, json.loads(Path(observed["body"]["path"]).read_text()))
            studio = Client(self.snapshot(studio=True))
            require_installed_package(native.membership(studio, "studio", studio=True)[0], self.package)
            self.assertEqual(["/_elsa/module-management/registry"], studio.paths)
            bad = Client({"packages": []})
            with self.assertRaises(RuntimeError): native.membership(bad, "bad")
            self.assertEqual({"packages": []}, json.loads(Path(native.observations[-1]["body"]["path"]).read_text()))


class FeedOwnershipTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.feed = self.root / "feed"
        self.feed.mkdir()
        self.original = self.root / "authors/Module.1.0.0.nupkg"
        self.original.parent.mkdir()
        with zipfile.ZipFile(self.original, "w") as package:
            package.writestr("Module.nuspec", "<package><metadata><id>Module</id><version>1.0.0</version></metadata></package>")
        self.resource = self.feed / self.original.name
        self.resource.write_bytes(self.original.read_bytes())
        self.protocol = self.feed / "Protocol.1.0.0.nupkg"
        self.dependency = self.feed / "Shared.Dependency.1.0.0.nupkg"
        self.protocol.write_bytes(b"baseline provider archive")
        self.dependency.write_bytes(b"baseline shared dependency archive")
        self.baseline = {path.name: sha(path) for path in (self.protocol, self.dependency)}
        item = {"identity": "Module/1.0.0", "path": str(self.resource), "sourceArchive": str(self.original), "sha256": sha(self.original)}
        self.delta = {"added": [item], "baseline": self.baseline,
                      "selected": [item, {"path": str(self.dependency), "sha256": sha(self.dependency)}]}
        self.run = SimpleNamespace(root=self.root, profiles={"backend": {"runtimeFeed": self.feed,
                                    "runtimeArchiveInputs": [item.copy()]}})

    def tearDown(self):
        self.temporary.cleanup()

    def test_module_removal_preserves_the_protocol_and_shared_dependency_and_restores_exact_original_bytes(self):
        original = self.original.read_bytes()
        remove_resource_archives(self.run, "backend", self.delta)
        self.assertFalse(self.resource.exists())
        self.assertEqual({path.name: sha(path) for path in self.feed.iterdir()}, self.baseline)
        self.assertEqual(self.original.read_bytes(), original)
        restore_resource_archives(self.run, "backend", self.delta)
        self.assertEqual(self.resource.read_bytes(), original)
        self.assertEqual({name: sha(self.feed / name) for name in self.baseline}, self.baseline)

    def test_changed_or_foreign_archive_cannot_be_removed_as_the_original_module(self):
        self.resource.write_bytes(b"changed native input")
        with self.assertRaises(RuntimeError):
            remove_resource_archives(self.run, "backend", self.delta)
        self.assertEqual(self.resource.read_bytes(), b"changed native input")
        self.resource.write_bytes(self.original.read_bytes())
        self.delta["added"][0]["path"] = str(self.original)
        with self.assertRaises(RuntimeError):
            remove_resource_archives(self.run, "backend", self.delta)
        self.assertTrue(self.original.exists())
        self.assertEqual({name: sha(self.feed / name) for name in self.baseline}, self.baseline)


if __name__ == "__main__":
    unittest.main()
