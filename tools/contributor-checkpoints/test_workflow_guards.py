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
from textwrap import dedent
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
            and len(re.findall(r"(?m)^    needs: versions$", text)) == 2
            and "needs: [versions, build-and-push-foundation-host]" in text
            and "needs: [versions, stage-profile-images]" in text
            and 'current=$(gh api "repos/$GITHUB_REPOSITORY/branches/main"' in text
            and '[ "$current" != "$GITHUB_SHA" ]' in text
        )
        self.assertTrue(contract(docker))
        self.assertFalse(contract(docker.replace("if: needs.admission.outputs.allowed == 'true'", "if: true", 1)))
        self.assertFalse(contract(docker.replace("    needs: versions\n", "    needs: admission\n", 1)))
        self.assertFalse(contract(docker.replace("needs: [versions, stage-profile-images]", "needs: versions", 1)))
        self.assertFalse(contract(docker.replace('[ "$current" != "$GITHUB_SHA" ]', "false", 1)))

    def test_essentials_writer_requires_the_qualified_registry_receipt(self) -> None:
        workflow = (SCRIPT.parents[2] / ".github/workflows/docker.yml").read_text()

        def run_step(name: str, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
            step = workflow.split(f"      - name: {name}\n", 1)[1].split("\n      - ", 1)[0]
            commands = dedent(step.split("        run: |\n", 1)[1])
            return subprocess.run(["bash", "-c", docker_fixture + "\n" + commands],
                                  env=os.environ | env, capture_output=True, text=True, check=False)

        docker_fixture = r'''
docker() {
  case "$1 $2 ${3:-}" in
    "load -i "*) loaded_arch=$(basename "$(dirname "$3")") ;;
    "image inspect --format")
      if [ "$4" = '{{.Id}}' ]; then echo "$LOCAL_ID"; else echo "linux/$loaded_arch"; fi ;;
    "image inspect "*) jq -nc --arg base "$FOUNDATION_IMAGE" '[{Config:{Labels:{"org.opencontainers.image.base.name":$base}}}]' ;;
    "tag "*|"image rm "*) ;;
    "push "*)
      if [[ "$2" == *-amd64 ]]; then pushed=$AMD64; else pushed=$ARM64; fi
      printf '%s: digest: %s size: 123\n' "${2##*:}" "$pushed" ;;
    "buildx imagetools inspect")
      case "$4" in
        *-amd64) jq -nc --arg digest "$AMD64" '{digest:$digest}' ;;
        *-arm64) jq -nc --arg digest "$NATIVE_READBACK" '{digest:$digest}' ;;
        *:latest)
          digest=$PREVIOUS
          if grep -q 'latest written' "$PUBLISH_LOG"; then digest=$INDEX; fi
          jq -nc --arg digest "$digest" '{digest:$digest}' ;;
        *) jq -nc --arg digest "$INDEX_READBACK" '{digest:$digest,manifests:[
              {platform:{os:"linux",architecture:"amd64"}},{platform:{os:"linux",architecture:"arm64"}}]}' ;;
      esac ;;
    "buildx imagetools create")
      printf '%s\n' "$*" >> "$PUBLISH_LOG"
      if [ "$4" = --metadata-file ]; then
        test "$8" = "$REPOSITORY@$AMD64" && test "$9" = "$REPOSITORY@$ARM64" || return 10
        jq -nc --arg digest "$INDEX" '{"containerimage.descriptor":{digest:$digest}}' > "$5"
      else
        test "${!#}" = "$REPOSITORY@$INDEX" || return 11
        if [ "$5" = "$REPOSITORY:latest" ]; then
          grep -q "Previous latest $REPOSITORY@$PREVIOUS" "$GITHUB_STEP_SUMMARY" || return 12
          echo 'latest written' >> "$PUBLISH_LOG"
        fi
      fi ;;
    *) return 13 ;;
  esac
}
python3() { test "$3" = "$IMAGE" && test "$4" = "$LOCAL_ID" && test "$5" = "$LOCAL_ID"; }
gh() { echo "$MAIN_COMMIT"; }
'''
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            receipt = directory / "essentials-native-receipt" / "staged.json"
            publish_log = directory / "publication.log"
            repository = "elsaworkflows/elsa-essentials-host"
            version = "4.0.0-preview.10.1"
            image = "elsa-server-essentials-curated:4.0.0-preview.10"
            base = "elsaworkflows/elsa-foundation-host@sha256:" + "1" * 64
            amd64, arm64, local_id, index, previous, retag = ("sha256:" + value * 64 for value in "234567")
            env = {
                "RUNNER_TEMP": temporary, "ARTIFACT": "elsa-server-essentials-curated", "IMAGE": image,
                "TARGET": f"{repository}:profile-{version}", "VERSION": version, "REPOSITORY": repository,
                "RECORD_ESSENTIALS_RECEIPT": "true", "FOUNDATION_IMAGE": base, "GITHUB_SHA": SHA,
                "GITHUB_REPOSITORY": "elsa-workflows/elsa-foundation", "GITHUB_RUN_NUMBER": "10",
                "MAIN_COMMIT": SHA,
                "GITHUB_STEP_SUMMARY": str(directory / "summary"), "PUBLISH_LOG": str(publish_log),
                "AMD64": amd64, "ARM64": arm64, "LOCAL_ID": local_id, "INDEX": index, "PREVIOUS": previous,
                "NATIVE_READBACK": arm64, "INDEX_READBACK": index,
            }
            for arch in ("amd64", "arm64"):
                bundle = directory / "artifacts" / arch
                bundle.mkdir(parents=True)
                data = f"{arch} archive".encode()
                (bundle / "image.tar.gz").write_bytes(data)
                (bundle / "build.json").write_text(json.dumps({
                    "image": image, "platform": f"linux/{arch}", "foundationImage": base, "commit": SHA,
                    "imageId": local_id, "archiveSha256": hashlib.sha256(data).hexdigest(),
                }))
                (bundle / "smoke.jsonl").write_text(json.dumps({"imageId": local_id}) + "\n")

            stage = "Push the tested native images under architecture-specific tags"
            publish = "Verify the native image pair and publish the existing release tags"
            manual = run_step(stage, env | {"RECORD_ESSENTIALS_RECEIPT": "false", "NATIVE_READBACK": retag})
            self.assertEqual(0, manual.returncode, manual.stderr)
            self.assertFalse(receipt.exists())
            rejected = run_step(stage, env | {"NATIVE_READBACK": retag})
            self.assertNotEqual(0, rejected.returncode)
            self.assertFalse(receipt.exists())
            staged = run_step(stage, env)
            self.assertEqual(0, staged.returncode, staged.stderr)
            saved = receipt.read_text()
            self.assertEqual({"repository": repository, "version": version, "commit": SHA,
                              "nativeDigests": {"amd64": amd64, "arm64": arm64}}, json.loads(saved))

            receipt.write_text(saved + saved)
            self.assertNotEqual(0, run_step(publish, env).returncode)
            self.assertFalse(publish_log.exists())
            receipt.write_text(saved)
            self.assertNotEqual(0, run_step(publish, env | {"INDEX_READBACK": retag}).returncode)
            self.assertEqual(1, len(publish_log.read_text().splitlines()))
            publish_log.write_text("")
            stale = run_step(publish, env | {"MAIN_COMMIT": "b" * 40})
            self.assertEqual(0, stale.returncode, stale.stderr)
            self.assertNotIn("latest written", publish_log.read_text())
            publish_log.write_text("")
            promoted = run_step(publish, env)
            self.assertEqual(0, promoted.returncode, promoted.stderr)
            self.assertIn(f"{repository}:4.0.0-preview.10", publish_log.read_text())
            self.assertIn(f"{repository}:sha-{SHA[:7]}", publish_log.read_text())
            self.assertIn("latest written", publish_log.read_text())


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
