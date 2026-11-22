#!/usr/bin/env python3
"""One real runner attempt; observe stopped private stores without altering runtime behavior."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import sqlite3
import subprocess
import sys
import tempfile


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def stores(directory):
    return sorted(path for path in directory.rglob("*") if path.is_file()
                  and re.search(r"\.(db|sqlite|sqlite3)(-wal|-shm|-journal)?$", path.name))


def identifier(value):
    return '"' + value.replace('"', '""') + '"'


def snapshot(directory, private_root):
    raw, inspection = private_root / "raw", private_root / "inspection"
    raw.mkdir(parents=True, exist_ok=False)
    result = {"files": [], "stores": [], "errors": []}
    # Copy every main file and sidecar before opening any SQLite connection.
    for source in stores(directory):
        relative = source.relative_to(directory)
        try:
            if source.is_symlink():
                raise RuntimeError("Unexpected symlink in an owned store")
            target = raw / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            before = digest(source)
            shutil.copy2(source, target)
            copied, after = digest(target), digest(source)
            if before != copied or before != after:
                raise RuntimeError("Stopped store changed during copy")
            result["files"].append({"path": str(relative), "bytes": target.stat().st_size, "sha256": copied})
        except Exception as error:
            result["errors"].append({"path": str(relative), "phase": "copy", "error": str(error)})
    if not result["files"]:
        result["errors"].append({"phase": "copy", "error": "No owned SQLite stores present"})
    shutil.copytree(raw, inspection)
    for database in stores(inspection):
        if database.suffix not in (".db", ".sqlite", ".sqlite3"):
            continue
        relative = database.relative_to(inspection)
        facts = {"path": str(relative), "tables": [], "migrations": {}, "locks": {}, "errors": []}
        try:
            # Ordinary opening permits WAL/SHM bookkeeping on this disposable inspection
            # copy. SQL is query-only; original stores and the frozen raw copies stay untouched.
            with sqlite3.connect(str(database), timeout=5) as connection:
                connection.execute("PRAGMA query_only=ON")
                names = [row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")]
                for name in names:
                    try:
                        columns = [{"name": row[1], "type": row[2], "notNull": row[3], "primaryKey": row[5]}
                                   for row in connection.execute("PRAGMA table_info(" + identifier(name) + ")")]
                        count = connection.execute("SELECT count(*) FROM " + identifier(name)).fetchone()[0]
                        facts["tables"].append({"name": name, "columns": columns, "count": count})
                        keys = {column["name"] for column in columns}
                        if {"MigrationId", "ProductVersion"} <= keys:
                            facts["migrations"][name] = connection.execute(
                                'SELECT "MigrationId", "ProductVersion" FROM ' + identifier(name) + ' ORDER BY "MigrationId"').fetchall()
                        if "migrationslock" in name.lower():
                            allowed = sorted(keys & {"Id", "Timestamp"})
                            facts["locks"][name] = {"count": count, "columns": allowed, "rows": connection.execute(
                                "SELECT " + ", ".join(map(identifier, allowed)) + " FROM " + identifier(name)).fetchall() if allowed else []}
                    except Exception as error:
                        facts["errors"].append({"table": name, "error": str(error)})
        except Exception as error:
            facts["errors"].append({"error": str(error)})
        result["stores"].append(facts)
    if not result["stores"]:
        result["errors"].append({"phase": "inspect", "error": "No main SQLite store was inspected"})
    return result


def self_test():
    with tempfile.TemporaryDirectory(prefix="playback-observer-wal-control-") as temporary:
        root = Path(temporary)
        source = root / "source"
        source.mkdir()
        with sqlite3.connect(source / "control.db") as writer:
            writer.execute("CREATE TABLE observer_control(value INTEGER)")
            writer.commit()
            writer.execute("PRAGMA journal_mode=WAL")
            writer.execute("PRAGMA wal_autocheckpoint=0")
            writer.execute("INSERT INTO observer_control VALUES(1)")
            writer.commit()
            assert (source / "control.db-wal").stat().st_size > 0
            observed = snapshot(source, root / "private")
            assert not observed["errors"]
            tables = observed["stores"][0]["tables"]
            assert next(table["count"] for table in tables if table["name"] == "observer_control") == 1
            with sqlite3.connect((root / "private/raw/control.db").as_uri() + "?immutable=1", uri=True) as immutable:
                assert immutable.execute("SELECT count(*) FROM observer_control").fetchone()[0] == 0
            assert all(digest(root / "private/raw" / row["path"]) == row["sha256"] for row in observed["files"])
    print("Synthetic observer control passed: WAL-aware copy sees1 committed row; immutable-only sees0; raw hashes unchanged. No runtime evidence substituted.")


def attempt(arguments):
    root, output, private = map(lambda value: Path(value).resolve(), (arguments.repo, arguments.output, arguments.private))
    if private.is_relative_to(output) or output.is_relative_to(private) or private.is_relative_to(root):
        raise ValueError("Private stores must be outside the uploaded output and source tree")
    if output.exists() or private.exists():
        raise ValueError("Use new output and private-store directories for this single attempt")
    private.mkdir(parents=True)
    driver_bytes = Path(__file__).read_bytes()
    spec = importlib.util.spec_from_file_location("playback_actual_runner", root / "tools/ux-acceptance/run.py")
    runner = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = runner
    spec.loader.exec_module(runner)
    original_temp, original_popen, original_prepare = tempfile.TemporaryDirectory, subprocess.Popen, runner.prepare_host
    owned, observations = [], []
    metadata = {"kind": "observer-only-real-runner", "driverSha256": digest(Path(__file__)), "sourceBefore": runner.source_state(root), "runnerReturned": False,
                "preCopy": [], "observations": observations, "errors": []}
    if metadata["sourceBefore"] != (arguments.head, ""):
        raise ValueError("Expected exact clean source before the attempt")

    def start(*args, **kwargs):
        process = original_popen(*args, **kwargs)
        command = args[0] if args else kwargs.get("args", [])
        if isinstance(command, (list, tuple)) and "--contentRoot" in command and kwargs.get("start_new_session"):
            owned.append(process)
        return process

    def prepare(project, destination):
        original_prepare(project, destination)
        present = [str(path.relative_to(destination)) for path in stores(destination)]
        metadata["preCopy"].append({"destination": destination.name, "copiedStores": present})
        if present:
            raise RuntimeError("A supposedly fresh owned host contains copied stores")

    class ObserveTemporaryDirectory:
        def __init__(self, *args, **kwargs):
            self.inner = original_temp(*args, **kwargs)
            self.capture = kwargs.get("prefix") == "elsa-ux-acceptance-"

        def __enter__(self):
            value = self.inner.__enter__()
            self.directory = Path(value)
            if self.capture:
                metadata["freshTemporaryDirectoryEmpty"] = not any(self.directory.iterdir())
                if not metadata["freshTemporaryDirectoryEmpty"]:
                    raise RuntimeError("Expected a new empty private temporary directory")
            return value

        def __exit__(self, *exception):
            try:
                if self.capture:
                    stopped = []
                    for process in owned:
                        try:
                            os.killpg(process.pid, 0)
                            group_exists = True
                        except ProcessLookupError:
                            group_exists = False
                        stopped.append({"pid": process.pid, "returncode": process.poll(), "groupExists": group_exists})
                    metadata["ownedProcessesAfterStop"] = stopped
                    if any(row["returncode"] is None or row["groupExists"] for row in stopped):
                        raise RuntimeError("Owned host/group still active; do not inspect or copy its stores")
                    observations.append(snapshot(self.directory, private / "stopped-hosts"))
            except Exception as error:
                metadata["errors"].append({"phase": "stopped-observer", "error": str(error)})
            finally:
                return self.inner.__exit__(*exception)

    def terminate(_signal, _frame):
        raise KeyboardInterrupt("SIGTERM")

    previous_term = signal.signal(signal.SIGTERM, terminate)
    tempfile.TemporaryDirectory, subprocess.Popen, runner.prepare_host = ObserveTemporaryDirectory, start, prepare
    try:
        runner.run(output, arguments.journey, None)
        metadata["runnerReturned"] = True
    except BaseException as error:
        metadata["runnerReturned"] = False
        metadata["errors"].append({"phase": "runner", "errorType": type(error).__name__, "error": str(error)})
    finally:
        tempfile.TemporaryDirectory, subprocess.Popen, runner.prepare_host = original_temp, original_popen, original_prepare
        output.mkdir(parents=True, exist_ok=True)
        (output / "store-observer-driver.py").write_bytes(driver_bytes)
        metadata["sourceAfter"] = runner.source_state(root)
        metadata["sourceFence"] = metadata["sourceBefore"] == metadata["sourceAfter"]
        metadata["driverFence"] = metadata["driverSha256"] == digest(Path(__file__))
        metadata["cleanupFence"] = all(row["returncode"] == 0 and not row["groupExists"] for row in metadata.get("ownedProcessesAfterStop", []))
        metadata["inspectionErrors"] = sum(len(item["errors"]) + sum(len(store["errors"]) for store in item["stores"]) for item in observations)
        metadata["accepted"] = metadata["runnerReturned"] and metadata["sourceFence"] and metadata["driverFence"] and metadata["cleanupFence"] and not metadata["errors"] and not metadata["inspectionErrors"] and len(observations) == 1
        (output / "store-observation.json").write_text(json.dumps(metadata, indent=2) + "\n")
        signal.signal(signal.SIGTERM, previous_term)
    print(json.dumps({key: metadata[key] for key in ["runnerReturned", "accepted", "inspectionErrors", "sourceFence", "driverFence", "cleanupFence"]}))
    return 0 if metadata["accepted"] else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--repo")
    parser.add_argument("--output")
    parser.add_argument("--private")
    parser.add_argument("--head")
    parser.add_argument("--journey", action="append", help="Select a real journey; repeat for the full feature campaign")
    options = parser.parse_args()
    if options.self_test:
        self_test()
    else:
        if not all((options.repo, options.output, options.private, options.head)):
            parser.error("--repo, --output, --private and --head are required")
        sys.exit(attempt(options))
