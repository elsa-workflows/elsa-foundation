"""Complete, sequential real-package witness. Missing observations fail qualification.

This driver owns the expected causes. Expected negative children remain failed;
only the complete evidence set can establish the independent package witness.
"""
from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor
import copy
import json
from pathlib import Path
import secrets
import time
import uuid

from artifacts import TOOL, check, sha
from authors import build_authors, build_incompatible_studio
from case_records import CaseRecords, retained
from consumers import (configure_runtime, freeze_consumer_baseline, capture_consumer_after,
                       select_native_archives, stage_native_archives, prepare_consumers)
from evidence import CONTROL_IDS, REQUIRED_CASES, validate_report
from journey_runtime import (BASE_GRANTS, HOME_PREFERENCES_PATH, NATIVE_HOME, HOST_HEADER, JourneyRuntime,
                             atomic_json, expect_error, same)
from native_lifecycle import (add_resource_archives, remove_resource_archives, restore_resource_archives,
                              require_installed_package, require_removed_package)
from owner_resources import seed_personal_layout
from runtime import Http, owned_origin, private_json, server, RUNTIME_ENVIRONMENT

INVALID_CONTEXT = "Only a current Acme status card reference is supported."
INVALID_SCHEMA = "The installed module does not support this context schema version."
MALFORMED_RESULT = "The reported client effect did not validate against the exact prepared operation. Its outcome is uncertain and will not be replayed."
EXPECTED_CONTROLS = {
    "module-missing": {"stage": "message", "diagnostic": "agent.policyDenied: No installed module contributes the requested capability."},
    "sdk-incompatible": {"stage": "module-admission", "diagnostic": "incompatible"},
    "pending-module-removed": {"stage": "proposal-lookup", "diagnostic": "agent.proposal.not_found"},
    "read-denied": {"stage": "execute", "diagnostic": "agent.proposal.forbidden"},
    "write-denied": {"stage": "execute", "diagnostic": "agent.proposal.forbidden"},
    "cross-user": {"stage": "execute", "diagnostic": "agent.proposal.forbidden"},
    "cross-tenant": {"stage": "execute", "diagnostic": "agent.proposal.forbidden"},
    "cross-host": {"stage": "execute", "diagnostic": "acme.home.host_denied"},
    "foreign-scope": {"stage": "context-admission", "diagnostic": "agent.context.incomplete: " + INVALID_CONTEXT},
    "malformed-scope": {"stage": "context-admission", "diagnostic": "agent.context.incomplete: " + INVALID_CONTEXT},
    "malformed-arguments": {"stage": "tool-preparation", "diagnostic": "acme.home.arguments_invalid"},
    "missing-resource": {"stage": "context-admission", "diagnostic": "agent.context.incomplete: The Acme status card is absent from the personal Home layout."},
    "default-resource": {"stage": "context-admission", "diagnostic": "agent.context.incomplete: Create a personal Home layout before using this widget."},
    "no-op-resource": {"stage": "tool-preparation", "diagnostic": "acme.home.noop"},
    "invalid-schema": {"stage": "context-admission", "diagnostic": "agent.context.incomplete: " + INVALID_SCHEMA},
    "stale-revision": {"stage": "execute", "diagnostic": "acme.home.stale"},
    "concurrent-cas": {"stage": "native-cas", "diagnostic": "acme.home.conflict"},
    "malformed-result": {"stage": "client-acknowledgement", "diagnostic": MALFORMED_RESULT},
    "client-binding-removed": {"stage": "client-execution", "diagnostic": "The originating workflow editor is unavailable."},
    "deny-before-effect": {"stage": "review", "diagnostic": "denied"},
    "cancel-before-effect": {"stage": "native-turn-cancellation", "diagnostic": "turnCancelled"},
    "lost-confirmation": {"stage": "client-confirmation-delivery", "diagnostic": "net::ERR_FAILED"},
}
check(set(EXPECTED_CONTROLS) == CONTROL_IDS, "The coordinator must explicitly name every expected control cause")


def _unchanged(before, after):
    check(same(before, after), "A before-effect control changed the independently read owner")
    return {"ownerObserved": True, "noFalseSuccess": True, "noReplay": True}


def _semantic_refusal(turn, sdk_result, tool_name):
    """SDK transport returned; require its real, correlated native semantic failure too."""
    check(turn.stream is not None, "A preparation refusal needs the actual native stream")
    events = [frame.value for frame in turn.stream.frames]
    contributions = [event["payload"] for event in events if event["kind"] == "contributedResult"
                     and isinstance(event.get("payload"), dict) and event["payload"].get("toolName") == tool_name]
    check(len(contributions) == 1, "Actual refusal did not emit one semantic contributed result")
    result = contributions[0]
    call_id = result.get("toolCallId")
    check(isinstance(call_id, str) and bool(call_id) and result.get("succeeded") is False
          and result.get("payload") is None and result.get("receipt") is None
          and result.get("content") == sdk_result["error"]["message"], "Contributed result did not preserve the actual typed refusal")
    started = [event["payload"] for event in events if event["kind"] == "toolCallStarted"
               and isinstance(event.get("payload"), dict) and event["payload"].get("toolCallId") == call_id]
    completed = [event["payload"] for event in events if event["kind"] == "toolCallCompleted"
                 and isinstance(event.get("payload"), dict) and event["payload"].get("toolCallId") == call_id]
    check(len(started) == len(completed) == 1 and started[0].get("toolName") == tool_name
          and completed[0].get("toolName") == tool_name and completed[0].get("succeeded") is False
          and completed[0].get("summary") == result["content"] and same(completed[0].get("result"), result),
          "Actual semantic completion was missing, successful, or correlated to another SDK/native invocation")


def _seed_principals(run):
    """Configure only real private password identities and concrete, predeclared IAM profiles."""
    primary = "witness-owner-" + secrets.token_hex(8)
    username = "witness-user-" + secrets.token_hex(8)
    principals = []
    for subject, tenant, user in ((primary, "default", username),
                                  ("witness-other-" + secrets.token_hex(8), "default", "witness-other-" + secrets.token_hex(8)),
                                  (primary, "qualification-tenant-" + secrets.token_hex(6), username),
                                  ("witness-missing-" + secrets.token_hex(8), "default", "witness-missing-" + secrets.token_hex(8)),
                                  ("witness-default-" + secrets.token_hex(8), "default", "witness-default-" + secrets.token_hex(8))):
        principals.append({"subject": subject, "tenant": tenant, "username": user, "password": secrets.token_urlsafe(36) + "!aA1", "exactDirectPermissions": list(BASE_GRANTS)})
    path = run.profiles["backend"]["published"] / "appsettings.json"
    settings = json.loads(path.read_text())
    settings["Qualification"].update(Principals=principals, GrantProfiles={
        "full": BASE_GRANTS, "without-read": [key for key in BASE_GRANTS if key != "studio/preferences:read"],
        "without-write": [key for key in BASE_GRANTS if key != "studio/preferences:write"]})
    private_json(path, settings)
    return principals


def _bootstrap(runtime):
    runtime.sign_in()
    runtime.baseline_membership = {}
    for name, client in (("backend", runtime.backend), ("studio", runtime.studio)):
        runtime.baseline_membership[name], _ = runtime.native.membership(client, name + "-baseline-membership", studio=name == "studio")
        generation, _ = runtime.native.generation(client, name + "-baseline-generation")
        if name == "backend":
            check(not any(item["moduleId"] == runtime.witness["moduleId"] for item in generation["tools"] + generation["contextSources"]),
                  "Resource tools/context existed before installing the independently authored package")
        runtime.native.json(client, name + "-baseline-features", "/modularity/features")
    runtime.native.json(runtime.backend, "baseline-health", "/health/live")
    registry, _ = runtime.native.json(runtime.studio, "baseline-registry", "/_elsa/module-management/registry")
    check(not any(item["id"] == runtime.witness["moduleId"] for item in registry["modules"]), "Resource manifest existed before native installation")
    bootstrap = runtime.transport("baseline-agent").bootstrap().require_status(200)
    check(bootstrap.json()["data"]["providerStatus"] == "available", "Baseline actual provider is unavailable")
    seed_personal_layout(runtime.backend, runtime.output("admin-personal-seed") / "seed")
    for index in (0, 1, 2):
        http, _ = runtime.sign_in(index)
        seed_personal_layout(http, runtime.output("principal-personal-seed") / "seed")
    # Missing-card and default-only users are distinct actual owning resources.
    missing, _ = runtime.sign_in(3)
    runtime.native_write("missing-card-seed", missing, {"widgets": [{"id": "qualification.unrelated:keep", "visible": True, "size": "small", "options": {"canary": secrets.token_hex(12)}}]})
    default_value = {"widgets": [{"id": runtime.witness["widgetId"], "visible": True, "size": "small", "options": {"canary": secrets.token_hex(12)}}]}
    runtime.native_write("native-default-layout-seed", runtime.backend, default_value, endpoint=NATIVE_HOME + "/default")
    default_http, _ = runtime.sign_in(4)
    fallback, _, _ = runtime.snapshot("default-only-fallback", http=default_http, endpoint=NATIVE_HOME)
    check(fallback["source"] == "default" and same(fallback["value"], default_value), "The default-only control must observe a genuine native fallback")
    runtime.sign_in()


def _module_missing(runtime, records):
    before, bref, bread = runtime.snapshot("missing-module-owner-before")
    turn = runtime.turn("module-missing", arguments={"visible": True})
    value = expect_error(turn.message, 403, "agent.policyDenied", message="No installed module contributes the requested capability.")
    check(turn.stream is None and turn.proposal is None, "Absent module unexpectedly prepared an operation")
    after, aref, aread = runtime.snapshot("missing-module-owner-after")
    runtime.record(records, "module-missing", bref, aref, turn.evidence + [bread, aread], _unchanged(before, after),
        actual={"stage": "message", "diagnostic": value["error"]["code"] + ": " + value["error"]["message"]})


def _active_resource(runtime, name):
    generation, gen = runtime.native.generation(runtime.backend, name + "-backend-generation")
    w = runtime.witness
    tools = [item for item in generation["tools"] if item["moduleId"] == w["moduleId"]]
    contexts = [item for item in generation["contextSources"] if item["moduleId"] == w["moduleId"]]
    check({item["name"] for item in tools} == {w["readTool"], w["writeTool"]} and len(contexts) == 1,
          "Native serving shell did not compose the exact resource tools and context source")
    registry, reg = runtime.native.json(runtime.studio, name + "-registry", "/_elsa/module-management/registry")
    modules = [item for item in registry["modules"] if item["id"] == w["moduleId"]]
    packages = [item for item in registry["packages"] if item["id"] == w["studioPackage"]]
    original = runtime.run.report["authors"]["studio"]["packed"]["package"]
    check(len(modules) == len(packages) == 1 and modules[0]["status"] == "available" and modules[0]["compatibility"] == "compatible"
          and modules[0]["version"] == packages[0]["version"] == original["version"], "Native registry did not admit the independently built original module")
    assets = runtime.native.assets(runtime.studio, name + "-asset", modules[0]["manifest"], original, w["studioAssembly"])
    runtime.resource_manifest = copy.deepcopy(modules[0]["manifest"])
    state, _, owner = runtime.state(name + "-resource-owner")
    check(state["hostId"] == w["hostId"] and state["widgetId"] == w["widgetId"] and bool(state["revision"]), "Actual resource owner identity differs")
    membership = []
    for host, client in (("backend", runtime.backend), ("studio", runtime.studio)):
        inventory, observed = runtime.native.membership(client, name + "-" + host + "-membership", studio=host == "studio")
        require_installed_package(inventory, runtime.run.report["authors"][host]["packed"]["package"])
        membership.append(observed["receipt"])
    return [gen["receipt"], reg["receipt"], owner, *assets, *membership]


def _install(runtime, records):
    before, bref, bread = runtime.snapshot("install-owner-before")
    evidence, deltas = [bread], {}
    for name in ("backend", "studio"):
        client = runtime.backend if name == "backend" else runtime.studio
        deltas[name] = add_resource_archives(runtime.run, name, name)
        evidence.append(runtime.save(runtime.output("native-inputs-" + name), "archive-delta", deltas[name]))
        _, record = runtime.native.reconcile(client, name + "-native-install")
        evidence.append(record["receipt"])
        configuration = {"StudioHostId": runtime.witness["hostId"], "WriteRendezvousControlFile": str(runtime.cas_control),
                         "WriteRendezvousReceiptFile": str(runtime.cas_receipt)} if name == "backend" else {}
        feature = runtime.witness[name + "Feature"]
        applied = runtime.native.feature(client, name + "-resource-enable", feature, enabled=True, configuration=configuration)
        evidence.extend(applied["evidence"])
    evidence.extend(_active_resource(runtime, "installed"))
    after, aref, aread = runtime.snapshot("install-owner-after")
    check(same(before, after), "Installing contributions changed the owning resource")
    runtime.record(records, "module-add", bref, aref, evidence + [aread], {"membership": True, "servingGeneration": True, "contributions": True})
    return deltas


def _context_controls(runtime, records):
    for name in ("foreign-scope", "malformed-scope", "invalid-schema"):
        before, bref, bread = runtime.snapshot(name + "-owner-before")
        state, _, _ = runtime.state(name + "-state")
        attachment = runtime.attachment(state)
        # ActiveSurface route is presentation, not authority. Change actual owning references.
        if name == "foreign-scope": attachment["target"]["moduleId"] = "qualification.foreign-owner"
        elif name == "malformed-scope": attachment["target"]["resourceId"] = ""
        else: attachment["schemaVersion"] = "999"
        turn = runtime.turn(name, arguments={"visible": not state["visible"]}, attachment=attachment)
        expected = INVALID_SCHEMA if name == "invalid-schema" else INVALID_CONTEXT
        value = expect_error(turn.message, 400, "agent.context.incomplete", message=expected)
        check(turn.stream is None and turn.proposal is None, "Invalid owning context reached tool preparation")
        after, aref, aread = runtime.snapshot(name + "-owner-after")
        runtime.record(records, name, bref, aref, [bread, aread, *turn.evidence], _unchanged(before, after),
            actual={"stage": "context-admission", "diagnostic": value["error"]["code"] + ": " + value["error"]["message"]})
    for name in ("malformed-arguments", "no-op-resource"):
        before, bref, bread = runtime.snapshot(name + "-owner-before")
        state, _, _ = runtime.state(name + "-state")
        args = {"visible": "not-a-boolean"} if name == "malformed-arguments" else {"visible": state["visible"]}
        turn = runtime.turn(name, arguments=args, attachment=runtime.attachment(state))
        turn.message.require_status(200)
        check(turn.proposal is None, "Invalid/no-op arguments unexpectedly created an approval")
        value, _ = runtime.tool_result(turn)
        expected = "acme.home.arguments_invalid" if name == "malformed-arguments" else "acme.home.noop"
        check(value.get("status") == "denied" and value.get("executed") is False and value.get("receipt") is None,
              "A preparation refusal was reported as applied")
        # The SDK return preserves the actual semantic error code; SSE transport alone does not.
        check(value.get("error", {}).get("code") == expected,
              "Actual SDK return did not identify the expected module preparation cause: " + expected)
        _semantic_refusal(turn, value, runtime.witness["writeTool"])
        after, aref, aread = runtime.snapshot(name + "-owner-after")
        runtime.record(records, name, bref, aref, [bread, aread, *turn.evidence], _unchanged(before, after),
            actual={"stage": "tool-preparation", "diagnostic": expected})


def _resource_absence(runtime, records):
    for name, index, message in (("missing-resource", 3, "The Acme status card is absent from the personal Home layout."),
                                  ("default-resource", 4, "Create a personal Home layout before using this widget.")):
        http, _ = runtime.sign_in(index)
        before, bref, bread = runtime.snapshot(name + "-owner-before", http=http, endpoint=NATIVE_HOME)
        # A valid-shaped own reference still cannot authorize a nonexistent personal resource.
        attachment = runtime.attachment({"revision": before["revision"], "widgetId": runtime.witness["widgetId"], "visible": True})
        turn = runtime.turn(name, http=http, tool=runtime.witness["readTool"], mode="explain", attachment=attachment)
        value = expect_error(turn.message, 400, "agent.context.incomplete", message=message)
        after, aref, aread = runtime.snapshot(name + "-owner-after", http=http, endpoint=NATIVE_HOME)
        runtime.record(records, name, bref, aref, [bread, aread, *turn.evidence], _unchanged(before, after),
            actual={"stage": "context-admission", "diagnostic": value["error"]["code"] + ": " + value["error"]["message"]})


def _authority_controls(runtime, records):
    for name, profile in (("read-denied", "without-read"), ("write-denied", "without-write")):
        owner_http, identity = runtime.sign_in(0)
        before, bref, bread = runtime.snapshot(name + "-owner-before", http=owner_http)
        turn = runtime.prepare(name, http=owner_http); runtime.approve(turn)
        authority = runtime.set_grants(0, profile)
        selected = runtime.clients[0]
        current = runtime.transport(name + "-fresh-authority", http=selected)
        response = runtime.execute(turn, transport=current)
        expect_error(response, 403, "agent.proposal.forbidden", message="Current server authority denies the prepared target.")
        # Restore through the real IAM authority, then freshly authenticate the same
        # owner for independent readback. An old token is not an authorization bypass.
        restored = runtime.set_grants(0, "full")
        after, aref, aread = runtime.snapshot(name + "-owner-after", http=runtime.clients[0])
        runtime.record(records, name, bref, aref, [bread, aread, *turn.evidence, *authority, response.receipt, *restored], _unchanged(before, after),
            actual={"stage": "execute", "diagnostic": response.json()["error"]["code"]})
    for name, index in (("cross-user", 1), ("cross-tenant", 2)):
        owner_http, identity = runtime.sign_in(0)
        before, bref, bread = runtime.snapshot(name + "-owner-before", http=owner_http)
        turn = runtime.prepare(name, http=owner_http); runtime.approve(turn)
        foreign, selected_identity = runtime.sign_in(index)
        check((selected_identity["subject"] != identity["subject"] and selected_identity["tenantId"] == identity["tenantId"]) if name == "cross-user"
              else (selected_identity["subject"] == identity["subject"] and selected_identity["tenantId"] != identity["tenantId"]), "The cross-principal control did not isolate its intended identity boundary")
        response = runtime.execute(turn, transport=runtime.transport(name + "-foreign-principal", http=foreign))
        expect_error(response, 403, "agent.proposal.forbidden", message="The agent proposal is not available to the current principal.")
        after, aref, aread = runtime.snapshot(name + "-owner-after", http=owner_http)
        runtime.record(records, name, bref, aref, [bread, aread, *turn.evidence, response.receipt], _unchanged(before, after),
            actual={"stage": "execute", "diagnostic": response.json()["error"]["code"]})
    before, bref, bread = runtime.snapshot("cross-host-owner-before")
    turn = runtime.prepare("cross-host"); runtime.approve(turn)
    class ForeignHost(Http):
        def request(self, path, body=None, *, method="GET", headers=None):
            return super().request(path, body, method=method, headers={**(headers or {}), HOST_HEADER: "qualification.other-host"})
    foreign = ForeignHost(runtime.backend_origin, bearer=runtime.backend.bearer, opener=runtime.backend.opener)
    response = runtime.execute(turn, transport=runtime.transport("cross-host-transport", http=foreign))
    expect_error(response, 403, "acme.home.host_denied")
    after, aref, aread = runtime.snapshot("cross-host-owner-after")
    runtime.record(records, "cross-host", bref, aref, [bread, aread, *turn.evidence, response.receipt], _unchanged(before, after),
        actual={"stage": "execute", "diagnostic": response.json()["error"]["code"]})


def _stale(runtime, records):
    turn = runtime.prepare("stale-review"); runtime.approve(turn)
    document, _, read = runtime.snapshot("stale-current-layout")
    changed = copy.deepcopy(document["value"]); changed["independentEdit"] = secrets.token_hex(16)
    saved, write = runtime.native_write("stale-independent-native-edit", runtime.backend, changed, revision=document["revision"])
    before, bref, bread = runtime.snapshot("stale-owner-before-execution")
    check(saved["revision"] == before["revision"] != turn.proposal["preparedInvocation"]["target"]["revision"], "The independent edit did not invalidate the reviewed native revision")
    response = runtime.execute(turn); expect_error(response, 409, "acme.home.stale")
    after, aref, aread = runtime.snapshot("stale-owner-after")
    runtime.record(records, "stale-revision", bref, aref, [*turn.evidence, read, write["receipt"], bread, response.receipt, aread], _unchanged(before, after),
        actual={"stage": "execute", "diagnostic": response.json()["error"]["code"]})


def _deny(runtime, records):
    before, bref, bread = runtime.snapshot("deny-owner-before")
    turn = runtime.prepare("deny-review")
    denied = turn.transport.deny(turn.proposal["id"]).require_status(200)
    check(denied.json()["data"]["status"] == "denied", "Native review decision did not deny the proposal")
    response = runtime.execute(turn)
    value = expect_error(response, 409, "agent.proposal.closed")
    check(value["data"]["status"] == "denied" and value["data"]["executed"] is False, "Denied execute did not retain its actual terminal denied state")
    after, aref, aread = runtime.snapshot("deny-owner-after")
    runtime.record(records, "deny-before-effect", bref, aref, [bread, aread, *turn.evidence, denied.receipt, response.receipt], _unchanged(before, after),
        actual={"stage": "review", "diagnostic": value["data"]["status"]})


def _cancel(runtime, records):
    before, bref, bread = runtime.snapshot("cancel-owner-before")
    state, _, _ = runtime.state("cancel-own-reference")
    command_id, epoch = str(uuid.uuid4()), str(uuid.uuid4())
    atomic_json(runtime.gate_file, {"commandId": command_id, "epoch": epoch, "state": "hold", "timeoutMilliseconds": 60000})
    turn = runtime.turn("cancel-before-call", arguments={"visible": not state["visible"]}, attachment=runtime.attachment(state), command_id=command_id, drain=False)
    turn.message.require_status(200)
    with turn.transport.stream(turn.session) as stream:
        started = stream.until(lambda frame: frame.value["kind"] == "started")
        turn_id = started.value["payload"]["turnId"]
        deadline = time.monotonic() + 30
        while True:
            trace = turn.transport.protocol_trace(turn.offset)
            waiting = [row for row in trace["records"] if row["kind"] == "protocol.gate.waiting" and row["data"].get("commandId") == command_id and row["data"].get("epoch") == epoch]
            if waiting: break
            check(time.monotonic() < deadline, "Actual SDK pre-call gate never reported waiting")
            time.sleep(0.02)
        cancelled = turn.transport.cancel(turn.session, turn_id).require_status(200)
        check(cancelled.json()["data"]["cancelled"] is True and cancelled.json()["data"]["turnId"] == turn_id, "Native cancellation did not cancel this actual running turn")
        terminal = stream.until_turn_end(turn_id=turn_id)
        check(terminal.value["kind"] == "turnCancelled", "A timeout/error cannot qualify as native cancellation")
    trace = turn.transport.protocol_trace(turn.offset)
    rows = [row for row in trace["records"] if row.get("data", {}).get("commandId") == command_id]
    check(any(row["kind"] == "protocol.gate.cancelled" for row in rows) and not any(row["kind"] in ("copilot.call", "anthropic.call") for row in rows), "Cancellation did not interrupt the actual token before SDK tool dispatch")
    after, aref, aread = runtime.snapshot("cancel-owner-after")
    runtime.record(records, "cancel-before-effect", bref, aref, [bread, aread, *turn.evidence, stream.receipt, cancelled.receipt, trace["receipt"]], _unchanged(before, after),
        actual={"stage": "native-turn-cancellation", "diagnostic": terminal.value["kind"]})


def _concurrent_cas(runtime, records):
    before, bref, bread = runtime.snapshot("cas-owner-before")
    state, _, _ = runtime.state("cas-state")
    # Keep the single fixture command byte-identical while two real, distinct sessions
    # review the same semantic request. Native invocation/proposal IDs remain distinct.
    command_id = str(uuid.uuid4())
    turns = [runtime.prepare("cas-review-" + str(index), command_id=command_id) for index in range(2)]
    check(turns[0].proposal["preparedInvocation"]["target"] == turns[1].proposal["preparedInvocation"]["target"], "CAS operations were not reviewed against one exact owner revision")
    check(turns[0].proposal["id"] != turns[1].proposal["id"] and turns[0].proposal["preparedInvocation"]["invocationId"] != turns[1].proposal["preparedInvocation"]["invocationId"],
          "Concurrent CAS requires two independently reviewed actual native invocations")
    for turn in turns: runtime.approve(turn)
    control = {"schemaVersion": 1, "epoch": str(uuid.uuid4()), "key": {"subjectId": runtime.active_identity["subject"], "tenantId": runtime.active_identity["tenantId"],
        "studioHostId": runtime.witness["hostId"], "namespace": "home-layout"}, "revision": state["revision"], "timeoutMilliseconds": 30000}
    check(not runtime.cas_control.exists() and not runtime.cas_receipt.exists(), "Concurrent CAS needs a fresh one-shot rendezvous")
    atomic_json(runtime.cas_control, control)
    with ThreadPoolExecutor(max_workers=2) as executor:
        responses = list(executor.map(runtime.execute, turns))
    check(sorted(response.status for response in responses) == [200, 409], "Actual SQLite CAS did not produce exactly one native success and one conflict409")
    winner = next(response for response in responses if response.status == 200)
    loser = next(response for response in responses if response.status == 409)
    expect_error(loser, 409, "acme.home.conflict")
    admitted = winner.json()["data"]
    check(admitted["executed"] is True and admitted["toolResult"]["succeeded"] is True and admitted["toolResult"]["receipt"]["scope"] == "serverPersisted", "CAS winner lacks a real persisted receipt")
    barrier = json.loads(runtime.cas_receipt.read_text())
    check(barrier["status"] == "released" and barrier["releasedCount"] == 2 and barrier["epoch"] == control["epoch"] and barrier["key"] == control["key"]
          and barrier["revision"] == control["revision"] and len({row["httpTraceIdentifier"] for row in barrier["arrivals"]}) == 2,
          "The two distinct actual writes did not reach/release the original native CAS condition")
    after, aref, aread = runtime.snapshot("cas-owner-after")
    expected = copy.deepcopy(before["value"])
    next(item for item in expected["widgets"] if item["id"] == runtime.witness["widgetId"])["visible"] = not state["visible"]
    check(same(after["value"], expected) and after["revision"] == admitted["toolResult"]["receipt"]["after"]["revision"] != before["revision"], "CAS loser overwrote the winner or unrelated owner values changed")
    evidence = [bread, aread, winner.receipt, loser.receipt, retained(runtime.cas_receipt), runtime.save(runtime.output("cas-input"), "control", control)]
    for turn in turns: evidence.extend(turn.evidence)
    runtime.cas_control.unlink()  # Retain the immutable receipt/claimed epoch; do not rearm it.
    runtime.record(records, "concurrent-cas", bref, aref, evidence, {"ownerObserved": True, "noFalseSuccess": True, "noReplay": True,
        "oneWinner": True, "oneConflict409": True, "winnerStatePreserved": True}, actual={"stage": "native-cas", "diagnostic": loser.json()["error"]["code"]})


def _redaction(runtime, records):
    before, bref, bread = runtime.snapshot("redaction-owner-before")
    state, _, _ = runtime.state("redaction-state")
    turn = runtime.turn("opaque-read", tool=runtime.witness["readTool"], mode="explain", attachment=runtime.attachment(state))
    turn.message.require_status(200)
    opaque, _ = runtime.tool_result(turn)
    check(opaque.get("succeeded") is True and opaque.get("payload", {}).get("typeId") == runtime.witness["readResultType"]
          and opaque["payload"]["schemaVersion"] == "1" and same(opaque["payload"]["data"], state), "The real SDK did not preserve the module-owned opaque read payload")
    attachment = runtime.attachment(state)
    canaries = ["CANARY_" + secrets.token_hex(20) for _ in range(3)]
    attachment.update(sensitivity="secret", label=canaries[0], summary=canaries[1], references={"canary": canaries[2]}, content={"canary": canaries[1]})
    secret = runtime.turn("secret-redaction", tool=runtime.witness["readTool"], mode="explain", attachment=attachment)
    secret.message.require_status(200)
    trace = secret.transport.protocol_trace(secret.offset)
    requests = [row for row in trace["records"] if row["kind"] == "copilot.request" and row.get("data", {}).get("commandId") == secret.command_id]
    check(len(requests) == 1, "No unique actual provider-input observation for redaction")
    native_context = secret.message.json()["data"]["message"]["context"]
    redacted = [item for item in native_context if item["moduleId"] == runtime.witness["moduleId"] and item["contentType"] == runtime.witness["contextType"]]
    check(len(redacted) == 1 and redacted[0]["sensitivity"] == "secretRedacted" and redacted[0]["content"] is None
          and redacted[0]["target"] is None and redacted[0]["sourceId"] is None and not redacted[0]["references"],
          "Native admission did not actually redact the secret owning context")
    upstream = json.dumps(requests[0]["data"], sort_keys=True)
    owner_canaries = []
    def strings(value):
        if isinstance(value, dict):
            for child in value.values(): strings(child)
        elif isinstance(value, list):
            for child in value: strings(child)
        elif isinstance(value, str) and ("PRIVATE_" in value or "UNRELATED_" in value): owner_canaries.append(value)
    strings(before["value"])
    check(owner_canaries and all(value not in upstream for value in canaries + owner_canaries), "Actual provider input exposed a private caller/owner canary")
    # The fixture's defensive credential filter has no rules for these arbitrary strings.
    check("[redacted]" not in upstream and "Redacted context" in upstream, "Trace filtering cannot substitute for native context redaction")
    for profile in runtime.run.profiles.values():
        for file in profile["published"].glob("*.dll"):
            check(b"acme.home.widget" not in file.read_bytes() and b"acme_home_set_visibility" not in file.read_bytes(), "A common consumer DLL contains resource-specific protocol logic")
    after, aref, aread = runtime.snapshot("redaction-owner-after")
    check(same(before, after), "Redaction/read changed the full native owner")
    runtime.record(records, "redaction-opacity", bref, aref, [bread, aread, *turn.evidence, *secret.evidence, trace["receipt"]],
        {"canariesOmitted": True, "unrelatedPreserved": True, "opaqueCommonPayloads": True, "separateProtocolFixture": True})


def _remove(runtime, records, deltas):
    before, bref, bread = runtime.snapshot("remove-owner-before")
    pending = runtime.prepare("pending-before-removal"); runtime.approve(pending)
    evidence = [bread, *pending.evidence]
    before_generations = {}
    membership_transitions = {}
    for name in ("backend", "studio"):
        client = runtime.backend if name == "backend" else runtime.studio
        package = runtime.run.report["authors"][name]["packed"]["package"]
        installed, installed_record = runtime.native.membership(client, name + "-membership-before-disable", studio=name == "studio")
        require_installed_package(installed, package)
        evidence.append(installed_record["receipt"])
        generation, record = runtime.native.generation(client, name + "-before-module-removal")
        before_generations[name] = generation["generation"]; evidence.append(record["receipt"])
        feature = runtime.witness[name + "Feature"]
        disabled = runtime.native.feature(client, name + "-disable-module", feature, enabled=False)
        evidence.extend(disabled["evidence"])
        disabled_inventory, disabled_record = runtime.native.membership(client, name + "-membership-after-disable", studio=name == "studio")
        require_installed_package(disabled_inventory, package)
        evidence.append(disabled_record["receipt"])
        remove_resource_archives(runtime.run, name, deltas[name])
        _, record = runtime.native.reconcile(client, name + "-native-remove")
        evidence.append(record["receipt"])
        current, record = runtime.native.generation(client, name + "-removed-generation")
        evidence.append(record["receipt"])
        removed, removed_record = runtime.native.membership(client, name + "-membership-after-reconcile", studio=name == "studio")
        require_removed_package(disabled_inventory, removed, package)
        check(all(identity in removed and removed[identity]["version"] == row["version"] for identity, row in runtime.baseline_membership[name].items()),
              "Resource removal changed unrelated baseline native package membership")
        evidence.append(removed_record["receipt"])
        membership_transitions[name] = {"archive": {"id": package["id"], "version": package["version"], "sha256": package["sha256"]},
            "beforeDisable": installed_record["body"], "afterDisable": disabled_record["body"], "afterReconcile": removed_record["body"],
            "generationBeforeDisable": before_generations[name], "generationAfterDisable": disabled["after"]["generation"], "generationAfterRemoval": current["generation"],
            "removalReloadedServingShell": current["generation"] != disabled["after"]["generation"]}
        if name == "backend":
            check(not any(row["moduleId"] == runtime.witness["moduleId"] for row in current["tools"] + current["contextSources"]), "Removed package still contributes resource contracts")
    registry, reg = runtime.native.json(runtime.studio, "removed-registry", "/_elsa/module-management/registry")
    check(not any(row["id"] == runtime.witness["moduleId"] for row in registry["modules"])
          and not any(row["id"] == runtime.witness["studioPackage"] for row in registry["packages"]), "Removed module/package remains in actual native membership")
    evidence.append(reg["receipt"])
    for index, url in enumerate([runtime.resource_manifest["entry"], *runtime.resource_manifest.get("styles", [])]):
        removed_asset, _ = runtime.native.request(runtime.studio, "removed-resource-asset-" + str(index), url)
        check(removed_asset["status"] == 404, "Removed/unloaded resource still serves its former protected asset URL")
        evidence.extend([removed_asset["receipt"], removed_asset["body"]])
    # Authenticate in the actual current shell. A stale/expired token does not qualify as lookup refusal.
    runtime.sign_in()
    current = runtime.transport("removed-proposal-lookup")
    current.bootstrap().require_status(200)
    response = runtime.execute(pending, transport=current)
    value = expect_error(response, 404, "agent.proposal.not_found")
    check(pending.proposal["id"] in value["error"]["message"], "Lookup refusal did not name the original retained proposal")
    after, aref, aread = runtime.snapshot("remove-owner-after")
    checks = _unchanged(before, after)
    runtime.record(records, "pending-module-removed", bref, aref, evidence + [response.receipt, aread], checks,
        actual={"stage": "proposal-lookup", "diagnostic": value["error"]["code"]},
        extra={"generationBoundRetention": "The native reload replaced shell-local in-memory Agent stores; the old proposal is absent and was never retargeted or replayed.",
               "originalProposalId": pending.proposal["id"], "nativeMembership": membership_transitions})
    bootstrap = current.bootstrap().require_status(200)
    check(bootstrap.json()["data"]["providerStatus"] == "available", "Removing the resource made the unrelated actual provider unavailable")
    runtime.record(records, "module-remove", bref, aref, evidence + [aread, bootstrap.receipt],
        {"membership": True, "servingGeneration": True, "contributions": True, "providerAvailable": True}, extra={"nativeMembership": membership_transitions})


def _incompatible(runtime, records):
    before, bref, bread = runtime.snapshot("incompatible-owner-before")
    author = runtime.run.report["authors"]["incompatibleStudio"]
    package = author["packed"]["package"]
    selected = select_native_archives(runtime.run, "studio", [package], author)
    check(author["requiredSdkVersion"] == "999.0.0" and package["version"] != runtime.run.report["authors"]["studio"]["packed"]["package"]["version"], "SDK control is not a separately compiled incompatible artifact")
    stage_native_archives(runtime.run, "studio", selected)
    _, reconciled = runtime.native.reconcile(runtime.studio, "incompatible-native-install")
    # Enable the actual feature so admission is tested rather than hidden behind disabled status.
    applied = runtime.native.feature(runtime.studio, "incompatible-feature-enable", runtime.witness["studioFeature"], enabled=True)
    installed, installed_record = runtime.native.membership(runtime.studio, "incompatible-active-membership", studio=True)
    require_installed_package(installed, package)
    registry, record = runtime.native.json(runtime.studio, "incompatible-registry", "/_elsa/module-management/registry")
    bootstrap, manifest_record = runtime.native.json(runtime.studio, "incompatible-bootstrap-manifest", "/_elsa/studio/modules")
    modules = [item for item in registry["modules"] if item["id"] == runtime.witness["moduleId"]]
    declarations = [item for item in bootstrap["modules"] if item["id"] == runtime.witness["moduleId"]]
    check(len(modules) == len(declarations) == 1, "Incompatible archive has no unique active native manifest/registry identity")
    module, declaration = modules[0], declarations[0]
    check(module.get("version") == declaration.get("version") == package["version"] and module.get("compatibility") == "incompatible"
          and declaration.get("requiredSdkVersion") == "999.0.0" and declaration.get("requiredHostVersion") == "*"
          and isinstance(bootstrap.get("sdkVersion"), str) and bootstrap["sdkVersion"] not in ("", "999.0.0"),
          "Real unsupported SDK declaration was not the cause of module incompatibility")
    check(same(module.get("manifest"), {key: declaration.get(key) for key in ("entry", "styles", "capabilities")}),
          "Slim registry and full bootstrap manifest describe different module contributions")
    # Management 'available' means manifest discovery; the browser loader owns
    # SDK compatibility exclusion and the absence of loaded contributions below.
    native_evidence = [bread, reconciled["receipt"], installed_record["receipt"], record["receipt"], manifest_record["receipt"], *applied["evidence"],
        runtime.save(runtime.output("incompatible-input"), "artifact", package)]
    class BrowserAndNativeRecords:
        def add(self, name, observation):
            check(name == "sdk-incompatible" and observation.get("observations", {}).get("browserContributionsAbsent") is True
                  and observation["observations"].get("commonStudioUsable") is True, "SDK control lacks actual browser exclusion and common Studio usability observations")
            after, aref, aread = runtime.snapshot("incompatible-owner-after-browser")
            _unchanged(before, after)
            current, current_record = runtime.native.membership(runtime.studio, "incompatible-membership-after-browser", studio=True)
            require_installed_package(current, package)
            observation.update(ownerBefore=bref, ownerAfter=aref, nativeIncompatibleModule={
                "packageId": package["id"], "version": package["version"], "sha256": package["sha256"],
                "requiredSdkVersion": declaration["requiredSdkVersion"], "requiredHostVersion": declaration["requiredHostVersion"],
                "actualSdkVersion": bootstrap["sdkVersion"], "discoveryStatus": module.get("status"), "compatibility": module["compatibility"]})
            observation["evidence"].extend([*native_evidence, aread, current_record["receipt"]])
            return records.add(name, observation)
    runtime.browser(BrowserAndNativeRecords(), "sdk-incompatible", "sdk-incompatible", extra={
        "ownerPath": HOME_PREFERENCES_PATH, "incompatibleModule": {"moduleId": runtime.witness["moduleId"],
        "packageId": package["id"], "version": package["version"], "requiredSdkVersion": declaration["requiredSdkVersion"]}})
    author["runtimeRejectionObserved"] = True
    runtime.run.save()
    runtime.native.feature(runtime.studio, "incompatible-feature-disable", runtime.witness["studioFeature"], enabled=False)
    # Remove only actual staged feed copies, retaining every original archive and closure input.
    feed = runtime.run.profiles["studio"]["runtimeFeed"]
    for item in selected:
        source = Path(item["path"]); destination = feed / source.name
        check(destination.resolve().parent == feed.resolve() and destination.resolve() != source.resolve() and sha(destination) == item["sha256"], "Incompatible feed copy identity changed")
        # Keep dependencies that were already present before the incompatible root; only this root differs.
        if destination.name == Path(package["path"]).name: destination.unlink()
    runtime.native.reconcile(runtime.studio, "incompatible-native-remove")


def _restore(runtime, records, deltas):
    before, bref, bread = runtime.snapshot("restore-owner-before")
    evidence = [bread]
    for name in ("backend", "studio"):
        client = runtime.backend if name == "backend" else runtime.studio
        restore_resource_archives(runtime.run, name, deltas[name])
        _, record = runtime.native.reconcile(client, name + "-native-restore")
        evidence.append(record["receipt"])
        config = {"StudioHostId": runtime.witness["hostId"], "WriteRendezvousControlFile": str(runtime.cas_control),
                  "WriteRendezvousReceiptFile": str(runtime.cas_receipt)} if name == "backend" else {}
        applied = runtime.native.feature(client, name + "-restore-enable", runtime.witness[name + "Feature"], enabled=True, configuration=config)
        evidence.extend(applied["evidence"])
    evidence.extend(_active_resource(runtime, "restored"))
    after, aref, aread = runtime.snapshot("restore-owner-after")
    check(same(before, after), "Restoring original archive bytes changed the owner")
    for name, delta in deltas.items():
        for item in delta["added"]:
            check(sha(Path(item["path"])) == sha(Path(item["sourceArchive"])) == item["sha256"], "Restoration did not use the original independent artifact bytes")
    runtime.record(records, "module-restore", bref, aref, [*evidence, aread], {"originalArchive": True, "membership": True, "servingGeneration": True, "contributions": True})


def _positives(runtime, records, *, restored=False):
    prefix = "restored-" if restored else ""
    for provider in ("copilot", "anthropic"):
        runtime.select_provider(provider)
        runtime.browser(records, prefix + "resource-" + provider, "resource", provider=provider)
        runtime.browser(records, prefix + "workflow-" + provider, "workflow", provider=provider, workflow=True)


def qualify_distributions(run):
    """Only this complete path may promote a prepared distribution report to passed."""
    if run.report["status"] == "preparing":
        check(not run.report.get("authors") and not run.report["consumers"] and run.report.get("producer", {}).get("source") == run.source,
              "Do not resume or replace partial author/consumer preparation")
        build_authors(run)
        build_incompatible_studio(run)
        prepare_consumers(run)
        profiles = {name: {key: value for key, value in profile.items() if key != "env"} for name, profile in run.profiles.items()}
        for name, profile in profiles.items():
            profile["envOverrides"] = {key: value for key, value in run.profiles[name]["env"].items() if key in RUNTIME_ENVIRONMENT}
        private_json(run.root / "consumer-profiles-private.json", json.loads(json.dumps(profiles, default=str)))
        run.assert_immutable_inputs()
        run.report["status"] = "distributions-prepared"
        run.save()
    check(run.report["status"] == "distributions-prepared", "Prepare all real distributions before native qualification")
    check(not run.report["cases"], "A final qualification needs fresh case outputs, not resumed partial status")
    run.assert_immutable_inputs()
    run.report.update(status="running", independentPackageWitness=False, expectedControls=copy.deepcopy(EXPECTED_CONTROLS))
    run.save()
    admin = {"username": "weaver-" + secrets.token_hex(10), "password": secrets.token_urlsafe(40) + "!aA1"}
    backend, studio = owned_origin(), owned_origin()
    configure_runtime(run, backend_url=backend, studio_url=studio, admin_username=admin["username"], admin_password=admin["password"], provider="copilot", enroll=True)
    principals = _seed_principals(run)
    runtime = JourneyRuntime(run, backend, studio, admin, principals)
    # Configure the generic optional gate before the first process; default nonmatching release is already valid.
    shells_path = run.profiles["backend"]["published"] / "shells.json"
    shells = json.loads(shells_path.read_text())
    shells["CShells"]["Shells"]["Authoring"]["Features"]["AcmeCopilotProtocolFixture"]["GateFile"] = str(runtime.gate_file)
    private_json(shells_path, shells)
    private_json(runtime.root / "private-seeds.json", {"admin": admin, "principals": principals})
    records = CaseRecords(run, EXPECTED_CONTROLS)
    with server(run, "backend", backend), server(run, "studio", studio):
        _bootstrap(runtime)
        freeze_consumer_baseline(run)
        _module_missing(runtime, records)
        deltas = _install(runtime, records)
        _positives(runtime, records)
        runtime.select_provider("copilot")
        runtime.browser(records, "duplicate-execute", "duplicate-execute")
        runtime.browser(records, "duplicate-ack", "duplicate-ack", workflow=True)
        runtime.browser(records, "renderer-unavailable", "renderer-unavailable", workflow=True)
        runtime.browser(records, "consumed-intent-remount", "consumed-intent-remount")
        # The actual UI remount disabled the resource Studio feature. Restore its
        # selected state explicitly for the remaining journeys and later removal.
        runtime.native.feature(runtime.studio, "after-remount-resource-enable", runtime.witness["studioFeature"], enabled=True)
        _active_resource(runtime, "after-remount-restored")
        for name in ("malformed-result", "lost-confirmation", "client-binding-removed"):
            runtime.browser(records, name, name, workflow=True)
        _context_controls(runtime, records)
        _resource_absence(runtime, records)
        _authority_controls(runtime, records)
        _stale(runtime, records)
        _deny(runtime, records)
        _cancel(runtime, records)
        _redaction(runtime, records)
        _concurrent_cas(runtime, records)
        _remove(runtime, records, deltas)
        _incompatible(runtime, records)
        _restore(runtime, records, deltas)
        _positives(runtime, records, restored=True)
        capture_consumer_after(run)
        check({case["id"] for case in run.report["cases"]} == set(REQUIRED_CASES) and len(run.report["cases"]) == len(REQUIRED_CASES),
              "Every one of the 38 actual required cases must be completed exactly once")
        run.assert_immutable_inputs()
        run.report["status"] = "verifying"
        run.save()
        validate_report(run.report, run.root, EXPECTED_CONTROLS)
    # Process cleanup/cache evidence must also pass before the aggregate becomes accepted.
    run.assert_immutable_inputs()
    validate_report(run.report, run.root, EXPECTED_CONTROLS)
    run.report.update(status="passed", independentPackageWitness=True)
    run.save()
