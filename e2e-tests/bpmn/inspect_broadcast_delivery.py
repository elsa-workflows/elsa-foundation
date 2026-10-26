#!/usr/bin/env python3
"""Read-only legacy outbox preflight; the runtime provider validates each source.

Closed .sqlite.gz captures are immutable whole databases. Live SQLite sources are
backed up coherently with committed WAL data. No event is delivered or repaired.
"""
from __future__ import annotations

import argparse
from contextlib import closing
from datetime import datetime, timezone
import gzip
import hashlib
import json
from pathlib import Path
import sqlite3
import subprocess
import tempfile
import time
from typing import Any

VALIDATOR = Path(__file__).resolve().parents[2] / "tools/bpmn/DeliveryPreflight/bin/Debug/net10.0/DeliveryPreflight.dll"


def digest(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def inspect_database(source: Path, validator: Path = VALIDATOR) -> dict[str, Any]:
    started_at = datetime.now(timezone.utc).isoformat()
    source = source.resolve(strict=True)
    if not validator.is_file():
        raise ValueError("The checked runtime validator has not been built.")
    compressed = source.name.endswith(".sqlite.gz")
    with tempfile.TemporaryDirectory(prefix="elsa-broadcast-preflight-") as directory:
        copy = Path(directory) / "snapshot.sqlite"
        compressed_hash = None
        if compressed:
            raw = source.read_bytes()
            compressed_hash = digest(raw)
            copy.write_bytes(gzip.decompress(raw))
        else:
            with closing(sqlite3.connect(source.as_uri() + "?mode=ro", uri=True)) as live:
                live.execute("PRAGMA query_only=ON")
                with closing(sqlite3.connect(copy)) as destination:
                    deadline = time.monotonic() + 30
                    def bounded_backup(_status: int, _remaining: int, _total: int) -> None:
                        if time.monotonic() > deadline:
                            raise TimeoutError("A coherent read-only backup did not complete in time.")
                    live.backup(destination, pages=256, progress=bounded_backup)
        database_hash = digest(copy.read_bytes())
        with closing(sqlite3.connect(copy.as_uri() + "?mode=ro&immutable=1", uri=True)) as connection:
            connection.execute("PRAGMA query_only=ON")
            if connection.execute("PRAGMA quick_check").fetchall() != [("ok",)]:
                raise ValueError("Database integrity check failed.")
        run = subprocess.run(["dotnet", str(validator), str(copy), database_hash], capture_output=True, text=True, timeout=60)
        result = json.loads(run.stdout)
        if not isinstance(result, dict):
            raise ValueError("The checked runtime validator returned no result object.")
        if result.get("status") not in {"NoLegacyPublishResponsibilityOwed", "RecoveryRequired", "InventoryUnavailable"}:
            raise ValueError("The checked runtime validator returned an unsupported result.")
        if result["status"] != "InventoryUnavailable" and result.get("providerChecked") is not True:
            raise ValueError("Provider validation did not establish this inventory.")
        if (run.returncode == 0) != (result["status"] == "NoLegacyPublishResponsibilityOwed"):
            raise ValueError("The runtime validator exit status differs from its receipt.")
        if digest(copy.read_bytes()) != database_hash:
            raise ValueError("The private snapshot changed during provider validation.")
        assemblies = {name: digest((validator.parent / name).read_bytes()) for name in [
            "DeliveryPreflight.dll", "Elsa.Workflows.Runtime.Core.dll",
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll", "Elsa.Persistence.EntityFramework.dll"]}
        return {"sourceName": source.name,
                "observationStartedAtUtc": started_at,
                "observationCompletedAtUtc": datetime.now(timezone.utc).isoformat(),
                "sourceKind": "closed-compressed-capture" if compressed else "sqlite-backup-including-wal",
                "databaseSha256": database_hash, "compressedSha256": compressed_hash, "readerAssembliesSha256": assemblies, **result}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", action="append", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--source-revision", default="unspecified")
    parser.add_argument("--validator", type=Path, default=VALIDATOR)
    options = parser.parse_args()
    stores = []
    for path in options.database:
        try:
            stores.append(inspect_database(path, options.validator))
        except (OSError, ValueError, EOFError, sqlite3.Error, subprocess.SubprocessError) as error:
            stores.append({"sourceName": path.name, "status": "InventoryUnavailable",
                           "failureType": type(error).__name__, "providerChecked": False,
                           "reason": "The supplied store could not be classified safely; responsibility is unresolved."})
    receipt = {"schemaVersion": 1, "purpose": "FR-015 actual-store legacy inventory only",
               "sourceRevision": options.source_revision,
               "status": "RecoveryRequired" if any(row["status"] != "NoLegacyPublishResponsibilityOwed" for row in stores)
               else "NoLegacyPublishResponsibilityOwed",
               "limitations": ["Only supplied stores are inventoried; no production-wide or mixed-writer quiescence claim.",
                               "Attempt zero is not evidence that no recipient effect occurred.",
                               "This does not qualify cohort dispatch or upgraded-host recovery."], "stores": stores}
    wire = json.dumps(receipt, indent=2) + "\n"
    if options.output:
        options.output.parent.mkdir(parents=True, exist_ok=True)
        options.output.write_text(wire)
    print(wire, end="")
    return 2 if receipt["status"] == "RecoveryRequired" else 0


if __name__ == "__main__":
    raise SystemExit(main())
