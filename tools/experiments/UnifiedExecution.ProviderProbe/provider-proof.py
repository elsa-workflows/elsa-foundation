#!/usr/bin/env python3
"""Run the execution-grain adapter through real two-silo failure and recovery."""
import base64
import hashlib
import hmac
import json
import os
import pathlib
import shutil
import socket
import sqlite3
import subprocess
import tempfile
import time
import uuid
import zipfile

root = pathlib.Path(__file__).resolve().parent
repo = root.parents[2]
probe_dll = root / "bin/Debug/net10.0/UnifiedExecution.ProviderProbe.dll"
worker_dll = root / "bin/Debug/net10.0/UnifiedExecution.WorkProof.dll"
evidence_dir = root / "evidence"
evidence_dir.mkdir(parents=True, exist_ok=True)
evidence_path = evidence_dir / "provider-proof.json"
logs_zip = evidence_dir / "provider-proof-logs.zip"
if not probe_dll.exists() or not worker_dll.exists():
    raise SystemExit("Build UnifiedExecution.ProviderProbe before running this proof.")

proof_key = "unified-work-proof-local-key-2026"
persistence_scope, authority = "shared-local-store", "orleans-proof-host"


def make_scope(tenant, persistence=persistence_scope, scope_authority=authority):
    signature = hmac.new(proof_key.encode(), f"{tenant}\n{persistence}\n{scope_authority}".encode(), hashlib.sha256).hexdigest()
    return json.dumps({"TenantId": tenant, "PersistenceScope": persistence, "Authority": scope_authority, "Capability": signature}, separators=(",", ":"))


scope_json = make_scope("provider-tenant")
scope_arg = base64.b64encode(scope_json.encode()).decode()
ambiguous_scope_left = make_scope("collision-tenant\nsegment", "persist", "authority")
ambiguous_scope_right = make_scope("collision-tenant", "segment\npersist", "authority")
ambiguous_scope_left_value = json.loads(ambiguous_scope_left)
ambiguous_scope_right_value = json.loads(ambiguous_scope_right)
assert ambiguous_scope_left_value["Capability"] == ambiguous_scope_right_value["Capability"]
ambiguous_scope_capability_collision = {
    "leftIdentity": {key: ambiguous_scope_left_value[key] for key in ("TenantId", "PersistenceScope", "Authority")},
    "rightIdentity": {key: ambiguous_scope_right_value[key] for key in ("TenantId", "PersistenceScope", "Authority")},
    "sameLegacyHmacInput": True,
    "rejectedAtProviderBoundary": [],
}

evidence = {
    "schema": "unified-execution-provider-comparison-proof/v3",
    "framework": "Microsoft.Orleans.Server 10.4.0",
    "semantics": "same WorkProof SequencePolicy and FlowchartPolicy compare equal through direct CompositionCoordinator and one Orleans grain per complete execution",
    "providerCostNotes": {
        "builtIn": "Direct coordinator hosting adds no Orleans package, cluster membership, grain activation, or capability placement setup; distribution and failover remain the host's responsibility.",
        "orleans": "The Orleans adapter adds framework packages and operational concerns for cluster membership, activation, canonical grain identity, capability metadata, and placement; it supplies execution-addressed placement and activation recovery.",
        "measurement": "Qualitative dependency and operations comparison only; no performance or cost benchmark was run."
    },
    "placement": "custom Orleans placement director filters IPlacementContext.GetCompatibleSilos by membership-published capability metadata; no separate liveness registry",
    "limits": ["local loopback clustering and SQLite WAL on one machine", "proof identity key and tenant scope are experiment-only", "worker handlers execute in independent local OS processes"],
    "capabilityRefusals": [],
    "scenarios": [],
    "providerComparisons": [],
    "logs": {},
}
temp = pathlib.Path(tempfile.mkdtemp(prefix="elsa-orleans-provider-proof-"))
work_db = temp / "coordinator.sqlite"
sink_db = temp / "destination.sqlite"
run_logs = temp / "logs"
run_logs.mkdir()
env = os.environ.copy()
env["ELSA_WORK_DB"] = str(work_db)
env["ELSA_SINK_DB"] = str(sink_db)
env["ELSA_WORK_PROOF_KEY"] = proof_key
process_records = []


def record_log(name, content):
    path = run_logs / name
    path.write_text(content)
    evidence["logs"][name] = hashlib.sha256(path.read_bytes()).hexdigest()
    return content


def run_cli(name, *args, timeout=25, check=True):
    completed = subprocess.run(["dotnet", str(probe_dll), *map(str, args)], env=env, capture_output=True, text=True, timeout=timeout)
    content = completed.stdout + completed.stderr
    record_log(name, content)
    if check and completed.returncode != 0:
        raise RuntimeError(f"{name} exited {completed.returncode}: {content}")
    return completed.returncode, content


def run_worker(name, execution_id, invocation_id):
    process = start_worker(name, execution_id, invocation_id)
    return finish_worker(name, execution_id, invocation_id, process)


def start_worker(name, execution_id, invocation_id, barrier_id="-"):
    process = subprocess.Popen(
        ["dotnet", str(worker_dll), "--worker", str(work_db), scope_arg, execution_id, invocation_id,
         f"provider-{name}", barrier_id, "none", str(sink_db)],
        env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    process_records.append({"role": "workproof-worker", "pid": process.pid, "name": name,
                            "executionId": execution_id, "invocationId": invocation_id,
                            "exit": None, "cleanupAction": "awaiting-result"})
    return process


def finish_worker(name, execution_id, invocation_id, process):
    timed_out = False
    try:
        content, _ = process.communicate(timeout=35)
    except subprocess.TimeoutExpired:
        timed_out = True
        process.kill()
        content, _ = process.communicate(timeout=5)
    record_log(f"worker-{name}.log", content)
    record = next(item for item in process_records if item.get("role") == "workproof-worker" and item["pid"] == process.pid)
    record.update({"exit": process.returncode, "cleanupAction": "killed-after-timeout" if timed_out else "completed"})
    if timed_out:
        raise RuntimeError(f"worker {name} process {process.pid} exceeded its bounded wait")
    if process.returncode != 0:
        raise RuntimeError(f"worker {name} exited {process.returncode}: {content}")
    return content


def run_barrier_workers(policy, execution_id, work_items):
    barrier_id = f"provider-{policy}-{uuid.uuid4().hex}"
    run_cli(f"{policy}-barrier-create.log", "barrier-create", barrier_id)
    workers = []
    deadline = time.monotonic() + 35
    barrier = None
    try:
        for work in work_items:
            workers.append((work["InvocationId"], start_worker(
                f"{policy}-{work['InvocationId']}", execution_id, work["InvocationId"], barrier_id)))
        while time.monotonic() < deadline:
            _, state_text = run_cli(f"{policy}-barrier-state.log", "barrier-state", barrier_id)
            barrier = json.loads(state_text.strip().splitlines()[-1])
            if barrier["count"] >= len(work_items):
                break
            if any(process.poll() is not None for _, process in workers):
                raise RuntimeError("A Flowchart worker exited before entering the overlap barrier.")
            time.sleep(.2)
        if barrier is None or barrier["count"] < len(work_items):
            raise RuntimeError(f"Flowchart workers did not all enter barrier {barrier_id}.")
        worker_pids = [process.pid for _, process in workers]
        barrier_pids = barrier["processIds"]
        assert len(set(worker_pids)) == len(work_items)
        assert sorted(barrier_pids) == sorted(worker_pids), f"barrier did not contain these worker processes: {barrier}"
        assert all(process.poll() is None for _, process in workers), "workers must still be alive and blocked before release"
        run_cli(f"{policy}-barrier-release.log", "barrier-release", barrier_id)
        for (invocation_id, process) in workers:
            finish_worker(f"{policy}-{invocation_id}", execution_id, invocation_id, process)
        return {"barrierId": barrier_id, "enteredProcessIds": barrier_pids, "workersOverlappedBeforeRelease": True}
    finally:
        # Release before reaping on an assertion/timeout so a healthy child can leave its barrier.
        try:
            run_cli(f"{policy}-barrier-release-cleanup.log", "barrier-release", barrier_id, check=False)
        finally:
            for _, process in workers:
                if process.poll() is None:
                    process.kill()
                    process.wait(timeout=5)
                    record = next(item for item in process_records
                                  if item.get("role") == "workproof-worker" and item["pid"] == process.pid)
                    record.update({"exit": process.returncode, "cleanupAction": "killed-during-barrier-cleanup"})


def normalize_semantics(snapshot):
    def field(value, upper, lower):
        return value[upper] if upper in value else value[lower]

    work = []
    for item in snapshot["work"]:
        work.append({
            "invocationId": field(item, "InvocationId", "invocationId"),
            "handlerId": field(item, "HandlerId", "handlerId"),
            "handlerVersion": field(item, "HandlerVersion", "handlerVersion"),
            "inputJson": field(item, "InputJson", "inputJson"),
            "status": field(item, "Status", "status"),
        })
    return {
        "policyId": field(snapshot, "PolicyId", "policyId"),
        "status": field(snapshot, "Status", "status"),
        "stateJson": field(snapshot, "StateJson", "stateJson"),
        "work": sorted(work, key=lambda item: item["invocationId"]),
        "pendingContinuations": sorted(snapshot["pendingContinuations"]),
        "effects": snapshot["effectPayloads"],
    }


def read_effect_payloads(database, scope_text, execution_id):
    scope = json.loads(scope_text)
    with sqlite3.connect(database) as connection:
        rows = connection.execute(
            "SELECT effect_key,payload FROM effects WHERE tenant=? AND persistence_scope=? AND authority=? AND execution_id=? ORDER BY effect_key",
            (scope["TenantId"], scope["PersistenceScope"], scope["Authority"], execution_id),
        ).fetchall()
    return [{"effectKey": effect_key, "payload": payload} for effect_key, payload in rows]


def run_builtin_reference(policy, execution_id, encoded_input):
    builtin_work = temp / f"builtin-{policy}.sqlite"
    builtin_sink = temp / f"builtin-{policy}-sink.sqlite"
    env["ELSA_WORK_DB"] = str(builtin_work)
    env["ELSA_SINK_DB"] = str(builtin_sink)
    completed = subprocess.run(
        ["dotnet", str(probe_dll), "builtin-run", str(builtin_work), str(builtin_sink), scope_arg,
         execution_id, policy, encoded_input], env=env, capture_output=True, text=True, timeout=90)
    content = completed.stdout + completed.stderr
    record_log(f"{policy}-builtin-run.log", content)
    if completed.returncode != 0:
        raise RuntimeError(f"built-in {policy} reference exited {completed.returncode}: {content}")
    return json.loads(content.strip().splitlines()[-1])


def grain_key(scope_text, execution_id, policy):
    scope = json.loads(scope_text)
    capability = "sequence-v1" if policy == "sequence" else "flowchart-v1"

    def binary_writer_string(value):
        payload = value.encode("utf-8")
        length = len(payload)
        prefix = bytearray()
        while length >= 0x80:
            prefix.append((length & 0x7F) | 0x80)
            length >>= 7
        prefix.append(length)
        return bytes(prefix) + payload

    identity = b"".join(binary_writer_string(value) for value in (
        "elsa.execution-grain-key.v1", capability, scope["TenantId"],
        scope["PersistenceScope"], scope["Authority"], execution_id))
    return capability + "|" + hashlib.sha256(identity).hexdigest()


def encoded(value):
    return base64.b64encode(value.encode()).decode()


def call_with_retry(log_name, gateway, cluster, command, *args, timeout_seconds=90):
    deadline = time.monotonic() + timeout_seconds
    attempts = []
    while time.monotonic() < deadline:
        code, content = run_cli(f"{log_name}-{len(attempts)}.log", command, gateway, cluster, scope_arg, *args, check=False)
        attempts.append({"exit": code, "log": f"{log_name}-{len(attempts)}.log"})
        if code == 0:
            return json.loads(content.strip().splitlines()[-1]), attempts
        time.sleep(.7)
    raise RuntimeError(f"{command} failed through surviving gateway after {len(attempts)} attempts: {attempts[-1] if attempts else 'no attempt'}")


def wait_for_ready(process, log_path, port, deadline_seconds=30):
    deadline = time.monotonic() + deadline_seconds
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f"Silo {port} exited {process.returncode}; see {log_path}")
        content = log_path.read_text(errors="replace")
        if f"SILO_READY {port} pid={process.pid}" in content:
            return content
        time.sleep(.1)
    raise RuntimeError(f"Silo {port} did not announce readiness; see {log_path}")


def start_silos(cluster, ports):
    processes, streams = [], []
    capabilities = "sequence-v1,flowchart-v1"
    try:
        for port in ports:
            log_path = run_logs / f"silo-{cluster}-{port}.log"
            stream = log_path.open("w")
            streams.append(stream)
            process = subprocess.Popen(
                ["dotnet", str(probe_dll), "silo", str(port), cluster, capabilities, str(ports[0])],
                env=env, stdout=stream, stderr=subprocess.STDOUT)
            processes.append(process)
            process_records.append({"role": "orleans-silo", "cluster": cluster, "pid": process.pid,
                                    "port": port, "log": log_path.name})
            wait_for_ready(process, log_path, port)
        time.sleep(1.0)
        return processes, streams
    except BaseException as error:
        try:
            stop_silos(processes, streams)
        except Exception as cleanup_error:
            error.add_note(f"Silo startup cleanup also failed: {cleanup_error}")
        raise


def stop_silos(processes, streams):
    actions = {}
    for process in processes:
        if process.poll() is None:
            process.terminate()
            actions[process.pid] = "terminate"
        else:
            actions[process.pid] = "already-exited"
    failures = []
    for process in processes:
        try:
            process.wait(timeout=12)
        except subprocess.TimeoutExpired:
            actions[process.pid] = "kill-after-timeout"
            process.kill()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired as error:
                failures.append(f"silo process {process.pid} remained alive after kill: {error}")
        record = next((item for item in process_records
                       if item.get("role") == "orleans-silo" and item.get("pid") == process.pid), None)
        if record is not None:
            record.update({"shutdownAction": actions[process.pid], "exitCode": process.poll(),
                           "shutdownConfirmed": process.poll() is not None})
        if process.poll() is None:
            failures.append(f"silo process {process.pid} is still running")
    for stream in streams:
        stream.close()
    if failures:
        raise RuntimeError("Silo cleanup failed: " + "; ".join(failures))


def assert_ports_free(ports):
    for port in ports:
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", port))


def run_scenario(policy, ports):
    cluster = f"elsa-provider-{policy}-{uuid.uuid4().hex[:8]}"
    gateway_primary, gateway_secondary = ports[0] + 100, ports[1] + 100
    execution_id = f"provider-{policy}-{uuid.uuid4().hex}"
    input_json = json.dumps({"value": f"same-{policy}-definition"}, separators=(",", ":"))
    encoded_input = base64.b64encode(input_json.encode()).decode()
    builtin = run_builtin_reference(policy, execution_id, encoded_input)
    builtin_final = builtin["snapshot"]
    if policy == "flowchart":
        assert builtin["workerOverlap"] and len(set(builtin["workerBarrierProcessIds"])) == 2

    env["ELSA_WORK_DB"] = str(work_db)
    env["ELSA_SINK_DB"] = str(sink_db)
    processes, streams = start_silos(cluster, ports)
    try:
        _, start_text = run_cli(f"{policy}-start.log", "proof-start", gateway_secondary, cluster, scope_arg,
                                execution_id, policy, encoded_input)
        initial = json.loads(start_text.strip().splitlines()[-1])
        assert initial["Status"] == "Running"
        assert initial["PolicyId"] == ("native-sequence-witness-v1" if policy == "sequence" else "native-flowchart-witness-v1")
        assert initial["pid"] == processes[1].pid, f"execution should be placed on the last compatible silo ({processes[1].pid}), got {initial['pid']}"
        initial_work = initial["work"]
        assert len(initial_work) == (1 if policy == "sequence" else 2)
        required_capability = "sequence-v1" if policy == "sequence" else "flowchart-v1"

        # First prove that the metadata cache contains the positive capability before
        # testing a demand which neither live silo advertises.
        refusal_code, refusal_text = run_cli(f"{policy}-missing-capability.log", "capability-probe", gateway_primary, cluster, "missing-proof-v1", check=False)
        assert refusal_code == 0 and "CAPABILITY_REFUSED" in refusal_text
        silo_logs = [run_logs / f"silo-{cluster}-{port}.log" for port in ports]
        placement_lines = "\n".join(path.read_text(errors="replace") for path in silo_logs)
        assert f"CAPABILITY_PLACEMENT required={required_capability} compatible=2 capable=2" in placement_lines
        evidence["capabilityRefusals"].append({"requested": "missing-proof-v1", "whilePolicy": policy,
                                                "refused": True, "log": f"{policy}-missing-capability.log"})

        provider_overlap = None
        if policy == "flowchart":
            provider_overlap = run_barrier_workers(policy, execution_id, initial_work)
        else:
            for work in initial_work:
                run_worker(f"{policy}-{work['InvocationId']}", execution_id, work["InvocationId"])

        _, before_kill_text = run_cli(f"{policy}-before-kill-read.log", "proof-read", gateway_secondary, cluster,
                                      scope_arg, execution_id, policy)
        before_kill = json.loads(before_kill_text.strip().splitlines()[-1])
        assert all(work["Status"] == "succeeded" for work in before_kill["work"])
        assert before_kill["revision"] == initial["revision"], "worker receipts must be durable before the composition applies them"
        assert before_kill["owner"] == initial["owner"] and before_kill["fence"] == initial["fence"]
        if policy == "sequence":
            assert all(work["InvocationId"] != "sequence-1" for work in before_kill["work"]), "Sequence successor must remain component-owned"
        else:
            assert before_kill["pendingContinuations"] == [], "join continuation is not durable until the Flowchart component applies both results"

        killed = processes[1]
        survivor = processes[0]
        killed.kill()
        killed.wait(timeout=10)
        process_records.append({"role": "killed-execution-owner", "cluster": cluster, "pid": killed.pid,
                                "signal": "SIGKILL", "port": ports[1], "exitCode": killed.returncode,
                                "shutdownConfirmed": killed.poll() is not None})

        recovered, attempts = call_with_retry(f"{policy}-recovery-read", gateway_primary, cluster, "proof-read",
                                              execution_id, policy)
        assert recovered["pid"] == survivor.pid, f"activation must recover on compatible surviving silo {survivor.pid}, got {recovered['pid']}"
        assert recovered["owner"] != initial["owner"] and recovered["fence"] > initial["fence"]

        stale_args = ["proof-stale", gateway_primary, cluster, scope_arg, execution_id, policy,
                      base64.b64encode(initial["owner"].encode()).decode(), str(initial["fence"])]
        stale_code, stale_text = run_cli(f"{policy}-stale-owner.log", *stale_args, check=False)
        assert stale_code == 0, f"stale probe call failed: {stale_text}"
        stale = json.loads(stale_text.strip().splitlines()[-1])
        assert stale["rejected"] and stale["current"]["Fence"] == recovered["fence"]

        key = grain_key(scope_json, execution_id, policy)
        _, canonical_key_text = run_cli(f"{policy}-canonical-key.log", "canonical-key", encoded(scope_json),
                                        execution_id, policy)
        assert canonical_key_text.strip().splitlines()[-1] == key, "Python canonical key must match the provider implementation"
        changed_tenant_scope = make_scope("another-provider-tenant")
        mismatch_probes = [
            ("alias", required_capability + "|alias-" + uuid.uuid4().hex, scope_json, execution_id),
            ("changed-execution", key, scope_json, execution_id + "-other"),
            ("changed-tenant", key, changed_tenant_scope, execution_id),
            ("ambiguous-scope", key, ambiguous_scope_right, execution_id),
        ]
        for label, addressed_key, payload_scope, payload_execution in mismatch_probes:
            code, mismatch_text = run_cli(
                f"{policy}-identity-{label}.log", "proof-mismatch", gateway_primary, cluster,
                encoded(addressed_key), encoded(payload_scope), payload_execution, policy)
            expected_rejection = "CANONICAL_SCOPE_REJECTED" if label == "ambiguous-scope" else "IDENTITY_MISMATCH_REJECTED"
            assert code == 0 and expected_rejection in mismatch_text
            _, unchanged_text = run_cli(f"{policy}-identity-{label}-state.log", "proof-read", gateway_primary,
                                        cluster, scope_arg, execution_id, policy)
            unchanged = json.loads(unchanged_text.strip().splitlines()[-1])
            assert unchanged["owner"] == recovered["owner"] and unchanged["fence"] == recovered["fence"]
            assert unchanged["revision"] == recovered["revision"], f"{label} mismatch changed the durable execution"
            if label == "ambiguous-scope":
                ambiguous_scope_capability_collision["rejectedAtProviderBoundary"].append(policy)

        _, pump_text = run_cli(f"{policy}-recovery-pump.log", "proof-pump", gateway_primary, cluster,
                               scope_arg, execution_id, policy)
        pump = json.loads(pump_text.strip().splitlines()[-1])
        after_first_pump = pump["snapshot"]
        if policy == "sequence":
            successor = next(work for work in after_first_pump["work"] if work["InvocationId"] == "sequence-1")
            assert successor["Status"] == "owed"
            run_worker(f"{policy}-sequence-1", execution_id, "sequence-1")
            _, final_pump_text = run_cli(f"{policy}-completion-pump.log", "proof-pump", gateway_primary, cluster,
                                         scope_arg, execution_id, policy)
            final = json.loads(final_pump_text.strip().splitlines()[-1])["snapshot"]
        else:
            final = after_first_pump
        assert final["Status"] == "Completed", f"{policy} component must own its terminal decision"
        assert final["effects"] == 2
        assert final["pid"] == survivor.pid and final["fence"] == recovered["fence"]

        builtin_effects = read_effect_payloads(temp / f"builtin-{policy}-sink.sqlite", scope_json, execution_id)
        orleans_effects = read_effect_payloads(sink_db, scope_json, execution_id)
        assert len(builtin_effects) == builtin_final["effects"]
        assert len(orleans_effects) == final["effects"]
        builtin_semantics = normalize_semantics({**builtin_final, "effectPayloads": builtin_effects})
        orleans_semantics = normalize_semantics({**final, "effectPayloads": orleans_effects})
        assert builtin_semantics == orleans_semantics, (
            f"built-in and Orleans semantic outcomes differ for {policy}: "
            f"built-in={builtin_semantics} Orleans={orleans_semantics}")
        semantic_fingerprint = lambda value: hashlib.sha256(
            json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
        comparison = {
            "policy": policy,
            "sameExecutionId": execution_id,
            "samePolicyDefinition": builtin_semantics["policyId"] == orleans_semantics["policyId"],
            "builtInSemanticSha256": semantic_fingerprint(builtin_semantics),
            "orleansSemanticSha256": semantic_fingerprint(orleans_semantics),
            "semanticOutcomesEqual": True,
            "effectPayloadsEqual": builtin_semantics["effects"] == orleans_semantics["effects"],
            "effectPayloadCount": len(orleans_semantics["effects"]),
            "builtInWorkerOverlap": builtin.get("workerOverlap", False),
            "orleansWorkerOverlap": bool(provider_overlap and provider_overlap["workersOverlappedBeforeRelease"]),
            "workerOverlap": provider_overlap or {"workerBarrierProcessIds": builtin.get("workerBarrierProcessIds", [])},
            "orchestration": "one composition coordinator applies accepted results and owns successor/join completion",
        }
        evidence["providerComparisons"].append(comparison)

        return {
            "policy": policy,
            "policyId": initial["PolicyId"],
            "executionId": execution_id,
            "requiredCapability": required_capability,
            "compatibleSiloPids": [process.pid for process in processes],
            "initialOwnerPid": initial["pid"],
            "killedOwnerPid": killed.pid,
            "recoveredOwnerPid": recovered["pid"],
            "oldOwner": initial["owner"],
            "oldFence": initial["fence"],
            "recoveredOwner": recovered["owner"],
            "recoveredFence": recovered["fence"],
            "sameExecutionKeyReused": True,
            "workerReceiptsWereDurableBeforeCoordinatorPump": True,
            "recoveryAttempts": attempts,
            "stalePriorOwnerRejected": stale["rejected"],
            "identityMismatchProbesRejected": [label for label, *_ in mismatch_probes if label != "ambiguous-scope"],
            "ambiguousScopeRejectedBeforeMutation": "ambiguous-scope" in mismatch_probes[-1][0]
                and ambiguous_scope_capability_collision["rejectedAtProviderBoundary"].count(policy) == 1,
            "identityMismatchChangedFenceOrRevision": False,
            "finalStatus": final["Status"],
            "finalRevision": final["revision"],
            "destinationEffects": final["effects"],
            "builtInFinalStatus": builtin_final["status"],
            "builtInFinalRevision": builtin_final["revision"],
            "semanticFingerprint": semantic_fingerprint(orleans_semantics),
            "workerOverlap": comparison["workerOverlap"],
        }
    finally:
        stop_silos(processes, streams)


try:
    assert_ports_free([19311, 19312, 19411, 19412])
    evidence["scenarios"].append(run_scenario("sequence", (19311, 19312)))
    evidence["scenarios"].append(run_scenario("flowchart", (19311, 19312)))
    evidence["processes"] = process_records
    silo_records = [item for item in process_records if item.get("role") == "orleans-silo"]
    worker_records = [item for item in process_records if item.get("role") == "workproof-worker"]
    assert silo_records and all(item.get("shutdownConfirmed") and item.get("exitCode") is not None for item in silo_records), \
        "Every Orleans silo process must be observed exited before the proof passes."
    assert worker_records and all(item.get("exit") is not None for item in worker_records), \
        "Every provider worker process must be observed exited before the proof passes."
    assert all(item["exit"] == 0 for item in worker_records), "Every provider worker must exit successfully."
    assert all(item.get("shutdownConfirmed") for item in process_records if item.get("role") == "killed-execution-owner")
    evidence["cleanup"] = {
        "allSilosExited": all(item.get("shutdownConfirmed") for item in silo_records),
        "allProviderWorkersExited": all(item.get("exit") is not None for item in worker_records),
        "allProviderWorkersSucceeded": all(item.get("exit") == 0 for item in worker_records),
        "killedOwnersConfirmedExited": all(item.get("shutdownConfirmed") for item in process_records
                                            if item.get("role") == "killed-execution-owner"),
        "temporaryRunDirectoryRemoved": False,
    }
    evidence["canonicalScopeCollision"] = ambiguous_scope_capability_collision
    source_paths = [
        root / "Program.cs", root / "OrleansExecutionAdapter.cs", root / "ProviderClientCommands.cs",
        root / "BuiltInCoordinatorProbe.cs", root / "README.md", root / "UnifiedExecution.ProviderProbe.csproj",
        root / "packages.lock.json", root / "provider-proof.py",
    ]
    work_proof_root = repo / "tools/experiments/UnifiedExecution.WorkProof"
    source_paths.extend(sorted(work_proof_root.glob("*.cs")))
    source_paths.extend([
        work_proof_root / "UnifiedExecution.WorkProof.csproj",
        work_proof_root / "packages.lock.json",
        repo / "Directory.Build.props",
        repo / "Directory.Packages.props",
    ])
    evidence["sourceSha256"] = {
        str(path.relative_to(repo)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in source_paths
    }
    evidence["passed"] = True
except BaseException as error:
    evidence["passed"] = False
    evidence["error"] = f"{type(error).__name__}: {error}"
    raise
finally:
    run_was_passing = bool(evidence.get("passed"))
    evidence["processes"] = process_records
    archive_error = None
    try:
        with zipfile.ZipFile(logs_zip, "w", zipfile.ZIP_DEFLATED) as archive:
            for path in sorted(run_logs.iterdir()):
                archive.write(path, path.name)
    except Exception as error:
        archive_error = f"{type(error).__name__}: {error}"
        evidence["passed"] = False
        evidence["archiveError"] = archive_error

    try:
        shutil.rmtree(temp)
        evidence.setdefault("cleanup", {})["temporaryRunDirectoryRemoved"] = not temp.exists()
    except OSError as error:
        evidence["cleanup"] = evidence.get("cleanup", {})
        evidence["cleanup"]["temporaryRunDirectoryRemoved"] = False
        evidence["cleanupError"] = f"{type(error).__name__}: {error}"
        evidence["passed"] = False

    if logs_zip.exists():
        digest = hashlib.sha256(logs_zip.read_bytes()).hexdigest()
        manifest_path = evidence_dir / "sha256.json"
        manifest = json.loads(manifest_path.read_text()) if manifest_path.exists() else {}
        manifest["provider-proof-logs.zip"] = digest
        evidence_path.write_text(json.dumps(evidence, indent=2) + "\n")
        manifest["provider-proof.json"] = hashlib.sha256(evidence_path.read_bytes()).hexdigest()
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    else:
        evidence_path.write_text(json.dumps(evidence, indent=2) + "\n")
    print(evidence_path)
    print(logs_zip)
    if run_was_passing and not evidence.get("passed"):
        raise RuntimeError(evidence.get("cleanupError") or evidence.get("archiveError") or "Provider proof cleanup failed.")
