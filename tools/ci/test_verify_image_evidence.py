"""Negative tests for copying an otherwise valid SBOM to another platform or image."""
import hashlib
import json
import unittest

from verify_image_evidence import SLSA, SPDX, verify


class ImageEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.blobs = {}
        self.children = {"amd64": "sha256:" + "1" * 64, "arm64": "sha256:" + "2" * 64}

    def store(self, document):
        data = json.dumps(document).encode()
        digest = "sha256:" + hashlib.sha256(data).hexdigest()
        self.blobs[digest] = data
        return {"digest": digest}

    def index(self, missing=False, swap=False):
        descriptors = []
        for arch, child in self.children.items():
            descriptors.append({"platform": {"os": "linux", "architecture": arch}, "digest": child})
            layers = []
            for kind in [SPDX, SLSA]:
                if missing and kind == SPDX and arch == "arm64":
                    continue
                predicate = {"runDetails": {"builder": {"id": "fixture"}}}
                if kind == SPDX:
                    refs = [{"referenceType": "purl", "referenceLocator": purl}
                            for purl in ["pkg:deb/os@1", "pkg:nuget/Elsa.Test@1"]]
                    predicate = {"spdxVersion": "SPDX-2.3", "packages": [{"externalRefs": refs}]}
                subject = self.children["amd64"] if swap and kind == SPDX else child
                layer = self.store({"subject": [{"digest": {"sha256": subject[7:]}}],
                                    "predicateType": kind, "predicate": predicate})
                layer["annotations"] = {"in-toto.io/predicate-type": kind}
                layers.append(layer)
            attestation = self.store({"subject": {"digest": child}, "layers": layers})
            attestation["annotations"] = {"vnd.docker.reference.digest": child,
                                          "vnd.docker.reference.type": "attestation-manifest"}
            descriptors.append(attestation)
        return "fixture@" + self.store({"manifests": descriptors})["digest"]

    def fetch(self, kind, reference):
        return self.blobs[reference.split("@")[1]]

    def test_paired_statements(self):
        verify(self.index(), self.fetch, None)

    def test_missing_arm_inventory(self):
        with self.assertRaisesRegex(ValueError, "Missing SPDX"):
            verify(self.index(missing=True), self.fetch, None)

    def test_amd64_subject_in_arm_inventory(self):
        with self.assertRaisesRegex(ValueError, "Incorrect .* subject for linux/arm64"):
            verify(self.index(swap=True), self.fetch, None)

    def test_corrupt_download(self):
        ref = self.index()
        self.blobs[ref.split("@")[1]] += b" "
        with self.assertRaisesRegex(ValueError, "Incorrect manifest digest"):
            verify(ref, self.fetch, None)
