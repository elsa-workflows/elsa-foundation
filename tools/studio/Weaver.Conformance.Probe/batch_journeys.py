"""Mandatory workflow-batch qualification (spec 1193, stories 2 and 3; T022).

On each actual adapter, the reviewed Operation Batch tool (workflow_apply_operation_batch) must:

- prepare one batch of seven operations of six kinds, three of them in a nested flowchart, as a proposal that awaits review even in
  full-auto, labelled destructive because it disconnects, with every target resolved and nothing changed on the server;
- end each negative control at preparation with the workflow module's own clarification or refusal and no proposal:
  an activity the catalog does not offer, an activity the catalog lists but does not let anyone add, an input the
  activity does not declare, a name that matches several activities, a literal on a credential input, and a historical
  working state; and never be offered to an actor without manage permission;
- apply in the actual packaged Studio (browser.mjs, workflow-batch) as one designer-history step, with the native
  validation, dirty state, autosave suspension across Undo and Redo, and no save or publish, while an edit after the
  proposal, a selection change, a remount and an approval from a second tab are each refused with zero change.

The consumer's catalog has one container activity, the Flowchart, so the nested scope is a flowchart inside the root
flowchart. It has no credential input and no excluded activity either, so the host's test-only
QualificationBatchCatalog feature, selected through the ordinary native feature apply for this inventory only, adds one
of each to the designer's own catalog read. The native turns send the open editor's snapshot as Studio does, in its
compact structured form, for the workflow this case seeds. Nothing is retried.
"""
from __future__ import annotations

from contextlib import contextmanager
import copy
import json
from pathlib import Path
import os
import shutil
import signal
import threading
import time
import uuid

from artifacts import TOOL, check, sha
from authorization_journeys import select_command
from journey_runtime import browser_inputs, same
from owner_resources import seed_batch_workflow
from runtime import RUNTIME_ENVIRONMENT, private_json
from truth_seam_journeys import READER, same_target


SECTION = "workflowBatchQualification"
EVIDENCE_CATEGORY = "deterministic-provider-protocol"
CASE_SECONDS = 480
PROVIDERS = ("copilot", "anthropic")
EXPECTED_CASES = frozenset(provider + "-workflow-batch" for provider in PROVIDERS)
SEAM_FEATURE = "QualificationBatchCatalog"
BATCH_TOOL = "workflow_apply_operation_batch"
WORKFLOW_MODULE = "Elsa.Studio.Workflows"
RESULT_TYPE = "workflows.batch-result"
CREDENTIAL = {"activityVersionId": "qualification-credential-activity-v1", "activityTypeKey": "Qualification.CredentialActivity", "input": "apiKey"}
EXCLUDED = {"activityTypeKey": "Qualification.ExcludedActivity", "version": "1.0.0", "reason": "Excluded by the qualification availability policy."}
ADDED = "batch-greet"
LITERAL = "Hello from the reviewed batch"
DISPLAY_NAME = "Greet first"
POSITION = {"x": 520, "y": 420}
NOTHING_CHANGED = "The whole batch was refused and nothing changed."

# Each negative control: who asks, how the snapshot differs, the operations, and the module's own refusal text.
CONTROLS = ("missing-activity", "excluded-activity", "undeclared-property", "ambiguous-target", "credential-literal", "historical-version")
EXPECTED_REFUSALS = {
    "missing-activity": ("Clarification needed for operation 0 (add):", "'Qualification.NotInstalled' version '1.0.0' is not in the activity design catalog"),
    "excluded-activity": ("Clarification needed for operation 0 (add):", "is installed but cannot be added here: " + EXCLUDED["reason"]),
    "undeclared-property": ("Clarification needed for operation 0 (set-property):", "declares no input 'notDeclared'"),
    "ambiguous-target": ("Clarification needed for operation 0 (remove):", "matches 3 activities"),
    "credential-literal": ("Operation 0 (set-property):", "is a credential input, so a literal value is refused. Offer to bind a Secret Reference"),
    "historical-version": ("The open workflow is a published or historical version", "Offer to work on the current draft instead"),
}
BROWSER_JOURNEY = "workflow-batch"
BROWSER_COMMANDS = ("staleEdit", "selectionChange", "remount", "applied", "secondTab")
BROWSER_OBSERVATIONS = {"staleEdit": "refused", "selectionChange": "refused", "remount": "refused", "secondTab": "refused", "applied": True,
                        "operationCount": 7, "nestedScope": True, "handEditInBase": True, "validated": True, "dirty": True,
                        "autosaveSuspended": True, "undoRestored": True, "redoRestored": True, "noSaveOrPublish": True}


def positive_operations(seed):
    """Six kinds over the seeded draft: an add into the nested flowchart wired after its existing activity, its declared
    input, a display name, a move, and a rewiring that removes one connection (destructive) and adds another, so every
    node stays connected and reachable and the designer's structural validation stays clean."""
    n, write, flow = seed["nodes"], seed["writeLine"], seed["flowchart"]
    return [
        {"kind": "add", "ref": ADDED, "activityTypeKey": write["activityTypeKey"], "version": write["version"],
         "scopePath": [n["root"], n["nested"]], "slot": flow["slot"], "position": {"x": 200, "y": 240}},
        {"kind": "set-property", "activity": {"ref": ADDED}, "property": "text", "value": {"literal": LITERAL}},
        {"kind": "connect", "source": {"id": n["inner"]}, "target": {"ref": ADDED}},
        {"kind": "update", "activity": {"id": n["a"]}, "fields": {"displayName": DISPLAY_NAME}},
        {"kind": "position", "activity": {"id": n["b"]}, "x": POSITION["x"], "y": POSITION["y"]},
        {"kind": "disconnect", "source": {"id": n["a"]}, "target": {"id": n["nested"]}},
        {"kind": "connect", "source": {"id": n["b"]}, "target": {"id": n["nested"]}},
    ]


POSITIVE_RISKS = ["reviewRequired", "reviewRequired", "reviewRequired", "reviewRequired", "reviewRequired", "destructive", "reviewRequired"]


def control_operations(seed, control, canary):
    n, write, flow = seed["nodes"], seed["writeLine"], seed["flowchart"]
    return {
        "missing-activity": [{"kind": "add", "ref": "batch-missing", "activityTypeKey": "Qualification.NotInstalled", "version": "1.0.0",
                              "scopePath": [n["root"], n["nested"]], "slot": flow["slot"]}],
        "excluded-activity": [{"kind": "add", "ref": "batch-excluded", "activityTypeKey": EXCLUDED["activityTypeKey"], "version": EXCLUDED["version"],
                               "scopePath": [n["root"], n["nested"]], "slot": flow["slot"]}],
        "undeclared-property": [{"kind": "set-property", "activity": {"id": n["a"]}, "property": "notDeclared", "value": {"literal": "x"}}],
        "ambiguous-target": [{"kind": "remove", "activity": {"name": write["displayName"]}}],
        "credential-literal": [{"kind": "set-property", "activity": {"id": "batch-credential"}, "property": CREDENTIAL["input"], "value": {"literal": canary}}],
        "historical-version": [{"kind": "position", "activity": {"id": n["b"]}, "x": 10, "y": 10}],
    }[control]


def batch_snapshot(seed, revision, *, working_state="draft", credential_node=False):
    """The workflow module's client snapshot of the seeded draft, as the open editor sends it: identity, revision, binding,
    the activities, and the compact structure (versions and containers by activity index, as compactWorkflowStructure)."""
    n, write, flow = seed["nodes"], seed["writeLine"], seed["flowchart"]
    activities = [{"id": n["root"], "type": flow["activityTypeKey"], "displayName": flow["displayName"]},
                  {"id": n["a"], "type": write["activityTypeKey"], "displayName": write["displayName"]},
                  {"id": n["b"], "type": write["activityTypeKey"], "displayName": write["displayName"]},
                  {"id": n["nested"], "type": flow["activityTypeKey"], "displayName": flow["displayName"]},
                  {"id": n["inner"], "type": write["activityTypeKey"], "displayName": write["displayName"]}]
    versions = {}
    for index, version in enumerate([flow["activityVersionId"], write["activityVersionId"], write["activityVersionId"], flow["activityVersionId"], write["activityVersionId"]]):
        versions.setdefault(version, []).append(index)
    root_members = [1, 2, 3]
    if credential_node:
        # An unsaved hand-added node of the credential activity, as the working draft holds it before any save.
        activities.append({"id": "batch-credential", "type": CREDENTIAL["activityTypeKey"], "displayName": "Qualification credential activity"})
        versions[CREDENTIAL["activityVersionId"]] = [5]
        root_members.append(5)
    slot = lambda members: [{"id": flow["slot"], "mode": "flowchart", "cardinality": "many", "nodes": members}]
    structure = {"versions": [{"id": key, "nodes": value} for key, value in versions.items()], "containers": [
        {"id": n["root"], "parentId": None, "slot": None, "slots": slot(root_members)},
        {"id": n["nested"], "parentId": n["root"], "slot": flow["slot"], "slots": slot([4])}]}
    target = {"moduleId": WORKFLOW_MODULE, "resourceType": "workflow-definition", "resourceId": seed["definitionId"], "revision": revision}
    return {"id": str(uuid.uuid4()), "moduleId": WORKFLOW_MODULE, "schemaVersion": "1", "source": WORKFLOW_MODULE, "sourceId": seed["definitionId"],
            "label": "Open workflow", "contentType": "workflow.definition", "sensitivity": "internal", "scope": "selection", "origin": "clientSnapshot",
            "target": target, "client": {"clientId": "qualification-editor", "generationId": str(uuid.uuid4())},
            "content": {"workflowDefinitionId": seed["definitionId"], "draftId": seed["draftId"], "revision": revision, "selectedNodeId": n["a"],
                        "workingState": working_state, "activities": activities, "structure": structure,
                        "connections": [{"source": n["a"], "target": n["b"], "sourcePort": "Done"}, {"source": n["a"], "target": n["nested"], "sourcePort": "Done"}],
                        "positions": [{"nodeId": n["a"], "x": 120, "y": 160}, {"nodeId": n["b"], "x": 420, "y": 160}, {"nodeId": n["nested"], "x": 120, "y": 420},
                                      {"nodeId": n["inner"], "x": 80, "y": 80}],
                        "secretReferences": []}}


# Each actual adapter's own frame order, pinned. Observed natively on the slice B witness (b0d45f106): a reviewed proposal
# (lifecycle denied-result-continuation, both adapters), a tool refused at preparation (malformed-arguments, Copilot), and
# a turn whose commanded tool the adapter was not offered (truth-seam read-only layout request, both adapters). Copilot's
# SDK runs the supplied function and reports the refusal through its own events. Anthropic leaves the call to the engine
# (AgentTurnServices): a refused dispatch carries no result, so no context is re-admitted before toolCallCompleted (a
# successful read re-admits there, as observed), and the continuation step admits its context once, as observed.
PROPOSAL_FRAMES = {
    "copilot": ["started", "contextAdmitted", "toolCallStarted", "proposalCreated", "toolApprovalRequested"],
    "anthropic": ["started", "contextAdmitted", "contextAdmitted", "stepStarted", "toolCallRequested", "stepCompleted", "toolCallStarted",
                  "proposalCreated", "toolApprovalRequested"],
}
REFUSED_FRAMES = {
    "copilot": ["started", "contextAdmitted", "toolCallStarted", "toolCallCompleted", "contextAdmitted", "contributedResult", "messageDelta", "completed"],
    "anthropic": ["started", "contextAdmitted", "contextAdmitted", "stepStarted", "toolCallRequested", "stepCompleted", "toolCallStarted",
                  "toolCallCompleted", "contributedResult", "contextAdmitted", "stepStarted", "messageDelta", "stepCompleted", "completed"],
}
# The continuation after the reviewed proposal is denied (lifecycle denied-result-continuation, both adapters).
DENIED_CONTINUATION_FRAMES = {
    "copilot": ["started", "contextAdmitted", "messageDelta", "completed"],
    "anthropic": ["started", "contextAdmitted", "contextAdmitted", "stepStarted", "messageDelta", "stepCompleted", "completed"],
}
UNOFFERED_FRAMES = {
    "copilot": ["started", "contextAdmitted", "error"],
    "anthropic": ["started", "contextAdmitted", "contextAdmitted", "stepStarted", "error"],
}
UNOFFERED_ERROR = {"copilot": "agent.provider.github_copilot.turn_failed", "anthropic": "agent.provider.anthropic.turn_failed"}
UNOFFERED_MESSAGE = "The commanded tool is not uniquely advertised by the actual adapter."
# How each adapter hands a refused tool outcome back: Copilot's function returns, Anthropic continues with the results.
OUTCOME_ROWS = {"copilot": ".return", "anthropic": ".continuation"}


def _kinds(values, expected, what):
    kinds = [frame.get("kind") for frame in values]
    check(kinds == expected, what + " did not follow this adapter's frame order: " + json.dumps(kinds))
    return kinds


def _frames(frames):
    values = [frame for frame in frames if isinstance(frame, dict)]
    check(len(values) == len(frames) and values, "Malformed native stream frame")
    return values


def _owned(rows, provider, command_id):
    return [row for row in rows if isinstance(row, dict) and (row.get("data") or {}).get("commandId") == command_id
            and str(row.get("kind", "")).startswith(provider + ".")]


def _offered(owned, provider):
    offered = [row["data"] for row in owned if row.get("kind") == provider + ".request" and row["data"].get("tools")]
    check(bool(offered), "The actual adapter recorded no tool advertisement")
    return sorted({tool.get("name") for tool in offered[0]["tools"] if isinstance(tool, dict)})


def _admitted_draft(values, target):
    admitted = [frame for frame in values if frame.get("kind") == "contextAdmitted"]
    check(bool(admitted), "The turn admitted no context")
    for frame in admitted:
        payload = frame.get("payload")
        check(isinstance(payload, dict) and payload.get("diagnostics") == [], "Context admission reported a diagnostic")
        drafts = [item for item in payload.get("attachments", []) if isinstance(item, dict) and item.get("contentType") == "workflow.definition"]
        check(len(drafts) == 1 and same_target(drafts[0].get("target"), target)
              and isinstance((drafts[0].get("content") or {}).get("structure"), dict),
              "The admission lost the exact working-state target or its structure")
    return admitted


def require_proposal_turn(frames, rows, *, provider, command_id, target, operations):
    """The positive turn: one reviewed proposal, destructive, for the exact target, awaiting approval even in full-auto."""
    check(provider in PROVIDERS, "Unknown adapter")
    values = _frames(frames)
    _admitted_draft(values, target)
    _kinds(values, PROPOSAL_FRAMES[provider], "The batch turn")
    proposal = values[-2].get("payload")
    prepared = proposal.get("preparedInvocation") if isinstance(proposal, dict) else None
    check(isinstance(prepared, dict) and prepared.get("toolName") == BATCH_TOOL and prepared.get("requiresApproval") is True
          and proposal.get("requiresApproval") is True and str(proposal.get("status", "")).replace("-", "").lower() == "awaitingapproval"
          and prepared.get("executionLocation") == "clientWorkingState" and prepared.get("risk") == "destructive"
          and same_target(prepared.get("target"), target) and values[-1].get("proposalId") == proposal.get("id"),
          "The batch proposal is not a reviewed, destructive client working-state change of the exact target")
    frozen = (prepared.get("arguments") or {}).get("operations")
    labels = ((prepared.get("preview") or {}).get("data") or {}).get("operations")
    check(prepared.get("preview", {}).get("typeId") == RESULT_TYPE and isinstance(frozen, list) and len(frozen) == len(operations)
          and [item.get("kind") for item in frozen] == [item["kind"] for item in operations]
          and isinstance(labels, list) and [item.get("risk") for item in labels] == POSITIVE_RISKS
          and [item.get("risk") for item in frozen] == POSITIVE_RISKS, "The frozen batch or its per-operation risk labels differ")
    owned = _owned(rows, provider, command_id)
    calls = [row for row in owned if row.get("kind") == provider + ".call"]
    check(BATCH_TOOL in _offered(owned, provider) and len(calls) == 1 and calls[0]["data"].get("toolName") == BATCH_TOOL,
          "The actual adapter was not offered the batch tool or did not call it exactly once")
    return {"proposalId": proposal.get("id"), "risk": prepared["risk"], "operations": len(frozen), "labels": POSITIVE_RISKS}


def require_refused_turn(frames, rows, *, provider, command_id, target, control, canary=None):
    """A negative control: the batch tool ran its preparation and refused, with the module's own text; no review followed."""
    check(provider in PROVIDERS and control in EXPECTED_REFUSALS, "Unknown adapter or control")
    values = _frames(frames)
    _admitted_draft(values, target)
    _kinds(values, REFUSED_FRAMES[provider], "The refused batch turn")
    completed = next(frame.get("payload") for frame in values if frame.get("kind") == "toolCallCompleted")
    result = completed.get("result") if isinstance(completed, dict) else None
    check(isinstance(completed, dict) and completed.get("toolName") == BATCH_TOOL and completed.get("succeeded") is False
          and isinstance(result, dict) and result.get("succeeded") is False and result.get("payload") is None and result.get("receipt") is None
          and completed.get("summary") == result.get("content"), "The batch tool did not complete once, refused, with no payload or receipt")
    content = completed.get("summary")
    start, detail = EXPECTED_REFUSALS[control]
    check(isinstance(content, str) and content.startswith(start) and detail in content
          and (control == "historical-version" or content.endswith(NOTHING_CHANGED)),
          "The " + control + " control was refused for another reason: " + str(content)[:300])
    owned = _owned(rows, provider, command_id)
    calls = [row for row in owned if row.get("kind") == provider + ".call"]
    check(BATCH_TOOL in _offered(owned, provider) and len(calls) == 1 and calls[0]["data"].get("toolName") == BATCH_TOOL
          and any(row.get("kind") == provider + OUTCOME_ROWS[provider] for row in owned),
          "The actual adapter did not call the batch tool once and receive its refusal")
    if canary is not None:
        # The model wrote the literal itself, so only its own call carries it: no admitted context, result or refusal does.
        disclosed = [frame for frame in values if frame.get("kind") in ("contextAdmitted", "toolCallCompleted", "contributedResult", "messageDelta", "completed")]
        check(canary not in json.dumps(disclosed), "A credential literal reached admitted context, a result or the refusal")
    return {"control": control, "refusal": content[:400]}


def require_unoffered_turn(frames, rows, *, provider, command_id, target):
    """An actor without manage permission: the permissions say so, the adapter is never offered the tool, nothing is reviewed."""
    check(provider in PROVIDERS, "Unknown adapter")
    values = _frames(frames)
    for frame in _admitted_draft(values, target):
        permissions = [item for item in frame["payload"]["attachments"] if isinstance(item, dict) and item.get("contentType") == "workflow.permissions"]
        check(len(permissions) == 1 and (permissions[0].get("content") or {}).get("manage") == "denied"
              and BATCH_TOOL not in (permissions[0].get("content") or {}).get("tools", [BATCH_TOOL]),
              "The reader's permissions do not withhold the batch tool")
    kinds = _kinds(values, UNOFFERED_FRAMES[provider], "The no-manage turn")
    error = values[-1].get("error")
    check(isinstance(error, dict) and error.get("code") == UNOFFERED_ERROR[provider] and error.get("message") == UNOFFERED_MESSAGE,
          "The no-manage turn did not end because the actual adapter was never offered the batch tool")
    owned = _owned(rows, provider, command_id)
    check(BATCH_TOOL not in _offered(owned, provider) and not any(row.get("kind") == provider + ".call" for row in owned),
          "The actual adapter was offered or called the batch tool for an actor without manage permission")
    return {"control": "no-manage", "offered": False, "terminal": kinds[-1]}


def require_batch_browser(observed, commands, rows, *, provider):
    """The editor's own journey, checked against the adapter trace for every command the browser retained."""
    check(provider in PROVIDERS, "Unknown adapter")
    check(isinstance(observed, dict) and observed.get("journey") == BROWSER_JOURNEY and observed.get("status") == "passed"
          and not observed.get("failure") and not observed.get("pageErrors") and observed.get("provider") == provider,
          "The batch Studio journey did not pass on this adapter")
    check(same(observed.get("observations"), BROWSER_OBSERVATIONS), "The batch Studio journey observed another outcome")
    check(isinstance(observed.get("ownerBefore"), dict) and isinstance(observed.get("ownerAfter"), dict)
          and isinstance(observed.get("evidence"), list) and observed["evidence"], "The batch Studio journey retained no owner evidence")
    selected = observed.get("commands")
    check(isinstance(selected, dict) and sorted(selected) == sorted(BROWSER_COMMANDS) and len(set(selected.values())) == len(BROWSER_COMMANDS),
          "The batch journey must retain one distinct command per step")
    by_id = {}
    for item in commands:
        check(isinstance(item, dict) and isinstance(item.get("commandId"), str) and item.get("toolName") == BATCH_TOOL
              and item["commandId"] not in by_id, "Each retained browser command must be one distinct batch request")
        # The strict schema admits only the operations; anything else is refused at preparation, never reviewed.
        arguments = item.get("arguments")
        check(isinstance(arguments, dict) and sorted(arguments) == ["operations"] and isinstance(arguments["operations"], list)
              and arguments["operations"], "A retained browser command carried arguments other than its operations")
        by_id[item["commandId"]] = item
    check(set(by_id) == set(selected.values()), "The retained commands differ from the journey's steps")
    for step, command_id in selected.items():
        owned = _owned(rows, provider, command_id)
        calls = [row for row in owned if row.get("kind") == provider + ".call"]
        check(BATCH_TOOL in _offered(owned, provider) and len(calls) == 1 and calls[0]["data"].get("toolName") == BATCH_TOOL,
              "The actual adapter did not call the batch tool exactly once for the " + step + " step")
    return {"journey": BROWSER_JOURNEY, "observations": observed["observations"], "commands": selected,
            "ownerBefore": observed["ownerBefore"], "ownerAfter": observed["ownerAfter"]}


@contextmanager
def _bounded(runtime, name):
    check(threading.current_thread() is threading.main_thread() and hasattr(signal, "setitimer"),
          "Native batch qualification requires an interruptible main-thread stage deadline")
    check(signal.getitimer(signal.ITIMER_REAL) == (0.0, 0.0), "Do not replace another qualification stage's active deadline")
    started = time.monotonic()
    stage = {"name": name, "status": "running", "boundSeconds": CASE_SECONDS}
    runtime.run.report[SECTION]["stages"].append(stage)
    runtime.run.save()
    def expired(_signum, _frame):
        raise TimeoutError("Native batch stage expired without retry: " + name)
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


def _turn(runtime, name, http, attachment, operations):
    """One native turn whose adapter calls the batch tool with exactly these operations; a fresh full-auto session."""
    transport = runtime.transport(name, http=http)
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    created = transport.create_session({"conversationId": str(uuid.uuid4()), "mode": "build",
                                        "activeSurface": {"route": "/workflows/definitions"}, "autonomyMode": "full-auto"}).require_status(200)
    session = created.json()["data"]["sessionId"]
    command_id = str(uuid.uuid4())
    command = select_command(transport, command_id, BATCH_TOOL, {"operations": operations})
    message = transport.message(session, {"content": "Owned workflow batch qualification request", "mode": "build", "requestId": str(uuid.uuid4()),
        "requestedCapabilities": ["workflow.propose-change"], "contextAttachments": [attachment]}).require_status(200)
    accepted = message.json()["data"]["message"]
    check(any(isinstance(item, dict) and item.get("contentType") == "workflow.definition" and same_target(item.get("target"), attachment["target"])
              for item in accepted.get("context") or []), "The accepted message does not carry the admitted working-state context")
    stream = transport.collect_turn(session)
    return transport, session, command_id, [frame.value for frame in stream.frames], offset, [created.receipt, command, message.receipt, stream.receipt]


def _native(runtime, name, seed, clients):
    """The positive turn and every negative control, each its own turn and session, with the owner's draft unchanged."""
    before, before_data = runtime.native.request(runtime.backend, name + "-owner-before", seed["definitionPath"])
    records, evidence = {}, [before["receipt"]]
    attachment = batch_snapshot(seed, "qualification-unsaved-" + uuid.uuid4().hex[:8])
    transport, session, command_id, frames, offset, refs = _turn(runtime, name + "-positive", runtime.backend, attachment, positive_operations(seed))
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the batch proposal")
    records["positive"] = require_proposal_turn(frames, trace["records"], provider=runtime.provider, command_id=command_id,
                                                target=attachment["target"], operations=positive_operations(seed))
    denied = transport.deny(records["positive"]["proposalId"]).require_status(200)
    continuation = transport.collect_turn(session)
    _kinds([frame.value for frame in continuation.frames], DENIED_CONTINUATION_FRAMES[runtime.provider], "The denied batch's continuation")
    evidence += [*refs, trace["receipt"], denied.receipt, continuation.receipt]
    for control in CONTROLS:
        canary = "CANARY-CREDENTIAL-" + uuid.uuid4().hex if control == "credential-literal" else None
        attachment = batch_snapshot(seed, "qualification-unsaved-" + uuid.uuid4().hex[:8],
                                    working_state="historical" if control == "historical-version" else "draft",
                                    credential_node=control == "credential-literal")
        transport, _, command_id, frames, offset, refs = _turn(runtime, name + "-" + control, runtime.backend, attachment,
                                                               control_operations(seed, control, canary))
        trace = transport.protocol_trace(offset)
        check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the " + control + " control")
        records[control] = require_refused_turn(frames, trace["records"], provider=runtime.provider, command_id=command_id,
                                                target=attachment["target"], control=control, canary=canary)
        evidence += [*refs, trace["receipt"]]
    attachment = batch_snapshot(seed, "qualification-unsaved-" + uuid.uuid4().hex[:8])
    transport, _, command_id, frames, offset, refs = _turn(runtime, name + "-no-manage", clients[READER], attachment, positive_operations(seed))
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the no-manage control")
    records["no-manage"] = require_unoffered_turn(frames, trace["records"], provider=runtime.provider, command_id=command_id, target=attachment["target"])
    evidence += [*refs, trace["receipt"]]
    after, after_data = runtime.native.request(runtime.backend, name + "-owner-after", seed["definitionPath"])
    check(before["status"] == after["status"] == 200 and json.loads(after_data)["draft"] == json.loads(before_data)["draft"],
          "A native batch turn changed the owning draft")
    evidence.append(after["receipt"])
    return records, evidence


def _browser(runtime, name, seed):
    """Runs the actual browser journey as the workflow's manager (the admin) with a fresh normal-login state of its own."""
    _, identity = runtime.sign_in()
    root = runtime.output(name)
    output = root / "observation"
    n, write = seed["nodes"], seed["writeLine"]
    workflow = {key: seed[key] for key in ("definitionId", "draftId", "definitionPath", "draftPath", "validationPath")}
    # The shared browser-input summary names one primary activity; for this journey it is the seed's first root node.
    workflow["activityId"] = n["a"]
    workflow.update(nodes=n, batch={"operations": positive_operations(seed), "addedNodeId": ADDED, "writeLineVersionId": write["activityVersionId"],
                                    "literal": LITERAL, "displayName": DISPLAY_NAME, "position": POSITION, "slot": seed["flowchart"]["slot"]},
                    followUp={"operations": [{"kind": "position", "activity": {"id": n["a"]}, "x": 160, "y": 200}]})
    config = {"journey": BROWSER_JOURNEY, "provider": runtime.provider, "studioOrigin": runtime.studio_origin,
              "backendOrigin": runtime.backend_origin, "storageState": identity["storageState"],
              "commandFile": str(runtime.command_file), "output": str(output), "workflow": workflow}
    private_json(root / "config-private.json", config)
    transport = runtime.transport(name + "-protocol")
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    node = shutil.which("node")
    check(node, "The actual browser driver needs Node")
    runtime.run.run(name, [node, str(TOOL / "browser.mjs"), str(root / "config-private.json")], TOOL,
                    env={key: value for key, value in os.environ.items() if key in RUNTIME_ENVIRONMENT})
    observation = output / "observation.json"
    observed = json.loads(observation.read_text())
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the batch journey")
    # The browser retains a frozen copy of each command it selected; the shared command file is never evidence.
    commands = [json.loads(path.read_text()) for path in sorted(output.glob("command-*.json"))]
    value = require_batch_browser(observed, commands, trace["records"], provider=runtime.provider)
    evidence = [*observed["evidence"], observed["ownerBefore"], observed["ownerAfter"], {"path": str(observation), "sha256": sha(observation)},
                runtime.save(root, "browser-inputs", browser_inputs(config)), trace["receipt"]]
    return value, evidence


def _case(runtime, provider):
    name = provider + "-workflow-batch"
    evidence = list(runtime.select_provider(provider))
    _, admin = runtime.sign_in()
    clients = {}
    clients[READER], reader = runtime.sign_in(READER)
    check(reader["tenantId"] == admin["tenantId"], "The no-manage reader must share the seeded workflow's tenant")
    seed = seed_batch_workflow(runtime.backend, runtime.output(name + "-workflow") / "workflow-seed")
    evidence.extend(seed["evidence"])
    evidence.extend(runtime.apply_features(name + "-select-seam", {SEAM_FEATURE: (True, {})}))
    try:
        native, native_evidence = _native(runtime, name, seed, clients)
        evidence.extend(native_evidence)
        browser, browser_evidence = _browser(runtime, name + "-browser", seed)
        evidence.extend(browser_evidence)
    finally:
        evidence.extend(runtime.apply_features(name + "-deselect-seam", {SEAM_FEATURE: (False, {})}))
    return {"id": name, "status": "passed", "adapter": provider, "evidenceCategory": EVIDENCE_CATEGORY, "native": native,
            "browser": browser, "evidence": evidence}


def qualify_workflow_batch(runtime):
    run = runtime.run
    check(SECTION not in run.report, "Never resume or overwrite a prior workflow batch qualification")
    original_cases = copy.deepcopy(run.report["cases"])
    section = {"status": "running", "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False, "source": run.source,
               "caseIDs": [], "records": [], "stages": [],
               "driver": {"repositoryPath": "tools/studio/Weaver.Conformance.Probe/batch_journeys.py", "sha256": sha(TOOL / "batch_journeys.py")}}
    run.report[SECTION] = section
    run.save()
    try:
        for provider in PROVIDERS:
            name = provider + "-workflow-batch"
            section["activeCase"] = name
            with _bounded(runtime, name):
                value = _case(runtime, provider)
                for ref in value["evidence"]:
                    path = Path(ref["path"])
                    check(path.resolve().is_relative_to(run.root.resolve()) and path.is_file() and sha(path) == ref["sha256"],
                          "Workflow batch evidence is missing, changed or outside the owned run")
                record = runtime.save(runtime.output(name + "-record"), "observation", value)
                section["caseIDs"].append(name)
                section["records"].append({"id": name, "status": "passed", "record": record})
        check(set(section["caseIDs"]) == EXPECTED_CASES and len(section["records"]) == len(EXPECTED_CASES),
              "Each workflow batch case must be observed exactly once")
        check(same(original_cases, run.report["cases"]), "Workflow batch qualification changed the original M1 case records")
        section.pop("activeCase", None)
        section["status"] = "passed"
    except BaseException as error:
        section.update(status="failed", failure={"type": type(error).__name__, "message": str(error)})
        raise
    finally:
        run.save()


def batch_manifest_gaps(section, source=None):
    """Structural reasons a section cannot complete a witness; reads no files."""
    if not isinstance(section, dict):
        return ["The mandatory workflow batch section is absent"]
    gaps = []
    names = section.get("caseIDs") if isinstance(section.get("caseIDs"), list) else []
    records = section.get("records") if isinstance(section.get("records"), list) else []
    if section.get("status") != "passed":
        gaps.append("Workflow batch section status is " + repr(section.get("status")))
    if section.get("evidenceCategory") != EVIDENCE_CATEGORY or section.get("liveProvider") is not False:
        gaps.append("Unsupported workflow batch provider evidence claim")
    if source is not None and section.get("source") != source:
        gaps.append("Workflow batch section source differs from the witness source")
    if (sorted(names) != sorted(EXPECTED_CASES) or len(names) != len(EXPECTED_CASES)
            or [row.get("id") if isinstance(row, dict) else None for row in records] != names
            or not all(isinstance(row, dict) and row.get("status") == "passed" and isinstance(row.get("record"), dict) for row in records)):
        gaps.append("The workflow batch cases were not each observed passed exactly once")
    return gaps


NATIVE_RECORDS = ("positive", *CONTROLS, "no-manage")


def require_workflow_batch(section, owned_root=None, *, source=None):
    """Mandatory final gate: both adapters passed, and each retained record still holds every native control and the journey."""
    gaps = batch_manifest_gaps(section, source)
    check(not gaps, "The workflow batch inventory is mandatory on each actual adapter: " + "; ".join(gaps))
    if owned_root is not None:
        root = Path(owned_root).resolve()
        for row in section["records"]:
            path = Path(row["record"]["path"])
            check(path.is_absolute() and path.resolve().is_relative_to(root) and path.is_file() and sha(path) == row["record"]["sha256"],
                  "Workflow batch record is missing, changed or outside the owned run: " + row["id"])
            observed = json.loads(path.read_text())
            native = observed.get("native") if isinstance(observed.get("native"), dict) else {}
            check(observed.get("id") == row["id"] and observed.get("status") == "passed" and observed.get("adapter") == row["id"].split("-", 1)[0]
                  and sorted(native) == sorted(NATIVE_RECORDS) and (native.get("positive") or {}).get("risk") == "destructive"
                  and (native.get("no-manage") or {}).get("offered") is False
                  and all((native.get(control) or {}).get("control") == control for control in CONTROLS)
                  and same((observed.get("browser") or {}).get("observations"), BROWSER_OBSERVATIONS),
                  "Workflow batch record differs: " + row["id"])
    return section["records"]


def synthetic_native():
    """The minimal native record shape the final gate checks; for synthetic self-tests only, never native evidence."""
    return {"positive": {"risk": "destructive"}, **{control: {"control": control} for control in CONTROLS}, "no-manage": {"offered": False}}


def synthetic_browser():
    """The minimal browser shape the final gate checks; for synthetic self-tests only."""
    return {"journey": BROWSER_JOURNEY, "observations": copy.deepcopy(BROWSER_OBSERVATIONS)}
