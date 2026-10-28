#!/usr/bin/env python3
"""Observe real owned-host SQLite snapshots through checked runtime readers; never modifies source data."""
from __future__ import annotations
import argparse, base64, hashlib, json, shutil, sqlite3, subprocess, tempfile, time
from contextlib import closing
from pathlib import Path


def copy_snapshot(source: sqlite3.Connection, destination: Path) -> None:
    deadline = time.monotonic() + 30
    def progress(_status: int, _remaining: int, _total: int) -> None:
        if time.monotonic() > deadline:
            raise TimeoutError("The coherent SQLite snapshot did not finish within its bound.")
    with closing(sqlite3.connect(destination)) as target:
        source.backup(target, pages=256, progress=progress)
    with closing(sqlite3.connect(destination.as_uri() + "?mode=ro&immutable=1", uri=True)) as verify:
        if verify.execute("pragma quick_check").fetchall() != [("ok",)]:
            raise ValueError("The closed snapshot failed integrity verification.")


def read_snapshot(path: Path, reader: Path, marker: dict, extras: list[str], publisher: str | None = None) -> dict:
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    run = subprocess.run(["dotnet", str(reader), *(["--find-publisher-source"] if publisher else []),
                          str(path), digest, marker["scope"], publisher or marker["sourceOutboxItemId"], *extras],
                         text=True, capture_output=True, timeout=60)
    result = json.loads(run.stdout)
    if run.returncode or result.get("providerChecked") is not True:
        raise ValueError("The actual runtime provider refused the recovery observation: " + str(result.get("failureType")))
    if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
        raise ValueError("The private snapshot changed while being read.")
    return result


def retain_snapshot(source: Path, destination: Path) -> dict:
    """Persist the already checked closed input, without overwriting an earlier capture."""
    expected = hashlib.sha256(source.read_bytes()).hexdigest()
    created = False
    try:
        with destination.open('xb') as output:
            created = True
            with source.open('rb') as input_stream:
                shutil.copyfileobj(input_stream, output)
        if hashlib.sha256(destination.read_bytes()).hexdigest() != expected:
            raise ValueError('Retained snapshot differs from the checked reader input.')
    except BaseException:
        if created:
            destination.unlink(missing_ok=True)
        raise
    return {'file': destination.name, 'sha256': expected, 'byteLength': destination.stat().st_size}


def observe(database: Path, reader: Path, marker: dict, extras: list[str], publisher: str | None = None,
            retain: Path | None = None) -> dict:
    with tempfile.TemporaryDirectory(prefix="elsa-broadcast-recovery-read-") as directory:
        copy = Path(directory) / "snapshot.sqlite"
        with closing(sqlite3.connect(database.as_uri() + "?mode=ro", uri=True)) as source:
            source.execute("pragma query_only=ON")
            copy_snapshot(source, copy)
        result = read_snapshot(copy, reader, marker, extras, publisher)
        if retain is not None:
            result['retainedSnapshot'] = retain_snapshot(copy, retain)
        return result


def observe_membership(database: Path, reader: Path) -> dict:
    with tempfile.TemporaryDirectory(prefix="elsa-membership-recovery-read-") as directory:
        copy = Path(directory) / "snapshot.sqlite"
        with closing(sqlite3.connect(database.as_uri() + "?mode=ro", uri=True)) as source:
            source.execute("pragma query_only=ON"); copy_snapshot(source, copy)
        digest = hashlib.sha256(copy.read_bytes()).hexdigest()
        run = subprocess.run(["dotnet", str(reader), "--inspect-membership", str(copy), digest], text=True, capture_output=True, timeout=60)
        result = json.loads(run.stdout)
        if run.returncode or result.get("providerChecked") is not True:
            raise ValueError("The actual membership reader refused the observation: " + str(result.get("failureType")))
        if hashlib.sha256(copy.read_bytes()).hexdigest() != digest:
            raise ValueError("The closed membership snapshot changed while being read.")
        return result


def watch_claim(database: Path, reader: Path, marker: dict, extras: list[str], timeout: float, previous_fence: int) -> dict:
    # A live read transaction pins the actual claimed view while backup copies it. WAL writers keep running.
    identity = base64.b64encode(marker["sourceOutboxItemId"].encode("utf-16le")).decode()
    scope = base64.b64encode(marker["scope"].encode("utf-16le")).decode()
    deadline = time.monotonic() + timeout
    with tempfile.TemporaryDirectory(prefix="elsa-broadcast-successor-claim-") as directory:
        copy = Path(directory) / "snapshot.sqlite"
        with closing(sqlite3.connect(database.as_uri() + "?mode=ro", uri=True, isolation_level=None, timeout=5)) as source:
            source.execute("pragma query_only=ON")
            while time.monotonic() < deadline:
                source.execute("begin")
                try:
                    row = source.execute("select ContentJson from elsa_runtime_post_commit_outbox where OutboxItemId=? and ScopeKey=?", (identity, scope)).fetchone()
                    item = json.loads(row[0]) if row else None
                    if item and item["deliveryFencingToken"] > previous_fence and item.get("deliveringOwnerId") is not None:
                        copy_snapshot(source, copy)
                        result = read_snapshot(copy, reader, marker, extras)
                        claim = result["source"]
                        if claim["deliveryFencingToken"] <= previous_fence or not claim["deliveringOwnerId"] or claim["deliveringOwnerId"] == marker["ownerId"]:
                            raise ValueError("The checked snapshot did not prove successor ownership.")
                        return result
                    if item and item["status"] == 2:
                        raise ValueError("Source completed before a coherent successor-claim observation; no owner proof was captured.")
                finally:
                    source.execute("rollback")
                time.sleep(0.005)
    raise TimeoutError("No coherent successor claim was observed within the supplied bound.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", type=Path, required=True)
    parser.add_argument("--reader", type=Path, required=True)
    parser.add_argument("--marker", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--extra-workflow", action="append", default=[])
    parser.add_argument("--watch-claim", action="store_true")
    parser.add_argument("--membership", action="store_true")
    parser.add_argument("--publisher")
    parser.add_argument("--previous-fence", type=int, default=0)
    parser.add_argument("--timeout", type=float, default=180)
    parser.add_argument("--retain-snapshot", type=Path, help="Keep the exact checked closed input as a new portable file")
    args = parser.parse_args()
    marker = json.loads(args.marker.read_text())
    try:
        database = args.database.resolve(strict=True); reader = args.reader.resolve(strict=True)
        if args.membership and args.watch_claim: raise ValueError("Membership mode cannot watch a source claim.")
        if args.publisher and (args.membership or args.watch_claim): raise ValueError("Publisher discovery is a separate checked observation mode.")
        if args.retain_snapshot and (args.membership or args.watch_claim):
            raise ValueError("Snapshot retention requires an ordinary checked source observation.")
        if args.membership:
            result = observe_membership(database, reader)
        elif args.watch_claim:
            result = watch_claim(database, reader, marker, args.extra_workflow, args.timeout, args.previous_fence)
        else:
            result = observe(database, reader, marker, args.extra_workflow, args.publisher, args.retain_snapshot)
        args.output.write_text(json.dumps(result, indent=2) + "\n")
        print(json.dumps({"providerChecked": True, "members": len(result["members"])} if args.membership else {"providerChecked": True, "status": result["source"]["status"], "fence": result["source"]["deliveryFencingToken"]}))
        return 0
    except (OSError, ValueError, sqlite3.Error, subprocess.SubprocessError, TimeoutError) as error:
        args.output.write_text(json.dumps({"providerChecked": False, "status": "ObservationUnavailable", "failureType": type(error).__name__, "reason": str(error)}, indent=2) + "\n")
        print(str(error))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
