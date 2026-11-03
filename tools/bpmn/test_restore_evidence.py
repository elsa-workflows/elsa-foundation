#!/usr/bin/env python3
"""Offline controls for restoring authentic captured inputs from pinned archives."""

import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile

from restore_evidence import PREFIX, STUDIO_PREFIX, restore, restore_members


class RestoreTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.name = PREFIX + "baseline/captured.sqlite"

    def archive(self, name=None, source="original"):
        name = name or self.name
        data = b"actual old captured bytes"
        path = self.root / "fixture.zip"
        item = {"path": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
        with zipfile.ZipFile(path, "w") as writer:
            writer.writestr(name, data)
            writer.writestr("ARCHIVE-MANIFEST.json", json.dumps({"sourceCommit": source, "files": [item]}))
        return path, {"sourceCommit": "original", "files": 1, "bytes": path.stat().st_size,
                      "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}

    def test_restores_exact_bytes_and_repeat_preserves_them(self):
        path, record = self.archive()
        self.assertEqual(1, restore(self.root, path, record, all_evidence=False))
        self.assertEqual(b"actual old captured bytes", (self.root / self.name).read_bytes())
        self.assertEqual(0, restore(self.root, path, record, all_evidence=False))

    def test_refuses_a_different_existing_capture(self):
        path, record = self.archive()
        target = self.root / self.name
        target.parent.mkdir(parents=True)
        target.write_bytes(b"another checkpoint")
        with self.assertRaisesRegex(ValueError, "refusing to overwrite"):
            restore(self.root, path, record, all_evidence=False)
        self.assertEqual(b"another checkpoint", target.read_bytes())

    def test_refuses_wrong_archive_digest_before_writing(self):
        path, record = self.archive()
        record["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "pinned size and SHA"):
            restore(self.root, path, record, all_evidence=False)
        self.assertFalse((self.root / self.name).exists())

    def test_refuses_wrong_source(self):
        path, record = self.archive(source="different")
        with self.assertRaisesRegex(ValueError, "archived source"):
            restore(self.root, path, record, all_evidence=False)

    def test_refuses_escaping_archive_paths(self):
        path, record = self.archive(name=PREFIX + "../../outside")
        with self.assertRaisesRegex(ValueError, "Unexpected evidence path"):
            restore(self.root, path, record, all_evidence=False)
        self.assertFalse((self.root / "outside").exists())

    def test_full_restore_preserves_current_tracked_receipts(self):
        self.name = PREFIX + "receipt.json"
        path, record = self.archive()
        target = self.root / self.name
        target.parent.mkdir(parents=True)
        target.write_bytes(b"current receipt")
        subprocess.run(["git", "init", "-q", str(self.root)], check=True)
        subprocess.run(["git", "add", self.name], cwd=self.root, check=True)
        self.assertEqual(0, restore(self.root, path, record, all_evidence=True))
        self.assertEqual(b"current receipt", target.read_bytes())


class RestoreMembersTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)

    def archive(self, target="preclaim/wait.sqlite.gz", data=b"actual pre-claim bytes"):
        path = self.root / "capture.zip"
        with zipfile.ZipFile(path, "w") as writer:
            writer.writestr("stage/wait.sqlite.gz", data)
            writer.writestr("unpinned.json", b"{}")
        member = {"path": "stage/wait.sqlite.gz", "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(), "target": target}
        return path, {"files": 2, "bytes": path.stat().st_size, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "members": [member]}

    def test_restores_only_pinned_members_and_repeat_preserves_them(self):
        path, record = self.archive()
        self.assertEqual(1, restore_members(self.root, path, record))
        self.assertEqual(b"actual pre-claim bytes", (self.root / STUDIO_PREFIX / "preclaim/wait.sqlite.gz").read_bytes())
        self.assertFalse((self.root / STUDIO_PREFIX / "unpinned.json").exists())
        self.assertEqual(0, restore_members(self.root, path, record))

    def test_refuses_a_member_whose_bytes_differ_from_its_pin(self):
        path, record = self.archive()
        record["members"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "differs from its original bytes"):
            restore_members(self.root, path, record)
        self.assertFalse((self.root / STUDIO_PREFIX / "preclaim/wait.sqlite.gz").exists())

    def test_refuses_wrong_archive_digest_and_inventory(self):
        path, record = self.archive()
        with self.assertRaisesRegex(ValueError, "pinned size and SHA"):
            restore_members(self.root, path, {**record, "sha256": "0" * 64})
        with self.assertRaisesRegex(ValueError, "inventory"):
            restore_members(self.root, path, {**record, "files": 3})

    def test_refuses_targets_outside_the_fixture_root(self):
        path, record = self.archive(target="../../outside")
        with self.assertRaisesRegex(ValueError, "Unexpected fixture path"):
            restore_members(self.root, path, record)
        self.assertFalse((self.root / "specs/outside").exists())

    def test_refuses_a_different_existing_fixture(self):
        path, record = self.archive()
        target = self.root / STUDIO_PREFIX / "preclaim/wait.sqlite.gz"
        target.parent.mkdir(parents=True)
        target.write_bytes(b"another capture")
        with self.assertRaisesRegex(ValueError, "refusing to overwrite"):
            restore_members(self.root, path, record)
        self.assertEqual(b"another capture", target.read_bytes())


if __name__ == "__main__":
    unittest.main()
