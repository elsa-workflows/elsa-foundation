# SQLite test cleanup ownership probe

Status: bounded supporting evidence for [#2185](https://github.com/elsa-workflows/elsa-foundation/issues/2185), not a completed causal investigation or cleanup fix. Owner bucket: [Code Reality and Test Maturity](../program-goals/code-reality-and-test-maturity.md). Recorded on 2026-10-01 at source `5a8adbe29b382fad5b7f69c237ada5bba73f6440`, while [#2177](https://github.com/elsa-workflows/elsa-foundation/issues/2177) remained the sole active Runtime Composition delivery/verification leaf.

## Finding and decision

The current temporary database helper clears every SQLite pool in its test-host process. A deterministic control confirms that cleaning database B closes database A's cached idle native handle. A test-only `ClearPool` control for B's exact connection string preserves A's cached handle. A separate live A handle stays usable in both controls.

The bounded concurrent probe **did not reproduce a native-handle crash**: all three workloads completed 4,000 opens/reads and 1,000 cleanups with zero observed provider errors. This is neither proof that global cleanup is safe at every native activation boundary nor proof that scoped cleanup fixes the reported CI failures. Keep #2185 open. Do not change the provider or helper based on an invented cause, serialize the suite, or present an unchanged retry as a fix.

Use the finding to refine an owned-resource cleanup contract: dispose every owned context/connection before cleanup, make pooling policy explicit, and clear only the pools actually owned by the fixture. An exact connection string identifies a pool; a database path alone cannot enumerate differently spelled connection strings or other pooled option variants. A scoped policy still needs an inventory of actual helper consumers, demonstrated cleanup of every owned pool/sidecar, and a meaningful adverse control before implementation. The existing helper's tolerated filesystem deletion failures remain a separate compatibility decision.

## Observed CI failures

[#2185](https://github.com/elsa-workflows/elsa-foundation/issues/2185) retains the original Backfill and Identity failure logs and pinned-version/source checks. The additional [CI36859130012 attempt1, job110359269989](https://github.com/elsa-workflows/elsa-foundation/actions/runs/36859130012/job/110359269989) failed before a write-gate test body at `EfSchemaWriteGateInterceptorTests.InitializeAsync` line23:

- `ObjectDisposedException`, object `SQLitePCL.sqlite3`.
- Stack: `sqlite3_create_collation` -> `SqliteConnection.Open` -> `EnsureCreatedAsync`.
- Failed case: `A_row_at_the_write_version_is_written_and_one_at_the_hosts_newer_current_version_is_refused(synchronous: True)`.
- Existing EF assembly: 699 passed, one failed, one existing opt-in stress skip, 701 total.

The failed fixture disposes its tracked contexts before its database helper. [PR #2250](https://github.com/elsa-workflows/elsa-foundation/pull/2250) changes only synthetic upcaster observation code/tests; it does not change this fixture or helper. The matching low-level stack across sites is evidence to investigate, not identification of the competing cleanup or its exact native interleaving. The embedded Event-bookmark incident in #2216 remains a different, unexplained failure.

## Source boundary

- [TemporarySqliteDatabase](../../tests/essentials/Persistence/EntityFramework/Tests/TemporarySqliteDatabase.cs) owns a unique file and calls process-wide `ClearAllPools` before deleting its database and sidecars.
- [Write-gate fixture](../../tests/essentials/Persistence/EntityFramework/Tests/EfSchemaWriteGateInterceptorTests.cs) uses [SchemaGateSupport](../../tests/essentials/Persistence/EntityFramework/Tests/SchemaGateSupport.cs), which configures direct `UseSqlite`.
- [EfSqliteSerialOpenInterceptor](../../src/essentials/Persistence/EntityFramework/EfSqliteSerialOpenInterceptor.cs) orders same-pool opens through the normal binding, and explicitly excludes pool clearing. Its separate provider-upgrade owner is [#2220](https://github.com/elsa-workflows/elsa-foundation/issues/2220).
- The pinned [provider pool](https://github.com/dotnet/efcore/blob/v10.0.10/src/Microsoft.Data.Sqlite.Core/SqliteConnectionPool.cs) and [native connection](https://github.com/dotnet/efcore/blob/v10.0.10/src/Microsoft.Data.Sqlite.Core/SqliteConnectionInternal.cs) show cleanup/leak reclamation and owner publication. These sources make an activation/cleanup collision a qualified hypothesis; the probe supplies no observation of that precise instruction gap.

## Executed controls

The [retained probe source](evidence/sqlite-pool-cleanup-probe.cs.txt) is a noncompiled evidence artifact, not another permanent test suite. Its digest is `d02dc449341d80de3cd0b71d19fed59c872220226f19f71500219f12b5726c95`. It was temporarily copied into the existing EF project, executed with default suite settings and removed before restoring that suite.

The ownership theory opens two A connections simultaneously, verifies distinct native handles, then closes one back into the pool. Cleaning B closes the idle A handle only in the global control. The other A connection remains live and executes `SELECT 1` in both controls. All contexts/connections are disposed before final owned-pool cleanup.

The overlap experiment uses four dedicated openers, each with its own unique A database/pool, and one B cleanup worker. Each policy runs 1,000 barrier rounds. B is recreated at its same unique owned path each round and its connection is closed before cleanup. A uses the normal `EfRelationalProviderBinding` serial-open workaround; this deliberately avoids the separately known concurrent-checkout-of-one-pool race and **does not replay the failing direct-UseSqlite fixture**. Half the A workers open/read synchronously and half use the async EF APIs. The unpooled control changes A's pooling mode while retaining global B cleanup.

| Policy | Attempted / opened / reads | B cleanups | Open-during-cleanup samples | Cleanup-during-open samples | Provider errors | Owned tasks joined | File/sidecar leftovers |
|---|---:|---:|---:|---:|---:|---|---:|
| Global helper | 4,000 / 4,000 / 4,000 | 1,000 | 1,683 | 221 | 0 | yes | 0 |
| Scoped B pool | 4,000 / 4,000 / 4,000 | 1,000 | 1,088 | 40 | 0 | yes | 0 |
| Unpooled A, global B | 4,000 / 4,000 / 4,000 | 1,000 | 1,294 | 198 | 0 | yes | 0 |

The overlap counters are instantaneous managed observations and can undercount; they are not exact native-gap evidence. Each phase has a deadline, joins workers before file deletion and asserts the full fixed workload completed. A provider error is recorded separately from whether the harness completes; a green harness summary alone is not zero-error evidence. Root parsed the actual per-policy TRX output. Process-wide native-handle leak counts were not measured; joined workers and absent owned files are the actual cleanup evidence. macOS file deletion alone does not prove a Windows handle-release guarantee.

## Reproduce the bounded probe

Use a clean disposable worktree with the required SDK and restored project dependencies. The first recorded launch in an unrestored isolated worktree stopped before execution with `NETSDK1004`; that failure was retained, then identical source ran in the already-restored integration checkout at the same source head. No result was inferred from the failed launch.

```bash
(
  set -euo pipefail
  probe_target=tests/essentials/Persistence/EntityFramework/Tests/EfSqlitePoolCleanupProbeTests.cs
  test ! -e "$probe_target"
  trap 'rm -f "$probe_target"' EXIT
  cp docs/reports/evidence/sqlite-pool-cleanup-probe.cs.txt "$probe_target"
  DOTNET_PROCESSOR_COUNT=3 dotnet test \
    tests/essentials/Persistence/EntityFramework/Tests/Elsa.Persistence.EntityFramework.Tests.csproj \
    --filter FullyQualifiedName~EfSqlitePoolCleanupProbeTests \
    --logger 'trx;LogFileName=owned-cleanup-probe.trx' \
    --results-directory /tmp/elsa-owned-cleanup-probe
)
```

The recorded successful invocation used `--no-restore` because those dependencies were present. Initial failure and successful logs/TRX/receipts were retained under `/tmp/runtime-composition-2185-cleanup-probe/`; these local paths are supplementary, not portable publication or enduring hosted artifacts. The table, command, source identity and retained noncompiled probe make the finding reviewable without those local files.

After removing the temporary file, root rebuilt and ran the full existing EF assembly at the same unchanged head: **700 passed, zero failed, one existing opt-in stress skip of 701 total**. Actual result identities confirm no temporary probe remains in that suite. No new test project, provider matrix, workflow cadence, runtime configuration, database migration or product behavior was introduced.

## Review and remaining work

Root reviewed and repaired the delegated draft: distinct idle/live handles, dedicated workers, valid C# control flow, bounded complete workloads and join-before-delete. The original unexecuted draft and initial missing-assets log were retained. Luna Extra High independently reviewed the final probe digest above and found no material source bug for its stated boundary. Root owns execution and interpretation; the table and limits require report review before publication.

The full #2185 investigation remains open: exact failing-fixture/test-host instrumentation, causal reproduction or a documented inability to reproduce, an inventory of exact owned pool identities, an agreed cleanup policy, meaningful adverse proof, Identity and Persistence default-parallel suites, the complete fast gate, and platform-specific cleanup/native-handle evidence. Existing #2220 provider-upgrade work and the runtime-composition delivery remain separate owners. A single bounded CI verification rerun after this investigation retains attempt1 as failure evidence and cannot complete #2185 or establish a cleanup fix.
