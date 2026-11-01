"""Same-session native mode qualification and the final, exact lifecycle manifest.

The four mode cases per actual adapter (T012) require an actual acknowledged
policy update, authoritative readback and accepted-policy attribution. They are
not a complete package witness. Provider and owning-resource proof comes from
retained native bytes, never a selector label, provider text, synthetic result
or inferred successful write.
"""
from __future__ import annotations

import copy
from decimal import Decimal
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
from urllib.parse import quote, urlparse

from agent_transport import _json as strict_json
from artifacts import TOOL, check, sha
from case_records import retained, correlate_browser_protocol, _normalized_result
from disclosure_journeys import require_stored_result
from journey_runtime import browser_inputs, same
from runtime import RUNTIME_ENVIRONMENT, private_json


PROVIDERS = ("copilot", "anthropic")
MODE_CASES = {
    "automatic-size-control": ("full-auto", "full-auto"),
    "reviewed-size-control": ("manual", "manual"),
    "automatic-to-manual": ("full-auto", "manual"),
    "manual-to-automatic": ("manual", "full-auto"),
}
LIFECYCLE_CASES = (*MODE_CASES, "denied-result-continuation", "duplicate-effect-delivery",
    "cancel-before-effect", "interrupted-after-effect", "stale-target-continuation",
    "authority-refusal-continuation", "visibility-review-control", "workflow-review-control", "busy-policy-lifecycle")
EXPECTED_CASES = frozenset(f"{provider}-{key}" for provider in PROVIDERS for key in LIFECYCLE_CASES)
MODE_CASE_IDS = frozenset(f"{provider}-{key}" for provider in PROVIDERS for key in MODE_CASES)
EVIDENCE_CATEGORY = "deterministic-provider-protocol"
MODE_QUALIFICATION = "autonomyModeQualification"
MATRIX_QUALIFICATION = "autonomyLifecycleQualification"
MODE_LABELS = {"manual": "Manual review", "full-auto": "Autopilot"}
# Accepted messages carry the domain policy, serialized with the API's camelCase enum names.
ACCEPTED_MODES = {"manual": "manual", "autoReadOnly": "auto-read-only", "fullAuto": "full-auto"}
POLICY_FIELDS = {"autonomyMode", "maxAutonomyMode", "allowedAutonomyModes", "revision"}


class ModeCaseFailure(RuntimeError):
    """A browser journey that ended with its own precise, retained cause."""
    def __init__(self, message, cause, observation=None):
        super().__init__(message)
        self.cause, self.observation = cause, observation


def require_complete_autonomy(section):
    """Mandatory final gate; the eight mode observations alone can never satisfy it."""
    check(isinstance(section, dict) and section.get("evidenceCategory") == EVIDENCE_CATEGORY
          and section.get("liveProvider") is False and section.get("status") == "passed",
          "Final autonomy qualification needs explicit complete native evidence")
    records = section.get("cases")
    check(isinstance(records, list) and len(records) == len(EXPECTED_CASES)
          and {row.get("id") for row in records} == EXPECTED_CASES,
          "All thirteen lifecycle cases per actual adapter are mandatory exactly once")
    check(all(row.get("status") == "passed" and row.get("nativeEvidenceVerified") is True
              and row.get("provider") in PROVIDERS and row.get("caseKey") in LIFECYCLE_CASES
              and row["id"] == row["provider"] + "-" + row["caseKey"]
              and not row.get("modeMismatch") and row.get("evidence") for row in records),
          "Incomplete, failed, wrong-adapter or diagnostic-only cases cannot qualify")
    return records


def require_mode_qualification(records):
    """T012: all four mode cases on each actual adapter passed with acknowledged policy evidence."""
    check(isinstance(records, list) and len(records) == len(MODE_CASE_IDS) and {row.get("id") for row in records} == MODE_CASE_IDS,
          "Mode qualification needs exactly four mode cases per actual adapter")
    for row in records:
        check(row.get("provider") in PROVIDERS and row.get("caseKey") in MODE_CASES
              and row["id"] == row["provider"] + "-" + row["caseKey"], "A mode case must name its exact actual adapter")
        initial, requested = MODE_CASES[row["caseKey"]]
        policy = row.get("policy")
        check(row.get("status") == "passed" and row.get("modeMismatch") is False and row.get("cause") is None
              and row.get("nativeEvidenceVerified") is True and row.get("qualified") is False and row.get("evidence")
              and isinstance(policy, dict) and policy.get("acknowledged") is (initial != requested)
              and policy.get("updateRequests") == (1 if initial != requested else 0),
              "Failed, mismatched, unacknowledged or unverified mode cases cannot pass mode qualification")
    return records


def _owned_ref(root, reference):
    check(isinstance(reference, dict) and isinstance(reference.get("path"), str), "Missing retained native evidence reference")
    path = Path(reference["path"])
    check(path.is_absolute() and path.resolve().is_relative_to(Path(root).resolve())
          and path.is_file() and sha(path) == reference.get("sha256"), "Missing, changed or foreign native evidence")
    return path


def _owner(root, reference):
    value = strict_json(_owned_ref(root, reference).read_text())
    check(isinstance(value, dict) and value.get("status") == 200 and isinstance(value.get("value"), dict),
          "A real successful native owner/session GET is required")
    return value["value"]


def _frames(output, session, backend_origin):
    path = Path(output) / "agent-streams.json"
    streams = strict_json(path.read_text())
    check(isinstance(streams, list) and streams, "Native SSE is mandatory")
    frames = []
    request_ids = set()
    expected_url = backend_origin + "/_elsa/agent/sessions/" + quote(session, safe="") + "/stream"
    for stream in streams:
        check(isinstance(stream, dict), "A native stream observation must be a structured CDP record")
        request, response = stream.get("requestObservation"), stream.get("responseObservation")
        check(isinstance(request, dict) and isinstance(response, dict) and isinstance(stream.get("requestId"), str) and stream["requestId"]
              and stream["requestId"] not in request_ids and request.get("source") == "Network.requestWillBeSent"
              and response.get("source") == "Network.responseReceived" and type(stream.get("requestObservationCount")) is int
              and stream["requestObservationCount"] == 1 and type(stream.get("responseObservationCount")) is int
              and stream["responseObservationCount"] == 1 and stream.get("requestIdentityMatchesResponse") is True
              and request.get("requestId") == response.get("requestId") == stream["requestId"]
              and request.get("url") == response.get("url") == stream.get("url") == expected_url,
              "Native stream/preflight needs its actual matching CDP request/response identity and exact owned URL")
        request_ids.add(stream["requestId"])
        if request.get("method") == "OPTIONS":
            check(request.get("resourceType") in ("Other", "Preflight") and request.get("initiatorType") == "preflight"
                  and response.get("resourceType") == "Preflight"
                  and stream.get("status") == 204 and stream.get("termination") == "loadingFinished"
                  and isinstance(stream.get("contentType"), str) and stream["contentType"].split(";", 1)[0].strip() != "text/event-stream"
                  and stream.get("rawBody") == "" and stream.get("captureMethod") == "not-sse"
                  and all(type(stream.get(key)) is int and stream[key] == 0 for key in
                          ("receivedDataBytes", "observedBodyBytes", "bufferedBytes", "completeBodyBytes", "streamedBytes"))
                  and type(stream.get("dataEventCount")) is int and stream["dataEventCount"] >= 0
                  and stream.get("responseBodyAttempted") is False and not stream.get("errorText") and not stream.get("canceled")
                  and all(not stream.get(key) for key in ("captureError", "captureValidationError", "responseBodyError", "streamResourceContentError", "dataLengthMetadataError")),
                  "Only an observed empty successful same-session OPTIONS preflight may be ignored")
            continue
        check(request.get("method") == "GET" and request.get("resourceType") == response.get("resourceType") == "Fetch"
              and stream.get("status") == 200 and isinstance(stream.get("contentType"), str)
              and stream["contentType"].split(";", 1)[0].strip() == "text/event-stream"
              and all(not stream.get(key) for key in ("captureError", "captureValidationError", "responseBodyError", "dataLengthMetadataError"))
              and all(type(stream.get(key)) is int and stream[key] >= 0 for key in ("receivedDataBytes", "dataEventCount"))
              and isinstance(stream.get("rawBody"), str),
              "A wrong-session, failed or unretained native stream cannot prove a mode outcome")
        # Native UI closes its original stream at a review pause. Only complete frames count.
        complete = []
        for raw in re.split(r"\r?\n\r?\n", stream["rawBody"])[:-1]:
            data = "\n".join(line[5:].removeprefix(" ") for line in raw.splitlines() if line.startswith("data:"))
            if data:
                value = strict_json(data)
                check(isinstance(value, dict) and value.get("kind") != "error" and not value.get("error"),
                      "A native error cannot stand in for the session-mode mismatch")
                complete.append(value)
        if stream.get("termination") == "loadingFailed":
            last = complete[-1] if complete else {}
            check(stream.get("errorText") == "net::ERR_ABORTED" and stream.get("canceled") is True
                  and stream["rawBody"].endswith(("\n\n", "\r\n\r\n")) and stream.get("textTruncation") == "none"
                  and type(stream.get("trailingIncompleteUtf8Bytes")) is int and stream["trailingIncompleteUtf8Bytes"] == 0
                  and last.get("kind") == "toolApprovalRequested"
                  and isinstance(last.get("proposalId"), str) and last["proposalId"]
                  and any(row.get("kind") == "proposalCreated" and isinstance(row.get("payload"), dict)
                          and row["payload"].get("id") == last["proposalId"] for row in complete[:-1]),
                  "A failed GET is not a complete native approval-pause cancellation prefix")
        else:
            check(stream.get("termination") == "loadingFinished" and not stream.get("errorText") and not stream.get("canceled"),
                  "An open or failed native GET cannot prove the mode outcome")
        frames.extend(complete)
    check(frames and len({row.get("id") for row in frames}) == len(frames), "Native frames need distinct actual event identities")
    return frames, retained(path)


def _layout(output, name):
    # Parse the exact native body using Decimal so an unrelated precise JSON value
    # cannot compare equal merely because both observations rounded to a JS double.
    record = strict_json((Path(output) / (name + "-http.json")).read_text())
    check(record.get("status") == 200 and isinstance(record.get("rawBody"), str), "The native Home document is required")
    def pairs(items):
        value = {}
        for key, item in items:
            check(key not in value, "Ambiguous duplicate native document field")
            value[key] = item
        return value
    value = json.loads(record["rawBody"], parse_float=Decimal, object_pairs_hook=pairs,
                       parse_constant=lambda _: (_ for _ in ()).throw(ValueError("Nonfinite native value")))
    check(isinstance(value, dict) and value.get("namespace") == "home-layout"
          and type(value.get("schemaVersion")) is int and value["schemaVersion"] == 1
          and isinstance(value.get("revision"), str) and isinstance(value.get("value"), dict), "Wrong native Home document")
    return value


def _same_native_json(left, right):
    # Preserve JSON kinds as well as exact Decimal values: Python's True == 1
    # and False == 0 must not conceal an unrelated authored-value change.
    if isinstance(left, bool) or isinstance(right, bool):
        return type(left) is type(right) and left == right
    if isinstance(left, dict) or isinstance(right, dict):
        return isinstance(left, dict) and isinstance(right, dict) and left.keys() == right.keys() and all(
            _same_native_json(left[key], right[key]) for key in left)
    if isinstance(left, list) or isinstance(right, list):
        return isinstance(left, list) and isinstance(right, list) and len(left) == len(right) and all(
            _same_native_json(a, b) for a, b in zip(left, right))
    if isinstance(left, (int, Decimal)) or isinstance(right, (int, Decimal)):
        return isinstance(left, (int, Decimal)) and isinstance(right, (int, Decimal)) and left == right
    return type(left) is type(right) and left == right


def require_size_owner_journey(output, observation, witness):
    """Owning module evidence: precisely one personal card size differs, or none at review."""
    a = observation["autonomy"]
    before, after = _owner(output, observation["ownerBefore"]), _owner(output, observation["ownerAfter"])
    decision = _owner(output, a["ownerAtDecision"])
    fields = {"widgetId", "revision", "size", "hostId"}
    for owner in (before, decision, after):
        check(set(owner) == fields and owner["widgetId"] == witness["widgetId"] and owner["hostId"] == witness["hostId"]
              and owner["size"] in ("small", "medium") and isinstance(owner["revision"], str) and owner["revision"],
              "The native size owner must identify this exact supported personal card")
    check(a["targetSize"] in ("small", "medium") and a["targetSize"] != before["size"], "A no-op cannot establish application")
    old, at_decision, new = (_layout(output, name) for name in ("size-layout-before", "size-layout-at-decision", "size-layout-after"))
    for document, owner in ((old, before), (at_decision, decision), (new, after)):
        check(document["revision"] == owner["revision"], "The independent owner and full native document disagree")
        widgets = document["value"].get("widgets")
        check(isinstance(widgets, list), "Native widget collection is absent")
        matches = [widget for widget in widgets if widget.get("id") == witness["widgetId"]]
        check(len(matches) == 1 and matches[0].get("size") == owner["size"], "The owning native card size differs")
    expected = copy.deepcopy(old["value"])
    widget = next(row for row in expected["widgets"] if row["id"] == witness["widgetId"])
    if a["applied"]:
        widget["size"] = a["targetSize"]
        check(after["revision"] != before["revision"] and after["size"] == a["targetSize"], "No real changed revision/size was observed")
    else:
        check(after == before, "A pending review performed an effect")
    check(_same_native_json(new["value"], expected), "The size journey changed unrelated native Home JSON")
    if a["outcomeAtDecision"] == "review":
        check(decision == before and _same_native_json(at_decision, old), "The owner must remain unchanged at review")
    else:
        check(decision == after and _same_native_json(at_decision, new), "Automatic application was not observed at the original decision")
    return before, after


def _messages(output, a, witness, before):
    requests = strict_json((Path(output) / "agent-requests.json").read_text())
    check(isinstance(requests, list), "Native requests were not retained")
    creates = [row for row in requests if urlparse(row["url"]).path == "/_elsa/agent/sessions"]
    messages = [row for row in requests if urlparse(row["url"]).path.endswith("/messages")]
    check(len(creates) == 1 and creates[0].get("method") == "POST"
          and creates[0]["body"].get("autonomyMode") == a["initialMode"] and len(messages) == 2
          and all(row.get("method") == "POST" and urlparse(row["url"]).path == "/_elsa/agent/sessions/" + a["sameSessionId"] + "/messages"
                  for row in messages), "The mode journey created or submitted to another native conversation")
    bootstrap = strict_json((Path(output) / "autonomy-native-bootstrap.json").read_text())
    capabilities = bootstrap.get("value", {}).get("data", {}).get("capabilities")
    check(bootstrap.get("status") == 200 and isinstance(capabilities, list)
          and any(row.get("id") == witness["sizeCapability"] and row.get("moduleId") == witness["moduleId"] for row in capabilities),
          "The owning backend must actually advertise the new size capability")
    installed = {row.get("id") for row in capabilities if row.get("moduleId") == witness["moduleId"]}
    accepted, selected = [], []
    for index, (label, identity, request_id, tool) in enumerate((("autonomy-read-message", a["readMessageId"], a["readRequestId"], witness["readTool"]),
                                         ("autonomy-size-message", a["sizeMessageId"], a["sizeRequestId"], witness["sizeTool"]))):
        record = strict_json((Path(output) / (label + ".json")).read_text())
        value = record.get("response", {})
        message = value.get("data", {}).get("message", {})
        check(record.get("status") == 200 and value.get("error") is None and message.get("sessionId") == a["sameSessionId"]
              and message.get("id") == identity and identity and message.get("requestId") == request_id and request_id
              and str(message.get("role", "")).lower() == "user" and record.get("request", {}).get("requestId") == request_id,
              "The native accepted message/request/session correlation changed")
        request = record.get("request", {})
        requested = request.get("requestedCapabilities")
        check(same(messages[index]["body"], request) and isinstance(requested, list) and len(requested) == 1
              and requested[0] in installed and request.get("source", {}).get("moduleId") == witness["moduleId"]
              and request["source"].get("actionId") == tool,
              "The actual installed capability and module action source must admit each exact prompt")
        selected.append(requested)
        accepted.append(message)
    check(selected[0] == selected[1], "This baseline must preserve the current host's actual first-capability forwarding")
    check(a["readMessageId"] != a["sizeMessageId"] and a["readRequestId"] != a["sizeRequestId"], "Distinct prompts need distinct identities")
    refs = [row for row in accepted[1].get("context", []) if row.get("contentType") == witness["contextType"]]
    check(len(refs) == 1 and refs[0].get("moduleId") == witness["moduleId"]
          and refs[0].get("contributorId") == "acme.home.widget-context", "Accepted size context lost its owning source provenance")
    target = refs[0].get("target", {})
    check(target.get("moduleId") == witness["moduleId"] and target.get("resourceType") == witness["resourceType"]
          and target.get("resourceId") == witness["widgetId"] and target.get("revision") == before["revision"],
          "The accepted context is not bound to the independently read original resource")
    session = _owner(output, a["sessionAfter"]).get("data", {})
    check(session.get("sessionId") == a["sameSessionId"] and all(any(row.get("id") == item["id"] for row in session.get("messages", []))
          for item in accepted), "The native session lost its original accepted messages")
    return requests, session


def _commands(output, a, witness):
    commands = [strict_json(path.read_text()) for path in Path(output).glob("command-*.json")]
    check(len(commands) == 2 and {row.get("commandId") for row in commands} == {a["readCommandId"], a["sizeCommandId"]},
          "Exactly the original read and size commands are required")
    selected = {row["commandId"]: row for row in commands}
    check(selected[a["readCommandId"]] == {"commandId": a["readCommandId"], "toolName": witness["readTool"], "arguments": {}}
          and selected[a["sizeCommandId"]] == {"commandId": a["sizeCommandId"], "toolName": witness["sizeTool"], "arguments": {"size": a["targetSize"]}},
          "Actual protocol command/input changed")
    return selected


def _actual_calls(trace, commands, provider, session):
    model_rows = [row for row in trace["records"] if row.get("kind") in {provider + "." + key for key in ("request", "call", "return", "continuation")}]
    check(model_rows and all(row.get("data", {}).get("commandId") in commands for row in model_rows), "A foreign command reached the actual provider")
    grouped = {}
    for identity, command in commands.items():
        rows = [row for row in model_rows if row["data"]["commandId"] == identity]
        calls = [row["data"] for row in rows if row["kind"] == provider + ".call"]
        requests = [row["data"] for row in rows if row["kind"] == provider + ".request"]
        check(len(calls) == 1 and calls[0].get("toolName") == command["toolName"] and same(calls[0].get("arguments"), command["arguments"])
              and requests and sum(tool.get("name") == command["toolName"] for tool in requests[0].get("tools", [])) == 1,
              "The actual adapter must advertise and call the exact commanded function once")
        if provider == "copilot":
            check(all(row.get("sessionId") == session for row in requests), "The actual SDK resumed another native session")
        grouped[identity] = rows
    return grouped


def _proposal(frames, a, witness, before):
    proposals = [row.get("payload") for row in frames if row.get("kind") == "proposalCreated"]
    check(len(proposals) == 1 and isinstance(proposals[0], dict), "A real single pending native proposal is required")
    proposal = proposals[0]
    prepared = proposal.get("preparedInvocation", {})
    target = prepared.get("target", {})
    check(proposal.get("sessionId") == a["sameSessionId"] and proposal.get("id") and proposal.get("requiresApproval") is True
          and str(proposal.get("status", "")).replace("-", "").lower() == "awaitingapproval"
          and prepared.get("toolName") == witness["sizeTool"] and prepared.get("moduleId") == witness["moduleId"]
          and prepared.get("requiresApproval") is True and prepared.get("arguments") == {"size": a["targetSize"]}
          and target.get("moduleId") == witness["moduleId"] and target.get("resourceId") == witness["widgetId"]
          and target.get("resourceType") == witness["resourceType"] and target.get("revision") == before["revision"],
          "The pending review is not the original prepared personal size operation")
    check(sum(row.get("kind") == "toolApprovalRequested" and row.get("proposalId") == proposal["id"] for row in frames) == 1,
          "The native turn did not stop at this exact approval boundary")
    return proposal


def _pending_size_returns(rows, provider, proposal):
    """A reviewed call's SDK return describes the pause; it never claims application."""
    returns = [row["data"].get("result") for row in rows if row["kind"] == provider + ".return"]
    check(len(returns) <= (1 if provider == "copilot" else 0), "Duplicate or unexpected pending function returns")
    for value in returns:
        check(isinstance(value, dict) and value.get("status") == "awaiting-approval" and value.get("executed") is False
              and value.get("proposalId") == proposal["id"] and not value.get("receipt")
              and same(value.get("preview"), proposal["preparedInvocation"].get("preview")), "A pending SDK return claimed application")


def _reviewed_effect(output, requests, frames, a, witness, before, inline_results, backend_origin):
    """Reviewed server application is carried by execute HTTP, then journal/continuation.

    The original proposal remains the review binding. A duplicate SSE result is
    optional corroboration, never a substitute for missing or corrupt HTTP proof.
    """
    proposal = _proposal(frames, a, witness, before)
    prepared = proposal["preparedInvocation"]
    check(proposal.get("baseRevision") == before["revision"] and prepared.get("sessionId") == a["sameSessionId"]
          and prepared.get("toolContractVersion") == witness["contractVersion"]
          and prepared.get("executionLocation") == "server" and prepared.get("client") is None
          and prepared.get("disclosureSourceId") == "acme.home.widget-context"
          and all(isinstance(prepared.get(key), str) and prepared[key] for key in ("invocationId", "turnId", "toolCallId", "actorId", "tenantId", "approvalDigest")),
          "The original reviewed invocation binding is incomplete")
    proposal_index = next(index for index, row in enumerate(frames) if row.get("kind") == "proposalCreated")
    check(any(row.get("kind") == "started" and isinstance(row.get("payload"), dict)
              and row["payload"].get("turnId") == prepared["turnId"] for row in frames[:proposal_index]),
          "The reviewed operation is not bound to the actual originating native turn")
    preview = {"typeId": witness["sizeResultType"], "schemaVersion": "1", "data": {"widgetId": witness["widgetId"],
        "revision": before["revision"], "size": a["targetSize"], "beforeSize": before["size"], "hostId": witness["hostId"]}}
    check(same(prepared.get("preview"), preview), "The original reviewed preview does not identify the requested size change")
    create = next(row for row in requests if urlparse(row["url"]).path == "/_elsa/agent/sessions")
    backend = urlparse(create["url"])
    check(create["url"] == backend_origin + "/_elsa/agent/sessions", "The reviewed session must use the actual authenticated backend origin")
    expected_url = backend.scheme + "://" + backend.netloc + "/_elsa/agent/proposals/" + quote(proposal["id"], safe="") + "/execute"
    effects = [row for row in requests if urlparse(row["url"]).path.endswith(("/execute", "/acknowledgements"))]
    check(len(effects) == 1 and effects[0].get("method") == "POST" and effects[0].get("url") == expected_url,
          "Exactly one original same-backend server execute request is required")
    structured_path, raw_path = Path(output) / "autonomy-size-execute.json", Path(output) / "autonomy-size-execute-http.json"
    check(structured_path.is_file() and raw_path.is_file(), "The original native execute HTTP response was not retained")
    response, raw = strict_json(structured_path.read_text()), strict_json(raw_path.read_text())
    check(isinstance(response, dict) and isinstance(raw, dict), "Native execution proof must contain the original HTTP envelopes")
    envelope = response.get("response", {})
    data = envelope.get("data", {}) if isinstance(envelope, dict) else {}
    check(isinstance(envelope, dict) and isinstance(data, dict), "Native execution response data is absent or malformed")
    check(response.get("url") == raw.get("url") == expected_url and response.get("status") == raw.get("status") == 200
          and isinstance(raw.get("rawBody"), str) and same(strict_json(raw["rawBody"]), envelope)
          and same(response.get("request"), effects[0].get("body"))
          and response.get("request", {}).get("revision") == before["revision"]
          and envelope.get("error") is None and data.get("executed") is True and data.get("status") == "executed"
          and data.get("proposalId") == proposal["id"] and data.get("resourceType") == witness["resourceType"]
          and data.get("resourceId") == witness["widgetId"] and data.get("clientInvocation") is None
          and isinstance(data.get("toolResult"), dict),
          "The native execute response is failed, foreign, stale or differs from its exact original request/bytes")
    accepted = []
    for path in Path(output).glob("*.json"):
        value = strict_json(path.read_text())
        payload = value.get("response") if isinstance(value, dict) else None
        if isinstance(payload, dict) and isinstance(payload.get("data"), dict) and isinstance(payload["data"].get("toolResult"), dict):
            accepted.append(path)
    check(accepted == [structured_path], "Duplicate or competing native execution-response proof is not one original application")
    result = data["toolResult"]
    canonical = _normalized_result(result)
    receipt, access = canonical.get("receipt", {}), canonical.get("access", {})
    check(isinstance(receipt, dict) and isinstance(access, dict) and result.get("toolName") == prepared["toolName"] and result.get("toolCallId") == prepared["toolCallId"]
          and receipt.get("invocationId") == prepared["invocationId"] and access.get("invocationId") == prepared["invocationId"]
          and access.get("contractVersion") == prepared["toolContractVersion"]
          and same(receipt.get("before"), prepared["target"]) and same(access.get("target"), prepared["target"]),
          "The accepted effect does not retain its exact original prepared invocation/call/resource binding")
    check(len(inline_results) <= 1 and all(same(_normalized_result(value), canonical) for value in inline_results),
          "Optional mutation SSE conflicts with the actual accepted reviewed HTTP result")
    return result


def _effect(output, requests, frames, a, witness, before, after, session, backend_origin):
    results = [row.get("payload") for row in frames if row.get("kind") == "contributedResult" and row.get("resultKind") == "moduleResult"
               and row.get("payload", {}).get("toolName") == witness["sizeTool"]]
    if a["outcomeAtDecision"] == "review":
        result = _reviewed_effect(output, requests, frames, a, witness, before, results, backend_origin)
    else:
        check(len(results) == 1 and isinstance(results[0], dict), "One original automatic native SSE result is required")
        result = results[0]
    check(result.get("succeeded") is True, "A refused outcome is not a confirmed size effect")
    canonical = _normalized_result(result)
    receipt, access = canonical.get("receipt"), canonical.get("access", {})
    payload = {"typeId": witness["sizeResultType"], "schemaVersion": "1", "data": {"widgetId": witness["widgetId"],
        "revision": after["revision"], "size": after["size"], "beforeSize": before["size"], "hostId": witness["hostId"]}}
    check(isinstance(receipt, dict) and receipt.get("scope") == 1 and receipt.get("invocationId")
          and same(canonical.get("payload"), payload) and same(receipt.get("evidence"), payload)
          and access.get("moduleId") == witness["moduleId"] and access.get("disclosureSourceId") == "acme.home.widget-context"
          and access.get("invocationId") == receipt["invocationId"], "A native receipt, exact size schema and original source identity are required")
    for name, owner in (("before", before), ("after", after)):
        target = receipt.get(name, {})
        check(target.get("moduleId") == witness["moduleId"] and target.get("resourceType") == witness["resourceType"]
              and target.get("resourceId") == witness["widgetId"] and target.get("revision") == owner["revision"], "Receipt target differs from independent native owner")
    require_stored_result(session.get("messages", []), result)
    return result


def _policy(value, message):
    check(isinstance(value, dict) and set(value) == POLICY_FIELDS and isinstance(value["autonomyMode"], str)
          and isinstance(value["maxAutonomyMode"], str) and isinstance(value["allowedAutonomyModes"], list)
          and all(isinstance(item, str) for item in value["allowedAutonomyModes"])
          and type(value["revision"]) is int and value["revision"] >= 0, message)
    return value


def _request_kind(row, session, backend_origin):
    url = urlparse(row.get("url", ""))
    check(backend_origin == url.scheme + "://" + url.netloc and not url.query and not url.fragment,
          "A native Agent request left the owned backend")
    base = "/_elsa/agent/sessions"
    exact = {(base, "POST"): "create", (base + "/" + quote(session, safe="") + "/messages", "POST"): "message",
             (base + "/" + quote(session, safe="") + "/autonomy", "PUT"): "autonomy"}
    kind = exact.get((url.path, row.get("method")))
    if kind is None and row.get("method") == "POST":
        match = re.fullmatch(r"/_elsa/agent/proposals/[^/]+/(approve|execute)", url.path)
        kind = match[1] if match else None
    check(kind is not None, "An unrecognized native Agent request is not part of this mode journey")
    return kind


def _accepted_policy(message, mode_message):
    accepted = message.get("acceptedPolicy") if isinstance(message, dict) else None
    check(isinstance(accepted, dict) and type(accepted.get("revision")) is int and isinstance(accepted.get("policy"), dict)
          and ACCEPTED_MODES.get(accepted["policy"].get("autonomyMode")) is not None, mode_message)
    return accepted


def require_policy_acknowledgement(output, a, requests, backend_origin, owned_root):
    """The mode in effect is the owner's acknowledgement, read back and stamped on accepted work.

    A transition needs exactly one actual conditional PUT carrying the requested mode and the
    revision acknowledged at creation, an HTTP 200 policy at the next revision and unchanged limits.
    An unchanged control sends none. Readback, accepted-message attribution, session history and
    visible attribution must all name the same revisions; the selector label is only corroboration.
    """
    output = Path(output)
    initial, requested = MODE_CASES[a["caseKey"]]
    transition = initial != requested
    session = a["sameSessionId"]
    create = strict_json((output / "autonomy-create-session.json").read_text())
    created = _policy(create.get("response", {}).get("data", {}).get("policy"), "The created session did not acknowledge a readable policy")
    check(create.get("status") == 200 and create.get("request", {}).get("autonomyMode") == initial
          and created["autonomyMode"] == initial and initial in created["allowedAutonomyModes"] and requested in created["allowedAutonomyModes"],
          "The created session did not acknowledge the selected initial mode within its allowed modes")
    kinds = [_request_kind(row, session, backend_origin) for row in requests]
    review = a["outcomeAtDecision"] == "review"
    check(kinds == ["create", "message"] + (["autonomy"] if transition else []) + ["message"] + (["approve", "execute"] if review else []),
          "Mode changes must be exactly one acknowledged PUT between the original prompts; controls send none")
    update_url = backend_origin + "/_elsa/agent/sessions/" + quote(session, safe="") + "/autonomy"
    structured, raw = output / "autonomy-mode-update.json", output / "autonomy-mode-update-http.json"
    if transition:
        check(_owned_ref(owned_root, a.get("policyUpdate")).resolve() == structured.resolve() and raw.is_file(),
              "The actual autonomy exchange was not retained")
        exchange, http = strict_json(structured.read_text()), strict_json(raw.read_text())
        put = requests[kinds.index("autonomy")]
        check(exchange.get("url") == http.get("url") == put["url"] == update_url and exchange.get("method") == http.get("method") == "PUT"
              and same(exchange.get("request"), put.get("body"))
              and same(exchange.get("request"), {"autonomyMode": requested, "expectedRevision": created["revision"]}),
              "The autonomy request must carry only the requested mode and the revision acknowledged at creation")
        check(exchange.get("status") == http.get("status") == 200 and isinstance(http.get("rawBody"), str)
              and same(strict_json(http["rawBody"]), exchange.get("response")) and isinstance(exchange.get("response"), dict)
              and exchange["response"].get("error") is None, "The owning backend did not acknowledge the mode change with HTTP 200")
        acknowledged = _policy(exchange["response"].get("data"), "The acknowledgement carries no readable policy")
        check(acknowledged["autonomyMode"] == requested and acknowledged["revision"] == created["revision"] + 1
              and acknowledged["maxAutonomyMode"] == created["maxAutonomyMode"]
              and same(acknowledged["allowedAutonomyModes"], created["allowedAutonomyModes"]),
              "The acknowledgement is not the requested mode at the next revision with unchanged limits")
    else:
        check(a.get("policyUpdate") is None and not structured.exists() and not raw.exists(), "An unchanged control cannot carry a policy update")
        acknowledged = None
    effective = acknowledged or created
    check(same(a.get("createdPolicy"), created) and same(a.get("acknowledgedPolicy"), acknowledged) and same(a.get("effectivePolicy"), effective)
          and a.get("policyAcknowledgementClaimed") is transition, "The browser relabeled the acknowledged policy")
    network = strict_json((output / "autonomy-network.json").read_text())
    check(isinstance(network, list) and all(isinstance(row, dict) for row in network), "Autonomy network metadata was not retained")
    puts = [row for row in network if row.get("method") == "PUT"]
    preflights = [row for row in network if row.get("method") == "OPTIONS"]
    check(len(puts) + len(preflights) == len(network) and len(puts) == (1 if transition else 0) and len(preflights) <= len(puts),
          "Only one actual PUT and at most its CORS preflight may reach the autonomy route")
    for row in network:
        put = row.get("method") == "PUT"
        check(row.get("url") == row.get("responseUrl") == update_url and row.get("requestObservationCount") == 1
              and row.get("responseObservationCount") == 1 and row.get("termination") == "loadingFinished"
              and not row.get("errorText") and row.get("canceled") is False
              and (row.get("resourceType") == row.get("responseResourceType") == "Fetch" and row.get("status") == 200 if put else
                   row.get("resourceType") in ("Other", "Preflight") and row.get("initiatorType") == "preflight"
                   and row.get("responseResourceType") == "Preflight" and row.get("status") == 204),
              "Autonomy PUT/preflight metadata is failed, foreign, repeated or not the exact owned route")
    before = _owner(owned_root, a["sessionBefore"]).get("data", {})
    selected = _owner(owned_root, a["sessionAfterSelection"]).get("data", {})
    final = _owner(owned_root, a["sessionAfter"]).get("data", {})
    check(before.get("sessionId") == selected.get("sessionId") == final.get("sessionId") == session
          and same(before.get("policy"), created) and same(selected.get("policy"), effective) and same(final.get("policy"), effective),
          "Session detail does not read back the acknowledged policy")
    check(any(row.get("id") == a["readMessageId"] and row.get("status") == "completed" for row in before.get("messages", [])),
          "The original read was not terminal in its owner before the mode decision")
    messages = final.get("messages", [])
    index = next((position for position, row in enumerate(messages) if row.get("id") == a["sizeMessageId"]), -1)
    check(index > 0 and all(type(row.get("acceptedPolicyRevision")) is int
                            and row["acceptedPolicyRevision"] == (created if position < index else effective)["revision"]
                            for position, row in enumerate(messages)),
          "Session history is not attributed to the policy revisions that accepted it")
    accepted = []
    for label, policy in (("read", created), ("size", effective)):
        message = strict_json((output / ("autonomy-" + label + "-message.json")).read_text()).get("response", {}).get("data", {}).get("message", {})
        value = _accepted_policy(message, "The accepted " + label + " prompt carries no accepted policy")
        derived = value["policy"].get("requiresApprovalForMutations", policy["autonomyMode"] != "full-auto")
        check(value["revision"] == policy["revision"] and ACCEPTED_MODES[value["policy"]["autonomyMode"]] == policy["autonomyMode"]
              and ACCEPTED_MODES.get(value["policy"].get("maxAutonomyMode")) == policy["maxAutonomyMode"]
              and derived is (policy["autonomyMode"] != "full-auto"),
              "The " + label + " prompt was not accepted under its acknowledged policy revision")
        # Only the mode (and the approval requirement derived from it) may differ between the two prompts.
        accepted.append({key: item for key, item in value["policy"].items() if key not in ("autonomyMode", "requiresApprovalForMutations")})
    check(same(accepted[0], accepted[1]), "The mode change altered unrelated accepted policy")
    selector = strict_json((output / "autonomy-mode-selector.json").read_text())
    check(selector == {"state": "acknowledged", "pendingMode": None, "ariaBusy": "false", "label": MODE_LABELS[effective["autonomyMode"]],
                       "statusCount": 0, "mode": effective["autonomyMode"]},
          "The selector did not settle on only the acknowledged mode")
    visible = [row for row in strict_json((output / "autonomy-ui-attribution.json").read_text()) if row.get("revision") is not None]
    modes = {str(created["revision"]): created["autonomyMode"], str(effective["revision"]): effective["autonomyMode"]}
    check(len(visible) >= 2 and all(modes.get(row["revision"]) == row.get("autonomyMode") for row in visible)
          and visible[0]["revision"] == str(created["revision"]) and visible[-1]["revision"] == str(effective["revision"]),
          "Visible results are not attributed to the policy that admitted their turn")
    references = [retained(output / name) for name in ("autonomy-create-session.json", "autonomy-network.json",
                  "autonomy-mode-selector.json", "autonomy-ui-attribution.json")]
    references += [a["sessionBefore"], a["sessionAfterSelection"]] + ([a["policyUpdate"], retained(raw)] if transition else [])
    return {"acknowledged": transition, "updateRequests": len(puts), "corsPreflights": len(preflights),
            "createdRevision": created["revision"], "effectiveRevision": effective["revision"],
            "effectiveMode": effective["autonomyMode"]}, references


def verify_mode_observation(output, observation, witness, provider, transport, *, offset=0, owned_root=None):
    output = Path(output)
    owned_root = owned_root or output
    check(provider in PROVIDERS and isinstance(observation, dict) and not observation.get("failure")
          and observation.get("provider") == provider and observation.get("journey") == "autonomy-size"
          and not observation.get("pageErrors") and observation.get("status") == "passed"
          and not observation.get("expectedFailureObserved") and "actual" not in observation,
          "An unfinished, failed or expected-failure browser journey cannot qualify a mode case")
    a = observation.get("autonomy", {})
    key = a.get("caseKey")
    check(key in MODE_CASES and (a.get("initialMode"), a.get("requestedMode")) == MODE_CASES[key]
          and a.get("outcomeAtDecision") in ("review", "automatic"), "The case must retain its actual initial/requested modes")
    expected = "review" if a["requestedMode"] == "manual" else "automatic"
    check(a.get("expectedOutcome") == expected and a["outcomeAtDecision"] == expected and a.get("modeMismatch") is False
          and a.get("applied") is True, "autonomy.mode_mismatch: the acknowledged mode did not govern the native outcome")
    before, after = require_size_owner_journey(output, observation, witness)
    requests, session = _messages(output, a, witness, before)
    policy, policy_refs = require_policy_acknowledgement(output, a, requests, transport.http.base, owned_root)
    check(same(a.get("selectionRequests"), [row for row in requests if urlparse(row["url"]).path.endswith("/autonomy")]),
          "The browser selection requests differ from the actual retained Agent requests")
    commands = _commands(output, a, witness)
    frames, sse = _frames(output, a["sameSessionId"], transport.http.base)
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete actual factory trace cannot prove the outcome")
    grouped = _actual_calls(trace, commands, provider, a["sameSessionId"])
    if a["outcomeAtDecision"] == "review":
        _pending_size_returns(grouped[a["sizeCommandId"]], provider, _proposal(frames, a, witness, before))
    # The autonomy PUT is policy, not an effect: only execute/ACK can carry a reviewed application.
    effect_requests = [row for row in requests if urlparse(row["url"]).path.endswith(("/execute", "/acknowledgements"))]
    check(len(effect_requests) == (1 if a["outcomeAtDecision"] == "review" else 0), "Automatic work must not issue execute and reviewed work executes once")
    _effect(output, requests, frames, a, witness, before, after, session, transport.http.base)
    correlated = correlate_browser_protocol(transport, output, provider, offset=offset)
    references = [observation["ownerBefore"], observation["ownerAfter"], sse, *observation.get("evidence", []),
                  correlated["receipt"], correlated["trace"], *policy_refs]
    for reference in references: _owned_ref(owned_root, reference)
    return {"id": provider + "-" + key, "provider": provider, "caseKey": key,
            "status": "passed", "modeMismatch": False, "cause": None, "source": None, "qualified": False,
            "nativeEvidenceVerified": True, "correlation": {"kind": "completed-native-result", "completeProviderJourney": True},
            "policy": policy, "sessionId": a["sameSessionId"], "initialMode": a["initialMode"], "requestedMode": a["requestedMode"],
            "outcomeAtDecision": a["outcomeAtDecision"], "ownerBefore": observation["ownerBefore"], "ownerAfter": observation["ownerAfter"],
            "evidence": references}


def execute_mode_case(runtime, provider, key):
    """Execute one genuinely selected UI case without using the all-positive browser wrapper."""
    check(provider == runtime.provider and key in MODE_CASES, "Select the actual native adapter before its declared mode case")
    runtime.run.assert_immutable_inputs()
    runtime.sign_in()
    root = runtime.output("autonomy-" + provider + "-" + key)
    output = root / "observation"
    config = {"journey": "autonomy-size", "autonomyCase": key, "provider": provider,
              "studioOrigin": runtime.studio_origin, "backendOrigin": runtime.backend_origin,
              "storageState": runtime.active_identity["storageState"], "commandFile": str(runtime.command_file), "output": str(output)}
    private_json(root / "config-private.json", config)
    transport = runtime.transport("autonomy-protocol-" + provider + "-" + key)
    path = transport.trace_file
    prefix = path.read_bytes() if path.exists() else b""
    node = shutil.which("node")
    check(node, "The real browser mode journey needs Node")
    try:
        runtime.run.run("autonomy-" + provider + "-" + key, [node, str(TOOL / "browser.mjs"), str(root / "config-private.json")], TOOL,
                        env={name: value for name, value in os.environ.items() if name in RUNTIME_ENVIRONMENT})
    except RuntimeError as error:
        # Retain the browser's own precise cause; a failed journey is never an expected outcome.
        observed = output / "observation.json"
        value = strict_json(observed.read_text()) if observed.is_file() else {}
        failure = value.get("failure") if isinstance(value, dict) else None
        cause = failure.get("cause") if isinstance(failure, dict) and isinstance(failure.get("cause"), str) else "browser.failed"
        detail = failure.get("message") if isinstance(failure, dict) and isinstance(failure.get("message"), str) else "no browser failure retained"
        raise ModeCaseFailure(str(error) + "; browser: " + detail[:500], cause, retained(observed) if observed.is_file() else None) from None
    check(path.is_file() and path.read_bytes()[:len(prefix)] == prefix, "The prior actual factory trace was replaced")
    observation = strict_json((output / "observation.json").read_text())
    record = verify_mode_observation(output, observation, runtime.witness, provider, transport, offset=len(prefix), owned_root=runtime.run.root)
    record.update(source=runtime.run.source, tracePrefix={"offset": len(prefix), "sha256": hashlib.sha256(prefix).hexdigest()},
                  observation=retained(output / "observation.json"), inputs=runtime.save(root, "browser-inputs", {**browser_inputs(config), "autonomyCase": key}))
    runtime.run.assert_immutable_inputs()
    return record


def qualify_autonomy_modes(runtime):
    """T012: eight acknowledged mode cases; never promotes an independent package witness.

    Each case runs once. A failed case keeps its precise cause and the remaining cases still
    run, so one native attempt observes the whole matrix. Success requires all eight passed.
    """
    run = runtime.run
    check(MODE_QUALIFICATION not in run.report and run.report.get("independentPackageWitness") is False,
          "Use a fresh mode qualification, never replace a qualified or partial observation")
    section_path = run.root / "autonomy-mode-qualification.json"
    check(not section_path.exists(), "Never overwrite a retained mode qualification")
    section = {"status": "running", "scope": "established-session-mode-cases", "source": run.source,
               "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False, "qualified": False, "independentPackageWitness": False,
               "expectedCaseIds": sorted(MODE_CASE_IDS), "finalRequiredCaseIds": sorted(EXPECTED_CASES), "cases": []}
    run.report[MODE_QUALIFICATION] = section
    run.save()
    try:
        for provider in PROVIDERS:
            runtime.select_provider(provider)
            for key in MODE_CASES:
                try:
                    record = execute_mode_case(runtime, provider, key)
                except RuntimeError as error:
                    record = {"id": provider + "-" + key, "provider": provider, "caseKey": key, "status": "failed",
                              "cause": error.cause if isinstance(error, ModeCaseFailure) else "verification",
                              "message": str(error)[:1000], "observation": getattr(error, "observation", None),
                              "modeMismatch": isinstance(error, ModeCaseFailure) and error.cause == "autonomy.mode_mismatch",
                              "nativeEvidenceVerified": False, "qualified": False, "source": run.source}
                section["cases"].append(record)
                run.save()
                run.assert_immutable_inputs()
        require_mode_qualification(section["cases"])
        section["status"] = "passed"
    except BaseException as error:
        section.update(status="failed", failure={"type": type(error).__name__, "message": str(error)[:1000]})
        raise
    finally:
        check(run.report.get("independentPackageWitness") is False, "Mode qualification cannot promote package qualification")
        private_json(section_path, section)
        run.save()
    return section


def require_lifecycle_matrix(section, owned_root):
    """T015: every one of the thirteen cases passed exactly once on each actual adapter.

    Omissions, duplicates, foreign or wrong-adapter rows, wrong-cause outcomes and missing or
    changed owner/correlation evidence fail. This never qualifies the package witness (T025).
    """
    from lifecycle_journeys import EXPECTED_OUTCOMES, require_lifecycle_record
    check(set(LIFECYCLE_CASES) - set(MODE_CASES) == set(EXPECTED_OUTCOMES), "The lifecycle manifest and its expected outcomes disagree")
    check(isinstance(section, dict) and section.get("evidenceCategory") == EVIDENCE_CATEGORY and section.get("liveProvider") is False
          and section.get("qualified") is False and section.get("independentPackageWitness") is False,
          "The lifecycle matrix is deterministic provider-protocol evidence and never a package witness")
    records = section.get("cases")
    check(isinstance(records, list) and all(isinstance(row, dict) for row in records), "The lifecycle matrix has no case records")
    identities = [row.get("id") for row in records]
    check(len(identities) == len(set(identities)), "A duplicated lifecycle case cannot qualify")
    check(set(identities) == EXPECTED_CASES, "Every one of the thirteen cases per actual adapter is mandatory exactly once")
    check(all(row.get("provider") in PROVIDERS and row.get("caseKey") in LIFECYCLE_CASES
              and row["id"] == row["provider"] + "-" + row["caseKey"] for row in records), "A case must name its exact actual adapter")
    failed = sorted(row["id"] for row in records if row.get("status") != "passed")
    check(not failed, "Every case must pass on each actual adapter: " + ", ".join(failed))
    modes = [row for row in records if row["caseKey"] in MODE_CASES]
    require_mode_qualification(modes)
    for row in modes:
        check(row.get("sessionId") and row.get("correlation", {}).get("kind") == "completed-native-result", "A mode case lost its native correlation")
        for reference in (row.get("ownerBefore"), row.get("ownerAfter"), *row["evidence"]): _owned_ref(owned_root, reference)
    for row in records:
        if row["caseKey"] not in MODE_CASES: require_lifecycle_record(owned_root, row)
    return records


def qualify_autonomy_matrix(runtime):
    """T015: the full per-adapter thirteen-case matrix; never promotes an independent package witness.

    Each case runs once. A failed case keeps its precise cause and the remaining cases still run,
    so one native attempt shows every outcome. Success requires all twenty-six passed.
    """
    from lifecycle_journeys import LifecycleCaseFailure, execute_lifecycle_case
    run = runtime.run
    check(MATRIX_QUALIFICATION not in run.report and MODE_QUALIFICATION not in run.report
          and run.report.get("independentPackageWitness") is False, "Use a fresh lifecycle matrix, never replace a qualified or partial observation")
    section_path = run.root / "autonomy-lifecycle-qualification.json"
    check(not section_path.exists(), "Never overwrite a retained lifecycle matrix")
    section = {"status": "running", "scope": "per-adapter-lifecycle-matrix", "source": run.source,
               "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False, "qualified": False, "independentPackageWitness": False,
               "expectedCaseIds": sorted(EXPECTED_CASES), "cases": []}
    run.report[MATRIX_QUALIFICATION] = section
    run.save()
    try:
        for provider in PROVIDERS:
            runtime.select_provider(provider)
            for key in LIFECYCLE_CASES:
                try:
                    record = execute_mode_case(runtime, provider, key) if key in MODE_CASES else execute_lifecycle_case(runtime, provider, key)
                except RuntimeError as error:
                    precise = isinstance(error, (ModeCaseFailure, LifecycleCaseFailure))
                    record = {"id": provider + "-" + key, "provider": provider, "adapter": provider, "caseKey": key, "status": "failed",
                              "cause": error.cause if precise else "verification", "message": str(error)[:1000],
                              "observation": getattr(error, "observation", None),
                              "modeMismatch": isinstance(error, ModeCaseFailure) and error.cause == "autonomy.mode_mismatch",
                              "nativeEvidenceVerified": False, "qualified": False, "source": run.source}
                section["cases"].append(record)
                run.save()
                run.assert_immutable_inputs()
        require_lifecycle_matrix(section, run.root)
        section["status"] = "passed"
    except BaseException as error:
        section.update(status="failed", failure={"type": type(error).__name__, "message": str(error)[:1000]})
        raise
    finally:
        check(run.report.get("independentPackageWitness") is False, "The lifecycle matrix cannot promote package qualification")
        private_json(section_path, section)
        run.save()
    return section
