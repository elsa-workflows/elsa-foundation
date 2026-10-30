"""Synthetic retained-file controls for evidence helpers; never native/provider proof."""
import copy
import json
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock

from agent_transport import AgentTransport
from case_records import CaseRecords, correlate_browser_protocol, retained
from evidence import CONTROL_IDS, REQUIRED_CASES


class CaseRecordTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.run = SimpleNamespace(root=self.root, source="a" * 40, report={"cases": [], "status": "running"}, save=Mock())
        self.expected = {name: {"stage": "native-execution", "diagnostic": "synthetic.expected." + name} for name in CONTROL_IDS}
        self.records = CaseRecords(self.run, self.expected)

    def tearDown(self):
        self.temporary.cleanup()

    def write(self, name, value):
        path = self.root / name
        path.write_text(json.dumps(value))
        return retained(path)

    def observation(self, name="write-denied"):
        owner = self.write("owner.json", {"syntheticState": "unchanged"})
        return {"status": "failed", "expectedFailureObserved": True, "actual": copy.deepcopy(self.expected[name]),
                "observations": dict.fromkeys(REQUIRED_CASES[name], True), "ownerBefore": owner, "ownerAfter": owner,
                "evidence": [self.write("wire.json", {"syntheticSelfTest": True})]}

    def test_completed_control_keeps_failed_status_and_original_observation(self):
        observation = self.observation(); before = copy.deepcopy(observation)
        entry = self.records.add("write-denied", observation)
        self.assertEqual(observation, before)
        self.assertEqual(entry["status"], "failed")
        self.assertTrue(entry["expectedFailureObserved"])
        self.assertEqual(json.loads(Path(entry["receipt"]["path"]).read_text())["diagnostic"], self.expected["write-denied"]["diagnostic"])
        self.assertEqual(self.run.report["status"], "running")

    def test_wrong_cause_unfinished_and_unrecognized_controls_never_enter_index(self):
        for mutation in (lambda value: value.update(status="pending"), lambda value: value.update(status="passed"),
                         lambda value: value.update(actual={"stage": "restore", "diagnostic": "network-timeout"}),
                         lambda value: value.update(expectedFailureObserved=False),
                         lambda value: value.update(failure={"message": "unexpected failure"})):
            with self.subTest(mutation=mutation):
                value = self.observation(); mutation(value)
                with self.assertRaises(RuntimeError): self.records.add("write-denied", value)
                self.assertEqual(self.run.report["cases"], [])
                self.assertFalse((self.records.root / "write-denied.json").exists())
        self.run.save.assert_not_called()

    def test_missing_changed_and_foreign_retained_files_are_rejected(self):
        for kind in ("missing", "changed", "foreign"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as foreign:
                value = self.observation(); path = Path(value["evidence"][0]["path"])
                if kind == "missing": path.unlink()
                elif kind == "changed": path.write_text("different retained bytes")
                else:
                    path = Path(foreign) / "wire.json"; path.write_text("{}")
                    value["evidence"] = [retained(path)]
                with self.assertRaises(RuntimeError): self.records.add("write-denied", value)
        self.assertEqual(self.run.report["cases"], [])

    def test_changed_owner_and_duplicate_case_cannot_be_relabelled(self):
        value = self.observation(); value["ownerAfter"] = self.write("changed-owner.json", {"syntheticState": "changed"})
        with self.assertRaises(RuntimeError): self.records.add("write-denied", value)
        value = self.observation(); entry = self.records.add("write-denied", value)
        receipt_bytes = Path(entry["receipt"]["path"]).read_bytes()
        with self.assertRaises(RuntimeError): self.records.add("write-denied", value)
        self.assertEqual(len(self.run.report["cases"]), 1)
        self.assertEqual(Path(entry["receipt"]["path"]).read_bytes(), receipt_bytes)
        self.run.save.assert_called_once()


class ProtocolCorrelationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.root = Path(self.temporary.name); self.sequence = 0

    def tearDown(self):
        self.temporary.cleanup()

    def fixture(self, provider="copilot", *, serialized=False, read=False):
        self.sequence += 1; root = self.root / str(self.sequence); root.mkdir()
        output = root / "browser"; output.mkdir()
        command = {"commandId": "driver-command", "toolName": "sample_change", "arguments": {"opaque": [False, 3]}}
        call_id = "native-call" if provider == "copilot" else command["commandId"]
        native = {"toolCallId": call_id, "toolName": command["toolName"], "succeeded": True, "content": "Synthetic unit observation",
                  "payload": {"typeId": "sample.result", "schemaVersion": "1", "data": {"opaque": True}},
                  "receipt": {"invocationId": "invocation", "scope": "serverPersisted", "before": {"revision": "r1"}, "after": {"revision": "r2"}}}
        if read: native["receipt"] = None
        adapter_result = copy.deepcopy(native)
        if not read: adapter_result["receipt"]["scope"] = "ServerPersisted"
        row = lambda kind, **data: {"kind": provider + "." + kind, "data": {"commandId": command["commandId"], **data}}
        rows = [row("request", sessionId="session-a", tools=[{"name": command["toolName"], "jsonSchema": {"type": "object"}}]),
                row("call", toolName=command["toolName"], arguments=copy.deepcopy(command["arguments"]))]
        if provider == "copilot":
            rows += ([row("return", result=adapter_result)] if read else [
                row("return", result={"status": "awaiting-approval", "executed": False, "proposalId": "proposal"}),
                row("continuation", sessionId="session-a", results=[adapter_result])])
        else:
            rows += [row("continuation", results=[{"kind": "functionResult", "callId": command["commandId"],
                                                    "result": json.dumps(adapter_result) if serialized else adapter_result}])]
        trace = root / "trace.jsonl"
        transport = AgentTransport(SimpleNamespace(bearer=None), root / "transport", trace_file=trace)
        response = {"url": "http://127.0.0.1:49123/_elsa/agent/proposals/proposal/execute", "status": 200,
                    "response": {"data": {"proposalId": "proposal", "executed": True, "toolResult": native}, "error": None}}
        event = {"id": "native-event", "kind": "contributedResult", "resultKind": "moduleResult", "error": None,
                 "payload": copy.deepcopy(native)}
        stream = {"url": "http://127.0.0.1:49123/_elsa/agent/sessions/session-a/stream", "status": 200,
                  "contentType": "text/event-stream", "termination": "loadingFailed", "canceled": True,
                  "errorText": "net::ERR_ABORTED", "captureError": None, "rawBody": "data: " + json.dumps(event) + "\n\n"}
        fixture = SimpleNamespace(output=output, command=command, rows=rows, trace=trace, transport=transport,
                                  response=response, streams=[stream] if read else [])
        self.sync(fixture)
        return fixture

    def sync(self, fixture):
        (fixture.output / "command-one.json").write_text(json.dumps(fixture.command))
        (fixture.output / "agent-requests.json").write_text(json.dumps([{"url": "http://127.0.0.1:49123/_elsa/agent/sessions/session-a/messages"}]))
        # Real browser runs retain successful owner HTTP sidecars alongside execute
        # responses. Their status200 must not be mistaken for an Agent envelope.
        (fixture.output / "owner-http.json").write_text(json.dumps({"status": 200, "rawBody": '{"owner":"synthetic"}'}))
        (fixture.output / "execute.json").write_text(json.dumps(fixture.response))
        (fixture.output / "agent-streams.json").write_text(json.dumps(fixture.streams))
        fixture.trace.write_text("".join(json.dumps(row) + "\n" for row in fixture.rows))

    def correlate(self, fixture, provider="copilot"):
        return correlate_browser_protocol(fixture.transport, fixture.output, provider)

    def test_correlated_native_ids_are_distinct_from_copilot_command_ids(self):
        fixture = self.fixture(); result = self.correlate(fixture)
        proof = json.loads(Path(result["receipt"]["path"]).read_text())
        self.assertEqual(proof["correlations"][0]["nativeToolCallId"], "native-call")
        self.assertFalse(proof["liveProvider"])

    def test_wrong_session_call_and_native_result_are_rejected(self):
        for kind in ("session", "continuation-session", "call-id", "native-result"):
            with self.subTest(kind=kind):
                fixture = self.fixture()
                if kind == "session": fixture.rows[0]["data"]["sessionId"] = "another-session"
                elif kind == "continuation-session": fixture.rows[-1]["data"]["sessionId"] = "another-session"
                elif kind == "call-id": fixture.rows[-1]["data"]["results"][0]["toolCallId"] = "another-call"
                else: fixture.response["response"]["data"]["toolResult"]["payload"]["data"]["opaque"] = "changed"
                self.sync(fixture)
                with self.assertRaises(RuntimeError): self.correlate(fixture)

    def test_repeated_command_files_or_actual_calls_and_partial_trace_are_rejected(self):
        for kind in ("command", "call", "request", "partial"):
            with self.subTest(kind=kind):
                fixture = self.fixture()
                if kind in ("call", "request"): fixture.rows.append(copy.deepcopy(fixture.rows[1 if kind == "call" else 0]))
                self.sync(fixture)
                if kind == "command": (fixture.output / "command-two.json").write_text(json.dumps(fixture.command))
                if kind == "partial":
                    with fixture.trace.open("a") as stream: stream.write('{"incomplete":')
                with self.assertRaises(RuntimeError): self.correlate(fixture)

    def test_anthropic_source_shaped_json_string_result_correlates(self):
        # AnthropicAgentProvider.ToolResults constructs FunctionResultContent with a
        # serialized JSON string. This synthetic input tests that wire shape only.
        fixture = self.fixture("anthropic", serialized=True)
        self.assertIn("receipt", self.correlate(fixture, "anthropic"))

    def test_both_provider_read_shapes_match_complete_native_frame_in_aborted_stream(self):
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                fixture = self.fixture(provider, serialized=True, read=True)
                self.assertIn("receipt", self.correlate(fixture, provider))

    def test_provider_read_payload_cannot_change_while_native_read_stays_unchanged(self):
        for provider in ("copilot", "anthropic"):
            for field in ("payload", "content"):
                with self.subTest(provider=provider, field=field):
                    fixture = self.fixture(provider, serialized=True, read=True)
                    native_bytes = fixture.streams[0]["rawBody"]
                    result = (fixture.rows[-1]["data"]["result"] if provider == "copilot" else
                              json.loads(fixture.rows[-1]["data"]["results"][0]["result"]))
                    if field == "payload": result["payload"]["data"]["opaque"] = 1  # True is not JSON 1.
                    else: result["content"] = "changed provider result"
                    if provider == "anthropic": fixture.rows[-1]["data"]["results"][0]["result"] = json.dumps(result)
                    self.sync(fixture)
                    self.assertEqual(fixture.streams[0]["rawBody"], native_bytes)
                    with self.assertRaises(RuntimeError): self.correlate(fixture, provider)

    def test_read_without_matching_native_envelope_identity_or_complete_frame_is_rejected(self):
        for kind in ("missing", "call-id", "tool-name", "session", "origin", "incomplete", "capture-error"):
            with self.subTest(kind=kind):
                fixture = self.fixture(read=True)
                if kind == "missing": fixture.streams = []
                elif kind in ("session", "origin"):
                    fixture.streams[0]["url"] = fixture.streams[0]["url"].replace(
                        "session-a" if kind == "session" else "49123", "session-b" if kind == "session" else "49124")
                elif kind == "incomplete": fixture.streams[0]["rawBody"] = fixture.streams[0]["rawBody"].rstrip("\n")
                elif kind == "capture-error": fixture.streams[0]["captureError"] = "unsupported CDP capture"
                else:
                    event = json.loads(fixture.streams[0]["rawBody"][6:])
                    event["payload"]["toolCallId" if kind == "call-id" else "toolName"] = "another-native-identity"
                    fixture.streams[0]["rawBody"] = "data: " + json.dumps(event) + "\n\n"
                self.sync(fixture)
                with self.assertRaises(RuntimeError): self.correlate(fixture)

    def test_anthropic_malformed_nonobject_and_double_encoded_results_are_rejected(self):
        for value in ("{broken", "null", '{"toolName":"sample_change","toolName":"duplicate"}', json.dumps("{}"), {}):
            with self.subTest(value=value):
                fixture = self.fixture("anthropic", serialized=True)
                fixture.rows[-1]["data"]["results"][0]["result"] = value
                self.sync(fixture)
                with self.assertRaises(RuntimeError): self.correlate(fixture, "anthropic")

    def test_json_boolean_is_not_interchangeable_with_a_number_in_opaque_results(self):
        for kind in ("result", "arguments"):
            with self.subTest(kind=kind):
                fixture = self.fixture()
                if kind == "result": fixture.response["response"]["data"]["toolResult"]["payload"]["data"]["opaque"] = 1
                else: fixture.rows[1]["data"]["arguments"]["opaque"][0] = 0
                self.sync(fixture)
                with self.assertRaises(RuntimeError): self.correlate(fixture)

    def test_failed_http_or_unsuccessful_native_result_cannot_qualify(self):
        for kind in ("http-refusal", "unsuccessful-result"):
            with self.subTest(kind=kind):
                fixture = self.fixture()
                if kind == "http-refusal": fixture.response["status"] = 503
                else:
                    fixture.response["response"]["data"]["toolResult"]["succeeded"] = False
                    fixture.rows[-1]["data"]["results"][0]["succeeded"] = False
                self.sync(fixture)
                with self.assertRaises(RuntimeError): self.correlate(fixture)


class BrowserCaptureTests(unittest.TestCase):
    def test_buffer_prefix_and_streaming_chunks_preserve_aborted_bytes_once(self):
        # Exercise the actual capture helper with synthetic CDP notifications.
        # This checks its byte boundary only, never a native browser journey.
        source = Path(__file__).with_name("browser.mjs").read_text()
        helper = source[source.index("async function captureNativeStreams("):source.index("function command(")]
        script = r'''
import assert from "node:assert/strict";
const backend = "http://127.0.0.1:49123", handlers = new Map();
let deliverPrefix;
const cdp = { on: (kind, callback) => handlers.set(kind, callback), send: async kind => {
  if (kind === "Network.enable") return {};
  assert.equal(kind, "Network.streamResourceContent");
  return new Promise(resolve => { deliverPrefix = resolve; });
} };
const context = {newCDPSession: async () => cdp};
const diagnosticUrl = value => { const url = new URL(value); return url.origin + url.pathname; };
''' + helper + r'''
const capture = await captureNativeStreams({});
const raw = Buffer.from('data: {"id":"unit","payload":{"opaque":"café"}}\n\n');
const split = raw.indexOf(Buffer.from("é")) + 1;
const event = {requestId:"r1", response:{url:backend+"/_elsa/agent/sessions/s1/stream",status:200,
  headers:{"Content-Type":"text/event-stream",Authorization:"private-header-never-retained"}}};
handlers.get("Network.responseReceived")(event);
handlers.get("Network.responseReceived")({...event, requestId:"foreign",response:{...event.response,url:event.response.url.replace("49123","49124")}});
handlers.get("Network.dataReceived")({requestId:"r1",data:raw.subarray(9,split).toString("base64")});
deliverPrefix({bufferedData:raw.subarray(0,9).toString("base64")});
await Promise.resolve();
handlers.get("Network.dataReceived")({requestId:"r1",data:raw.subarray(split).toString("base64")});
handlers.get("Network.loadingFailed")({requestId:"r1",errorText:"net::ERR_ABORTED",canceled:true});
const rows = await capture();
assert.equal(rows.length,1);
assert.equal(rows[0].rawBody,raw.toString());
assert.equal(rows[0].bufferedBytes+rows[0].streamedBytes,raw.length);
assert.equal(rows[0].termination,"loadingFailed");
assert.equal(rows[0].canceled,true);
assert.equal(rows[0].captureError,null);
assert.ok(!JSON.stringify(rows).includes("private-header-never-retained"));
'''
        result = subprocess.run(["node", "--input-type=module", "-e", script], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == "__main__":
    unittest.main()
