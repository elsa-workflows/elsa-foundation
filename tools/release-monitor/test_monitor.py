import json
from datetime import datetime, timedelta, timezone
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import monitor


class MonitorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name)
        self.catalog = json.loads(Path(monitor.__file__).with_name("supported.json").read_text())
        self.tag, = self.catalog
        self.entry = self.catalog[self.tag]["images"][0]

    def test_release_selection(self):
        def release(tag, preview=False, draft=False):
            return {"tag_name": tag, "prerelease": preview, "draft": draft}
        self.assertEqual(monitor.release_tags([
            release("v4.0.0"), release("evidence/run-1"), release("v9.0.0", draft=True),
            release("contributor-preview-2026-10-07.2", True),
            release("contributor-preview-2026-10-07.10", True)]),
            {"v4.0.0", "contributor-preview-2026-10-07.10"})
        with self.assertRaises(ValueError):
            monitor.release_tags([])

    def test_release_members(self):
        rows = monitor.subjects(self.tag, self.catalog[self.tag])
        self.assertEqual(len(rows), 4)
        self.assertEqual(len({record["digest"] for record, _, _ in rows}), 4)
        with self.assertRaisesRegex(ValueError, "ambiguous"):
            monitor.subjects(self.tag, {"images": [self.entry, self.entry]})
        with self.assertRaisesRegex(ValueError, "Invalid image"):
            monitor.subjects(self.tag, {"images": [{**self.entry, "children": {}}]})

    def image_transport(self, entry, kind=monitor.SPDX):
        descriptors = []
        for platform, child in entry["children"].items():
            os_name, arch = platform.split("/")
            descriptors.append({"platform": {"os": os_name, "architecture": arch}, "digest": child})
            descriptors.append({"platform": {"os": "unknown", "architecture": "unknown"},
                                "annotations": {"vnd.docker.reference.type": "attestation-manifest",
                                                "vnd.docker.reference.digest": child}, "digest": "sha256:" + "a" * 64})
        def fetch(repository, digest):
            if digest == entry["index"]:
                return {"manifests": descriptors}
            return {"layers": [{"annotations": {"in-toto.io/predicate-type": kind}}]}
        return fetch

    def test_historical_coverage_requires_observed_absence(self):
        with patch.object(monitor, "checked_oci", self.image_transport(self.entry, "https://slsa.dev/provenance/v1")):
            self.assertIsNone(monitor.image_inventories(self.entry, self.path))
        with patch.object(monitor, "checked_oci", self.image_transport(self.entry)):
            with self.assertRaisesRegex(ValueError, "predicate set changed"):
                monitor.image_inventories(self.entry, self.path)

    def test_modern_missing_and_wrong_producer_are_separate(self):
        entry = {**self.entry, "index": "sha256:" + "b" * 64, "producerRun": 34, "publisherCommit": "c" * 40}
        with patch.object(monitor, "checked_oci", self.image_transport(entry, "https://slsa.dev/provenance/v1")):
            with self.assertRaises(monitor.MissingInventory):
                monitor.image_inventories(entry, self.path)
        with patch.object(monitor, "checked_oci", self.image_transport(entry)), patch.object(
                monitor, "run", return_value=b'{"head_sha":"wrong"}'):
            with self.assertRaisesRegex(ValueError, "Unexpected image publisher"):
                monitor.image_inventories(entry, self.path)

    def test_modern_image_requires_signature_and_real_producer_reader(self):
        for product in ["elsa-workbench", "elsa-studio"]:
            entry = {**self.entry, "product": product, "index": "sha256:" + "b" * 64,
                     "producerRun": 34, "publisherCommit": "c" * 40}
            calls = []
            def command(*args, **kwargs):
                calls.append(args)
                if args[:2] == ("gh", "api"):
                    workflow = "studio-docker" if product == "elsa-studio" else "docker"
                    return json.dumps({"head_sha": entry["publisherCommit"], "head_branch": "main",
                                       "path": f".github/workflows/{workflow}.yml", "conclusion": "success"})
                if args[0] == monitor.sys.executable:
                    # Invoke the actual reader against a bad subject; it must reject the unavailable schema.
                    self.assertTrue(Path(args[1]).is_file(), "Integrate Foundation image evidence PR #5545")
                    failure = subprocess.run(args, capture_output=True, env={k: v for k, v in monitor.os.environ.items() if k != "ELSA_IMAGE_SPDX_SCHEMA"})
                    self.assertIn(b"spdx-schema", failure.stderr.lower())
            with patch.object(monitor, "checked_oci", self.image_transport(entry)), patch.object(monitor, "run", command):
                paths = monitor.image_inventories(entry, self.path)
            self.assertEqual(set(paths), set(entry["children"].values()))
            expected = "attestation" if product == "elsa-studio" else "verify"
            self.assertTrue(any(call[1] == expected and entry["publisherCommit"] in call for call in calls))
            self.assertTrue(str(calls[-1][1]).endswith("tools/ci/verify_image_evidence.py"))

    def test_package_bytes_checked_before_verifying_attestations(self):
        import verify
        entry = {"id": "Elsa.Test", "version": "1.0.0", "feed": verify.FEED, "sha256": "sha256:" + "f" * 64}
        def provide(feed, name, version, package):
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("Elsa.Test.nuspec", '<package><metadata><id>Elsa.Test</id><version>1.0.0</version>' +
                                 '<repository commit="' + "c" * 40 + '"/></metadata></package>')
        with patch.object(verify, "download", provide), patch.object(verify, "signed_inventory") as signed:
            with self.assertRaisesRegex(ValueError, "differs from release"):
                monitor.package_inventory(entry, self.path)
            signed.assert_not_called()
            entry["sha256"] = monitor.sha((self.path / "subject.nupkg").read_bytes())
            with patch.object(verify, "download"):
                signed.return_value = {"bomFormat": "CycloneDX"}
                result = monitor.package_inventory(entry, self.path)
                self.assertEqual(json.loads(result.read_bytes()), signed.return_value)
                signed.side_effect = verify.NoAttestations("absent")
                with self.assertRaises(monitor.MissingInventory):
                    monitor.package_inventory(entry, self.path)

    def status(self, age=0):
        return {"valid": True, "built": (datetime.now(timezone.utc) - timedelta(days=age)).isoformat(),
                "schemaVersion": "v6", "from": "https://example.test/db?checksum=sha256%3A" + "d" * 64}

    def test_database_age_and_identity(self):
        for age in [-1, 6]:
            with self.assertRaisesRegex(ValueError, "stale or future"):
                monitor.database(self.status(age))
        with self.assertRaises(ValueError):
            monitor.database({**self.status(), "valid": False})
        with self.assertRaisesRegex(ValueError, "no checksum"):
            monitor.database({**self.status(), "from": ""})

    def test_scan_dedup_and_database_binding(self):
        artifact = self.path / "input.json"
        artifact.write_text("{}")
        status = self.status()
        match = {"artifact": {"purl": "pkg:nuget/Example@1"},
                 "vulnerability": {"id": "example", "severity": "Low", "fix": {}}}
        scan = {"descriptor": {"version": monitor.SCANNER, "db": {"status": status}}, "matches": [match, match]}
        with patch.object(monitor, "run", side_effect=lambda *a, **kw: json.dumps(scan)):
            result = monitor.scan(artifact, monitor.database(status))
            self.assertEqual((result["state"], len(result["matches"])), ("findings", 1))
            scan["matches"] = []
            self.assertEqual(monitor.scan(artifact, monitor.database(status))["state"], "no-findings")
            with self.assertRaisesRegex(ValueError, "database changed"):
                monitor.scan(artifact, {})

    def test_retain_discovered_subjects_on_database_error(self):
        report = self.path / "report.json"
        releases = [[{"tag_name": self.tag, "prerelease": True, "draft": False},
                     {"tag_name": "v4.0.1", "prerelease": False, "draft": False}]]
        with patch.object(monitor, "run", side_effect=[json.dumps(releases), OSError("database unavailable")]):
            self.assertEqual(monitor.monitor(report), 1)
        state = json.loads(report.read_bytes())
        self.assertEqual(len(state["results"]), 4)
        self.assertTrue(all(row["state"] == "error" for row in state["results"]))
        self.assertEqual(len(state["errors"]), 2)
