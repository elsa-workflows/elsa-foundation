"""Safety checks for the no-write setup plan."""
import json
from pathlib import Path
import subprocess
import sys
import unittest
from urllib.request import ProxyHandler
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

    def test_local_api_opener_disables_environment_proxies(self):
        proxy = "http://127.0.0.1:8765"
        proxy_environment = {
            "HTTP_PROXY": proxy,
            "http_proxy": proxy,
            "NO_PROXY": "",
            "no_proxy": "",
        }
        with patch.dict("os.environ", proxy_environment), \
                patch("setup_demo.http.cookiejar.CookieJar", return_value=[object()]), \
                patch("setup_demo.build_opener") as build_opener, \
                patch.object(setup_demo.ElsaApi, "request"):
            setup_demo.ElsaApi("fixture-user", "fixture-password")

        proxy_handler = next(
            handler for handler in build_opener.call_args.args
            if isinstance(handler, ProxyHandler)
        )
        self.assertEqual(proxy_handler.proxies, {})


if __name__ == "__main__":
    unittest.main()
