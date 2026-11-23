import io
import json
import tarfile
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

from studio_archives import inspect, sha256
from studio_publish_npm import ORDER, publish


def data(body):
    return json.dumps(body).encode()


def library(name, file, declarations=()):
    return data({"bomFormat": "CycloneDX", "specVersion": "1.6", "metadata": {"component": {
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
                else:
                    with self.assertRaisesRegex(ValueError, "payload mismatch"):
                        inspect(path)
