"""Synthetic cause/integrity controls; no native/provider qualification is claimed."""
import copy
import json
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from autonomy_journeys import (MODE_CASES, MODE_OUTCOMES, EXPECTED_CASES, MODE_CASE_IDS, EVIDENCE_CATEGORY, MODE_QUALIFICATION,
    ModeCaseFailure, autonomy_manifest_gaps, autonomy_stages, require_complete_autonomy, require_mode_qualification, verify_mode_observation,
    require_size_owner_journey, qualify_autonomy_modes, execute_mode_case, _frames)
from case_records import retained


class AutonomyEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.sequence = 0
        self.witness = json.loads((Path(__file__).parent / "witness.json").read_text())

    def tearDown(self):
        self.temporary.cleanup()

    @staticmethod
    def stream(body, identity="native-get-1", *, method="GET", resource_type="Fetch"):
        url = "http://127.0.0.1:52001/_elsa/agent/sessions/session-1/stream"
        size = len(body.encode())
        return {"requestId": identity, "url": url, "status": 200 if method == "GET" else 204,
                "requestObservation": {"source": "Network.requestWillBeSent", "requestId": identity, "method": method,
                    "url": url, "resourceType": resource_type, "initiatorType": "preflight" if method == "OPTIONS" else "script"},
                "responseObservation": {"source": "Network.responseReceived", "requestId": identity, "url": url,
                    "resourceType": "Preflight" if method == "OPTIONS" else resource_type},
                "requestObservationCount": 1, "responseObservationCount": 1, "requestIdentityMatchesResponse": True,
                "contentType": "text/event-stream" if method == "GET" else "", "termination": "loadingFinished", "captureError": None,
                "captureValidationError": None, "streamResourceContentError": None, "responseBodyError": None,
                "captureMethod": "streamResourceContent" if method == "GET" else "not-sse", "responseBodyAttempted": False,
                "bufferedBytes": size, "completeBodyBytes": 0, "streamedBytes": 0, "observedBodyBytes": size,
                "receivedDataBytes": size, "dataEventCount": 1 if size else 0, "dataLengthMetadataError": None,
                "textTruncation": "none", "trailingIncompleteUtf8Bytes": 0, "rawBody": body}

    def with_preflights(self, fixture):
        path = fixture[0] / "agent-streams.json"
        original = json.loads(path.read_text())[0]["rawBody"]
        split = original.index('data: {"id": "event-size-started"')
        rows = [self.stream("", "native-preflight-1", method="OPTIONS", resource_type="Other"),
                self.stream(original[:split], "native-get-1"),
                self.stream("", "native-preflight-2", method="OPTIONS", resource_type="Other"),
                self.stream(original[split:], "native-get-2")]
        path.write_text(json.dumps(rows))
        return fixture

    @staticmethod
    def domain_policy(mode):
        """The accepted message's domain AgentPolicy, serialized with the API's camelCase enums."""
        names = {"manual": "manual", "full-auto": "fullAuto"}
        return {"id": "default", "enabled": True, "contextVisibility": True, "maxContextSensitivity": "sensitive",
                "allowedContextKinds": ["*"], "autonomyMode": names[mode], "maxAutonomyMode": "fullAuto",
                "deniedCapabilityIds": [], "retentionLabel": "Configured by administrator",
                "requiresApprovalForMutations": mode != "full-auto",
                "allowedAutonomyModes": ["manual", "autoReadOnly", "fullAuto"]}

    def fixture(self, provider="copilot", key="automatic-size-control"):
        self.sequence += 1
        output = self.root / str(self.sequence)
        output.mkdir()
        write = lambda name, value: self.write(output, name, value)
        initial, requested = MODE_CASES[key]
        transition = initial != requested
        outcome = "automatic" if requested == "full-auto" else "review"
        w = self.witness
        base = "http://127.0.0.1:52001"
        created = {"autonomyMode": initial, "maxAutonomyMode": "full-auto", "allowedAutonomyModes": ["manual", "auto-read-only", "full-auto"], "revision": 0}
        acknowledged = {**created, "autonomyMode": requested, "revision": 1} if transition else None
        effective = acknowledged or created
        before = {"widgetId": w["widgetId"], "revision": "rev-1", "size": "medium", "hostId": w["hostId"]}
        after = {**before, "size": "small", "revision": "rev-2"}
        decision = copy.deepcopy(after if outcome == "automatic" else before)
        owner = lambda name, value: write(name, {"status": 200, "url": base + w["sizeOwnerEndpoint"], "value": value})
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
            # The observed review resume carries response text/completion, not another mutation result event.
            frames += [{"id": "event-proposal", "kind": "proposalCreated", "payload": proposal},
                       {"id": "event-approval", "kind": "toolApprovalRequested", "proposalId": proposal["id"]},
                       {"id": "event-resume-message", "kind": "messageDelta", "content": "Synthetic receipt continuation."},
                       {"id": "event-resume-completed", "kind": "completed"}]
        else:
            frames.append({"id": "event-size", "kind": "contributedResult", "resultKind": "moduleResult", "error": None, "payload": result})
        write("agent-streams", [self.stream("".join("data: " + json.dumps(value) + "\n\n" for value in frames))])
        write("autonomy-native-bootstrap", {"status": 200, "value": {"data": {"capabilities": [
            {"id": "acme.home.read", "moduleId": w["moduleId"]}, {"id": w["sizeCapability"], "moduleId": w["moduleId"]}]}}})
        create_body = {"autonomyMode": initial}
        write("autonomy-create-session", {"url": base + "/_elsa/agent/sessions", "status": 200, "request": create_body,
            "response": {"data": {"sessionId": "session-1", "status": "active", "policy": created}, "error": None}})
        requests = [{"url": base + "/_elsa/agent/sessions", "method": "POST", "body": create_body}]
        update_url = base + "/_elsa/agent/sessions/session-1/autonomy"
        update_ref, network, selection = None, [], []
        details = {}
        for label, identity, policy, mode in (("read", "message-read", created, initial), ("size", "message-size", effective, effective["autonomyMode"])):
            if label == "size" and transition:
                body = {"autonomyMode": requested, "expectedRevision": created["revision"]}
                selection = [{"url": update_url, "method": "PUT", "body": body}]
                requests += selection
                response = {"data": acknowledged, "error": None}
                update_ref = write("autonomy-mode-update", {"url": update_url, "method": "PUT", "status": 200, "request": body, "response": response})
                write("autonomy-mode-update-http", {"url": update_url, "method": "PUT", "status": 200, "rawBody": json.dumps(response)})
                metadata = {"url": update_url, "responseUrl": update_url, "requestObservationCount": 1, "responseObservationCount": 1,
                            "termination": "loadingFinished", "errorText": None, "canceled": False}
                network = [{**metadata, "requestId": "autonomy-preflight", "method": "OPTIONS", "resourceType": "Preflight",
                            "initiatorType": "preflight", "responseResourceType": "Preflight", "status": 204},
                           {**metadata, "requestId": "autonomy-put", "method": "PUT", "resourceType": "Fetch",
                            "initiatorType": "script", "responseResourceType": "Fetch", "status": 200}]
            body = {"requestId": "request-" + label, "requestedCapabilities": ["acme.home.read"],
                    "source": {"moduleId": w["moduleId"], "actionId": w["readTool"] if label == "read" else w["sizeTool"]}}
            message = {"sessionId": "session-1", "id": identity, "requestId": body["requestId"], "role": "user", "status": "pending",
                "context": [{"moduleId": w["moduleId"], "contentType": w["contextType"], "contributorId": "acme.home.widget-context", "target": target("rev-1")}],
                "acceptedPolicy": {"policy": self.domain_policy(mode), "revision": policy["revision"]}}
            write("autonomy-" + label + "-message", {"status": 200, "request": body, "response": {"data": {"message": message}, "error": None}})
            details[label] = {"id": identity, "role": "user", "content": "", "status": "completed", "acceptedPolicyRevision": policy["revision"]}
            requests.append({"url": base + "/_elsa/agent/sessions/session-1/messages", "method": "POST", "body": body})
        tool = {"id": "message-tool", "role": "tool", "status": "completed", "content": json.dumps(result), "acceptedPolicyRevision": effective["revision"]}
        if outcome == "review":
            url = base + "/_elsa/agent/proposals/proposal-1/execute"
            request = {"actorId": "studio", "revision": "rev-1"} # Native API resolves the actual actor; this field grants no authority.
            response = {"data": {"proposalId": "proposal-1", "executed": True, "status": "executed", "resourceType": w["resourceType"],
                                 "resourceId": w["widgetId"], "toolResult": result}, "error": None}
            requests.append({"url": base + "/_elsa/agent/proposals/proposal-1/approve", "method": "POST", "body": {"revision": "rev-1"}})
            requests.append({"url": url, "method": "POST", "body": request})
            write("autonomy-size-execute", {"url": url, "status": 200, "request": request, "response": response})
            write("autonomy-size-execute-http", {"url": url, "status": 200, "rawBody": json.dumps(response)})
        write("agent-requests", requests)
        write("autonomy-network", network)
        detail = lambda name, policy, messages: write(name, {"status": 200, "url": base + "/_elsa/agent/sessions/session-1",
            "value": {"data": {"sessionId": "session-1", "status": "active", "policy": policy, "messages": messages}}})
        session_before = detail("session-before", created, [details["read"]])
        session_selected = detail("session-selected", effective, [details["read"]])
        session = detail("session-after", effective, [details["read"], details["size"], tool])
        label = {"manual": "Manual review", "full-auto": "Autopilot"}[effective["autonomyMode"]]
        write("autonomy-mode-selector", {"state": "acknowledged", "pendingMode": None, "ariaBusy": "false", "label": label,
                                         "statusCount": 0, "mode": effective["autonomyMode"]})
        write("autonomy-ui-attribution", [{"role": "user", "revision": None, "autonomyMode": None},
            {"role": "assistant", "revision": str(created["revision"]), "autonomyMode": initial},
            {"role": "user", "revision": None, "autonomyMode": None},
            {"role": "assistant", "revision": str(effective["revision"]), "autonomyMode": effective["autonomyMode"]}])
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
            if provider == "copilot":
                pending = identity == "command-size" and outcome == "review"
                returned = {"status": "awaiting-approval", "executed": False, "proposalId": proposal["id"], "preview": preview} if pending else tool_result
                rows.append(row("return", command, result=returned))
                if pending:
                    rows.append(row("request", command, sessionId="session-1", tools=[]))
                    rows.append(row("continuation", command, sessionId="session-1", results=[tool_result]))
            else:
                function_result = {"kind": "functionResult", "callId": identity, "result": json.dumps(tool_result)}
                rows.append(row("request", command, tools=[], messages=[{"role": "user", "contents": []}, {"role": "tool", "contents": [function_result]}]))
                rows.append(row("continuation", command, results=[function_result]))
        tref = write("actual-trace", rows)
        trace = {"records": rows, "incompleteTailBytes": 0, "nextOffset": 100, "receipt": tref}
        transport = SimpleNamespace(protocol_trace=lambda offset: trace, http=SimpleNamespace(base=base))
        autonomy = {"caseKey": key, "initialMode": initial, "requestedMode": requested, "expectedOutcome": outcome,
            "outcomeAtDecision": outcome, "sameSessionId": "session-1", "readCommandId": "command-read", "sizeCommandId": "command-size",
            "readMessageId": "message-read", "sizeMessageId": "message-size", "readRequestId": "request-read", "sizeRequestId": "request-size",
            "selectionRequests": selection, "targetSize": "small", "applied": True, "modeMismatch": False,
            "policyAcknowledgementClaimed": transition, "createdPolicy": created, "acknowledgedPolicy": acknowledged,
            "effectivePolicy": effective, "policyUpdate": update_ref, "ownerAtDecision": dref,
            "sessionBefore": session_before, "sessionAfterSelection": session_selected, "sessionAfter": session}
        observation = {"provider": provider, "journey": "autonomy-size", "status": "passed", "pageErrors": [],
            "autonomy": autonomy, "ownerBefore": bref, "ownerAfter": aref, "evidence": []}
        return output, observation, transport, trace

    @staticmethod
    def write(output, name, value):
        path = output / (name + ".json")
        path.write_text(json.dumps(value))
        return retained(path)

    def verify(self, fixture, provider="copilot"):
        output, observation, transport, _ = fixture
        return verify_mode_observation(output, observation, self.witness, provider, transport)

    def test_both_actual_adapters_pass_all_four_mode_cases_with_acknowledged_policy(self):
        records = []
        for provider in ("copilot", "anthropic"):
            for key in MODE_CASES:
                with self.subTest(provider=provider, key=key):
                    record = self.verify(self.fixture(provider, key), provider)
                    records.append(record)
                    transition = "-to-" in key
                    self.assertFalse(record["qualified"])
                    self.assertEqual("passed", record["status"])
                    self.assertIsNone(record["cause"])
                    self.assertTrue(record["correlation"]["completeProviderJourney"])
                    self.assertEqual("review" if key.endswith("manual") or key.startswith("reviewed") else "automatic", record["outcomeAtDecision"])
                    self.assertEqual({"acknowledged": transition, "updateRequests": int(transition), "corsPreflights": int(transition),
                                      "createdRevision": 0, "effectiveRevision": int(transition),
                                      "effectiveMode": MODE_CASES[key][1]}, record["policy"])
        require_mode_qualification(records)

    def test_exact_observed_options_preflights_preserve_each_native_mode_outcome(self):
        for provider in ("copilot", "anthropic"):
            for key in MODE_CASES:
                with self.subTest(provider=provider, key=key):
                    record = self.verify(self.with_preflights(self.fixture(provider, key)), provider)
                    self.assertEqual("passed", record["status"])

    def test_captured_other_preflight_and_legacy_preflight_require_the_observed_initiator(self):
        for resource_type in ("Other", "Preflight"):
            fixture = self.with_preflights(self.fixture()); path = fixture[0] / "agent-streams.json"
            rows = json.loads(path.read_text())
            rows[0]["requestObservation"]["resourceType"] = resource_type
            self.assertEqual("OPTIONS", rows[0]["requestObservation"]["method"])
            self.assertEqual("preflight", rows[0]["requestObservation"]["initiatorType"])
            self.assertEqual("Preflight", rows[0]["responseObservation"]["resourceType"])
            path.write_text(json.dumps(rows)); self.verify(fixture)
            for initiator in (None, "script", "other"):
                with self.subTest(resource_type=resource_type, initiator=initiator):
                    fixture = self.with_preflights(self.fixture()); path = fixture[0] / "agent-streams.json"
                    rows = json.loads(path.read_text())
                    rows[0]["requestObservation"].update(resourceType=resource_type, initiatorType=initiator)
                    path.write_text(json.dumps(rows))
                    with self.assertRaises(RuntimeError): self.verify(fixture)

    def test_only_finished_empty_same_identity_options_preflight_can_be_ignored(self):
        for mutation in ("get", "missing-method", "wrong-request-type", "script-request-type", "wrong-response-type", "missing-request", "missing-response",
                         "request-id", "response-id", "request-url", "response-url", "foreign", "wrong-session", "unmatched", "repeated-request",
                         "repeated-response", "boolean-count", "wrong-source", "nonempty", "unobserved-body", "missing-data-count", "failed-data-metadata",
                         "capture-error", "validation-error", "network-error", "canceled", "open", "failed", "http401", "http200"):
            with self.subTest(mutation=mutation):
                fixture = self.with_preflights(self.fixture()); path = fixture[0] / "agent-streams.json"
                rows = json.loads(path.read_text()); row = rows[0]
                if mutation == "get": row["requestObservation"].update(method="GET", resourceType="Fetch"); row["responseObservation"]["resourceType"] = "Fetch"
                elif mutation == "missing-method": row["requestObservation"].pop("method")
                elif mutation == "wrong-request-type": row["requestObservation"]["resourceType"] = "Fetch"
                elif mutation == "script-request-type": row["requestObservation"]["resourceType"] = "Script"
                elif mutation == "wrong-response-type": row["responseObservation"]["resourceType"] = "Fetch"
                elif mutation == "missing-request": row.pop("requestObservation")
                elif mutation == "missing-response": row.pop("responseObservation")
                elif mutation == "request-id": row["requestObservation"]["requestId"] = "another"
                elif mutation == "response-id": row["responseObservation"]["requestId"] = "another"
                elif mutation == "request-url": row["requestObservation"]["url"] += "?unknown=1"
                elif mutation == "response-url": row["responseObservation"]["url"] += "?unknown=1"
                elif mutation == "foreign":
                    for value in (row, row["requestObservation"], row["responseObservation"]): value["url"] = value["url"].replace(":52001", ":52002")
                elif mutation == "wrong-session":
                    for value in (row, row["requestObservation"], row["responseObservation"]): value["url"] = value["url"].replace("session-1", "another")
                elif mutation == "unmatched": row["requestIdentityMatchesResponse"] = False
                elif mutation == "repeated-request": row["requestObservationCount"] = 2
                elif mutation == "repeated-response": row["responseObservationCount"] = 2
                elif mutation == "boolean-count": row["requestObservationCount"] = True
                elif mutation == "wrong-source": row["requestObservation"]["source"] = "inferred-from-status"
                elif mutation == "nonempty": row["rawBody"] = "body"
                elif mutation == "unobserved-body": row["receivedDataBytes"] = 1; row["dataEventCount"] = 1
                elif mutation == "missing-data-count": row.pop("receivedDataBytes")
                elif mutation == "failed-data-metadata": row["dataLengthMetadataError"] = "missing length"
                elif mutation == "capture-error": row["captureError"] = "missing capture"
                elif mutation == "validation-error": row["captureValidationError"] = "corrupt"
                elif mutation == "network-error": row["errorText"] = "net::ERR_FAILED"
                elif mutation == "canceled": row["canceled"] = True
                elif mutation == "open": row["termination"] = "open"
                elif mutation == "failed": row["termination"] = "loadingFailed"
                elif mutation == "http401": row["status"] = 401
                else: row["status"] = 200
                path.write_text(json.dumps(rows))
                with self.assertRaises(RuntimeError): self.verify(fixture)

    def test_failed_get_or_unknown_metadata_is_not_filtered_as_a_preflight(self):
        for mutation in ("401", "204", "500", "method", "resource-type", "request-id", "identity", "capture", "validation", "missing-byte-count", "malformed-content-type", "network", "nonterminal-abort", "open"):
            with self.subTest(mutation=mutation):
                fixture = self.with_preflights(self.fixture()); path = fixture[0] / "agent-streams.json"
                rows = json.loads(path.read_text()); row = rows[1]
                if mutation.isdigit(): row["status"] = int(mutation)
                elif mutation == "method": row["requestObservation"].pop("method")
                elif mutation == "resource-type": row["requestObservation"]["resourceType"] = row["responseObservation"]["resourceType"] = "Other"
                elif mutation == "request-id": row["requestId"] = "another"
                elif mutation == "identity": row["requestIdentityMatchesResponse"] = False
                elif mutation == "capture": row["captureError"] = "unavailable"
                elif mutation == "validation": row["captureValidationError"] = "corrupt"
                elif mutation == "missing-byte-count": row.pop("receivedDataBytes")
                elif mutation == "malformed-content-type": row["contentType"] = []
                elif mutation == "network": row.update(termination="loadingFailed", errorText="net::ERR_CONNECTION_RESET", canceled=False)
                elif mutation == "nonterminal-abort": row.update(termination="loadingFailed", errorText="net::ERR_ABORTED", canceled=True)
                else: row["termination"] = "open"
                path.write_text(json.dumps(rows))
                with self.assertRaises(RuntimeError): self.verify(fixture)

    def test_complete_native_approval_pause_abort_is_preserved_without_hiding_failed_get(self):
        for provider, key in (("copilot", "reviewed-size-control"), ("anthropic", "reviewed-size-control"), ("copilot", "automatic-to-manual")):
            fixture = self.fixture(provider, key); path = fixture[0] / "agent-streams.json"
            body = json.loads(path.read_text())[0]["rawBody"]
            split = body.index('data: {"id": "event-resume-message"')
            pause = self.stream(body[:split], "native-pause")
            pause.update(termination="loadingFailed", errorText="net::ERR_ABORTED", canceled=True)
            rows = [pause, self.stream(body[split:], "native-resume")]
            path.write_text(json.dumps(rows)); self.verify(fixture, provider)
            for mutation in ("wrong-error", "not-canceled", "incomplete-terminal", "missing-proposal"):
                with self.subTest(provider=provider, mutation=mutation):
                    changed = copy.deepcopy(rows)
                    if mutation == "wrong-error": changed[0]["errorText"] = "net::ERR_FAILED"
                    elif mutation == "not-canceled": changed[0]["canceled"] = False
                    elif mutation == "incomplete-terminal": changed[0]["rawBody"] = changed[0]["rawBody"].rstrip()
                    else:
                        blocks = changed[0]["rawBody"].split("\n\n")
                        changed[0]["rawBody"] = "\n\n".join(block for block in blocks if '"kind": "proposalCreated"' not in block)
                    path.write_text(json.dumps(changed))
                    with self.assertRaises(RuntimeError): self.verify(fixture, provider)

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

    def test_acknowledged_transitions_require_real_advertisement_and_single_call(self):
        for provider in ("copilot", "anthropic"):
            for key, mutation in [(key, mutation) for key in ("manual-to-automatic", "automatic-to-manual") for mutation in
                                  ("missing-call", "duplicate-call", "wrong-tool", "wrong-arguments", "not-advertised", "continuation", "foreign-command")]:
                with self.subTest(provider=provider, key=key, mutation=mutation):
                    fixture = self.fixture(provider, key)
                    rows = fixture[3]["records"]
                    selected = [row for row in rows if row["data"]["commandId"] == "command-size"]
                    call = next(row for row in selected if row["kind"] == provider + ".call")
                    if mutation == "missing-call": rows.remove(call)
                    elif mutation == "duplicate-call": rows.append(copy.deepcopy(call))
                    elif mutation == "wrong-tool": call["data"]["toolName"] = "other"
                    elif mutation == "wrong-arguments": call["data"]["arguments"] = {"size": "medium"}
                    elif mutation == "not-advertised": selected[0]["data"]["tools"] = []
                    elif mutation == "continuation": rows.append({"kind": provider + ".continuation", "data": {"commandId": "command-size", "sessionId": "session-1", "results": []}})
                    else: rows.append({"kind": provider + ".request", "data": {"commandId": "other", "tools": []}})
                    with self.assertRaises(RuntimeError): self.verify(fixture, provider)

    def test_provider_transport_auth_busy_and_error_frames_cannot_pass_a_transition(self):
        for mutation in ("browser-failure", "page-error", "sse-http", "sse-capture", "sse-error", "truncated-proposal", "duplicate-event", "trace-tail"):
            with self.subTest(mutation=mutation):
                fixture = self.fixture(key="automatic-to-manual")
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

    def test_acknowledged_review_cannot_claim_effect_or_use_another_prepared_target(self):
        for mutation in ("wrong-target", "stale", "no-approval", "wrong-proposal", "effect", "sdk-applied"):
            with self.subTest(mutation=mutation):
                fixture = self.fixture(key="automatic-to-manual")
                output, _, _, trace = fixture
                path = output / "agent-streams.json"; streams = json.loads(path.read_text())
                values = [json.loads(raw.removeprefix("data: ")) for raw in streams[0]["rawBody"].strip().split("\n\n")]
                proposal = next(row["payload"] for row in values if row["kind"] == "proposalCreated")
                if mutation == "wrong-target": proposal["preparedInvocation"]["target"]["resourceId"] = "other"
                elif mutation == "stale": proposal["preparedInvocation"]["target"]["revision"] = "rev-0"
                elif mutation == "no-approval": proposal["requiresApproval"] = False
                elif mutation == "wrong-proposal": next(row for row in values if row["kind"] == "toolApprovalRequested")["proposalId"] = "other"
                elif mutation == "effect":
                    result = json.loads((output / "autonomy-size-execute.json").read_text())["response"]["data"]["toolResult"]
                    result["payload"]["data"]["size"] = "medium"
                    values.append({"id": "forbidden-effect", "kind": "contributedResult", "resultKind": "moduleResult", "payload": result})
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
        for mutation in ("new-session", "source", "accepted-target", "relabel", "fake-ack", "same-request", "selection-request", "relabeled-policy"):
            with self.subTest(mutation=mutation):
                fixture = self.fixture(key="manual-to-automatic"); output, observation, _, _ = fixture
                if mutation == "new-session":
                    path = output / "agent-requests.json"; value = json.loads(path.read_text()); value.append(copy.deepcopy(value[0])); path.write_text(json.dumps(value))
                elif mutation == "source":
                    path = output / "autonomy-size-message.json"; value = json.loads(path.read_text()); value["response"]["data"]["message"]["context"][0]["contributorId"] = "other"; path.write_text(json.dumps(value))
                elif mutation == "accepted-target":
                    path = output / "autonomy-size-message.json"; value = json.loads(path.read_text()); value["response"]["data"]["message"]["context"][0]["target"]["revision"] = "other"; path.write_text(json.dumps(value))
                elif mutation == "relabel": observation["autonomy"]["expectedOutcome"] = "review"
                elif mutation == "fake-ack": observation["autonomy"]["policyAcknowledgementClaimed"] = False
                elif mutation == "selection-request": observation["autonomy"]["selectionRequests"] = [{"method": "PATCH", "url": "/unimplemented", "status": 404}]
                elif mutation == "relabeled-policy": observation["autonomy"]["effectivePolicy"] = {**observation["autonomy"]["effectivePolicy"], "revision": 0}
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

    def test_owner_changed_at_review_is_not_a_reviewed_outcome(self):
        fixture = self.fixture(key="automatic-to-manual")
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


    def edit(self, output, name, change):
        path = Path(output) / (name + ".json"); value = json.loads(path.read_text()); change(value); path.write_text(json.dumps(value))
        return retained(path)

    def test_transition_needs_exactly_one_acknowledged_put_with_the_created_revision(self):
        cases = {
            "missing-put": "Mode changes must be exactly one acknowledged PUT",
            "two-puts": "Mode changes must be exactly one acknowledged PUT",
            "put-after-size": "Mode changes must be exactly one acknowledged PUT",
            "wrong-revision-body": "only the requested mode and the revision acknowledged at creation",
            "wrong-mode-body": "only the requested mode and the revision acknowledged at creation",
            "extra-body-field": "only the requested mode and the revision acknowledged at creation",
            "foreign-session": "unrecognized native Agent request",
            "busy-409": "did not acknowledge the mode change with HTTP 200",
            "error-envelope": "did not acknowledge the mode change with HTTP 200",
            "raw-mismatch": "did not acknowledge the mode change with HTTP 200",
            "ack-revision": "requested mode at the next revision",
            "ack-mode": "requested mode at the next revision",
            "ack-ceiling": "requested mode at the next revision",
            "ack-extra-field": "acknowledgement carries no readable policy",
            "unretained": "actual autonomy exchange was not retained",
        }
        for key in ("automatic-to-manual", "manual-to-automatic"):
            for mutation, message in cases.items():
                with self.subTest(key=key, mutation=mutation):
                    fixture = self.fixture("anthropic", key); output, observation, _, _ = fixture
                    a = observation["autonomy"]
                    def requests(change): self.edit(output, "agent-requests", change)
                    def exchange(change):
                        a["policyUpdate"] = self.edit(output, "autonomy-mode-update", change)
                        http = output / "autonomy-mode-update-http.json"; raw = json.loads(http.read_text())
                        raw.update(status=json.loads(Path(a["policyUpdate"]["path"]).read_text())["status"],
                                   rawBody=json.dumps(json.loads(Path(a["policyUpdate"]["path"]).read_text())["response"]))
                        http.write_text(json.dumps(raw))
                    if mutation == "missing-put": requests(lambda value: value.pop(2))
                    elif mutation == "two-puts": requests(lambda value: value.insert(2, copy.deepcopy(value[2])))
                    elif mutation == "put-after-size": requests(lambda value: value.insert(3, value.pop(2)))
                    elif mutation in ("wrong-revision-body", "wrong-mode-body", "extra-body-field"):
                        def body(value):
                            changed = value[2]["body"] if isinstance(value, list) else value["request"]
                            if mutation == "wrong-revision-body": changed["expectedRevision"] = 7
                            elif mutation == "wrong-mode-body": changed["autonomyMode"] = "auto-read-only"
                            else: changed["actorId"] = "caller-selected"
                        requests(body); exchange(body)
                    elif mutation == "foreign-session": requests(lambda value: value[2].update(url=value[2]["url"].replace("session-1", "session-2")))
                    elif mutation == "busy-409": exchange(lambda value: value.update(status=409, response={"data": None, "error": {"code": "agent.session.busy"}}))
                    elif mutation == "error-envelope": exchange(lambda value: value["response"].update(error={"code": "agent.session.policy_stale"}))
                    elif mutation == "raw-mismatch": self.edit(output, "autonomy-mode-update-http", lambda value: value.update(rawBody="{}"))
                    elif mutation == "ack-revision": exchange(lambda value: value["response"]["data"].update(revision=2))
                    elif mutation == "ack-mode": exchange(lambda value: value["response"]["data"].update(autonomyMode="auto-read-only"))
                    elif mutation == "ack-ceiling": exchange(lambda value: value["response"]["data"].update(maxAutonomyMode="auto-read-only"))
                    elif mutation == "ack-extra-field": exchange(lambda value: value["response"]["data"].update(sessionId="session-1"))
                    else: a["policyUpdate"] = observation["ownerBefore"]
                    with self.assertRaisesRegex(RuntimeError, message): self.verify(fixture, "anthropic")

    def test_unchanged_control_with_any_autonomy_request_fails(self):
        for key in ("automatic-size-control", "reviewed-size-control"):
            for mutation in ("put", "retained-exchange", "network-put", "network-preflight", "claimed"):
                with self.subTest(key=key, mutation=mutation):
                    fixture = self.fixture("copilot", key); output, observation, _, _ = fixture
                    initial = MODE_CASES[key][0]
                    url = "http://127.0.0.1:52001/_elsa/agent/sessions/session-1/autonomy"
                    put = {"url": url, "method": "PUT", "body": {"autonomyMode": initial, "expectedRevision": 0}}
                    if mutation == "put":
                        self.edit(output, "agent-requests", lambda value: value.insert(2, put))
                        message = "exactly one acknowledged PUT"
                    elif mutation == "retained-exchange":
                        self.write(output, "autonomy-mode-update", {"url": url, "method": "PUT", "status": 200, "request": put["body"], "response": {}})
                        message = "unchanged control cannot carry a policy update"
                    elif mutation in ("network-put", "network-preflight"):
                        self.write(output, "autonomy-network", [{"requestId": "x", "method": "PUT" if mutation == "network-put" else "OPTIONS", "url": url}])
                        message = "Only one actual PUT and at most its CORS preflight"
                    else:
                        observation["autonomy"]["policyAcknowledgementClaimed"] = True
                        message = "relabeled the acknowledged policy"
                    with self.assertRaisesRegex(RuntimeError, message): self.verify(fixture)

    def test_autonomy_network_admits_only_the_exact_put_and_its_preflight(self):
        fixture = self.fixture("copilot", "automatic-to-manual"); output = fixture[0]
        self.edit(output, "autonomy-network", lambda value: value.pop(0)) # A cached/absent preflight is admitted.
        self.verify(fixture)
        for mutation in ("missing-put", "two-puts", "two-preflights", "put-xhr", "put-409", "put-failed", "put-canceled", "put-query",
                         "foreign-origin", "response-url", "repeated-request", "preflight-200", "preflight-initiator", "preflight-type",
                         "preflight-response-type", "preflight-open", "get"):
            with self.subTest(mutation=mutation):
                fixture = self.fixture("copilot", "automatic-to-manual"); output = fixture[0]
                def change(rows):
                    preflight, put = rows
                    if mutation == "missing-put": rows.pop()
                    elif mutation == "two-puts": rows.append(dict(put, requestId="put-2"))
                    elif mutation == "two-preflights": rows.append(dict(preflight, requestId="preflight-2"))
                    elif mutation == "put-xhr": put.update(resourceType="XHR", responseResourceType="XHR")
                    elif mutation == "put-409": put["status"] = 409
                    elif mutation == "put-failed": put.update(termination="loadingFailed", errorText="net::ERR_FAILED")
                    elif mutation == "put-canceled": put["canceled"] = True
                    elif mutation == "put-query": put["url"] = None
                    elif mutation == "foreign-origin": put["url"] = put["responseUrl"] = put["url"].replace(":52001", ":52002")
                    elif mutation == "response-url": put["responseUrl"] = None
                    elif mutation == "repeated-request": put["requestObservationCount"] = 2
                    elif mutation == "preflight-200": preflight["status"] = 200
                    elif mutation == "preflight-initiator": preflight["initiatorType"] = "script"
                    elif mutation == "preflight-type": preflight["resourceType"] = "Fetch"
                    elif mutation == "preflight-response-type": preflight["responseResourceType"] = "Fetch"
                    elif mutation == "preflight-open": preflight["termination"] = "open"
                    else: preflight["method"] = "GET"
                self.edit(output, "autonomy-network", change)
                with self.assertRaises(RuntimeError): self.verify(fixture)

    def test_readback_attribution_and_selector_must_name_the_acknowledged_revision(self):
        cases = {
            "create-policy-missing": "created session did not acknowledge a readable policy",
            "create-mode": "did not acknowledge the selected initial mode",
            "before-readback": "does not read back the acknowledged policy",
            "selected-readback": "does not read back the acknowledged policy",
            "final-readback": "does not read back the acknowledged policy",
            "final-old-revision": "does not read back the acknowledged policy",
            "read-not-terminal": "was not terminal in its owner",
            "size-accepted-old": "size prompt was not accepted under its acknowledged policy revision",
            "size-accepted-mode": "size prompt was not accepted under its acknowledged policy revision",
            "size-accepted-missing": "accepted size prompt carries no accepted policy",
            "size-derived-approval": "size prompt was not accepted under its acknowledged policy revision",
            "read-relabeled": "read prompt was not accepted under its acknowledged policy revision",
            "unrelated-policy": "altered unrelated accepted policy",
            "history-size": "Session history is not attributed",
            "history-read-relabeled": "Session history is not attributed",
            "history-missing": "Session history is not attributed",
            "selector-pending": "selector did not settle",
            "selector-label": "selector did not settle",
            "selector-status": "selector did not settle",
            "ui-relabeled": "Visible results are not attributed",
            "ui-mode": "Visible results are not attributed",
            "ui-missing": "Visible results are not attributed",
        }
        for mutation, message in cases.items():
            with self.subTest(mutation=mutation):
                fixture = self.fixture("copilot", "manual-to-automatic"); output, observation, _, _ = fixture
                a = observation["autonomy"]
                detail = lambda name, change: self.edit(output, name, lambda value: change(value["value"]["data"]))
                accepted = lambda label, change: self.edit(output, "autonomy-" + label + "-message", lambda value: change(value["response"]["data"]["message"]))
                if mutation == "create-policy-missing": self.edit(output, "autonomy-create-session", lambda value: value["response"]["data"].pop("policy"))
                elif mutation == "create-mode": self.edit(output, "autonomy-create-session", lambda value: value["response"]["data"]["policy"].update(autonomyMode="full-auto"))
                elif mutation == "before-readback": a["sessionBefore"] = detail("session-before", lambda value: value.update(policy=a["effectivePolicy"]))
                elif mutation == "selected-readback": a["sessionAfterSelection"] = detail("session-selected", lambda value: value.update(policy=a["createdPolicy"]))
                elif mutation == "final-readback": a["sessionAfter"] = detail("session-after", lambda value: value.pop("policy"))
                elif mutation == "final-old-revision": a["sessionAfter"] = detail("session-after", lambda value: value["policy"].update(revision=0))
                elif mutation == "read-not-terminal": a["sessionBefore"] = detail("session-before", lambda value: value["messages"][0].update(status="pending"))
                elif mutation == "size-accepted-old": accepted("size", lambda value: value["acceptedPolicy"].update(revision=0))
                elif mutation == "size-accepted-mode": accepted("size", lambda value: value["acceptedPolicy"]["policy"].update(autonomyMode="manual"))
                elif mutation == "size-accepted-missing": accepted("size", lambda value: value.pop("acceptedPolicy"))
                elif mutation == "size-derived-approval": accepted("size", lambda value: value["acceptedPolicy"]["policy"].update(requiresApprovalForMutations=True))
                elif mutation == "read-relabeled": accepted("read", lambda value: value["acceptedPolicy"].update(revision=1))
                elif mutation == "unrelated-policy": accepted("size", lambda value: value["acceptedPolicy"]["policy"].update(retentionLabel="changed"))
                elif mutation == "history-size": a["sessionAfter"] = detail("session-after", lambda value: value["messages"][1].update(acceptedPolicyRevision=0))
                elif mutation == "history-read-relabeled": a["sessionAfter"] = detail("session-after", lambda value: value["messages"][0].update(acceptedPolicyRevision=1))
                elif mutation == "history-missing": a["sessionAfter"] = detail("session-after", lambda value: value["messages"][2].pop("acceptedPolicyRevision"))
                elif mutation == "selector-pending": self.edit(output, "autonomy-mode-selector", lambda value: value.update(state="pending", pendingMode="full-auto"))
                elif mutation == "selector-label": self.edit(output, "autonomy-mode-selector", lambda value: value.update(label="Manual review"))
                elif mutation == "selector-status": self.edit(output, "autonomy-mode-selector", lambda value: value.update(statusCount=1))
                elif mutation == "ui-relabeled": self.edit(output, "autonomy-ui-attribution", lambda value: value[1].update(revision="1", autonomyMode="full-auto"))
                elif mutation == "ui-mode": self.edit(output, "autonomy-ui-attribution", lambda value: value[3].update(autonomyMode="manual"))
                else: self.edit(output, "autonomy-ui-attribution", lambda value: value[3].update(revision=None))
                with self.assertRaisesRegex(RuntimeError, message): self.verify(fixture)

    def test_mode_mismatch_or_expected_failure_is_now_a_real_failure(self):
        for mutation in ("outcome", "status", "expected-failure", "actual", "failure", "not-applied"):
            with self.subTest(mutation=mutation):
                fixture = self.fixture("copilot", "automatic-to-manual"); observation = fixture[1]
                if mutation == "outcome": observation["autonomy"]["outcomeAtDecision"] = "automatic"
                elif mutation == "status": observation["status"] = "failed"
                elif mutation == "expected-failure": observation["expectedFailureObserved"] = True
                elif mutation == "actual": observation["actual"] = {"stage": "session-mode", "diagnostic": "autonomy.mode_mismatch"}
                elif mutation == "failure": observation["failure"] = {"type": "AutonomyFailure", "cause": "autonomy.mode_mismatch"}
                else: observation["autonomy"]["applied"] = False
                with self.assertRaises(RuntimeError): self.verify(fixture)


class NativeRequestMetadataCaptureTests(unittest.TestCase):
    def test_actual_capture_retains_request_response_identity_and_counts_non_sse_body(self):
        source = Path(__file__).with_name("browser.mjs").read_text()
        helper = source[source.index("async function captureNativeStreams("):source.index("function command(")]
        script = '''import assert from "node:assert/strict";
const backend="http://127.0.0.1:52001";let context;
const diagnosticUrl=value=>{const url=new URL(value);return url.origin+url.pathname;};
''' + helper + r'''
const observations=[];
for(const scenario of ['valid','missing-request','method-conflict','url-conflict','nonempty-preflight','missing-data-length','wrong-response-id','resource-missing','query']) {
 const handlers=new Map(),raw='data: {"id":"native-unit","kind":"completed"}\n\n';
 const cdp={on:(kind,callback)=>handlers.set(kind,callback),send:async(kind,args)=>{
  if(kind==='Network.enable')return {};
  assert.equal(kind,'Network.streamResourceContent');assert.equal(args.requestId,'native-get');
  return {bufferedData:Buffer.from(raw).toString('base64')};
 }};
 context={newCDPSession:async()=>cdp};const capture=await captureNativeStreams({});
 const url=backend+'/_elsa/agent/sessions/session-1/stream';
 const query=scenario==='query'?'?access_token=private-query-value':'';
 if(scenario!=='missing-request')handlers.get('Network.requestWillBeSent')({requestId:'native-preflight',
  type:scenario==='resource-missing'?undefined:'Preflight',initiator:{type:'preflight'},
  request:{url:(scenario==='url-conflict'?url.replace('session-1','another'):url)+query,
    method:scenario==='method-conflict'?'GET':'OPTIONS',headers:{Authorization:'private-metadata-header'}}});
 const id=scenario==='wrong-response-id'?'unknown-request':'native-preflight';
 handlers.get('Network.responseReceived')({requestId:id,type:'Preflight',response:{url:url+query,status:204,charset:'',mimeType:'',
  headers:{Authorization:'private-metadata-header'}}});
 if(scenario==='nonempty-preflight')handlers.get('Network.dataReceived')({requestId:id,dataLength:1});
 if(scenario==='missing-data-length')handlers.get('Network.dataReceived')({requestId:id});
 handlers.get('Network.loadingFinished')({requestId:id});
 handlers.get('Network.requestWillBeSent')({requestId:'native-get',type:'Fetch',initiator:{type:'script'},request:{url,method:'GET',headers:{Authorization:'private-metadata-header'}}});
 handlers.get('Network.responseReceived')({requestId:'native-get',type:'Fetch',response:{url,status:200,charset:'utf-8',mimeType:'text/event-stream',headers:{'Content-Type':'text/event-stream',Authorization:'private-metadata-header'}}});
 handlers.get('Network.loadingFinished')({requestId:'native-get'});
 const rows=await capture();assert.equal(rows.length,2);
 assert.equal(rows[1].requestObservation.method,'GET');assert.equal(rows[1].requestObservation.resourceType,'Fetch');
 assert.equal(rows[1].responseObservation.resourceType,'Fetch');assert.equal(rows[1].requestIdentityMatchesResponse,true);
 assert.equal(rows[1].rawBody,raw);
 if(scenario==='valid') { assert.equal(rows[0].requestObservation.method,'OPTIONS');assert.equal(rows[0].requestIdentityMatchesResponse,true); }
 if(scenario==='method-conflict')assert.equal(rows[0].requestObservation.method,'GET');
 if(scenario==='nonempty-preflight')assert.equal(rows[0].receivedDataBytes,1);
 if(scenario==='missing-data-length')assert.ok(rows[0].dataLengthMetadataError);
 if(scenario==='missing-request')assert.equal(rows[0].requestObservation,null);
 if(scenario==='url-conflict'||scenario==='query')assert.equal(rows[0].requestIdentityMatchesResponse,false);
 assert.ok(!JSON.stringify(rows).includes('private-metadata-header'));assert.ok(!JSON.stringify(rows).includes('private-query-value'));
 observations.push({scenario,rows});
}
console.log(JSON.stringify(observations));
'''
        completed = subprocess.run(["node", "--input-type=module", "-e", script], capture_output=True, text=True, timeout=60)
        self.assertEqual(0, completed.returncode, completed.stderr)
        values = json.loads(completed.stdout)
        self.assertEqual(9, len(values))
        with tempfile.TemporaryDirectory() as directory:
            for value in values:
                with self.subTest(scenario=value["scenario"]):
                    path = Path(directory) / "agent-streams.json"; path.write_text(json.dumps(value["rows"]))
                    if value["scenario"] == "valid":
                        frames, _ = _frames(Path(directory), "session-1", "http://127.0.0.1:52001")
                        self.assertEqual(["native-unit"], [row["id"] for row in frames])
                    else:
                        with self.assertRaises(RuntimeError): _frames(Path(directory), "session-1", "http://127.0.0.1:52001")


def complete_autonomy_rows(ref, source="frozen"):
    """Synthetic shapes of the actual passed records (mode and lifecycle) for every inventory case."""
    from lifecycle_journeys import CORRELATION_KEYS, EXPECTED_OUTCOMES
    rows = []
    for identity in sorted(EXPECTED_CASES):
        provider, key = identity.split("-", 1)
        row = {"id": identity, "provider": provider, "caseKey": key, "status": "passed", "cause": None, "modeMismatch": False,
               "nativeEvidenceVerified": True, "qualified": False, "source": source, "ownerBefore": ref, "ownerAfter": ref, "evidence": [ref]}
        if key in MODE_CASES:
            initial, requested = MODE_CASES[key]
            transition = initial != requested
            row.update(sessionId="session", initialMode=initial, requestedMode=requested, outcomeAtDecision=MODE_OUTCOMES[key],
                       correlation={"kind": "completed-native-result", "completeProviderJourney": True},
                       policy={"acknowledged": transition, "updateRequests": int(transition)})
        else:
            row.update(adapter=provider, outcome=copy.deepcopy(EXPECTED_OUTCOMES[key]),
                       correlation={"adapter": provider, "providerTrace": ref, **{name: "id" for name in CORRELATION_KEYS[key]}})
        rows.append(row)
    return rows


def complete_autonomy_section(ref, source="frozen"):
    return {"status": "passed", "scope": "complete-package-witness-matrix", "source": source, "evidenceCategory": EVIDENCE_CATEGORY,
            "liveProvider": False, "qualified": False, "independentPackageWitness": False,
            "expectedCaseIds": sorted(EXPECTED_CASES), "cases": complete_autonomy_rows(ref, source)}


class AutonomyManifestTests(unittest.TestCase):
    @staticmethod
    def final_section():
        return complete_autonomy_section({"synthetic": True})

    def test_exact_final_manifest_is_twenty_six_and_mode_eight_is_incomplete(self):
        self.assertEqual(26, len(EXPECTED_CASES)); self.assertEqual(8, len(MODE_CASE_IDS))
        section = self.final_section(); require_complete_autonomy(section)
        section["cases"] = [row for row in section["cases"] if row["id"] in MODE_CASE_IDS]
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

    def test_final_gate_refuses_wrong_cause_missing_owner_correlation_and_promotion(self):
        def lifecycle(section, key="stale-target-continuation"):
            return next(row for row in section["cases"] if row["caseKey"] == key)
        def mode(section, key="reviewed-size-control"):
            return next(row for row in section["cases"] if row["caseKey"] == key)
        mutations = {
            "lifecycle-wrong-cause": lambda s: lifecycle(s)["outcome"].update(execute="agent.proposal.approval_required"),
            "lifecycle-wrong-adapter": lambda s: lifecycle(s).update(adapter="anthropic" if lifecycle(s)["provider"] == "copilot" else "copilot"),
            "lifecycle-no-correlation": lambda s: lifecycle(s)["correlation"].pop("invocationId"),
            "lifecycle-no-trace": lambda s: lifecycle(s)["correlation"].pop("providerTrace"),
            "mode-wrong-outcome": lambda s: mode(s).update(outcomeAtDecision="automatic"),
            "mode-wrong-requested": lambda s: mode(s).update(requestedMode="full-auto"),
            "mode-unacknowledged": lambda s: mode(s, "manual-to-automatic")["policy"].update(acknowledged=False),
            "mode-boolean-update-count": lambda s: mode(s, "manual-to-automatic")["policy"].update(updateRequests=True),
            "mode-no-correlation": lambda s: mode(s).pop("correlation"),
            "no-owner": lambda s: mode(s).pop("ownerAfter"),
            "failed-with-cause": lambda s: lifecycle(s).update(status="failed", cause="lifecycle.refusal_cause"),
            "passed-with-cause": lambda s: lifecycle(s).update(cause="lifecycle.refusal_cause"),
            "self-promoted-row": lambda s: lifecycle(s).update(qualified=True),
            "self-promoted-section": lambda s: s.update(independentPackageWitness=True),
            "foreign-source": lambda s: lifecycle(s).update(source="other"),
            "undeclared-inventory": lambda s: s.update(expectedCaseIds=sorted(MODE_CASE_IDS)),
            "extra": lambda s: s["cases"].append(dict(copy.deepcopy(s["cases"][0]), id="copilot-invented-case", caseKey="invented-case")),
            "running": lambda s: s.update(status="running"),
        }
        for name, mutate in mutations.items():
            with self.subTest(mutation=name):
                section = self.final_section(); mutate(section)
                self.assertTrue(autonomy_manifest_gaps(section, "frozen"))
                with self.assertRaises(RuntimeError): require_complete_autonomy(section, source="frozen")
        self.assertEqual([], autonomy_manifest_gaps(self.final_section(), "frozen"))

    def test_gaps_name_each_missing_and_duplicated_case_on_its_actual_adapter(self):
        section = self.final_section()
        section["cases"] = [row for row in section["cases"] if row["id"] != "anthropic-busy-policy-lifecycle"]
        section["cases"].append(copy.deepcopy(section["cases"][0]))
        gaps = autonomy_manifest_gaps(section, "frozen")
        self.assertIn("Missing autonomy case: anthropic-busy-policy-lifecycle", gaps)
        self.assertIn("Duplicated autonomy case: " + section["cases"][0]["id"], gaps)

    def test_matrix_stages_are_derived_from_the_manifest(self):
        stages = autonomy_stages()
        self.assertEqual(14, len(stages))
        self.assertEqual({"autonomy-" + p + "-" + k for p in ("copilot", "anthropic") for k in MODE_CASES},
                         {name for name in stages if name.startswith("autonomy-")})
        self.assertEqual({"lifecycle-" + p + "-" + k for p in ("copilot", "anthropic")
                          for k in ("interrupted-after-effect", "visibility-review-control", "workflow-review-control")},
                         {name for name in stages if name.startswith("lifecycle-")})

    @staticmethod
    def mode_records():
        records = []
        for identity in sorted(MODE_CASE_IDS):
            provider, key = identity.split("-", 1)
            transition = "-to-" in key
            records.append({"id": identity, "provider": provider, "caseKey": key, "status": "passed", "cause": None, "modeMismatch": False,
                            "nativeEvidenceVerified": True, "qualified": False, "evidence": [{"synthetic": True}],
                            "policy": {"acknowledged": transition, "updateRequests": int(transition)}})
        return records

    def test_mode_qualification_needs_all_eight_passed_with_acknowledgement(self):
        records = self.mode_records(); require_mode_qualification(records)
        transition = next(index for index, row in enumerate(records) if "-to-" in row["caseKey"])
        control = next(index for index, row in enumerate(records) if "-to-" not in row["caseKey"])
        for mutation in ("missing", "duplicate", "failed", "mismatch", "cause", "unverified", "qualified", "no-evidence",
                         "unacknowledged-transition", "missing-update", "two-updates", "control-update", "control-acknowledged"):
            with self.subTest(mutation=mutation):
                changed = copy.deepcopy(records)
                if mutation == "missing": changed.pop()
                elif mutation == "duplicate": changed[-1] = copy.deepcopy(changed[0])
                elif mutation == "failed": changed[0]["status"] = "failed"
                elif mutation == "mismatch": changed[0].update(status="failed", cause="autonomy.mode_mismatch", modeMismatch=True)
                elif mutation == "cause": changed[0]["cause"] = "timeout"
                elif mutation == "unverified": changed[0]["nativeEvidenceVerified"] = False
                elif mutation == "qualified": changed[0]["qualified"] = True
                elif mutation == "no-evidence": changed[0]["evidence"] = []
                elif mutation == "unacknowledged-transition": changed[transition]["policy"]["acknowledged"] = False
                elif mutation == "missing-update": changed[transition]["policy"]["updateRequests"] = 0
                elif mutation == "two-updates": changed[transition]["policy"]["updateRequests"] = 2
                elif mutation == "control-update": changed[control]["policy"]["updateRequests"] = 1
                else: changed[control]["policy"]["acknowledged"] = True
                with self.assertRaises(RuntimeError): require_mode_qualification(changed)

    @staticmethod
    def qualification_runtime(directory):
        original = [{"id": "module-add", "status": "passed"}]
        run = SimpleNamespace(root=Path(directory), report={"cases": copy.deepcopy(original), "independentPackageWitness": False},
                              source="frozen", save=Mock(), assert_immutable_inputs=Mock())
        return SimpleNamespace(run=run, select_provider=Mock()), original

    def test_all_eight_passed_cases_complete_mode_qualification_without_package_witness(self):
        records = {row["id"]: row for row in self.mode_records()}
        with tempfile.TemporaryDirectory() as directory:
            runtime, original = self.qualification_runtime(directory)
            with patch("autonomy_journeys.execute_mode_case", side_effect=lambda _runtime, provider, key: copy.deepcopy(records[provider + "-" + key])):
                section = qualify_autonomy_modes(runtime)
            self.assertEqual("passed", section["status"])
            self.assertEqual(8, len(section["cases"]))
            self.assertFalse(section["qualified"]); self.assertFalse(section["independentPackageWitness"])
            self.assertEqual(original, runtime.run.report["cases"])
            self.assertFalse(runtime.run.report["independentPackageWitness"])
            self.assertIs(section, runtime.run.report[MODE_QUALIFICATION])
            self.assertEqual("passed", json.loads((Path(directory) / "autonomy-mode-qualification.json").read_text())["status"])
            self.assertEqual(["copilot", "anthropic"], [call.args[0] for call in runtime.select_provider.call_args_list])

    def test_failed_case_keeps_its_precise_cause_runs_the_rest_and_fails_qualification(self):
        records = {row["id"]: row for row in self.mode_records()}
        def execute(_runtime, provider, key):
            if provider == "copilot" and key == "automatic-to-manual":
                raise ModeCaseFailure("browser failed", "autonomy.update_refused", {"path": "/observation.json", "sha256": "0"})
            if provider == "anthropic" and key == "manual-to-automatic":
                raise RuntimeError("Session detail does not read back the acknowledged policy")
            return copy.deepcopy(records[provider + "-" + key])
        with tempfile.TemporaryDirectory() as directory:
            runtime, original = self.qualification_runtime(directory)
            with patch("autonomy_journeys.execute_mode_case", side_effect=execute), self.assertRaises(RuntimeError):
                qualify_autonomy_modes(runtime)
            section = runtime.run.report[MODE_QUALIFICATION]
            self.assertEqual("failed", section["status"])
            self.assertEqual(8, len(section["cases"]))
            failed = {row["id"]: row for row in section["cases"] if row["status"] == "failed"}
            self.assertEqual({"copilot-automatic-to-manual": "autonomy.update_refused", "anthropic-manual-to-automatic": "verification"},
                             {identity: row["cause"] for identity, row in failed.items()})
            self.assertEqual({"path": "/observation.json", "sha256": "0"}, failed["copilot-automatic-to-manual"]["observation"])
            self.assertTrue(all(row["nativeEvidenceVerified"] is False and row["qualified"] is False for row in failed.values()))
            self.assertEqual(original, runtime.run.report["cases"])
            self.assertFalse(runtime.run.report["independentPackageWitness"])
            self.assertEqual("failed", json.loads((Path(directory) / "autonomy-mode-qualification.json").read_text())["status"])

    def test_browser_failure_is_raised_with_its_precise_retained_cause(self):
        for failure, cause in (({"type": "AutonomyFailure", "message": "HTTP 409 agent.session.busy", "cause": "autonomy.update_refused"}, "autonomy.update_refused"),
                               ({"type": "TimeoutError", "message": "locator timeout"}, "browser.failed"), (None, "browser.failed")):
            with self.subTest(cause=cause, failure=failure), tempfile.TemporaryDirectory() as directory:
                root = Path(directory); trace = root / "trace.jsonl"; trace.write_bytes(b"")
                def run(name, command, cwd, env=None):
                    output = Path(json.loads(Path(command[-1]).read_text())["output"]); output.mkdir()
                    if failure is not None:
                        (output / "observation.json").write_text(json.dumps({"status": "failed", "failure": failure}))
                    raise RuntimeError(name + " failed (1)")
                def output(name):
                    path = root / name; path.mkdir(); return path
                runtime = SimpleNamespace(provider="copilot", run=SimpleNamespace(assert_immutable_inputs=Mock(), run=run, root=root, source="frozen"),
                    sign_in=Mock(), output=output, studio_origin="http://127.0.0.1:52002", backend_origin="http://127.0.0.1:52001",
                    active_identity={"storageState": str(root / "state.json")}, command_file=root / "command.json",
                    transport=lambda _name: SimpleNamespace(trace_file=trace), witness={})
                with self.assertRaises(ModeCaseFailure) as raised: execute_mode_case(runtime, "copilot", "automatic-to-manual")
                self.assertEqual(cause, raised.exception.cause)
                self.assertEqual(failure is not None, raised.exception.observation is not None)
                if failure: self.assertIn(failure["message"], str(raised.exception))

    def test_mode_mismatch_failure_is_a_real_failed_case(self):
        records = {row["id"]: row for row in self.mode_records()}
        def execute(_runtime, provider, key):
            if key == "manual-to-automatic": raise ModeCaseFailure("mismatch", "autonomy.mode_mismatch")
            return copy.deepcopy(records[provider + "-" + key])
        with tempfile.TemporaryDirectory() as directory:
            runtime, _ = self.qualification_runtime(directory)
            with patch("autonomy_journeys.execute_mode_case", side_effect=execute), self.assertRaises(RuntimeError):
                qualify_autonomy_modes(runtime)
            failed = [row for row in runtime.run.report[MODE_QUALIFICATION]["cases"] if row["status"] == "failed"]
            self.assertEqual(2, len(failed))
            self.assertTrue(all(row["modeMismatch"] is True and row["cause"] == "autonomy.mode_mismatch" for row in failed))

    def test_immutable_input_drift_stops_the_matrix_and_never_promotes_package_witness(self):
        with tempfile.TemporaryDirectory() as directory:
            runtime, original = self.qualification_runtime(directory)
            runtime.run.assert_immutable_inputs.side_effect = RuntimeError("inputs changed")
            with patch("autonomy_journeys.execute_mode_case", side_effect=RuntimeError("native-failure")), self.assertRaisesRegex(RuntimeError, "inputs changed"):
                qualify_autonomy_modes(runtime)
            section = runtime.run.report[MODE_QUALIFICATION]
            self.assertEqual(1, len(section["cases"]))
            self.assertEqual("failed", section["status"])
            self.assertEqual(original, runtime.run.report["cases"])
            self.assertFalse(runtime.run.report["independentPackageWitness"])


if __name__ == "__main__":
    unittest.main()
