"""Spec1203 T037: the independent module's effects through actual packaged adapters.

Fixture meaning belongs here, never in an adapter, host or product SDK. This section
cannot promote a witness. T038 owns the separately mandatory durable/upgrade matrix.
"""
from __future__ import annotations

from contextlib import contextmanager
import copy
import ctypes
import hashlib
import json
import os
from pathlib import Path
import secrets
import re
import shutil
import signal
import subprocess
import sys
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
CLASSED_RESTART_CASES = tuple(provider + "/" + name for provider in PROVIDERS for name in (
    *(tool + ":recovery-disabled" for tool in TOOLS), "removed-owner-pending"))
EXECUTION_HEADER = '{"Format":"acme.home.execution-entries","Version":2}'
EXECUTION_ENDPOINT = OWNER + "/execution-entries"
RESTART_SOURCE_PATHS = tuple(dict.fromkeys((*SOURCE_PATHS,
    "tools/studio/Weaver.Conformance.Probe/restart_journeys.py",
    "tools/studio/Weaver.Conformance.Probe/journey_runtime.py",
    "tools/studio/Weaver.Conformance.Probe/runtime.py",
    "tools/studio/Weaver.Conformance.Probe/consumers.py",
    "tools/studio/Weaver.Conformance.Probe/native_lifecycle.py",
    "tools/studio/Weaver.Conformance.Probe/weaver_transport.py",
    "tools/studio/Weaver.Conformance.Probe/witness.json",
    "tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Backend/AcmeHomeFeature.cs.in",
    "tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Backend/WidgetResource.cs.in",
    "tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Backend/WidgetExecutionJournal.cs.in")))


def effect_restart_bindings():
    return {name: sha(TOOL.parents[2] / name) for name in RESTART_SOURCE_PATHS}


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
            # HTTP receipts retain their actual body as a JSON string. Searching
            # the enclosing receipt alone misses escaped diagnostic text.
            if key == "rawBody" and isinstance(item, str) and item.lstrip().startswith(("{", "[")):
                require_canary_absent(strict_json(item), digest)
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


def _restart_http(root, ref, origin, method, path, status=200):
    record, value = _http(root, ref, origin, method, path)
    _need(record["status"] == status and not record.get("errorType") and isinstance(record.get("rawBody"), str)
          and record.get("rawBodySha256") == hashlib.sha256(record["rawBody"].encode()).hexdigest(), "wrong restart wire response")
    # _http predates these cases. Require strict JSON here as well as in retained envelopes.
    _need(same(value, strict_json(record["rawBody"])), "ambiguous restart wire JSON")
    return record, value


def _restart_native(root, ref, origin, path):
    record = _owned(root, ref)
    _need(record.get("origin") == origin and record.get("method") == "GET" and record.get("path") == path
          and record.get("status") == 200 and not record.get("transportFailure"), "missing native restart observation")
    return _owned(root, record["body"])


def require_execution_observation(value, *, path, owner, execution=True):
    """Decode the module's durable journal; missing/unarmed/poisoned storage never establishes zero."""
    _need(isinstance(value, dict) and value.get("status") == "observed" and value.get("sourcePath") == path
          and type(value.get("device")) is int and type(value.get("inode")) is int
          and type(value.get("mode")) is int and value["mode"] & 0o777 == 0o600
          and type(value.get("parentMode")) is int and value["parentMode"] & 0o777 == 0o700
          and value.get("failedMarker") is False and value.get("probeExists") is False,
          "journal is unarmed, poisoned, foreign or unknown")
    raw = value.get("rawBody")
    _need(isinstance(raw, str) and len(raw.encode()) <= 2 * 1024 * 1024
          and type(value.get("size")) is int and value["size"] == len(raw.encode()) and value.get("bytesSha256") == hashlib.sha256(raw.encode()).hexdigest(),
          "incomplete journal bytes")
    lines = raw.splitlines()
    if execution:
        _need(lines and lines.pop(0) == EXECUTION_HEADER, "wrong execution journal version/header")
    rows = [strict_json(line) for line in lines]
    _need(len(rows) <= 4096 and all(isinstance(row, dict) for row in rows), "invalid module journal")
    fields = ({"Actor", "Tenant", "Host", "RequestedActor", "RequestedTenant", "ToolName", "RequestedToolName", "SessionId", "TurnId",
               "InvocationId", "ToolCallId", "EntryNumber"} if execution else
              {"Actor", "Tenant", "Host", "InvocationId", "ToolCallId", "ToolName", "Revision", "Passed", "CallNumber"})
    counts, selected = {}, []
    for row in rows:
        _need(set(row) == fields, "malformed journal row")
        strings = fields - ({"EntryNumber"} if execution else {"Passed", "CallNumber"})
        _need(all(isinstance(row[key], str) and len(row[key]) <= 256 for key in strings)
              and all(row[key].strip() for key in ("Actor", "Tenant", "Host")), "malformed journal identities")
        key = (row["Actor"], row["Tenant"], row["Host"])
        counts[key] = counts.get(key, 0) + 1
        number = row["EntryNumber" if execution else "CallNumber"]
        _need(type(number) is int and number == counts[key] and row["ToolName"] in TOOLS, "invalid journal order/tool")
        if not execution:
            _need(row["ToolName"] == CHECK_TOOL and type(row["Passed"]) is bool
                  and all(row[key].strip() for key in ("InvocationId", "ToolCallId", "Revision")), "invalid business check journal")
        if key == (owner["subject"], owner["tenantId"], HOST): selected.append(row)
    return selected


def _restart_snapshot(root, snapshot, origin, owner, paths, *, available=True):
    _need(set(snapshot) == ({"widget", "layout", "checks", "entries", "journals"} if available else {"layout", "journals"}),
          "incomplete restart owner snapshot")
    values = {"layout": _restart_native(root, snapshot["layout"], origin, HOME_PREFERENCES_PATH)}
    journals = snapshot["journals"]
    _need(set(journals) == {"execution", "checks"}, "missing durable module journal")
    decoded = {kind: require_execution_observation(_owned(root, journals[kind]), path=paths[kind], owner=owner, execution=kind == "execution")
               for kind in journals}
    values["journals"] = decoded
    if available:
        for key, path in (("widget", OWNER), ("checks", OWNER + "/checks"), ("entries", EXECUTION_ENDPOINT)):
            values[key] = _restart_native(root, snapshot[key], origin, path)
        widget = values["widget"]
        _need(widget.get("widgetId") == WIDGET and widget.get("hostId") == HOST and isinstance(widget.get("visible"), bool)
              and isinstance(widget.get("revision"), str) and widget["revision"] and values["layout"].get("revision") == widget["revision"],
              "wrong native owner resource")
        for key in ("checks", "entries"):
            _need(all(values[key].get(field) == widget[field] for field in ("widgetId", "revision", "hostId")), "foreign journal projection")
        entries = [{key[0].lower() + key[1:]: item for key, item in row.items()} for row in decoded["execution"]]
        checks = [{key[0].lower() + key[1:]: item for key, item in row.items() if key not in ("Actor", "Tenant", "Host")}
                  for row in decoded["checks"][-16:]]
        _need(type(values["entries"].get("entryCount")) is int and values["entries"]["entryCount"] == len(entries)
              and same(values["entries"].get("entries"), entries), "native entry projection differs from durable module journal")
        _need(type(values["checks"].get("ranCount")) is int and values["checks"]["ranCount"] == len(decoded["checks"])
              and same(values["checks"].get("runs"), checks), "native business runs differ from durable module journal")
    return values


def _restart_proposal(root, ref, origin, session, proposal_id, prepared):
    _, value = _restart_http(root, ref, origin, "GET", "/_elsa/weaver/sessions/" + session + "/proposals")
    rows = value.get("data")
    _need(isinstance(rows, list) and len(rows) == 1 and isinstance(rows[0], dict), "lost/duplicate/foreign recovered proposal")
    row = rows[0]
    _need(row.get("id") == proposal_id and row.get("sessionId") == session and same(row.get("preparedInvocation"), prepared),
          "recovery rewrote the frozen preparation, digest or deadline")
    return row


def _restart_policy(root, ref, origin, enabled):
    _, value = _restart_http(root, ref, origin, "GET", "/_elsa/weaver/bootstrap")
    _need(value.get("error") is None and same(value.get("data", {}).get("privileges"), dict.fromkeys(CLASSES, enabled))
          and value["data"].get("recovery") == "durable" and value["data"].get("providerStatus") == "available",
          "wrong live policy, actual provider or durable recovery composition")


def _restart_identity(root, ref, origin):
    value = _restart_native(root, ref, origin, "/_elsa/identity/session")
    _need(value.get("status") == "authenticated" and all(isinstance(value.get(key), str) and value[key].strip() for key in ("subject", "tenantId")),
          "missing real current actor/tenant")
    return {key: value[key] for key in ("subject", "tenantId")}


def _restart_generation(root, ref, origin):
    value = _restart_native(root, ref, origin, "/__qualification/generation")
    _need(isinstance(value.get("generation"), str) and value["generation"] and isinstance(value.get("tools"), list)
          and isinstance(value.get("contextSources"), list) and type(value.get("featureConfiguration", {}).get("rootGuardUses")) is int
          and value["featureConfiguration"]["rootGuardUses"] == 0,
          "missing native serving generation")
    return value


def _restart_membership(root, ref, origin):
    from native_lifecycle import package_inventory
    return package_inventory(_restart_native(root, ref, origin, "/__qualification/packages"))


def _restart_feature_transition(root, ref, origin, enabled, configuration):
    transition = _owned(root, ref)
    evidence = transition.get("evidence")
    _need(isinstance(evidence, list) and len(evidence) == 4, "missing actual owner feature transition")
    before = _restart_generation(root, evidence[0], origin)
    catalog = _restart_native(root, evidence[1], origin, "/modularity/features")
    wire = _owned(root, evidence[2])
    _need(wire.get("origin") == origin and wire.get("method") == "POST" and wire.get("path") == "/modularity/features/apply"
          and wire.get("status") == 200 and not wire.get("transportFailure"), "missing native full-state apply")
    applied = _owned(root, wire["body"])
    after = _restart_generation(root, evidence[3], origin)
    _need(before["generation"] != after["generation"] and type(applied.get("reloadedShellCount")) is int and applied["reloadedShellCount"] > 0
          and same(transition.get("before"), before) and same(transition.get("after"), after) and same(transition.get("applied"), applied),
          "owner feature change did not reload the serving shell")
    selected = {row["id"]: row for row in applied["catalog"]["features"]}
    original = {row["id"]: row for row in catalog["features"]}
    _need(len(selected) == len(applied["catalog"]["features"]) and selected.get("AcmeHomeWidget", {}).get("enabled") is enabled
          and same(selected["AcmeHomeWidget"].get("configuration"), configuration)
          and all(same(selected.get(key, {}).get("enabled"), row["enabled"])
                  and same(selected[key].get("configuration"), row.get("configuration")) for key, row in original.items() if key != "AcmeHomeWidget"),
          "feature transition changed unrelated selection or observation paths")


def _restart_reconcile(root, ref, origin):
    record = _owned(root, ref)
    _need(record.get("origin") == origin and record.get("method") == "POST" and record.get("path") == "/__qualification/reconcile"
          and record.get("status") == 200 and not record.get("transportFailure"), "missing native package reconciliation")
    value = _owned(root, record["body"])
    _need(type(value.get("outcomeCode")) is int and value["outcomeCode"] == 0 and isinstance(value.get("runResult"), dict)
          and value["runResult"].get("isDegraded") is False and value["runResult"].get("failedPackages") == [], "package reconciliation failed or is unknown")


def _restart_failure(root, ref, origin, proposal, action, code, status):
    wire, value = _restart_http(root, ref, origin, "POST", "/_elsa/weaver/proposals/" + proposal["id"] + "/" + action, status)
    _need(wire.get("requestBody") == {"revision": proposal["baseRevision"], **({"comment": None} if action == "approve" else {})}
          and value.get("data") is None and isinstance(value.get("error"), dict), "wrong explicit refusal request/envelope")
    error = value["error"]
    _need(error.get("code") == code and error.get("statusCode") == status and type(error.get("statusCode")) is int
          and isinstance(error.get("message"), str) and error["message"] and error.get("ownerUnavailable", False) is False,
          "wrong-cause restart refusal")
    return error


def require_effect_restart_case(value, root, *, source, bindings=None):
    """Deep reader for the fourteen classed/removed-owner records; never qualifies the remaining T038 requirements."""
    from restart_journeys import GRACEFUL, _restart, instant
    current_bindings = effect_restart_bindings()
    _need(bindings is None or bindings == current_bindings, "restart validator source bindings were overridden")
    bindings = current_bindings
    _need(isinstance(value, dict) and value.get("id") in CLASSED_RESTART_CASES and value.get("source") == source
          and value.get("bindings") == bindings and value.get("status") == "passed" and value.get("qualified") is False
          and value.get("independentPackageWitness") is False and value.get("liveProvider") is False
          and value.get("evidenceCategory") == EVIDENCE_CATEGORY, "incomplete/foreign/self-promoted effect restart record")
    provider, key = value["id"].split("/", 1)
    removed = key == "removed-owner-pending"
    tool = CHECK_TOOL if removed else key.removesuffix(":recovery-disabled")
    _need(value.get("provider") == provider and value.get("tool") == tool and value.get("adapter") == provider,
          "wrong actual adapter/tool")
    refs, origin, paths = value["refs"], value["backendOrigin"], value["journalPaths"]
    _need(same(value.get("witness"), strict_json((TOOL / "witness.json").read_text())), "foreign independent witness contract")
    _need(set(paths) == {"execution", "checks"} and paths["execution"] != paths["checks"]
          and all(isinstance(path, str) and Path(path).is_absolute() and Path(path).is_relative_to(Path(root)) for path in paths.values()),
          "foreign observation namespace")
    owner = _restart_identity(root, refs["identityBefore"], origin)
    _need(same(owner, _restart_identity(root, refs["identityRecovered"], origin))
          and same(owner, _restart_identity(root, refs["identityAfter"], origin)), "restart changed the authenticated owner")
    for label, enabled in (("policyBefore", True), ("policyRecovered", removed), ("policyRestored", True)):
        _restart_policy(root, refs[label], origin, enabled)
    catalog = _restart_native(root, refs["catalog"], origin, "/modularity/features")
    features = {row["id"]: row for row in catalog["features"]}
    from consumers import DURABLE_STORE_FEATURE, DURABLE_STORE_DEPENDENCY
    _need(len(features) == len(catalog["features"]) and features.get(COMMON_EFFECTS_FEATURE, {}).get("enabled") is not True
          and all(features.get(name, {}).get("enabled") is True and features[name].get("runtimeAvailable") is True
                  for name in (DURABLE_STORE_FEATURE, DURABLE_STORE_DEPENDENCY, DURABLE_EFFECTS_FEATURE, "AcmeHomeWidget")),
          "classed recovery did not use the packaged durable effects composition")
    config = features["AcmeHomeWidget"].get("configuration", {})
    _need(config.get("ExecutionObservationFile") == paths["execution"] and config.get("CheckObservationFile") == paths["checks"]
          and config.get("StudioHostId") == HOST, "native observation was not armed on the module feature")
    restart = _restart(root, {"refs": {"restart": refs["restart"]}, "fromGeneration": value["fromGeneration"],
                              "toGeneration": value["toGeneration"]}, GRACEFUL)
    _need(restart.get("origin") == origin and restart.get("name") == "backend"
          and restart["toGeneration"] == restart["fromGeneration"] + 1, "wrong owned consumer restart")
    old_process, new_process = restart["stoppedProcess"], restart["startedProcess"]
    _need(old_process.get("inputsUnchangedAtCloseout") is True and old_process.get("origin") == new_process.get("origin") == origin
          and type(old_process.get("pid")) is int and type(new_process.get("pid")) is int
          and all(old_process.get(field) is not None and same(old_process[field], new_process.get(field))
                  for field in ("assemblySha256", "publishedDirectory", "contentRoot", "contentRootIdentity", "command")),
          "restart binary/content-root provenance changed")
    generations = {label: _restart_generation(root, refs[label], origin) for label in ("generationBefore", "generationRecovered", "generationAfter")}
    _need(generations["generationBefore"]["generation"] != generations["generationRecovered"]["generation"], "no actual serving generation change")
    snapshots = {label: _restart_snapshot(root, snapshot, origin, owner, paths, available=not (removed and label == "recovered"))
                 for label, snapshot in value["snapshots"].items()}
    _need(set(snapshots) == {"before", "recovered", "after", "repeated"}, "missing before-dispatch or terminal native snapshot")
    before = snapshots["before"]
    _need(before["entries"]["entryCount"] == before["checks"]["ranCount"] == 0, "new observation namespace did not start at observed zero")
    for label, snapshot in snapshots.items():
        _need(same(snapshot["layout"], before["layout"]) and same(snapshot["journals"], before["journals"]), "refusal/recovery changed owner or module runs/entries")
        if label != "recovered" or not removed:
            _need(same(snapshot, before), "refusal changed complete native widget/check/entry observations")
        for kind, ref in value["snapshots"][label]["journals"].items():
            initial, observed = _owned(root, value["snapshots"]["before"]["journals"][kind]), _owned(root, ref)
            _need(all(observed[field] == initial[field] for field in ("sourcePath", "device", "inode", "bytesSha256")), "journal was replaced/truncated across generations")
    branches = value["branches"]
    _need(set(branches) == ({"pending"} if removed else {"pending", "approved"}), "missing pending/already-approved recovery branch")
    identities = set()
    all_times = [branch["times"] for branch in branches.values()]
    boundaries = {"before": (float("-inf"), min(times["prepared"] for times in all_times)),
                  "recovered": (max(times["recovered"] for times in all_times), min(times["execute"] for times in all_times)),
                  "after": (max(times["execute"] for times in all_times), min(times["repeated"] for times in all_times)),
                  "repeated": (max(times["repeated"] for times in all_times), float("inf"))}
    for label, snapshot in value["snapshots"].items():
        lower, upper = boundaries[label]
        for ref in snapshot["journals"].values():
            observed_at = _owned(root, ref).get("observedAt")
            _need(type(observed_at) in (int, float) and lower <= observed_at <= upper, "owner snapshot does not bracket the native recovery/dispatch boundary")
    for branch_name, branch in branches.items():
        brefs, session, command = branch["refs"], branch["sessionId"], branch["commandId"]
        initial = native_frames(_owned(root, brefs["initialStream"]), origin, session)
        proposals = [frame.get("payload") for frame in initial if frame.get("kind") == "proposalCreated"]
        _need(len(proposals) == 1 and isinstance(proposals[0], dict), "missing unique real preparation")
        proposal, prepared = proposals[0], proposals[0].get("preparedInvocation") or {}
        _need(same(prepared, branch["prepared"]) and proposal.get("id") == branch["proposalId"] and proposal.get("sessionId") == session
              and proposal.get("status") == "awaitingApproval" and proposal.get("baseRevision") == before["widget"]["revision"], "wrong initial pending proposal")
        expected_risk = "reviewRequired" if tool == "acme_home_activation_visibility" else "admin"
        expected_arguments = {"expectedVisible": before["widget"]["visible"]} if tool == CHECK_TOOL else {"visible": not before["widget"]["visible"]}
        _need(prepared.get("moduleId") == MODULE and prepared.get("toolName") == tool and prepared.get("toolContractVersion") == "1"
              and prepared.get("executionLocation") == "server" and prepared.get("requiresApproval") is True and prepared.get("client") is None
              and prepared.get("actorId") == owner["subject"] and prepared.get("tenantId") == owner["tenantId"] and prepared.get("sessionId") == session
              and prepared.get("risk") == expected_risk and sorted(prepared.get("requiredPrivilegeClasses") or []) == sorted(TOOLS[tool])
              and same(prepared.get("arguments"), expected_arguments) and _target(prepared.get("target"), before["widget"]["revision"])
              and isinstance(prepared.get("approvalDigest"), str) and prepared["approvalDigest"], "wrong frozen effect contract/classes/identity")
        preview = {"widgetId": WIDGET, "revision": before["widget"]["revision"], "hostId": HOST}
        preview.update({"expectedVisible": expected_arguments["expectedVisible"]} if tool == CHECK_TOOL else
                       {"beforeVisible": before["widget"]["visible"], "visible": expected_arguments["visible"]})
        _need(prepared.get("preview") == {"typeId": CHECK_KIND if tool == CHECK_TOOL else "acme.home.widget-visibility", "schemaVersion": "1", "data": preview},
              "frozen owner preview does not match the reviewed native resource")
        for field in ("sessionId", "turnId", "invocationId", "toolCallId"):
            _need(isinstance(prepared.get(field), str) and prepared[field] and prepared[field] not in identities, "missing/reused native branch identity")
            identities.add(prepared[field])
        _need(_owned(root, brefs["command"]) == {"commandId": command, "toolName": tool, "arguments": expected_arguments}, "wrong recovered adapter command")
        recovered = _restart_proposal(root, brefs["recoveredProposals"], origin, session, proposal["id"], prepared)
        _need(recovered.get("status") == ("approved" if branch_name == "approved" else "awaitingApproval"), "recovery executed/settled undispatched operation")
        if branch_name == "approved":
            _, decision = _restart_http(root, brefs["approvedBefore"], origin, "POST", "/_elsa/weaver/proposals/" + proposal["id"] + "/approve")
            _need(decision.get("data", {}).get("status") == "approved" and same(decision["data"].get("preparedInvocation"), prepared), "missing real pre-restart approval")
        _, recovered_session = _restart_http(root, brefs["recoveredSession"], origin, "GET", "/_elsa/weaver/sessions/" + session)
        _need(recovered_session.get("error") is None and recovered_session.get("data", {}).get("sessionId") == session, "lost recovered session")
        times = branch["times"]
        _need(all(type(times.get(key)) in (int, float) for key in ("prepared", "recovered", "execute", "repeated"))
              and times["prepared"] < restart["stoppedAt"] <= restart["startedAt"] <= times["recovered"] <= times["execute"] <= times["repeated"] < instant(prepared.get("expiresAt")),
              "wrong native recovery/deadline boundary")
        code, status = ("weaver.proposal.executor_unavailable", 409) if removed else (POLICY_CODE, 403)
        if branch_name == "pending":
            if removed:
                decision_wire, decision = _restart_http(root, brefs["decision"], origin, "POST", "/_elsa/weaver/proposals/" + proposal["id"] + "/approve")
                _need(decision_wire.get("requestBody") == {"revision": proposal["baseRevision"], "comment": None}
                      and decision.get("error") is None and decision.get("data", {}).get("id") == proposal["id"]
                      and decision["data"].get("status") == "approved" and same(decision["data"].get("preparedInvocation"), prepared),
                      "approval did not succeed on core checks with owner absent")
            else: _restart_failure(root, brefs["decision"], origin, proposal, "approve", code, status)
        error = _restart_failure(root, brefs["execute"], origin, proposal, "execute", code, status)
        repeated = _restart_failure(root, brefs["repeatExecute"], origin, proposal, "execute", code, status)
        _need(same(error, repeated), "reenabling policy/restoring owner resurrected the refused proposal")
        terminal = native_frames(_owned(root, brefs["continuation"]), origin, session)
        _, reopened = _restart_http(root, brefs["session"], origin, "GET", "/_elsa/weaver/sessions/" + session)
        trace = _owned(root, brefs["trace"])
        _need(trace.get("incompleteTailBytes") == 0 and type(trace.get("offset")) is int and trace["offset"] >= 0
              and isinstance(trace.get("sourcePath"), str) and Path(trace["sourcePath"]).is_relative_to(Path(root)), "incomplete/foreign actual provider trace")
        result = require_called_refusal(trace["records"], provider, command, tool, expected_arguments, session, initial, terminal,
                                       code, reopened.get("data"), proposal_id=proposal["id"])
        _need(same(result["error"], error), "provider continuation lost the native refusal")
        failed = _restart_proposal(root, brefs["terminalProposals"], origin, session, proposal["id"], prepared)
        _need(failed.get("status") == "failed", "refused proposal is not terminal")
        _, audit = _restart_http(root, brefs["audit"], origin, "GET", "/_elsa/weaver/audit?take=100&sessionId=" + session)
        _need(isinstance(audit.get("data"), list) and all(row.get("sessionId") == session for row in audit["data"])
              and any(row.get("kind") == "proposalFailed" and row.get("metadata", {}).get("proposalId") == proposal["id"] for row in audit["data"]),
              "missing correlated native failure audit")
    if removed:
        from native_lifecycle import require_installed_package, require_removed_package
        package = value["package"]
        inventories = {name: _restart_membership(root, refs[name], origin) for name in ("installed", "disabled", "removed", "removedRecovered", "restored")}
        require_installed_package(inventories["installed"], package)
        require_installed_package(inventories["disabled"], package)
        for name in ("removed", "removedRecovered"):
            require_removed_package(inventories["installed"], inventories[name], package)
            _need(same(inventories[name], {key: row for key, row in inventories["installed"].items() if key != package["id"].casefold()}),
                  "module removal changed unrelated packages")
        _need(same(inventories["installed"], inventories["restored"]), "restoration did not retain exact package membership")
        for label in ("generationRemoved", "generationRecovered"):
            current = _restart_generation(root, refs[label], origin)
            _need(not any(row.get("moduleId") == MODULE for row in current["tools"] + current["contextSources"]), "removed owner still contributes contracts")
        _need(package.get("id") == value["witness"]["backendPackage"] and re.fullmatch(r"[a-f0-9]{64}", package.get("sha256", "")), "wrong independent owner package")
        _restart_feature_transition(root, refs["disableTransition"], origin, False, config)
        _restart_feature_transition(root, refs["restoreTransition"], origin, True, config)
        for label in ("removeReconcile", "restoreReconcile"): _restart_reconcile(root, refs[label], origin)
        delta = value["archiveDelta"]
        _need(isinstance(delta, dict) and len(delta.get("added", [])) == 1, "unexpected owner archive removal scope")
        archive = delta["added"][0]
        _need(archive.get("identity", "").casefold() == (package["id"] + "/" + package["version"]).casefold()
              and archive.get("sha256") == package["sha256"], "wrong removed owner archive identity")
        for field in ("path", "sourceArchive"):
            _owned_file(root, {"path": archive[field], "sha256": package["sha256"]})
        _need(Path(archive["path"]).resolve() != Path(archive["sourceArchive"]).resolve(), "retained author archive was used as the disposable feed copy")
    for label in ("generationBefore", "generationAfter") + (() if removed else ("generationRecovered",)):
        current = generations[label]
        require_resource_inventory([row for row in current["tools"] if row.get("moduleId") == MODULE],
                                   [row for row in current["contextSources"] if row.get("moduleId") == MODULE], value["witness"])
    return value


def require_classed_effect_restarts(records, root, *, source):
    """Validate this subset without claiming the full thirty-case T038 inventory passed."""
    _need(isinstance(records, list) and [row.get("id") for row in records] == list(CLASSED_RESTART_CASES), "missing/duplicate classed recovery records")
    bindings = effect_restart_bindings()
    for row in records:
        _need(isinstance(row.get("record"), dict) and same(_owned(root, row["record"]), {key: item for key, item in row.items() if key != "record"}),
              "classed recovery index differs from its immutable native record")
        require_effect_restart_case(row, root, source=source, bindings=bindings)
    return records


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


def require_nested_observations(observations, trace, provider):
    """Compare real admissions and adapter requests, never registry/summary flags."""
    names = ("nestedBefore", "unrelatedBefore", "nestedAfter", "unrelatedAfter")
    _need(set(observations) == set(names), "Missing nested/unrelated native observe turn")
    requests = {}
    sessions, commands, positions = set(), set(), []
    for name in names:
        observed = observations[name]
        command, session = observed["commandId"], observed["sessionId"]
        _need(command not in commands and session not in sessions, "Nested observations reused a command or historical conversation")
        commands.add(command); sessions.add(session)
        require_normal_terminal(observed["frames"])
        _need(not any(frame.get("kind") in ("toolCallStarted", "toolCallCompleted", "proposalCreated", "contributedResult")
                      for frame in observed["frames"]), "Observe turn called a tool or acquired an effect")
        admitted = [frame.get("payload", {}).get("attachments") for frame in observed["frames"] if frame.get("kind") == "contextAdmitted"]
        _need(len(admitted) == 1 and isinstance(admitted[0], list), "Observe turn has no unique native context admission")
        rows = [(index, row) for index, row in enumerate(trace) if row.get("data", {}).get("commandId") == command
                and row.get("kind", "").startswith(provider + ".")]
        native_requests = [(index, row["data"]) for index, row in rows if row.get("kind") == provider + ".request"]
        _need([row.get("kind") for _, row in rows] == [provider + ".request", provider + ".observation"],
              "Observe turn lacks its exact request/observation pair or invoked a tool")
        position, request = native_requests[0]; positions.append(position)
        if provider == "copilot": _need(request.get("sessionId") == session, "Observe SDK request belongs to another native session")
        _need(isinstance(request.get("tools"), list), "Observe request omitted actual tool discovery")
        observation = rows[-1][1]["data"]
        _need(observation.get("toolCount") == len(request["tools"])
              and (provider != "copilot" or observation.get("sessionId") == session), "Observe event differs from its actual request/session")
        requests[name] = (admitted[0], request)
    _need(positions == sorted(positions), "Nested observations are not in unload order")
    before_context, before_request = requests["nestedBefore"]
    after_context, after_request = requests["nestedAfter"]
    owned_guidance = [item["summary"] for item in before_context if item.get("moduleId") == MODULE
                      and item.get("origin") == "installedGuidance" and item.get("summary")]
    _need(any(item.get("moduleId") == MODULE and item.get("origin") != "installedGuidance" for item in before_context)
          and owned_guidance and any(tool.get("name", "").startswith("acme_home_") for tool in before_request["tools"]),
          "Nested positive control lacked actual context, installed guidance or tools")
    def strings(value):
        if isinstance(value, str): return [value]
        if isinstance(value, list): return [text for item in value for text in strings(item)]
        if isinstance(value, dict): return [text for item in value.values() for text in strings(item)]
        return []
    _need(all(any(summary in text for text in strings(before_request)) for summary in owned_guidance), "Installed nested guidance never reached the provider")
    _need(not any(item.get("moduleId") == MODULE for item in after_context)
          and not any(tool.get("name", "").startswith("acme_home_") for tool in after_request["tools"])
          and not any(MODULE in text or any(summary in text for summary in owned_guidance) for text in strings(after_request)),
          "Removed module context, guidance or tools reached the provider")
    unrelated_context, unrelated_request = requests["unrelatedBefore"]
    retained_context, retained_request = requests["unrelatedAfter"]
    unrelated = {item["moduleId"] for item in unrelated_context if item.get("moduleId") not in (None, MODULE)
                 and item.get("origin") != "installedGuidance"}
    guidance = [item for item in unrelated_context if item.get("moduleId") in unrelated and item.get("origin") == "installedGuidance"]
    tools = [tool for tool in unrelated_request["tools"] if tool.get("name", "").startswith("workflow_")]
    _need(unrelated and guidance and tools and all(any(item.get("moduleId") == module for item in retained_context) for module in unrelated)
          and all(item in retained_context for item in guidance)
          and all(tool in retained_request["tools"] for tool in tools)
          and all(item.get("summary") and any(item["summary"] in text for text in strings(unrelated_request))
                  and any(item["summary"] in text for text in strings(retained_request)) for item in guidance),
          "Unrelated resource context, installed guidance and actual tools did not survive module removal")
    return True


def require_browser_case(value, root, spec):
    shutdown = _owned(root, value["browserShutdown"])
    _need(shutdown.get("provenAbsent") is True and shutdown.get("remaining") == []
          and type(shutdown.get("nodePid")) is int and type(shutdown.get("browserPid")) is int
          and shutdown["nodePid"] > 1 and shutdown["browserPid"] > 1
          and isinstance(shutdown.get("observations"), list) and shutdown["observations"]
          and shutdown["observations"][-1] == {}, "Browser descendant shutdown has no retained absence observation")
    owned = shutdown.get("ownedProcesses")
    _need(isinstance(owned, dict) and str(shutdown["nodePid"]) in owned and str(shutdown["browserPid"]) in owned
          and all(isinstance(row.get("started"), str) and row["started"]
                  and type(row.get("session")) is int and type(row.get("group")) is int for row in owned.values())
          and owned[str(shutdown["nodePid"])]["session"] == owned[str(shutdown["nodePid"])]["group"] == shutdown["nodePid"]
          and shutdown.get("groups") == sorted({row["group"] for row in owned.values()}),
          "Browser shutdown omitted its observed process birth/session/group ownership")
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
        _need(browser["effects"].get("removedNestedWidget") is True, "nested module removal not witnessed")
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
        observations = {}
        for name, item in native.get("nestedObservations", {}).items():
            _need(_owned(root, item["command"]) == {"commandId": item["commandId"], "mode": "observe"}, "Nested turn changed the exact observe command")
            message = _owned(root, item["message"])
            path = item["sessionPath"]
            _need(re.fullmatch(r"/_elsa/weaver/sessions/[^/]+", path) and message.get("url") == value["backendOrigin"] + path + "/messages"
                  and message.get("status") == 200 and message.get("response", {}).get("error") is None
                  and message.get("response", {}).get("data", {}).get("message", {}).get("sessionId") == path.split("/")[-1],
                  "Nested observe message lacks its actual native session")
            streams = _owned(root, item["stream"])
            _need(isinstance(streams, list) and len(streams) == 1, "Nested observe stream is missing or duplicated")
            stream = streams[0]
            _need(stream.get("url") == value["backendOrigin"] + path + "/stream" and stream.get("status") == 200
                  and stream.get("termination") == "loadingFinished" and stream.get("captureError") is None
                  and stream.get("captureMethod") in ("streamResourceContent", "getResponseBody")
                  and stream.get("contentType", "").split(";", 1)[0].strip() == "text/event-stream"
                  and not stream.get("sizeExceeded") and not stream.get("captureValidationError")
                  and stream.get("trailingIncompleteUtf8Bytes") == 0 and stream.get("textTruncation") == "none", "Nested observe stream is incomplete")
            observations[name] = {"commandId": item["commandId"], "sessionId": path.split("/")[-1], "frames": frames_from_raw(stream.get("rawBody"))}
        require_nested_observations(observations, trace["records"], value["provider"])
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


class _DarwinProcessInfo(ctypes.Structure):
    # Darwin sys/proc_info.h proc_bsdinfo (PROC_PIDTBSDINFO=3). Native
    # microsecond birth avoids ps lstart's one-second PID-reuse ambiguity.
    _fields_ = [(name, ctypes.c_uint32) for name in
                "flags status xstatus pid parent uid gid ruid rgid svuid svgid reserved".split()] + [
                    ("comm", ctypes.c_char * 16), ("name", ctypes.c_char * 32)] + [
                    (name, ctypes.c_uint32) for name in "nfiles group jobc tty ttygroup".split()] + [
                    ("nice", ctypes.c_int32), ("startseconds", ctypes.c_uint64), ("startmicros", ctypes.c_uint64)]


def _process_snapshot():
    observed = subprocess.run(["ps", "-axo", "pid="], capture_output=True, text=True, timeout=2, check=True)
    native = ctypes.CDLL("/usr/lib/libproc.dylib", use_errno=True) if sys.platform == "darwin" else None
    if native:
        native.proc_pidinfo.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_uint64, ctypes.c_void_p, ctypes.c_int]
        native.proc_pidinfo.restype = ctypes.c_int
    rows = {}
    for value in observed.stdout.split():
        pid = int(value)
        try:
            if native:
                info, confirmed = _DarwinProcessInfo(), _DarwinProcessInfo()
                size = ctypes.sizeof(info)
                if native.proc_pidinfo(pid, 3, 0, ctypes.byref(info), size) != size:
                    if ctypes.get_errno() == 3: continue  # ESRCH, never a permission/incomplete census.
                    raise RuntimeError("Native browser process census unavailable")
                session = os.getsid(pid)
                if native.proc_pidinfo(pid, 3, 0, ctypes.byref(confirmed), size) != size:
                    if ctypes.get_errno() == 3: continue
                    raise RuntimeError("Native browser process census confirmation unavailable")
                _need((info.pid, info.parent, info.group, info.startseconds, info.startmicros)
                      == (confirmed.pid, confirmed.parent, confirmed.group, confirmed.startseconds, confirmed.startmicros),
                      "Process identity changed during its native census")
                rows[pid] = {"parent": info.parent, "group": info.group, "session": session,
                             "started": str(info.startseconds) + ":" + str(info.startmicros)}
            else:
                _need(sys.platform.startswith("linux"), "Native process birth identity unsupported on this platform")
                fields = Path("/proc", str(pid), "stat").read_text().rsplit(")", 1)[1].split()
                rows[pid] = {"parent": int(fields[1]), "group": int(fields[2]), "session": int(fields[3]), "started": fields[19]}
        except (FileNotFoundError, ProcessLookupError): continue
    return rows


def _same_process(left, right):
    return all(left.get(key) == right.get(key) and left.get(key) is not None for key in ("started", "session", "group"))


def _capture_owned_descendants(owned, current):
    """Admit births only while a live unchanged ancestor proves this launch's lineage."""
    _need(all(pid not in current or _same_process(row, current[pid]) for pid, row in owned.items()),
          "An owned process identity was reused or changed")
    changed = True
    while changed:
        changed = False
        for pid, row in current.items():
            parent = row["parent"]
            if pid not in owned and parent in owned and parent in current and _same_process(owned[parent], current[parent]):
                owned[pid] = dict(row); changed = True


def _owned_group_census(groups, owned, current):
    _need(all(pid not in current or _same_process(row, current[pid]) for pid, row in owned.items()),
          "Owned process birth/session/group changed; no further signals or restoration")
    members = {pid: row for pid, row in current.items() if row["group"] in groups}
    _need(all(pid in owned and _same_process(owned[pid], row) for pid, row in members.items()),
          "Unowned process group member; no further signals or restoration")
    return members


def require_browser_stopped(runtime):
    _need(getattr(runtime, "effects_browser_stopped", True) is True,
          "Owned browser descendants are not proven stopped; do not restore privileges, faults or modules")
    _need(getattr(runtime, "effects_management_settled", True) is True,
          "The Studio module-selection request has no acknowledged outcome; restoration remains blocked")


def _stop_browser_groups(process, lease, token, owned, *, snapshot=_process_snapshot, send=os.killpg,
                         now=time.monotonic, sleep=time.sleep):
    """Stop only this launch's verified groups; absence is required even after a successful driver exit."""
    try: receipt = strict_json(lease.read_text()) if lease.is_file() else {}
    except (ValueError, OSError): receipt = {}
    valid = receipt.get("token") == token and receipt.get("nodePid") == process.pid \
        and type(receipt.get("browserPid")) is int and receipt["browserPid"] > 1
    browser_pid = receipt["browserPid"] if valid else None
    current = snapshot()
    owned = dict(owned)
    _capture_owned_descendants(owned, current)
    # Freeze before signalling. A lease or numeric PGID alone grants no authority.
    browser = owned.get(browser_pid)
    groups = {process.pid, *(row["group"] for row in owned.values())}
    if valid and browser is None: groups.add(browser_pid)  # A census blocker, never authority to signal.
    _need(all(group > 1 and group != os.getpgrp() for group in groups), "Unsafe browser process group")
    observations, signals = [], []
    _owned_group_census(groups, owned, current)
    for sig, seconds in ((signal.SIGTERM, 5), (signal.SIGKILL, 5)):
        for group in sorted(groups):
            current = snapshot()
            members = _owned_group_census(groups, owned, current)
            if any(row["group"] == group for row in members.values()):
                try: send(group, sig)
                except ProcessLookupError: pass
                signals.append({"group": group, "signal": int(sig)})
        deadline = now() + seconds
        while True:
            process.poll()  # Reap Node so a zombie cannot masquerade as a live descendant.
            current = snapshot()
            remaining = list(_owned_group_census(groups, owned, current))
            observations.append({str(pid): current[pid] for pid in remaining})
            if not remaining:
                _need(valid and browser is not None, "No exact browser lease and observed ancestry; restoration remains blocked")
                return {"nodePid": process.pid, "browserPid": browser_pid, "groups": sorted(groups), "remaining": [],
                        "ownedProcesses": {str(pid): row for pid, row in owned.items()},
                        "signals": signals, "observations": observations, "provenAbsent": True}
            if now() >= deadline: break
            sleep(0.05)
    raise RuntimeError("Owned browser descendants remain after bounded shutdown; restoration blocked")


def _run_effects_browser(runtime, name, command, config, output):
    run = runtime.run
    run.report["stage"] = name; run.save()
    log, lease = run.root / (name + ".log"), output / "browser-process-private.json"
    token = secrets.token_hex(24)
    config.update(effectsProcessLease=str(lease), effectsProcessToken=token)
    private_json(output / "config-private.json", config)
    runtime.effects_browser_stopped = False
    owned = {}
    with log.open("w") as stream:
        process = subprocess.Popen(command, cwd=TOOL, stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT,
                                   env={key: value for key, value in os.environ.items() if key in RUNTIME_ENVIRONMENT}, start_new_session=True)
        try:
            initial = _process_snapshot()
            node = initial.get(process.pid)
            _need(node is not None and node["parent"] == os.getpid() and node["group"] == node["session"] == process.pid,
                  "Node launch ancestry, birth and isolated session could not be established")
            owned[process.pid] = dict(node)
            deadline = time.monotonic() + 150
            while process.poll() is None:
                processes = _process_snapshot()
                _capture_owned_descendants(owned, processes)
                if lease.is_file():
                    receipt = strict_json(lease.read_text())
                    _need(receipt.get("token") == token and receipt.get("nodePid") == process.pid, "Foreign browser process lease")
                    browser_pid = receipt.get("browserPid")
                    if browser_pid in owned:
                        browser = owned[browser_pid]
                        _need(browser["group"] in (process.pid, browser_pid) and browser["session"] in (process.pid, browser_pid),
                              "Browser launch did not establish an isolated group/session")
                if time.monotonic() >= deadline: raise TimeoutError("Effects browser deadline; no action replay")
                time.sleep(0.05)
        finally:
            # An outer SIGALRM may interrupt any await. Suppress it only while
            # bounded shutdown proves that no stale UI action can run afterwards.
            remaining, interval = signal.getitimer(signal.ITIMER_REAL)
            signal.setitimer(signal.ITIMER_REAL, 0)
            try:
                stopped = _stop_browser_groups(process, lease, token, owned)
                stopped_receipt = runtime.save(output, "browser-process-stopped", stopped)
                runtime.effects_browser_stopped = True
            finally:
                run.report["stages"].append({"name": name, "exitCode": process.poll(), "log": str(log),
                                             "descendantsStopped": runtime.effects_browser_stopped})
                run.save()
                if remaining: signal.setitimer(signal.ITIMER_REAL, max(0.001, remaining), interval)
    _need(process.returncode == 0, "Effects browser failed; private log: " + str(log))
    return stopped_receipt


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
    studio_original = None
    if spec["condition"] == "nested-removal":
        catalog, _ = runtime.native.json(runtime.studio, "effects-original-studio-catalog", "/modularity/features")
        studio_original = next(row for row in catalog["features"] if row["id"] == runtime.witness["studioFeature"])
    transport = runtime.transport("effects-browser-trace")
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    node = shutil.which("node")
    _need(node, "actual browser driver needs Node")
    try:
        stage_name = "effects-" + name.replace("/", "-").replace(":", "-")
        shutdown = _run_effects_browser(runtime, stage_name, [node, str(TOOL / "browser.mjs"), str(output / "config-private.json")], config, output)
    finally:
        require_browser_stopped(runtime)
        if studio_original is not None:
            attempted = target / "effects-module-selection-attempt-false.json"
            acknowledgement = target / "effects-module-selection-false.json"
            if attempted.is_file() and not acknowledgement.is_file():
                runtime.effects_management_settled = False
            require_browser_stopped(runtime)
            catalog, _ = runtime.native.json(runtime.studio, "effects-restore-studio-catalog", "/modularity/features")
            current = next(row for row in catalog["features"] if row["id"] == studio_original["id"])
            if (current["enabled"], current["configuration"]) != (studio_original["enabled"], studio_original["configuration"]):
                runtime.apply_features("effects-restore-studio-owner", {studio_original["id"]:
                                       (studio_original["enabled"], studio_original["configuration"])}, client=runtime.studio)
        fault_file.unlink(missing_ok=True)
    observed = retained(target / "observation.json")
    trace = transport.protocol_trace(offset)
    _need(canary not in json.dumps(trace["records"]), "browser typed canary reached actual adapter payloads")
    result = {"id": name, "source": runtime.run.source, "provider": runtime.provider, "scenario": spec, "status": "passed", "bindings": source_bindings(),
              "backendOrigin": runtime.backend_origin, "canarySha256": hashlib.sha256(canary.encode()).hexdigest(),
              "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False, "browser": observed, "browserShutdown": shutdown, "trace": trace["receipt"]}
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
            require_browser_stopped(runtime)
            fault.unlink(missing_ok=True)
            _policy(runtime, [True] * 3)
            runtime.select_provider(original_provider)
            runtime.apply_features("effects-restore-owner-config", {feature: (original["enabled"], original["configuration"])})
        except BaseException as error:
            section.update(status="failed", cleanupFailure={"type": type(error).__name__, "message": str(error)})
            raise
        finally: run.save()
