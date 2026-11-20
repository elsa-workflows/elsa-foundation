# Implementation Plan: The ETL runtime store's schema on all four EF engines

**Branch**: `agent/claude/2908-etl-runtime-providers` | **Spec**: [spec.md](spec.md)

## Summary

Mirror spec 1202 for the runtime context, plus the runtime-specific schema work: a run-key seam that hashes run ids on the server engines, check constraints rendered per engine, short alternate-key names, binary collation, and exact totals. The registration stays SQLite-only; the provider suite constructs stores directly. Concurrency is spec 1206.

## Constitution check

- **Persistence family** (ADR 0073 D1): EF Core only; the package stays provider-free.
- **Visibility** (§2.23.3): contexts, the seam and migrations stay internal; the existing same-domain grants cover the tooling and the provider suite.
- **Compatibility:** the SQLite context keeps its encoded run key and byte-identical check constraint text; the SQLite snapshot and chain do not change (a pending-model-changes check proves it). Persisted JSON and artifacts are untouched: nothing outside the key columns depends on `RunKey`.
- **Tests:** the runtime-store tests become contracts over `IEtlRuntimeTestDatabase`, run on SQLite and, where they do not race, on the server engines.

## Changes by project

- **Persistence**
  - `EtlRuntimeDbContext`: check constraints declared as templates and rendered per engine through a protected abstract `ConstraintDialect` each provider context supplies (the contexts' existing `ExpectedProviderName` constants stay); `HasColumnType("INTEGER")` removed from the shared model (a no-op on SQLite); `internal string RunKeyOf(string runId)`.
  - Provider contexts: SQLite (encoded key, unchanged text), SQL Server, PostgreSQL, MySQL (64-character `RunKey`, short AK names, collation, utf8mb4 on MySQL).
  - Stores and codec: every place that derives a run key from a run id calls `RunKeyOf` (the `RunKey` helper, `ClaimPartition`, the run listing seek, `ClaimConnectorPartition`, and `RunRecord`'s three callers); the static helpers that need it become instance methods. `ValidateRun` keeps its `Decode(RunId) == runId` check and adds `RunKey == RunKeyOf(runId)`; the codec's scope check retains the tenant/partition residual check; run reads validate the key and decoded id in `ValidateRun`. API cursors already carry the run id, so no engine-specific key leaks out. Side effects documented: the delivery candidate log id, the watermark's write-only `RunKey` and claim tie-break order follow the engine's key.
  - Totals: retain the existing aggregate queries in `FindConnectorRunAsync` and the inspector. The contracts pass their exact `long` totals on the server providers, so no aggregate rewrite is needed in this slice.
  - Generated `Initial` and additive `ContinuousCompaction` migrations under `Migrations/EtlRuntime/{SqlServer,PostgreSql,MySql}`; adopted current main’s compaction columns and predecessor constraint without rewriting the Initial migrations. `[EfModule]` keeps naming only the SQLite runtime context until spec 1206, so ordinary host composition does not advertise an unqualified server runtime; the tooling factories scaffold server migrations independently.
  - The registration keeps its SQLite-only NotSupportedException with a plain explanation; the contributor docs link to the runtime concurrency successor.
- **Tooling**: three runtime factories; the ETL migration script takes an optional third argument, the store (`design`, the default, or `runtime`), so existing invocations keep working.
- **Tests**: `IEtlRuntimeTestDatabase` (base-typed `CreateContext(params IInterceptor[])` and `CreateStore`) beside `IEtlDesignTestDatabase`. The runtime-store tests move into an abstract contract base class with the shared helpers as protected members, test by test: SQLite-only are the `sqlite_master` check (`EtlRuntimeStoreTests.cs:23`), the begin-immediate gate race (`:122`), the raw-SQL budget test (`EtlConnectorMetadataBudgetTests.cs:267`), the unquoted-update CSV test (`EtlConnectorCsvOutputTests.cs:208`), two inspector tests (`:248`, `:264`) and the eight `*MigrationTests`; `EtlConnectorRequestConstraintTests` is ported with per-engine quoting; `EtlConnectorAcceptanceBudgetTests.cs:248` builds its key through `RunKeyOf`. Server subclasses and provider checks (migrate/validate, recorded collation, maximum-length and case-only run ids, check-constraint and foreign-key refusals) in the `etl-ef` suite, which runs nightly.
- **Docs**: persistence README, ETL README, evidence README.

## Risks

- **Check constraint text drift on SQLite:** rendering must reproduce today's SQLite text exactly; the SQLite snapshot comparison catches any change.
- **Over-wide keys on SQL Server** create with a warning and fail at insert; the maximum-length identity test inserts real rows.
- **Contract extraction size:** about 167 tests across 31 partials; extract test by test and keep the SQLite-only cases in SQLite subclasses.
- **Oracle's MySQL provider** (not Pomelo) must emit the check constraints and read the projected counters as `long`; the provider suite verifies both.
