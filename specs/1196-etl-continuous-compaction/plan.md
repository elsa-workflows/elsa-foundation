# Implementation Plan: Bounded state for continuous ETL runs

**Branch**: `agent/claude/2908-etl-compaction` | **Spec**: [spec.md](spec.md) | **Research**: [research.md](research.md)

## Summary

The plan covers continuous runs only:
- **Compaction.** Each claim in a continuous run deletes, for every shard, the rows below the predecessor of its current request, in the claim transaction and before any work is charged. Their counts move into a compacted summary on the partition, and the retained byte counters fall.
- **Audits.** They start at the base the compaction leaves.
- **Files.** The worker deletes the files whose use the committed transition ended.
- **Poll generation.** A partition column, copied onto the lease and incremented by each caught-up record, separates the page-cache keys of successive polls.

Finite runs are unchanged.

## Technical context

- .NET 10, with EF Core on the SQLite runtime store, and xUnit.
- No Studio change.
- Constraints:
  - finite bytes, identities and rows stay unchanged;
  - prior writers are captured before the persisted format changes;
  - the PublicAPI files list every new public member;
  - the `EXTENSION_POINTS.md` and README files change in the same unit.

## Constitution check

- **Compatibility.**
  - A migration test seeds rows with the spec 1192 schema, migrates, checks every row and the foreign keys, then compacts and audits that run.
  - Positions and cache keys at generation 0 are asserted byte-identical.
- **Extension points.** Store providers gain obligations: compaction at each claim, base-relative audits and the generation on the caught-up record. The Core `EXTENSION_POINTS.md` states them.
- **Tests.**
  - No test asserts on source or markdown text.
  - Every changed existing test states its reason.
- **Naming.** The new public members are `EtlConnectorPartitionLease.PollGeneration` and the generation overload of `EtlRuntimeIdentity.ConnectorHttpPageId`. The new persisted columns are `PollGeneration` and the `Compacted…` set.

## Changes by project

### Core (`src/Core`)
- `EtlConnectorPartitionLease.PollGeneration`, init-only (R4). PublicAPI.
- A `ConnectorHttpPageId` overload that takes the generation and appends it when it is above 0.

### Persistence (`src/Persistence/EntityFrameworkCore`)
- **Entities and context:**
  - the partition gains `PollGeneration` and seven `Compacted…` columns, each with a check;
  - the relaxed request predecessor check.
- **Migration** `20261103090000_ConnectorContinuousCompaction`: adds the columns, and rebuilds the request table (R2). The model snapshot is updated to match.
- **`EtlConnectorRecordCodec.Pipeline`:** decodes a continuous run with no page-sequence bound (R7).
- **`EfEtlConnectorSourceStore.RecordConnectorHttpCaughtUpAsync`:** increments the generation on the non-final path.
- **`EfEtlConnectorCompaction`** (new partial of the runtime store). One method implements R3 and the accounting. The claim calls it after its audit (R8).
- **`EfEtlConnectorHistory` and `EfEtlConnectorAcceptanceHistory`:** base-relative audits.
- **`EfEtlConnectorRuntimeInspector`:** adds the compacted effect counts.
- **The claim:** copies `PollGeneration` onto the lease.

### Runtime (`src/Runtime/Elsa.Etl.Runtime`)
- **`EtlHttpPageCache`:**
  - generation-aware read and publish;
  - the per-page body identity for continuous runs, with legacy-tolerant validation;
  - `DeleteAsync` for one page.
- **`EtlHttpSourceAcquirer`:** passes the lease's generation to the cache.
- **`EtlConnectorExecution`:** after an acceptance or empty-page advance commits in a continuous run, deletes the route bodies and the left page's files.
- **`EtlConnectorOutputProcessor`:** `DeleteRetainedRouteBodiesAsync`.

### Tests (`Extensions/Etl/tests`)
- **`EtlContinuousCompactionJourneyTests.cs` and `EtlContinuousPollGenerationTests.cs`:**
  - window, accounting and lifetime counts;
  - finite runs untouched.
- **Poll generation (in `EtlContinuousPollGenerationTests.cs`):**
  - the superseded-late-page interleaving;
  - byte identity at generation 0.
- **`EtlContinuousCompactionMigrationTests.cs`:** the captured spec 1192 fixture, then migrate, check, resume, compact and complete.
- **`EtlContinuousPeerCompactionTests.cs`:** one claim compacts every shard of the fixture's run.
- **`EtlContinuousFixtureCaptureTests.cs`:** the gated capture of the fixture.
- **`EtlConnectorRequestConstraintTests.cs`:** a null predecessor outside the base is refused by the audit.
- **The journey and processes tests:** long continuous runs with kills, asserting bounded rows and files.

### Docs
- The ETL README, the Core README and `EXTENSION_POINTS.md`.
- Spec 1192's declared limits, pointed here.
- The evidence README, the bite-proofs and the Workbench journey.
