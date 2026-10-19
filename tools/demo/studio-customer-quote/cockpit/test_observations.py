import hashlib
import json
import re
import unittest
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone

from observations import CredentialNotRevealable, ObservationStore, RequestNotFound


class ObservationStoreTests(unittest.TestCase):
    def make_store(self, **kwargs):
        return ObservationStore(
            known_credentials={"demo-v1-secret": "v1", "demo-v2-secret": "v2"},
            **kwargs,
        )

    def record(self, store, token="demo-v2-secret", **kwargs):
        headers = {"Accept": "application/json", "Authorization": f"Bearer {token}"}
        return store.record_request(
            method="GET",
            path="/api/customers/C-1042?access_token=must-not-be-kept",
            status=200,
            duration_ms=12.3,
            request_headers=headers,
            response_headers={"Content-Type": "application/json", "Set-Cookie": "secret-cookie"},
            response_body={"customerId": "C-1042", "name": "Alex Morgan"},
            **kwargs,
        )

    def test_rejects_ambiguous_reveal_configuration(self):
        with self.assertRaises(ValueError):
            ObservationStore({"first": "v1", "second": "v1"})

    def test_records_newest_first_with_bounded_history_and_timing(self):
        store = self.make_store(max_entries=3)
        entries = [self.record(store, token=f"unknown-{i}") for i in range(5)]

        state = store.snapshot()
        self.assertEqual([item["id"] for item in state["requests"]], [e["id"] for e in reversed(entries[-3:])])
        self.assertEqual(len({item["id"] for item in state["requests"]}), 3)
        newest = state["requests"][0]
        self.assertEqual(newest["method"], "GET")
        self.assertEqual(newest["path"], "/api/customers/C-1042")
        self.assertEqual(newest["durationMs"], 12.3)
        self.assertEqual(newest["status"], 200)
        self.assertRegex(newest["timestamp"], r"^\d{4}-\d\d-\d\dT.*Z$")

    def test_allowlisted_headers_are_kept_and_authorization_is_masked(self):
        store = self.make_store()
        item = store.record_request(
            method="GET", path="/api/x", status=200, duration_ms=1,
            request_headers={
                "aCcEpT": "application/json", "Authorization": "Bearer demo-v2-secret",
                "Cookie": "session=private", "X-Api-Key": "private-key", "X-Demo-Cockpit": "1",
                "X-Demo-Request": "demo-v2-secret",
            },
            response_headers={"Content-Type": "application/json", "Set-Cookie": "private-cookie"},
        )

        self.assertEqual(item["headers"], {
            "Accept": "application/json", "Authorization": "Bearer ••••••••",
            "X-Demo-Request": "[redacted]",
        })
        self.assertEqual(item["response"]["headers"], {"Content-Type": "application/json"})
        self.assertEqual(item["authentication"], {
            "accepted": True, "version": "v2",
            "fingerprint": hashlib.sha256(b"demo-v2-secret").hexdigest()[:8], "canReveal": True,
        })

    def test_only_a_matched_demo_credential_can_be_explicitly_revealed(self):
        store = self.make_store()
        known = self.record(store)
        unknown = self.record(store, token="customer-supplied-credential")

        self.assertEqual(store.reveal_authorization(known["id"]), "Bearer demo-v2-secret")
        with self.assertRaises(CredentialNotRevealable):
            store.reveal_authorization(unknown["id"])
        with self.assertRaises(RequestNotFound):
            store.reveal_authorization("missing")

    def test_unknown_credential_is_fingerprinted_but_never_retained(self):
        store = self.make_store()
        secret = "do-not-retain-this-unknown-token"
        item = self.record(store, token=secret)
        malformed = store.record_request(
            method="GET", path="/api/x", status=401, duration_ms=1,
            request_headers={"Authorization": "single-word-unknown-secret"},
        )

        self.assertEqual(item["authentication"]["accepted"], False)
        self.assertIsNone(item["authentication"]["version"])
        self.assertEqual(item["authentication"]["fingerprint"], hashlib.sha256(secret.encode()).hexdigest()[:8])
        self.assertFalse(item["authentication"]["canReveal"])
        self.assertEqual(malformed["headers"]["Authorization"], "••••••••")
        self.assertEqual(malformed["authentication"]["fingerprint"], hashlib.sha256(b"single-word-unknown-secret").hexdigest()[:8])
        self.assertNotIn(secret, json.dumps(store.snapshot()))
        self.assertNotIn("single-word-unknown-secret", json.dumps(store.snapshot()))
        self.assertNotIn(secret, repr(vars(store)))

    def test_timestamp_can_be_supplied_and_snapshots_are_detached(self):
        store = self.make_store()
        supplied = datetime(2026, 10, 19, 12, 30, tzinfo=timezone.utc)
        body = {"name": "<script>alert('x')</script>"}
        item = store.record_request(
            method="GET", path="/api/x", status=200, duration_ms=0,
            request_headers={}, response_body=body, timestamp=supplied,
        )
        body["name"] = "changed"
        self.assertEqual(item["timestamp"], "2026-10-19T12:30:00.000Z")
        self.assertEqual(item["response"]["body"]["name"], "<script>alert('x')</script>")
        item["response"]["body"]["name"] = "mutated snapshot"
        self.assertEqual(store.snapshot()["requests"][0]["response"]["body"]["name"], "<script>alert('x')</script>")

    def test_quote_observation_is_separate_from_customer_requests(self):
        store = self.make_store()
        quote = store.record_quote(
            status=200, duration_ms=81.5, request={"subtotal": 40, "shipping": 2},
            body={"customerId": "C-1042", "total": 42},
        )

        state = store.snapshot()
        self.assertEqual(state["requests"], [])
        self.assertEqual(state["quote"], quote)
        self.assertEqual(quote["request"], {"subtotal": 40, "shipping": 2})
        self.assertEqual(quote["body"]["total"], 42)
        self.assertTrue(re.match(r"^\d{4}-\d\d-\d\dT.*Z$", quote["timestamp"]))

    def test_concurrent_requests_remain_unique_and_bounded(self):
        store = self.make_store()
        with ThreadPoolExecutor(max_workers=12) as pool:
            items = list(pool.map(lambda i: self.record(store, token=f"unknown-{i}"), range(240)))

        requests = store.snapshot()["requests"]
        self.assertEqual(len(requests), 100)
        self.assertEqual(len({item["id"] for item in requests}), 100)
        self.assertEqual(len({item["id"] for item in items}), 240)


if __name__ == "__main__":
    unittest.main()
