"""Synthetic cause/integrity controls for the T015 lifecycle matrix; no native/provider qualification is claimed."""
import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from autonomy_journeys import (EXPECTED_CASES, EVIDENCE_CATEGORY, LIFECYCLE_CASES, MATRIX_QUALIFICATION, MODE_CASES,
                               ModeCaseFailure, qualify_autonomy_matrix, require_lifecycle_matrix)
from case_records import retained
from journey_runtime import BASE_GRANTS
from lifecycle_journeys import (BROWSER_CASES, CORRELATION_KEYS, EXPECTED_OUTCOMES, VERIFIERS, LifecycleCaseFailure,
                                require_lifecycle_record, verify_browser_case)
from test_autonomy_journeys import AutonomyEvidenceTests

WITNESS = json.loads((Path(__file__).parent / "witness.json").read_text())
SESSION, COMMAND, TURN, PROPOSAL, INVOCATION = "session-1", "command-1", "turn-1", "proposal-1", "invocation-1"
CREATED = {"autonomyMode": "manual", "maxAutonomyMode": "full-auto", "allowedAutonomyModes": ["manual", "auto-read-only", "full-auto"], "revision": 1}
BACKEND = "http://127.0.0.1:52001"


def http(method, path, status, body, request=None):
    return {"origin": BACKEND, "method": method, "path": path, "requestBody": request, "status": status, "headers": {}, "rawBody": json.dumps(body)}


def sse(values, termination="client-closed"):
    return {"origin": BACKEND, "method": "GET", "path": "/_elsa/agent/sessions/" + SESSION + "/stream", "status": 200, "termination": termination,
            "errorType": None, "frames": [{"ordinal": index + 1, "value": dict(value, id=value.get("id", "event-" + str(index)))} for index, value in enumerate(values)]}


def owner(revision, size):
    return {"widgetId": WITNESS["widgetId"], "revision": revision, "size": size, "hostId": WITNESS["hostId"]}


class Native:
    """Builds the retained shape of one Python lifecycle case; mutations change parts before writing."""
    def __init__(self, root, key, provider, mutate=None):
        self.root, self.key, self.provider = Path(root), key, provider
        self.call = COMMAND if provider == "anthropic" else "native-call-1"
        self.parts = {}
        self.build()
        for name, change in (mutate or {}).items():
            change(self.parts[name])  # In place; a mutation's return value is ignored.

    def result(self, status=None):
        base = {"toolCallId": self.call, "toolName": WITNESS["sizeTool"], "content": "Synthetic outcome."}
        if status:
            return {**base, "succeeded": False, "payload": {"typeId": "agent.operation-outcome", "schemaVersion": "1",
                    "data": {"status": status, "invocationId": INVOCATION}}, "receipt": None}
        payload = {"typeId": WITNESS["sizeResultType"], "schemaVersion": "1", "data": {"size": "small", "revision": "rev-2"}}
        return {**base, "succeeded": True, "payload": payload, "receipt": {"scope": "serverPersisted", "invocationId": INVOCATION,
                "before": {"revision": "rev-1"}, "after": {"revision": "rev-2"}, "evidence": payload}}

    def trace(self, continuation, cancel=False):
        tools = [{"name": WITNESS["sizeTool"]}, {"name": WITNESS["readTool"]}]
        p = self.provider
        rows = [{"kind": p + ".request", "data": {"commandId": COMMAND, "sessionId": SESSION, "tools": tools, "messages": [{"role": "user", "contents": []}]}}]
        if cancel:
            return rows + [{"kind": "protocol.gate.waiting", "data": {"provider": p, "commandId": COMMAND, "epoch": "epoch-1"}},
                           {"kind": "protocol.gate.cancelled", "data": {"provider": p, "commandId": COMMAND, "epoch": "epoch-1"}}]
        rows.append({"kind": p + ".call", "data": {"commandId": COMMAND, "toolName": WITNESS["sizeTool"], "arguments": {"size": "small"}}})
        if p == "copilot":
            rows += [{"kind": "copilot.return", "data": {"commandId": COMMAND, "result": {"status": "awaiting-approval", "executed": False, "proposalId": PROPOSAL}}},
                     {"kind": "copilot.request", "data": {"commandId": COMMAND, "sessionId": SESSION, "tools": []}},
                     {"kind": "copilot.continuation", "data": {"commandId": COMMAND, "sessionId": SESSION, "results": [continuation]}}]
        else:
            item = {"kind": "functionResult", "callId": COMMAND, "result": json.dumps(continuation)}
            rows += [{"kind": "anthropic.request", "data": {"commandId": COMMAND, "tools": [], "messages": [
                         {"role": "user", "contents": []}, {"role": "assistant", "contents": [{"kind": "functionCall", "callId": COMMAND}]},
                         {"role": "tool", "contents": [item]}]}},
                     {"kind": "anthropic.continuation", "data": {"commandId": COMMAND, "results": [item]}}]
        return rows

    def session(self, messages, policy=None, user_status="completed"):
        user = {"id": "message-1", "role": "user", "status": user_status, "acceptedPolicyRevision": 1}
        return http("GET", "/_elsa/agent/sessions/" + SESSION, 200, {"data": {"sessionId": SESSION, "policy": policy or CREATED,
                    "messages": [user, *[{"role": "tool", "status": "completed", "acceptedPolicyRevision": 1, "content": json.dumps(value)} for value in messages]]}})

    def build(self):
        k, parts = self.key, self.parts
        proposal_path = lambda action: "/_elsa/agent/proposals/" + PROPOSAL + "/" + action
        parts.update(
            create=http("POST", "/_elsa/agent/sessions", 200, {"data": {"sessionId": SESSION, "policy": CREATED}, "error": None}),
            command={"commandId": COMMAND, "toolName": WITNESS["sizeTool"], "arguments": {"size": "small"}},
            message=http("POST", "/_elsa/agent/sessions/" + SESSION + "/messages", 200,
                         {"data": {"message": {"id": "message-1", "sessionId": SESSION, "role": "user", "requestId": "request-1",
                                                "acceptedPolicy": {"revision": 1, "policy": {"autonomyMode": "manual"}}}}},
                         {"requestId": "request-1", "requestedCapabilities": [WITNESS["sizeCapability"]]}),
            ownerBefore=owner("rev-1", "medium"))
        proposal = {"id": PROPOSAL, "sessionId": SESSION, "status": "awaitingApproval", "requiresApproval": True, "baseRevision": "rev-1",
                    "preparedInvocation": {"toolName": WITNESS["sizeTool"], "sessionId": SESSION, "turnId": TURN, "arguments": {"size": "small"},
                        "requiresApproval": True, "invocationId": INVOCATION, "toolCallId": self.call,
                        "target": {"resourceId": WITNESS["widgetId"], "revision": "rev-1"}}}
        continuation = sse([{"kind": "started", "payload": {"turnId": TURN}}, {"kind": "messageDelta"}, {"kind": "completed"}])
        if k == "cancel-before-effect":
            parts.update(initialStream=sse([{"kind": "started", "payload": {"turnId": TURN}}, {"kind": "contextAdmitted"},
                                            {"kind": "turnCancelled", "payload": {"turnId": TURN}}]),
                         cancel=http("POST", "/_elsa/agent/sessions/" + SESSION + "/turns/" + TURN + "/cancel", 200, {"data": {"cancelled": True, "turnId": TURN}}),
                         gate={"commandId": COMMAND, "epoch": "epoch-1", "state": "hold", "timeoutMilliseconds": 60000},
                         trace={"records": self.trace(None, cancel=True), "incompleteTailBytes": 0},
                         session=self.session([], user_status="cancelled"), ownerAfter=owner("rev-1", "medium"))
            return
        parts["initialStream"] = sse([{"kind": "started", "payload": {"turnId": TURN}}, {"kind": "proposalCreated", "payload": proposal},
                                      {"kind": "toolApprovalRequested", "proposalId": PROPOSAL}])
        parts["continuation"] = continuation
        approve = http("POST", proposal_path("approve"), 200, {"data": {"status": "approved"}})
        executed = {"proposalId": PROPOSAL, "executed": True, "status": "executed", "toolResult": self.result()}
        if k in ("denied-result-continuation", "stale-target-continuation", "authority-refusal-continuation"):
            status = "denied" if k.startswith("denied") else "refused"
            refusal = self.result(status)
            parts.update(trace={"records": self.trace(refusal), "incompleteTailBytes": 0}, session=self.session([refusal]),
                         ownerAfter=owner("rev-1", "medium"))
            if k.startswith("denied"):
                parts.update(deny=http("POST", proposal_path("deny"), 200, {"data": {"status": "denied"}}),
                             closedExecute=http("POST", proposal_path("execute"), 409, {"data": {"status": "denied", "executed": False},
                                                                                       "error": {"code": "agent.proposal.closed"}}))
            elif k.startswith("stale"):
                parts.update(approve=approve, independentEdit={"edited": True}, ownerBeforeExecution=owner("rev-9", "medium"),
                             execute=http("POST", proposal_path("execute"), 409, {"data": None, "error": {"code": "acme.home.stale"}}),
                             ownerAfter=owner("rev-9", "medium"))
            else:
                parts.update(approve=approve, authorityBody={"directPermissions": [key for key in BASE_GRANTS if key != "studio/preferences:write"]},
                             execute=http("POST", proposal_path("execute"), 403, {"data": None, "error": {"code": "agent.proposal.forbidden"}}),
                             ownerWithheld=owner("rev-1", "medium"), restored={"restored": True})
        elif k == "duplicate-effect-delivery":
            ok = http("POST", proposal_path("execute"), 200, {"data": executed, "error": None})
            parts.update(approve=approve, execute=ok, repeatExecute=copy.deepcopy(ok), lateExecute=copy.deepcopy(ok),
                         ownerAfterExecute=owner("rev-2", "small"), ownerAfter=owner("rev-2", "small"),
                         repeatDelivery=sse([{"kind": "contextAdmitted"}, {"kind": "error", "error": {"code": "agent.message.no_pending"}}]),
                         trace={"records": self.trace(self.result()), "incompleteTailBytes": 0}, session=self.session([self.result()]))
        else:
            path, body = "/_elsa/agent/sessions/" + SESSION + "/autonomy", {"autonomyMode": "full-auto", "expectedRevision": 1}
            busy = http("PUT", path, 409, {"data": None, "error": {"code": "agent.session.busy"}}, body)
            acknowledged = {**CREATED, "autonomyMode": "full-auto", "revision": 2}
            for name in ("busyReview", "busyApproved", "busyContinuation"):
                parts[name], parts[name + "Detail"] = copy.deepcopy(busy), self.session([])
            parts.update(approve=approve, execute=http("POST", proposal_path("execute"), 200, {"data": executed, "error": None}),
                         settled=self.session([self.result()]), acknowledge=http("PUT", path, 200, {"data": acknowledged, "error": None}, body),
                         session=self.session([self.result()], acknowledged), ownerAfter=owner("rev-2", "small"),
                         trace={"records": self.trace(self.result()), "incompleteTailBytes": 0})

    def observation(self):
        refs = {}
        for name, value in self.parts.items():
            if name == "authorityBody": continue
            path = self.root / (name + ".json")
            path.write_text(json.dumps(value))
            refs[name] = retained(path)
        if "authorityBody" in self.parts:
            body = self.root / "authority.body"
            body.write_text(json.dumps(self.parts["authorityBody"]))
            path = self.root / "authority.json"
            path.write_text(json.dumps({"status": 200, "body": retained(body)}))
            refs["authority"] = retained(path)
        return {"provider": self.provider, "sessionId": SESSION, "commandId": COMMAND, "targetSize": "small", "autonomy": "manual",
                "createdRevision": 1, "messageId": "message-1", "traceOffset": 0, "refs": refs}


def rewrite_http(change):
    def mutate(record):
        value = json.loads(record["rawBody"]); result = change(value)
        record["rawBody"] = json.dumps(value if result is None else result)
    return mutate


class LifecycleVerifierTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.sequence = 0

    def tearDown(self):
        self.temporary.cleanup()

    def verify(self, key, provider="copilot", mutate=None):
        self.sequence += 1
        root = Path(self.temporary.name) / str(self.sequence); root.mkdir()
        observation = Native(root, key, provider, mutate).observation()
        return VERIFIERS[key](Path(self.temporary.name), observation, WITNESS, provider)

    def cause(self, key, mutate, provider="copilot"):
        with self.assertRaises(LifecycleCaseFailure) as raised:
            self.verify(key, provider, mutate)
        return raised.exception.cause

    def test_every_python_case_passes_once_per_actual_adapter_with_its_expected_outcome(self):
        for key in VERIFIERS:
            for provider in ("copilot", "anthropic"):
                with self.subTest(key=key, provider=provider):
                    record = self.verify(key, provider)
                    self.assertEqual((provider + "-" + key, provider, EXPECTED_OUTCOMES[key]), (record["id"], record["adapter"], record["outcome"]))
                    self.assertTrue(all(record["correlation"][name] for name in CORRELATION_KEYS[key]))
                    self.assertFalse(record["qualified"])

    def test_refusals_require_their_exact_cause_continuation_and_zero_effect(self):
        effect = lambda value: value.update(size="small", revision="rev-2")
        cases = [
            ("denied-result-continuation", {"closedExecute": rewrite_http(lambda v: v["error"].update(code="agent.proposal.invalid_state"))}, "lifecycle.refusal_cause"),
            ("denied-result-continuation", {"ownerAfter": effect}, "lifecycle.effect_observed"),
            ("denied-result-continuation", {"continuation": lambda r: r["frames"].pop()}, "lifecycle.continuation_missing"),
            ("denied-result-continuation", {"continuation": lambda r: r["frames"][0]["value"]["payload"].update(turnId="another-turn")}, "lifecycle.continuation_missing"),
            ("stale-target-continuation", {"execute": rewrite_http(lambda v: v["error"].update(code="agent.proposal.approval_required"))}, "lifecycle.refusal_cause"),
            ("stale-target-continuation", {"ownerBeforeExecution": lambda v: v.update(revision="rev-1")}, "lifecycle.refusal_cause"),
            ("authority-refusal-continuation", {"authorityBody": lambda v: v.update(directPermissions=["agent:use"])}, "lifecycle.authority"),
            ("authority-refusal-continuation", {"execute": lambda r: r.update(status=409)}, "lifecycle.native_status"),
            ("authority-refusal-continuation", {"ownerWithheld": lambda v: v.update(size="small")}, "lifecycle.effect_observed"),
        ]
        for key, mutate, cause in cases:
            with self.subTest(key=key, cause=cause):
                self.assertEqual(cause, self.cause(key, mutate))
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                self.assertEqual("lifecycle.continuation_result", self.cause("denied-result-continuation",
                                 {"session": rewrite_http(lambda v: v["data"]["messages"][1].update(content=json.dumps({"toolCallId": "x"}))),
                                  "trace": lambda t: self.refused_as(t, provider)}, provider))

    def refused_as(self, trace, provider):
        row = trace["records"][-1]["data"]["results"][0]
        if provider == "copilot":
            row["payload"]["data"]["status"] = "refused"
        else:
            value = json.loads(row["result"]); value["payload"]["data"]["status"] = "refused"
            row["result"] = json.dumps(value)
            trace["records"][-2]["data"]["messages"][-1]["contents"][0]["result"] = row["result"]

    def test_provider_correlation_refuses_foreign_duplicate_or_missing_adapter_evidence(self):
        mutations = {
            "foreign-session": ("copilot", lambda t: t["records"][0]["data"].update(sessionId="another-session")),
            "duplicate-continuation": ("copilot", lambda t: t["records"].append(copy.deepcopy(t["records"][-1]))),
            "foreign-command": ("anthropic", lambda t: t["records"].insert(0, {"kind": "anthropic.call", "data": {"commandId": "other"}})),
            "other-adapter": ("anthropic", lambda t: t["records"].append({"kind": "copilot.request", "data": {"commandId": COMMAND}})),
            "no-continuation": ("anthropic", lambda t: t["records"].pop()),
            "wrong-call-id": ("anthropic", lambda t: t["records"][-1]["data"]["results"][0].update(callId="other")),
        }
        for name, (provider, mutate) in mutations.items():
            with self.subTest(name=name):
                self.assertEqual("lifecycle.provider_correlation", self.cause("denied-result-continuation", {"trace": mutate}, provider))

    def test_opening_review_and_session_evidence_are_mandatory(self):
        cases = [
            ({"message": rewrite_http(lambda v: v["data"]["message"]["acceptedPolicy"].update(revision=7))}, "lifecycle.message"),
            ({"create": rewrite_http(lambda v: v["data"]["policy"].update(autonomyMode="full-auto"))}, "lifecycle.session_policy"),
            ({"initialStream": lambda r: r["frames"].pop(1)}, "lifecycle.review_missing"),
            ({"initialStream": lambda r: r["frames"].insert(2, {"ordinal": 9, "value": {"id": "x", "kind": "contributedResult"}})}, "lifecycle.review_bypassed"),
            ({"command": lambda v: v["arguments"].update(size="medium")}, "lifecycle.command"),
            ({"session": rewrite_http(lambda v: v["data"]["messages"].append(copy.deepcopy(v["data"]["messages"][1])))}, "lifecycle.stored_result"),
        ]
        for mutate, cause in cases:
            with self.subTest(cause=cause):
                self.assertEqual(cause, self.cause("denied-result-continuation", mutate))

    def test_cancel_requires_the_gate_no_call_and_a_cancelled_prompt(self):
        call = {"kind": "copilot.call", "data": {"commandId": COMMAND, "toolName": WITNESS["sizeTool"], "arguments": {"size": "small"}}}
        self.assertEqual("lifecycle.provider_correlation", self.cause("cancel-before-effect", {"trace": lambda t: t["records"].insert(1, call)}))
        self.assertEqual("lifecycle.gate", self.cause("cancel-before-effect", {"trace": lambda t: t["records"].pop()}))
        self.assertEqual("lifecycle.gate", self.cause("cancel-before-effect", {"gate": lambda g: g.update(epoch="other")}))
        self.assertEqual("lifecycle.cancel", self.cause("cancel-before-effect", {"session": rewrite_http(lambda v: v["data"]["messages"][0].update(status="completed"))}))
        self.assertEqual("lifecycle.cancel", self.cause("cancel-before-effect", {"initialStream": lambda r: r["frames"][-1]["value"].update(kind="completed")}))
        self.assertEqual("lifecycle.effect_observed", self.cause("cancel-before-effect", {"ownerAfter": lambda v: v.update(size="small")}))

    def test_duplicate_delivery_keeps_one_effect_and_admits_no_second_continuation(self):
        self.assertEqual("lifecycle.repeat_delivery", self.cause("duplicate-effect-delivery", {"repeatDelivery": lambda r: r["frames"].insert(
            0, {"ordinal": 0, "value": {"id": "again", "kind": "started", "payload": {"turnId": TURN}}})}))
        self.assertEqual("lifecycle.duplicate_effect", self.cause("duplicate-effect-delivery", {"lateExecute": rewrite_http(
            lambda v: v["data"]["toolResult"]["receipt"]["after"].update(revision="rev-3"))}))
        self.assertEqual("lifecycle.duplicate_effect", self.cause("duplicate-effect-delivery", {"ownerAfter": lambda v: v.update(revision="rev-3")}))
        self.assertEqual("lifecycle.native_status", self.cause("duplicate-effect-delivery", {"repeatExecute": lambda r: r.update(status=409)}))
        self.assertEqual("lifecycle.continuation_result", self.cause("duplicate-effect-delivery", {"trace": lambda t: t["records"][-1]["data"]["results"][0].update(content="another")}))

    def test_busy_requires_three_real_refusals_then_one_acknowledged_settled_update(self):
        self.assertEqual("lifecycle.native_status", self.cause("busy-policy-lifecycle", {"busyApproved": lambda r: r.update(status=200)}))
        self.assertEqual("lifecycle.busy_missing", self.cause("busy-policy-lifecycle", {"busyContinuation": rewrite_http(lambda v: v["error"].update(code="agent.session.policy_stale"))}))
        self.assertEqual("lifecycle.busy_missing", self.cause("busy-policy-lifecycle", {"busyReview": lambda r: r.update(requestBody={"autonomyMode": "full-auto", "expectedRevision": 0})}))
        self.assertEqual("lifecycle.policy_relabelled", self.cause("busy-policy-lifecycle", {"busyReviewDetail": rewrite_http(lambda v: v["data"]["policy"].update(revision=2))}))
        self.assertEqual("lifecycle.busy_not_released", self.cause("busy-policy-lifecycle", {"settled": rewrite_http(lambda v: v["data"]["messages"][0].update(status="streaming"))}))
        self.assertEqual("lifecycle.busy_not_released", self.cause("busy-policy-lifecycle", {"acknowledge": rewrite_http(lambda v: v["data"].update(revision=3))}))
        self.assertEqual("lifecycle.policy_relabelled", self.cause("busy-policy-lifecycle", {"session": rewrite_http(lambda v: v["data"]["messages"][0].update(acceptedPolicyRevision=2))}))

    def test_changed_retained_evidence_is_refused(self):
        root = Path(self.temporary.name) / "changed"; root.mkdir()
        observation = Native(root, "denied-result-continuation", "copilot").observation()
        Path(observation["refs"]["deny"]["path"]).write_text("{}")
        with self.assertRaises(RuntimeError): VERIFIERS["denied-result-continuation"](Path(self.temporary.name), observation, WITNESS, "copilot")


class BrowserLifecycleTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.sequence = 0

    def tearDown(self):
        self.temporary.cleanup()

    def fixture(self, key, provider="copilot", change=None):
        self.sequence += 1
        output = self.root / str(self.sequence); output.mkdir()
        files = {}
        tool = WITNESS["writeTool"] if key == "visibility-review-control" else "workflow_set_designer_position"
        mode = BROWSER_CASES[key][2]
        policy = {**CREATED, "autonomyMode": mode or "manual"}
        proposals = [{"id": "proposal-" + str(index), "requiresApproval": True, "preparedInvocation": {"toolName": tool, "requiresApproval": True,
                      "invocationId": "invocation-" + str(index), "toolCallId": "call-" + str(index)}} for index in range(2 if key == "workflow-review-control" else 1)]
        frames = []
        for proposal in proposals:
            frames += [{"kind": "started", "payload": {"turnId": "turn-" + proposal["id"]}}, {"kind": "proposalCreated", "payload": proposal},
                       {"kind": "toolApprovalRequested", "proposalId": proposal["id"]}]
        frames.append({"kind": "completed"})
        body = "".join("data: " + json.dumps(dict(value, id="event-" + str(index))) + "\n\n" for index, value in enumerate(frames))
        files["agent-streams"] = [AutonomyEvidenceTests.stream(body)]
        files["autonomy-create-session"] = {"status": 200, "request": {"autonomyMode": mode or "manual"}, "response": {"data": {"sessionId": SESSION, "policy": policy}}}
        accepted = {"status": 200, "response": {"data": {"message": {"sessionId": SESSION, "acceptedPolicy": {"revision": 1,
                    "policy": {"autonomyMode": "fullAuto" if mode else "manual"}}}}}}
        base = BACKEND + "/_elsa/agent"
        create = {"url": base + "/sessions", "method": "POST", "body": {}}
        message = {"url": base + "/sessions/" + SESSION + "/messages", "method": "POST", "body": {}}
        action = lambda name, index=0: {"url": base + "/proposals/proposal-" + str(index) + "/" + name, "method": "POST", "body": {}}
        ack = lambda: {"url": base + "/sessions/" + SESSION + "/client-invocations/invocation-0/acknowledgements", "method": "POST", "body": {"outcome": "applied"}}
        own = lambda value: {"status": 200, "url": "/owner", "value": value}
        receipt = lambda scope, before="rev-1", after="rev-2": {"scope": scope, "before": {"revision": before}, "after": {"revision": after}}
        results = []
        if key == "visibility-review-control":
            result = {"toolCallId": "call-0", "toolName": tool, "succeeded": True, "receipt": receipt("serverPersisted")}
            results.append(result)
            files.update({"resource-message": accepted, "owner-before": own({"visible": True, "revision": "rev-1"}),
                          "owner-at-review": own({"visible": True, "revision": "rev-1"}), "owner-after-approval": own({"visible": True, "revision": "rev-1"}),
                          "owner-after": own({"visible": False, "revision": "rev-2"}),
                          "resource-execute": {"status": 200, "response": {"data": {"proposalId": "proposal-0", "executed": True, "status": "executed", "toolResult": result}}},
                          "agent-requests": [create, message, message, action("approve"), action("execute")]})
            observation = {"status": "passed", "journey": "resource", "observations": {}}
        else:
            draft, saved = {"layout": [1]}, {"layout": [2]}
            work = lambda index: {"toolCallId": "call-" + str(index), "toolName": tool, "succeeded": True, "receipt": receipt("clientWorkingState")}
            files.update({"owner-before": own({"draft": draft}), "owner-after-local-apply": own({"draft": draft}),
                          "workflow-execute": {"status": 202, "response": {"data": {"executed": False, "status": "awaiting-client"}}}})
            if key == "workflow-review-control":
                results += [work(0), work(1)]
                files.update({"workflow-message": accepted, "workflow-review": {"status": "awaiting-approval"}, "workflow-save-review": {"status": "awaiting-approval"},
                              "workflow-ack": {"status": 200, "response": {"data": {"executed": True, "toolResult": results[0]}}},
                              "workflow-save-ack": {"status": 200, "response": {"data": {"executed": True, "toolResult": results[1]}}},
                              "owner-after-undo": own({"draft": draft}), "owner-after": own({"draft": saved}),
                              "owning-write-attempts": [{"method": "PUT"}], "explicit-save": {"status": 200}, "saved-draft-validations": own({"errors": []}),
                              "publish-attempts": [], "agent-requests": [create, message, action("approve"), action("execute"), ack(),
                                                                         message, action("approve", 1), action("execute", 1), ack()]})
                observation = {"status": "passed", "journey": "workflow", "observations": {name: True for name in (
                    "workingStateChanged", "noImplicitSave", "wholeUndo", "validated", "explicitSaveReadback")}}
            else:
                result = work(0); results.append(result)
                envelope = {"data": {"executed": True, "status": "executed", "toolResult": result}, "error": None}
                files.update({"lost-confirmation-accepted-http": {"status": 200, "rawBody": json.dumps(envelope)},
                              "lost-confirmation-original-ack": {"request": {"outcome": "applied"}},
                              "confirmation-retry": {"status": 200, "request": {"outcome": "applied"}, "response": envelope},
                              "lost-confirmation-uncertain-ui": {"status": "uncertain"}, "owner-after": own({"draft": draft}),
                              "browser-diagnostics": {"failedRequests": [{"url": ack()["url"], "errorText": "net::ERR_FAILED"}]},
                              "agent-requests": [create, message, action("approve"), action("execute"), ack(), ack()]})
                observation = {"status": "failed", "journey": "lost-confirmation", "expectedFailureObserved": True,
                               "actual": {"stage": "client-confirmation-delivery", "diagnostic": "net::ERR_FAILED"},
                               "observations": {name: True for name in ("responseLost", "uncertain", "sameAcknowledgement", "sameInvocation",
                                                                        "retainedResult", "wholeUndo", "noReplay")}}
        files["lifecycle-session-after"] = own({"data": {"sessionId": SESSION, "policy": copy.deepcopy(policy), "messages": [
            {"role": "tool", "content": json.dumps(value)} for value in results]}})
        if change: change(files, observation)
        for name, value in files.items(): (output / (name + ".json")).write_text(json.dumps(value))
        observation.update(provider=provider, lifecycle={"caseKey": key, "sessionId": SESSION, "createdPolicy": policy},
                           ownerBefore=retained(output / "owner-before.json"), ownerAfter=retained(output / "owner-after.json"),
                           evidence=[retained(output / "agent-requests.json")])
        (output / "trace.json").write_text("{}"); (output / "adapter-correlation.json").write_text("{}")
        correlated = {"trace": retained(output / "trace.json"), "receipt": retained(output / "adapter-correlation.json")}
        return output, observation, correlated

    def verify(self, key, provider="copilot", change=None):
        output, observation, correlated = self.fixture(key, provider, change)
        return verify_browser_case(self.root, output, observation, WITNESS, provider, key, BACKEND, correlated)

    def cause(self, key, change):
        with self.assertRaises(LifecycleCaseFailure) as raised: self.verify(key, change=change)
        return raised.exception.cause

    def test_every_browser_case_passes_once_per_actual_adapter(self):
        for key in BROWSER_CASES:
            for provider in ("copilot", "anthropic"):
                with self.subTest(key=key, provider=provider):
                    record = self.verify(key, provider)
                    self.assertEqual((provider + "-" + key, EXPECTED_OUTCOMES[key]), (record["id"], record["outcome"]))

    def test_full_auto_review_controls_cannot_bypass_review_or_policy(self):
        self.assertEqual("lifecycle.session_policy", self.cause("visibility-review-control",
                         lambda f, o: f["autonomy-create-session"]["request"].update(autonomyMode="manual")))
        self.assertEqual("lifecycle.message", self.cause("visibility-review-control",
                         lambda f, o: f["resource-message"]["response"]["data"]["message"]["acceptedPolicy"]["policy"].update(autonomyMode="manual")))
        self.assertEqual("lifecycle.review_bypassed", self.cause("visibility-review-control",
                         lambda f, o: f["owner-at-review"]["value"].update(visible=False)))
        self.assertEqual("lifecycle.native_requests", self.cause("visibility-review-control",
                         lambda f, o: f["agent-requests"].append({"url": BACKEND + "/_elsa/agent/sessions/" + SESSION + "/autonomy", "method": "PUT"})))
        self.assertEqual("lifecycle.duplicate_effect", self.cause("visibility-review-control",
                         lambda f, o: f["agent-requests"].append(f["agent-requests"][-1])))
        self.assertEqual("lifecycle.policy_relabelled", self.cause("workflow-review-control",
                         lambda f, o: f["lifecycle-session-after"]["value"]["data"]["policy"].update(revision=2)))

    def test_workflow_boundaries_and_publish_are_mandatory(self):
        for change in (lambda f, o: f.update({"publish-attempts": [{"method": "POST"}]}),
                       lambda f, o: f["owning-write-attempts"].append({"method": "PUT"}),
                       lambda f, o: f["owner-after-local-apply"]["value"].update(draft={"layout": [9]}),
                       lambda f, o: o["observations"].update(wholeUndo=False)):
            with self.subTest():
                self.assertEqual("lifecycle.workflow_boundary", self.cause("workflow-review-control", change))
        self.assertEqual("lifecycle.review_bypassed", self.cause("workflow-review-control", lambda f, o: f.update({"workflow-save-review": {"status": "approved"}})))

    def test_interruption_requires_lost_delivery_late_original_receipt_and_no_replay(self):
        self.assertEqual("lifecycle.confirmation", self.cause("interrupted-after-effect", lambda f, o: o["actual"].update(diagnostic="net::ERR_ABORTED")))
        self.assertEqual("lifecycle.confirmation", self.cause("interrupted-after-effect",
                         lambda f, o: f["confirmation-retry"].update(request={"outcome": "uncertain"})))
        self.assertEqual("lifecycle.confirmation", self.cause("interrupted-after-effect", lambda f, o: f["browser-diagnostics"].update(failedRequests=[])))
        self.assertEqual("lifecycle.duplicate_effect", self.cause("interrupted-after-effect",
                         lambda f, o: f["agent-requests"].insert(4, f["agent-requests"][3])))
        self.assertEqual("lifecycle.stored_result", self.cause("interrupted-after-effect",
                         lambda f, o: f["lifecycle-session-after"]["value"]["data"].update(messages=[])))


class LifecycleManifestTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        evidence = self.root / "evidence.json"; evidence.write_text("{}")
        self.ref = retained(evidence)

    def tearDown(self):
        self.temporary.cleanup()

    def rows(self):
        rows = []
        for identity in sorted(EXPECTED_CASES):
            provider, key = identity.split("-", 1)
            row = {"id": identity, "provider": provider, "caseKey": key, "status": "passed", "cause": None, "modeMismatch": False,
                   "nativeEvidenceVerified": True, "qualified": False, "ownerBefore": self.ref, "ownerAfter": self.ref, "evidence": [self.ref]}
            if key in MODE_CASES:
                transition = "-to-" in key
                row.update(sessionId="session", correlation={"kind": "completed-native-result", "completeProviderJourney": True},
                           policy={"acknowledged": transition, "updateRequests": int(transition)})
            else:
                row.update(adapter=provider, outcome=copy.deepcopy(EXPECTED_OUTCOMES[key]),
                           correlation={"adapter": provider, "providerTrace": self.ref, **{name: "id" for name in CORRELATION_KEYS[key]}})
            rows.append(row)
        return rows

    def section(self, rows=None):
        return {"evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False, "qualified": False, "independentPackageWitness": False,
                "cases": self.rows() if rows is None else rows}

    def test_exact_twenty_six_passed_cases_with_owner_and_correlation_evidence_qualify(self):
        self.assertEqual(26, len(require_lifecycle_matrix(self.section(), self.root)))
        self.assertEqual(13, len(LIFECYCLE_CASES))

    def test_omission_duplicate_wrong_cause_adapter_or_evidence_fails(self):
        lifecycle = next(index for index, row in enumerate(self.rows()) if row["caseKey"] == "busy-policy-lifecycle")
        def mutate(name, rows, section):
            row = rows[lifecycle]
            if name == "omitted": rows.pop(lifecycle)
            elif name == "duplicate": rows[lifecycle - 1] = copy.deepcopy(row)
            elif name == "wrong-cause": row["outcome"]["busyCode"] = "agent.session.policy_stale"
            elif name == "wrong-adapter": row["adapter"] = "anthropic" if row["provider"] == "copilot" else "copilot"
            elif name == "no-owner": row["ownerAfter"] = None
            elif name == "no-correlation": row["correlation"].pop("proposalId")
            elif name == "foreign-correlation": row["correlation"]["adapter"] = "other"
            elif name == "no-trace": row["correlation"].pop("providerTrace")
            elif name == "changed-evidence": row["evidence"] = [{**self.ref, "sha256": "0" * 64}]
            elif name == "no-evidence": row["evidence"] = []
            elif name == "failed": row.update(status="failed", cause="lifecycle.busy_missing")
            elif name == "promoted": section["independentPackageWitness"] = True
            elif name == "qualified": row["qualified"] = True
            elif name == "mode-missing-correlation": next(item for item in rows if item["caseKey"] in MODE_CASES).pop("correlation")
        for name in ("omitted", "duplicate", "wrong-cause", "wrong-adapter", "no-owner", "no-correlation", "foreign-correlation", "no-trace",
                     "changed-evidence", "no-evidence", "failed", "promoted", "qualified", "mode-missing-correlation"):
            with self.subTest(name=name):
                rows = self.rows(); section = self.section(rows)
                mutate(name, rows, section)
                with self.assertRaises(RuntimeError): require_lifecycle_matrix(section, self.root)

    def test_single_record_gate_names_the_wrong_cause(self):
        row = next(item for item in self.rows() if item["caseKey"] == "stale-target-continuation")
        require_lifecycle_record(self.root, row)
        row["outcome"]["execute"] = "agent.proposal.approval_required"
        with self.assertRaisesRegex(RuntimeError, "Wrong-cause"): require_lifecycle_record(self.root, row)

    def runtime(self):
        run = SimpleNamespace(root=self.root, report={"cases": [], "independentPackageWitness": False}, source="frozen",
                              save=Mock(), assert_immutable_inputs=Mock())
        return SimpleNamespace(run=run, select_provider=Mock())

    def test_matrix_runs_every_case_once_continues_after_failure_and_never_promotes(self):
        rows = {row["id"]: row for row in self.rows()}
        calls = []
        def execute(_runtime, provider, key):
            calls.append(provider + "-" + key)
            if provider == "copilot" and key == "busy-policy-lifecycle":
                raise LifecycleCaseFailure("busy missing", "lifecycle.busy_missing", self.ref)
            if provider == "anthropic" and key == "reviewed-size-control":
                raise ModeCaseFailure("mismatch", "autonomy.mode_mismatch")
            if provider == "anthropic" and key == "cancel-before-effect":
                raise RuntimeError("Native SSE reached EOF")
            return copy.deepcopy(rows[provider + "-" + key])
        runtime = self.runtime()
        with patch("autonomy_journeys.execute_mode_case", side_effect=execute), patch("lifecycle_journeys.execute_lifecycle_case", side_effect=execute), \
                self.assertRaises(RuntimeError):
            qualify_autonomy_matrix(runtime)
        section = runtime.run.report[MATRIX_QUALIFICATION]
        self.assertEqual(("failed", 26, sorted(EXPECTED_CASES)), (section["status"], len(section["cases"]), sorted(calls)))
        failed = {row["id"]: row["cause"] for row in section["cases"] if row["status"] != "passed"}
        self.assertEqual({"copilot-busy-policy-lifecycle": "lifecycle.busy_missing", "anthropic-reviewed-size-control": "autonomy.mode_mismatch",
                          "anthropic-cancel-before-effect": "verification"}, failed)
        self.assertFalse(runtime.run.report["independentPackageWitness"]); self.assertFalse(section["qualified"])
        self.assertEqual("failed", json.loads((self.root / "autonomy-lifecycle-qualification.json").read_text())["status"])
        self.assertEqual(["copilot", "anthropic"], [call.args[0] for call in runtime.select_provider.call_args_list])

    def test_matrix_passes_only_with_all_twenty_six(self):
        rows = {row["id"]: row for row in self.rows()}
        execute = lambda _runtime, provider, key: copy.deepcopy(rows[provider + "-" + key])
        runtime = self.runtime()
        with patch("autonomy_journeys.execute_mode_case", side_effect=execute), patch("lifecycle_journeys.execute_lifecycle_case", side_effect=execute):
            section = qualify_autonomy_matrix(runtime)
        self.assertEqual(("passed", 26), (section["status"], len(section["cases"])))
        self.assertFalse(section["independentPackageWitness"])
        with self.assertRaises(RuntimeError): qualify_autonomy_matrix(runtime)  # never replaces a retained matrix


if __name__ == "__main__":
    unittest.main()
