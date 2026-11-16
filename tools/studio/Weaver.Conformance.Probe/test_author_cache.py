"""NuGet's content identity and downloaded archive identity have different hash meanings."""
import base64
import hashlib
import json
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest

from authors import _closure


class AuthorCacheTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.source = self.root / "author"
        (self.source / "obj").mkdir(parents=True)
        self.cache = self.root / "cache"
        self.package = self.cache / "external/1.0.0"
        self.package.mkdir(parents=True)
        self.archive = self.package / "external.1.0.0.nupkg"
        # Synthetic hash-boundary fixture. This does not claim a verified signature.
        self.archive.write_bytes(b"archive bytes include packaging and signature metadata")
        self.raw_hash = base64.b64encode(hashlib.sha512(self.archive.read_bytes()).digest()).decode()
        self.content_hash = base64.b64encode(hashlib.sha512(b"NuGet canonical package content").digest()).decode()
        self.sidecar = self.package / "external.1.0.0.nupkg.sha512"
        self.sidecar.write_text(self.raw_hash)
        self.metadata = self.package / ".nupkg.metadata"
        self.metadata.write_text(json.dumps({"version": 2, "contentHash": self.content_hash, "source": "https://api.nuget.org/v3/index.json"}))
        self.assets = {"packageFolders": {str(self.cache): {}}, "libraries": {
            "External/1.0.0": {"type": "package", "path": "external/1.0.0", "sha512": self.content_hash}}}
        (self.source / "obj/project.assets.json").write_text(json.dumps(self.assets))
        (self.source / "packages.lock.json").write_text("{}")
        self.run = SimpleNamespace(packages={}, module_packages={}, source="a" * 40)

    def tearDown(self):
        self.temp.cleanup()

    def audit(self):
        return _closure(self.run, self.source, {"NUGET_PACKAGES": str(self.cache)})

    def test_nuget_content_identity_and_actual_archive_hash_are_retained_separately(self):
        record = self.audit()["cachedArchives"][0]
        self.assertEqual(record["sha512"], self.content_hash)
        self.assertNotEqual(self.raw_hash, self.content_hash)
        self.assertEqual(record["archive"]["sha256"], hashlib.sha256(self.archive.read_bytes()).hexdigest())

    def test_changed_archive_is_rejected_even_when_nuget_content_identity_still_matches(self):
        self.archive.write_bytes(b"changed after extraction")
        with self.assertRaisesRegex(RuntimeError, "archive hash mismatch"):
            self.audit()

    def test_metadata_content_identity_must_match_the_actual_restored_graph(self):
        self.metadata.write_text(json.dumps({"version": 2, "contentHash": self.raw_hash}))
        with self.assertRaisesRegex(RuntimeError, "content hash mismatch"):
            self.audit()

    def test_missing_native_hash_records_cannot_be_replaced_by_an_unverified_marker(self):
        for file in (self.sidecar, self.metadata):
            original = file.read_bytes()
            file.unlink()
            with self.assertRaises((RuntimeError, FileNotFoundError)):
                self.audit()
            file.write_bytes(original)


if __name__ == "__main__":
    unittest.main()
