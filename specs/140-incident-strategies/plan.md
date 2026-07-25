# Implementation Plan: Extensible Incident Strategies

**Branch**: `codex/1015-incident-strategies` | **Date**: 2026-07-24 | **Spec**: [spec.md](spec.md) | **Issue**: [#1015](https://github.com/elsa-workflows/elsa-foundation/issues/1015)

**Input**: Approved feature specification from `/specs/140-incident-strategies/spec.md`

## Summary

Replace the unreleased finite incident-resolution enum and authored strategy type string with a
versioned strategy reference, public strategy/action extension contracts, and immutable durable
resolution outcomes. Publishing resolves and pins the effective strategy into the executable and
includes it in the behavioral hash. Runtime evaluates still-blocking ordinary activity-fault
incidents once per outer workflow drain at causal quiescence, stages extensible action effects in
incident-local capability-limited contexts, and commits the ordered batch atomically. The
Workflow Publishing API exposes descriptor-only discovery without constructing strategy instances.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (`net10.0`)
**Primary Dependencies**: Microsoft.Extensions.DependencyInjection, Elsa Runtime checkpoint/outbox contracts, Elsa Mediator, FastEndpoints, API Capabilities
**Storage**: Existing provider-neutral runtime checkpoint stores plus Groundwork-backed supported persistence providers; clean-break JSON/document shape
**Testing**: xUnit unit, registration, contract, architecture, persistence, and REST-driven PowerShell e2e tests
**Target Platform**: Cross-platform ASP.NET Core host and Elsa runtime libraries
**Project Type**: Modular .NET library and web API monorepo
**Performance Goals**: No strategy construction during discovery; one ordered strategy batch and one resolution checkpoint per successful outer drain; O(n log n) incident ordering with n bounded by one workflow execution
**Constraints**: Runtime must not reference Design; exact pinned identity only; replay-safe atomic state/outbox commit; no raw exception or workflow variables in extension contexts; no retry/suspend/mutation API
**Scale/Scope**: Cross-cutting clean break across Primitives, Design Core, Publishing API, Runtime Core/default implementation, inspection projections, tests, docs, and e2e fixtures

## Constitution Check

*GATE: Pass with one explicitly tracked provisional tension. Re-check after Phase 1 design.*

The framework and Elsa constitutions are draft/provisional with their ratification date still unset. This work
uses them as quality gates without ratifying them.

| Gate | Result | Design response |
|---|---|---|
| Layering and dependency envelope (§2.1, Elsa §E2.2) | Pass | `IncidentStrategyReference` lives in dependency-free `Elsa.Workflows.Primitives`; Runtime never references Design. Public extension contracts live in Runtime Core; orchestration and built-ins live in Runtime. |
| Strategy pattern (§2.24) | Pass | `IIncidentStrategy` is a consumer-selected algorithm family and `IIncidentResolutionAction` is its extensible result object. |
| Cross-feature contribution (§2.6.1) | Pass by registry/startup pattern | `AddIncidentStrategy<T>` contributes immutable registration data; a startup-built registry validates and materializes exact descriptor-to-service mappings before synchronous runtime/discovery lookup. It is not a replacement-contract `IEnumerable<T>` resolver. |
| Replacement contracts (§2.6.2) | Pass | Registry, resolver, batch executor, and clock/checkpoint collaborators each have one selected implementation; duplicate replacements fail diagnostics. |
| Scoped services (§2.3/§2.4) | Pass | Strategy instances resolve in the workflow execution scope; only immutable descriptors/registrations may be singleton. |
| Artifact-only runtime (§E2.6.2) | Pass | Runtime reads only the pinned `WorkflowExecutable.IncidentStrategy`; it never loads authored Design state or the publishing host default. |
| Executable-always-runs (§E2.6.1) | Provisional tension | A custom strategy implementation missing from a deployment is an invalid runtime composition. The accepted safety behavior records `WaitForIntervention` and keeps a pre-start workflow pending rather than throwing or retrying. Publishing/runtime preflight must detect the mismatch when deployment inventory is available. The user accepted this operational fallback for unreleased software; the PR must call out the constitutional tension. |
| Commands/queries (§2.10) | Pass | Discovery is read-only. Strategy actions stage commands but do not expose query/mutation hybrid persistence contracts. |
| Test obligations (§2.21, §2.23) | Pass planned | Existing test objectives are preserved; every new logic-bearing implementation gets branch tests and feature registration is verified. No test deletion is planned. |
| Extension-point documentation (§2.22) | Pass planned | Update Runtime and Publishing API `EXTENSION_POINTS.md`, root index if needed, and feature README wiring/registration notes in the same work unit. |
| Persistence exceptions (§2.23.5) | Pass planned | New public boundaries expose domain exceptions; persistence infrastructure remains behind the checkpoint abstraction. |

## Project Structure

### Documentation (this feature)

```text
specs/140-incident-strategies/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── incident-strategy-api.md
│   └── workflow-publishing.openapi.yaml
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Workflows/
├── Primitives/
│   └── Models/IncidentStrategyReference.cs
├── Design/Core/
│   └── Models/WorkflowStrategyOptions.cs
├── Publishing/Api/
│   ├── Capabilities/
│   ├── Constants/
│   ├── Endpoints/
│   ├── Handlers/
│   ├── Models/
│   ├── Requests/
│   └── Services/{WorkflowExecutableCompiler,WorkflowExecutableHasher}.cs
├── Runtime/Core/
│   ├── Constants/
│   ├── Contracts/
│   ├── Extensions/
│   └── Models/
└── Runtime/
    ├── Extensions/RuntimeCoreServiceCollectionExtensions.cs
    └── Services/
        ├── IncidentStrategyRegistry.cs
        ├── IncidentStrategyResolutionDrainObserver.cs
        ├── IncidentResolutionBatchExecutor.cs
        ├── BlockingIncidentWorkflowFaultObserver.cs
        └── Coalescing/CoalescingRuntimeCheckpointPersistencePolicy.cs

tests/Elsa/Workflows/
├── Design/Tests/
├── Publishing/Api/Tests/
├── Runtime/Tests/
└── Runtime/Api/Tests/

tests/Elsa/Architecture/
e2e-tests/
```

**Structure Decision**: Extend the existing Workflow Primitives, Design, Publishing, and Runtime
domain projects. Do not create a new package: the reference is a primitive, public execution
contracts belong to Runtime Core, and default behavior belongs to the existing Runtime composition
root. Publishing API remains the bridge that can read Design and emit Runtime artifacts.

## Runtime Integration

1. `ActivityFaultIncidentRecorder` keeps its existing immediate first checkpoint and writes an
   ordinary blocking incident with `ResolutionOutcome = null`.
2. One outer `WorkflowDrainOrchestrator` drain processes scheduler work and in-drain outbox
   deliveries until causal quiescence. Structural propagation, absorption, and cancellation use
   their internal outcome-producing operations during these hops.
3. `IncidentStrategyResolutionDrainObserver` runs after poison/system classification and before the
   terminal blocking-incident safety observer. It selects durable ordinary activity-fault incidents
   that are still Blocking and outcome-free, ordered by ordinal IncidentId.
4. The batch executor resolves only the executable's pinned reference, creates a policy-safe
   context, executes the returned action against an incident-local staging context, substitutes a
   fresh runtime-owned Fault action on non-cancellation failures, and accumulates safe state,
   outcome, and strategy-safe intent effects.
5. One deterministic `IncidentResolutionBatchApplied` checkpoint persists all incident changes,
   optional workflow faulting, projections, and outbox intents. A pre-commit failure leaves all
   incidents outcome-free; a committed outcome prevents replay.
6. The legacy terminal observer ignores outcome-bearing incidents and only faults an unhandled
   blocking incident as a final safety fallback. It therefore cannot override Continue or Wait.

## Delivery Slices

1. **Contracts and clean-break state**: primitive reference, outcome/status invariants, public
   strategy/action/context contracts, action/source constants, enum removal, projections.
2. **Registration and publication**: atomic explicit/attribute registration, startup validation,
   defaults, compiler pinning/validation/hash, exact runtime lookup.
3. **Runtime execution**: built-in strategies/actions, guarded staging, ordered drain observer,
   fallback/cancellation behavior, atomic checkpoint and strategy-safe outbox intents.
4. **System paths and inspection**: absorption, suppression, activation, poison, missing deployment,
   API/read models/persistence fixtures.
5. **Discovery and documentation**: permission-protected publishing endpoint, capability relation,
   descriptor-only registry view, extension-point catalogs and README.
6. **Verification**: branch tests, registration/architecture/persistence checks, relevant backend
   e2e suites, bounded self-review, commit/push/PR.

## Complexity Tracking

| Tension | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Missing pinned implementation produces a durable Wait outcome despite provisional executable-always-runs wording | Gives operators deterministic, non-retrying evidence for a deployment composition defect and was explicitly accepted for #1015 | Throwing violates operational safety; silently substituting Fault changes authored policy; serializing arbitrary strategy code into the artifact is not a viable extension model |
