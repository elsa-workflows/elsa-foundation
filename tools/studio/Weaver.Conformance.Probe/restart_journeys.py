"""#3441 restart inventory (T031): story 2 recovery across real restarts of the packaged consumer host.

The backend consumer runs the packaged durable session store (`WeaverEntityFrameworkCore` on SQLite, a
per-run database under the owned output root) and the driver restarts its own host process between phases
(runtime.RestartableServer). Every case runs once on each actual adapter, Copilot's delegated loop and Anthropic's
host-driven loop, through the same deterministic protocol factories as the rest of the witness. Seven restarts per
adapter carry the ten boundaries:

* graceful, one restart per case: idle, pending review and approved-before-dispatch before their deadline, a receipt
  recorded while the turn is paused, and a second client of the same owner beside a foreign actor. Each of these
  applies one effect to the same native owner (the seed user's status card), so each gets its own restart: its owner
  reads bracket only its own effect, and its reviewed operation stays bound to the revision it was prepared against;
* abrupt (the owned process group is killed): a server write dispatched and held at the module's native write
  rendezvous, and a provider turn held at the protocol fixture's pre-call gate;
* after-deadline (stopped before, started after the prepared deadline, on the real clock): pending review,
  approved-before-dispatch and an uncertain client operation the owner then resolves.

The abrupt and after-deadline cases leave their owner unchanged, so each of those phases shares one restart. A graceful
stop releases the leases its sessions hold, so a recovered request after a graceful restart is never answered
`weaver.session.owned_elsewhere`; only an abrupt stop's leases have to lapse.

A native negative control first restarts the same host on the in-memory default store, where the pending review must
be lost. Each case asserts the recovered outcome, the execution count at the module's independent native owner
(Preferences or the workflow draft) and the audit `settledBy` values. Gates and the write rendezvous only hold real
paths; they never stand in for the engine, the store or the owner. Nothing here promotes a package witness.
"""
from __future__ import annotations

import calendar
from contextlib import contextmanager
from concurrent.futures import ThreadPoolExecutor
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import signal
import stat
import threading
import time
from urllib.parse import urlencode
import uuid

from weaver_transport import WeaverTransportError, _json as strict_json
from artifacts import check
from case_records import retained
from consumers import DURABLE_STORE_DEPENDENCY, DURABLE_STORE_FEATURE, enable_durable_weaver_store
from journey_runtime import atomic_json, same
from lifecycle_journeys import (LifecycleCaseFailure, _executed, _exchange, _json_file, _open, _path, _provider, _proposal_path,
                                _session, _session_path, _settle, _size_owner, _sse, _stored_once, _trace, _verify_continuation,
                                _verify_open, _verify_pause)
from owner_resources import seed_workflow
from runtime import private_json

PROVIDERS = ("copilot", "anthropic")
EVIDENCE_CATEGORY = "deterministic-provider-protocol"
RESTART_QUALIFICATION = "restartRecoveryQualification"
GRACEFUL, ABRUPT, AFTER_DEADLINE = "graceful", "abrupt", "after-deadline"
PHASES = (GRACEFUL, ABRUPT, AFTER_DEADLINE)
RESTART_CASES = {
    "idle": GRACEFUL,
    "pending-review-before-deadline": GRACEFUL,
    "pending-review-after-deadline": AFTER_DEADLINE,
    "approved-before-dispatch-before-deadline": GRACEFUL,
    "approved-before-dispatch-after-deadline": AFTER_DEADLINE,
    "dispatched-without-receipt": ABRUPT,
    "receipt-recorded-paused": GRACEFUL,
    "provider-turn-in-progress": ABRUPT,
    "owner-resolution": AFTER_DEADLINE,
    "second-client": GRACEFUL,
}
EXPECTED_CASES = frozenset(f"{provider}-{key}" for provider in PROVIDERS for key in RESTART_CASES)
# Preparation order within one phase: deadline-sensitive work is prepared last, so it is freshest at the restart.
PHASE_ORDER = {
    GRACEFUL: ("idle", "receipt-recorded-paused", "second-client", "approved-before-dispatch-before-deadline", "pending-review-before-deadline"),
    ABRUPT: ("dispatched-without-receipt", "provider-turn-in-progress"),
    AFTER_DEADLINE: ("owner-resolution", "approved-before-dispatch-after-deadline", "pending-review-after-deadline"),
}
check(all(set(PHASE_ORDER[phase]) == {key for key, kind in RESTART_CASES.items() if kind == phase} for phase in PHASES),
      "Every restart case belongs to exactly one phase")
# One host restart per group, in this order on each adapter. Every graceful case applies an effect to the same native
# owner, so each one is alone in its group; the abrupt and after-deadline cases leave the owner unchanged and share theirs.
RESTART_GROUPS = tuple((GRACEFUL, (key,)) for key in PHASE_ORDER[GRACEFUL]) + ((ABRUPT, PHASE_ORDER[ABRUPT]), (AFTER_DEADLINE, PHASE_ORDER[AFTER_DEADLINE]))
# Driver-owned expected outcomes; each record states its observed outcome and any other value fails.
EXPECTED_OUTCOMES = {
    "idle": {"restart": GRACEFUL, "session": "same", "history": "retained", "policy": "same", "continuation": "completed",
             "executions": 1, "settledBy": []},
    "pending-review-before-deadline": {"restart": GRACEFUL, "recovered": "awaitingApproval", "decision": "approved", "execute": "executed",
                                       "continuation": "completed", "executions": 1, "settledBy": []},
    "pending-review-after-deadline": {"restart": AFTER_DEADLINE, "recovered": "expired", "execute": "weaver.proposal.expired",
                                      "executions": 0, "settledBy": ["recovery"]},
    "approved-before-dispatch-before-deadline": {"restart": GRACEFUL, "recovered": "approved", "recoveryExecuted": False, "execute": "executed",
                                                 "continuation": "completed", "executions": 1, "settledBy": []},
    "approved-before-dispatch-after-deadline": {"restart": AFTER_DEADLINE, "recovered": "expired", "execute": "weaver.proposal.expired",
                                                "executions": 0, "settledBy": ["recovery"]},
    "dispatched-without-receipt": {"restart": ABRUPT, "dispatch": "held-at-owner", "recovered": "uncertain", "execute": "uncertain",
                                   "executions": 0, "settledBy": ["recovery"]},
    "receipt-recorded-paused": {"restart": GRACEFUL, "recovered": "executed", "continuation": "completed", "continuations": 1,
                                "repeatDelivery": "weaver.message.no_pending", "executions": 1, "settledBy": []},
    "provider-turn-in-progress": {"restart": ABRUPT, "recovered": "cancelled", "statusReason": "interrupted", "proposals": 0,
                                  "continuation": "completed", "executions": 0, "settledBy": []},
    "owner-resolution": {"restart": AFTER_DEADLINE, "dispatch": "awaiting-client", "recovered": "uncertain", "resolution": "notApplied",
                         "repeatResolution": "retained", "conflictingResolution": "weaver.invocation.resolution_conflict",
                         "resolvedAudits": 1, "execute": "uncertain", "executions": 0, "settledBy": ["recovery"]},
    "second-client": {"restart": GRACEFUL, "recovered": "awaitingApproval", "clients": 2, "foreign": "refused", "repeatDecision": "approved",
                      "concurrentExecute": "retained", "continuation": "completed", "executions": 1, "settledBy": []},
}
check(set(EXPECTED_OUTCOMES) == set(RESTART_CASES), "Every restart case needs one driver-owned expected outcome")
check(sorted(key for _, keys in RESTART_GROUPS for key in keys) == sorted(RESTART_CASES)
      and all(RESTART_CASES[key] == phase for phase, keys in RESTART_GROUPS for key in keys),
      "Every restart case runs in exactly one restart group of its own phase")
check(all(len(keys) == 1 for _, keys in RESTART_GROUPS if any(EXPECTED_OUTCOMES[key]["executions"] for key in keys)),
      "A case with an effect at the shared native owner needs a restart of its own, so no other effect falls between its owner reads")
PROPOSAL_KEYS = ("sessionId", "commandId", "proposalId", "invocationId", "toolCallId")
CORRELATION_KEYS = {key: PROPOSAL_KEYS for key in RESTART_CASES}
CORRELATION_KEYS.update({"idle": ("sessionId", "commandId", "continuationCommandId"),
                         "provider-turn-in-progress": ("sessionId", "commandId", "gateEpoch", "continuationCommandId")})
# The native negative control: the same restart on the in-memory default store must lose the pending review.
CONTROL_ID = "restart-without-durable-store"
EXPECTED_CONTROL = {"stage": "proposal-lookup", "diagnostic": "weaver.proposal.not_found"}
WORKFLOW_TOOL = "workflow_set_designer_position"
WORKFLOW_CAPABILITY = "workflow.propose-change"
WORKFLOW_MODULE = "Elsa.Studio.Workflows"
DEADLINE_MARGIN_SECONDS = 3.0
# One case's malformed or failed evidence keeps its own cause; the remaining cases still run once.
CASE_ERRORS = (RuntimeError, KeyError, TypeError, ValueError, AttributeError, OSError)
OWNED_ELSEWHERE_LIMIT_SECONDS = 150
HOLD_OBSERVATION_SECONDS = 25
STATUS_NAMES = {"awaitingapproval": "awaitingApproval", "approved": "approved", "expired": "expired", "uncertain": "uncertain",
                "executed": "executed", "awaitingclient": "awaitingClient", "executing": "executing", "failed": "failed",
                "cancelled": "cancelled", "denied": "denied", "completed": "completed"}


class RestartCaseFailure(RuntimeError):
    """A restart case that ended with its own precise, retained cause."""
    def __init__(self, message, cause, observation=None):
        super().__init__(message)
        self.cause, self.observation = cause, observation


def need(condition, cause, message):
    if not condition:
        raise RestartCaseFailure(cause + ": " + message, cause)


def status_name(value):
    key = str(value or "").replace("-", "").replace("_", "").lower()
    return STATUS_NAMES.get(key, key)


_INSTANT = re.compile(r"(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,9}))?(Z|[+-]\d{2}:\d{2})")


def instant(value):
    """Epoch seconds of a native ISO-8601 instant (.NET writes up to seven fractional digits)."""
    match = _INSTANT.fullmatch(value) if isinstance(value, str) else None
    check(match is not None, "Not a native ISO-8601 instant: " + repr(value))
    year, month, day, hour, minute, second, fraction, zone = match.groups()
    seconds = float(calendar.timegm((int(year), int(month), int(day), int(hour), int(minute), int(second), 0, 0, 0)))
    if fraction:
        seconds += int(fraction) / 10 ** len(fraction)
    if zone != "Z":
        sign = 1 if zone[0] == "+" else -1
        seconds -= sign * (int(zone[1:3]) * 3600 + int(zone[4:6]) * 60)
    return seconds


# --- structural manifest and gates -------------------------------------------------------------

def restart_manifest_gaps(section, source=None):
    """Every reason a restart section cannot complete a witness; reads no files (require_restart_inventory does)."""
    if not isinstance(section, dict):
        return ["The mandatory restart inventory section is absent"]
    gaps = []
    if section.get("status") != "passed":
        gaps.append("Restart section status is " + repr(section.get("status")))
    if section.get("evidenceCategory") != EVIDENCE_CATEGORY or section.get("liveProvider") is not False:
        gaps.append("Unsupported restart provider evidence claim")
    if section.get("qualified") is not False or section.get("independentPackageWitness") is not False:
        gaps.append("A restart section cannot promote itself")
    if source is not None and section.get("source") != source:
        gaps.append("Restart section source differs from the witness source")
    if section.get("expectedCaseIds") != sorted(EXPECTED_CASES) or section.get("controlId") != CONTROL_ID:
        gaps.append("Restart section did not declare the current mandatory inventory")
    store = section.get("durableStore") if isinstance(section.get("durableStore"), dict) else {}
    if (store.get("feature") != DURABLE_STORE_FEATURE or store.get("dependsOn") != DURABLE_STORE_DEPENDENCY or store.get("provider") != "Sqlite"
            or store.get("databaseObserved") is not True or not isinstance(store.get("restart"), dict)):
        gaps.append("The packaged durable store was not configured and observed on SQLite")
    control = section.get("control")
    if not isinstance(control, dict) or control.get("id") != CONTROL_ID or control.get("status") != "failed" \
            or control.get("expectedFailureObserved") is not True or not same(control.get("actual"), EXPECTED_CONTROL) \
            or not all(isinstance(control.get(name), dict) for name in ("ownerBefore", "ownerAfter")) \
            or (control.get("ownerBefore") or {}).get("sha256") != (control.get("ownerAfter") or {}).get("sha256") \
            or not (isinstance(control.get("evidence"), list) and control["evidence"]):
        gaps.append("The in-memory restart control was not observed losing the pending review with its exact cause")
    records = section.get("cases")
    if not isinstance(records, list) or not all(isinstance(row, dict) for row in records):
        return gaps + ["Restart section has no case records"]
    identities = [row.get("id") for row in records]
    gaps += ["Missing restart case: " + name for name in sorted(EXPECTED_CASES - set(identities))]
    gaps += ["Duplicated restart case: " + str(name) for name in sorted({str(name) for name in identities if identities.count(name) > 1})]
    gaps += ["Unexpected restart case: " + str(name) for name in sorted({str(name) for name in identities} - EXPECTED_CASES)]
    for row in records:
        name, key, provider = row.get("id"), row.get("caseKey"), row.get("provider")
        if key not in RESTART_CASES or provider not in PROVIDERS or name != f"{provider}-{key}" or row.get("adapter") != provider:
            gaps.append(f"{name}: wrong adapter or case identity"); continue
        if row.get("status") != "passed" or row.get("cause") is not None:
            gaps.append(f"{name}: {row.get('status')} ({row.get('cause')})"); continue
        if row.get("nativeEvidenceVerified") is not True or row.get("qualified") is not False:
            gaps.append(f"{name}: unverified or self-promoted"); continue
        if source is not None and row.get("source") != source:
            gaps.append(f"{name}: case source differs")
        if row.get("restart") != RESTART_CASES[key] or not same(row.get("outcome"), EXPECTED_OUTCOMES[key]):
            gaps.append(f"{name}: wrong-cause restart outcome")
        if not (isinstance(row.get("evidence"), list) and row["evidence"]) or not all(isinstance(row.get(owner), dict) for owner in ("ownerBefore", "ownerAfter")):
            gaps.append(f"{name}: missing owner/native evidence")
        correlation = row.get("correlation") if isinstance(row.get("correlation"), dict) else {}
        if (correlation.get("adapter") != provider or not isinstance(correlation.get("providerTrace"), dict)
                or not isinstance(correlation.get("restartReceipt"), dict)
                or not (type(correlation.get("fromGeneration")) is int and type(correlation.get("toGeneration")) is int
                        and correlation["toGeneration"] > correlation["fromGeneration"])
                or not all(isinstance(correlation.get(item), str) and correlation[item] for item in CORRELATION_KEYS[key])):
            gaps.append(f"{name}: missing native or restart correlation")
    return gaps


def require_restart_record(root, row):
    """Manifest gate for one restart case: exact adapter, expected outcome and retained owner/correlation evidence."""
    key, provider = row.get("caseKey"), row.get("provider")
    check(key in RESTART_CASES and provider in PROVIDERS and row.get("adapter") == provider and row.get("id") == f"{provider}-{key}",
          "A restart case must name its exact actual adapter")
    check(row.get("status") == "passed" and row.get("cause") is None and row.get("nativeEvidenceVerified") is True
          and row.get("qualified") is False, "Failed, unverified or promoted restart case: " + row["id"])
    check(row.get("restart") == RESTART_CASES[key] and same(row.get("outcome"), EXPECTED_OUTCOMES[key]),
          "Wrong-cause restart outcome cannot qualify: " + row["id"])
    correlation = row.get("correlation")
    check(isinstance(correlation, dict) and correlation.get("adapter") == provider
          and all(isinstance(correlation.get(name), str) and correlation[name] for name in CORRELATION_KEYS[key])
          and type(correlation.get("fromGeneration")) is int and type(correlation.get("toGeneration")) is int
          and correlation["toGeneration"] > correlation["fromGeneration"], "Missing native or restart correlation: " + row["id"])
    check(isinstance(row.get("evidence"), list) and row["evidence"], "Owner markers alone cannot qualify a restart case")
    for reference in (row.get("ownerBefore"), row.get("ownerAfter"), correlation.get("providerTrace"), correlation.get("restartReceipt"), *row["evidence"]):
        _path(root, reference)
    return row


def require_restart_inventory(section, owned_root=None, *, source=None):
    """Mandatory final gate: every restart case passed once on each actual adapter, plus the observed negative control."""
    gaps = restart_manifest_gaps(section, source)
    check(not gaps, "The restart inventory is mandatory on each actual adapter: " + "; ".join(gaps[:20]))
    if owned_root is not None:
        for row in section["cases"]:
            require_restart_record(owned_root, row)
        control = section["control"]
        for reference in (control["ownerBefore"], control["ownerAfter"], *control["evidence"]):
            _path(owned_root, reference)
        _path(owned_root, section["durableStore"]["restart"])
    return section["cases"]


# --- retained evidence readers -----------------------------------------------------------------

def _audit_path(session):
    return "/_elsa/weaver/audit?" + urlencode({"take": 100, "sessionId": session})


def _restart(root, o, expected):
    """The retained restart record between the prepared and recovered observations of this case."""
    record = _json_file(root, o["refs"]["restart"])
    need(isinstance(record, dict) and record.get("phase") == expected and record.get("kind") == ("abrupt" if expected == ABRUPT else "graceful"),
         "restart.kind", "The case was not recovered across its declared " + expected + " restart")
    stopped = record.get("stoppedProcess") or {}
    need(type(record.get("fromGeneration")) is int and type(record.get("toGeneration")) is int
         and record["toGeneration"] > record["fromGeneration"] == o.get("fromGeneration") and record["toGeneration"] == o.get("toGeneration"),
         "restart.generation", "The case's observations do not straddle one recorded host restart")
    need(stopped.get("exitCode") == (-9 if expected == ABRUPT else 0)
         and isinstance(stopped.get("runtimeCacheFiles"), dict) and not any(stopped["runtimeCacheFiles"].values())
         and isinstance(record.get("stoppedAt"), (int, float)) and isinstance(record.get("startedAt"), (int, float))
         and record["stoppedAt"] <= record["startedAt"], "restart.process", "The stopped generation did not end as declared with empty runtime caches")
    if expected != ABRUPT:
        need(o.get("ownedElsewhereAttempts", 0) == 0 and not any(name.startswith("ownedElsewhere") for name in o["refs"]),
             "restart.lease_not_released", "A graceful stop left its session lease held: the recovered request answered weaver.session.owned_elsewhere")
    return record


def _deadline(proposal):
    return instant(((proposal or {}).get("preparedInvocation") or {}).get("expiresAt"))


def _before_deadline(o, proposal):
    need(isinstance(o.get("recoveredAt"), (int, float)) and o["recoveredAt"] < _deadline(proposal), "restart.deadline_elapsed",
         "The recovery was observed after the prepared deadline; a before-deadline case cannot prove decidability")


def _after_deadline(record, proposal):
    expires = _deadline(proposal)
    need(record["stoppedAt"] < expires <= record["startedAt"], "restart.deadline_window",
         "The host was not stopped before and started after the prepared deadline")


def _listed(root, reference, session, proposal):
    value, _ = _exchange(root, reference, "GET", _session_path(session) + "/proposals", 200)
    rows = value.get("data")
    need(isinstance(rows, list), "restart.proposal_lost", "The recovered session listed no proposals")
    matches = [row for row in rows if isinstance(row, dict) and row.get("id") == proposal["id"]]
    need(len(matches) == 1, "restart.proposal_lost", "The recovered session lost the exact prepared operation")
    recovered, original = matches[0], proposal["preparedInvocation"]
    prepared = recovered.get("preparedInvocation") or {}
    need(recovered.get("sessionId") == session and all(prepared.get(name) == original.get(name)
         for name in ("invocationId", "toolCallId", "approvalDigest", "toolName", "expiresAt")),
         "restart.proposal_changed", "The recovered proposal is not the original prepared operation")
    return recovered, rows


def _settled_by(root, reference, session, proposal_id=None, status=None):
    value, _ = _exchange(root, reference, "GET", _audit_path(session), 200)
    rows = value.get("data")
    need(isinstance(rows, list) and all(isinstance(row, dict) and row.get("sessionId") == session for row in rows),
         "restart.audit", "The owner audit read names another session")
    selected = [row for row in rows if proposal_id is None or (row.get("metadata") or {}).get("proposalId") == proposal_id]
    settled = [row for row in selected if "settledBy" in (row.get("metadata") or {})]
    for row in settled:
        need(row.get("actorId") is None and str(row.get("kind", "")).lower() == "proposalfailed"
             and (status is None or row["metadata"].get("status") == status), "restart.audit",
             "A recovery settlement is attributed to a user or names another outcome")
    return sorted(row["metadata"]["settledBy"] for row in settled), selected


def _executions(before, after, receipt=None):
    """Effects at the independent native owner: none, or exactly the one recorded receipt; anything else is a replay."""
    if after == before:
        return 0
    need(isinstance(receipt, dict) and (receipt.get("before") or {}).get("revision") == before["revision"]
         and (receipt.get("after") or {}).get("revision") == after["revision"], "restart.effect_replayed",
         "The native owner changed beyond the one recorded receipt")
    return 1


def _initial_proposal(root, o, witness):
    _, _, before = _verify_open(root, o, witness)
    turn, proposal = _verify_pause(root, o, witness, before)
    return before, turn, proposal


def _correlation(provider, o, restart, proposal=None, **extra):
    value = {"adapter": provider, "sessionId": o["sessionId"], "commandId": o["commandId"], "providerTrace": o["refs"]["trace"],
             "restartReceipt": o["refs"]["restart"], "fromGeneration": restart["fromGeneration"], "toGeneration": restart["toGeneration"]}
    if proposal is not None:
        prepared = proposal["preparedInvocation"]
        value.update(proposalId=proposal["id"], invocationId=prepared["invocationId"], toolCallId=prepared["toolCallId"])
    value.update(extra)
    return value


def _record(root, provider, key, outcome, correlation, o):
    refs = o["refs"]
    record = {"id": provider + "-" + key, "provider": provider, "adapter": provider, "caseKey": key, "restart": RESTART_CASES[key],
              "status": "passed", "cause": None, "qualified": False, "nativeEvidenceVerified": True, "outcome": outcome,
              "correlation": correlation, "ownerBefore": refs["ownerBefore"], "ownerAfter": refs["ownerAfter"],
              "ownedElsewhereAttempts": o.get("ownedElsewhereAttempts", 0), "evidence": [value for value in refs.values() if isinstance(value, dict)]}
    require_restart_record(root, record)
    return record


def _read_continuation(root, o, witness, provider):
    refs, session = o["refs"], o["sessionId"]
    value, _ = _exchange(root, refs["continuationMessage"], "POST", _session_path(session) + "/messages", 200)
    message = (value.get("data") or {}).get("message") or {}
    need(message.get("sessionId") == session and str(message.get("role", "")).lower() == "user", "restart.continuation",
         "The recovered session did not accept a new prompt")
    need(_json_file(root, refs["continuationCommand"]) == {"commandId": o["continuationCommandId"], "toolName": witness["readTool"], "arguments": {}},
         "restart.continuation", "The continuation command changed")
    values = _sse(root, refs["continuation"], session)
    need(values[0]["kind"] == "started" and values[-1]["kind"] == "completed"
         and not any(value["kind"] in ("proposalCreated", "toolApprovalRequested", "turnCancelled", "error") for value in values),
         "restart.continuation", "The recovered session could not continue to a completed turn")
    calls = [row for row in _trace(root, refs["continuationTrace"]) if row.get("kind") == provider + ".call"]
    need(len(calls) == 1 and (calls[0].get("data") or {}).get("toolName") == witness["readTool"], "restart.continuation",
         "The continuation did not reach the actual adapter exactly once")
    return "completed"


# --- verifiers ---------------------------------------------------------------------------------

def verify_idle(root, o, witness, provider):
    refs, session = o["refs"], o["sessionId"]
    restart = _restart(root, o, GRACEFUL)
    _, _, before = _verify_open(root, o, witness)
    values = _sse(root, refs["initialStream"], session)
    results = [value.get("payload") for value in values if value["kind"] == "contributedResult"
               and (value.get("payload") or {}).get("toolName") == witness["sizeTool"]]
    need(values[-1]["kind"] == "completed" and len(results) == 1 and results[0].get("succeeded") is True
         and not any(value["kind"] == "proposalCreated" for value in values), "restart.idle_setup",
         "The idle session needs one completed automatic size effect before the restart")
    receipt = results[0].get("receipt") or {}
    applied = _size_owner(root, refs["ownerApplied"], witness)
    need(applied["size"] == o["targetSize"] and _executions(before, applied, receipt) == 1, "restart.idle_setup",
         "The owner does not confirm the automatic receipt before the restart")
    prior = _session(root, refs["sessionBeforeRestart"], session)
    recovered = _session(root, refs["recoveredSession"], session)
    need(same(recovered.get("policy"), prior.get("policy")), "restart.policy_lost", "The acknowledged policy and revision changed across the restart")
    need(same(recovered.get("messages"), prior.get("messages")), "restart.history_lost", "The recorded history changed across the restart")
    _stored_once(recovered["messages"], results[0])
    continuation = _read_continuation(root, o, witness, provider)
    settled, _ = _settled_by(root, refs["audit"], session)
    after = _size_owner(root, refs["ownerAfter"], witness)
    outcome = {"restart": GRACEFUL, "session": "same", "history": "retained", "policy": "same", "continuation": continuation,
               "executions": _executions(before, after, receipt), "settledBy": settled}
    correlation = _correlation(provider, o, restart, continuationCommandId=o["continuationCommandId"])
    return _record(root, provider, "idle", outcome, correlation, o)


def _reviewed_execution(root, o, witness, provider, before, turn, proposal):
    """The explicit post-restart execution: one receipt, one continuation of the originating turn, stored once."""
    refs, session = o["refs"], o["sessionId"]
    executed = _executed(root, refs["execute"], proposal, before, witness)
    _verify_continuation(root, refs["continuation"], session, turn)
    result = _provider(root, o, witness, proposal, provider)
    need(_normalized(result) == _normalized(executed["toolResult"]), "restart.continuation_result",
         "The originating turn received another result than the original receipt")
    _stored_once(_session(root, refs["session"], session)["messages"], executed["toolResult"])
    after = _size_owner(root, refs["ownerAfter"], witness)
    need(after["size"] == o["targetSize"], "restart.effect_missing", "The owner does not hold the reviewed size")
    return executed, after


def _normalized(value):
    from case_records import _normalized_result
    return json.dumps(_normalized_result(value), sort_keys=True)


def verify_pending_before(root, o, witness, provider):
    refs, session = o["refs"], o["sessionId"]
    restart = _restart(root, o, GRACEFUL)
    before, turn, proposal = _initial_proposal(root, o, witness)
    recovered, _ = _listed(root, refs["recoveredProposals"], session, proposal)
    _before_deadline(o, proposal)
    approved, _ = _exchange(root, refs["approve"], "POST", _proposal_path(proposal["id"], "approve"), 200)
    executed, after = _reviewed_execution(root, o, witness, provider, before, turn, proposal)
    settled, _ = _settled_by(root, refs["audit"], session, proposal["id"])
    outcome = {"restart": GRACEFUL, "recovered": status_name(recovered.get("status")), "decision": status_name((approved.get("data") or {}).get("status")),
               "execute": executed["status"], "continuation": "completed",
               "executions": _executions(before, after, executed["toolResult"]["receipt"]), "settledBy": settled}
    return _record(root, provider, "pending-review-before-deadline", outcome, _correlation(provider, o, restart, proposal), o)


def verify_approved_before(root, o, witness, provider):
    refs, session = o["refs"], o["sessionId"]
    restart = _restart(root, o, GRACEFUL)
    before, turn, proposal = _initial_proposal(root, o, witness)
    _exchange(root, refs["approve"], "POST", _proposal_path(proposal["id"], "approve"), 200)
    recovered, _ = _listed(root, refs["recoveredProposals"], session, proposal)
    _before_deadline(o, proposal)
    at_recovery = _size_owner(root, refs["ownerAtRecovery"], witness)
    executed, after = _reviewed_execution(root, o, witness, provider, before, turn, proposal)
    settled, _ = _settled_by(root, refs["audit"], session, proposal["id"])
    outcome = {"restart": GRACEFUL, "recovered": status_name(recovered.get("status")), "recoveryExecuted": at_recovery != before,
               "execute": executed["status"], "continuation": "completed",
               "executions": _executions(before, after, executed["toolResult"]["receipt"]), "settledBy": settled}
    return _record(root, provider, "approved-before-dispatch-before-deadline", outcome, _correlation(provider, o, restart, proposal), o)


def _verify_expired(root, o, witness, provider, key, approved):
    refs, session = o["refs"], o["sessionId"]
    restart = _restart(root, o, AFTER_DEADLINE)
    before, _, proposal = _initial_proposal(root, o, witness)
    if approved:
        _exchange(root, refs["approve"], "POST", _proposal_path(proposal["id"], "approve"), 200)
    _after_deadline(restart, proposal)
    recovered, _ = _listed(root, refs["recoveredProposals"], session, proposal)
    value, _ = _exchange(root, refs["execute"], "POST", _proposal_path(proposal["id"], "execute"), 409)
    data = value.get("data") or {}
    need(data.get("executed") is not True, "restart.effect_observed", "An expired operation reported execution")
    settled, _ = _settled_by(root, refs["audit"], session, proposal["id"], status="Expired")
    after = _size_owner(root, refs["ownerAfter"], witness)
    outcome = {"restart": AFTER_DEADLINE, "recovered": status_name(recovered.get("status")), "execute": (value.get("error") or {}).get("code"),
               "executions": _executions(before, after), "settledBy": settled}
    return _record(root, provider, key, outcome, _correlation(provider, o, restart, proposal), o)


def verify_pending_after(root, o, witness, provider):
    return _verify_expired(root, o, witness, provider, "pending-review-after-deadline", False)


def verify_approved_after(root, o, witness, provider):
    return _verify_expired(root, o, witness, provider, "approved-before-dispatch-after-deadline", True)


def verify_receipt_paused(root, o, witness, provider):
    refs, session = o["refs"], o["sessionId"]
    restart = _restart(root, o, GRACEFUL)
    before, turn, proposal = _initial_proposal(root, o, witness)
    _exchange(root, refs["approve"], "POST", _proposal_path(proposal["id"], "approve"), 200)
    executed = _executed(root, refs["execute"], proposal, before, witness)
    applied = _size_owner(root, refs["ownerAfterExecute"], witness)
    receipt = executed["toolResult"]["receipt"]
    need(applied["size"] == o["targetSize"] and _executions(before, applied, receipt) == 1, "restart.receipt_setup",
         "The owner does not confirm the receipt recorded before the restart")
    recovered, _ = _listed(root, refs["recoveredProposals"], session, proposal)
    need(_size_owner(root, refs["ownerAtRecovery"], witness) == applied, "restart.effect_replayed", "Recovery changed the owner again")
    _verify_continuation(root, refs["continuation"], session, turn)
    result = _provider(root, o, witness, proposal, provider)
    need(_normalized(result) == _normalized(executed["toolResult"]), "restart.continuation_result",
         "The paused turn reported another result than the recorded receipt")
    continuations = sum(row.get("kind") == provider + ".continuation" for row in _trace(root, refs["trace"]))
    repeat = _sse(root, refs["repeatDelivery"], session)
    error = repeat[-1].get("error") or {}
    need(repeat[-1]["kind"] == "error" and not any(value["kind"] in ("started", "contributedResult", "proposalCreated") for value in repeat),
         "restart.repeat_delivery", "A repeated delivery was admitted as another continuation")
    _stored_once(_session(root, refs["session"], session)["messages"], executed["toolResult"])
    settled, _ = _settled_by(root, refs["audit"], session, proposal["id"])
    after = _size_owner(root, refs["ownerAfter"], witness)
    outcome = {"restart": GRACEFUL, "recovered": status_name(recovered.get("status")), "continuation": "completed", "continuations": continuations,
               "repeatDelivery": error.get("code"), "executions": _executions(before, after, receipt), "settledBy": settled}
    return _record(root, provider, "receipt-recorded-paused", outcome, _correlation(provider, o, restart, proposal), o)


def verify_second_client(root, o, witness, provider):
    refs, session = o["refs"], o["sessionId"]
    restart = _restart(root, o, GRACEFUL)
    before, turn, proposal = _initial_proposal(root, o, witness)
    _before_deadline(o, proposal)
    for name in ("foreignSession", "foreignProposals", "foreignExecute"):
        record = strict_json(_path(root, refs[name]).read_text())
        body = strict_json(record.get("rawBody") or "null") if record.get("rawBody") else None
        need(record.get("status") in (403, 404) and isinstance(body, dict) and body.get("data") is None
             and str((body.get("error") or {}).get("code", "")).startswith("weaver."), "restart.foreign_access",
             "A foreign actor reached the recovered session, its proposals or its execution")
    need(_size_owner(root, refs["ownerAfterForeign"], witness) == before, "restart.foreign_access", "A foreign request changed the owner")
    seen = [_listed(root, refs[name], session, proposal)[0] for name in ("proposalsA", "proposalsB")]
    need(all(status_name(row.get("status")) == "awaitingApproval" for row in seen) and same(seen[0], seen[1]), "restart.second_client",
         "The two clients of the owner did not observe the same recovered review")
    decisions = [_exchange(root, refs[name], "POST", _proposal_path(proposal["id"], "approve"), 200)[0] for name in ("approveA", "approveB")]
    need(all(status_name((value.get("data") or {}).get("status")) == "approved" for value in decisions)
         and same((decisions[0].get("data") or {}).get("approvedAt"), (decisions[1].get("data") or {}).get("approvedAt")),
         "restart.second_client", "The repeated decision of the second client was not the recorded one")
    executions = [_executed(root, refs[name], proposal, before, witness) for name in ("executeA", "executeB")]
    need(same(executions[0], executions[1]), "restart.duplicate_effect", "The concurrent executions returned different outcomes")
    _verify_continuation(root, refs["continuation"], session, turn)
    result = _provider(root, o, witness, proposal, provider)
    need(_normalized(result) == _normalized(executions[0]["toolResult"]), "restart.continuation_result",
         "The originating turn received another result than the one receipt")
    _stored_once(_session(root, refs["session"], session)["messages"], executions[0]["toolResult"])
    settled, _ = _settled_by(root, refs["audit"], session, proposal["id"])
    after = _size_owner(root, refs["ownerAfter"], witness)
    need(after["size"] == o["targetSize"], "restart.effect_missing", "The owner does not hold the reviewed size")
    outcome = {"restart": GRACEFUL, "recovered": status_name(seen[0].get("status")), "clients": len(seen), "foreign": "refused",
               "repeatDecision": "approved", "concurrentExecute": "retained", "continuation": "completed",
               "executions": _executions(before, after, executions[0]["toolResult"]["receipt"]), "settledBy": settled}
    return _record(root, provider, "second-client", outcome, _correlation(provider, o, restart, proposal), o)


def verify_dispatched(root, o, witness, provider):
    refs, session = o["refs"], o["sessionId"]
    restart = _restart(root, o, ABRUPT)
    before, _, proposal = _initial_proposal(root, o, witness)
    _exchange(root, refs["approve"], "POST", _proposal_path(proposal["id"], "approve"), 200)
    control, held = _json_file(root, refs["rendezvousControl"]), _json_file(root, refs["rendezvousHeld"])
    need(held.get("status") == "waiting" and held.get("epoch") == control.get("epoch") and held.get("revision") == before["revision"]
         and isinstance(held.get("arrivals"), list) and len(held["arrivals"]) == 1, "restart.dispatch_not_held",
         "The dispatched write was not held at the owner's native write boundary when the host stopped")
    interrupted = strict_json(_path(root, refs["interruptedExecute"]).read_text())
    need(interrupted.get("path") == _proposal_path(proposal["id"], "execute") and interrupted.get("transportErrorType")
         and "status" not in interrupted, "restart.dispatch_not_held", "The dispatched execution received a response instead of the stop")
    recovered, _ = _listed(root, refs["recoveredProposals"], session, proposal)
    value, _ = _exchange(root, refs["repeatExecute"], "POST", _proposal_path(proposal["id"], "execute"), 200)
    data = value.get("data") or {}
    need(data.get("executed") is False and status_name(data.get("status")) == "uncertain", "restart.replayed",
         "A repeated execution of the uncertain operation was not refused as retained uncertainty")
    settled, _ = _settled_by(root, refs["audit"], session, proposal["id"], status="Uncertain")
    after = _size_owner(root, refs["ownerAfter"], witness)
    outcome = {"restart": ABRUPT, "dispatch": "held-at-owner", "recovered": status_name(recovered.get("status")),
               "execute": status_name(data.get("status")), "executions": _executions(before, after), "settledBy": settled}
    return _record(root, provider, "dispatched-without-receipt", outcome, _correlation(provider, o, restart, proposal), o)


def verify_in_progress(root, o, witness, provider):
    refs, session = o["refs"], o["sessionId"]
    restart = _restart(root, o, ABRUPT)
    _, message, before = _verify_open(root, o, witness)
    gate = _json_file(root, refs["gate"])
    rows = _trace(root, refs["trace"])
    need([row.get("kind") for row in rows] == [provider + ".request", "protocol.gate.waiting"]
         and (rows[1].get("data") or {}).get("epoch") == gate.get("epoch") == o["gateEpoch"], "restart.turn_not_running",
         "The provider turn was not held in progress at the pre-call gate when the host stopped")
    stream = strict_json(_path(root, refs["interruptedStream"]).read_text())
    frames = [frame.get("value") or {} for frame in stream.get("frames", [])]
    need(stream.get("status") == 200 and frames and frames[0].get("kind") == "started"
         and not any(frame.get("kind") in ("completed", "turnCancelled", "proposalCreated", "contributedResult") for frame in frames),
         "restart.turn_not_running", "The turn ended before the host stopped")
    recovered = _session(root, refs["recoveredSession"], session)
    prompts = [row for row in recovered["messages"] if row.get("id") == o["messageId"]]
    need(len(prompts) == 1, "restart.history_lost", "The interrupted prompt is missing after the restart")
    value, _ = _exchange(root, refs["recoveredProposals"], "GET", _session_path(session) + "/proposals", 200)
    proposals = value.get("data")
    need(isinstance(proposals, list), "restart.proposal_lost", "The recovered session listed no proposals")
    continuation = _read_continuation(root, o, witness, provider)
    settled, _ = _settled_by(root, refs["audit"], session)
    after = _size_owner(root, refs["ownerAfter"], witness)
    outcome = {"restart": ABRUPT, "recovered": status_name(prompts[0].get("status")), "statusReason": prompts[0].get("statusReason"),
               "proposals": len(proposals), "continuation": continuation, "executions": _executions(before, after), "settledBy": settled}
    correlation = _correlation(provider, o, restart, gateEpoch=o["gateEpoch"], continuationCommandId=o["continuationCommandId"])
    return _record(root, provider, "provider-turn-in-progress", outcome, correlation, o)


def verify_owner_resolution(root, o, witness, provider):
    refs, session = o["refs"], o["sessionId"]
    restart = _restart(root, o, AFTER_DEADLINE)
    created, _ = _exchange(root, refs["create"], "POST", "/_elsa/weaver/sessions", 200)
    need((created.get("data") or {}).get("sessionId") == session, "restart.client_setup", "The workflow conversation identity changed")
    need(_json_file(root, refs["command"]) == {"commandId": o["commandId"], "toolName": WORKFLOW_TOOL, "arguments": o["arguments"]},
         "restart.client_setup", "The client operation command changed")
    _exchange(root, refs["message"], "POST", _session_path(session) + "/messages", 200)
    values = _sse(root, refs["initialStream"], session)
    proposals = [value.get("payload") for value in values if value["kind"] == "proposalCreated"]
    need(len(proposals) == 1 and isinstance(proposals[0], dict), "restart.client_setup", "The client operation did not pause at one review")
    proposal = proposals[0]
    prepared = proposal.get("preparedInvocation") or {}
    need(prepared.get("toolName") == WORKFLOW_TOOL and status_name(prepared.get("executionLocation")) == "clientworkingstate"
         and same(prepared.get("client"), o["binding"]) and same(prepared.get("arguments"), o["arguments"]),
         "restart.client_setup", "The prepared operation is not the reviewed client working-state change of this binding")
    _exchange(root, refs["approve"], "POST", _proposal_path(proposal["id"], "approve"), 200)
    dispatched, _ = _exchange(root, refs["execute"], "POST", _proposal_path(proposal["id"], "execute"), 202)
    client = ((dispatched.get("data") or {}).get("clientInvocation") or {})
    need(client.get("invocationId") == prepared.get("invocationId") and (dispatched.get("data") or {}).get("executed") is False,
         "restart.client_setup", "The client operation was not dispatched to its client")
    calls = [row for row in _trace(root, refs["trace"]) if row.get("kind") == provider + ".call"]
    need(len(calls) == 1 and (calls[0].get("data") or {}).get("toolName") == WORKFLOW_TOOL
         and same((calls[0].get("data") or {}).get("arguments"), o["arguments"]), "restart.client_setup",
         "The actual adapter did not call the client operation exactly once")
    _after_deadline(restart, proposal)
    recovered, _ = _listed(root, refs["recoveredProposals"], session, proposal)
    path = _session_path(session) + "/client-invocations/" + prepared["invocationId"] + "/resolution"
    resolved, record = _exchange(root, refs["resolution"], "POST", path, 200)
    statement = ((resolved.get("data") or {}).get("ownerResolution") or {})
    need(same(record.get("requestBody"), {"claim": "notApplied"}) and statement.get("claim") == "notApplied", "restart.resolution",
         "The owner's statement was not recorded")
    repeated, _ = _exchange(root, refs["repeatResolution"], "POST", path, 200)
    need(same(((repeated.get("data") or {}).get("ownerResolution")), statement), "restart.resolution", "A repeated statement was not idempotent")
    conflicting, _ = _exchange(root, refs["conflictingResolution"], "POST", path, 409)
    retry, _ = _exchange(root, refs["repeatExecute"], "POST", _proposal_path(proposal["id"], "execute"), 200)
    data = retry.get("data") or {}
    need(data.get("executed") is False and data.get("clientInvocation") is None, "restart.replayed",
         "The uncertain client operation was dispatched again")
    settled, rows = _settled_by(root, refs["audit"], session, proposal["id"], status="Uncertain")
    resolutions = [row for row in rows if str(row.get("kind", "")).lower() == "proposalresolved"]
    need(all((row.get("metadata") or {}).get("claim") == "notApplied" for row in resolutions), "restart.resolution",
         "The resolution audit names another statement")
    before, after = _json_file(root, refs["ownerBefore"]), _json_file(root, refs["ownerAfter"])
    need(isinstance(before, dict) and isinstance(after, dict), "restart.owner", "The native workflow owner was not read")
    outcome = {"restart": AFTER_DEADLINE, "dispatch": "awaiting-client", "recovered": status_name(recovered.get("status")),
               "resolution": statement.get("claim"), "repeatResolution": "retained",
               "conflictingResolution": (conflicting.get("error") or {}).get("code"), "resolvedAudits": len(resolutions),
               "execute": status_name(data.get("status")), "executions": 0 if same(before.get("draft"), after.get("draft")) else 1,
               "settledBy": settled}
    return _record(root, provider, "owner-resolution", outcome, _correlation(provider, o, restart, proposal), o)


def verify_control(root, o, witness):
    """The native negative control: on the in-memory default the pending review is lost by the same restart."""
    refs, session = o["refs"], o["sessionId"]
    restart = _restart(root, o, GRACEFUL)
    before, _, proposal = _initial_proposal(root, o, witness)
    record = strict_json(_path(root, refs["recoveredSession"]).read_text())
    need(record.get("status") in (403, 404), "control.unexpected_recovery", "The in-memory default unexpectedly recovered the session")
    value, _ = _exchange(root, refs["recoveredExecute"], "POST", _proposal_path(proposal["id"], "execute"), 404)
    code = (value.get("error") or {}).get("code")
    after = _size_owner(root, refs["ownerAfter"], witness)
    need(after == before, "control.effect_observed", "The lost review changed the owner")
    return {"id": CONTROL_ID, "provider": o["provider"], "status": "failed", "expectedFailureObserved": True,
            "actual": {"stage": "proposal-lookup", "diagnostic": code}, "restart": GRACEFUL,
            "fromGeneration": restart["fromGeneration"], "toGeneration": restart["toGeneration"],
            "ownerBefore": refs["ownerBefore"], "ownerAfter": refs["ownerAfter"],
            "evidence": [value for value in refs.values() if isinstance(value, dict)]}


VERIFIERS = {"idle": verify_idle, "pending-review-before-deadline": verify_pending_before, "pending-review-after-deadline": verify_pending_after,
             "approved-before-dispatch-before-deadline": verify_approved_before, "approved-before-dispatch-after-deadline": verify_approved_after,
             "dispatched-without-receipt": verify_dispatched, "receipt-recorded-paused": verify_receipt_paused,
             "provider-turn-in-progress": verify_in_progress, "owner-resolution": verify_owner_resolution, "second-client": verify_second_client}
check(set(VERIFIERS) == set(RESTART_CASES), "Every restart case needs exactly one verifier")


# --- executors: real native operations, retained before any assertion ---------------------------

def _owner_ref(runtime, name, *, http=None):
    _, reference, _ = runtime.snapshot(name, http=http, endpoint=runtime.witness["sizeOwnerEndpoint"])
    return reference


def _command_trace(t, o, command_id, name):
    """This command's rows of the actual factory trace; one phase interleaves several cases in one adapter trace."""
    raw = t.protocol_trace(o["traceOffset"])
    rows = [row for row in raw["records"] if isinstance(row.get("data"), dict) and row["data"].get("commandId") == command_id]
    path = t.output / (name + "-command-trace.json")
    check(not path.exists(), "Never overwrite a retained command trace")
    private_json(path, {"commandId": command_id, "sourcePath": raw["sourcePath"], "offset": raw["offset"], "nextOffset": raw["nextOffset"],
                        "incompleteTailBytes": raw["incompleteTailBytes"], "rawTrace": raw["receipt"], "records": rows})
    return retained(path), raw["receipt"]


def _size_command(runtime, t, o):
    # The protocol fixture reads its one command file on every provider request, including a continuation.
    t.set_command(o["commandId"], runtime.witness["sizeTool"], {"size": o["targetSize"]})


def _pending(runtime, name):
    t, o, proposal = _open(runtime, name)
    o["expiresAt"] = (proposal.get("preparedInvocation") or {}).get("expiresAt")
    return {"t": t, "o": o, "proposal": proposal}


def _approve(state):
    t, o, proposal = state["t"], state["o"], state["proposal"]
    o["refs"]["approve"] = t.approve(proposal["id"], revision=proposal["baseRevision"]).receipt
    return state


def _prepare_idle(runtime, name):
    t, o, _ = _open(runtime, name, autonomy="full-auto", drain=False)
    o["refs"]["initialStream"] = t.collect_turn(o["sessionId"]).receipt
    o["refs"]["ownerApplied"] = _owner_ref(runtime, name + "-owner-applied")
    o["refs"]["sessionBeforeRestart"] = t.session(o["sessionId"]).receipt
    return {"t": t, "o": o}


def _prepare_receipt_paused(runtime, name):
    state = _approve(_pending(runtime, name))
    t, o, proposal = state["t"], state["o"], state["proposal"]
    o["refs"]["execute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    o["refs"]["ownerAfterExecute"] = _owner_ref(runtime, name + "-owner-after-execute")
    return state


def _prepare_dispatched(runtime, name):
    state = _approve(_pending(runtime, name))
    owner = _json_file(runtime.run.root, state["o"]["refs"]["ownerBefore"])
    identity = runtime.active_identity
    state["control"] = {"schemaVersion": 1, "epoch": str(uuid.uuid4()), "key": {"subjectId": identity["subject"], "tenantId": identity["tenantId"],
                        "studioHostId": runtime.witness["hostId"], "namespace": "home-layout"}, "revision": owner["revision"],
                        "timeoutMilliseconds": 30000}
    state["engage"] = lambda: _engage_dispatched(runtime, state)
    return state


def _engage_dispatched(runtime, state):
    """Dispatch the approved write and hold it at the module's native write rendezvous until the host is killed."""
    t, o, proposal = state["t"], state["o"], state["proposal"]
    control_file, receipt_file = state["rendezvous"]
    check(not control_file.exists() and not receipt_file.exists(), "The restart rendezvous is one-shot per adapter")
    control_copy = t.output / "rendezvous-control.json"
    private_json(control_copy, state["control"])
    o["refs"]["rendezvousControl"] = retained(control_copy)
    atomic_json(control_file, state["control"])
    outcome = {}

    def execute():
        try:
            outcome["response"] = t.execute(proposal["id"], revision=proposal["baseRevision"])
        except WeaverTransportError as error:
            outcome["error"] = error

    thread = threading.Thread(target=execute, name="restart-dispatch", daemon=True)
    thread.start()
    state["thread"], state["outcome"] = thread, outcome
    deadline = time.monotonic() + HOLD_OBSERVATION_SECONDS
    while True:
        if receipt_file.is_file():
            try:
                held = json.loads(receipt_file.read_text())
            except ValueError:
                held = {}
            if held.get("status") == "waiting" and len(held.get("arrivals") or []) == 1:
                copy_path = t.output / "rendezvous-held.json"
                private_json(copy_path, held)
                o["refs"]["rendezvousHeld"] = retained(copy_path)
                return
        need(thread.is_alive(), "restart.dispatch_not_held", "The execution finished before its write reached the native rendezvous")
        need(time.monotonic() < deadline, "restart.dispatch_not_held", "The dispatched write never reached the native rendezvous")
        time.sleep(0.05)


def _prepare_in_progress(runtime, name):
    epoch = str(uuid.uuid4())

    def arm(t, command_id):
        gate = {"commandId": command_id, "epoch": epoch, "state": "hold", "timeoutMilliseconds": 60000}
        atomic_json(runtime.gate_file, gate)
        path = t.output / "gate.json"
        private_json(path, gate)
        return retained(path)

    t, o, _ = _open(runtime, name, drain=False, before_message=arm)
    o["gateEpoch"] = epoch
    # The held turn sends nothing while it waits; keep the reader open well past the transport's default read timeout.
    t.timeout = 180
    started, holder = threading.Event(), {}

    def read():
        try:
            with t.stream(o["sessionId"]) as stream:
                holder["stream"] = stream
                frame = stream.until(lambda item: item.value["kind"] == "started")
                holder["turnId"] = (frame.value.get("payload") or {}).get("turnId")
                started.set()
                stream.until(lambda item: False)  # Ends only when the killed host drops the connection.
        except (WeaverTransportError, RuntimeError) as error:
            holder["error"] = type(error).__name__
        finally:
            started.set()

    thread = threading.Thread(target=read, name="restart-turn", daemon=True)
    thread.start()
    started.wait(60)
    need(holder.get("turnId"), "restart.turn_not_running", "The provider turn did not start")
    deadline = time.monotonic() + HOLD_OBSERVATION_SECONDS
    while not any(row.get("kind") == "protocol.gate.waiting" and (row.get("data") or {}).get("commandId") == o["commandId"]
                  for row in t.protocol_trace(o["traceOffset"])["records"]):
        need(time.monotonic() < deadline, "restart.turn_not_running", "The actual adapter never reached its pre-call gate")
        time.sleep(0.05)
    return {"t": t, "o": o, "thread": thread, "holder": holder}


def _prepare_owner_resolution(runtime, name):
    """A reviewed client working-state change, dispatched to an HTTP client that disappears with the host.

    The binding stands in for the originating editor and never applies the change; the native workflow draft read
    back by its owning API is the effect oracle.
    """
    seed = seed_workflow(runtime.backend, runtime.output(name + "-workflow") / "workflow-seed")
    owner, before = runtime.native.json(runtime.backend, name + "-workflow-owner-before", seed["definitionPath"])
    draft = owner["draft"]
    revision = "client-" + hashlib.sha256(json.dumps(draft, sort_keys=True, separators=(",", ":")).encode()).hexdigest()[:32]
    binding = {"clientId": "restart-witness-" + uuid.uuid4().hex, "generationId": uuid.uuid4().hex}
    activity = seed["activityId"]
    target = {"moduleId": WORKFLOW_MODULE, "resourceType": "workflow-definition", "resourceId": seed["definitionId"], "revision": revision}
    content = {"workflowDefinitionId": seed["definitionId"], "draftId": seed["draftId"], "revision": revision, "selectedNodeId": activity,
               "activities": [{"id": activity, "type": "Elsa.Activities.Primitives.Activities.WriteLine", "displayName": "WriteLine"}],
               "connections": [], "positions": [{"nodeId": item["nodeId"], "x": item["x"], "y": item["y"]}
                                                for item in draft.get("layout", []) if item.get("nodeId") == activity]}
    attachment = {"id": "workflow:" + seed["definitionId"], "moduleId": WORKFLOW_MODULE, "schemaVersion": "1", "target": target,
                  "source": "workflow", "sourceId": seed["definitionId"], "label": "Restart witness workflow", "contentType": "workflow.definition",
                  "sensitivity": "internal", "scope": "selection", "origin": "clientSnapshot", "client": binding, "weaverScopes": ["workflow-author"],
                  "summary": "Restart witness workflow", "content": content,
                  "references": {"workflowDefinitionId": seed["definitionId"], "draftId": seed["draftId"], "revision": revision}}
    t = runtime.transport(name)
    offset = t.trace_file.stat().st_size if t.trace_file.exists() else 0
    refs = {"ownerBefore": before["body"], "workflowOwnerRead": before["receipt"]}
    created = t.create_session({"conversationId": str(uuid.uuid4()), "mode": "build", "autonomyMode": "manual",
                                "activeSurface": {"route": "/workflows/definitions/" + seed["definitionId"]}})
    refs["create"] = created.receipt
    session = created.require_status(200).json()["data"]["sessionId"]
    command_id, arguments = str(uuid.uuid4()), {"activityId": activity, "x": 480, "y": 320}
    t.set_command(command_id, WORKFLOW_TOOL, arguments)
    command = t.output / "selected-command.json"
    private_json(command, {"commandId": command_id, "toolName": WORKFLOW_TOOL, "arguments": arguments})
    refs["command"] = retained(command)
    message = t.message(session, {"content": "Owned restart client-operation request", "mode": "build", "requestId": str(uuid.uuid4()),
                                  "requestedCapabilities": [WORKFLOW_CAPABILITY], "contextAttachments": [attachment]})
    refs["message"] = message.receipt
    message.require_status(200)
    stream = t.collect_turn(session)
    refs["initialStream"] = stream.receipt
    proposals = [frame.value.get("payload") for frame in stream.frames if frame.value.get("kind") == "proposalCreated"]
    need(len(proposals) == 1 and isinstance(proposals[0], dict), "restart.client_setup", "The client operation did not pause at one review")
    proposal = proposals[0]
    refs["approve"] = t.approve(proposal["id"], revision=proposal["baseRevision"]).receipt
    refs["execute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    o = {"provider": runtime.provider, "sessionId": session, "commandId": command_id, "arguments": arguments, "binding": binding,
         "traceOffset": offset, "definitionPath": seed["definitionPath"], "expiresAt": (proposal.get("preparedInvocation") or {}).get("expiresAt"),
         "refs": refs}
    return {"t": t, "o": o, "proposal": proposal}


PREPARERS = {"idle": _prepare_idle, "pending-review-before-deadline": _pending, "pending-review-after-deadline": _pending,
             "approved-before-dispatch-before-deadline": lambda runtime, name: _approve(_pending(runtime, name)),
             "approved-before-dispatch-after-deadline": lambda runtime, name: _approve(_pending(runtime, name)),
             "dispatched-without-receipt": _prepare_dispatched, "receipt-recorded-paused": _prepare_receipt_paused,
             "provider-turn-in-progress": _prepare_in_progress, "owner-resolution": _prepare_owner_resolution, "second-client": _pending}


def _await_owner(t, session, *, tolerate):
    """Read the recovered session. A killed generation's live lease answers owned_elsewhere until it lapses (tolerate);
    a graceful stop released it, so there the first owned_elsewhere is returned at once for the case to refuse."""
    attempts = []
    deadline = time.monotonic() + OWNED_ELSEWHERE_LIMIT_SECONDS
    while True:
        observed_at = time.time()
        response = t.session(session)
        attempts.append(response.receipt)
        if response.status != 409:
            return response, observed_at, attempts
        code = ((response.json() or {}).get("error") or {}).get("code")
        need(code == "weaver.session.owned_elsewhere", "restart.ownership", "The recovered session answered " + str(code))
        if not tolerate:
            return response, observed_at, attempts
        need(time.monotonic() < deadline, "restart.ownership", "The stopped generation's lease never lapsed")
        hint = str(response.headers.get("retry-after", ""))
        delay = float(hint) if re.fullmatch(r"\d+(\.\d+)?", hint) else 1.0
        time.sleep(min(max(delay, 0.5), 5.0))


def _begin(runtime, state, restart, name):
    o = state["o"]
    o["refs"]["restart"] = restart["ref"]
    o["fromGeneration"], o["toGeneration"] = restart["fromGeneration"], restart["toGeneration"]
    t = runtime.transport(name + "-recovered")
    graceful = restart.get("kind") != "abrupt"
    response, observed_at, attempts = _await_owner(t, o["sessionId"], tolerate=not graceful)
    if graceful and response.status == 409:
        o["refs"]["ownedElsewhere0"], o["ownedElsewhereAttempts"] = response.receipt, 1
        raise RestartCaseFailure("restart.lease_not_released: A graceful stop left its session lease held: the recovered request answered "
                                 "weaver.session.owned_elsewhere", "restart.lease_not_released")
    o["refs"]["recoveredSession"] = response.receipt
    o["refs"].update({"ownedElsewhere" + str(index): reference for index, reference in enumerate(attempts[:-1])})
    o["recoveredAt"], o["ownedElsewhereAttempts"] = observed_at, len(attempts) - 1
    return t, o


def _finish(runtime, t, o, name):
    o["refs"]["trace"], o["refs"]["rawTrace"] = _command_trace(t, o, o["commandId"], name)
    if o.get("continuationCommandId"):
        o["refs"]["continuationTrace"], _ = _command_trace(t, o, o["continuationCommandId"], name + "-continuation")
    o["refs"]["session"] = t.session(o["sessionId"]).receipt
    o["refs"]["audit"] = t.audit(o["sessionId"]).receipt
    if "definitionPath" in o:
        _, after = runtime.native.json(runtime.backend, name + "-workflow-owner-after", o["definitionPath"])
        o["refs"]["ownerAfter"], o["refs"]["workflowOwnerAfterRead"] = after["body"], after["receipt"]
    else:
        o["refs"]["ownerAfter"] = _owner_ref(runtime, name + "-size-owner-after")
    return o


def _continue_read(runtime, t, o, name):
    w = runtime.witness
    state, _, _ = runtime.state(name + "-continuation-state")
    command = str(uuid.uuid4())
    t.set_command(command, w["readTool"], {})
    path = t.output / "continuation-command.json"
    private_json(path, {"commandId": command, "toolName": w["readTool"], "arguments": {}})
    o["refs"]["continuationCommand"], o["continuationCommandId"] = retained(path), command
    message = t.message(o["sessionId"], {"content": "Owned restart continuation", "mode": "build", "requestId": str(uuid.uuid4()),
                                         "requestedCapabilities": ["acme.home.read"], "contextAttachments": [runtime.attachment(state)]})
    o["refs"]["continuationMessage"] = message.receipt
    message.require_status(200)
    o["refs"]["continuation"] = t.collect_turn(o["sessionId"]).receipt


def _recover_idle(runtime, state, restart, name):
    t, o = _begin(runtime, state, restart, name)
    _continue_read(runtime, t, o, name)
    return _finish(runtime, t, o, name)


def _recover_reviewed(runtime, state, restart, name, *, approve, at_recovery=False):
    t, o = _begin(runtime, state, restart, name)
    proposal = state["proposal"]
    o["refs"]["recoveredProposals"] = t.request("GET", _session_path(o["sessionId"]) + "/proposals").receipt
    if at_recovery:
        o["refs"]["ownerAtRecovery"] = _owner_ref(runtime, name + "-owner-at-recovery")
    _size_command(runtime, t, o)
    if approve:
        o["refs"]["approve"] = t.approve(proposal["id"], revision=proposal["baseRevision"]).receipt
    o["refs"]["execute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    o["refs"]["continuation"] = t.collect_turn(o["sessionId"]).receipt
    return _finish(runtime, t, o, name)


def _recover_expired(runtime, state, restart, name):
    t, o = _begin(runtime, state, restart, name)
    proposal = state["proposal"]
    o["refs"]["recoveredProposals"] = t.request("GET", _session_path(o["sessionId"]) + "/proposals").receipt
    o["refs"]["execute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    return _finish(runtime, t, o, name)


def _recover_receipt_paused(runtime, state, restart, name):
    t, o = _begin(runtime, state, restart, name)
    o["refs"]["recoveredProposals"] = t.request("GET", _session_path(o["sessionId"]) + "/proposals").receipt
    o["refs"]["ownerAtRecovery"] = _owner_ref(runtime, name + "-owner-at-recovery")
    _size_command(runtime, t, o)
    o["refs"]["continuation"] = t.collect_turn(o["sessionId"]).receipt
    o["refs"]["settled"] = _settle(t, o)
    o["refs"]["repeatDelivery"] = t.collect_turn(o["sessionId"]).receipt
    return _finish(runtime, t, o, name)


def _recover_second_client(runtime, state, restart, name):
    t, o = _begin(runtime, state, restart, name)
    proposal, session = state["proposal"], o["sessionId"]
    foreign_http, _ = runtime.sign_in(1)
    foreign = runtime.transport(name + "-foreign", http=foreign_http)
    o["refs"]["foreignSession"] = foreign.session(session).receipt
    o["refs"]["foreignProposals"] = foreign.request("GET", _session_path(session) + "/proposals").receipt
    o["refs"]["foreignExecute"] = foreign.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    o["refs"]["ownerAfterForeign"] = _owner_ref(runtime, name + "-owner-after-foreign")
    first, _ = runtime.sign_in()
    second, _ = runtime.sign_in()
    clients = {"A": runtime.transport(name + "-client-a", http=first), "B": runtime.transport(name + "-client-b", http=second)}
    for label, client in clients.items():
        o["refs"]["proposals" + label] = client.request("GET", _session_path(session) + "/proposals").receipt
    _size_command(runtime, t, o)
    for label, client in clients.items():
        o["refs"]["approve" + label] = client.approve(proposal["id"], revision=proposal["baseRevision"]).receipt
    with ThreadPoolExecutor(max_workers=2) as executor:
        responses = dict(zip(clients, executor.map(lambda client: client.execute(proposal["id"], revision=proposal["baseRevision"]), clients.values())))
    for label, response in responses.items():
        o["refs"]["execute" + label] = response.receipt
    o["refs"]["continuation"] = clients["A"].collect_turn(session).receipt
    return _finish(runtime, clients["A"], o, name)


def _recover_dispatched(runtime, state, restart, name):
    state["thread"].join(60)
    outcome = state["outcome"]
    need(not state["thread"].is_alive(), "restart.dispatch_not_held", "The interrupted execution never ended")
    error = outcome.get("error")
    o = state["o"]
    o["refs"]["interruptedExecute"] = error.receipt if error is not None else outcome["response"].receipt
    t, o = _begin(runtime, state, restart, name)
    proposal = state["proposal"]
    o["refs"]["recoveredProposals"] = t.request("GET", _session_path(o["sessionId"]) + "/proposals").receipt
    o["refs"]["repeatExecute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    return _finish(runtime, t, o, name)


def _recover_in_progress(runtime, state, restart, name):
    state["thread"].join(60)
    need(not state["thread"].is_alive(), "restart.turn_not_running", "The interrupted stream never ended")
    stream = state["holder"].get("stream")
    need(stream is not None and stream.receipt is not None, "restart.turn_not_running", "The interrupted stream was not retained")
    o = state["o"]
    o["refs"]["interruptedStream"] = stream.receipt
    t, o = _begin(runtime, state, restart, name)
    o["refs"]["recoveredProposals"] = t.request("GET", _session_path(o["sessionId"]) + "/proposals").receipt
    _continue_read(runtime, t, o, name)
    return _finish(runtime, t, o, name)


def _recover_owner_resolution(runtime, state, restart, name):
    t, o = _begin(runtime, state, restart, name)
    proposal, session = state["proposal"], o["sessionId"]
    invocation = proposal["preparedInvocation"]["invocationId"]
    o["refs"]["recoveredProposals"] = t.request("GET", _session_path(session) + "/proposals").receipt
    path = _session_path(session) + "/client-invocations/" + invocation + "/resolution"
    o["refs"]["resolution"] = t.request("POST", path, {"claim": "notApplied"}).receipt
    o["refs"]["repeatResolution"] = t.request("POST", path, {"claim": "notApplied"}).receipt
    o["refs"]["conflictingResolution"] = t.request("POST", path, {"claim": "applied"}).receipt
    o["refs"]["repeatExecute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    return _finish(runtime, t, o, name)


RECOVERERS = {"idle": _recover_idle,
              "pending-review-before-deadline": lambda runtime, state, restart, name: _recover_reviewed(runtime, state, restart, name, approve=True),
              "approved-before-dispatch-before-deadline": lambda runtime, state, restart, name: _recover_reviewed(runtime, state, restart, name, approve=False, at_recovery=True),
              "pending-review-after-deadline": _recover_expired, "approved-before-dispatch-after-deadline": _recover_expired,
              "dispatched-without-receipt": _recover_dispatched, "receipt-recorded-paused": _recover_receipt_paused,
              "provider-turn-in-progress": _recover_in_progress, "owner-resolution": _recover_owner_resolution,
              "second-client": _recover_second_client}
check(set(PREPARERS) == set(RECOVERERS) == set(RESTART_CASES), "Every restart case needs one preparer and one recoverer")


# --- orchestration -------------------------------------------------------------------------------

def _restart_host(runtime, host, reason, phase, *, while_stopped=None):
    record = host.restart(reason, abrupt=phase == ABRUPT, while_stopped=while_stopped)
    record["phase"] = phase
    reference = runtime.save(runtime.output("restart-" + reason), "restart", record)
    runtime.sign_in()  # Never reuse a previous generation's token.
    return dict(record, ref=reference)


def _failed(runtime, provider, key, error, observation=None):
    precise = isinstance(error, (RestartCaseFailure, LifecycleCaseFailure))
    return {"id": provider + "-" + key, "provider": provider, "adapter": provider, "caseKey": key, "restart": RESTART_CASES[key],
            "status": "failed", "cause": error.cause if precise else ("restart.native_transport" if isinstance(error, WeaverTransportError) else "verification"),
            "message": str(error)[:1000], "observation": getattr(error, "observation", None) or observation,
            "nativeEvidenceVerified": False, "qualified": False, "source": runtime.run.source}


def _arm_rendezvous(runtime, provider):
    """New one-shot native write rendezvous paths for this adapter's abrupt phase (the module's own feature setting)."""
    control = runtime.controls / ("restart-" + provider + "-write-rendezvous.json")
    receipt = runtime.controls / ("restart-" + provider + "-write-rendezvous-receipt.json")
    check(not control.exists() and not receipt.exists(), "Restart rendezvous paths are new per adapter")
    applied = runtime.native.feature(runtime.backend, "restart-" + provider + "-rendezvous", runtime.witness["backendFeature"], enabled=True,
                                     configuration={"StudioHostId": runtime.witness["hostId"], "WriteRendezvousControlFile": str(control),
                                                    "WriteRendezvousReceiptFile": str(receipt)})
    return (control, receipt), applied["evidence"]


def _run_phase(runtime, host, provider, phase, keys=None):
    """Prepare the group's cases in order, restart the host once, then recover and verify every prepared case."""
    keys = PHASE_ORDER[phase] if keys is None else tuple(keys)
    check(keys and all(RESTART_CASES.get(key) == phase for key in keys), "A restart group holds only cases of its own phase")
    run, records, states = runtime.run, [], {}
    rendezvous = None
    if phase == ABRUPT:
        rendezvous, _ = _arm_rendezvous(runtime, provider)
    for key in keys:
        name = "restart-" + provider + "-" + key
        try:
            run.assert_immutable_inputs()
            runtime.sign_in()
            state = PREPARERS[key](runtime, name)
            state["name"] = name
            if rendezvous is not None:
                state["rendezvous"] = rendezvous
            states[key] = state
        except CASE_ERRORS as error:
            records.append(_failed(runtime, provider, key, error))
    for key, state in list(states.items()):
        if "engage" in state:
            try:
                state["engage"]()
            except CASE_ERRORS as error:
                records.append(_failed(runtime, provider, key, error))
                states.pop(key)

    def while_stopped():
        if phase == ABRUPT:
            atomic_json(runtime.gate_file, {"commandId": "not-an-active-command", "epoch": str(uuid.uuid4()), "state": "release"})
            removed = False
            if rendezvous is not None and rendezvous[0].exists():
                rendezvous[0].unlink()  # Its exact bytes are retained per case; a new generation must not re-arm it.
                removed = True
            return {"gateReleased": True, "rendezvousControlRemoved": removed}
        if phase == AFTER_DEADLINE:
            deadlines = [instant(state["o"]["expiresAt"]) for state in states.values()]
            target = (max(deadlines) + DEADLINE_MARGIN_SECONDS) if deadlines else time.time()
            while time.time() < target:
                time.sleep(min(5.0, max(target - time.time(), 0.05)))
            return {"waitedUntil": target, "deadlines": sorted(deadlines)}
        return None

    reason = provider + "-" + phase + ("" if keys == PHASE_ORDER[phase] else "-" + "-".join(keys))
    restart = _restart_host(runtime, host, reason, phase, while_stopped=while_stopped)
    for key, state in states.items():
        observation = None
        try:
            o = RECOVERERS[key](runtime, state, restart, state["name"])
            observation = runtime.save(runtime.output(state["name"] + "-observation"), "observation", o)
            record = VERIFIERS[key](run.root, o, runtime.witness, provider)
            record.update(observation=observation, source=run.source)
        except CASE_ERRORS as error:
            if observation is None and isinstance(state.get("o"), dict):
                try:
                    observation = runtime.save(runtime.output(state["name"] + "-partial-observation"), "observation", state["o"])
                except (RuntimeError, OSError, TypeError, ValueError):
                    observation = None
            record = _failed(runtime, provider, key, error, observation)
        records.append(record)
        run.save()
    return records, restart


def _control(runtime, host):
    """The native negative control on the in-memory default: the restart must lose the pending review."""
    name = "restart-control-" + runtime.provider
    state = _pending(runtime, name)
    restart = _restart_host(runtime, host, "in-memory-control", GRACEFUL)
    o, proposal = state["o"], state["proposal"]
    o["refs"]["restart"] = restart["ref"]
    o["fromGeneration"], o["toGeneration"] = restart["fromGeneration"], restart["toGeneration"]
    t = runtime.transport(name + "-recovered")
    o["refs"]["recoveredSession"] = t.session(o["sessionId"]).receipt
    o["refs"]["recoveredExecute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    o["refs"]["ownerAfter"] = _owner_ref(runtime, name + "-size-owner-after")
    observation = runtime.save(runtime.output(name + "-observation"), "observation", o)
    control = verify_control(runtime.run.root, o, runtime.witness)
    control.update(observation=observation, source=runtime.run.source)
    return control


def _enable_store(runtime, host):
    run = runtime.run
    directory = run.root / "durable-weaver-store"
    directory.mkdir(mode=0o700)
    database = directory / "weaver-records.db"
    key = secrets.token_urlsafe(48)
    selected = {}
    restart = _restart_host(runtime, host, "enable-durable-store", GRACEFUL,
                            while_stopped=lambda: selected.update(enable_durable_weaver_store(run, database, key)) or {"configured": DURABLE_STORE_FEATURE})
    catalog, record = runtime.native.json(runtime.backend, "durable-store-catalog", "/modularity/features")
    features = {item["id"]: item for item in catalog["features"]}
    check(all(features.get(name, {}).get("enabled") is True and features[name].get("runtimeAvailable") is True
              for name in (DURABLE_STORE_FEATURE, DURABLE_STORE_DEPENDENCY)), "The restarted host does not run the packaged durable store")
    bootstrap = runtime.transport("durable-store-weaver").bootstrap().require_status(200)
    check(bootstrap.json()["data"]["providerStatus"] == "available", "The durable store composition left the actual provider unavailable")
    return {**selected, "restart": restart["ref"], "catalog": record["receipt"], "bootstrap": bootstrap.receipt, "databaseObserved": False}


# --- Spec1203 T038: classed recovery and actual removed-owner recovery ----------------------------
# This slice produces fourteen records. It never replaces the separate thirty-case aggregate gate.

@contextmanager
def _classed_restart_bound():
    check(threading.current_thread() is threading.main_thread() and signal.getitimer(signal.ITIMER_REAL) == (0.0, 0.0),
          "Classed recovery needs an unoccupied interruptible main-thread stage")
    def expired(_signum, _frame):
        raise TimeoutError("Classed recovery stage exceeded 180 seconds; no action is replayed")
    previous = signal.signal(signal.SIGALRM, expired)
    signal.setitimer(signal.ITIMER_REAL, 180)
    try:
        yield
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, previous)


def _effect_journal_snapshot(runtime, paths, name):
    """Retain immutable bytes and namespace identity; failure is retained outside the potentially broken namespace."""
    refs = {}
    for kind, path in paths.items():
        record = {"sourcePath": str(path), "observedAt": time.time(), "status": "unknown"}
        output = runtime.output(name + "-" + kind)
        try:
            path = Path(path)
            check(path.is_absolute() and path.resolve().is_relative_to(runtime.run.root.resolve()) and not path.is_symlink()
                  and not path.parent.is_symlink(), "Journal escaped its private observation namespace")
            with path.open("rb") as stream:
                identity = os.fstat(stream.fileno())
                check(stat.S_ISREG(identity.st_mode) and identity.st_size <= 2 * 1024 * 1024, "Journal is not a bounded regular file")
                raw = stream.read(2 * 1024 * 1024 + 1)
                final = os.fstat(stream.fileno())
            check((identity.st_dev, identity.st_ino, identity.st_size, identity.st_mtime_ns) ==
                  (final.st_dev, final.st_ino, final.st_size, final.st_mtime_ns) and len(raw) == identity.st_size,
                  "Journal changed while its bytes were being observed")
            record.update(status="observed", device=identity.st_dev, inode=identity.st_ino, mode=stat.S_IMODE(identity.st_mode),
                          parentMode=stat.S_IMODE(path.parent.stat().st_mode), size=len(raw), rawBody=raw.decode("utf-8", "strict"),
                          bytesSha256=hashlib.sha256(raw).hexdigest(), failedMarker=os.path.lexists(str(path) + ".failed"),
                          probeExists=os.path.lexists(str(path) + ".probe"))
            reference = runtime.save(output, "journal", record)
            refs[kind] = reference
            from effects_journeys import require_execution_observation
            require_execution_observation(record, path=str(path), owner=runtime.active_identity, execution=kind == "execution")
        except Exception as error:
            # Even when a module cannot retain its .failed marker, this external receipt stays failed/unknown.
            failure = runtime.save(output, "failed-unknown", {"observation": record, "cause": type(error).__name__,
                                                              "requiresFreshObservationPath": True})
            raise RestartCaseFailure("effects.restart.journal_unknown: A module observation cannot prove a count",
                                     "effects.restart.journal_unknown", {"journal": refs.get(kind), "failure": failure}) from error
    return refs


def _effect_restart_snapshot(runtime, paths, name, *, available=True):
    from effects_journeys import OWNER, EXECUTION_ENDPOINT
    from journey_runtime import HOME_PREFERENCES_PATH
    result = {}
    reads = (("layout", HOME_PREFERENCES_PATH),)
    if available: reads += (("widget", OWNER), ("checks", OWNER + "/checks"), ("entries", EXECUTION_ENDPOINT))
    for key, path in reads:
        _, _, result[key] = runtime.snapshot(name + "-" + key, endpoint=path)
    result["journals"] = _effect_journal_snapshot(runtime, paths, name + "-journals")
    return result


def _effect_restart_identity(runtime, name):
    _, record = runtime.native.json(runtime.backend, name, "/_elsa/identity/session")
    return record["receipt"]


def _effect_restart_prepare(runtime, tool, branch_name, value):
    from effects_journeys import OWNER, CHECK_TOOL, _attachment
    before, _, _ = runtime.snapshot("classed-preparation-owner", endpoint=OWNER)
    arguments = {"expectedVisible": before["visible"]} if tool == CHECK_TOOL else {"visible": not before["visible"]}
    prepared_at = time.time()
    turn = runtime.turn("classed-restart-" + branch_name, tool=tool, arguments=arguments,
                        attachment=_attachment(runtime, before), capability="acme.home.effects")
    turn.message.require_status(200)
    check(turn.proposal is not None and turn.proposal.get("status") == "awaitingApproval", "No genuine classed pending preparation")
    proposal = turn.proposal
    branch = {"sessionId": turn.session, "commandId": turn.command_id, "proposalId": proposal["id"],
              "prepared": copy.deepcopy(proposal["preparedInvocation"]), "times": {"prepared": prepared_at},
              "refs": {"initialStream": turn.stream.receipt,
                       "command": next(ref for ref in turn.evidence if Path(ref["path"]).name == "selected-command.json")}}
    value["branches"][branch_name] = branch
    if branch_name == "approved": branch["refs"]["approvedBefore"] = runtime.approve(turn).receipt
    return turn, branch


def _effect_restart_reopen(runtime, turn, branch, restart, name):
    # _begin owns graceful lease-release diagnostics and fresh-generation transport selection.
    state = {"o": {"sessionId": turn.session, "refs": {}}}
    t, observation = _begin(runtime, state, restart, name)
    branch["refs"]["recoveredSession"] = observation["refs"]["recoveredSession"]
    branch["times"]["recovered"] = observation["recoveredAt"]
    branch["refs"]["recoveredProposals"] = t.request("GET", _session_path(turn.session) + "/proposals").require_status(200).receipt
    return t


def _effect_restart_refuse(runtime, turn, branch, t, branch_name, *, removed):
    from effects_journeys import POLICY_CODE, _restart_failure, _restart_proposal
    # One command file is shared by the fixture. Restore this exact command before its continuation, never another tool.
    t.set_command(turn.command_id, branch["prepared"]["toolName"], branch["prepared"]["arguments"])
    if branch_name == "pending":
        decision = t.approve(turn.proposal["id"], revision=turn.proposal["baseRevision"])
        branch["refs"]["decision"] = decision.receipt
        if removed:
            decision.require_status(200)
            check(decision.json().get("data", {}).get("status") == "approved", "Removed-owner approval did not succeed")
        else:
            _restart_failure(runtime.run.root, decision.receipt, runtime.backend_origin, turn.proposal, "approve", POLICY_CODE, 403)
    branch["times"]["execute"] = time.time()
    branch["refs"]["execute"] = t.execute(turn.proposal["id"], revision=turn.proposal["baseRevision"]).receipt
    _restart_failure(runtime.run.root, branch["refs"]["execute"], runtime.backend_origin, turn.proposal, "execute",
                     "weaver.proposal.executor_unavailable" if removed else POLICY_CODE, 409 if removed else 403)
    branch["refs"]["continuation"] = t.collect_turn(turn.session).receipt
    branch["refs"]["session"] = t.session(turn.session).require_status(200).receipt
    branch["refs"]["terminalProposals"] = t.request("GET", _session_path(turn.session) + "/proposals").require_status(200).receipt
    terminal = _restart_proposal(runtime.run.root, branch["refs"]["terminalProposals"], runtime.backend_origin,
                                 turn.session, turn.proposal["id"], branch["prepared"])
    check(terminal.get("status") == "failed", "No proven terminal refusal; restoration must never trigger an execute retry")
    branch["refs"]["audit"] = t.audit(turn.session).require_status(200).receipt
    branch["refs"]["trace"] = t.protocol_trace(turn.offset)["receipt"]


def _effect_restart_remove(runtime, value):
    """Remove only the resource's exact private feed archive using the existing native package lifecycle."""
    from native_lifecycle import remove_resource_archives, require_installed_package, require_removed_package
    from artifacts import sha
    package = runtime.run.report["authors"]["backend"]["packed"]["package"]
    value["package"] = {key: package[key] for key in ("id", "version", "sha256")}
    selected = [item for item in runtime.run.profiles["backend"]["runtimeArchiveInputs"]
                if item["identity"].casefold() == (package["id"] + "/" + package["version"]).casefold()]
    check(len(selected) == 1 and selected[0]["sha256"] == package["sha256"], "No unique original independent resource archive")
    feed = runtime.run.profiles["backend"]["runtimeFeed"]
    delta = {"added": selected, "baseline": {path.name: sha(path) for path in feed.glob("*.nupkg") if path != Path(selected[0]["path"])}}
    installed, ref = runtime.native.membership(runtime.backend, "classed-owner-installed")
    value["refs"]["installed"] = ref["receipt"]
    require_installed_package(installed, package)
    disabled = runtime.native.feature(runtime.backend, "classed-owner-disable", runtime.witness["backendFeature"], enabled=False)
    value["refs"]["disableTransition"] = runtime.save(runtime.output("classed-owner-disable"), "transition", disabled)
    disabled_inventory, ref = runtime.native.membership(runtime.backend, "classed-owner-disabled")
    value["refs"]["disabled"] = ref["receipt"]
    require_installed_package(disabled_inventory, package)
    # Publish the delta before removal so cleanup knows exactly what was attempted even on an ambiguous failure.
    value["archiveDelta"] = delta
    remove_resource_archives(runtime.run, "backend", delta)
    _, ref = runtime.native.reconcile(runtime.backend, "classed-owner-native-remove")
    value["refs"]["removeReconcile"] = ref["receipt"]
    removed, ref = runtime.native.membership(runtime.backend, "classed-owner-removed")
    value["refs"]["removed"] = ref["receipt"]
    require_removed_package(installed, removed, package)
    _, ref = runtime.native.generation(runtime.backend, "classed-owner-removed-generation")
    value["refs"]["generationRemoved"] = ref["receipt"]


def _effect_restart_restore(runtime, value, configuration):
    from native_lifecycle import restore_resource_archives
    if "archiveDelta" in value:
        restore_resource_archives(runtime.run, "backend", value["archiveDelta"])
        _, ref = runtime.native.reconcile(runtime.backend, "classed-owner-native-restore")
        value["refs"]["restoreReconcile"] = ref["receipt"]
        restored = runtime.native.feature(runtime.backend, "classed-owner-enable", runtime.witness["backendFeature"],
                                          enabled=True, configuration=configuration)
        value["refs"]["restoreTransition"] = runtime.save(runtime.output("classed-owner-restore"), "transition", restored)
        _, ref = runtime.native.membership(runtime.backend, "classed-owner-restored")
        value["refs"]["restored"] = ref["receipt"]
        value["ownerRestored"] = True


def _classed_effect_restart(runtime, host, value, original):
    from effects_journeys import (EXECUTION_HEADER, CLASSES, _policy, require_effect_restart_case, _restart_policy)
    from consumers import configure_weaver_privileges
    removed = value["id"].endswith("/removed-owner-pending")
    refs, paths = value["refs"], value["journalPaths"]
    root = runtime.output("classed-journal-namespace")
    for kind, filename in (("execution", "execution-entries.jsonl"), ("checks", "checks.jsonl")):
        path = root / filename
        # Never repair/rearm a path, including a failed marker whose journal vanished.
        check(not any(os.path.lexists(str(path) + suffix) for suffix in ("", ".failed", ".probe")), "Journal namespace was already used")
        with path.open("x", encoding="utf-8") as stream:
            stream.write(EXECUTION_HEADER + "\n" if kind == "execution" else "")
        path.chmod(0o600)
        paths[kind] = str(path)
    config = dict(original["configuration"], StudioHostId=runtime.witness["hostId"], ExecutionObservationFile=paths["execution"],
                  CheckObservationFile=paths["checks"])
    runtime.apply_features("classed-arm-owner-journals", {runtime.witness["backendFeature"]: (True, config)})
    runtime.sign_in()
    refs["policyBefore"] = _policy(runtime, [True] * len(CLASSES))
    refs["identityBefore"] = _effect_restart_identity(runtime, "classed-owner-identity-before")
    _, catalog = runtime.native.json(runtime.backend, "classed-journal-configuration", "/modularity/features")
    refs["catalog"] = catalog["receipt"]
    _, generation = runtime.native.generation(runtime.backend, "classed-owner-generation-before")
    refs["generationBefore"] = generation["receipt"]
    value["snapshots"]["before"] = _effect_restart_snapshot(runtime, paths, "classed-before")
    states = {name: _effect_restart_prepare(runtime, value["tool"], name, value)
              for name in (("pending",) if removed else ("pending", "approved"))}
    if removed: _effect_restart_remove(runtime, value)
    def disable_policy():
        configure_weaver_privileges(runtime.run, **dict.fromkeys(CLASSES, False))
        return {"privileges": dict.fromkeys(CLASSES, False)}
    restart = _restart_host(runtime, host, "classed-" + runtime.provider + "-" + value["tool"] + ("-removed" if removed else "-disabled"),
                            GRACEFUL, while_stopped=None if removed else disable_policy)
    refs["restart"] = restart["ref"]
    value.update(fromGeneration=restart["fromGeneration"], toGeneration=restart["toGeneration"])
    refs["identityRecovered"] = _effect_restart_identity(runtime, "classed-owner-identity-recovered")
    # Read the new generation before making any live policy write; this proves the disabled policy crossed the restart.
    refs["policyRecovered"] = runtime.transport("classed-recovered-bootstrap").bootstrap().require_status(200).receipt
    _restart_policy(runtime.run.root, refs["policyRecovered"], runtime.backend_origin, removed)
    _, generation = runtime.native.generation(runtime.backend, "classed-owner-generation-recovered")
    refs["generationRecovered"] = generation["receipt"]
    if removed:
        _, ref = runtime.native.membership(runtime.backend, "classed-owner-absent-after-restart")
        refs["removedRecovered"] = ref["receipt"]
    transports = {name: _effect_restart_reopen(runtime, turn, branch, restart, "classed-recovered-" + name)
                  for name, (turn, branch) in states.items()}
    value["snapshots"]["recovered"] = _effect_restart_snapshot(runtime, paths, "classed-recovered-no-dispatch", available=not removed)
    for name, (turn, branch) in states.items():
        _effect_restart_refuse(runtime, turn, branch, transports[name], name, removed=removed)
    if removed: _effect_restart_restore(runtime, value, config)
    runtime.sign_in()
    refs["identityAfter"] = _effect_restart_identity(runtime, "classed-owner-identity-after")
    value["snapshots"]["after"] = _effect_restart_snapshot(runtime, paths, "classed-refused")
    refs["policyRestored"] = _policy(runtime, [True] * len(CLASSES))
    for name, (turn, branch) in states.items():
        t = runtime.transport("classed-repeat-closed-" + name)
        branch["times"]["repeated"] = time.time()
        branch["refs"]["repeatExecute"] = t.execute(turn.proposal["id"], revision=turn.proposal["baseRevision"]).receipt
    value["snapshots"]["repeated"] = _effect_restart_snapshot(runtime, paths, "classed-refused-after-restoration")
    _, generation = runtime.native.generation(runtime.backend, "classed-owner-generation-after")
    refs["generationAfter"] = generation["receipt"]
    value["status"] = "passed"
    require_effect_restart_case(value, runtime.run.root, source=runtime.run.source)


def qualify_classed_effect_restarts(runtime, host):
    """Produce fourteen native records once; failures stay explicit and cannot promote the full T038 matrix."""
    from effects_journeys import CLASSED_RESTART_CASES, CHECK_TOOL, effect_restart_bindings, EVIDENCE_CATEGORY, _policy
    from consumers import DURABLE_EFFECTS_FEATURE
    check(runtime.run.report.get("independentPackageWitness") is False and host.current is not None,
          "Classed recovery runs inside an unqualified owned package witness")
    output = runtime.run.root / "classed-effect-restart-records.json"
    started = runtime.run.root / "classed-effect-restarts-started.json"
    check(not output.exists() and not started.exists(), "Never resume/overwrite classed recovery observations")
    private_json(started, {"source": runtime.run.source, "expectedCaseIds": list(CLASSED_RESTART_CASES), "qualified": False})
    original_provider, records, blocked = runtime.provider, [], None
    original = None
    try:
        catalog, _ = runtime.native.json(runtime.backend, "classed-restart-original-catalog", "/modularity/features")
        features = {row["id"]: row for row in catalog["features"]}
        check(all(features.get(name, {}).get("enabled") is True for name in (DURABLE_STORE_FEATURE, DURABLE_STORE_DEPENDENCY, DURABLE_EFFECTS_FEATURE)),
              "Root must select/finalize the packaged durable effects composition before classed recovery")
        original = copy.deepcopy(features[runtime.witness["backendFeature"]])
        check(original["enabled"] is True, "Independent resource owner must initially be installed and enabled")
    except Exception as error:
        blocked = {"type": type(error).__name__, "message": str(error)[:1000], "cause": "effects.restart.setup"}
    for identity in CLASSED_RESTART_CASES:
        provider, key = identity.split("/", 1)
        value = {"id": identity, "provider": provider, "adapter": provider, "tool": CHECK_TOOL if key == "removed-owner-pending" else key.removesuffix(":recovery-disabled"),
                 "status": "running", "source": runtime.run.source, "bindings": effect_restart_bindings(), "evidenceCategory": EVIDENCE_CATEGORY,
                 "liveProvider": False, "qualified": False, "independentPackageWitness": False, "backendOrigin": runtime.backend_origin,
                 "witness": copy.deepcopy(runtime.witness), "journalPaths": {}, "refs": {}, "snapshots": {}, "branches": {}}
        try:
            if blocked is not None:
                value.update(status="failed", failure=dict(blocked), attempted=False)
            else:
                with _classed_restart_bound():
                    runtime.select_provider(provider)
                    _classed_effect_restart(runtime, host, value, original)
        except Exception as error:
            value.update(status="failed", failure={"type": type(error).__name__, "cause": getattr(error, "cause", "effects.restart.verification"),
                                                   "message": str(error)[:1000], "observation": getattr(error, "observation", None)})
        finally:
            if original is not None and blocked is None:
                try:
                    check(host.current is not None, "Owned backend stopped before recovery cleanup; do not start/replay it implicitly")
                    runtime.sign_in()
                    if "archiveDelta" in value and value.get("ownerRestored") is not True:
                        configuration = dict(original["configuration"], StudioHostId=runtime.witness["hostId"],
                                             ExecutionObservationFile=value["journalPaths"]["execution"], CheckObservationFile=value["journalPaths"]["checks"])
                        _effect_restart_restore(runtime, value, configuration)
                    _policy(runtime, [True] * 3)
                    runtime.apply_features("classed-restore-original-owner", {runtime.witness["backendFeature"]: (original["enabled"], original["configuration"])})
                except Exception as error:
                    blocked = {"type": type(error).__name__, "message": str(error)[:1000], "cause": "effects.restart.cleanup"}
                    value.update(status="failed", cleanupFailure=dict(blocked))
            # A failed/unknown result is immutable evidence too; no native retry or same-namespace repair.
            value["record"] = runtime.save(runtime.output("classed-restart-case"), "observation", value)
            records.append(value)
    if blocked is None:
        try:
            runtime.select_provider(original_provider)
        except Exception as error:
            last = records[-1]
            previous = last.pop("record")
            last.update(status="failed", previousRecord=previous, cleanupFailure={"type": type(error).__name__, "message": str(error)[:1000]})
            last["record"] = runtime.save(runtime.output("classed-final-cleanup-failure"), "observation", last)
    private_json(output, records)
    return records


def qualify_witness_restarts(runtime, host):
    """T031: the restart inventory appended to the complete package witness; it never promotes the witness itself."""
    run = runtime.run
    check(run.report.get("status") == "running" and isinstance(run.report.get("witnessManifest"), dict)
          and (run.report.get("autonomyLifecycleQualification") or {}).get("status") == "passed",
          "The restart inventory runs only inside a running complete witness after the autonomy matrix")
    section = _run_restart_inventory(runtime, host, "complete-package-witness-restarts")
    require_restart_inventory(section, run.root, source=run.source)
    return section


def _run_restart_inventory(runtime, host, scope):
    run = runtime.run
    check(RESTART_QUALIFICATION not in run.report and run.report.get("independentPackageWitness") is False,
          "Use a fresh restart inventory, never replace a qualified or partial observation")
    section_path = run.root / "restart-recovery-qualification.json"
    check(not section_path.exists(), "Never overwrite a retained restart inventory")
    section = {"status": "running", "scope": scope, "source": run.source, "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False,
               "qualified": False, "independentPackageWitness": False, "expectedCaseIds": sorted(EXPECTED_CASES), "controlId": CONTROL_ID,
               "cases": [], "restarts": []}
    run.report[RESTART_QUALIFICATION] = section
    run.save()
    try:
        try:
            section["control"] = _control(runtime, host)
        except CASE_ERRORS as error:
            section["control"] = {"id": CONTROL_ID, "status": "unobserved", "cause": getattr(error, "cause", "verification"), "message": str(error)[:1000]}
        section["durableStore"] = _enable_store(runtime, host)
        run.save()
        for provider in PROVIDERS:
            runtime.select_provider(provider)
            for phase, keys in RESTART_GROUPS:
                records, restart = _run_phase(runtime, host, provider, phase, keys)
                section["cases"].extend(records)
                section["restarts"].append(restart["ref"])
                run.save()
                run.assert_immutable_inputs()
        database = Path(section["durableStore"]["database"])
        section["durableStore"]["databaseObserved"] = database.is_file() and database.stat().st_size > 0
        # The final gate reads the finished section; a failing gate turns it back to failed below.
        section["status"] = "passed"
        require_restart_inventory(section, run.root, source=run.source)
    except BaseException as error:
        section.update(status="failed", failure={"type": type(error).__name__, "message": str(error)[:1000]})
        raise
    finally:
        check(run.report.get("independentPackageWitness") is False, "The restart inventory cannot promote package qualification")
        private_json(section_path, section)
        run.save()
    return section
