# Research: Graph activity body nodes read the activity's inputs, variables and results

Sources: code on `main` at `4014274b6`, specs 221, 236, 237 and 246, ADRs 0027, 0038 and 0048. Paths are
repository-relative, and all `Essentials/src/...` paths drop the `Essentials/src/` prefix after the first mention.

## R-1 What exists today

- **The scope.** `Essentials/src/Activities/Graph/Runtime/Services/GraphActivityScope.cs`.
  - At entry, `GraphActivity` captures the caller's inputs and initialises graph variables as durable value rows,
    keyed by `ActivityExecutionScopeKeys.Input/Variable(outerActivityExecutionId, referenceKey)`
    (`GraphActivity.cs:177-234`). It commits them with the entry checkpoint.
  - Row metadata: `runtime.boundaryExecutionScopeId`, `boundaryValueRole`, `boundaryReferenceKey` and
    `runtime.storageDriverKey`. Only input rows also carry `BoundaryInputName` (`GraphActivityScope.cs:314-357`).
  - `IGraphActivityInputReader`, `GraphActivityScopeFactory.FindNearest` and `SetVariableAsync` exist (spec 221
    FR-035), but only tests call them.
- **Who reads it.** Only boundary expressions, through `GraphActivity.NewEvaluator` (`GraphActivity.cs:285-362`):
  input defaults, variable `initialValue`s and `outputMappings`.
- **How a body node resolves its inputs.**
  - The resolution context is `RuntimeInputBindingResolutionContext`, built by
    `RuntimeActivityInputSnapshotMaterializer.cs:32-51` and `WorkflowIntrinsicExecutor.cs:606-608`.
  - `WorkflowInputEnvelopes` holds the started workflow's request inputs: every durable value with
    `RuntimeMetadataKeys.InputName` (`RuntimeInputBindingStateProjection.cs:32-34`).
  - The visible frames from `RuntimeContainerScopeService.BuildVisibleFramesAsync` (:21-74) always begin with the
    caller's `"workflow"` root frame. The compiled boundary node has no `Structure`, so it owns no frame.
- **Placement.** `ActivityTemplatePlacer` gives each placed node `node-{sha256(origin + node:{templateNodeId})}`
  (:136-143, :469-480). It remaps structure, resume targets, layout and the descriptor entry, but not input
  bindings, intrinsic variables or captures (:191-199, :391-411).
  - A body `ActivityResult` keeps its template-local `producerNodeId`, so `ActivityResultConversionPlanLinker`
    (:65-75) refuses it with `VF-COER-001`.
- **Captures.** Authored occurrence `outputs` reach the descriptor's informational `outputBindings`
  (`GraphActivityProvider.cs:1086`), but no overlay or executable node. `RuntimeOutputCaptureCompiler` (:112-130)
  accepts only workflow-scope targets.
- **Studio.**
  - Graph variables are offered as `scopeId: "activity-graph"` (`ActivityGraphImplementationEditor.tsx:1135`), but
    the type index looks them up under `"workflow"` (`variableReferenceContribution.tsx:132-136`), so the options
    render disabled.
  - Graph inputs are picked as syntax `"Input"` `{referenceKey}`, which the binding compiler turns into a generic
    expression (`RuntimeInputBindingCompiler.cs:439-458`).
  - Earlier body results are offered nowhere in the graph editor.

So the caller's values leak in, the graph's own values cannot be reached, and body result references cannot survive
placement. That is #3027 as reported.

## R-2 How a body node sees the graph's inputs: re-rooted request envelopes

**Decision.** For a node whose nearest enclosing graph boundary is closed-scope (R-5), the resolution context's
`WorkflowInputEnvelopes` holds that boundary's captured input rows, keyed by `BoundaryReferenceKey`. The started
workflow's inputs are not included. The existing `WorkflowRequest` binding and its JavaScript parameter form
(`WorkflowRequestExpressionParameterBinding`) then read the graph's inputs unchanged. No new binding kind is needed.

**Keying.** The envelopes are keyed by the input's reference key, because Studio stores the contract input's
`referenceKey` and the Elsa 3 importer sets reference key = name (`Elsa3ArgumentDefinitionToInputOutput.cs:11-16`).
A body `WorkflowRequest.memberKey` is a graph input reference key.

**Finding the boundary.** `BuildVisibleFramesAsync` already loads every ancestor state through
`ParentActivityExecutionId` (`RuntimeContainerScopeService.cs:45-57`). Walking that chain up to the first state whose
executable node is a closed-scope graph boundary finds the scope.
- `Provenance.ExecutionScopeId` is not used. Every placed activity opens a scope, not just graph activities
  (`WorkflowScheduleActivitySchedulerWorkHandler.cs:218`), and Flowchart writes local ids into it.
- A boundary is never its own ancestor. So the boundary's own input bindings, and a nested boundary's bindings,
  resolve against the parent scope, as FR-007 and Story 4 scenario 3 need.
- `GraphActivityScopeFactory.FindNearest` returns a node's *own* scope first (`GraphActivityScope.cs:558-576`), so it
  must not be used for the boundary's own inputs.

**Alternative rejected.** A new `GraphInput` binding kind: it means new authoring, compile and hashing surface for
the same meaning that `WorkflowRequest` already has inside a template, and the Elsa 3 lowering already emits it.

## R-3 How a body node sees graph variables: an explicit graph frame

**Decision.**
- **Authoring.** Inside a graph template, `declaringScopeId: "workflow"` means the template's root, that is the graph.
  This is what the Elsa 3 lowering emits (`Elsa3ValueFlowLowerer.cs:184-196`, :341-360), and what Studio emits after
  the R-8 fix.
- **Placement.** The placer rewrites every body `"workflow"` variable reference to the *placed boundary node id*:
  `VariableRead.DeclaringScopeId`, the parameter `DeclaringScopeNodeId`, capture targets and `IntrinsicVariable`.
  The legacy Studio id `"activity-graph"` is read as an alias of `"workflow"`.
- **Runtime.** For a body node, the visible frame chain starts with a synthetic, read-only Root-kind frame:
  - `FrameId = activity-scope:{boundaryExecutionId}`, `ScopeId = placed boundary node id`, parent null.
  - It is projected from the scope's variable rows, so there is no second store.
  - The caller's root frame, caller containers and iterations, and the boundary's own iteration frame are excluded.
  - Body container frames below the boundary stay visible and are parented to the graph frame. Its deterministic
    `FrameId` lets `ActivateOwnedFramesAsync` (:124-144) do this.

**Why not leave `"workflow"` in place and re-root it at runtime.** Every root-frame writer would silently target the
caller:
- the capture compiler (`RuntimeOutputCaptureCompiler.cs:119`);
- capture write-back (`RuntimeOutputCaptureProjection.cs:71-108`);
- Set's owner lookup (`WorkflowIntrinsicExecutor.cs:713`);
- the ambient name projection over `executable.WorkflowVariables` (`RuntimeContainerScopeService.cs:326-327`).

ADR 0048 §2 forbids such ambiguous targets. An explicit address makes a wrong write a resolution failure, not a
corruption.

**Names.** JavaScript ambient access (`variables.x`, `getVariable('x')`, `get<X>()`) needs names. Variable rows gain
`BoundaryVariableName` metadata, as input rows already carry `BoundaryInputName`. The ambient projection reads names
from the rows, not from `executable.WorkflowVariables`, so `Workflows.Runtime` does not depend on `Activities.Graph`.

**Storage trade-off.** ADR 0048 §4 forbids durable rows that *shadow* a frame. Here the rows are the only store, and
the frame is a projection of them. The reference doc says so (plan A-12).
- The rows have no frame revision guard, so concurrent captures into the same graph variable must be refused at
  publication, as ADR 0048 §5 already requires for frames.
- **Alternative rejected:** a real container frame stored on the boundary activation. It changes the checkpoint
  participant contract, rehydration, retry and the declaration projector, for no behaviour the projection lacks.

## R-4 Captures into graph variables (D-2)

**Compile.**
- `ActivityTemplateOccurrenceCompilation` and `ExecutableActivityTemplateOccurrenceOverlay` gain optional
  `OutputBindings`.
- `GraphActivityProvider` (:169-175) and `ActivityTemplateCompiler` (:370-383) carry the authored occurrence
  `outputs` into them.
- The placer compiles them through `RuntimeOutputCaptureCompiler.CompileResultProjectionOutputs`, the leaf-node path
  (`ExecutableNodeCompiler.cs:259-273`). It does so against the parent graph's variables, read from the parent
  boundary descriptor's `variables` and carried on `PlacementFrame`.
- A new compiler entry point accepts a graph target. The capture is stamped with
  `runtime.captureTargetScopeId = <placed boundary node id>` and pinned to the declared type and storage driver.
  Capture metadata is hashed (`WorkflowExecutableHasher.cs:173`).

**Runtime.**
- `RuntimeOutputCaptureProjector` (:70-110) routes captures stamped with a graph target to a third bucket.
- It finds the producer's nearest boundary by the R-2 walk, and requires that boundary's `ExecutableNodeId` to equal
  the target scope.
- It emits the scope's own variable row:
  - id `ActivityExecutionScopeKeys.Variable(boundaryExecId, key)`, with the same metadata;
  - `Type.Id`, `Kind` and `Schema` equal to the declaration, which `Rehydrate` checks
    (`GraphActivityScope.cs:430-452`, :525-549).
- The row is committed with the producer's completion, at the same four call sites as workflow captures:
  `WorkflowInvokeActivitySchedulerWorkHandler`, `WorkflowParentActivityCompletionSchedulerWorkHandler`,
  `WorkflowResumeBookmarkSchedulerWorkHandler` and `StructuralCallbackEvaluation`. `ProjectAsync` gains the
  producer's ancestor states.
- A withheld (secret) value into a graph variable is refused, as for durable outputs (:77-80), because graph rows
  have no withheld form.
- Output mappings and `NewEvaluator` read the same rows, so they see captured values with no graph runtime change.

## R-5 Published versions keep their behaviour: a hashed closed-scope flag

**Decision.**
- `BuildBoundaryPayload` (`GraphActivityProvider.cs:1051`) writes `"scopeModel": "closed"` into the boundary
  descriptor, and the provider fingerprint moves to `elsa.activity-graph/compiler/3.0.0` (:42).
- `GraphActivityDescriptor` gains the property. Unknown or absent means legacy.
- The placer copies the flag onto a neutral, hashed `ExecutableNode.ActivityExecutionScope` field, which
  `WorkflowExecutableHasher.FormatNode` includes next to the intrinsic (:197-199). `Workflows.Runtime` cannot read
  the Graph descriptor without depending on `Activities.Graph` (plan D-3).
- The runtime re-roots (R-2, R-3, R-4) only below a node with that field set to `Closed`.

**Why the descriptor and not node metadata.** `WorkflowExecutableHasher.FormatNode` (:182-212) hashes the descriptor
payload, bindings, captures, structure and intrinsic, but not node metadata. A metadata flag would change behaviour
without changing the artifact hash, which breaks ADR 0038. With the flag in the descriptor:
- executables already published keep running as compiled;
- in-flight instances of them keep their frames, because no `AddFrame` parent check fails;
- a re-published activity gets a new template hash and new behaviour.

The template hash changes anyway, because the fingerprint and payload are hashed (`ActivityTemplateCompiler.cs:454-462`).
No test pins `compiler/2.0.0`.

## R-6 Remapping template-local ids at placement (FR-005)

**Decision.** A new `PlacedReferenceRemapper` in `Elsa.Workflows.Publishing`.
- **Where.** The parent placement frame runs it after its children are built, in `ReconstructOccurrenceRoots`
  (`ActivityTemplatePlacer.cs:331-345`). That method already builds the occurrence-id → placed-id map used by
  `RewriteStructure`.
- **Why there.** A child frame cannot remap, because when it compiles its inputs (:391-411) its siblings' placed ids
  do not exist yet. Placed ids depend on the invocation origin (:131-139), so the template compiler cannot remap
  either.
- **What it does.** For every occurrence root (not only those with children), it rewrites that node's own
  `InputBindings`, `OutputCaptures` and `IntrinsicVariable` from the frame map. It never walks into descendants,
  because a nested graph's body was already remapped by its own frame.

The binding model is closed. `RuntimeInputBinding` has 6 sources, and expression parameters are a fixed set
(`Expressions/Core/Models/ExpressionDefinition.cs:106-177`).

| Shape | Field | Rule |
|---|---|---|
| `ActivityResult` | `ProducerExecutableNodeId`, `ProducerScopeId` | Map a body node id to its placed id |
| `VariableRead` | `DeclaringScopeId` | `"workflow"` / `"activity-graph"` → placed boundary id; a body container id → its placed id |
| `VariableExpressionParameterBinding` | `DeclaringScopeNodeId` | As `VariableRead` |
| `ActivityResultExpressionParameterBinding` | `ProducerNodeId` | Map |
| `WorkflowRequest` and its parameter | none | Unchanged (re-rooted at runtime) |
| Capture target | `runtime.captureTargetScopeId` | Set to the placed boundary id |
| `IntrinsicVariable` | `DeclaringScopeId` | As `VariableRead`; #3023 relies on it |

- **Rebuilding.** A rebuilt binding keeps every constructor argument: `Secret`, `ConversionRequest`,
  `ConversionPlan`, `ParameterConversionPlans`, `Options`, `CapabilityProfile` and `Metadata`. The linker's rebuild
  at `ActivityResultConversionPlanLinker.cs:101-112` drops `Secret` and `ConversionRequest`, so it is not copied.
- **Unknown ids.** These are left as they are. FR-006 validation rules them out for `closed` templates, and a legacy
  template still fails at `VF-COER-001` as today.
- **Scope.** The remap runs only for `closed` boundaries (R-5).
- **Hashes.** The artifact hash changes only for executables whose bodies hold references, which were broken or
  wrong before.
- **Other callers.** `ActivityDraftTestRunService` (:222-231) places templates through the same placer and gets the
  remap for free.

## R-7 Activity results inside a body (FR-004)

Once producer ids are placed, `CausalActivityResultResolver` lineage is correct: it walks only backwards through
scheduling and parent links (:88-121). An earlier invocation's body nodes are children of another boundary
execution, not predecessors, so loop iterations and two placements stay independent.

**Gap.**
- `IsStructurallyVisible` (:128-140) compares the declared `ProducerScopeId` with the producer's
  `Provenance.ExecutionScopeId`.
- Inside a body, that value is the boundary's outer execution id (`GraphActivity.cs:77`, `SchedulerWorkItems.cs:151`).
  A Flowchart body's local scope moves to the `flowchart.executionScopeId` metadata.
- So every body `ActivityResult` that carries a producer scope would read as Unavailable.

**Decision.** When the producer's nearest boundary is a closed-scope boundary:
- require producer and consumer to share that nearest boundary execution, which is the FR-004 guarantee;
- do not compare the declared scope at all. *(Revised during implementation, plan A-2: every placed body node opens its
  own execution scope, so no declared scope matches a runtime one, Flowchart-local or not.)*

JavaScript `ActivityResultExpressionParameterBinding` passes no scope (:49) and needs only the remap.

## R-8 Studio

- **Variables.** Graph variables are offered with `scopeId: WORKFLOW_SCOPE_ID` instead of `"activity-graph"`
  (`ActivityGraphImplementationEditor.tsx:1135`). Picks then store `declaringScopeId: "workflow"`, and the type
  index finds them. This one line fixes body input picks, output mapping picks and output capture picks
  (`ActivityOutputsPanel.tsx:130-144`).
- **Inputs.** A body pick stores `{expressionType: "Input", value: {referenceKey}}` (`inputReferenceContribution.tsx`,
  `activityInputWire.ts:334`). The placer's occurrence compile (`CompileOccurrenceInputs`) passes a graph-body option
  to `RuntimeInputBindingCompiler`, which compiles `Input {referenceKey}` to `WorkflowRequest(memberKey = referenceKey)`.
  - Outside a graph body, `Input` keeps compiling as today (FR-011). Its plain-workflow meaning is out of scope
    (Q-1 below).
- **Container variables.** The graph editor's `workflowState` gains `rootActivity`, so body container variables are
  offered like in the workflow editor.
- **Earlier body results.** The graph editor passes `draftId`, `expressionTooling` and the session scope to
  `InspectorPanel` (:812ff), as `WorkflowEditor.tsx:1051-1053` does. Results then reach expressions through the
  existing tooling symbols.
  - No non-expression `ActivityResult` picker exists in either editor, and none is added.
  - The tooling server side must answer for an activity draft. If it cannot without a new endpoint, the result
    pickers split off to a follow-up issue, and FR-009 is narrowed in this unit (Q-2).
- **Tests.** Vitest in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__`, extending
  `activityGraphImplementationEditor.test.tsx` (its Input picker test is at :254), plus
  `Studio/tests/browser/activity-definition-graph-authoring.spec.ts` for the end-to-end build of "Add two numbers".
  No test pins `"activity-graph"` as a scope id. There are no `.razor` files, so there is no C# Studio gate.

## R-9 Publication validation (FR-006, FR-013)

**Decision.** A new `GraphBodyReferenceValidator` in `Activities/Graph/Design/Services`.
- `GraphActivityProvider.CompileAsync` (:126-136) calls it with the resolved dependency contracts.
- `ValidateAsync` (:96-112) also calls it, without projection checks, so Studio gets early feedback.

**Checks**, each refusal naming the node pointer and the reference:

| Code | Refused when |
|---|---|
| `activity.graph.body-input-unknown` | A `WorkflowRequest` member or an `Input` reference is not a declared graph input |
| `activity.graph.body-variable-unknown` | A `"workflow"`/`"activity-graph"` variable read or parameter is not a declared graph variable |
| `activity.graph.body-variable-scope-invisible` | A container scope is not an ancestor container of the node inside the body |
| `activity.graph.body-producer-unknown` | A producer is not a body node, or has no such projection in its contract |
| `activity.graph.body-capture-target-unknown` | A capture targets a variable that is not a graph variable, or a non-graph scope (FR-013) |
| `activity.graph.body-capture-type-incompatible` | A capture target's type does not match (see below) |
| `activity.graph.body-capture-concurrent` | Two nodes that can run concurrently capture into the same graph variable (ADR 0048 §5) |

- **Reuse.** `ScopedVariableResolver.Resolve` (`Workflows/Design/Core/Services/ScopedVariableResolver.cs:14-55`) gives
  the container ancestry, with the graph variables passed as the root scope.
- **Copy, don't reference.** The `ActivityResult` reference readers in `ValueFlowValidator.cs:241-280` live in the
  Validations implementation project, which Graph.Design does not reference. They are about ten lines, so they are
  copied (§2.17).
- **Type check.** Exact alias and collection kind match, an `Object` target, or an authored conversion.
  `ValueConversionPlanResolver` is not reachable from Graph.Design. Placement compile stays the authoritative second
  check.
- **Concurrency.** A Parallel/Fork body with two branches capturing into one variable is refused. A sequential
  re-capture is allowed.
- **Ambient JavaScript.** `variables.x`, `getVariable('x')` and `get<X>()` cannot be checked statically, so they
  resolve at run time only. This matches workflows.

## R-10 Elsa 3 converter (FR-010)

The lowering already emits the right shapes for a body under R-2 and R-3.

| Elsa 3 construct | Lowering | Location |
|---|---|---|
| `getInput(x)` | `WorkflowRequest(x)` | `Elsa3ValueFlowLowerer.cs:524-534` |
| Variable read and capture | `Variable{referenceKey: elsa3Variable.Id, "workflow"}` | :184-196, :341-360 |
| Carried and loop values | Body container scope | :229-240, :386-396 |
| Producer result | `ActivityResult{producerNodeId, projectionKey, producerScopeId}` | :273-280 |

Keys match the manifest: variable reference key = `source.Id` (`Elsa3VariableMapper.cs:23`), and input reference
key = name.

**Remove.**
- In `Elsa3ValueFlowLowerer.cs`: the reusable refusal blocks at :111-126 and :514-520, and the constant at :678.
- `Elsa3ActivityDiagnosticCodes.ReusableValueReadUnsupported` (:83-88).
- `Elsa3ReusableGuidance.ValueRead`, `Capture` and `ValueReadIssue` (:15, :72-80).
- Reword `Intrinsic("SetVariable")` (:37-40) and `HttpProjection` (:68-70), which cite #3027.
- D-7 (`RefuseIntrinsics`, `Elsa3DefinitionConverter.cs`) stays.

**Residual gap.** In an Elsa 3 reusable, inputs could also be read as variables (`getVariable("approve")`). The
lowering resolves these only against `definition.Variables` (:537-541), so they become MissingAmbient.
- The task checks Elsa 3's `UseActivity` input semantics.
- If they show that inputs are readable as variables, an `IsReusable` fallback to `WorkflowRequest` is faithful.
- If not, the read stays refused under the existing missing-variable diagnostic, so no new code is needed.

**Tests.**

| Test | Today | After |
|---|---|---|
| `Elsa3ReusableValueReadTests` | Refusal | Converts, asserting the binding shapes |
| `Elsa3ReusableGuidanceTests` (:72-90) | Refusal | Converts (the HTTP projection test at :60-68 stays) |
| `Elsa3GeneratedAccessorTests` (:106-117) | Refusal | Converts |
| `Elsa3ReusableTerminalTests` (:160), `Elsa3CollectionLoweringTests` (:112) | Intrinsic refusal | Unchanged (still D-7) |
| `ImportedReusableRunsTests` (:157-165) | Refusals | Run tests for `reusable-reads-input` (a caller whose own `approve` differs gets the branch it passed), `reusable-reads-result` and a new `reusable-captures-variable.json` |

`reusable-sets-variable` stays refused, because it uses `Elsa.SetVariable` (D-7).

**Docs.** `docs/reference/elsa3-migration-readiness.md` (:24, :31, :57), `Extensions/Elsa3/src/Mapping/README.md`
(:133-134), `specs/246-publish-imported-reusables/contracts/diagnostic-codes.md` (:32) and spec 246's D-8 rows get a
"lifted by spec 265" note. `specs/236-elsa3-activity-map/member-map.md` has no D-8 rows.

## R-11 Existing activities affected by D-1

No seeded, sample or Workbench graph activity exists (`samples/`, `Essentials/apps/Elsa.Workbench`, `e2e-tests`). No
test reads a caller value from inside a body. The refusal therefore reaches only user-published activities, at their
next publication (R-5 keeps published versions running).

## Questions left for the tasks

- **Q-1.** `Input` syntax picks in *plain workflows* compile to a generic expression today, so they are probably
  broken. This is out of scope (FR-011). The task files a `needs-triage` issue after a test confirms the behaviour.
- **Q-2.** Graph editor expression tooling for an activity draft (R-8). If the server side needs a new endpoint, the
  result pickers in Studio split into a follow-up, and FR-009 is narrowed in this unit. Graph inputs and variables
  still ship in Studio.
