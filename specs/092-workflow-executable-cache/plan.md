# Implementation Plan: Bounded Workflow Executable Cache

**Branch**: `codex/624-shell-readiness` | **Date**: 2026-07-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/092-workflow-executable-cache/spec.md`

## Summary

Add a provider-neutral, bounded LRU decorator around durable workflow-executable stores. Positive immutable lookups are cached by artifact ID, concurrent misses are coalesced, mutations preserve durable-store authority, and bounded telemetry makes behavior tunable. Wire the decorator into Groundwork-backed runtime compositions, then rerun spec 091's frozen cold/first/warm performance lane before delivering one PR that closes #624 and #625.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: `IWorkflowExecutableStore`, Microsoft dependency injection/options, `System.Diagnostics.Metrics`, Groundwork runtime stores

**Storage**: Provider-local in-memory bounded cache in front of existing durable Groundwork stores; no new persistent schema

**Testing**: xUnit provider-neutral behavior/telemetry tests, Groundwork registration/restart tests, existing runtime/HTTP/lifecycle suites, frozen HTTP benchmark

**Target Platform**: Every .NET host using a durable workflow-executable store; initial composition in SQLite and PostgreSQL Groundwork runtime/unified providers

**Project Type**: Modular .NET runtime foundation

**Performance Goals**: One provider load per resident artifact; first-after-ready p95 ≤750 ms; 200 warm HTTP requests p95 ≤50 ms

**Constraints**: Bounded memory; no stale values after successful delete; no negative/failure retention; no stampede; no mutable source-reference caching; bounded telemetry cardinality

**Scale/Scope**: One reusable decorator/options/telemetry surface, Groundwork composition for four durable-provider features, focused and provider-backed tests, final combined benchmark

## Constitution Check

*GATE: Passed before research and re-checked after design. The constitutions remain draft; applicable gates are treated as binding.*

| Gate | Status | Evidence |
|---|---|---|
| Framework §2.1 / §2.20 layering | PASS | Cache semantics live beside the provider-neutral runtime store contract; Groundwork registration remains in its provider implementation layer. |
| Framework §2.5.1 lifetimes | PASS | Cache and its durable store share one runtime service-provider/shell lifetime; no process-global mutable state is introduced. |
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
- Save and delete call the provider first, then update/evict cache state. Listing delegates without populating cache.
- Wrap only durable Groundwork registrations. Existing in-memory stores already avoid serialization and custom hosts retain explicit selection control.
- Use counters for hit/miss/eviction and a histogram for provider-load duration/outcome. Artifact IDs remain trace/log correlation only, never metric tags.

## Design

### Cache decorator

`CachingWorkflowExecutableStore` implements the existing store contract and receives the selected concrete provider, options, and telemetry. It owns a capacity-bounded LRU and a concurrent in-flight-load map. Fast hits promote the entry under a short lock. Misses publish a shared provider task; its owner records duration/outcome, admits only a positive result, and removes the in-flight entry in a finally path.

Save delegates first and then admits the supplied immutable executable. Delete delegates first and then evicts. A provider mutation failure leaves the prior cache entry intact because the durable authority did not confirm a state transition. List delegates directly.

### Composition and controls

`AddGroundworkRuntimeStores` registers `GroundworkWorkflowExecutableStore` concretely and selects either it or its cache decorator as `IWorkflowExecutableStore`. Runtime and unified SQLite/PostgreSQL feature settings expose `CacheWorkflowExecutables` (default true) and `WorkflowExecutableCacheCapacity` (default 256) and pass those settings into the shared registration helper. Invalid enabled capacities fail options validation during composition.

### Evidence

Provider-neutral tests prove all state-machine branches with a counting controllable store. Groundwork tests prove the DI graph wraps the durable provider and that rebuilding the service provider starts empty. The final Release build is measured against spec 091's frozen baseline: a new 20-boot cold lane and 200-request warm lane must satisfy both specs' budgets.

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
├── Options/WorkflowExecutableCacheOptions.cs
├── Services/CachingWorkflowExecutableStore.cs
└── Diagnostics/WorkflowExecutableCacheTelemetry.cs

src/Elsa/Persistence/Groundwork/
└── DependencyInjection/GroundworkRuntimeStoreRegistration.cs

tests/Elsa/Workflows/Runtime/Tests/
└── CachingWorkflowExecutableStoreTests.cs

tests/Elsa/Persistence/Groundwork/Tests/
└── GroundworkRuntimeStoreRegistrationTests.cs
```

**Structure Decision**: Put reusable cache semantics in Runtime.Core beside the existing store seam and keep concrete wrapping/feature settings in the Groundwork implementation layer. No new project or persistence entity is warranted.

## Post-Design Constitution Re-check

The design adds no package, schema, public replacement contract, cross-shell singleton, Runtime → Design dependency, or high-cardinality metric. Provider authority and source-reference resolution remain unchanged. All gates remain passing with the same provisional configuration-classification note.

## Complexity Tracking

No constitutional violations or exceptions are required.
