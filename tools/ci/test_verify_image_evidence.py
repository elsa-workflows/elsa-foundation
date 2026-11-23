"""Negative tests for copying an otherwise valid SBOM to another platform or image."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

from verify_image_evidence import BUILDKIT, CONFIG, MANIFEST, resolved_build_input, SLSA, SPDX, package_identities, verify


class ImageEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.blobs = {}
        self.children = {}
        for arch in ["amd64", "arm64"]:
            config = self.store({"os": "linux", "architecture": arch,
                                 "rootfs": {"type": "layers", "diff_ids": ["sha256:" + "a" * 64]}})
            config["mediaType"] = CONFIG
            self.children[arch] = self.store({"schemaVersion": 2, "mediaType": "application/vnd.oci.image.manifest.v1+json",
                                              "config": config, "layers": [{"digest": "sha256:" + "9" * 64}]})["digest"]

    def store(self, document):
        data = json.dumps(document).encode()
        digest = "sha256:" + hashlib.sha256(data).hexdigest()
        self.blobs[digest] = data
        return {"digest": digest, "size": len(data)}

    def index(self, missing=False, swap=False, swap_platforms=False, attestation_arch="unknown", build_type=BUILDKIT, descriptor_override=None, wrong_arch=False, empty_parameters=False):
        descriptors = []
        for arch, child in self.children.items():
            platform_arch = ({"amd64": "arm64", "arm64": "amd64"}[arch] if swap_platforms else arch)
            descriptor = {"platform": {"os": "linux", "architecture": platform_arch}, "mediaType": MANIFEST,
                          "digest": child, "size": len(self.blobs[child])}
            if descriptor_override and arch == "amd64":
                descriptor.update(descriptor_override)
            descriptors.append(descriptor)
            layers = []
            for kind in [SPDX, SLSA]:
                if missing and kind == SPDX and arch == "arm64":
                    continue
                parameters = {} if empty_parameters else {"configSource": {"path": "Dockerfile"},
                                                          "request": {"frontend": "dockerfile.v0"}}
                predicate = {"buildDefinition": {"buildType": build_type, "externalParameters": parameters,
                                                 "resolvedDependencies": [{"uri": "pkg:docker/base", "digest": {"sha256": "a" * 64}}]},
                             "runDetails": {"builder": {"id": "fixture/attempts/1"}, "metadata": {"invocationId": "build-1"}}}
                if kind == SPDX:
                    packages = [
                        {"name": name, "versionInfo": "1", "externalRefs": [
                            {"referenceType": "purl", "referenceLocator": purl}]}
                        for name, purl in [("os", "pkg:deb/os@1?arch=" + ("amd64" if wrong_arch else arch)), ("Elsa.Test", "pkg:nuget/Elsa.Test@1")]]
                    predicate = {"spdxVersion": "SPDX-2.3", "packages": packages}
                subject = self.children["amd64"] if swap and kind == SPDX else child
                layer = self.store({"_type": "https://in-toto.io/Statement/v1", "subject": [{"digest": {"sha256": subject[7:]}}],
                                    "predicateType": kind, "predicate": predicate})
                layer["mediaType"] = "application/vnd.in-toto+json"
                layer["annotations"] = {"in-toto.io/predicate-type": kind}
                layers.append(layer)
            attestation = self.store({"schemaVersion": 2, "mediaType": MANIFEST,
                                      "subject": {key: descriptor[key] for key in ("mediaType", "digest", "size")},
                                      "layers": layers})
            attestation["mediaType"] = MANIFEST
            attestation["platform"] = {"os": "unknown", "architecture": attestation_arch}
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

    def test_child_descriptor_matches_what_a_client_fetches(self):
        for override in ({"size": 1}, {"mediaType": "application/octet-stream"},
                         {"platform": {"os": "linux", "architecture": "amd64", "variant": "v3"}}):
            with self.subTest(override=override), self.assertRaisesRegex(ValueError, "descriptor|baseline"):
                verify(self.index(descriptor_override=override), self.fetch, None, "fixture", "Elsa.Test", self.validate_spdx)

    def test_sibling_inventory_with_correct_subject(self):
        with self.assertRaisesRegex(ValueError, "native operating-system inventory for linux/arm64"):
            verify(self.index(wrong_arch=True), self.fetch, None, "fixture", "Elsa.Test", self.validate_spdx)

    def test_empty_build_request(self):
        with self.assertRaisesRegex(ValueError, "SLSA build definition"):
            verify(self.index(empty_parameters=True), self.fetch, None, "fixture", "Elsa.Test", self.validate_spdx)

    def test_attestation_platform_cannot_be_runnable(self):
        with self.assertRaisesRegex(ValueError, "selectable"):
            verify(self.index(attestation_arch="amd64"), self.fetch, None, "fixture", "Elsa.Test", self.validate_spdx)

    def test_build_type_must_identify_buildkit(self):
        for kind in (True, "https://example.invalid/other"):
            with self.subTest(kind=kind), self.assertRaisesRegex(ValueError, "SLSA build definition"):
                verify(self.index(build_type=kind), self.fetch, None, "fixture", "Elsa.Test", self.validate_spdx)

    def test_malformed_resolved_input(self):
        for record in ({"uri": True, "digest": True}, {"uri": "", "digest": {}},
                       {"uri": "pkg:docker/test", "digest": {"sha256": "abc"}}):
            with self.subTest(record=record):
                self.assertFalse(resolved_build_input(record))

    def test_config_must_describe_all_layers(self):
        original = json.loads(self.blobs[self.children["arm64"]])
        invalid = self.store({"os": "linux", "architecture": "arm64", "rootfs": {"type": "layers", "diff_ids": []}})
        original["config"] = dict(invalid, mediaType=CONFIG)
        self.children["arm64"] = self.store(original)["digest"]
        with self.assertRaisesRegex(ValueError, "root filesystem"):
            verify(self.index(), self.fetch, None, "fixture", "Elsa.Test", self.validate_spdx)

    def test_other_producer(self):
        with self.assertRaisesRegex(ValueError, "Unexpected SLSA builder"):
            verify(self.index(), self.fetch, None, "another-run", "Elsa.Test", self.validate_spdx)

    def test_unrelated_elsa_package(self):
        with self.assertRaisesRegex(ValueError, "Missing Elsa.Workbench inventory"):
            verify(self.index(), self.fetch, None, "fixture", "Elsa.Workbench", self.validate_spdx)

    def test_versioned_purls_must_match_their_package(self):
        for locator in ("pkg:deb/", "pkg:deb//os@1", "pkg:nuget/Elsa.Test@",
                        "pkg:nuget/Elsa.Test@bad%20version", "pkg:nuget/Elsa.Test@1%XX",
                        "pkg:nuget/Elsa.Test@1?broken", "pkg:nuget/Elsa.Test@1?arch=one&arch=two",
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

class TagPromotionTests(unittest.TestCase):
    def test_only_the_current_verified_index_can_move_latest(self):
        source = "a" * 40
        verified = "sha256:" + "b" * 64
        other = "sha256:" + "c" * 64
        cases = (
            ("verified", source, verified, verified, True, True),
            ("main advanced", "d" * 40, verified, verified, True, False),
            ("new preview from same commit", source, other, verified, True, False),
            ("wrong latest after copy", source, verified, other, False, True),
        )
        for name, main, staged, latest, success, write in cases:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                commands = {
                    "gh": 'echo "$MAIN"',
                    "docker": """case "$3" in
  create) touch "$MARKER" ;;
  inspect)
    case "$6" in
      *:latest) digest=$LATEST ;;
      *:sha-aaaaaaa) digest=$STAGED ;;
      *) exit 1 ;;
    esac
    printf '{"digest":"%s"}\n' "$digest" ;;
  *) exit 1 ;;
esac""",
                }
                for binary, body in commands.items():
                    path = root / binary
                    path.write_text("#!/bin/sh\n" + body + "\n")
                    path.chmod(0o755)
                marker = root / "promoted"
                env = dict(os.environ, PATH=directory + os.pathsep + os.environ["PATH"],
                           SOURCE_SHA=source, DIGEST=verified, MAIN=main, STAGED=staged, LATEST=latest,
                           IMAGE="fixture/image", MARKER=str(marker), GITHUB_REPOSITORY="fixture/repo",
                           GITHUB_STEP_SUMMARY=str(root / "summary"))
                result = subprocess.run(["bash", str(Path(__file__).with_name("promote_verified_image.sh"))], env=env)
                self.assertEqual(result.returncode == 0, success)
                self.assertEqual(marker.exists(), write)
