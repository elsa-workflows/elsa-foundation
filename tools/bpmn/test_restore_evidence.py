#!/usr/bin/env python3
"""Offline controls for restoring authentic captured inputs from pinned archives."""

import hashlib
from contextlib import redirect_stderr
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import call, patch
import urllib.error
import zipfile

import restore_evidence
from restore_evidence import PREFIX, STUDIO_PREFIX, restore, restore_members


class FetchTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.data = b"pinned archive bytes"
        self.record = {"url": "https://example.invalid/archive.zip", "bytes": len(self.data),
                       "sha256": hashlib.sha256(self.data).hexdigest()}
        self.cache = self.root / ".artifacts/bpmn-evidence"
        self.output = io.StringIO()
        self.start_patch(patch.object(restore_evidence, "REPO", self.root))
        self.urlopen = self.start_patch(patch.object(restore_evidence.urllib.request, "urlopen"))
        # Patch the standard module so the recovery control also runs against the old downloader.
        self.sleep = self.start_patch(patch("time.sleep"))

    def start_patch(self, patcher):
        self.addCleanup(patcher.stop)
        return patcher.start()

    def error(self, code):
        return urllib.error.HTTPError(self.record["url"], code, "transport response", {}, io.BytesIO())

    def fetch(self):
        with redirect_stderr(self.output):
            return restore_evidence.fetch(self.record)

    def test_retries_gateway_failure_then_atomically_caches_exact_bytes(self):
        error = self.error(504)
        self.urlopen.side_effect = [error, io.BytesIO(self.data)]
        archive = self.fetch()
        self.assertEqual(self.data, archive.read_bytes())
        self.assertEqual([archive], list(self.cache.iterdir()))
        self.assertEqual(2, self.urlopen.call_count)
        self.sleep.assert_called_once_with(2)
        self.assertTrue(error.closed)
        self.assertIn("attempt 1/3", self.output.getvalue())
        self.assertIn("HTTP 504", self.output.getvalue())
        self.assertIn("retrying in 2s", self.output.getvalue())
        # A verified cached archive still needs no network request on repeat.
        self.assertEqual(archive, self.fetch())
        self.assertEqual(2, self.urlopen.call_count)

    def test_persistent_gateway_failure_raises_the_final_original_error_and_cleans_staging(self):
        errors = [self.error(504) for _ in range(3)]
        self.urlopen.side_effect = errors
        with self.assertRaises(urllib.error.HTTPError) as caught:
            self.fetch()
        self.assertIs(errors[-1], caught.exception)
        self.assertEqual(3, self.urlopen.call_count)
        self.assertEqual([call(2), call(4)], self.sleep.call_args_list)
        self.assertTrue(all(error.closed for error in errors))
        self.assertEqual([], list(self.cache.iterdir()))
        self.assertIn("attempt 3/3", self.output.getvalue())
        self.assertIn("giving up", self.output.getvalue())

    def test_retries_only_the_selected_server_responses(self):
        for code in (500, 502, 503):
            with self.subTest(code=code):
                self.urlopen.reset_mock()
                self.sleep.reset_mock()
                self.urlopen.side_effect = [self.error(code), io.BytesIO(self.data)]
                self.fetch().unlink()
                self.assertEqual(2, self.urlopen.call_count)
                self.sleep.assert_called_once_with(2)

    def test_does_not_retry_auth_missing_or_other_http_errors(self):
        for code in (401, 403, 404, 429, 501, 505):
            with self.subTest(code=code):
                self.urlopen.reset_mock()
                self.sleep.reset_mock()
                error = self.error(code)
                self.urlopen.side_effect = error
                with self.assertRaises(urllib.error.HTTPError) as caught:
                    self.fetch()
                self.assertIs(error, caught.exception)
                self.assertEqual(1, self.urlopen.call_count)
                self.sleep.assert_not_called()
                self.assertTrue(error.closed)
                self.assertEqual([], list(self.cache.iterdir()))

    def test_does_not_retry_other_transport_failures(self):
        error = urllib.error.URLError("connection failed")
        self.urlopen.side_effect = error
        with self.assertRaises(urllib.error.URLError) as caught:
            self.fetch()
        self.assertIs(error, caught.exception)
        self.assertEqual(1, self.urlopen.call_count)
        self.sleep.assert_not_called()
        self.assertEqual([], list(self.cache.iterdir()))

    def test_invalid_bytes_never_retry_or_enter_the_cache(self):
        for retried, data in ((retried, data) for retried in (False, True)
                              for data in (b"wrong size", b"x" * len(self.data))):
            with self.subTest(retried=retried, data=data):
                self.urlopen.reset_mock()
                self.sleep.reset_mock()
                preceding = [self.error(504)] if retried else []
                self.urlopen.side_effect = preceding + [io.BytesIO(data), io.BytesIO(self.data)]
                with self.assertRaisesRegex(ValueError, "pinned size/SHA-256"):
                    self.fetch()
                self.assertEqual(2 if retried else 1, self.urlopen.call_count)
                if retried:
                    self.sleep.assert_called_once_with(2)
                else:
                    self.sleep.assert_not_called()
                self.assertEqual([], list(self.cache.iterdir()))

    def test_retry_discards_partial_transfer_before_validating_fresh_bytes(self):
        error = self.error(504)

        class InterruptedResponse(io.BytesIO):
            def read(self, size):
                if self.tell():
                    raise error
                return super().read(size)

        self.urlopen.side_effect = [InterruptedResponse(b"partial transfer"), io.BytesIO(self.data)]
        archive = self.fetch()
        self.assertEqual(self.data, archive.read_bytes())
        self.assertEqual([archive], list(self.cache.iterdir()))
        self.assertEqual(2, self.urlopen.call_count)

    def test_interrupted_retry_delay_still_cleans_staging(self):
        self.urlopen.side_effect = self.error(504)
        self.sleep.side_effect = InterruptedError("stop retrying")
        with self.assertRaisesRegex(InterruptedError, "stop retrying"):
            self.fetch()
        self.assertEqual(1, self.urlopen.call_count)
        self.assertEqual([], list(self.cache.iterdir()))


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
