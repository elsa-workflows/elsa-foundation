"""Mandatory workflow-authorization-unavailable qualification (spec 1193, story 5; T006).

On each actual adapter, a replacement workflow definition authorizer that cannot decide must answer the
native definition API as unavailable (503, errorCode workflow.authorization.unavailable, no cause) and
must stop a Weaver read-graph turn closed, with the workflow module's fixed unavailable text, before the
tool dispatches; a replacement that denies must keep the existing denied outcomes. The replacement is the host's test-only QualificationWorkflowAuthority feature,
selected through the ordinary native feature apply for this inventory only. Its private mode file switches
decisions without a reload, and a pre-call gate holds the actual adapter so the call's pre-dispatch
admission refresh meets the replacement after the actual owner admitted the context. Nothing is retried.
"""
from __future__ import annotations

from contextlib import contextmanager
import copy
import json
from pathlib import Path
import signal
import threading
import time
import uuid

from weaver_transport import _json as strict_json
from artifacts import TOOL, check, sha
from case_records import retained
from journey_runtime import atomic_json, same
from owner_resources import seed_workflow


SECTION = "workflowAuthorizationQualification"
EVIDENCE_CATEGORY = "deterministic-provider-protocol"
CASE_SECONDS = 240
PROVIDERS = ("copilot", "anthropic")
EXPECTED_CASES = frozenset(provider + "-workflow-authorization-unavailable" for provider in PROVIDERS)
SEAM_FEATURE = "QualificationWorkflowAuthority"
READ_TOOL = "workflow_read_graph"
WORKFLOW_MODULE = "Elsa.Studio.Workflows"
UNAVAILABLE_CODE = "workflow.authorization.unavailable"
DESIGN_ENVELOPE = "One or more errors occurred!"
UNAVAILABLE_MESSAGE = "Workflow definition authorization could not be completed."
DENIED_MESSAGE = "The requested workflow definition is not readable in the current application context."
# A native turn's tool dispatch first refreshes the turn's current context admission (the #3439 current-authority
# boundary), which asks the same owner before the tool's own owner check can run. A replacement owner therefore stops
# the turn at that refresh. An owner that cannot decide reports as unavailable: the workflow module marks its
# diagnostic as owner-unavailable, so the refusal carries the Design API's stable code with 503 and the engine's fixed
# text. A denying owner keeps the ordinary incomplete-context refusal with the module's fixed read-permission text. The
# tool's own owner check is never reached through a native turn; its 503 is component evidence (Elsa.Weaver.Tests).
TURN_REFUSALS = {
    "unavailable": {"code": UNAVAILABLE_CODE, "statusCode": 503,
                    "message": "The owning module could not establish current authority for this operation."},
    "denied": {"code": "weaver.context.incomplete", "statusCode": 400,
               "message": "Current read permission for the workflow context is required."},
}


def _problem(data):
    try: value = json.loads(data)
    except (ValueError, UnicodeError): value = None
    check(isinstance(value, dict), "The design API answer is not a JSON problem body")
    return value


def require_unavailable_problem(status, data, cause):
    """503 in the design error shape with the stable code and the fixed message, and no trace of the cause."""
    check(status == 503, "An owner that cannot decide must answer 503, not " + str(status))
    value = _problem(data)
    check(set(value) == {"errors", "message", "statusCode", "errorCode"} and value["errorCode"] == UNAVAILABLE_CODE
          and value["statusCode"] == 503 and value["message"] == DESIGN_ENVELOPE
          and same(value["errors"], {"generalErrors": [UNAVAILABLE_MESSAGE]}),
          "The unavailable answer lost its stable code, fixed message or established design error shape")
    _no_cause(data, cause)
    return value


def require_denied_problem(status, data):
    """The existing 403 body, unchanged: no errorCode member at all."""
    check(status == 403, "A denying owner must keep the existing 403, not " + str(status))
    value = _problem(data)
    check(same(value, {"errors": {"generalErrors": [DENIED_MESSAGE]}, "message": DESIGN_ENVELOPE, "statusCode": 403}),
          "The denied answer changed from the existing design error body")
    return value


def _no_cause(value, cause):
    text = value.decode("utf-8", "replace") if isinstance(value, (bytes, bytearray)) else value if isinstance(value, str) \
        else json.dumps(value, sort_keys=True)
    check(isinstance(cause, str) and len(cause) >= 16, "Use a distinct private cause canary")
    check(cause not in text and "IOException" not in text, "An observed surface disclosed the owner's private cause")


# Each actual adapter's own frame order up to the refused pre-dispatch refresh, observed natively on 97f75e4f9.
# Copilot admits context once and dispatches the held call directly. Anthropic admits the prompt and its step
# context, then requests the call within a step before dispatch. Both end in the same terminal error.
TURN_FRAMES = {
    "copilot": ["started", "contextAdmitted", "toolCallStarted", "error"],
    "anthropic": ["started", "contextAdmitted", "contextAdmitted", "stepStarted", "toolCallRequested", "stepCompleted",
                  "toolCallStarted", "error"],
}


def require_turn_refusal(frames, rows, *, provider, command_id, expected, cause):
    """The gated read-graph turn stops at its pre-dispatch admission refresh, before any tool dispatch or adapter return."""
    check(provider in PROVIDERS and expected in TURN_REFUSALS, "Unknown adapter or refusal")
    values = [frame for frame in frames if isinstance(frame, dict)]
    check(len(values) == len(frames), "Malformed native stream frame")
    kinds = [frame.get("kind") for frame in values]
    check(kinds == TURN_FRAMES[provider],
          "The read-graph turn did not stop at its pre-dispatch admission refresh after the adapter's own admission frames")
    for admitted in (frame.get("payload") for frame in values if frame.get("kind") == "contextAdmitted"):
        check(isinstance(admitted, dict) and admitted.get("diagnostics") == [] and isinstance(admitted.get("attachments"), list)
              and any(isinstance(item, dict) and item.get("contentType") == "workflow.definition" for item in admitted["attachments"]),
              "The actual owner did not admit the workflow snapshot before the replacement decided")
        # Spec 1193 slice B: the host composes the Activities Design API, so the workflow module's catalog is the real
        # one, read for the admin; an unavailable catalog would also have added a diagnostic above.
        check(any(isinstance(item, dict) and item.get("contentType") == "workflow.catalog" and isinstance(item.get("content"), dict)
                  and item["content"].get("status") == "available" for item in admitted["attachments"]),
              "The admitted context did not carry the host's available activity catalog")
    for frame in values:
        if frame.get("kind") in ("toolCallRequested", "toolCallStarted"):
            payload = frame.get("payload")
            check(isinstance(payload, dict) and payload.get("toolName") == READ_TOOL, "The held call was not the read-graph tool")
    error = values[-1].get("error")
    check(isinstance(error, dict) and {key: error.get(key) for key in ("code", "statusCode", "message")} == TURN_REFUSALS[expected],
          "Wrong refusal at the pre-dispatch admission refresh; expected the " + expected + " owner's code, status and fixed text")
    owned = [row for row in rows if isinstance(row, dict) and (row.get("data") or {}).get("commandId") == command_id]
    calls = [row for row in owned if row.get("kind") == provider + ".call"]
    check(len(calls) == 1 and calls[0]["data"].get("toolName") == READ_TOOL, "The actual adapter did not make exactly one read-graph call")
    check(not any(row.get("kind") in (provider + ".return", provider + ".continuation") for row in owned),
          "A refused admission still returned a tool outcome to the actual adapter")
    _no_cause(values, cause)
    _no_cause(rows, cause)
    return {"code": error["code"], "statusCode": error["statusCode"], "message": error["message"]}


def select_command(transport, command_id, tool_name, arguments):
    """Select the adapter's next command and retain a frozen copy of its exact bytes as evidence.

    The protocol command file is one shared, mutable input: the next turn atomically replaces it. A reference to it
    would no longer match its hash once a later turn ran, so the evidence is this turn's own immutable copy.
    """
    copy_path = Path(transport.output) / "selected-command.json"
    check(not copy_path.exists(), "Never overwrite a selected protocol command")
    selected = transport.set_command(command_id, tool_name, arguments)
    data = Path(transport.command_file).read_bytes()
    copy_path.write_bytes(data)
    copy_path.chmod(0o600)
    check(sha(copy_path) == selected["sha256"] and strict_json(data.decode())["commandId"] == command_id,
          "The retained command differs from the command the adapter will read")
    return retained(copy_path)


def snapshot_attachment(seed, revision):
    """The workflow module's admitted client snapshot of the seeded draft, as an open editor sends it."""
    activity = seed["activityId"]
    return {"id": str(uuid.uuid4()), "moduleId": WORKFLOW_MODULE, "schemaVersion": "1", "source": WORKFLOW_MODULE,
            "sourceId": seed["definitionId"], "label": "Open workflow", "contentType": "workflow.definition",
            "sensitivity": "internal", "scope": "selection", "origin": "clientSnapshot",
            "target": {"moduleId": WORKFLOW_MODULE, "resourceType": "workflow-definition", "resourceId": seed["definitionId"], "revision": revision},
            "client": {"clientId": "qualification-editor", "generationId": str(uuid.uuid4())},
            "content": {"workflowDefinitionId": seed["definitionId"], "draftId": seed["draftId"], "revision": revision,
                        "selectedNodeId": activity, "activities": [{"id": activity, "type": "Elsa.Activities.Primitives.Activities.WriteLine",
                                                                     "displayName": "Write line"}],
                        "connections": [], "positions": [{"nodeId": activity, "x": 120, "y": 160}]}}


@contextmanager
def _bounded(runtime, name):
    check(threading.current_thread() is threading.main_thread() and hasattr(signal, "setitimer"),
          "Native authorization qualification requires an interruptible main-thread stage deadline")
    check(signal.getitimer(signal.ITIMER_REAL) == (0.0, 0.0), "Do not replace another qualification stage's active deadline")
    started = time.monotonic()
    stage = {"name": name, "status": "running", "boundSeconds": CASE_SECONDS}
    runtime.run.report[SECTION]["stages"].append(stage)
    runtime.run.save()
    def expired(_signum, _frame):
        raise TimeoutError("Native authorization stage expired without retry: " + name)
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


def _definition(runtime, name, seed, mode_file, mode):
    atomic_json(mode_file, mode)
    record, data = runtime.native.request(runtime.backend, name, seed["definitionPath"])
    return record, data


def _tool(runtime, name, seed, mode_file, decision, expected, cause):
    """A gated read-graph turn: context is admitted by the actual owner, then the pre-dispatch refresh meets the replacement."""
    atomic_json(mode_file, {"mode": "native"})
    command_id, epoch = str(uuid.uuid4()), str(uuid.uuid4())
    atomic_json(runtime.gate_file, {"commandId": command_id, "epoch": epoch, "state": "hold", "timeoutMilliseconds": 60000})
    transport = runtime.transport(name)
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    session_response = transport.create_session({"conversationId": str(uuid.uuid4()), "mode": "explain",
        "activeSurface": {"route": "/workflows/definitions"}, "autonomyMode": "auto-read-only"}).require_status(200)
    session = session_response.json()["data"]["sessionId"]
    command = select_command(transport, command_id, READ_TOOL, {})
    message = transport.message(session, {"content": "Owned workflow authorization qualification request", "mode": "explain",
        "requestId": str(uuid.uuid4()), "requestedCapabilities": ["workflow.explain"],
        "contextAttachments": [snapshot_attachment(seed, "qualification-unsaved-" + uuid.uuid4().hex[:8])]}).require_status(200)
    try:
        with transport.stream(session) as stream:
            started = stream.until(lambda frame: frame.value["kind"] == "started")
            turn_id = started.value["payload"]["turnId"]
            deadline = time.monotonic() + 30
            while True:
                trace = transport.protocol_trace(offset)
                if any(row["kind"] == "protocol.gate.waiting" and row["data"].get("commandId") == command_id
                       and row["data"].get("epoch") == epoch for row in trace["records"]):
                    break
                check(time.monotonic() < deadline, "The actual adapter's pre-call gate never reported waiting")
                time.sleep(0.02)
            # Context was admitted by the actual owner; the dispatch's admission refresh now meets the replacement.
            atomic_json(mode_file, decision)
            atomic_json(runtime.gate_file, {"commandId": command_id, "epoch": epoch, "state": "release", "timeoutMilliseconds": 60000})
            terminal = stream.until_turn_end(turn_id=turn_id)
    finally:
        atomic_json(mode_file, {"mode": "native"})
        atomic_json(runtime.gate_file, {"commandId": "not-an-active-command", "epoch": str(uuid.uuid4()), "state": "release"})
    check(terminal.value["kind"] == "error", "The gated read turn did not stop with a native refusal")
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the refused call")
    observed = require_turn_refusal([frame.value for frame in stream.frames], trace["records"], provider=runtime.provider,
                                    command_id=command_id, expected=expected, cause=cause)
    _no_cause(bytes(stream.raw), cause)
    return {"commandId": command_id, "sessionId": session, "turnId": turn_id, "terminal": terminal.value["kind"], "outcome": observed,
            "evidence": [session_response.receipt, command, message.receipt, stream.receipt, trace["receipt"]]}


def _case(runtime, provider):
    name = provider + "-workflow-authorization-unavailable"
    evidence = list(runtime.select_provider(provider))
    runtime.sign_in()
    seed = seed_workflow(runtime.backend, runtime.output(name + "-workflow") / "workflow-seed")
    evidence.extend(seed["evidence"])
    mode_file = runtime.controls / (provider + "-workflow-authority-mode.json")
    atomic_json(mode_file, {"mode": "native"})
    cause = "QUALIFICATION_PRIVATE_OWNER_CAUSE_" + uuid.uuid4().hex
    evidence.extend(runtime.apply_features(name + "-select-seam", {SEAM_FEATURE: (True, {"ModeFile": str(mode_file)})}))
    try:
        before, before_data = _definition(runtime, name + "-definition-native-before", seed, mode_file, {"mode": "native"})
        check(before["status"] == 200, "The actual owner did not read the seeded definition through the seam")
        denied, denied_data = _definition(runtime, name + "-definition-denied", seed, mode_file, {"mode": "deny"})
        denied_body = require_denied_problem(denied["status"], denied_data)
        unavailable, unavailable_data = _definition(runtime, name + "-definition-unavailable", seed, mode_file, {"mode": "throw", "cause": cause})
        unavailable_body = require_unavailable_problem(unavailable["status"], unavailable_data, cause)
        tool_unavailable = _tool(runtime, name + "-tool-unavailable", seed, mode_file, {"mode": "throw", "cause": cause}, "unavailable", cause)
        tool_denied = _tool(runtime, name + "-tool-denied", seed, mode_file, {"mode": "deny"}, "denied", cause)
    finally:
        atomic_json(mode_file, {"mode": "native"})
    evidence.extend(runtime.apply_features(name + "-deselect-seam", {SEAM_FEATURE: (False, {"ModeFile": str(mode_file)})}))
    after, after_data = runtime.native.request(runtime.backend, name + "-definition-native-after", seed["definitionPath"])
    check(after["status"] == 200 and same(json.loads(before_data), json.loads(after_data)),
          "Deselecting the replacement did not restore the unchanged native definition read")
    evidence.extend([before["receipt"], denied["receipt"], unavailable["receipt"], after["receipt"],
                     *tool_unavailable.pop("evidence"), *tool_denied.pop("evidence")])
    return {"id": name, "status": "passed", "adapter": provider, "evidenceCategory": EVIDENCE_CATEGORY,
            "definition": {"denied": denied_body, "unavailable": unavailable_body},
            "tool": {"unavailable": tool_unavailable, "denied": tool_denied},
            "ownerBefore": before["body"], "ownerAfter": after["body"], "ownerUnchanged": True, "causeDisclosed": False,
            "evidence": evidence}


def qualify_workflow_authorization(runtime):
    run = runtime.run
    check(SECTION not in run.report, "Never resume or overwrite a prior workflow authorization qualification")
    original_cases = copy.deepcopy(run.report["cases"])
    section = {"status": "running", "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False, "source": run.source,
               "caseIDs": [], "records": [], "stages": [],
               "driver": {"repositoryPath": "tools/studio/Weaver.Conformance.Probe/authorization_journeys.py",
                          "sha256": sha(TOOL / "authorization_journeys.py")}}
    run.report[SECTION] = section
    run.save()
    try:
        for provider in PROVIDERS:
            name = provider + "-workflow-authorization-unavailable"
            section["activeCase"] = name
            with _bounded(runtime, name):
                value = _case(runtime, provider)
                for ref in [value["ownerBefore"], value["ownerAfter"], *value["evidence"]]:
                    path = Path(ref["path"])
                    check(path.resolve().is_relative_to(run.root.resolve()) and path.is_file() and sha(path) == ref["sha256"],
                          "Workflow authorization evidence is missing, changed or outside the owned run")
                record = runtime.save(runtime.output(name + "-record"), "observation", value)
                section["caseIDs"].append(name)
                section["records"].append({"id": name, "status": "passed", "record": record})
        check(set(section["caseIDs"]) == EXPECTED_CASES and len(section["records"]) == len(EXPECTED_CASES),
              "Each workflow authorization case must be observed exactly once")
        check(same(original_cases, run.report["cases"]), "Workflow authorization qualification changed the original M1 case records")
        section.pop("activeCase", None)
        section["status"] = "passed"
    except BaseException as error:
        section.update(status="failed", failure={"type": type(error).__name__, "message": str(error)})
        raise
    finally:
        run.save()


def authorization_manifest_gaps(section, source=None):
    """Structural reasons a section cannot complete a witness; reads no files."""
    if not isinstance(section, dict):
        return ["The mandatory workflow authorization section is absent"]
    gaps = []
    names = section.get("caseIDs") if isinstance(section.get("caseIDs"), list) else []
    records = section.get("records") if isinstance(section.get("records"), list) else []
    if section.get("status") != "passed":
        gaps.append("Workflow authorization section status is " + repr(section.get("status")))
    if section.get("evidenceCategory") != EVIDENCE_CATEGORY or section.get("liveProvider") is not False:
        gaps.append("Unsupported workflow authorization provider evidence claim")
    if source is not None and section.get("source") != source:
        gaps.append("Workflow authorization section source differs from the witness source")
    if (sorted(names) != sorted(EXPECTED_CASES) or len(names) != len(EXPECTED_CASES)
            or [row.get("id") if isinstance(row, dict) else None for row in records] != names
            or not all(isinstance(row, dict) and row.get("status") == "passed" and isinstance(row.get("record"), dict) for row in records)):
        gaps.append("The workflow authorization cases were not each observed passed exactly once")
    return gaps


def require_workflow_authorization(section, owned_root=None, *, source=None):
    """Mandatory final gate: both adapters passed, and each retained record still says so."""
    gaps = authorization_manifest_gaps(section, source)
    check(not gaps, "The workflow authorization inventory is mandatory on each actual adapter: " + "; ".join(gaps))
    if owned_root is not None:
        root = Path(owned_root).resolve()
        for row in section["records"]:
            path = Path(row["record"]["path"])
            check(path.is_absolute() and path.resolve().is_relative_to(root) and path.is_file() and sha(path) == row["record"]["sha256"],
                  "Workflow authorization record is missing, changed or outside the owned run: " + row["id"])
            observed = json.loads(path.read_text())
            check(observed.get("id") == row["id"] and observed.get("status") == "passed"
                  and observed.get("adapter") == row["id"].split("-", 1)[0] and observed.get("causeDisclosed") is False,
                  "Workflow authorization record differs: " + row["id"])
    return section["records"]
