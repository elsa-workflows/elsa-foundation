# Implementation Plan: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Branch**: `agent/claude/3052-spec1242-bulk-dispatch` | **Spec**: [spec.md](spec.md) | **Issue**: #5833, part of #3052

## Summary

Add an Elsa 4 structural activity, `BulkDispatchWorkflows`, to the DispatchWorkflow activity package. It has
`Items` and three slots: `Dispatch` (one authored `DispatchWorkflow`), `ChildCompleted` and `ChildFaulted`. For each
item it runs `Dispatch` once, with its own iteration id and item scope, and at most 16 passes are open at once. It runs
the matching slot per child that ends, and completes with `Done`.

Because each child dispatch is its own activity execution, the whole dispatch subsystem is reused unchanged: identity,
stager, resume, cancellation, audit, test-run scope and recovery. The Elsa 3 converter maps its activity to the new one
one to one.

## Technical Context

**Language/Version**: C# / .NET 10; Studio (TypeScript) only through generic slot rendering.

**Primary Dependencies**: none new.

**Storage**: none new. The dispatch records, intents and bookmarks are the existing ones, one set per child.

**Testing**: xunit only. Runtime tests next to the existing DispatchWorkflow runtime tests; converter tests in
`extensions/Elsa3/tests/Mapping`.

**Project Type**: a new activity in `essentials/src/Activities/DispatchWorkflow/Runtime` (plus a structure handler on
the design side), and converter rules.

**Constraints**: pre-stable, so no shims. No author-facing degree, batch or channel input.

## Constitution Check

- **Sanctioned patterns only**: the activity is a structural activity with a structure handler, as `If` and `ForEach`
  are. The converter change is a map entry and existing rule kinds. Pass.
- **No runtime knob for authors**: no degree, batch or channel input. The cap (FR-007) and the window of 16
  (FR-005) are fixed constants. Pass. This departs from large-fan-out.md decision 1's author input. The coordinator
  kept 16 as the starting value; tuning it for GA is #5980 (Q4).
- **Refuse over workaround**: the converter refuses what it cannot map faithfully (spec FR-019), and Elsa 3 defects
  (the shared bookmark counter, the leaking `Input` dictionary) are not reproduced. Pass.
- **ADR needed?** No.
  - Each child keeps "one dispatch per activity execution" (spec 096 FR-012).
  - Per-item iteration ids and scopes follow ADR 0028, and BPMN multi-instance already schedules one node per
    iteration.
  - The runtime and designer synthesize no node. Only the converter generates the `Dispatch` child, with its
    existing synthesized-node pattern (`Elsa3TerminalPlacementRewrite`, `Elsa3HttpTriggerResponseModeRewrite`; §3), so there
    is no new pattern.
  - The window of 16 stays within large-fan-out.md's bound on outstanding children. The rejected option B would have
    needed an ADR.

## Design

### 1. Activity

- **Placement**: `BulkDispatchWorkflows : StructuralActivity, IRuntimeStructuralActivity,
  IRuntimeActivityChildCompletionHandler, IRuntimeActivityChildFaultHandler, IRuntimeActivityDescendantFaultHandler` in
  `essentials/src/Activities/DispatchWorkflow/Runtime/Activities/`.
  - Runtime type `Elsa.BulkDispatchWorkflows`.
  - Catalog key `Elsa.Activities.DispatchWorkflow.Runtime.Activities.BulkDispatchWorkflows` (spec FR-001).
  - The descendant handler gives a fault deep inside a slot the same intervention-and-defer path as a direct slot
    child (spec FR-008).
  - It does not implement `IRuntimeAcceptsRescheduledFaultedChild`, because retry goes through
    `RetryFaultedActivity`, which does not need it.
- **Input**: `Items`.
- **Slots**: `Dispatch`, `ChildCompleted`, `ChildFaulted`, all `[ActivityChildSlot(… Single)]`.
- **Structure handler**: `BulkDispatchWorkflowsStructureHandler` projects and replaces the three slots, as
  `IfStructureHandler` does, with these members spelled out (do not copy If's answers):
  - **`ProjectIterationVariables`** returns `currentItem` and `currentIndex`, typed as `LoopStructureHandler` types
    them. `ScopedVariableResolver` adds them to the descendants' scope only then. Without it, publish refuses every
    `variables.currentItem` read.
  - **`CompletesAfterItsChildren`** returns true, as `If` and `LoopStructureHandler` do, so spec 266's unordered-write
    analysis does not report false findings around the bulk node.
  - **`AssessChildResult`**, per pair:
    - **Producer `Dispatch`, consumer `ChildCompleted` or `ChildFaulted`**: `Unknown`, with
      `MayRunConcurrently = true` and `EarlierProducerRunCanReachConsumer = true`, so a slot binding to the `Dispatch`
      node's result is refused (spec FR-010).
    - **A child paired with itself** (the graph asks every container above the common one about its child's
      self-pair): `EarlierProducerRunCanReachConsumer = false`, as `LoopStructureHandler` answers. The bulk activity
      schedules each pass itself, and the runtime resolves results by causal history. So a result binding between two
      activities inside one slot (for example inside a `Sequence` or `Flowchart`) still publishes.
    - **Any other pair**: answered as `If` answers its pairs.
  - **`RelateChildren`** answers `Unordered` for every pair, including a child paired with itself. `Dispatch` and both
    slots run concurrently across passes. If's "never overlap" facts would feed spec 266's concurrency checks false
    ordering.
- **`Dispatch` slot check**: an `IDraftValidator` in DispatchWorkflow.Design, modelled on `CompensationTargetValidator`.
  - It looks up the slot child's `ActivityVersionId` in the catalog and reports `BulkDispatch/DispatchSlot` when the
    slot is empty or holds another activity.
  - It needs a reference to Validations.Core. The structure handler's compile step cannot do this check: it sees only
    the version id and cannot emit a coded diagnostic.
- **Initial payload**: `BuildInitialPayload` emits only null or `[]` slots, so a new bulk node starts with `Dispatch`
  empty, and the validator's message guides the author. No Studio-specific code.
- **Pinning**: there is no synthesized node and no `DispatchPinSource` change. The `Dispatch` node is in the executable
  like any authored child, so the existing pin source pins it.

### 2. Scheduling and completion

- **`Items` binding**: it gets `ForEach`'s durable-binding treatment, `[RefusesSecretBinding]` and the
  item-envelope policy checks, because per-pass scopes and later window refills re-read `Items` from the committed
  input. The state does not hold the items.
- **Start**: evaluate `Items`, refusing a value that is not a collection as `ForEach` does. Check the cap (FR-007).
  Schedule `Dispatch` for the first min(N, 16) items, each with an iteration id and a `LoopIterationScopeRequest`
  carrying `currentItem` and `currentIndex`. The scope declares only those two names, typed as `ForEach` types them.
- **Iteration ids**: item-indexed, derived from the bulk activity execution id with the prefix `bulk-dispatch-item`.
  A small local helper produces them, duplicated from `StructuredExecutionIdentity.Iteration` rather than adding a
  reference from DispatchWorkflow.Runtime to `Elsa.Activities.ControlFlow` ("duplication beats dependency").
  Spec 1243's neutral-prefix change, if it lands first, can replace the helper.
- **State**: kept in the activity's structural state, which commits with each evaluation:
  - the item count;
  - the next item index to schedule;
  - the set of ended item indexes.
- **Ending a pass**: an index is marked ended only when its pass has really ended: after a `Dispatch` outcome with no
  slot to run, or after its slot child completes. Only a newly ended index schedules the next item, so a repeated
  callback cannot exceed the window. There is no recount and no new store method.
- **`OnChildCompletedAsync`**:
  - For the `Dispatch` node, route by outcome (FR-006).
  - For a slot child, end the pass.
  - **Slot scheduling**: a slot is scheduled with the same iteration id as its pass's `Dispatch`, and with its own
    `LoopIterationScopeRequest(bulkNodeId, iterationId, {currentItem, currentIndex})`, rebuilt from `Items` at that
    index.
    - A frame belongs to one activity execution, not to an iteration id (`RuntimeContainerScopeService`), so without
      that request `variables.currentItem` fails at run time in the slot, even though publish passes.
    - A `RetryFaultedActivity` successor gets its frame from the source's frame (`ActivitySuccessorStaging`).
  - Complete with `Done` when the set holds every index.
- **`OnChildFaultedAsync`**:
  - Every fault, child or descendant: `RequestChildFaultIntervention` and `Defer`. Nothing is absorbed (spec FR-008).
    Retry goes through `RetryFaultedActivity`, and the retried child's completion ends the pass as usual.
- **Concurrent same-node scheduling**: no new scheduler (FR-024). Every `ScheduleChildActivity` call gets its own
  activity execution id (`SchedulerWorkItems.cs`). `BpmnProcessRuntime` already schedules one bound node, its
  call-activity `DispatchWorkflow` included, once per instance with an `IterationId` and a
  `LoopIterationScopeRequest`. Use it as the reference.

### 3. Converter

- **Map entry**: `Elsa3BuiltInActivityMap.Entries.cs` replaces `NoCounterpart("BulkDispatchWorkflows")` with a
  `Catalog` entry targeting the CLR key `Elsa.Activities.DispatchWorkflow.Runtime.Activities.BulkDispatchWorkflows`.
  - **Bulk-entry members**: `Items` (`Carry`, `NullUnbound`, FR-014). `ChildCompleted` and `ChildFaulted` are
    `Structure(…, Elsa3MemberKind.Port)`, not `Slot()`: `ApplyMembers` would add a `Slot()` port as a child before
    the rewrite runs. The rewrite calls `context.AddChild` for each branch itself, unless the wait is a literal false.
    Every dispatch member is `Structure` (R7) on
    the bulk entry: `WorkflowDefinitionId`, `Input`, `DefaultItemInputKey`, `CorrelationIdFunction`,
    `WaitForCompletion`, `StartNewTrace` and `ChannelName`. The coverage test requires targeted keys to be inputs of
    the entry's own type, and the bulk node has only `Items`.
  - **Behaviour warning**: `ELS3-BEHAVIOUR-BULK-DISPATCH-CAPPED` is an entry-level behaviour warning (spec FR-020).
  - **One rewrite**: `Elsa3NodeRewriteKind.BulkDispatchChild` (the entry takes exactly one) builds the `Dispatch`
    child, an Elsa 4 `DispatchWorkflow` with id `{elsa3NodeId}.dispatch`.
    - The child is a ready-made plan registered in `plan.Nodes`, so `VisitAsync` skips it (`Elsa3ActivityNormalizer`).
      It is also attached to the bulk node: added to its `Activity.Activities` and to `Node.Slots` under `Dispatch`, as
      `Elsa3HttpTriggerResponseModeRewrite` attaches its node. Lowering, the inventory and the mapper walk those lists,
      so without this the child's `CorrelationId` and `WaitForCompletion` are never lowered and the child is never
      emitted. It is not added through `context.AddChild`, which would normalize it through the Elsa 3
      `DispatchWorkflow` entry.
      Its type is resolved with `Elsa3ActivityResolver.ResolveCatalogTypeKeyAsync`, and its properties are already
      keyed for Elsa 4.
    - It uses the bulk node's `JsonPath`, and it maps its source names back for diagnostics:
      `CorrelationId` → `CorrelationIdFunction`, `Inputs` → `Input`. This is not the `SynthesizedFrom` mechanism, which
      only intrinsics consume.
    - The children the rewrite adds reach the lowerer, because the inventory is built after normalization.
    - It sets every member explicitly: `WorkflowDefinitionId` (literal only), `Inputs` (the per-item script),
      `CorrelationId`, `WaitForCompletion` (missing → true, null or valueless → false, case-insensitive literal,
      otherwise carried), and `CancelChildOnParentCancellation` false.
    - It refuses a bound `StartNewTrace` or `ChannelName`. The normalizer's `CheckRemoved` is private and those members
      are `Structure` on the bulk entry, so the rewrite repeats that check.
    - It refuses any non-null literal `Items` with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` (FR-014). No existing value
      contract refuses literals; if spec 1243 adds one for FR-017, use it instead.
    - It records the wait classification (missing, literal true, literal false, expression) in a plan field on the
      bulk node, for the outcome step.
    - The child is not normalized through the Elsa 3 `DispatchWorkflow` entry, so neither that entry's
      `WaitForCompletion` rule (Carry, NullUnbound) nor its `DispatchInputNames` warning applies.
  - **Surface fixture**: `elsa3-activity-surface.json` gets the two ports (kind `port`).
- **Item read**: there is no ambient-name registry today, so the converter gains one plan field and two code changes.
  - **Plan field**: on `Elsa3ConversionPlan`, mapping each generated `Dispatch` child to its bulk node id and item key.
  - **`Elsa3ValueFlowLowerer`**: the field is passed into `AccessorScope`, and `ResolveGeneratedAccessor` resolves
    `get{key}` to `VariableExpressionParameterBinding(bulkNodeId, "currentItem")` for that child. A workflow input or
    variable of that name still wins, as in Elsa 3.
  - **`Elsa3ExpressionRewriter`**: it currently refuses every Elsa 3 `args` read (ForbiddenCapability). It must accept
    `args.<camelKey>` in that child's correlation function and route it to the same resolution. Elsa 3 exposed `args`
    there.
- **Shadowing**: `AncestorScopeNames` (`Elsa3DefinitionConverter`) adds loop names only for the keys in
  `Elsa3LoopVariableReferencesRewrite.CurrentValueVariables` (For, ForEach). Add the bulk key with `currentItem` and
  `currentIndex`, so the existing FR-031 shadow refusal also fires for a workflow variable named `currentItem` read
  inside `Dispatch` or a slot.
- **Inputs script**: the rewrite builds `Inputs` as an Elsa 4 `ExpressionDefinition` directly, with a pinned
  `VariableExpressionParameterBinding` to the bulk node's `currentItem`, as `Elsa3HttpTriggerResponseModeRewrite`
  builds its script.
  - It does not go through the Elsa 3 lowerer, so the item is always the item. Elsa 3 merged it in C#, never through a
    getter, so a variable or input with the key's name never wins there.
  - Declared names appear as literals, and item members are reached via `Object.entries`/`filter`/`Object.fromEntries`,
    never `item[name]`, because the read analysis treats computed member access as possible state access (the comment
  in `Elsa3HttpTriggerResponseModeRewrite`).
  - The static `Input` follows the #5808 rules (literal or Object) and is embedded as constant entries. An expression
    `Input`, JavaScript included, is refused (spec FR-016). Node rewrites run inside `NormalizeAsync`, and JavaScript
    is lowered later in `LowerAsync`, so the rewrite cannot merge a script into `Inputs`.
  - **Target declarations**: a separate, bulk-only field on `Elsa3ConversionRequest`, `BulkDispatchTargetInputs`. It
    stays separate so that the #5808 check for plain dispatches, which reads `DispatchTargetInputs`, keeps its
    same-import scope.
    - `Elsa3ReusableActivityImportMaterializer` fills it before conversion, because the request is synchronous. It
      pre-scans the import's sources for `BulkDispatchWorkflows` nodes with a literal `WorkflowDefinitionId`, and for
      each target id takes the same-import declarations, or else loads the published Elsa 4 definition's declared
      inputs.
    - The Elsa 4 workflow definition id is the Elsa 3 `DefinitionId` unchanged (`ReusableActivityCollectionAnalyzer`),
      so the id maps one to one.
    - Research item R-3 is narrowed to which store or lookup service gives the published workflow's declared inputs.
    - A target in neither place is refused (`…-TARGET-UNKNOWN`). The single-definition entry point
      `Elsa3WorkflowDefinitionImporter.ImportAsync` passes no targets, so a bulk node converted there is always refused
      as target-unknown. Its callers are the test fixture and the migration boundary, so that is acceptable.
  - Item resolution through `get{key}`/`args.<key>` (Item read, above) therefore applies only to the correlation
    function.
- **Outcomes** (spec FR-017): in `Elsa3FlowchartGraphBuilder.DropDeadEdges`, before `MapPorts` and `CheckJoins`. The
  node rewrite cannot see connections. The step reads the wait classification from the plan field the rewrite
  recorded, because `WaitForCompletion` is a consumed `Structure` member by then.
- **Branches with a literal-false wait** (spec FR-018): the rewrite does not call `AddChild` for them, and raises
  `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED` for each dropped branch.
- **Outer writes** (spec FR-010a, FR-020a, Q5): after lowering, a write inside a converted `ChildCompleted` or
  `ChildFaulted` branch to a variable declared outside the bulk node raises the behaviour warning
  `…-BULK-DISPATCH-OUTER-WRITE`. It
  goes in `raisedByRewrites`. The definition converts. Publish adds spec 266's `ValueFlow/ConcurrentWrite` warning,
  because Elsa 4 deliberately allows such writes.
- **Child-result reads** (spec FR-019): resolved to `…-CHILD-RESULT-READ` in name resolution for nodes inside the
  converted slots, before the generic missing-name failures.
- **Codes**: 3 errors and 3 warnings in `extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs`.
  `…-ITEM-INPUTS`, raised by the rewrite, goes in `raisedByRewrites` in `Elsa3ActivityMapCoverageTests`.
  `…-CAPPED` is the entry's behaviour warning.

### 4. Docs

- A README section for the activity in `essentials/src/Activities/DispatchWorkflow/Runtime/README.md`: cap, batching,
  branches.
- `EXTENSION_POINTS.md` in the same project, if the structure handler is a catalogued extension.
- `extensions/Elsa3/src/Mapping/README.md`.
- `specs/236-elsa3-activity-map/member-map.md` and `contracts/diagnostic-codes.md`.

## Test scope

- **Runtime** (`BulkDispatchWorkflows` tests next to the DispatchWorkflow runtime tests):
  - with N ≤ 16, N children start in one turn, each with a distinct dispatch identity;
  - the wait and no-wait completion;
  - each branch routing (Completed, Faulted, Cancelled, DispatchFailed, a dispatch input fault);
  - an empty or null `Items`;
  - the cap fault;
  - a slot fault becomes a blocking incident, the bulk activity defers, and a retry ends the pass;
  - a `Dispatch` self-fault stays an incident (`ChildFaulted` does not run), and a retry ends the pass;
  - the window of 16: never more than 16 passes open, including under repeated callbacks;
  - publish refuses an empty or wrong `Dispatch` slot (`BulkDispatch/DispatchSlot`);
  - `variables.currentItem` reads in `Dispatch` and in the slots resolve at publish;
  - at run time, `ChildCompleted` and `ChildFaulted` read the right `currentItem` and `currentIndex` for their pass, also
    after a retry;
  - a fault inside a `Sequence` in a slot takes the intervention path (descendant fault);
  - parent cancellation cancels the waiting children;
  - publish pins the `Dispatch` node through the unchanged `DispatchPinSource`;
  - a slot that writes a workflow variable publishes with the `ValueFlow/ConcurrentWrite` warning;
  - a slot binding to the `Dispatch` node's result is refused at publish: an optional binding with
    `ValueFlow/EarlierProducerRun`, a required one with `ValueFlow/UnavailableProducer`;
  - a result binding between two activities inside one slot `Sequence` publishes.
- **Converter** (`Elsa3BulkDispatchConversionTests`): every member rule; each FR-017 and FR-019 row; an item key
  that clashes with a workflow variable (the item wins in `Inputs`, and the variable wins in the correlation function,
  as in Elsa 3); the item-read
  resolution, including the variable-wins case and `args.item`; the inputs script through publish validation; kept
  slot ids.
- **Coverage tests**: the map coverage and the surface fixture.

## Coordination

- **Spec 1243**: the same `currentItem`/`currentIndex` scope contracts (FR-024). The helpers stay local in 1242.
- **Spec 1245**: the per-child `Output`, for a later follow-up that exposes it to branches.
- **S-15**: the hand-over of FR-022 after approval.

## Project Structure

```text
essentials/src/Activities/DispatchWorkflow/
├── Runtime/Activities/BulkDispatchWorkflows.cs          (new)
├── Runtime/Constants/…                                  (type key, outcome, cap)
├── Runtime/README.md
├── Design/Services/BulkDispatchWorkflowsStructureHandler.cs (new)
└── Design/Services/BulkDispatchSlotValidator.cs        (new, IDraftValidator)
extensions/Elsa3/src/Mapping/
├── ActivityMap/Elsa3BuiltInActivityMap.Entries.cs
├── ActivityMap/Elsa3NodeRewriteKind.cs
├── ActivityMap/Rewrites/Elsa3BulkDispatchChildRewrite.cs (new)
├── Conversion/Elsa3FlowchartGraphBuilder.cs             (outcome step)
├── Services/Elsa3ValueFlowLowerer.cs                    (item read, child-result reads)
├── Services/Elsa3ExpressionRewriter.cs                  (args.<key> in the correlation function)
├── Conversion/Elsa3DefinitionConverter.cs               (bulk key in AncestorScopeNames)
├── Conversion/Elsa3ConversionPlan.cs                    (bulk child → item key field)
├── Conversion/Elsa3ConversionRequest.cs                 (BulkDispatchTargetInputs field)
├── Services/Elsa3ReusableActivityImportMaterializer.cs  (pre-scan; published lookup via the DI constructor)
└── README.md
extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs (6 codes: 3 errors, 3 warnings)
```

## Complexity Tracking

None. No constitution gate is exceeded.
