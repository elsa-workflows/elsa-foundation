"""Integrity checks for the workflow-run driver; synthetic records are not native evidence."""
import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from artifacts import sha
from batch_journeys import OUTCOME_ROWS
from explain_journeys import READ_FRAMES
from inspection_journeys import ADMISSION_REFUSAL, MUTATING_TOOLS
from run_journeys import (BROWSER_JOURNEY, _browser, run_browser_inputs, BROWSER_OBSERVATIONS, EXPECTED_CASES, RUN_KIND, RUN_LIMITED, RUN_LIMITED_GRANTS, RUN_NATIVE, RUN_TOOL, TEXTS,
                          expected_disclosure, no_canary, require_controls_unoffered, require_no_canary_in_evidence, require_run_browser,
                          require_run_context, require_run_read, require_run_result, require_workflow_run, run_attachment, run_manifest_gaps,
                          run_principal, synthetic_browser, synthetic_native, synthetic_search)

COMMAND = "command-1"
CANARY = "CANARY-RUN-SENSITIVE-" + "0" * 32


def evidence(name, *, capture="diagnosticSnapshotCaptured", access="resolutionAvailable", sensitive=False, withheld=None, key=None):
    return {"evidenceId": "evidence-" + name, "name": name, "subject": "ActivityInput", "inputKey": key or name, "captureState": capture,
            "accessState": access, "isSensitive": sensitive, "withheldKind": withheld}


SENSITIVE = evidence("text", capture="notCaptured", access="unavailable", sensitive=True)
CODE, MESSAGE = evidence("code"), evidence("message")
WRITE = {"detail": {"activityExecutionId": "act-write", "workflowExecutionId": "run-1", "activityType": "Elsa.Activities.Primitives.Activities.WriteLine",
                    "status": "Completed", "valueSnapshots": [SENSITIVE], "incidents": [], "bookmarks": []}, "path": "/runtime/act-write"}
FAULT = {"detail": {"activityExecutionId": "act-fault", "workflowExecutionId": "run-1", "activityType": "Elsa.Activities.Primitives.Activities.Fault",
                    "status": "Faulted", "valueSnapshots": [CODE, MESSAGE], "incidents": [{"incidentId": "incident-1"}], "bookmarks": []}, "path": "/runtime/act-fault"}
INCIDENT = {"incidentId": "incident-1", "activityExecutionId": "act-fault", "failureType": "qualification.run.fault", "isBlocking": True,
            "message": "Qualification Run fault."}


def run(status="Faulted", message=INCIDENT["message"]):
    return {"workflowExecutionId": "run-1", "instancePath": "/runtime/run-1",
            "instance": {"instance": {"workflowExecutionId": "run-1", "status": status, "artifactId": "artifact-1", "artifactHash": "sha256:abc",
                                      "definitionId": "definition-1", "definitionVersionId": "version-1", "incidentCount": 1, "activityCount": 3},
                         "incidents": [{**INCIDENT, "message": message}],
                         "activities": [{"activityExecutionId": item} for item in ("act-root", "act-write", "act-fault")]}}


def context(attachment, value, selected=None, *, read_at=None):
    instance = value["instance"]["instance"]
    chosen = None if selected is None else {"activityExecutionId": selected["detail"]["activityExecutionId"], "activityType": selected["detail"]["activityType"],
                                            "status": selected["detail"]["status"], "valueCount": len(selected["detail"]["valueSnapshots"])}
    return {"id": attachment["id"], "moduleId": "Elsa.Studio.Workflows", "contentType": RUN_KIND, "origin": "authoritativeRead",
            "target": dict(attachment["target"]),
            "content": {**{key: instance[key] for key in ("workflowExecutionId", "status", "artifactId", "artifactHash", "definitionId", "definitionVersionId",
                                                            "incidentCount")},
                        "activityExecutionId": attachment["content"]["activityExecutionId"], "inProgress": instance["status"] in ("Running", "Suspended"),
                        "readAt": read_at, "selectedActivity": chosen}}


def result(value, selected=None, *, permitted=True, payloads=None, overrides=None):
    instance = value["instance"]["instance"]
    terminal = instance["status"] not in ("Running", "Suspended")
    data = {"readOnly": True, "workflowExecutionId": instance["workflowExecutionId"], "readAt": "2026-11-03T12:00:00Z", "inProgress": not terminal,
            "attribution": "This Run has finished (Faulted); its recorded state is final." if terminal else "This Run is still in progress (Running); read at ...",
            "instance": dict(instance), "canInspectValues": permitted, "canResolveValues": permitted, "runControls": "Retry ... use the run page's own controls (the run toolbar).",
            "incidents": [{**row, "messageRedacted": not permitted} for row in value["instance"]["incidents"]], "incidentsTruncated": False,
            "activities": {"items": value["instance"]["activities"], "itemsTruncated": False}, "selectedActivity": None,
            "unavailable": [] if permitted else [{"data": "incident messages", "reason": "notPermitted"}]}
    if selected is not None:
        sensitive = any(row["isSensitive"] for row in selected["detail"]["valueSnapshots"])
        values = []
        for row in selected["detail"]["valueSnapshots"]:
            disclosure, reason = expected_disclosure(row)
            values.append({"evidenceId": row["evidenceId"], "name": row["name"], "inputKey": row["inputKey"], "disclosure": disclosure, "unavailableReason": reason,
                           "sensitive": row["isSensitive"], "captureState": row["captureState"], "accessState": row["accessState"],
                           "value": (payloads or {}).get(row["evidenceId"]) if disclosure == "resolved" else None})
            if disclosure == "withheld": data["unavailable"].append({"data": "ActivityInput '" + row["inputKey"] + "'", "reason": "withheld"})
        data["selectedActivity"] = {"activityExecutionId": selected["detail"]["activityExecutionId"], "summary": {"status": selected["detail"]["status"]},
                                    "incidents": selected["detail"]["incidents"], "bookmarks": [], "values": values, "valuesTruncated": False,
                                    "stateView": {"available": False, "withheld": sensitive, "unavailableReason": None if sensitive else "notCaptured",
                                                  "view": None}}
    for key, item in (overrides or {}).items(): data[key] = item
    return data


def turn(provider, item, data):
    def frame(kind):
        if kind == "contextAdmitted": return {"kind": kind, "payload": {"diagnostics": [], "attachments": [copy.deepcopy(item), {"contentType": "workflows.run-guidance"}]}}
        if kind == "toolCallCompleted": return {"kind": kind, "payload": {"toolName": RUN_TOOL, "succeeded": True,
                                                                           "result": {"succeeded": True, "payload": {"typeId": RUN_KIND, "data": copy.deepcopy(data)}}}}
        return {"kind": kind}
    return [frame(kind) for kind in READ_FRAMES[provider]], {"data": {"message": {"context": [copy.deepcopy(item)]}}}


def rows(provider, tools=(RUN_TOOL,), calls=1):
    value = [{"kind": provider + ".request", "data": {"commandId": COMMAND, "tools": [{"name": "other"}] + [{"name": tool} for tool in tools]}}]
    value += [{"kind": provider + ".call", "data": {"commandId": COMMAND, "toolName": RUN_TOOL}}] * calls
    value.append({"kind": provider + OUTCOME_ROWS[provider], "data": {"commandId": COMMAND}})
    return value


class RunAttachmentTests(unittest.TestCase):
    def test_binds_one_exact_run_and_selection_never_the_latest_run(self):
        selected = run_attachment("run-1", "act-1")
        self.assertEqual({"moduleId": "Elsa.Studio.Workflows", "resourceType": "workflow-run", "resourceId": "run-1", "revision": "act-1"}, selected["target"])
        self.assertEqual({"workflowExecutionId": "run-1", "activityExecutionId": "act-1"}, selected["content"])
        self.assertEqual("run-1", run_attachment("run-1")["target"]["revision"])
        for invalid in (("", None), ("run-1", "run-1"), ("run-1", ""), ("x" * 257, None)):
            with self.subTest(invalid=invalid), self.assertRaises(RuntimeError): run_attachment(*invalid)

    def test_the_run_actor_reads_runs_without_value_inspection(self):
        principals = [{"tenant": "default"}] * RUN_LIMITED
        actor = run_principal(principals, lambda prefix, tenant, grants: {"prefix": prefix, "tenant": tenant, "grants": grants})[0]
        self.assertEqual("default", actor["tenant"])
        self.assertTrue({"workflows/runtime:read", "workflows/activity-executions:inspect", "api-capabilities:read"} <= set(actor["grants"]))
        self.assertNotIn("workflows/activity-executions:inspect-values", RUN_LIMITED_GRANTS)
        with self.assertRaises(RuntimeError): run_principal(principals[:-1], lambda *args: {})


class RunDisclosureTests(unittest.TestCase):
    def test_a_values_disclosure_follows_its_evidence(self):
        self.assertEqual(("withheld", None), expected_disclosure(SENSITIVE))
        # A sensitive value a capture policy captured is never released either.
        self.assertEqual(("withheld", None), expected_disclosure(evidence("pin", sensitive=True)))
        self.assertEqual(("withheld", None), expected_disclosure(evidence("token", capture="unavailable", access="unavailable", withheld="SecretReference")))
        self.assertEqual(("unavailable", "notCaptured"), expected_disclosure(evidence("off", capture="notCaptured", access="unavailable")))
        self.assertEqual(("unavailable", "notPermitted"), expected_disclosure(evidence("hidden", access="redacted")))
        self.assertEqual(("unavailable", "notPermitted"), expected_disclosure(evidence("gated", access="resolutionPermissionRequired")))
        self.assertEqual(("resolved", None), expected_disclosure(CODE))

    def test_the_context_is_the_run_apis_read_of_that_run_and_selection(self):
        attachment = run_attachment("run-1", "act-fault")
        content = require_run_context(context(attachment, run(), FAULT), attachment, run(), FAULT)
        self.assertNotIn("readAt", content)
        for change in ({"status": "Running"}, {"artifactHash": "sha256:other"}, {"inProgress": True}, {"selectedActivity": None}):
            item = context(attachment, run(), FAULT)
            item["content"].update(change)
            with self.subTest(change=change), self.assertRaises(RuntimeError): require_run_context(item, attachment, run(), FAULT)
        other = context(attachment, run(), FAULT)
        other["target"]["revision"] = "act-write"
        with self.assertRaises(RuntimeError): require_run_context(other, attachment, run(), FAULT)
        # An in-progress Run carries its read time; a finished one never does.
        waiting = run(status="Running")
        require_run_context(context(attachment, waiting, FAULT, read_at="2026-11-03T12:00:00Z"), attachment, waiting, FAULT)
        with self.assertRaises(RuntimeError): require_run_context(context(attachment, waiting, FAULT), attachment, waiting, FAULT)

    def test_the_read_equals_the_run_api_with_the_sensitive_input_withheld(self):
        record = require_run_result(result(run(), WRITE), run(), selected=WRITE)
        self.assertEqual({"text": "withheld"}, record["values"])
        leaked = result(run(), WRITE)
        leaked["selectedActivity"]["values"][0].update(disclosure="resolved", value=CANARY)
        with self.assertRaises(RuntimeError): require_run_result(leaked, run(), selected=WRITE)
        unlisted = result(run(), WRITE, overrides={"unavailable": []})
        with self.assertRaises(RuntimeError): require_run_result(unlisted, run(), selected=WRITE)
        self.assertEqual("withheld", record["stateView"])
        # The state view of an activity with a sensitive input must be withheld, never shown or merely unavailable.
        shown = result(run(), WRITE)
        shown["selectedActivity"]["stateView"] = {"available": True, "withheld": False, "view": {"echo": CANARY}}
        with self.assertRaises(RuntimeError): require_run_result(shown, run(), selected=WRITE)
        # A captured sensitive value is withheld too.
        captured = {"detail": {**WRITE["detail"], "valueSnapshots": [evidence("text", sensitive=True)]}, "path": WRITE["path"]}
        self.assertEqual({"text": "withheld"}, require_run_result(result(run(), captured), run(), selected=captured)["values"])

    def test_released_values_equal_the_payload_api_and_differences_fail(self):
        payloads = {CODE["evidenceId"]: "qualification.run.fault", MESSAGE["evidenceId"]: "Qualification Run fault."}
        record = require_run_result(result(run(), FAULT, payloads=payloads), run(), selected=FAULT, payloads=payloads)
        self.assertEqual({"code": "resolved", "message": "resolved"}, record["values"])
        with self.assertRaises(RuntimeError):
            require_run_result(result(run(), FAULT, payloads={**payloads, CODE["evidenceId"]: "other"}), run(), selected=FAULT, payloads=payloads)
        for change in ({"incidents": []}, {"inProgress": True}, {"runControls": "nothing"}, {"canInspectValues": False}, {"canResolveValues": False}):
            with self.subTest(change=change), self.assertRaises(RuntimeError):
                require_run_result(result(run(), FAULT, payloads=payloads, overrides=change), run(), selected=FAULT, payloads=payloads)

    def test_an_actor_without_value_inspection_reads_redacted_incidents_listed_as_not_permitted(self):
        limited = run(message=TEXTS["redacted"])
        hidden = {"detail": {**FAULT["detail"], "valueSnapshots": [evidence("code", access="redacted")]}, "path": FAULT["path"]}
        record = require_run_result(result(limited, hidden, permitted=False), limited, selected=hidden, permitted=False)
        self.assertEqual({"code": "unavailable"}, record["values"])
        with self.assertRaises(RuntimeError):
            require_run_result(result(limited, hidden, permitted=False, overrides={"unavailable": []}), limited, selected=hidden, permitted=False)

    def test_an_in_progress_read_is_attributed_to_its_read_time(self):
        waiting = run(status="Running")
        self.assertTrue(require_run_result(result(waiting), waiting)["inProgress"])
        with self.assertRaises(RuntimeError):
            require_run_result(result(waiting, overrides={"attribution": "This Run has finished (Running)."}), waiting)


class RunSeedSettleTests(unittest.TestCase):
    """The settle predicates over the instance reads the packaged runtime returned natively (d5d731de2), reduced."""
    WAITING = {"instance": {"status": "Running"}, "activities": [
        {"authoredActivityId": "run-flow", "status": "Running", "subStatus": None, "bookmarkCount": 0},
        {"authoredActivityId": "run-wait", "status": "Suspended", "subStatus": "BookmarkWaiting", "bookmarkCount": 1}]}
    FAULTED = {"instance": {"status": "Faulted"}, "activities": [
        {"authoredActivityId": "run-sensitive", "status": "Completed", "subStatus": None, "bookmarkCount": 0},
        {"authoredActivityId": "run-fault", "status": "Faulted", "subStatus": "ActivityReturnedFault", "bookmarkCount": 0}]}

    def test_a_running_run_waiting_on_its_bookmark_is_in_progress_and_waiting(self):
        from owner_resources import run_terminal, run_waiting
        self.assertTrue(run_waiting(self.WAITING, "run-wait"))
        self.assertFalse(run_terminal(self.WAITING))
        self.assertTrue(run_terminal(self.FAULTED))
        self.assertFalse(run_waiting(self.FAULTED, "run-wait"))
        # Not yet waiting: scheduled but no bookmark, or the waiting activity another one.
        starting = copy.deepcopy(self.WAITING); starting["activities"][1].update(status="Running", subStatus=None, bookmarkCount=0)
        self.assertFalse(run_waiting(starting, "run-wait"))
        self.assertFalse(run_waiting(self.WAITING, "run-other"))
        self.assertFalse(run_waiting({"instance": {"status": "Pending"}, "activities": self.WAITING["activities"]}, "run-wait"))


class RunTurnTests(unittest.TestCase):
    def test_a_read_follows_each_adapters_pinned_order_and_discloses_no_canary(self):
        for provider in ("copilot", "anthropic"):
            attachment = run_attachment("run-1", "act-write")
            frames, accepted = turn(provider, context(attachment, run(), WRITE), result(run(), WRITE))
            with self.subTest(provider=provider):
                record = require_run_read(frames, rows(provider), accepted, provider=provider, command_id=COMMAND, attachment=attachment, run=run(),
                                          selected=WRITE, canary=CANARY)
                self.assertEqual("act-write", record["selected"])
                with self.assertRaises(RuntimeError):
                    require_run_read(frames[1:], rows(provider), accepted, provider=provider, command_id=COMMAND, attachment=attachment, run=run(),
                                     selected=WRITE, canary=CANARY)
                leaked = copy.deepcopy(frames)
                leaked[0]["note"] = CANARY
                with self.assertRaises(RuntimeError):
                    require_run_read(leaked, rows(provider), accepted, provider=provider, command_id=COMMAND, attachment=attachment, run=run(),
                                     selected=WRITE, canary=CANARY)
                with self.assertRaises(RuntimeError):
                    require_run_read(frames, rows(provider, calls=2), accepted, provider=provider, command_id=COMMAND, attachment=attachment, run=run(),
                                     selected=WRITE, canary=CANARY)

    def test_run_controls_and_mutating_tools_are_never_offered(self):
        frames = [{"kind": kind} for kind in READ_FRAMES["copilot"]]
        self.assertEqual(["other", RUN_TOOL], require_controls_unoffered(frames, rows("copilot"), provider="copilot", command_id=COMMAND))
        for tool in (sorted(MUTATING_TOOLS)[0], "workflow_retry_run", "workflow_cancel_run"):
            with self.subTest(tool=tool), self.assertRaises(RuntimeError):
                require_controls_unoffered(frames, rows("copilot", tools=(RUN_TOOL, tool)), provider="copilot", command_id=COMMAND)
        with self.assertRaises(RuntimeError):
            require_controls_unoffered([*frames, {"kind": "proposalCreated"}], rows("copilot"), provider="copilot", command_id=COMMAND)

    def test_no_canary_finds_it_anywhere(self):
        no_canary(CANARY, {"value": "nothing"}, b"bytes", "text")
        for value in ({"nested": [CANARY]}, CANARY.encode(), "prefix " + CANARY):
            with self.subTest(value=type(value).__name__), self.assertRaises(RuntimeError): no_canary(CANARY, value)
        with self.assertRaises(RuntimeError): no_canary("short", "x")


class RunBrowserAndGateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def write(self, name, value):
        path = self.root / name
        path.write_text(json.dumps(value))
        return {"path": str(path), "sha256": sha(path)}

    def observed(self, **change):
        value = {"journey": BROWSER_JOURNEY, "status": "passed", "provider": "copilot", "observations": copy.deepcopy(BROWSER_OBSERVATIONS),
                 "ownerBefore": {"path": "a"}, "ownerAfter": {"path": "b"}, "evidence": [{"path": "c"}], "commands": {"explain": COMMAND}}
        value.update(change)
        return value

    def test_the_browser_journey_is_the_run_pages_own(self):
        commands = [{"commandId": COMMAND, "toolName": RUN_TOOL, "arguments": {}}]
        self.assertEqual(BROWSER_OBSERVATIONS, require_run_browser(self.observed(), commands, rows("copilot"), provider="copilot")["observations"])
        for change in ({"status": "failed"}, {"observations": {**BROWSER_OBSERVATIONS, "canaryAbsentInPage": False}}, {"commands": {}}):
            with self.subTest(change=change), self.assertRaises(RuntimeError):
                require_run_browser(self.observed(**change), commands, rows("copilot"), provider="copilot")
        with self.assertRaises(RuntimeError):
            require_run_browser(self.observed(), [{"commandId": COMMAND, "toolName": "workflow_read_executable", "arguments": {}}], rows("copilot"), provider="copilot")

    def test_the_public_browser_inputs_never_carry_the_canary(self):
        config = {"journey": BROWSER_JOURNEY, "provider": "copilot", "studioOrigin": "http://127.0.0.1:1", "backendOrigin": "http://127.0.0.1:2",
                  "storageState": "private", "workflow": {"run": {"workflowExecutionId": "run-1", "activityExecutionId": "act-1", "path": "/runtime/run-1",
                                                                  "canary": CANARY}}}
        value = run_browser_inputs(config)
        self.assertEqual({"workflowExecutionId": "run-1", "activityExecutionId": "act-1", "path": "/runtime/run-1"}, value["run"])
        self.assertNotIn(CANARY, json.dumps(value))
        self.assertNotIn("storageState", value)
        with self.assertRaises(RuntimeError): run_browser_inputs({**config, "workflow": {"run": {"workflowExecutionId": "run-1"}}})
        with self.assertRaises(RuntimeError):
            run_browser_inputs({**config, "workflow": {"run": {**config["workflow"]["run"], "path": "/runtime/run-1#fragment"}}})

    def test_the_canary_search_reads_every_retained_file(self):
        clean = [self.write("a.json", {"ok": True}), self.write("b.json", {"ok": False})]
        self.assertEqual(2, require_no_canary_in_evidence(clean, CANARY, self.root))
        with self.assertRaises(RuntimeError): require_no_canary_in_evidence([*clean, self.write("c.json", {"value": CANARY})], CANARY, self.root)
        with self.assertRaises(RuntimeError): require_no_canary_in_evidence([{**clean[0], "sha256": "0" * 64}], CANARY, self.root)

    def section(self, native=None, search=None):
        records = []
        for name in sorted(EXPECTED_CASES):
            value = {"id": name, "status": "passed", "adapter": name.split("-", 1)[0], "native": native or synthetic_native(), "browser": synthetic_browser(),
                     "canarySearch": search or synthetic_search()}
            records.append({"id": name, "status": "passed", "record": self.write(name + ".json", value)})
        return {"status": "passed", "evidenceCategory": "deterministic-provider-protocol", "liveProvider": False, "source": "a" * 40,
                "caseIDs": sorted(EXPECTED_CASES), "records": records}

    def test_the_final_gate_requires_every_control_and_a_clean_canary_search(self):
        self.assertEqual(2, len(require_workflow_run(self.section(), self.root, source="a" * 40)))
        self.assertEqual(sorted(RUN_NATIVE), sorted(synthetic_native()))
        self.assertTrue(run_manifest_gaps(None))
        self.assertTrue(run_manifest_gaps({**self.section(), "status": "failed"}))
        self.assertTrue(run_manifest_gaps({**self.section(), "liveProvider": True}))
        self.assertTrue(run_manifest_gaps(self.section(), source="b" * 40))
        for name, native, search in (
                ("control-missing", {key: value for key, value in synthetic_native().items() if key != "runtime-denied"}, None),
                ("value-released", {**synthetic_native(), "explain-activity": {"control": "explain-activity", "values": {"text": "resolved"}, "canaryCaptured": True}}, None),
                ("canary-not-captured", {**synthetic_native(), "explain-activity": {"control": "explain-activity", "values": {"text": "withheld"}}}, None),
                ("not-in-progress", {**synthetic_native(), "in-progress": {"control": "in-progress", "inProgress": False}}, None),
                ("control-offered", {**synthetic_native(), "controls-refused": {"control": "controls-refused", "unchanged": True,
                                                                                  "offered": [RUN_TOOL, sorted(MUTATING_TOOLS)[0]]}}, None),
                ("changed", {**synthetic_native(), "controls-refused": {"control": "controls-refused", "unchanged": False, "offered": [RUN_TOOL]}}, None),
                ("run-control-offered", {**synthetic_native(), "controls-refused": {"control": "controls-refused", "unchanged": True,
                                                                                      "offered": [RUN_TOOL, "workflow_retry_run"]}}, None),
                ("run-tool-missing", {**synthetic_native(), "controls-refused": {"control": "controls-refused", "unchanged": True, "offered": []}}, None),
                ("shape", {**synthetic_native(), "runtime-agent-absent": {"control": "runtime-agent-absent", "code": "other"}}, None),
                ("canary", None, {"files": 3, "found": 1}), ("no-files", None, {"files": 0, "found": 0})):
            with self.subTest(change=name), self.assertRaises(RuntimeError):
                require_workflow_run(self.section(native, search), self.root, source="a" * 40)
        self.assertEqual(ADMISSION_REFUSAL["code"], synthetic_native()["runtime-agent-absent"]["code"])


class RunBrowserFailureDiagnosticTests(unittest.TestCase):
    """Exercise the direct subprocess boundary only; no browser, host or provider runs."""

    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.owned = Path(directory.name)
        self.root = self.owned / "run"
        self.case = self.root / "copilot-workflow-run-browser"
        self.output = self.case / "observation"
        self.output.mkdir(parents=True)
        self.original = RuntimeError("original browser subprocess failure")
        self.report = {"status": "failed", "source": "a" * 40, "cases": [],
                       "stage": "copilot-workflow-run-browser"}
        self.runtime = SimpleNamespace(
            sign_in=Mock(return_value=(None, {"storageState": str(self.case / "storage-private.json")})),
            output=Mock(return_value=self.case), provider="copilot", studio_origin="http://127.0.0.1:41000",
            backend_origin="http://127.0.0.1:41001", command_file=self.case / "command-private.json",
            transport=Mock(return_value=SimpleNamespace(trace_file=self.case / "trace.jsonl")),
            run=SimpleNamespace(run=Mock(side_effect=self.original), report=self.report, save=Mock()))
        self.seed = {"faulted": {"workflowExecutionId": "owned-run", "instancePath": "/runtime/owned-run",
                                 "activities": {"sensitive": {"detail": {"activityExecutionId": "owned-activity"}}}},
                     "nodes": {"sensitive": "sensitive"}}
        node = patch("run_journeys.shutil.which", return_value="node")
        node.start()
        self.addCleanup(node.stop)

    def invoke_failure(self):
        with self.assertRaises(RuntimeError) as caught:
            _browser(self.runtime, "copilot-workflow-run-browser", self.seed, CANARY)
        self.assertIs(self.original, caught.exception)
        self.assertEqual("failed", self.report["status"])

    def write_diagnostic(self):
        value = {"kind": "AssertionError", "message": CANARY, "password": "private-password",
                 "frames": [{"file": "browser.mjs", "line": 111, "column": 2},
                            {"file": "browser.mjs", "line": 111, "column": 2},
                            {"file": "/private/browser.mjs", "line": 222, "column": 3},
                            {"file": "browser.mjs", "line": True, "column": 3}]}
        path = self.output / "failure-diagnostic.json"
        path.write_text(json.dumps(value))
        return path

    def test_failed_browser_retains_safe_primary_location_and_same_exception(self):
        path = self.write_diagnostic()
        original_bytes = path.read_bytes()
        self.invoke_failure()
        self.assertEqual({"kind": "AssertionError", "frames": [{"file": "browser.mjs", "line": 111, "column": 2}]},
                         self.report.get("browserFailureDiagnostic"))
        self.runtime.run.save.assert_called_once()
        self.assertEqual(original_bytes, path.read_bytes())
        self.assertNotIn(CANARY, json.dumps(self.report))
        self.assertNotIn("private-password", json.dumps(self.report))

    def test_missing_or_malformed_diagnostic_keeps_original_failure(self):
        path = self.output / "failure-diagnostic.json"
        for data in (None, "{", "[]", '{"kind":"private-kind","frames":false}'):
            with self.subTest(data=data):
                self.report.pop("browserFailureDiagnostic", None)
                self.runtime.run.save.reset_mock()
                if data is None:
                    path.unlink(missing_ok=True)
                else:
                    path.write_text(data)
                self.invoke_failure()
                if data is None or data == "{":
                    self.assertNotIn("browserFailureDiagnostic", self.report)
                    self.runtime.run.save.assert_not_called()
                else:
                    self.assertEqual({"kind": "Unavailable", "frames": []}, self.report["browserFailureDiagnostic"])

    def test_diagnostic_read_or_save_failure_cannot_mask_subprocess_failure(self):
        self.write_diagnostic()
        with patch.object(Path, "read_text", side_effect=TimeoutError("diagnostic read failed")):
            self.invoke_failure()
        self.runtime.run.save.assert_not_called()
        self.runtime.run.save.side_effect = RuntimeError("diagnostic save failed")
        self.invoke_failure()
        self.runtime.run.save.assert_called_once()

    def test_success_does_not_consume_failure_diagnostic_or_change_report(self):
        self.write_diagnostic()
        self.runtime.run.run.side_effect = None
        self.runtime.transport.return_value.protocol_trace = Mock(return_value={"incompleteTailBytes": 0, "records": [], "receipt": {}})
        self.runtime.save = Mock(return_value={})
        (self.output / "observation.json").write_text(json.dumps({"evidence": [], "ownerBefore": {}, "ownerAfter": {}}))
        before = copy.deepcopy(self.report)
        with patch("run_journeys.require_run_browser", return_value={"syntheticUnitOnly": True}):
            value, _ = _browser(self.runtime, "copilot-workflow-run-browser", self.seed, CANARY)
        self.assertEqual({"syntheticUnitOnly": True}, value)
        self.assertEqual(before, self.report)
        self.runtime.run.save.assert_not_called()

    def test_safe_failure_survives_strict_full_export_refusal(self):
        from export_evidence import export_evidence
        from export_failure_diagnostic import export_failure_diagnostic

        self.write_diagnostic()
        outside = self.owned / "outside.txt"
        outside.write_text("outside the run")
        self.report.update(evidence=[{"path": str(outside), "sha256": sha(outside)}], failure={"message": CANARY})
        report = self.root / "report.json"
        self.runtime.run.save.side_effect = lambda: report.write_text(json.dumps(self.report))
        self.invoke_failure()
        with self.assertRaisesRegex(ValueError, "escaped"):
            export_evidence(self.root, self.owned / "full")
        destination = self.owned / "diagnostic"
        index = export_failure_diagnostic(self.root, destination)
        selected = json.loads((destination / "report.json").read_text())
        self.assertEqual("copilot-workflow-run-browser", selected["stage"])
        self.assertEqual("AssertionError", selected["browserFailureDiagnostic"]["kind"])
        self.assertEqual(111, selected["browserFailureDiagnostic"]["frames"][0]["line"])
        self.assertFalse(selected["qualificationComplete"])
        self.assertFalse(selected["independentPackageWitness"])
        self.assertEqual("failed", index["qualificationStatus"])
        self.assertNotIn(CANARY, (destination / "report.json").read_text())
        self.assertFalse((self.owned / "full").exists())


if __name__ == "__main__":
    unittest.main()
