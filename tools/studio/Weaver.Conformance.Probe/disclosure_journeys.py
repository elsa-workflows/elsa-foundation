"""Mandatory current-authority qualification, separate from the original 38 M1 cases.

Only real native sessions, Identity grants, owning-resource responses and SDK-factory
traces establish these observations. No provider call or effect is retried.
"""
from __future__ import annotations

from contextlib import contextmanager
import copy
import hashlib
from pathlib import Path
import signal
import threading
import time

from agent_transport import AgentTransport, _json as strict_json
from artifacts import TOOL, check, sha
from case_records import retained
from journey_runtime import BASE_GRANTS, HOST_HEADER, same


CASE_SECONDS = 180
EXPECTED_CASES = {
    f"{provider}-{phase}-authority-withdrawal"
    for provider in ("copilot", "anthropic") for phase in ("accepted-message", "accepted-receipt")
}
EXPECTED_ERRORS = {"accepted-message": "agent.context.incomplete", "accepted-receipt": "agent.result.disclosure_denied"}


@contextmanager
def _bounded(runtime, name):
    check(threading.current_thread() is threading.main_thread() and hasattr(signal, "setitimer"),
          "Native disclosure qualification requires an interruptible main-thread stage deadline")
    previous = signal.getitimer(signal.ITIMER_REAL)
    check(previous == (0.0, 0.0), "Do not replace another qualification stage's active deadline")
    started = time.monotonic()
    stage = {"name": name, "status": "running", "boundSeconds": CASE_SECONDS}
    runtime.run.report["contextDisclosureQualification"]["stages"].append(stage)
    runtime.run.save()
    def expired(_signum, _frame):
        raise TimeoutError("Native disclosure stage expired without retry: " + name)
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


def require_native_denial(stream, phase):
    """A transport/authentication/provenance/provider failure cannot stand in for admission."""
    expected = EXPECTED_ERRORS[phase]
    check(stream.status == 200 and stream.error_type is None, "Disclosure refusal needs a successful native SSE transport")
    values = [frame.value for frame in stream.frames]
    errors = [value.get("error") for value in values if value.get("kind") == "error"]
    check(len(errors) == 1 and isinstance(errors[0], dict) and errors[0].get("code") == expected,
          "Wrong native disclosure refusal; expected " + expected)
    check(errors[0].get("statusCode") == (400 if phase == "accepted-message" else 403),
          "The disclosure refusal has the wrong native status")
    check(not any(value.get("kind") in {"messageDelta", "completed", "proposalCreated", "toolCallStarted", "toolCallCompleted"}
                  for value in values), "A withheld turn emitted provider content, a proposal or tool execution")
    return errors[0]


def require_no_adapter_admission(records):
    forbidden = {f"{provider}.{kind}" for provider in ("copilot", "anthropic")
                 for kind in ("request", "call", "return", "continuation")}
    check(not any(row.get("kind") in forbidden for row in records),
          "A denied native stream reached an actual adapter request, call, return or continuation")


def canonical_result(result):
    value = copy.deepcopy(result)
    receipt = value.get("receipt")
    check(isinstance(receipt, dict), "A genuine accepted effect receipt is required")
    scope = receipt.get("scope")
    check(scope in ("serverPersisted", "ServerPersisted") or type(scope) is int and scope == 1,
          "The native outcome has an unexpected effect scope")
    # API and generic result serializers use different representations of the same enum.
    # Preserve the originals and compare every other result/receipt field exactly.
    receipt["scope"] = "ServerPersisted"
    return value


def require_stored_result(messages, expected):
    matches = []
    for message in messages:
        if str(message.get("role", "")).lower() != "tool":
            continue
        value = strict_json(message.get("content", ""))
        if isinstance(value, dict) and value.get("toolCallId") == expected.get("toolCallId"):
            check(str(message.get("status", "")).lower() == "completed", "The accepted result journal was replaced by a failure")
            matches.append(value)
    check(len(matches) == 1, "The owning native session must retain exactly one genuine accepted outcome")
    check(same(canonical_result(matches[0]), canonical_result(expected)),
          "Disclosure refusal changed the genuine stored result or its historical receipt")
    return matches[0]


def _identity(runtime, principal=0):
    return {key: value for key, value in runtime.identities[principal].items() if key != "storageState"}


def _accepted(runtime, turn, state, source_id):
    message = turn.message.require_status(200).json()["data"]["message"]
    check(message.get("sessionId") == turn.session and str(message.get("role", "")).lower() == "user"
          and str(message.get("status", "")).lower() == "pending" and bool(message.get("id"))
          and bool(message.get("requestId")), "The native session did not accept this exact pending prompt")
    owned = [item for item in message.get("context", []) if item.get("moduleId") == runtime.witness["moduleId"]]
    check(owned and all(item.get("contributorId") == source_id for item in owned), "Accepted context lost its actual installed source provenance")
    references = [item for item in owned if item.get("contentType") == runtime.witness["contextType"]]
    check(len(references) == 1 and references[0].get("target", {}).get("revision") == state["revision"]
          and references[0]["target"].get("resourceId") == state["widgetId"], "Accepted context identified another resource or revision")
    return message


def _session(transport, turn, accepted):
    response = transport.session(turn.session).require_status(200)
    session = response.json()["data"]
    check(session.get("sessionId") == turn.session and any(message.get("id") == accepted["id"]
          and str(message.get("role", "")).lower() == "user" for message in session.get("messages", [])),
          "The denial lost or changed its original native session/message")
    return session, response.receipt


def _checkpoint(runtime):
    result = {}
    for provider, path in runtime.run.profiles["backend"]["traceFiles"].items():
        check(path.is_file(), "A completed actual-adapter positive must precede disclosure qualification")
        data = path.read_bytes()
        check(data and data.endswith(b"\n"), "The actual adapter trace is missing or incomplete")
        rows = [strict_json(line.decode("utf-8")) for line in data.splitlines() if line.strip()]
        check(any(row.get("kind") == provider + ".request" for row in rows), "No actual request established the adapter trace instrumentation")
        result[provider] = {"path": str(path), "offset": len(data), "prefixSha256": hashlib.sha256(data).hexdigest()}
    return result


def _no_admission(runtime, checkpoint, name):
    evidence, observations = [], {}
    for provider, before in checkpoint.items():
        path = runtime.run.profiles["backend"]["traceFiles"][provider]
        data = path.read_bytes()
        check(len(data) >= before["offset"] and hashlib.sha256(data[:before["offset"]]).hexdigest() == before["prefixSha256"],
              "The actual adapter trace was truncated or replaced")
        observer = AgentTransport(runtime.backend, runtime.output(name + "-trace-" + provider), trace_file=path)
        delta = observer.protocol_trace(before["offset"])
        check(delta["incompleteTailBytes"] == 0, "An incomplete SDK trace cannot prove absence of provider disclosure")
        require_no_adapter_admission(delta["records"])
        observations[provider] = {**before, "nextOffset": delta["nextOffset"], "modelAdmissionCount": 0}
        evidence.append(delta["receipt"])
    return observations, evidence


def _withdraw(runtime, name, identity):
    endpoint = "/__qualification/principals/0/authority"
    before, first = runtime.native.json(runtime.backend, name + "-authority-before", endpoint)
    receipts = runtime.set_grants(0, "without-read")
    after, last = runtime.native.json(runtime.backend, name + "-authority-after", endpoint)
    fresh = _identity(runtime)
    check(fresh["subject"] == identity["subject"] == before["subject"] == after["subject"]
          and fresh["tenantId"] == identity["tenantId"] == before["tenant"] == after["tenant"]
          and before["revision"] != after["revision"] and after["revision"]
          and set(after["directPermissions"]) == set(BASE_GRANTS) - {"studio/preferences:read"},
          "Authority withdrawal did not isolate the same native actor/tenant with an exact saved IAM revision")
    denied, owner = runtime.native.json(runtime.clients[0], name + "-current-owner-denied", runtime.witness["ownerEndpoint"],
        headers={HOST_HEADER: runtime.witness["hostId"]}, expected=403)
    check(denied.get("title") == "acme.home.permission_denied", "Wrong-cause owning-resource refusal cannot qualify disclosure")
    return runtime.clients[0], {"before": before, "after": after, "freshIdentity": fresh, "ownerDenial": denied}, [first["receipt"], *receipts, last["receipt"], owner["receipt"]]


def _correlation(runtime, turn, accepted, identity, source_id):
    selected = strict_json(runtime.command_file.read_text())
    frozen = turn.transport.output / "selected-command.json"
    check(frozen.is_file() and same(selected, strict_json(frozen.read_text())) and selected.get("commandId") == turn.command_id,
          "The accepted native turn's frozen protocol command changed")
    return {"sessionId": turn.session, "messageId": accepted["id"], "requestId": accepted["requestId"],
            "commandId": turn.command_id, "toolName": selected["toolName"], "contributorId": source_id,
            "actor": identity["subject"], "tenant": identity["tenantId"], "commandInput": retained(frozen)}


def _message_case(runtime, provider, source_id):
    name = provider + "-accepted-message-authority-withdrawal"
    owner, _ = runtime.sign_in(0)
    identity = _identity(runtime)
    before, bref, bread = runtime.state(name + "-owner-before", http=owner)
    checkpoint = _checkpoint(runtime)
    turn = runtime.turn(name, tool=runtime.witness["readTool"], arguments={}, http=owner, attachment=runtime.attachment(before),
                        capability="acme.home.read", mode="explain", drain=False)
    accepted = _accepted(runtime, turn, before, source_id)
    current, authority, grants = _withdraw(runtime, name, identity)
    transport = runtime.transport(name + "-fresh-authority-stream", http=current)
    stream = transport.collect_turn(turn.session)
    error = require_native_denial(stream, "accepted-message")
    counters, traces = _no_admission(runtime, checkpoint, name)
    _, session_receipt = _session(transport, turn, accepted)
    restored = runtime.set_grants(0, "full")
    after, aref, aread = runtime.state(name + "-owner-restored", http=runtime.clients[0])
    check(same(before, after), "The withheld initial stream changed its owning resource")
    return {"id": name, "status": "passed", "admissionWithheld": True, "observedError": error,
            "correlation": _correlation(runtime, turn, accepted, identity, source_id), "authority": authority,
            "adapterCounters": counters, "ownerBefore": bref, "ownerAfter": aref, "ownerUnchanged": True,
            "evidence": [bread, *turn.evidence, *grants, stream.receipt, *traces, session_receipt, *restored, aread]}


def _receipt_case(runtime, provider, source_id):
    name = provider + "-accepted-receipt-authority-withdrawal"
    owner, _ = runtime.sign_in(0)
    identity = _identity(runtime)
    before, bref, bread = runtime.state(name + "-owner-before", http=owner)
    turn = runtime.prepare(name, http=owner)
    accepted = _accepted(runtime, turn, before, source_id)
    runtime.approve(turn)
    execution = runtime.execute(turn).require_status(200)
    applied = execution.json()["data"]
    result = applied.get("toolResult")
    check(applied.get("executed") is True and applied.get("status") == "executed" and isinstance(result, dict)
          and result.get("succeeded") is True, "A genuine accepted effect must precede receipt disclosure qualification")
    canonical_result(result)
    access, receipt = result.get("access", {}), result["receipt"]
    check(access.get("moduleId") == runtime.witness["moduleId"] and access.get("disclosureSourceId") == source_id
          and bool(access.get("invocationId")) and access["invocationId"] == receipt.get("invocationId"),
          "Accepted effect lost its frozen source/preparation identity")
    after_effect, eref, eread = runtime.state(name + "-owner-after-effect", http=owner)
    check(after_effect["visible"] is not before["visible"] and after_effect["revision"] != before["revision"]
          and receipt["before"]["revision"] == before["revision"] and receipt["after"]["revision"] == after_effect["revision"],
          "Independent owning-resource readback did not confirm the accepted receipt")
    checkpoint = _checkpoint(runtime)
    current, authority, grants = _withdraw(runtime, name, identity)
    transport = runtime.transport(name + "-fresh-authority-stream", http=current)
    stream = transport.collect_turn(turn.session)
    error = require_native_denial(stream, "accepted-receipt")
    counters, traces = _no_admission(runtime, checkpoint, name)
    restored = runtime.set_grants(0, "full")
    after, aref, aread = runtime.state(name + "-owner-restored", http=runtime.clients[0])
    check(same(after_effect, after), "Withholding the receipt changed or repeated the accepted resource effect")
    session_transport = runtime.transport(name + "-retained-session", http=runtime.clients[0])
    session, session_receipt = _session(session_transport, turn, accepted)
    stored = require_stored_result(session["messages"], result)
    trace = transport.protocol_trace(turn.offset)
    calls = [row for row in trace["records"] if row.get("kind") == provider + ".call" and row.get("data", {}).get("commandId") == turn.command_id]
    check(len(calls) == 1, "The accepted native command must have exactly one original actual adapter tool call")
    return {"id": name, "status": "passed", "admissionWithheld": True, "observedError": error,
            "correlation": _correlation(runtime, turn, accepted, identity, source_id), "authority": authority,
            "adapterCounters": counters, "ownerBefore": bref, "ownerAfterEffect": eref, "ownerAfter": aref,
            "acceptedResult": result, "storedResult": stored, "acceptedResultPreserved": True,
            "effectCountObserved": 1, "originalToolCalls": 1, "continuationToolCalls": 0,
            "ownerAfterEffectEqualsAfterContinuation": True,
            "evidence": [bread, *turn.evidence, execution.receipt, eread, *grants, stream.receipt, *traces,
                         *restored, aread, session_receipt, trace["receipt"]]}


def qualify_context_disclosure(runtime):
    run = runtime.run
    check("contextDisclosureQualification" not in run.report, "Never resume or overwrite a prior disclosure qualification")
    original_cases = copy.deepcopy(run.report["cases"])
    section = {"status": "running", "evidenceCategory": "deterministic-provider-protocol", "liveProvider": False,
               "source": run.source, "caseIDs": [], "records": [], "stages": []}
    # Tool input identity is recorded as a digest, not an export reference outside the run.
    section["driver"] = {"repositoryPath": "tools/studio/Weaver.Conformance.Probe/disclosure_journeys.py", "sha256": sha(TOOL / "disclosure_journeys.py")}
    run.report["contextDisclosureQualification"] = section
    run.save()
    try:
        for provider in ("copilot", "anthropic"):
            with _bounded(runtime, provider + "-disclosure-provider-selection"):
                selection = runtime.select_provider(provider)
                generation, observation = runtime.native.generation(runtime.backend, provider + "-disclosure-source")
                sources = [item for item in generation["contextSources"] if item["moduleId"] == runtime.witness["moduleId"]]
                check(len(sources) == 1 and sources[0].get("id"), "The actual installed resource source is absent or ambiguous")
                source_id = sources[0]["id"]
            for phase, execute in (("accepted-message", _message_case), ("accepted-receipt", _receipt_case)):
                name = provider + "-" + phase + "-authority-withdrawal"
                section["activeCase"] = name
                with _bounded(runtime, name):
                    value = execute(runtime, provider, source_id)
                    value["evidence"].extend([*selection, observation["receipt"]])
                    owners = [value["ownerBefore"], value["ownerAfter"]]
                    if "ownerAfterEffect" in value: owners.append(value["ownerAfterEffect"])
                    for ref in [*owners, *value["evidence"]]:
                        path = Path(ref["path"])
                        check(path.resolve().is_relative_to(run.root.resolve()) and path.is_file() and sha(path) == ref["sha256"],
                              "Disclosure qualification evidence is missing, changed or outside the owned run")
                    record = runtime.save(runtime.output(name + "-record"), "observation", value)
                    section["caseIDs"].append(name)
                    section["records"].append({"id": name, "status": "passed", "record": record})
        check(set(section["caseIDs"]) == EXPECTED_CASES and len(section["records"]) == 4,
              "All four current-authority disclosure cases must be observed exactly once")
        check(same(original_cases, run.report["cases"]), "Disclosure qualification changed the original M1 case records")
        section.pop("activeCase", None)
        section["status"] = "passed"
    except BaseException as error:
        section.update(status="failed", failure={"type": type(error).__name__, "message": str(error)})
        raise
    finally:
        run.save()
