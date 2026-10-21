# Tasks: Graph activity body nodes read the activity's inputs, variables and results

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/graph-scope-references.md](contracts/graph-scope-references.md), [quickstart.md](quickstart.md)

**Tests**: required. Every story changes logic-bearing compile or runtime behaviour (framework §2.23.2).
- The tests use xunit only, with no FluentAssertions.
- Write each story's tests first and run them on unchanged code to confirm they fail for the expected reason. This is
  working practice, not a TDD cadence declaration; the Elsa constitution declares none.
- No new features are added, so no registration tests are needed (§2.23.1).

**Builds**: build and test only the touched test projects, through the build-slot wrapper (AGENTS.md). Allow for
queueing in timeouts.

| Short name | Project |
|---|---|
| Graph tests | `Essentials/tests/Activities/Graph/Tests/Elsa.Activities.Graph.Tests.csproj` |
| Publishing tests | `Essentials/tests/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj` |
| Runtime tests | `Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj` |
| Activities.Runtime tests | `Essentials/tests/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj` |
| Behavioral tests | `Essentials/tests/Activities/Behavioral/Tests/Elsa.Activities.Behavioral.Tests.csproj` |
| Flowchart tests | `Essentials/tests/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj` |
| Elsa 3 mapping tests | `Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj` |
| Elsa 3 import tests | `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Elsa3.Activities.Design.Import.Integration.Tests.csproj` |

**Paths**: `Graph.Design` = `Essentials/src/Activities/Graph/Design`, `Graph.Runtime` = `Essentials/src/Activities/Graph/Runtime`,
`Publishing` = `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services`,
`Runtime.Core` = `Essentials/src/Workflows/Runtime/Core`,
`Runtime.Values` = `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values`,
`Activities.Runtime` = `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services`,
`Studio.Client` = `Studio/src/essentials/Elsa.Studio.Workflows/Client/src`.

## Phase 1: Setup

- [x] T001 Create the worktree `../w3027` on `agent/claude/3027-graph-scope-reads` from `origin/main` (done during specify)
- [ ] T002 Claim #3027 per Issue Bus Lite (org AGENTS.md): assign it and remove `needs-triage` once the owner agrees. Merge `origin/main` into the branch (no rebase).
- [ ] T003 Record a green baseline: build and run the Graph, Publishing, Runtime, Activities.Runtime, Behavioral, Flowchart, Elsa 3 mapping and Elsa 3 import test projects once. Note any test that already fails, so it is not attributed to this unit.

## Phase 2: Foundational (blocks every story)

These tasks add the closed-scope marker and the placement remap. With them in place, a body's references survive
placement. Behaviour changes only below a boundary carrying the marker.

- [ ] T004 Add `ActivityExecutionScope` (enum `ActivityExecutionScopeKind { Closed }`, nullable) to `ExecutableNode` in `Runtime.Core/Models/ExecutableNode.cs`. Add it to every constructor and copy path, and update `Runtime.Core`'s `PublicAPI.Unshipped.txt`.
- [ ] T005 Include `ActivityExecutionScope` in `WorkflowExecutableHasher.FormatNode`, next to the intrinsic segment, in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executables/WorkflowExecutableHasher.cs`. A null value must leave today's hash input byte-identical, so existing artifact hashes are unchanged.
- [ ] T006 [P] Add `RuntimeMetadataKeys.BoundaryVariableName` (`runtime.boundaryVariableName`) and `RuntimeMetadataKeys.CaptureTargetScopeId` (`runtime.captureTargetScopeId`) in `Runtime.Core/Constants/RuntimeMetadataKeys.cs`, next to `BoundaryInputName` (:308), and update `PublicAPI.Unshipped.txt`.
- [ ] T007 [P] Add `ScopeModel` (string, null = legacy) to `GraphActivityDescriptor` in `Graph.Runtime/Models/GraphActivityDescriptor.cs`, deserialised from `scopeModel`. Update `PublicAPI.Unshipped.txt`.
- [ ] T008 In `Graph.Design/Services/GraphActivityProvider.cs`, make `BuildBoundaryPayload` (:1051) write `"scopeModel": "closed"`, and bump `Fingerprint` (:42) to `elsa.activity-graph/compiler/3.0.0`.
- [ ] T009 In `Publishing/ActivityTemplatePlacer.cs`, where the boundary is stamped (`StampBoundaryDescriptor` :426-453, or the boundary node build near :177-193), set `ActivityExecutionScope = Closed` when the boundary descriptor's `scopeModel` is `closed`.
- [ ] T010 Create `Publishing/PlacedReferenceRemapper.cs` (`internal sealed`) with a pure method `ExecutableNode Remap(ExecutableNode node, IReadOnlyDictionary<string,string> nodeIdMap, string? graphScopeId)`. It rewrites only the node's own `InputBindings`, `OutputCaptures` and `IntrinsicVariable`, per the research R-6 table:
  - `ActivityResult.ProducerExecutableNodeId` and `ProducerScopeId`;
  - `VariableRead.DeclaringScopeId`: `"workflow"`/`"activity-graph"` → `graphScopeId`, and a mapped container id → its placed id;
  - expression parameters `VariableExpressionParameterBinding.DeclaringScopeNodeId` and `ActivityResultExpressionParameterBinding.ProducerNodeId`;
  - `IntrinsicVariable.DeclaringScopeId` (same rule as `VariableRead`).

  Ids not in the map stay as they are. Rebuilt bindings keep `Secret`, `ConversionRequest`, `ConversionPlan`, `ParameterConversionPlans`, `Options`, `CapabilityProfile` and `Metadata`. Do not copy the linker rebuild at `ActivityResultConversionPlanLinker.cs:101-112`. A node with nothing to remap is returned as the same instance.
- [ ] T011 Hook the remapper into `ActivityTemplatePlacer.ReconstructOccurrenceRoots` (:331-345). Run it only for a frame whose boundary has `ActivityExecutionScope = Closed`:
  - build the map occurrenceId → placed root id for all of the frame's occurrences;
  - include the template's own body node ids;
  - apply the remapper to every occurrence root, not only those with children;
  - pass the placed boundary node id as `graphScopeId`.
- [ ] T012 [P] Create `Essentials/tests/Workflows/Publishing/Api/Tests/PlacedReferenceRemapperTests.cs`:
  - one test per R-6 shape;
  - `"activity-graph"` treated as an alias;
  - unknown ids left alone;
  - `Secret`, `ConversionRequest` and the conversion plans preserved;
  - the same instance returned when there is nothing to remap.
- [ ] T013 Extend `Essentials/tests/Workflows/Publishing/Api/Tests/ActivityTemplatePlacementTests.cs`:
  - a closed-scope template whose body node B has an `ActivityResult` from body node A gets A's placed id;
  - the same template placed twice gets two independent id sets;
  - a nested graph's boundary bindings referencing an outer body node are mapped to the outer placed id;
  - the boundary carries `ActivityExecutionScope = Closed`;
  - a legacy descriptor without `scopeModel` gets neither the remap nor the marker.
- [ ] T014 Extend `Essentials/tests/Activities/Graph/Tests/GraphActivityProviderTests.cs`: assert the new fingerprint and `scopeModel: closed` in the payload. Extend `Essentials/tests/Workflows/Runtime/Tests` hasher coverage (find the existing `WorkflowExecutableHasher` tests): the marker changes the hash, and a null marker keeps it unchanged.
- [ ] T015 Run the Publishing, Graph and Runtime tests. Everything outside the new tests must stay green.

**Checkpoint**: body references carry placed ids, and nothing runs differently yet.

## Phase 3: User Story 1 - A body node computes from the activity's inputs (P1) 🎯 MVP

**Goal**: below a closed boundary, `WorkflowRequest` bindings and parameters read the graph's inputs, and the
caller's inputs are not visible (plan D-1, D-4).

**Independent test**: "Add two numbers", with `Sum` computed by a body node, returns `5` for `A = 2`, `B = 3`. The
caller has its own `A = 99`.

- [ ] T016 [US1] Write a failing end-to-end test in `Essentials/tests/Activities/Graph/Tests/GraphActivityBodyScopeTests.cs` (new). Follow the publish-and-run helpers in `GraphActivityExecutionTests.cs`.
  - Fixture: a graph activity with inputs `A` and `B`, a body JavaScript node binding `args.a`/`args.b` through `WorkflowRequest` parameters, and an output mapping `Sum` from its result.
  - Cases: a caller passes 2 and 3 and gets 5; a caller with its own request input `A = 99` still gets 5; an optional input with a default that is not passed gives the default; an optional input with no default gives `null`.
- [ ] T017 [US1] In `Runtime.Values/RuntimeInputBindingStateProjection.cs`, add `ProjectGraphInputEnvelopes(IEnumerable<DurableValueState> values, string boundaryExecutionId)`. It selects rows whose `runtime.boundaryExecutionScopeId` equals the boundary and whose `boundaryValueRole` is `input`, keyed by `runtime.boundaryReferenceKey`. Use `RuntimeMetadataKeys` constants only; no `Activities.Graph` reference.
- [ ] T018 [US1] Add a nearest-closed-boundary lookup: given the ancestor states already loaded and the executable, return the first ancestor (not the node itself) whose node has `ActivityExecutionScope = Closed`.
  - Put it as an internal static helper beside `RuntimeContainerScopeService` in `Runtime.Values` (for example `RuntimeActivityExecutionScopeLocator.cs`), so the materializer, the intrinsic executor and the result resolver share it.
  - Walk `ParentActivityExecutionId` as `BuildVisibleFramesAsync` does (:45-57).
- [ ] T019 [US1] In `Runtime.Values/RuntimeActivityInputSnapshotMaterializer.cs` (:32-51), use T017 to build `WorkflowInputEnvelopes` when T018 finds a boundary, instead of `ProjectWorkflowInputEnvelopes`.
- [ ] T020 [US1] Apply the same input rule in `Runtime.Values/WorkflowIntrinsicExecutor.cs` (:606-608).
- [ ] T021 [US1] Make sure the boundary node's own input bindings still resolve in the parent scope. The helper never returns the node itself. Add a nested-graph case to T016: an outer graph passes its own input `X` to an inner graph's input `Y`, and the inner body reads `Y` (Story 4 scenario 3).
- [ ] T022 [US1] Run the T016 tests to green. Then run the Runtime, Flowchart and Behavioral tests (FR-011).

**Checkpoint**: US1 works through the API.

## Phase 4: User Story 2 - A body node reads a graph variable (P1)

**Goal**: the visible frame chain below a closed boundary starts with a read-only graph frame projected from the
scope's variable rows. Caller frames are excluded, and body container frames are kept (plan D-2, D-5).

**Independent test**: graph variable `RunningTotal = 10` read in the body gives a result computed from 10. The
caller's own `RunningTotal = 0` does not affect it.

- [ ] T023 [US2] Add tests to `GraphActivityBodyScopeTests.cs`:
  - a `Variable` binding to a graph variable gives its initial value;
  - JavaScript `variables.RunningTotal`, `getVariable('RunningTotal')` and `getRunningTotal()` give the graph's value;
  - a caller variable with the same name is ignored;
  - a body Sequence with its own variable is read by a node inside it (Story 2 scenario 4);
  - a graph placed twice in one workflow reads independent values;
  - a body suspended on a bookmark and resumed after a restart reads the same input and variable values (FR-008, SC-006). Use the rehydrate harness from `GraphActivityRecoveryTests.cs`.
- [ ] T024 [P] [US2] In `Graph.Runtime/Services/GraphActivityScope.cs` (`NewStateAsync` :314-357), stamp `RuntimeMetadataKeys.BoundaryVariableName` on variable rows. Add a `GraphActivityExecutionTests` assertion for it.
- [ ] T025 [US2] In `Runtime.Values/RuntimeContainerScopeService.cs` `BuildVisibleFramesAsync` (:21-74), when T018 finds a boundary:
  - begin the chain with a synthetic Root `VariableFrameState` (`FrameId = graph:{boundaryExecutionId}`, `ScopeId = boundary ExecutableNodeId`, parent null), with values from the boundary's variable rows;
  - skip the caller root frame and every frame owned by the boundary or its ancestors;
  - keep the frames of descendants between the boundary and the node.
- [ ] T026 [US2] In `RuntimeContainerScopeService.ActivateOwnedFramesAsync` (:76-152), parent a body container's new frame to `graph:{boundaryExecutionId}` when its nearest visible root is the graph frame. Do not persist the synthetic frame.
- [ ] T027 [US2] In the ambient name projection (`ProjectAmbientVisibleVariableEnvelopes`, around :293-346), take names for the graph frame from the rows' `BoundaryVariableName`, not from `executable.WorkflowVariables` (:326-327).
- [ ] T028 [US2] In `WorkflowIntrinsicExecutor.cs` (:721-752), build the same chain. Make the Set owner lookup (:713) unable to resolve the caller's root from inside a body. #3023 builds on this; for now, add a test that the lookup fails closed.
- [ ] T029 [US2] Run the T023 tests to green, then the Runtime, Activities.Runtime (`ContainerScopedVariableEvaluatorTests`, `SetVariableDurabilityExecutionTests`), Flowchart and Behavioral tests.

## Phase 5: User Story 3 - A body node reads another body node's result (P1)

**Goal**: a chained-result body publishes, and each invocation reads its own producer (plan D-7).

**Independent test**: an activity with `compute` → `format` publishes inside a workflow without `VF-COER-001`.
Placed twice, and run in a loop, each invocation reads its own `compute`.

- [ ] T030 [US3] Add tests:
  - in `Essentials/tests/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs`, a workflow using a chained-result body publishes, and the linker finds the placed producer;
  - in `GraphActivityBodyScopeTests.cs`, run with two placements, in a loop with three iterations, and through both a direct `ActivityResult` binding and a JavaScript `ActivityResultExpressionParameterBinding`.
- [ ] T031 [US3] In `Runtime.Values/CausalActivityResultResolver.cs` `IsStructurallyVisible` (:128-140), when the producer has a closed boundary (T018):
  - compare `declaredProducerScopeId` with the producer's `RuntimeMetadataKeys.FlowchartExecutionScopeId` metadata, not `Provenance.ExecutionScopeId`;
  - require producer and consumer to share the same nearest boundary execution id.

  Outside a boundary, behaviour is unchanged.
- [ ] T032 [US3] Extend `Essentials/tests/Workflows/Runtime/Tests/CausalActivityResultResolverTests.cs`: the body scope match, the cross-boundary refusal, and unchanged behaviour outside a boundary.
- [ ] T033 [US3] Run the T030–T032 tests to green.

**Checkpoint**: all P1 stories work through the API. This is the MVP.

## Phase 6: User Story 4 - The activity is a closed scope (P2)

**Goal**: activity publication refuses body references that the activity does not declare (FR-006, FR-007, FR-013;
contract codes).

**Independent test**: each refusal code fires with the node pointer and reference named. A valid body publishes.

- [ ] T034 [US4] Write refusal tests in `Essentials/tests/Activities/Graph/Tests/GraphBodyReferenceValidatorTests.cs` (new), through `GraphActivityProvider.ValidateAsync` and `CompileAsync`:
  - one case per code in [contracts/graph-scope-references.md](contracts/graph-scope-references.md);
  - a valid body with every reference shape, which passes;
  - a nested graph whose inner body reads an outer input, which is refused.
- [ ] T035 [US4] Create `Graph.Design/Services/GraphBodyReferenceValidator.cs` (`internal sealed`). It walks the manifest body and checks:
  - `WorkflowRequest` members and `Input {referenceKey}` against `contract.Inputs`;
  - `"workflow"`/`"activity-graph"` variables against `graph.Variables`;
  - container scopes through `ScopedVariableResolver.Resolve` (`Essentials/src/Workflows/Design/Core/Services/ScopedVariableResolver.cs:14-55`), with graph variables as the root scope;
  - producers against body node ids, and against dependency contract projections when contracts are given.

  Copy the ten-line `ActivityResult` reference readers from `ValueFlowValidator.cs:241-280` (§2.17). Emit diagnostics with the provider's `Diagnostic` helper (:1225), using the contract's codes and messages.
- [ ] T036 [US4] Call the validator from `GraphActivityProvider.ValidateAsync` (:96-112), without projection checks, and from `CompileAsync` (:126-136), with `ResolvedDirectDependencies` contracts.
- [ ] T037 [US4] Add an end-to-end test to `GraphActivityBodyScopeTests.cs`: a body that read the caller's input name before this change is refused at publication. An executable already published under fingerprint 2.0.0, with no marker, still runs as before. Build that executable through a fixture without `scopeModel`.
- [ ] T038 [US4] Run the Graph and Publishing tests to green.

## Phase 7: User Story 5 - A body node captures an output into a graph variable (P2)

**Goal**: a body capture writes the graph scope's variable row in the producer's completion commit (plan D-8, FR-012).

**Independent test**: `lookup` captures `Customer` into `CurrentCustomer`. A later node and the output mapping read
it, the caller's `CurrentCustomer` is unchanged, and the value survives resume after a restart.

- [ ] T039 [US5] Add tests to `GraphActivityBodyScopeTests.cs`:
  - the capture is seen by a later node and by the output mapping;
  - the caller's same-name variable is unchanged;
  - the value is present after suspend, restart and resume (use the rehydrate harness from `GraphActivityRecoveryTests.cs`);
  - a withheld (secret) value capture is refused at run time.

  Add `Activities.Runtime` projector tests to `Essentials/tests/Activities/Runtime/Tests` (next to `OutputCaptureVariableWithholdingRunTests.cs`).
- [ ] T040 [P] [US5] Add optional `OutputBindings` to `ActivityTemplateOccurrenceCompilation` (`Essentials/src/Workflows/Publishing/Core/Models/ActivityTemplateCompileModels.cs:40-43`) and `ExecutableActivityTemplateOccurrenceOverlay` (`Runtime.Core/Models/ReusableActivityRuntimeModels.cs:107-129`). Update both `PublicAPI.Unshipped.txt` files.
- [ ] T041 [US5] Fill `OutputBindings` from the authored occurrence `outputs` in `GraphActivityProvider.cs` (:169-175) and `Publishing/ActivityTemplateCompiler.cs` (:370-383).
- [ ] T042 [US5] Add a graph-target entry point to `Publishing/RuntimeOutputCaptureCompiler.cs`. It accepts `"workflow"`/`"activity-graph"` targets against a given graph variable list, pins the declared type and storage driver, and stamps `RuntimeMetadataKeys.CaptureTargetScopeId` with the placed boundary id. Non-graph targets keep the current refusal (:116-122).
- [ ] T043 [US5] In `ActivityTemplatePlacer.cs`, carry `ParentGraphVariables` on `PlacementFrame`, read from the boundary descriptor's `variables`. Compile each occurrence root's `OutputBindings` through T042 (around :194-199), using the template root's result projections, as `ExecutableNodeCompiler.cs:259-273` does.
- [ ] T044 [US5] In `Activities.Runtime/RuntimeOutputCaptureProjector.cs` (:70-110), add a graph-target bucket for captures stamped `CaptureTargetScopeId`:
  - resolve the producer's nearest closed boundary (T018) and require its `ExecutableNodeId` to equal the target;
  - emit a `DurableValueState` with id `ActivityExecutionScopeKeys.Variable(boundaryExecId, key)`, the scope row metadata (including `BoundaryVariableName`), and type id, kind and schema from the declaration;
  - refuse withheld values.
- [ ] T045 [US5] Pass the producer's ancestor states to `ProjectAsync` at the four call sites in `Activities.Runtime`: `WorkflowInvokeActivitySchedulerWorkHandler.cs` (:594, :694), `WorkflowParentActivityCompletionSchedulerWorkHandler.cs` (:661, :680), `WorkflowResumeBookmarkSchedulerWorkHandler.cs` (:484, :498) and `StructuralCallbackEvaluation.cs` (:374, :388). Commit the graph rows in the same completion commit.
- [ ] T046 [US5] Add the capture codes to `GraphBodyReferenceValidator`: `body-capture-target-unknown`, `body-capture-type-incompatible` (exact alias and collection kind, an `Object` target, or an authored conversion) and `body-capture-concurrent` (two capturing nodes in parallel branches of a Parallel/Fork). Add them to `GraphBodyReferenceValidatorTests.cs`.
- [ ] T047 [US5] Add an ADR 0048 amendment note in `docs/adr/0048-container-output-captures-resolve-through-producer-visible-variable-frames.md`: graph variable rows are the single store, the graph frame is a projection (not a §4 shadow), and §5 concurrency is enforced at activity publication.
- [ ] T048 [US5] Run the Graph, Publishing, Activities.Runtime and Behavioral tests to green.

## Phase 8: User Story 6 - Designers can pick graph values in Studio (P2)

**Goal**: Studio's graph editor offers graph inputs, variables, container variables and earlier results, and its
picks publish and run (plan D-11).

**Independent test**: in Studio, build "Add two numbers" by picking `A` and `B`, publish it, run a caller and get `Sum`.

- [ ] T049 [US6] In `Publishing/RuntimeInputBindingCompiler.cs` (:439-458), add a graph-body compile option, used only by the placer's `CompileOccurrenceInputs` (`ActivityTemplatePlacer.cs:391-411`). With it, `Input {referenceKey}` compiles to `WorkflowRequest(memberKey = referenceKey)`; without it, behaviour is unchanged. Add placement tests for both paths.
- [ ] T050 [P] [US6] In `Studio.Client/ActivityGraphImplementationEditor.tsx:1135`, use `WORKFLOW_SCOPE_ID` (from `./scopedVariables`) instead of `"activity-graph"`. Add `rootActivity: root` to the graph `workflowState` (:161-164, :399-402), so body container variables are offered.
- [ ] T051 [US6] Wire expression tooling into the graph editor's `InspectorPanel` (:812ff): pass `draftId`, `expressionTooling` and `expressionEditorSessionScope`, as `WorkflowEditor.tsx:1051-1053` does. First check whether the expression-tooling server answers for an activity draft.
  - If it does, wire it in.
  - If it needs a new endpoint, stop: draft a follow-up issue, show it to the owner, then narrow FR-009 in `spec.md` and note it in `plan.md` D-11.
- [ ] T052 [US6] Extend `Studio.Client/__tests__/activityGraphImplementationEditor.test.tsx`:
  - a graph variable option is enabled, and a pick stores `declaringScopeId: "workflow"`;
  - an output capture into a graph variable;
  - a container variable is offered;
  - save and reopen round-trips.

  Run `pnpm --filter @elsa-workflows/studio-workflows test`, `pnpm typecheck` and `pnpm lint` from `Studio/`.
- [ ] T053 [US6] Extend `Studio/tests/browser/activity-definition-graph-authoring.spec.ts` with the "Add two numbers" build, publish and caller run. Run `pnpm test:browser -- activity-definition-graph-authoring`.

## Phase 9: User Story 7 - Imported Elsa 3 reusables that read values convert and run (P2)

**Goal**: the D-8 refusal is gone, and reads and memory-reference captures in reusable bodies convert, publish and
run (plan D-12).

**Independent test**: `reusable-reads-input` (whose caller has a different `approve`), `reusable-reads-result` and
`reusable-captures-variable` import, publish and run. `reusable-sets-variable` is still refused under D-7.

- [ ] T054 [US7] Flip the tests in `Extensions/Elsa3/tests/Mapping/Tests` from refusal to conversion, asserting the binding shapes from research R-10:
  - `Elsa3ReusableValueReadTests.cs`: `WorkflowRequest{memberKey:"message"}`, `Variable{variable-total, workflow}`, the memory read as `Variable`, and RunJavaScript as `ActivityResult`;
  - `Elsa3ReusableGuidanceTests.cs` (:72-90): the capture as `Variable{workflow}`;
  - `Elsa3GeneratedAccessorTests.cs` (:106-117): a `WorkflowRequest` for `customInput`.

  Leave `Elsa3ReusableTerminalTests.cs` (:160) and `Elsa3CollectionLoweringTests.cs` (:112) unchanged.
- [ ] T055 [US7] In `Extensions/Elsa3/src/Mapping/Services/Elsa3ValueFlowLowerer.cs`, remove the reusable refusals (:111-126, :514-520) and the constant (:678). Keep the `AccessorScope.IsReusable` semantics (:630, :640).
- [ ] T056 [US7] Remove `ReusableValueReadUnsupported` from `Extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs` (:83-88). In `Extensions/Elsa3/src/Mapping/Conversion/Elsa3ReusableGuidance.cs`, remove `ValueRead`, `Capture` and `ValueReadIssue` (:15, :72-80), and reword `Intrinsic("SetVariable")` (:37-40) and `HttpProjection` (:68-70) so they no longer cite #3027 or say nothing can read.
- [ ] T057 [US7] Check Elsa 3's `UseActivity`/reusable input semantics in the Elsa 3 sources the converter already cites.
  - If inputs are readable as variables (`getVariable("approve")`), add an `IsReusable` fallback from a missing variable to `WorkflowRequest` in `Elsa3ValueFlowLowerer.cs` (:537-541), with a test.
  - Otherwise, keep today's missing-variable diagnostic and add a test pinning it.

  Record the finding in `research.md` R-10.
- [ ] T058 [US7] Add `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Fixtures/reusable-captures-variable.json`: an output memory reference into a declared variable, then a later node that reads it. In `ImportedReusableRunsTests.cs` (:157-165), replace the D-8 refusal theory with run tests for `reusable-reads-input` (`approve` true → Approved, false → Rejected, with a caller whose own `approve` differs), `reusable-reads-result` and `reusable-captures-variable`. Keep the `reusable-sets-variable` refusal. Update the comments (:17-22, :101) and the `description` in `reusable-reads-input.json`.
- [ ] T059 [US7] Run the Elsa 3 mapping and import tests to green.

## Phase 10: Polish and cross-cutting

- [ ] T060 [P] Document the body value model in `docs/reference/activity-definition-authoring-composition.md`: closed scope, what each reference reads, captures, the refusal codes and the compatibility of earlier versions. Update the "Graph activity" row in `docs/glossary/elsa.md` (:60) to say a graph activity is a closed value scope.
- [ ] T061 [P] Update the Elsa 3 docs:
  - `docs/reference/elsa3-migration-readiness.md` (:24, :31, :57);
  - `Extensions/Elsa3/src/Mapping/README.md` (:133-134);
  - `specs/246-publish-imported-reusables/contracts/diagnostic-codes.md` (:32);
  - spec 246 D-8 and its references (`spec.md` :88, :124, :277, :295, :344): mark them "lifted by spec 265".
- [ ] T062 [P] Draft the D-13 issue: plain-workflow `Input` picks compile to a generic expression. Add a test that confirms today's behaviour, then show the draft to the owner before filing it as `needs-triage`.
- [ ] T063 Confirm that no `EXTENSION_POINTS.md` or generated map changes are needed: there are no new contributors, replacement contracts or project references. Regenerate `dependency-map.json` only if project references changed, and check with `git diff --stat` (Windows CRLF note).
- [ ] T064 Run every test project from T003, plus the Studio checks, and compare with the baseline. Set the spec `Status: Implemented — via #<PR>` in this PR before merge. Commit, push, open the PR, and run the review gate (`elsa-auto-review-merge`).

## Dependencies

- **Phase 2** blocks all stories. T004–T009 come before T010–T011.
- **US1 (Phase 3)** comes before US2 and US3, because T018 is shared.
- **US2** and **US3** can run in parallel after US1; they touch different files (`RuntimeContainerScopeService` vs. `CausalActivityResultResolver`). They share only `GraphActivityBodyScopeTests.cs`, so their tests go in separate test methods.
- **US4** needs US1–US3 for its end-to-end case (T037). T034–T036 can start after Phase 2.
- **US5** needs US2 (the graph frame) and US4 (the validator, for T046).
- **US6** needs US1–US5 for the browser test. T049, T050 and T052 can start after US2.
- **US7** needs US1–US3 and US5 to run its fixtures. T054–T057 (conversion only) can start any time.
- **Polish** comes after all stories.

## Parallel examples

- After Phase 2: US2 runtime (T025–T028) ‖ US3 resolver (T031–T032) ‖ US4 validator (T034–T036) ‖ US7 conversion (T054–T057).
- Within Phase 2: T006 ‖ T007 ‖ T012.
- US5: T040 ‖ T044 once T042's metadata key exists.
- Polish: T060 ‖ T061 ‖ T062.

## Implementation strategy

1. **MVP**: Phase 2 plus US1–US3 (the P1 stories). Graph activities built through the API compute with their
   inputs, variables and results. Ship nothing before this point.
2. **Closed scope**: US4 makes refusals happen at publication.
3. **Writes**: US5 adds captures, which #3023 then reuses.
4. **Authoring**: US6 Studio, and US7 Elsa 3.
5. One PR for the unit (owner preference), with the status set in it. If T051 narrows FR-009, its follow-up issue is
   linked from the PR.
