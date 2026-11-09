#!/usr/bin/env python3
"""Run the frozen v1 producer and current native consumer against one owned root.

Opt-in acceptance, not normal test discovery. Requires Git, Python 3.11+ and the
repository's .NET SDK. Logs, input snapshots and receipts stay in the local
evidence directory; they include machine paths and must not be published.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess
import tempfile
import xml.etree.ElementTree as ET


BASELINE = "b555bd8460270476e49496e519c69b65180e9505"
REPOSITORY = Path(__file__).resolve().parents[4]
TESTS = Path("Extensions/Etl/tests/Tests")
PROJECT = TESTS / "Elsa.Etl.Tests.csproj"
HARNESS = Path("Extensions/Etl/tests/Shared/EtlV1NativeUpgradeJourneyTests.cs")
HARNESS_FILES = {
    str(PROJECT), str(TESTS / "EtlPublishedWorkflowFixture.cs"),
    str(TESTS / "EtlPublishedWorkflowJourneyTests.cs"),
    str(HARNESS),
}
ADAPTER = Path("Extensions/Etl/tests/Shared/v1-native-upgrade-harness.patch")
SUCCESSOR_CHANGES = HARNESS_FILES | {"Extensions/Etl/tests/Shared/test-v1-native-upgrade.py", str(ADAPTER)}


def git(root, *arguments):
    return subprocess.check_output(["git", "-C", str(root), *arguments], text=True).strip()


def digest(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def save_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def integrity(root):
    results = {}
    for path in sorted(root.glob("*.db*")):
        if path.suffix not in (".db", ".offline"):
            continue
        # Read-only SQLite understands any retained WAL; no checkpoint or data rewrite.
        with sqlite3.connect(path.as_uri() + "?mode=ro", uri=True) as connection:
            verdict = connection.execute("PRAGMA integrity_check").fetchall()
        assert verdict == [("ok",)], (path.name, verdict)
        results[path.name] = "ok"
    assert "runtime.db" in results and "workflow.db" in results
    return results


def patch_predecessor(root):
    git(root, "apply", "--check", str(REPOSITORY / ADAPTER))
    git(root, "apply", str(REPOSITORY / ADAPTER))
    assert not (root / HARNESS).exists()
    shutil.copyfile(REPOSITORY / HARNESS, root / HARNESS)
    return HARNESS_FILES


def verify_predecessor(root, allowed):
    assert git(root, "rev-parse", "HEAD") == BASELINE
    changed = set(git(root, "diff", "--name-only", BASELINE).splitlines())
    untracked = set(git(root, "ls-files", "--others", "--exclude-standard").splitlines())
    assert changed | untracked <= allowed, (changed | untracked) - allowed
    # Checking every tracked file outside the allowlist also covers project files,
    # dependency pins and build inputs outside the usual production src trees.
    git(root, "diff", "--exit-code", BASELINE, "--", ".",
        *[f":(exclude){path}" for path in sorted(allowed)])
    return {path: digest(root / path) for path in sorted(allowed)}


def verify_successor():
    changed = set(git(REPOSITORY, "diff", "--name-only", "HEAD").splitlines())
    changed.update(git(REPOSITORY, "ls-files", "--others", "--exclude-standard").splitlines())
    assert changed <= SUCCESSOR_CHANGES, f"Commit unrelated changes before acceptance: {changed - SUCCESSOR_CHANGES}"
    return {path: digest(REPOSITORY / path) for path in sorted(SUCCESSOR_CHANGES)}


def tree_hashes(root):
    return {str(path.relative_to(root)): digest(path) for path in sorted(root.rglob("*")) if path.is_file()}


def runtime_migrations(root):
    with sqlite3.connect((root / "runtime.db").as_uri() + "?mode=ro", uri=True) as connection:
        return [row[0] for row in connection.execute(
            'SELECT MigrationId FROM "__EFMigrationsHistory_EtlRuntime" ORDER BY MigrationId')]


def run_phase(root, evidence, data, phase, test_name):
    results = evidence / phase
    results.mkdir()
    command = ["dotnet", "test", str(root / PROJECT), "-p:EtlV1NativeUpgrade=true",
               "-p:RestoreLockedMode=true", "--filter", f"FullyQualifiedName~{test_name}",
               "--logger", "trx;LogFileName=result.trx", "--logger", "console;verbosity=minimal",
               "--results-directory", str(results)]
    env = os.environ.copy()
    env["ETL_V1_NATIVE_UPGRADE_ROOT"] = str(data)
    save_json(results / "command.json", {"cwd": str(root), "arguments": command,
                                        "ETL_V1_NATIVE_UPGRADE_ROOT": str(data)})
    print(f"Running {phase}; log: {results / 'test.log'}", flush=True)
    with (results / "test.log").open("w", encoding="utf-8") as log:
        result = subprocess.run(command, cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
    if result.returncode:
        raise RuntimeError(f"{phase} exited {result.returncode}; inspect {results / 'test.log'}")
    report = ET.parse(results / "result.trx")
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = report.find("t:ResultSummary/t:Counters", ns)
    tests = report.findall("t:Results/t:UnitTestResult", ns)
    assert counters is not None and counters.get("total") == "1" and counters.get("passed") == "1"
    assert len(tests) == 1 and tests[0].get("outcome") == "Passed" and test_name in tests[0].get("testName", "")
    binaries = {}
    for project in ("Tests", "WorkerHost"):
        directory = root / "Extensions/Etl/tests" / project / "bin/Debug/net10.0"
        files = sorted(directory.glob("*.dll"))
        assert files, f"Missing {phase} {project} binaries"
        binaries.update({str(path.relative_to(root)): digest(path) for path in files})
    save_json(results / "binaries.json", binaries)
    print(f"{phase}: exactly one test passed, no skipped or failed tests", flush=True)


def main():
    if not __debug__:
        raise RuntimeError("Run without Python optimization; acceptance assertions must remain enabled")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence-root", type=Path,
                        help="New directory; defaults to a unique temporary directory. Never overwritten.")
    args = parser.parse_args()
    if args.evidence_root:
        evidence = args.evidence_root.resolve()
        evidence.mkdir(parents=True, exist_ok=False)
    else:
        evidence = Path(tempfile.mkdtemp(prefix="elsa-v1-native-upgrade-")).resolve()
    predecessor = evidence / "predecessor"
    data = evidence / "data"
    print(f"Private acceptance evidence: {evidence}", flush=True)
    git(REPOSITORY, "cat-file", "-e", BASELINE + "^{commit}")
    git(REPOSITORY, "worktree", "add", "--detach", str(predecessor), BASELINE)
    try:
        allowed = patch_predecessor(predecessor)
        hashes = verify_predecessor(predecessor, allowed)
        for relative in allowed:
            target = evidence / "producer-harness" / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(predecessor / relative, target)
        successor_hashes = verify_successor()
        current_diff = subprocess.check_output(["git", "-C", str(REPOSITORY), "diff", "--binary", "HEAD"])
        (evidence / "successor-harness.patch").write_bytes(current_diff)
        for relative in SUCCESSOR_CHANGES:
            target = evidence / "successor-harness" / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(REPOSITORY / relative, target)
        provenance = {"producerCommit": BASELINE, "producerHarness": hashes,
                      "successorCommit": git(REPOSITORY, "rev-parse", "HEAD"),
                      "successorHarness": successor_hashes,
                      "successorTrackedDiffSha256": hashlib.sha256(current_diff).hexdigest(),
                      "successorStatus": git(REPOSITORY, "status", "--porcelain"),
                      "runnerSha256": digest(Path(__file__).resolve()), "dataRoot": str(data)}
        save_json(evidence / "provenance.json", provenance)
        (evidence / "dotnet-info.txt").write_text(
            subprocess.check_output(["dotnet", "--info"], text=True, cwd=predecessor), encoding="utf-8")
        run_phase(predecessor, evidence, data, "producer",
                  "EtlPublishedWorkflowJourneyTests.V1_native_upgrade_producer_suspends_before_acceptance")
        assert verify_predecessor(predecessor, allowed) == hashes
        save_json(evidence / "producer-integrity.json", integrity(data))
        old_migrations = runtime_migrations(data)
        assert old_migrations == ["20261020120244_EtlRuntimeInitial"]
        save_json(evidence / "producer-migrations.json", old_migrations)
        before = tree_hashes(data)
        save_json(evidence / "before-upgrade-hashes.json", before)
        # The consumer uses the live original root; this is an evidence copy only.
        shutil.copytree(data, evidence / "before-upgrade")
        assert tree_hashes(evidence / "before-upgrade") == before
        assert tree_hashes(data) == before
        assert verify_successor() == successor_hashes
        run_phase(REPOSITORY, evidence, data, "consumer",
                  "EtlPublishedWorkflowJourneyTests.V1_native_upgrade_successor_completes_same_run")
        save_json(evidence / "consumer-integrity.json", integrity(data))
        new_migrations = runtime_migrations(data)
        expected_migrations = sorted(path.stem for path in
            (REPOSITORY / "Extensions/Etl/src/Persistence/EntityFrameworkCore/Migrations/Sqlite/Runtime").glob("[0-9]*.cs")
            if not path.name.endswith(".Designer.cs"))
        assert new_migrations == expected_migrations
        assert set(old_migrations) < set(new_migrations), "The successor must apply the additive runtime migrations"
        save_json(evidence / "consumer-migrations.json", new_migrations)
        save_json(evidence / "after-upgrade-hashes.json", tree_hashes(data))
        assert tree_hashes(evidence / "before-upgrade") == before
        assert verify_predecessor(predecessor, allowed) == hashes
        assert verify_successor() == successor_hashes
        assert git(REPOSITORY, "rev-parse", "HEAD") == provenance["successorCommit"]
        save_json(evidence / "result.json", {"passed": True, "producerCommit": BASELINE,
                                           "successorCommit": provenance["successorCommit"]})
    except BaseException:
        print(f"Acceptance incomplete. Retaining owned worktree and evidence at {evidence}", flush=True)
        raise
    else:
        # Only this runner's verified, test-only disposable checkout is removed.
        git(REPOSITORY, "worktree", "remove", "--force", str(predecessor))
        print(f"Upgrade acceptance passed. Private evidence retained at {evidence}", flush=True)


if __name__ == "__main__":
    main()
