#!/usr/bin/env python3
"""Actual native Flowchart → ETL → Done with both ETL stores on each server engine.

Uses the existing owned-container/host helpers. Only completed-state restart is
proved here; no mid-work crash, old-writer upgrade or Studio claim follows.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import re
import secrets
import shutil
import subprocess
import sys
import tempfile
import urllib.parse
import uuid
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
HELPER = REPO / "specs/1202-etl-design-store-providers/evidence/design_store_workbench_journey.py"
spec = importlib.util.spec_from_file_location("etl_design_journey", HELPER)
legacy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(legacy)


def verify_product(assembly: Path, receipt_path: Path) -> dict:
    """Require a clean, committed product source and verify every published byte."""
    receipt = json.loads(receipt_path.read_text())
    source = receipt["sourceRevision"]
    if not re.fullmatch(r"[0-9a-f]{40}", source) or receipt["workingTreeDirty"] is not False:
        raise ValueError("Products must have a clean committed source receipt.")
    subprocess.run(["git", "merge-base", "--is-ancestor", source, "HEAD"], cwd=REPO, check=True)
    changes = subprocess.check_output(["git", "diff", "--name-only", source, "HEAD"], cwd=REPO, text=True).splitlines()
    if any(not name.startswith("specs/1206-etl-runtime-concurrency/evidence/") for name in changes):
        raise ValueError("Product source differs beyond this journey's evidence files; rebuild intended products.")
    if subprocess.check_output(["git", "status", "--porcelain"], cwd=REPO, text=True).strip():
        raise ValueError("Commit the executing harness before recording evidence.")
    directory = assembly.parent
    actual = {str(f.relative_to(directory)): legacy.sha256(f.read_bytes())
              for f in sorted(directory.rglob("*")) if f.is_file()}
    if actual != receipt["files"]:
        raise ValueError("Published product files do not match the build receipt.")
    return {"sourceRevision": source, "workingTreeDirty": False, "fileCount": len(actual),
            "receiptSha256": legacy.sha256(receipt_path.read_bytes()),
            "workbenchAssemblySha256": actual[assembly.name], "helperSha256": legacy.sha256(HELPER.read_bytes())}


def database_scalars(database) -> dict:
    """Read only the fresh owned database; never fabricate run/lease/result authority."""
    provider = database.provider
    def table(name):
        return f'"{name}"' if provider == "PostgreSql" else f"`{name}`" if provider == "MySql" else f"[dbo].[{name}]"
    def column(name):
        return f'"{name}"' if provider == "PostgreSql" else f"`{name}`" if provider == "MySql" else f"[{name}]"
    r, c, p, t, o = [table("elsa_etl_" + suffix) for suffix in
                      ("runtime_runs", "connector_runs", "connector_partitions", "runtime_terminals", "runtime_outbox")]
    queries = {
        "runs": f"SELECT COUNT(*) FROM {r}",
        "connectorRuns": f"SELECT COUNT(*) FROM {c}",
        "partitions": f"SELECT COUNT(*) FROM {p}",
        "terminals": f"SELECT COUNT(*) FROM {t}",
        "outbox": f"SELECT COUNT(*) FROM {o}",
        "acknowledged": f"SELECT COUNT(*) FROM {o} WHERE {column('AcknowledgedAtUtcTicks')} IS NOT NULL AND {column('Disposition')} IN (0,1,2)",
        "disposition": f"SELECT COALESCE({column('Disposition')},-1) FROM {o}",
        "correlated": f"SELECT COUNT(*) FROM {t} t JOIN {o} o ON t.{column('ScopeKey')}=o.{column('ScopeKey')} AND t.{column('RunKey')}=o.{column('RunKey')} JOIN {r} r ON t.{column('ScopeKey')}=r.{column('ScopeKey')} AND t.{column('RunKey')}=r.{column('RunKey')}",
        "resultHash": f"SELECT {column('ResultHash')} FROM {t}",
        "payloadHash": f"SELECT {column('PayloadHash')} FROM {o}",
        "resultJson": f"SELECT {column('ResultJson')} FROM {t}",
        "invocationJson": f"SELECT {column('InvocationJson')} FROM {r}",
        "resultId": f"SELECT {column('ResultId')} FROM {t}",
        "deliveryId": f"SELECT {column('DeliveryId')} FROM {o}",
    }
    result = {}
    for key, sql in queries.items():
        if provider == "PostgreSql":
            command = ["docker", "exec", database.name, "psql", "-U", "postgres", "-d", "elsa_etl", "-At", "-c", sql]
        elif provider == "MySql":
            command = ["docker", "exec", database.name, "mysql", "-h", "127.0.0.1", "-uroot", f"-p{database.secret}", "--raw", "-N", "-e", sql, "elsa_etl"]
        else:
            command = ["docker", "exec", database.name, "/opt/mssql-tools18/bin/sqlcmd", "-C", "-S", "localhost", "-U", "sa",
                       "-P", database.secret, "-d", "elsa_etl", "-b", "-y", "0", "-w", "65535", "-Q",
                       "SET NOCOUNT ON; SELECT (" + sql + ") AS Value"]
        completed = subprocess.run(command, capture_output=True, text=True)
        if completed.returncode:
            # Command arguments can contain the generated password; do not expose them in an exception.
            raise RuntimeError(f"Owned {provider} readback failed for {key}.")
        values = [line.strip() for line in completed.stdout.splitlines() if line.strip()]
        if provider == "SqlServer":
            # Unlimited variable-width output cannot be combined with header suppression.
            values = [line for line in values if line != "Value" and set(line) != {"-"}]
        if len(values) != 1:
            raise ValueError(f"Expected one owned-database scalar for {key}.")
        result[key] = values[0] if key.endswith(("Hash", "Json", "Id")) else int(values[0])
    return result


def physical_output(root: Path) -> dict:
    paths = sorted((root / "people-output").glob("*.jsonl"))
    errors = sorted((root / "people-errors").glob("*.jsonl"))
    records = [json.loads(line) for path in paths for line in path.read_text().splitlines() if line]
    return {"records": sorted(records, key=lambda row: row["id"]),
            "files": {path.name: legacy.sha256(path.read_bytes()) for path in paths},
            "errorFiles": {path.name: legacy.sha256(path.read_bytes()) for path in errors}}


def snapshot(api, database, root: Path, run_id: str, workflow_id: str) -> dict:
    run_path = "/etl/runs/" + urllib.parse.quote(run_id, safe="")
    partitions = api.request(run_path + "/connector-partitions?limit=100")
    return {"run": api.request(run_path), "partitions": partitions,
            "workflow": api.request("/runtime/workflows/instances/" + urllib.parse.quote(workflow_id, safe="")),
            "database": database_scalars(database), "output": physical_output(root)}


def native_facts(view: dict) -> dict:
    return {"status": view["instance"]["status"], "incidents": len(view.get("incidents") or []),
            "executedNodes": sorted(activity["executableNodeId"] for activity in view["activities"])}


def evaluate(case: dict) -> dict:
    before, after = case["before"], case["after"]
    run, parts, db = before["run"], before["partitions"], before["database"]
    rows = sorted(parts["items"], key=lambda row: row["partitionOrdinal"])
    ranges = [row["file"] for row in rows]
    expected_records = len(legacy.RECORDS)
    complete_ranges = (len(rows) == 2 and [row["partitionOrdinal"] for row in rows] == [0, 1]
        and all(r is not None and r["nextOrdinal"] == r["endOrdinalExclusive"] and
                r["startOrdinal"] < r["endOrdinalExclusive"] for r in ranges)
        and ranges[0]["startOrdinal"] == 1 and ranges[0]["endOrdinalExclusive"] == ranges[1]["startOrdinal"]
        and ranges[1]["endOrdinalExclusive"] == expected_records + 1)
    expected_effects = sum((r["endOrdinalExclusive"] - r["startOrdinal"] + 1) // 2 for r in ranges) if complete_ranges else -1
    terminal = run.get("terminalResult") or {}
    source = run.get("sourceInput") or {}
    connector = run.get("connector") or {}
    facts = native_facts(before["workflow"])
    after_facts = native_facts(after["workflow"])
    return {
        "nativeCompletedOnce": facts["status"] in ("Completed", "Finished") and facts["incidents"] == 0
            and facts["executedNodes"].count("etl") == 1 and facts["executedNodes"].count("after-done") == 1,
        "publishedRunAndPins": run["runId"] == case["runId"] and run["selection"] == case["selection"]
            and run["formatVersion"] == 2 and run["bindingAliases"] == {"source": "people-source", "lookup": None,
                "output": "people-output", "errors": "people-errors"}
            and source == {"bindingRevision": "v1", "expectedSha256": case["inputSha256"],
                "snapshotSha256": case["inputSha256"], "snapshotByteLength": case["inputBytes"]},
        "exactTerminalCounters": run["status"] == "succeeded" and run["sourceRecordCount"] == expected_records
            and run["acceptedRecords"] == expected_records and run["rejectedRecords"] == run["filteredRecords"] == 0
            and run["outstandingRecords"] == run["outstandingBytes"] == 0 and run.get("diagnostic") is None
            and terminal.get("outcome") == "succeeded" and terminal.get("acceptedRecords") == expected_records
            and terminal.get("rejectedRecords") == terminal.get("filteredRecords") == 0 and terminal.get("diagnostic") is None,
        "twoCompletedPartitions": complete_ranges and parts["nextCursor"] is None
            and all(row["owedBatch"] is None and row["http"] is None and row["notBefore"] is None for row in rows),
        "connectorEffects": connector.get("sourceKind") == connector.get("mainKind") == connector.get("errorKind") == "jsonl"
            and connector.get("mode") == "finite" and connector.get("acceptedMainEffects") == expected_effects
            and connector.get("acceptedErrorEffects") == 0
            and sum(row["acceptedMainEffects"] for row in rows) == expected_effects
            and sum(row["acceptedErrorEffects"] for row in rows) == 0,
        "oneAcknowledgedResultOnServer": all(db[key] == count for key, count in
            {"runs": 1, "connectorRuns": 1, "partitions": 2, "terminals": 1, "outbox": 1, "acknowledged": 1, "correlated": 1}.items())
            and db["resultHash"] == legacy.sha256(db["resultJson"].encode())
            and db["payloadHash"] == legacy.sha256(("etl-result-envelope-v1|" + db["invocationJson"] + "|" + db["resultJson"]).encode())
            and db["deliveryId"] == legacy.sha256(("etl-delivery-v1|" + db["resultId"]).encode())
            and db["disposition"] in (0, 1, 2),
        "exactPhysicalOutput": before["output"]["records"] == legacy.RECORDS and len(before["output"]["files"]) == expected_effects
            and not before["output"]["errorFiles"],
        "newProcessSameRetainedRun": case["firstPid"] != case["secondPid"] and run == after["run"]
            and parts == after["partitions"] and db == after["database"] and before["output"] == after["output"]
            and facts == after_facts,
        "designPublicationRetained": case["reopenedDraft"]["lastPublishedVersion"] == 1
            and case["reopenedDraft"]["revision"] == case["publishedDraftRevision"]
            and [v["selection"] for v in case["versions"]["items"]] == [case["selection"]],
        "bothStoreTablesOnServer": {"elsa_etl_definition_drafts", "elsa_etl_published_artifacts", "elsa_etl_runtime_runs",
            "elsa_etl_connector_runs", "elsa_etl_connector_partitions", "elsa_etl_runtime_outbox", "elsa_etl_runtime_terminals"}.issubset(case["tables"]),
        "intendedExecutionProductsUnchanged": case["executedProducts"]["unchanged"] and case["executedProducts"]["fileCount"] > 0,
        "noGeneratedSecretInEvidence": not case["secretInEvidence"],
    }


def run_engine(provider: str, assembly: Path, output: Path) -> dict:
    database = host = root = None
    try:
        database = legacy.Database(provider)
        root = Path(tempfile.mkdtemp(prefix="elsa-etl-runtime-journey-"))
        # Nuplane writes its local store state beside the assembly. Give each
        # engine an owned execution copy so the verified published tree stays immutable.
        expected_products = {str(f.relative_to(assembly.parent)): legacy.sha256(f.read_bytes())
                             for f in assembly.parent.rglob("*") if f.is_file()}
        execution_directory = root / "product"
        shutil.copytree(assembly.parent, execution_directory)
        copied_products = {name: legacy.sha256((execution_directory / name).read_bytes()) for name in expected_products}
        if copied_products != expected_products:
            raise ValueError("The owned execution copy differs from the verified products.")
        execution_assembly = execution_directory / assembly.name
        for name in ("appsettings.json", "appsettings.Development.json", "shells.json"):
            shutil.copyfile(REPO / "Essentials/apps/Elsa.Workbench" / name, root / name)
        (root / "etl-snapshots").mkdir()
        data = "".join(json.dumps(record) + "\n" for record in legacy.RECORDS).encode()
        (root / "people.jsonl").write_bytes(data)
        bindings = [{"TenantId": "default", "PersistencePartition": "default", "Alias": "people-source", "Role": "Source",
                     "Revision": "v1", "Path": str(root / "people.jsonl"), "ExpectedSha256": legacy.sha256(data),
                     "IsCurrent": True, "ImmutableInput": True, "AtomicPublication": False}]
        for route, role in (("output", "MainOutput"), ("errors", "ErrorOutput")):
            (root / f"people-{route}").mkdir()
            bindings.append({"TenantId": "default", "PersistencePartition": "default", "Alias": f"people-{route}",
                "Role": role, "Revision": "v1", "Path": str(root / f"people-{route}"), "IsCurrent": True,
                "ImmutableInput": False, "AtomicPublication": True})
        shells = json.loads((root / "shells.json").read_text())
        design_cursor_key, runtime_cursor_key = secrets.token_hex(32), secrets.token_hex(32)
        shells["CShells"]["Shells"]["default"]["Features"].update({
            "EtlDesign": {}, "EtlDesignEntityFrameworkCore": {"Provider": provider, "ConnectionString": database.connection_string},
            "EtlDesignApi": {"CursorSigningKey": design_cursor_key},
            "EtlRuntime": {"SnapshotRoot": str(root / "etl-snapshots"), "FileBindings": bindings},
            "EtlRuntimeEntityFrameworkCore": {"Provider": provider, "ConnectionString": database.connection_string},
            "EtlRuntimeApi": {"CursorSigningKey": runtime_cursor_key},
            "EtlWorkflowsDesign": {}, "EtlWorkflowsRuntime": {}})
        (root / "shells.json").write_text(json.dumps(shells, indent=2))
        log_path = output / f"workbench-{provider.lower()}.log"
        host = legacy.Host(execution_assembly, root, log_path, 0)
        first_pid, api = host.process.pid, host.api
        definition = "runtime-" + uuid.uuid4().hex[:8]
        draft = api.request("/etl/v2/definitions", {"id": definition, "pipeline": legacy.pipeline()})
        validation = api.request(f"/etl/v2/definitions/{definition}/validate", {"expectedRevision": draft["revision"]})
        published = api.request(f"/etl/v2/definitions/{definition}/publish", {"expectedRevision": validation["revision"],
            "definitionVersion": validation["definitionVersion"], "contentHash": validation["contentHash"]})
        published_draft = api.request(f"/etl/v2/definitions/{definition}")
        selection = published["version"]["selection"]
        def node(identity, version, inputs=None, structure=None):
            return {"nodeId": identity, "activityVersionId": version, "inputs": inputs or [], "outputs": [], "structure": structure}
        flowchart = api.activity("Elsa.Activities.Flowchart.Activities.Flowchart")
        run_etl = api.activity("Elsa.Etl.Workflows.Runtime.Activities.RunEtl")
        write_line = api.activity("Elsa.Activities.Primitives.Activities.WriteLine")
        root_node = node("root", flowchart, structure={"kind": "elsa.flowchart.structure", "schemaVersion": "1.0.0", "payload": {
            "startNodeId": "etl", "activities": [node("etl", run_etl, [legacy.literal("Pipeline", selection)]),
                node("after-done", write_line, [legacy.literal("text", "ETL runtime journey succeeded")])],
            "connections": [{"source": {"nodeId": "etl", "port": "Done"}, "target": {"nodeId": "after-done", "port": "Done"}}]}})
        native = api.request("/design/workflows/definitions/submit", {"name": f"Spec 1206 {provider} {definition}",
            "state": {"variables": [], "inputs": [], "outputs": [], "rootActivity": root_node}})
        publication = api.request("/publishing/workflows/" + native["version"]["id"] + "/publish", {})
        started = api.request("/runtime/workflows/executables/" + publication["artifactId"] + "/execute",
                              {"sourceReferenceId": publication["sourceReferenceId"]})
        workflow_id = started["workflowExecutionId"]
        legacy.wait_for(lambda: api.request("/runtime/workflows/instances/" + urllib.parse.quote(workflow_id, safe="")),
            lambda view: view["instance"]["status"] in ("Completed", "Finished", "Faulted", "Cancelled"), "Native workflow did not finish.")
        runs = api.request("/etl/runs?limit=100")
        if len(runs["items"]) != 1 or runs["nextCursor"] is not None:
            raise ValueError("The fresh owned database must expose exactly one ETL run.")
        run_id = runs["items"][0]["runId"]
        # Native completion can precede the outbox acknowledgement by a short interval.
        legacy.wait_for(lambda: database_scalars(database), lambda value: value["acknowledged"] == 1, "Result was not acknowledged.")
        before = snapshot(api, database, root, run_id, workflow_id)
        host.stop()
        host = legacy.Host(execution_assembly, root, log_path, 0)
        after = snapshot(host.api, database, root, run_id, workflow_id)
        case = {"provider": provider, "stores": {"design": provider, "runtime": provider, "native": "Sqlite"},
                "definition": definition, "selection": selection, "publishedDraftRevision": published_draft["revision"],
                "nativeVersionId": native["version"]["id"], "nativePublicationArtifactId": publication["artifactId"],
                "nativeSourceReferenceId": publication["sourceReferenceId"], "workflowExecutionId": workflow_id, "runId": run_id,
                "firstPid": first_pid, "secondPid": host.process.pid, "inputSha256": legacy.sha256(data), "inputBytes": len(data),
                "before": before, "after": after, "reopenedDraft": host.api.request(f"/etl/v2/definitions/{definition}"),
                "versions": host.api.request(f"/etl/v2/definitions/{definition}/versions"), "tables": database.tables()}
        case["executedProducts"] = {"fileCount": len(expected_products),
            "manifestSha256": legacy.sha256(json.dumps(expected_products, sort_keys=True).encode()),
            "unchanged": all(legacy.sha256((execution_directory / name).read_bytes()) == digest
                             for name, digest in expected_products.items())}
        case["secretInEvidence"] = any(value in json.dumps(case) for value in (database.secret, design_cursor_key, runtime_cursor_key))
        return case
    finally:
        failure = sys.exc_info()[1]
        cleanup_failures = []
        for resource in (host, database, root):
            if resource is None:
                continue
            try:
                shutil.rmtree(resource) if isinstance(resource, Path) else resource.stop()
            except Exception as cleanup:
                cleanup_failures.append(type(cleanup).__name__)
        if cleanup_failures:
            message = "Owned journey cleanup failed (" + ", ".join(cleanup_failures) + ")."
            if failure is None:
                raise RuntimeError(message) from None
            if hasattr(failure, "add_note"):
                failure.add_note(message)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workbench-assembly", type=Path, required=True)
    parser.add_argument("--product-receipt", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--provider", action="append", choices=["PostgreSql", "SqlServer", "MySql"])
    args = parser.parse_args()
    assembly = args.workbench_assembly.resolve(strict=True)
    products = verify_product(assembly, args.product_receipt.resolve(strict=True))
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    summary = {"schemaVersion": 1, "spec": "1206-etl-runtime-concurrency",
        "harnessSourceRevision": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip(),
        "products": products, "engines": {}, "checks": {}, "passed": False,
        "proofScope": "Both ETL stores on server engines; native persistence SQLite. Completed-state restart, not mid-work/upgrade recovery or Studio."}
    for provider in args.provider or ["PostgreSql", "SqlServer", "MySql"]:
        case = summary["engines"][provider] = run_engine(provider, assembly, output)
        summary["checks"].update({provider + ":" + key: bool(value) for key, value in evaluate(case).items()})
        (output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
        print(json.dumps({"provider": provider, "checks": {k: v for k, v in summary["checks"].items() if k.startswith(provider + ":")}}), flush=True)
    summary["passed"] = bool(summary["checks"]) and all(summary["checks"].values())
    (output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    return 0 if summary["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
