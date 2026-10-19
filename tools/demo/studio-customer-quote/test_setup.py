"""Safety checks for the no-write setup plan."""
import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch

import setup_demo


class SetupPlanTests(unittest.TestCase):
    def test_default_plan_needs_no_credentials_and_uses_the_backend_endpoint(self):
        with patch.dict("os.environ", {}, clear=True):
            result = subprocess.run([sys.executable, str(Path(setup_demo.__file__))], capture_output=True, text=True, check=True)
        plan = json.loads(result.stdout)
        self.assertFalse(plan["writes"])
        self.assertEqual(plan["endpoint"], "POST http://127.0.0.1:5595/workflows/http/quotes")

    def test_authentication_is_never_sent_to_an_external_origin(self):
        with patch("setup_demo.build_opener") as opener:
            with self.assertRaises(setup_demo.DemoSetupError):
                setup_demo.ElsaApi("fixture-user", "fixture-password", "https://external.invalid")
        opener.assert_not_called()


if __name__ == "__main__":
    unittest.main()
