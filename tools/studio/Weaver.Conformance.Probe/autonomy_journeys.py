"""Same-session native mode diagnostics and the final, exact lifecycle manifest.

The four initial cases are diagnostic inputs, not a complete package witness.
Provider and owning-resource proof comes from retained native bytes, never a
selector label, provider text, synthetic result or inferred successful write.
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
from case_records import (retained, correlate_browser_protocol, _normalized_result,
                          _current_function_results)
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
DIAGNOSTIC_CASES = frozenset(f"{provider}-{key}" for provider in PROVIDERS for key in MODE_CASES)
EVIDENCE_CATEGORY = "deterministic-provider-protocol"


def require_complete_autonomy(section):
    """Mandatory final gate; the initial eight observations can never satisfy it."""
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


def require_diagnostic_manifest(records):
    check(len(records) == len(DIAGNOSTIC_CASES) and {row.get("id") for row in records} == DIAGNOSTIC_CASES,
          "The bounded baseline needs exactly four mode cases per actual adapter")
    for row in records:
        check(row.get("provider") in PROVIDERS and row.get("caseKey") in MODE_CASES
              and row["id"] == row["provider"] + "-" + row["caseKey"]
              and row.get("nativeEvidenceVerified") is True and row.get("qualified") is False,
              "A diagnostic needs its exact actual-adapter proof without a qualification claim")
        if row.get("status") == "failed":
            check(row.get("caseKey") in ("automatic-to-manual", "manual-to-automatic")
                  and row.get("modeMismatch") is True and row.get("cause") == "autonomy.mode_mismatch",
                  "Only the precise established-session mode contrast is an expected diagnostic failure")
        else:
            check(row.get("status") == "passed" and row.get("modeMismatch") is False,
                  "Unexpected, unfinished or wrong-cause diagnostic outcomes are not accepted")
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
            check(request.get("resourceType") == response.get("resourceType") == "Preflight"
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
        check(same(requests[index + 1]["body"], request) and isinstance(requested, list) and len(requested) == 1
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


def require_deferred_diagnostic(trace, commands, frames, a, witness, before, provider):
    """Prove a real pending call, explicitly NOT a completed provider/result journey."""
    grouped = _actual_calls(trace, commands, provider, a["sameSessionId"])
    proposal = _proposal(frames, a, witness, before)
    size_rows = grouped[a["sizeCommandId"]]
    check(sum(row["kind"] == provider + ".request" for row in size_rows) == 1
          and not any(row["kind"] == provider + ".continuation" for row in size_rows),
          "A pending review cannot contain an admitted result continuation")
    returns = [row["data"].get("result") for row in size_rows if row["kind"] == provider + ".return"]
    check(len(returns) <= (1 if provider == "copilot" else 0), "Duplicate or unexpected pending function returns")
    for value in returns:
        check(isinstance(value, dict) and value.get("status") == "awaiting-approval" and value.get("executed") is False
              and value.get("proposalId") == proposal["id"] and not value.get("receipt")
              and same(value.get("preview"), proposal["preparedInvocation"].get("preview")), "A pending SDK return claimed application")
    check(not any(row.get("kind") == "contributedResult" and row.get("payload", {}).get("toolName") == witness["sizeTool"] for row in frames),
          "A pending size operation already exposed a result")
    # The first read has a completed result, rather than a failing setup disguised as a review stop.
    native_reads = [row.get("payload") for row in frames if row.get("kind") == "contributedResult"
                    and row.get("resultKind") == "moduleResult" and row.get("payload", {}).get("toolName") == witness["readTool"]]
    check(len(native_reads) == 1 and native_reads[0].get("succeeded") is True and not native_reads[0].get("receipt"),
          "The original harmless read did not complete natively")
    read_rows = grouped[a["readCommandId"]]
    if provider == "copilot":
        check([row["kind"] for row in read_rows] == ["copilot.request", "copilot.call", "copilot.return"],
              "The original harmless SDK read must complete once without another request")
        results = [row["data"].get("result") for row in read_rows if row["kind"] == "copilot.return"]
        check(len(results) == 1 and same(results[0], native_reads[0]), "The actual first SDK read differs from native evidence")
    else:
        check([row["kind"] for row in read_rows] == ["anthropic.request", "anthropic.call", "anthropic.request", "anthropic.continuation"],
              "The first actual host-driven read needs its complete result continuation")
        items = read_rows[-1]["data"].get("results")
        check(isinstance(items, list) and len(items) == 1 and items[0].get("kind") == "functionResult"
              and items[0].get("callId") == a["readCommandId"] and same(_current_function_results(read_rows[-2]["data"]), items)
              and isinstance(items[0].get("result"), str) and same(strict_json(items[0]["result"]), native_reads[0]),
              "The actual first host-driven result differs from native evidence")
    return {"kind": "deferred-call-diagnostic", "completeProviderJourney": False, "proposalId": proposal["id"],
            "oneActualSizeCall": True, "sizeContinuationCount": 0, "sizeEffectCount": 0}


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


def verify_mode_observation(output, observation, witness, provider, transport, *, offset=0, owned_root=None):
    output = Path(output)
    check(provider in PROVIDERS and isinstance(observation, dict) and not observation.get("failure")
          and observation.get("provider") == provider and observation.get("journey") == "autonomy-size"
          and not observation.get("pageErrors"), "An unexpected browser/provider/transport failure is not a mode contrast")
    a = observation.get("autonomy", {})
    key = a.get("caseKey")
    check(key in MODE_CASES and (a.get("initialMode"), a.get("requestedMode")) == MODE_CASES[key]
          and a.get("outcomeAtDecision") in ("review", "automatic") and a.get("policyAcknowledgementClaimed") is False,
          "The baseline must retain actual initial/requested modes without fabricated acknowledgement")
    check(a.get("selectionRequests") == [],
          "This source-local selector baseline cannot stand in for a failed or unqualified policy-update request")
    expected = "review" if a["requestedMode"] == "manual" else "automatic"
    mismatch = a["outcomeAtDecision"] != expected
    check(a.get("expectedOutcome") == expected and a.get("modeMismatch") is mismatch
          and a.get("applied") is (a["outcomeAtDecision"] == "automatic" or not mismatch), "The mode outcome was relabeled")
    if mismatch:
        check(key in ("automatic-to-manual", "manual-to-automatic") and observation.get("status") == "failed"
              and observation.get("expectedFailureObserved") is True
              and observation.get("actual") == {"stage": "session-mode", "diagnostic": "autonomy.mode_mismatch"}, "Wrong-cause failure cannot satisfy the baseline")
    else:
        check(observation.get("status") == "passed" and not observation.get("expectedFailureObserved"), "An unchanged-mode control did not pass")
    before, after = require_size_owner_journey(output, observation, witness)
    requests, session = _messages(output, a, witness, before)
    commands = _commands(output, a, witness)
    frames, sse = _frames(output, a["sameSessionId"], transport.http.base)
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete actual factory trace cannot prove the outcome")
    _actual_calls(trace, commands, provider, a["sameSessionId"])
    effect_requests = [row for row in requests if urlparse(row["url"]).path.endswith(("/execute", "/acknowledgements"))]
    if not a["applied"]:
        check(not effect_requests and not a.get("selectionRequests"), "The deferred baseline must not execute or fabricate a policy acknowledgement")
        correlation = require_deferred_diagnostic(trace, commands, frames, a, witness, before, provider)
        proof = retained(Path(output) / "deferred-call-diagnostic.json") if (Path(output) / "deferred-call-diagnostic.json").exists() else None
        check(proof is None, "Never replace a prior deferred diagnostic")
        private_json(Path(output) / "deferred-call-diagnostic.json", correlation)
        correlation_refs = [retained(Path(output) / "deferred-call-diagnostic.json"), trace["receipt"]]
    else:
        check(len(effect_requests) == (1 if a["outcomeAtDecision"] == "review" else 0), "Automatic work must not issue execute and reviewed work executes once")
        _effect(output, requests, frames, a, witness, before, after, session, transport.http.base)
        correlated = correlate_browser_protocol(transport, output, provider, offset=offset)
        correlation_refs = [correlated["receipt"], correlated["trace"]]
        correlation = {"kind": "completed-native-result", "completeProviderJourney": True}
    references = [observation["ownerBefore"], observation["ownerAfter"], sse, *observation.get("evidence", []), *correlation_refs]
    for reference in references: _owned_ref(owned_root or output, reference)
    return {"id": provider + "-" + key, "provider": provider, "caseKey": key,
            "status": observation["status"], "modeMismatch": mismatch, "cause": "autonomy.mode_mismatch" if mismatch else None,
            "source": None, "qualified": False, "nativeEvidenceVerified": True, "correlation": correlation,
            "sessionId": a["sameSessionId"], "initialMode": a["initialMode"], "requestedMode": a["requestedMode"],
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
    check(node, "The real browser mode diagnostic needs Node")
    runtime.run.run("autonomy-" + provider + "-" + key, [node, str(TOOL / "browser.mjs"), str(root / "config-private.json")], TOOL,
                    env={name: value for name, value in os.environ.items() if name in RUNTIME_ENVIRONMENT})
    check(path.is_file() and path.read_bytes()[:len(prefix)] == prefix, "The prior actual factory trace was replaced")
    observation = strict_json((output / "observation.json").read_text())
    record = verify_mode_observation(output, observation, runtime.witness, provider, transport, offset=len(prefix), owned_root=runtime.run.root)
    record.update(source=runtime.run.source, tracePrefix={"offset": len(prefix), "sha256": hashlib.sha256(prefix).hexdigest()},
                  observation=retained(output / "observation.json"), inputs=runtime.save(root, "browser-inputs", {**browser_inputs(config), "autonomyCase": key}))
    runtime.run.assert_immutable_inputs()
    return record


def diagnose_autonomy(runtime):
    """Initial eight-case diagnostic ONLY; never promotes an independent package witness."""
    run = runtime.run
    check("autonomyBaselineDiagnostic" not in run.report and run.report.get("independentPackageWitness") is False,
          "Use a fresh diagnostic, never replace a qualified or partial observation")
    diagnostic_path = run.root / "baseline-diagnostic.json"
    check(not diagnostic_path.exists(), "Never overwrite a retained diagnostic")
    section = {"status": "diagnostic-running", "source": run.source, "evidenceCategory": EVIDENCE_CATEGORY,
               "liveProvider": False, "qualified": False, "independentPackageWitness": False,
               "expectedCaseIds": sorted(DIAGNOSTIC_CASES), "finalRequiredCaseIds": sorted(EXPECTED_CASES), "cases": []}
    run.report["autonomyBaselineDiagnostic"] = section
    run.save()
    try:
        for provider in PROVIDERS:
            runtime.select_provider(provider)
            for key in MODE_CASES:
                record = execute_mode_case(runtime, provider, key)
                section["cases"].append(record)
                run.save()
        require_diagnostic_manifest(section["cases"])
        section["status"] = "diagnostic-complete"
    except BaseException as error:
        section.update(status="diagnostic-failed", failure={"type": type(error).__name__})
        raise
    finally:
        check(run.report.get("independentPackageWitness") is False, "A bounded diagnostic cannot promote package qualification")
        private_json(diagnostic_path, section)
        run.save()
    return section
