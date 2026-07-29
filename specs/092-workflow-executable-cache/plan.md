# Implementation Plan: Bounded Workflow Executable Cache

**Branch**: `codex/624-shell-readiness` | **Date**: 2026-07-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/092-workflow-executable-cache/spec.md`

## Summary

Add a provider-neutral, bounded LRU decorator around durable workflow-executable stores. Positive immutable lookups are cached by artifact ID, concurrent misses are coalesced, mutations preserve durable-store authority, and bounded telemetry makes behavior tunable. Wire the decorator into Groundwork-backed runtime compositions, then rerun spec 091's frozen cold/first/warm performance lane before delivering one PR that closes #624 and #625.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: `IWorkflowExecutableStore`, Microsoft dependency injection/options, `System.Diagnostics.Metrics`, Groundwork runtime stores

**Storage**: Provider-local in-memory bounded caches in front of existing durable Groundwork stores; one compact executable-coordination document kind separates mutable lease/guard state from the immutable executable payload

**Testing**: xUnit provider-neutral behavior/telemetry tests, Groundwork registration/restart tests, existing runtime/HTTP/lifecycle suites, frozen HTTP benchmark

**Target Platform**: Every .NET host using a durable workflow-executable store; built-in composition in SQLite, PostgreSQL, MongoDB, and SQL Server Groundwork runtime/unified providers

**Project Type**: Modular .NET runtime foundation

**Performance Goals**: One provider load per resident artifact; first-after-ready p95 ≤750 ms; 200 warm HTTP requests p95 ≤50 ms

**Constraints**: Bounded memory; no stale values after successful delete; no negative/failure retention; no stampede; no mutable source-reference caching; bounded telemetry cardinality

**Scale/Scope**: One reusable executable-cache decorator/options/telemetry surface, eight Groundwork runtime/unified provider features, one SQLite-only bounded store-adapter cache, compact executable coordination persistence, focused/provider-backed tests, and a final combined benchmark

## Constitution Check

*GATE: Passed before research and re-checked after design. The constitutions remain draft; applicable gates are treated as binding.*

| Gate | Status | Evidence |
|---|---|---|
| Framework §2.1 / §2.20 layering | PASS | Cache semantics live beside the provider-neutral runtime store contract; Groundwork registration remains in its provider implementation layer. |
| Framework §2.5.1 lifetimes | PASS | Process-local cache state and its scoped provider loader share one shell service-provider lifetime. Entries are partitioned by persistence scope; request scopes receive adapters rather than isolated cache instances. |
| Framework §2.6.2 replacement semantics | PASS | The decorator wraps the selected Groundwork implementation; it does not register a competing source of truth. Custom/in-memory providers remain unchanged. |
| Framework §2.12 configuration | PASS WITH DRAFT NOTE | Enabled/capacity are explicit runtime-provider settings. No tenant/workflow classification is asserted while that constitution area is provisional. |
| Framework §2.21 / §2.23 testing | PASS | Logic-bearing concurrency, lifecycle, eviction, error, and telemetry branches receive deterministic tests plus provider-backed registration evidence. |
| Framework §2.22 documentation | PASS | Contract, settings, defaults, telemetry, rollback, and benchmark evidence are recorded in this work unit and the shared performance report. |
| Framework §2.24 sanctioned patterns | PASS | An internal decorator behind an existing store seam is a conventional implementation pattern and adds no new feature-composition mechanism. |
| Elsa §E2.2 Design/Runtime split | PASS | The cache is entirely runtime-side and introduces no Runtime → Design dependency. |
| Elsa §E2.4 shell isolation | PASS | Cache lifetime follows each runtime shell provider and does not share mutable entries across shells. |
| Elsa §E6 naming | PASS | `CachingWorkflowExecutableStore`, `WorkflowExecutableCacheOptions`, and telemetry names state concrete roles. |

## Research Decisions

- Cache at `IWorkflowExecutableStore.FindAsync`, after source-reference resolution has selected an immutable artifact ID. Caching workflow definitions or mutable source references would create invalidation ambiguity.
- Use a locked dictionary plus linked list for deterministic bounded LRU operations. The existing generic cache manager is unbounded and does not guarantee same-key miss coalescing.
- Use one per-key shared in-flight task and remove it on every completion. Positive results enter the LRU; null, cancellation, and failure do not.
- Shared provider loads use an independent cancellation token; each caller may cancel only its own wait. This avoids one caller poisoning all coalesced readers.
- Save and unconditional delete call the provider first, then update/evict cache state. Root-write lease and deletion-guard transitions delegate directly because they are provider-owned durable safety state. A guarded delete evicts only when the provider confirms deletion. Listing delegates without populating cache.
- Wrap only durable Groundwork registrations. Existing in-memory stores already avoid serialization and custom hosts retain explicit selection control.
- Use counters for hit/miss/eviction and a histogram for provider-load duration/outcome. Artifact IDs remain trace/log correlation only, never metric tags.

## Design

### Cache decorator

`WorkflowExecutableCache` owns the capacity-bounded, persistence-partitioned LRU and concurrent in-flight-load map for the shell service-provider lifetime. `CachingWorkflowExecutableStore` is the scoped `IWorkflowExecutableStore` adapter: fast hits promote entries in the shared state, while misses publish one shared load per partition/artifact key. `GroundworkWorkflowExecutableCacheLoader` creates an independently owned persistence-operation scope for a provider miss so the shared load never captures a request-scoped durable store. The load owner records duration/outcome, admits only a positive result, and always removes the in-flight entry.

Save and unconditional delete delegate first and then invalidate the key. Save cannot safely admit the caller-supplied value because the provider contract is idempotent by artifact ID: a non-throwing save may be a no-op that retained an existing provider-authoritative object. Root-write lease and deletion-guard acquire/renew/release/cancel operations pass through unchanged. Guarded delete invalidates only when the provider reports success; a rejected or already-absent guarded delete leaves the local positive entry unchanged under the documented process-local immutable-retention policy. A provider mutation failure leaves the prior cache entry intact because the durable authority did not confirm a state transition. List delegates directly.

### Composition and controls

`AddGroundworkRuntimeStores` registers `GroundworkWorkflowExecutableStore` as a keyed scoped backend and selects either it or a scoped cache adapter as the unkeyed runtime store. When enabled, singleton `WorkflowExecutableCache` and `GroundworkWorkflowExecutableCacheLoader` services provide cross-request reuse without extending request-scoped store lifetimes. Privileged/global reads bypass shared values; successful privileged scoped mutations invalidate their partition, while global/across-scope mutations invalidate the artifact in every resident partition. Original overloads remain compatible and direct-read capable; additive overloads accept `WorkflowExecutableCacheOptions`. Runtime and unified provider features expose default-on `CacheWorkflowExecutables` and `WorkflowExecutableCacheCapacity` (default 256). Artifact IDs are content-addressed and mutable source-reference selection remains authoritative; operators that require coordinated eager eviction can disable the process-local cache until distributed invalidation lands. Invalid enabled capacities fail options validation during composition.

### Measured Groundwork/SQLite residual

The first executable-cache lane showed that tiny warmed artifacts were not the dominant residual.
CPU attribution identified repeated store construction, applied-schema-state deserialization, and
route-plan binding on every Groundwork operation. Groundwork `0.0.1-preview.95` accepts the exact
startup-admitted physical target and exposes a concurrent per-operation SQLite store whose
connections remain independently owned. Elsa compiles route plan sets once and retains a bounded
set of immutable access-bound adapters. SQLite runtime/unified features expose default-on
`ReuseAccessBoundStores` and `AccessBoundStoreCacheCapacity` (default 256); disabling reuse restores
fresh per-operation materialization.

Mutable root-write lease and deletion-guard fields move into a compact
`WorkflowExecutableCoordinationDocumentKind` record instead of rewriting the large immutable
executable JSON. Save/delete update payload and coordination documents atomically. Reads lazily
migrate legacy embedded coordination fields without rewriting the executable payload.

### Evidence

Provider-neutral tests prove all cache state-machine branches, lease/guard pass-through, and both successful and rejected guarded-delete outcomes with a counting controllable store plus the in-memory provider. Groundwork tests prove the DI graph wraps the durable provider and that rebuilding the service provider starts empty. The final Release build is measured against spec 091's frozen baseline: a new 20-boot cold lane and 200-request warm lane must satisfy both specs' budgets.

## Project Structure

```text
specs/092-workflow-executable-cache/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── executable-cache.md
└── tasks.md

src/Elsa/Workflows/Runtime/Core/
├── Models/WorkflowExecutableCacheOptions.cs
├── Services/WorkflowExecutableCache.cs
├── Services/CachingWorkflowExecutableStore.cs
├── Services/InvalidatingWorkflowExecutableStore.cs
└── Diagnostics/WorkflowExecutableCacheTelemetry.cs

src/Elsa/Persistence/Groundwork/
├── DependencyInjection/GroundworkRuntimeStoreRegistration.cs
├── Stores/GroundworkWorkflowExecutableCacheLoader.cs
├── Stores/GroundworkWorkflowExecutableStore.cs
├── Sqlite/AccessBoundGroundworkStoreCache.cs
├── Sqlite/SqliteGroundworkDocumentStoreInitializer.cs
└── {Sqlite,PostgreSql,MongoDb,SqlServer}/...Runtime|Unified...ShellFeature.cs

tests/Elsa/Workflows/Runtime/Tests/
└── CachingWorkflowExecutableStoreTests.cs

tests/Elsa/Persistence/Groundwork/Tests/
└── GroundworkRuntimeStoreRegistrationTests.cs
```

**Structure Decision**: Put reusable cache semantics in Runtime.Core beside the existing store seam and keep concrete wrapping/feature settings in the Groundwork implementation layer. No new project or persistence entity is warranted.

## Post-Design Constitution Re-check

The design adds no public replacement contract, cross-shell singleton, Runtime → Design dependency,
or high-cardinality metric. The compact coordination schema is owned by the existing executable
store contract and preserves provider authority, atomicity, and lazy legacy migration. Groundwork
`0.0.1-preview.95` supplies the exact-target/concurrent-store primitive. All gates remain passing
with the same provisional configuration-classification note.

## Complexity Tracking

No constitutional violations or exceptions are required.
