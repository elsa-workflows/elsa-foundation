"""Cause/integrity checks for the workflow-authorization-unavailable driver; synthetic records are not native evidence."""
import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from agent_transport import AgentTransport
from authorization_journeys import (EXPECTED_CASES, SECTION, TURN_FRAMES, TURN_REFUSALS, UNAVAILABLE_CODE, authorization_manifest_gaps,
                                    qualify_workflow_authorization, require_denied_problem, require_turn_refusal,
                                    require_unavailable_problem, require_workflow_authorization, select_command, snapshot_attachment)
from artifacts import sha

CAUSE = "QUALIFICATION_PRIVATE_OWNER_CAUSE_0123456789abcdef"
CATALOG = {"contentType": "workflow.catalog", "content": {"status": "available", "entries": []}}


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
            "other-code": lambda v: (503, {**v, "errorCode": "weaver.workflow.authority_unavailable"}),
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


class TurnRefusalTests(unittest.TestCase):
    """The frame and trace shapes observed natively: Copilot on 300d3c286, Anthropic on 97f75e4f9."""
    command = "command-1"

    def frames(self, expected, provider="copilot"):
        def frame(kind):
            if kind == "contextAdmitted":
                return {"kind": kind, "payload": {"attachments": [{"contentType": "workflow.definition"}, copy.deepcopy(CATALOG), {"contentType": "workflow.guidance"}],
                                                  "diagnostics": []}}
            if kind in ("toolCallRequested", "toolCallStarted"):
                return {"kind": kind, "payload": {"toolCallId": "call-1", "toolName": "workflow_read_graph"}}
            if kind == "error":
                return {"kind": kind, "error": dict(TURN_REFUSALS[expected])}
            return {"kind": kind, "payload": {"turnId": "turn-1"}}
        return [frame(kind) for kind in TURN_FRAMES[provider]]

    @staticmethod
    def first(frames, kind):
        return next(frame for frame in frames if frame["kind"] == kind)

    def rows(self, provider):
        return [{"kind": provider + ".request", "data": {"commandId": self.command}},
                {"kind": "protocol.gate.waiting", "data": {"commandId": self.command}},
                {"kind": "protocol.gate.released", "data": {"commandId": self.command}},
                {"kind": provider + ".call", "data": {"commandId": self.command, "toolName": "workflow_read_graph"}}]

    def check(self, provider, expected, frames=None, rows=None):
        return require_turn_refusal(frames if frames is not None else self.frames(expected, provider), rows if rows is not None else self.rows(provider),
                                    provider=provider, command_id=self.command, expected=expected, cause=CAUSE)

    def test_the_observed_pre_dispatch_refusal_qualifies_on_each_adapter(self):
        for provider in ("copilot", "anthropic"):
            for expected in ("unavailable", "denied"):
                with self.subTest(provider=provider, expected=expected):
                    self.assertEqual(TURN_REFUSALS[expected], self.check(provider, expected))
        self.assertEqual({"code": UNAVAILABLE_CODE, "statusCode": 503, "message": "The owning module could not establish current authority for this operation."},
                         TURN_REFUSALS["unavailable"])
        self.assertEqual({"code": "weaver.context.incomplete", "statusCode": 400, "message": "Current read permission for the workflow context is required."},
                         TURN_REFUSALS["denied"])

    def test_the_pre_change_incomplete_context_shape_no_longer_qualifies_as_unavailable(self):
        # Observed on 300d3c286 before the engine reported the owner's code: code and status were collapsed.
        for provider in ("copilot", "anthropic"):
            frames = self.frames("unavailable", provider)
            frames[-1]["error"] = {"code": "weaver.context.incomplete", "statusCode": 400, "message": "Workflow definition authorization could not be completed."}
            with self.subTest(provider=provider):
                with self.assertRaises(RuntimeError): self.check(provider, "unavailable", frames=frames)

    def test_unavailable_and_denied_cannot_stand_in_for_each_other(self):
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                with self.assertRaises(RuntimeError): self.check(provider, "unavailable", frames=self.frames("denied", provider))
                with self.assertRaises(RuntimeError): self.check(provider, "denied", frames=self.frames("unavailable", provider))
                other = "anthropic" if provider == "copilot" else "copilot"
                with self.assertRaises(RuntimeError): self.check(provider, "unavailable", frames=self.frames("unavailable", other))

    def test_a_dispatched_returned_or_otherwise_shaped_turn_never_qualifies(self):
        first = self.first
        def completed(frames): frames.insert(len(frames) - 1, {"kind": "toolCallCompleted", "payload": {"toolName": "workflow_read_graph", "succeeded": False}})
        def finished(frames): frames[-1] = {"kind": "completed", "payload": {}}
        def message(frames): frames.insert(len(frames) - 1, {"kind": "messageDelta", "payload": "text"})
        def no_context(frames): frames.remove(first(frames, "contextAdmitted"))
        def context_refused(frames): frames[1]["payload"].update(attachments=[], diagnostics=[{"code": UNAVAILABLE_CODE, "required": True}])
        def later_context_refused(frames):
            [frame for frame in frames if frame["kind"] == "contextAdmitted"][-1]["payload"].update(diagnostics=[{"code": UNAVAILABLE_CODE, "required": True}])
        def no_snapshot(frames): first(frames, "contextAdmitted")["payload"].update(attachments=[{"contentType": "workflow.guidance"}])
        def no_catalog(frames):
            admitted = first(frames, "contextAdmitted")["payload"]
            admitted.update(attachments=[item for item in admitted["attachments"] if item["contentType"] != "workflow.catalog"])
        def unavailable_catalog(frames):
            for item in first(frames, "contextAdmitted")["payload"]["attachments"]:
                if item["contentType"] == "workflow.catalog": item["content"] = {"status": "unavailable", "entries": []}
        def other_tool(frames): first(frames, "toolCallStarted")["payload"]["toolName"] = "workflow_set_designer_position"
        def wrong_code(frames): frames[-1]["error"]["code"] = "weaver.tool.admission_unavailable"
        def wrong_status(frames): frames[-1]["error"]["statusCode"] = 500
        def owner_text(frames): frames[-1]["error"]["message"] = "Workflow definition authorization could not be completed."
        def leaked(frames): frames[-1]["error"]["message"] += " " + CAUSE
        for change in (completed, finished, message, no_context, context_refused, later_context_refused, no_snapshot, no_catalog,
                       unavailable_catalog, other_tool,
                       wrong_code, wrong_status, owner_text, leaked):
            for provider in ("copilot", "anthropic"):
                with self.subTest(change=change.__name__, provider=provider):
                    frames = self.frames("unavailable", provider); change(frames)
                    with self.assertRaises(RuntimeError): self.check(provider, "unavailable", frames=frames)

    def test_the_actual_adapter_called_once_and_received_no_outcome(self):
        for provider in ("copilot", "anthropic"):
            rows = self.rows(provider)
            for name, changed in (("no-call", rows[:-1]), ("two-calls", rows + [rows[-1]]),
                                  ("returned", rows + [{"kind": provider + ".return", "data": {"commandId": self.command}}]),
                                  ("continued", rows + [{"kind": provider + ".continuation", "data": {"commandId": self.command}}]),
                                  ("trace-cause", rows + [{"kind": provider + ".request", "data": {"commandId": self.command, "prompt": CAUSE}}])):
                with self.subTest(provider=provider, change=name):
                    with self.assertRaises(RuntimeError): self.check(provider, "unavailable", rows=changed)
            other = [dict(row, data={"commandId": "another-command"}) if row["kind"] == provider + ".return" else row
                     for row in rows + [{"kind": provider + ".return", "data": {}}]]
            self.check(provider, "unavailable", rows=other)


class SelectedCommandEvidenceTests(unittest.TestCase):
    """Witness 997887f4f: the first turn's evidence referenced the shared command file, which the next turn replaced."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        (self.root / "protocol").mkdir(mode=0o700)
        self.command_file = self.root / "protocol" / "command.json"

    def transport(self, name):
        return AgentTransport(SimpleNamespace(base="http://127.0.0.1:1", bearer=None), self.root / name, command_file=self.command_file)

    def test_the_shared_command_file_reference_goes_stale_when_the_next_turn_selects_its_command(self):
        first = self.transport("tool-unavailable").set_command("command-unavailable", "workflow_read_graph", {})
        self.transport("tool-denied").set_command("command-denied", "workflow_read_graph", {})
        self.assertEqual(first["path"], str(self.command_file))
        self.assertNotEqual(first["sha256"], sha(self.command_file))

    def test_each_turn_retains_its_own_command_copy_that_still_verifies_after_later_turns(self):
        first_transport = self.transport("tool-unavailable")
        first = select_command(first_transport, "command-unavailable", "workflow_read_graph", {})
        second = select_command(self.transport("tool-denied"), "command-denied", "workflow_read_graph", {})
        for ref, command in ((first, "command-unavailable"), (second, "command-denied")):
            path = Path(ref["path"])
            self.assertNotEqual(self.command_file, path)
            self.assertTrue(path.is_relative_to(self.root) and path.is_file())
            self.assertEqual(ref["sha256"], sha(path))
            self.assertEqual(command, json.loads(path.read_text())["commandId"])
        self.assertEqual("command-denied", json.loads(self.command_file.read_text())["commandId"])
        with self.assertRaises(RuntimeError): select_command(first_transport, "command-again", "workflow_read_graph", {})
        # A refused selection never replaces the shared command it could not retain.
        self.assertEqual("command-denied", json.loads(self.command_file.read_text())["commandId"])

    def test_the_retained_copy_is_the_exact_bytes_the_adapter_reads(self):
        transport = self.transport("tool-unavailable")
        ref = select_command(transport, "command-unavailable", "workflow_read_graph", {"definitionId": "d-1"})
        self.assertEqual(self.command_file.read_bytes(), Path(ref["path"]).read_bytes())
        self.assertEqual(sha(self.command_file), ref["sha256"])


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
