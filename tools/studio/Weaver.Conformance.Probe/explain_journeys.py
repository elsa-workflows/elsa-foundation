"""Mandatory workflow-explain qualification (spec 1193, story 1; T028).

On each actual adapter, explaining and troubleshooting the active draft must read exactly the open editor's working state:

- four explanation turns (the workflow with a credential activity selected, the validation error, the selected JavaScript
  expression, and a Secret Reference binding) each read the draft through workflow_read_graph, and every admission and
  the tool's own result carry the working state as the editor disclosed it: its activities and nested structure, the
  designer's diagnostics, the visible Secret References and the selected activity's declared properties. Each activity is
  addressed by its scope path; a non-literal expression's interpretation is the server's own "unavailable", because no
  Expression Module registers Weaver tools; a credential literal is only present and withheld;
- a snapshot that discloses a credential input's value (as a non-credential literal) is refused at admission with the
  module's own reason, before any provider call;
- the actual packaged Studio (browser.mjs, workflow-explain) discloses the selected input's expression, the designer's own
  diagnostic for it and an unsaved hand edit, attributed to the working-state revision it read; then the reader types a
  credential canary into a password-hinted input through the editor's own masked field, as an unsaved edit, and the
  next read discloses that input only as withheld; nothing is saved;
- that canary, which exists only in Studio's working state, appears in no disclosed payload, stream frame, adapter trace,
  captured browser request or retained evidence of the case, so a regression in Studio's withholding fails the case.

The seeded draft is a root flowchart with a nested flowchart (the consumer's only container activity, so its nested and
parallel structure is flowchart-in-flowchart); b's text is a Variable expression naming a variable the workflow does not
declare, so the owner's own draft validation reports it by activity and input; it also holds one activity of the test-only
QualificationBatchCatalog seam's password-hinted activity, with no value (the consumer's catalog has no masked input of
its own, and Studio offers a credential input only the secret picker, so a literal can be typed only into such an input).
The native unsaved working state adds a JavaScript expression on the nested activity and two activities of the seam's
credential activity: one holding a credential literal, one bound to a visible Secret Reference. The packaged consumer ships no JavaScript Expression Module and no graph activity definition, so JavaScript is
disclosed by type and content only, and no graph activity reference is disclosed (graph activities are covered by the
server and Studio unit tests). The native turns send the open editor's snapshot as Studio's bridge builds it from that
working state (workflowExplainContext.ts). Nothing is retried.
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
from batch_journeys import CREDENTIAL, SEAM_FEATURE
from journey_runtime import browser_inputs, same
from owner_resources import EXPLAIN_MISSING_VARIABLE, PASSWORD_ACTIVITY, UNRESOLVED_VARIABLE, capability_links, owner_path, seed_batch_workflow
from runtime import RUNTIME_ENVIRONMENT, private_json
from truth_seam_journeys import same_target


SECTION = "workflowExplainQualification"
EVIDENCE_CATEGORY = "deterministic-provider-protocol"
CASE_SECONDS = 480
PROVIDERS = ("copilot", "anthropic")
EXPECTED_CASES = frozenset(provider + "-workflow-explain" for provider in PROVIDERS)
READ_TOOL = "workflow_read_graph"
WORKFLOW_MODULE = "Elsa.Studio.Workflows"
RESULT_TYPE = "workflows.graph"
INTERPRETATION = "unavailable"
JAVASCRIPT = "return `Hello ${getVariable('customer')}`;"
SECRET_REFERENCE = {"name": "qualification-crm-api-key", "typeName": "Text", "scope": "tenant"}
LITERAL_NODE, SECRET_NODE = "explain-credential-literal", "explain-credential-secret"
# Studio's bounds (workflowExplainContext.ts), which the server admits strictly.
MAX_DIAGNOSTICS, MAX_MESSAGE, MAX_CONTENT, CONTENT_BUDGET = 64, 1024, 2048, 16384
# Each turn and the node the editor has selected for it.
TURNS = {"explain": LITERAL_NODE, "validation": "b", "expression": "inner", "secret-reference": SECRET_NODE}
CONTROL = "credential-disclosed"
CONTROL_ERROR = {"code": "weaver.context.incomplete", "statusCode": 400,
                 "message": "The selected activity's property context does not match the activity's declared inputs, so the snapshot was not admitted."}

# Each actual adapter's own frame order for a read tool that completes. The engine's own loop (WeaverTurnServices) emits it:
# Copilot's SDK runs the function, and the engine admits context again before the contributed result; Anthropic leaves the
# call to the engine, which admits context again before toolCallCompleted (a dispatch with a result, as the batch case
# observed natively for its successful reads) and once more for the continuation step. The refused-preparation order of the
# batch case (b0d45f106) is the same sequence; the native witness pins this one for the read.
READ_FRAMES = {
    "copilot": ["started", "contextAdmitted", "toolCallStarted", "toolCallCompleted", "contextAdmitted", "contributedResult", "messageDelta", "completed"],
    "anthropic": ["started", "contextAdmitted", "contextAdmitted", "stepStarted", "toolCallRequested", "stepCompleted", "toolCallStarted",
                  "contextAdmitted", "toolCallCompleted", "contributedResult", "contextAdmitted", "stepStarted", "messageDelta", "stepCompleted",
                  "completed"],
}


def working_state(seed, canary):
    """The open editor's unsaved working state over the seeded draft: each node's version and input values. The canary is
    the credential literal the editor holds; Studio's bridge never discloses it."""
    n, write, flow = seed["nodes"], seed["writeLine"], seed["flowchart"]
    wrapped = lambda kind, value: {"typeName": "String", "expression": {"type": kind, "value": value}}
    return {
        n["root"]: {"version": flow["activityVersionId"], "inputs": {}},
        n["a"]: {"version": write["activityVersionId"], "inputs": {"text": wrapped("Literal", "First")}},
        n["b"]: {"version": write["activityVersionId"], "inputs": {"text": wrapped("Variable", {"referenceKey": EXPLAIN_MISSING_VARIABLE, "declaringScopeId": "workflow"})}},
        n["nested"]: {"version": flow["activityVersionId"], "inputs": {}},
        # The unsaved change: the nested activity's text is now a JavaScript expression.
        n["inner"]: {"version": write["activityVersionId"], "inputs": {"text": wrapped("JavaScript", JAVASCRIPT)}},
        LITERAL_NODE: {"version": CREDENTIAL["activityVersionId"], "inputs": {CREDENTIAL["input"]: wrapped("Literal", canary)}},
        SECRET_NODE: {"version": CREDENTIAL["activityVersionId"], "inputs": {CREDENTIAL["input"]: wrapped("Secret", dict(SECRET_REFERENCE))}},
        # Seeded with no value; only the browser journey types a value into it.
        PASSWORD_ACTIVITY["nodeId"]: {"version": PASSWORD_ACTIVITY["activityVersionId"], "inputs": {}},
    }


def _bounded(value, limit):
    return value[:limit] if isinstance(value, str) and value.strip() else None


def designer_diagnostics(errors):
    """The owner's draft validations as the designer's panel lists them first (fromDraftValidationError), in Studio's
    disclosed shape (collectWorkflowContextDiagnostics): by activity and input, bounded, and counted beyond the bound."""
    check(isinstance(errors, list), "The owner's draft validations are not a list")
    disclosed = []
    for error in errors[:MAX_DIAGNOSTICS]:
        check(isinstance(error, dict), "Malformed owner validation")
        node = _bounded((error.get("nodeId") or "").strip(), 256)
        member = (error.get("member") or "").strip()
        value = {"severity": "warning" if str(error.get("severity") or "Error").lower() == "warning" else "error",
                 "message": _bounded(error.get("message"), MAX_MESSAGE) or "Workflow validation issue."}
        if _bounded(error.get("code"), 128): value["type"] = error["code"][:128]
        if node: value["nodeId"] = node
        if node and member.startswith("inputs/") and len(member) > len("inputs/"): value["property"] = member[len("inputs/"):][:256]
        disclosed.append(value)
    return disclosed, max(0, len(errors) - len(disclosed))


def property_context(node_id, state, inputs):
    """The selected activity's declared properties as Studio's bridge discloses them (collectSelectedActivityContext): the
    owner catalog's declared inputs in order, an expression's type and bounded content, a masked value withheld, and a
    Secret Reference by name, type and scope."""
    properties, budget = [], CONTENT_BUDGET
    for item in inputs:
        key = item["referenceKey"]
        masked = item.get("isSensitive") is True or item.get("isCredential") is True or str(item.get("uiHint") or "").lower() == "password"
        stored = state["inputs"].get(key)
        expression = stored.get("expression") if isinstance(stored, dict) else None
        if not isinstance(expression, dict):
            value = {"kind": "unset"}
        else:
            kind, raw = expression.get("type") or "Literal", expression.get("value")
            if kind == "Secret":
                value = ({"kind": "secretReference", "secretReference": {"name": raw["name"], "typeName": raw.get("typeName"), "scope": raw.get("scope")}}
                         if isinstance(raw, dict) and isinstance(raw.get("name"), str) and raw["name"].strip() else {"kind": "withheld", "expressionType": kind})
            elif masked:
                value = {"kind": "withheld", "expressionType": kind}
            elif raw is None:
                value = {"kind": "unset"}
            else:
                text = raw if isinstance(raw, str) else json.dumps(raw, separators=(",", ":"), ensure_ascii=False)
                content = text[:max(0, min(MAX_CONTENT, budget))]
                budget -= len(content)
                value = {"kind": "expression", "expressionType": kind, "content": content, "truncated": len(text) > len(content), "length": len(text)}
        properties.append({"name": item.get("name") or key, "referenceKey": key, "typeName": item.get("type") or "",
                           "isCredential": item.get("isCredential") is True, "isSensitive": masked, "value": value})
    truncated = any(item["value"]["kind"] == "expression" and item["value"]["truncated"] for item in properties)
    return {"nodeId": node_id, "activityVersionId": state["version"], "truncated": truncated, "properties": properties}


def admitted_selection(selected):
    """The server's admission of the property context: as disclosed, with its own interpretation status stamped on every
    non-literal expression."""
    value = copy.deepcopy(selected)
    for item in value["properties"]:
        if item["value"]["kind"] == "expression" and item["value"]["expressionType"] != "Literal":
            item["value"]["interpretation"] = INTERPRETATION
    return value


def _order(seed):
    n = seed["nodes"]
    # The designer's own order: the seeded tree depth first, then the unsaved activities added to the root.
    return [n["root"], n["a"], n["b"], n["nested"], n["inner"], PASSWORD_ACTIVITY["nodeId"], LITERAL_NODE, SECRET_NODE]


def expected_addresses(seed):
    """Each activity's scope-path address (FR-014): the container ids from the root to the container that holds it."""
    n, slot = seed["nodes"], seed["flowchart"]["slot"]
    return [{"id": node, "scopePath": [] if node == n["root"] else [n["root"], n["nested"]] if node == n["inner"] else [n["root"]],
             "slot": None if node == n["root"] else slot} for node in _order(seed)]


def explain_snapshot(seed, revision, state, catalog, diagnostics, omitted, selected):
    """The workflow module's client snapshot of the working state, as the open editor's bridge sends it."""
    n, flow = seed["nodes"], seed["flowchart"]
    order = _order(seed)
    types = {seed["writeLine"]["activityVersionId"]: (seed["writeLine"]["activityTypeKey"], seed["writeLine"]["displayName"]),
             flow["activityVersionId"]: (flow["activityTypeKey"], flow["displayName"]),
             CREDENTIAL["activityVersionId"]: (CREDENTIAL["activityTypeKey"], "Qualification credential activity"),
             PASSWORD_ACTIVITY["activityVersionId"]: (PASSWORD_ACTIVITY["activityTypeKey"], "Qualification password activity")}
    activities = [{"id": node, "type": types[state[node]["version"]][0], "displayName": types[state[node]["version"]][1]} for node in order]
    versions = {}
    for index, node in enumerate(order):
        versions.setdefault(state[node]["version"], []).append(index)
    members = lambda nodes: [{"id": flow["slot"], "mode": "flowchart", "cardinality": "many", "nodes": [order.index(node) for node in nodes]}]
    structure = {"versions": [{"id": key, "nodes": value} for key, value in versions.items()], "containers": [
        {"id": n["root"], "parentId": None, "slot": None, "slots": members([n["a"], n["b"], n["nested"], PASSWORD_ACTIVITY["nodeId"], LITERAL_NODE, SECRET_NODE])},
        {"id": n["nested"], "parentId": n["root"], "slot": flow["slot"], "slots": members([n["inner"]])}]}
    content = {"workflowDefinitionId": seed["definitionId"], "draftId": seed["draftId"], "revision": revision, "selectedNodeId": selected,
               "workingState": "draft", "activities": activities, "structure": structure,
               "connections": [{"source": n["a"], "target": n["b"], "sourcePort": "Done"}, {"source": n["a"], "target": n["nested"], "sourcePort": "Done"}],
               "positions": [{"nodeId": n["a"], "x": 120, "y": 160}, {"nodeId": n["b"], "x": 420, "y": 160}, {"nodeId": n["nested"], "x": 120, "y": 420},
                             {"nodeId": n["inner"], "x": 80, "y": 80}, {"nodeId": PASSWORD_ACTIVITY["nodeId"], "x": 720, "y": 160},
                             {"nodeId": LITERAL_NODE, "x": 960, "y": 160}, {"nodeId": SECRET_NODE, "x": 960, "y": 420}],
               "secretReferences": [dict(SECRET_REFERENCE)], "diagnostics": diagnostics}
    if omitted: content["diagnosticsOmitted"] = omitted
    if selected is not None:
        content["selectedActivity"] = property_context(selected, state[selected], catalog[state[selected]["version"]])
    target = {"moduleId": WORKFLOW_MODULE, "resourceType": "workflow-definition", "resourceId": seed["definitionId"], "revision": revision}
    return {"id": str(uuid.uuid4()), "moduleId": WORKFLOW_MODULE, "schemaVersion": "1", "source": WORKFLOW_MODULE, "sourceId": seed["definitionId"],
            "label": "Open workflow", "contentType": "workflow.definition", "sensitivity": "internal", "scope": "selection", "origin": "clientSnapshot",
            "target": target, "client": {"clientId": "qualification-editor", "generationId": str(uuid.uuid4())}, "content": content}


def require_admitted_draft(item, attachment):
    """One admitted working-state context equals what the editor disclosed: the exact target, its activities, structure,
    diagnostics and visible Secret References, the property context with the server's interpretation status, and no graph
    activity or other addition. Activity identity is compared; presentation members of the target are ignored."""
    sent = attachment["content"]
    check(isinstance(item, dict) and item.get("contentType") == "workflow.definition" and item.get("origin") == "clientSnapshot"
          and same_target(item.get("target"), attachment["target"]), "The admission lost the exact working-state target")
    content = item.get("content")
    check(isinstance(content, dict) and content.get("revision") == sent["revision"] and content.get("selectedNodeId") == sent["selectedNodeId"],
          "The admitted context is not attributed to the working-state revision the editor disclosed")
    check([(row.get("id"), row.get("type"), row.get("displayName")) for row in content.get("activities") or []]
          == [(row["id"], row["type"], row["displayName"]) for row in sent["activities"]]
          and same(content.get("structure"), sent["structure"]), "The admitted activities or nested structure differ from the working state")
    check(same(content.get("diagnostics"), sent["diagnostics"]) and content.get("diagnosticsOmitted") == sent.get("diagnosticsOmitted"),
          "The admitted diagnostics differ from the designer's")
    check(same(content.get("secretReferences"), sent["secretReferences"]), "The admitted Secret References differ from the visible ones")
    if "selectedActivity" in sent:
        check(same(content.get("selectedActivity"), admitted_selection(sent["selectedActivity"])),
              "The admitted property context differs from the selected activity's working state")
    else:
        check("selectedActivity" not in content, "A property context was admitted that the editor did not disclose")
    check("graphActivities" not in content, "A graph activity was disclosed that the working state does not reference")
    return content


def _no_canary(canary, *values):
    check(isinstance(canary, str) and len(canary) >= 24, "Use a distinct credential canary")
    for value in values:
        text = value.decode("utf-8", "replace") if isinstance(value, (bytes, bytearray)) else value if isinstance(value, str) else json.dumps(value, sort_keys=True)
        check(canary not in text, "The credential canary left the browser's working state")


def _owned(rows, provider, command_id):
    return [row for row in rows if isinstance(row, dict) and (row.get("data") or {}).get("commandId") == command_id
            and str(row.get("kind", "")).startswith(provider + ".")]


def require_explain_turn(frames, rows, accepted, *, provider, command_id, attachment, addresses, canary):
    """One explanation turn: the accepted message admitted the working state as disclosed; every admission in the turn
    carried that same projection; the read completed once with each activity's scope-path address; the actual adapter was
    offered and called the read tool once; and the canary appears in none of it."""
    check(provider in PROVIDERS, "Unknown adapter")
    values = [frame for frame in frames if isinstance(frame, dict)]
    check(len(values) == len(frames) and values, "Malformed native stream frame")
    kinds = [frame.get("kind") for frame in values]
    check(kinds == READ_FRAMES[provider], "The explanation turn did not follow this adapter's frame order: " + json.dumps(kinds))
    message = (accepted.get("data") or {}).get("message") if isinstance(accepted, dict) else None
    context = message.get("context") if isinstance(message, dict) else None
    check(isinstance(context, list), "The accepted message carries no admitted context")
    drafts = [item for item in context if isinstance(item, dict) and item.get("contentType") == "workflow.definition"]
    check(len(drafts) == 1, "The accepted message must admit exactly one working-state context")
    admitted = require_admitted_draft(drafts[0], attachment)
    for frame in values:
        if frame.get("kind") != "contextAdmitted":
            continue
        payload = frame.get("payload")
        check(isinstance(payload, dict) and payload.get("diagnostics") == [], "Context admission reported a diagnostic")
        current = [item for item in payload.get("attachments", []) if isinstance(item, dict) and item.get("contentType") == "workflow.definition"]
        check(len(current) == 1 and same(require_admitted_draft(current[0], attachment), admitted),
              "An admission in the turn differs from the accepted working-state context")
    completed = next(frame.get("payload") for frame in values if frame.get("kind") == "toolCallCompleted")
    result = completed.get("result") if isinstance(completed, dict) else None
    payload = result.get("payload") if isinstance(result, dict) else None
    data = payload.get("data") if isinstance(payload, dict) else None
    check(isinstance(completed, dict) and completed.get("toolName") == READ_TOOL and completed.get("succeeded") is True
          and isinstance(payload, dict) and payload.get("typeId") == RESULT_TYPE and isinstance(data, dict),
          "The read tool did not complete once with its typed result")
    check(same(data.get("addresses"), addresses) and data.get("addressesTruncated") is False,
          "The read result does not address each activity by its scope path")
    check(same({key: value for key, value in data.items() if key not in ("addresses", "addressesTruncated")}, admitted),
          "The read result is not the admitted working state")
    owned = _owned(rows, provider, command_id)
    calls = [row for row in owned if row.get("kind") == provider + ".call"]
    offered = [row["data"] for row in owned if row.get("kind") == provider + ".request" and row["data"].get("tools")]
    check(bool(offered) and READ_TOOL in {tool.get("name") for tool in offered[0]["tools"] if isinstance(tool, dict)}
          and len(calls) == 1 and calls[0]["data"].get("toolName") == READ_TOOL,
          "The actual adapter was not offered the read tool or did not call it exactly once")
    _no_canary(canary, values, rows, accepted)
    selected = admitted.get("selectedActivity")
    return {"revision": admitted["revision"], "selectedNodeId": admitted["selectedNodeId"], "diagnostics": len(admitted["diagnostics"]),
            "properties": [{"referenceKey": item["referenceKey"], "kind": item["value"]["kind"], "expressionType": item["value"].get("expressionType"),
                            "interpretation": item["value"].get("interpretation")} for item in (selected or {}).get("properties", [])],
            "addresses": len(addresses)}


def require_disclosure_refused(response, marker):
    """A snapshot that discloses a credential input's value is refused at admission with the module's own reason: no
    stream follows, and the refusal does not echo the value."""
    check(isinstance(marker, str) and len(marker) >= 24, "Use a distinct disclosure marker")
    check(response.status == CONTROL_ERROR["statusCode"], "A disclosed credential value was not refused at admission")
    value = response.json()
    error = value.get("error") if isinstance(value, dict) else None
    check(isinstance(error, dict) and {"code": error.get("code"), "statusCode": response.status, "message": error.get("message")} == CONTROL_ERROR,
          "The disclosed credential value was refused for another reason")
    check(marker.encode() not in response.body, "The refusal echoed the disclosed credential value")
    return {"control": CONTROL, "code": error["code"], "message": error["message"]}


BROWSER_JOURNEY = "workflow-explain"
BROWSER_STEPS = ("before", "after", "masked")
BROWSER_OBSERVATIONS = {"explainCompleted": True, "selectedExpressionType": "Variable", "interpretation": INTERPRETATION,
                        "diagnosticOnSelectedInput": True, "autosaveOff": True, "revisionChanged": True, "positionFollowed": True,
                        "maskedTyped": True, "maskedWithheld": True, "canaryAbsentInPage": True, "draftUnchanged": True, "owningWrites": 0}


def require_stream_capture(evidence):
    """The canary searches over the browser's agent streams count only when the streams were captured: every captured row
    without a capture or validation error, and at least one actual agent event stream (not a non-SSE response)."""
    refs = [ref for ref in evidence if isinstance(ref, dict) and Path(str(ref.get("path", ""))).name == "agent-streams.json"]
    check(len(refs) == 1, "The explanation Studio journey retained no single agent stream capture")
    path = Path(refs[0]["path"])
    check(path.is_file() and sha(path) == refs[0].get("sha256"), "The agent stream capture is missing or changed")
    rows = json.loads(path.read_text())
    check(isinstance(rows, list) and all(isinstance(row, dict) for row in rows), "Malformed agent stream capture")
    check(all(row.get("captureError") is None and row.get("captureValidationError") is None for row in rows),
          "An agent stream was not captured completely, so its absence of the canary proves nothing")
    check(any(row.get("captureMethod") not in (None, "not-sse") and isinstance(row.get("rawBody"), str) and row["rawBody"].startswith("data: ")
              for row in rows), "No actual agent event stream was captured to search")
    return len(rows)


def require_explain_browser(observed, commands, rows, *, provider):
    """The editor's own journey, checked against the adapter trace for each read the browser retained."""
    check(provider in PROVIDERS, "Unknown adapter")
    check(isinstance(observed, dict) and observed.get("journey") == BROWSER_JOURNEY and observed.get("status") == "passed"
          and not observed.get("failure") and not observed.get("pageErrors") and observed.get("provider") == provider,
          "The explanation Studio journey did not pass on this adapter")
    check(same(observed.get("observations"), BROWSER_OBSERVATIONS), "The explanation Studio journey observed another outcome")
    check(isinstance(observed.get("ownerBefore"), dict) and isinstance(observed.get("ownerAfter"), dict)
          and isinstance(observed.get("evidence"), list) and observed["evidence"], "The explanation Studio journey retained no owner evidence")
    require_stream_capture(observed["evidence"])
    selected = observed.get("commands")
    check(isinstance(selected, dict) and sorted(selected) == sorted(BROWSER_STEPS) and len(set(selected.values())) == len(BROWSER_STEPS),
          "The explanation journey must retain one distinct read per step")
    retained = {}
    for item in commands:
        check(isinstance(item, dict) and isinstance(item.get("commandId"), str) and item.get("toolName") == READ_TOOL and item.get("arguments") == {}
              and item["commandId"] not in retained, "Each retained browser command must be one distinct read with no arguments")
        retained[item["commandId"]] = item
    check(set(retained) == set(selected.values()), "The retained commands differ from the journey's steps")
    for step, command_id in selected.items():
        calls = [row for row in _owned(rows, provider, command_id) if row.get("kind") == provider + ".call"]
        check(len(calls) == 1 and calls[0]["data"].get("toolName") == READ_TOOL, "The actual adapter did not read exactly once for the " + step + " step")
    return {"journey": BROWSER_JOURNEY, "observations": observed["observations"], "commands": selected,
            "ownerBefore": observed["ownerBefore"], "ownerAfter": observed["ownerAfter"]}


@contextmanager
def _stage(runtime, name):
    check(threading.current_thread() is threading.main_thread() and hasattr(signal, "setitimer"),
          "Native explanation qualification requires an interruptible main-thread stage deadline")
    check(signal.getitimer(signal.ITIMER_REAL) == (0.0, 0.0), "Do not replace another qualification stage's active deadline")
    started = time.monotonic()
    stage = {"name": name, "status": "running", "boundSeconds": CASE_SECONDS}
    runtime.run.report[SECTION]["stages"].append(stage)
    runtime.run.save()
    def expired(_signum, _frame):
        raise TimeoutError("Native explanation stage expired without retry: " + name)
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


def _catalog(runtime, name, seed):
    """The owner's declared inputs for each version the working state uses, read after the seam is selected (the designer's
    own catalog read, availability all)."""
    links, capability = capability_links(runtime.backend, "elsa.api.activity-design", runtime.output(name + "-capabilities"))
    path = owner_path(runtime.backend, links["activity-catalog"])
    record, data = runtime.native.request(runtime.backend, name + "-owner-catalog", path + ("&" if "?" in path else "?") + "availability=all")
    check(record["status"] == 200, "The designer's catalog read failed")
    entries = json.loads(data).get("activities")
    inputs = {}
    for version in (seed["writeLine"]["activityVersionId"], seed["flowchart"]["activityVersionId"], CREDENTIAL["activityVersionId"],
                    PASSWORD_ACTIVITY["activityVersionId"]):
        matches = [entry for entry in entries if isinstance(entry, dict) and entry.get("activityVersionId") == version]
        check(len(matches) == 1 and isinstance(matches[0].get("inputs"), list), "The designer's catalog does not list exactly one " + version)
        inputs[version] = matches[0]["inputs"]
    check(any(item.get("referenceKey") == CREDENTIAL["input"] and item.get("isCredential") is True for item in inputs[CREDENTIAL["activityVersionId"]]),
          "The qualification credential activity has no credential input")
    check(any(item.get("referenceKey") == PASSWORD_ACTIVITY["input"] and str(item.get("uiHint") or "").lower() == "password"
              and item.get("isSensitive") is not True and item.get("isCredential") is not True for item in inputs[PASSWORD_ACTIVITY["activityVersionId"]]),
          "The qualification password activity has no input that only its password hint masks")
    return inputs, [capability, record["receipt"]]


def _turn(runtime, name, attachment):
    """One native explanation turn: a fresh session, the read tool commanded with no arguments."""
    transport = runtime.transport(name)
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    created = transport.create_session({"conversationId": str(uuid.uuid4()), "mode": "explain",
                                        "activeSurface": {"route": "/workflows/definitions"}, "autonomyMode": "auto-read-only"}).require_status(200)
    session = created.json()["data"]["sessionId"]
    command_id = str(uuid.uuid4())
    command = select_command(transport, command_id, READ_TOOL, {})
    message = transport.message(session, {"content": "Owned workflow explanation qualification request", "mode": "explain",
        "requestId": str(uuid.uuid4()), "requestedCapabilities": ["workflow.explain"], "contextAttachments": [attachment]}).require_status(200)
    stream = transport.collect_turn(session)
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the explanation turn")
    return (command_id, [frame.value for frame in stream.frames], trace, message.json(),
            [created.receipt, command, message.receipt, stream.receipt, trace["receipt"]])


def _native(runtime, name, seed, canary):
    before, before_data = runtime.native.request(runtime.backend, name + "-owner-before", seed["definitionPath"])
    catalog, evidence = _catalog(runtime, name, seed)
    evidence.append(before["receipt"])
    diagnostics, omitted = designer_diagnostics(seed["validations"])
    check(any(item.get("nodeId") == seed["nodes"]["b"] and item.get("property") == "text" and item.get("type") == UNRESOLVED_VARIABLE for item in diagnostics),
          "The designer's diagnostics do not name b's text")
    state, addresses, records = working_state(seed, canary), expected_addresses(seed), {}
    for turn, node in TURNS.items():
        selected = seed["nodes"].get(node, node)
        attachment = explain_snapshot(seed, "qualification-unsaved-" + uuid.uuid4().hex[:8], state, catalog, diagnostics, omitted, selected)
        _no_canary(canary, attachment)
        command_id, frames, trace, accepted, refs = _turn(runtime, name + "-" + turn, attachment)
        records[turn] = require_explain_turn(frames, trace["records"], accepted, provider=runtime.provider, command_id=command_id,
                                             attachment=attachment, addresses=addresses, canary=canary)
        evidence += refs
    # The selected credential literal, the withheld value, the unavailable interpretation and the bound reference, as read.
    check([item["kind"] for item in records["explain"]["properties"] if item["referenceKey"] == CREDENTIAL["input"]] == ["withheld"]
          and any(item["referenceKey"] == "text" and item["expressionType"] == "Variable" and item["interpretation"] == INTERPRETATION
                  for item in records["validation"]["properties"])
          and any(item["referenceKey"] == "text" and item["expressionType"] == "JavaScript" and item["interpretation"] == INTERPRETATION
                  for item in records["expression"]["properties"])
          and [item["kind"] for item in records["secret-reference"]["properties"] if item["referenceKey"] == CREDENTIAL["input"]] == ["secretReference"],
          "The explanation turns did not read the selected properties as the working state holds them")
    # The control: an editor that reports the credential input as an ordinary literal, carrying a value of its own.
    marker = "DISCLOSED-CREDENTIAL-CONTROL-" + uuid.uuid4().hex
    attachment = explain_snapshot(seed, "qualification-unsaved-" + uuid.uuid4().hex[:8], state, catalog, diagnostics, omitted, LITERAL_NODE)
    for item in attachment["content"]["selectedActivity"]["properties"]:
        if item["referenceKey"] == CREDENTIAL["input"]:
            item.update(isCredential=False, isSensitive=False, value={"kind": "expression", "expressionType": "Literal", "content": marker,
                                                                      "truncated": False, "length": len(marker)})
    transport = runtime.transport(name + "-" + CONTROL)
    created = transport.create_session({"conversationId": str(uuid.uuid4()), "mode": "explain",
                                        "activeSurface": {"route": "/workflows/definitions"}, "autonomyMode": "auto-read-only"}).require_status(200)
    refused = transport.message(created.json()["data"]["sessionId"], {"content": "Owned workflow explanation disclosure control", "mode": "explain",
        "requestId": str(uuid.uuid4()), "requestedCapabilities": ["workflow.explain"], "contextAttachments": [attachment]})
    records[CONTROL] = require_disclosure_refused(refused, marker)
    evidence += [created.receipt, refused.receipt]
    after, after_data = runtime.native.request(runtime.backend, name + "-owner-after", seed["definitionPath"])
    check(before["status"] == after["status"] == 200 and json.loads(after_data)["draft"] == json.loads(before_data)["draft"],
          "A native explanation turn changed the owning draft")
    evidence.append(after["receipt"])
    return records, evidence


def _browser(runtime, name, seed, canary):
    """Runs the actual browser journey as the workflow's manager (the admin) with a fresh normal-login state of its own."""
    _, identity = runtime.sign_in()
    root = runtime.output(name)
    output = root / "observation"
    n = seed["nodes"]
    workflow = {key: seed[key] for key in ("definitionId", "draftId", "definitionPath", "draftPath", "validationPath")}
    workflow["activityId"] = n["b"]
    # The canary travels in the private configuration only (never browser-inputs evidence): the driver types it into the
    # masked field, so it exists only in Studio's working state.
    workflow.update(nodes=n, explain={"property": "text", "expressionType": "Variable", "diagnosticType": UNRESOLVED_VARIABLE,
                                      "interpretation": INTERPRETATION, "moveNode": n["a"], "diagnosticMarker": EXPLAIN_MISSING_VARIABLE,
                                      "maskedNode": PASSWORD_ACTIVITY["nodeId"],
                                      "maskedProperty": PASSWORD_ACTIVITY["input"], "maskedLabel": PASSWORD_ACTIVITY["label"], "canary": canary})
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
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the explanation journey")
    # The browser retains a frozen copy of each command it selected; the shared command file is never evidence.
    commands = [json.loads(path.read_text()) for path in sorted(output.glob("command-*.json"))]
    value = require_explain_browser(observed, commands, trace["records"], provider=runtime.provider)
    evidence = [*observed["evidence"], observed["ownerBefore"], observed["ownerAfter"], {"path": str(observation), "sha256": sha(observation)},
                runtime.save(root, "browser-inputs", browser_inputs(config)), trace["receipt"]]
    # The typed canary in no captured browser request, agent stream, owner read, adapter trace or other browser evidence.
    value["canarySearch"] = {"files": require_no_canary_in_evidence(evidence, canary, runtime.run.root), "found": 0}
    _no_canary(canary, trace["records"])
    return value, evidence


def require_no_canary_in_evidence(evidence, canary, root):
    """The canary search over every retained evidence file of the case (frames, adapter traces, HTTP receipts, owner
    readbacks, browser captures): it must appear in none."""
    check(isinstance(evidence, list) and evidence, "The explanation case retained no evidence to search")
    searched = 0
    for ref in evidence:
        path = Path(ref["path"])
        check(path.resolve().is_relative_to(Path(root).resolve()) and path.is_file() and sha(path) == ref["sha256"],
              "Explanation evidence is missing, changed or outside the owned run")
        check(canary.encode() not in path.read_bytes(), "The credential canary appears in retained evidence: " + path.name)
        searched += 1
    return searched


def _case(runtime, provider):
    name = provider + "-workflow-explain"
    evidence = list(runtime.select_provider(provider))
    canary = "CANARY-CREDENTIAL-" + uuid.uuid4().hex
    runtime.sign_in()
    seed = seed_batch_workflow(runtime.backend, runtime.output(name + "-workflow") / "workflow-seed", explain=True)
    evidence.extend(seed["evidence"])
    evidence.extend(runtime.apply_features(name + "-select-seam", {SEAM_FEATURE: (True, {})}))
    try:
        native, native_evidence = _native(runtime, name, seed, canary)
        evidence.extend(native_evidence)
        browser, browser_evidence = _browser(runtime, name + "-browser", seed, canary)
        evidence.extend(browser_evidence)
    finally:
        evidence.extend(runtime.apply_features(name + "-deselect-seam", {SEAM_FEATURE: (False, {})}))
    searched = require_no_canary_in_evidence(evidence, canary, runtime.run.root)
    return {"id": name, "status": "passed", "adapter": provider, "evidenceCategory": EVIDENCE_CATEGORY, "native": native,
            "browser": browser, "canarySearch": {"files": searched, "found": 0}, "evidence": evidence}


def qualify_workflow_explain(runtime):
    run = runtime.run
    check(SECTION not in run.report, "Never resume or overwrite a prior workflow explanation qualification")
    original_cases = copy.deepcopy(run.report["cases"])
    section = {"status": "running", "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False, "source": run.source,
               "caseIDs": [], "records": [], "stages": [],
               "driver": {"repositoryPath": "tools/studio/Weaver.Conformance.Probe/explain_journeys.py", "sha256": sha(TOOL / "explain_journeys.py")}}
    run.report[SECTION] = section
    run.save()
    try:
        for provider in PROVIDERS:
            name = provider + "-workflow-explain"
            section["activeCase"] = name
            with _stage(runtime, name):
                value = _case(runtime, provider)
                record = runtime.save(runtime.output(name + "-record"), "observation", value)
                section["caseIDs"].append(name)
                section["records"].append({"id": name, "status": "passed", "record": record})
        check(set(section["caseIDs"]) == EXPECTED_CASES and len(section["records"]) == len(EXPECTED_CASES),
              "Each workflow explanation case must be observed exactly once")
        check(same(original_cases, run.report["cases"]), "Workflow explanation qualification changed the original M1 case records")
        section.pop("activeCase", None)
        section["status"] = "passed"
    except BaseException as error:
        section.update(status="failed", failure={"type": type(error).__name__, "message": str(error)})
        raise
    finally:
        run.save()


def explain_manifest_gaps(section, source=None):
    """Structural reasons a section cannot complete a witness; reads no files."""
    if not isinstance(section, dict):
        return ["The mandatory workflow explanation section is absent"]
    gaps = []
    names = section.get("caseIDs") if isinstance(section.get("caseIDs"), list) else []
    records = section.get("records") if isinstance(section.get("records"), list) else []
    if section.get("status") != "passed":
        gaps.append("Workflow explanation section status is " + repr(section.get("status")))
    if section.get("evidenceCategory") != EVIDENCE_CATEGORY or section.get("liveProvider") is not False:
        gaps.append("Unsupported workflow explanation provider evidence claim")
    if source is not None and section.get("source") != source:
        gaps.append("Workflow explanation section source differs from the witness source")
    if (sorted(names) != sorted(EXPECTED_CASES) or len(names) != len(EXPECTED_CASES)
            or [row.get("id") if isinstance(row, dict) else None for row in records] != names
            or not all(isinstance(row, dict) and row.get("status") == "passed" and isinstance(row.get("record"), dict) for row in records)):
        gaps.append("The workflow explanation cases were not each observed passed exactly once")
    return gaps


NATIVE_RECORDS = (*TURNS, CONTROL)


def require_workflow_explain(section, owned_root=None, *, source=None):
    """Mandatory final gate: both adapters passed, and each retained record still holds every turn, the control, the
    journey and a canary search that found nothing."""
    gaps = explain_manifest_gaps(section, source)
    check(not gaps, "The workflow explanation inventory is mandatory on each actual adapter: " + "; ".join(gaps))
    if owned_root is not None:
        root = Path(owned_root).resolve()
        for row in section["records"]:
            path = Path(row["record"]["path"])
            check(path.is_absolute() and path.resolve().is_relative_to(root) and path.is_file() and sha(path) == row["record"]["sha256"],
                  "Workflow explanation record is missing, changed or outside the owned run: " + row["id"])
            observed = json.loads(path.read_text())
            native = observed.get("native") if isinstance(observed.get("native"), dict) else {}
            search = observed.get("canarySearch") if isinstance(observed.get("canarySearch"), dict) else {}
            check(observed.get("id") == row["id"] and observed.get("status") == "passed" and observed.get("adapter") == row["id"].split("-", 1)[0]
                  and sorted(native) == sorted(NATIVE_RECORDS) and (native.get(CONTROL) or {}).get("code") == CONTROL_ERROR["code"]
                  and all(isinstance(native.get(turn), dict) and isinstance(native[turn].get("revision"), str) for turn in TURNS)
                  and search.get("found") == 0 and type(search.get("files")) is int and search["files"] > 0
                  and same((observed.get("browser") or {}).get("observations"), BROWSER_OBSERVATIONS),
                  "Workflow explanation record differs: " + row["id"])
    return section["records"]


def synthetic_native():
    """The minimal native record shape the final gate checks; for synthetic self-tests only, never native evidence."""
    return {**{turn: {"revision": "synthetic-" + turn} for turn in TURNS}, CONTROL: {"control": CONTROL, "code": CONTROL_ERROR["code"]}}


def synthetic_browser():
    """The minimal browser shape the final gate checks; for synthetic self-tests only."""
    return {"journey": BROWSER_JOURNEY, "observations": copy.deepcopy(BROWSER_OBSERVATIONS)}


def synthetic_search():
    """The minimal canary search shape the final gate checks; for synthetic self-tests only."""
    return {"files": 1, "found": 0}
