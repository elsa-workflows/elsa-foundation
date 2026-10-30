"""Owned feed controls; synthetic archives are not native lifecycle acceptance evidence."""
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
import zipfile

from artifacts import sha
from native_lifecycle import remove_resource_archives, restore_resource_archives


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
