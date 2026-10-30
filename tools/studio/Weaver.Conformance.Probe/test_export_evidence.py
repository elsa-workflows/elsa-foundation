import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from export_evidence import export_evidence


class ExportEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / "run"
        self.root.mkdir()
        self.destination = self.root.parent / "export"

    def ref(self, name, value):
        path = self.root / name
        path.write_text(json.dumps(value))
        return {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}

    def report(self, value):
        (self.root / "report.json").write_text(json.dumps(value))

    def test_exports_nested_actual_refs_and_preserves_failed_child(self):
        body = self.ref("owner.json", {"revision": "r1"})
        receipt = self.ref("case.json", {"status": "failed", "ownerAfter": body})
        self.report({"status": "passed", "cases": [{"status": "failed", "receipt": receipt}]})
        index = export_evidence(self.root, self.destination)
        self.assertEqual({"report.json", "case.json", "owner.json"}, set(index["files"]))
        self.assertEqual("failed", json.loads((self.destination / "case.json").read_text())["status"])

    def test_does_not_copy_unreferenced_runtime_credentials(self):
        self.ref("bearer-private.json", {"accessToken": "generated-own-credential"})
        self.report({"status": "failed"})
        index = export_evidence(self.root, self.destination)
        self.assertEqual({"report.json"}, set(index["files"]))
        self.assertEqual("failed", index["qualificationStatus"])

    def test_refuses_referenced_credentials_even_with_innocent_filename(self):
        ref = self.ref("response.json", {"accessToken": "generated-own-credential"})
        self.report({"evidence": ref})
        with self.assertRaisesRegex(ValueError, "Credential"):
            export_evidence(self.root, self.destination)
        self.assertFalse(self.destination.exists())

    def test_refuses_private_auth_file_and_changed_evidence(self):
        for name, change in (("storage-state.json", False), ("owner.json", True)):
            with self.subTest(name=name):
                ref = self.ref(name, {"value": "public"})
                self.report({"evidence": ref})
                if change:
                    (self.root / name).write_text("changed")
                with self.assertRaises(ValueError):
                    export_evidence(self.root, self.destination)
                self.assertFalse(self.destination.exists())

    def test_refuses_foreign_or_symlinked_reference(self):
        path = self.root.parent / "foreign.json"
        path.write_text("{}")
        link = self.root / "alias.json"
        link.symlink_to(path)
        for selected in (path, link):
            with self.subTest(path=selected):
                self.report({"evidence": {"path": str(selected), "sha256": hashlib.sha256(b"{}").hexdigest()}})
                with self.assertRaisesRegex(ValueError, "escaped"):
                    export_evidence(self.root, self.destination)
                self.assertFalse(self.destination.exists())

    def test_refuses_token_inside_raw_text_evidence(self):
        path = self.root / "response.body"
        path.write_text("eyJabcdefghijk.eyJabcdefghijk.abcdefghijk")
        self.report({"evidence": {"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}})
        with self.assertRaisesRegex(ValueError, "Credential"):
            export_evidence(self.root, self.destination)
        self.assertFalse(self.destination.exists())

    def test_treats_module_payload_as_opaque_and_checks_nested_http_credentials(self):
        unrelated = self.ref("unrelated.json", {"message": "must not be selected"})
        payload = self.ref("owner.json", {"value": {"opaqueModuleData": unrelated}})
        self.report({"evidence": payload})
        index = export_evidence(self.root, self.destination)
        self.assertNotIn("unrelated.json", index["files"])
        other_destination = self.root.parent / "unsafe-export"
        response = self.ref("http.json", {"rawBody": json.dumps({"accessToken": "generated-own-credential"})})
        self.report({"evidence": response})
        with self.assertRaisesRegex(ValueError, "Credential"):
            export_evidence(self.root, other_destination)
        self.assertFalse(other_destination.exists())


if __name__ == "__main__":
    unittest.main()
