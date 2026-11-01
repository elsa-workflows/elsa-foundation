"""The nine remaining per-adapter #3440 lifecycle cases (T015) through real native paths.

Each case runs once on the actual selected Copilot or Anthropic adapter. The Python cases reuse
the original M1 driver operations (deny, stale, authority, cancel, duplicate delivery) with the
module's size operation and the owning Agent HTTP/SSE routes; the browser cases reuse the original
resource, workflow and lost-confirmation journeys. The fixture's pre-call gate and the browser ACK
interception only coordinate those real paths. Verifiers read retained bytes: an HTTP status,
selector label or provider text alone never establishes an outcome. Nothing here qualifies a
package witness; T025 owns that.
"""
from __future__ import annotations

import copy
import json
import os
from pathlib import Path
import secrets
import shutil
import time
from urllib.parse import quote, urlparse
import uuid

from agent_transport import AgentTransportError, _json as strict_json
from artifacts import TOOL, check, sha
from case_records import _current_function_results, _normalized_result, _same_json, correlate_browser_protocol, retained
from journey_runtime import BASE_GRANTS, atomic_json, browser_inputs, private_policy_keys, same
from owner_resources import seed_workflow
from runtime import RUNTIME_ENVIRONMENT, private_json

PROVIDERS = ("copilot", "anthropic")
REVIEW_STATUSES = {"denied-result-continuation": "denied", "stale-target-continuation": "refused",
                   "authority-refusal-continuation": "refused"}
# Browser cases reuse an original journey; the mode is selected before the conversation exists.
BROWSER_CASES = {"interrupted-after-effect": ("lost-confirmation", True, None),
                 "visibility-review-control": ("resource", False, "full-auto"),
                 "workflow-review-control": ("workflow", True, "full-auto")}
# Driver-owned expected outcomes. Each record states its observed outcome; any other value fails.
EXPECTED_OUTCOMES = {
    "denied-result-continuation": {"decision": "denied", "execute": "agent.proposal.closed", "continuation": "denied", "effects": 0},
    "duplicate-effect-delivery": {"execute": "executed", "repeatExecute": "retained", "repeatDelivery": "agent.message.no_pending", "effects": 1},
    "cancel-before-effect": {"terminal": "turnCancelled", "gate": "protocol.gate.cancelled", "effects": 0},
    "interrupted-after-effect": {"lostDelivery": "net::ERR_FAILED", "uncertain": True, "lateConfirmation": "executed", "replays": 0},
    "stale-target-continuation": {"execute": "acme.home.stale", "continuation": "refused", "revisionAdvanced": True, "effects": 0},
    "authority-refusal-continuation": {"execute": "agent.proposal.forbidden", "continuation": "refused", "readRetained": True, "effects": 0},
    "visibility-review-control": {"policy": "full-auto", "atDecision": "review", "application": "executed", "effects": 1},
    "workflow-review-control": {"policy": "full-auto", "atDecision": "review", "application": "executed", "explicitSave": True, "publishRequests": 0},
    "busy-policy-lifecycle": {"busy": ["review", "approved", "receipt-continuation"], "busyCode": "agent.session.busy", "settled": "full-auto", "effects": 1},
}
REVIEWED = ("sessionId", "turnId", "commandId", "proposalId", "invocationId", "toolCallId")
CORRELATION_KEYS = {key: REVIEWED for key in EXPECTED_OUTCOMES}
CORRELATION_KEYS.update({"cancel-before-effect": ("sessionId", "turnId", "commandId", "gateEpoch"),
                         "interrupted-after-effect": ("sessionId", "proposalId", "invocationId", "toolCallId"),
                         "visibility-review-control": ("sessionId", "proposalId", "invocationId", "toolCallId"),
                         "workflow-review-control": ("sessionId", "proposalId", "invocationId", "toolCallId")})
WORKFLOW_TOOL = "workflow_set_designer_position"
SETTLE_POLLS = 20


class LifecycleCaseFailure(RuntimeError):
    """A lifecycle case that ended with its own precise, retained cause."""
    def __init__(self, message, cause, observation=None):
        super().__init__(message)
        self.cause, self.observation = cause, observation


def need(condition, cause, message):
    if not condition:
        raise LifecycleCaseFailure(cause + ": " + message, cause)


def _public_policy_only(value, message):
    """Retained native client data carries the public session-policy shape only (data-model.md)."""
    leaked = private_policy_keys(value)
    need(not leaked, "lifecycle.policy_disclosed", message + " (" + ", ".join(sorted(leaked)) + ")")


# --- retained evidence readers -------------------------------------------------------------

def _path(root, reference):
    check(isinstance(reference, dict) and isinstance(reference.get("path"), str) and isinstance(reference.get("sha256"), str),
          "Missing retained native evidence reference")
    path = Path(reference["path"])
    check(path.is_absolute() and path.resolve().is_relative_to(Path(root).resolve()) and path.is_file()
          and sha(path) == reference["sha256"], "Missing, changed or foreign native evidence")
    return path


def _exchange(root, reference, method, path, status):
    """One retained AgentTransport exchange: exact method/route/status and its parsed native body."""
    record = strict_json(_path(root, reference).read_text())
    check(isinstance(record, dict) and record.get("method") == method and record.get("path") == path
          and isinstance(record.get("rawBody"), str) and not record.get("transportErrorType"),
          "A retained native exchange is missing or names another route: " + method + " " + path)
    need(record.get("status") == status, "lifecycle.native_status",
         f"{method} {path} returned {record.get('status')}; expected {status}")
    value = strict_json(record["rawBody"]) if record["rawBody"] else None
    check(isinstance(value, dict), "A native Agent response must be a JSON envelope")
    return value, record


def _sse(root, reference, session):
    record = strict_json(_path(root, reference).read_text())
    check(isinstance(record, dict) and record.get("method") == "GET" and record.get("path") == _session_path(session) + "/stream"
          and isinstance(record.get("frames"), list), "The retained native stream names another session")
    need(record.get("status") == 200 and record.get("errorType") is None and record.get("termination") in ("eof", "client-closed"),
         "lifecycle.native_stream", "A failed or open native SSE cannot prove a lifecycle outcome")
    values = [frame.get("value") if isinstance(frame, dict) else None for frame in record["frames"]]
    check(values and all(isinstance(value, dict) and isinstance(value.get("kind"), str) and isinstance(value.get("id"), str)
                         for value in values) and len({value["id"] for value in values}) == len(values),
          "Native frames need distinct actual event identities")
    _public_policy_only(values, "A native stream frame disclosed private policy")
    return values


def _json_file(root, reference):
    return strict_json(_path(root, reference).read_bytes().decode("utf-8"))


def _size_owner(root, reference, witness):
    value = _json_file(root, reference)
    check(isinstance(value, dict) and set(value) == {"widgetId", "revision", "size", "hostId"}
          and value["widgetId"] == witness["widgetId"] and value["hostId"] == witness["hostId"]
          and value["size"] in ("small", "medium") and isinstance(value["revision"], str) and value["revision"],
          "The native size owner must identify this exact supported personal card")
    return value


def _session_path(session):
    return "/_elsa/agent/sessions/" + quote(session, safe="")


def _proposal_path(proposal, action):
    return "/_elsa/agent/proposals/" + quote(proposal, safe="") + "/" + action


def _trace(root, reference):
    trace = _json_file(root, reference)
    check(isinstance(trace, dict) and isinstance(trace.get("records"), list) and trace.get("incompleteTailBytes") == 0,
          "An incomplete actual factory trace cannot prove the lifecycle outcome")
    return trace["records"]


# --- shared lifecycle verification ---------------------------------------------------------

def _verify_open(root, o, witness):
    session, command_id, target = o["sessionId"], o["commandId"], o["targetSize"]
    created, _ = _exchange(root, o["refs"]["create"], "POST", "/_elsa/agent/sessions", 200)
    data = created.get("data") or {}
    policy = data.get("policy")
    need(data.get("sessionId") == session and isinstance(policy, dict) and policy.get("autonomyMode") == o["autonomy"]
         and type(policy.get("revision")) is int, "lifecycle.session_policy", "The new conversation did not acknowledge its declared mode")
    need(_json_file(root, o["refs"]["command"]) == {"commandId": command_id, "toolName": witness["sizeTool"], "arguments": {"size": target}},
         "lifecycle.command", "The actual protocol command changed")
    accepted, record = _exchange(root, o["refs"]["message"], "POST", _session_path(session) + "/messages", 200)
    _public_policy_only(accepted, "The accepted size prompt disclosed private policy")
    message = (accepted.get("data") or {}).get("message") or {}
    request = record.get("requestBody") or {}
    need(message.get("sessionId") == session and str(message.get("role", "")).lower() == "user" and message.get("id") == o["messageId"]
         and message.get("requestId") == request.get("requestId") and request.get("requestedCapabilities") == [witness["sizeCapability"]]
         and same(message.get("acceptedPolicy"), policy),
         "lifecycle.message", "The size prompt was not accepted into this conversation under its acknowledged policy")
    before = _size_owner(root, o["refs"]["ownerBefore"], witness)
    need(before["size"] != target, "lifecycle.command", "A no-op size cannot establish a lifecycle outcome")
    return policy, message, before


def _verify_pause(root, o, witness, before):
    values = _sse(root, o["refs"]["initialStream"], o["sessionId"])
    started = [value for value in values if value["kind"] == "started"]
    need(len(started) == 1 and isinstance(started[0].get("payload"), dict) and started[0]["payload"].get("turnId"),
         "lifecycle.review_missing", "The size turn did not start exactly once")
    turn = started[0]["payload"]["turnId"]
    proposals = [value.get("payload") for value in values if value["kind"] == "proposalCreated"]
    need(len(proposals) == 1 and isinstance(proposals[0], dict), "lifecycle.review_missing", "A single pending native review is required")
    proposal = proposals[0]
    prepared, target = proposal.get("preparedInvocation") or {}, (proposal.get("preparedInvocation") or {}).get("target") or {}
    need(proposal.get("sessionId") == o["sessionId"] and proposal.get("requiresApproval") is True and proposal.get("id")
         and str(proposal.get("status", "")).replace("-", "").lower() == "awaitingapproval"
         and prepared.get("toolName") == witness["sizeTool"] and prepared.get("sessionId") == o["sessionId"] and prepared.get("turnId") == turn
         and prepared.get("arguments") == {"size": o["targetSize"]} and prepared.get("requiresApproval") is True
         and all(isinstance(prepared.get(key), str) and prepared[key] for key in ("invocationId", "toolCallId"))
         and target.get("resourceId") == witness["widgetId"] and target.get("revision") == before["revision"]
         and proposal.get("baseRevision") == before["revision"],
         "lifecycle.review_binding", "The pending review is not the original prepared personal size operation")
    need(values[-1]["kind"] == "toolApprovalRequested" and values[-1].get("proposalId") == proposal["id"]
         and not any(value["kind"] == "contributedResult" for value in values),
         "lifecycle.review_bypassed", "The native turn applied or did not stop at this exact review boundary")
    return turn, proposal


def _verify_continuation(root, reference, session, turn):
    values = _sse(root, reference, session)
    need(values[0]["kind"] == "started" and (values[0].get("payload") or {}).get("turnId") == turn and values[-1]["kind"] == "completed"
         and sum(value["kind"] == "started" for value in values) == 1
         and not any(value["kind"] in ("proposalCreated", "toolCallRequested", "toolCallStarted", "toolApprovalRequested", "error", "turnCancelled")
                     for value in values),
         "lifecycle.continuation_missing", "The outcome did not continue and complete the originating native turn")
    return values


def _provider(root, o, witness, proposal, provider, *, continued=True):
    """Exactly one advertisement/call and, when continued, one result continuation for this command."""
    records = _trace(root, o["refs"]["trace"])
    kinds = {provider + "." + name for name in ("request", "call", "return", "continuation")}
    others = {other + "." + name for other in PROVIDERS if other != provider for name in ("request", "call", "return", "continuation")}
    need(not any(row.get("kind") in others for row in records), "lifecycle.provider_correlation", "Another adapter answered the selected case")
    rows = [row for row in records if row.get("kind") in kinds]
    need(rows and all((row.get("data") or {}).get("commandId") == o["commandId"] for row in rows),
         "lifecycle.provider_correlation", "A foreign command reached the actual adapter")
    sequence = [row["kind"].split(".", 1)[1] for row in rows]
    expected = ([["request", "call", "request", "continuation"]] + ([["request", "call", "return", "request", "continuation"]] if provider == "copilot" else [])
                if continued else [["request"]])
    need(sequence in expected, "lifecycle.provider_correlation", "The actual adapter sequence is " + ",".join(sequence))
    first = rows[0]["data"]
    need(sum(tool.get("name") == witness["sizeTool"] for tool in first.get("tools", [])) == 1, "lifecycle.provider_correlation",
         "The actual adapter did not advertise the size operation exactly once")
    if provider == "copilot":
        need(all(row["data"].get("sessionId") == o["sessionId"] for row in rows if row["kind"] in ("copilot.request", "copilot.continuation")),
             "lifecycle.provider_correlation", "The actual SDK resumed another native session")
    if not continued:
        return None
    call = rows[1]["data"]
    need(call.get("toolName") == witness["sizeTool"] and same(call.get("arguments"), {"size": o["targetSize"]}),
         "lifecycle.provider_correlation", "The actual call differs from its command")
    prepared = proposal["preparedInvocation"]
    for row in rows:
        if row["kind"] == "copilot.return":
            value = row["data"].get("result") or {}
            need(value.get("status") == "awaiting-approval" and value.get("executed") is False and value.get("proposalId") == proposal["id"]
                 and not value.get("receipt"), "lifecycle.provider_correlation", "A pending SDK return claimed application")
    resumed = rows[-2]["data"]
    need(not resumed.get("tools"), "lifecycle.provider_correlation", "A receipt continuation advertised callable tools again")
    if provider == "copilot":
        results = rows[-1]["data"].get("results")
    else:
        items = rows[-1]["data"].get("results")
        need(prepared["toolCallId"] == o["commandId"] and isinstance(items, list) and len(items) == 1
             and items[0].get("kind") == "functionResult" and items[0].get("callId") == o["commandId"]
             and isinstance(items[0].get("result"), str) and _same_json(_current_function_results(resumed), items),
             "lifecycle.provider_correlation", "Anthropic function-result correlation changed")
        results = [strict_json(items[0]["result"])]
    need(isinstance(results, list) and len(results) == 1 and isinstance(results[0], dict)
         and results[0].get("toolCallId") == prepared["toolCallId"] and results[0].get("toolName") == witness["sizeTool"],
         "lifecycle.continuation_result", "The continuation result does not identify the original prepared call")
    return results[0]


def _lifecycle_result(result, prepared, status):
    payload = result.get("payload") or {}
    data = payload.get("data") if isinstance(payload.get("data"), dict) else {}
    need(result.get("succeeded") is False and not result.get("receipt") and payload.get("typeId") == "agent.operation-outcome"
         and payload.get("schemaVersion") == "1"
         and {key.lower(): value for key, value in data.items()} == {"status": status, "invocationid": prepared["invocationId"]},
         "lifecycle.continuation_result", "The originating turn did not receive the exact " + status + " outcome")
    return data


def _stored_once(messages, result):
    matches = []
    for message in messages if isinstance(messages, list) else []:
        if str(message.get("role", "")).lower() != "tool":
            continue
        try:
            value = strict_json(message.get("content", ""))
        except (ValueError, TypeError):
            continue
        if isinstance(value, dict) and value.get("toolCallId") == result.get("toolCallId"):
            matches.append(value)
    need(len(matches) == 1 and _same_json(_normalized_result(matches[0]), _normalized_result(result)),
         "lifecycle.stored_result", "The owning session must retain exactly this one original outcome")
    return matches[0]


def _session(root, reference, session):
    value, _ = _exchange(root, reference, "GET", _session_path(session), 200)
    _public_policy_only(value, "Session detail disclosed private policy")
    data = value.get("data") or {}
    check(data.get("sessionId") == session and isinstance(data.get("messages"), list), "Session detail names another conversation")
    return data


def _executed(root, reference, proposal, before, witness):
    value, _ = _exchange(root, reference, "POST", _proposal_path(proposal["id"], "execute"), 200)
    data = value.get("data") or {}
    result = data.get("toolResult") or {}
    receipt = result.get("receipt") or {}
    need(value.get("error") is None and data.get("executed") is True and data.get("status") == "executed" and data.get("proposalId") == proposal["id"]
         and result.get("succeeded") is True and result.get("toolCallId") == proposal["preparedInvocation"]["toolCallId"]
         and receipt.get("invocationId") == proposal["preparedInvocation"]["invocationId"]
         and (receipt.get("before") or {}).get("revision") == before["revision"], "lifecycle.effect_missing",
         "The approved size operation has no exact owning receipt")
    return data


def _correlation(provider, o, turn, proposal):
    value = {"adapter": provider, "sessionId": o["sessionId"], "turnId": turn, "commandId": o["commandId"], "providerTrace": o["refs"]["trace"]}
    if proposal is not None:
        prepared = proposal["preparedInvocation"]
        value.update(proposalId=proposal["id"], invocationId=prepared["invocationId"], toolCallId=prepared["toolCallId"])
    return value


def _record(root, provider, key, outcome, correlation, owner_before, owner_after, evidence):
    record = {"id": provider + "-" + key, "provider": provider, "adapter": provider, "caseKey": key, "status": "passed", "cause": None,
              "modeMismatch": False, "qualified": False, "nativeEvidenceVerified": True, "outcome": outcome,
              "correlation": correlation, "ownerBefore": owner_before, "ownerAfter": owner_after, "evidence": evidence}
    require_lifecycle_record(root, record)
    return record


def require_lifecycle_record(root, row):
    """Manifest gate for one non-mode case: exact adapter, expected cause, owner and correlation evidence."""
    key, provider = row.get("caseKey"), row.get("provider")
    check(key in EXPECTED_OUTCOMES and provider in PROVIDERS and row.get("adapter") == provider and row.get("id") == f"{provider}-{key}",
          "A lifecycle case must name its exact actual adapter")
    check(row.get("status") == "passed" and row.get("cause") is None and row.get("nativeEvidenceVerified") is True
          and row.get("qualified") is False and row.get("modeMismatch") is False, "Failed, unverified or promoted lifecycle case: " + row["id"])
    check(same(row.get("outcome"), EXPECTED_OUTCOMES[key]), "Wrong-cause lifecycle outcome cannot qualify: " + row["id"])
    correlation = row.get("correlation")
    check(isinstance(correlation, dict) and correlation.get("adapter") == provider
          and all(isinstance(correlation.get(name), str) and correlation[name] for name in CORRELATION_KEYS[key]),
          "Missing native correlation evidence: " + row["id"])
    check(isinstance(row.get("evidence"), list) and row["evidence"], "Owner markers alone cannot qualify a lifecycle case")
    for reference in (row.get("ownerBefore"), row.get("ownerAfter"), correlation.get("providerTrace"), *row["evidence"]):
        _path(root, reference)
    return row


# --- Python verifiers ----------------------------------------------------------------------

def _refusal_case(root, o, witness, provider, key):
    """Denied, stale and authority refusals: zero effect and the refusal returns to the originating turn."""
    refs = o["refs"]
    _, _, before = _verify_open(root, o, witness)
    turn, proposal = _verify_pause(root, o, witness, before)
    prepared = proposal["preparedInvocation"]
    outcome = {}
    if key == "denied-result-continuation":
        denied, _ = _exchange(root, refs["deny"], "POST", _proposal_path(proposal["id"], "deny"), 200)
        need((denied.get("data") or {}).get("status") == "denied", "lifecycle.decision", "Native review did not deny the proposal")
        closed, _ = _exchange(root, refs["closedExecute"], "POST", _proposal_path(proposal["id"], "execute"), 409)
        need((closed.get("error") or {}).get("code") == "agent.proposal.closed" and (closed.get("data") or {}).get("status") == "denied"
             and (closed.get("data") or {}).get("executed") is False, "lifecycle.refusal_cause", "A denied proposal was not retained closed")
        outcome.update(decision="denied", execute=closed["error"]["code"])
        settled = before
    else:
        approved, _ = _exchange(root, refs["approve"], "POST", _proposal_path(proposal["id"], "approve"), 200)
        need((approved.get("data") or {}).get("status") == "approved", "lifecycle.decision", "Native review did not approve the proposal")
        expected = ("acme.home.stale", 409) if key == "stale-target-continuation" else ("agent.proposal.forbidden", 403)
        refused, _ = _exchange(root, refs["execute"], "POST", _proposal_path(proposal["id"], "execute"), expected[1])
        need((refused.get("error") or {}).get("code") == expected[0], "lifecycle.refusal_cause", "Wrong native refusal; expected " + expected[0])
        outcome["execute"] = refused["error"]["code"]
        if key == "stale-target-continuation":
            settled = _size_owner(root, refs["ownerBeforeExecution"], witness)
            need(settled["size"] == before["size"] and settled["revision"] != before["revision"], "lifecycle.refusal_cause",
                 "The independent native edit did not advance the prepared revision")
            outcome["revisionAdvanced"] = True
        else:
            grants = _json_file(root, _json_file(root, refs["authority"])["body"])
            need(set(grants.get("directPermissions", [])) == set(BASE_GRANTS) - {"studio/preferences:write"}, "lifecycle.authority",
                 "Only native mutation authority may be withdrawn")
            settled = _size_owner(root, refs["ownerWithheld"], witness)
            need(settled == before, "lifecycle.effect_observed", "The refused operation changed its owner")
            outcome["readRetained"] = True
    _verify_continuation(root, refs["continuation"], o["sessionId"], turn)
    result = _provider(root, o, witness, proposal, provider)
    outcome["continuation"] = _lifecycle_result(result, prepared, REVIEW_STATUSES[key])["status"]
    _stored_once(_session(root, refs["session"], o["sessionId"])["messages"], result)
    after = _size_owner(root, refs["ownerAfter"], witness)
    need(after == settled, "lifecycle.effect_observed", "A refused operation produced an additional owner effect")
    outcome["effects"] = 0
    return _record(root, provider, key, outcome, _correlation(provider, o, turn, proposal), refs["ownerBefore"], refs["ownerAfter"], list(refs.values()))


def verify_cancel(root, o, witness, provider):
    refs = o["refs"]
    _, _, before = _verify_open(root, o, witness)
    values = _sse(root, refs["initialStream"], o["sessionId"])
    turn = (values[0].get("payload") or {}).get("turnId") if values[0]["kind"] == "started" else None
    need(turn and values[-1]["kind"] == "turnCancelled" and (values[-1].get("payload") or {}).get("turnId") == turn
         and not any(value["kind"] in ("proposalCreated", "toolCallStarted", "contributedResult", "toolApprovalRequested", "error", "completed")
                     for value in values), "lifecycle.cancel", "The turn was not cancelled before any operation or result")
    cancelled, _ = _exchange(root, refs["cancel"], "POST", _session_path(o["sessionId"]) + "/turns/" + quote(turn, safe="") + "/cancel", 200)
    need((cancelled.get("data") or {}).get("cancelled") is True and cancelled["data"].get("turnId") == turn, "lifecycle.cancel",
         "Native cancellation did not cancel this exact running turn")
    _provider(root, o, witness, None, provider, continued=False)
    gate = _json_file(root, refs["gate"])
    rows = [row for row in _trace(root, refs["trace"]) if str(row.get("kind", "")).startswith("protocol.gate.")]
    need([row["kind"] for row in rows] == ["protocol.gate.waiting", "protocol.gate.cancelled"]
         and all((row.get("data") or {}).get("commandId") == o["commandId"] == gate.get("commandId") and row["data"].get("epoch") == gate.get("epoch")
                 and row["data"].get("provider") == provider for row in rows), "lifecycle.gate",
         "Cancellation did not interrupt this adapter at the declared pre-call gate")
    session = _session(root, refs["session"], o["sessionId"])
    need(not any(str(message.get("role", "")).lower() == "tool" for message in session["messages"]), "lifecycle.effect_observed",
         "A cancelled turn recorded an applied result")
    prompts = [message for message in session["messages"] if message.get("id") == o["messageId"]]
    need(len(prompts) == 1 and str(prompts[0].get("status", "")).lower() == "cancelled", "lifecycle.cancel",
         "The cancelled turn did not terminalize its own accepted prompt as cancelled")
    after = _size_owner(root, refs["ownerAfter"], witness)
    need(after == before, "lifecycle.effect_observed", "Cancellation before effect changed the owner")
    correlation = _correlation(provider, o, turn, None)
    correlation["gateEpoch"] = gate["epoch"]
    outcome = {"terminal": values[-1]["kind"], "gate": rows[-1]["kind"], "effects": 0}
    return _record(root, provider, "cancel-before-effect", outcome, correlation, refs["ownerBefore"], refs["ownerAfter"], list(refs.values()))


def verify_duplicate(root, o, witness, provider):
    refs = o["refs"]
    _, _, before = _verify_open(root, o, witness)
    turn, proposal = _verify_pause(root, o, witness, before)
    _exchange(root, refs["approve"], "POST", _proposal_path(proposal["id"], "approve"), 200)
    first = _executed(root, refs["execute"], proposal, before, witness)
    applied = _size_owner(root, refs["ownerAfterExecute"], witness)
    need(applied["size"] == o["targetSize"] and applied["revision"] != before["revision"]
         and (first["toolResult"]["receipt"].get("after") or {}).get("revision") == applied["revision"], "lifecycle.effect_missing",
         "The owner does not confirm the executed receipt")
    for name in ("repeatExecute", "lateExecute"):
        repeated, _ = _exchange(root, refs[name], "POST", _proposal_path(proposal["id"], "execute"), 200)
        need(same(repeated.get("data"), first) and repeated.get("error") is None, "lifecycle.duplicate_effect",
             "A repeated execution did not return the original retained receipt")
    _verify_continuation(root, refs["continuation"], o["sessionId"], turn)
    result = _provider(root, o, witness, proposal, provider)
    need(_same_json(_normalized_result(result), _normalized_result(first["toolResult"])), "lifecycle.continuation_result",
         "The originating turn received another result than the original receipt")
    repeat = _sse(root, refs["repeatDelivery"], o["sessionId"])
    error = repeat[-1].get("error") or {}
    need(repeat[-1]["kind"] == "error" and error.get("code") == "agent.message.no_pending"
         and not any(value["kind"] in ("started", "messageDelta", "contributedResult", "proposalCreated") for value in repeat),
         "lifecycle.repeat_delivery", "A repeated delivery was admitted as another continuation")
    _stored_once(_session(root, refs["session"], o["sessionId"])["messages"], first["toolResult"])
    after = _size_owner(root, refs["ownerAfter"], witness)
    need(after == applied, "lifecycle.duplicate_effect", "A repeated delivery changed the owner again")
    outcome = {"execute": "executed", "repeatExecute": "retained", "repeatDelivery": error["code"], "effects": 1}
    return _record(root, provider, "duplicate-effect-delivery", outcome, _correlation(provider, o, turn, proposal),
                   refs["ownerBefore"], refs["ownerAfter"], list(refs.values()))


BUSY_PHASES = (("busyReview", "review"), ("busyApproved", "approved"), ("busyContinuation", "receipt-continuation"))


def verify_busy(root, o, witness, provider):
    refs = o["refs"]
    created, _, before = _verify_open(root, o, witness)
    turn, proposal = _verify_pause(root, o, witness, before)
    session = o["sessionId"]
    body = {"autonomyMode": "full-auto", "expectedRevision": created["revision"]}
    phases = []
    for name, phase in BUSY_PHASES:
        value, record = _exchange(root, refs[name], "PUT", _session_path(session) + "/autonomy", 409)
        need(same(record.get("requestBody"), body) and (value.get("error") or {}).get("code") == "agent.session.busy" and value.get("data") is None,
             "lifecycle.busy_missing", "Retained " + phase + " work did not block the policy change as busy")
        need(same(_session(root, refs[name + "Detail"], session).get("policy"), created), "lifecycle.policy_relabelled",
             "A refused policy change altered the acknowledged policy")
        phases.append(phase)
    _exchange(root, refs["approve"], "POST", _proposal_path(proposal["id"], "approve"), 200)
    executed = _executed(root, refs["execute"], proposal, before, witness)
    _verify_continuation(root, refs["continuation"], session, turn)
    result = _provider(root, o, witness, proposal, provider)
    need(_same_json(_normalized_result(result), _normalized_result(executed["toolResult"])), "lifecycle.continuation_result",
         "The originating turn received another result than the original receipt")
    settled = _session(root, refs["settled"], session)
    need(all(str(message.get("status", "")).lower() == "completed" for message in settled["messages"] if str(message.get("role", "")).lower() == "user"),
         "lifecycle.busy_not_released", "The receipt continuation did not settle before the policy change")
    acknowledged, record = _exchange(root, refs["acknowledge"], "PUT", _session_path(session) + "/autonomy", 200)
    policy = acknowledged.get("data") or {}
    need(same(record.get("requestBody"), body) and policy.get("autonomyMode") == "full-auto" and policy.get("revision") == created["revision"] + 1
         and policy.get("maxAutonomyMode") == created.get("maxAutonomyMode") and same(policy.get("allowedAutonomyModes"), created.get("allowedAutonomyModes")),
         "lifecycle.busy_not_released", "The settled conversation did not acknowledge the policy change")
    final = _session(root, refs["session"], session)
    need(same(final.get("policy"), policy) and final["messages"]
         and all(message.get("acceptedPolicyRevision") == created["revision"] for message in final["messages"]),
         "lifecycle.policy_relabelled", "The later policy relabelled work accepted under the earlier revision")
    _stored_once(final["messages"], executed["toolResult"])
    after = _size_owner(root, refs["ownerAfter"], witness)
    need(after["size"] == o["targetSize"] and after["revision"] == executed["toolResult"]["receipt"]["after"]["revision"],
         "lifecycle.duplicate_effect", "The owner does not hold exactly the one original receipt")
    outcome = {"busy": phases, "busyCode": "agent.session.busy", "settled": policy["autonomyMode"], "effects": 1}
    return _record(root, provider, "busy-policy-lifecycle", outcome, _correlation(provider, o, turn, proposal),
                   refs["ownerBefore"], refs["ownerAfter"], list(refs.values()))


VERIFIERS = {key: (lambda key: lambda root, o, witness, provider: _refusal_case(root, o, witness, provider, key))(key) for key in REVIEW_STATUSES}
VERIFIERS.update({"cancel-before-effect": verify_cancel, "duplicate-effect-delivery": verify_duplicate, "busy-policy-lifecycle": verify_busy})


# --- Python executors: real native operations, retained before any assertion ---------------

def _open(runtime, name, *, autonomy="manual", http=None, drain=True, before_message=None):
    w = runtime.witness
    refs = {}
    state, _, _ = runtime.state(name + "-context-state", http=http)
    size, refs["ownerBefore"], _ = runtime.snapshot(name + "-size-owner-before", http=http, endpoint=w["sizeOwnerEndpoint"])
    target = "small" if size.get("size") == "medium" else "medium"
    t = runtime.transport(name, http=http)
    offset = t.trace_file.stat().st_size if t.trace_file.exists() else 0
    created = t.create_session({"conversationId": str(uuid.uuid4()), "mode": "build", "activeSurface": {"route": w["route"]}, "autonomyMode": autonomy})
    refs["create"] = created.receipt
    data = created.require_status(200).json()["data"]
    session = data["sessionId"]
    command_id = str(uuid.uuid4())
    if before_message: refs["gate"] = before_message(t, command_id)
    t.set_command(command_id, w["sizeTool"], {"size": target})
    command = t.output / "selected-command.json"
    private_json(command, {"commandId": command_id, "toolName": w["sizeTool"], "arguments": {"size": target}})
    refs["command"] = retained(command)
    message = t.message(session, {"content": "Owned lifecycle qualification request", "mode": "build", "requestId": str(uuid.uuid4()),
                                  "requestedCapabilities": [w["sizeCapability"]], "contextAttachments": [runtime.attachment(state)]})
    refs["message"] = message.receipt
    accepted = message.require_status(200).json()["data"]["message"]
    o = {"provider": runtime.provider, "sessionId": session, "commandId": command_id, "targetSize": target, "autonomy": autonomy,
         "createdRevision": (data.get("policy") or {}).get("revision"), "messageId": accepted.get("id"), "traceOffset": offset, "refs": refs}
    proposal = None
    if drain:
        stream = t.collect_turn(session)
        refs["initialStream"] = stream.receipt
        proposals = [frame.value.get("payload") for frame in stream.frames if frame.value.get("kind") == "proposalCreated"]
        need(len(proposals) == 1 and isinstance(proposals[0], dict) and proposals[0].get("id"), "lifecycle.review_missing",
             "The size prompt did not pause at one native review")
        proposal = proposals[0]
    return t, o, proposal


def _finish(runtime, t, o, *, http=None):
    trace = t.protocol_trace(o["traceOffset"])
    o["refs"]["trace"] = trace["receipt"]
    o["refs"]["session"] = t.session(o["sessionId"]).receipt
    _, o["refs"]["ownerAfter"], _ = runtime.snapshot("lifecycle-size-owner-after", http=http, endpoint=runtime.witness["sizeOwnerEndpoint"])
    return o


def _settle(t, o):
    """Claims release when the accepted prompt is terminal; read the owner, never retry the work."""
    for _ in range(SETTLE_POLLS):
        detail = t.session(o["sessionId"])
        messages = (detail.json() or {}).get("data", {}).get("messages", []) if detail.status == 200 else []
        if messages and all(str(row.get("status", "")).lower() == "completed" for row in messages if str(row.get("role", "")).lower() == "user"):
            break
        time.sleep(0.25)
    return detail.receipt


def _approve(t, o, proposal):
    o["refs"]["approve"] = t.approve(proposal["id"], revision=proposal["baseRevision"]).receipt


def _run_denied(runtime, name):
    t, o, proposal = _open(runtime, name)
    o["refs"]["deny"] = t.deny(proposal["id"]).receipt
    o["refs"]["closedExecute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    o["refs"]["continuation"] = t.collect_turn(o["sessionId"]).receipt
    return _finish(runtime, t, o)


def _run_stale(runtime, name):
    t, o, proposal = _open(runtime, name)
    _approve(t, o, proposal)
    # Reuses the original stale-revision control: an independent native CAS write after review.
    document, _, _ = runtime.snapshot(name + "-current-layout")
    changed = copy.deepcopy(document["value"]); changed["independentEdit"] = secrets.token_hex(16)
    _, write = runtime.native_write(name + "-independent-native-edit", runtime.backend, changed, revision=document["revision"])
    o["refs"]["independentEdit"] = write["receipt"]
    _, o["refs"]["ownerBeforeExecution"], _ = runtime.snapshot(name + "-size-owner-before-execution", endpoint=runtime.witness["sizeOwnerEndpoint"])
    o["refs"]["execute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    o["refs"]["continuation"] = t.collect_turn(o["sessionId"]).receipt
    return _finish(runtime, t, o)


def _run_authority(runtime, name):
    owner, _ = runtime.sign_in(0)
    t, o, proposal = _open(runtime, name, http=owner)
    _approve(t, o, proposal)
    try:
        # Reuses the original write-denied control; read/disclosure authority is retained.
        o["refs"]["authority"] = runtime.set_grants(0, "without-write")[2]
        current = runtime.clients[0]
        fresh = runtime.transport(name + "-current-authority", http=current)
        o["refs"]["execute"] = fresh.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
        _, o["refs"]["ownerWithheld"], _ = runtime.snapshot(name + "-size-owner-current-authority", http=current,
                                                            endpoint=runtime.witness["sizeOwnerEndpoint"])
        o["refs"]["continuation"] = fresh.collect_turn(o["sessionId"]).receipt
        o["refs"]["session"] = fresh.session(o["sessionId"]).receipt
        o["refs"]["trace"] = fresh.protocol_trace(o["traceOffset"])["receipt"]
    finally:
        o["refs"]["restored"] = runtime.set_grants(0, "full")[2]
    _, o["refs"]["ownerAfter"], _ = runtime.snapshot(name + "-size-owner-restored", http=runtime.clients[0], endpoint=runtime.witness["sizeOwnerEndpoint"])
    return o


def _run_cancel(runtime, name):
    epoch = str(uuid.uuid4())
    def arm(t, command_id):
        gate = {"commandId": command_id, "epoch": epoch, "state": "hold", "timeoutMilliseconds": 60000}
        atomic_json(runtime.gate_file, gate)
        path = t.output / "gate.json"
        private_json(path, gate)
        return retained(path)
    t, o, _ = _open(runtime, name, drain=False, before_message=arm)
    try:
        with t.stream(o["sessionId"]) as stream:
            started = stream.until(lambda frame: frame.value["kind"] == "started")
            turn = started.value["payload"]["turnId"]
            deadline = time.monotonic() + 30
            while not any(row["kind"] == "protocol.gate.waiting" and row["data"].get("commandId") == o["commandId"]
                          for row in t.protocol_trace(o["traceOffset"])["records"]):
                need(time.monotonic() < deadline, "lifecycle.gate", "The actual adapter never reached its pre-call gate")
                time.sleep(0.05)
            o["refs"]["cancel"] = t.cancel(o["sessionId"], turn).receipt
            stream.until_turn_end(turn_id=turn)
        o["refs"]["initialStream"] = stream.receipt
    finally:
        atomic_json(runtime.gate_file, {"commandId": "not-an-active-command", "epoch": str(uuid.uuid4()), "state": "release"})
    return _finish(runtime, t, o)


def _run_duplicate(runtime, name):
    t, o, proposal = _open(runtime, name)
    _approve(t, o, proposal)
    o["refs"]["execute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    _, o["refs"]["ownerAfterExecute"], _ = runtime.snapshot(name + "-size-owner-after-execute", endpoint=runtime.witness["sizeOwnerEndpoint"])
    o["refs"]["repeatExecute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    o["refs"]["continuation"] = t.collect_turn(o["sessionId"]).receipt
    o["refs"]["settled"] = _settle(t, o)
    o["refs"]["repeatDelivery"] = t.collect_turn(o["sessionId"]).receipt
    o["refs"]["lateExecute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    return _finish(runtime, t, o)


def _run_busy(runtime, name):
    t, o, proposal = _open(runtime, name)
    body = {"autonomyMode": "full-auto", "expectedRevision": o["createdRevision"]}
    def attempt(name):
        o["refs"][name] = t.update_autonomy(o["sessionId"], body).receipt
        o["refs"][name + "Detail"] = t.session(o["sessionId"]).receipt
    attempt("busyReview")
    _approve(t, o, proposal)
    attempt("busyApproved")
    o["refs"]["execute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    attempt("busyContinuation")
    o["refs"]["continuation"] = t.collect_turn(o["sessionId"]).receipt
    o["refs"]["settled"] = _settle(t, o)
    o["refs"]["acknowledge"] = t.update_autonomy(o["sessionId"], body).receipt
    return _finish(runtime, t, o)


EXECUTORS = {"denied-result-continuation": _run_denied, "stale-target-continuation": _run_stale,
             "authority-refusal-continuation": _run_authority, "cancel-before-effect": _run_cancel,
             "duplicate-effect-delivery": _run_duplicate, "busy-policy-lifecycle": _run_busy}
check(set(EXECUTORS) | set(BROWSER_CASES) == set(EXPECTED_OUTCOMES) and set(VERIFIERS) == set(EXECUTORS),
      "Every lifecycle case needs exactly one executor and verifier")


# --- browser cases: the original journeys, with the mode selected before the conversation ----

def _browser_json(output, name):
    path = Path(output) / (name + ".json")
    check(path.is_file(), "The browser did not retain " + name)
    return strict_json(path.read_text())


def _browser_owner(output, name):
    value = _browser_json(output, name)
    check(isinstance(value, dict) and value.get("status") == 200 and isinstance(value.get("value"), dict), "A real successful native owner GET is required: " + name)
    return value["value"]


def _browser_requests(output, session):
    requests = _browser_json(output, "agent-requests")
    check(isinstance(requests, list), "Native requests were not retained")
    counts = {}
    for row in requests:
        path = urlparse(row.get("url", "")).path
        kind = ("create" if path == "/_elsa/agent/sessions" else "message" if path == _session_path(session) + "/messages"
                else "autonomy" if path.endswith("/autonomy") else path.rsplit("/", 1)[-1])
        counts[kind] = counts.get(kind, 0) + 1
    need(not counts.get("autonomy") and not counts.get("deny") and counts.get("create") == 1, "lifecycle.native_requests",
         "The journey changed policy, denied or created another conversation")
    return requests, counts


def _browser_session(output, observation, provider, journey, key):
    check(isinstance(observation, dict) and observation.get("provider") == provider and observation.get("journey") == journey
          and not observation.get("failure") and not observation.get("pageErrors"), "An unfinished or failed browser journey cannot qualify " + key)
    life = observation.get("lifecycle") or {}
    session = life.get("sessionId")
    check(life.get("caseKey") == key and isinstance(session, str) and session, "The journey must retain its declared lifecycle conversation")
    create = _browser_json(output, "autonomy-create-session")
    data = (create.get("response") or {}).get("data") or {}
    mode = BROWSER_CASES[key][2]
    need(create.get("status") == 200 and data.get("sessionId") == session and same(data.get("policy"), life.get("createdPolicy"))
         and (mode is None or (create.get("request") or {}).get("autonomyMode") == mode and (data.get("policy") or {}).get("autonomyMode") == mode),
         "lifecycle.session_policy", "The conversation did not acknowledge its selected mode")
    detail = _browser_owner(output, "lifecycle-session-after").get("data") or {}
    need(detail.get("sessionId") == session and same(detail.get("policy"), data.get("policy")), "lifecycle.policy_relabelled",
         "The conversation's acknowledged policy changed during the journey")
    return session, data["policy"], detail


def _reviewed(frames, tool, count):
    proposals = [row.get("payload") for row in frames if row.get("kind") == "proposalCreated"
                 and ((row.get("payload") or {}).get("preparedInvocation") or {}).get("toolName") == tool]
    need(len(proposals) == count and all(proposal.get("requiresApproval") is True and proposal["preparedInvocation"].get("requiresApproval") is True
                                         and sum(row.get("kind") == "toolApprovalRequested" and row.get("proposalId") == proposal.get("id") for row in frames) == 1
                                         for proposal in proposals),
         "lifecycle.review_bypassed", "The operation did not stop at its own native review")
    return proposals


def _accepted_under(output, name, session, policy):
    response = _browser_json(output, name).get("response") or {}
    _public_policy_only(response, "The accepted prompt disclosed private policy")
    message = (response.get("data") or {}).get("message") or {}
    # The accepted message carries exactly the acknowledged public policy: mode, ceiling, allowed modes and revision.
    need(message.get("sessionId") == session and same(message.get("acceptedPolicy"), policy),
         "lifecycle.message", "The reviewed prompt was not accepted under the acknowledged mode")


def _browser_correlation(provider, session, proposal):
    prepared = proposal["preparedInvocation"]
    return {"adapter": provider, "sessionId": session, "proposalId": proposal["id"], "invocationId": prepared.get("invocationId"),
            "toolCallId": prepared.get("toolCallId")}


def verify_visibility_review(output, observation, witness, provider, backend_origin):
    """FullAuto still reviews the original visibility operation; the owner waits for application."""
    from autonomy_journeys import _frames
    key = "visibility-review-control"
    check(observation.get("status") == "passed", "The visibility journey did not complete")
    session, policy, detail = _browser_session(output, observation, provider, "resource", key)
    _accepted_under(output, "resource-message", session, policy)
    frames, sse = _frames(output, session, backend_origin)
    proposal = _reviewed(frames, witness["writeTool"], 1)[0]
    before, review, approved, after = (_browser_owner(output, name) for name in ("owner-before", "owner-at-review", "owner-after-approval", "owner-after"))
    need(review == before and approved == before, "lifecycle.review_bypassed", "The owner changed before approved application")
    execute = _browser_json(output, "resource-execute")
    data = (execute.get("response") or {}).get("data") or {}
    receipt = (data.get("toolResult") or {}).get("receipt") or {}
    need(execute.get("status") == 200 and data.get("executed") is True and data.get("proposalId") == proposal["id"]
         and after.get("visible") is not before.get("visible") and after.get("revision") != before.get("revision")
         and (receipt.get("before") or {}).get("revision") == before["revision"] and (receipt.get("after") or {}).get("revision") == after["revision"],
         "lifecycle.effect_missing", "The approved application has no exact owner-confirmed receipt")
    _, counts = _browser_requests(output, session)
    need(counts.get("execute") == 1 and counts.get("approve") == 1, "lifecycle.duplicate_effect", "The reviewed operation was not applied exactly once")
    _stored_once(detail.get("messages"), data["toolResult"])
    outcome = {"policy": policy["autonomyMode"], "atDecision": "review", "application": data["status"], "effects": 1}
    return outcome, _browser_correlation(provider, session, proposal), [sse]


def verify_workflow_review(output, observation, witness, provider, backend_origin):
    """FullAuto still reviews the native client edit; validation, Undo, Save/reload and Publish boundaries hold."""
    from autonomy_journeys import _frames
    key = "workflow-review-control"
    check(observation.get("status") == "passed", "The workflow journey did not complete")
    session, policy, detail = _browser_session(output, observation, provider, "workflow", key)
    _accepted_under(output, "workflow-message", session, policy)
    frames, sse = _frames(output, session, backend_origin)
    proposals = _reviewed(frames, WORKFLOW_TOOL, 2)
    flags = observation.get("observations") or {}
    need(all(flags.get(name) is True for name in ("workingStateChanged", "noImplicitSave", "wholeUndo", "validated", "explicitSaveReadback")),
         "lifecycle.workflow_boundary", "Atomic validation, dirty working state, whole Undo or explicit Save/reload was not observed")
    for name in ("workflow-review", "workflow-save-review"):
        need(_browser_json(output, name).get("status") == "awaiting-approval", "lifecycle.review_bypassed", "The client edit was not held for review")
    results = []
    for name in ("workflow-ack", "workflow-save-ack"):
        ack = _browser_json(output, name)
        data = (ack.get("response") or {}).get("data") or {}
        need(ack.get("status") == 200 and data.get("executed") is True and ((data.get("toolResult") or {}).get("receipt") or {}).get("scope") == "clientWorkingState",
             "lifecycle.effect_missing", "An approved client edit has no admitted working-state receipt")
        results.append(data["toolResult"])
    execute = _browser_json(output, "workflow-execute")
    need(execute.get("status") == 202 and ((execute.get("response") or {}).get("data") or {}).get("executed") is False,
         "lifecycle.workflow_boundary", "The server applied the client edit instead of its originating editor")
    before, after = _browser_owner(output, "owner-before"), _browser_owner(output, "owner-after")
    need(_browser_owner(output, "owner-after-local-apply").get("draft") == before.get("draft")
         and _browser_owner(output, "owner-after-undo").get("draft") == before.get("draft") and after.get("draft") != before.get("draft"),
         "lifecycle.workflow_boundary", "Only the explicit Save may change the owning workflow draft")
    writes = _browser_json(output, "owning-write-attempts")
    need(isinstance(writes, list) and len(writes) == 1 and writes[0].get("method") == "PUT" and 200 <= _browser_json(output, "explicit-save").get("status", 0) < 300
         and (_browser_owner(output, "saved-draft-validations").get("errors") == []), "lifecycle.workflow_boundary",
         "The owning draft needs exactly one explicit validated Save")
    publish = _browser_json(output, "publish-attempts")
    need(publish == [], "lifecycle.workflow_boundary", "The journey crossed the explicit Publish boundary")
    _, counts = _browser_requests(output, session)
    need(counts.get("execute") == 2 and counts.get("acknowledgements") == 2 and counts.get("approve") == 2, "lifecycle.duplicate_effect",
         "Each reviewed client edit must be dispatched and confirmed exactly once")
    for result in results: _stored_once(detail.get("messages"), result)
    outcome = {"policy": policy["autonomyMode"], "atDecision": "review", "application": "executed", "explicitSave": True, "publishRequests": len(publish)}
    return outcome, _browser_correlation(provider, session, proposals[0]), [sse]


def verify_interrupted(output, observation, witness, provider, backend_origin):
    """Lost confirmation after the real client effect: truthful uncertainty, late receipt and no replay."""
    from autonomy_journeys import _frames
    key = "interrupted-after-effect"
    actual = {"stage": "client-confirmation-delivery", "diagnostic": "net::ERR_FAILED"}
    check(observation.get("status") == "failed" and observation.get("expectedFailureObserved") is True, "The interrupted journey did not observe its loss")
    need(same(observation.get("actual"), actual), "lifecycle.confirmation", "The interruption was not the lost confirmation delivery")
    session, _, detail = _browser_session(output, observation, provider, "lost-confirmation", key)
    flags = observation.get("observations") or {}
    need(all(flags.get(name) is True for name in ("responseLost", "uncertain", "sameAcknowledgement", "sameInvocation", "retainedResult", "wholeUndo", "noReplay")),
         "lifecycle.confirmation", "Truthful uncertainty, late confirmation or whole Undo was not observed")
    frames, sse = _frames(output, session, backend_origin)
    proposal = _reviewed(frames, WORKFLOW_TOOL, 1)[0]
    lost = _browser_json(output, "lost-confirmation-accepted-http")
    original = _browser_json(output, "lost-confirmation-original-ack")
    retry = _browser_json(output, "confirmation-retry")
    accepted = strict_json(lost.get("rawBody") or "null")
    data = (retry.get("response") or {}).get("data") or {}
    need(lost.get("status") == 200 and retry.get("status") == 200 and same(retry.get("response"), accepted) and same(retry.get("request"), original.get("request"))
         and data.get("executed") is True and ((data.get("toolResult") or {}).get("receipt") or {}).get("scope") == "clientWorkingState",
         "lifecycle.confirmation", "The late confirmation did not return the original accepted receipt")
    need(_browser_json(output, "lost-confirmation-uncertain-ui").get("status") == "uncertain", "lifecycle.confirmation",
         "The lost confirmation was not shown as uncertain")
    diagnostics = _browser_json(output, "browser-diagnostics")
    need(any(row.get("errorText") == actual["diagnostic"] and str(row.get("url", "")).endswith("/acknowledgements") for row in diagnostics.get("failedRequests", [])),
         "lifecycle.confirmation", "The accepted ACK response was not lost by the actual browser")
    requests, counts = _browser_requests(output, session)
    acks = [row.get("body") for row in requests if urlparse(row.get("url", "")).path.endswith("/acknowledgements")]
    need(counts.get("execute") == 1 and len(acks) == 2 and same(acks[0], acks[1]), "lifecycle.duplicate_effect",
         "The interrupted edit was replayed instead of reconfirmed")
    need(_browser_owner(output, "owner-after").get("draft") == _browser_owner(output, "owner-before").get("draft"), "lifecycle.workflow_boundary",
         "Confirmation changed the owning draft without an explicit Save")
    _stored_once(detail.get("messages"), data["toolResult"])
    outcome = {"lostDelivery": actual["diagnostic"], "uncertain": True, "lateConfirmation": data["status"], "replays": counts["execute"] - 1}
    return outcome, _browser_correlation(provider, session, proposal), [sse]


BROWSER_VERIFIERS = {"interrupted-after-effect": verify_interrupted, "visibility-review-control": verify_visibility_review,
                     "workflow-review-control": verify_workflow_review}


def verify_browser_case(root, output, observation, witness, provider, key, backend_origin, correlated):
    outcome, correlation, refs = BROWSER_VERIFIERS[key](output, observation, witness, provider, backend_origin)
    correlation["providerTrace"] = correlated["trace"]
    evidence = [*observation.get("evidence", []), *refs, correlated["receipt"], correlated["trace"]]
    return _record(root, provider, key, outcome, correlation, observation.get("ownerBefore"), observation.get("ownerAfter"), evidence)


def _browser_case(runtime, provider, key, name):
    journey, workflow, _ = BROWSER_CASES[key]
    runtime.sign_in()
    root = runtime.output(name)
    output = root / "observation"
    config = {"journey": journey, "lifecycleCase": key, "provider": provider, "studioOrigin": runtime.studio_origin,
              "backendOrigin": runtime.backend_origin, "storageState": runtime.active_identity["storageState"],
              "commandFile": str(runtime.command_file), "output": str(output)}
    if workflow: config["workflow"] = seed_workflow(runtime.backend, root / "workflow-seed")
    private_json(root / "config-private.json", config)
    transport = runtime.transport(name + "-protocol")
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    node = shutil.which("node")
    check(node, "The real browser lifecycle journey needs Node")
    try:
        runtime.run.run(name, [node, str(TOOL / "browser.mjs"), str(root / "config-private.json")], TOOL,
                        env={item: value for item, value in os.environ.items() if item in RUNTIME_ENVIRONMENT})
    except RuntimeError as error:
        observed = output / "observation.json"
        value = strict_json(observed.read_text()) if observed.is_file() else {}
        failure = value.get("failure") if isinstance(value, dict) else None
        detail = failure.get("message") if isinstance(failure, dict) and isinstance(failure.get("message"), str) else "no browser failure retained"
        raise LifecycleCaseFailure(str(error) + "; browser: " + detail[:500], "browser.failed", retained(observed) if observed.is_file() else None) from None
    observation = strict_json((output / "observation.json").read_text())
    correlated = correlate_browser_protocol(transport, output, provider, offset=offset)
    record = verify_browser_case(runtime.run.root, output, observation, runtime.witness, provider, key, transport.http.base, correlated)
    record.update(observation=retained(output / "observation.json"),
                  inputs=runtime.save(root, "browser-inputs", {**browser_inputs(config), "lifecycleCase": key}))
    return record


def execute_lifecycle_case(runtime, provider, key):
    """Run one non-mode case once on the selected actual adapter; a failure keeps its precise cause."""
    check(provider == runtime.provider and key in EXPECTED_OUTCOMES, "Select the actual native adapter before its declared lifecycle case")
    runtime.run.assert_immutable_inputs()
    runtime.sign_in()
    name = "lifecycle-" + provider + "-" + key
    observation = None
    try:
        if key in BROWSER_CASES:
            record = _browser_case(runtime, provider, key, name)
        else:
            value = EXECUTORS[key](runtime, name)
            observation = runtime.save(runtime.output(name + "-observation"), "observation", value)
            record = VERIFIERS[key](runtime.run.root, value, runtime.witness, provider)
            record["observation"] = observation
    except LifecycleCaseFailure as error:
        error.observation = error.observation or observation
        raise
    except AgentTransportError as error:
        raise LifecycleCaseFailure(str(error), "lifecycle.native_transport", error.receipt or observation) from None
    record.update(source=runtime.run.source)
    runtime.run.assert_immutable_inputs()
    return record
