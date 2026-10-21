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
| §E2.1 domain boundaries | PASS | `Workflows.Runtime` recognises a scope boundary through a neutral, hashed `ExecutableNode` field (D-3), not through `Activities.Graph` types. Graph inputs and variables are read through `ActivityExecutionScopeKeys` and `RuntimeMetadataKeys`, which already live in `Workflows.Runtime.Core`. |
| §E2.7 Elsa 3 stays behind the import boundary | PASS | Converter changes stay in `Extensions/Elsa3`. Elsa 4 gains nothing Elsa 3-specific. |
| §E5 versioning / §2.23.3 visibility | ACTION | Additive public surface only: `ExecutableNode.ActivityExecutionScope`, `GraphActivityDescriptor.ScopeModel`, optional `OutputBindings` on the occurrence compilation and overlay records, and new `RuntimeMetadataKeys` constants. Update `PublicAPI.Unshipped.txt` in each project. New services are `internal sealed`. |
| §2.6.2 replacement contracts | PASS | None added or changed |
| §2.17 duplication beats dependency | PASS | The ten-line `ActivityResult` reference readers are copied into Graph.Design and not referenced from Validations (R-9). |
| §2.24.2 sanctioned patterns | PASS | Existing patterns only: placement-time id rewriting (as `RewriteStructure`), a provider-side validator, and runtime projection of durable rows. No new extension point. |
| §2.22 / §2.22.1 docs and extension points | ACTION | The authoring reference `docs/reference/activity-definition-authoring-composition.md` (body value model), the glossary row for "Graph activity", the Elsa 3 Mapping README and migration readiness guide, and spec 246 D-8 notes. No contributor or replacement contract changes, so `EXTENSION_POINTS.md` and the generated maps are unchanged. |
| §2.23.1–§2.23.2 tests | PASS | Behaviour tests for each story, each refusal code, remap shape, the legacy flag path, resume and FR-011 non-regression. No new features, so no registration tests. |
| §2.23.5 exception boundaries | PASS | Remap and validation produce diagnostics, not exceptions. Runtime resolution failures keep their existing domain exceptions. |
| ADR 0038 content-addressed executables | PASS | The closed-scope flag is in the hashed descriptor and node field (R-5, D-3). Node metadata is not used for behaviour. |
| ADR 0048 container output captures | ACTION | An amendment note: graph variable rows are the single store and the graph frame is a projection, not a shadow (R-3). Concurrent captures into one graph variable are refused (R-9). |
| ADR 0027 scoped references | PASS | Placement remaps the template's scoped references to placed identities, as line 33 already describes for copied subtrees. |

## Decisions

| # | Decision | Detail |
|---|---|---|
| D-1 | Inputs are re-rooted, not re-addressed | Below a closed boundary, `WorkflowInputEnvelopes` is the boundary's input rows, keyed by `BoundaryReferenceKey`. `WorkflowRequest` bindings and parameters are unchanged (R-2). |
| D-2 | Variables are re-addressed at placement | Body `"workflow"` (and legacy `"activity-graph"`) variable references are rewritten to the placed boundary node id. The runtime's graph frame has that `ScopeId`, so a write can never land on the caller's root (R-3). |
| D-3 | Neutral scope marker on the executable node | The placer sets a hashed `ExecutableNode.ActivityExecutionScope` (`Closed`) on a boundary whose descriptor says `scopeModel: closed`. `WorkflowExecutableHasher.FormatNode` includes it next to the intrinsic. `Workflows.Runtime` keys every re-rooting rule on this field, never on `Activities.Graph` types or unhashed metadata (R-5). |
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
├── Runtime/Models/GraphActivityDescriptor.cs          # ScopeModel
└── Runtime/Services/GraphActivityScope.cs             # BoundaryVariableName on variable rows
docs/reference/activity-definition-authoring-composition.md  # body value model (closed scope, references, captures)
Essentials/src/Workflows/Publishing/
├── Core/Models/ActivityTemplateCompileModels.cs       # OutputBindings on the occurrence compilation
└── Elsa.Workflows.Publishing/Services/
    ├── ActivityTemplatePlacer.cs                      # remap hook, capture compile, scope marker, parent graph variables
    ├── PlacedReferenceRemapper.cs                     # new: D-6
    ├── ActivityTemplateCompiler.cs                    # carry outputs into the overlay
    ├── RuntimeInputBindingCompiler.cs                 # graph-body option: Input → WorkflowRequest
    └── RuntimeOutputCaptureCompiler.cs                # graph-target entry point
Essentials/src/Workflows/Runtime/
├── Core/Models/ExecutableNode.cs                      # ActivityExecutionScope
├── Core/Models/ReusableActivityRuntimeModels.cs       # overlay OutputBindings
├── Core/Constants/RuntimeMetadataKeys.cs              # BoundaryVariableName, CaptureTargetScopeId
└── Elsa.Workflows.Runtime/Services/
    ├── Executables/WorkflowExecutableHasher.cs        # hash the scope marker
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
docs/adr/0048-container-output-captures-resolve-through-producer-visible-variable-frames.md  # amendment note
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
| New hashed `ExecutableNode.ActivityExecutionScope` field (D-3) | Runtime must recognise a closed scope without depending on Graph, and the flag must be content-addressed | Node metadata is not hashed (ADR 0038). Reading the Graph descriptor from `Workflows.Runtime` breaks the domain boundary. |
| Synthetic graph frame (D-5) | Body JavaScript and bindings use the frame machinery | A real stored frame changes the checkpoint, rehydration and retry contracts for the same behaviour (R-3) |
