"""Exercise retained package binaries in both directions across the unchanged durable format.

Requires two successful durable qualification directories; builds nothing and changes neither input.
Uses their actual ordinary CShell/migration/readiness lifecycle and separate process-local closures.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sqlite3

ROOT = Path(__file__).resolve().parents[3]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def qualified(path):
    path = path.resolve()
    receipt = path / "report.json"
    report = json.loads(receipt.read_text())
    if report["status"] != "PassedDurableRecoveryProfile":
        raise ValueError("input must have passed durable qualification")
    if sha(path / "source.tar") != report["sourceArchiveSha256"]:
        raise ValueError("source archive changed after qualification")
    consumer = report["consumers"]["DurableConsumer"]
    if consumer["sourceReferences"] or consumer["authoringDependencies"]:
        raise ValueError("runtime consumer closure is outside the qualified package profile")
    binaries = path / "DurableConsumer/bin/Release/net10.0"
    archives = {sha(p) for p in (path / "feed").glob("*.nupkg")}
    for value in consumer["resolvedCandidates"].values():
        if value["archiveSha256"] not in archives:
            raise ValueError("a qualified package archive changed or is missing")
        for relative, expected in value["dlls"].items():
            if sha(binaries / Path(relative).name) != expected:
                raise ValueError("runtime product DLL differs from its qualified archive")
    return path, report, {"source": report["source"], "version": report["version"], "reportSha256": sha(receipt),
                          "consumerSha256": sha(binaries / "DurableConsumer.dll"),
                          "depsSha256": sha(binaries / "DurableConsumer.deps.json"),
                          "runtimeConfigSha256": sha(binaries / "DurableConsumer.runtimeconfig.json")}


def runtime_snapshot(case):
    # Read only the real state being protected; no schema/data repair or finalization is performed.
    with sqlite3.connect((case / "runtime.db").as_uri() + "?mode=ro", uri=True) as database:
        rows = {}
        for table in ["elsa_specialist_turns", "elsa_specialist_conversations", "elsa_runtime_workflow_execution_state",
                      "elsa_runtime_background_activity_work", "elsa_runtime_post_commit_outbox"]:
            rows[table] = sorted(database.execute('SELECT * FROM "' + table + '"').fetchall())
        versions = database.execute('SELECT SchemaVersion, FinalizedVersion FROM "__ElsaSchemaFinalization_ElsaRuntime" WHERE Family = ?',
                                    ("RuntimeWorkflowExecution",)).fetchone()
        turns = [json.loads(row[0]) for row in database.execute("SELECT Payload FROM elsa_specialist_turns")]
        if len(turns) != 1 or turns[0]["status"] != "Accepted" or not turns[0].get("protectedResult") or turns[0].get("applications"):
            raise RuntimeError("rollback refusal requires one protected accepted/unapplied turn")
        body = json.dumps({"rows": rows, "versions": versions}, sort_keys=True, default=lambda value: value.hex())
        return {"sha256": hashlib.sha256(body.encode()).hexdigest(), "schemaVersions": versions,
                "modelDispatches": len(list((case / "dispatches").glob("dispatch-*.json")))}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--older", type=Path, required=True)
    parser.add_argument("--newer", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--expect-runtime-schema-rollback-refusal", action="store_true",
                        help="Require old RuntimeWorkflowExecution reader1 refusal of finalized2, then recover using the new reader")
    args = parser.parse_args()
    older, newer = qualified(args.older), qualified(args.newer)
    if older[1]["source"] == newer[1]["source"] or older[1]["version"] == newer[1]["version"]:
        parser.error("two distinct qualified package revisions are required")
    if older[1].get("streamResponses", False) or not newer[1].get("streamResponses", False):
        parser.error("expected an older atomic and newer streaming witness")
    if subprocess.run(["git", "merge-base", "--is-ancestor", older[1]["source"], newer[1]["source"]], cwd=ROOT).returncode:
        parser.error("older source must be an ancestor of the newer source")
    output = args.output
    if not output.is_absolute() or output.exists() or output.resolve().is_relative_to(ROOT) or any(output.resolve().is_relative_to(x[0]) for x in [older, newer]):
        parser.error("output must be new, absolute and outside the checkout/input witnesses")
    output.mkdir(parents=True)
    output = output.resolve()
    report = {"status": "InProgress", "driverSha256": sha(Path(__file__)), "older": older[2], "newer": newer[2], "cases": [], "liveModel": False,
              "rollbackSchemaRefusal": args.expect_runtime_schema_rollback_refusal,
              "boundary": "Named package-binary upgrade/recovery and optional explicit schema rollback refusal, file SQLite, normal CShell lifecycle; no arbitrary downgrade, schema reversal, live model or power-loss claim."}

    def save():
        (output / "report.json").write_text(json.dumps(report, indent=2) + "\n")

    try:
        for direction, writer, reader in [("old-to-new", older, newer), ("new-to-old", newer, older)]:
            for persistence in ["Balanced", "Durable"]:
                case = output / (direction + "-" + persistence)
                case.mkdir()
                stages = []
                rollback_refusal = None
                active_case = {"direction": direction, "persistence": persistence, "stages": stages, "status": "InProgress"}
                report["cases"].append(active_case)
                save()
                inbox = writer[0] / "runtime-inbox"
                inbox_hashes = {p.name: sha(p) for p in inbox.iterdir() if p.is_file()}
                for role in ["start", "worker", "recover", "settle", "verify"]:
                    selected = writer if role in ["start", "worker"] else reader
                    if direction == "new-to-old" and role == "recover" and args.expect_runtime_schema_rollback_refusal:
                        before = runtime_snapshot(case)
                        if before["schemaVersions"][1] != "2.0.0" or before["modelDispatches"] != 1:
                            raise RuntimeError("rollback control has no actual finalized2/accepted source")
                        old_binary = reader[0] / "DurableConsumer/bin/Release/net10.0/DurableConsumer.dll"
                        refusal_log = case / "rollback-refusal.log"
                        with refusal_log.open("w") as stream:
                            refused = subprocess.run(["dotnet", str(old_binary), str(inbox), "recover", str(case), persistence],
                                                     cwd=old_binary.parent, stdout=stream, stderr=subprocess.STDOUT)
                        diagnostic = refusal_log.read_text()
                        if refused.returncode in [0, 197, 198] or not all(marker in diagnostic for marker in
                                ["EfSchemaActivationRefusedException", "RuntimeWorkflowExecution", "finalized at '2.0.0'", "reads only [1.0.0]"]):
                            raise RuntimeError("rollback did not refuse for the exact schema-reader boundary")
                        after = runtime_snapshot(case)
                        if before != after:
                            raise RuntimeError("refused rollback changed accepted/native/source state or repeated inference")
                        rollback_refusal = {"status": "ExpectedRefusal", "exitCode": refused.returncode, "source": reader[1]["source"],
                                            "family": "RuntimeWorkflowExecution", "readerVersion": "1.0.0", "finalizedVersion": "2.0.0",
                                            "preservedStateSha256": before["sha256"], "modelDispatches": 1}
                        stages.append({"role": "rollback-refusal", **rollback_refusal})
                        active_case["rollbackRefusal"] = rollback_refusal
                        save()
                    if direction == "new-to-old" and args.expect_runtime_schema_rollback_refusal and role not in ["start", "worker"]:
                        selected = newer
                    binary = selected[0] / "DurableConsumer/bin/Release/net10.0/DurableConsumer.dll"
                    command = ["dotnet", str(binary), str(inbox), role, str(case), persistence]
                    if selected[1].get("streamResponses", False):
                        command.append("stream")
                    log = case / (role + ".log")
                    with log.open("w") as stream:
                        result = subprocess.run(command, cwd=binary.parent, stdout=stream, stderr=subprocess.STDOUT)
                    expected = {"worker": 197, "recover": 198}.get(role, 0)
                    stages.append({"role": role, "source": selected[1]["source"], "exitCode": result.returncode,
                                   "expectedExitCode": expected, "status": ("ExpectedCrash" if expected else "Passed") if result.returncode == expected else "Failed"})
                    save()
                    if result.returncode != expected:
                        raise RuntimeError(str(log) + ": unexpected process outcome")
                receipts = [json.loads((case / (role + "-receipt.json")).read_text()) for role in ["start", "settle", "verify"]]
                crashes = [json.loads((case / name).read_text()) for name in ["crash.json", "ack-crash.json"]]
                if len({r["processId"] for r in receipts + crashes}) != 5:
                    raise RuntimeError("case did not cross five distinct processes")
                if crashes[0]["phase"] != "journal-accepted-before-native-receipt" or crashes[1]["phase"] != "application-written-before-source-ack":
                    raise RuntimeError("case missed its actual declared crash boundaries")
                dispatches = list((case / "dispatches").glob("dispatch-*.json"))
                if len(dispatches) != 1:
                    raise RuntimeError("case repeated or omitted inference")
                mode = json.loads(dispatches[0].read_text()).get("responseMode", "atomic")
                if mode != ("stream" if writer[1].get("streamResponses", False) else "atomic"):
                    raise RuntimeError("writer used the wrong model response path")
                if inbox_hashes != {p.name: sha(p) for p in inbox.iterdir() if p.is_file()}:
                    raise RuntimeError("writer publication was modified")
                active_case.update({"status": "PassedUpgradeWithRollbackRefusal" if rollback_refusal else "PassedBinaryRecovery",
                                    "publicationSha256": inbox_hashes, "responseMode": mode, "modelDispatches": 1,
                                    "processes": receipts, "expectedCrashes": crashes, "databaseSha256": sha(case / "runtime.db")})
                save()
                print(direction + "/" + persistence + ": binary recovery verified", flush=True)
        # Recheck the immutable package inputs after all role processes.
        if qualified(args.older)[2] != older[2] or qualified(args.newer)[2] != newer[2]:
            raise RuntimeError("qualification inputs changed during recovery")
        report["status"] = "PassedUpgradeAndRollbackRefusal" if args.expect_runtime_schema_rollback_refusal else "PassedBidirectionalPackageRecovery"
        save()
    except Exception:
        report["status"] = "Failed"
        save()
        raise


if __name__ == "__main__":
    main()
