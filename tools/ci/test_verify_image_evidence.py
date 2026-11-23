"""Negative tests for copying an otherwise valid SBOM to another platform or image."""
import hashlib
import json
import unittest

from verify_image_evidence import SLSA, SPDX, package_identities, verify


class ImageEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.blobs = {}
        self.children = {}
        for arch in ["amd64", "arm64"]:
            config = self.store({"os": "linux", "architecture": arch})
            self.children[arch] = self.store({"schemaVersion": 2, "mediaType": "application/vnd.oci.image.manifest.v1+json",
                                              "config": config, "layers": [{"digest": "sha256:" + "9" * 64}]})["digest"]

    def store(self, document):
        data = json.dumps(document).encode()
        digest = "sha256:" + hashlib.sha256(data).hexdigest()
        self.blobs[digest] = data
        return {"digest": digest}

    def index(self, missing=False, swap=False, swap_platforms=False):
        descriptors = []
        for arch, child in self.children.items():
            platform_arch = ({"amd64": "arm64", "arm64": "amd64"}[arch] if swap_platforms else arch)
            descriptors.append({"platform": {"os": "linux", "architecture": platform_arch}, "digest": child})
            layers = []
            for kind in [SPDX, SLSA]:
                if missing and kind == SPDX and arch == "arm64":
                    continue
                predicate = {"buildDefinition": {"buildType": "fixture", "externalParameters": {}}, "runDetails": {"builder": {"id": "fixture/attempts/1"}}}
                if kind == SPDX:
                    packages = [
                        {"name": name, "versionInfo": "1", "externalRefs": [
                            {"referenceType": "purl", "referenceLocator": purl}]}
                        for name, purl in [("os", "pkg:deb/os@1"), ("Elsa.Test", "pkg:nuget/Elsa.Test@1")]]
                    predicate = {"spdxVersion": "SPDX-2.3", "packages": packages}
                subject = self.children["amd64"] if swap and kind == SPDX else child
                layer = self.store({"_type": "https://in-toto.io/Statement/v1", "subject": [{"digest": {"sha256": subject[7:]}}],
                                    "predicateType": kind, "predicate": predicate})
                layer["mediaType"] = "application/vnd.in-toto+json"
                layer["annotations"] = {"in-toto.io/predicate-type": kind}
                layers.append(layer)
            attestation = self.store({"subject": {"digest": child}, "layers": layers})
            attestation["annotations"] = {"vnd.docker.reference.digest": child,
                                          "vnd.docker.reference.type": "attestation-manifest"}
            descriptors.append(attestation)
        return "fixture@" + self.store({"schemaVersion": 2, "mediaType": "application/vnd.oci.image.index.v1+json",
                                        "manifests": descriptors})["digest"]

    def validate_spdx(self, document):
        self.assertEqual("SPDX-2.3", document["spdxVersion"])

    def fetch(self, kind, reference):
        return self.blobs[reference.split("@")[1]]

    def test_paired_statements(self):
        verify(self.index(), self.fetch, None, "fixture", "Elsa.Test", self.validate_spdx)

    def test_missing_arm_inventory(self):
        with self.assertRaisesRegex(ValueError, "Missing SPDX"):
            verify(self.index(missing=True), self.fetch, None, "fixture", "Elsa.Test", self.validate_spdx)

    def test_amd64_subject_in_arm_inventory(self):
        with self.assertRaisesRegex(ValueError, "Incorrect .* subject for linux/arm64"):
            verify(self.index(swap=True), self.fetch, None, "fixture", "Elsa.Test", self.validate_spdx)

    def test_swapped_platform_labels(self):
        with self.assertRaisesRegex(ValueError, "Image config disagrees"):
            verify(self.index(swap_platforms=True), self.fetch, None, "fixture", "Elsa.Test", self.validate_spdx)

    def test_other_producer(self):
        with self.assertRaisesRegex(ValueError, "Unexpected SLSA builder"):
            verify(self.index(), self.fetch, None, "another-run", "Elsa.Test", self.validate_spdx)

    def test_unrelated_elsa_package(self):
        with self.assertRaisesRegex(ValueError, "Missing Elsa.Workbench inventory"):
            verify(self.index(), self.fetch, None, "fixture", "Elsa.Workbench", self.validate_spdx)

    def test_versioned_purls_must_match_their_package(self):
        for locator in ("pkg:deb/", "pkg:deb//os@1", "pkg:nuget/Elsa.Test@",
                        "pkg:nuget/Elsa.Test@bad%20version", "pkg:nuget/Elsa.Test@1%XX",
                        "pkg:nuget/Other@1", "pkg:nuget/Elsa.Test@2"):
            with self.subTest(locator=locator):
                package = {"name": "Elsa.Test", "versionInfo": "1", "externalRefs": [
                    {"referenceType": "purl", "referenceLocator": locator}]}
                self.assertFalse(list(package_identities([package])))

    def test_corrupt_download(self):
        ref = self.index()
        self.blobs[ref.split("@")[1]] += b" "
        with self.assertRaisesRegex(ValueError, "Incorrect manifest digest"):
            verify(ref, self.fetch, None, "fixture", "Elsa.Test", self.validate_spdx)
