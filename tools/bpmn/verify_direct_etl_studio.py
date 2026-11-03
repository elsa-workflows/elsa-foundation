#!/usr/bin/env python3
"""Read-only physical acceptance of the owned direct BPMN/ETL Studio recovery journey."""
from __future__ import annotations
import argparse
import base64
import gzip
import hashlib
import json
from pathlib import Path
import sqlite3
import tempfile


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(root, name):
    return json.loads((root / name).read_text())


def decoded(value):
    if isinstance(value, str) and value.startswith("u16:"):
        return base64.b64decode(value[4:], validate=True).decode("utf-16le")
    if isinstance(value, dict):
        return {key: decoded(item) for key, item in value.items()}
    if isinstance(value, list):
        return [decoded(item) for item in value]
    return value


def opaque(value):
    return base64.b64decode(value, validate=True).decode("utf-16le")


def verify(directory):
    root = Path(directory).resolve()
    process = read(root, "process.json")
    browser = read(root, "browser-result.json")
    require(browser.get("passed") is True and browser.get("errors") == [], "The actual browser journey did not pass cleanly.")
    source = process["sourceRevision"]
    require(source == browser["productSource"] == process["hostProducts"]["sourceCommit"] == process["studioProducts"]["sourceCommit"], "Product sources differ.")
    roles = process["processes"]
    hosts = [item for item in roles if item["role"] == "workbench"]
    require(len(hosts) == 3 and len({item["processId"] for item in hosts}) == 3 and len({item["assemblySha256"] for item in hosts}) == 1, "The journey lacks two actual restarts of the unchanged host.")
    expected = ("recovered", "held-before-restart", "restarted-held", "completed", "restarted-completed", "finished")
    require(tuple(process["stages"]) == expected, "A required whole-store boundary is absent.")
    databases = 0
    with tempfile.TemporaryDirectory(prefix="elsa-direct-etl-verify-") as temporary:
        for stage, stores in process["stages"].items():
            require(set(stores) == {"runtime", "design", "etl-runtime", "etl-design"}, "A coupled workflow/ETL store is absent.")
            for name, asset in stores.items():
                compressed = root / asset["file"]
                require(compressed.parent == root and digest(compressed) == asset["gzipSha256"], "A captured store differs from its receipt.")
                raw = gzip.decompress(compressed.read_bytes())
                require(hashlib.sha256(raw).hexdigest() == asset["databaseSha256"] == asset["observer"]["snapshotSha256"] and asset["observer"]["readOnly"] is True, "A provider snapshot differs or was not read-only.")
                path = Path(temporary) / f"{stage}-{name}.sqlite"
                path.write_bytes(raw)
                with sqlite3.connect(path.as_uri() + "?mode=ro&immutable=1", uri=True) as connection:
                    require(connection.execute("PRAGMA integrity_check").fetchall() == [("ok",)], "A physical store is corrupt.")
                databases += 1
        def connection(stage, store):
            value = sqlite3.connect((Path(temporary) / f"{stage}-{store}.sqlite").as_uri() + "?mode=ro&immutable=1", uri=True)
            value.row_factory = sqlite3.Row
            return value
        held = read(root, "held-before-restart-typed-read.json")["models"]
        restarted = read(root, "restarted-held-typed-read.json")["models"]
        final = read(root, "finished-typed-read.json")["models"]
        require(len(held) == len(restarted) == len(final) == 1, "The isolated fixture must have one direct model.")
        held, restarted, final = held[0], restarted[0], final[0]
        require(held == restarted, "Host restart changed the held state, receipts or native work.")
        h, f = held["model"], final["model"]
        require(h["lifecycle"] == 1 and h["revision"] == 2 and f["lifecycle"] == 3 and f["revision"] == 4 and f["terminalResult"] == {"status": "Completed", "outcome": "Done"}, "The physical model did not hold then complete once.")
        require(all(h[key] == f[key] for key in ("executionId", "scope", "tenantId", "authority", "executable", "materialIncarnationId")), "Model authority or immutable pin changed.")
        require(h["payload"]["sourceReferenceId"] == f["payload"]["sourceReferenceId"] and len(f["payload"]["nativeResults"]) == 2 and h["payload"]["nativeResults"][0] in f["payload"]["nativeResults"], "Original failure/source was lost or extra attempts appeared.")
        origins = [result["origin"] for result in f["payload"]["nativeResults"]]
        require(len({item["workerExecutionId"] for item in origins}) == 2 and len({item["attemptId"] for item in origins}) == 2 and origins[0]["workerExecutable"] == origins[1]["workerExecutable"], "The replacement does not retain the exact worker pin.")
        retry = [item for item in final["inputs"] if item["inputKind"] == "retry-service"]
        require(len(final["inputs"]) == 4 and len(retry) == 1 and retry[0]["acceptedRevision"] == 3 and retry[0]["result"]["status"] == "Accepted", "Retry replay created another accepted input or revision.")
        for stage in ("completed", "restarted-completed"):
            require(read(root, stage + "-typed-read.json")["models"] == [final], "Completion/replay changed the physical model or receipts.")
        cut = read(root, "accepted-retry-response-cut.json")
        require(cut == browser["lostResponse"] and cut["status"] == 200 and cut["body"]["pending"] is False and cut["body"]["result"]["status"] == "Accepted" and cut["request"]["requestId"] == cut["body"]["inputId"] == retry[0]["inputId"], "A real accepted response was not cut for the exact retained retry.")
        replayed = [event for event in browser["events"] if event["path"].endswith("/retry-service")]
        require(len(replayed) == 2 and all(event["request"] == cut["request"] and event["response"]["replay"] is True and event["response"]["result"] == cut["body"]["result"] for event in replayed), "Reload/restart did not replay the exact accepted command.")
        failure = f["payload"]["failures"]
        require(len(failure) == 1 and failure[0]["status"] == "Retried" and failure[0]["resolutionInputId"] == retry[0]["inputId"] and failure[0]["attemptNumber"] == 1 and failure[0]["maxAttempts"] == 2 and failure[0]["retryAttemptId"] != failure[0]["failedAttemptId"], "The held incident/budget/history changed.")
        with connection("finished", "runtime") as c:
            rows = [json.loads(row[0]) for row in c.execute("SELECT ContentJson FROM elsa_runtime_workflow_execution_state")]
            require(len(rows) == 2 and {opaque(row["workflowExecutionId"]) for row in rows} == {item["workerExecutionId"] for item in origins} and {row["status"] for row in rows} == {"Faulted", "Completed"}, "Physical native workers differ from the two recorded attempts.")
            require(all({key: opaque(value) for key, value in row["pinnedExecutable"].items()} == origins[0]["workerExecutable"] for row in rows), "A physical native worker changed its immutable pin.")
        with connection("finished", "etl-runtime") as c:
            runs = list(c.execute("SELECT * FROM elsa_etl_runtime_runs"))
            require(len(runs) == 2 and {row["Status"] for row in runs} == {2, 4}, "Expected one succeeded ETL run and its retained original fault.")
            invocations = [decoded(json.loads(row["InvocationJson"])) for row in runs]
            require({item["resultAddress"]["workflowExecutionId"] for item in invocations} == {item["workerExecutionId"] for item in origins} and len({item["runId"] for item in invocations}) == 2, "ETL results do not address the exact native occurrences.")
            require(all(item["artifact"] == invocations[0]["artifact"] and item["artifact"]["definitionVersion"] == 1 for item in invocations), "Accepted work drifted to a newer ETL pipeline.")
            require(all(row["OutstandingRecords"] == row["OutstandingBytes"] == 0 for row in runs), "ETL capacity remains owed.")
            terminals = [decoded(json.loads(row[0])) for row in c.execute("SELECT ResultJson FROM elsa_etl_runtime_terminals")]
            require({item["outcome"] for item in terminals} == {"faulted", "succeeded"} and any(item.get("diagnostic", {}).get("code") == "etl.snapshot-read-failed" for item in terminals if item.get("diagnostic")) and any(item["outcome"] == "succeeded" and item["acceptedRecords"] == 4 for item in terminals), "Physical ETL failure/success evidence is missing.")
            for row in runs:
                partitions = list(c.execute("SELECT Completed FROM elsa_etl_connector_partitions WHERE RunKey=?", (row["RunKey"],)))
                require(len(partitions) == 2 and (row["Status"] != 2 or all(item[0] for item in partitions)), "The actual two-partition journey is incomplete.")
            outbox = list(c.execute("SELECT Disposition,AcknowledgedAtUtcTicks FROM elsa_etl_runtime_outbox"))
            require(len(outbox) == 2 and all(row[0] in (0, 1, 2) and row[1] is not None for row in outbox), "ETL terminal deliveries lack an acknowledged application/terminal proof.")
        with connection("finished", "etl-design") as c:
            versions = list(c.execute("SELECT DefinitionVersion,PipelineHash FROM elsa_etl_published_artifacts"))
            require(len(versions) == 2 and {row[0] for row in versions} == {1, 2} and len({row[1] for row in versions}) == 2, "A changed newer ETL version was not actually published.")
    manifests = {}
    for stage in expected:
        assets = read(root, stage + "-etl-files.json")["files"]
        manifests[stage] = {item["file"]: item for item in assets}
        for item in assets:
            path = root / (stage + "-etl-files") / item["file"]
            require(path.resolve().is_relative_to(root) and path.stat().st_size == item["bytes"] and digest(path) == item["sha256"], "A physical destination/snapshot differs from the captured bytes.")
    before = {name: value for name, value in manifests["held-before-restart"].items() if name.startswith("main-output/")}
    require(before and all(manifests["finished"].get(name) == value for name, value in before.items()), "External effects before failure were removed or overwritten.")
    newer = set(name for name in manifests["finished"] if name.startswith("main-output/")) - set(before)
    records = [json.loads(line) for name in newer for line in (root / "finished-etl-files" / name).read_text().splitlines()]
    require(sorted(record["name"] for record in records) == ["Ada", "Grace", "Linus", "Margaret"], "The replacement's actual JSONL records are incomplete or duplicated.")
    http = read(root, "finished-http-effects.json")
    require(not http["overflow"] and http["sourceHeld"] is False and http["heldLookup"] and http["effectsBeforeFailure"], "The physical lookup fault/restore lacks retained external effects.")
    lookup = http["heldLookup"]
    require(any(name.endswith("/" + Path(lookup["originalPath"]).name) and item["sha256"] == lookup["sha256"] for name, item in manifests["finished"].items()), "The identical retained CSV lookup was not restored.")
    for image in ("ui-held-fresh-browser", "ui-original-native-attempt", "ui-original-etl-fault", "ui-retry-response-lost", "ui-parent-completed", "ui-completed-replay"):
        require((root / (image + ".png")).read_bytes().startswith(b"\x89PNG\r\n\x1a\n"), "A required actual Studio observation is absent.")
    return {"readOnly": True, "passed": True, "productSource": source, "controllerSource": process["harnessSourceRevision"], "driverSource": browser["driverSource"], "wholeStoresVerified": databases, "directModels": 1, "nativeAttempts": 2, "etlRuns": 2, "pipelineVersionRetained": 1, "newerPipelineVersionPublished": 2, "replacementRecords": 4, "originalOutputFilesRetained": len(before), "retryAcceptedInputs": 1, "etlDeliveryDispositions": [row[0] for row in outbox], "modelTerminalRevision": 4, "responseLossReloadAndRestartReplay": True, "claimsInterruptionFinalAndWiderAcceptance": False}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    print(json.dumps(verify(parser.parse_args().directory), indent=2))
