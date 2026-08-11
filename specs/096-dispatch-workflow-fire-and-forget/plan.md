# Implementation Plan: Dispatch a Published Workflow Fire-and-Forget

**Branch**: `codex/dispatch-workflow-program` | **Date**: 2026-07-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/096-dispatch-workflow-fire-and-forget/spec.md`

## Summary

Deliver the first complete DispatchWorkflow tracer bullet through separate runtime and design modules. The design module contributes generic dropdown options and an async executable-node metadata source that resolves one unambiguous live Published source reference and pins its full artifact/source identity through Publishing's named fan-in event and single aggregating handler. The runtime activity stages a deterministic dispatch record and child-start intent into the ordinary activity-completed checkpoint; the #675 global pump invokes a contributed handler, which starts the reserved child through `IWorkflowStartDispatcher` and the configured actor provider. Start models are minimally deepened with typed lineage, correlation, tenant/partition, authority, root-initiator, and run-kind context.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, nullable enabled, implicit usings enabled)

**Primary Dependencies**: Elsa.Activities.Runtime.Core, Elsa.Workflows.Runtime.Core/Runtime, Elsa.Workflows.Design.Core/Persistence.Core, Elsa.Workflows.Publishing.Core/Api, Microsoft.Extensions.DependencyInjection, CShells

**Storage**: Existing checkpoint/outbox stores plus a new first-class workflow-dispatch state dimension; #676 implements in-memory atomic projection and makes unsupported Groundwork composition fail explicitly, while #678 supplies provider-backed durability

**Testing**: xUnit, CLR reconciliation/catalog tests, publishing compiler tests, runtime checkpoint/outbox tests, and the in-memory workflow execution harness

**Target Platform**: Cross-platform .NET server hosts

**Project Type**: Multi-project framework feature with separate runtime and design bridge assemblies

**Performance Goals**: One publication-time live-source lookup per DispatchWorkflow node; one checkpoint write and one post-commit handler lookup per dispatch; no synchronous child materialization on the parent activity path

**Constraints**: Static target only; exact pinned Published source; checkpoint-before-delivery; deterministic identities; no Runtime → Design dependency; no broker, Studio, or construct-only workflow-definition activity dependency

**Scale/Scope**: One complete in-memory fire-and-forget tracer bullet and the reusable lineage/persistence contracts required by later #677–#683 slices

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- **Runtime/Design boundary**: PASS — the runtime assembly references only runtime contracts. The separate design bridge owns options and publication-time enrichment.
- **Artifact-only runtime**: PASS — runtime consumes full artifact/source identity pinned into `ExecutableNode.Metadata`; it never reloads an authored definition.
- **Publication authority**: PASS — options and compilation require one unambiguous live Published source reference. Multiple live sources fail closed rather than selecting arbitrarily.
- **Checkpoint/post-commit rule (ADR 0020)**: PASS — activity completion, output, outcome, dispatch record, and start intent are one checkpoint commit; child start occurs only from outbox delivery.
- **Pipeline/single-writer boundary (ADRs 0029/0031)**: PASS — the parent activity stages work inside its mailbox; cross-execution child start runs from the global pump outside both parent and child mailboxes.
- **Content-addressed artifact rule (ADRs 0038/0040)**: PASS for #676’s minimal pin — exact identity and source provenance are captured. Hashing/retention and replaced/unpublished retained-pin execution are explicitly #677.
- **Contribution semantics**: PASS — executable enrichment uses generic `IExecutableNodeMetadataSource` registrations collected through `ExecutableNodeMetadataCollecting` by Publishing's single `CollectExecutableNodeMetadata` handler; runtime delivery uses #675’s conflict-safe keyed handler mechanism.
- **State/persistence honesty**: PASS — workflow dispatch is a first-class state category. In-memory commit projection is atomic; Groundwork must reject the unsupported dimension until #678 adds it, never ignore it.
- **Authority boundary**: PASS — lineage and execution authority use typed immutable models, not caller-writable workflow inputs or loose metadata. Enforcement hardening remains a later unnumbered concern where not assigned.
- **Testing gate**: PASS — tests cross catalog/options, publication pinning, real checkpoint/outbox/global resumption, real start dispatcher, and actor-provider boundaries.
- **Constitution status**: The constitution remains draft/provisional. Accepted checkpoint/artifact ADRs and current contracts are the controlling gates.

Post-design re-check: PASS. The data model and contracts below preserve these boundaries; no constitution exception is required.

## Project Structure

### Documentation (this feature)

```text
specs/096-dispatch-workflow-fire-and-forget/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Activities/DispatchWorkflow/
├── Runtime/
│   ├── Activities/DispatchWorkflow.cs
│   ├── Constants/
│   ├── Models/
│   ├── Services/ChildStartExecutor.cs
│   ├── DispatchWorkflowRuntimeFeature.cs
│   └── Elsa.Activities.DispatchWorkflow.Runtime.csproj
└── Design/
    ├── Services/WorkflowDefinitionOptionsProvider.cs
    ├── Services/DispatchPinSource.cs
    ├── README.md
    ├── DispatchWorkflowDesignFeature.cs
    └── Elsa.Activities.DispatchWorkflow.Design.csproj

src/Elsa/Workflows/Publishing/Core/
├── Contracts/                       # generic executable-node metadata source/enricher contracts
└── Events/                          # named executable-node metadata fan-in event

src/Elsa/Workflows/Publishing/Api/
├── Handlers/                        # single metadata-source aggregating handler
└── Services/WorkflowExecutableCompiler.cs # deterministic async enrichment before hashing

src/Elsa/Workflows/Runtime/Core/
├── Contracts/                       # dispatch store + activity checkpoint staging surface
└── Models/                          # dispatch record/identity/start intent/authority and state changes

src/Elsa/Workflows/Runtime/Services/
├── SimpleActivityExecutionContext.cs
├── WorkflowStartDispatcher.cs
├── WorkflowStartSchedulerWorkHandler.cs
├── WorkflowCheckpointSchedulerWorkHandler.cs
└── InMemoryRuntimeCheckpointCommitStore.cs

src/Elsa/Activities/Runtime/Services/
└── WorkflowInvokeActivitySchedulerWorkHandler.cs

tests/Elsa/Activities/DispatchWorkflow/Tests/
├── DispatchWorkflowContractTests.cs
├── DispatchWorkflowDesignTests.cs
├── DispatchWorkflowCheckpointTests.cs
└── DispatchWorkflowEndToEndTests.cs

tests/Elsa/Workflows/Runtime/Tests/
└── WorkflowStartLineageTests.cs
```

**Structure Decision**: Use a dedicated feature family with runtime and design assemblies so the logical activity and delivery handler remain design-free while authoring options and publication pinning stay in a third-party design bridge. Add only generic publishing/runtime seams to shared projects.

## Complexity Tracking

No constitution violations require justification.
