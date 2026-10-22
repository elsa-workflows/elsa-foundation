"""Loopback HTTP integration tests; all credentials and upstreams are synthetic."""

import http.client
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request
import unittest
from unittest.mock import patch

import server as cockpit_server


FAKE_CREDENTIALS = {"test-only-key-v1": "v1", "test-only-key-v2": "v2"}


class FakeWorkflowHandler(BaseHTTPRequestHandler):
    def do_POST(self):
        length = int(self.headers.get("Content-Length", "0"))
        request_body = json.loads(self.rfile.read(length))
        self.server.calls.append((self.command, self.path, request_body))
        body = self.server.response_body
        self.send_response(self.server.response_status)
        if self.server.redirect_location:
            self.send_header("Location", self.server.redirect_location)
        self.send_header("Content-Type", self.server.response_content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        if body:
            self.wfile.write(body)

    def do_GET(self):
        self.server.calls.append((self.command, self.path, None))
        self.send_response(200)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def log_message(self, format, *args):
        return


class FakeProxyHandler(BaseHTTPRequestHandler):
    def do_POST(self):
        self.server.calls.append(self.path)
        self.send_response(502)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def log_message(self, format, *args):
        return


class CockpitServerIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.workflow = ThreadingHTTPServer(("127.0.0.1", 0), FakeWorkflowHandler)
        self.workflow.daemon_threads = True
        self.workflow.calls = []
        self.workflow.response_status = 200
        self.workflow.response_content_type = "application/json"
        self.workflow.redirect_location = None
        self.workflow.response_body = json.dumps({
            "customerId": "C-1042", "name": "Synthetic Customer", "total": 42
        }).encode()
        self.workflow_thread = threading.Thread(target=self.workflow.serve_forever, daemon=True)
        self.workflow_thread.start()
        self.workflow_url = (
            f"http://127.0.0.1:{self.workflow.server_address[1]}"
            "/workflows/http/quotes"
        )

        # The wrapped demo API normally appends its safe call ledger to disk. Keep these
        # tests entirely in-memory while still exercising its real request handler.
        self.append_call_patch = patch.object(cockpit_server.demo, "append_call")
        self.safe_log_patch = patch.object(cockpit_server.demo.DemoApiHandler, "_safe_log")
        self.append_call_patch.start()
        self.safe_log_patch.start()

        self.cockpit = cockpit_server.make_server(0, FAKE_CREDENTIALS, self.workflow_url)
        self.cockpit_thread = threading.Thread(target=self.cockpit.serve_forever, daemon=True)
        self.cockpit_thread.start()
        self.port = self.cockpit.server_address[1]

    def tearDown(self):
        self.cockpit.shutdown()
        self.cockpit.server_close()
        self.cockpit_thread.join(timeout=2)
        self.workflow.shutdown()
        self.workflow.server_close()
        self.workflow_thread.join(timeout=2)
        self.safe_log_patch.stop()
        self.append_call_patch.stop()

    def request(self, method, path, *, body=None, headers=None):
        request_headers = {"Accept": "application/json", **(headers or {})}
        encoded = None
        if body is not None:
            encoded = json.dumps(body).encode("utf-8")
            request_headers.setdefault("Content-Type", "application/json")
        connection = http.client.HTTPConnection("127.0.0.1", self.port, timeout=3)
        try:
            connection.request(method, path, body=encoded, headers=request_headers)
            response = connection.getresponse()
            data = response.read()
            try:
                payload = json.loads(data)
            except (ValueError, UnicodeDecodeError):
                payload = data
            return response.status, dict(response.getheaders()), payload
        finally:
            connection.close()

    def post_headers(self, **extra):
        return {
            "Origin": f"http://127.0.0.1:{self.port}",
            "X-Demo-Cockpit": "1",
            **extra,
        }

    def test_real_customer_requests_record_success_and_rejection_without_raw_keys(self):
        customer_path = "/api/customers/C-1042"
        status, _, success = self.request(
            "GET", customer_path,
            headers={"Authorization": "Bearer test-only-key-v2"},
        )
        self.assertEqual(status, 200)
        self.assertEqual(success["customerId"], "C-1042")

        unknown = "unknown-customer-key-never-retain"
        status, _, rejected = self.request(
            "GET", customer_path,
            headers={"Authorization": f"Bearer {unknown}"},
        )
        self.assertEqual(status, 401)
        self.assertEqual(rejected, {"error": "unauthorized"})

        status, _, state = self.request("GET", "/cockpit/state")
        self.assertEqual(status, 200)
        self.assertEqual(len(state["requests"]), 2)
        self.assertEqual([item["status"] for item in state["requests"]], [401, 200])
        rejected_observation, accepted_observation = state["requests"]
        self.assertFalse(rejected_observation["authentication"]["accepted"])
        self.assertFalse(rejected_observation["authentication"]["canReveal"])
        self.assertEqual(accepted_observation["authentication"]["version"], "v2")
        self.assertTrue(accepted_observation["authentication"]["canReveal"])
        self.assertEqual(accepted_observation["headers"]["Authorization"], "Bearer ••••••••")
        serialized = json.dumps(state)
        self.assertNotIn("test-only-key-v2", serialized)
        self.assertNotIn(unknown, serialized)

    def test_authentication_display_matches_rejected_malformed_bearer_header(self):
        status, _, _ = self.request("GET", "/api/customers/C-1042", headers={"Authorization": "Bearer\ttest-only-key-v2"})
        self.assertEqual(status, 401)
        _, _, state = self.request("GET", "/cockpit/state")
        auth = state["requests"][0]["authentication"]
        self.assertFalse(auth["accepted"])
        self.assertFalse(auth["canReveal"])

    def test_reveal_requires_a_recognized_observation_and_explicit_demo_header(self):
        self.request("GET", "/api/customers/C-1042", headers={"Authorization": "Bearer test-only-key-v1"})
        self.request("GET", "/api/customers/C-1042", headers={"Authorization": "Bearer unknown-key"})
        _, _, state = self.request("GET", "/cockpit/state")
        known_id, unknown_id = state["requests"][1]["id"], state["requests"][0]["id"]

        status, _, payload = self.request(
            "POST", "/cockpit/reveal", body={"requestId": known_id}, headers=self.post_headers()
        )
        self.assertEqual(status, 200)
        self.assertEqual(payload, {"authorization": "Bearer test-only-key-v1"})

        status, _, payload = self.request(
            "POST", "/cockpit/reveal", body={"requestId": unknown_id}, headers=self.post_headers()
        )
        self.assertEqual(status, 409)
        self.assertNotIn("unknown-key", json.dumps(payload))

        status, _, _ = self.request(
            "POST", "/cockpit/reveal", body={"requestId": "not-in-history"}, headers=self.post_headers()
        )
        self.assertEqual(status, 404)

        status, _, _ = self.request("POST", "/cockpit/reveal", body={"requestId": known_id}, headers={
            "Origin": f"http://127.0.0.1:{self.port}"
        })
        self.assertEqual(status, 403)

    def test_loopback_host_origin_and_fetch_site_guards(self):
        status, _, _ = self.request("GET", "/cockpit/state", headers={"Host": "evil.example"})
        self.assertEqual(status, 403)

        status, _, _ = self.request("POST", "/cockpit/quote", body={"subtotal": 1, "shipping": 1}, headers=self.post_headers(Origin="http://evil.example"))
        self.assertEqual(status, 403)

        status, _, _ = self.request("GET", "/cockpit/state", headers={"Sec-Fetch-Site": "cross-site"})
        self.assertEqual(status, 403)
        self.assertEqual(self.workflow.calls, [])

    def test_cockpit_static_page_uses_same_origin_csp_and_no_store(self):
        for path in ("/cockpit", "/cockpit/app.js", "/cockpit/style.css"):
            with self.subTest(path=path):
                status, headers, _ = self.request("GET", path)
                self.assertEqual(status, 200)
                csp = headers["Content-Security-Policy"]
                self.assertIn("default-src 'self'", csp)
                self.assertIn("script-src 'self'", csp)
                self.assertIn("object-src 'none'", csp)
                self.assertNotIn("unsafe-inline", csp)
                self.assertEqual(headers["Cache-Control"], "no-store")
                self.assertNotIn("Access-Control-Allow-Origin", headers)

    def test_quote_validation_prevents_upstream_calls_and_success_uses_fixed_local_route(self):
        invalid_bodies = [
            {"subtotal": 40.5, "shipping": 2},
            {"subtotal": -1, "shipping": 2},
            {"subtotal": 1_000_001, "shipping": 2},
            {"subtotal": True, "shipping": 2},
            {"subtotal": 40},
            {"subtotal": 40, "shipping": 2, "url": "https://untrusted.invalid"},
        ]
        for body in invalid_bodies:
            with self.subTest(body=body):
                status, _, _ = self.request("POST", "/cockpit/quote", body=body, headers=self.post_headers())
                self.assertEqual(status, 400)
                self.assertEqual(self.workflow.calls, [])

        status, _, quote = self.request(
            "POST", "/cockpit/quote", body={"subtotal": 40, "shipping": 2}, headers=self.post_headers()
        )
        self.assertEqual(status, 200)
        self.assertEqual(self.workflow.calls, [
            ("POST", "/workflows/http/quotes", {"subtotal": 40, "shipping": 2})
        ])
        self.assertEqual(quote["status"], 200)
        self.assertEqual(quote["request"], {"subtotal": 40, "shipping": 2})
        self.assertEqual(quote["body"]["total"], 42)
        self.assertTrue(quote["responseValid"])
        _, _, state = self.request("GET", "/cockpit/state")
        self.assertEqual(state["requests"], [])
        self.assertEqual(state["quote"], quote)

    def test_quote_preserves_redirect_status_without_following_location(self):
        self.workflow.response_status = 302
        self.workflow.response_content_type = "text/plain"
        self.workflow.redirect_location = "/redirect-target"
        self.workflow.response_body = b"redirect response"

        status, _, quote = self.request(
            "POST", "/cockpit/quote", body={"subtotal": 40, "shipping": 2}, headers=self.post_headers()
        )

        self.assertEqual(status, 200)
        self.assertEqual(quote["status"], 302)
        self.assertFalse(quote["responseValid"])
        self.assertEqual(self.workflow.calls, [
            ("POST", "/workflows/http/quotes", {"subtotal": 40, "shipping": 2})
        ])

    def test_quote_request_bypasses_configured_environment_proxy(self):
        proxy = ThreadingHTTPServer(("127.0.0.1", 0), FakeProxyHandler)
        proxy.daemon_threads = True
        proxy.calls = []
        proxy_thread = threading.Thread(target=proxy.serve_forever, daemon=True)
        proxy_thread.start()
        proxy_url = f"http://127.0.0.1:{proxy.server_address[1]}"
        proxy_environment = {
            "HTTP_PROXY": proxy_url,
            "http_proxy": proxy_url,
            "NO_PROXY": "",
            "no_proxy": "",
            "ALL_PROXY": "",
            "all_proxy": "",
        }
        try:
            with patch.dict(os.environ, proxy_environment):
                status, _, quote = self.request(
                    "POST", "/cockpit/quote", body={"subtotal": 40, "shipping": 2}, headers=self.post_headers()
                )
        finally:
            proxy.shutdown()
            proxy.server_close()
            proxy_thread.join(timeout=2)

        self.assertEqual(status, 200)
        self.assertEqual(quote["status"], 200)
        self.assertEqual(proxy.calls, [])
        self.assertEqual(self.workflow.calls, [
            ("POST", "/workflows/http/quotes", {"subtotal": 40, "shipping": 2})
        ])

    def test_quote_marks_non_json_success_response_invalid(self):
        self.workflow.response_status = 200
        self.workflow.response_content_type = "text/plain"
        self.workflow.response_body = b"not JSON"

        status, _, quote = self.request(
            "POST", "/cockpit/quote", body={"subtotal": 40, "shipping": 2}, headers=self.post_headers()
        )

        self.assertEqual(status, 200)
        self.assertEqual(quote["status"], 200)
        self.assertFalse(quote["responseValid"])
        self.assertEqual(quote["body"], {"error": "The workflow did not return a JSON response."})
        self.assertEqual(len(self.workflow.calls), 1)

    def test_workflow_target_rejects_external_hosts_and_wrong_paths(self):
        with self.assertRaises(ValueError):
            cockpit_server.make_server(0, FAKE_CREDENTIALS, "https://workflow.example/workflows/http/quotes")
        with self.assertRaises(ValueError):
            cockpit_server.make_server(0, FAKE_CREDENTIALS, self.workflow_url.replace("/workflows/http/quotes", "/other"))
        for invalid_port in ("abc", "0", "-1", "65536"):
            with self.subTest(port=invalid_port), self.assertRaises(ValueError):
                cockpit_server.make_server(0, FAKE_CREDENTIALS, f"http://localhost:{invalid_port}/workflows/http/quotes")

    def test_fresh_package_copy_starts_without_the_original_demo_checkout(self):
        source_dir = Path(__file__).resolve().parent
        runtime_files = (
            "server.py", "observations.py", "demo_api.py", "cockpit.html",
            "app.js", "style.css", "elsa-icon.png",
        )
        with tempfile.TemporaryDirectory() as temporary_dir:
            temporary_root = Path(temporary_dir)
            package_dir = temporary_root / "cockpit"
            package_dir.mkdir()
            for filename in runtime_files:
                shutil.copy2(source_dir / filename, package_dir / filename)

            isolated_state = temporary_root / "isolated-state"
            with socket.socket() as port_probe:
                port_probe.bind(("127.0.0.1", 0))
                port = port_probe.getsockname()[1]

            environment = os.environ.copy()
            environment.pop("PYTHONPATH", None)
            environment["DEMO_CUSTOMER_API_TOKEN_V1"] = "test-only-key-v1"
            environment["DEMO_CUSTOMER_API_TOKEN_V2"] = "test-only-key-v2"
            environment["ELSA_DEMO_STATE_DIR"] = str(isolated_state)
            process = subprocess.Popen(
                [sys.executable, "server.py", "--port", str(port)],
                cwd=package_dir,
                env=environment,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
            )
            base_url = f"http://127.0.0.1:{port}"
            deadline = time.monotonic() + 5
            try:
                state_response = None
                while time.monotonic() < deadline and process.poll() is None:
                    try:
                        with urllib.request.urlopen(f"{base_url}/cockpit/state", timeout=0.5) as response:
                            state_response = json.loads(response.read())
                        break
                    except (urllib.error.URLError, TimeoutError):
                        time.sleep(0.05)
                if process.poll() is not None:
                    stdout, stderr = process.communicate(timeout=1)
                    self.fail(f"Fresh package server exited during startup. stdout={stdout!r}, stderr={stderr!r}")
                self.assertIsNotNone(state_response, "fresh package server did not answer its state endpoint")
                self.assertEqual(state_response["requests"], [])

                request = urllib.request.Request(
                    f"{base_url}/api/customers/C-1042",
                    headers={"Authorization": "Bearer test-only-key-v2"},
                )
                with urllib.request.urlopen(request, timeout=1) as response:
                    self.assertEqual(response.status, 200)
                    self.assertEqual(json.loads(response.read())["customerId"], "C-1042")

                ledger = isolated_state / "demo-api-calls.jsonl"
                self.assertTrue(ledger.exists())
                self.assertFalse((package_dir / "state" / ledger.name).exists())
                ledger_text = ledger.read_text(encoding="utf-8")
                self.assertIn('"credentialVersion":"v2"', ledger_text)
                self.assertNotIn("test-only-key-v2", ledger_text)
            finally:
                if process.poll() is None:
                    process.terminate()
                process.communicate(timeout=3)


if __name__ == "__main__":
    unittest.main()
