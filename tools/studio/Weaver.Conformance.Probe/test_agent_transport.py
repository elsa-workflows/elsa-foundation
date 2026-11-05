"""Transport controls use HTTP doubles and real byte framing; no live hosts/providers."""
import hashlib
import io
import json
from pathlib import Path
import stat
import tempfile
import unittest

from agent_transport import AgentTransport, AgentTransportError


class Wire(io.BytesIO):
    def __init__(self, data, status=200, content_type="text/event-stream"):
        super().__init__(data); self.status = status; self.headers = {"Content-Type": content_type}


class Opener:
    def __init__(self): self.calls, self.responses = [], []
    def open(self, request, timeout):
        self.calls.append(request)
        value = self.responses.pop(0)
        if isinstance(value, Exception): raise value
        return value


class HttpDouble:
    base, bearer = "http://127.0.0.1:45671", "private-authentication-marker"
    def __init__(self): self.calls, self.responses, self.opener = [], [], Opener()
    def request(self, path, body=None, *, method="GET"):
        self.calls.append((method, path, body))
        value = self.responses.pop(0)
        if isinstance(value, Exception): raise value
        return value
    def respond(self, status, value, headers=None):
        self.responses.append((status, json.dumps(value).encode(), headers or {"content-type": "application/json"}))


class AgentTransportTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.root = Path(self.temporary.name)
        self.http = HttpDouble()
        self.agent = AgentTransport(self.http, self.root / "receipts", command_file=self.root / "command.json", trace_file=self.root / "trace.jsonl")
    def tearDown(self): self.temporary.cleanup()
    def saved(self, receipt):
        raw = Path(receipt["path"]).read_bytes()
        self.assertEqual(receipt["sha256"], hashlib.sha256(raw).hexdigest())
        return json.loads(raw)
    def event(self, kind, **extra): return {"id": "actual-event-id", "kind": kind, **extra}
    def sse(self, value): return b"data: " + json.dumps(value).encode() + b"\n\n"

    def test_pending_and_terminal_results_keep_actual_status_error_and_data(self):
        pending = {"data": {"executed": False, "status": "awaiting-client", "clientInvocation": {"invocationId": "i"}}, "error": None}
        self.http.respond(202, pending)
        result = self.agent.execute("ProposalAa", revision="reviewed-revision")
        self.assertEqual(202, result.status); self.assertEqual(pending, result.json())
        terminal = {"data": {"executed": False, "status": "failed", "toolResult": {"payload": {"arbitrary": 7}}},
                    "error": {"code": "agent.proposal.refused", "statusCode": 409}}
        self.http.respond(409, terminal)
        failed = self.agent.execute("ProposalAa", revision="reviewed-revision")
        with self.assertRaises(AgentTransportError) as caught: failed.require_status(200)
        self.assertIs(failed, caught.exception.response); self.assertEqual(terminal, failed.json())
        self.assertEqual(409, self.saved(failed.receipt)["status"])

    def test_message_provenance_and_opaque_arguments_are_not_rewritten(self):
        request = {"requestId": "CaseSensitive", "content": "intent", "promptMode": "steer",
                   "source": {"moduleId": "opaque-owner", "actionId": "opaque-action"},
                   "contextAttachments": [{"id": "a", "content": {"nested": [False, {"any": 3}]}}]}
        self.http.respond(200, {"data": {"message": {"id": "native-id"}}, "error": None})
        self.agent.message("Aa/?", request)
        self.assertEqual(("POST", "/_elsa/agent/sessions/Aa%2F%3F/messages", request), self.http.calls[0])

    def test_ack_cancel_and_recovery_are_explicit_native_requests(self):
        acknowledgement = {"client": {"clientId": "c", "generationId": "g"}, "approvalDigest": "d", "outcome": "uncertain", "result": {"data": [1, 2]}}
        for _ in range(4): self.http.respond(200, {"data": {"observed": "native"}, "error": None})
        self.agent.acknowledge("SessionAa", "InvocationBb", acknowledgement)
        self.agent.cancel("SessionAa", "TurnCc")
        snapshot = self.agent.recovery_snapshot("SessionAa")
        self.assertEqual(acknowledgement, self.http.calls[0][2])
        self.assertEqual("/_elsa/agent/sessions/SessionAa/client-invocations/InvocationBb/acknowledgements", self.http.calls[0][1])
        self.assertEqual("/_elsa/agent/sessions/SessionAa/turns/TurnCc/cancel", self.http.calls[1][1])
        self.assertEqual(["GET", "GET"], [call[0] for call in self.http.calls[2:]])
        self.assertEqual({"session", "audit"}, set(snapshot))

    def test_private_receipt_redacts_auth_but_does_not_hide_unrelated_canaries(self):
        self.http.respond(200, {"data": {"password": "private-password", "unrelatedCanary": "detect-upstream-leak", "text": self.http.bearer}},
                          {"set-cookie": "private-cookie", "authorization": "Bearer " + self.http.bearer})
        result = self.agent.bootstrap(); text = Path(result.receipt["path"]).read_text()
        for secret in (self.http.bearer, "private-cookie", "private-password"): self.assertNotIn(secret, text)
        self.assertIn("detect-upstream-leak", text)
        self.assertNotIn(self.http.bearer, repr(result))
        self.assertEqual(0o600, stat.S_IMODE(Path(result.receipt["path"]).stat().st_mode))

    def test_command_is_atomic_exact_private_and_has_no_global_deduplication(self):
        self.agent.set_command("same-id", "any_installed_tool", {"values": [1, {"nested": True}]})
        self.agent.set_command("same-id", "any_installed_tool", {"values": [2]})
        self.assertEqual({"commandId": "same-id", "toolName": "any_installed_tool", "arguments": {"values": [2]}}, json.loads((self.root / "command.json").read_text()))
        before = (self.root / "command.json").read_bytes()
        with self.assertRaises(ValueError): self.agent.set_command("x", "tool", {"invalid": float("nan")})
        self.assertEqual(before, (self.root / "command.json").read_bytes())
        self.assertEqual(0o600, stat.S_IMODE((self.root / "command.json").stat().st_mode))

    def test_stream_preserves_multiline_crlf_frames_comments_and_provenance(self):
        raw = b': heartbeat\r\nid: transport-id\r\nevent: native\r\ndata: {"id":"E1",\r\ndata: "kind":"contributedResult","payload":{"opaque":[3,false]}}\r\n\r\n'
        self.http.opener.responses.append(Wire(raw, content_type="Text/Event-Stream; charset=utf-8"))
        stream = self.agent.collect_stream("SessionAa")
        self.assertEqual(1, len(stream.frames)); frame = stream.frames[0]
        self.assertEqual([3, False], frame.value["payload"]["opaque"])
        self.assertEqual(("native", "transport-id"), (frame.event, frame.event_id))
        receipt = self.saved(stream.receipt)
        self.assertEqual("eof", receipt["termination"]); self.assertEqual(hashlib.sha256(raw).hexdigest(), receipt["rawBodySha256"])
        self.assertNotIn(self.http.bearer, json.dumps(receipt))

    def test_error_event_with_http_200_is_preserved_without_invented_completion(self):
        value = self.event("error", error={"code": "agent.session.busy", "statusCode": 409})
        self.http.opener.responses.append(Wire(self.sse(value)))
        stream = self.agent.collect_stream("s")
        self.assertEqual(200, stream.status); self.assertEqual([value], [frame.value for frame in stream.frames])

    def test_malformed_sse_is_retained_and_refused(self):
        for raw in (b'data: not-json\n\n', b'data: {"id":"a","id":"b","kind":"completed"}\n\n',
                    b'data: []\n\n', b'data: \xff\n\n', b'data: {"id":"a","kind":"completed"}\n'):
            with self.subTest(raw=raw):
                self.http.opener.responses.append(Wire(raw))
                with self.assertRaises(AgentTransportError) as caught: self.agent.collect_stream("s")
                self.assertEqual("read-error", self.saved(caught.exception.receipt)["termination"])

    def test_native_non_sse_error_keeps_http_status_and_original_envelope(self):
        value = {"error": {"code": "permission-denied"}, "data": None}
        self.http.opener.responses.append(Wire(json.dumps(value).encode(), 403, "application/json"))
        with self.assertRaises(AgentTransportError) as caught: self.agent.collect_stream("s")
        self.assertEqual(403, caught.exception.response.status); self.assertEqual(value, caught.exception.response.json())

    def test_early_client_close_does_not_claim_stream_eof(self):
        self.http.opener.responses.append(Wire(self.sse(self.event("started", payload={"turnId": "T"})) + self.sse(self.event("completed"))))
        with self.agent.stream("s") as stream:
            iterator = iter(stream); self.assertEqual("started", next(iterator).value["kind"])
        self.assertEqual("client-closed", self.saved(stream.receipt)["termination"])
        self.assertEqual(1, len(stream.frames))

    def test_supported_review_terminal_stops_without_reading_open_ended_following_bytes(self):
        prefix = self.sse(self.event("started", payload={"turnId": "ActualTurn"}))
        prefix += self.sse(self.event("proposalCreated", proposalId="P", payload={"opaque": "preview"}))
        prefix += self.sse(self.event("toolApprovalRequested", proposalId="P"))
        class OpenEnded(Wire):
            def readline(self, size=-1):
                if self.tell() >= len(prefix): raise AssertionError("Reader waited beyond actual turn terminal")
                return super().readline(size)
        self.http.opener.responses.append(OpenEnded(prefix + b'following bytes that never finish\n'))
        stream = self.agent.collect_turn("s", turn_id="ActualTurn")
        receipt = self.saved(stream.receipt)
        self.assertEqual("client-closed", receipt["termination"])
        self.assertEqual("turn-terminal", receipt["readerStops"][-1]["reason"])
        self.assertEqual("toolApprovalRequested", receipt["terminalEvents"][-1]["kind"])
        self.assertEqual("ActualTurn", receipt["terminalEvents"][-1]["turnId"])
        self.assertEqual(prefix, bytes(stream.raw))

    def test_predicate_can_pause_then_cancel_and_continue_same_live_stream(self):
        raw = self.sse(self.event("started", payload={"turnId": "T"}))
        raw += self.sse(self.event("turnCancelled", payload={"turnId": "T"})) + b'following bytes\n'
        self.http.opener.responses.append(Wire(raw))
        self.http.respond(200, {"data": {"turnId": "T", "cancelled": True}, "error": None})
        with self.agent.stream("s") as stream:
            started = stream.until(lambda frame: frame.value["kind"] == "started")
            self.assertFalse(stream.response.closed)
            self.agent.cancel("s", started.value["payload"]["turnId"])
            terminal = stream.until_turn_end(turn_id="T")
            self.assertEqual("turnCancelled", terminal.value["kind"])
            self.assertFalse(stream.response.closed)
        self.assertEqual(1, len(self.http.opener.calls)); self.assertEqual(2, len(stream.frames))
        self.assertEqual("client-closed", self.saved(stream.receipt)["termination"])

    def test_terminal_cannot_be_assigned_to_an_unobserved_other_turn(self):
        raw = self.sse(self.event("started", payload={"turnId": "Actual"})) + self.sse(self.event("completed"))
        self.http.opener.responses.append(Wire(raw))
        with self.assertRaises(AgentTransportError) as caught: self.agent.collect_turn("s", turn_id="Other")
        receipt = self.saved(caught.exception.receipt)
        self.assertEqual("eof", receipt["termination"])
        self.assertEqual("Actual", receipt["terminalEvents"][-1]["turnId"])
        self.assertEqual("eof-before-predicate", receipt["readerStops"][-1]["reason"])

    def test_bounded_predicate_limit_is_not_reported_as_a_turn_terminal(self):
        self.http.opener.responses.append(Wire(self.sse(self.event("progress")) + b'following bytes\n'))
        with self.assertRaises(AgentTransportError) as caught:
            self.agent.collect_until("s", lambda frame: frame.value["kind"] == "completed", max_events=1)
        receipt = self.saved(caught.exception.receipt)
        self.assertEqual("client-closed", receipt["termination"]); self.assertEqual([], receipt["terminalEvents"])
        self.assertEqual("event-limit", receipt["readerStops"][-1]["reason"])

    def test_pascal_case_kind_is_not_misclassified_as_native_camel_case_terminal(self):
        self.http.opener.responses.append(Wire(self.sse(self.event("Completed"))))
        with self.assertRaises(AgentTransportError) as caught: self.agent.collect_turn("s")
        self.assertEqual([], self.saved(caught.exception.receipt)["terminalEvents"])

    def test_read_disconnect_preserves_observed_frames_and_never_reconnects(self):
        class Broken(Wire):
            def readline(self, size=-1):
                if self.tell() == len(self.getvalue()): raise OSError("connection-lost")
                return super().readline(size)
        self.http.opener.responses.append(Broken(self.sse(self.event("contributedResult", payload={"opaque": 9}))))
        with self.assertRaises(AgentTransportError) as caught: self.agent.collect_stream("s")
        receipt = self.saved(caught.exception.receipt)
        self.assertEqual(1, len(receipt["frames"])); self.assertEqual(1, len(self.http.opener.calls))

    def test_missing_http_response_is_not_retried_or_reported_as_a_native_result(self):
        self.http.responses.append(OSError(self.http.bearer))
        with self.assertRaises(AgentTransportError) as caught: self.agent.execute("p")
        receipt = self.saved(caught.exception.receipt)
        self.assertNotIn("status", receipt); self.assertNotIn(self.http.bearer, json.dumps(receipt))
        self.assertEqual(1, len(self.http.calls))

    def test_malformed_rest_json_retains_actual_response_without_guessing_data(self):
        raw = b'{"data":{"executed":false},"data":{"executed":true}}'
        self.http.responses.append((200, raw, {"content-type": "application/json"}))
        result = self.agent.bootstrap()
        with self.assertRaises(AgentTransportError) as caught: result.json()
        self.assertIs(result, caught.exception.response)
        self.assertEqual(hashlib.sha256(raw).hexdigest(), self.saved(result.receipt)["rawBodySha256"])

    def test_trace_offsets_preserve_complete_observations_and_refuse_malformed_records(self):
        path = self.root / "trace.jsonl"; first = b'{"kind":"call","data":{"commandId":"c"}}\n'
        path.write_bytes(first + b'{"kind":"continuation"}')
        value = self.agent.protocol_trace()
        self.assertEqual(len(first), value["nextOffset"]); self.assertEqual(1, len(value["records"]))
        with path.open("ab") as stream: stream.write(b'\n')
        self.assertEqual("continuation", self.agent.protocol_trace(value["nextOffset"])["records"][0]["kind"])
        path.write_bytes(b'not-json\n')
        with self.assertRaises(AgentTransportError) as caught: self.agent.protocol_trace()
        self.assertEqual("JSONDecodeError", self.saved(caught.exception.receipt)["errorType"])


if __name__ == "__main__":
    unittest.main()
