"""Focused fixtures for the helpers used by Packages and Docker."""

import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tarfile
import tempfile
import unittest

import workflow_guards as guards


SCRIPT = Path(guards.__file__)
SHA = "a" * 40


def run_helper(command: str, env: dict[str, str], payload: str | None = None) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(SCRIPT), command],
        env=os.environ | env,
        input=payload,
        capture_output=True,
        text=True,
        check=False,
    )


def packages_run(run_id: int, event: str, started: str, **overrides: object) -> dict[str, object]:
    return {
        "id": run_id,
        "event": event,
        "conclusion": "success",
        "head_branch": "main",
        "head_sha": SHA,
        "run_started_at": started,
        **overrides,
    }


class WorkflowGuardTests(unittest.TestCase):
    def test_release_matrix_and_event_text_as_data(self) -> None:
        cases = (
            ("prereleased", "true", "contributor-preview-2026-10-07.1", True),
            ("published", "true", "contributor-preview-2026-10-07.2", True),
            ("published", "true", "v4.0.0-preview", False),
            ("published", "false", "contributor-preview-2026-10-07.1", False),
            ("prereleased", "true", "contributor-preview-2026-10-07.0", False),
            ("prereleased", "true", "contributor-preview-2026-10-07.01", False),
            ("prereleased", "true", "contributor-preview-2026-99-07.1", False),
            ("released", "true", "contributor-preview-2026-10-07.1", False),
            ("", "true", "contributor-preview-2026-10-07.1", False),
        )
        for action, prerelease, tag, expected in cases:
            with self.subTest(action=action, prerelease=prerelease, tag=tag):
                self.assertEqual(expected, guards.is_metadata_only_checkpoint(action, prerelease, tag))

        with tempfile.TemporaryDirectory() as temporary:
            marker = Path(temporary) / "unexpected"
            refused = run_helper("release", {
                "RELEASE_ACTION": "published",
                "RELEASE_PRERELEASE": "true",
                "RELEASE_TAG_NAME": f"$(touch {marker})",
            })
            accepted = run_helper("release", {
                "RELEASE_ACTION": "published",
                "RELEASE_PRERELEASE": "true",
                "RELEASE_TAG_NAME": "contributor-preview-2026-10-07.1",
            })
            ordinary = run_helper("release", {
                "RELEASE_ACTION": "published",
                "RELEASE_PRERELEASE": "false",
                "RELEASE_TAG_NAME": "v4.0.0",
            })
            self.assertEqual(1, refused.returncode)
            self.assertFalse(marker.exists())
            self.assertEqual(0, accepted.returncode)
            self.assertEqual(1, ordinary.returncode)
            self.assertIn("Packages from a GitHub Release are not published", ordinary.stdout)
            self.assertIn("#2085", ordinary.stdout)

    def test_docker_admission_cli_matrix_preserves_direct_paths(self) -> None:
        cases = [("workflow_run", event, result, event in {"push", "workflow_dispatch"} and result == "success")
                 for event in ("push", "workflow_dispatch", "release")
                 for result in ("success", "failure", "skipped")]
        cases += [("pull_request", "", "", False), ("workflow_dispatch", "", "", True), ("push", "", "success", False)]
        for name, source, conclusion, expected in cases:
            with self.subTest(name=name, source=source, conclusion=conclusion), tempfile.TemporaryDirectory() as temporary:
                output = Path(temporary) / "github-output"
                result = run_helper("docker-admission", {
                    "GITHUB_EVENT_NAME": name,
                    "WORKFLOW_RUN_EVENT": source,
                    "WORKFLOW_RUN_CONCLUSION": conclusion,
                    "GITHUB_OUTPUT": str(output),
                })
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(expected, output.read_text().strip() == "allowed=true")

    def test_manual_resolver_selects_newest_exact_sha_eligible_run(self) -> None:
        runs = [
            packages_run(100, "push", "2026-10-06T10:00:00Z"),
            packages_run(200, "release", "2026-10-07T10:00:00Z"),
            packages_run(150, "workflow_dispatch", "2026-10-06T12:00:00Z"),
            packages_run(201, "push", "2026-10-07T11:00:00Z", head_sha="b" * 40),
            packages_run(202, "push", "2026-10-07T12:00:00Z", conclusion="failure"),
            packages_run(203, "push", "2026-10-07T13:00:00Z", head_branch="feature"),
        ]
        result = run_helper("resolve-packages-run", {"REQUESTED_COMMIT": SHA}, json.dumps(runs))
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("150", result.stdout.strip())

        none = run_helper("resolve-packages-run", {"REQUESTED_COMMIT": SHA}, json.dumps([runs[1]]))
        invalid_sha = run_helper("resolve-packages-run", {"REQUESTED_COMMIT": "main"}, "[]")
        self.assertEqual(1, none.returncode)
        self.assertEqual(1, invalid_sha.returncode)

    def test_studio_workflow_gate_supports_bare_and_ref_qualified_ci_paths(self) -> None:
        workflow = (SCRIPT.parents[2] / ".github/workflows/studio-docker.yml").read_text()
        self.assertIn("workflows: [CI]", workflow)
        self.assertIn("github.event.workflow_run.path == '.github/workflows/ci.yml'", workflow)
        self.assertIn("startsWith(github.event.workflow_run.path, '.github/workflows/ci.yml@')", workflow)
        self.assertNotIn("pull_request:", workflow.split("permissions:", 1)[0])
        packages = (SCRIPT.parents[2] / ".github/workflows/studio-packages.yml").read_text()
        self.assertNotIn("\n  push:", packages.split("env:", 1)[0])

    def test_studio_registry_writes_require_immutable_publish_authority(self) -> None:
        workflow = (SCRIPT.parents[2] / ".github/workflows/studio-docker.yml").read_text()
        authority = "github.event_name == 'workflow_run' || inputs.publish == true"
        self.assertNotIn("steps.image.outputs.publish", workflow)
        self.assertNotIn("MANUAL_PUBLISH:", workflow)
        self.assertIn("push: ${{ " + authority + " }}", workflow)
        for name in ("Log in to Docker Hub", "Promote the verified main digest to latest"):
            step = workflow.split("- name: " + name, 1)[1].split("\n      - name:", 1)[0]
            self.assertIn(authority, step)
        self.assertIn('sys.path.insert(0, os.environ["STUDIO_IMAGE_VALIDATORS"])', workflow)
        self.assertIn('require_preview_above_floor(os.environ["PREVIEW_IMAGE_TAG"], read_repository_preview_floor(image))', workflow)
        # Each flavor publishes from its own writer queue and the repository preflight stays behind publish authority.
        self.assertIn("group: studio-docker-${{ matrix.flavor }}-", workflow)
        self.assertIn("fail-fast: false", workflow)
        preflight = workflow.split("- name: Require the flavor's Docker Hub repository", 1)[1].split("\n      - name:", 1)[0]
        self.assertIn(authority, preflight)
        self.assertLess(workflow.index("Preserve trusted image validators"), workflow.index("Checkout the verified source commit"))

    def test_workflow_wiring_and_removal_mutant(self) -> None:
        repo = SCRIPT.parents[2]
        packages = (repo / ".github/workflows/packages.yml").read_text()
        docker = (repo / ".github/workflows/docker.yml").read_text()
        ci = (repo / ".github/workflows/ci.yml").read_text()
        self.assertIn("run: python3 tools/contributor-checkpoints/workflow_guards.py release", packages)
        self.assertIn("RELEASE_TAG_NAME: ${{ github.event.release.tag_name }}", packages)
        self.assertIn("if: ${{ github.event_name != 'release' }}", packages)
        self.assertIn("github.event_name == 'push' || github.event_name == 'workflow_dispatch'", packages)
        self.assertIn("run: python3 tools/contributor-checkpoints/workflow_guards.py docker-admission", docker)
        self.assertFalse("pull_request:" in docker, "docker.yml must not build images on pull requests (#2751)")
        self.assertIn("event=push&head_sha=$commit", docker)
        self.assertIn("event=workflow_dispatch&head_sha=$commit", docker)
        self.assertIn("format('ineligible-{0}', github.run_id)", docker)
        self.assertIn("python3 tools/contributor-checkpoints/test_workflow_guards.py", ci)

        contract = lambda text: (
            "needs: admission" in text
            and "if: needs.admission.outputs.allowed == 'true'" in text
            and len(re.findall(r"(?m)^    needs: versions$", text)) == 3
        )
        self.assertTrue(contract(docker))
        self.assertFalse(contract(docker.replace("if: needs.admission.outputs.allowed == 'true'", "if: true", 1)))
        self.assertFalse(contract(docker.replace("    needs: versions\n", "    needs: admission\n", 1)))


class ServerProfileArchiveTests(unittest.TestCase):
    def test_index_id_matches_only_the_verified_runtime_image_config(self) -> None:
        verifier = SCRIPT.parents[1] / "docker-profiles" / "archive_identity.py"
        image = "elsa-server-essentials-curated:qualification"
        config = b'{"architecture":"arm64","os":"linux"}'
        config_id = "sha256:" + hashlib.sha256(config).hexdigest()
        files = {
            "config.json": config,
            "manifest.json": json.dumps([{"RepoTags": [image], "Config": "config.json"}]).encode(),
        }

        def oci_descriptor(document, **metadata):
            data = json.dumps(document).encode()
            digest = "sha256:" + hashlib.sha256(data).hexdigest()
            files["blobs/" + digest.replace(":", "/", 1)] = data
            return {"digest": digest, **metadata}

        runtime = oci_descriptor({"config": {"digest": config_id}}, platform={"os": "linux", "architecture": "arm64"})
        foreign = oci_descriptor({"config": {"digest": "sha256:" + "f" * 64}}, platform={"os": "linux", "architecture": "amd64"})
        attestation = {"digest": "sha256:" + "a" * 64, "platform": {"os": "unknown", "architecture": "unknown"},
                       "annotations": {"vnd.docker.reference.type": "attestation-manifest", "vnd.docker.reference.digest": runtime["digest"]}}
        valid = oci_descriptor({"manifests": [runtime, attestation]})
        extra_runtime = oci_descriptor({"manifests": [runtime, foreign]})
        files["index.json"] = json.dumps({"manifests": [valid, foreign, extra_runtime]}).encode()

        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "image.tar.gz"
            with tarfile.open(path, "w:gz") as archive:
                for name, data in files.items():
                    info = tarfile.TarInfo(name)
                    info.size = len(data)
                    archive.addfile(info, io.BytesIO(data))
            for descriptor, expected in ((valid, 0), (foreign, 1), (extra_runtime, 1)):
                with self.subTest(descriptor=descriptor):
                    result = subprocess.run([sys.executable, str(verifier), str(path), image, config_id, descriptor["digest"]],
                                            capture_output=True, text=True, check=False)
                    self.assertEqual(expected, result.returncode, result.stderr)


if __name__ == "__main__":
    unittest.main()
