"""Real qualification operations, with private auth and stable owning-resource receipts.

Nothing here supplies an effect or provider response. Every semantic assertion is
made against a retained HTTP, native stream, browser, or actual factory record.
"""
from __future__ import annotations

from dataclasses import dataclass, field
import copy
import json
import os
from pathlib import Path
import secrets
import shutil
import time
import uuid

from agent_transport import AgentTransport
from artifacts import TOOL, check, sha
from case_records import retained, correlate_browser_protocol
from consumers import PROTOCOL_FEATURES
from native_lifecycle import NativeObservations
from owner_resources import seed_workflow
from runtime import Http, normal_sign_in, private_json, RUNTIME_ENVIRONMENT

HOME_PREFERENCES_PATH = "/_elsa/studio/preferences/home-layout"
NATIVE_HOME = "/_elsa/studio/home-layout"
HOST_HEADER = "X-Elsa-Studio-Host-Id"
BASE_GRANTS = ["agent:use", "agent/proposals:manage", "agent/audit:read", "studio/preferences:read", "studio/preferences:write"]


def atomic_json(path, value):
    path = Path(path)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex)
    private_json(temporary, value)
    os.replace(temporary, path)


def same(left, right):
    return json.dumps(left, sort_keys=True, separators=(",", ":"), allow_nan=False) == json.dumps(right, sort_keys=True, separators=(",", ":"), allow_nan=False)


def browser_inputs(config):
    """Export only allowlisted public journey inputs; the original auth config stays private."""
    summary = {key: config[key] for key in ("journey", "provider", "studioOrigin", "backendOrigin")}
    summary["driverSha256"] = sha(TOOL / "browser.mjs")
    if "workflow" in config:
        fields = ("definitionId", "activityId", "draftId", "definitionPath", "draftPath", "validationPath")
        summary["workflow"] = {key: config["workflow"][key] for key in fields}
        check(all(isinstance(value, str) and value for value in summary["workflow"].values()), "Browser summary requires actual native workflow identities and paths")
        check(all(summary["workflow"][key].startswith("/") and not summary["workflow"][key].startswith("//")
                  and "?" not in summary["workflow"][key] and "#" not in summary["workflow"][key]
                  for key in ("definitionPath", "draftPath", "validationPath")), "Browser summary paths must be local native paths without auth/query metadata")
    return summary


def expect_error(response, status, code, *, message=None):
    response.require_status(status)
    value = response.json()
    check(value.get("error", {}).get("code") == code, "Wrong native refusal cause; expected " + code)
    if message is not None:
        check(value["error"]["message"] == message, "Wrong native refusal message")
    return value


def exact_refusal(response, status, code):
    value = expect_error(response, status, code)
    return {"stage": "execute", "diagnostic": value["error"]["code"]}


@dataclass
class Turn:
    transport: AgentTransport
    session: str
    command_id: str
    message: object
    stream: object | None
    proposal: dict | None
    evidence: list
    offset: int


@dataclass
class JourneyRuntime:
    run: object
    backend_origin: str
    studio_origin: str
    admin: dict
    principals: list
    witness: dict = field(default_factory=lambda: json.loads((TOOL / "witness.json").read_text()))
    provider: str = "copilot"
    sequence: int = 0
    identities: dict = field(default_factory=dict)
    clients: dict = field(default_factory=dict)
    active_http: object | None = None
    active_identity: dict | None = None

    def __post_init__(self):
        self.root = self.run.root / "journeys"
        self.root.mkdir(mode=0o700)
        self.native = NativeObservations(self.root / "native")
        self.command_file = self.run.profiles["backend"]["commandFile"]
        self.controls = self.root / "controls"
        self.controls.mkdir(mode=0o700)
        self.gate_file = self.controls / "call-gate.json"
        self.cas_control = self.controls / "write-rendezvous.json"
        self.cas_receipt = self.controls / "write-rendezvous-receipt.json"
        atomic_json(self.gate_file, {"commandId": "not-an-active-command", "epoch": str(uuid.uuid4()), "state": "release"})

    def output(self, name):
        self.sequence += 1
        path = self.root / (f"{self.sequence:03d}-" + name)
        path.mkdir(mode=0o700)
        return path

    def save(self, output, name, value):
        path = Path(output) / (name + ".json")
        check(not path.exists(), "Never overwrite a journey observation")
        private_json(path, value)
        return retained(path)

    def sign_in(self, principal="admin"):
        selected = self.admin if principal == "admin" else self.principals[principal]
        output = self.output("normal-login-" + str(principal))
        http, identity = normal_sign_in(self.backend_origin, selected["username"], selected["password"],
                                      output / "storage-state-private.json", tenant_id=selected.get("tenant"))
        self.identities[principal], self.clients[principal] = identity, http
        if principal == "admin":
            self.active_http, self.active_identity = http, identity
        self.save(output, "identity", {key: value for key, value in identity.items() if key != "storageState"})
        return http, identity

    @property
    def backend(self): return self.active_http

    @property
    def studio(self): return Http(self.studio_origin, bearer=self.backend.bearer, opener=self.backend.opener)

    def transport(self, name, *, http=None):
        return AgentTransport(http or self.backend, self.output(name), command_file=self.command_file,
                              trace_file=self.run.profiles["backend"]["traceFiles"][self.provider])

    def snapshot(self, name, *, http=None, endpoint=HOME_PREFERENCES_PATH, host=None, expected=200):
        """Return actual response bytes as the stable owner ref, without receipt timestamps."""
        value, record = self.native.json(http or self.backend, name, endpoint,
            headers={HOST_HEADER: host or self.witness["hostId"]}, expected=expected)
        return value, record["body"], record["receipt"]

    def state(self, name, *, http=None):
        return self.snapshot(name, http=http, endpoint=self.witness["ownerEndpoint"])

    def attachment(self, state):
        w = self.witness
        return {"id": str(uuid.uuid4()), "moduleId": w["moduleId"], "schemaVersion": "1", "source": w["moduleId"],
                "sourceId": w["widgetId"], "label": "Acme status card", "contentType": w["contextType"],
                "sensitivity": "internal", "scope": w["route"], "origin": "clientSnapshot",
                "target": {"moduleId": w["moduleId"], "resourceType": w["resourceType"], "resourceId": w["widgetId"], "revision": state["revision"]},
                "content": {key: state[key] for key in ("widgetId", "revision", "visible")}}

    def turn(self, name, *, tool=None, arguments=None, http=None, attachment=None, capability=None, mode="build", command_id=None, drain=True):
        t = self.transport(name, http=http)
        trace = t.trace_file
        offset = trace.stat().st_size if trace.exists() else 0
        session_response = t.create_session({"conversationId": str(uuid.uuid4()), "mode": mode,
            "activeSurface": {"route": self.witness["route"]}, "autonomyMode": "auto-read-only"}).require_status(200)
        session = session_response.json()["data"]["sessionId"]
        cid = command_id or str(uuid.uuid4())
        command = t.set_command(cid, tool or self.witness["writeTool"], arguments if arguments is not None else {})
        body = {"content": "Owned package qualification request", "mode": mode, "requestId": str(uuid.uuid4()),
                "requestedCapabilities": [capability or ("acme.home.read" if mode == "explain" else "acme.home.visibility")],
                "contextAttachments": [] if attachment is None else [attachment]}
        message = t.message(session, body)
        stream = t.collect_turn(session) if drain and message.status == 200 else None
        proposals = [frame.value["payload"] for frame in stream.frames if frame.value["kind"] == "proposalCreated"] if stream else []
        check(len(proposals) <= 1, "One command created multiple native proposals")
        evidence = [session_response.receipt, command, message.receipt] + ([stream.receipt] if stream else [])
        # The mutable command file is an input, not a stable retained receipt. Copy its exact bytes now.
        command_copy = t.output / "selected-command.json"
        private_json(command_copy, {"commandId": cid, "toolName": tool or self.witness["writeTool"], "arguments": arguments if arguments is not None else {}})
        evidence[1] = retained(command_copy)
        return Turn(t, session, cid, message, stream, proposals[0] if proposals else None, evidence, offset)

    def prepare(self, name, *, http=None, command_id=None):
        state, _, read = self.state(name + "-state", http=http)
        turn = self.turn(name, http=http, arguments={"visible": not state["visible"]}, attachment=self.attachment(state), command_id=command_id)
        turn.message.require_status(200)
        check(turn.proposal is not None and turn.proposal["status"] == "awaitingApproval", "Actual review proposal was not observed")
        turn.evidence.append(read)
        return turn

    def approve(self, turn):
        check(turn.proposal is not None, "No actual proposal to approve")
        response = turn.transport.approve(turn.proposal["id"], revision=turn.proposal["baseRevision"]).require_status(200)
        check(response.json()["data"].get("approvalStatus") in ("approved", "Approved", 1)
              or response.json()["data"].get("status") == "approved", "Native approval did not confirm approved state")
        turn.evidence.append(response.receipt)
        return response

    def execute(self, turn, *, transport=None):
        return (transport or turn.transport).execute(turn.proposal["id"], revision=turn.proposal["baseRevision"])

    def settled(self, turn):
        stream = turn.transport.collect_turn(turn.session)
        turn.evidence.append(stream.receipt)
        return stream

    def tool_result(self, turn):
        trace = turn.transport.protocol_trace(turn.offset)
        rows = [row for row in trace["records"] if row.get("data", {}).get("commandId") == turn.command_id]
        check(sum(row["kind"] == self.provider + ".call" for row in rows) == 1, "Expected exactly one actual SDK call")
        results = [row["data"]["result"] for row in rows if row["kind"] == "copilot.return"]
        check(self.provider == "copilot" and len(results) == 1 and isinstance(results[0], dict), "A direct control requires the actual Copilot function return")
        turn.evidence.append(trace["receipt"])
        return results[0], trace

    def record(self, records, name, before, after, evidence, checks, *, actual=None, extra=None):
        observation = {"status": "failed" if actual else "passed", "ownerBefore": before, "ownerAfter": after,
                       "evidence": evidence, "observations": checks, **(extra or {})}
        if actual: observation.update(expectedFailureObserved=True, actual=actual)
        return records.add(name, observation)

    def browser(self, records, name, journey, *, provider=None, workflow=False, extra=None):
        # Renew actual native auth before every browser; no long-run token expiry workaround.
        self.sign_in()
        provider = provider or self.provider
        check(provider == self.provider, "Native provider profile and browser provider differ")
        root = self.output("browser-" + name)
        output = root / "observation"
        config = {"journey": journey, "provider": provider, "studioOrigin": self.studio_origin, "backendOrigin": self.backend_origin,
                  "storageState": self.active_identity["storageState"], "commandFile": str(self.command_file), "output": str(output), **(extra or {})}
        if workflow:
            config["workflow"] = seed_workflow(self.backend, root / "workflow-seed")
            if journey == "renderer-unavailable":
                selected = config["workflow"]
                observed, read = self.native.json(self.backend, "renderer-native-owner", selected["definitionPath"])
                draft = observed["draft"]
                canary = "<b data-qualification-canary>literal markup remains data " + secrets.token_hex(6) + "</b>"
                body = {"state": draft["state"], "layout": draft["layout"], "activityPresentation": [
                    {"nodeId": selected["activityId"], "displayName": canary, "description": "Actual owning workflow presentation"}]}
                _, saved = self.native.json(self.backend, "renderer-native-presentation-save", selected["draftPath"], body, method="PUT")
                confirmed, confirmation = self.native.json(self.backend, "renderer-native-presentation-confirmed", selected["definitionPath"])
                check(confirmed["draft"]["state"] == draft["state"] and confirmed["draft"]["layout"] == draft["layout"]
                      and confirmed["draft"]["activityPresentation"] == body["activityPresentation"], "Native owner did not retain the exact markup-like activity presentation")
                selected["htmlCanary"] = canary
                selected["evidence"].extend([read["receipt"], saved["receipt"], confirmation["receipt"]])
        if journey == "consumed-intent-remount":
            config["traceFile"] = str(self.run.profiles["backend"]["traceFiles"][provider])
        private_json(root / "config-private.json", config)
        t = self.transport("browser-protocol-" + name)
        offset = t.trace_file.stat().st_size if t.trace_file.exists() else 0
        node = shutil.which("node")
        check(node, "Actual browser driver needs Node")
        self.run.run("browser-" + name, [node, str(TOOL / "browser.mjs"), str(root / "config-private.json")], TOOL,
                     env={key: value for key, value in os.environ.items() if key in RUNTIME_ENVIRONMENT})
        observed = json.loads((output / "observation.json").read_text())
        check(not observed.get("failure"), "Browser control stopped with an unexpected cause: " + name)
        observed["evidence"].append(self.save(root, "browser-inputs", browser_inputs(config)))
        if workflow: observed["evidence"].extend(config["workflow"]["evidence"])
        if name not in ("malformed-result", "lost-confirmation", "deny-before-effect", "client-binding-removed"):
            correlation = correlate_browser_protocol(t, output, provider, offset=offset)
            observed["evidence"].extend([correlation["receipt"], correlation["trace"]])
            observed["observations"]["actualAdapter"] = True
        # Browser owner envelopes contain stable values only; filenames never enter their contents.
        records.add(name, observed)
        return observed

    def apply_features(self, name, changes, *, client=None):
        client = client or self.backend
        before, bref = self.native.generation(client, name + "-before")
        catalog, cref = self.native.json(client, name + "-catalog", "/modularity/features")
        features = {item["id"]: item for item in catalog["features"]}
        check(set(changes) <= set(features), "Native catalog does not advertise every requested feature")
        request = {"revision": catalog["revision"], "features": []}
        for identity, item in features.items():
            enabled, configuration = changes.get(identity, (item["enabled"], item["configuration"]))
            if enabled: check(item["runtimeAvailable"], "Cannot enable a descriptor without its actual runtime assembly")
            if item["enabled"] or identity in changes:
                request["features"].append({"id": identity, "enabled": enabled, "configuration": configuration})
        check(any(features[key]["enabled"] != enabled or features[key]["configuration"] != config for key, (enabled, config) in changes.items()),
              "No-op feature selection cannot establish generation change")
        applied, aref = self.native.json(client, name + "-apply", "/modularity/features/apply", request, method="POST")
        check(applied["reloadedShellCount"] > 0, "Full-state apply did not reload a real shell")
        after, afref = self.native.generation(client, name + "-after")
        check(before["generation"] != after["generation"], "Full-state feature apply did not change native generation")
        selected = {item["id"]: item for item in applied["catalog"]["features"]}
        check(all(selected[key]["enabled"] == enabled for key, (enabled, _) in changes.items()), "Native catalog did not confirm full-state feature selection")
        return [bref["receipt"], cref["receipt"], aref["receipt"], afref["receipt"]]

    def select_provider(self, provider):
        check(provider in PROTOCOL_FEATURES, "Unknown actual provider")
        actual, fixture = PROTOCOL_FEATURES[provider]
        catalog, _ = self.native.json(self.backend, "provider-selection-catalog-" + provider, "/modularity/features")
        selected = {row["id"]: row for row in catalog["features"]}
        active_pair = {identity for pair in PROTOCOL_FEATURES.values() for identity in pair if selected[identity]["enabled"]}
        if self.provider == provider and active_pair == {actual, fixture}:
            # Observe the already-selected profile without manufacturing a no-op generation change.
            check(selected[fixture]["configuration"].get("GateFile") == str(self.gate_file)
                  or selected[fixture]["configuration"].get("gateFile") == str(self.gate_file), "Selected fixture lacks its real configured pre-call gate")
            bootstrap = self.transport("provider-bootstrap").bootstrap().require_status(200)
            check(bootstrap.json()["data"]["providerStatus"] == "available", "Actual selected adapter is unavailable")
            return [bootstrap.receipt]
        config = ({"Enabled": True, "RuntimeUrl": "http://127.0.0.1:1", "UseLoggedInUser": False,
                   "GitHubToken": "", "GitHubTokenEnvironmentVariable": "", "RuntimeConnectionToken": ""} if provider == "copilot"
                  else {"Enabled": True, "ApiKey": "protocol-fixture-only-not-a-credential", "ApiKeyEnvironmentVariable": ""})
        fixture_config = {"CommandFile": str(self.command_file), "TraceFile": str(self.run.profiles["backend"]["traceFiles"][provider]), "GateFile": str(self.gate_file)}
        changes = {identity: (identity in (actual, fixture), fixture_config if identity == fixture else config if identity == actual else selected[identity]["configuration"])
                   for pair in PROTOCOL_FEATURES.values() for identity in pair}
        evidence = self.apply_features("select-provider-" + provider, changes)
        self.provider = provider
        bootstrap = self.transport("provider-bootstrap").bootstrap().require_status(200)
        check(bootstrap.json()["data"]["providerStatus"] == "available", "Actual selected adapter is unavailable")
        return evidence + [bootstrap.receipt]

    def native_write(self, name, http, document, *, host=None, revision=None, endpoint=NATIVE_HOME):
        headers = {HOST_HEADER: host or self.witness["hostId"]}
        headers["If-Match" if revision else "If-None-Match"] = '"' + revision + '"' if revision else "*"
        value, record = self.native.json(http, name, endpoint, {"schemaVersion": 1, "value": document}, method="PUT", headers=headers)
        check(same(value["value"], document) and bool(value["revision"]), "Native owning preference read/write did not confirm submitted JSON")
        return value, record

    def set_grants(self, index, profile):
        endpoint = f"/__qualification/principals/{index}/authority"
        owner, read = self.native.json(self.backend, "authority-before", endpoint)
        selected, write = self.native.json(self.backend, "authority-select", f"/__qualification/principals/{index}/grant-profile",
            {"profile": profile, "revision": owner["revision"]}, method="POST")
        check(selected["subject"] == owner["subject"] and selected["tenant"] == owner["tenant"] and selected["revision"] != owner["revision"]
              and selected["selectedProfile"] == profile and not selected["roleIds"], "Owning IAM store did not confirm exact same-principal profile change")
        expected = {key for key in BASE_GRANTS if profile != "without-read" or key != "studio/preferences:read"}
        if profile == "without-write": expected.remove("studio/preferences:write")
        check(set(selected["directPermissions"]) == expected, "Actual IAM selection did not apply the exact expected concrete grants")
        confirmed, confirmation = self.native.json(self.backend, "authority-confirmed", endpoint)
        check(confirmed["revision"] == selected["revision"] and confirmed["directPermissions"] == selected["directPermissions"]
              and confirmed["subject"] == owner["subject"] and confirmed["tenant"] == owner["tenant"] and not confirmed["roleIds"],
              "Independent Identity readback did not confirm the selected revision/grants")
        self.sign_in(index)
        check(self.identities[index]["subject"] == owner["subject"] and self.identities[index]["tenantId"] == owner["tenant"], "Fresh login changed the grant-profile principal")
        return [read["receipt"], write["receipt"], confirmation["receipt"]]
