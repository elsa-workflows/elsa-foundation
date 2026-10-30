"""Fail-closed coordinator boundaries; these tests are not runtime witness evidence."""
from pathlib import Path
import json
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from agent_transport import Response, AgentTransportError
from journey_runtime import expect_error, same, BASE_GRANTS
from journeys import _unchanged, _seed_principals, qualify_distributions


class JourneyBoundariesTests(unittest.TestCase):
    def test_owner_comparison_does_not_coerce_json_boolean_to_integer(self):
        with self.assertRaises(RuntimeError):
            _unchanged({"value": {"visible": True}}, {"value": {"visible": 1}})
        self.assertFalse(same({"a": False}, {"a": 0}))

    def test_owner_comparison_keeps_unknown_nested_values(self):
        before = {"revision": "r1", "value": {"widgets": [{"id": "other", "options": {"unknown": [True, None, {"x": 2}]}}]}}
        after = json.loads(json.dumps(before))
        self.assertTrue(all(_unchanged(before, after).values()))
        after["value"]["widgets"][0]["options"]["unknown"].append("lost-field")
        with self.assertRaises(RuntimeError): _unchanged(before, after)

    def test_identical_http_failure_status_cannot_mask_wrong_cause(self):
        def response(code, status=403):
            return Response("POST", "/_elsa/agent/proposals/p/execute", status, {},
                            json.dumps({"error": {"code": code, "message": "observed native refusal"}}).encode(), {})
        self.assertEqual("agent.proposal.forbidden", expect_error(response("agent.proposal.forbidden"), 403, "agent.proposal.forbidden")["error"]["code"])
        with self.assertRaises(RuntimeError): expect_error(response("agent.proposal.expired"), 403, "agent.proposal.forbidden")
        with self.assertRaises(AgentTransportError): expect_error(response("agent.proposal.forbidden", 409), 403, "agent.proposal.forbidden")

    def test_private_principal_recipe_is_concrete_and_isolates_tenant_axis(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "appsettings.json").write_text(json.dumps({"Qualification": {"StudioOrigin": "http://127.0.0.1:51001"}, "unrelated": {"preserve": True}}))
            run = SimpleNamespace(profiles={"backend": {"published": root}})
            principals = _seed_principals(run)
            settings = json.loads((root / "appsettings.json").read_text())
            self.assertEqual({"preserve": True}, settings["unrelated"])
            self.assertEqual(principals[0]["subject"], principals[2]["subject"])
            self.assertNotEqual(principals[0]["tenant"], principals[2]["tenant"])
            self.assertNotEqual(principals[0]["subject"], principals[1]["subject"])
            self.assertEqual(set(BASE_GRANTS), set(settings["Qualification"]["GrantProfiles"]["full"]))
            self.assertNotIn("studio/preferences:read", settings["Qualification"]["GrantProfiles"]["without-read"])
            self.assertNotIn("studio/preferences:write", settings["Qualification"]["GrantProfiles"]["without-write"])
            self.assertTrue(all("*" not in key for profile in settings["Qualification"]["GrantProfiles"].values() for key in profile))
            self.assertEqual(0o600, (root / "appsettings.json").stat().st_mode & 0o777)

    def test_failed_or_partial_preparation_never_reaches_a_host(self):
        for status in ("failed", "running", "passed"):
            run = SimpleNamespace(report={"status": status})
            with self.subTest(status=status), patch("journeys.server") as launch:
                with self.assertRaises(RuntimeError): qualify_distributions(run)
                launch.assert_not_called()
        run = SimpleNamespace(source="a" * 40, report={"status": "preparing", "authors": {"protocol": {"status": "building"}}, "consumers": {}, "producer": {"source": "a" * 40}})
        with patch("journeys.server") as launch, patch("journeys.build_authors") as authors:
            with self.assertRaises(RuntimeError): qualify_distributions(run)
            authors.assert_not_called(); launch.assert_not_called()


if __name__ == "__main__": unittest.main()
