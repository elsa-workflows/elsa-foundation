"""T038 genuine Weaver 1.0 -> 1.1 native upgrade lane.

All controls authored here are UNRUN until the owning package qualifier executes
them. This lane uses separate processes, feeds, credentials, native owners and
runtime databases. It never promotes the ordinary witness or substitutes a
synthetic old preparation. Failed/unknown observations remain failed records.
"""
from __future__ import annotations

import copy
import base64
import hashlib
import json
import math
import os
from pathlib import Path
import secrets
import signal
import stat
import subprocess
import time
import uuid
import zipfile
from urllib.parse import quote

from artifacts import REPO, TOOL, check, sha
from case_records import retained
from consumers import (DURABLE_EFFECTS_FEATURE, DURABLE_STORE_FEATURE, PROTOCOL_FEATURES,
                       configure_runtime, select_native_archives,
                       stage_native_archives)
from effects_journeys import (CLASSES, EVIDENCE_CATEGORY, HOST, POLICY_CODE, PROVIDERS,
                             RESTART_REQUIREMENTS, _effect_restart_trace, _owned, _owned_file,
                             _restart_http, _restart_identity, _restart_native, _restart_proposal,
                             effect_restart_bindings, native_frames, require_adapter_result,
                             require_called_refusal, require_normal_terminal)
from journey_runtime import (BASE_GRANTS, HOME_PREFERENCES_PATH, HOST_HEADER, JourneyRuntime, atomic_json, same)
from owner_resources import seed_personal_layout
from restart_journeys import _await_owner, _command_trace, _prepare_owner_resolution, instant
from runtime import (RUNTIME_ENVIRONMENT, RestartableServer, owned_content_root, owned_origin,
                     private_json, published_files, published_files_digest)
from upgrade_distribution import (BASELINE_SOURCE, BASELINE_TREE, JOURNAL_HEADER, OPERATOR_ASSEMBLY, RESOURCE_FEATURE,
                                  RESOURCE_MODULE, RESOURCE_OWNER, RESOURCE_TOOLS,
                                  prepare_upgrade_content_root, prepare_upgrade_distributions,
                                  select_upgrade_generation, upgrade_operator_command)
from upgrade_store import require_identity_rows, require_retained_counter, snapshot_store
from weaver_transport import _json as strict_json

CASE_KEYS = ("legacy-pending-upgrade", "legacy-approved-upgrade", "legacy-admin-disabled-upgrade",
             "expired-upgrade", "client-awaiting-upgrade", "all-legacy-row-kinds", "effects-held", "effects-finalized")
LEGACY_RESTART_CASES = tuple(provider + "/" + name for provider in PROVIDERS for name in CASE_KEYS)
check(all(name in RESTART_REQUIREMENTS for name in LEGACY_RESTART_CASES), "Upgrade inventory drifted")
INSPECTOR = "/_qualification/acme/upgrade-records/"
ATTENTION = "/_elsa/attention/items"
PREPARATION_ENDPOINT = "/_qualification/acme/home-widget/effects/prepare"
OWNED_SECONDS = 60
OPERATOR_REASON = "T038 independently packaged native upgrade qualification"
OPERATOR_ACTOR = "weaver-conformance-owned-operator"
BRANCHES = {
    "legacy-pending-upgrade": ("pending",), "legacy-approved-upgrade": ("approved",),
    "legacy-admin-disabled-upgrade": ("off-pending", "off-approved", "on-pending", "on-approved"),
    "expired-upgrade": ("pending", "approved"), "all-legacy-row-kinds": ("pending",),
}


def _need(value, message):
    check(value, "Legacy upgrade evidence: " + message)


class UpgradeObservationFailure(RuntimeError):
    def __init__(self, message, observation):
        super().__init__(message)
        self.observation = observation


def upgrade_bindings():
    paths = set(effect_restart_bindings())
    paths.update(str(path.relative_to(REPO)) for path in TOOL.glob("upgrade*.py"))
    paths.update(str(path.relative_to(REPO)) for directory in ("Resource.LegacyUpgrade", "Resource.Backend", "Upgrade.Operator", "ProviderProtocolFixtures", "BackendConsumer")
                 for path in (TOOL / "fixtures" / directory).rglob("*") if path.is_file())
    paths.add(str((TOOL / "owner_resources.py").relative_to(REPO)))
    paths.update(str(path.relative_to(REPO)) for path in (REPO / "Extensions/Weaver/src").rglob("*")
                 if path.is_file() and path.suffix in {".cs", ".csproj", ".props", ".targets"})
    paths.update(str((TOOL / name).relative_to(REPO)) for name in ("authors.py", "artifacts.py"))
    return {name: sha(REPO / name) for name in sorted(paths)}


def _native(runtime, name, path, body=None, *, method="GET"):
    headers = {HOST_HEADER: HOST} if path == HOME_PREFERENCES_PATH or path.startswith(RESOURCE_OWNER) else None
    value, record = runtime.native.json(runtime.backend, name, path, body, method=method, headers=headers)
    return value, record["receipt"]


def _select_actor(runtime, actor):
    # Every login is native; no previous-generation token crosses a restart.
    http, identity = runtime.sign_in(actor)
    runtime.active_http, runtime.active_identity = http, identity


def _identity(runtime, name):
    return _native(runtime, name, "/_elsa/identity/session")[1]


def _snapshot(runtime, name):
    """Read the native owner and armed observer before retaining journal bytes.

    An unknown journal response aborts before any count is used. No observer is
    repaired, truncated, recreated or reused after a poisoned observation.
    """
    refs = {key: _native(runtime, name + "-" + key, endpoint)[1] for key, endpoint in (
        ("owner", RESOURCE_OWNER), ("layout", HOME_PREFERENCES_PATH), ("entries", RESOURCE_OWNER + "/execution-entries"))}
    path = Path(runtime.upgrade_store["executionObservationFile"])
    record = {"sourcePath": str(path), "observedAt": time.time(), "status": "unknown"}
    output = runtime.output(name + "-journal")
    try:
        _need(path.is_absolute() and path.resolve().is_relative_to(runtime.run.root.resolve())
              and not path.is_symlink() and not path.parent.is_symlink(), "foreign journal")
        with path.open("rb") as stream:
            before = os.fstat(stream.fileno())
            _need(stat.S_ISREG(before.st_mode) and before.st_size <= 2 * 1024 * 1024, "unbounded journal")
            raw = stream.read(2 * 1024 * 1024 + 1)
            after = os.fstat(stream.fileno())
        _need((before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) ==
              (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns) and len(raw) == before.st_size, "journal changed during observation")
        record.update(status="observed", device=before.st_dev, inode=before.st_ino, mode=stat.S_IMODE(before.st_mode),
                      parentMode=stat.S_IMODE(path.parent.stat().st_mode), rawBody=raw.decode("utf-8", "strict"),
                      size=len(raw), bytesSha256=hashlib.sha256(raw).hexdigest(), failedMarker=os.path.lexists(str(path) + ".failed"),
                      probeExists=os.path.lexists(str(path) + ".probe"))
        _journal(record, str(path), runtime.active_identity)
        refs["journal"] = runtime.save(output, "observed", record)
    except Exception as failure:
        refs["failedUnknown"] = runtime.save(output, "failed-unknown", {"observation": record,
            "cause": type(failure).__name__, "requiresFreshObservationPath": True})
        raise UpgradeObservationFailure("Upgrade journal is failed/unknown; no zero or retry is inferred", refs) from failure
    return refs


def _journal(value, path, owner):
    _need(value.get("status") == "observed" and value.get("sourcePath") == path and value.get("mode") == 0o600
          and value.get("parentMode") == 0o700 and value.get("failedMarker") is False and value.get("probeExists") is False
          and all(type(value.get(key)) is int for key in ("device", "inode", "mode", "parentMode", "size"))
          and type(value.get("observedAt")) in (int, float) and math.isfinite(value["observedAt"]), "journal is unarmed/poisoned/foreign")
    raw = value.get("rawBody")
    _need(isinstance(raw, str) and len(raw.encode()) <= 2 * 1024 * 1024 and value.get("size") == len(raw.encode())
          and value.get("bytesSha256") == hashlib.sha256(raw.encode()).hexdigest(), "journal byte custody differs")
    lines = raw.splitlines()
    _need(lines and lines.pop(0) == JOURNAL_HEADER, "wrong real journal header")
    rows, selected, counts = [strict_json(line) for line in lines], [], {}
    fields = {"Actor", "Tenant", "Host", "RequestedActor", "RequestedTenant", "ToolName", "RequestedToolName",
              "SessionId", "TurnId", "InvocationId", "ToolCallId", "EntryNumber"}
    _need(len(rows) <= 4096, "too many journal rows")
    for row in rows:
        _need(isinstance(row, dict) and set(row) == fields and all(isinstance(row[name], str) and row[name].strip()
              and len(row[name]) <= 256 for name in fields - {"EntryNumber"}), "malformed journal identity")
        key = tuple(row[name] for name in ("Actor", "Tenant", "Host"))
        counts[key] = counts.get(key, 0) + 1
        _need(row["ToolName"] in RESOURCE_TOOLS.values() and row["RequestedToolName"] == row["ToolName"]
              and row["RequestedActor"] == row["Actor"] and row["RequestedTenant"] == row["Tenant"]
              and type(row["EntryNumber"]) is int and row["EntryNumber"] == counts[key], "journal tool/order/request identity differs")
        if key == (owner["subject"], owner["tenantId"], HOST): selected.append(row)
    return selected


def _read_snapshot(root, refs, origin, owner, journal_path):
    _need(set(refs) == {"owner", "layout", "entries", "journal"}, "missing complete native/journal snapshot")
    values = {key: _restart_native(root, refs[key], origin, endpoint) for key, endpoint in (
        ("owner", RESOURCE_OWNER), ("layout", HOME_PREFERENCES_PATH), ("entries", RESOURCE_OWNER + "/execution-entries"))}
    entries = _journal(_owned(root, refs["journal"]), journal_path, owner)
    widget = values["owner"]
    _need(widget.get("widgetId") == "acme.home:status" and widget.get("hostId") == HOST and type(widget.get("visible")) is bool
          and widget.get("revision") == values["layout"].get("revision") and bool(widget.get("revision")), "wrong native personal owner")
    projected = [{key[0].lower() + key[1:]: item for key, item in row.items()} for row in entries]
    _need(same(values["entries"], {"entries": projected}), "native authorized entries differ from the real journal")
    return {**values, "journal": entries}


def _operator(runtime, prepared, generation, command):
    """Finite owned stdin/stdout process; never invokes finalize/force or echoes credentials."""
    item = prepared[generation]["operator"]
    database = runtime.upgrade_store["database"]
    argv = upgrade_operator_command(runtime.run, prepared, generation, database)
    request = {"version": 1, "command": command, "selection": {"kind": "modules", "modules": ["Weaver"]},
               "provider": "Sqlite", "connection": "Data Source=" + database,
               "finalization": {"family": "Weaver", "version": None}}
    if command in ("hold", "release"): request["finalization"]["operator"] = OPERATOR_ACTOR
    if command == "hold": request["finalization"]["reason"] = OPERATOR_REASON
    output = runtime.output("operator-" + generation + "-" + command)
    stdin = json.dumps(request, separators=(",", ":")).encode()
    stdin_path = output / "stdin.json"
    stdin_path.write_bytes(stdin); stdin_path.chmod(0o600)
    env = {key: value for key, value in item["runtimeEnv"].items() if key in RUNTIME_ENVIRONMENT}
    caches = {}
    for key, name in (("NUGET_PACKAGES", "packages"), ("NUGET_HTTP_CACHE_PATH", "http")):
        target = output / name
        target.mkdir(mode=0o700)
        env[key], caches[key] = str(target), str(target)
    record = {"generation": generation, "command": argv, "operator": item, "request": retained(stdin_path),
              "startedAt": time.time(), "timeoutSeconds": OWNED_SECONDS, "cachePaths": caches}
    stdout, stderr = output / "stdout.json", output / "stderr.log"
    process = None
    try:
        with stdout.open("xb") as out, stderr.open("xb") as err:
            stdout.chmod(0o600); stderr.chmod(0o600)
            process = subprocess.Popen(argv, stdin=subprocess.PIPE, stdout=out, stderr=err, env=env,
                                       cwd=item["published"], start_new_session=True)
            record["pid"] = process.pid
            try:
                process.communicate(stdin, timeout=OWNED_SECONDS)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.communicate(timeout=5)
                record["timedOut"] = True
            record["exitCode"] = process.returncode
    except BaseException as failure:
        record["failureType"] = type(failure).__name__
        raise
    finally:
        if process is not None:
            try:
                record.update(_stop_operator(process))
            except BaseException as failure:
                record.update(processStopped=False, cleanupFailureType=type(failure).__name__)
        record["endedAt"] = time.time()
        if stdout.exists(): record["stdout"] = retained(stdout)
        if stderr.exists(): record["stderr"] = retained(stderr)
        record["runtimeCacheFiles"] = {key: sorted(str(path.relative_to(Path(directory))) for path in Path(directory).rglob("*") if path.is_file())
                                       for key, directory in caches.items()}
        reference = runtime.save(output, "process", record)
    _read_operator(runtime.run.root, reference, generation=generation, command=command, database=database)
    return reference


def _stop_operator(process):
    """Reap exactly the process group started with start_new_session=True."""
    _need(type(process.pid) is int and process.pid > 0, "operator cleanup requires its exact positive owned PID")
    result = {"pid": process.pid}
    if process.poll() is None:
        try:
            os.killpg(process.pid, signal.SIGKILL)
            result["cleanupSignal"] = "SIGKILL"
        except ProcessLookupError:
            result["cleanupTargetAbsent"] = True
        process.wait(timeout=5)
    _need(process.poll() is not None, "owned operator did not stop after bounded cleanup")
    result.update(processStopped=True, exitCode=process.returncode)
    return result


def _read_operator(root, ref, *, generation, command, database):
    value = _owned(root, ref)
    item = value["operator"]
    prefix = item.get("commandPrefix")
    _need(item.get("assembly") == OPERATOR_ASSEMBLY and isinstance(prefix, list) and len(prefix) == 2
          and isinstance(prefix[0], str) and prefix[0] and prefix[1] == str(Path(item["published"]) / OPERATOR_ASSEMBLY)
          and item.get("status") == "prepared" and item.get("family") == "Weaver" and item.get("provider") == "Sqlite"
          and item.get("allowedOperations") == ["hold", "release", "status"], "not the exact independently packaged existing-tooling operator")
    _need(value.get("generation") == generation and type(value.get("exitCode")) is int and value.get("exitCode") == 0
          and value.get("processStopped") is True and value.get("timedOut") is not True
          and type(value.get("pid")) is int and value["pid"] > 0 and value.get("timeoutSeconds") == OWNED_SECONDS
          and 0 <= value["endedAt"] - value["startedAt"] <= OWNED_SECONDS + 5, "operator did not exit successfully within its owned bound")
    _need(value.get("command") == [*item["commandPrefix"], "--owned-root", str(Path(database).parent.parent), "--database", database]
          and set(value.get("runtimeCacheFiles", {})) == {"NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH"}
          and all(not files for files in value["runtimeCacheFiles"].values()), "operator target/command/cache identity differs")
    _need(published_files(item["published"]) == item["publishedFiles"]
          and published_files_digest(item["publishedFiles"]) == item["publishedFilesSha256"], "operator binary input changed")
    _require_closure(root, item["closure"])
    _owned_file(root, item["frontendAudit"])
    _need(not Path(item["frontendAudit"]["path"]).read_text().strip() and item["binaryProvenance"]["assets"] == item["closure"]["assets"],
          "operator invoked frontend tools or substituted its restored dependency graph")
    _require_binary_provenance(root, item["binaryProvenance"])
    request = _owned(root, value["request"])
    expected = {"version": 1, "command": command, "selection": {"kind": "modules", "modules": ["Weaver"]}, "provider": "Sqlite",
                "connection": "Data Source=" + database, "finalization": {"family": "Weaver", "version": None}}
    if command in ("hold", "release"): expected["finalization"]["operator"] = OPERATOR_ACTOR
    if command == "hold": expected["finalization"]["reason"] = OPERATOR_REASON
    _need(same(request, expected), "operator request was not the exact existing family-wide hold/release/status contract")
    _owned_file(root, value["stdout"]); _owned_file(root, value["stderr"])
    _need(Path(value["stdout"]["path"]).stat().st_size <= 1024 * 1024, "operator response exceeds bound")
    response = _owned(root, value["stdout"])
    _need(response.get("version") == 1 and response.get("status") == "ok" and response.get("exitCode") == 0
          and response.get("command") == command and response.get("error") is None, "operator response refused the command")
    payload = response.get("finalization", {})
    families = payload.get("families", [])
    _need(payload.get("provider") == "Sqlite" and len(families) == 1 and families[0].get("family") == "Weaver"
          and families[0].get("module") == "Weaver" and isinstance(families[0].get("databaseIdentity"), str)
          and bool(families[0]["databaseIdentity"]), "operator did not read the owned Weaver family")
    return families[0]


def _bound_operator(root, row, ref, generation, command):
    _need(same(_owned(root, ref).get("operator"), row["generations"][generation]["operator"]), "operator process substituted another package generation")
    return _read_operator(root, ref, generation=generation, command=command, database=row["store"]["database"])


def _require_binary_provenance(root, proof):
    _owned_file(root, proof["deps"]); _owned_file(root, proof["assets"])
    _need(isinstance(proof.get("files"), list) and proof["files"], "missing package-only binary provenance")
    for row in proof["files"]:
        for name in ("archive", "restored", "published"): _owned_file(root, row[name])
        with zipfile.ZipFile(row["archive"]["path"]) as archive:
            _need(archive.namelist().count(row["archiveEntry"]) == 1, "nonunique package binary entry")
            digest = hashlib.sha256(archive.read(row["archiveEntry"])).hexdigest()
        _need(digest == row["archiveEntrySha256"] == row["restored"]["sha256"] == row["published"]["sha256"], "package/published bytes differ")


def _require_closure(root, closure):
    _need(closure.get("status") == "passed" and closure.get("sourceReferences") == [], "source-linked/unprepared dependency closure")
    _owned_file(root, closure["lock"])
    graph = _owned(root, closure["assets"])
    folders = closure.get("packageFolders", [])
    _need(len(folders) == 1 and Path(folders[0]).resolve().is_relative_to(Path(root).resolve())
          and {Path(value).resolve() for value in graph.get("packageFolders", {})} == {Path(folders[0]).resolve()}
          and graph.get("libraries") and all(value.get("type") == "package" for value in graph["libraries"].values()), "restore used another cache or product source")
    cached = closure.get("cachedArchives", [])
    _need(len(cached) == len(graph["libraries"]) and {value["identity"] for value in cached} == set(graph["libraries"]), "cached archive roster differs from package-only graph")
    for item in cached:
        for name in ("archive", "archiveHashSidecar", "nugetMetadata"): _owned_file(root, item[name])
        archive = Path(item["archive"]["path"])
        digest = base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()).decode()
        metadata = _owned(root, item["nugetMetadata"])
        _need(archive.resolve().is_relative_to(Path(folders[0]).resolve()) and digest == item["archiveSha512"]
              == Path(item["archiveHashSidecar"]["path"]).read_text().strip()
              and metadata.get("contentHash") == item["sha512"] == graph["libraries"][item["identity"]]["sha512"], "NuGet archive/extraction identities differ")


def _prepare(runtime, actor, name, *, admin=False, approved=False):
    _select_actor(runtime, actor)
    seed_personal_layout(runtime.backend, runtime.output(name + "-personal-owner-seed") / "seed")
    identity = _identity(runtime, name + "-identity-before")
    before = _snapshot(runtime, name + "-before")
    owner = _restart_native(runtime.run.root, before["owner"], runtime.backend_origin, RESOURCE_OWNER)
    tool = RESOURCE_TOOLS["admin" if admin else "write"]
    turn = runtime.turn(name, tool=tool, arguments={"visible": not owner["visible"]},
                        attachment=runtime.attachment(owner), capability="acme.upgrade.admin" if admin else "acme.upgrade.write")
    turn.message.require_status(200)
    _need(turn.proposal is not None and turn.proposal.get("status") == "awaitingApproval", "no actual old pending proposal")
    branch = {"actor": actor, "sessionId": turn.session, "commandId": turn.command_id, "proposalId": turn.proposal["id"],
              "prepared": copy.deepcopy(turn.proposal["preparedInvocation"]), "approved": approved, "times": {"prepared": time.time()},
              "snapshots": {"before": before}, "refs": {"identityBefore": identity, "initialStream": turn.stream.receipt,
                "command": next(ref for ref in turn.evidence if Path(ref["path"]).name == "selected-command.json")}}
    if approved: branch["refs"]["approvedBefore"] = runtime.approve(turn).receipt
    branch["refs"]["oldInspector"] = _native(runtime, name + "-old-inspector", INSPECTOR + quote(turn.session, safe=""))[1]
    return turn, branch


def _reopen(runtime, branch, name):
    _select_actor(runtime, branch["actor"])
    branch["refs"]["identityRecovered"] = _identity(runtime, name + "-identity-recovered")
    t = runtime.transport(name + "-recovered")
    response, at, attempts = _await_owner(t, branch["sessionId"], tolerate=True)
    response.require_status(200)
    branch["refs"]["ownershipAttempts"] = attempts
    branch["refs"]["recoveredSession"] = response.receipt
    branch["times"]["recovered"] = at
    branch["refs"]["recoveredProposals"] = t.request("GET", "/_elsa/weaver/sessions/" + branch["sessionId"] + "/proposals").require_status(200).receipt
    branch["snapshots"]["recovered"] = _snapshot(runtime, name + "-recovered-no-dispatch")
    return t


def _dispatch(runtime, turn, branch, name):
    t = _reopen(runtime, branch, name)
    t.set_command(branch["commandId"], branch["prepared"]["toolName"], branch["prepared"]["arguments"])
    branch["refs"]["policy"] = t.bootstrap().require_status(200).receipt
    if not branch["approved"]:
        branch["refs"]["decision"] = t.approve(branch["proposalId"], revision=turn.proposal["baseRevision"]).receipt
    branch["times"]["execute"] = time.time()
    branch["refs"]["execute"] = t.execute(branch["proposalId"], revision=turn.proposal["baseRevision"]).receipt
    branch["refs"]["continuation"] = t.collect_turn(branch["sessionId"]).receipt
    branch["refs"]["terminalProposals"] = t.request("GET", "/_elsa/weaver/sessions/" + branch["sessionId"] + "/proposals").require_status(200).receipt
    branch["refs"]["session"] = t.session(branch["sessionId"]).require_status(200).receipt
    branch["refs"]["audit"] = t.audit(branch["sessionId"]).require_status(200).receipt
    branch["times"]["disclosureStopped"] = time.time()
    branch["refs"]["trace"], branch["refs"]["rawTrace"] = _command_trace(t, {"traceOffset": turn.offset}, turn.command_id, name, owned_root=runtime.run.root)
    branch["snapshots"]["after"] = _snapshot(runtime, name + "-after")
    branch["times"]["repeated"] = time.time()
    branch["refs"]["repeatExecute"] = t.execute(branch["proposalId"], revision=turn.proposal["baseRevision"]).receipt
    branch["snapshots"]["repeated"] = _snapshot(runtime, name + "-after-repeat")
    branch["refs"]["identityAfter"] = _identity(runtime, name + "-identity-after")


def _new_record(run, provider, key, prepared, store, lane_ref):
    return {"id": provider + "/" + key, "provider": provider, "adapter": provider, "source": run.source,
            "status": "failed", "qualified": False, "independentPackageWitness": False, "liveProvider": False,
            "evidenceCategory": EVIDENCE_CATEGORY, "bindings": upgrade_bindings(), "branches": {}, "refs": {},
            "store": store, "lane": lane_ref, "generations": {name: prepared[name]["record"] for name in ("old", "current")}}


def _stage_current_home(run, prepared):
    """Stage the exact ordinary author closure only in the current generation's private feed."""
    current = prepared["current"]["profile"]
    previous, run.profiles["backend"] = run.profiles.get("backend"), current
    try:
        author = run.report["authors"]["backend"]
        stage_native_archives(run, "backend", select_native_archives(run, "backend", [author["packed"]["package"]], author))
    finally:
        if previous is None: run.profiles.pop("backend", None)
        else: run.profiles["backend"] = previous


def _start_current(runtime, prepared):
    selected = select_upgrade_generation(runtime.run, prepared, "current", effects=True)
    # Acme.Home's classed contributions are present only in this generation.
    root = owned_content_root(runtime.run, runtime.run.profiles["backend"])
    observations = root / "observations"
    paths = {"execution": observations / "home-execution.jsonl", "checks": observations / "home-checks.jsonl"}
    for kind, path in paths.items():
        _need(not any(os.path.lexists(str(path) + suffix) for suffix in ("", ".failed", ".probe")), "Home journal path was used")
        with path.open("x") as stream: stream.write(JOURNAL_HEADER + "\n" if kind == "execution" else "")
        path.chmod(0o600)
    shells = strict_json((root / "shells.json").read_text())
    shells["CShells"]["Shells"]["Authoring"]["Features"]["AcmeHomeWidget"] = {
        "StudioHostId": HOST, "ExecutionObservationFile": str(paths["execution"]), "CheckObservationFile": str(paths["checks"])}
    private_json(root / "shells.json", shells)
    return selected


def _restart(runtime, host, name, callback=None):
    record = host.restart(name, while_stopped=callback)
    ref = runtime.save(runtime.output(name + "-restart"), "restart", record)
    runtime.sign_in()
    return ref


def _store_snapshot(runtime, session, name):
    return runtime.save(runtime.output(name), "store", snapshot_store(runtime.run.root, runtime.upgrade_store["database"], [session]))


def _rows_before(runtime, state, row):
    turn, branch = state
    _select_actor(runtime, branch["actor"])
    event_id = uuid.uuid4().hex
    # A live engine owns/renews its old sessions. The caller first restarts the
    # old process, releasing its leases without hydrating this particular session.
    endpoint = INSPECTOR + turn.session + "/owed-audit"
    attempts, deadline = [], time.monotonic() + OWNED_SECONDS
    while True:
        receipt, raw = runtime.native.request(runtime.backend, "old-owed-audit-supplement", endpoint, {"eventId": event_id}, method="POST")
        attempts.append(receipt["receipt"])
        value, ref = strict_json(raw.decode("utf-8", "strict")), receipt["receipt"]
        if receipt["status"] == 200: break
        _need(receipt["status"] == 409 and value.get("code") == "acme.upgrade.record_ownership_unavailable"
              and time.monotonic() < deadline, "owed-audit write was refused/unknown; never retry with a new identity")
        time.sleep(0.5)
    row["refs"]["owedAuditSupplement"] = ref
    row["supplement"] = {"kind": "PendingAuditStoreWrite", "eventId": event_id, "response": value, "ownershipAttempts": attempts}
    row["refs"]["storeBefore"] = _store_snapshot(runtime, turn.session, "old-all-row-snapshot")
    row["refs"]["decodeBefore"] = _native(runtime, "old-all-row-decode", INSPECTOR + turn.session)[1]
    # Neither the list nor the audit read hydrates the engine's paused turn.
    row["refs"]["listBefore"] = turn.transport.request("GET", "/_elsa/weaver/sessions").require_status(200).receipt
    row["refs"]["auditBefore"] = turn.transport.audit().require_status(200).receipt


def _rows_after(runtime, state, row):
    turn, branch = state
    _select_actor(runtime, branch["actor"])
    branch["refs"]["identityRecovered"] = _identity(runtime, "rows-current-identity")
    # The inspector must precede every engine hydration of this old session.
    row["refs"]["decodeAfter"] = _native(runtime, "current-all-row-decode", INSPECTOR + turn.session)[1]
    row["refs"]["storeAfter"] = _store_snapshot(runtime, turn.session, "current-all-row-snapshot")
    t = runtime.transport("current-all-row-readers")
    row["refs"]["listAfter"] = t.request("GET", "/_elsa/weaver/sessions").require_status(200).receipt
    row["refs"]["auditAfter"] = t.audit().require_status(200).receipt
    branch["snapshots"]["after"] = _snapshot(runtime, "current-all-row-owner")
    branch["refs"]["identityAfter"] = _identity(runtime, "rows-current-identity-after")
    branch["refs"]["trace"], branch["refs"]["rawTrace"] = _command_trace(t, {"traceOffset": turn.offset}, turn.command_id,
        "all-old-row-kinds", owned_root=runtime.run.root)


def _observe_effects(runtime, name):
    refs = {key: _native(runtime, name + "-" + key, endpoint)[1] for key, endpoint in (
        ("catalog", "/modularity/features"), ("attention", ATTENTION), ("capabilities", "/capabilities"),
        ("generation", "/__qualification/generation"))}
    refs["bootstrap"] = runtime.transport(name + "-bootstrap").bootstrap().require_status(200).receipt
    return refs


def _observe_turn(runtime, name, attachment):
    t = runtime.transport(name)
    offset = t.trace_file.stat().st_size if t.trace_file.exists() else 0
    created = t.create_session({"conversationId": str(uuid.uuid4()), "mode": "build", "autonomyMode": "manual",
                                "activeSurface": {"route": "/qualification/acme-home-effects"}}).require_status(200)
    session, command = created.json()["data"]["sessionId"], uuid.uuid4().hex
    document = {"commandId": command, "mode": "observe"}
    atomic_json(t.command_file, document)
    command_ref = runtime.save(t.output, "selected-observation-command", document)
    message = t.message(session, {"content": "Observe current effects admission without requesting a tool call", "mode": "build",
        "requestId": str(uuid.uuid4()), "requestedCapabilities": ["acme.home.effects"], "contextAttachments": [attachment]}).require_status(200)
    stream = t.collect_turn(session)
    trace, raw = _command_trace(t, {"traceOffset": offset}, command, name, owned_root=runtime.run.root)
    return {"sessionId": session, "commandId": command, "create": created.receipt, "command": command_ref,
            "message": message.receipt, "stream": stream.receipt, "trace": trace, "rawTrace": raw}


def _preparation_probe(runtime, session, expected_visible, *, held):
    output = runtime.output("held-preparation" if held else "released-preparation")
    request = {"sessionId": session, "expectedVisible": expected_visible}
    request_ref = runtime.save(output, "request", request)
    # This module endpoint invokes the existing public invoker under the real
    # current owned session/policy. It never approves/executes. Its server UUIDs
    # are separate from actual provider-protocol call identities.
    _, response = runtime.native.json(runtime.backend, "held-public-preparation" if held else "released-public-preparation",
        PREPARATION_ENDPOINT, request, method="POST", headers={HOST_HEADER: HOST}, expected=409 if held else 200)
    proposals = runtime.transport("preparation-owning-proposals").request("GET", "/_elsa/weaver/sessions/" + session + "/proposals").require_status(200)
    return {"request": request_ref, "response": response["receipt"], "proposals": proposals.receipt}


def _effects(runtime, prepared, host, held, finalized):
    from effects_journeys import CHECK_TOOL, OWNER, _attachment
    # This is the ordinary independently authored current module's own contract.
    runtime.witness = strict_json((TOOL / "witness.json").read_text())
    runtime.sign_in()
    seed_personal_layout(runtime.backend, runtime.output("effects-owner-seed") / "seed")
    held["refs"]["identityBefore"] = _identity(runtime, "held-identity")
    held["refs"]["status"] = _operator(runtime, prepared, "current", "status")
    held["observations"] = _observe_effects(runtime, "held")
    before, _, held["refs"]["ownerBefore"] = runtime.snapshot("held-owner-before", endpoint=OWNER)
    held["refs"]["entriesBefore"] = runtime.snapshot("held-entries-before", endpoint=OWNER + "/execution-entries")[2]
    held["refs"]["checksBefore"] = runtime.snapshot("held-checks-before", endpoint=OWNER + "/checks")[2]
    from restart_journeys import _effect_journal_snapshot
    paths = {"execution": str(owned_content_root(runtime.run, runtime.run.profiles["backend"]) / "observations/home-execution.jsonl"),
             "checks": str(owned_content_root(runtime.run, runtime.run.profiles["backend"]) / "observations/home-checks.jsonl")}
    held["homeJournalPaths"] = paths
    held["journalsBefore"] = _effect_journal_snapshot(runtime, paths, "held-journals-before")
    held["times"] = {"attempt": time.time()}
    # Discovery withholds classed tools while held. An observe command exercises
    # actual context admission/refusal without commanding an unadvertised tool.
    held["attempt"] = _observe_turn(runtime, "held-classed-context-refusal", _attachment(runtime, before))
    held["preparation"] = _preparation_probe(runtime, held["attempt"]["sessionId"], before["visible"], held=True)
    held["refs"]["ownerAfter"] = runtime.snapshot("held-owner-after", endpoint=OWNER)[2]
    held["refs"]["entriesAfter"] = runtime.snapshot("held-entries-after", endpoint=OWNER + "/execution-entries")[2]
    held["refs"]["checksAfter"] = runtime.snapshot("held-checks-after", endpoint=OWNER + "/checks")[2]
    held["journalsAfter"] = _effect_journal_snapshot(runtime, paths, "held-journals-after")
    held["refs"]["identityAfter"] = _identity(runtime, "held-identity-after")
    finalized["refs"]["identityBefore"] = _identity(runtime, "finalized-identity-before")
    finalized["refs"]["release"] = _operator(runtime, prepared, "current", "release")
    finalized["times"] = {"release": time.time()}
    process = copy.deepcopy(host.current.started)
    deadline = time.monotonic() + OWNED_SECONDS
    attempts = []
    while True:
        ref = _operator(runtime, prepared, "current", "status")
        attempts.append(ref)
        family = _read_operator(runtime.run.root, ref, generation="current", command="status", database=runtime.upgrade_store["database"])
        if family["finalizedVersion"] == "1.1.0": break
        _need(time.monotonic() < deadline, "same current process did not automatically finalize1.1 after hold release")
        time.sleep(0.5)
    finalized["refs"]["statusAttempts"] = attempts
    finalized["observations"] = _observe_effects(runtime, "finalized")
    finalized["sameProcess"] = process
    _need(host.current.started["pid"] == process["pid"], "adoption replaced the current process")
    finalized["refs"]["counterBefore"] = _store_snapshot(runtime, held["attempt"]["sessionId"], "finalized-counter-before")
    from effects_journeys import require_performed
    from restart_journeys import _effect_restart_snapshot
    finalized["preparationSnapshots"] = {"before": _effect_restart_snapshot(runtime, paths, "released-before-preparation")}
    finalized["preparation"] = _preparation_probe(runtime, held["attempt"]["sessionId"], before["visible"], held=False)
    finalized["preparationSnapshots"]["after"] = _effect_restart_snapshot(runtime, paths, "released-after-preparation")
    finalized["homeJournalPaths"] = paths
    finalized["journalsBefore"] = _effect_journal_snapshot(runtime, paths, "finalized-journals-before")
    owner_before, _, before_ref = runtime.snapshot("finalized-owner-before", endpoint=OWNER)
    layout_before, _, layout_ref = runtime.snapshot("finalized-layout-before")
    checks_before, _, checks_ref = runtime.snapshot("finalized-checks-before", endpoint=OWNER + "/checks")
    performed = runtime.turn("finalized-classed-prepare", tool=CHECK_TOOL, arguments={"expectedVisible": owner_before["visible"]},
                             attachment=_attachment(runtime, owner_before), capability="acme.home.effects")
    performed.message.require_status(200)
    _need(performed.proposal is not None, "finalized classed tool did not prepare")
    approved = runtime.approve(performed)
    execution = runtime.execute(performed).require_status(200)
    terminal = runtime.settled(performed)
    owner_after, _, after_ref = runtime.snapshot("finalized-owner-after", endpoint=OWNER)
    layout_after, _, after_layout_ref = runtime.snapshot("finalized-layout-after")
    checks_after, _, after_checks_ref = runtime.snapshot("finalized-checks-after", endpoint=OWNER + "/checks")
    require_performed(performed.proposal["preparedInvocation"], execution.json()["data"], owner_before, owner_after,
                      layout_before, layout_after, checks_before, checks_after)
    finalized["performed"] = {"sessionId": performed.session, "commandId": performed.command_id, "initialStream": performed.stream.receipt,
        "approved": approved.receipt, "execute": execution.receipt, "continuation": terminal.receipt,
        "ownerBefore": before_ref, "ownerAfter": after_ref, "layoutBefore": layout_ref, "layoutAfter": after_layout_ref,
        "checksBefore": checks_ref, "checksAfter": after_checks_ref}
    finalized["performed"]["trace"], finalized["performed"]["rawTrace"] = _command_trace(performed.transport, {"traceOffset": performed.offset},
        performed.command_id, "finalized-performed", owned_root=runtime.run.root)
    finalized["journalsAfter"] = _effect_journal_snapshot(runtime, paths, "finalized-journals-after")
    finalized["refs"]["counterAfter"] = _store_snapshot(runtime, held["attempt"]["sessionId"], "finalized-counter-after")
    finalized["refs"]["newRows"] = _store_snapshot(runtime, performed.session, "finalized-new-rows")
    finalized["refs"]["identityAfter"] = _identity(runtime, "finalized-identity-after")


def _lane(parent_runtime, prepared, provider):
    run = copy.copy(parent_runtime.run)
    run.profiles = {}
    # Each adapter gets fresh runtime state; immutable builds/archives are reused.
    selected = {name: {**item, "profile": copy.deepcopy(item["profile"])} for name, item in prepared.items()}
    namespace = "upgrade-native-" + provider
    for item in selected.values(): item["profile"]["processNamespace"] = namespace
    store = prepare_upgrade_content_root(run, selected, run.root / (namespace + "-content"),
                                         encryption_key=secrets.token_urlsafe(48), studio_host_id=HOST)
    store["database"] = str(Path(store["contentRoot"]) / "state/weaver-records.db")
    old_selection = select_upgrade_generation(run, selected, "old")
    admin = {"username": "upgrade-admin-" + secrets.token_hex(8), "password": secrets.token_urlsafe(36) + "!aA1"}
    origin, studio = owned_origin(), owned_origin()
    configure_runtime(run, backend_url=origin, studio_url=studio, admin_username=admin["username"], admin_password=admin["password"],
                      provider=provider, backend_only=True)
    witness = dict(parent_runtime.witness, moduleId=RESOURCE_MODULE, backendFeature=RESOURCE_FEATURE,
                   ownerEndpoint=RESOURCE_OWNER, contextType="acme.upgrade.widget", readTool=RESOURCE_TOOLS["read"], writeTool=RESOURCE_TOOLS["write"])
    # Distinct real actors make every frozen owner revision independently executable.
    principals, actor_map = [], {}
    for key, branches in BRANCHES.items():
        for branch in branches:
            actor_map[key, branch] = len(principals)
            principals.append({"subject": "upgrade-owner-" + uuid.uuid4().hex, "tenant": "default", "username": "upgrade-user-" + uuid.uuid4().hex,
                               "password": secrets.token_urlsafe(36) + "!aA1", "exactDirectPermissions": list(BASE_GRANTS)})
    config_path = Path(store["contentRoot"]) / "appsettings.json"
    settings = strict_json(config_path.read_text())
    settings["Qualification"]["Principals"] = principals
    private_json(config_path, settings)
    runtime = JourneyRuntime(run, origin, studio, admin, principals, witness=witness, provider=provider, output_namespace=namespace)
    runtime.upgrade_store = store
    shells_path = Path(store["contentRoot"]) / "shells.json"
    shells = strict_json(shells_path.read_text())
    shells["CShells"]["Shells"]["Authoring"]["Features"][PROTOCOL_FEATURES[provider][1]]["GateFile"] = str(runtime.gate_file)
    private_json(shells_path, shells)
    _stage_current_home(run, selected)
    # stage_native_archives wrote the future feed while stopped: restore old-only inputs.
    old_selection = select_upgrade_generation(run, selected, "old")
    lane = {"source": run.source, "provider": provider, "origin": origin, "store": store, "oldSelection": old_selection,
            "processNamespace": namespace, "ordinaryBackendOrigin": parent_runtime.backend_origin,
            "homeAuthor": run.report["authors"]["backend"], "generations": {name: selected[name]["record"] for name in selected}}
    lane_ref = runtime.save(runtime.output("lane"), "inputs", lane)
    records = {key: _new_record(run, provider, key, selected, store, lane_ref) for key in CASE_KEYS}
    states, host = {}, None
    try:
        with RestartableServer(run, "backend", origin) as host:
            runtime.sign_in()
            runtime.select_provider(provider)
            lane["oldObservations"] = {key: _native(runtime, "old-" + key, endpoint)[1] for key, endpoint in (
                ("catalog", "/modularity/features"), ("generation", "/__qualification/generation"), ("packages", "/__qualification/packages"))}
            for key, branches in BRANCHES.items():
                for branch_name in branches:
                    state = _prepare(runtime, actor_map[key, branch_name], key + "-" + branch_name,
                                     admin=key == "legacy-admin-disabled-upgrade", approved=branch_name.endswith("approved"))
                    states[key, branch_name] = state
                    records[key]["branches"][branch_name] = state[1]
            runtime.sign_in()
            client = _prepare_owner_resolution(runtime, "old-native-client-awaiting")
            records["client-awaiting-upgrade"]["client"] = client["o"]
            records["client-awaiting-upgrade"]["clientProposal"] = client["proposal"]
            records["client-awaiting-upgrade"]["refs"]["identityBefore"] = _identity(runtime, "client-old-identity")
            old_restart = _restart(runtime, host, "old-release-native-owner-leases")
            for row in records.values(): row["refs"]["oldReleaseRestart"] = old_restart
            rows_state = states["all-legacy-row-kinds", "pending"]
            _rows_before(runtime, rows_state, records["all-legacy-row-kinds"])
            runtime.sign_in()
            hold = _operator(runtime, selected, "old", "hold")
            for row in records.values(): row["refs"]["hold"] = hold
            restart = _restart(runtime, host, "old-to-current-held", lambda: _start_current(runtime, selected))
            for row in records.values(): row["refs"]["upgrade"] = restart
            lane["currentObservations"] = {key: _native(runtime, "current-" + key, endpoint)[1] for key, endpoint in (
                ("catalog", "/modularity/features"), ("generation", "/__qualification/generation"), ("packages", "/__qualification/packages"))}
            _rows_after(runtime, rows_state, records["all-legacy-row-kinds"])
            # New typed lifecycle results need1.1 too. First prove held legacy
            # identity reads and new-contract refusal; release/adopt before any
            # Admin/expiry continuation can retain a typed terminal result.
            _effects(runtime, selected, host, records["effects-held"], records["effects-finalized"])
            runtime.witness = witness
            for key in ("legacy-pending-upgrade", "legacy-approved-upgrade"):
                for name in BRANCHES[key]: _dispatch(runtime, *states[key, name], key)
            runtime.sign_in()
            from effects_journeys import _policy
            _policy(runtime, [False, True, True])
            admin_row = records["legacy-admin-disabled-upgrade"]
            for name in ("off-pending", "off-approved"): _dispatch(runtime, *states["legacy-admin-disabled-upgrade", name], name)
            _policy(runtime, [True, True, True])
            # Refused proposals stay terminal. These are different genuine old preparations.
            for name in ("off-pending", "off-approved"):
                turn, branch = states["legacy-admin-disabled-upgrade", name]
                _select_actor(runtime, branch["actor"])
                branch["refs"]["afterPolicyRestoration"] = runtime.execute(turn, transport=runtime.transport(name + "-terminal-after-enable")).receipt
                branch["snapshots"]["restored"] = _snapshot(runtime, name + "-restored-zero")
            for name in ("on-pending", "on-approved"): _dispatch(runtime, *states["legacy-admin-disabled-upgrade", name], name)
            # Original deadlines are never shortened or extended. Stop before the old
            # deadline, wait on the actual clock, then recover the same preparations.
            deadlines = [instant(states["expired-upgrade", name][1]["prepared"]["expiresAt"]) for name in ("pending", "approved")]
            deadlines.append(instant(client["proposal"]["preparedInvocation"]["expiresAt"]))
            _need(time.time() < min(deadlines), "setup missed an original expiry boundary")
            def expire_while_stopped():
                target = max(deadlines) + 1
                while time.time() < target: time.sleep(min(5, max(0.05, target - time.time())))
                return {"originalDeadlines": deadlines, "observedAfter": time.time()}
            expiry_restart = _restart(runtime, host, "current-original-deadline", expire_while_stopped)
            records["expired-upgrade"]["refs"]["expiryRestart"] = expiry_restart
            records["client-awaiting-upgrade"]["refs"]["expiryRestart"] = expiry_restart
            for name in ("pending", "approved"): _dispatch(runtime, *states["expired-upgrade", name], "expired-" + name)
            _recover_client(runtime, client, records["client-awaiting-upgrade"])
    except Exception as failure:
        # A transport failure may hide a write. Never replay the lane or start a new
        # identity to rescue it. Partial evidence remains under its original owner.
        for row in records.values():
            row["failure"] = {"cause": type(failure).__name__, "message": "Native upgrade lane stopped; retained partial evidence requires inspection",
                              "observation": getattr(failure, "observation", None)}
    finally:
        process_refs = []
        for path in sorted(run.root.glob(namespace + "-process*.json")):
            process_refs.append(retained(path))
        lane["processes"] = process_refs
        lane["processLogs"] = [retained(Path(_owned(run.root, ref)["log"])) for ref in process_refs]
        closeout = runtime.save(runtime.output("lane-closeout"), "closeout", lane)
        for row in records.values():
            row["backendOrigin"], row["refs"]["laneCloseout"] = origin, closeout
            try:
                candidate = dict(row, status="passed", nativeEvidenceVerified=True)
                candidate.pop("failure", None)
                require_legacy_effect_restart(candidate, run.root, source=run.source)
                row.update(status="passed", nativeEvidenceVerified=True)
                row.pop("failure", None)
            except Exception as error:
                row.update(status="failed", nativeEvidenceVerified=False, validationFailure={"cause": type(error).__name__, "message": str(error)[:1000]})
            ref = runtime.save(runtime.output("case-" + row["id"].split("/", 1)[1]), "record", row)
            row["record"] = ref
    return [records[key] for key in CASE_KEYS]


def _recover_client(runtime, state, row):
    """Native HTTP binding, never a claim that an actual browser/editor was used."""
    runtime.sign_in()
    o, proposal = state["o"], state["proposal"]
    t = runtime.transport("current-client-owner-resolution")
    response, observed, attempts = _await_owner(t, o["sessionId"], tolerate=True)
    o["refs"]["recoveredSession"] = response.require_status(200).receipt
    o["refs"]["ownershipAttempts"] = attempts
    o["recoveredAt"] = observed
    o["refs"]["recoveredProposals"] = t.request("GET", "/_elsa/weaver/sessions/" + o["sessionId"] + "/proposals").require_status(200).receipt
    o["refs"]["policy"] = t.bootstrap().require_status(200).receipt
    path = "/_elsa/weaver/sessions/" + o["sessionId"] + "/client-invocations/" + proposal["preparedInvocation"]["invocationId"] + "/resolution"
    for name, claim in (("resolution", "notApplied"), ("repeatResolution", "notApplied"), ("conflictingResolution", "applied")):
        o["refs"][name] = t.request("POST", path, {"claim": claim}).receipt
    o["refs"]["repeatExecute"] = t.execute(proposal["id"], revision=proposal["baseRevision"]).receipt
    o["refs"]["audit"] = t.audit(o["sessionId"]).require_status(200).receipt
    o["refs"]["session"] = t.session(o["sessionId"]).require_status(200).receipt
    _, read = runtime.native.json(runtime.backend, "client-workflow-owner-after", o["definitionPath"])
    o["refs"]["workflowOwnerAfterRead"] = read["receipt"]
    o["refs"]["trace"], o["refs"]["rawTrace"] = _command_trace(t, o, o["commandId"], "client-upgrade", owned_root=runtime.run.root)
    row["refs"]["identityAfter"] = _identity(runtime, "client-current-identity")
    row["browserObserved"] = False


def qualify_legacy_effect_restarts(parent_runtime):
    """Return exactly sixteen ordered records; does not admit the thirty-case aggregate.

    The caller invokes this only after ordinary backend/Studio contexts close.
    Preparation builds are owned by the existing package preparer; no second
    Studio/SDK build is introduced. Every invocation owns a never-reused namespace.
    """
    parent_runtime.run.assert_immutable_inputs()
    run = copy.copy(parent_runtime.run)
    run.profiles = dict(parent_runtime.run.profiles)
    original_report = copy.deepcopy(parent_runtime.run.report)
    original_profiles = copy.deepcopy(parent_runtime.run.profiles)
    original_packages = copy.deepcopy(parent_runtime.run.packages)
    original_modules = copy.deepcopy(parent_runtime.run.module_packages)
    def unchanged_ordinary_inputs():
        _need(all(same(value, parent_runtime.run.report.get(key)) for key, value in original_report.items())
              and parent_runtime.run.profiles == original_profiles and parent_runtime.run.packages == original_packages
              and parent_runtime.run.module_packages == original_modules, "legacy lane changed ordinary host/package/report evidence")
    started = run.root / "legacy-effect-restarts-started.json"
    _need(not any(os.path.lexists(run.root / name) for name in (started.name, "legacy-effect-restart-records.json")), "never resume/overwrite a legacy lane")
    private_json(started, {"source": run.source, "caseIDs": list(LEGACY_RESTART_CASES), "qualified": False})
    result = []
    def failed(provider, cause):
        output = run.root / ("upgrade-setup-failed-" + provider)
        output.mkdir(mode=0o700)
        rows = []
        for key in CASE_KEYS:
            row = {"id": provider + "/" + key, "provider": provider, "adapter": provider, "source": run.source, "status": "failed",
                   "nativeEvidenceVerified": False, "qualified": False, "independentPackageWitness": False,
                   "liveProvider": False, "evidenceCategory": EVIDENCE_CATEGORY, "bindings": upgrade_bindings(),
                   "failure": {"cause": type(cause).__name__, "message": "Native upgrade preparation stopped; no host/owner success inferred"}}
            row["partialEvidenceNamespace"] = str(run.root / ("upgrade-native-" + provider))
            path = output / (key + ".json")
            private_json(path, row)
            row["record"] = retained(path)
            rows.append(row)
        return rows
    try:
        prepared = prepare_upgrade_distributions(run)
    except Exception as failure:
        for provider in PROVIDERS: result.extend(failed(provider, failure))
    else:
        for provider in PROVIDERS:
            try: result.extend(_lane(parent_runtime, prepared, provider))
            except Exception as failure: result.extend(failed(provider, failure))
            unchanged_ordinary_inputs()
    unchanged_ordinary_inputs()
    private_json(run.root / "legacy-effect-restart-records.json", result)
    return result


def _read_lane(root, row, source):
    lane, closeout = _owned(root, row["lane"]), _owned(root, row["refs"]["laneCloseout"])
    _need(all(same(value, closeout.get(key)) for key, value in lane.items()), "lane changed its original immutable inputs")
    _need(lane.get("source") == source and lane.get("provider") == row["provider"] and lane.get("origin") == row["backendOrigin"]
          and lane.get("ordinaryBackendOrigin") != lane["origin"] and lane.get("processNamespace") == "upgrade-native-" + row["provider"]
          and same(row["store"], lane["store"]) and same(row["generations"], lane["generations"]), "ordinary/upgrade lane identity contaminated")
    store = row["store"]
    content = Path(store["contentRoot"])
    _need(content.is_absolute() and content.resolve().is_relative_to(Path(root).resolve()) and not content.is_symlink()
          and store["database"] == str(content / "state/weaver-records.db") and store["executionObservationFile"] == str(content / "observations/execution-entries.jsonl")
          and stat.S_IMODE(content.stat().st_mode) == 0o700,
          "foreign/private content root or database")
    from native_lifecycle import package_inventory, require_installed_package
    generations = row["generations"]
    _need(set(generations) == {"old", "current"}, "both real writer generations are required")
    for name, generation in generations.items():
        product = generation["productSource"]
        _need(generation.get("source") == source and generation.get("status") == "prepared" and generation.get("qualified") is False,
              "foreign/promoted package preparation")
        _need(product.get("commit") == (BASELINE_SOURCE if name == "old" else source)
              and (name != "old" or product.get("tree") == BASELINE_TREE), "not the exact post-rename old/current product source")
        _owned_file(root, product["archive"]); _owned_file(root, product["identityLog"])
        _need(Path(product["identityLog"]["path"]).read_text().splitlines() == [product["commit"], product["tree"]], "product Git identity bytes differ")
        report = _owned(root, generation["report"])
        _need(same(report.get("productSource"), product) and report.get("source") == source and report.get("independentPackageWitness") is False,
              "generation report lost product/driver separation")
        consumer = generation["consumer"]
        _owned_file(root, consumer["lock"]); _owned_file(root, consumer["frontendAudit"])
        _need(consumer.get("sourceReferences") == [] and consumer.get("frontendInvocations") == 0
              and not Path(consumer["frontendAudit"]["path"]).read_text().strip(), "native-only consumer used frontend/source shortcuts")
        _require_closure(root, generation["consumerClosure"])
        proof = generation["consumerBinaryProvenance"]
        _need(proof["assets"] == consumer["assets"] == generation["consumerClosure"]["assets"], "consumer/published closure graphs differ")
        _require_binary_provenance(root, proof)
        files = _owned(root, generation["publishedFiles"])
        publish = Path(proof["deps"]["path"]).parent
        _need(files == published_files(publish) and published_files_digest(files) == generation["publishedFilesSha256"], "immutable publish manifest differs")
        assets = _owned(root, proof["assets"])
        _need(assets.get("libraries") and all(value.get("type") == "package" for value in assets["libraries"].values()), "consumer used product source references")
        for author_name in ("protocolAuthor", "resourceAuthor"):
            author = generation[author_name]
            _need(author.get("productSource") == product and author.get("fixtureSource") == source, "old fixture/old product binding differs")
            _owned_file(root, author["packed"]["package"])
            _require_closure(root, author)
            for ref in author["sourceFiles"].values(): _owned_file(root, ref)
        observations = closeout[name + "Observations"]
        native = {key: _restart_native(root, ref, lane["origin"], endpoint) for key, ref, endpoint in (
            ("catalog", observations["catalog"], "/modularity/features"), ("generation", observations["generation"], "/__qualification/generation"),
            ("packages", observations["packages"], "/__qualification/packages"))}
        inventory = package_inventory(native["packages"])
        require_installed_package(inventory, generation["resourceAuthor"]["packed"]["package"])
        require_installed_package(inventory, generation["protocolAuthor"]["packed"]["package"])
        features = {item["id"]: item for item in native["catalog"]["features"]}
        _need(all(features.get(identity, {}).get("enabled") is True for identity in (DURABLE_STORE_FEATURE, RESOURCE_FEATURE))
              and features[RESOURCE_FEATURE].get("configuration", {}).get("ExecutionObservationFile") == store["executionObservationFile"]
              and features[RESOURCE_FEATURE]["configuration"].get("StudioHostId") == HOST, "real legacy owner/store/observer not composed")
        effects_selected = features.get(DURABLE_EFFECTS_FEATURE, {}).get("enabled") is True
        _need(effects_selected == (name == "current"),
              "old writer selected effects/current writer omitted its durable effects selection")
        tools = [item for item in native["generation"]["tools"] if item.get("moduleId") == RESOURCE_MODULE]
        _need({item.get("name") for item in tools} == set(RESOURCE_TOOLS.values()) and len(tools) == 3,
              "legacy native module was not actually registered")
        if name == "current": require_installed_package(inventory, lane["homeAuthor"]["packed"]["package"])
        else: _need("acme.home.weaver" not in inventory, "current classed author leaked into old writer")
    processes = [_owned(root, ref) for ref in closeout["processes"]]
    _need(len(processes) >= 3 and len({value.get("pid") for value in processes}) == len(processes), "missing actual isolated process generations")
    logs = closeout["processLogs"]
    _need(len(logs) == len(processes), "missing actual process logs")
    for process, log in zip(processes, logs):
        _owned_file(root, log)
        _need(log["path"] == process.get("log") and "WEAVER_BACKEND_CONSUMER_READY" in Path(log["path"]).read_text()
              and ("Now listening on: " + lane["origin"]) in Path(log["path"]).read_text(), "host did not actually serve its own origin")
        generation_name = "old" if process["publishedDirectory"] == str(Path(generations["old"]["consumerBinaryProvenance"]["deps"]["path"]).parent) else "current"
        generation = generations[generation_name]
        publish = Path(generation["consumerBinaryProvenance"]["deps"]["path"]).parent
        _need(process.get("publishedDirectory") == str(publish) and process.get("publishedFilesSha256") == generation["publishedFilesSha256"]
              and process.get("processNamespace") == lane["processNamespace"] and process.get("contentRoot") == str(content)
              and process.get("contentRootIdentity") == {"device": content.stat().st_dev, "inode": content.stat().st_ino}
              and process.get("origin") == lane["origin"] and process.get("exitCode") == 0
              and process.get("inputsUnchangedAtCloseout") is True and isinstance(process.get("runtimeCacheFiles"), dict)
              and not any(process["runtimeCacheFiles"].values()), "process binary/root/cache/closeout identity differs")
        argv = process["command"]
        _need(len(argv) == 6 and Path(argv[1]).parent == publish and argv[2:] == ["--contentRoot", str(content), "--urls", lane["origin"]]
              and sha(argv[1]) == process["assemblySha256"] and process["startedAt"] <= process["readyAt"] <= process["stoppedAt"], "process launch/times differ")
        for path in process["runtimePackageCaches"].values():
            directory = Path(path)
            _need(directory.resolve().is_relative_to(Path(root).resolve()) and not any(p.is_file() for p in directory.rglob("*")), "runtime cache populated")
    by_pid = {value["pid"]: value for value in processes}
    restart = _read_restart(root, row["refs"]["upgrade"], lane, by_pid)
    _need(restart["stoppedProcess"]["publishedDirectory"] == processes[0]["publishedDirectory"]
          and restart["startedProcess"]["publishedDirectory"] != restart["stoppedProcess"]["publishedDirectory"], "upgrade did not replace old package process")
    release_restart = _read_restart(root, row["refs"]["oldReleaseRestart"], lane, by_pid)
    _need(release_restart["stoppedProcess"]["publishedDirectory"] == release_restart["startedProcess"]["publishedDirectory"]
          == restart["stoppedProcess"]["publishedDirectory"] and release_restart["readyAt"] < restart["stoppedAt"], "owed-audit supplement had no genuine old lease-release restart")
    from artifacts import package_identity
    selection = restart["whileStopped"]
    _need(selection.get("generation") == "current" and selection.get("effectsSelected") is True
          and selection.get("contentRoot") == store["contentRoot"] and selection.get("productSource") == generations["current"]["productSource"]
          and selection.get("publishedDirectory") == restart["startedProcess"]["publishedDirectory"], "restart did not select the exact current archive/root inputs while stopped")
    archive_inputs = selection.get("runtimeArchiveInputs")
    _need(isinstance(archive_inputs, list) and archive_inputs, "actual native feed selection is absent")
    identities = set()
    for item in archive_inputs:
        _need(item.get("identity") not in identities, "native feed identity repeated")
        identities.add(item["identity"])
        for name in ("sourceArchive", "path"): _owned_file(root, {"path": item[name], "sha256": item["sha256"]})
        identity = package_identity(Path(item["sourceArchive"]))
        _need(item["identity"].casefold() == (identity["id"] + "/" + identity["version"]).casefold()
              and Path(item["path"]).parent == content / "runtime-feed" and item["path"] != item["sourceArchive"], "native feed path/package/retained archive differs")
    _need({path.name for path in (content / "runtime-feed").iterdir()} == {Path(item["path"]).name for item in archive_inputs}, "native feed contains undeclared archives")
    hold = _bound_operator(root, row, row["refs"]["hold"], "old", "hold")
    _need(hold.get("finalizedVersion") == "1.0.0" and any(item.get("version") is None and item.get("reason") == OPERATOR_REASON
          and item.get("placedBy") == OPERATOR_ACTOR for item in hold.get("holds", [])), "old1.0 family hold was never placed")
    operator = _owned(root, row["refs"]["hold"])
    _need(operator["endedAt"] <= restart["stoppedAt"], "hold was placed after the upgrade")
    return lane, closeout, restart, by_pid


def _read_restart(root, ref, lane, processes):
    value = _owned(root, ref)
    _need(value.get("name") == "backend" and value.get("kind") == "graceful" and value.get("origin") == lane["origin"]
          and type(value.get("fromGeneration")) is int and value.get("toGeneration") == value["fromGeneration"] + 1
          and value["stoppedAt"] <= value["startedAt"] <= value["readyAt"], "wrong actual upgrade/recovery restart")
    for key in ("stoppedProcess", "startedProcess"):
        summary = value[key]
        actual = processes.get(summary.get("pid"))
        _need(actual is not None and all(same(item, actual.get(field)) for field, item in summary.items()), "restart summary differs from actual retained process receipt")
    return value


def _bind_protocol(root, row, reference, command, *, old_call):
    """Join immutable protocol bytes to their actual old/current process lifetime."""
    trace = _effect_restart_trace(root, reference, command)
    capture = _owned(root, reference)
    expected = Path(row["store"]["contentRoot"]) / "protocol" / (row["provider"] + ".jsonl")
    _need(capture.get("sourcePath") == str(expected), "protocol source is outside this adapter's fresh content root")
    closeout = _owned(root, row["refs"]["laneCloseout"])
    processes = [_owned(root, ref) for ref in closeout["processes"]]
    old_publish = str(Path(row["generations"]["old"]["consumerBinaryProvenance"]["deps"]["path"]).parent)
    calls = []
    for event in trace:
        at = instant(event.get("at"))
        matching = [process for process in processes if process["startedAt"] <= at <= process["stoppedAt"]]
        _need(event.get("evidenceKind") == EVIDENCE_CATEGORY and len(matching) == 1, "protocol event did not occur in one actual owned process lifetime")
        if event.get("kind") == row["provider"] + ".call":
            _need((matching[0]["publishedDirectory"] == old_publish) == old_call, "actual native call used the wrong old/current product generation")
            calls.append(event)
    _need(not old_call or len(calls) == 1, "old preparation did not have exactly one actual native adapter call")
    return trace


def _read_initial(root, row, branch, *, admin=False):
    refs, origin = branch["refs"], row["backendOrigin"]
    frames = native_frames(_owned(root, refs["initialStream"]), origin, branch["sessionId"])
    proposals = [frame.get("payload") for frame in frames if frame.get("kind") == "proposalCreated"]
    _need(len(proposals) == 1 and isinstance(proposals[0], dict), "missing unique actual old native preparation")
    proposal, prepared = proposals[0], proposals[0]["preparedInvocation"]
    _need(proposal.get("id") == branch["proposalId"] and proposal.get("sessionId") == branch["sessionId"]
          and proposal.get("status") == "awaitingApproval" and same(prepared, branch["prepared"]), "old pending preparation identity differs")
    starts = [frame.get("payload", {}) for frame in frames if frame.get("kind") == "started"]
    calls = [frame.get("payload", {}) for frame in frames if frame.get("kind") == "toolCallStarted"]
    _need(len(starts) == len(calls) == 1 and starts[0].get("turnId") == prepared.get("turnId")
          and calls[0].get("toolCallId") == prepared.get("toolCallId") and calls[0].get("toolName") == prepared.get("toolName")
          and [frame.get("kind") for frame in frames if frame.get("kind") in ("completed", "error", "turnCancelled", "toolApprovalRequested")] == ["toolApprovalRequested"],
          "old proposal did not pause its originating native turn/call normally")
    owner = _restart_identity(root, refs["identityBefore"], origin)
    before = _read_snapshot(root, branch["snapshots"]["before"], origin, owner, row["store"]["executionObservationFile"])
    _need(prepared.get("requiredPrivilegeClasses") in (None, []) and prepared.get("moduleId") == RESOURCE_MODULE
          and prepared.get("toolName") == RESOURCE_TOOLS["admin" if admin else "write"] and prepared.get("toolContractVersion") == "1"
          and prepared.get("executionLocation") == "server" and prepared.get("client") is None and prepared.get("requiresApproval") is True
          and prepared.get("risk") == ("admin" if admin else "reviewRequired") and prepared.get("actorId") == owner["subject"]
          and prepared.get("tenantId") == owner["tenantId"] and prepared.get("sessionId") == branch["sessionId"]
          and all(isinstance(prepared.get(key), str) and prepared[key] for key in ("invocationId", "toolCallId", "turnId", "approvalDigest", "expiresAt")),
          "fabricated/classed/foreign legacy preparation")
    target = {"moduleId": RESOURCE_MODULE, "resourceType": "home-widget", "resourceId": "acme.home:status", "revision": before["owner"]["revision"]}
    arguments = {"visible": not before["owner"]["visible"]}
    _need(prepared.get("target") == target and prepared.get("arguments") == arguments and proposal.get("baseRevision") == target["revision"], "legacy frozen owner target/arguments differ")
    _need(prepared.get("preview") == {"typeId": "acme.upgrade.widget-result", "schemaVersion": "1", "data": {
        "widgetId": "acme.home:status", "revision": target["revision"], "beforeVisible": before["owner"]["visible"],
        "visible": arguments["visible"], "hostId": HOST}} and prepared.get("disclosureSourceId") == "acme.upgrade.widget-context",
        "old reviewed preview/disclosure owner differs from the real native resource")
    _need(_owned(root, refs["command"]) == {"commandId": branch["commandId"], "toolName": prepared["toolName"], "arguments": arguments}, "old adapter command differs")
    decoded = _restart_native(root, refs["oldInspector"], origin, INSPECTOR + branch["sessionId"])
    _need(decoded.get("actorId") == owner["subject"] and decoded.get("tenantId") == owner["tenantId"]
          and decoded.get("sessionId") == branch["sessionId"] and decoded.get("legacyDefaults", {}).get("preparationsHaveNoClasses") is True
          and len(decoded.get("proposals", [])) == 1 and decoded["proposals"][0].get("id") == proposal["id"]
          and str(decoded["proposals"][0].get("status", "")).lower() == ("approved" if branch["approved"] else "awaitingapproval")
          and decoded["proposals"][0].get("approvalDigest") == prepared["approvalDigest"]
          and instant(decoded["proposals"][0].get("expiresAt")) == instant(prepared["expiresAt"]), "real old decoder did not observe its actual preparation")
    if branch["approved"]:
        _, decision = _restart_http(root, refs["approvedBefore"], origin, "POST", "/_elsa/weaver/proposals/" + proposal["id"] + "/approve")
        _need(decision.get("data", {}).get("status") == "approved" and same(decision["data"].get("preparedInvocation"), prepared), "no genuine old approval before dispatch")
    return frames, proposal, prepared, owner, before


def _read_branch(root, row, branch, restart, *, admin=False, refusal=False, expired=False):
    initial, proposal, prepared, owner, before = _read_initial(root, row, branch, admin=admin)
    refs, origin, snapshots = branch["refs"], row["backendOrigin"], branch["snapshots"]
    _need(same(owner, _restart_identity(root, refs["identityRecovered"], origin))
          and same(owner, _restart_identity(root, refs["identityAfter"], origin)), "recovery changed actual native actor/tenant")
    attempts = refs["ownershipAttempts"]
    _need(isinstance(attempts, list) and attempts and same(attempts[-1], refs["recoveredSession"]), "missing bounded real ownership observation")
    for ref in attempts[:-1]:
        _, value = _restart_http(root, ref, origin, "GET", "/_elsa/weaver/sessions/" + branch["sessionId"], 409)
        _need(value.get("error", {}).get("code") == "weaver.session.owned_elsewhere", "ownership wait hid another refusal")
    recovered = _restart_proposal(root, refs["recoveredProposals"], origin, branch["sessionId"], proposal["id"], prepared)
    _need(recovered.get("status") == ("expired" if expired else "approved" if branch["approved"] else "awaitingApproval"), "recovery autodispatched/rewrote an old operation")
    decoded = {name: _read_snapshot(root, value, origin, owner, row["store"]["executionObservationFile"]) for name, value in snapshots.items()}
    _need(not before["journal"] and same(decoded["recovered"], before), "upgrade dispatched before explicit execution")
    times = branch["times"]
    _need(all(type(times.get(name)) in (int, float) and math.isfinite(times[name]) for name in ("prepared", "recovered", "execute", "disclosureStopped", "repeated"))
          and times["prepared"] < restart["stoppedAt"] <= restart["startedAt"] <= times["recovered"] <= times["execute"] <= times["disclosureStopped"] <= times["repeated"], "unbound native execution/recovery times")
    _need(_owned(root, refs["trace"])["capturedAt"] >= times["disclosureStopped"], "protocol window predates observed disclosure boundary")
    previous_journal = _owned(root, snapshots["before"]["journal"])
    windows = {"before": (float("-inf"), times["prepared"]), "recovered": (times["recovered"], times["execute"]),
               "after": (times["execute"], times["repeated"]), "repeated": (times["repeated"], float("inf"))}
    for name, (lower, upper) in windows.items():
        observed = _owned(root, snapshots[name]["journal"])
        _need(lower <= observed["observedAt"] <= upper and all(observed[field] == previous_journal[field] for field in ("sourcePath", "device", "inode")),
              "journal was replaced or does not bracket preparation/recovery/explicit dispatch")
    _need(times["execute"] > instant(prepared["expiresAt"]) if expired else times["repeated"] < instant(prepared["expiresAt"]), "wrong original deadline boundary")
    _, policy = _restart_http(root, refs["policy"], origin, "GET", "/_elsa/weaver/bootstrap")
    _need(policy.get("data", {}).get("privileges") == dict(zip(CLASSES, [not refusal, True, True]))
          and policy["data"].get("providerStatus") == "available" and policy["data"].get("recovery") == "durable", "wrong current privilege/provider/store policy")
    code = "weaver.proposal.expired" if expired else POLICY_CODE
    status = 409 if expired else 403
    if not branch["approved"]:
        _, decision = _restart_http(root, refs["decision"], origin, "POST", "/_elsa/weaver/proposals/" + proposal["id"] + "/approve", status if refusal or expired else 200)
        _need(decision.get("error", {}).get("code") == code if refusal or expired else decision.get("data", {}).get("status") == "approved", "wrong approval outcome")
    _, execution = _restart_http(root, refs["execute"], origin, "POST", "/_elsa/weaver/proposals/" + proposal["id"] + "/execute", status if refusal or expired else 200)
    trace = _bind_protocol(root, row, refs["trace"], branch["commandId"], old_call=True)
    _need(same(refs["rawTrace"], _owned(root, refs["trace"])["rawTrace"]), "raw trace join differs")
    calls = [item["data"] for item in trace if item.get("kind") == row["provider"] + ".call"]
    _need(len(calls) == 1 and calls[0].get("toolName") == prepared["toolName"] and same(calls[0].get("arguments"), prepared["arguments"]), "old preparation did not use exact actual adapter call")
    terminal = native_frames(_owned(root, refs["continuation"]), origin, branch["sessionId"])
    _, session = _restart_http(root, refs["session"], origin, "GET", "/_elsa/weaver/sessions/" + branch["sessionId"])
    closed = _restart_proposal(root, refs["terminalProposals"], origin, branch["sessionId"], proposal["id"], prepared)
    _, repeated = _restart_http(root, refs["repeatExecute"], origin, "POST", "/_elsa/weaver/proposals/" + proposal["id"] + "/execute", status if refusal or expired else 200)
    if refusal or expired:
        _need(execution.get("error", {}).get("code") == repeated.get("error", {}).get("code") == code
              and closed.get("status") == ("expired" if expired else "failed") and same(decoded["after"], before)
              and same(decoded["repeated"], before), "refusal expired/failed incorrectly or entered native owner")
        require_called_refusal(trace, row["provider"], branch["commandId"], prepared["toolName"], prepared["arguments"], branch["sessionId"],
                               initial, terminal, code, session.get("data"), proposal_id=proposal["id"])
        if refusal:
            _, restored = _restart_http(root, refs["afterPolicyRestoration"], origin, "POST", "/_elsa/weaver/proposals/" + proposal["id"] + "/execute", 403)
            _need(restored.get("error", {}).get("code") == POLICY_CODE and same(decoded["restored"], before), "reenabling Admin resurrected a terminal failed proposal")
    else:
        data, after = execution.get("data", {}), decoded["after"]
        result, receipt = data.get("toolResult", {}), data.get("toolResult", {}).get("receipt", {})
        _need(data.get("executed") is True and data.get("status") == "executed" and closed.get("status") == "executed"
              and result.get("succeeded") is True and result.get("toolName") == prepared["toolName"] and result.get("toolCallId") == prepared["toolCallId"]
              and receipt.get("invocationId") == prepared["invocationId"] and receipt.get("scope") == "serverPersisted"
              and receipt.get("before") == prepared["target"] and receipt.get("after", {}).get("revision") == after["owner"]["revision"]
              and after["owner"]["revision"] != before["owner"]["revision"] and after["owner"]["visible"] == prepared["arguments"]["visible"], "no exact once persisted legacy native effect")
        expected_layout = copy.deepcopy(before["layout"])
        expected_layout["revision"] = after["layout"]["revision"]
        if "updatedAt" in expected_layout:
            _need(instant(after["layout"]["updatedAt"]) >= instant(before["layout"]["updatedAt"]), "native owner update time moved backwards")
            expected_layout["updatedAt"] = after["layout"]["updatedAt"]
        next(item for item in expected_layout["value"]["widgets"] if item["id"] == "acme.home:status")["visible"] = prepared["arguments"]["visible"]
        _need(same(after["layout"], expected_layout) and len(after["journal"]) == 1
              and all(after["journal"][0][name] == prepared[field] for name, field in (("InvocationId", "invocationId"), ("ToolCallId", "toolCallId"),
                    ("SessionId", "sessionId"), ("TurnId", "turnId"))) and same(decoded["repeated"], after), "native owner ran multiple times or changed unreviewed layout data")
        require_normal_terminal(terminal)
        require_adapter_result(trace, row["provider"], branch["commandId"], result)
        _need(same(repeated.get("data"), data), "explicit repeat lost original retained execution")
    _, audit = _restart_http(root, refs["audit"], origin, "GET", "/_elsa/weaver/audit?take=100&sessionId=" + branch["sessionId"])
    _need(isinstance(audit.get("data"), list) and audit["data"] and all(value.get("sessionId") == branch["sessionId"] for value in audit["data"]), "missing actual authorized audit decoding")
    if expired:
        _need(any(value.get("metadata", {}).get("proposalId") == proposal["id"] and value.get("metadata", {}).get("dispatch") == "none"
                  and value.get("metadata", {}).get("effect") == "none" for value in audit["data"]), "expiry lacks native no-dispatch audit")
    return prepared, owner


def _read_rows(root, row):
    branch = row["branches"]["pending"]
    _, _, _, owner, before = _read_initial(root, row, branch)
    refs, origin = row["refs"], row["backendOrigin"]
    decoded = {name: _restart_native(root, refs["decode" + name], origin, INSPECTOR + branch["sessionId"]) for name in ("Before", "After")}
    stored_before, stored_after = _owned(root, refs["storeBefore"]), _owned(root, refs["storeAfter"])
    require_identity_rows(stored_before, stored_after, decoded["Before"], decoded["After"],
                          actor_id=owner["subject"], tenant_id=owner["tenantId"])
    database = Path(row["store"]["database"])
    _need(stored_before["database"] == {"path": str(database), "device": database.stat().st_dev, "inode": database.stat().st_ino}, "physical database was replaced after the native snapshots")
    _need(same(owner, _restart_identity(root, branch["refs"]["identityRecovered"], origin))
          and same(owner, _restart_identity(root, branch["refs"]["identityAfter"], origin)), "all-row owner changed")
    _need(same(before, _read_snapshot(root, branch["snapshots"]["after"], origin, owner, row["store"]["executionObservationFile"])), "inert all-row read entered owner")
    supplement = row["supplement"]
    _need(supplement.get("kind") == "PendingAuditStoreWrite" and len(supplement["eventId"]) == 32
          and supplement["response"] == {"eventId": supplement["eventId"], "recorded": True, "repeated": False,
              "evidenceKind": "fixture-only-real-store-owed-audit-supplement"}, "owed-audit supplement was fabricated or not distinctly labelled")
    native = _owned(root, refs["owedAuditSupplement"])
    _need(native.get("method") == "POST" and native.get("origin") == origin and native.get("path") == INSPECTOR + branch["sessionId"] + "/owed-audit"
          and native.get("status") == 200 and same(_owned(root, native["body"]), supplement["response"]), "no actual old-writer supplemental write")
    audits = []
    for label in ("Before", "After"):
        _, listing = _restart_http(root, refs["list" + label], origin, "GET", "/_elsa/weaver/sessions")
        _need(any(item.get("id") == branch["sessionId"] for item in listing.get("data", {}).get("items", [])), "native owning session list did not decode the old record")
        _, audit = _restart_http(root, refs["audit" + label], origin, "GET", "/_elsa/weaver/audit?take=100")
        _need(isinstance(audit.get("data"), list) and audit["data"] and all(item.get("sessionId") == branch["sessionId"]
              and isinstance(item.get("id"), str) for item in audit["data"]), "native audit reader did not decode owned audit content")
        audits.append(audit["data"])
    _need(same(*audits), "identity upcast changed actual native audit-reader content")
    trace = _bind_protocol(root, row, branch["refs"]["trace"], branch["commandId"], old_call=True)
    _need(not any(item.get("kind") == row["provider"] + ".continuation" for item in trace), "inert all-row read resumed a paused operation")


def _read_client(root, row, lane, processes):
    o, proposal, origin = row["client"], row["clientProposal"], row["backendOrigin"]
    refs, session, prepared = o["refs"], o["sessionId"], proposal["preparedInvocation"]
    owner = _restart_identity(root, row["refs"]["identityBefore"], origin)
    _need(same(owner, _restart_identity(root, row["refs"]["identityAfter"], origin)) and row.get("browserObserved") is False,
          "client HTTP fixture changed owner or falsely claimed a browser")
    initial = native_frames(_owned(root, refs["initialStream"]), origin, session)
    proposals = [frame.get("payload") for frame in initial if frame.get("kind") == "proposalCreated"]
    _need(proposals == [proposal] and prepared.get("requiredPrivilegeClasses") in (None, [])
          and prepared.get("executionLocation") == "clientWorkingState" and same(prepared.get("client"), o["binding"])
          and prepared.get("actorId") == owner["subject"] and prepared.get("tenantId") == owner["tenantId"], "not a genuine old native waiting client invocation")
    _, dispatch = _restart_http(root, refs["execute"], origin, "POST", "/_elsa/weaver/proposals/" + proposal["id"] + "/execute", 202)
    _, approval = _restart_http(root, refs["approve"], origin, "POST", "/_elsa/weaver/proposals/" + proposal["id"] + "/approve")
    _need(approval.get("data", {}).get("status") == "approved" and same(approval["data"].get("preparedInvocation"), prepared), "old waiting client had no genuine native approval")
    _need(dispatch.get("data", {}).get("clientInvocation", {}).get("invocationId") == prepared["invocationId"]
          and dispatch["data"].get("executed") is False, "old native client was not awaiting its owner")
    expiry = _read_restart(root, row["refs"]["expiryRestart"], lane, processes)
    _need(expiry["stoppedAt"] < instant(prepared["expiresAt"]) < expiry["startedAt"] <= o["recoveredAt"], "client recovery missed original deadline")
    recovered = _restart_proposal(root, refs["recoveredProposals"], origin, session, proposal["id"], prepared)
    _need(recovered.get("status") == "uncertain", "client recovery redelivered unknown work or claimed not applied")
    _, policy = _restart_http(root, refs["policy"], origin, "GET", "/_elsa/weaver/bootstrap")
    _need(policy.get("data", {}).get("privileges") == dict.fromkeys(CLASSES, True) and policy["data"].get("providerStatus") == "available"
          and policy["data"].get("recovery") == "durable", "client recovery bypassed current native policy/adapter/store observation")
    path = "/_elsa/weaver/sessions/" + session + "/client-invocations/" + prepared["invocationId"] + "/resolution"
    _, resolved = _restart_http(root, refs["resolution"], origin, "POST", path)
    _, repeated = _restart_http(root, refs["repeatResolution"], origin, "POST", path)
    _, conflict = _restart_http(root, refs["conflictingResolution"], origin, "POST", path, 409)
    statement = resolved.get("data", {}).get("ownerResolution")
    _need(isinstance(statement, dict) and statement.get("claim") == "notApplied" and same(statement, repeated.get("data", {}).get("ownerResolution"))
          and conflict.get("error") is not None, "owner uncertainty resolution was not idempotent/conflict checked")
    _, retry = _restart_http(root, refs["repeatExecute"], origin, "POST", "/_elsa/weaver/proposals/" + proposal["id"] + "/execute")
    _need(retry.get("data", {}).get("executed") is False and retry["data"].get("clientInvocation") is None
          and retry["data"].get("status") == "uncertain", "unknown client operation replayed after resolution")
    before = _restart_native(root, refs["workflowOwnerRead"], origin, o["definitionPath"])
    after = _restart_native(root, refs["workflowOwnerAfterRead"], origin, o["definitionPath"])
    _need(same(before, after), "native workflow owner changed during unknown-client recovery")
    trace = _bind_protocol(root, row, refs["trace"], o["commandId"], old_call=True)
    _need(sum(item.get("kind") == row["provider"] + ".call" for item in trace) == 1
          and not any(item.get("kind") == row["provider"] + ".continuation" for item in trace), "client unknown outcome called/continued provider again")
    _, audit = _restart_http(root, refs["audit"], origin, "GET", "/_elsa/weaver/audit?take=100&sessionId=" + session)
    _need(any(item.get("kind") == "proposalResolved" and item.get("metadata", {}).get("claim") == "notApplied" for item in audit.get("data", [])),
          "actual client-resolution audit not decoded")


def _read_availability(root, row, *, held):
    refs, origin = row["observations"], row["backendOrigin"]
    catalog = _restart_native(root, refs["catalog"], origin, "/modularity/features")
    features = {item["id"]: item for item in catalog["features"]}
    feature = features[DURABLE_EFFECTS_FEATURE]
    expected = "dormant" if held else "available"
    _need(feature.get("enabled") is True and feature.get("runtimeAvailable") is True and feature.get("availability", {}).get("status") == expected,
          "native feature catalog disagrees with shared effects admission")
    if held:
        _need(any(item.get("family") == "Weaver" and item.get("version") == "1.1.0" for item in feature["availability"].get("reasons", [])), "dormancy reason names wrong shared schema requirement")
    for name in ("Weaver", "WeaverApi", DURABLE_STORE_FEATURE, RESOURCE_FEATURE):
        _need(features[name].get("enabled") is True and features[name].get("availability", {}).get("status") == "available", "legacy feature was made dormant with effects")
    attention = _restart_native(root, refs["attention"], origin, ATTENTION)
    contributors = attention.get("contributors")
    modularity = [item for item in (contributors or []) if item.get("contributorId") == "modularity"]
    _need(isinstance(contributors, list) and len(modularity) == 1 and modularity[0].get("status") == "ready" and modularity[0].get("isTruncated") is False,
          "Attention is unavailable/partial, cannot establish dormancy absence")
    items = [item for contributor in contributors for item in contributor.get("items", [])]
    matches = [item for item in items if item.get("id") == "feature-dormant:" + DURABLE_EFFECTS_FEATURE]
    _need(len(matches) == (1 if held else 0), "Attention does not report the same effects dormancy")
    capabilities = _restart_native(root, refs["capabilities"], origin, "/capabilities")
    selected = [item for item in capabilities.get("capabilities", []) if item.get("id") == "elsa.api.weaver.effects"]
    legacy = [item for item in capabilities.get("capabilities", []) if item.get("id") == "elsa.api.weaver"]
    _need(len(selected) == len(legacy) == 1 and legacy[0].get("status") is None
          and selected[0].get("contractVersion") == "1" and selected[0].get("status") == ("dormant" if held else None)
          and (bool(selected[0].get("reason")) if held else selected[0].get("reason") is None), "actual effects/legacy capabilities disagree with same admission")
    return _restart_native(root, refs["generation"], origin, "/__qualification/generation")


def _read_home_journals(root, row, owner):
    from effects_journeys import require_execution_observation
    paths = row["homeJournalPaths"]
    result = {}
    for name in ("Before", "After"):
        result[name] = {kind: require_execution_observation(_owned(root, reference), path=paths[kind], owner=owner, execution=kind == "execution")
                        for kind, reference in row["journals" + name].items()}
    return result


def _read_preparation_probe(root, row, owner, *, held):
    from effects_journeys import CHECK_TOOL
    probe, origin = row["preparation"], row["backendOrigin"]
    request = _owned(root, probe["request"])
    _need(set(request) == {"sessionId", "expectedVisible"} and isinstance(request["sessionId"], str)
          and request["sessionId"] and type(request["expectedVisible"]) is bool, "wrong owning preparation request")
    wire = _owned(root, probe["response"])
    _need(wire.get("origin") == origin and wire.get("method") == "POST" and wire.get("path") == PREPARATION_ENDPOINT
          and wire.get("status") == (409 if held else 200) and not wire.get("transportFailure"), "missing actual public-invoker preparation response")
    value = _owned(root, wire["body"])
    _need(value.get("evidenceKind") == "native-module-public-invoker-preparation" and value.get("sessionId") == request["sessionId"]
          and value.get("toolName") == CHECK_TOOL and value.get("result") is None
          and all(isinstance(value.get(key), str) and value[key] for key in ("turnId", "toolCallId")), "direct fixture call was not a native owned preparation-only operation")
    _, proposals = _restart_http(root, probe["proposals"], origin, "GET", "/_elsa/weaver/sessions/" + request["sessionId"] + "/proposals")
    rows = proposals.get("data")
    _need(proposals.get("error") is None and isinstance(rows, list), "owning proposal list did not confirm preparation boundary")
    if held:
        error = value.get("error") or {}
        _need(value.get("outcome") == "Denied" and value.get("proposal") is None and error.get("code") == "schema-version-not-finalized"
              and error.get("statusCode") == 409 and error.get("ownerUnavailable") is False and rows == [],
              "held actual preparation was not refused before proposal/review with its exact schema cause")
    else:
        _need(value.get("outcome") == "ProposalCreated" and value.get("error") is None and isinstance(value.get("proposal"), dict)
              and value["proposal"].get("status") == "AwaitingApproval" and value["proposal"].get("requiresApproval") is True
              and len(rows) == 1 and rows[0].get("id") == value["proposal"].get("id") and rows[0].get("status") == "awaitingApproval",
              "released public preparation was not available/unapproved at the native boundary")
        prepared = rows[0].get("preparedInvocation") or {}
        _need(prepared.get("sessionId") == request["sessionId"] and prepared.get("toolName") == CHECK_TOOL
              and prepared.get("actorId") == owner["subject"] and prepared.get("tenantId") == owner["tenantId"]
              and prepared.get("turnId") == value["turnId"] and prepared.get("toolCallId") == value["toolCallId"]
              and prepared.get("requiredPrivilegeClasses") == ["administrative"] and prepared.get("requiresApproval") is True
              and prepared.get("arguments") == {"expectedVisible": request["expectedVisible"]}, "released real proposal changed its current native owner/correlation/classes")
    return value


def _read_effects(root, row, lane, processes, *, held):
    from effects_journeys import CHECK_TOOL, OWNER, TOOLS, require_performed
    generation = _read_availability(root, row, held=held)
    origin, refs = row["backendOrigin"], row["refs"]
    owner = _restart_identity(root, refs["identityBefore"], origin)
    _need(same(owner, _restart_identity(root, refs["identityAfter"], origin)), "held/finalized native owner changed")
    journals = _read_home_journals(root, row, owner)
    _read_preparation_probe(root, row, owner, held=held)
    if held:
        family = _bound_operator(root, row, refs["status"], "current", "status")
        _need(family.get("finalizedVersion") == "1.0.0" and family.get("holds"), "effects were not actually held at1.0")
        for key, endpoint in (("owner", OWNER), ("entries", OWNER + "/execution-entries"), ("checks", OWNER + "/checks")):
            before = _restart_native(root, refs[key + "Before"], origin, endpoint)
            after = _restart_native(root, refs[key + "After"], origin, endpoint)
            _need(same(before, after), "held classed admission entered/changed native owner")
            if key == "entries": _need(before.get("entryCount") == 0, "held journal did not start at observed zero")
            if key == "checks": _need(before.get("ranCount") == 0, "held business check ran")
        _need(journals == {"Before": {"execution": [], "checks": []}, "After": {"execution": [], "checks": []}}, "held raw module journals contain an entry")
        attempt = row["attempt"]
        _need(_owned(root, attempt["command"]) == {"commandId": attempt["commandId"], "mode": "observe"}, "held attempt commanded a tool despite absent discovery")
        frames = native_frames(_owned(root, attempt["stream"]), origin, attempt["sessionId"])
        require_normal_terminal(frames)
        _need(not any(item.get("kind") in ("proposalCreated", "toolCallStarted", "toolCallCompleted") for item in frames), "held tool reached preparation/dispatch")
        admission = [item.get("payload", {}) for item in frames if item.get("kind") == "contextAdmitted"]
        message_wire, message = _restart_http(root, attempt["message"], origin, "POST", "/_elsa/weaver/sessions/" + attempt["sessionId"] + "/messages")
        # Collection happens at message acceptance. Its optional refusal is a
        # warning there; refused content is absent from the recorded user prompt,
        # so the later stream need not repeat that collection diagnostic.
        _need(any(item.get("code") == "schema-version-not-finalized" and item.get("target") == "acme.home.effects-context"
                  for item in message.get("data", {}).get("warnings", []))
              and any(item.get("moduleId") == "Acme.Home" and item.get("contentType") == "acme.home.effects"
                  for item in (message_wire.get("requestBody") or {}).get("contextAttachments", []))
              and admission and not any(item.get("contentType") in ("acme.home.effects", "acme.home.effects-guidance")
                  for value in admission for item in value.get("attachments", [])), "held context lacked exact native message refusal or reached provider")
        trace = _bind_protocol(root, row, attempt["trace"], attempt["commandId"], old_call=False)
        selected = [item for item in trace if item.get("kind", "").startswith(row["provider"] + ".")]
        _need([item["kind"] for item in selected] == [row["provider"] + ".request", row["provider"] + ".observation"]
              and not any(item.get("name") in TOOLS for item in selected[0]["data"].get("tools", [])), "held classed tools reached actual SDK discovery")
    else:
        release = _bound_operator(root, row, refs["release"], "current", "release")
        _need(not release.get("holds"), "operator did not release the family hold")
        attempts = refs["statusAttempts"]
        _need(attempts and _bound_operator(root, row, attempts[-1], "current", "status").get("finalizedVersion") == "1.1.0",
              "same current process did not automatically adopt1.1")
        process = processes.get(row["sameProcess"].get("pid"))
        _need(process and all(same(value, process.get(key)) for key, value in row["sameProcess"].items())
              and process["startedAt"] < _owned(root, refs["release"])["startedAt"] < process["stoppedAt"], "adoption came from another process")
        for ref in attempts:
            value = _owned(root, ref)
            _need(process["startedAt"] <= value["startedAt"] <= value["endedAt"] <= process["stoppedAt"], "adoption operator status escaped current process lifetime")
        from effects_journeys import _restart_snapshot
        preparation_snapshots = {name: _restart_snapshot(root, value, origin, owner, row["homeJournalPaths"])
                                 for name, value in row["preparationSnapshots"].items()}
        _need(set(preparation_snapshots) == {"before", "after"} and same(preparation_snapshots["before"], preparation_snapshots["after"])
              and preparation_snapshots["before"]["entries"].get("entryCount") == 0
              and preparation_snapshots["before"]["checks"].get("ranCount") == 0, "released preparation executed or changed its native owner before approval")
        performed = row["performed"]
        initial = native_frames(_owned(root, performed["initialStream"]), origin, performed["sessionId"])
        proposals = [item["payload"] for item in initial if item.get("kind") == "proposalCreated"]
        _need(len(proposals) == 1 and proposals[0]["preparedInvocation"].get("requiredPrivilegeClasses") == ["administrative"], "finalized preparation did not freeze actual new classes")
        prepared = proposals[0]["preparedInvocation"]
        _, execution = _restart_http(root, performed["execute"], origin, "POST", "/_elsa/weaver/proposals/" + proposals[0]["id"] + "/execute")
        values = {name: _restart_native(root, performed[name], origin, endpoint) for name, endpoint in (
            ("ownerBefore", OWNER), ("ownerAfter", OWNER), ("layoutBefore", HOME_PREFERENCES_PATH), ("layoutAfter", HOME_PREFERENCES_PATH),
            ("checksBefore", OWNER + "/checks"), ("checksAfter", OWNER + "/checks"))}
        require_performed(prepared, execution["data"], values["ownerBefore"], values["ownerAfter"], values["layoutBefore"], values["layoutAfter"],
                          values["checksBefore"], values["checksAfter"])
        _need(len(journals["After"]["execution"]) == len(journals["Before"]["execution"]) + 1
              and len(journals["After"]["checks"]) == len(journals["Before"]["checks"]) + 1
              and journals["After"]["execution"][-1]["InvocationId"] == prepared["invocationId"]
              and journals["After"]["checks"][-1]["InvocationId"] == prepared["invocationId"], "new classed effect did not enter exact native owner once")
        require_retained_counter(_owned(root, refs["counterBefore"]), _owned(root, refs["counterAfter"]), advances=True)
        rows = _owned(root, refs["newRows"])["tables"]
        _need(all(value["SchemaVersion"] == "1.1.0" for role, values in rows.items() if role != "counter" for value in values)
              and rows["sessions"] and rows["messages"] and rows["proposals"] and rows["audit"], "new real current rows did not use admitted1.1")
        trace = _bind_protocol(root, row, performed["trace"], performed["commandId"], old_call=False)
        _need(sum(item.get("kind") == row["provider"] + ".call" for item in trace) == 1, "classed effect repeated adapter call")
        adopted_at = _owned(root, attempts[-1])["endedAt"]
        _need(all(adopted_at <= instant(item["at"]) <= process["stoppedAt"] and process["startedAt"] <= instant(item["at"]) for item in trace),
              "classed SDK preparation/execution did not follow automatic admission in the same current process")
        require_adapter_result(trace, row["provider"], performed["commandId"], execution["data"]["toolResult"])
        require_normal_terminal(native_frames(_owned(root, performed["continuation"]), origin, performed["sessionId"]))
    return generation


def require_legacy_effect_restart(row, root, *, source):
    """Deep read one native case, joining bytes, callers, sources and real package processes."""
    _need(isinstance(row, dict) and row.get("id") in LEGACY_RESTART_CASES and row.get("source") == source
          and row.get("bindings") == upgrade_bindings() and row.get("status") == "passed" and row.get("nativeEvidenceVerified") is True
          and row.get("qualified") is False and row.get("independentPackageWitness") is False and row.get("liveProvider") is False
          and row.get("evidenceCategory") == EVIDENCE_CATEGORY and row.get("failure") is None, "failed/foreign/unverified/promoted native case")
    provider, key = row["id"].split("/", 1)
    _need(row.get("provider") == row.get("adapter") == provider, "wrong actual adapter identity")
    lane, closeout, restart, processes = _read_lane(root, row, source)
    branches = row["branches"]
    _need(set(branches) == set(BRANCHES.get(key, ())), "missing/extra required legacy branch")
    if key == "all-legacy-row-kinds": _read_rows(root, row)
    elif key == "client-awaiting-upgrade": _read_client(root, row, lane, processes)
    elif key in ("effects-held", "effects-finalized"): _read_effects(root, row, lane, processes, held=key == "effects-held")
    else:
        expired = key == "expired-upgrade"
        boundary = _read_restart(root, row["refs"]["expiryRestart"], lane, processes) if expired else restart
        identities, actors = set(), set()
        for name, branch in branches.items():
            prepared, owner = _read_branch(root, row, branch, boundary, admin=key == "legacy-admin-disabled-upgrade",
                                           refusal=name.startswith("off-"), expired=expired)
            _need(prepared["invocationId"] not in identities and (owner["subject"], owner["tenantId"]) not in actors,
                  "independent branches reused a preparation/native revision owner")
            identities.add(prepared["invocationId"]); actors.add((owner["subject"], owner["tenantId"]))
            if expired: _need(boundary["stoppedAt"] < instant(prepared["expiresAt"]) < boundary["startedAt"], "expiry did not cross original clock deadline while stopped")
    return row


def require_legacy_effect_restarts(records, root, *, source):
    """Read the sixteen-case subset; this does not qualify the full T038 roster."""
    _need(isinstance(records, list) and [item.get("id") for item in records] == list(LEGACY_RESTART_CASES), "missing/duplicate/reordered legacy restart records")
    roots, pids = {}, {}
    for row in records:
        _need(isinstance(row.get("record"), dict) and same(_owned(root, row["record"]), {key: value for key, value in row.items() if key != "record"}),
              "legacy restart index differs from immutable full native record")
        require_legacy_effect_restart(row, root, source=source)
        provider = row["provider"]
        content = row["store"]["contentRoot"]
        _need(provider not in roots or roots[provider] == content, "adapter lane switched runtime databases")
        roots[provider] = content
        closeout = _owned(root, row["refs"]["laneCloseout"])
        pids[provider] = {value["pid"] for value in (_owned(root, ref) for ref in closeout["processes"])}
    _need(len(set(roots.values())) == len(PROVIDERS) and pids["copilot"].isdisjoint(pids["anthropic"]), "adapters shared a finalized database/process")
    by_id = {row["id"]: row for row in records}
    for provider in PROVIDERS:
        held, finalized = (by_id[provider + "/" + name] for name in ("effects-held", "effects-finalized"))
        process = finalized["sameProcess"]
        trace = _effect_restart_trace(root, held["attempt"]["trace"], held["attempt"]["commandId"])
        release = _owned(root, finalized["refs"]["release"])
        _need(all(process["startedAt"] <= instant(event["at"]) < release["startedAt"] for event in trace), "held refusal and release did not occur in the same actual current process")
        held_generation = _restart_native(root, held["observations"]["generation"], held["backendOrigin"], "/__qualification/generation")
        final_generation = _restart_native(root, finalized["observations"]["generation"], finalized["backendOrigin"], "/__qualification/generation")
        _need(held_generation.get("generation") == final_generation.get("generation"), "hold release replaced the serving shell instead of automatic same-process adoption")
    return records
