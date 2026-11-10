"""Mandatory workflow-run qualification (spec 1193, story 9; T045, FR-005, FR-027, FR-029, SC-005).

On each actual adapter, Weaver's read-only explanation of one exact Run must hold against the packaged consumer's own run and
evidence APIs. The seed (owner_resources.seed_faulted_run) publishes and executes two workflows in the packaged consumer:

- a Run that faults: a Write Line whose text is a private canary authored sensitive, then a Fault that faults the Run with one
  blocking incident. The runtime's default capture policy never captures a sensitive payload, which would make every
  "canary absent" check vacuous, so the case selects the test-only host seam QualificationRunCapture first: it captures
  sensitive activity inputs as full payloads, and the driver requires the run API to report the canary input captured before
  it checks that Weaver withholds it;
- a Run that is still in progress: an Event that does not start the workflow. The runtime keeps the Run Running and suspends
  the Event's activity execution on its bookmark (Suspended, BookmarkWaiting), the waiting signal the seed settles on.

Native controls, each a fresh session and turn:
- explain-fault: the run page's workflows.run reference (the faulted Run, the Fault's activity execution selected) is admitted
  as the server's own read (data.message.context), bound to that Workflow Instance and activity execution; every admission
  repeats it; workflow_read_run returns the instance, Executable, incidents and activity executions the run API reads, and
  the selected execution's incidents and value evidence, each value's disclosure following its evidence (a captured value
  released only by the audited payload read and equal to the payload API's answer);
- explain-activity: the Write Line's activity execution selected: its captured sensitive input is disclosed only as withheld,
  never as a value, and listed as withheld; its state view is withheld too (the activity has a sensitive input);
- in-progress: the waiting Run is attributed to its read time (readAt, inProgress, the attribution sentence) and its
  selected execution's bookmarks are the run API's;
- not-permitted: an actor with runtime read and structure inspection but no value inspection reads incident messages as the
  run API's redaction, listed as not permitted, and no value;
- controls-refused: a build-mode, full-auto turn asking to retry, cancel and delete with only the Run in context: the engine
  admits no mutating tool for that target, so the adapter is offered the read and none of MUTATING_TOOLS (and no run
  control, which no package registers); the read completes and the Run reads back byte-equal;
- unknown-run, unknown-activity and runtime-denied (an actor without workflows/runtime:read) are refused at admission with
  the module's own texts;
- runtime-agent-absent: with the Runtime-side agent feature deselected, a workflows.run reference is explicitly unavailable
  (agent.context.kind_unsupported); the feature is selected again afterwards.

The packaged consumer cannot prune activity evidence (its in-memory runtime stores keep a Run's evidence for the process
lifetime) and composes activity execution inspection as part of WorkflowsRuntimeApi, so a pruned execution and a backend
without activity execution inspection are proven by WorkflowRunInspectionTests (Runtime API) and WorkflowRunAgentTests
(Elsa.Weaver.Workflows.Runtime.Tests); natively the Fault's state view is reported unavailable by reason and the Runtime-side
deployment shape is deselected. The deselection reloads the shell, which empties the consumer's in-memory run stores, so it
runs after the browser journey, last. The canary is searched, raw, in every disclosed payload, stream frame, adapter trace and
retained evidence file of the case, and in the browser journey's own agent traffic. The Write Line writes its text to the
backend's console, which is the host's own process log and not a Weaver disclosure; it is not part of the case evidence.

In the actual packaged Studio (browser.mjs, workflow-run) the run page, with the Write Line's activity execution (the captured
canary) selected, discloses the exact Run and selection and its Explain action reads it; the page's own agent traffic and
streams are searched raw for the canary. Frame orders are each adapter's own, pinned from earlier witnesses (explain_journeys.READ_FRAMES).
Nothing is retried.
"""
from __future__ import annotations

from contextlib import contextmanager
import copy
import hashlib
import json
from pathlib import Path
import os
import secrets
import shutil
import signal
import threading
import time
import uuid

from artifacts import TOOL, check, sha
from explain_journeys import READ_FRAMES
from authorization_journeys import select_command
from inspection_journeys import (ADMISSION_REFUSAL, MUTATING_TOOLS, RUNTIME_AGENT_FEATURE, RUNTIME_DENIED, _completed, _frames, _kinds, _owned,
                                 _called_once, _offered, _refused_at_admission, _session, _without_feature, require_admissions, require_unchanged)
from journey_runtime import BASE_GRANTS, safe_browser_failure, same
from owner_resources import owner_path, seed_faulted_run
from runtime import RUNTIME_ENVIRONMENT, private_json
from truth_seam_journeys import CAPABILITIES_READ, same_target


SECTION = "workflowRunQualification"
EVIDENCE_CATEGORY = "deterministic-provider-protocol"
CASE_SECONDS = 720
PROVIDERS = ("copilot", "anthropic")
EXPECTED_CASES = frozenset(provider + "-workflow-run" for provider in PROVIDERS)
WORKFLOW_MODULE = "Elsa.Studio.Workflows"
RUN_TOOL, RUN_KIND, RUN_GUIDANCE, RUN_RESOURCE = "workflow_read_run", "workflows.run", "workflows.run-guidance", "workflow-run"
RUN_CAPABILITY, CHANGE_CAPABILITY = "workflow.run-explain", "workflow.propose-change"
# The test-only host seam (fixtures/BackendConsumer/QualificationRunCapture.cs) that captures sensitive activity inputs.
SEAM_FEATURE = "QualificationRunCapture"
# No package registers a run control; a tool whose name says one would be a regression.
RUN_CONTROL_WORDS = ("retry", "cancel", "delete", "dispatch", "rerun", "restart")
TEXTS = {
    "run-not-found": "The workflow Run is unavailable.",
    "activity-not-found": "The selected activity execution is not part of this workflow Run.",
    "run-denied": "Current workflow runtime read permission is required for the workflow Run.",
    "kind-unsupported": "No installed module admits the supplied context kind and owner.",
    "run-controls": "run page's own controls",
    "redacted": "Incident details are redacted.",
}
# The dedicated Run actor, appended after the library actor so every earlier index keeps its meaning: runtime read and
# structure inspection (the run endpoints' own per-run grant), but no value inspection, so incident messages and values
# are not permitted. api-capabilities:read discovers the run links.
RUN_LIMITED = 10
RUN_LIMITED_GRANTS = [*BASE_GRANTS, CAPABILITIES_READ, "workflows/runtime:read", "workflows/activity-executions:inspect"]
RUN_NATIVE = ("explain-fault", "explain-activity", "in-progress", "not-permitted", "controls-refused", "unknown-run", "unknown-activity",
              "runtime-denied", "runtime-agent-absent")
BROWSER_JOURNEY = "workflow-run"
BROWSER_OBSERVATIONS = {"runContext": True, "selectionBound": True, "explainCompleted": True, "readOnly": True, "canaryAbsentInPage": True}
REASONS = ("notCaptured", "pruned", "unsupported", "notPermitted")


def run_principal(principals, new_identity):
    """The Run actor, in the first principal's tenant (the consumer stores runs under the default tenant, #3701)."""
    check(len(principals) == RUN_LIMITED, "The Run principal extends exactly the ten earlier principals")
    return [new_identity("witness-run-limited", principals[0]["tenant"], RUN_LIMITED_GRANTS)]


# ---- Attachments: the run page's own reference, as Studio builds it (workflowRunAttachment). ----

def run_attachment(workflow_execution_id, activity_execution_id=None):
    """The run page's reference to one exact Run and the activity execution selected on it: identity only."""
    check(isinstance(workflow_execution_id, str) and 0 < len(workflow_execution_id) <= 256, "A Run reference needs its Workflow Instance identity")
    check(activity_execution_id is None or isinstance(activity_execution_id, str) and 0 < len(activity_execution_id) <= 256
          and activity_execution_id != workflow_execution_id, "A selection is one activity execution of the Run")
    return {"id": "workflows.run:" + workflow_execution_id, "moduleId": WORKFLOW_MODULE, "schemaVersion": "1", "contentType": RUN_KIND,
            "sensitivity": "internal", "scope": "screen", "origin": "clientSnapshot", "source": RUN_RESOURCE, "sourceId": workflow_execution_id,
            "label": "Run " + workflow_execution_id, "summary": "Workflow Run " + workflow_execution_id,
            "target": {"moduleId": WORKFLOW_MODULE, "resourceType": RUN_RESOURCE, "resourceId": workflow_execution_id,
                       "revision": activity_execution_id or workflow_execution_id},
            "client": {"clientId": "qualification-runs", "generationId": str(uuid.uuid4())},
            "content": {"workflowExecutionId": workflow_execution_id, "activityExecutionId": activity_execution_id}}


# ---- Pure checks over a native turn's frames and adapter rows, against the run API's own reads. ----

def no_canary(canary, *values):
    check(isinstance(canary, str) and len(canary) >= 24, "Use a distinct Run canary")
    for value in values:
        text = value.decode("utf-8", "replace") if isinstance(value, (bytes, bytearray)) else value if isinstance(value, str) else json.dumps(value, sort_keys=True)
        check(canary not in text, "The Run canary was disclosed")


def expected_disclosure(evidence):
    """A value's disclosure as its evidence (the run API's activity execution read) determines it: withheld by its marker or
    because it is sensitive (never released, captured or not); not captured; not permitted; otherwise released by the
    payload read."""
    capture, access = evidence.get("captureState"), evidence.get("accessState")
    if evidence.get("withheldKind") or evidence.get("isSensitive") is True:
        return "withheld", None
    if capture in ("notCaptured", "metadataOnly", "captureFailed", "unavailable"):
        return "unavailable", "notCaptured"
    if access in ("redacted", "resolutionPermissionRequired"):
        return "unavailable", "notPermitted"
    if access == "resolutionAvailable":
        return "resolved", None
    return "unavailable", "notCaptured"


def require_run_context(item, attachment, run, selected=None):
    """The server's own read of the referenced Run: that Workflow Instance (never another Run), its Executable, status and
    counts as the run API reads them, and the selection. Returns the content without its read time, which an in-progress
    Run's admissions each carry anew."""
    check(isinstance(item, dict) and item.get("contentType") == RUN_KIND and item.get("origin") in ("authoritativeRead", "AuthoritativeRead")
          and item.get("moduleId") == WORKFLOW_MODULE and same_target(item.get("target"), attachment["target"]),
          "The Run reference was not admitted as the server's read of that exact Run and selection")
    content, instance = item.get("content"), run["instance"]["instance"]
    terminal = instance["status"] in ("Completed", "Faulted", "Cancelled")
    check(isinstance(content, dict) and content.get("workflowExecutionId") == instance["workflowExecutionId"]
          and content.get("activityExecutionId") == attachment["content"]["activityExecutionId"]
          and all(content.get(key) == instance.get(key) for key in ("status", "artifactId", "artifactHash", "definitionId", "definitionVersionId", "incidentCount"))
          and content.get("inProgress") is (not terminal) and (content.get("readAt") is None) is terminal,
          "The admitted Run is not the run API's read of that Run")
    if selected is None:
        check(content.get("selectedActivity") is None, "A Run without a selection was admitted with one")
    else:
        detail = selected["detail"]
        chosen = content.get("selectedActivity") or {}
        check(chosen.get("activityExecutionId") == detail["activityExecutionId"] and chosen.get("activityType") == detail["activityType"]
              and chosen.get("status") == detail["status"] and chosen.get("valueCount") == len(detail.get("valueSnapshots") or []),
              "The admitted selection is not the run API's read of that activity execution")
    return {key: value for key, value in content.items() if key != "readAt"}


def require_run_result(data, run, *, selected=None, payloads=None, permitted=True):
    """The tool's read equals the run API's: instance, incidents, activity executions and the selected execution's
    incidents, bookmarks and value evidence, each value disclosed as its evidence determines."""
    instance = run["instance"]["instance"]
    check(isinstance(data, dict) and data.get("readOnly") is True and data.get("workflowExecutionId") == instance["workflowExecutionId"]
          and isinstance(data.get("instance"), dict)
          and all(data["instance"].get(key) == instance.get(key) for key in ("status", "artifactId", "artifactHash", "definitionId", "definitionVersionId",
                                                                               "incidentCount", "activityCount")),
          "The read instance is not the run API's")
    terminal = instance["status"] in ("Completed", "Faulted", "Cancelled")
    check(data.get("inProgress") is (not terminal) and isinstance(data.get("readAt"), str) and isinstance(data.get("attribution"), str)
          and ("has finished" if terminal else "still in progress") in data["attribution"],
          "The read is not attributed as the run API's status says")
    check(data.get("canInspectValues") is permitted and data.get("canResolveValues") is permitted and TEXTS["run-controls"] in str(data.get("runControls")),
          "The read does not report the actor's value inspection and resolution or the run controls")
    incident = lambda row: (row.get("incidentId"), row.get("activityExecutionId"), row.get("failureType"), row.get("isBlocking"), row.get("message"))
    check(data.get("incidentsTruncated") is False
          and sorted(incident(row) for row in data.get("incidents") or []) == sorted(incident(row) for row in run["instance"].get("incidents") or [])
          and all(row.get("messageRedacted") is (not permitted) for row in data.get("incidents") or []),
          "The read incidents differ from the run API's")
    listed = sorted(row.get("activityExecutionId") for row in (data.get("activities") or {}).get("items") or [])
    check(listed == sorted(row["activityExecutionId"] for row in run["instance"].get("activities") or [])
          and (data.get("activities") or {}).get("itemsTruncated") is False, "The read activity executions differ from the run API's")
    unavailable = {(row.get("data"), row.get("reason")) for row in data.get("unavailable") or [] if isinstance(row, dict)}
    check(all(reason in (*REASONS, "withheld") for _, reason in unavailable), "An unavailable item names an unknown reason")
    if not permitted and run["instance"].get("incidents"):
        check(("incident messages", "notPermitted") in unavailable, "Redacted incident messages are not listed as not permitted")
    record = {"status": instance["status"], "incidents": len(data.get("incidents") or []), "activities": len(listed), "inProgress": data["inProgress"]}
    if selected is None:
        check(data.get("selectedActivity") is None, "A selection was read that the context does not name")
        return record
    detail, chosen = selected["detail"], data.get("selectedActivity") or {}
    check(chosen.get("activityExecutionId") == detail["activityExecutionId"] and (chosen.get("summary") or {}).get("status") == detail["status"],
          "The read selection is not the run API's activity execution")
    check(sorted(row.get("incidentId") for row in chosen.get("incidents") or []) == sorted(row.get("incidentId") for row in detail.get("incidents") or [])
          and sorted(row.get("bookmarkId") for row in chosen.get("bookmarks") or [])
          == sorted((row.get("identity") or {}).get("bookmarkId") for row in detail.get("bookmarks") or []),
          "The selected execution's incidents or bookmarks differ from the run API's")
    values = {row.get("evidenceId"): row for row in chosen.get("values") or []}
    evidence = {row["evidenceId"]: row for row in detail.get("valueSnapshots") or []}
    check(chosen.get("valuesTruncated") is False and sorted(values) == sorted(evidence), "The read value evidence differs from the run API's")
    disclosures = {}
    for evidence_id, row in evidence.items():
        value = values[evidence_id]
        disclosure, reason = expected_disclosure(row)
        check((value.get("disclosure"), value.get("unavailableReason")) == (disclosure, reason) and value.get("sensitive") is (row.get("isSensitive") is True)
              and value.get("captureState") == row.get("captureState") and value.get("accessState") == row.get("accessState"),
              "Value " + str(row.get("name")) + " is not disclosed as its evidence determines: " + json.dumps([value.get("disclosure"), value.get("unavailableReason")]))
        if disclosure == "resolved":
            check(payloads is not None and evidence_id in payloads and same(value.get("value"), payloads[evidence_id]),
                  "A released value differs from the payload API's answer")
        else:
            check(value.get("value") is None, "A value that is not released was disclosed")
        if disclosure == "withheld":
            check(any(reason == "withheld" for _, reason in unavailable), "A withheld value is not listed as withheld")
        disclosures[str(row.get("inputKey") or row.get("name"))] = disclosure
    state = chosen.get("stateView") or {}
    sensitive = any(row.get("isSensitive") is True and row.get("subject") == "ActivityInput" for row in evidence.values())
    if sensitive:
        check(state.get("withheld") is True and state.get("available") is False and state.get("view") is None,
              "The state view of an activity with a sensitive input was not withheld")
    else:
        check(state.get("withheld") is False and (state.get("available") is False and state.get("unavailableReason") in ("notCaptured", "unsupported")
              and state.get("view") is None or state.get("available") is True), "The state view is neither shown nor unavailable by reason")
    label = "withheld" if state.get("withheld") else state.get("unavailableReason") or "available"
    return {**record, "selected": detail["activityExecutionId"], "values": disclosures, "stateView": label}


def require_run_read(frames, rows, accepted, *, provider, command_id, attachment, run, selected=None, payloads=None, permitted=True, canary):
    """One completed Run read: the admitted context, this adapter's pinned order, the run API's answers, and no canary."""
    check(provider in PROVIDERS, "Unknown adapter")
    values = _frames(frames)
    _kinds(values, READ_FRAMES[provider], "The Run read turn")
    require_admissions(values, accepted, RUN_KIND, lambda item: require_run_context(item, attachment, run, selected))
    completed = _completed(values, RUN_TOOL, succeeded=True)
    payload = (completed.get("result") or {}).get("payload") or {}
    check(payload.get("typeId") == RUN_KIND, "The Run tool did not return its typed result")
    record = require_run_result(payload.get("data"), run, selected=selected, payloads=payloads, permitted=permitted)
    _called_once(rows, provider, command_id, RUN_TOOL)
    no_canary(canary, values, rows, accepted)
    return record


def require_controls_unoffered(frames, rows, *, provider, command_id):
    """A retry, cancel and delete request with only the Run in context: the read is offered, no mutating workflow tool or run
    control is, and nothing is proposed. Returns the offered tools."""
    check(provider in PROVIDERS, "Unknown adapter")
    values = _frames(frames)
    check(not any(frame.get("kind") in ("proposalCreated", "toolApprovalRequested") for frame in values), "A Run turn proposed a change")
    offered = _offered(_owned(rows, provider, command_id), provider)
    check(RUN_TOOL in offered and not MUTATING_TOOLS & set(offered) and not any(word in name for name in offered for word in RUN_CONTROL_WORDS),
          "With only a Run in context the adapter must be offered the read and no mutating tool or run control: " + json.dumps(offered))
    return offered


# ---- Native runner. ----

@contextmanager
def _bounded(runtime, name):
    check(threading.current_thread() is threading.main_thread() and hasattr(signal, "setitimer"),
          "Native Run qualification requires an interruptible main-thread stage deadline")
    check(signal.getitimer(signal.ITIMER_REAL) == (0.0, 0.0), "Do not replace another qualification stage's active deadline")
    started = time.monotonic()
    stage = {"name": name, "status": "running", "boundSeconds": CASE_SECONDS}
    runtime.run.report[SECTION]["stages"].append(stage)
    runtime.run.save()
    def expired(_signum, _frame):
        raise TimeoutError("Native Run stage expired without retry: " + name)
    handler = signal.signal(signal.SIGALRM, expired)
    signal.setitimer(signal.ITIMER_REAL, CASE_SECONDS)
    try:
        yield
    except BaseException:
        stage["status"] = "failed"
        raise
    else:
        stage["status"] = "completed"
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, handler)
        stage["elapsedSeconds"] = time.monotonic() - started
        runtime.run.save()


def _payloads(runtime, name, http, seed, run, selected):
    """The payload API's own answer for every value the run API says may be resolved, each read authorized and audited."""
    template = seed["runtimeLinks"].get("activity-execution-value-payload")
    answers, evidence = {}, []
    for row in selected["detail"].get("valueSnapshots") or []:
        if expected_disclosure(row)[0] != "resolved": continue
        check(template, "The run API advertises no value payload read")
        path = owner_path(http, template, workflowExecutionId=run["workflowExecutionId"], activityExecutionId=selected["detail"]["activityExecutionId"],
                          evidenceId=row["evidenceId"])
        value, ref = runtime.native.json(http, name + "-payload-" + row["evidenceId"][:16], path)
        answers[row["evidenceId"]] = value.get("payload")
        evidence.append(ref["receipt"])
    return answers, evidence


def _turn(runtime, name, http, attachment, capabilities, content, *, mode, autonomy):
    """One native turn whose adapter calls workflow_read_run with no arguments (the Run is the admitted context's), in a fresh session."""
    transport = runtime.transport(name, http=http)
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    created, session = _session(transport, mode, autonomy)
    command_id = str(uuid.uuid4())
    command = select_command(transport, command_id, RUN_TOOL, {})
    message = transport.message(session, {"content": content, "mode": mode, "requestId": str(uuid.uuid4()), "requestedCapabilities": capabilities,
                                          "contextAttachments": [attachment]}).require_status(200)
    stream = transport.collect_turn(session)
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the " + name + " turn")
    return {"commandId": command_id, "frames": [frame.value for frame in stream.frames], "rows": trace["records"], "accepted": message.json(),
            "evidence": [created.receipt, command, message.receipt, stream.receipt, trace["receipt"]]}


def _read(runtime, name, http, seed, run, selected, *, permitted=True, canary, mode="explain", autonomy="auto-read-only", capabilities=None,
          content="Owned workflow Run qualification request: explain this Run."):
    attachment = run_attachment(run["workflowExecutionId"], selected["detail"]["activityExecutionId"] if selected else None)
    payloads, evidence = _payloads(runtime, name, http, seed, run, selected) if selected and permitted else ({}, [])
    turn = _turn(runtime, name, http, attachment, capabilities or [RUN_CAPABILITY], content, mode=mode, autonomy=autonomy)
    record = require_run_read(turn["frames"], turn["rows"], turn["accepted"], provider=runtime.provider, command_id=turn["commandId"], attachment=attachment,
                              run=run, selected=selected, payloads=payloads, permitted=permitted, canary=canary)
    return record, [*evidence, *turn["evidence"]], turn


def _native(runtime, name, seed, canary):
    records, evidence = {}, []
    faulted, waiting, n = seed["faulted"], seed["waiting"], seed["nodes"]
    record, refs, _ = _read(runtime, name + "-explain-fault", runtime.backend, seed, faulted, faulted["activities"][n["fault"]], canary=canary)
    records["explain-fault"], evidence = {"control": "explain-fault", **record}, evidence + refs
    sensitive = faulted["activities"][n["sensitive"]]
    # The seam made the runtime capture the canary, so this control could fail: a regression that released a captured
    # sensitive value would put the canary in the disclosed payload.
    captured = [row for row in sensitive["detail"].get("valueSnapshots") or [] if (row.get("inputKey") or row.get("name")) == "text"]
    check(len(captured) == 1 and captured[0].get("isSensitive") is True and captured[0].get("captureState") == "payloadCaptured",
          "The run API does not hold the captured sensitive input, so its withholding would prove nothing: " + json.dumps(captured)[:300])
    # One admin resolve through the payload API confirms the captured payload really is the canary. Its body is never
    # retained (the canary search would rightly find it); only its status, digest and the confirmation are.
    template = seed["runtimeLinks"].get("activity-execution-value-payload")
    check(template, "The run API advertises no value payload read")
    path = owner_path(runtime.backend, template, workflowExecutionId=faulted["workflowExecutionId"],
                      activityExecutionId=sensitive["detail"]["activityExecutionId"], evidenceId=captured[0]["evidenceId"])
    status, body, _ = runtime.backend.request(path)
    check(status == 200 and canary.encode() in body, "The payload API does not resolve the captured sensitive input to the canary (HTTP " + str(status) + ")")
    evidence.append(runtime.save(runtime.output(name + "-canary-captured"), "payload-digest",
                                 {"status": status, "sha256": hashlib.sha256(body).hexdigest(), "bytes": len(body), "containsCanary": True}))
    record, refs, _ = _read(runtime, name + "-explain-activity", runtime.backend, seed, faulted, sensitive, canary=canary)
    check(record["values"].get("text") == "withheld" and record["stateView"] == "withheld",
          "The captured sensitive input (or its activity's state view) was not disclosed only as withheld")
    records["explain-activity"], evidence = {"control": "explain-activity", **record, "canaryCaptured": True}, evidence + refs
    record, refs, _ = _read(runtime, name + "-in-progress", runtime.backend, seed, waiting, waiting["activities"][n["wait"]], canary=canary)
    check(record["inProgress"] is True, "The waiting Run was not read as in progress")
    records["in-progress"], evidence = {"control": "in-progress", **record}, evidence + refs
    # An actor without value inspection: compared with that actor's own run API reads.
    limited, _ = runtime.sign_in(RUN_LIMITED)
    instance, ref = runtime.native.json(limited, name + "-limited-instance", faulted["instancePath"])
    detail, detail_ref = runtime.native.json(limited, name + "-limited-activity", faulted["activities"][n["fault"]]["path"])
    evidence += [ref["receipt"], detail_ref["receipt"]]
    view = {**faulted, "instance": instance}
    record, refs, _ = _read(runtime, name + "-not-permitted", limited, seed, view, {"detail": detail, "path": faulted["activities"][n["fault"]]["path"]},
                            permitted=False, canary=canary)
    check(all(row.get("message") == TEXTS["redacted"] for row in instance.get("incidents") or []), "The run API disclosed incident details to the limited actor")
    records["not-permitted"], evidence = {"control": "not-permitted", **record}, evidence + refs
    runtime.sign_in()
    # Retry, cancel and delete are the run page's own: only the read is offered, it completes, and the Run is unchanged.
    before = runtime.native.request(runtime.backend, name + "-run-before", faulted["instancePath"])[0]
    record, refs, turn = _read(runtime, name + "-controls-refused", runtime.backend, seed, faulted, None, canary=canary, mode="build", autonomy="full-auto",
                               capabilities=[CHANGE_CAPABILITY, RUN_CAPABILITY],
                               content="Owned workflow Run qualification request: retry this Run, cancel it, then delete it.")
    offered = require_controls_unoffered(turn["frames"], turn["rows"], provider=runtime.provider, command_id=turn["commandId"])
    after = runtime.native.request(runtime.backend, name + "-run-after", faulted["instancePath"])[0]
    require_unchanged(before, after, "The Run")
    records["controls-refused"] = {"control": "controls-refused", "read": record, "offered": offered, "unchanged": True}
    evidence += [*refs, before["receipt"], after["receipt"]]
    for control, attachment, text in (
            ("unknown-run", run_attachment("qualification-missing-run-" + secrets.token_hex(6)), TEXTS["run-not-found"]),
            ("unknown-activity", run_attachment(faulted["workflowExecutionId"], "qualification-missing-activity-" + secrets.token_hex(6)), TEXTS["activity-not-found"])):
        refusal, refs = _refused_at_admission(runtime, name + "-" + control, runtime.backend, attachment, RUN_CAPABILITY, text)
        records[control], evidence = {"control": control, **refusal}, evidence + refs
    denied, _ = runtime.sign_in(RUNTIME_DENIED)
    refusal, refs = _refused_at_admission(runtime, name + "-runtime-denied", denied, run_attachment(faulted["workflowExecutionId"]), RUN_CAPABILITY,
                                          TEXTS["run-denied"])
    records["runtime-denied"], evidence = {"control": "runtime-denied", **refusal}, evidence + refs
    runtime.sign_in()
    return records, evidence


def _absent(runtime, name, seed):
    """The deployment-shape control. It reloads the shell, which empties the consumer's in-memory run stores, so it runs last."""
    def absent():
        value, refs = _refused_at_admission(runtime, name + "-runtime-agent-absent", runtime.backend, run_attachment(seed["faulted"]["workflowExecutionId"]),
                                            None, TEXTS["kind-unsupported"])
        return {"control": "runtime-agent-absent", **value}, refs
    return _without_feature(runtime, name + "-runtime-agent", RUNTIME_AGENT_FEATURE, absent)


def _browser(runtime, name, seed, canary):
    _, identity = runtime.sign_in()
    root = runtime.output(name)
    output = root / "observation"
    faulted, n = seed["faulted"], seed["nodes"]
    # The canary travels in the private configuration only (never browser-inputs evidence): the page searches its own agent
    # traffic for it, raw.
    workflow = {"run": {"workflowExecutionId": faulted["workflowExecutionId"], "path": faulted["instancePath"],
                        "activityExecutionId": faulted["activities"][n["sensitive"]]["detail"]["activityExecutionId"], "canary": canary}}
    config = {"journey": BROWSER_JOURNEY, "provider": runtime.provider, "studioOrigin": runtime.studio_origin, "backendOrigin": runtime.backend_origin,
              "storageState": identity["storageState"], "commandFile": str(runtime.command_file), "output": str(output), "workflow": workflow}
    private_json(root / "config-private.json", config)
    transport = runtime.transport(name + "-protocol")
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    node = shutil.which("node")
    check(node, "The actual browser driver needs Node")
    try:
        runtime.run.run(name, [node, str(TOOL / "browser.mjs"), str(root / "config-private.json")], TOOL,
                        env={key: value for key, value in os.environ.items() if key in RUNTIME_ENVIRONMENT})
    except RuntimeError:
        try:
            runtime.run.report["browserFailureDiagnostic"] = safe_browser_failure({
                "failureDiagnostic": json.loads((output / "failure-diagnostic.json").read_text())})
            runtime.run.save()
        except Exception:
            pass  # Optional diagnostics must never replace the original failed subprocess.
        raise
    observation = output / "observation.json"
    observed = json.loads(observation.read_text())
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the Run journey")
    commands = [json.loads(path.read_text()) for path in sorted(output.glob("command-*.json"))]
    value = require_run_browser(observed, commands, trace["records"], provider=runtime.provider)
    evidence = [*observed["evidence"], observed["ownerBefore"], observed["ownerAfter"], {"path": str(observation), "sha256": sha(observation)},
                runtime.save(root, "browser-inputs", run_browser_inputs(config)), trace["receipt"]]
    no_canary(canary, trace["records"])
    return value, evidence


def run_browser_inputs(config):
    """The journey's allowlisted public inputs: the Run's identity, its selected activity execution and path, never the canary
    or the authentication state. Stricter than journey_runtime.browser_inputs, which does not require the selection."""
    run = (config.get("workflow") or {}).get("run") or {}
    check(all(isinstance(run.get(key), str) and run[key] for key in ("workflowExecutionId", "activityExecutionId", "path"))
          and run["path"].startswith("/") and not run["path"].startswith("//") and "?" not in run["path"] and "#" not in run["path"],
          "The Run journey needs native identities and a local path")
    return {**{key: config[key] for key in ("journey", "provider", "studioOrigin", "backendOrigin")}, "driverSha256": sha(TOOL / "browser.mjs"),
            "run": {key: run[key] for key in ("workflowExecutionId", "activityExecutionId", "path")}}


def require_run_browser(observed, commands, rows, *, provider):
    """The run page's own journey, checked against the adapter trace for its retained command."""
    check(provider in PROVIDERS, "Unknown adapter")
    check(isinstance(observed, dict) and observed.get("journey") == BROWSER_JOURNEY and observed.get("status") == "passed"
          and not observed.get("failure") and not observed.get("pageErrors") and observed.get("provider") == provider,
          "The Run Studio journey did not pass on this adapter")
    check(same(observed.get("observations"), BROWSER_OBSERVATIONS), "The Run Studio journey observed another outcome")
    check(isinstance(observed.get("ownerBefore"), dict) and isinstance(observed.get("ownerAfter"), dict)
          and isinstance(observed.get("evidence"), list) and observed["evidence"], "The Run journey retained no owner evidence")
    selected = observed.get("commands")
    check(isinstance(selected, dict) and sorted(selected) == ["explain"], "The Run journey must retain one command for its one step")
    by_id = {}
    for item in commands:
        check(isinstance(item, dict) and isinstance(item.get("commandId"), str) and item["commandId"] not in by_id
              and isinstance(item.get("arguments"), dict), "Each retained browser command must be one distinct request")
        by_id[item["commandId"]] = item
    check(set(by_id) == set(selected.values()) and by_id[selected["explain"]].get("toolName") == RUN_TOOL, "The Run journey commanded another tool")
    _called_once(rows, provider, selected["explain"], RUN_TOOL)
    return {"journey": BROWSER_JOURNEY, "observations": observed["observations"], "commands": selected,
            "ownerBefore": observed["ownerBefore"], "ownerAfter": observed["ownerAfter"]}


def require_no_canary_in_evidence(evidence, canary, root):
    """The raw canary search over every retained evidence file of the case: it must appear in none."""
    check(isinstance(evidence, list) and evidence, "The Run case retained no evidence to search")
    searched = 0
    for ref in evidence:
        path = Path(ref["path"])
        check(path.resolve().is_relative_to(Path(root).resolve()) and path.is_file() and sha(path) == ref["sha256"],
              "Run evidence is missing, changed or outside the owned run")
        check(canary.encode() not in path.read_bytes(), "The Run canary appears in retained evidence: " + path.name)
        searched += 1
    return searched


def _case(runtime, provider):
    name = provider + "-workflow-run"
    evidence = list(runtime.select_provider(provider))
    canary = "CANARY-RUN-SENSITIVE-" + uuid.uuid4().hex
    runtime.sign_in()
    # The test-only capture seam first (a shell reload), so the seeded Runs' sensitive input is really captured.
    evidence += runtime.apply_features(name + "-select-seam", {SEAM_FEATURE: (True, {})})
    try:
        seed = seed_faulted_run(runtime.backend, runtime.output(name + "-workflow") / "run-seed", canary)
        evidence += seed["evidence"]
        native, native_evidence = _native(runtime, name, seed, canary)
        evidence += native_evidence
        browser, browser_evidence = _browser(runtime, name + "-browser", seed, canary)
        evidence += browser_evidence
        native["runtime-agent-absent"], absent_evidence = _absent(runtime, name, seed)
        evidence += absent_evidence
    finally:
        evidence += runtime.apply_features(name + "-deselect-seam", {SEAM_FEATURE: (False, {})})
    searched = require_no_canary_in_evidence(evidence, canary, runtime.run.root)
    return {"id": name, "status": "passed", "adapter": provider, "evidenceCategory": EVIDENCE_CATEGORY, "native": native, "browser": browser,
            "canarySearch": {"files": searched, "found": 0}, "evidence": evidence}


def qualify_workflow_run(runtime):
    run = runtime.run
    check(SECTION not in run.report, "Never resume or overwrite a prior workflow Run qualification")
    original_cases = copy.deepcopy(run.report["cases"])
    section = {"status": "running", "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False, "source": run.source,
               "caseIDs": [], "records": [], "stages": [],
               "driver": {"repositoryPath": "tools/studio/Weaver.Conformance.Probe/run_journeys.py", "sha256": sha(TOOL / "run_journeys.py")}}
    run.report[SECTION] = section
    run.save()
    try:
        for provider in PROVIDERS:
            name = provider + "-workflow-run"
            section["activeCase"] = name
            with _bounded(runtime, name):
                value = _case(runtime, provider)
                record = runtime.save(runtime.output(name + "-record"), "observation", value)
                section["caseIDs"].append(name)
                section["records"].append({"id": name, "status": "passed", "record": record})
        check(set(section["caseIDs"]) == EXPECTED_CASES and len(section["records"]) == len(EXPECTED_CASES),
              "Each workflow Run case must be observed exactly once")
        check(same(original_cases, run.report["cases"]), "Workflow Run qualification changed the original M1 case records")
        section.pop("activeCase", None)
        section["status"] = "passed"
    except BaseException as error:
        section.update(status="failed", failure={"type": type(error).__name__, "message": str(error)})
        raise
    finally:
        run.save()


def run_manifest_gaps(section, source=None):
    """Structural reasons a section cannot complete a witness; reads no files."""
    if not isinstance(section, dict):
        return ["The mandatory workflow Run section is absent"]
    gaps = []
    names = section.get("caseIDs") if isinstance(section.get("caseIDs"), list) else []
    records = section.get("records") if isinstance(section.get("records"), list) else []
    if section.get("status") != "passed":
        gaps.append("Workflow Run section status is " + repr(section.get("status")))
    if section.get("evidenceCategory") != EVIDENCE_CATEGORY or section.get("liveProvider") is not False:
        gaps.append("Unsupported workflow Run provider evidence claim")
    if source is not None and section.get("source") != source:
        gaps.append("Workflow Run section source differs from the witness source")
    if (sorted(names) != sorted(EXPECTED_CASES) or len(names) != len(EXPECTED_CASES)
            or [row.get("id") if isinstance(row, dict) else None for row in records] != names
            or not all(isinstance(row, dict) and row.get("status") == "passed" and isinstance(row.get("record"), dict) for row in records)):
        gaps.append("The workflow Run cases were not each observed passed exactly once")
    return gaps


def require_workflow_run(section, owned_root=None, *, source=None):
    """Mandatory final gate: both adapters passed, and each retained record still holds every native control, the withheld
    sensitive input, the in-progress attribution, the refused run controls, the deployment shape, the journey and a canary
    search that found nothing."""
    gaps = run_manifest_gaps(section, source)
    check(not gaps, "The workflow Run inventory is mandatory on each actual adapter: " + "; ".join(gaps))
    if owned_root is not None:
        root = Path(owned_root).resolve()
        for row in section["records"]:
            path = Path(row["record"]["path"])
            check(path.is_absolute() and path.resolve().is_relative_to(root) and path.is_file() and sha(path) == row["record"]["sha256"],
                  "Workflow Run record is missing, changed or outside the owned run: " + row["id"])
            observed = json.loads(path.read_text())
            native = observed.get("native") if isinstance(observed.get("native"), dict) else {}
            search = observed.get("canarySearch") if isinstance(observed.get("canarySearch"), dict) else {}
            controls = native.get("controls-refused") or {}
            check(observed.get("id") == row["id"] and observed.get("status") == "passed" and observed.get("adapter") == row["id"].split("-", 1)[0]
                  and sorted(native) == sorted(RUN_NATIVE) and all((native.get(control) or {}).get("control") == control for control in RUN_NATIVE)
                  and ((native.get("explain-activity") or {}).get("values") or {}).get("text") == "withheld"
                  and (native.get("explain-activity") or {}).get("canaryCaptured") is True
                  and (native.get("in-progress") or {}).get("inProgress") is True
                  and controls.get("unchanged") is True and isinstance(controls.get("offered"), list) and not MUTATING_TOOLS & set(controls["offered"])
                  and RUN_TOOL in controls["offered"] and not any(word in str(name) for name in controls["offered"] for word in RUN_CONTROL_WORDS)
                  and (native.get("runtime-agent-absent") or {}).get("code") == ADMISSION_REFUSAL["code"]
                  and search.get("found") == 0 and type(search.get("files")) is int and search["files"] > 0
                  and same((observed.get("browser") or {}).get("observations"), BROWSER_OBSERVATIONS),
                  "Workflow Run record differs: " + row["id"])
    return section["records"]


def synthetic_native():
    """The minimal native record shape the final gate checks; for synthetic self-tests only, never native evidence."""
    value = {control: {"control": control} for control in RUN_NATIVE}
    value["explain-activity"].update(values={"text": "withheld"}, canaryCaptured=True)
    value["in-progress"]["inProgress"] = True
    value["controls-refused"].update(unchanged=True, offered=[RUN_TOOL])
    value["runtime-agent-absent"]["code"] = ADMISSION_REFUSAL["code"]
    return value


def synthetic_browser():
    """The minimal browser shape the final gate checks; for synthetic self-tests only."""
    return {"journey": BROWSER_JOURNEY, "observations": copy.deepcopy(BROWSER_OBSERVATIONS)}


def synthetic_search():
    """The minimal canary search shape the final gate checks; for synthetic self-tests only."""
    return {"files": 1, "found": 0}
