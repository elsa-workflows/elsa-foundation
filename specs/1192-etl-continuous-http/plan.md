# Implementation Plan: Continuous HTTP ETL runs

**Branch**: `agent/claude/2908-continuous-http` | **Spec**: [spec.md](spec.md) | **Research**: [research.md](research.md)

## Summary

A v2 pipeline may opt into continuous mode with `Continuous = new EtlContinuousRunPolicy(seconds)`. When a shard of a continuous HTTP run catches up, the shard idles: its poll journal is cleared, its reservations are released and its partition waits for the interval. Nothing is retained.

A completion request causes each shard's next qualifying caught-up response to be retained as a final empty page. That exhausts the shard through the existing path, so the run settles through the existing rules. Cancellation, leases, effects, acceptance and the native activity are unchanged.

## Technical context

- .NET 10, EF Core with the SQLite runtime store, React and TypeScript Studio, xUnit, Vitest and Playwright.
- The ETL test project is in the PR tier (child-process).
- Constraints:
  - finite artifacts stay byte-identical;
  - no enumeration gains a member;
  - the persisted change is additive and nullable;
  - PublicAPI files list every new public member;
  - the `EXTENSION_POINTS.md` and README files change in the same unit.

## Constitution check

- **Design/runtime separation.** The policy is authored in the artifact and pinned. The runtime only reads it.
- **Compatibility.**
  - Prior writers' rows are captured by a migration test before the column is added. This follows `EtlConnectorSourceMigrationTests`.
  - Finite artifact bytes, hashes and identities are asserted unchanged.
- **Extension points.**
  - The Core `EXTENSION_POINTS.md` gains the provider obligations for the two new store members, `RecordConnectorHttpCaughtUpAsync` and `RequestConnectorRunCompletionAsync`.
  - The Runtime/Api `EXTENSION_POINTS.md` gains the new operations member and the permission.
- **Tests.** No test asserts on source or markdown text. Every changed existing test states its reason (§2.21.1).
- **Naming.** The new names (`EtlContinuousRunPolicy`, `EtlHttpCaughtUpResult`, `EtlRunCompletionView`) follow the existing `Etl…` families, use four components or fewer, and avoid banned words.

## Changes by project

### Core (`src/Core`)
- `EtlPipelineV2.Continuous`, `EtlContinuousRunPolicy` and its `FindViolation` rule (R1, R7).
- `EtlArtifactV2Json`: `ShouldSerialize` for `continuous`.
- `IEtlConnectorRuntimeStore`:
  - `RecordConnectorHttpCaughtUpAsync` and `EtlHttpCaughtUpResult`;
  - `RequestConnectorRunCompletionAsync` and `EtlRunCompletionOutcome`.

  Both are new interface members. The only implementation is the EF store; test wrappers such as `ConnectorWorkerGateStore` delegate.
- Connector inspection records: `Continuous`, `CompletionRequestedAt` and `NextPollAt`; the API projects `Continuous` as `mode` (R8).
- `PublicAPI.Unshipped.txt`, `README.md` and `EXTENSION_POINTS.md`.

### Design and compiler
- `EtlArtifactCompilerV2` calls the rules and reports the three error codes as design diagnostics.

### Runtime (`src/Runtime/Elsa.Etl.Runtime`)
- `EtlHttpRequestBuilder.IsCaughtUp` (R2).
- `EtlHttpSourceAcquirer`: classify; record idle and return without a page; or, when the result is `Final`, publish, capture and attach the normalized page.
- `EtlHttpPageCache.Decode`: normalize a caught-up continuation to null for continuous pipelines.
- `EtlConnectorExecutableReader.ValidateLimits`: the rules.

### Persistence (`src/Persistence/EntityFrameworkCore`)
- `EtlConnectorRunRecord.CompletionRequestedAtUtcTicks`, its `EtlRuntimeDbContext` CHECK, the migration and the model snapshot (R5).
- `EfEtlConnectorSourceStore.RecordConnectorHttpCaughtUpAsync` (R3).
- `RequestConnectorRunCompletionAsync`, in the runtime store partial next to cancel (R5).
- `EfEtlConnectorHistory`: the idle invariant (R4).
- `EfEtlConnectorHttpPageStore.AdvanceEmptyHttpPageAsync`: the interval after a moving empty page (R6).
- `EtlConnectorRecordCodec.ValidateBindings`: the rules.
- `EfEtlConnectorRuntimeInspector`: mode, completion-request time and the next poll.

### Runtime API (`src/Runtime/Api`)
- The `CompleteRun` endpoint, permission, resource entry, operations member and view (R8).
- `EtlRuntimeOperations.Connectors`: the new inspection fields.

### Studio (`studio/Elsa.Studio.Etl/Client`)
- `etlTypes.ts`, `etlApi.completeRun`, `EtlConnectorPipelineEditor`, `EtlRunInspection` and `EtlRunsPage` (R9).
- Vitest component tests and the Playwright authoring spec.

### Tests (`tests/Tests`, `tests/WorkerHost`, `tests/Shared`)
- **Artifact.** Continuous policy round-trips. A finite artifact's bytes and hash are unchanged. Validation errors are covered at all three sites.
- **Store.**
  - A caught-up response releases exactly what `Begin` reserved and adds no rows.
  - The invariant accepts an idle wait in a continuous run and refuses it in a finite one.
  - A stale fence or attempt is refused.
  - `Final` follows the completion-request time and `attempt.StartedAt`.
  - Completion results and the idle wake-up are covered; retry waits are left alone.
- **Acquirer and execution, with an in-process feed.**
  - Idle polls over 20 or more cycles leave state unchanged.
  - Appended records are delivered.
  - A moving empty page is followed by the interval.
  - Drain settles `Succeeded` and `BusinessError`.
  - Cancellation wins over a pending completion.
- **Migration.** A prior-writer finite HTTP run reads back unchanged after the migration, and the new column is null.
- **API.** Every completion result and its authorization are covered. The inspection fields are covered.
- **Worker processes.** A kill after capture causes a cache replay. A kill during an idle poll's attempt causes the poll to be retried. Two shards are leased independently.
- **Bite-proofs** for:
  - the `ShouldSerialize` guard;
  - classification;
  - the release of reservations;
  - the idle invariant;
  - the `Final` time comparison;
  - the selective wake-up;
  - the moving-empty interval;
  - cache normalization.

### Docs and evidence
- The ETL README gets a "Continuous HTTP runs" section and a feed contract.
- The Core README and the extension-point catalogues are updated.
- `specs/1192-etl-continuous-http/evidence/` holds:
  - the feed server and journey driver;
  - the summary JSON;
  - the bite-proofs;
  - Studio screenshots.

## Risks

- **Accounting invariants are re-verified on every read.** A wrong delta in R3 surfaces as `InvalidDataException`. Mitigation: store tests assert the request, partition and connector totals before `Begin`, after `Begin` and after the caught-up record.
- **Normalization in the cache must not change any finite decode.** Mitigation: it is guarded on `pipeline.Continuous`, and a test shows that a finite empty same-cursor page still fails with `pagination-cycle`.
- **Old workers in a mixed fleet** fail closed on a continuous artifact. This is documented, not mitigated in code (spec Edge Cases).
