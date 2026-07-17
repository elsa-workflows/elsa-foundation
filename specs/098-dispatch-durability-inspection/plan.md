# Implementation Plan: Durable and Inspectable Detached Dispatch

**Branch**: `codex/dispatch-workflow-program` | **Date**: 2026-07-16 | **Spec**: [spec.md](spec.md)

**Input**: GitHub issue #678 and the approved specification in this work unit.

## Summary

Replace #676's explicit Groundwork deferral with a provider-neutral durable dispatch document and queries. The Groundwork checkpoint writer applies dispatch and child-start outbox changes in one cross-unit transaction. Add the missing provider-backed outbox claim/visibility lease with stale-owner fencing so process failure during delivery is recoverable. A runtime lifecycle service marks successful child admission Started, while checkpoint enrichment projects child terminal state atomically with the child checkpoint. Authenticated runtime-read endpoints expose bounded safe views. A guarded collector retains dispatches while either linked execution exists, and a composition assessment distinguishes process-local in-memory behavior from complete production Groundwork durability.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)
**Primary Dependencies**: Workflows Runtime Core/Services/API, DispatchWorkflow Runtime, Persistence Groundwork, FastEndpoints/Mediator, Tasks/Runtime Resumption
**Storage**: Existing runtime checkpoint document unit-of-work plus a new Groundwork `workflowDispatch` document kind and provider-neutral indexed queries
**Testing**: xUnit with AwesomeAssertions; Groundwork in-memory/SQLite and provider manifest/registration suites; runtime API endpoint tests
**Target Platform**: Cross-platform .NET server hosts
**Project Type**: Multi-project domain libraries with durable provider bridges and HTTP capabilities
**Performance Goals**: Bounded indexed inspection; O(number of matching dispatches) lifecycle/retention work; no full-document-store scans
**Constraints**: Preserve existing public constructors and `IWorkflowDispatchStore`; add query/delete capabilities through separate contracts; safe projections only; no wait/cancel propagation/redrive/TestRun/distributed expansion
**Scale/Scope**: One durable lifecycle document per dispatch, queried by parent, child, status, and collection for guarded cleanup

## Constitution Check

*GATE: Passed before research; re-check after design and implementation.*

- **Core direction**: Dispatch query, lifecycle, safe projection source, readiness result, and retention contracts are runtime-owned. Groundwork and HTTP adapt inward.
- **Checkpoint atomicity**: Pending dispatch, post-commit outbox, parent state, and marker share one Groundwork unit-of-work. Child terminal projection is added before fingerprinting and committed with child state.
- **Provider neutrality**: One manifest declaration and store implementation use Groundwork document/query abstractions; provider suites validate physicalization without domain branches.
- **Artifact-only runtime**: Inspection uses persisted immutable executable/source data; no authoring definition lookup.
- **Access and safety**: Store operations enforce persistence access context; endpoints use `WorkflowRuntimeRead`; views are allowlist projections without values/exceptions.
- **Replay/idempotency**: Existing deterministic identities and marker fingerprint remain authoritative. Lifecycle transitions use shared validation and optimistic concurrency.
- **Compatibility**: Preserve the complete `IWorkflowDispatchStore` interface. Add query/delete capabilities through separate optional contracts implemented by built-in stores. Preserve public constructor overloads.
- **Operability**: Readiness explicitly names process-local vs durable-safe guarantees and missing component codes without secrets.
- **Retention**: Cleanup double-checks both linked execution roots and retains on uncertainty.

No constitutional exception is requested. The constitution remains draft/provisional; this plan follows the repository's current persistence, access, checkpoint, and API gates.

## Architecture and Flow

1. `DispatchWorkflow` stages the Pending record and child-start intent as today.
2. `RuntimeCheckpointCommitter` folds the intent into outbox changes and gives one change set to the provider.
3. Groundwork opens a unit-of-work spanning workflow state, dispatch, outbox, and marker; transactional store adapters apply all changes and commit together.
4. The outbox processor atomically claims a pending/expired item with owner, fencing token, and visibility deadline. Only that claim may acknowledge or record failure. After restart or expiry a new claim resumes delivery. For a retained dispatch start, `WorkflowStartDispatcher` resolves the committed server-owned dispatch record and derives stable internal command, envelope, scheduler-work, and root activity IDs from its dispatch ID without changing the public request. Accepted or duplicate admission calls the lifecycle service to compare-and-save Started. A crash between materialization and Started is repaired by byte-equivalent duplicate redelivery.
5. Before a child terminal checkpoint is fingerprinted, `WorkflowDispatchCheckpointEnricher` queries dispatches linked to that child and appends legal terminal projections. The provider commits them with child state.
6. Runtime list/get handlers query the store with bounded filters and map records to `WorkflowDispatchView`, copying only approved safe fields.
7. `WorkflowDispatchRetentionCollector` queries terminal dispatches, checks both linked execution states, then repeats both checks before delete; nonterminal records and failures retain. Executable garbage collection treats Pending/Started dispatch pins as child artifact roots.
8. Contributed provider-neutral durability evidence feeds `IWorkflowDispatchReadinessAssessor.AssessAsync`. A DispatchWorkflow shell initializer reports the stable Unsafe, ProcessLocal, or DurableReady assessment through host readiness logging without changing existing startup behavior.

## Project Structure

### Documentation

```text
specs/098-dispatch-durability-inspection/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/dispatch-durability-inspection.md
└── tasks.md
```

### Source and Tests

```text
src/Elsa/Workflows/Runtime/Core/                 # query/lifecycle/readiness/retention models and contracts
src/Elsa/Workflows/Runtime/                      # lifecycle service, checkpoint enrichment, collector, readiness
src/Elsa/Activities/DispatchWorkflow/Runtime/   # Started transition after child admission
src/Elsa/Workflows/Runtime/Api/                  # authenticated requests, handlers, views, endpoints, capability links
src/Elsa/Persistence/Groundwork/                 # store, manifest, checkpoint integration, serialization, registration

tests/Elsa/Workflows/Runtime/
tests/Elsa/Activities/DispatchWorkflow/
tests/Elsa/Workflows/Runtime/Api/
tests/Elsa/Persistence/Groundwork/
tests/Elsa/Architecture/
```

**Structure Decision**: Extend the existing runtime, DispatchWorkflow adapter, API, and Groundwork projects; do not create a separate dispatch infrastructure or provider project.

## Ordered Delivery

1. Lock store/query/lifecycle compatibility with failing runtime tests.
2. Add Groundwork manifest/document/store/registration and atomic writer application.
3. Add outbox claim/visibility fencing and restart/crash/duplicate provider tests.
4. Add Started and child-terminal lifecycle projection with replay tests.
5. Add safe authenticated inspection endpoints and security tests.
6. Add retention collector and readiness assessment.
7. Update coverage ledger, fixtures, docs/maps, provider suites, and completion audits.

## Complexity Tracking

No constitution violation requires justification.
