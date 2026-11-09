#!/usr/bin/env python3
"""Exercise the checked reader against a real closed capture and private invalid copies."""
import argparse
import base64
from contextlib import closing
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import sqlite3
import subprocess
import tempfile


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def freeze_private_copy(database):
    closed = database.with_name("closed-" + database.name)
    closed.open("xb").close()
    with closing(sqlite3.connect("file:" + str(database) + "?mode=ro", uri=True)) as source, closing(sqlite3.connect(closed)) as target:
        source.backup(target)
    return closed


def create_orphan(database):
    """The canonical Delivered row stays intact; only a new orphan row is deliberately invalid."""
    with closing(sqlite3.connect(database)) as connection:
        connection.row_factory = sqlite3.Row
        row = next(dict(candidate) for candidate in connection.execute("SELECT * FROM elsa_runtime_post_commit_outbox ORDER BY Id")
                   if json.loads(candidate["ContentJson"])["status"] == "Delivered")
        canonical_id, canonical_content = row["Id"], row["ContentJson"]
        if json.loads(canonical_content)["status"] != "Delivered":
            raise RuntimeError("The orphan control requires an actual Delivered canonical source.")
        row["Id"] = hashlib.sha256(b"called-reader-orphan-physical-id").hexdigest()
        row["OutboxItemIdHash"] = hashlib.sha256(b"called-reader-orphan-lookup-hash").hexdigest()
        wire = json.loads(row["ContentJson"])
        wire["status"] = "Pending"
        wire["deliveredAt"] = None
        wire["intent"]["kind"] = base64.b64encode("Elsa.Activities.DispatchWorkflow.NotifyControl".encode("utf-16-le")).decode()
        row["ContentJson"] = json.dumps(wire, separators=(",", ":"))
        row["Status"] = 0
        columns = list(row)
        connection.execute(
            'INSERT INTO elsa_runtime_post_commit_outbox (' + ','.join('"' + name + '"' for name in columns) + ') VALUES (' + ','.join('?' for _ in columns) + ')',
            [row[name] for name in columns],
        )
        connection.commit()
        if tuple(connection.execute("SELECT ContentJson FROM elsa_runtime_post_commit_outbox WHERE Id=?", (canonical_id,)).fetchone()) != (canonical_content,):
            raise RuntimeError("The canonical source changed while creating its private orphan.")
    return freeze_private_copy(database)


def create_declared_capability(database):
    """A private variant acquires a valid metadata declaration; the real old capture remains unchanged."""
    encode = lambda value: base64.b64encode(value.encode("utf-16-le")).decode()
    with closing(sqlite3.connect(database)) as connection:
        identity, text = connection.execute("SELECT Id, ContentJson FROM elsa_runtime_workflow_dispatch ORDER BY Id LIMIT 1").fetchone()
        wire = json.loads(text)
        wire["metadata"][encode("runtime.childControlConsumerKey")] = encode("elsa.bpmn")
        wire["metadata"][encode("runtime.childControlConsumerVersion")] = encode("1")
        connection.execute("UPDATE elsa_runtime_workflow_dispatch SET ContentJson=? WHERE Id=?", (json.dumps(wire, separators=(",", ":")), identity))
        connection.commit()
    return freeze_private_copy(database)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reader-assembly", type=Path, required=True)
    parser.add_argument("--database", type=Path, required=True)
    parser.add_argument("--sha256", required=True, help="Expected uncompressed complete runtime database hash.")
    parser.add_argument("--capture", type=Path, required=True)
    parser.add_argument("--scope", default="default")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    original_bytes = args.database.read_bytes()
    raw = gzip.decompress(original_bytes) if args.database.suffix == ".gz" else original_bytes
    if hashlib.sha256(raw).hexdigest() != args.sha256:
        raise ValueError("The supplied real capture differs from its expected hash.")
    records = []
    with tempfile.TemporaryDirectory(prefix="elsa-called-reader-controls-") as temporary:
        root = Path(temporary)
        baseline = root / "baseline.sqlite"
        baseline.write_bytes(raw)

        def run(name, database, capture, expected, expected_hash=None):
            if any(Path(str(database) + suffix).exists() for suffix in ("-wal", "-shm", "-journal")):
                raise RuntimeError("A private fixture connection or sidecar is still live before inspection.")
            with closing(sqlite3.connect("file:" + str(database) + "?mode=ro&immutable=1", uri=True)) as connection:
                if connection.execute("PRAGMA integrity_check").fetchone() != ("ok",):
                    raise RuntimeError("A private fixture lost SQLite integrity.")
            before = digest(database)
            result = subprocess.run(
                ["dotnet", str(args.reader_assembly.resolve()), "--inspect-called", str(database), expected_hash or before,
                 args.scope, str(capture.resolve()), "captured"], capture_output=True, text=True, check=False,
            )
            try:
                observed = json.loads(result.stdout)
            except json.JSONDecodeError as error:
                raise RuntimeError("Reader did not produce its bounded JSON result.") from error
            if result.returncode != expected or observed.get("providerChecked") != (expected == 0):
                raise RuntimeError(f"Reader control {name} returned unexpected status {result.returncode}.")
            if result.stderr or digest(database) != before:
                raise RuntimeError("Reader leaked diagnostics or changed a closed snapshot.")
            if expected == 0:
                if not observed.get("readOnly") or observed.get("snapshotSha256") != before or len(observed.get("calledWorkflows", [])) != 2:
                    raise RuntimeError("The real positive capture was not completely checked.")
            elif observed.get("status") != "ObservationUnavailable" or observed.get("failureType") != "InvalidDataException":
                raise RuntimeError("Invalid fixture did not fail through the expected checked boundary.")
            records.append({"name": name, "exitCode": result.returncode, "databaseSha256": before,
                            "outputSha256": hashlib.sha256(result.stdout.encode()).hexdigest(), "unchanged": True})

        run("actual-frozen-capture", baseline, args.capture, 0)
        run("wrong-snapshot-hash", baseline, args.capture, 2, "0" * 64)
        foreign = json.loads(args.capture.read_text())
        foreign["cases"][0]["parentAtCapture"]["instance"]["artifactHash"] = "sha256:foreign-capture-pin"
        foreign_path = root / "foreign-capture.json"
        foreign_path.write_text(json.dumps(foreign))
        run("foreign-captured-pin", baseline, foreign_path, 2)
        orphan = root / "orphan.sqlite"
        shutil.copy2(baseline, orphan)
        orphan = create_orphan(orphan)
        run("owed-orphan-hidden-behind-canonical-source", orphan, args.capture, 2)
        declared = root / "declared.sqlite"
        shutil.copy2(baseline, declared)
        declared = create_declared_capability(declared)
        run("legacy-dispatch-acquires-control-declaration", declared, args.capture, 2)
        if args.database.read_bytes() != original_bytes:
            raise RuntimeError("The original capture changed.")
    args.output.write_text(json.dumps({"passed": True, "controls": records,
                                      "originalCaptureUnchanged": True, "scope": args.scope}, indent=2) + "\n")
    print("Actual capture accepted; wrong hash, foreign pin, owed orphan and declared capability refused without writes or diagnostic leakage.")


if __name__ == "__main__":
    main()
