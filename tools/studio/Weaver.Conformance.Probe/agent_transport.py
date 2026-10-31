"""Native Agent HTTP/SSE observations through an already normally signed-in runtime.Http.

There is no direct prepare/tool-list/proposal-read/recovery endpoint. A fixture
command selects an advertised opaque tool; message + stream invokes preparation.
Approve/execute use the returned proposal's baseRevision. ACK is caller-supplied
native evidence, never constructed here. Recovery snapshots only GET session/audit;
callers explicitly repeat the same execute/ACK within the server's retention scope.
No automatic retries, new session fallback, effect inference or provider selection.

The current native enumerator finishes one turn/review pause and returns; it is not
a cross-turn subscription. collect_stream explicitly drains that native EOF. For a
bounded reader use stream.until(predicate), stream.until_turn_end(turn_id=...) or
collect_turn. A predicate stop keeps the stream open; context exit records client
close separately from any observed terminal event and EOF. Receipt hashes finalize
after context exit. In-memory responses retain actual bytes; redacted snapshots go
to the private output. Never dump Http/opener/authentication state or raw bodies.
"""
from __future__ import annotations

from dataclasses import dataclass, field
import hashlib
import json
import os
from pathlib import Path
import re
import tempfile
import threading
from urllib.parse import quote, urlencode
import urllib.error
import urllib.request

from artifacts import REPO, check, sha
from runtime import private_json

PREFIX = "/_elsa/agent"
TURN_TERMINALS = frozenset({"completed", "toolApprovalRequested", "turnCancelled", "error"})
CREDENTIALS = {"authorization", "proxy-authorization", "cookie", "set-cookie", "password", "apikey",
               "githubtoken", "runtimeconnectiontoken", "accesstoken", "refreshtoken", "clientsecret"}


def _json(raw):
    def pairs(items):
        value = {}
        for key, item in items:
            if key in value:
                raise ValueError("Duplicate JSON member")
            value[key] = item
        return value
    def constant(_):
        raise ValueError("Nonfinite JSON value")
    return json.loads(raw, object_pairs_hook=pairs, parse_constant=constant)


def _segment(value):
    check(isinstance(value, str) and bool(value), "Use an actual nonempty native identity")
    return quote(value, safe="")


class AgentTransportError(RuntimeError):
    def __init__(self, message, *, response=None, receipt=None):
        super().__init__(message)
        self.response, self.receipt = response, receipt


@dataclass(frozen=True)
class Response:
    method: str
    path: str
    status: int
    headers: dict = field(repr=False)
    body: bytes = field(repr=False)
    receipt: dict = field(repr=False)

    def json(self):
        try:
            return None if not self.body else _json(self.body.decode("utf-8"))
        except (ValueError, UnicodeError):
            raise AgentTransportError("Native response is not unambiguous UTF-8 JSON", response=self, receipt=self.receipt) from None

    def require_status(self, expected):
        if self.status != expected:
            raise AgentTransportError(f"{self.method} {self.path} returned {self.status}; expected {expected}", response=self, receipt=self.receipt)
        return self


@dataclass(frozen=True)
class Frame:
    ordinal: int
    value: dict = field(repr=False)
    data: str = field(repr=False)
    event: str | None = None
    event_id: str | None = None


class AgentTransport:
    def __init__(self, http, private_output, *, command_file=None, trace_file=None, timeout=30):
        self.http, self.output, self.timeout = http, Path(private_output), timeout
        check(self.output.is_absolute() and not self.output.resolve().is_relative_to(REPO), "Agent receipts require an external private output")
        self.output.mkdir(parents=True, exist_ok=True, mode=0o700)
        self.output.chmod(0o700)
        self.command_file = Path(command_file) if command_file is not None else None
        self.trace_file = Path(trace_file) if trace_file is not None else None
        self.sequence = 0
        self.sequence_lock = threading.Lock()

    def _redact(self, value):
        if isinstance(value, dict):
            return {key: "[redacted]" if isinstance(key, str) and key.casefold() in CREDENTIALS else self._redact(item) for key, item in value.items()}
        if isinstance(value, list):
            return [self._redact(item) for item in value]
        if isinstance(value, str):
            bearer = self.http.bearer
            if bearer:
                value = value.replace(bearer, "[redacted]")
            value = re.sub(r"(?i)\bBearer\s+[^\s\"\\]+", "Bearer [redacted]", value)
            value = re.sub(r"\bsk-(?:proj-|ant-)?[A-Za-z0-9_-]{12,}", "[redacted]", value)
            return re.sub(r'(?i)("(?:authorization|password|apiKey|githubToken|runtimeConnectionToken|accessToken|refreshToken|cookie|set-cookie|clientSecret)"\s*:\s*)"(?:\\.|[^"\\])*"',
                          r'\1"[redacted]"', value)
        return value

    def _path(self, kind):
        with self.sequence_lock:
            while True:
                self.sequence += 1
                path = self.output / f"{self.sequence:04d}-{kind}.json"
                if not path.exists():
                    return path

    def _save(self, path, value):
        private_json(path, self._redact(value))
        return {"path": str(path), "sha256": sha(path)}

    def request(self, method, path, body=None):
        check(path.startswith(PREFIX + "/"), "Use a native Agent endpoint")
        receipt_path = self._path("http")
        record = {"origin": self.http.base, "method": method, "path": path, "requestBody": body.decode("utf-8", "replace") if isinstance(body, bytes) else body}
        try:
            status, raw, headers = self.http.request(path, body, method=method)
        except Exception as error:
            record["transportErrorType"] = type(error).__name__
            receipt = self._save(receipt_path, record)
            raise AgentTransportError("No native HTTP response was received; request was not replayed", receipt=receipt) from None
        record.update(status=status, headers=headers, rawBody=raw.decode("utf-8", "replace"), rawBodySha256=hashlib.sha256(raw).hexdigest())
        return Response(method, path, status, headers, raw, self._save(receipt_path, record))

    def bootstrap(self): return self.request("GET", PREFIX + "/bootstrap")
    def create_session(self, body): return self.request("POST", PREFIX + "/sessions", body)
    def session(self, session_id): return self.request("GET", PREFIX + "/sessions/" + _segment(session_id))
    def message(self, session_id, body): return self.request("POST", PREFIX + "/sessions/" + _segment(session_id) + "/messages", body)
    def approve(self, proposal_id, *, revision=None, comment=None):
        return self.request("POST", PREFIX + "/proposals/" + _segment(proposal_id) + "/approve", {"revision": revision, "comment": comment})
    def deny(self, proposal_id, *, comment=None):
        return self.request("POST", PREFIX + "/proposals/" + _segment(proposal_id) + "/deny", {"comment": comment})
    def execute(self, proposal_id, *, revision=None):
        return self.request("POST", PREFIX + "/proposals/" + _segment(proposal_id) + "/execute", {"revision": revision})
    def acknowledge(self, session_id, invocation_id, body):
        return self.request("POST", PREFIX + "/sessions/" + _segment(session_id) + "/client-invocations/" + _segment(invocation_id) + "/acknowledgements", body)
    def cancel(self, session_id, turn_id):
        return self.request("POST", PREFIX + "/sessions/" + _segment(session_id) + "/turns/" + _segment(turn_id) + "/cancel", {})
    def audit(self, session_id=None, *, take=100):
        query = {"take": take}
        if session_id is not None: query["sessionId"] = session_id
        return self.request("GET", PREFIX + "/audit?" + urlencode(query))
    def recovery_snapshot(self, session_id):
        return {"session": self.session(session_id), "audit": self.audit(session_id)}
    def stream(self, session_id): return EventStream(self, PREFIX + "/sessions/" + _segment(session_id) + "/stream")
    def collect_stream(self, session_id):
        with self.stream(session_id) as stream:
            list(stream)
        return stream
    def collect_until(self, session_id, predicate, *, max_events=1000):
        with self.stream(session_id) as stream:
            stream.until(predicate, max_events=max_events)
        return stream
    def collect_turn(self, session_id, *, turn_id=None, max_events=1000):
        with self.stream(session_id) as stream:
            stream.until_turn_end(turn_id=turn_id, max_events=max_events)
        return stream

    def set_command(self, command_id, tool_name, arguments):
        path = self.command_file
        check(path is not None and path.is_absolute() and not path.resolve().is_relative_to(REPO), "Explicit external protocol CommandFile required")
        check(isinstance(command_id, str) and command_id and isinstance(tool_name, str) and tool_name and isinstance(arguments, dict), "Protocol command needs identities and object arguments")
        data = json.dumps({"commandId": command_id, "toolName": tool_name, "arguments": arguments}, allow_nan=False).encode()
        descriptor, temporary = tempfile.mkstemp(prefix=".command-", dir=path.parent)
        try:
            with os.fdopen(descriptor, "wb") as stream:
                stream.write(data); stream.flush(); os.fsync(stream.fileno())
            os.replace(temporary, path)
        finally:
            if Path(temporary).exists(): Path(temporary).unlink()
        return {"path": str(path), "sha256": sha(path)}

    def protocol_trace(self, offset=0):
        check(self.trace_file is not None and self.trace_file.is_absolute() and self.trace_file.is_file(), "Actual retained protocol trace is required")
        check(type(offset) is int and offset >= 0, "Trace offset must be a byte position")
        with self.trace_file.open("rb") as stream:
            check(offset <= os.fstat(stream.fileno()).st_size, "Protocol trace was truncated or replaced")
            stream.seek(offset); raw = stream.read()
        complete = raw[:raw.rfind(b"\n") + 1]
        try:
            records = [_json(line.decode("utf-8")) for line in complete.splitlines() if line.strip()]
            if not all(isinstance(item, dict) for item in records): raise ValueError("Protocol trace record must be an object")
        except (ValueError, UnicodeError) as error:
            receipt = self._save(self._path("protocol-trace-error"), {"sourcePath": str(self.trace_file), "offset": offset,
                "rawBody": raw.decode("utf-8", "replace"), "rawBodySha256": hashlib.sha256(raw).hexdigest(), "errorType": type(error).__name__})
            raise AgentTransportError("Malformed actual protocol trace; no observations fabricated", receipt=receipt) from None
        value = {"sourcePath": str(self.trace_file), "offset": offset, "nextOffset": offset + len(complete),
                 "incompleteTailBytes": len(raw) - len(complete), "observedBytesSha256": hashlib.sha256(raw).hexdigest(), "records": records}
        return dict(value, receipt=self._save(self._path("protocol-trace"), value))


class EventStream:
    def __init__(self, owner, path):
        self.owner, self.path = owner, path
        self.frames, self.raw = [], bytearray()
        self.status, self.headers, self.response = None, {}, None
        self.termination, self.error_type, self.error_response = "not-opened", None, None
        self.receipt_path, self.receipt = owner._path("sse"), None
        self.iterator, self.turn_id, self.terminal_events, self.reader_stops = None, None, [], []

    def _save(self):
        self.receipt = self.owner._save(self.receipt_path, {"origin": self.owner.http.base, "method": "GET", "path": self.path,
            "status": self.status, "headers": self.headers, "termination": self.termination, "errorType": self.error_type,
            "observedTurnId": self.turn_id, "terminalEvents": self.terminal_events, "readerStops": self.reader_stops,
            "rawBody": bytes(self.raw).decode("utf-8", "replace"), "rawBodySha256": hashlib.sha256(self.raw).hexdigest(),
            "frames": [{"ordinal": frame.ordinal, "event": frame.event, "eventId": frame.event_id, "data": frame.data, "value": frame.value} for frame in self.frames]})

    def __enter__(self):
        headers = {"Accept": "text/event-stream"}
        if self.owner.http.bearer: headers["Authorization"] = "Bearer " + self.owner.http.bearer
        request = urllib.request.Request(self.owner.http.base + self.path, headers=headers)
        try:
            try: self.response = self.owner.http.opener.open(request, timeout=self.owner.timeout)
            except urllib.error.HTTPError as error: self.response = error
            self.status = self.response.status
            self.headers = {key.lower(): value for key, value in self.response.headers.items()}
            self.termination = "open"
            if self.headers.get("content-type", "").split(";", 1)[0].strip().casefold() != "text/event-stream":
                self.raw.extend(self.response.read()); self.termination = "non-sse-response"
                self._save()
                self.error_response = Response("GET", self.path, self.status, self.headers, bytes(self.raw), self.receipt)
            self._save()
            return self
        except Exception as error:
            if self.response is not None: self.response.close()
            self.termination, self.error_type = "open-error", type(error).__name__
            self._save()
            raise AgentTransportError("Native SSE response could not be opened; no reconnect attempted", receipt=self.receipt) from None

    def __iter__(self):
        if self.iterator is None: self.iterator = self._read_frames()
        return self.iterator

    def _stop(self, reason, frame=None):
        self.reader_stops.append({"reason": reason, "frameOrdinal": frame.ordinal if frame else None})
        self._save()

    def until(self, predicate, *, max_events=1000):
        """Observe until a caller predicate; leave this same stream open and resumable."""
        check(callable(predicate) and type(max_events) is int and max_events > 0, "Bounded SSE reader needs a predicate and positive event limit")
        iterator = iter(self)
        for _ in range(max_events):
            try: frame = next(iterator)
            except StopIteration:
                self._stop("eof-before-predicate")
                raise AgentTransportError("Native SSE reached EOF before the requested observation", receipt=self.receipt) from None
            if predicate(frame):
                self._stop("predicate", frame)
                return frame
        self._stop("event-limit", frame)
        raise AgentTransportError("Native SSE event limit reached before the requested observation", receipt=self.receipt)

    def until_turn_end(self, *, turn_id=None, max_events=1000):
        """Observe a native stream terminal/review pause, tied to Started when requested.

        toolApprovalRequested retains the paused turn; this never means its effect
        completed. The returned actual kind and payload remain the authority.
        """
        check(turn_id is None or isinstance(turn_id, str) and bool(turn_id), "Use an actual nonempty turn identity")
        def terminal(frame):
            return bool(self.terminal_events and self.terminal_events[-1]["frameOrdinal"] == frame.ordinal
                        and (turn_id is None or self.terminal_events[-1]["turnId"] == turn_id))
        frame = self.until(terminal, max_events=max_events)
        self.reader_stops[-1]["reason"] = "turn-terminal"
        self._save()
        return frame

    def _observe_turn(self, frame):
        value = frame.value
        payload = value.get("payload") if isinstance(value.get("payload"), dict) else {}
        if value["kind"] == "started":
            self.turn_id = payload.get("turnId") if isinstance(payload.get("turnId"), str) else None
        if value["kind"] in TURN_TERMINALS:
            turn_id = payload.get("turnId") if value["kind"] == "turnCancelled" else self.turn_id
            self.terminal_events.append({"frameOrdinal": frame.ordinal, "eventId": value["id"], "kind": value["kind"], "turnId": turn_id})

    def _read_frames(self):
        if self.error_response is not None:
            raise AgentTransportError("Native stream returned a non-SSE response", response=self.error_response, receipt=self.receipt)
        check(self.response is not None, "Open native SSE with its context manager")
        data, event, event_id, first = [], None, None, True
        try:
            while True:
                line = self.response.readline(1048577)
                if not line:
                    if data: raise ValueError("Unterminated SSE frame")
                    self.termination = "eof"; self._save(); return
                self.raw.extend(line)
                if len(line) > 1048576 or len(self.raw) > 67108864: raise ValueError("SSE evidence limit exceeded")
                text = line.decode("utf-8-sig" if first else "utf-8").rstrip("\r\n"); first = False
                if text == "":
                    if data:
                        raw = "\n".join(data); value = _json(raw)
                        if not isinstance(value, dict) or not isinstance(value.get("kind"), str) or not value["kind"] or not isinstance(value.get("id"), str) or not value["id"]:
                            raise ValueError("Malformed native Agent SSE envelope")
                        frame = Frame(len(self.frames) + 1, value, raw, event, event_id)
                        self.frames.append(frame); self._observe_turn(frame); self._save(); yield frame
                    data, event, event_id = [], None, None
                elif not text.startswith(":"):
                    key, _, value = text.partition(":")
                    if value.startswith(" "): value = value[1:]
                    if key == "data": data.append(value)
                    elif key == "event": event = value
                    elif key == "id": event_id = value
        except Exception as error:
            self.termination, self.error_type = "read-error", type(error).__name__
            self._save()
            raise AgentTransportError("Native SSE ended with a read/protocol error; no reconnect attempted", receipt=self.receipt) from None

    def __exit__(self, _type, error, _traceback):
        if self.termination == "open": self.termination = "client-closed"
        if self.response is not None: self.response.close()
        self._save()
        if isinstance(error, AgentTransportError) and error.receipt and error.receipt["path"] == str(self.receipt_path):
            error.receipt = self.receipt
