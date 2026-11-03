"""Cause/integrity checks for the workflow-authorization-unavailable driver; synthetic records are not native evidence."""
import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from authorization_journeys import (EXPECTED_CASES, SECTION, TOOL_OUTCOMES, UNAVAILABLE_CODE, authorization_manifest_gaps,
                                    qualify_workflow_authorization, require_denied_problem, require_tool_outcome,
                                    require_unavailable_problem, require_workflow_authorization, snapshot_attachment)
from artifacts import sha

CAUSE = "QUALIFICATION_PRIVATE_OWNER_CAUSE_0123456789abcdef"


def body(value):
    return json.dumps(value).encode()


UNAVAILABLE = {"errors": {"generalErrors": ["Workflow definition authorization could not be completed."]},
               "message": "One or more errors occurred!", "statusCode": 503, "errorCode": UNAVAILABLE_CODE}
DENIED = {"errors": {"generalErrors": ["The requested workflow definition is not readable in the current application context."]},
          "message": "One or more errors occurred!", "statusCode": 403}


class DefinitionOutcomeTests(unittest.TestCase):
    def test_unavailable_requires_503_the_stable_code_the_fixed_message_and_no_cause(self):
        self.assertEqual(UNAVAILABLE, require_unavailable_problem(503, body(UNAVAILABLE), CAUSE))

    def test_wrong_status_code_shape_or_any_cause_never_qualifies_as_unavailable(self):
        changes = {
            "generic-500": lambda v: (500, v),
            "denied-403": lambda v: (403, v),
            "no-code": lambda v: (503, {key: value for key, value in v.items() if key != "errorCode"}),
            "other-code": lambda v: (503, {**v, "errorCode": "agent.workflow.authority_unavailable"}),
            "status-mismatch": lambda v: (503, {**v, "statusCode": 500}),
            "cause-message": lambda v: (503, {**v, "errors": {"generalErrors": [CAUSE]}}),
            "cause-extra": lambda v: (503, {**v, "detail": CAUSE}),
            "exception-type": lambda v: (503, {**v, "errors": {"generalErrors": ["System.IO.IOException"]}}),
            "envelope": lambda v: (503, {**v, "message": "Workflow definition authorization could not be completed."}),
        }
        for name, change in changes.items():
            with self.subTest(change=name):
                status, value = change(copy.deepcopy(UNAVAILABLE))
                with self.assertRaises(RuntimeError): require_unavailable_problem(status, body(value), CAUSE)
        with self.assertRaises(RuntimeError): require_unavailable_problem(503, b"not json", CAUSE)
        with self.assertRaises(RuntimeError): require_unavailable_problem(503, body(UNAVAILABLE) + CAUSE.encode(), CAUSE)

    def test_denied_keeps_the_existing_403_body_without_an_error_code(self):
        self.assertEqual(DENIED, require_denied_problem(403, body(DENIED)))
        for status, value in ((503, DENIED), (403, {**DENIED, "errorCode": UNAVAILABLE_CODE}), (403, {**DENIED, "errorCode": None}),
                              (403, UNAVAILABLE), (404, DENIED)):
            with self.subTest(status=status, value=value):
                with self.assertRaises(RuntimeError): require_denied_problem(status, body(value))


class ToolOutcomeTests(unittest.TestCase):
    command = "command-1"

    def frames(self, expected, *, extra=None):
        summary = TOOL_OUTCOMES[expected]["summary"]
        result = {"toolCallId": self.command, "toolName": "workflow_read_graph", "succeeded": False, "content": summary}
        values = [{"kind": "started", "payload": {"turnId": "turn-1"}}, {"kind": "contextAdmitted", "payload": {"attachments": [], "diagnostics": []}},
                  {"kind": "toolCallCompleted", "payload": {"toolCallId": self.command, "toolName": "workflow_read_graph", "succeeded": False,
                                                           "summary": summary, "result": result}},
                  {"kind": "contributedResult", "payload": result}, {"kind": "completed", "payload": {}}]
        return values + (extra or [])

    def rows(self, provider, expected):
        outcome = TOOL_OUTCOMES[expected]
        rows = [{"kind": provider + ".request", "data": {"commandId": self.command}},
                {"kind": provider + ".call", "data": {"commandId": self.command, "toolName": "workflow_read_graph", "arguments": {}}}]
        if provider == "copilot":
            rows.append({"kind": "copilot.return", "data": {"commandId": self.command, "result": {"status": "denied", "executed": False,
                "error": {"code": outcome["code"], "message": outcome["summary"], "statusCode": outcome["statusCode"]}}}})
        else:
            result = {"toolCallId": self.command, "toolName": "workflow_read_graph", "succeeded": False, "content": outcome["summary"]}
            rows.append({"kind": "anthropic.continuation", "data": {"commandId": self.command, "results": [
                {"kind": "functionResult", "callId": self.command, "result": json.dumps(result)}]}})
        return rows

    def check(self, provider, expected, frames=None, rows=None):
        return require_tool_outcome(frames if frames is not None else self.frames(expected), rows if rows is not None else self.rows(provider, expected),
                                    provider=provider, command_id=self.command, expected=expected, cause=CAUSE)

    def test_each_adapter_observes_the_unavailable_and_denied_outcomes(self):
        for provider in ("copilot", "anthropic"):
            for expected in ("unavailable", "denied"):
                with self.subTest(provider=provider, expected=expected):
                    observed = self.check(provider, expected)
                    self.assertEqual(TOOL_OUTCOMES[expected]["summary"], observed["summary"])
                    self.assertEqual(TOOL_OUTCOMES[expected]["code"] if provider == "copilot" else None, observed["code"])

    def test_unavailable_and_denied_cannot_stand_in_for_each_other(self):
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                with self.assertRaises(RuntimeError): self.check(provider, "unavailable", frames=self.frames("denied"))
                with self.assertRaises(RuntimeError): self.check(provider, "unavailable", rows=self.rows(provider, "denied"))
                with self.assertRaises(RuntimeError): self.check(provider, "denied", frames=self.frames("unavailable"))

    def test_copilot_return_must_carry_the_stable_code_and_status(self):
        for change in (lambda e: e.update(code="agent.tool.forbidden"), lambda e: e.update(statusCode=403), lambda e: e.pop("code"),
                       lambda e: e.update(message="Owner detail")):
            rows = self.rows("copilot", "unavailable")
            change(rows[-1]["data"]["result"]["error"])
            with self.subTest(change=change):
                with self.assertRaises(RuntimeError): self.check("copilot", "unavailable", rows=rows)

    def test_disclosed_cause_success_proposal_or_missing_call_never_qualifies(self):
        def leak_frame(frames): frames[2]["payload"]["summary"] += " " + CAUSE
        def leak_extra(frames): frames.append({"kind": "messageDelta", "payload": CAUSE})
        def succeeded(frames): frames[2]["payload"]["succeeded"] = True
        def proposal(frames): frames.append({"kind": "proposalCreated", "payload": {}})
        def contributed(frames): frames[3]["payload"] = {**frames[3]["payload"], "succeeded": True}
        def twice(frames): frames.append(copy.deepcopy(frames[2]))
        def other_tool(frames): frames[2]["payload"]["toolName"] = "workflow_set_designer_position"
        for change in (leak_frame, leak_extra, succeeded, proposal, contributed, twice, other_tool):
            for provider in ("copilot", "anthropic"):
                with self.subTest(change=change.__name__, provider=provider):
                    frames = self.frames("unavailable"); change(frames)
                    with self.assertRaises(RuntimeError): self.check(provider, "unavailable", frames=frames)
        for provider in ("copilot", "anthropic"):
            rows = self.rows(provider, "unavailable")
            with self.subTest(provider=provider, change="no-call"):
                with self.assertRaises(RuntimeError): self.check(provider, "unavailable", rows=[row for row in rows if not row["kind"].endswith(".call")])
            with self.subTest(provider=provider, change="trace-cause"):
                leaked = rows + [{"kind": provider + ".request", "data": {"commandId": self.command, "prompt": CAUSE}}]
                with self.assertRaises(RuntimeError): self.check(provider, "unavailable", rows=leaked)


class SnapshotTests(unittest.TestCase):
    def test_snapshot_is_the_workflow_module_s_bounded_admission_shape(self):
        seed = {"definitionId": "definition-1", "draftId": "draft-1", "activityId": "witness-write"}
        value = snapshot_attachment(seed, "revision-1")
        self.assertEqual(("Elsa.Studio.Workflows", "workflow.definition", "1", "clientSnapshot"),
                         (value["moduleId"], value["contentType"], value["schemaVersion"], value["origin"]))
        self.assertEqual({"moduleId": "Elsa.Studio.Workflows", "resourceType": "workflow-definition", "resourceId": "definition-1", "revision": "revision-1"},
                         value["target"])
        content = value["content"]
        self.assertEqual(("definition-1", "draft-1", "revision-1"), (content["workflowDefinitionId"], content["draftId"], content["revision"]))
        self.assertEqual({"witness-write"}, {item["id"] for item in content["activities"]} | {item["nodeId"] for item in content["positions"]})
        self.assertTrue(value["client"]["clientId"] and value["client"]["generationId"])


class SectionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def record(self, name, **changes):
        path = self.root / (name + ".json")
        path.write_text(json.dumps({"id": name, "status": "passed", "adapter": name.split("-", 1)[0], "causeDisclosed": False, **changes}))
        return {"path": str(path), "sha256": sha(path)}

    def section(self):
        names = sorted(EXPECTED_CASES)
        return {"status": "passed", "evidenceCategory": "deterministic-provider-protocol", "liveProvider": False, "source": "frozen",
                "caseIDs": names, "stages": [], "records": [{"id": name, "status": "passed", "record": self.record(name)} for name in names]}

    def test_complete_section_passes_both_gates(self):
        section = self.section()
        self.assertEqual([], authorization_manifest_gaps(section, "frozen"))
        self.assertEqual(2, len(require_workflow_authorization(section, self.root, source="frozen")))

    def test_absent_partial_failed_or_foreign_sections_are_gaps(self):
        changes = {
            "absent": None,
            "failed": lambda s: s.update(status="failed"),
            "live": lambda s: s.update(liveProvider=True),
            "category": lambda s: s.update(evidenceCategory="live-provider"),
            "source": lambda s: s.update(source="other"),
            "one-adapter": lambda s: (s["caseIDs"].pop(), s["records"].pop()),
            "duplicate": lambda s: (s["caseIDs"].append(s["caseIDs"][0]), s["records"].append(s["records"][0])),
            "reordered": lambda s: s["records"].reverse(),
            "record-failed": lambda s: s["records"][0].update(status="failed"),
        }
        for name, change in changes.items():
            with self.subTest(change=name):
                section = None if change is None else self.section()
                if change is not None: change(section)
                self.assertTrue(authorization_manifest_gaps(section, "frozen"))
                with self.assertRaises(RuntimeError): require_workflow_authorization(section, self.root, source="frozen")

    def test_retained_record_must_still_say_passed_on_its_own_adapter_without_disclosure(self):
        for name, changes in (("status", {"status": "failed"}), ("adapter", {"adapter": "anthropic"}), ("disclosed", {"causeDisclosed": True})):
            with self.subTest(change=name):
                section = self.section()
                copilot = next(row for row in section["records"] if row["id"].startswith("copilot"))
                copilot["record"] = self.record(copilot["id"] + "-" + name, **changes) | {}
                # Keep the id inside the record matching the row, so only the changed field differs.
                path = Path(copilot["record"]["path"])
                path.write_text(json.dumps({**json.loads(path.read_text()), "id": copilot["id"]}))
                copilot["record"]["sha256"] = sha(path)
                with self.assertRaises(RuntimeError): require_workflow_authorization(section, self.root)
        section = self.section()
        section["records"][0]["record"]["sha256"] = "0" * 64
        with self.assertRaises(RuntimeError): require_workflow_authorization(section, self.root)

    def test_case_failure_is_mandatory_and_preserves_original_case_records(self):
        original = [{"id": "original-m1-control", "status": "failed", "expectedFailureObserved": True}]
        run = SimpleNamespace(report={"cases": copy.deepcopy(original)}, source="committed-source", save=Mock(), root=self.root)
        runtime = SimpleNamespace(run=run)
        with patch("authorization_journeys._case", side_effect=RuntimeError("copilot: wrong unavailable cause")):
            with self.assertRaisesRegex(RuntimeError, "wrong unavailable cause"):
                qualify_workflow_authorization(runtime)
        self.assertEqual(original, run.report["cases"])
        section = run.report[SECTION]
        self.assertEqual(("failed", [], "copilot-workflow-authorization-unavailable"), (section["status"], section["records"], section["activeCase"]))
        self.assertEqual("failed", section["stages"][-1]["status"])
        with self.assertRaisesRegex(RuntimeError, "Never resume"):
            qualify_workflow_authorization(runtime)


if __name__ == "__main__":
    unittest.main()
