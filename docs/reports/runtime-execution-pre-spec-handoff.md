# Runtime Execution Pre-Spec Handoff

Status: pre-spec architecture handoff for the architect who will plan runtime execution. This is not an implementation plan and not a Speckit specification.

## Purpose

This report collects the constraints, experiments, risks, and open questions that should be in front of the architect before a runtime execution specification is written.

The unit is done when the next architect has enough input to think through the runtime execution design and split it into whatever Speckit units make sense. It deliberately does not decide the final `WorkflowExecutable` shape, executor topology, persistence model, or workflow-as-activity execution behavior.

## Zoom-Out Check

Program milestone advanced:

- Codebase reality: records the current runtime execution state, weak surfaces, and known shortcuts before implementation starts.
- Workspace split readiness: keeps Design, Runtime, bridge, report, and future spec responsibilities separate so later `elsa-workspace` extraction stays possible.
- Operating model: preserves source-of-truth layers by keeping findings in a report, not in the constitutions or glossary.

This remains the highest-value next step because runtime execution is the handoff point for another architect. A code change now would force decisions into implementation before the execution seam is specified.

Result type: report finding and pre-spec input. New gates belong in the constitutions only after architecture review and ratification. New executable workflow belongs in `docs/skills/` only after this handoff shape proves useful.

## Inputs

- [AGENTS.md](../../AGENTS.md), especially source-of-truth layers, work tracking and drift guard, task paths, and constitution boundary.
- [Elsa constitution](../../.specify/memory/constitution.md), especially `§E2.2`, `§E2.6`, and `§E2.9`.
- [Framework constitution](../../.specify/memory/constitution-framework.md), especially `§2.6`, `§2.7`, `§2.21`, and `§2.23`.
- [Test maturity and weak implementation report](test-maturity-and-weak-implementation-report.md).
- [Unfinished work](unfinished-work.md).
- [Architecture reference map](../maps/architecture-reference-map.md).
- [Project reference map](../maps/project-reference-map.md).
- [Seams reference](../seams.md).
- [Activity construction seam spec](../../specs/006-activity-construction-seam/spec.md).
- Runtime and design source surfaces:
  - `src/Elsa.Workflows.Runtime.Core/`
  - `src/Elsa.Workflows.Design.Core/`
  - `src/Elsa.Activities.Runtime.Core/`
  - `src/Elsa.Activities.Composition.Runtime/`

## Constitution Gates

The next runtime execution spec should treat these as non-negotiable inputs unless an architect explicitly opens a constitution amendment:

- Elsa `§E2.2`: `Elsa.Workflows.Runtime.*` must not directly depend on `Elsa.Workflows.Design.*`.
- Elsa `§E2.6`: Runtime executes runnable artifacts without loading design-side data.
- Elsa `§E2.9`: `WorkflowDefinitionState`, read projections, and `WorkflowExecutable` are separate authoring, reading, and executing scopes.
- Framework `§2.6.4`: design-time and runtime contract consumers require split contracts, each bound to its consumer.
- Framework `§2.7`: bridges/adapters isolate dependencies and connect seams without making either side own the other.
- Framework `§2.21`: refactors preserve existing tests unless removal is explicitly approved.
- Framework `§2.23`: new feature classes and logic-bearing implementations need focused unit tests.

## Current Runtime Surface

`Elsa.Workflows.Runtime.Core` is intentionally minimal. `WorkflowExecutionContext` implements `IWorkflowExecutionContext`, but every public member throws `NotImplementedException`. The project has no direct test-project reference in the generated test map.

The current runtime contracts expose workflow execution identity, variables, workflow inputs, activity outputs, and activity-context lookup for expression execution. These are the pressure points the next spec must either keep, replace, or decompose.

The current activity construction seam from Unit 006 is healthier and should be treated as context, not as something to re-decide casually:

- Design persists activity descriptors as `DescriptorType` plus opaque payload.
- Runtime owns typed deserialization through `IActivityConstructor<TDescriptor>`.
- `IActivityFactory` is the construction entry point.
- Workflow-backed activity construction produces `WorkflowDefinitionActivity` from a `WorkflowIdentity` descriptor.

`WorkflowDefinitionActivity` is construct-only. Its `Execute` override throws `NotSupportedException` and explicitly defers load-and-run behavior to the consumer/pinning/runtime execution unit.

## Minimal Runnable Artifact Questions

The constitution names `WorkflowExecutable` as the runnable artifact but does not pin its shape. The next architect should decide what a minimal runnable artifact must carry before runtime implementation begins.

At minimum, the spec should answer:

- Does the artifact carry a compiled executable graph, serialized runtime node descriptors, already constructed activity objects, or another staged representation?
- How are activity descriptors, author-filled arguments, expression bindings, workflow variables, workflow inputs, workflow outputs, and strategy options represented?
- Does the artifact include only immutable version identity, or does it also include publication/build metadata?
- How does the artifact reference source design entities by foreign key without requiring them for execution?
- What are the failure semantics when an artifact refers to an activity descriptor whose runtime feature is not installed?
- Is `WorkflowExecutable` the final name, or should it remain an architectural placeholder until entity design pins the concrete type?

The answer should preserve the `§E2.9` triplet:

- `WorkflowDefinitionState`: authored graph and authored contract.
- Read models/projections: listing, detail, UI, and visualisation views.
- `WorkflowExecutable` or equivalent: runtime-owned runnable form.

## Allowed Crossing Points

Allowed:

- A compile/publish bridge may read immutable Design-side workflow state and activity catalog data through Design seams.
- That bridge may drive Runtime seams, especially activity construction, to build a runnable artifact.
- The runtime host may load runtime features and runtime-owned providers required to interpret the artifact.
- Application-layer visualisation may traverse foreign keys from executed instance to runnable artifact to source design entities after execution.

Forbidden at execution time:

- Runtime loading `WorkflowDefinitionState` to decide what to run.
- Runtime loading draft/version design rows, layout rows, validation rows, or designer metadata to execute.
- Runtime treating activity catalog rows as live execution dependencies instead of compile/publish inputs.
- `Elsa.Workflows.Runtime.*` directly referencing `Elsa.Workflows.Design.*`, except for the currently documented Runtime JavaScript shortcut while it remains deferred architecture debt.

## Runtime Dependency Envelope

Likely allowed runtime dependencies:

- `Elsa.Activities.Runtime.Core` for activity contracts and execution context shape.
- `Elsa.Expressions.Core` for expression execution contracts.
- `Elsa.Workflows.Primitives` and other neutral primitive/building-block packages.
- Runtime-owned implementations and adapters that interpret artifact content.
- Runtime persistence/storage contracts once their role is specified.

Dependencies requiring explicit review:

- `Elsa.Expressions.*` implementations, especially JavaScript and Jint, because expression execution depends on variables, inputs, outputs, and workflow context.
- `Elsa.Activities.Composition.Runtime`, because workflow-as-activity execution needs pinning, cycle guards, and nested execution semantics.
- `Elsa.Workflows.Runtime.JavaScript`, because it currently carries a direct `Elsa.Workflows.Design.Core` reference as a documented shortcut.

Forbidden unless the constitution changes:

- `Elsa.Workflows.Design.*` as an execution-time dependency of `Elsa.Workflows.Runtime.*`.
- Designer layout, validation, authoring history, or source draft/version data as runtime-required execution inputs.

## Known Experiments and Shortcuts

### Runtime JavaScript design-reference shortcut

`Elsa.Workflows.Runtime.JavaScript` directly references `Elsa.Workflows.Design.Core`. Follow-up review found no active source usage in production runtime JavaScript code; the reference exists because JavaScript function declarations are currently shared across design and runtime surfaces while ownership is unstable.

Classification: known deferred architecture debt.

Pre-spec instruction: do not refactor this as a drive-by fix during runtime execution planning. Keep it named, and require the eventual design-time declaration/runtime binding split after Elsa brain/workspace ownership stabilizes.

### Shared activity/workflow input-output models

Activity input/output models were renamed compared to elsa-core and are currently shared with workflow-level inputs/outputs because the shapes looked similar during experimentation.

Classification: risky experiment.

Pre-spec question: are activity-level I/O and workflow-level I/O the same domain concept, or only similar shapes? They may diverge once runtime binding, default values, validation, expression resolution, persistence, and public API semantics are pinned.

The next spec should avoid assuming that shape similarity is semantic identity. If the shared model remains, the spec should explicitly say why it is stable across both scopes. If it splits, it should define the adapter/translation boundary.

### `ActivityNode` design ownership vs executable graph

`ActivityNode` now belongs to Workflows.Design because an authored workflow graph is a design-time tree. That is consistent with `WorkflowDefinitionState` as the authored document.

The naming risk is that "workflow graph" may also be used for the execution seam or runnable artifact. If runtime uses Design-side `ActivityNode` directly, artifact-only runtime collapses.

Classification: naming and ownership risk.

Pre-spec question: distinguish at least these concepts before coding:

- Authored workflow node: Design-owned `ActivityNode` inside `WorkflowDefinitionState`.
- Executable graph node: Runtime-owned representation inside `WorkflowExecutable` or equivalent.
- Runtime activity instance: live activity object and execution state for one running workflow execution.

The next spec should decide whether a runtime node model is needed, and if so, name it so it cannot be confused with Design's `ActivityNode`.

### Execution context through DI scopes

The current direction assumes execution context can be resolved through DI instead of passed through every method signature. This may reduce signature coupling, but it must be specified carefully.

Classification: lifecycle and scoping risk.

Pre-spec questions:

- Is there one DI scope per workflow execution context?
- Can one workflow instance have multiple workflow execution contexts?
- How do nested workflow-as-activity executions receive their own context without contaminating the parent?
- How do expression evaluators, JavaScript preprocessors, activities, and completion handlers resolve the active context?
- What prevents ambient-context bugs when multiple executions run concurrently?
- Which services are scoped to workflow execution, activity execution, request, tenant, or application?

The next spec should choose an explicit lifetime model before introducing runtime behavior. Passing context everywhere is not automatically better, but DI context resolution must not hide lifecycle boundaries.

### Runtime substrate maturity

Runtime execution depends heavily on variables, expressions, workflow inputs, workflow outputs, activity outputs, activity completion, and storage drivers. Several of these are implemented only far enough to integrate the JavaScript expression library.

Classification: incomplete substrate.

Pre-spec question: what can be ported from elsa-core once boundary rules are pinned, and what must be redesigned for the current Elsa brain architecture?

The architect should not treat JavaScript integration as proof that the full runtime substrate is ready. It is useful evidence for required seams and helper functions, but not a complete runtime design.

### Extension-point catalog wording

`src/Elsa.Workflows.Runtime.Core/EXTENSION_POINTS.md` currently describes activity runtime extension points while anchored in the workflow runtime project. This may be inherited wording from nearby activity runtime concepts.

Classification: documentation drift candidate.

Pre-spec question: once runtime execution ownership is clarified, should workflow runtime and activity runtime extension points be split, renamed, or cross-linked more precisely?

## Risk Register

| Risk | Classification | Impact | Pre-spec action |
|---|---|---|---|
| `Elsa.Workflows.Runtime.Core` is stub-like | Deferred implementation | Runtime cannot execute anything yet | Start with artifact and context decisions, not method patching |
| `WorkflowExecutable` shape is unpinned | Architecture gap | Implementation may smuggle Design models into Runtime | Define minimal runnable artifact contract |
| Runtime loads Design data at execution time | Constitution violation | Breaks `§E2.2` and `§E2.6` | Keep Design reads in compile/publish bridge only |
| `ActivityNode` name crosses authoring/execution meanings | Naming/ownership risk | Design model may become accidental runtime model | Separate authored node, executable node, and runtime instance terms |
| Shared activity/workflow I/O models may be accidental | Domain-model risk | Binding and validation semantics may diverge later | Decide shared concept vs adapter boundary |
| Execution context via DI scopes is unspecified | Lifecycle risk | Context contamination or hidden concurrency bugs | Define scope model per workflow execution context |
| Workflow-as-activity is construct-only | Deferred behavior | Catalog entries may look executable before they are | Specify pinning, nested execution, and cycle guards |
| Runtime JavaScript design reference persists | Known deferred architecture debt | Map reviews may rediscover it as drift | Keep named until declaration/binding split is planned |
| Expressions/variables are only partial | Runtime substrate risk | Execution spec may overfit JavaScript demo behavior | Triage port vs redesign after boundaries are set |
| Runtime tests are thin | Test maturity risk | Regressions may hide behind architecture churn | Plan structural reference tests and focused unit tests |

## Test and Gate Questions

The next Speckit plan should likely include:

- Structural project-reference tests asserting `Elsa.Workflows.Runtime.*` does not depend on `Elsa.Workflows.Design.*`, with the Runtime JavaScript shortcut either explicitly excluded or still flagged as known deferred debt.
- Unit tests for loading a minimal runnable artifact without Design assemblies.
- Unit tests for workflow execution context lifetime and DI scope isolation.
- Unit tests for variable/input/output binding semantics.
- Unit tests for expression evaluation against workflow context.
- Workflow-as-activity tests only after pinning and nested execution semantics are defined.
- Domain failure tests for missing runtime features, unknown activity descriptors, corrupt artifact payloads, and invalid expression/variable references.

Integration testing remains constitutionally out of scope for the current framework unit-test rules. If runtime execution needs TestContainers or deployed-bundle verification, that should be a separate integration-testing policy/work unit.

## Candidate Speckit Starting Scope

Recommended first architect-owned Speckit unit:

`Runtime execution seam and runnable artifact contract`

Likely first-unit boundaries:

- Define the minimal `WorkflowExecutable` or equivalent artifact contract.
- Define compile/publish bridge inputs and outputs.
- Define runtime dependency envelope.
- Define execution context lifetime and DI scoping.
- Decide whether the executable graph has a runtime-owned node model.
- Decide whether shared activity/workflow I/O models stay shared.
- Identify what runtime substrate can be ported from elsa-core after boundaries are pinned.

Likely out of first-unit scope unless the architect chooses otherwise:

- Full workflow scheduler/bookmark behavior.
- Full persistence/storage-driver implementation.
- Full JavaScript declaration split.
- Workflow-as-activity nested execution implementation.
- Static analyzer enforcement.
- Integration test policy.

## What This Report Deliberately Does Not Decide

- The final concrete `WorkflowExecutable` entity or model shape.
- Whether the runtime graph is object-based, descriptor-based, compiled, interpreted, or staged.
- Whether activity and workflow I/O models remain shared.
- Whether `ActivityNode` gets a runtime counterpart and what it is named.
- Whether execution context is always DI-scoped, parameter-passed, or hybrid.
- How workflow-as-activity pinning, cycles, and nested execution are implemented.
- How much elsa-core runtime code should be ported.
- Whether Runtime JavaScript's design-reference shortcut is fixed now or later. Current instruction is later, after ownership stabilizes.
