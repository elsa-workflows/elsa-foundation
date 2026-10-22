# Implementation Plan: Graph activity body nodes read the activity's inputs, variables and results

**Branch**: `agent/claude/3027-graph-scope-reads` | **Date**: 2026-10-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/265-graph-activity-scope-reads/spec.md`

## Summary

A graph activity becomes a closed *activity execution scope*, as spec 221 FR-035 intended:

- **Inputs.** Below a closed-scope boundary, a body node's request inputs are the graph's captured inputs. Existing
  `WorkflowRequest` bindings read them, and the caller's inputs are not visible (R-2).
- **Variables.** Body variable references are remapped at placement to the placed boundary node id. The runtime puts
  a read-only graph frame at the root of the visible chain, projected from the scope's existing variable rows, and
  drops the caller's frames (R-3).
- **Results.** Placement remaps template-local producer and container ids to the placed ids (R-6). The causal result
  resolver compares scopes the way a body needs (R-7).
- **Captures.** A body node's output capture into a graph variable writes the scope's variable row (R-4).
- **Publication.** A graph activity refuses body references to anything it does not declare (R-9). Its descriptor
  carries a hashed closed-scope flag, so versions already published keep their behaviour (R-5).
- **Studio.** Studio offers graph variables under the root scope, compiles graph input picks, offers body container
  variables and wires expression tooling for earlier results (R-8).
- **Elsa 3.** The converter drops the D-8 refusal (R-10).

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (Studio client, React)

**Primary Dependencies**: none added

**Storage**: no schema change. Graph variables stay in the existing durable value rows of `GraphActivityScope`. These
rows gain a `BoundaryVariableName` metadata entry.

**Testing**:
- xunit only (no FluentAssertions).
- Graph runtime: `Essentials/tests/Activities/Graph/Tests`.
- Placement and compile: `Essentials/tests/Workflows/Publishing/Api/Tests`.
- Runtime resolution: `Essentials/tests/Workflows/Runtime/Tests`.
- Behaviour drives: `Essentials/tests/Activities/Behavioral/Tests`.
- Elsa 3: `Extensions/Elsa3/tests/Mapping/Tests` and `.../Import/Integration/Tests`.
- Studio: Vitest under `Client/src/__tests__` and the Playwright spec `Studio/tests/browser/activity-definition-graph-authoring.spec.ts`.

**Target Platform**: Elsa 4 server libraries and Studio

**Project Type**: multi-project library change, plus the Studio client

**Performance Goals**: no regression on workflows outside a graph. Below a boundary, the resolution walk reuses the
ancestor states it already loads (`RuntimeContainerScopeService.cs:45-57`).

**Constraints**:
- `Workflows.Runtime` takes no reference to `Activities.Graph` (D-3).
- Published executables keep their behaviour (ADR 0038, R-5).
- Faithful or refuse in the converter.
- Build and test only the touched projects, through the build-slot wrapper (shared machine).

**Scale/Scope**:
- 2 new production types (`PlacedReferenceRemapper`, `GraphBodyReferenceValidator`).
- Edits in about 20 production files, across Graph, Publishing, Workflows.Runtime, Activities.Runtime, Elsa 3 and
  Studio.
- 1 ADR amendment note.

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §E2.2 Runtime takes no Design reference | PASS | Runtime changes stay in `Workflows.Runtime*` and `Activities.Runtime`. Design-side validation is in `Activities.Graph.Design`. No project reference changes. |
| §E2.1 domain boundaries | PASS | `Workflows.Runtime` recognises a scope boundary through neutral node metadata stamped from the hashed descriptor (D-3 as amended by A-1), not through `Activities.Graph` types. Graph inputs and variables are read through `ActivityExecutionScopeKeys` and `RuntimeMetadataKeys`, which already live in `Workflows.Runtime.Core`. |
| §E2.7 Elsa 3 stays behind the import boundary | PASS | Converter changes stay in `Extensions/Elsa3`. Elsa 4 gains nothing Elsa 3-specific. |
| §E5 versioning / §2.23.3 visibility | PASS | Additive public surface: optional `OutputBindings` on the occurrence compilation and overlay records, new `RuntimeMetadataKeys` constants, and `RuntimeContainerScopeService`'s optional store and lookup. One deliberate removal: `Elsa3ActivityDiagnosticCodes.ReusableValueReadUnsupported` (pre-GA cleanup, ADR 0080). None of the touched projects tracks a `PublicAPI` file. New services are internal. |
| §2.6.2 replacement contracts | PASS | None added or changed |
| §2.17 duplication beats dependency | PASS | The ten-line `ActivityResult` reference readers are copied into Graph.Design and not referenced from Validations (R-9). |
| §2.24.2 sanctioned patterns | PASS | Existing patterns only: placement-time id rewriting (as `RewriteStructure`), a provider-side validator, and runtime projection of durable rows. No new extension point. |
| §2.22 / §2.22.1 docs and extension points | ACTION | The authoring reference `docs/reference/activity-definition-authoring-composition.md` (body value model), the glossary row for "Graph activity", the Elsa 3 Mapping README and migration readiness guide, and spec 246 D-8 notes. No contributor or replacement contract changes, so `EXTENSION_POINTS.md` and the generated maps are unchanged. |
| §2.23.1–§2.23.2 tests | PASS | Behaviour tests for each story, each refusal code, remap shape, the legacy flag path, resume and FR-011 non-regression. No new features, so no registration tests. |
| §2.23.5 exception boundaries | PASS | Remap and validation produce diagnostics, not exceptions. Runtime resolution failures keep their existing domain exceptions. |
| ADR 0038 content-addressed executables | PASS | The closed-scope flag is in the hashed descriptor (R-5). The node metadata marker is derived from it, so it never differs between equal hashes (A-1). |
| ADR 0048 container output captures | PASS | Unchanged (A-12). Graph variable rows are the single store and the graph frame is a projection, not a shadow (R-3); the reference doc says so. §5's concurrency refusal is not built for any capture yet; #3198 builds it. |
| ADR 0027 scoped references | PASS | Placement remaps the template's scoped references to placed identities, as line 33 already describes for copied subtrees. |

## Decisions

| # | Decision | Detail |
|---|---|---|
| D-1 | Inputs are re-rooted, not re-addressed | Below a closed boundary, `WorkflowInputEnvelopes` is the boundary's input rows, keyed by `BoundaryReferenceKey`. `WorkflowRequest` bindings and parameters are unchanged (R-2). |
| D-2 | Variables are re-addressed at placement | Body `"workflow"` (and legacy `"activity-graph"`) variable references are rewritten to the placed boundary node id. The runtime's graph frame has that `ScopeId`, so a write can never land on the caller's root (R-3). |
| D-3 | Neutral scope marker on the executable node | *Superseded by A-1:* the placer stamps node metadata `runtime.activityExecutionScope = closed` from the hashed `scopeModel: closed`, and scheduling copies it into the boundary's state. `Workflows.Runtime` keys every re-rooting rule on that marker, never on `Activities.Graph` types (R-5). |
| D-4 | Boundary found by ancestor walk | The nearest ancestor state whose executable node is a closed scope, through `ParentActivityExecutionId`. A node is never its own boundary, so a boundary's own bindings resolve in the parent scope (R-2). |
| D-5 | One store | The graph frame is a synthetic read-only Root frame projected from the variable rows. Captures write the rows. Output mappings and `NewEvaluator` are unchanged (R-3, R-4). |
| D-6 | Remap in the parent placement frame | `PlacedReferenceRemapper` runs in `ReconstructOccurrenceRoots` over each occurrence root's own bindings, captures and intrinsic variable. All constructor fields are kept, and unknown ids are left alone (R-6). |
| D-7 | Result visibility below a boundary | Compare the declared producer scope with the producer's Flowchart-local scope metadata, and require the same nearest boundary execution (R-7). |
| D-8 | Body captures compile at placement | The occurrence `outputs` are carried in the overlay and compiled against the parent graph's variables. They are stamped `runtime.captureTargetScopeId`, and the projector writes the scope row. Withheld values are refused (R-4). |
| D-9 | Publication refusals | `GraphBodyReferenceValidator` with seven `activity.graph.body-*` codes, run from `CompileAsync` and `ValidateAsync` (R-9). Contract: [contracts/graph-scope-references.md](contracts/graph-scope-references.md). |
| D-10 | Fingerprint bump | `elsa.activity-graph/compiler/3.0.0`. Each new publication writes `scopeModel: closed`, and the legacy path is kept only for executables already stored (R-5). |
| D-11 | Studio | Graph variables use `WORKFLOW_SCOPE_ID`; `Input` picks compile to `WorkflowRequest` in a graph body only; `workflowState.rootActivity` lists container variables; the graph editor gets expression tooling for earlier results. If the tooling needs a new server endpoint, the result pickers split to a follow-up issue and FR-009 is narrowed in this unit (R-8, Q-2). |
| D-12 | Elsa 3 | Remove the D-8 refusals, the `ReusableValueReadUnsupported` code and the stale guidance. Add run tests and a capture fixture. Check `getVariable(<input>)` against Elsa 3 semantics before adding a fallback (R-10). |
| D-13 | Plain-workflow `Input` picks | Out of scope (FR-011). The task confirms the behaviour with a test and files a `needs-triage` issue (Q-1). |

## Amendments during implementation (2026-10-22)

| # | Change | Why |
|---|---|---|
| A-1 | **D-3 revised.** The closed-scope marker is executable-node metadata (`runtime.activityExecutionScope = closed`), which placement stamps only from the boundary's hashed descriptor (`scopeModel: closed`). Scheduling copies it into the boundary's activity state, and the runtime walk reads it there. There is no new `ExecutableNode` field and no `GraphActivityDescriptor.ScopeModel` property (T004, T005 and T007 dropped). | The descriptor payload is already in the artifact hash (`WorkflowExecutableHasher.FormatNode`), so two executables with the same hash never differ in the marker. This avoids widening `ExecutableNode`'s seven public constructors. |
| A-2 | **Result visibility below a closed scope** compares no declared structural scope. It requires producer and consumer to share the nearest closed boundary invocation. | Every placed body node carries `activity.definitionVersionId` and so opens its own execution scope. A declared scope can never equal a runtime one there. |
| A-3 | **Body captures** are compiled in the parent placement frame, next to the reference remap, through `RuntimeOutputCaptureCompiler.CompileGraphVariableOutputs`. They are written by `RuntimeOutputCaptureProjector` as the boundary's own `ActivityExecutionScopeKeys.Variable` row. The four completion handlers pass the producer's nearest closed boundary through `RuntimeContainerScopeService.FindClosedScopeBoundaryAsync`, which is looked up only for nodes with such a capture. | The row passes `GraphActivityScope.Rehydrate`'s type checks, so output mappings read it unchanged. |
| A-4 | **`activity.graph.body-capture-concurrent` dropped.** | No concurrency analysis for captures exists, and workflow-root captures don't enforce ADR 0048 §5 either. A slot-based guess would refuse exclusive If/Switch branches. Follow-up issue #3198; see A-12. |
| A-5 | **Container variable checks** confirm that the scope is an ancestor container. They do not check that the container declares the key. | An undeclared key fails at run time, when the frame lookup finds no such variable. The spec's FR-006 is amended to say so. |
| A-6 | **An optional input that was neither passed nor defaulted** reads as `null` below a closed scope. The runtime fills every input that the boundary's pinned contract declares and that has no row. | Spec edge case. The graph runtime's own capture of inputs is unchanged. |
| A-7 | **FR-009 narrowed.** Studio offers graph inputs, graph variables (root scope) and body container variables. Pickers for earlier body results need the graph editor wired to expression tooling, which can't be built or tested on this machine (no Node). That moves to a follow-up issue. The Studio unit test is added, but CI runs it. | D-11 fallback. |
| A-8 | **SC-004 partly blocked** (filed as #3195 and #3196). The Elsa 3 run tests prove body input reads, including D-1 against a caller's own input. Result reads and captures from an imported body can't run end to end, because of two existing limits that affect every converted workflow: a RunJavaScript without arguments faults when it runs, and a RunJavaScript `Object` result has no Auto conversion into a typed input. Captures and results are proven in `GraphBodyScopeTests` (Behavioral) and in placement tests. | Found during T058. Two issue drafts are in the PR description. |
| A-10 | **A graph compiled before closed scopes, nested in a closed one,** reads the enclosing closed graph as its root. The closed root frame is also addressed under the `workflow` scope (`RuntimeClosedScope.AliasWorkflowScope`). A closed body never contains `workflow`, because placement remaps it. | Found in review. Without it, the legacy body's request reads switch to the closed graph while its variable reads fail. |
| A-11 | **Studio `Input` picks** become request reads in the placer (`ActivityTemplatePlacer.ToGraphInputRead`), for occurrences of a closed scope. They are not handled by a `RuntimeInputBindingCompiler` option. | The placer knows whether the parent graph is closed; the binding compiler does not. |
| A-12 | **ADR 0048 amendment withdrawn** (owner, 2026-10-22). ADR 0048 is unchanged; §5's refusal of concurrent captures stays the rule. The storage note (rows are the single store, the frame a projection) moves to `docs/reference/activity-definition-authoring-composition.md`. Tests pin what holds until #3198 lands: publication accepts sequential captures into one graph variable, and a run keeps the last value. | §5 is already accepted but was never built, for workflow-root and container captures either. #3198 builds it for every capture as the next spec, instead of an amendment recording the gap. |
| A-9 | **Runtime tests live in `Essentials/tests/Activities/Behavioral/Tests/GraphBodyScopeTests.cs`.** That project runs real graph boundaries through the engine. It gains a reference to `Elsa.Expressions` for the JavaScript cases. | Not `GraphActivityBodyScopeTests` in the Graph tests, which use hand-made contexts. |

## Project Structure

### Documentation (this feature)

```text
specs/265-graph-activity-scope-reads/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/graph-scope-references.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
Essentials/src/Activities/Graph/
├── Design/Services/GraphActivityProvider.cs           # scopeModel, fingerprint 3.0.0, overlay outputs, validator calls
├── Design/Services/GraphBodyReferenceValidator.cs     # new: D-9
└── Runtime/Services/GraphActivityScope.cs             # BoundaryVariableName on variable rows
docs/reference/activity-definition-authoring-composition.md  # body value model (closed scope, references, captures)
Essentials/src/Workflows/Publishing/
├── Core/Models/ActivityTemplateCompileModels.cs       # OutputBindings on the occurrence compilation
└── Elsa.Workflows.Publishing/Services/
    ├── ActivityTemplatePlacer.cs                      # remap hook, capture compile, scope marker, parent graph variables
    ├── PlacedReferenceRemapper.cs                     # new: D-6
    ├── ActivityTemplateCompiler.cs                    # carry outputs into the overlay
    └── RuntimeOutputCaptureCompiler.cs                # graph-target entry point
Essentials/src/Workflows/Runtime/
├── Core/Models/ReusableActivityRuntimeModels.cs       # overlay OutputBindings
├── Core/Constants/RuntimeMetadataKeys.cs              # BoundaryVariableName, CaptureTargetScopeId
└── Elsa.Workflows.Runtime/Services/
    ├── Values/RuntimeContainerScopeService.cs         # cut the chain, graph frame, names from rows
    ├── Values/RuntimeActivityInputSnapshotMaterializer.cs  # re-rooted request envelopes
    ├── Values/RuntimeInputBindingStateProjection.cs   # project graph inputs by reference key
    ├── Values/WorkflowIntrinsicExecutor.cs            # same chain and inputs; no caller-root owner in a body
    └── Values/CausalActivityResultResolver.cs         # D-7
Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/
├── RuntimeOutputCaptureProjector.cs                   # graph-target bucket
└── (4 capture call sites)                             # pass producer lineage
Extensions/Elsa3/src/
├── Mapping/Services/Elsa3ValueFlowLowerer.cs          # drop D-8 refusals
├── Models/Elsa3ActivityDiagnosticCodes.cs             # remove ReusableValueReadUnsupported
├── Mapping/Conversion/Elsa3ReusableGuidance.cs        # remove/reword guidance
└── Mapping/README.md
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/
└── ActivityGraphImplementationEditor.tsx              # scope id, rootActivity, expression tooling
docs/reference/elsa3-migration-readiness.md
specs/246-publish-imported-reusables/{spec.md,contracts/diagnostic-codes.md}               # D-8 lifted
```

**Structure Decision**: existing projects only; two new internal services.

## Delivery order

Each slice is testable on its own and follows the spec's story priorities:

1. **Foundation**: scope marker, fingerprint, descriptor flag, remapper. With these, body results publish (US3).
2. **Runtime re-rooting**: inputs and the graph frame (US1, US2, US4), and result visibility (US3 end to end).
3. **Publication validation**: US4 refusals, then FR-006.
4. **Captures** (US5).
5. **Studio** (US6).
6. **Elsa 3 converter and docs** (US7).

## Complexity Tracking

| Item | Why needed | Simpler alternative rejected because |
|---|---|---|
| Closed-scope node metadata derived from the hashed descriptor (D-3 as amended by A-1) | Runtime must recognise a closed scope without depending on Graph, and the flag must be content-addressed | A new hashed `ExecutableNode` field widens seven public constructors for no added guarantee. Reading the Graph descriptor from `Workflows.Runtime` breaks the domain boundary. |
| Synthetic graph frame (D-5) | Body JavaScript and bindings use the frame machinery | A real stored frame changes the checkpoint, rehydration and retry contracts for the same behaviour (R-3) |
