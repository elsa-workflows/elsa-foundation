# Extension points — Workflows.Runtime engine package

The implementation half of the runtime domain (framework §2.22.1; split from
`Elsa.Workflows.Runtime.Core` by [ADR 0033](../../../../docs/adr/0033-runtime-core-splits-contracts-from-engine.md)).
This package owns the runtime engine: the scheduler work handlers, drainer, orchestrator,
checkpoint committer and commit stores, coalescing session, execution pipelines, materializers,
resolvers, and the reference `InMemory*` stores. All moved types keep their
`Elsa.Workflows.Runtime.Core.*` namespaces, so no consumer code or serialized identifier changed.

**The contract catalog lives with the contracts:** see
[`Core/EXTENSION_POINTS.md`](Core/EXTENSION_POINTS.md) for every replaceable/implementable runtime
contract. Extend the runtime by implementing a `.Core` contract and registering it in DI; this
package's registrations all use `TryAdd*`, so your registration wins regardless of composition
order. Nothing in this package is intended as a direct extension surface except the entries below.

## `AddWorkflowRuntime(IServiceCollection)`

- **Kind:** Composition root (host-agnostic runtime registration).
- **Usage:** RT-4; renamed from `AddWorkflowRuntimeCore` when it moved here (ADR 0033). Registers
  the full hosting-agnostic runtime so a worker or test harness can compose and drive a drain
  without the API feature. `WorkflowsRuntimeApiFeature` composes it and adds only API/endpoint
  concerns. See `docs/runtime-durable-resumption.md` for the lifetime story.

## `WorkflowsRuntimeCheckpointPersistenceFeature`

- **Kind:** Shell-scoped policy selector and post-provider decorator.
- **Usage:** Configure `Mode` as `Immediate` (default/pass-through) or `Coalesced`, with a positive
  `MaxSegmentCheckpoints` (default 50). The feature implements `IPostConfigureShellServices` so provider packages
  first replace the runtime stores and coalescing then wraps the selected implementations. Duplicate composition is
  idempotent. See `docs/runtime-durable-resumption.md` for the latency, replay, and cap trade-offs.

## Documented ADR 0033 deviations hosted here

Two contract-shaped surfaces live in this package rather than `.Core`, deliberately:

- **`IRuntimeCoalescingSessionAccessor` + `IRuntimeCoalescingDrainScopeFactory`** (namespace
  `Elsa.Workflows.Runtime.Core.Contracts`): both expose the concrete `RuntimeCoalescingSession`
  (engine working state) on their signatures, so they cannot stand ahead of the engine; their only
  consumers are the opt-in coalescing composition in `Runtime.Api` and its tests.
- **`ActivityRuntimePipelineBuilder` + `WorkflowRuntimePipelineBuilder`** (namespace
  `Elsa.Workflows.Runtime.Core.Builders`): the default-plan builders bake the concrete
  checkpoint/invoke middleware (which inject the concrete `RuntimeCheckpointCommitter`) into their
  constructors, and their only production consumer is the composition root here. The declarative
  slot machinery (`RuntimePipelinePlanBuilder`, slot constants, `[RuntimeMiddleware]`, placeholder
  middleware) stays in `.Core`, so third-party middleware authors never need this package at
  compile time.

## Guardrail

The ADR 0033 semantic guard (`RuntimeCoreEngineShapeGuardTests` in `tests/Elsa/Architecture`)
asserts that no concrete engine-role types (`*Service`/`*Handler`/`*Dispatcher`/`*Drainer`/
`*Orchestrator`/`*Materializer`/`*Committer`/`*Scheduler`/`*Router`/`*Pipeline`/`*Session`/
`*Scanner`/`*Processor`, `InMemory*`) exist in the `.Core` assembly, so the contracts layer cannot
silently re-absorb the engine.

## Cross-references

- Contract catalog: [`Core/EXTENSION_POINTS.md`](Core/EXTENSION_POINTS.md).
- Repo-wide index: [`EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.2 + §2.22.1; split rationale: ADR 0033.
