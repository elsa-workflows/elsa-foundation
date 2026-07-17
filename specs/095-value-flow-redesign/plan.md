# Implementation Plan: Role-Owned Workflow Value Flow

**Branch**: `598-value-flow-redesign` | **Date**: 2026-07-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/095-value-flow-redesign/spec.md`

## Summary

Replace the Elsa 3 memory-address programming surface with a single role-specific path from authored
bindings to durable invocation records. Publishing emits alias-typed literal, workflow-request,
variable, causal result, or explicit expression bindings. Runtime materialization pins one immutable
input snapshot before transient CLR activation, hydrates plain `[ActivityInput]` properties, and commits one
typed completion result plus outcome atomically. Variables remain lexical mutable frames changed only
by explicit intrinsic operations; expressions receive explicit immutable parameters; Elsa 3 memory
references are lowered only inside the importer. Code-first workflows compile through an IDE-guided
builder and generated activity-call facade into the same `WorkflowDefinitionState` and
`WorkflowExecutable` used by dynamic authoring.

This is an intentional public-contract break. No canonical forwarding shim preserves
`IMemoryBlock`, `InputArgument`, `OutputArgument`, ambient expression mutation, or synthetic
workflow-value bags.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (`net10.0`); source generator targets the compiler-compatible
Roslyn surface

**Primary Dependencies**: Existing Elsa Activities/Expressions/Workflows Design, Publishing, and
Runtime modules; Microsoft.Extensions.DependencyInjection; Microsoft.CodeAnalysis.CSharp 5.0.0 for
the authoring generator; BenchmarkDotNet 0.15.8 for the activation-scope evidence harness

**Storage**: Existing `IActivityExecutionStateStore`, `IDurableValueStateStore`, bookmark state, and
`RuntimeCheckpointCommitter`; Groundwork activity-execution documents gain an explicitly versioned
shape for input snapshots, attempts, private state, and completion

**Testing**: xUnit 2.9; contract, architecture, publishing golden, runtime checkpoint/recovery,
expression-engine, activity-library, importer, source-generator, and benchmark verification

**Target Platform**: Cross-platform .NET server/runtime hosts; generated authoring supports normal
SDK-style C# projects and does not become a runtime dependency

**Project Type**: Modular .NET libraries, compiler/source-generator helper, server-side durable
workflow runtime, and benchmark console harness

**Performance Goals**: Intrinsic-only flows create zero CLR activations/scopes; the selected activity
scope policy has measured throughput, p50/p95 latency, and allocation impact against burst-only
activation; input materialization and completion remain linear in declared members

**Constraints**: Runtime remains Design-free and artifact-only; `WorkflowDefinitionState` remains
authored content only; canonical types use aliases/schema descriptors rather than assembly-qualified
names; checkpoints remain the sole atomic persistence path; no service locator on activity or
expression value contexts; no Elsa 3 runtime compatibility dependency

**Scale/Scope**: 34 production files currently use `InputArgument<T>`, 11 use `OutputArgument<T>`, 20
use memory-block/register contracts, and 49 use `IExpressionExecutionContext`; all first-party
activities and direct test objectives are included

## Constitution Check

*GATE: Passed before research and re-checked after design. Both constitutions remain draft/provisional;
this plan applies them as quality gates and does not claim to ratify them.*

| Gate | Result | Evidence / design consequence |
| --- | --- | --- |
| Framework §2.1 three-layer separation | PASS | Role models/contracts remain in their existing `.Core` owners; activation, materialization, persistence, and generators remain outside Core. |
| Framework §2.6.4 design/runtime split | PASS | Code-first builder and generator emit authored state; Publishing alone lowers it to Runtime Core models; Runtime adds no Design reference. |
| Framework §2.16.1 project-size discipline | PASS | One Roslyn helper and one benchmark harness have independent dependency envelopes and substantial behavior; no micro-project is introduced. |
| Framework §2.21.1 / §2.23 test discipline | PASS | `test-migration-ledger.md` records every retained, replaced, or architect-approved removed objective before deletion. Tests are written before each migration slice. |
| Framework §2.24 sanctioned patterns | PASS | The binding discriminated union is a closed domain model, DI activators are factories, and evaluator/contract lookup remains registry/resolver based. No new generic provider framework is introduced. |
| Framework §4.2 Core compatibility | PASS WITH DECLARED MAJOR BREAK | Memory/argument interfaces are intentionally removed from public Core packages. Release notes and package versioning must classify this as a major API break. |
| Elsa §E2.2 Design → Runtime asymmetry | PASS | Design may consume stable runtime-independent activity metadata, but Runtime never consumes builder, generator, catalog, or importer types. Architecture tests enforce the direction. |
| Elsa §E2.6 artifact-only / executable-always-runs | PASS | Activity contract identity, schema fingerprint, result/outcome schema, and activation requirements are publication preflight inputs pinned into the executable. Missing activation capability is not a supported invocation outcome. |
| Elsa §E2.7 import-only Elsa 3 | PASS | Importer-local memory-reference tables lower authored definitions one way; no live-instance resume, dual read, or canonical memory DTO exists. |
| Elsa §E2.9 authored/executable/runtime triplet | PASS | Code-first definitions produce `WorkflowDefinitionState`; Publishing produces `WorkflowExecutable`; snapshots/results/frames/state remain runtime records only. |
| Elsa §E6 naming | PASS | New names use domain nouns (`ActivityContract`, `ActivityInputSnapshot`, `ActivityCompletion`, `ActivityAttempt`, `ExpressionParameters`) and sanctioned `Factory`/`Resolver` suffixes. |
| Groundwork evolution | PASS | Persisted activity-execution changes require a version bump, upcaster, serializer round-trip fixture, and mixed-version recovery test before use. |

### Post-design re-check

The design preserves the gates above. The Roslyn dependency is isolated in
`Elsa.Workflows.Design.CodeGeneration`; activity libraries and runtime packages do not reference it.
The benchmark harness is isolated under `benchmarks/` and cannot affect runtime composition. Runtime
records extend the existing checkpoint state set instead of opening a second persistence route.

## Project Structure

### Documentation (this feature)

```text
specs/095-value-flow-redesign/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── test-migration-ledger.md
├── contracts/
│   ├── activation-scope-benchmark.md
│   ├── activity-invocation-contract.md
│   ├── authoring-contract.md
│   └── expression-and-import-contract.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Activities/Runtime/Core/
├── Attributes/{InputAttribute,OutputAttribute}.cs
├── Contracts/IActivity.cs
└── Models/{ActivityContract,ActivityTransition,ActivityExecutionContext}.cs

src/Elsa/Activities/Primitives/
├── Activation/ClrActivityActivator.cs
└── first-party activity migrations

src/Elsa/Expressions/Core/
├── Contracts/IExpressionEvaluator.cs
└── Models/{ExpressionDefinition,ExpressionParameters,Variable}.cs

src/Elsa/Workflows/Design/Core/
└── Authoring/{WorkflowDefinition,WorkflowBuilder,ActivityArgument,CallHandle}.cs

src/Elsa/Workflows/Design/CodeGeneration/
└── activity-call incremental generator

src/Elsa/Workflows/Publishing/Api/
├── RuntimeInputBindingCompiler.cs
├── ExecutableNodeCompiler.cs
└── contract/preflight validators

src/Elsa/Workflows/Runtime/Core/
└── Models/{RuntimeInputBinding,ActivityInputSnapshot,ActivityAttempt,ActivityCompletion}.cs

src/Elsa/Workflows/Runtime/
├── Services/RuntimeActivityInputMaterializer.cs
├── Services/ActivityResultPublisher.cs
└── invoke/resume/parent-completion handlers

src/Elsa3/Activities/Design/Import/
└── importer-local memory-reference graph and diagnostics

benchmarks/Elsa/Activities/Runtime/Benchmarks/
└── activation scope candidates and reproducible workloads

tests/Elsa/{Activities,Expressions,Workflows,Elsa3,Architecture}/
└── contract, migration, recovery, generator, and boundary tests
```

**Structure Decision**: Reuse the existing domain owners and checkpoint stores. Add only the Roslyn
helper, its tests, and the benchmark harness because they have dependencies forbidden from Core and
independent consumers. The code-first builder lives in existing Workflows Design Core; generated code
exists only in consuming workflow projects. The generator enumerates referenced activity metadata and
therefore does not force activity modules to reference Design or Roslyn.

## Complexity Tracking

| Deliberate complexity | Why needed | Simpler alternative rejected because |
| --- | --- | --- |
| Major public Core break | Canonical packages must stop exposing mutable memory addresses and argument wrappers | Forwarding shims preserve the wrong model and permit new dependencies on it. |
| Source-generator helper | Method-call authoring with named typed arguments and IDE discovery is a required developer experience | Reflection-only builders lose compile-time request/result/output typing; hand-written facades do not scale across activity packages. |
| Versioned activity-execution document expansion | Durable retries/resumptions must pin inputs, attempt identity, private state, and completion | Burst memory or recomputing bindings violates durable invocation semantics. |
| Activation benchmark harness | DI scope lifetime remains an evidence-gated public contract | Selecting per-attempt or burst scope from intuition would ignore the explicit performance/isolation decision gate. |

## Delivery Phases

### Phase 0 — Research and baseline

Research decisions are recorded in [research.md](research.md). Baseline commands and the complete
memory-dependent test-objective inventory are recorded before source deletion. No unresolved
clarification marker remains.

### Phase 1 — Canonical contracts and persistence

Implement the role model, binding alternatives, activity contract fingerprint, invocation snapshot,
attempt, private state, completion, transitions, Groundwork version/upcaster, and architecture guards.
This phase is compile-breaking by design and establishes the target APIs before adapters are removed.

### Phase 2 — Activation, invocation, and first-party activities

Change construction to transient DI activation plus one-time plain-property hydration. Pin input
snapshots before activation, return transitions/results directly, atomically commit completion, and
migrate every first-party activity. Remove argument wrappers, runtime memory seeding, output memory
publication, and `IActivity.SyntheticProperties` as a workflow-value channel.

### Phase 3 — Variables and expressions

Separate authored `Variable<T>` handles from runtime frames, route all writes through explicit
intrinsics, replace ambient expression contexts with immutable parameter maps, and migrate JavaScript
and Liquid. Remove delegates, service location, ambient output access, and JavaScript write-back from
binding expressions.

### Phase 4 — Code-first authoring and canonical equivalence

Add `WorkflowDefinition<TRequest,TResult>`, sequential/structured builders, foundational
`.From(source)` / `.Value(literal)` operations, authoring-only `ActivityArgument<T>`, generated
activity methods, and call handles exposing `Node`, `Result`, `Outputs`, and `Outcomes`. Compile into
the existing authored state and prove equality with dynamic fixtures.

### Phase 5 — Elsa 3 import and clean removal

Lower output-only and combined expression/reference Elsa 3 values through an importer-local graph;
emit path-specific diagnostics for dangling, cross-subtree, ambiguous, and custom references. Remove
all canonical memory interfaces/implementations and pass zero-reference architecture guards.

### Phase 6 — Activation scope evidence and finalization

Implement and run burst-only, per-attempt, and conditional candidates with the agreed workloads. Select
and document the lifetime only after semantic gates and measurements pass. Run focused, full solution,
golden, Groundwork, importer, architecture, and code-first conformance verification; refresh relevant
maps; reconcile every requirement and task.
