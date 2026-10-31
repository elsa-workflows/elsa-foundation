"""Exercise actual author templates; these checks do not claim package/runtime proof."""
from pathlib import Path
import tempfile
import unittest

from artifacts import ConformanceRun


class MaterializationTests(unittest.TestCase):
    def test_studio_author_has_explicit_admission_input_and_can_build_a_real_incompatible_variant(self):
        with tempfile.TemporaryDirectory() as directory:
            run = ConformanceRun.__new__(ConformanceRun)
            run.root = Path(directory)
            run.version = "4.0.0-witness"
            run.sdk = run.root / "actual-sdk.tgz"
            run.pin = lambda name: "1.0.0"
            run.config = lambda source: None
            run.assert_immutable_inputs = lambda: None  # This unit only checks template materialization.
            author = run.materialize("author", "Resource.Studio")
            self.assertIn('RequiredSdkVersion = "*"', (author / "AcmeHomeStudioFeature.cs").read_text())
            incompatible = run.materialize("incompatible", "Resource.Studio", {"__REQUIRED_SDK_VERSION__": "999.0.0"})
            self.assertIn('RequiredSdkVersion = "999.0.0"', (incompatible / "AcmeHomeStudioFeature.cs").read_text())
            self.assertEqual((author / "Client/src/module.tsx").read_bytes(), (incompatible / "Client/src/module.tsx").read_bytes())


if __name__ == "__main__":
    unittest.main()
