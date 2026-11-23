#!/usr/bin/env python3
"""Run the relational stress workload through abrupt SQLite owner process loss."""

import argparse
from contextlib import closing
from pathlib import Path
import signal
import sqlite3
import subprocess
import tempfile
import time


PROJECT = Path(__file__).resolve().parent
RUNNER = PROJECT / "bin/Release/net10.0/Kernel.Ef.Run.dll"


def run(mode, output):
    output.mkdir()
    with tempfile.TemporaryDirectory(prefix="sqlite-owner-loss-") as temporary:
        database = Path(temporary) / "kernel.db"
        stop_base = Path(temporary) / "run"
        connection = f"Data Source={database};Default Timeout=5"
        command = [
            "dotnet", str(RUNNER), "x", "stress", "sqlite", connection,
            f"fence={mode}", "commit=owned", "kill=0", "wal=1", "partitions=2",
            "seconds=40", "messages=5000", f"signal={stop_base}",
        ]
        processes = []

        def start(log_name, *options):
            with (output / f"{log_name}.log").open("w") as log:
                child = subprocess.Popen([*command, *options], stdout=log, stderr=subprocess.STDOUT)
            processes.append(child)
            return child

        def partitions():
            with closing(sqlite3.connect(database.as_uri() + "?mode=ro", uri=True, timeout=1)) as store:
                return store.execute('SELECT "Id", "Owner", "Epoch", "Position" FROM "Partitions" ORDER BY "Id"').fetchall()

        def observe(description, condition, *live):
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                for process in live:
                    if process.poll() is not None:
                        raise RuntimeError(f"Fixture process {process.pid} exited early with {process.returncode}.")
                try:
                    rows = partitions()
                except sqlite3.OperationalError:
                    rows = []
                if condition(rows):
                    return rows
                time.sleep(0.05)
            raise TimeoutError(description)

        try:
            producer = start("producer", "nodes=0")
            observe("The producer did not initialize both partitions.", lambda rows: len(rows) == 2, producer)
            owner = start("owner", "role=workers", "nodes=1", "name=owner-")
            initial = observe(
                "The first process did not advance both partitions.",
                lambda rows: len(rows) == 2 and all(str(name).startswith("owner-") and position > 0
                                                   for _, name, _, position in rows),
                producer, owner,
            )
            print(f"{mode}: owner process {owner.pid} advanced both partitions: {initial}", flush=True)
            owner.kill()
            if owner.wait(timeout=10) != -signal.SIGKILL:
                raise RuntimeError("The original fixture owner did not exit abruptly.")

            before = observe(
                "The durable partition rows are missing after the first process exited.",
                lambda rows: len(rows) == 2 and all(
                    partition == previous and name == previous_name and epoch >= old_epoch and position >= old_position
                    for (partition, name, epoch, position), (previous, previous_name, old_epoch, old_position)
                    in zip(rows, initial)),
                producer,
            )
            print(f"{mode}: durable positions after owner exit: {before}", flush=True)
            successor = start("successor", "role=workers", "nodes=1", "name=successor-")
            after = observe(
                "The successor did not retake both partitions and advance their journals.",
                lambda rows: len(rows) == 2 and all(
                    partition == previous and str(name).startswith("successor-") and epoch > old_epoch and position > old_position
                    for (partition, name, epoch, position), (previous, _, old_epoch, old_position) in zip(rows, before)),
                producer, successor,
            )
            print(f"{mode}: successor process {successor.pid} retook and advanced both partitions: {after}", flush=True)

            if producer.wait(timeout=300) != 0 or successor.wait(timeout=10) != 0:
                raise RuntimeError(f"Stress or the surviving worker failed. See logs in {output}.")
            with closing(sqlite3.connect(database.as_uri() + "?mode=ro", uri=True)) as store:
                integrity = store.execute("PRAGMA integrity_check").fetchall()
            if integrity != [("ok",)]:
                raise RuntimeError(f"SQLite integrity check failed: {integrity}")
            print(f"{mode}: 5,000 inputs passed the stress invariants and SQLite integrity_check.", flush=True)
        finally:
            for process in processes:
                if process.poll() is None:
                    process.kill()
            for process in processes:
                process.wait(timeout=10)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("checked", "batch"))
    parser.add_argument("output", type=Path, help="New directory for the separate process logs.")
    arguments = parser.parse_args()
    if not hasattr(signal, "SIGKILL"):
        parser.error("This fixture requires POSIX process exit status.")
    if arguments.output.exists():
        parser.error(f"Log directory already exists: {arguments.output}")
    subprocess.run(["dotnet", "restore", "--locked-mode"], cwd=PROJECT, check=True)
    subprocess.run(["dotnet", "build", "-c", "Release", "--no-restore"], cwd=PROJECT, check=True)
    run(arguments.mode, arguments.output)


if __name__ == "__main__":
    main()
