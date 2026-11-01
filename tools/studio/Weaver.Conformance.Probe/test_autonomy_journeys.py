"""Synthetic cause/integrity controls; no native/provider qualification is claimed."""
import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from autonomy_journeys import (MODE_CASES, EXPECTED_CASES, DIAGNOSTIC_CASES, EVIDENCE_CATEGORY,
    require_complete_autonomy, require_diagnostic_manifest, verify_mode_observation,
    require_size_owner_journey, diagnose_autonomy)
from case_records import retained


class AutonomyEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.sequence = 0
        self.witness = json.loads((Path(__file__).parent / "witness.json").read_text())

    def tearDown(self):
        self.temporary.cleanup()

    def fixture(self, provider="copilot", key="automatic-size-control"):
        self.sequence += 1
        output = self.root / str(self.sequence)
        output.mkdir()
        write = lambda name, value: self.write(output, name, value)
        initial, requested = MODE_CASES[key]
        outcome = "automatic" if initial == "full-auto" else "review"
        expected = "automatic" if requested == "full-auto" else "review"
        mismatch = outcome != expected
        applied = outcome == "automatic" or not mismatch
        w = self.witness
        before = {"widgetId": w["widgetId"], "revision": "rev-1", "size": "medium", "hostId": w["hostId"]}
        after = {**before, "size": "small", "revision": "rev-2"} if applied else copy.deepcopy(before)
        decision = copy.deepcopy(after if outcome == "automatic" else before)
        owner = lambda name, value: write(name, {"status": 200, "url": "http://127.0.0.1:52001" + w["sizeOwnerEndpoint"], "value": value})
        bref, aref, dref = owner("before", before), owner("after", after), owner("decision", decision)
        document = lambda value: ('{"namespace":"home-layout","schemaVersion":1,"revision":"' + value["revision"] +
            '","value":{"widgets":[{"id":"' + w["widgetId"] + '","size":"' + value["size"] +
            '","visible":true,"options":{"precise":9007199254740993.123456789}},{"id":"other","visible":false}],"future":{"keep":[1,2]}}}')
        for name, value in (("size-layout-before", before), ("size-layout-at-decision", decision), ("size-layout-after", after)):
            write(name + "-http", {"status": 200, "rawBody": document(value)})
        target = lambda revision: {"moduleId": w["moduleId"], "resourceType": w["resourceType"], "resourceId": w["widgetId"], "revision": revision}
        preview = {"typeId": w["sizeResultType"], "schemaVersion": "1", "data": {"widgetId": w["widgetId"],
            "revision": "rev-1", "size": "small", "beforeSize": "medium", "hostId": w["hostId"]}}
        payload = copy.deepcopy(preview); payload["data"]["revision"] = "rev-2"
        result = {"toolName": w["sizeTool"], "toolCallId": "native-size-call", "succeeded": True, "content": "Synthetic owner confirmed.",
            "payload": payload, "receipt": {"scope": "serverPersisted", "invocationId": "prepared-1", "before": target("rev-1"),
                "after": target("rev-2"), "evidence": copy.deepcopy(payload)},
            "access": {"moduleId": w["moduleId"], "contractVersion": "1", "invocationId": "prepared-1", "disclosureSourceId": "acme.home.widget-context"}}
        result["access"]["target"] = target("rev-1")
        read = {"toolName": w["readTool"], "toolCallId": "native-read-call", "succeeded": True, "content": "Synthetic read.",
            "payload": {"typeId": w["readResultType"], "schemaVersion": "1", "data": {"widgetId": w["widgetId"], "revision": "rev-1", "visible": True}}, "receipt": None}
        proposal = {"id": "proposal-1", "sessionId": "session-1", "status": "awaitingApproval", "requiresApproval": True, "baseRevision": "rev-1",
            "preparedInvocation": {"toolName": w["sizeTool"], "moduleId": w["moduleId"], "requiresApproval": True,
                "arguments": {"size": "small"}, "target": target("rev-1"), "preview": preview,
                "invocationId": "prepared-1", "sessionId": "session-1", "turnId": "turn-size", "toolCallId": "native-size-call",
                "toolContractVersion": "1", "actorId": "actor-1", "tenantId": "tenant-1", "executionLocation": "server",
                "risk": "reviewRequired", "client": None, "disclosureSourceId": "acme.home.widget-context", "approvalDigest": "digest-1"}}
        frames = [{"id": "event-read-started", "kind": "started", "payload": {"turnId": "turn-read"}},
                  {"id": "event-read", "kind": "contributedResult", "resultKind": "moduleResult", "error": None, "payload": read},
                  {"id": "event-size-started", "kind": "started", "payload": {"turnId": "turn-size"}}]
        if outcome == "review":
            frames += [{"id": "event-proposal", "kind": "proposalCreated", "payload": proposal},
                {"id": "event-approval", "kind": "toolApprovalRequested", "proposalId": proposal["id"]}]
        if applied and outcome == "automatic":
            frames.append({"id": "event-size", "kind": "contributedResult", "resultKind": "moduleResult", "error": None, "payload": result})
        if applied and outcome == "review":
            # The observed review resume carries response text/completion, not another mutation result event.
            frames += [{"id": "event-resume-message", "kind": "messageDelta", "content": "Synthetic receipt continuation."},
                       {"id": "event-resume-completed", "kind": "completed"}]
        sse = [{"url": "http://127.0.0.1:52001/_elsa/agent/sessions/session-1/stream", "status": 200,
            "contentType": "text/event-stream", "termination": "loadingFinished", "captureError": None,
            "rawBody": "".join("data: " + json.dumps(value) + "\n\n" for value in frames)}]
        write("agent-streams", sse)
        write("autonomy-native-bootstrap", {"status": 200, "value": {"data": {"capabilities": [
            {"id": "acme.home.read", "moduleId": w["moduleId"]}, {"id": w["sizeCapability"], "moduleId": w["moduleId"]}]}}})
        native_messages = []
        requests = [{"url": "http://127.0.0.1:52001/_elsa/agent/sessions", "method": "POST", "body": {"autonomyMode": initial}}]
        for label, identity in (("read", "message-read"), ("size", "message-size")):
            body = {"requestId": "request-" + label, "requestedCapabilities": ["acme.home.read"],
                    "source": {"moduleId": w["moduleId"], "actionId": w["readTool"] if label == "read" else w["sizeTool"]}}
            message = {"sessionId": "session-1", "id": identity, "requestId": body["requestId"], "role": "user",
                "context": [{"moduleId": w["moduleId"], "contentType": w["contextType"], "contributorId": "acme.home.widget-context", "target": target("rev-1")}]}
            write("autonomy-" + label + "-message", {"status": 200, "request": body, "response": {"data": {"message": message}, "error": None}})
            native_messages.append(message)
            requests.append({"url": "http://127.0.0.1:52001/_elsa/agent/sessions/session-1/messages", "method": "POST", "body": body})
        if applied:
            native_messages.append({"role": "tool", "status": "completed", "content": json.dumps(result)})
        if outcome == "review" and not mismatch:
            url = "http://127.0.0.1:52001/_elsa/agent/proposals/proposal-1/execute"
            request = {"actorId": "studio", "revision": "rev-1"} # Native API resolves the actual actor; this field grants no authority.
            response = {"data": {"proposalId": "proposal-1", "executed": True, "status": "executed", "resourceType": w["resourceType"],
                                 "resourceId": w["widgetId"], "toolResult": result}, "error": None}
            requests.append({"url": url, "method": "POST", "body": request})
            write("autonomy-size-execute", {"url": url, "status": 200, "request": request, "response": response})
            write("autonomy-size-execute-http", {"url": url, "status": 200, "rawBody": json.dumps(response)})
        write("agent-requests", requests)
        session = write("session-after", {"status": 200, "value": {"data": {"sessionId": "session-1", "messages": native_messages}}})
        commands = [{"commandId": "command-read", "toolName": w["readTool"], "arguments": {}},
                    {"commandId": "command-size", "toolName": w["sizeTool"], "arguments": {"size": "small"}}]
        for value in commands: write("command-" + value["commandId"], value)
        rows = []
        def row(kind, command, **data): return {"kind": provider + "." + kind, "data": {"commandId": command["commandId"], **data}}
        for command, tool_result in zip(commands, (read, result)):
            identity = command["commandId"]
            req = {"tools": [{"name": command["toolName"]}], "sessionId": "session-1"} if provider == "copilot" else {
                "tools": [{"name": command["toolName"]}], "messages": [{"role": "user", "contents": []}]}
            rows += [row("request", command, **req), row("call", command, toolName=command["toolName"], arguments=command["arguments"])]
            if identity == "command-size" and not applied:
                if provider == "copilot": rows.append(row("return", command, result={"status": "awaiting-approval", "executed": False, "proposalId": proposal["id"], "preview": preview}))
            elif provider == "copilot":
                returned = {"status": "awaiting-approval", "executed": False, "proposalId": proposal["id"], "preview": preview} if identity == "command-size" and outcome == "review" else tool_result
                rows.append(row("return", command, result=returned))
                if identity == "command-size" and outcome == "review":
                    rows.append(row("request", command, sessionId="session-1", tools=[]))
                    rows.append(row("continuation", command, sessionId="session-1", results=[tool_result]))
            else:
                function_result = {"kind": "functionResult", "callId": identity, "result": json.dumps(tool_result)}
                rows.append(row("request", command, tools=[], messages=[{"role": "user", "contents": []}, {"role": "tool", "contents": [function_result]}]))
                rows.append(row("continuation", command, results=[function_result]))
        tref = write("actual-trace", rows)
        trace = {"records": rows, "incompleteTailBytes": 0, "nextOffset": 100, "receipt": tref}
        transport = SimpleNamespace(protocol_trace=lambda offset: trace, http=SimpleNamespace(base="http://127.0.0.1:52001"))
        autonomy = {"caseKey": key, "initialMode": initial, "requestedMode": requested, "expectedOutcome": expected,
            "outcomeAtDecision": outcome, "sameSessionId": "session-1", "readCommandId": "command-read", "sizeCommandId": "command-size",
            "readMessageId": "message-read", "sizeMessageId": "message-size", "readRequestId": "request-read", "sizeRequestId": "request-size",
            "selectionRequests": [], "targetSize": "small", "applied": applied, "modeMismatch": mismatch, "policyAcknowledgementClaimed": False,
            "ownerAtDecision": dref, "sessionAfter": session}
        observation = {"provider": provider, "journey": "autonomy-size", "status": "failed" if mismatch else "passed", "pageErrors": [],
            "autonomy": autonomy, "ownerBefore": bref, "ownerAfter": aref, "evidence": []}
        if mismatch: observation.update(expectedFailureObserved=True, actual={"stage": "session-mode", "diagnostic": "autonomy.mode_mismatch"})
        return output, observation, transport, trace

    @staticmethod
    def write(output, name, value):
        path = output / (name + ".json")
        path.write_text(json.dumps(value))
        return retained(path)

    def verify(self, fixture, provider="copilot"):
        output, observation, transport, _ = fixture
        return verify_mode_observation(output, observation, self.witness, provider, transport)

    def test_both_actual_adapter_shapes_keep_four_controls_and_precise_failed_transitions(self):
        records = []
        for provider in ("copilot", "anthropic"):
            for key in MODE_CASES:
                with self.subTest(provider=provider, key=key):
                    record = self.verify(self.fixture(provider, key), provider)
                    records.append(record)
                    self.assertFalse(record["qualified"])
                    self.assertEqual(record["status"], "failed" if "-to-" in key else "passed")
                    self.assertEqual(record["correlation"]["completeProviderJourney"], key != "manual-to-automatic")
        require_diagnostic_manifest(records)
        self.assertEqual(4, sum(row["status"] == "failed" for row in records))

    def test_reviewed_execute_journal_and_continuation_do_not_require_duplicate_mutation_sse(self):
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                fixture = self.fixture(provider, "reviewed-size-control")
                streams = json.loads((fixture[0] / "agent-streams.json").read_text())
                contributed = [json.loads(raw.removeprefix("data: "))["payload"]["toolName"] for raw in streams[0]["rawBody"].strip().split("\n\n")
                               if json.loads(raw.removeprefix("data: ")).get("kind") == "contributedResult"]
                self.assertEqual([self.witness["readTool"]], contributed)
                record = self.verify(fixture, provider)
                self.assertEqual("passed", record["status"])
                self.assertTrue(record["correlation"]["completeProviderJourney"])

    def test_automatic_effect_still_requires_one_full_native_sse_without_execute(self):
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                self.verify(self.fixture(provider), provider)
                fixture = self.fixture(provider)
                path = fixture[0] / "agent-streams.json"; streams = json.loads(path.read_text())
                streams[0]["rawBody"] = streams[0]["rawBody"].split('data: {"id": "event-size"')[0]
                path.write_text(json.dumps(streams))
                with self.assertRaises(RuntimeError): self.verify(fixture, provider)

    def test_reviewed_optional_sse_must_match_http_and_can_never_replace_it(self):
        for provider in ("copilot", "anthropic"):
            fixture = self.fixture(provider, "reviewed-size-control")
            output = fixture[0]
            response = json.loads((output / "autonomy-size-execute.json").read_text())
            result = response["response"]["data"]["toolResult"]
            path = output / "agent-streams.json"; streams = json.loads(path.read_text())
            streams[0]["rawBody"] += "data: " + json.dumps({"id": "optional-size", "kind": "contributedResult", "resultKind": "moduleResult", "payload": result}) + "\n\n"
            path.write_text(json.dumps(streams))
            self.verify(fixture, provider)
            for mutation in ("missing-structured", "missing-raw", "bad-status", "error", "executed", "outcome-status", "foreign-url", "wrong-proposal",
                             "stale-request", "wrong-call", "wrong-invocation", "wrong-before", "missing-result", "raw-mismatch", "duplicate-http"):
                with self.subTest(provider=provider, mutation=mutation):
                    fixture = self.fixture(provider, "reviewed-size-control"); output = fixture[0]
                    structured = output / "autonomy-size-execute.json"; raw = output / "autonomy-size-execute-http.json"
                    response, http = json.loads(structured.read_text()), json.loads(raw.read_text())
                    result = copy.deepcopy(response["response"]["data"]["toolResult"])
                    sse_path = output / "agent-streams.json"; streams = json.loads(sse_path.read_text())
                    streams[0]["rawBody"] += "data: " + json.dumps({"id": "optional-size", "kind": "contributedResult", "resultKind": "moduleResult", "payload": result}) + "\n\n"
                    sse_path.write_text(json.dumps(streams))
                    if mutation == "missing-structured": structured.unlink()
                    elif mutation == "missing-raw": raw.unlink()
                    else:
                        data = response["response"]["data"]
                        if mutation == "bad-status": response["status"] = http["status"] = 403
                        elif mutation == "error": response["response"]["error"] = {"code": "agent.proposal.forbidden"}
                        elif mutation == "executed": data["executed"] = False
                        elif mutation == "outcome-status": data["status"] = "uncertain"
                        elif mutation == "foreign-url": response["url"] = http["url"] = "http://127.0.0.1:52002/_elsa/agent/proposals/proposal-1/execute"
                        elif mutation == "wrong-proposal": data["proposalId"] = "another"
                        elif mutation == "stale-request": response["request"]["revision"] = "rev-0"
                        elif mutation == "wrong-call": data["toolResult"]["toolCallId"] = "another-call"
                        elif mutation == "wrong-invocation": data["toolResult"]["receipt"]["invocationId"] = "another-invocation"
                        elif mutation == "wrong-before": data["toolResult"]["receipt"]["before"]["revision"] = "rev-0"
                        elif mutation == "missing-result": data["toolResult"] = None
                        elif mutation == "duplicate-http": self.write(output, "competing-execute", response)
                        http["rawBody"] = json.dumps(response["response"]) if mutation != "raw-mismatch" else "{}"
                        structured.write_text(json.dumps(response)); raw.write_text(json.dumps(http))
                    with self.assertRaises((RuntimeError, ValueError)): self.verify(fixture, provider)

    def test_reviewed_binding_journal_and_provider_continuation_are_all_required(self):
        for provider in ("copilot", "anthropic"):
            for mutation in ("prepared-call", "prepared-invocation", "prepared-target", "prepared-preview", "prepared-version", "prepared-turn",
                             "duplicate-execute", "wrong-execute-path", "optional-sse-mismatch", "optional-sse-duplicate", "missing-journal", "missing-continuation"):
                with self.subTest(provider=provider, mutation=mutation):
                    fixture = self.fixture(provider, "reviewed-size-control"); output, observation, _, trace = fixture
                    path = output / "agent-streams.json"; streams = json.loads(path.read_text())
                    values = [json.loads(raw.removeprefix("data: ")) for raw in streams[0]["rawBody"].strip().split("\n\n")]
                    proposal = next(row["payload"] for row in values if row["kind"] == "proposalCreated")
                    prepared = proposal["preparedInvocation"]
                    if mutation == "prepared-call": prepared["toolCallId"] = "another-call"
                    elif mutation == "prepared-invocation": prepared["invocationId"] = "another-invocation"
                    elif mutation == "prepared-target": prepared["target"]["resourceId"] = "another-widget"
                    elif mutation == "prepared-preview": prepared["preview"]["data"]["size"] = "medium"
                    elif mutation == "prepared-version": prepared["toolContractVersion"] = "2"
                    elif mutation == "prepared-turn": prepared["turnId"] = "another-turn"
                    elif mutation in ("duplicate-execute", "wrong-execute-path"):
                        requests_path = output / "agent-requests.json"; requests = json.loads(requests_path.read_text())
                        if mutation == "duplicate-execute": requests.append(copy.deepcopy(requests[-1]))
                        else: requests[-1]["url"] = requests[-1]["url"].replace("proposal-1", "proposal-2")
                        requests_path.write_text(json.dumps(requests))
                    elif mutation in ("optional-sse-mismatch", "optional-sse-duplicate"):
                        result = json.loads((output / "autonomy-size-execute.json").read_text())["response"]["data"]["toolResult"]
                        if mutation == "optional-sse-mismatch": result["content"] = "Changed native result"
                        values.append({"id": "optional-result", "kind": "contributedResult", "resultKind": "moduleResult", "payload": result})
                        if mutation == "optional-sse-duplicate": values.append({"id": "duplicate-optional", "kind": "contributedResult", "resultKind": "moduleResult", "payload": copy.deepcopy(result)})
                    elif mutation == "missing-journal":
                        session_path = Path(observation["autonomy"]["sessionAfter"]["path"]); session = json.loads(session_path.read_text())
                        session["value"]["data"]["messages"] = session["value"]["data"]["messages"][:2]; session_path.write_text(json.dumps(session))
                        observation["autonomy"]["sessionAfter"] = retained(session_path)
                    else: trace["records"][:] = [row for row in trace["records"] if not (row["kind"] == provider + ".continuation" and row["data"]["commandId"] == "command-size")]
                    streams[0]["rawBody"] = "".join("data: " + json.dumps(value) + "\n\n" for value in values)
                    path.write_text(json.dumps(streams))
                    with self.assertRaises((RuntimeError, ValueError)): self.verify(fixture, provider)

    def test_deferred_call_requires_real_advertisement_and_single_call(self):
        for provider in ("copilot", "anthropic"):
            for mutation in ("missing-call", "duplicate-call", "wrong-tool", "wrong-arguments", "not-advertised", "continuation", "foreign-command"):
                with self.subTest(provider=provider, mutation=mutation):
                    fixture = self.fixture(provider, "manual-to-automatic")
                    rows = fixture[3]["records"]
                    selected = [row for row in rows if row["data"]["commandId"] == "command-size"]
                    call = next(row for row in selected if row["kind"] == provider + ".call")
                    if mutation == "missing-call": rows.remove(call)
                    elif mutation == "duplicate-call": rows.append(copy.deepcopy(call))
                    elif mutation == "wrong-tool": call["data"]["toolName"] = "other"
                    elif mutation == "wrong-arguments": call["data"]["arguments"] = {"size": "medium"}
                    elif mutation == "not-advertised": selected[0]["data"]["tools"] = []
                    elif mutation == "continuation": rows.append({"kind": provider + ".continuation", "data": {"commandId": "command-size", "results": []}})
                    else: rows.append({"kind": provider + ".request", "data": {"commandId": "other", "tools": []}})
                    with self.assertRaises(RuntimeError): self.verify(fixture, provider)

    def test_provider_transport_auth_busy_and_error_frames_are_not_mode_mismatch(self):
        for mutation in ("browser-failure", "page-error", "sse-http", "sse-capture", "sse-error", "truncated-proposal", "duplicate-event", "trace-tail"):
            with self.subTest(mutation=mutation):
                fixture = self.fixture(key="manual-to-automatic")
                output, observation, _, trace = fixture
                if mutation == "browser-failure": observation["failure"] = {"type": "TimeoutError"}
                elif mutation == "page-error": observation["pageErrors"] = ["Error"]
                elif mutation == "trace-tail": trace["incompleteTailBytes"] = 1
                else:
                    path = output / "agent-streams.json"; streams = json.loads(path.read_text())
                    if mutation == "sse-http": streams[0]["status"] = 401
                    elif mutation == "sse-capture": streams[0]["captureError"] = "unavailable"
                    elif mutation == "sse-error": streams[0]["rawBody"] += 'data: {"id":"error","kind":"error","error":{"code":"agent.turn.busy"}}\n\n'
                    elif mutation == "truncated-proposal": streams[0]["rawBody"] = streams[0]["rawBody"].split('data: {"id": "event-proposal"')[0]
                    else: streams[0]["rawBody"] += streams[0]["rawBody"].split("\n\n")[0] + "\n\n"
                    path.write_text(json.dumps(streams))
                with self.assertRaises(RuntimeError): self.verify(fixture)

    def test_pending_review_cannot_claim_effect_or_use_another_prepared_target(self):
        for mutation in ("wrong-target", "stale", "no-approval", "wrong-proposal", "effect", "sdk-applied"):
            with self.subTest(mutation=mutation):
                fixture = self.fixture(key="manual-to-automatic")
                output, _, _, trace = fixture
                path = output / "agent-streams.json"; streams = json.loads(path.read_text())
                values = [json.loads(raw.removeprefix("data: ")) for raw in streams[0]["rawBody"].strip().split("\n\n")]
                proposal = next(row["payload"] for row in values if row["kind"] == "proposalCreated")
                if mutation == "wrong-target": proposal["preparedInvocation"]["target"]["resourceId"] = "other"
                elif mutation == "stale": proposal["preparedInvocation"]["target"]["revision"] = "rev-0"
                elif mutation == "no-approval": proposal["requiresApproval"] = False
                elif mutation == "wrong-proposal": next(row for row in values if row["kind"] == "toolApprovalRequested")["proposalId"] = "other"
                elif mutation == "effect": values.append({"id": "forbidden-effect", "kind": "contributedResult", "payload": {"toolName": self.witness["sizeTool"]}})
                else: next(row for row in trace["records"] if row["kind"] == "copilot.return" and row["data"]["commandId"] == "command-size")["data"]["result"]["executed"] = True
                streams[0]["rawBody"] = "".join("data: " + json.dumps(value) + "\n\n" for value in values)
                path.write_text(json.dumps(streams))
                with self.assertRaises(RuntimeError): self.verify(fixture)

    def test_full_native_json_precision_unrelated_visibility_and_receipt_cannot_be_lost(self):
        for mutation in ("precision", "visibility", "other-widget", "size", "revision", "receipt", "journal"):
            with self.subTest(mutation=mutation):
                fixture = self.fixture()
                output, observation, _, _ = fixture
                if mutation in ("precision", "visibility", "other-widget"):
                    path = output / "size-layout-after-http.json"; value = json.loads(path.read_text())
                    value["rawBody"] = value["rawBody"].replace(*{
                        "precision": ("9007199254740993.123456789", "9007199254740993.123456788"),
                        "visibility": ('"visible":true', '"visible":false'),
                        "other-widget": ('"id":"other"', '"id":"other-changed"')}[mutation])
                    path.write_text(json.dumps(value))
                elif mutation in ("size", "revision"):
                    path = Path(observation["ownerAfter"]["path"]); value = json.loads(path.read_text()); value["value"][mutation] = "medium" if mutation == "size" else "rev-9"
                    path.write_text(json.dumps(value)); observation["ownerAfter"] = retained(path)
                elif mutation == "receipt":
                    path = output / "agent-streams.json"; value = json.loads(path.read_text()); value[0]["rawBody"] = value[0]["rawBody"].replace('"after": {"moduleId"', '"otherAfter": {"moduleId"'); path.write_text(json.dumps(value))
                else:
                    path = Path(observation["autonomy"]["sessionAfter"]["path"]); value = json.loads(path.read_text()); value["value"]["data"]["messages"] = value["value"]["data"]["messages"][:2]; path.write_text(json.dumps(value)); observation["autonomy"]["sessionAfter"] = retained(path)
                with self.assertRaises(RuntimeError): self.verify(fixture)

    def test_automatic_mode_never_issues_extra_execute_to_obtain_proof(self):
        fixture = self.fixture(); output = fixture[0]
        path = output / "agent-requests.json"; value = json.loads(path.read_text())
        value.append({"method": "POST", "url": "http://127.0.0.1:52001/_elsa/agent/proposals/extra/execute", "body": {}})
        path.write_text(json.dumps(value))
        with self.assertRaises(RuntimeError): self.verify(fixture)

    def test_unrelated_native_boolean_and_number_are_not_interchangeable(self):
        for original, mutation in (('"keep":[1,2]', '"keep":[true,2]'), ('"keep":[1,0]', '"keep":[1,false]')):
            with self.subTest(mutation=mutation):
                fixture = self.fixture()
                output, observation, _, _ = fixture
                if original.endswith('0]'):
                    for name in ("size-layout-before", "size-layout-at-decision", "size-layout-after"):
                        path = output / (name + "-http.json")
                        record = json.loads(path.read_text())
                        record["rawBody"] = record["rawBody"].replace('"keep":[1,2]', original)
                        path.write_text(json.dumps(record))
                path = output / "size-layout-after-http.json"
                record = json.loads(path.read_text())
                self.assertIn(original, record["rawBody"])
                record["rawBody"] = record["rawBody"].replace(original, mutation)
                path.write_text(json.dumps(record))
                with self.assertRaises(RuntimeError):
                    require_size_owner_journey(output, observation, self.witness)

    def test_second_conversation_missing_provenance_and_relabeling_are_rejected(self):
        for mutation in ("new-session", "source", "accepted-target", "relabel", "fake-ack", "same-request", "selection-request"):
            with self.subTest(mutation=mutation):
                fixture = self.fixture(key="manual-to-automatic"); output, observation, _, _ = fixture
                if mutation == "new-session":
                    path = output / "agent-requests.json"; value = json.loads(path.read_text()); value.append(copy.deepcopy(value[0])); path.write_text(json.dumps(value))
                elif mutation == "source":
                    path = output / "autonomy-size-message.json"; value = json.loads(path.read_text()); value["response"]["data"]["message"]["context"][0]["contributorId"] = "other"; path.write_text(json.dumps(value))
                elif mutation == "accepted-target":
                    path = output / "autonomy-size-message.json"; value = json.loads(path.read_text()); value["response"]["data"]["message"]["context"][0]["target"]["revision"] = "other"; path.write_text(json.dumps(value))
                elif mutation == "relabel": observation["status"] = "passed"
                elif mutation == "fake-ack": observation["autonomy"]["policyAcknowledgementClaimed"] = True
                elif mutation == "selection-request": observation["autonomy"]["selectionRequests"] = [{"method": "PATCH", "url": "/unimplemented", "status": 404}]
                else: observation["autonomy"]["sizeRequestId"] = observation["autonomy"]["readRequestId"]
                with self.assertRaises(RuntimeError): self.verify(fixture)

    def test_actual_first_read_capability_shape_and_distinct_module_action_are_required(self):
        # The current host sends capabilities[0] for both prompts. Size is identified
        # by the honest module action plus the actual installed tool call, not a fake size-cap request.
        fixture = self.fixture()
        size = json.loads((fixture[0] / "autonomy-size-message.json").read_text())["request"]
        self.assertEqual(["acme.home.read"], size["requestedCapabilities"])
        self.assertEqual(self.witness["sizeTool"], size["source"]["actionId"])
        self.verify(fixture)
        for mutation in ("uninstalled", "missing-size-capability", "wrong-action", "wrong-module", "fabricated-size-propagation"):
            with self.subTest(mutation=mutation):
                fixture = self.fixture(); output = fixture[0]
                if mutation == "missing-size-capability":
                    path = output / "autonomy-native-bootstrap.json"; value = json.loads(path.read_text()); value["value"]["data"]["capabilities"].pop(); path.write_text(json.dumps(value))
                else:
                    path = output / "autonomy-size-message.json"; value = json.loads(path.read_text()); request = value["request"]
                    if mutation == "uninstalled": request["requestedCapabilities"] = ["missing"]
                    elif mutation == "wrong-action": request["source"]["actionId"] = self.witness["writeTool"]
                    elif mutation == "wrong-module": request["source"]["moduleId"] = "Foreign"
                    else: request["requestedCapabilities"] = [self.witness["sizeCapability"]]
                    path.write_text(json.dumps(value))
                    requests_path = output / "agent-requests.json"; requests = json.loads(requests_path.read_text()); requests[2]["body"] = request; requests_path.write_text(json.dumps(requests))
                with self.assertRaises(RuntimeError): self.verify(fixture)

    def test_owner_changed_at_review_is_not_a_deferred_outcome(self):
        fixture = self.fixture(key="manual-to-automatic")
        path = Path(fixture[1]["autonomy"]["ownerAtDecision"]["path"])
        value = json.loads(path.read_text()); value["value"]["size"] = "small"; value["value"]["revision"] = "rev-2"
        path.write_text(json.dumps(value)); fixture[1]["autonomy"]["ownerAtDecision"] = retained(path)
        with self.assertRaises(RuntimeError): self.verify(fixture)

    def test_changed_or_foreign_retained_owner_reference_is_rejected(self):
        for mutation in ("changed", "foreign"):
            fixture = self.fixture()
            reference = fixture[1]["ownerBefore"]
            if mutation == "changed": Path(reference["path"]).write_text("{}")
            else:
                path = self.root / "foreign-owner.json"; path.write_bytes(Path(reference["path"]).read_bytes()); fixture[1]["ownerBefore"] = retained(path)
            with self.assertRaises(RuntimeError): self.verify(fixture)


class AutonomyManifestTests(unittest.TestCase):
    @staticmethod
    def final_section():
        return {"status": "passed", "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False,
                "cases": [{"id": identity, "provider": identity.split("-", 1)[0], "caseKey": identity.split("-", 1)[1], "status": "passed", "nativeEvidenceVerified": True,
                           "modeMismatch": False, "evidence": [{"synthetic": True}]} for identity in sorted(EXPECTED_CASES)]}

    def test_exact_final_manifest_is_twenty_six_and_initial_eight_is_incomplete(self):
        self.assertEqual(26, len(EXPECTED_CASES)); self.assertEqual(8, len(DIAGNOSTIC_CASES))
        section = self.final_section(); require_complete_autonomy(section)
        section["cases"] = [row for row in section["cases"] if row["id"] in DIAGNOSTIC_CASES]
        with self.assertRaises(RuntimeError): require_complete_autonomy(section)

    def test_final_missing_duplicate_wrong_adapter_or_unverified_case_refuses(self):
        for mutation in ("missing", "duplicate", "wrong-adapter", "failed", "unverified", "mismatch", "live", "no-evidence"):
            with self.subTest(mutation=mutation):
                section = self.final_section()
                if mutation == "missing": section["cases"].pop()
                elif mutation == "duplicate": section["cases"][-1] = copy.deepcopy(section["cases"][0])
                elif mutation == "wrong-adapter": section["cases"][0]["provider"] = "anthropic" if section["cases"][0]["provider"] == "copilot" else "copilot"
                elif mutation == "failed": section["cases"][0]["status"] = "failed"
                elif mutation == "unverified": section["cases"][0]["nativeEvidenceVerified"] = False
                elif mutation == "mismatch": section["cases"][0]["modeMismatch"] = True
                elif mutation == "live": section["liveProvider"] = True
                else: section["cases"][0]["evidence"] = []
                with self.assertRaises(RuntimeError): require_complete_autonomy(section)

    def test_incomplete_or_wrong_cause_diagnostic_does_not_complete(self):
        records = [{"id": identity, "provider": identity.split("-", 1)[0], "caseKey": identity.split("-", 1)[1], "status": "passed",
                    "modeMismatch": False, "nativeEvidenceVerified": True, "qualified": False} for identity in sorted(DIAGNOSTIC_CASES)]
        require_diagnostic_manifest(records)
        for mutation in ("missing", "duplicate", "timeout", "qualification"):
            with self.subTest(mutation=mutation):
                changed = copy.deepcopy(records)
                if mutation == "missing": changed.pop()
                elif mutation == "duplicate": changed[-1] = copy.deepcopy(changed[0])
                elif mutation == "timeout": changed[0].update(status="failed", cause="timeout", modeMismatch=True)
                else: changed[0]["qualified"] = True
                with self.assertRaises(RuntimeError): require_diagnostic_manifest(changed)

    def test_native_failure_keeps_original_case_and_never_promotes_package_witness(self):
        with tempfile.TemporaryDirectory() as directory:
            original = [{"id": "module-add", "status": "passed"}]
            run = SimpleNamespace(root=Path(directory), report={"cases": copy.deepcopy(original), "independentPackageWitness": False}, source="frozen", save=Mock())
            runtime = SimpleNamespace(run=run, select_provider=Mock())
            with patch("autonomy_journeys.execute_mode_case", side_effect=RuntimeError("native-failure")), self.assertRaisesRegex(RuntimeError, "native-failure"):
                diagnose_autonomy(runtime)
            self.assertEqual(original, run.report["cases"])
            self.assertFalse(run.report["independentPackageWitness"])
            self.assertEqual("diagnostic-failed", run.report["autonomyBaselineDiagnostic"]["status"])
            self.assertEqual("diagnostic-failed", json.loads((run.root / "baseline-diagnostic.json").read_text())["status"])


if __name__ == "__main__":
    unittest.main()
