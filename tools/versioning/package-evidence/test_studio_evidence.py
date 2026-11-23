import io
import json
import os
import subprocess
import sys
import tarfile
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

from studio_archives import inspect, main as inspect_cli, nuget_payload, sha256
from studio_publish_npm import ORDER, publish


def data(body):
    return json.dumps(body).encode()


def library(name, file, declarations=()):
    return data({"bomFormat": "CycloneDX", "specVersion": "1.6", "metadata": {"properties": [
        {"name": "elsa:" + key, "value": val} for key, val in (("repository", "owner/repo"), ("commit", "abc"),
        ("producer-run", "42"), ("producer-attempt", "1"))], "component": {
        "name": name, "version": "4.0.0", "properties": list(declarations),
        "components": [{"type": "file", "name": file, "hashes": [{"alg": "SHA-256", "content": sha256(b"content")}]}]}}})


def npm(directory, short, body=b"content", peer="^19"):
    name = "@elsa-workflows/" + short
    path = directory / (short + ".tgz")
    file = "dist/index.js"
    entries = {"package/package.json": data({"name": name, "version": "4.0.0", "peerDependencies": {"react": peer}}),
               "package/" + file: body,
               "package/dist/sbom.cdx.json": library(name, file, [{"name": "elsa:peerDependencies:react", "value": "^19"}])}
    with tarfile.open(path, "w:gz") as archive:
        for filename, content in entries.items():
            info = tarfile.TarInfo(filename)
            info.size = len(content)
            archive.addfile(info, io.BytesIO(content))
    return path


class StudioEvidence(unittest.TestCase):
    def test_npm_archive_and_release_order(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)
            rows = [inspect(npm(path, name))[0] for name in reversed(ORDER)]
            (path / "npm-subjects.json").write_bytes(data(rows))
            # Simulate the first publication and its readback without registry access.
            answers = [answer for row in reversed(rows) for answer in (None, row["integrity"])]
            with patch("studio_publish_npm.observed", side_effect=answers), patch("studio_publish_npm.subprocess.run") as run:
                publish(path, "https://example.invalid/", "preview", False)
                self.assertEqual([Path(call.args[0][2]).stem for call in run.call_args_list], ORDER)
            with patch("studio_publish_npm.observed", return_value="sha512-wrong"), self.assertRaisesRegex(ValueError, "integrity differs"):
                publish(path, "https://example.invalid/", "preview", False)
            with self.assertRaisesRegex(ValueError, "payload mismatch"):
                inspect(npm(path, ORDER[0], body=b"changed"))
            with self.assertRaisesRegex(ValueError, "declarations differ"):
                inspect(npm(path, ORDER[0], peer="^18"))

    def test_snapshot_is_required_after_signing(self):
        script = Path(__file__).with_name("studio_snapshot.sh")
        with tempfile.TemporaryDirectory() as folder:
            target = Path(folder)
            gh = target / "gh"
            gh.write_text('#!/bin/sh\ncase "$*" in *artifacts*) echo "$SNAPSHOTS";; *jobs*) echo "$PRIOR_SIGNING_JOB";; *) exit 1;; esac\n')
            gh.chmod(0o755)
            for snapshots, signed, expected in [
                ([], "", ""), ([{"id": 123, "expired": False}], "37", "123"),
                ([], "37", None), ([{"id": 123, "expired": True}], "37", None)
            ]:
                output = target / "output"
                output.write_text("")
                env = dict(os.environ, PATH=folder + os.pathsep + os.environ["PATH"], GITHUB_OUTPUT=str(output),
                           GITHUB_RUN_ATTEMPT="2", GITHUB_REPOSITORY="owner/repo", GITHUB_RUN_ID="42",
                           SNAPSHOTS=json.dumps({"total_count": len(snapshots), "artifacts": snapshots}), PRIOR_SIGNING_JOB=signed)
                result = subprocess.run(["bash", str(script)], env=env, capture_output=True)
                if expected is None:
                    self.assertNotEqual(result.returncode, 0)
                else:
                    self.assertEqual(result.returncode, 0, result.stderr)
                    self.assertEqual(output.read_text().strip(), "id=" + expected)

    def test_signing_rejects_another_attempt(self):
        with tempfile.TemporaryDirectory() as folder, patch.dict("os.environ", VERSION="4.0.0", GITHUB_REPOSITORY="owner/repo",
                GITHUB_SHA="abc", GITHUB_RUN_ID="42", GITHUB_RUN_ATTEMPT="1"):
            npm(Path(folder), ORDER[0])
            for action in ["--write", "--check"]:
                with patch.object(sys, "argv", ["studio_archives.py", folder, action, "npm"]):
                    inspect_cli()
            with patch.dict("os.environ", GITHUB_RUN_ATTEMPT="2"), patch.object(sys, "argv", ["studio_archives.py", folder, "--check", "npm"]):
                with self.assertRaisesRegex(ValueError, "producer-attempt"):
                    inspect_cli()
                with patch.object(sys, "argv", ["studio_archives.py", folder, "--check", "npm", "--signed-retry"]):
                    inspect_cli()
            with patch.dict("os.environ", GITHUB_RUN_ATTEMPT="0"), patch.object(sys, "argv", [
                    "studio_archives.py", folder, "--check", "npm", "--signed-retry"]):
                with self.assertRaisesRegex(ValueError, "Invalid signed producer attempt"):
                    inspect_cli()

    def test_nuget_static_assets(self):
        file = "staticwebassets/studio/index.js"
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "Studio.nupkg"
            for body in [b"content", b"changed"]:
                with zipfile.ZipFile(path, "w") as archive:
                    archive.writestr("Studio.nuspec", "<package><metadata><id>Studio</id><version>4.0.0</version></metadata></package>")
                    archive.writestr(file, body)
                    archive.writestr("sbom/frontend.cdx.json", library("Studio", file))
                if body == b"content":
                    self.assertEqual(inspect(path)[0]["id"], "Studio")
                    before = nuget_payload(path)
                    with zipfile.ZipFile(path, "a") as archive:
                        archive.writestr(".signature.p7s", b"repository signature placeholder")
                    self.assertEqual(nuget_payload(path), before)
                else:
                    self.assertNotEqual(nuget_payload(path), before)
                    with self.assertRaisesRegex(ValueError, "payload mismatch"):
                        inspect(path)
