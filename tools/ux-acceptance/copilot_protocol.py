"""Owned, credential-free external peer for the pinned Copilot SDK 1.0.4.

This is qualification infrastructure outside the product host, not an AI provider.
The wire contract is pinned to github/copilot-sdk e7d2bd3483ecd2f49412709d5655157a2190dd91
(dotnet/src/{Client,JsonRpc,Session}.cs and Generated/{Rpc,SessionEvents}.cs).

Use ``with CopilotProtocolPeer(private_root, run_generated_token) as peer`` and
configure the normal shipped provider with peer.runtime_url and that token.
Queries name module IDs in ordinary user text and include one ``nonce=<8..64
ASCII letters/digits/hyphens>``. Route queries ask about the current route without
supplying its value. No phase or expected state is accepted. turn_records is a
PRIVATE snapshot; safe_receipt() intentionally exports hashes/counts only.
All service errors are fixed codes; raw prompts/configuration stay in a 0600
journal in an exclusively created 0700 child of private_root. Tokens are never
journaled. close() stops only owned sockets/threads and provides a close receipt.
"""
from __future__ import annotations

import copy
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import socket
import threading
import time
import uuid

MAX_FRAME = 1_048_576
MAX_HEADER = 4096
MAX_JOURNAL = 16_777_216
MAX_CONNECTIONS = 64
MAX_ACTIVE = 8
MAX_SESSIONS = 32
MAX_TURNS = 64
MAX_REQUESTS = 2048
LIFETIME_SECONDS = 1800
IO_TIMEOUT = 15
ID = re.compile(r"@?[A-Za-z0-9_-]+(?:[./:][A-Za-z0-9_-]+)*\Z")
NATIVE_ID = re.compile(r"[A-Za-z0-9._:*\-]{1,128}\Z")
NONCE = re.compile(r"\bnonce=([A-Za-z0-9][A-Za-z0-9-]{7,63})(?![A-Za-z0-9-])")
MODULE_WORD = re.compile(r"\b[A-Za-z0-9_]+(?:[.:/-][A-Za-z0-9_]+)+\b")


class ProtocolError(ValueError):
    """Fixed, safe failure code. Never include peer-supplied text in this error."""


def _require(condition, code):
    if not condition:
        raise ProtocolError(code)


def _hash(value):
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def _object_pairs(pairs):
    result = {}
    for key, value in pairs:
        _require(key not in result, "duplicate-json-key")
        result[key] = value
    return result


def _json(value):
    try:
        return json.loads(value, object_pairs_hook=_object_pairs,
                          parse_constant=lambda _: (_ for _ in ()).throw(ProtocolError("nonfinite-json")))
    except (ValueError, RecursionError) as error:
        if isinstance(error, ProtocolError):
            raise
        raise ProtocolError("invalid-json") from None


def _encode(value):
    data = json.dumps(value, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")
    _require(0 < len(data) <= MAX_FRAME, "frame-size")
    return b"Content-Length: " + str(len(data)).encode("ascii") + b"\r\n\r\n" + data


def _read_frame(stream):
    deadline = time.monotonic() + IO_TIMEOUT
    read = getattr(stream, "read1", stream.read)
    header = bytearray()
    while not header.endswith(b"\r\n\r\n"):
        _require(time.monotonic() < deadline, "frame-timeout")
        byte = read(1)
        if not byte:
            _require(not header, "truncated-header")
            return None
        header.extend(byte)
        _require(len(header) <= MAX_HEADER, "header-size")
    try:
        lines = bytes(header[:-4]).decode("ascii").split("\r\n")
    except UnicodeDecodeError:
        raise ProtocolError("invalid-header") from None
    lengths = []
    for line in lines:
        name, separator, value = line.partition(":")
        _require(separator and name.lower() in {"content-length", "content-type"}, "invalid-header")
        if name.lower() == "content-length":
            _require(re.fullmatch(r"[0-9]{1,8}", value.strip()), "invalid-length")
            lengths.append(int(value))
    _require(len(lengths) == 1 and 0 < lengths[0] <= MAX_FRAME, "frame-size")
    body = bytearray()
    while len(body) < lengths[0]:
        _require(time.monotonic() < deadline, "frame-timeout")
        chunk = read(min(65536, lengths[0] - len(body)))
        _require(chunk, "truncated-body")
        body.extend(chunk)
    try:
        result = _json(body.decode("utf-8"))
    except UnicodeDecodeError:
        raise ProtocolError("invalid-utf8") from None
    _require(isinstance(result, dict), "invalid-envelope")
    return result


def _identifier(value, native=False):
    pattern = NATIVE_ID if native else ID
    return isinstance(value, str) and len(value) <= 160 and pattern.fullmatch(value) is not None


def _rows(value, limit):
    _require(isinstance(value, list) and len(value) <= limit and all(isinstance(row, dict) for row in value), "support-rows")
    return value


def _unique(rows, key, fold=True):
    keys = [str(row.get(key, "")).casefold() if fold else str(row.get(key, "")) for row in rows]
    _require(len(keys) == len(set(keys)), "duplicate-support-row")


def _studio(value):
    _require(isinstance(value, dict) and value.get("side") == "studio" and value.get("clientAsserted") is True,
             "studio-provenance")
    _require(value.get("completeness") in {"complete", "incomplete", "unknown", "unavailable"}
             and value.get("registrationCompleteness") in {"complete", "incomplete"}, "studio-completeness")
    modules = _rows(value.get("modules"), 128)
    loader = _rows(value.get("loader"), 128)
    registrations = _rows(value.get("registrations"), 128)
    for row in modules:
        _require(_identifier(row.get("id")) and row.get("status") in
                 {"available", "disabled", "failed", "incompatible", "unknown"}, "studio-module")
    for row in loader:
        _require(_identifier(row.get("moduleId")) and row.get("status") in
                 {"registering", "loaded", "failed", "retired"}, "studio-loader")
    for row in registrations:
        _require(_identifier(row.get("kind")) and _identifier(row.get("id")) and row.get("status") in
                 {"available", "unavailable", "unknown"}, "studio-registration")
        for key in ("originModuleId", "declaredModuleId"):
            _require(row.get(key) is None or _identifier(row[key]), "studio-registration-owner")
        resources = row.get("resourceModuleIds")
        _require(resources is None or isinstance(resources, list) and len(resources) <= 8
                 and all(_identifier(item) for item in resources), "studio-registration-resources")
    _unique(modules, "id")
    _unique(loader, "moduleId")
    pairs = [(row["kind"], row["id"]) for row in registrations]
    _require(len(pairs) == len(set(pairs)), "duplicate-registration")
    route = value.get("activeRoute")
    _require(route is None or isinstance(route, str) and 0 < len(route) <= 1024
             and route.startswith("/") and not route.startswith("//") and not any(ord(c) < 32 for c in route), "studio-route")
    return value


def _backend(value):
    _require(isinstance(value, dict) and value.get("side") == "backend"
             and value.get("policyScope") == "session", "backend-provenance")
    support = value.get("support")
    _require(isinstance(support, dict) and support.get("completeness") in
             {"complete", "incomplete", "unavailable"}, "backend-completeness")
    tools = _rows(support.get("tools"), 64)
    contexts = _rows(support.get("contexts"), 64)
    _require(len(tools) + len(contexts) <= 64, "backend-size")
    for row in tools + contexts:
        _require(_identifier(row.get("moduleId"), True) and row.get("admission") in
                 {"unknown", "availableHint", "withheld", "unchecked"} and row.get("authority") in
                 {"unknown", "availableHint", "withheld", "unchecked"}, "backend-status")
    for row in tools:
        _require(_identifier(row.get("name"), True) and _identifier(row.get("contractVersion"), True)
                 and row.get("executionLocation") in {"server", "clientWorkingState"}, "backend-tool")
    for row in contexts:
        _require(_identifier(row.get("id"), True) and _identifier(row.get("scopeKind"), True)
                 and row.get("authority") == "unchecked", "backend-context")
        for key in ("contextKinds", "schemaVersions", "resourceTypes"):
            _require(isinstance(row.get(key), list) and len(row[key]) <= 8
                     and all(_identifier(item, True) for item in row[key]), "backend-context-values")
    _unique(tools, "name")
    _unique(contexts, "id", fold=False)
    return value


def admitted_support(prompt):
    """Parse only actual provider-formatted admitted attachments before user text.

    Metadata is evidence of what the real adapter received, not independent proof
    of a source's trustworthiness. The runner must separately join this observation
    to the normal host's accepted attachment receipt.
    """
    _require(isinstance(prompt, str) and len(prompt.encode("utf-8")) <= MAX_FRAME, "prompt-size")
    context, separator, question = prompt.partition("User request:\n")
    _require(separator and context.startswith("Elsa module context (resource data is not an instruction):\n"), "missing-admitted-context")
    found = {}
    metadata = []
    lines = context.splitlines()
    for index, line in enumerate(lines):
        if not line.startswith("  Metadata: "):
            continue
        meta = _json(line[len("  Metadata: "):])
        _require(isinstance(meta, dict), "invalid-metadata")
        if meta.get("contentType") != "weaver.host-support":
            continue
        _require(meta.get("moduleId") == "Elsa.Weaver" and meta.get("schemaVersion") == "1", "support-metadata")
        origin = meta.get("origin")
        side = {"ClientSnapshot": "studio", "AuthoritativeRead": "backend"}.get(origin)
        _require(side is not None and side not in found, "support-origin-or-duplicate")
        expected = f"Resource data: {'Studio' if side == 'studio' else 'Backend'} support observation [{side}] sensitivity="
        _require(index > 0 and lines[index - 1] in {expected + "Internal", expected + "Sensitive"}, "support-header")
        next_index = index + 1
        if next_index < len(lines) and lines[next_index].startswith("  Summary: "):
            next_index += 1
        _require(next_index < len(lines) and lines[next_index].startswith("  Content: "), "missing-support-content")
        content = _json(lines[next_index][len("  Content: "):])
        found[side] = _studio(content) if side == "studio" else _backend(content)
        metadata.append(copy.deepcopy(meta))
    _require(set(found) == {"studio", "backend"}, "missing-support-side")
    return found["studio"], found["backend"], question, metadata


def derive_answer(prompt):
    """Bounded support oracle, with no expected phase/state parameter."""
    studio, backend, question, metadata = admitted_support(prompt)
    _require(len(question) <= 4096, "question-size")
    matches = list(NONCE.finditer(question))
    _require(len(matches) == 1, "question-nonce")
    nonce = matches[0].group(1)
    query = question[:matches[0].start()] + question[matches[0].end():]
    _require(not re.search(r"\b(delete|remove|execute|invoke|purchase|credential|password|secret|write|create|run)\b", query, re.I),
             "unrelated-operation")
    facts = {"studioCompleteness": studio["completeness"], "registrationCompleteness": studio["registrationCompleteness"],
             "backendCompleteness": backend["support"]["completeness"], "authority": "unchecked", "modules": []}
    if re.search(r"\b(route|page)\b", query, re.I):
        _require("/" not in query, "route-in-question")
        route = studio.get("activeRoute")
        _require(route is not None, "missing-admitted-route")
        facts["activeRoute"] = route
        response = (f"Client-asserted current route: {route}. Module ownership and route-specific Weaver support are unknown; "
                    "the admitted host-support inventory supplies no module-owned route mapping. Resource permission and execution authority remain unchecked.")
    else:
        _require(re.search(r"\b(module|modules|support|installed)\b", query, re.I), "unrelated-question")
        ids = list(dict.fromkeys(word.casefold() for word in MODULE_WORD.findall(query)))
        _require(0 < len(ids) <= 8 and all(_identifier(item) for item in ids), "question-module-ids")
        modules = {row["id"].casefold(): row for row in studio["modules"]}
        for identity in ids:
            module = modules.get(identity)
            status = module["status"] if module else "absent" if studio["completeness"] == "complete" else "unknown"
            registrations = [row for row in studio["registrations"] if (row.get("originModuleId") or "").casefold() == identity]
            # Resource/declared-module references do not make another module the owner.
            contribution = "observed" if registrations else "none" if studio["registrationCompleteness"] == "complete" else "unknown"
            facts["modules"].append({"id": identity, "status": status, "weaverContributions": contribution,
                                     "registrations": copy.deepcopy(registrations)})
        clauses = []
        for row in facts["modules"]:
            contribution = {"none": "no Weaver contributions in complete Studio registration discovery",
                            "unknown": "Studio Weaver contributions unknown because registration discovery is incomplete",
                            "observed": "Studio Weaver registrations observed (their status is reported separately)"}[row["weaverContributions"]]
            statuses = ", ".join(sorted({item["status"] for item in row["registrations"]}))
            status = row["status"] + (" (not installed in complete Studio discovery)" if row["status"] == "absent" else "")
            clauses.append(f"{row['id']}: {status}; {contribution}" + (f"; registration statuses: {statuses}" if statuses else ""))
        response = "Client-asserted Studio discovery: " + ". ".join(clauses) + ". Resource permission and execution authority remain unchecked."
    support = backend["support"]
    facts["backendTools"] = copy.deepcopy(support["tools"])
    facts["backendContexts"] = copy.deepcopy(support["contexts"])
    response += (f" Session-scoped backend support: discovery {support['completeness']}, {len(support['tools'])} tool hints, "
                 f"{len(support['contexts'])} context hints; these do not grant authority. Observation nonce: {nonce}.")
    return {"nonce": nonce, "metadata": metadata, "facts": facts, "response": response,
            "promptHash": _hash(prompt), "userRequestHash": _hash(question), "responseHash": _hash(response)}


def _session_config(params):
    # Only the shipped adapter's pinned Empty-mode profile is implemented. Tools
    # are passive advertised definitions; this peer never invokes any of them.
    defaults = {"enableSessionTelemetry": False, "skipEmbeddingRetrieval": True,
                "embeddingCacheStorage": "in-memory", "enableOnDemandInstructionDiscovery": False,
                "enableFileHooks": False, "enableHostGitOperations": False, "enableSessionStore": False,
                "enableSkills": False, "memory": {"enabled": False}, "mcpOAuthTokenStorage": "in-memory",
                "envValueMode": "direct", "requestPermission": True, "requestElicitation": False,
                "streaming": True, "toolFilterPrecedence": "excluded"}
    allowed = set(defaults) | {"sessionId", "model", "reasoningEffort", "tools", "systemMessage",
                               "availableTools", "excludedTools", "traceparent", "tracestate"}
    _require(set(params) <= allowed and all(key in params and type(params[key]) is type(value)
             and params[key] == value for key, value in defaults.items()), "empty-session-config")
    for key in ("model", "reasoningEffort", "traceparent", "tracestate"):
        _require(key not in params or isinstance(params[key], str) and len(params[key]) <= 1024, "session-config-string")
    for key in ("availableTools", "excludedTools"):
        _require(isinstance(params.get(key), list) and len(params[key]) <= 256
                 and all(isinstance(item, str) and len(item) <= 256 for item in params[key]), "session-tool-filter")
    if "tools" in params:
        _rows(params["tools"], 128)
        _require(all(_identifier(item.get("name"), True) for item in params["tools"]), "session-tool-definition")
    system = params.get("systemMessage")
    _require(isinstance(system, dict) and set(system) <= {"mode", "content", "sections"}
             and system.get("mode") == "customize"
             and system.get("sections") == {"environment_context": {"action": "remove"}}
             and ("content" not in system or isinstance(system["content"], str) and len(system["content"]) <= 65536),
             "empty-system-message")


class CopilotProtocolPeer:
    """Bounded owned loopback service; start once, then close deterministically."""

    def __init__(self, private_root, connection_token):
        _require(isinstance(connection_token, str) and 32 <= len(connection_token) <= 256
                 and re.fullmatch(r"[A-Za-z0-9_-]+", connection_token), "connection-token-format")
        self._private_root = Path(private_root)
        self._token = connection_token
        self._lock = threading.RLock()
        self._close_lock = threading.Lock()
        self._stop = threading.Event()
        self._listener = None
        self._accept_thread = None
        self._workers = []
        self._sockets = set()
        self._sessions = {}
        self._turns = []
        self._errors = []
        self._connections = 0
        self._requests = 0
        self._journal_bytes = 0
        self._journal = None
        self._started = False
        self._closed = False
        self._runtime_url = None
        self._close_receipt = None

    @property
    def runtime_url(self):
        _require(self._runtime_url is not None and not self._closed, "peer-not-running")
        return self._runtime_url

    @property
    def turn_records(self):
        """PRIVATE detached snapshot. Includes actual admitted facts and response."""
        with self._lock:
            return tuple(copy.deepcopy(self._turns))

    @property
    def close_receipt(self):
        with self._lock:
            return copy.deepcopy(self._close_receipt)

    def safe_receipt(self):
        """No raw auth, prompts, configuration, routes, IDs, nonce or responses."""
        with self._lock:
            return {"kind": "external-copilot-protocol-observation", "sdkVersion": "1.0.4",
                    "protocolVersion": 3, "connections": self._connections, "requests": self._requests,
                    "errors": list(self._errors), "closed": self._closed,
                    "turns": [{"sessionHash": _hash(row["sessionId"]), "requestId": row["requestId"],
                               "nonceHash": _hash(row["nonce"]), "promptHash": row["promptHash"],
                               "requestHash": row["requestHash"],
                               "userRequestHash": row["userRequestHash"], "responseHash": row["responseHash"],
                               "wireComplete": row["wireComplete"],
                               "moduleCount": len(row["facts"]["modules"]),
                               "admittedMetadataCount": len(row["metadata"])} for row in self._turns],
                    "close": copy.deepcopy(self._close_receipt)}

    def _failure(self, code):
        with self._lock:
            if code not in self._errors and len(self._errors) < 32:
                self._errors.append(code)

    def assert_healthy(self):
        with self._lock:
            _require(not self._errors, "peer-observed-failure")

    def _journal_request(self, request):
        record = copy.deepcopy(request)
        if record.get("method") == "connect":
            record["params"] = {"tokenPresent": isinstance(record.get("params", {}).get("token"), str)}
        text = json.dumps(record, ensure_ascii=False, separators=(",", ":"), allow_nan=False)
        # Authentication must never enter even the private journal (including echoes).
        text = text.replace(self._token, "[REDACTED-CONNECTION-TOKEN]")
        data = (text + "\n").encode("utf-8")
        with self._lock:
            _require(self._journal_bytes + len(data) <= MAX_JOURNAL, "journal-budget")
            self._journal.write(data)
            self._journal.flush()
            self._journal_bytes += len(data)

    def start(self):
        _require(not self._started and not self._closed, "peer-start-once")
        self._started = True
        try:
            self._private_root.mkdir(parents=True, exist_ok=True)
            directory = self._private_root / "copilot-peer"
            directory.mkdir(mode=0o700)  # Exclusive; never share or overwrite another run.
            os.chmod(directory, 0o700)
            descriptor = os.open(directory / "requests.private.jsonl", os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            self._journal = os.fdopen(descriptor, "wb")
            self._listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self._listener.bind(("127.0.0.1", 0))
            self._listener.listen(MAX_ACTIVE)
            self._listener.settimeout(0.2)
            self._runtime_url = f"http://127.0.0.1:{self._listener.getsockname()[1]}"
            self._deadline = time.monotonic() + LIFETIME_SECONDS
            self._accept_thread = threading.Thread(target=self._accept, name="copilot-peer-accept", daemon=True)
            self._accept_thread.start()
            return self
        except BaseException:
            self._failure("peer-start-failed")
            self.close()
            raise

    def __enter__(self):
        return self.start()

    def __exit__(self, kind, value, traceback):
        self.close()
        if kind is None:
            self.assert_healthy()
        return False

    def _accept(self):
        try:
            while not self._stop.is_set():
                if time.monotonic() >= self._deadline:
                    self._failure("lifetime-budget")
                    self._stop.set()
                    break
                try:
                    connection, address = self._listener.accept()
                except socket.timeout:
                    continue
                except OSError:
                    if not self._stop.is_set():
                        self._failure("accept-io")
                    self._stop.set()
                    break
                with self._lock:
                    self._connections += 1
                    if address[0] != "127.0.0.1" or self._connections > MAX_CONNECTIONS or len(self._sockets) >= MAX_ACTIVE:
                        connection.close()
                        self._failure("connection-budget")
                        self._stop.set()
                        break
                    connection.settimeout(IO_TIMEOUT)
                    self._sockets.add(connection)
                    worker = threading.Thread(target=self._serve, args=(connection,), name="copilot-peer-client", daemon=True)
                    self._workers.append(worker)
                    worker.start()
        except BaseException:
            self._failure("accept-internal")
            self._stop.set()
        finally:
            # Owned listener must stop accepting even when lifetime expires without close().
            if self._listener is not None:
                self._listener.close()
            if self._stop.is_set():
                with self._lock:
                    connections = tuple(self._sockets)
                for connection in connections:
                    try:
                        connection.shutdown(socket.SHUT_RDWR)
                    except OSError:
                        pass
                    connection.close()
                    with self._lock:
                        self._sockets.discard(connection)

    def _serve(self, connection):
        authenticated = False
        attached = set()
        seen_ids = set()
        try:
            with connection.makefile("rb") as stream:
                while not self._stop.is_set():
                    request = _read_frame(stream)
                    if request is None:
                        return
                    with self._lock:
                        self._requests += 1
                        _require(self._requests <= MAX_REQUESTS, "request-budget")
                    _require(set(request) <= {"jsonrpc", "id", "method", "params"}
                             and request.get("jsonrpc") == "2.0" and isinstance(request.get("method"), str), "invalid-envelope")
                    method = request["method"]
                    params = request.get("params", {})
                    _require(isinstance(params, dict), "invalid-params")
                    if method == "$/cancelRequest":
                        _require(authenticated and "id" not in request and set(params) == {"id"}
                                 and type(params.get("id")) is int, "invalid-cancel")
                        self._journal_request(request)
                        continue
                    request_id = request.get("id")
                    _require(type(request_id) is int and 0 <= request_id <= 2**53 and request_id not in seen_ids, "invalid-request-id")
                    seen_ids.add(request_id)
                    if not authenticated:
                        _require(method == "connect" and set(params) <= {"token"}
                                 and isinstance(params.get("token"), str)
                                 and hmac.compare_digest(params["token"], self._token), "connection-auth")
                        authenticated = True
                        result, turn = {"ok": True, "protocolVersion": 3, "version": "elsa-qualification-peer"}, None
                    else:
                        _require(method != "connect", "duplicate-connect")
                        _require(self._token not in json.dumps(request, ensure_ascii=False), "connection-token-in-payload")
                        result, turn = self._dispatch(method, params, attached, request_id)
                    self._journal_request(request)
                    connection.sendall(_encode({"jsonrpc": "2.0", "id": request_id, "result": result}))
                    if turn is not None:
                        self._event(connection, turn["sessionId"], "assistant.message_delta",
                                    {"messageId": turn["messageId"], "deltaContent": turn["response"]})
                        self._event(connection, turn["sessionId"], "session.idle", {})
                        with self._lock:
                            for recorded in self._turns:
                                if recorded["messageId"] == turn["messageId"]:
                                    recorded["wireComplete"] = True
        except ProtocolError as error:
            if self._stop.is_set() and str(error) in {"truncated-header", "truncated-body", "frame-timeout"}:
                return
            self._failure(str(error))
            # Fixed code only. Authentication/setup errors cannot masquerade as answers.
            if authenticated and 'request_id' in locals():
                try:
                    connection.sendall(_encode({"jsonrpc": "2.0", "id": request_id,
                                                "error": {"code": -32602, "message": str(error)}}))
                except OSError:
                    pass
        except (OSError, TimeoutError):
            if not self._stop.is_set():
                self._failure("client-io")
        except BaseException:
            self._failure("client-internal")
        finally:
            with self._lock:
                for session_id in attached:
                    session = self._sessions.get(session_id)
                    if session:
                        session["attached"] = False
                self._sockets.discard(connection)
            connection.close()

    def _dispatch(self, method, params, attached, request_id):
        if method == "ping":
            _require(set(params) <= {"message"} and isinstance(params.get("message", ""), str)
                     and len(params.get("message", "")) <= 256, "invalid-ping")
            return {"message": params.get("message", ""), "timestamp": self._timestamp(), "protocolVersion": 3}, None
        if method == "models.list":
            _require(not params, "invalid-models")
            return {"models": [{"id": "qualification-context-oracle", "name": "Deterministic admitted-context peer"}]}, None
        _require(method in {"session.create", "session.resume", "session.options.update", "session.send", "session.abort", "session.destroy"}, "unsupported-method")
        session_id = params.get("sessionId")
        _require(isinstance(session_id, str) and 0 < len(session_id) <= 160
                 and re.fullmatch(r"[A-Za-z0-9._-]+", session_id), "invalid-session")
        with self._lock:
            if method in {"session.create", "session.resume"}:
                _session_config(params)
                _require(session_id not in attached, "session-already-attached")
                if method == "session.create":
                    _require(session_id not in self._sessions and len(self._sessions) < MAX_SESSIONS, "session-create-budget-or-duplicate")
                    self._sessions[session_id] = {"attached": False, "options": False}
                _require(session_id in self._sessions, "session-not-found")
                session = self._sessions[session_id]
                _require(not session["attached"], "session-concurrent-attachment")
                session["attached"], session["options"] = True, False
                attached.add(session_id)
                return {"sessionId": session_id}, None
            _require(session_id in attached, "session-not-attached")
            session = self._sessions[session_id]
            if method == "session.options.update":
                expected = {"sessionId": session_id, "skipCustomInstructions": True, "customAgentsLocalOnly": True,
                            "coauthorEnabled": False, "manageScheduleEnabled": False, "installedPlugins": []}
                _require(params == expected and all(type(params[key]) is type(value) for key, value in expected.items()), "empty-options")
                session["options"] = True
                return {"success": True}, None
            if method in {"session.abort", "session.destroy"}:
                _require(set(params) == {"sessionId"}, "invalid-session-operation")
                if method == "session.destroy":
                    session["attached"] = False
                    attached.remove(session_id)
                return {}, None
            _require(session["options"], "missing-empty-options")
            _require({"sessionId", "prompt"} <= set(params) <= {"sessionId", "prompt", "traceparent", "tracestate"}
                     and len(self._turns) < MAX_TURNS, "send-fields-or-budget")
            _require(all(key not in params or isinstance(params[key], str) and len(params[key]) <= 1024
                         for key in ("traceparent", "tracestate")), "send-trace")
            answer = derive_answer(params["prompt"])
            _require(all(row["nonce"] != answer["nonce"] for row in self._turns), "duplicate-turn-nonce")
            turn = {**answer, "sessionId": session_id, "requestId": request_id,
                    "requestHash": _hash(json.dumps(params, sort_keys=True, separators=(",", ":"))),
                    "messageId": str(uuid.uuid4()), "wireComplete": False}
            self._turns.append(copy.deepcopy(turn))
            return {"messageId": turn["messageId"]}, turn

    @staticmethod
    def _timestamp():
        return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())

    def _event(self, connection, session_id, kind, data):
        event = {"id": str(uuid.uuid4()), "parentId": None, "timestamp": self._timestamp(), "type": kind, "data": data}
        connection.sendall(_encode({"jsonrpc": "2.0", "method": "session.event", "params": {"sessionId": session_id, "event": event}}))

    def close(self):
        with self._close_lock:
            return self._close_owned()

    def _close_owned(self):
        with self._lock:
            if self._closed:
                return copy.deepcopy(self._close_receipt)
            self._closed = True
            self._stop.set()
            listener = self._listener
            connections = tuple(self._sockets)
        if listener is not None:
            listener.close()
        for connection in connections:
            try:
                connection.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
            connection.close()
            with self._lock:
                self._sockets.discard(connection)
        deadline = time.monotonic() + 3
        if self._accept_thread is not None and self._accept_thread.ident is not None:
            self._accept_thread.join(max(0, deadline - time.monotonic()))
        # The accept thread is joined before taking the final worker snapshot.
        with self._lock:
            workers = tuple(self._workers)
        for worker in workers:
            if worker.ident is not None:
                worker.join(max(0, deadline - time.monotonic()))
        with self._lock:
            live = sum(worker.is_alive() for worker in workers) + int(bool(self._accept_thread and self._accept_thread.is_alive()))
            if live or self._sockets:
                self._failure("close-incomplete")
            if self._journal is not None:
                self._journal.close()
            if not live:
                self._token = ""  # Release credential reference after owned workers have stopped.
            self._close_receipt = {"kind": "owned-copilot-peer-close", "complete": live == 0 and not self._sockets,
                                   "liveThreads": live, "openConnections": len(self._sockets),
                                   "connections": self._connections, "turns": len(self._turns), "errors": list(self._errors)}
            return copy.deepcopy(self._close_receipt)
