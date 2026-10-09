# Implementation Plan: Root-Write Lease Coordination Without a Hot Row

**Branch**: `runtime-throughput/root-write-lease-rows` | **Date**: 2026-10-09 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/200-root-write-lease-rows/spec.md`

## Summary

Concurrent executions of one published workflow fault because every root-write lease of an artifact lives in one optimistic-concurrency row, and every checkpoint commit writes that row twice ([research R1](research.md#r1--source-of-the-hot-row-verified)). The design:

- **Per-lease records:** move leases into one record per lease. The existing coordination row becomes guard-only.
- **Write-then-check:** keep guard/lease mutual exclusion by having each side commit its own record and then check the other side's in a fresh statement (R2). This was owner decision 3.
- **Isolated context:** run lease operations on an isolated context, so they never save or discard the checkpoint's staged changes (R4).
- **Legacy leases:** honour leases still in the old shared row until they expire (R5, owner decision 1).
- **Release-failure isolation:** a durable commit is never reported as failed because its lease release failed (R7).
- **#2286:** give each checkpoint commit attempt its own lease id (R8).

## Technical Context

**Language/Version**: C# / .NET 10 (SDK pinned by `global.json`)

**Primary Dependencies**: EF Core 10 with Sqlite, Npgsql, SqlServer and MySQL providers; the existing `EfWriteRetry`, `RuntimeArtifactEfModule` and EF tooling (`tools/ef`)

**Storage**: Runtime EF context (`__EFMigrationsHistory_ElsaRuntime`). One new table: `elsa_runtime_workflow_executable_root_write_lease`.

**Testing**: xUnit. Runtime EF SQLite tests (`EfRuntimeArtifactScopeTests`), Runtime in-memory tests, the provider container suite ("EF container suites (runtime)"), the EF migrations suite, and the e2e A1 matrix.

**Target Platform**: Server hosts (Workbench, Foundation host). Linux and Windows.

**Project Type**: Library: `Elsa.Workflows.Runtime` and `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore`.

**Performance Goals**: None as gates (ADR 0073 D7). The structural target is that no lease operation contends with other holders' leases (FR-001/FR-002).

**Constraints**:
- Expand-only migration.
- No public contract signature change.
- No provider-specific locking SQL (owner decision 3).
- In-memory parity.
- Mixed-version clusters are unsupported (ADR 0073 D5).

**Scale/Scope**: About 4 production files, 1 entity plus configuration, migrations for 4 providers, about 8 new tests plus 3 provider variants.

## Constitution Check

The plan was checked against both ratified constitutions. It relies on no draft section (framework §2.24 and Elsa §E2.9 are not used).

| Gate | Status |
|---|---|
| **Framework §2.6 (composition)** | Pass. No new cross-feature coupling. The store keeps its single-implementation replacement contract. The manager gains only an optional logger dependency. |
| **§2.16 (refactor cost) and §2.20 (provider decomposition)** | Pass. No new project or package. The new entity and migrations stay in the existing provider-family EF module. |
| **§2.21.1 / §2.23.4 (golden rule)** | Pass, with a duty. Every existing lease/guard/deletion/GC test must pass with its subject and objective preserved. Tests that read the coordination row's `Leases` JSON directly may need only setup or wiring changes. Each one is listed in tasks.md with its preserved objective. Deleting a test is not planned; if it ever became necessary it would need recorded architect approval. |
| **§2.23.2 (implementation tests)** | Pass. New logic in store, manager and checkpoint stores gets unit tests. Tests use stubbed or real fixtures following the existing patterns. |
| **§2.23.5 (exception boundaries)** | Pass. Provider failures in the new lease paths are wrapped by the existing `NormalizeProviderFailure` / `RuntimeArtifactEntityFrameworkPersistenceException` path. The manager no longer surfaces infrastructure release failures, so they are logged, not swallowed silently. |
| **§2.22.1 / §2.22.2 (extension-point catalog)** | Pass, with a duty. Update the "Lease identity" notes in Runtime `EXTENSION_POINTS.md` (checkpoint ids are now per attempt; store behavior). No new extension point. |
| **Elsa §E2.2 / §E2.6** | Pass. Runtime stays artifact-only, with no design dependency. |
| **ADR 0073 D5 / D7** | Pass. Clean-break data policy is respected: legacy leases are honoured until expiry and no data conversion runs. There is no performance gate; evidence is correctness only. |

The post-design re-check (after Phase 1) also passes. There are no violations.

## Project Structure

### Documentation (this feature)

```text
specs/200-root-write-lease-rows/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/root-write-lease-coordination.md
├── checklists/requirements.md
└── tasks.md                # next: speckit-tasks
```

### Source Code (repository root)

```text
src/essentials/Workflows/Runtime/
├── Services/Executables/WorkflowExecutableRootWriteLeaseManager.cs   # R7 release isolation + logging
├── Services/Executables/InMemoryWorkflowExecutableStore.cs           # parity only (no semantic change expected)
├── Services/Checkpoints/InMemoryRuntimeCheckpointCommitStore.cs       # R8 per-attempt lease id
└── EXTENSION_POINTS.md                                                # lease identity notes
src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/
├── Entities/RuntimeArtifactEntities.cs                                # + WorkflowExecutableRootWriteLeaseEntity
├── Configuration/RuntimeArtifactEntityConfigurations.cs               # + config, keys, index
├── RuntimeArtifactEfModule.cs, AssemblyInfo.cs, RuntimeProviderContexts.cs  # table name, schema family, text columns
├── Stores/EfWorkflowExecutableStore.cs                                # R2–R6 lease/guard paths, sibling lease context (R4)
├── Stores/EfRuntimeCheckpointCommitStore.cs                           # R8 per-attempt lease id
└── Migrations/Runtime/{Sqlite,PostgreSql,SqlServer,MySql}/*_WorkflowExecutableRootWriteLeases.*
tests/essentials/Workflows/Runtime/
├── Persistence/EntityFrameworkCore/Tests/EfRuntimeArtifactScopeTests.cs     # regression (exists) + race/legacy/isolation
├── Persistence/EntityFrameworkCore/ProviderTests/RuntimeArtifactsProviderSmokeTests.cs  # PG (exists) + SqlServer/MySql variants
└── Tests/ (manager + checkpoint commit store tests)                          # R7, R8
```

**Structure Decision**: The change stays inside the existing Runtime core and Runtime EF provider module, at the seams that own the behavior: the store for storage and exclusion, the manager for release semantics, and the checkpoint commit stores for lease identity. No new project or abstraction is added.

## Validation Design

1. **Red first (done):**
   - The three SQLite regression tests and the PostgreSQL parallel test fail on the base code, with the production messages.
   - Evidence is in the PR #2539 comments and CI runs on `a55706a` and `3d1706c`.
   - Each new behavior test (race, legacy, isolation, release, #2286) is also written and shown failing before its implementation, where the base code allows that. The release and #2286 tests fail on today's code. The legacy and race tests pass trivially today, so they serve as non-regression guards.
2. **Implement** R3–R8, in the order given in tasks.md.
3. **Green on SQLite and in memory, locally.** The local feed recipe is in the PR #2539 comment.
4. **PostgreSQL locally.** Then SQL Server and MySQL via CI container suites.
5. **Bite-proofs**, using temporary working-tree mutations only:
   - revert the store to the shared row: the regression tests must fail;
   - remove the acquire post-commit check: the race test must fail;
   - remove release isolation: the manager test must fail;
   - remove the nonce: the #2286 test must fail.
6. **Migrations suite and generated maps** check.
7. **Architecture guards.**
8. **A1 end-to-end matrix** on the Azure runner. All 8 cases must pass (SC-001).
9. **Merge gate**: green CI, CodeRabbit with no open blocking threads, and evidence comment on the PR. Then the spec status becomes Implemented in the same PR.

## Complexity Tracking

No violations.

**Rejected alternatives:**
- *Provider locking.* Rejected by owner decision 3: it needs provider SQL and recreates a shared-row lock.
- *Retry backoff only.* It leaves the hot row in place.
- *A global expired-lease sweep contract member.* Deferred, because crash-orphaned rows are inert and bounded (R6).
- *Per-execution lease (former US4).* Moved to a follow-up unit by owner decision 2.
