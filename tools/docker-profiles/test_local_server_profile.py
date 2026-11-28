import importlib.util
import os
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location(
    "local_server_profile", Path(__file__).with_name("local-server-profile.py")
)
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


class LocalServerProfileTests(unittest.TestCase):
    def test_snapshot_includes_saved_edits_without_changing_the_checkout(self):
        with TemporaryDirectory() as temporary:
            original = Path(temporary) / "original"
            original.mkdir()
            helper.git(original, "init", "--quiet")
            helper.git(original, "config", "diff.noprefix", "true")
            helper.git(original, "config", "color.ui", "always")
            (original / ".gitignore").write_text("ignored\n")
            (original / "edited").write_text("original\n")
            (original / "removed").write_bytes(b"\x00original")
            helper.git(original, "add", ".")
            helper.git(
                original, "-c", "user.name=Test", "-c", "user.email=test@example.invalid",
                "-c", "core.hooksPath=/dev/null", "commit", "--no-gpg-sign", "--quiet", "-m", "Fixture",
            )

            (original / "edited").write_text("staged\n")
            helper.git(original, "add", "edited")
            (original / "edited").write_text("working tree\n")
            (original / "removed").unlink()
            (original / "new file").write_bytes(b"\x00new")
            (original / "ignored").write_text("ignored\n")
            initial_head = helper.git(original, "rev-parse", "HEAD")
            initial_status = helper.git(original, "status", "--porcelain")
            index = original / ".git" / "index"
            initial_index = index.read_bytes()

            snapshot = Path(temporary) / "snapshot"
            with patch.dict(os.environ, {"GIT_INDEX_FILE": str(index)}):
                revision = helper.snapshot(original, snapshot)

            self.assertEqual(initial_index, index.read_bytes())
            self.assertNotEqual(initial_head.decode().strip(), revision)
            self.assertEqual("working tree\n", (snapshot / "edited").read_text())
            self.assertEqual(b"\x00new", (snapshot / "new file").read_bytes())
            self.assertFalse((snapshot / "removed").exists())
            self.assertFalse((snapshot / "ignored").exists())
            self.assertEqual(initial_head, helper.git(original, "rev-parse", "HEAD"))
            self.assertEqual(initial_status, helper.git(original, "status", "--porcelain"))
