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
        self.assertEqual(monitor.release_tags([]), set())

    def test_release_members(self):
        rows = monitor.subjects(self.tag, self.catalog[self.tag])
        self.assertEqual(len(rows), 4)
        self.assertEqual(len({record["digest"] for record, _, _ in rows}), 4)
        with self.assertRaisesRegex(ValueError, "ambiguous"):
            monitor.subjects(self.tag, {"images": [self.entry, self.entry], "packages": []})
        with self.assertRaisesRegex(ValueError, "Invalid image"):
            monitor.subjects(self.tag, {"images": [{**self.entry, "children": {}}], "packages": []})

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

    def test_modern_image_writes_both_validated_files_from_producer_cli(self):
        monitor.sys.path.insert(0, str(monitor.ROOT / "tools/ci"))
        import verify_image_evidence as producer
        from test_verify_image_evidence import ImageEvidenceTests

        for product, app in [("elsa-workbench", "Elsa.Workbench"), ("elsa-studio", "Elsa.Studio.Web")]:
            fixture = ImageEvidenceTests()
            fixture.setUp()
            builder = f"https://github.com/{monitor.REPO}/actions/runs/34/attempts/1"
            store = fixture.store

            def store_statement(document):
                if document.get("predicateType") == producer.SLSA:
                    document["predicate"]["runDetails"]["builder"]["id"] = builder
                if document.get("predicateType") == producer.SPDX:
                    bom = document["predicate"]
                    bom.update(SPDXID="SPDXRef-DOCUMENT", name="fixture", dataLicense="CC0-1.0",
                               creationInfo={"created": "2026-01-01T00:00:00Z", "creators": ["Tool: test"]},
                               documentNamespace="https://spdx.org/spdxdocs/foundation-monitor-test")
                    for i, package in enumerate(bom["packages"]):
                        package.update(SPDXID="SPDXRef-" + str(i), downloadLocation="NOASSERTION")
                        ref = package["externalRefs"][0]
                        ref["referenceCategory"] = "PACKAGE-MANAGER"
                        if package["name"] == "Elsa.Test":
                            package["name"] = app
                            ref["referenceLocator"] = "pkg:nuget/" + app + "@1"
                return store(document)

            fixture.store = store_statement
            digest = fixture.index().split("@")[1]
            entry = {"product": product, "index": digest, "producerRun": 34, "publisherCommit": "c" * 40,
                     "children": {"linux/" + arch: child for arch, child in fixture.children.items()}}
            calls = []

            def command(*args, **kwargs):
                calls.append(args)
                if args[:2] == ("gh", "api"):
                    workflow = "studio-docker" if product == "elsa-studio" else "docker"
                    return json.dumps({"head_sha": entry["publisherCommit"], "head_branch": "main",
                                       "path": f".github/workflows/{workflow}.yml", "conclusion": "success"})
                if args[0] == "oras":
                    return fixture.fetch("manifest", args[-1])
                if args[0] == monitor.sys.executable:
                    self.assertEqual(Path(args[1]).resolve(), Path(producer.__file__).resolve())
                    def read(cmd):
                        return fixture.fetch(cmd[1], cmd[-1])
                    with patch.object(producer.sys, "argv", [str(arg) for arg in args[1:]]), patch.object(
                            producer.subprocess, "check_output", side_effect=read):
                        producer.main()

            with patch.object(monitor, "run", command):
                output = self.path / product
                paths = monitor.image_inventories(entry, output)
            expected = "attestation" if product == "elsa-studio" else "verify"
            self.assertTrue(any(call[1] == expected and entry["publisherCommit"] in call for call in calls))
            for arch, child in fixture.children.items():
                sbom = json.loads(paths[child].read_text())
                self.assertIn(app, [package["name"] for package in sbom["packages"]])
                self.assertIn(arch, sbom["packages"][0]["externalRefs"][0]["referenceLocator"])

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

    def test_partial_image_and_scan_failure_report_only_affected_digests(self):
        release = [[{"tag_name": self.tag, "prerelease": True, "draft": False}]]
        status = self.status()
        inventories = {child: self.path / platform.replace("/", "-") for platform, child in self.entry["children"].items()}
        for path in inventories.values():
            path.write_text(path.name)
        studio = self.catalog[self.tag]["images"][1]
        def images(entry, directory):
            if entry["product"] == studio["product"]:
                raise monitor.MissingInventory("missing SPDX", [studio["children"]["linux/arm64"]])
            return inventories
        def scan(path, revision):
            if path.name == "linux-amd64":
                raise subprocess.TimeoutExpired("grype", 12)
            return {"state": "no-findings", "matches": [], "inventory": monitor.sha(path.read_bytes())}
        with patch.object(monitor, "image_inventories", side_effect=images), patch.object(monitor, "scan", side_effect=scan), patch.object(
                monitor, "run", side_effect=[json.dumps(release), b"", json.dumps(status)]):
            path = self.path / "report.json"
            self.assertEqual(monitor.monitor(path), 1)
        rows = json.loads(path.read_text())["results"]
        self.assertEqual([row["state"] for row in rows], ["error", "no-findings", "error", "missing"])
        self.assertIn("another platform", rows[2]["detail"])

    def test_truncated_archive_rejected_before_identity(self):
        import verify
        entry = {"id": "Elsa.Test", "version": "1", "feed": verify.FEED, "sha256": "sha256:" + "f" * 64}
        def truncated(feed, name, version, package):
            package.write_bytes(b"truncated bytes")
        with patch.object(verify, "download", truncated):
            with self.assertRaisesRegex(ValueError, "differs from release"):
                monitor.package_inventory(entry, self.path)
            entry["sha256"] = monitor.sha(b"truncated bytes")
            with self.assertRaises(monitor.FAILURES):
                monitor.package_inventory(entry, self.path)

    def test_unchanged_subject_reuses_scan_across_release_references(self):
        path = self.path / "bom.json"
        path.write_text("test inventory")
        scanned = {}
        records = [{"digest": "sha256:" + "1" * 64, "detail": "pending", "release": release} for release in ("4.0.0", "4.1.0")]
        with patch.object(monitor, "scan", return_value={"state": "findings", "inventory": monitor.sha(path.read_bytes()),
                                                        "matches": [{"advisory": "test"}]}) as scanner:
            for row in records:
                monitor.scan_subject(row, path, {}, scanned)
            self.assertEqual(scanner.call_count, 1)
            self.assertEqual(len(scanned), 1)
            self.assertEqual([row["matchCount"] for row in records], [1, 1])
            path.write_text("different verified inventory")
            with self.assertRaisesRegex(ValueError, "disagree"):
                monitor.scan_subject({"digest": records[0]["digest"]}, path, {}, scanned)

    def test_legacy_gap_stays_visible_after_next_checkpoint(self):
        # The old GitHub release may itself be deleted when the old core is retired.
        newer = [[{"tag_name": "contributor-preview-2026-11-14.2", "draft": False, "prerelease": True}]]
        with patch.object(monitor, "run", side_effect=[json.dumps(newer), b"", json.dumps(self.status())]), patch.object(
                monitor, "image_inventories", return_value=None):
            path = self.path / "report.json"
            self.assertEqual(monitor.monitor(path), 1)
        state = json.loads(path.read_bytes())
        self.assertEqual([(r["release"], r["state"]) for r in state["results"]], [(self.tag, "unavailable")] * 4)
        self.assertIn("contributor-preview-2026-11-14.2", state["errors"][0])

    def test_feed_contract_and_transport_failures_are_contained(self):
        from http.client import IncompleteRead
        import verify
        entry = {"id": "Elsa.Test", "version": "1", "feed": verify.FEED, "sha256": "sha256:" + "f" * 64}
        for error in (StopIteration(), IncompleteRead(b"", 5)):
            with patch.object(verify, "download", side_effect=error), self.assertRaises(monitor.FAILURES):
                monitor.package_inventory(entry, self.path)
