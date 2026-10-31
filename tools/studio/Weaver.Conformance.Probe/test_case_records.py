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
            item = {"kind": "functionResult", "callId": command["commandId"],
                    "result": json.dumps(adapter_result) if serialized else adapter_result}
            rows[0]["data"]["messages"] = [{"role": "user", "contents": [{"kind": "text", "text": "Synthetic request"}]}]
            rows += [row("request", tools=copy.deepcopy(rows[0]["data"]["tools"]) if read else [], messages=[
                *copy.deepcopy(rows[0]["data"]["messages"]), {"role": "tool", "contents": [copy.deepcopy(item)]}]),
                row("continuation", results=[item])]
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

    def test_executed_owner_without_actual_receipt_continuation_cannot_qualify(self):
        fixture = self.fixture()
        fixture.rows.pop()  # Keep the actual awaiting-approval SDK return, not its later receipt.
        self.sync(fixture)
        with self.assertRaisesRegex(RuntimeError, "The actual adapter result does not identify the native tool call"):
            self.correlate(fixture)
        self.assertFalse((fixture.output / "adapter-correlation.json").exists())

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

    def scoped_fixture(self, provider, native_scope, adapter_scope):
        fixture = self.fixture(provider, serialized=True)
        native = fixture.response["response"]["data"]["toolResult"]
        native["receipt"]["scope"] = native_scope
        adapter = copy.deepcopy(native); adapter["receipt"]["scope"] = adapter_scope
        self.store_adapter_result(fixture, provider, adapter)
        self.sync(fixture)
        return fixture

    def store_adapter_result(self, fixture, provider, adapter):
        if provider == "anthropic":
            fixture.rows[-1]["data"]["results"][0]["result"] = json.dumps(adapter)
            fixture.rows[-2]["data"]["messages"][-1]["contents"][0]["result"] = json.dumps(adapter)
        else: fixture.rows[-1]["data"]["results"][0] = adapter

    def test_receipt_scope_matches_actual_numeric_and_named_wire_forms_for_both_scopes(self):
        # AgentEffectScope: ClientWorkingState=0, ServerPersisted=1. The native
        # API emits camelCase; Anthropic's real JSON string emits the number.
        for provider in ("copilot", "anthropic"):
            for numeric, camel, pascal in ((0, "clientWorkingState", "ClientWorkingState"),
                                           (1, "serverPersisted", "ServerPersisted")):
                for native, adapter in ((camel, numeric), (numeric, camel), (camel, pascal), (numeric, numeric)):
                    with self.subTest(provider=provider, native=native, adapter=adapter):
                        self.assertIn("receipt", self.correlate(self.scoped_fixture(provider, native, adapter), provider))

    def test_receipt_scope_rejects_boolean_float_unknown_and_wrong_actual_scope(self):
        for provider in ("copilot", "anthropic"):
            for scope in (False, True, 0.0, 1.0, -1, 2, "unknown", "0", "serverpersisted", None, {}, []):
                with self.subTest(provider=provider, unsupported=scope):
                    with self.assertRaises(RuntimeError): self.correlate(self.scoped_fixture(provider, scope, scope), provider)
            for native, adapter in ((0, 1), (1, 0)):
                with self.subTest(provider=provider, wrongScope=(native, adapter)):
                    with self.assertRaises(RuntimeError): self.correlate(self.scoped_fixture(provider, native, adapter), provider)

    def test_module_payload_scope_is_opaque_even_when_generic_receipt_scopes_are_equivalent(self):
        for provider in ("copilot", "anthropic"):
            for location in ("payload", "receipt-evidence"):
                with self.subTest(provider=provider, location=location):
                    fixture = self.scoped_fixture(provider, "serverPersisted", 1)
                    native = fixture.response["response"]["data"]["toolResult"]
                    adapter = (fixture.rows[-1]["data"]["results"][0] if provider == "copilot" else
                               json.loads(fixture.rows[-1]["data"]["results"][0]["result"]))
                    if location == "payload":
                        native["payload"]["data"]["scope"] = 1
                        adapter["payload"]["data"]["scope"] = "serverPersisted"
                    else:
                        native["receipt"]["evidence"] = {"scope": 1}
                        adapter["receipt"]["evidence"] = {"scope": "serverPersisted"}
                    self.store_adapter_result(fixture, provider, adapter)
                    self.sync(fixture)
                    with self.assertRaises(RuntimeError): self.correlate(fixture, provider)

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
                    if provider == "anthropic": self.store_adapter_result(fixture, provider, result)
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

    def test_anthropic_read_continuation_can_retain_tools_without_another_actual_call(self):
        fixture = self.fixture("anthropic", serialized=True, read=True)
        self.assertTrue(fixture.rows[-2]["data"]["tools"])
        self.assertIn("receipt", self.correlate(fixture, "anthropic"))

    def test_anthropic_requires_unique_ordered_advertisement_call_and_current_result_request(self):
        for kind in ("duplicate-initial", "duplicate-post", "extra-call", "missing-post", "missing-initial",
                     "missing-continuation", "duplicate-continuation", "orphan-command", "wrong-result-id",
                     "historical-result", "altered-result"):
            with self.subTest(kind=kind):
                fixture = self.fixture("anthropic", serialized=True, read=True)
                if kind == "duplicate-initial": fixture.rows.insert(0, copy.deepcopy(fixture.rows[0]))
                elif kind == "duplicate-post": fixture.rows.insert(2, copy.deepcopy(fixture.rows[2]))
                elif kind == "extra-call": fixture.rows.insert(2, copy.deepcopy(fixture.rows[1]))
                elif kind == "missing-post": del fixture.rows[2]
                elif kind == "missing-initial": del fixture.rows[0]
                elif kind == "missing-continuation": fixture.rows.pop()
                elif kind == "duplicate-continuation": fixture.rows.append(copy.deepcopy(fixture.rows[-1]))
                elif kind == "orphan-command": fixture.rows[2]["data"]["commandId"] = "other-command"
                elif kind == "wrong-result-id": fixture.rows[2]["data"]["messages"][-1]["contents"][0]["callId"] = "other-call"
                elif kind == "historical-result": fixture.rows[2]["data"]["messages"].append({"role": "user", "contents": []})
                else:
                    item = fixture.rows[2]["data"]["messages"][-1]["contents"][0]
                    value = json.loads(item["result"]); value["payload"]["data"]["opaque"] = "changed"
                    item["result"] = json.dumps(value)
                self.sync(fixture)
                with self.assertRaises(RuntimeError): self.correlate(fixture, "anthropic")

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
    def run_capture_controls(self, body):
        source = Path(__file__).with_name("browser.mjs").read_text()
        helper = source[source.index("async function captureNativeStreams("):source.index("function command(")]
        script = 'import assert from "node:assert/strict";\nconst backend="http://127.0.0.1:49123"; let context;\n' + \
            'const diagnosticUrl=value=>{const url=new URL(value);return url.origin+url.pathname;};\n' + helper + body
        result = subprocess.run(["node", "--input-type=module", "-e", script], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_finished_sse_race_retrieves_only_same_retained_response_without_duplicate_bytes(self):
        self.run_capture_controls(r'''
for (const encoded of [false,true]) {
  const handlers=new Map(),calls=[],raw='data: {"id":"unit","kind":"completed"}\n\n';
  const cdp={on:(kind,callback)=>handlers.set(kind,callback),send:async(kind,args)=>{
    if(kind==='Network.enable')return {};
    calls.push({kind,args});
    if(kind==='Network.streamResourceContent')throw new Error('Request with the provided ID has already finished loading');
    assert.equal(kind,'Network.getResponseBody');assert.equal(args.requestId,'same-native-request');
    return {body:encoded?Buffer.from(raw).toString('base64'):raw,base64Encoded:encoded};
  }};
  context={newCDPSession:async()=>cdp};const capture=await captureNativeStreams({});
  handlers.get('Network.responseReceived')({requestId:'same-native-request',response:{url:backend+'/_elsa/agent/sessions/s1/stream',status:200,mimeType:'text/event-stream',charset:'',headers:{'Content-Type':'text/event-stream'}}});
  handlers.get('Network.dataReceived')({requestId:'same-native-request',data:Buffer.from(raw.slice(0,8)).toString('base64')});
  handlers.get('Network.loadingFinished')({requestId:'same-native-request'});
  const [row]=await capture();
  assert.equal(row.rawBody,raw);assert.equal(row.captureError,null);
  assert.equal(row.captureMethod,'getResponseBody');assert.equal(row.requestId,'same-native-request');
  assert.equal(row.completeBodyBytes,Buffer.byteLength(raw));assert.equal(row.termination,'loadingFinished');
  assert.match(row.streamResourceContentError,/already finished/);
  assert.deepEqual(calls.map(value=>value.kind),['Network.streamResourceContent','Network.getResponseBody']);
}
''')

    def test_finished_body_fallback_refuses_aborted_missing_failed_or_oversize_evidence(self):
        self.run_capture_controls(r'''
for(const scenario of ['aborted','open','non-sse','no-body','empty-body','body-error','oversize-body','stream-size-error','prefix-size-error','combined-size-error','missing-prefix','aborted-during-body']) {
  const handlers=new Map(),calls=[],limit=8*1024*1024;
  const cdp={on:(kind,callback)=>handlers.set(kind,callback),send:async(kind,args)=>{
    if(kind==='Network.enable')return {};
    calls.push(kind);
    if(kind==='Network.streamResourceContent') {
      if(scenario==='prefix-size-error')return {bufferedData:Buffer.alloc(limit+1).toString('base64')};
      if(scenario==='combined-size-error')return {bufferedData:Buffer.alloc(limit/2+1).toString('base64')};
      if(scenario==='missing-prefix')return {};
      throw new Error('stream enable failed');
    }
    assert.equal(kind,'Network.getResponseBody');assert.equal(args.requestId,'native-request');
    if(scenario==='body-error')throw new Error('No retained response body');
    if(scenario==='no-body')return {base64Encoded:false};
    if(scenario==='oversize-body')return {body:'x'.repeat(limit+1),base64Encoded:false};
    if(scenario==='aborted-during-body') {
      handlers.get('Network.loadingFailed')({requestId:'native-request',errorText:'net::ERR_ABORTED',canceled:true});
      return {body:'data: {}\n\n',base64Encoded:false};
    }
    return {body:'',base64Encoded:false};
  }};
  context={newCDPSession:async()=>cdp};const capture=await captureNativeStreams({});
  handlers.get('Network.responseReceived')({requestId:'native-request',response:{url:backend+'/_elsa/agent/sessions/s1/stream',status:scenario==='non-sse'?204:200,mimeType:'text/event-stream',charset:'',headers:scenario==='non-sse'?{}:{'Content-Type':'text/event-stream'}}});
  if(scenario==='stream-size-error')handlers.get('Network.dataReceived')({requestId:'native-request',data:Buffer.alloc(limit+1).toString('base64')});
  if(scenario==='combined-size-error')handlers.get('Network.dataReceived')({requestId:'native-request',data:Buffer.alloc(limit/2+1).toString('base64')});
  if(scenario==='aborted')handlers.get('Network.loadingFailed')({requestId:'native-request',errorText:'net::ERR_ABORTED',canceled:true});
  else if(scenario!=='open')handlers.get('Network.loadingFinished')({requestId:'native-request'});
  const [row]=await capture();
  assert.equal(row.rawBody,'');
  const eligible=['no-body','empty-body','body-error','oversize-body','aborted-during-body'].includes(scenario);
  assert.equal(calls.filter(kind=>kind==='Network.getResponseBody').length,eligible?1:0,scenario);
  if(scenario!=='non-sse')assert.ok(row.captureError,scenario);
  if(scenario==='aborted'){assert.equal(row.termination,'loadingFailed');assert.equal(row.canceled,true);}
  if(['stream-size-error','prefix-size-error','combined-size-error'].includes(scenario))assert.match(row.captureError,/bounded evidence size/);
}
''')

    def test_finished_cdp_text_reverses_only_verified_decoder_and_preserves_unicode_bytes(self):
        self.run_capture_controls(r'''
const raw='data: {"id":"unit","kind":"contributedResult","payload":{"opaque":"café € 😀 𝄞"}}\n\n';
const wire=Buffer.from(raw,'utf8'),legacy=new TextDecoder('windows-1252',{fatal:true,ignoreBOM:true});
for(const mode of ['default-text','utf8-text','raw-base64']) {
  const headers={'Content-Type':mode==='utf8-text'?'text/event-stream; charset=utf-8':'text/event-stream'};
  const response={url:backend+'/_elsa/agent/sessions/s1/stream',status:200,mimeType:'text/event-stream',charset:mode==='utf8-text'?'utf-8':'',headers};
  const body=mode==='default-text'?legacy.decode(wire):mode==='raw-base64'?wire.toString('base64'):raw;
  const handlers=new Map(),calls=[];
  const cdp={on:(kind,callback)=>handlers.set(kind,callback),send:async(kind,args)=>{
    calls.push({kind,args});if(kind==='Network.enable')return {};
    if(kind==='Network.streamResourceContent')throw new Error('already finished loading');
    assert.equal(kind,'Network.getResponseBody');assert.equal(args.requestId,'native');
    return {body,base64Encoded:mode==='raw-base64'};
  }};
  context={newCDPSession:async()=>cdp};const capture=await captureNativeStreams({});
  handlers.get('Network.responseReceived')({requestId:'native',response});handlers.get('Network.loadingFinished')({requestId:'native'});
  const [row]=await capture();assert.equal(row.captureError,null,mode);assert.equal(row.rawBody,raw,mode);
  assert.deepEqual(Buffer.from(row.rawBody,'utf8'),wire);
  const hash=async bytes=>Buffer.from(await crypto.subtle.digest('SHA-256',bytes)).toString('hex');
  assert.equal(row.cdpBodySha256,await hash(Buffer.from(body,'utf8')));
  assert.equal(row.reconstructedBodySha256,await hash(wire));
  assert.equal(row.reportedCharset,response.charset);assert.equal(row.reportedMimeType,'text/event-stream');
  assert.equal(row.bodyProfile,'native-agent-api-utf8-no-bom-data-lines');
  assert.equal(row.encodingMethod,mode==='default-text'?'chromium-149-default-windows-1252-inverse':mode==='utf8-text'?'declared-utf8-text-roundtrip':'cdp-base64-bytes');
  assert.equal(calls.filter(call=>call.kind==='Network.getResponseBody').length,1);
}
''')

    def test_finished_cdp_decoding_refuses_unknown_lossy_unmappable_and_invalid_utf8(self):
        self.run_capture_controls(r'''
const legacy=new TextDecoder('windows-1252',{fatal:true,ignoreBOM:true});
for(const scenario of ['missing-charset','unknown-charset','unsupported-legacy','header-conflict','missing-mime','unmappable','lossy','surrogate','invalid-default-utf8','invalid-base64-utf8','bom']) {
  const response={url:backend+'/_elsa/agent/sessions/s1/stream',status:200,mimeType:'text/event-stream',charset:'',headers:{'Content-Type':'text/event-stream'}};
  let body='data: {}\n\n',encoded=false;
  if(scenario==='missing-charset')delete response.charset;
  if(scenario==='unknown-charset')response.charset='utf-16le';
  if(scenario==='unsupported-legacy')response.charset='windows-1252';
  if(scenario==='header-conflict')response.headers['Content-Type']='text/event-stream; charset=utf-8';
  if(scenario==='missing-mime')delete response.mimeType;
  if(scenario==='unmappable')body='data: {"opaque":"😀"}\n\n';
  if(scenario==='lossy'){response.charset='utf-8';body='data: {"opaque":"\ufffd"}\n\n';}
  if(scenario==='surrogate'){response.charset='utf-8';body='data: {"opaque":"\ud800"}\n\n';}
  if(scenario==='invalid-default-utf8')body=legacy.decode(Buffer.concat([Buffer.from('data: '),Buffer.from([0xc3,0x28]),Buffer.from('\n\n')]));
  if(scenario==='invalid-base64-utf8'){body=Buffer.concat([Buffer.from('data: '),Buffer.from([0xc3,0x28]),Buffer.from('\n\n')]).toString('base64');encoded=true;}
  if(scenario==='bom'){body=Buffer.from('\ufeffdata: {}\n\n').toString('base64');encoded=true;}
  const handlers=new Map();
  const cdp={on:(kind,callback)=>handlers.set(kind,callback),send:async kind=>{
    if(kind==='Network.enable')return {};
    if(kind==='Network.streamResourceContent')throw new Error('already finished loading');
    assert.equal(kind,'Network.getResponseBody');return {body,base64Encoded:encoded};
  }};
  context={newCDPSession:async()=>cdp};const capture=await captureNativeStreams({});
  handlers.get('Network.responseReceived')({requestId:'native',response});handlers.get('Network.loadingFinished')({requestId:'native'});
  const [row]=await capture();assert.ok(row.captureError,scenario);assert.equal(row.rawBody,'',scenario);
}
''')

    def test_open_and_aborted_stream_keep_complete_frame_before_incomplete_utf8_tail(self):
        self.run_capture_controls(r'''
const frame='data: {"id":"native","kind":"contributedResult","resultKind":"moduleResult","payload":{"toolCallId":"call","toolName":"opaque_read","succeeded":true,"payload":{"opaque":"€ 😀"}}}\n\n';
for(const state of ['open','aborted','finished']) {
  const handlers=new Map(),tail=Buffer.concat([Buffer.from('data: {"unfinished":"'),Buffer.from('😀').subarray(0,3)]);
  const observed=Buffer.concat([Buffer.from(frame),tail]);
  const cdp={on:(kind,callback)=>handlers.set(kind,callback),send:async kind=>{
    if(kind==='Network.enable')return {};
    assert.equal(kind,'Network.streamResourceContent');return {bufferedData:observed.toString('base64')};
  }};
  context={newCDPSession:async()=>cdp};const capture=await captureNativeStreams({});
  handlers.get('Network.responseReceived')({requestId:'native',response:{url:backend+'/_elsa/agent/sessions/s1/stream',status:200,headers:{'Content-Type':'text/event-stream'}}});
  if(state==='aborted')handlers.get('Network.loadingFailed')({requestId:'native',errorText:'net::ERR_ABORTED',canceled:true});
  if(state==='finished')handlers.get('Network.loadingFinished')({requestId:'native'});
  const [row]=await capture();
  if(state!=='finished')assert.equal(row.captureError,null,state);
  assert.equal(row.observedBodyBytes,observed.length);
  assert.equal(row.reconstructedBodySha256,Buffer.from(await crypto.subtle.digest('SHA-256',observed)).toString('hex'));
  if(state==='finished'){assert.ok(row.captureError);assert.equal(row.rawBody,'');continue;}
  assert.equal(row.captureError,null,state);assert.equal(row.trailingIncompleteUtf8Bytes,3);
  assert.equal(row.textDecoding,'utf8-fatal-stream-prefix');assert.equal(row.textTruncation,'trailing-incomplete-utf8');
  assert.equal(row.rawBody,frame+'data: {"unfinished":"');
  const complete=row.rawBody.split('\n\n').slice(0,-1);assert.equal(complete.length,1);
  assert.equal(JSON.parse(complete[0].slice(6)).payload.toolCallId,'call');
}
''')

    def test_partial_stream_still_refuses_malformed_interior_utf8(self):
        self.run_capture_controls(r'''
const frame=Buffer.from('data: {"id":"complete","kind":"contributedResult"}\n\n');
for(const state of ['open','aborted']) {
  const handlers=new Map(),observed=Buffer.concat([frame,Buffer.from('data: '),Buffer.from([0xc3,0x28]),Buffer.from('\n\n')]);
  const cdp={on:(kind,callback)=>handlers.set(kind,callback),send:async kind=>{
    if(kind==='Network.enable')return {};assert.equal(kind,'Network.streamResourceContent');return {bufferedData:observed.toString('base64')};
  }};
  context={newCDPSession:async()=>cdp};const capture=await captureNativeStreams({});
  handlers.get('Network.responseReceived')({requestId:'native',response:{url:backend+'/_elsa/agent/sessions/s1/stream',status:200,headers:{'Content-Type':'text/event-stream'}}});
  if(state==='aborted')handlers.get('Network.loadingFailed')({requestId:'native',errorText:'net::ERR_ABORTED',canceled:true});
  const [row]=await capture();assert.ok(row.captureError,state);assert.equal(row.rawBody,'');
  assert.equal(row.observedBodyBytes,observed.length);
}
''')

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
