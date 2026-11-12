"""Spec1203 T037: the independent module's effects through actual packaged adapters.

Fixture meaning belongs here, never in an adapter, host or product SDK. This section
cannot promote a witness. T038 owns the separately mandatory durable/upgrade matrix.
"""
from __future__ import annotations

from contextlib import contextmanager
import copy
import hashlib
import json
import os
from pathlib import Path
import secrets
import re
import shutil
import signal
import threading
import time
import uuid
from urllib.parse import parse_qs, urlsplit

from artifacts import TOOL, check, sha
from case_records import retained, retained_json, correlate_browser_protocol, _normalized_result, _current_function_results
from consumers import COMMON_EFFECTS_FEATURE, DURABLE_EFFECTS_FEATURE, configure_weaver_privileges
from inspection_journeys import _called_once
from journey_runtime import HOME_PREFERENCES_PATH, HOST_HEADER, atomic_json, same
from runtime import owned_content_root, private_json, RUNTIME_ENVIRONMENT
from weaver_transport import _json as strict_json

SECTION = "moduleEffectsQualification"
RESTART_SECTION = "moduleEffectsRestartQualification"
EVIDENCE_CATEGORY = "deterministic-provider-protocol"
PROVIDERS = ("copilot", "anthropic")
CLASSES = ("administrative", "irrecoverable", "activation")
MODULE, WIDGET, HOST = "Acme.Home", "acme.home:status", "weaver-conformance3438"
ROUTE, OWNER = "/qualification/acme-home-effects", "/_qualification/acme/home-widget"
KIND, CHECK_KIND = "acme.home.effects", "acme.home.check-result"
CHECK_TOOL = "acme_home_check_widget"
TOOLS = {
    "acme_home_admin_visibility": ("administrative",),
    "acme_home_irrecoverable_visibility": ("administrative", "irrecoverable"),
    "acme_home_omitted_admin_visibility": ("administrative", "irrecoverable"),
    "acme_home_raised_risk_visibility": ("administrative",),
    "acme_home_activation_visibility": ("activation",),
    CHECK_TOOL: ("administrative",),
}
CHANNELS = ("authorization", "preparation", "execution", "receipt", "context")
POLICY_CODE = "weaver.proposal.privilege_disabled"
SOURCE_PATHS = (
    "tools/studio/Weaver.Conformance.Probe/effects_journeys.py",
    "tools/studio/Weaver.Conformance.Probe/case_records.py",
    "tools/studio/Weaver.Conformance.Probe/browser.mjs",
    "tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Studio/Client/src/effects.tsx",
    "tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Backend/WidgetEffectsTools.cs.in",
    "tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Backend/WidgetEffectsOwner.cs.in",
    "tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Backend/WidgetEffectsContext.cs.in",
    "tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Backend/WidgetEffectObservations.cs.in",
)


def scenarios():
    rows = {}
    for tool, classes in TOOLS.items():
        for condition in ("unconfigured", "administrative-only", "all-enabled", "approval-disabled", "execution-disabled", "denied", "cancelled", "stale", "duplicate-delivery",
                          *("missing-" + value for value in classes)):
            rows[tool + ":" + condition] = {"tool": tool, "condition": condition}
    rows[CHECK_TOOL + ":business-failed"] = {"tool": CHECK_TOOL, "condition": "business-failed"}
    for channel in CHANNELS:
        for unavailable in ((False,) if channel == "receipt" else (False, True)):
            rows["typed:" + channel + (":unavailable" if unavailable else ":refused")] = {
                "tool": CHECK_TOOL, "condition": "typed", "channel": channel, "ownerUnavailable": unavailable}
            rows["browser-typed:" + channel + (":unavailable" if unavailable else ":refused")] = {
                "tool": CHECK_TOOL, "condition": "browser-typed", "channel": channel, "ownerUnavailable": unavailable}
    for tool in TOOLS:
        if tool != CHECK_TOOL:
            rows["typed-receipt:" + tool] = {"tool": tool, "condition": "typed", "channel": "receipt", "ownerUnavailable": False}
    for tool in TOOLS:
        rows["browser:" + tool] = {"tool": tool, "condition": "browser"}
    rows["browser:business-failed"] = {"tool": CHECK_TOOL, "condition": "browser-failed"}
    rows["browser:nested-removal"] = {"tool": CHECK_TOOL, "condition": "nested-removal"}
    return rows


SCENARIOS = scenarios()
EXPECTED_CASES = tuple(provider + "/" + name for provider in PROVIDERS for name in SCENARIOS)
# T038 must deliver and validate these on a real durable host; classed preparations
# cannot be invented in an older writer. Kept separate from ordinary memory proof.
RESTART_REQUIREMENTS = tuple(provider + "/" + name for provider in PROVIDERS for name in (
    *(tool + ":recovery-disabled" for tool in TOOLS), "removed-owner-pending", "legacy-pending-upgrade",
    "legacy-approved-upgrade", "legacy-admin-disabled-upgrade", "expired-upgrade", "client-awaiting-upgrade",
    "all-legacy-row-kinds", "effects-held", "effects-finalized"))


def source_bindings():
    return {name: sha(TOOL.parents[2] / name) for name in SOURCE_PATHS}


def require_resource_inventory(tools, contexts, witness):
    """The legacy three remain exact; six new backend tools are additive, never replacements."""
    original = {witness["readTool"], witness["writeTool"], witness["sizeTool"]}
    _need(witness["moduleId"] == MODULE and len(original) == 3 and not original.intersection(TOOLS), "wrong independent resource inventory")
    _need(len(tools) == 9 and {row.get("name") for row in tools} == original | set(TOOLS)
          and all(row.get("moduleId") == MODULE and row.get("contractVersion") == "1" for row in tools), "missing/duplicate/foreign original or effects tool")
    _need(len(contexts) == 2 and {row.get("id") for row in contexts} == {"acme.home.widget-context", "acme.home.effects-context"}
          and all(row.get("moduleId") == MODULE for row in contexts), "missing/duplicate/foreign original or effects context")


def _need(value, message):
    check(value, "Module effects: " + message)


def _target(value, revision):
    return isinstance(value, dict) and all(value.get(key) == expected for key, expected in {
        "moduleId": MODULE, "resourceType": "home-widget", "resourceId": WIDGET, "revision": revision}.items())


def require_performed(prepared, execution, before, after, layout_before, layout_after, checks_before, checks_after):
    """No successful envelope or ran=true can replace exact native owner evidence."""
    _need(isinstance(prepared, dict) and prepared.get("moduleId") == MODULE and prepared.get("toolName") == CHECK_TOOL
          and prepared.get("toolContractVersion") == "1" and prepared.get("executionLocation") == "server"
          and prepared.get("client") is None and prepared.get("requiresApproval") is True and prepared.get("risk") == "admin",
          "foreign or malformed prepared check")
    _need(isinstance(before, dict) and before.get("widgetId") == WIDGET and before.get("hostId") == HOST
          and isinstance(before.get("visible"), bool) and isinstance(before.get("revision"), str) and before["revision"], "invalid native widget")
    _need(same(before, after) and same(layout_before, layout_after), "performed changed the complete owning resource or revision")
    _need(_target(prepared.get("target"), before["revision"]), "prepared target differs from native owner")
    _need(execution.get("status") == "performed" and execution.get("executed") is True, "no truthful performed status")
    result = execution.get("toolResult") or {}
    receipt = result.get("receipt") or {}
    _need(result.get("succeeded") is True and result.get("toolName") == CHECK_TOOL and result.get("toolCallId") == prepared.get("toolCallId")
          and bool(prepared.get("invocationId")) and bool(prepared.get("toolCallId")), "wrong result correlation")
    _need(receipt.get("invocationId") == prepared["invocationId"] and receipt.get("scope") == "noPersistedEffect"
          and _target(receipt.get("before"), before["revision"]) and _target(receipt.get("after"), before["revision"]), "wrong check receipt")
    payload, evidence = result.get("payload") or {}, receipt.get("evidence") or {}
    _need(payload.get("typeId") == evidence.get("typeId") == CHECK_KIND and payload.get("schemaVersion") == evidence.get("schemaVersion") == "1"
          and same(payload.get("data"), evidence.get("data")), "payload and evidence differ")
    value = payload.get("data") or {}
    _need(set(value) == {"widgetId", "hostId", "revision", "expectedVisible", "actualVisible", "passed", "ran", "callNumber"}
          and value.get("widgetId") == WIDGET and value.get("hostId") == HOST and value.get("revision") == before["revision"]
          and type(value.get("expectedVisible")) is bool and type(value.get("actualVisible")) is bool and type(value.get("passed")) is bool
          and value["actualVisible"] == before["visible"] and value["passed"] == (value["expectedVisible"] == before["visible"])
          and value.get("ran") is True and type(value.get("callNumber")) is int, "invalid business verdict")
    _need(prepared.get("arguments") == {"expectedVisible": value["expectedVisible"]}, "check arguments differ")
    _need(prepared.get("preview") == {"typeId": CHECK_KIND, "schemaVersion": "1", "data": {
        "widgetId": WIDGET, "hostId": HOST, "revision": before["revision"], "expectedVisible": value["expectedVisible"]}}, "frozen check preview differs")
    _need(receipt.get("moduleCode") in ("acme.home.effects.check_passed" if value["passed"] else "acme.home.effects.check_failed", "module.code.unsupported"),
          "missing or foreign typed check code")
    for snapshot in (checks_before, checks_after):
        _need(snapshot.get("widgetId") == WIDGET and snapshot.get("hostId") == HOST and snapshot.get("revision") == before["revision"]
              and type(snapshot.get("ranCount")) is int and snapshot["ranCount"] >= 0 and isinstance(snapshot.get("runs"), list)
              and len(snapshot["runs"]) == min(snapshot["ranCount"], 16), "invalid native journal")
        for index, row in enumerate(snapshot["runs"]):
            _need(set(row) == {"invocationId", "toolCallId", "toolName", "revision", "passed", "callNumber"}
                  and row.get("toolName") == CHECK_TOOL and type(row.get("passed")) is bool
                  and row.get("callNumber") == snapshot["ranCount"] - len(snapshot["runs"]) + index + 1, "malformed native journal ordering")
    _need(checks_after["ranCount"] == checks_before["ranCount"] + 1 and value["callNumber"] == checks_after["ranCount"], "wrong owner execution count")
    matches = [row for row in checks_after["runs"] if row.get("invocationId") == prepared["invocationId"]]
    _need(matches == [{"invocationId": prepared["invocationId"], "toolCallId": prepared["toolCallId"], "toolName": CHECK_TOOL,
                      "revision": before["revision"], "passed": value["passed"], "callNumber": value["callNumber"]}], "missing/duplicate/foreign owner invocation")
    return value


def require_visibility(prepared, execution, arguments, before, after, layout_before, layout_after, checks_before, checks_after):
    tool = prepared.get("toolName")
    result = execution.get("toolResult") or {}; receipt = result.get("receipt") or {}; payload = (result.get("payload") or {}).get("data") or {}
    _need(tool in TOOLS and tool != CHECK_TOOL and prepared.get("moduleId") == MODULE and prepared.get("toolContractVersion") == "1"
          and prepared.get("executionLocation") == "server" and prepared.get("client") is None and prepared.get("requiresApproval") is True
          and prepared.get("arguments") == arguments and bool(prepared.get("invocationId")) and bool(prepared.get("toolCallId"))
          and _target(prepared.get("target"), before["revision"]), "foreign prepared visibility")
    _need(execution.get("status") == "executed" and execution.get("executed") is True and result.get("succeeded") is True and result.get("toolName") == tool
          and result.get("toolCallId") == prepared["toolCallId"] and receipt.get("invocationId") == prepared["invocationId"]
          and receipt.get("scope") == "serverPersisted" and _target(receipt.get("before"), before["revision"])
          and _target(receipt.get("after"), after["revision"]) and before["revision"] != after["revision"]
          and payload.get("widgetId") == WIDGET and payload.get("hostId") == HOST and payload.get("revision") == after["revision"]
          and payload.get("beforeVisible") is before["visible"] and payload.get("visible") is arguments["visible"] and after["visible"] is arguments["visible"]
          and receipt.get("moduleCode") in ("acme.home.effects.changed", "module.code.unsupported")
          and (result.get("payload") or {}).get("typeId") == (receipt.get("evidence") or {}).get("typeId") == "acme.home.widget-visibility"
          and (result.get("payload") or {}).get("schemaVersion") == (receipt.get("evidence") or {}).get("schemaVersion") == "1"
          and same((result.get("payload") or {}).get("data"), (receipt.get("evidence") or {}).get("data"))
          and same(_layout_without_visibility(layout_before), _layout_without_visibility(layout_after))
          and checks_before["ranCount"] == checks_after["ranCount"] and same(checks_before["runs"], checks_after["runs"]), "visibility owner result differs")
    return payload


def require_refusal(values, code, *, owner_unavailable=None):
    """Only the expected typed source cause qualifies; transport/provider errors never do."""
    found = []
    def visit(item):
        if isinstance(item, dict):
            if "code" in item and item.get("code") == code:
                found.append(item)
            for value in item.values(): visit(value)
        elif isinstance(item, list):
            for value in item: visit(value)
    visit(values)
    _need(found, "wrong refusal cause; required " + code)
    if owner_unavailable is not None:
        # Core marks the false/default field JsonIgnore(WhenWritingDefault).
        _need(all(item.get("ownerUnavailable", False) is owner_unavailable for item in found), "owner-unavailable classification changed")
    return found


def _typed_code(spec):
    # Plain authorizer violations are normalized internally, then the public tool
    # refusal intentionally exposes the existing forbidden code, not the violation.
    if spec["channel"] == "authorization" and not spec["ownerUnavailable"]: return "weaver.tool.forbidden"
    return "module.owner.unavailable" if spec["ownerUnavailable"] else "module.code.unsupported"


def require_adapter_result(trace, provider, command_id, expected):
    continuations = [row["data"] for row in trace if row.get("kind") == provider + ".continuation"
                     and row.get("data", {}).get("commandId") == command_id]
    _need(len(continuations) == 1, "missing/duplicate actual adapter result continuation")
    values = continuations[0].get("results")
    _need(isinstance(values, list) and len(values) == 1, "actual adapter received conflicting results")
    if provider == "anthropic":
        _need(values[0].get("kind") == "functionResult" and values[0].get("callId") == command_id and isinstance(values[0].get("result"), str), "wrong Anthropic result binding")
        values = [json.loads(values[0]["result"])]
    _need(same(_normalized_result(values[0]), _normalized_result(expected)), "actual adapter outcome differs from native owner result")


def require_normal_terminal(frames):
    """A semantic refusal is a completed turn, never a provider/SDK exception."""
    _need(isinstance(frames, list) and frames and all(isinstance(row, dict) for row in frames), "missing native turn frames")
    starts = [row for row in frames if row.get("kind") == "started"]
    _need(len(starts) == 1 and isinstance(starts[0].get("payload"), dict)
          and isinstance(starts[0]["payload"].get("turnId"), str) and starts[0]["payload"]["turnId"], "missing unique current native turn")
    _need([row.get("kind") for row in frames if row.get("kind") in ("completed", "error", "turnCancelled", "toolApprovalRequested")] == ["completed"]
          and frames[-1].get("kind") == "completed", "refusal did not complete normally through its actual adapter")
    return starts[0]["payload"]["turnId"]


def require_called_refusal(trace, provider, command_id, tool, arguments, session_id, initial, terminal,
                           code, recorded_session, *, owner_unavailable=False, proposal_id=None, human_denial=None):
    """Bind native failed results to the actual current SDK call/completion, not a code elsewhere."""
    require_normal_terminal(terminal)
    starts = [row.get("payload") for row in initial if row.get("kind") == "toolCallStarted"]
    _need(len(starts) == 1 and isinstance(starts[0], dict) and starts[0].get("toolName") == tool
          and isinstance(starts[0].get("toolCallId"), str) and starts[0]["toolCallId"], "wrong current native refusal call")
    call_id = starts[0]["toolCallId"]
    initial_starts = [row.get("payload") for row in initial if row.get("kind") == "started"]
    _need(len(initial_starts) == 1 and isinstance(initial_starts[0], dict) and initial_starts[0].get("turnId"), "missing initial native refusal turn")
    _need(isinstance(recorded_session, dict) and recorded_session.get("sessionId") == session_id
          and isinstance(recorded_session.get("messages"), list), "missing exact reopened native session")
    results = []
    for row in recorded_session["messages"]:
        if row.get("role") != "tool": continue
        value = strict_json(row["content"])
        if value.get("toolCallId") == call_id:
            _need(row.get("status") == "completed", "recorded refusal is not an accepted tool message")
            results.append(value)
    _need(len(results) == 1 and isinstance(results[0], dict), "no unique reopened native failed tool result")
    result = results[0]
    _need(result.get("toolName") == tool and result.get("toolCallId") == call_id and result.get("succeeded") is False
          and result.get("receipt") is None, "foreign or contradictory native refusal result")
    error = result.get("error")
    if human_denial is None:
        _need(isinstance(error, dict) and error.get("code") == code and isinstance(error.get("message"), str)
              and error["message"] and type(error.get("statusCode")) is int and 400 <= error["statusCode"] <= 599,
              "native failure lost its typed error")
        require_refusal(error, code, owner_unavailable=owner_unavailable)
    else:
        # Human denial is an actual engine lifecycle decision, not an owning
        # module Error. The later execute closed-code is checked separately.
        proposals = [row.get("payload") for row in initial if row.get("kind") == "proposalCreated"]
        _need(code == "weaver.proposal.closed" and proposal_id and len(proposals) == 1 and human_denial.get("id") == proposal_id
              and human_denial.get("sessionId") == session_id and human_denial.get("status") == "denied"
              and same(human_denial.get("preparedInvocation"), proposals[0].get("preparedInvocation"))
              and error is None and result.get("payload") == {"typeId": "weaver.operation-outcome", "schemaVersion": "1", "data": {
                  "status": "denied", "InvocationId": proposals[0]["preparedInvocation"]["invocationId"]}},
              "provider denial differs from the native human decision and exact invocation")
    if proposal_id is None:
        native_results = [row.get("payload") for row in terminal if row.get("kind") == "contributedResult" and row.get("resultKind") == "moduleResult"]
        completed = [row.get("payload") for row in terminal if row.get("kind") == "toolCallCompleted"]
        _need(len(native_results) == len(completed) == 1 and same(native_results[0], result)
              and completed[0].get("toolCallId") == call_id and completed[0].get("toolName") == tool
              and completed[0].get("succeeded") is False and same(completed[0].get("result"), result),
              "native SSE completion differs from the reopened typed refusal")
    else:
        proposals = [row.get("payload") for row in initial if row.get("kind") == "proposalCreated"]
        _need(len(proposals) == 1 and proposals[0].get("id") == proposal_id
              and proposals[0].get("preparedInvocation", {}).get("toolCallId") == call_id
              and proposals[0]["preparedInvocation"].get("turnId") == initial_starts[0]["turnId"]
              and [row.get("kind") for row in initial if row.get("kind") in ("completed", "error", "turnCancelled", "toolApprovalRequested")] == ["toolApprovalRequested"],
              "refusal continuation lost its original reviewed native turn/call")
    rows = [row for row in trace if row.get("data", {}).get("commandId") == command_id]
    lifecycle = [row for row in rows if row.get("kind") in (provider + ".request", provider + ".call", provider + ".return", provider + ".continuation")]
    kinds = [row.get("kind") for row in lifecycle]
    expected_kinds = (["copilot.request", "copilot.call", "copilot.return"] +
                      (["copilot.request", "copilot.continuation"] if proposal_id else [])) if provider == "copilot" else [
                          "anthropic.request", "anthropic.call", "anthropic.request", "anthropic.continuation"]
    _need(provider in PROVIDERS and kinds == expected_kinds, "missing/duplicate actual refusal call or continuation")
    request, call = lifecycle[0]["data"], lifecycle[1]["data"]
    _need(sum(row.get("name") == tool for row in request.get("tools", [])) == 1 and call.get("toolName") == tool
          and same(call.get("arguments"), arguments), "refusal advertisement/arguments differ from selected command")
    if provider == "copilot":
        _need(request.get("sessionId") == session_id, "refusal SDK request belongs to another native session")
        returned = lifecycle[2]["data"].get("result")
        _need(lifecycle[2]["data"].get("toolName") == tool and isinstance(returned, dict), "missing actual SDK return")
        if proposal_id is None:
            _need(returned.get("status") == "denied" and returned.get("executed") is False and same(returned.get("error"), error),
                  "SDK denied return differs from native typed refusal")
        else:
            _need(returned.get("status") == "awaiting-approval" and returned.get("executed") is False
                  and returned.get("proposalId") == proposal_id, "SDK pause belongs to another native proposal")
            _need(all(row["data"].get("sessionId") == session_id for row in lifecycle if row["kind"] in ("copilot.request", "copilot.continuation")),
                  "refusal continuation changed the actual SDK session")
            values = lifecycle[-1]["data"].get("results")
            _need(isinstance(values, list) and len(values) == 1 and same(_normalized_result(values[0]), _normalized_result(result)),
                  "actual SDK continuation lost or changed the native refusal")
    else:
        _need(call_id == command_id and not _current_function_results(request), "Anthropic refusal reused a historical/current result")
        values = lifecycle[-1]["data"].get("results")
        _need(isinstance(values, list) and len(values) == 1 and values[0].get("kind") == "functionResult"
              and values[0].get("callId") == command_id and isinstance(values[0].get("result"), str)
              and same(_current_function_results(lifecycle[-2]["data"]), values), "Anthropic current function-result binding differs")
        _need(same(_normalized_result(strict_json(values[0]["result"])), _normalized_result(result)),
              "Anthropic continuation lost or changed the native refusal")
    return result


def native_frames(record, origin, session_id):
    _need(record.get("origin") == origin and record.get("method") == "GET" and record.get("status") == 200
          and record.get("path") == "/_elsa/weaver/sessions/" + session_id + "/stream"
          and not record.get("errorType") and record.get("headers", {}).get("content-type", "").split(";", 1)[0].strip() == "text/event-stream",
          "wrong native refusal stream binding")
    values = frames_from_raw(record.get("rawBody"))
    _need(same(values, [row.get("value") for row in record.get("frames", [])]), "native frame summary differs from retained raw SSE")
    return values


def frames_from_raw(raw):
    _need(isinstance(raw, str) and raw and raw.endswith(("\n\n", "\r\n\r\n")), "missing or incomplete native SSE bytes")
    rows = []
    for frame in raw.replace("\r\n", "\n").split("\n\n")[:-1]:
        lines = [line[5:].removeprefix(" ") for line in frame.splitlines() if line.startswith("data:")]
        if not lines: continue
        value = strict_json("\n".join(lines))
        _need(isinstance(value, dict) and isinstance(value.get("id"), str) and value["id"] and isinstance(value.get("kind"), str), "malformed native frame")
        rows.append(value)
    _need(rows, "empty native SSE cannot qualify a refusal")
    return rows


def _owned(root, ref):
    return retained_json(root, ref)


def _http(root, ref, origin, method, path):
    record = _owned(root, ref)
    _need(record.get("origin") == origin and record.get("method") == method and record.get("path") == path
          and type(record.get("status")) is int and not record.get("transportErrorType"), "wrong native HTTP binding")
    return record, json.loads(record["rawBody"])


def _native(root, ref, origin, path):
    receipt = _owned(root, ref)
    _need(receipt.get("origin") == origin and receipt.get("path") == path and receipt.get("method") == "GET" and receipt.get("status") == 200,
          "wrong owning GET receipt")
    return _owned(root, receipt["body"])


def _layout_without_visibility(value):
    result = copy.deepcopy(value)
    result.pop("revision", None); result.pop("updatedAt", None)
    widgets = result["value"]["widgets"]
    rows = [row for row in widgets if row.get("id") == WIDGET]
    _need(len(rows) == 1, "native layout has no unique widget")
    rows[0].pop("visible")
    return result


def require_case(value, root, source, bindings):
    """Re-derive semantic claims from retained wire/owner records, never summary flags."""
    _need(isinstance(value, dict) and value.get("source") == source and value.get("bindings") == bindings
          and value.get("status") == "passed" and value.get("id") in EXPECTED_CASES, "wrong case/source/driver binding")
    provider, name = value["id"].split("/", 1)
    expected = SCENARIOS[name]
    _need(value.get("scenario") == expected and value.get("provider") == provider and value.get("liveProvider") is False
          and value.get("evidenceCategory") == EVIDENCE_CATEGORY, "wrong case meaning or evidence category")
    if expected["condition"] in ("browser", "browser-failed", "browser-typed", "nested-removal"):
        return require_browser_case(value, root, expected)
    refs, origin = value["refs"], value["backendOrigin"]
    before = {key: _native(root, refs[key + "Before"], origin, path) for key, path in
              (("widget", OWNER), ("layout", HOME_PREFERENCES_PATH), ("checks", OWNER + "/checks"))}
    after = {key: _native(root, refs[key + "After"], origin, path) for key, path in
             (("widget", OWNER), ("layout", HOME_PREFERENCES_PATH), ("checks", OWNER + "/checks"))}
    stream = _owned(root, refs["stream"])
    frames = native_frames(stream, origin, value["sessionId"])
    trace = _owned(root, refs["trace"])
    _need(trace.get("incompleteTailBytes") == 0, "incomplete actual adapter trace")
    condition, tool = expected["condition"], expected["tool"]
    if condition != "cancelled" and not (condition == "typed" and expected["channel"] == "context"):
        _called_once(trace["records"], provider, value["commandId"], tool)
    else:
        _need(not any(row.get("kind") in (provider + ".call", provider + ".return") for row in trace["records"]), "denied context reached an adapter")
        if condition != "cancelled":
            _need(not any(row.get("kind", "").startswith(provider + ".") for row in trace["records"]), "required context refusal disclosed a request to the provider")
    command = _owned(root, refs["command"])
    _need(command == {"commandId": value["commandId"], "toolName": tool, "arguments": value["arguments"]}, "wrong selected command")
    if condition == "typed" and expected["channel"] == "authorization":
        for kind in ("protocol.gate.waiting", "protocol.gate.released"):
            _need(any(row.get("kind") == kind and row.get("data", {}).get("commandId") == value["commandId"]
                      and row["data"].get("epoch") == value["gateEpoch"] for row in trace["records"]), "authorization fault did not cross its actual pre-call gate")
    if "execute" in refs:
        response, execution = _http(root, refs["execute"], origin, "POST", "/_elsa/weaver/proposals/" + value["proposalId"] + "/execute")
    else:
        response, execution = None, None
    enabled = dict.fromkeys(CLASSES, True)
    if condition == "unconfigured": enabled = dict.fromkeys(CLASSES, False)
    if condition == "administrative-only": enabled.update(irrecoverable=False, activation=False)
    if condition.startswith("missing-"): enabled[condition.removeprefix("missing-")] = False
    refused = (not all(enabled[c] for c in TOOLS[tool]) or condition in ("approval-disabled", "execution-disabled", "denied")
               or condition in ("cancelled", "stale") or condition == "typed" and expected["channel"] != "receipt")
    _need(value["outcome"] == ("refused" if refused else "effect"), "outcome does not match the fixed matrix condition")
    bootstrap, policy = _http(root, refs["bootstrap"], origin, "GET", "/_elsa/weaver/bootstrap")
    _need(bootstrap["status"] == 200 and policy["data"]["privileges"] == enabled, "wrong live bootstrap class condition")
    proposals = [frame.get("payload") for frame in frames if frame.get("kind") == "proposalCreated"]
    if value.get("prepared") is not None:
        _need(len(proposals) == 1 and proposals[0].get("id") == value["proposalId"]
              and same(proposals[0].get("preparedInvocation"), value["prepared"]), "prepared summary is not the native proposal")
    else: _need(not proposals, "pre-review refusal produced a proposal")
    if condition == "cancelled":
        _, cancelled = _http(root, refs["cancel"], origin, "POST", "/_elsa/weaver/sessions/" + value["sessionId"] + "/turns/" + value["turnId"] + "/cancel")
        _need(cancelled.get("data", {}).get("cancelled") is True and cancelled["data"]["turnId"] == value["turnId"]
              and any(frame.get("kind") == "turnCancelled" for frame in frames) and same(before, after), "wrong cancellation cause or owner change")
        _need(any(row.get("kind") == "protocol.gate.cancelled" and row.get("data", {}).get("commandId") == value["commandId"]
                  and row["data"].get("epoch") == value["gateEpoch"] for row in trace["records"]), "missing actual gate cancellation")
        _need(not any(row.get("kind") in (provider + ".call", provider + ".return") for row in trace["records"]), "cancelled tool was dispatched")
        _need(all(ref in value["evidence"] for ref in refs.values()), "cancellation omitted evidence")
        for ref in value["evidence"]: _owned_file(root, ref)
        return value
    if value["outcome"] == "refused":
        _need(same(before, after), "refused action changed native owner or journal")
        require_refusal(execution if execution is not None else frames, value["expectedCode"],
                        owner_unavailable=expected.get("ownerUnavailable") if condition == "typed" else None)
        if condition != "typed":
            _need(value["expectedCode"] == ("weaver.proposal.closed" if condition == "denied" else "acme.home.effects.stale" if condition == "stale" else POLICY_CODE), "self-selected wrong policy cause")
        else:
            _need(value["expectedCode"] == _typed_code(expected), "wrong typed normalization cause")
        if not (condition == "typed" and expected["channel"] == "context"):
            terminal = native_frames(_owned(root, refs["continuation"]), origin, value["sessionId"]) if execution is not None else frames
            session_response, reopened = _http(root, refs["session"], origin, "GET", "/_elsa/weaver/sessions/" + value["sessionId"])
            _need(session_response["status"] == 200 and reopened.get("error") is None, "refused session could not be reopened")
            human_denial = None
            if condition == "denied":
                denied_response, denied = _http(root, refs["decision"], origin, "POST", "/_elsa/weaver/proposals/" + value["proposalId"] + "/deny")
                _need(denied_response["status"] == 200 and denied.get("error") is None, "human denial was not accepted natively")
                human_denial = denied.get("data")
            result = require_called_refusal(trace["records"], provider, value["commandId"], tool, value["arguments"], value["sessionId"],
                                           frames, terminal, value["expectedCode"], reopened.get("data"),
                                           owner_unavailable=expected.get("ownerUnavailable", False), proposal_id=value["proposalId"], human_denial=human_denial)
            if execution is not None and condition != "denied":
                _need(any(same(item, result["error"]) for item in require_refusal(execution, value["expectedCode"])),
                      "native execute error differs from its retained/provider refusal")
    else:
        _need(response is not None and response["status"] == 200 and execution.get("error") is None, "successful delivery missing")
        prepared = value["prepared"]
        continuation = _owned(root, refs["continuation"])
        _need(continuation.get("status") == 200 and continuation.get("origin") == origin and not continuation.get("errorType")
              and any(frame.get("value", {}).get("kind") == "completed" for frame in continuation.get("frames", []))
              and not any(frame.get("value", {}).get("kind") == "error" for frame in continuation.get("frames", [])), "result journey did not finish its actual native continuation")
        require_adapter_result(trace["records"], provider, value["commandId"], execution["data"]["toolResult"])
        _need(prepared.get("toolName") == tool and sorted(prepared.get("requiredPrivilegeClasses") or []) == sorted(TOOLS[tool]), "frozen effective classes differ")
        if tool == CHECK_TOOL:
            result = require_performed(prepared, execution["data"], before["widget"], after["widget"], before["layout"], after["layout"], before["checks"], after["checks"])
            _need(result["passed"] is (condition != "business-failed"), "wrong business condition")
        else:
            require_visibility(prepared, execution["data"], value["arguments"], before["widget"], after["widget"], before["layout"], after["layout"], before["checks"], after["checks"])
        if condition == "typed":
            _need(execution["data"]["toolResult"]["receipt"]["moduleCode"] == "module.code.unsupported", "receipt code was not replaced")
        if condition == "duplicate-delivery":
            _, duplicate = _http(root, refs["duplicate"], origin, "POST", "/_elsa/weaver/proposals/" + value["proposalId"] + "/execute")
            _need(same(execution, duplicate), "duplicate delivery did not retain the original outcome")
    # Search the exact retained bytes, including raw HTTP/SSE, adapter payloads and audit.
    _need(all(ref in value["evidence"] for ref in refs.values()) and value.get("canarySearch", {}).get("found") == 0
          and value["canarySearch"].get("files") == len(value["evidence"]), "canary search omitted required evidence")
    audit = _owned(root, refs["audit"])
    audit_url = urlsplit(audit.get("path", ""))
    _need(audit.get("origin") == origin and audit.get("method") == "GET" and audit.get("status") == 200
          and audit_url.path == "/_elsa/weaver/audit" and parse_qs(audit_url.query) == {"take": ["100"], "sessionId": [value["sessionId"]]}, "missing session-bound audit read")
    if condition == "typed" and expected["channel"] in ("authorization", "preparation"):
        starts = [frame["payload"] for frame in frames if frame.get("kind") == "toolCallStarted"]
        events = json.loads(audit["rawBody"]).get("data")
        _need(len(starts) == 1 and isinstance(events, list) and any(row.get("kind") == "toolInvoked" and row.get("sessionId") == value["sessionId"]
              and row.get("metadata", {}).get("toolCallId") == starts[0]["toolCallId"] and row["metadata"].get("toolName") == tool
              and row["metadata"].get("outcome") == "denied" for row in events), "missing matching native authorization/preparation denial audit")
    for ref in value["evidence"]:
        path = _owned_file(root, ref)
        if path.suffix == ".json":
            raw = path.read_text()
            require_canary_absent(raw, value["canarySearch"]["sha256"])
            require_canary_absent(strict_json(raw), value["canarySearch"]["sha256"])
    return value


def _owned_file(root, ref):
    path = Path(ref["path"])
    _need(path.is_absolute() and path.resolve().is_relative_to(Path(root).resolve()) and path.is_file() and sha(path) == ref["sha256"], "missing or changed case evidence")
    return path


def require_canary_absent(value, digest):
    """Recheck the retained channels without exporting the private synthetic canary."""
    _need(isinstance(digest, str) and re.fullmatch(r"[a-f0-9]{64}", digest), "missing private-canary digest binding")
    if isinstance(value, dict):
        for key, item in value.items():
            require_canary_absent(key, digest); require_canary_absent(item, digest)
    elif isinstance(value, list):
        for item in value: require_canary_absent(item, digest)
    elif isinstance(value, str):
        for match in re.finditer(r"(?=([a-f0-9]{48}))", value):
            _need(hashlib.sha256(match[1].encode()).hexdigest() != digest, "private typed canary appeared in retained disclosure")


def manifest_gaps(section, source=None):
    if not isinstance(section, dict): return ["The mandatory story1 module-effects matrix is absent"]
    ids = section.get("caseIDs", [])
    records = section.get("records", [])
    stages = section.get("stages", [])
    if (section.get("status") != "passed" or section.get("source") != source or section.get("evidenceCategory") != EVIDENCE_CATEGORY
            or section.get("liveProvider") is not False or ids != list(EXPECTED_CASES)
            or [row.get("id") for row in records if isinstance(row, dict)] != ids
            or len(records) != len(EXPECTED_CASES) or not all(row.get("status") == "passed" and isinstance(row.get("record"), dict) for row in records)
            or [stage.get("name") for stage in stages if isinstance(stage, dict)] != list(EXPECTED_CASES)
            or not all(isinstance(stage, dict) and stage.get("status") == "completed" and stage.get("boundSeconds") == 180 for stage in stages)):
        return ["Every source-bound story1 module-effects case must pass exactly once on both actual adapters"]
    return []


def require_effects(section, root, *, source):
    _need(not manifest_gaps(section, source), "incomplete module-effects matrix")
    _need(section.get("bindings") == source_bindings(), "fixture/driver source hashes differ")
    for row in section["records"]:
        value = _owned(root, row["record"])
        _need(value.get("id") == row["id"], "retained case id differs")
        require_case(value, root, source, section["bindings"])
    return section["records"]


def restart_manifest_gaps(section, source=None):
    # Deliberately fail closed until T038 delivers its real durable/old-writer
    # validator. A fabricated passed section cannot stand in for that implementation.
    return ["T038 source-bound story1 upgrade/recovery qualification remains required"]


def _snapshot(runtime, name):
    refs, values = {}, {}
    for kind, path in (("widget", OWNER), ("layout", HOME_PREFERENCES_PATH), ("checks", OWNER + "/checks")):
        values[kind], _, refs[kind] = runtime.snapshot(name + "-" + kind, endpoint=path)
    return values, refs


def _policy(runtime, enabled, *, unconfigured=False):
    if unconfigured:
        path = owned_content_root(runtime.run, runtime.run.profiles["backend"]) / "appsettings.json"
        _need(path.is_file() and not path.is_symlink() and path.resolve().is_relative_to(runtime.run.root.resolve()), "operator configuration escaped owned backend")
        settings = json.loads(path.read_text())
        settings.setdefault("Elsa", {}).setdefault("Weaver", {}).pop("Privileges", None)
        atomic_json(path, settings)
    else: configure_weaver_privileges(runtime.run, **dict(zip(CLASSES, enabled)))
    deadline = time.monotonic() + 15
    while True:
        response = runtime.transport("effects-bootstrap-policy").bootstrap().require_status(200)
        if response.json()["data"].get("privileges") == dict(zip(CLASSES, enabled)): return response.receipt
        _need(time.monotonic() < deadline, "live bootstrap never observed the configured host classes")
        time.sleep(0.1)


def _attachment(runtime, state):
    value = runtime.attachment(state)
    value.update(contentType=KIND, scope=ROUTE, content=dict(state))
    return value


@contextmanager
def _bounded(runtime, name):
    _need(threading.current_thread() is threading.main_thread() and signal.getitimer(signal.ITIMER_REAL) == (0.0, 0.0), "requires an unoccupied interruptible main-thread stage")
    stage = {"name": name, "status": "running", "boundSeconds": 180}
    runtime.run.report[SECTION]["stages"].append(stage); runtime.run.save()
    started = time.monotonic()
    def expired(_signum, _frame): raise TimeoutError("Module-effects stage deadline; no action replay: " + name)
    previous = signal.signal(signal.SIGALRM, expired); signal.setitimer(signal.ITIMER_REAL, 180)
    try:
        yield
        stage["status"] = "completed"
    except BaseException:
        stage["status"] = "failed"
        raise
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0); signal.signal(signal.SIGALRM, previous)
        stage["elapsedSeconds"] = time.monotonic() - started; runtime.run.save()


def _native_case(runtime, name, spec, fault_file):
    condition, tool = spec["condition"], spec["tool"]
    enabled = [True] * 3
    if condition == "unconfigured": enabled = [False] * 3
    if condition == "administrative-only": enabled = [True, False, False]
    if condition.startswith("missing-"): enabled[CLASSES.index(condition.removeprefix("missing-"))] = False
    bootstrap = _policy(runtime, enabled, unconfigured=condition == "unconfigured")
    runtime.sign_in()
    before, before_refs = _snapshot(runtime, "effects-before")
    arguments = {"expectedVisible": before["widget"]["visible"] if condition != "business-failed" else not before["widget"]["visible"]} if tool == CHECK_TOOL else {"visible": not before["widget"]["visible"]}
    canary = secrets.token_hex(24)
    authorization_gate = condition == "typed" and spec["channel"] == "authorization"
    if condition == "typed" and not authorization_gate:
        atomic_json(fault_file, {"channel": spec["channel"], "code": "outside.declared." + canary, "message": "Unsupported module diagnostic " + canary, "ownerUnavailable": spec["ownerUnavailable"]})
    command_id, epoch = str(uuid.uuid4()), str(uuid.uuid4())
    held = condition == "cancelled" or authorization_gate
    if held: atomic_json(runtime.gate_file, {"commandId": command_id, "epoch": epoch, "state": "hold", "timeoutMilliseconds": 60000})
    turn = runtime.turn("effects-" + condition, tool=tool, arguments=arguments, attachment=_attachment(runtime, before["widget"]), capability="acme.home.effects",
                        command_id=command_id, drain=not held)
    turn.message.require_status(200)
    cancelled = None
    turn_id = None
    if held:
        completed = False
        try:
            with turn.transport.stream(turn.session) as stream:
                started = stream.until(lambda frame: frame.value["kind"] == "started")
                turn_id = started.value["payload"]["turnId"]
                deadline = time.monotonic() + 30
                while True:
                    waiting = turn.transport.protocol_trace(turn.offset)
                    if any(row.get("kind") == "protocol.gate.waiting" and row.get("data", {}).get("commandId") == command_id
                           and row["data"].get("epoch") == epoch for row in waiting["records"]): break
                    _need(time.monotonic() < deadline, "actual pre-call gate never waited")
                    time.sleep(0.02)
                if authorization_gate:
                    # Offering calls owner authorization too. Inject only after the
                    # real adapter offered/captured the tool and entered its existing
                    # pre-call gate, so refusal proves the actual invocation boundary.
                    atomic_json(fault_file, {"channel": "authorization", "code": "outside.declared." + canary,
                        "message": "Unsupported module diagnostic " + canary, "ownerUnavailable": spec["ownerUnavailable"]})
                    atomic_json(runtime.gate_file, {"commandId": command_id, "epoch": epoch, "state": "release"})
                    stream.until_turn_end(turn_id=turn_id)
                else:
                    cancelled = turn.transport.cancel(turn.session, turn_id).require_status(200)
                    _need(stream.until_turn_end(turn_id=turn_id).value["kind"] == "turnCancelled", "turn was not cancelled")
            turn.stream = stream
            turn.evidence.append(stream.receipt)
            if cancelled: turn.evidence.append(cancelled.receipt)
            completed = True
        finally:
            # Failure leaves this one command held until its cancelling/timeout
            # boundary; releasing an unproved command could execute a late effect.
            if completed: atomic_json(runtime.gate_file, {"commandId": command_id, "epoch": epoch, "state": "release"})
    _need(turn.stream is not None, "native turn produced no stream")
    refs = {kind + "Before": ref for kind, ref in before_refs.items()}
    refs.update(bootstrap=bootstrap, stream=turn.stream.receipt,
                command=next(ref for ref in turn.evidence if Path(ref["path"]).name == "selected-command.json"))
    if cancelled: refs["cancel"] = cancelled.receipt
    refusal = condition == "cancelled" or not all(enabled[CLASSES.index(c)] for c in TOOLS[tool]) or condition == "typed" and spec["channel"] in ("authorization", "preparation", "context")
    if not refusal:
        _need(turn.proposal is not None and turn.proposal.get("status") == "awaitingApproval", "no actual pending review")
        prepared = turn.proposal["preparedInvocation"]
        if condition == "approval-disabled":
            _policy(runtime, [False] * 3)
            decision = turn.transport.approve(turn.proposal["id"], revision=turn.proposal["baseRevision"])
            refs["decision"] = decision.receipt
            refusal = True
        elif condition == "denied":
            refs["decision"] = turn.transport.deny(turn.proposal["id"]).require_status(200).receipt
            refusal = True
        else:
            runtime.approve(turn)
            if condition == "execution-disabled": _policy(runtime, [False] * 3)
            if condition == "stale":
                changed = copy.deepcopy(before["layout"]["value"]); changed["independentEdit"] = secrets.token_hex(16)
                _, write = runtime.native_write("effects-independent-edit", runtime.backend, changed, revision=before["layout"]["revision"])
                turn.evidence.append(write["receipt"])
                before, current_refs = _snapshot(runtime, "effects-stale-current")
                refs.update({kind + "Before": ref for kind, ref in current_refs.items()})
            if condition == "typed" and spec["channel"] == "execution": refusal = True
            if condition in ("execution-disabled", "stale"): refusal = True
        response = runtime.execute(turn)
        refs["execute"] = response.receipt
        if not refusal:
            response.require_status(200)
            if condition == "duplicate-delivery": refs["duplicate"] = runtime.execute(turn).require_status(200).receipt
    else:
        prepared = None
    # Removing a private control never retries a tool; audit/disclosure read the retained outcome.
    fault_file.unlink(missing_ok=True)
    if "execute" in refs:
        continuation = runtime.settled(turn)
        refs["continuation"] = continuation.receipt
    refs["session"] = turn.transport.session(turn.session).require_status(200).receipt
    audit = turn.transport.audit(turn.session).require_status(200)
    trace = turn.transport.protocol_trace(turn.offset); refs["trace"] = trace["receipt"]; refs["audit"] = audit.receipt
    after, after_refs = _snapshot(runtime, "effects-after")
    refs.update({kind + "After": ref for kind, ref in after_refs.items()})
    if condition == "approval-disabled":
        # Approval's terminal failure must also be retained by subsequent execute.
        refusal = True
    expected_code = ("weaver.proposal.closed" if condition == "denied" else POLICY_CODE)
    if condition == "stale": expected_code = "acme.home.effects.stale"
    if condition == "typed": expected_code = _typed_code(spec)
    evidence = [*turn.evidence, *refs.values()]
    for reference in evidence:
        _need(canary.encode() not in _owned_file(runtime.run.root, reference).read_bytes(), "canary appeared in HTTP/SSE/adapter/result/audit evidence")
    return {"id": name, "source": runtime.run.source, "provider": runtime.provider, "scenario": spec, "status": "passed",
            "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False, "bindings": source_bindings(), "backendOrigin": runtime.backend_origin,
            "sessionId": turn.session, "turnId": turn_id, "gateEpoch": epoch, "commandId": turn.command_id, "arguments": arguments, "proposalId": (turn.proposal or {}).get("id"),
            "prepared": prepared, "outcome": "refused" if refusal else "effect", "expectedCode": expected_code,
            "refs": refs, "evidence": evidence, "canarySearch": {"files": len(evidence), "found": 0, "sha256": hashlib.sha256(canary.encode()).hexdigest()}}


def require_browser_case(value, root, spec):
    browser = _owned(root, value["browser"])
    _need(browser.get("status") == "passed" and not browser.get("failure") and not browser.get("pageErrors")
          and browser.get("journey") == "module-effects" and browser.get("provider") == value["provider"], "browser journey incomplete")
    _need(browser.get("effectsScenario") == spec and isinstance(browser.get("effects"), dict), "browser case binding differs")
    captures = browser.get("effectsCaptures")
    _need(isinstance(captures, dict) and set(captures) == {"review", "result", "accessibility"}, "missing feature-specific visual/a11y captures")
    for ref in [*captures.values(), *browser["evidence"]]: _owned_file(root, ref)
    trace = _owned(root, value["trace"])
    _need(trace.get("incompleteTailBytes") == 0, "browser adapter trace is incomplete")
    if spec["condition"] == "browser-typed":
        digest = value.get("canarySha256")
        _need(browser.get("typedCanarySearch", {}).get("canarySha256") == digest, "browser canary differs from its private input")
        sent = _owned(root, next(ref for ref in browser["evidence"] if Path(ref["path"]).name == "effects-message.json"))
        url = urlsplit(sent["url"])
        _need(sent.get("status") == 200 and url.scheme + "://" + url.netloc == value["backendOrigin"]
              and re.fullmatch(r"/_elsa/weaver/sessions/[^/]+/messages", url.path), "typed browser has no exact native session")
        session_id = url.path.split("/")[-2]
        raw_audit = _owned(root, browser["effectsRefs"]["typedAuditRaw"])
        audit_url = urlsplit(raw_audit.get("url", ""))
        _need(audit_url.scheme + "://" + audit_url.netloc == value["backendOrigin"] and audit_url.path == "/_elsa/weaver/audit"
              and parse_qs(audit_url.query) == {"take": ["100"], "sessionId": [session_id]} and raw_audit.get("status") == 200
              and isinstance(raw_audit.get("rawBody"), str) and raw_audit["rawBody"]
              and same(strict_json(raw_audit["rawBody"]), raw_audit.get("value"))
              and same(_owned(root, browser["effectsRefs"]["typedAudit"]).get("value"), raw_audit["value"]),
              "typed audit raw/decoded/current-session evidence differs")
        # The in-browser gate searches unredacted raw and decoded traffic. Here
        # independently recheck all retained channels, including APIRequestContext
        # audit reads that never pass through page.on(response).
        for ref in browser["evidence"]:
            path = _owned_file(root, ref)
            if path.suffix == ".json":
                raw = path.read_text(); require_canary_absent(raw, digest)
                require_canary_absent(strict_json(raw), digest)
        require_canary_absent(trace, digest)
    if spec["condition"] == "nested-removal":
        _need(browser["effects"].get("removedNestedWidget") is True and browser["effects"].get("unrelatedSupportRetained") is True, "nested module removal not witnessed")
        native = browser["effects"]
        _need(native["before"]["visible"] is True and native["after"] == native["before"], "nested removal changed the owner instead of unloading its presentation")
        _need(native["registryBefore"].get("complete") is True and native["registryAfter"].get("complete") is True,
              "incomplete module discovery cannot establish absence")
        before_modules = {row["id"]: row for row in native["registryBefore"]["modules"]}
        after_modules = {row["id"]: row for row in native["registryAfter"]["modules"]}
        _need(MODULE in before_modules and before_modules[MODULE]["status"] == "available"
              and after_modules.get(MODULE, {}).get("status") in (None, "disabled")
              and all(after_modules.get(key) == row for key, row in before_modules.items() if key != MODULE), "nested removal altered unrelated registered modules")
        for label in ("registryBefore", "registryAfter"):
            registry = _owned(root, browser["effectsRefs"][label])
            _need(registry.get("endpoint") == "/_elsa/studio/module-registry" and registry.get("status") == 200
                  and registry.get("value") == native[label], "nested registry projection is not its owning GET")
    elif spec["condition"] == "browser-typed" and spec["channel"] != "receipt":
        data = browser["effects"]
        if spec["channel"] != "context": _called_once(trace["records"], value["provider"], data["commandId"], spec["tool"])
        else: _need(not any(row.get("kind", "").startswith(value["provider"] + ".") for row in trace["records"]), "refused browser context reached a provider")
        _need(data["before"] == data["after"] and data["layoutBefore"] == data["layoutAfter"] and data["checksBefore"] == data["checksAfter"], "typed browser refusal changed the native owner")
        message = _owned(root, next(ref for ref in browser["evidence"] if Path(ref["path"]).name == "effects-message.json"))
        url = urlsplit(message["url"])
        _need(message.get("status") == 200 and url.scheme + "://" + url.netloc == value["backendOrigin"]
              and re.fullmatch(r"/_elsa/weaver/sessions/[^/]+/messages", url.path), "browser message has no exact native session binding")
        session_id = url.path.split("/")[-2]
        streams = _owned(root, next(ref for ref in browser["evidence"] if Path(ref["path"]).name == "weaver-streams.json"))
        _need(isinstance(streams, list) and streams, "no actual browser native streams")
        grouped = []
        for stream in streams:
            _need(stream.get("url") == value["backendOrigin"] + "/_elsa/weaver/sessions/" + session_id + "/stream"
                  and stream.get("status") == 200 and stream.get("captureError") is None and stream.get("captureValidationError") is None
                  and stream.get("trailingIncompleteUtf8Bytes") == 0 and stream.get("textTruncation") == "none", "incomplete/foreign browser refusal stream")
            grouped.append(frames_from_raw(stream.get("rawBody")))
        frames = [frame for group in grouped for frame in group]
        if spec["channel"] == "execution":
            delivered = _owned(root, browser["effectsRefs"]["execute"])
            _need(delivered.get("status") in (400, 403, 409, 503), "typed execution had no native refusal response")
            frames.append(delivered.get("response"))
        require_refusal(frames, _typed_code(spec), owner_unavailable=spec["ownerUnavailable"])
        if spec["channel"] != "context":
            reviewed = spec["channel"] == "execution"
            _need(len(grouped) == (2 if reviewed else 1), "browser refused turn/continuation count differs")
            reopened = _owned(root, browser["effectsRefs"]["refusalSession"])
            _need(reopened.get("url") == value["backendOrigin"] + "/_elsa/weaver/sessions/" + session_id
                  and reopened.get("status") == 200 and reopened.get("value", {}).get("error") is None, "wrong browser refusal reopen")
            proposal_id = next((frame.get("payload", {}).get("id") for frame in grouped[0] if frame.get("kind") == "proposalCreated"), None) if reviewed else None
            arguments = data.get("prepared", {}).get("arguments") if reviewed else {"expectedVisible": data["before"]["visible"]}
            result = require_called_refusal(trace["records"], value["provider"], data["commandId"], spec["tool"], arguments, session_id,
                                           grouped[0], grouped[-1], _typed_code(spec), reopened["value"].get("data"),
                                           owner_unavailable=spec["ownerUnavailable"], proposal_id=proposal_id)
            if reviewed:
                _need(same(delivered.get("response", {}).get("error"), result["error"]), "browser execute differs from retained refusal")
        if spec["channel"] in ("authorization", "preparation"):
            audit = _owned(root, browser["effectsRefs"]["typedAudit"]).get("value", {}).get("data")
            starts = [frame["payload"] for frame in frames if frame.get("kind") == "toolCallStarted"]
            _need(len(starts) == 1 and isinstance(audit, list) and any(row.get("kind") == "toolInvoked"
                  and row.get("metadata", {}).get("toolCallId") == starts[0]["toolCallId"] and row["metadata"].get("toolName") == spec["tool"]
                  and row["metadata"].get("outcome") == "denied" for row in audit), "browser typed denial omitted its matching native audit")
        if spec["channel"] == "authorization":
            for kind in ("protocol.gate.waiting", "protocol.gate.released"):
                _need(any(row.get("kind") == kind and row.get("data", {}).get("commandId") == data["commandId"]
                          and row["data"].get("epoch") == browser.get("effectsGateEpoch") for row in trace["records"]), "browser authorization fault missed the actual invocation gate")
        _need(browser.get("typedCanarySearch", {}).get("found") == 0 and browser["typedCanarySearch"].get("rawWeaverTraffic") is True
              and browser["typedCanarySearch"].get("reviewCard") is True and browser["typedCanarySearch"].get("audit") is True, "typed browser omitted a canary channel")
    else:
        data = browser["effects"]
        _called_once(trace["records"], value["provider"], data["commandId"], spec["tool"])
        correlation = _owned(root, value["protocolCorrelation"])
        _need(correlation.get("provider") == value["provider"] and correlation.get("liveProvider") is False
              and len(correlation.get("correlations", [])) == 1 and correlation["correlations"][0].get("commandId") == data["commandId"]
              and correlation["correlations"][0].get("actualResultObserved") is True, "missing actual browser/adapter/native-result correlation")
        _need(data.get("toolName") == spec["tool"] and data.get("stableCardIdentity") is True, "wrong reviewed card/tool")
        for field in ("before", "after", "layoutBefore", "layoutAfter", "checksBefore", "checksAfter"):
            receipt = _owned(root, browser["effectsRefs"][field])
            _need(receipt.get("status") == 200 and same(receipt.get("value"), data[field]), "browser owner projection differs from its GET")
        delivery = _owned(root, browser["effectsRefs"]["execute"])
        decision = _owned(root, browser["effectsRefs"]["approve"])
        _need(delivery.get("status") == decision.get("status") == 200 and same(delivery.get("response", {}).get("data"), data["execution"])
              and same(decision.get("response", {}).get("data", {}).get("preparedInvocation"), data["prepared"]), "browser execution/preparation differs from its HTTP")
        # The same wire/owner validator is used on the browser's exact values; originals remain retained.
        if spec["tool"] == CHECK_TOOL:
            check_value = require_performed(data["prepared"], data["execution"], data["before"], data["after"], data["layoutBefore"], data["layoutAfter"], data["checksBefore"], data["checksAfter"])
            _need(check_value["passed"] is (spec["condition"] != "browser-failed") and data.get("noEnabledApply") is True, "browser check result differs")
        else:
            require_visibility(data["prepared"], data["execution"], data["prepared"]["arguments"], data["before"], data["after"], data["layoutBefore"], data["layoutAfter"], data["checksBefore"], data["checksAfter"])
        _need(sorted(data["prepared"].get("requiredPrivilegeClasses") or []) == sorted(TOOLS[spec["tool"]]), "review classes differ")
        if spec["condition"] == "browser-typed":
            _need(browser.get("typedCanarySearch", {}).get("found") == 0 and browser["typedCanarySearch"].get("rawWeaverTraffic") is True
                  and browser["typedCanarySearch"].get("audit") is True and browser["typedCanarySearch"].get("reviewCard") is True,
                  "receipt replacement omitted a browser canary channel")
    return value


def _browser_case(runtime, name, spec, fault_file):
    if spec["condition"] == "nested-removal":
        state, _, _ = runtime.state("effects-nested-precondition")
        if not state["visible"]:
            turn = runtime.turn("effects-visible-nested-precondition", tool="acme_home_admin_visibility", arguments={"visible": True},
                                attachment=_attachment(runtime, state), capability="acme.home.effects")
            _need(turn.proposal is not None, "cannot establish visible nested-widget precondition")
            runtime.approve(turn); runtime.execute(turn).require_status(200)
    runtime.sign_in()
    output = runtime.output("effects-browser")
    target = output / "observation"
    config = {"journey": "module-effects", "provider": runtime.provider, "studioOrigin": runtime.studio_origin, "backendOrigin": runtime.backend_origin,
              "storageState": runtime.active_identity["storageState"], "commandFile": str(runtime.command_file), "output": str(target), "effectsScenario": spec}
    canary = secrets.token_hex(24)
    if spec["condition"] == "browser-typed":
        if spec["channel"] != "authorization":
            atomic_json(fault_file, {"channel": spec["channel"], "code": "outside.declared." + canary, "message": "Unsupported module diagnostic " + canary, "ownerUnavailable": spec["ownerUnavailable"]})
        config["effectsCanary"] = canary  # Private input only; never included in exported browser inputs/receipts.
        config.update(effectsFaultFile=str(fault_file), effectsGateFile=str(runtime.gate_file), traceFile=str(runtime.run.profiles["backend"]["traceFiles"][runtime.provider]))
        config["effectsTraceOffset"] = Path(config["traceFile"]).stat().st_size if Path(config["traceFile"]).is_file() else 0
    private_json(output / "config-private.json", config)
    transport = runtime.transport("effects-browser-trace")
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    node = shutil.which("node")
    _need(node, "actual browser driver needs Node")
    try:
        stage_name = "effects-" + name.replace("/", "-").replace(":", "-")
        runtime.run.run(stage_name, [node, str(TOOL / "browser.mjs"), str(output / "config-private.json")], TOOL,
                        env={key: value for key, value in os.environ.items() if key in RUNTIME_ENVIRONMENT})
    finally: fault_file.unlink(missing_ok=True)
    observed = retained(target / "observation.json")
    trace = transport.protocol_trace(offset)
    _need(canary not in json.dumps(trace["records"]), "browser typed canary reached actual adapter payloads")
    result = {"id": name, "source": runtime.run.source, "provider": runtime.provider, "scenario": spec, "status": "passed", "bindings": source_bindings(),
              "backendOrigin": runtime.backend_origin, "canarySha256": hashlib.sha256(canary.encode()).hexdigest(),
              "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False, "browser": observed, "trace": trace["receipt"]}
    if spec["condition"] in ("browser", "browser-failed") or spec["condition"] == "browser-typed" and spec["channel"] == "receipt":
        correlation = correlate_browser_protocol(transport, target, runtime.provider, offset=offset)
        result["protocolCorrelation"] = correlation["receipt"]
    return result


def qualify_effects(runtime):
    run = runtime.run
    _need(SECTION not in run.report and run.report.get("independentPackageWitness") is False, "never resume or self-promote an effects matrix")
    prior = copy.deepcopy({key: value for key, value in run.report.items() if key.endswith("Qualification")
                           or key in ("cases", "witnessManifest", "expectedControls", "autonomyPreflight")})
    original_provider = runtime.provider
    section = {"source": run.source, "status": "running", "bindings": source_bindings(), "evidenceCategory": EVIDENCE_CATEGORY,
               "liveProvider": False, "caseIDs": [], "records": [], "stages": []}
    run.report[SECTION] = section; run.save()
    root = runtime.output("effects-controls")
    fault, journal = root / "fault-private.json", root / "checks-private.jsonl"
    feature = runtime.witness["backendFeature"]
    catalog, _ = runtime.native.json(runtime.backend, "effects-original-catalog", "/modularity/features")
    features = {row["id"]: row for row in catalog["features"]}
    _need(features.get(COMMON_EFFECTS_FEATURE, {}).get("enabled") is True
          and features.get(DURABLE_EFFECTS_FEATURE, {}).get("enabled") is not True, "T037 must run before durable-store selection")
    original = next(row for row in catalog["features"] if row["id"] == feature)
    _need(original["enabled"] is True, "original fixture must remain selected after the old autonomy matrix")
    config = dict(original["configuration"], EffectFaultControlFile=str(fault), CheckObservationFile=str(journal))
    runtime.apply_features("effects-configure-owner", {feature: (True, config)})
    try:
        for provider in PROVIDERS:
            runtime.select_provider(provider)
            for key, spec in SCENARIOS.items():
                name = provider + "/" + key
                section["activeCase"] = name
                with _bounded(runtime, name):
                    if spec["condition"] in ("browser", "browser-failed", "browser-typed", "nested-removal"):
                        _policy(runtime, [True] * 3)
                        value = _browser_case(runtime, name, spec, fault)
                    else: value = _native_case(runtime, name, spec, fault)
                    require_case(value, run.root, run.source, section["bindings"])
                    record = runtime.save(runtime.output("effects-case"), "observation", value)
                    section["caseIDs"].append(name); section["records"].append({"id": name, "status": "passed", "record": record})
        _need(section["caseIDs"] == list(EXPECTED_CASES), "missing or duplicate matrix case")
        _need(all(same(run.report[key], value) for key, value in prior.items()), "effects matrix changed an older qualification")
        section.pop("activeCase", None); section["status"] = "passed"
        require_effects(section, run.root, source=run.source)
    except BaseException as error:
        section.update(status="failed", failure={"type": type(error).__name__, "message": str(error)})
        run.save()
        raise
    finally:
        try:
            fault.unlink(missing_ok=True)
            _policy(runtime, [True] * 3)
            runtime.select_provider(original_provider)
            runtime.apply_features("effects-restore-owner-config", {feature: (original["enabled"], original["configuration"])})
        except BaseException as error:
            section.update(status="failed", cleanupFailure={"type": type(error).__name__, "message": str(error)})
            raise
        finally: run.save()
