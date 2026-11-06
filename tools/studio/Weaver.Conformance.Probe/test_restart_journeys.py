"""Synthetic oracle and negative controls for the #3441 restart inventory; no native/provider qualification is claimed."""
import copy
import json
import os
from pathlib import Path
import secrets
import stat
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock

from case_records import retained
from consumers import (COMMON_EFFECTS_FEATURE, DURABLE_EFFECTS_FEATURE, DURABLE_STORE_DEPENDENCY, DURABLE_STORE_FEATURE,
                       backend_features, enable_durable_agent_store)
import restart_journeys as rj
from restart_journeys import (ABRUPT, AFTER_DEADLINE, CONTROL_ID, CORRELATION_KEYS, EXPECTED_CASES, EXPECTED_CONTROL, EXPECTED_OUTCOMES,
                              GRACEFUL, PHASE_ORDER, RESTART_CASES, RESTART_GROUPS, RestartCaseFailure, require_restart_inventory,
                              restart_manifest_gaps)
from test_lifecycle_journeys import COMMAND, INVOCATION, PROPOSAL, SESSION, TURN, WITNESS, Native, http, owner, sse

EXPIRES = "2026-11-02T10:05:00.1234567+00:00"
DEADLINE = rj.instant(EXPIRES)
CACHES = {"NUGET_PACKAGES": [], "NUGET_HTTP_CACHE_PATH": []}
EXECUTE = "/_elsa/agent/proposals/" + PROPOSAL + "/execute"
APPROVED_AUDIT = {"id": "audit-approved", "kind": "proposalApproved", "actorId": "owner", "metadata": {"proposalId": PROPOSAL, "status": "Approved"}}


def restart_record(phase, *, stopped=DEADLINE - 30, started=DEADLINE + 5, exit_code=None):
    return {"phase": phase, "kind": "abrupt" if phase == ABRUPT else "graceful", "fromGeneration": 1, "toGeneration": 2,
            "stoppedAt": stopped, "startedAt": started,
            "stoppedProcess": {"exitCode": (-9 if phase == ABRUPT else 0) if exit_code is None else exit_code, "runtimeCacheFiles": copy.deepcopy(CACHES)}}


def settled(status):
    return {"id": "audit-settled", "kind": "proposalFailed", "actorId": None,
            "metadata": {"proposalId": PROPOSAL, "status": status, "settledBy": "recovery", "dispatch": "none", "effect": "none"}}


def audit(*rows):
    return http("GET", rj._audit_path(SESSION), 200, {"data": [dict(row, sessionId=SESSION) for row in rows]})


class Case:
    """One restart case's retained evidence, built over the lifecycle receipts of the same reviewed size operation."""
    def __init__(self, root, key, provider="copilot", change=None):
        self.native = Native(root, "duplicate-effect-delivery", provider, mutate={"initialStream": self.stamp})
        self.key, self.provider = key, provider
        parts = self.native.parts
        proposal = next(frame["value"]["payload"] for frame in parts["initialStream"]["frames"] if frame["value"]["kind"] == "proposalCreated")
        listed = lambda status: http("GET", "/_elsa/agent/sessions/" + SESSION + "/proposals", 200, {"data": [dict(proposal, status=status)]})
        result = self.native.result()
        self.recovered_at = DEADLINE - 60
        self.overrides = {}
        extra = {"recoveredSession": self.native.session([result])}
        continuation = {"continuationCommand": {"commandId": "command-2", "toolName": WITNESS["readTool"], "arguments": {}},
                        "continuationMessage": http("POST", "/_elsa/agent/sessions/" + SESSION + "/messages", 200,
                                                    {"data": {"message": {"id": "message-2", "sessionId": SESSION, "role": "user"}}}),
                        "continuationTrace": {"records": [{"kind": provider + ".request", "data": {"commandId": "command-2"}},
                                                          {"kind": provider + ".call", "data": {"commandId": "command-2", "toolName": WITNESS["readTool"]}}],
                                              "incompleteTailBytes": 0}}
        if key in ("pending-review-before-deadline", "approved-before-dispatch-before-deadline"):
            extra.update(restart=restart_record(GRACEFUL), recoveredProposals=listed("awaitingApproval" if key.startswith("pending") else "approved"),
                         ownerAtRecovery=owner("rev-1", "medium"), audit=audit(APPROVED_AUDIT))
        elif key == "second-client":
            refused = lambda method, path, status, code: http(method, path, status, {"data": None, "error": {"code": code}})
            decision = http("POST", "/_elsa/agent/proposals/" + PROPOSAL + "/approve", 200, {"data": {"status": "approved", "approvedAt": "2026-11-02T10:01:00Z"}})
            extra.update(restart=restart_record(GRACEFUL), audit=audit(APPROVED_AUDIT), ownerAfterForeign=owner("rev-1", "medium"),
                         foreignSession=refused("GET", "/_elsa/agent/sessions/" + SESSION, 404, "agent.session.not_found"),
                         foreignProposals=refused("GET", "/_elsa/agent/sessions/" + SESSION + "/proposals", 404, "agent.session.not_found"),
                         foreignExecute=refused("POST", EXECUTE, 403, "agent.proposal.forbidden"),
                         proposalsA=listed("awaitingApproval"), proposalsB=listed("awaitingApproval"),
                         approveA=decision, approveB=copy.deepcopy(decision), executeA=copy.deepcopy(parts["execute"]), executeB=copy.deepcopy(parts["execute"]))
        elif key == "idle":
            parts["initialStream"] = sse([{"kind": "started", "payload": {"turnId": TURN}},
                                          {"kind": "contributedResult", "payload": result}, {"kind": "completed"}])
            history = self.native.session([result])
            extra.update(restart=restart_record(GRACEFUL), ownerApplied=owner("rev-2", "small"), sessionBeforeRestart=history,
                         recoveredSession=copy.deepcopy(history), continuation=sse([{"kind": "started"}, {"kind": "completed"}]),
                         audit=audit(), **continuation)
            self.overrides = {"continuationCommandId": "command-2"}
        elif key == "provider-turn-in-progress":
            recovered = self.native.session([])
            body = json.loads(recovered["rawBody"])
            body["data"]["messages"][0].update(status="cancelled", statusReason="interrupted")
            recovered["rawBody"] = json.dumps(body)
            parts.update(trace={"records": [{"kind": provider + ".request", "data": {"commandId": COMMAND}},
                                            {"kind": "protocol.gate.waiting", "data": {"provider": provider, "commandId": COMMAND, "epoch": "epoch-1"}}],
                                "incompleteTailBytes": 0}, ownerAfter=owner("rev-1", "medium"))
            extra.update(restart=restart_record(ABRUPT), gate={"commandId": COMMAND, "epoch": "epoch-1", "state": "hold", "timeoutMilliseconds": 60000},
                         interruptedStream={"status": 200, "termination": "read-error", "errorType": "AgentTransportError",
                                            "frames": [{"ordinal": 1, "value": {"kind": "started", "id": "event-0", "payload": {"turnId": TURN}}}]},
                         recoveredSession=recovered, recoveredProposals=http("GET", "/_elsa/agent/sessions/" + SESSION + "/proposals", 200, {"data": []}),
                         continuation=sse([{"kind": "started"}, {"kind": "completed"}]), audit=audit(), **continuation)
            self.overrides = {"continuationCommandId": "command-2", "gateEpoch": "epoch-1"}
        elif key == "receipt-recorded-paused":
            extra.update(restart=restart_record(GRACEFUL), recoveredProposals=listed("executed"), ownerAtRecovery=owner("rev-2", "small"),
                         settled=self.native.session([result]), audit=audit(APPROVED_AUDIT))
        elif key in ("pending-review-after-deadline", "approved-before-dispatch-after-deadline"):
            parts["execute"] = http("POST", EXECUTE, 409, {"data": {"status": "expired", "executed": False}, "error": {"code": "agent.proposal.expired"}})
            parts["ownerAfter"] = owner("rev-1", "medium")
            extra.update(restart=restart_record(AFTER_DEADLINE), recoveredProposals=listed("expired"), audit=audit(APPROVED_AUDIT, settled("Expired")))
        elif key == "dispatched-without-receipt":
            parts["ownerAfter"] = owner("rev-1", "medium")
            extra.update(restart=restart_record(ABRUPT), recoveredProposals=listed("uncertain"),
                         rendezvousControl={"epoch": "epoch-1"},
                         rendezvousHeld={"status": "waiting", "epoch": "epoch-1", "revision": "rev-1", "arrivals": [{"httpTraceIdentifier": "trace-1"}]},
                         interruptedExecute={"origin": "http://127.0.0.1:52001", "method": "POST", "path": EXECUTE, "requestBody": {"revision": "rev-1"},
                                             "transportErrorType": "ConnectionResetError"},
                         repeatExecute=http("POST", EXECUTE, 200, {"data": {"proposalId": PROPOSAL, "executed": False, "status": "uncertain"}, "error": None}),
                         audit=audit(APPROVED_AUDIT, settled("Uncertain")))
        else:
            raise ValueError(key)
        parts.update(extra)
        self.native.order.extend(name for name in extra if name not in self.native.order)
        for name, mutation in (change or {}).items():
            mutation(parts[name])

    @staticmethod
    def stamp(stream):
        for frame in stream["frames"]:
            if frame["value"]["kind"] == "proposalCreated":
                frame["value"]["payload"]["preparedInvocation"].update(expiresAt=EXPIRES, approvalDigest="digest-1")

    def observation(self, **overrides):
        value = self.native.observation()
        value.update({"fromGeneration": 1, "toGeneration": 2, "recoveredAt": self.recovered_at, "expiresAt": EXPIRES, **self.overrides, **overrides})
        return value

    def verify(self, root, **overrides):
        return rj.VERIFIERS[self.key](root, self.observation(**overrides), WITNESS, self.provider)


BINDING = {"clientId": "restart-witness-client", "generationId": "generation-1"}
ARGUMENTS = {"activityId": "witness-write", "x": 480, "y": 320}
RESOLUTION = "/_elsa/agent/sessions/" + SESSION + "/client-invocations/" + INVOCATION + "/resolution"


def owner_resolution_observation(root, provider="copilot", change=None):
    """The retained shape of the uncertain client operation case; the draft is the workflow owner's actual readback."""
    proposal = {"id": PROPOSAL, "sessionId": SESSION, "status": "awaitingApproval", "requiresApproval": True, "baseRevision": "client-rev",
                "preparedInvocation": {"toolName": rj.WORKFLOW_TOOL, "executionLocation": "clientWorkingState", "client": BINDING,
                                       "arguments": ARGUMENTS, "invocationId": INVOCATION, "toolCallId": COMMAND, "approvalDigest": "digest-1",
                                       "expiresAt": EXPIRES}}
    workflow = {"definition": {"id": "definition-1"}, "draft": {"id": "draft-1", "layout": [{"nodeId": "witness-write", "x": 120, "y": 160}]}}
    statement = {"claim": "notApplied", "actorId": "owner", "resolvedAt": "2026-11-02T10:06:00Z"}
    resolved = {"id": "audit-resolved", "kind": "proposalResolved", "actorId": "owner", "metadata": {"proposalId": PROPOSAL, "claim": "notApplied"}}
    parts = {
        "create": http("POST", "/_elsa/agent/sessions", 200, {"data": {"sessionId": SESSION}}),
        "command": {"commandId": COMMAND, "toolName": rj.WORKFLOW_TOOL, "arguments": ARGUMENTS},
        "message": http("POST", "/_elsa/agent/sessions/" + SESSION + "/messages", 200, {"data": {"message": {"id": "message-1"}}}),
        "initialStream": sse([{"kind": "started", "payload": {"turnId": TURN}}, {"kind": "proposalCreated", "payload": proposal},
                              {"kind": "toolApprovalRequested", "proposalId": PROPOSAL}]),
        "approve": http("POST", "/_elsa/agent/proposals/" + PROPOSAL + "/approve", 200, {"data": {"status": "approved"}}),
        "execute": http("POST", EXECUTE, 202, {"data": {"proposalId": PROPOSAL, "executed": False, "clientInvocation": {"invocationId": INVOCATION}}}),
        "trace": {"records": [{"kind": provider + ".request", "data": {"commandId": COMMAND}},
                              {"kind": provider + ".call", "data": {"commandId": COMMAND, "toolName": rj.WORKFLOW_TOOL, "arguments": ARGUMENTS}}],
                  "incompleteTailBytes": 0},
        "restart": restart_record(AFTER_DEADLINE), "recoveredSession": {"status": 200},
        "recoveredProposals": http("GET", "/_elsa/agent/sessions/" + SESSION + "/proposals", 200, {"data": [dict(proposal, status="uncertain")]}),
        "resolution": http("POST", RESOLUTION, 200, {"data": dict(proposal, status="uncertain", ownerResolution=statement)}, {"claim": "notApplied"}),
        "repeatResolution": http("POST", RESOLUTION, 200, {"data": dict(proposal, status="uncertain", ownerResolution=statement)}, {"claim": "notApplied"}),
        "conflictingResolution": http("POST", RESOLUTION, 409, {"data": None, "error": {"code": "agent.invocation.resolution_conflict"}}, {"claim": "applied"}),
        "repeatExecute": http("POST", EXECUTE, 200, {"data": {"proposalId": PROPOSAL, "executed": False, "status": "uncertain", "clientInvocation": None}}),
        "audit": audit(APPROVED_AUDIT, dict(settled("Uncertain"), metadata={**settled("Uncertain")["metadata"], "dispatch": "client", "effect": "possible"}), resolved),
        "ownerBefore": workflow, "ownerAfter": copy.deepcopy(workflow)}
    for name, mutation in (change or {}).items():
        mutation(parts[name])
    refs = {}
    for index, (name, value) in enumerate(parts.items(), 1):
        path = Path(root) / f"{index:04d}-{name}.json"
        path.write_text(json.dumps(value))
        refs[name] = retained(path)
    return {"provider": provider, "sessionId": SESSION, "commandId": COMMAND, "arguments": ARGUMENTS, "binding": BINDING, "traceOffset": 0,
            "expiresAt": EXPIRES, "fromGeneration": 1, "toGeneration": 2, "recoveredAt": DEADLINE + 10, "refs": refs}


class RestartVerifierTests(unittest.TestCase):
    """Oracle: the actual retained shapes pass with exactly the driver-owned outcome; controls fail with their cause."""
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()

    def case_root(self, name):
        path = self.root / name
        path.mkdir()
        return path

    def test_oracle_cases_pass_on_each_actual_adapter_with_the_expected_outcome(self):
        for provider in ("copilot", "anthropic"):
            with self.subTest(key="owner-resolution", provider=provider):
                record = rj.verify_owner_resolution(self.root, owner_resolution_observation(self.case_root("owner-" + provider), provider), WITNESS, provider)
                self.assertEqual(EXPECTED_OUTCOMES["owner-resolution"], record["outcome"])
        for key in sorted(set(RESTART_CASES) - {"owner-resolution"}):
            for provider in ("copilot", "anthropic"):
                with self.subTest(key=key, provider=provider):
                    root = self.case_root(key + "-" + provider)
                    record = Case(root, key, provider).verify(self.root)
                    self.assertEqual((provider + "-" + key, "passed"), (record["id"], record["status"]))
                    self.assertEqual(EXPECTED_OUTCOMES[key], record["outcome"])
                    self.assertEqual((1, 2), (record["correlation"]["fromGeneration"], record["correlation"]["toGeneration"]))
                    self.assertTrue(all(record["correlation"].get(name) for name in CORRELATION_KEYS[key]))

    def test_a_skipped_settlement_write_fails_the_after_deadline_case(self):
        changes = {"still-pending": {"recoveredProposals": lambda part: part.update(rawBody=part["rawBody"].replace('"expired"', '"awaitingApproval"'))},
                   "no-settlement-record": {"audit": lambda part: part.update(rawBody=json.dumps({"data": [dict(APPROVED_AUDIT, sessionId=SESSION)]}))},
                   "user-attributed-settlement": {"audit": lambda part: part.update(rawBody=part["rawBody"].replace('"actorId": null', '"actorId": "owner"'))}}
        for name, change in changes.items():
            with self.subTest(change=name):
                with self.assertRaises(RuntimeError):
                    Case(self.case_root(name), "pending-review-after-deadline", change=change).verify(self.root)

    def test_a_replayed_effect_fails_with_its_precise_cause(self):
        for name, part in (("after", "ownerAfter"), ("at-recovery", "ownerAtRecovery")):
            with self.subTest(owner=name), self.assertRaises(RestartCaseFailure) as raised:
                Case(self.case_root("replayed-" + name), "receipt-recorded-paused",
                     change={part: lambda value: value.update(revision="rev-3")}).verify(self.root)
            self.assertEqual("restart.effect_replayed", raised.exception.cause)
        # T033's shape: another case's effect on the shared owner after the idle receipt is never one execution.
        with self.assertRaises(RestartCaseFailure) as raised:
            Case(self.case_root("idle-beyond-receipt"), "idle", change={"ownerAfter": lambda value: value.update(revision="rev-3", size="medium")}).verify(self.root)
        self.assertEqual("restart.effect_replayed", raised.exception.cause)
        with self.assertRaises(RestartCaseFailure) as raised:
            Case(self.case_root("dispatched-applied"), "dispatched-without-receipt",
                 change={"ownerAfter": lambda value: value.update(revision="rev-2", size="small")}).verify(self.root)
        self.assertEqual("restart.effect_replayed", raised.exception.cause)

    def test_a_second_continuation_or_admitted_repeat_delivery_fails(self):
        def twice(trace):
            trace["records"].append(copy.deepcopy(trace["records"][-1]))
        for name, change in (("second-continuation", {"trace": twice}),
                             ("repeat-admitted", {"repeatDelivery": lambda part: part["frames"].insert(0, {"ordinal": 0, "value": {"kind": "started", "id": "late"}})})):
            with self.subTest(change=name), self.assertRaises(RuntimeError):
                Case(self.case_root(name), "receipt-recorded-paused", change=change).verify(self.root)

    def test_restart_window_generation_and_stop_kind_are_proven_from_the_retained_restart(self):
        controls = {
            "started-before-deadline": ("pending-review-after-deadline", {"restart": lambda part: part.update(startedAt=DEADLINE - 1)}, {}, "restart.deadline_window"),
            "stopped-after-deadline": ("pending-review-after-deadline", {"restart": lambda part: part.update(stoppedAt=DEADLINE + 1, startedAt=DEADLINE + 2)}, {}, "restart.deadline_window"),
            "no-restart-between": ("receipt-recorded-paused", {}, {"toGeneration": 3}, "restart.generation"),
            "graceful-exit-claimed-abrupt": ("dispatched-without-receipt", {"restart": lambda part: part["stoppedProcess"].update(exitCode=0)}, {}, "restart.process"),
            "runtime-cache-written": ("receipt-recorded-paused", {"restart": lambda part: part["stoppedProcess"]["runtimeCacheFiles"].update(NUGET_PACKAGES=["x"])}, {}, "restart.process"),
            "wrong-phase": ("receipt-recorded-paused", {"restart": lambda part: part.update(phase=AFTER_DEADLINE)}, {}, "restart.kind"),
            "graceful-lease-held": ("idle", {}, {"ownedElsewhereAttempts": 1}, "restart.lease_not_released"),
            "after-deadline-lease-held": ("pending-review-after-deadline", {}, {"ownedElsewhereAttempts": 2}, "restart.lease_not_released"),
        }
        for name, (key, change, overrides, cause) in controls.items():
            with self.subTest(control=name), self.assertRaises(RestartCaseFailure) as raised:
                Case(self.case_root(name), key, change=change).verify(self.root, **overrides)
            self.assertEqual(cause, raised.exception.cause)

    def test_only_an_abrupt_stop_may_leave_a_lease_to_lapse(self):
        record = Case(self.case_root("abrupt-lapse"), "dispatched-without-receipt").verify(self.root, ownedElsewhereAttempts=2)
        self.assertEqual(2, record["ownedElsewhereAttempts"])
        case = Case(self.case_root("graceful-retained-refusal"), "receipt-recorded-paused")
        observation = case.observation()
        observation["refs"]["ownedElsewhere0"] = observation["refs"]["recoveredSession"]
        with self.assertRaises(RestartCaseFailure) as raised:
            rj.VERIFIERS["receipt-recorded-paused"](self.root, observation, WITNESS, "copilot")
        self.assertEqual("restart.lease_not_released", raised.exception.cause)

    def test_a_dispatch_that_was_not_held_or_a_replayed_uncertain_execution_fails(self):
        controls = {
            "response-received": ({"interruptedExecute": lambda part: part.update(status=200)}, "restart.dispatch_not_held"),
            "write-not-held": ({"rendezvousHeld": lambda part: part.update(status="released")}, "restart.dispatch_not_held"),
            "repeat-executed": ({"repeatExecute": lambda part: part.update(rawBody=json.dumps({"data": {"executed": True, "status": "executed"}}))}, "restart.replayed"),
        }
        for name, (change, cause) in controls.items():
            with self.subTest(control=name), self.assertRaises(RestartCaseFailure) as raised:
                Case(self.case_root(name), "dispatched-without-receipt", change=change).verify(self.root)
            self.assertEqual(cause, raised.exception.cause)

    def test_owner_resolution_refuses_an_unrecorded_conflicting_or_replayed_client_operation(self):
        controls = {
            "statement-not-recorded": ({"resolution": lambda part: part.update(rawBody=json.dumps({"data": {"status": "uncertain"}}))}, "restart.resolution"),
            "repeat-not-idempotent": ({"repeatResolution": lambda part: part.update(rawBody=part["rawBody"].replace("2026-11-02T10:06:00Z", "2026-11-02T10:07:00Z"))}, "restart.resolution"),
            "redispatched": ({"repeatExecute": lambda part: part.update(rawBody=json.dumps({"data": {"executed": False, "clientInvocation": {"invocationId": INVOCATION}}}))}, "restart.replayed"),
            "other-binding": ({"initialStream": lambda part: part["frames"][1]["value"]["payload"]["preparedInvocation"].update(client={"clientId": "x", "generationId": "y"})}, "restart.client_setup"),
            "not-dispatched": ({"execute": lambda part: part.update(status=200)}, "lifecycle.native_status"),
        }
        for name, (change, cause) in controls.items():
            with self.subTest(control=name), self.assertRaises(RuntimeError) as raised:
                rj.verify_owner_resolution(self.root, owner_resolution_observation(self.case_root(name), change=change), WITNESS, "copilot")
            self.assertEqual(cause, raised.exception.cause)
        changed = {"ownerAfter": lambda part: part["draft"]["layout"][0].update(x=480, y=320)}
        with self.assertRaises(RuntimeError):  # A client effect without a receipt is a wrong-cause outcome, never one execution.
            rj.verify_owner_resolution(self.root, owner_resolution_observation(self.case_root("draft-changed"), change=changed), WITNESS, "copilot")

    def test_interrupted_turn_and_second_client_controls_fail_with_their_cause(self):
        controls = {
            "turn-completed-before-stop": ("provider-turn-in-progress", {"interruptedStream": lambda part: part["frames"].append(
                {"ordinal": 2, "value": {"kind": "completed", "id": "event-1"}})}, "restart.turn_not_running"),
            "call-reached-adapter": ("provider-turn-in-progress", {"trace": lambda part: part["records"].append(
                {"kind": "copilot.call", "data": {"commandId": COMMAND}})}, "restart.turn_not_running"),
            "foreign-reached-session": ("second-client", {"foreignSession": lambda part: part.update(status=200)}, "restart.foreign_access"),
            "second-client-saw-other-review": ("second-client", {"proposalsB": lambda part: part.update(rawBody=part["rawBody"].replace('"awaitingApproval"', '"approved"'))},
                                               "restart.second_client"),
            "history-changed": ("idle", {"recoveredSession": lambda part: part.update(rawBody=part["rawBody"].replace('"completed"', '"cancelled"', 1))}, "restart.history_lost"),
            "approved-executed-by-recovery": ("approved-before-dispatch-before-deadline", {"ownerAtRecovery": lambda part: part.update(revision="rev-2", size="small")}, None),
        }
        for name, (key, change, cause) in controls.items():
            with self.subTest(control=name), self.assertRaises(RuntimeError) as raised:
                Case(self.case_root(name), key, change=change).verify(self.root)
            if cause is not None:
                self.assertEqual(cause, raised.exception.cause)

    def test_native_iso_instants_parse_to_epoch_seconds(self):
        self.assertAlmostEqual(rj.instant("2026-11-02T10:00:00Z") + 0.1234567, rj.instant("2026-11-02T10:00:00.1234567+00:00"))
        self.assertEqual(rj.instant("2026-11-02T10:00:00Z"), rj.instant("2026-11-02T12:00:00+02:00"))
        with self.assertRaises(RuntimeError):
            rj.instant("2026-11-02 10:00:00")


def complete_restart_section(ref, source="frozen"):
    """Synthetic shapes of the actual passed restart records for every inventory case and the observed control."""
    rows = []
    for identity in sorted(EXPECTED_CASES):
        provider, key = identity.split("-", 1)
        rows.append({"id": identity, "provider": provider, "adapter": provider, "caseKey": key, "restart": RESTART_CASES[key],
                     "status": "passed", "cause": None, "qualified": False, "nativeEvidenceVerified": True, "source": source,
                     "outcome": copy.deepcopy(EXPECTED_OUTCOMES[key]), "ownerBefore": ref, "ownerAfter": ref, "evidence": [ref],
                     "correlation": {"adapter": provider, "providerTrace": ref, "restartReceipt": ref, "fromGeneration": 1, "toGeneration": 2,
                                     **{name: "id" for name in CORRELATION_KEYS[key]}}})
    return {"status": "passed", "scope": "complete-package-witness-restarts", "source": source, "evidenceCategory": "deterministic-provider-protocol",
            "liveProvider": False, "qualified": False, "independentPackageWitness": False, "expectedCaseIds": sorted(EXPECTED_CASES),
            "controlId": CONTROL_ID, "cases": rows, "restarts": [ref],
            "durableStore": {"feature": DURABLE_STORE_FEATURE, "dependsOn": DURABLE_STORE_DEPENDENCY, "provider": "Sqlite",
                             "database": "/owned/durable-agent-store/agent-records.db", "databaseObserved": True, "restart": ref},
            "control": {"id": CONTROL_ID, "provider": "anthropic", "status": "failed", "expectedFailureObserved": True,
                        "actual": copy.deepcopy(EXPECTED_CONTROL), "ownerBefore": ref, "ownerAfter": ref, "evidence": [ref]}}


class RestartManifestTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        path = self.root / "owner.json"
        path.write_text(json.dumps({"synthetic": "owner"}))
        self.ref = retained(path)

    def test_inventory_is_ten_boundaries_on_each_actual_adapter_plus_one_control(self):
        self.assertEqual(20, len(EXPECTED_CASES))
        self.assertEqual({"copilot", "anthropic"}, {name.split("-", 1)[0] for name in EXPECTED_CASES})
        self.assertEqual({GRACEFUL: 5, ABRUPT: 2, AFTER_DEADLINE: 3}, {phase: len(keys) for phase, keys in PHASE_ORDER.items()})
        section = complete_restart_section(self.ref)
        self.assertEqual([], restart_manifest_gaps(section, "frozen"))
        self.assertEqual(20, len(require_restart_inventory(section, self.root, source="frozen")))

    def test_missing_duplicate_wrong_adapter_wrong_outcome_or_unobserved_control_fails(self):
        def row(section, key="dispatched-without-receipt", provider="anthropic"):
            return next(item for item in section["cases"] if item["id"] == provider + "-" + key)
        changes = {
            "missing": lambda s: s["cases"].remove(row(s)),
            "duplicate": lambda s: s["cases"].append(copy.deepcopy(row(s))),
            "replaced-by-other-adapter": lambda s: s["cases"].__setitem__(s["cases"].index(row(s)), copy.deepcopy(row(s, provider="copilot"))),
            "wrong-adapter": lambda s: row(s).update(adapter="copilot"),
            "replayed-effect": lambda s: row(s)["outcome"].update(executions=1),
            "skipped-settlement": lambda s: row(s)["outcome"].update(settledBy=[]),
            "wrong-restart-kind": lambda s: row(s).update(restart=GRACEFUL),
            "failed-case": lambda s: row(s).update(status="failed", cause="restart.dispatch_not_held"),
            "self-promoted": lambda s: row(s).update(qualified=True),
            "no-restart-correlation": lambda s: row(s)["correlation"].update(toGeneration=1),
            "no-evidence": lambda s: row(s).update(evidence=[]),
            "control-absent": lambda s: s.pop("control"),
            "control-passed": lambda s: s["control"].update(status="passed"),
            "control-wrong-cause": lambda s: s["control"]["actual"].update(diagnostic="agent.session.owned_elsewhere"),
            "store-not-observed": lambda s: s["durableStore"].update(databaseObserved=False),
            "store-other-provider": lambda s: s["durableStore"].update(provider="InMemory"),
            "section-failed": lambda s: s.update(status="failed"),
            "section-promoted": lambda s: s.update(independentPackageWitness=True),
            "foreign-source": lambda s: s.update(source="b" * 40),
        }
        for name, change in changes.items():
            with self.subTest(change=name):
                section = complete_restart_section(self.ref)
                change(section)
                self.assertTrue(restart_manifest_gaps(section, "frozen"))
                with self.assertRaises(RuntimeError):
                    require_restart_inventory(section, self.root, source="frozen")

    def test_changed_or_foreign_evidence_fails_the_file_gate(self):
        section = complete_restart_section(self.ref)
        section["cases"][0]["evidence"] = [{**self.ref, "sha256": "0" * 64}]
        self.assertEqual([], restart_manifest_gaps(section, "frozen"))
        with self.assertRaises(RuntimeError):
            require_restart_inventory(section, self.root, source="frozen")
        with self.assertRaises(RuntimeError):
            require_restart_inventory(complete_restart_section({"path": "/elsewhere/owner.json", "sha256": "0" * 64}), self.root, source="frozen")

    def test_a_complete_inventory_run_passes_its_own_final_gate(self):
        # T033 attempt 2 passed all 20 cases and the control, then failed because the gate saw status 'running'.
        complete = complete_restart_section(self.ref, source=None)
        database = self.root / "agent-records.db"
        database.write_bytes(b"sqlite")
        store = dict(complete["durableStore"], database=str(database))
        run = SimpleNamespace(root=self.root, report={"independentPackageWitness": False}, source=None, save=lambda: None, assert_immutable_inputs=lambda: None)
        runtime = SimpleNamespace(run=run, select_provider=lambda provider: None)

        def phase(_runtime, _host, provider, _phase, keys):
            return [row for row in complete["cases"] if row["provider"] == provider and row["caseKey"] in keys], {"ref": self.ref}

        originals = rj._control, rj._enable_store, rj._run_phase
        rj._control, rj._enable_store, rj._run_phase = lambda *_: complete["control"], lambda *_: store, phase
        try:
            section = rj._run_restart_inventory(runtime, Mock(), "complete-package-witness-restarts")
        finally:
            rj._control, rj._enable_store, rj._run_phase = originals
        self.assertEqual("passed", section["status"])
        self.assertEqual("passed", json.loads((self.root / "restart-recovery-qualification.json").read_text())["status"])

    def test_the_inventory_runs_only_inside_a_running_witness_after_the_autonomy_matrix(self):
        for report in ({"status": "running", "witnessManifest": {}}, {"status": "passed", "witnessManifest": {}, "autonomyLifecycleQualification": {"status": "passed"}},
                       {"status": "running", "autonomyLifecycleQualification": {"status": "passed"}}):
            runtime = SimpleNamespace(run=SimpleNamespace(report=report))
            with self.subTest(report=report), self.assertRaises(RuntimeError):
                rj.qualify_witness_restarts(runtime, Mock())


class RestartOrchestrationTests(unittest.TestCase):
    """Phase order and failure isolation with synthetic case runners; no host is started."""
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.order = []
        saved = []
        self.run = SimpleNamespace(root=self.root, source="a" * 40, report={"status": "running", "witnessManifest": {}, "independentPackageWitness": False,
                                   "autonomyLifecycleQualification": {"status": "passed"}}, save=Mock(), assert_immutable_inputs=Mock())
        def save(output, name, value):
            saved.append(value)
            path = Path(output) / (name + ".json")
            path.write_text(json.dumps(value, default=str))
            return retained(path)
        def output(name):
            path = self.root / (str(len(list(self.root.iterdir()))) + "-" + name)
            path.mkdir()
            return path
        self.runtime = SimpleNamespace(run=self.run, provider="anthropic", output=output, save=save, sign_in=Mock(return_value=(object(), {})),
                                       gate_file=self.root / "gate.json", select_provider=Mock(side_effect=lambda provider: self.order.append("select-" + provider)))
        def restart(reason, *, abrupt=False, while_stopped=None):
            self.order.append(("restart", reason, abrupt))
            waited = while_stopped() if while_stopped else None
            return {"name": "backend", "reason": reason, "kind": "abrupt" if abrupt else "graceful", "fromGeneration": 1, "toGeneration": 2,
                    "stoppedAt": 1.0, "startedAt": 2.0, "whileStopped": waited}
        self.host = SimpleNamespace(restart=Mock(side_effect=restart))

    def runners(self, failing=()):
        def prepare(key):
            def run(runtime, name):
                self.order.append(("prepare", key))
                if key in failing: raise RestartCaseFailure("synthetic", "restart.synthetic")
                state = {"o": {"expiresAt": EXPIRES}}
                if key == "dispatched-without-receipt": state["engage"] = lambda: self.order.append(("engage", key))
                return state
            return run
        def recover(key):
            def run(runtime, state, restart, name):
                self.order.append(("recover", key))
                return {"key": key}
            return run
        def verify(key):
            return lambda root, o, witness, provider: {"id": provider + "-" + key, "caseKey": key, "status": "passed"}
        return ({key: prepare(key) for key in RESTART_CASES}, {key: recover(key) for key in RESTART_CASES}, {key: verify(key) for key in RESTART_CASES})

    def phase(self, phase, failing=(), keys=None):
        preparers, recoverers, verifiers = self.runners(failing)
        from unittest.mock import patch
        with patch.dict(rj.PREPARERS, preparers), patch.dict(rj.RECOVERERS, recoverers), patch.dict(rj.VERIFIERS, verifiers), \
                patch.object(rj, "_arm_rendezvous", return_value=((self.root / "control.json", self.root / "receipt.json"), [])), \
                patch.object(rj.time, "sleep"), patch.object(rj.time, "time", return_value=DEADLINE + 10):
            self.runtime.witness = WITNESS
            return rj._run_phase(self.runtime, self.host, "copilot", phase, keys)

    def test_every_case_with_an_owner_effect_gets_a_restart_of_its_own(self):
        effects = {key for key, outcome in EXPECTED_OUTCOMES.items() if outcome["executions"]}
        self.assertEqual(set(PHASE_ORDER[GRACEFUL]), effects)
        self.assertEqual([(GRACEFUL, (key,)) for key in PHASE_ORDER[GRACEFUL]] + [(ABRUPT, PHASE_ORDER[ABRUPT]), (AFTER_DEADLINE, PHASE_ORDER[AFTER_DEADLINE])],
                         list(RESTART_GROUPS))
        for key in PHASE_ORDER[GRACEFUL]:
            with self.subTest(key=key):
                self.order.clear()
                records, restart = self.phase(GRACEFUL, keys=(key,))
                self.assertEqual([("prepare", key), ("restart", "copilot-graceful-" + key, False), ("recover", key)], self.order)
                self.assertEqual(["copilot-" + key], [record["id"] for record in records])
        with self.assertRaises(RuntimeError):
            self.phase(GRACEFUL, keys=("dispatched-without-receipt",))

    def test_a_graceful_recovery_refuses_a_held_lease_at_once_and_only_an_abrupt_one_waits_for_it_to_lapse(self):
        from unittest.mock import patch
        held = SimpleNamespace(status=409, receipt={"held": True}, headers={"retry-after": "1"},
                               json=lambda: {"error": {"code": "agent.session.owned_elsewhere"}})
        free = SimpleNamespace(status=200, receipt={"free": True})
        for kind, expected in (("graceful", "restart.lease_not_released"), ("abrupt", None)):
            with self.subTest(kind=kind):
                transport = SimpleNamespace(session=Mock(side_effect=[held, free]))
                self.runtime.transport = Mock(return_value=transport)
                state = {"o": {"sessionId": SESSION, "refs": {}}}
                restart = {"ref": {"restart": kind}, "kind": kind, "fromGeneration": 1, "toGeneration": 2}
                with patch.object(rj.time, "sleep"):
                    if expected:
                        with self.assertRaises(RestartCaseFailure) as raised:
                            rj._begin(self.runtime, state, restart, "case")
                        self.assertEqual(expected, raised.exception.cause)
                        self.assertEqual(1, transport.session.call_count)
                        self.assertEqual((1, {"held": True}), (state["o"]["ownedElsewhereAttempts"], state["o"]["refs"]["ownedElsewhere0"]))
                    else:
                        _, o = rj._begin(self.runtime, state, restart, "case")
                        self.assertEqual((1, {"free": True}, {"held": True}), (o["ownedElsewhereAttempts"], o["refs"]["recoveredSession"], o["refs"]["ownedElsewhere0"]))

    def test_each_phase_prepares_in_order_restarts_once_then_recovers_every_prepared_case(self):
        for phase in (GRACEFUL, ABRUPT, AFTER_DEADLINE):
            with self.subTest(phase=phase):
                self.order.clear()
                records, restart = self.phase(phase)
                keys = list(PHASE_ORDER[phase])
                expected = [("prepare", key) for key in keys] + ([("engage", "dispatched-without-receipt")] if phase == ABRUPT else [])
                expected += [("restart", "copilot-" + phase, phase == ABRUPT)] + [("recover", key) for key in keys]
                self.assertEqual(expected, self.order)
                self.assertEqual(["copilot-" + key for key in keys], [record["id"] for record in records])
                self.assertEqual(phase, restart["phase"])
                if phase == ABRUPT:
                    self.assertEqual("release", json.loads(self.runtime.gate_file.read_text())["state"])
                if phase == AFTER_DEADLINE:
                    self.assertEqual(DEADLINE + rj.DEADLINE_MARGIN_SECONDS, restart["whileStopped"]["waitedUntil"])

    def test_a_failed_preparation_keeps_its_cause_and_the_rest_still_restart_and_recover(self):
        records, _ = self.phase(GRACEFUL, failing={"second-client"})
        failed = [record for record in records if record["status"] == "failed"]
        self.assertEqual([("copilot-second-client", "restart.synthetic")], [(record["id"], record["cause"]) for record in failed])
        self.assertEqual(5, len(records))
        self.assertNotIn(("recover", "second-client"), self.order)
        self.assertEqual(1, sum(isinstance(item, tuple) and item[0] == "restart" for item in self.order))

    def test_the_inventory_runs_the_control_then_the_store_then_seven_restart_groups_per_actual_adapter(self):
        from unittest.mock import patch
        def control(runtime, host):
            self.order.append("control")
            return {"id": CONTROL_ID}
        def store(runtime, host):
            self.order.append("store")
            return {"database": str(self.root / "missing.db")}
        def phase(runtime, host, provider, kind, keys):
            self.order.append((provider, kind) + ((keys[0],) if kind == GRACEFUL else ()))
            return [], {"ref": {"synthetic": kind}}
        with patch.object(rj, "_control", side_effect=control), patch.object(rj, "_enable_store", side_effect=store), \
                patch.object(rj, "_run_phase", side_effect=phase), patch.object(rj, "private_json"):
            with self.assertRaises(RuntimeError):  # Synthetic phases record no cases, so the gate refuses.
                rj.qualify_witness_restarts(self.runtime, self.host)
        groups = lambda provider: [(provider, GRACEFUL, key) for key in PHASE_ORDER[GRACEFUL]] + [(provider, ABRUPT), (provider, AFTER_DEADLINE)]
        self.assertEqual(["control", "store", "select-copilot", *groups("copilot"), "select-anthropic", *groups("anthropic")], self.order)
        section = self.run.report[rj.RESTART_QUALIFICATION]
        self.assertEqual("failed", section["status"])
        self.assertFalse(self.run.report["independentPackageWitness"])


class DurableStoreConfigurationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        root = Path(self.temporary.name).resolve()
        published = root / "backend-published"
        published.mkdir()
        (published / "shells.json").write_text(json.dumps({"CShells": {"Shells": {"Authoring": {"Features": backend_features(published)}}}}))
        self.store = root / "durable-agent-store"
        self.store.mkdir(mode=0o700)
        self.run = SimpleNamespace(root=root, profiles={"backend": {"published": published}})

    def test_enables_the_packaged_store_on_a_new_owned_sqlite_file_without_returning_the_key(self):
        key = "k" * 48
        selected = enable_durable_agent_store(self.run, self.store / "agent-records.db", key)
        path = self.run.profiles["backend"]["published"] / "shells.json"
        features = json.loads(path.read_text())["CShells"]["Shells"]["Authoring"]["Features"]
        self.assertEqual({"Provider": "Sqlite", "ConnectionString": "Data Source=" + str(self.store / "agent-records.db"), "LeaseSeconds": 15},
                         features[DURABLE_STORE_FEATURE])
        self.assertEqual({"EncryptionKey": key}, features[DURABLE_STORE_DEPENDENCY])
        self.assertIn("FoundationAgentAbstractions", features)
        self.assertIn(DURABLE_EFFECTS_FEATURE, features)
        self.assertNotIn(COMMON_EFFECTS_FEATURE, features)
        self.assertEqual(DURABLE_EFFECTS_FEATURE, selected["effectsFeature"])
        self.assertNotIn(key, json.dumps(selected))
        self.assertEqual(0o600, stat.S_IMODE(path.stat().st_mode))

    def test_refuses_a_foreign_existing_or_repeated_store_and_a_weak_key(self):
        outside = tempfile.TemporaryDirectory()
        self.addCleanup(outside.cleanup)
        existing = self.store / "existing.db"
        existing.write_bytes(b"")
        for database, key in ((Path(outside.name) / "agent.db", "k" * 48), (existing, "k" * 48), (Path("relative.db"), "k" * 48),
                              (self.store / "agent.db", "short")):
            with self.subTest(database=str(database)), self.assertRaises(RuntimeError):
                enable_durable_agent_store(self.run, database, key)
        enable_durable_agent_store(self.run, self.store / "agent.db", "k" * 48)
        with self.assertRaises(RuntimeError):
            enable_durable_agent_store(self.run, self.store / "again.db", "k" * 48)

    def test_missing_or_already_durable_effects_selection_is_refused_without_rewriting_configuration(self):
        path = self.run.profiles["backend"]["published"] / "shells.json"
        original = json.loads(path.read_text())
        key = secrets.token_urlsafe(48)
        for common, durable in ((False, False), (True, True)):
            with self.subTest(common=common, durable=durable):
                document = copy.deepcopy(original)
                features = document["CShells"]["Shells"]["Authoring"]["Features"]
                if not common:
                    features.pop(COMMON_EFFECTS_FEATURE)
                if durable:
                    features[DURABLE_EFFECTS_FEATURE] = {}
                path.write_text(json.dumps(document))
                before = path.read_bytes()
                with self.assertRaises(RuntimeError):
                    enable_durable_agent_store(self.run, self.store / "agent-records.db", key)
                self.assertEqual(before, path.read_bytes())
                self.assertNotIn("durableAgentStore", self.run.profiles["backend"])


if __name__ == "__main__":
    unittest.main()
