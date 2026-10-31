"""Fail-closed coordinator boundaries; these tests are not runtime witness evidence."""
from pathlib import Path
import json
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from agent_transport import Response, AgentTransportError
from journey_runtime import expect_error, same, BASE_GRANTS, browser_inputs, JourneyRuntime
from journeys import _unchanged, _seed_principals, qualify_distributions, _semantic_refusal, _remove


class JourneyBoundariesTests(unittest.TestCase):
    def test_changed_disable_and_reconcile_generations_cannot_mask_package_still_installed(self):
        package = {"id": "Resource.Package", "version": "1.2.3", "sha256": "a" * 64}
        inventory = {"resource.package": {"id": package["id"], "version": package["version"], "installPath": "/owned/native/resource"}}
        class Native:
            def __init__(self, removal_generation): self.removal_generation = removal_generation
            def membership(self, *args, **kwargs): return inventory, {"receipt": {}, "body": {}}
            def generation(self, client, name):
                return {"generation": self.removal_generation if name.endswith("removed-generation") else "before-disable", "tools": [], "contextSources": []}, {"receipt": {}}
            def feature(self, *args, **kwargs): return {"after": {"generation": "after-disable"}, "evidence": []}
            def reconcile(self, *args, **kwargs): return {}, {"receipt": {}}
        runtime = SimpleNamespace(snapshot=lambda name: ({"unchanged": True}, {}, {}),
            prepare=lambda name: SimpleNamespace(evidence=[]), approve=lambda turn: None,
            backend=object(), studio=object(), witness={"backendFeature": "resource-feature"},
            run=SimpleNamespace(report={"authors": {"backend": {"packed": {"package": package}}}}))
        for generation in ("after-remove", "after-disable"):
            runtime.native = Native(generation)
            with self.subTest(removalGeneration=generation), patch("journeys.remove_resource_archives") as remove:
                # The same strict membership failure must be reached whether removal
                # produces another serving generation or keeps the already-disabled one.
                with self.assertRaisesRegex(RuntimeError, "left the resource package installed"):
                    _remove(runtime, SimpleNamespace(), {"backend": {}})
                remove.assert_called_once()

    def test_controls_get_a_fresh_private_parent_without_changing_protocol_directory(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            protocol = root / "prepared-protocol"
            protocol.mkdir(mode=0o755)
            protocol.chmod(0o755)
            command = protocol / "command.json"
            run = SimpleNamespace(root=root, profiles={"backend": {"commandFile": command}})
            runtime = JourneyRuntime(run, "http://127.0.0.1:51001", "http://127.0.0.1:51002", {}, [])
            self.assertEqual(root / "journeys" / "controls", runtime.controls)
            self.assertEqual(0o700, runtime.controls.stat().st_mode & 0o777)
            self.assertEqual(0o755, protocol.stat().st_mode & 0o777)
            self.assertEqual(command, runtime.command_file)
            for control in (runtime.gate_file, runtime.cas_control, runtime.cas_receipt):
                self.assertEqual(runtime.controls, control.parent)
                self.assertFalse(control.is_symlink())
            self.assertEqual(0o600, runtime.gate_file.stat().st_mode & 0o777)
            self.assertEqual("release", json.loads(runtime.gate_file.read_text())["state"])
            self.assertFalse(runtime.cas_control.exists()); self.assertFalse(runtime.cas_receipt.exists())

    def test_browser_export_summary_cannot_copy_private_auth_or_config_paths(self):
        config = {"journey": "workflow", "provider": "copilot", "studioOrigin": "http://127.0.0.1:51001", "backendOrigin": "http://127.0.0.1:51002",
                  "storageState": "/private/auth-state.json", "bearer": "PRIVATE_CREDENTIAL", "commandFile": "/private/command.json",
                  "traceFile": "/private/trace.jsonl", "workflow": {"definitionId": "definition", "activityId": "activity", "draftId": "draft",
                  "definitionPath": "/design/workflows/definitions/definition", "draftPath": "/design/workflows/drafts/draft",
                  "validationPath": "/design/workflows/drafts/draft/validations", "privateAuth": "/private/nested-auth.json", "evidence": []}}
        summary = browser_inputs(config)
        self.assertEqual({"journey", "provider", "studioOrigin", "backendOrigin", "driverSha256", "workflow"}, set(summary))
        self.assertEqual({"definitionId", "activityId", "draftId", "definitionPath", "draftPath", "validationPath"}, set(summary["workflow"]))
        self.assertNotIn("/private/", json.dumps(summary)); self.assertNotIn("PRIVATE_CREDENTIAL", json.dumps(summary))
        config["workflow"]["draftPath"] += "?accessToken=PRIVATE_CREDENTIAL"
        with self.assertRaises(RuntimeError): browser_inputs(config)

    def test_semantic_refusal_requires_native_failure_and_correlated_completion(self):
        result = {"toolCallId": "native-call", "toolName": "declared_tool", "succeeded": False,
                  "content": "The actual declared operation was refused.", "payload": None, "receipt": None}
        events = [{"kind": "toolCallStarted", "payload": {"toolCallId": "native-call", "toolName": "declared_tool"}},
                  {"kind": "toolCallCompleted", "payload": {"toolCallId": "native-call", "toolName": "declared_tool", "succeeded": False,
                   "summary": result["content"], "result": result}}, {"kind": "contributedResult", "payload": result}]
        turn = lambda rows: SimpleNamespace(stream=SimpleNamespace(frames=[SimpleNamespace(value=row) for row in rows]))
        sdk = {"error": {"code": "declared.refused", "message": result["content"]}}
        _semantic_refusal(turn(events), sdk, "declared_tool")
        for invalid in ([], events[:2], events[1:], [events[0], events[2]]):
            with self.subTest(events=invalid), self.assertRaises(RuntimeError): _semantic_refusal(turn(invalid), sdk, "declared_tool")
        for key, value in (("toolCallId", "unrelated-sdk-call"), ("succeeded", True), ("summary", "SDK transport completed.")):
            changed = json.loads(json.dumps(events)); changed[1]["payload"][key] = value
            with self.subTest(field=key), self.assertRaises(RuntimeError): _semantic_refusal(turn(changed), sdk, "declared_tool")

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
