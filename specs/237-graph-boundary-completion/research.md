# Research: Graph Boundary Completion From Any Depth

All findings were verified on origin/main `c6ae516ca` (2026-10-14). Paths are relative to `Essentials/` unless they
start with `e2e-tests/`, `docs/` or `specs/`. Line numbers drift.

## R1: How the exit reaches the nearest graph boundary

**Decision**: Add one generic runtime capability, an **ancestor-targeted notification**. A structural activity
stages `RequestAncestorNotification(ancestorConsumerKey, code, payload)` on its runtime context. When the runtime
builds the notification work item, it walks the committed `ParentActivityExecutionId` chain from the notifier's
parent upward. It stops at the first ancestor whose executable node descriptor has the given consumer key, and
addresses the existing `NotifyParentActivity` work item to that ancestor. `Complete` stages one such notification
for consumer `elsa.graph-activity`. `GraphActivity` handles it through the existing
`IRuntimeActivityChildNotificationHandler`.

**Rationale**:

- No composite between the node and the boundary needs to change (FR-004). Composites cannot intercept the
  notification because it never passes through them.
- The pieces already exist:
  - `RuntimeNotifyParentCommandPayload` addresses its target by activity execution id and executable node id.
  - `WorkflowNotifyParentActivitySchedulerWorkHandler` checks only that the target exists and that its node matches
    (L62-70). It does not check that the notifier is a direct child; that restriction is applied only when
    `ParentNotificationEvaluation.BuildAsync` picks the target (L43).
  - During a notification evaluation, the handler runs `PrepareCompletionCheckpointAsync`, output capture and
    `CommitCompletedParentActivityAsync`, and applies subtree-cancellation plans (L176-208, L372). That is the same
    path `OnChildCompletedAsync` takes, so FR-009's atomic commit comes for free.
  - The handler drops a notification whose target is no longer `Running` (L77, L251). That gives first-committed-wins
    (FR-006) with no new code.
- The consumer key keeps Workflows.Runtime free of any knowledge of graphs. It matches on a string the descriptor
  already carries (`WellKnownRuntimeActivityConsumers.GraphActivity`), and a nested graph is matched first because
  the walk goes nearest-first (FR-003).
- Today the ancestor chain is reliable only through `ActivityExecutionState.ParentActivityExecutionId`.
  `Provenance.ExecutionScopeId` is overwritten or nulled by Flowchart, Sequence, ForEach and Parallel, and the
  `graph.executionScopeId` command metadata is read nowhere. `RuntimeContainerScopeService.BuildVisibleFramesAsync`
  already walks the same chain through `IActivityExecutionStateStore.FindAsync`, so this walk is a sanctioned
  precedent, not a new pattern.

**Alternatives considered**:

- *Bubble an outcome like `Break`*. Rejected in the interview (D2): every composite would need changes, loops consume
  it, and Parallel ignores it.
- *Bubble `RequestParentNotification` level by level*. Only BPMN implements the notification handler. A composite
  without one silently acks and drops the notification (L129-137), so every composite would need a handler.
- *Use `Provenance.ExecutionScopeId` or the `graph.executionScopeId` metadata*. Intermediate composites overwrite or
  null them; making them inherit would change scope semantics for Flowchart and BPMN.
- *Reuse `CancelActivityScopeCommand`*. It cancels an arbitrary boundary but cannot complete it with an outcome or
  capture outputs.
- *A dedicated "complete ancestor" work item kind*. It would duplicate the notification handler's completion,
  output-capture and cancellation commit path.

## R2: Ordering, and keeping the rest of the body from running in between

**Finding**: A structural activity's initial execution cannot `Defer` without scheduling a child
(`WorkflowInvokeActivitySchedulerWorkHandler.cs` L294-295). So `Complete` must complete itself in the same commit
that stages the notification. Today structural handlers enqueue the completion intent before notification intents
(L1066; `StructuralParentEvaluationSupport.cs` L485-486). If that order held, the direct parent would see
`Complete`'s completion first and could schedule the next step, or, when `Complete` is the entry, `GraphActivity`
would receive an unmapped entry completion and throw.

**Decision**: Post-commit intents built from **ancestor-targeted** notifications are enqueued **before** the
notifier's own completion intent. Direct-parent notifications keep today's order. The work queue is ordered by key
(`InMemory…Queue` `OrderKey`), so the boundary evaluates the exit before the direct parent sees `Complete` finish.
The boundary then completes and cancels its entry subtree. `Complete`'s own state was already committed as
`Completed`, and the cancellation planner skips terminal members (`StructuralParentEvaluationSupport.cs` L176). The
`Complete` node therefore ends **Completed**, as the spec assumes, and every other live descendant ends `Cancelled`.
When the direct parent's completion work item is processed, that parent is either `Cancelled` or, when the parent
is the boundary itself, `Completed`. Either way it is no longer `Running` and the item is dropped (L112, L396).

**Verify first (task gate)**: the intents of one commit are dequeued in the order they are enqueued, for both the
in-memory and the EF queue. If not, fall back to having the boundary ignore late completions from a `Complete`
child. Today `GraphActivity.OnChildCompletedAsync` would throw on those, so the fallback changes that method.

**Alternatives considered**: Letting `Complete` defer with a dummy child was rejected as a hack. A new transition
kind ("complete silently, no parent completion") would touch every work handler.

## R3: The `Complete` activity's shape

**Decision**:

- `Elsa.Activities.Graph.Runtime.Activities.Complete` lives in `Activities/Graph/Runtime/Activities/Complete.cs`.
  It is a `StructuralActivity, IRuntimeStructuralActivity` because only structural activities see
  `IRuntimeActivityExecutionContext`; a plain leaf gets the sealed `ActivityExecutionContext` record.
- It has one input, `[ActivityInput(Key = "outcome")] string Outcome`. A lowercase key has precedent
  (`Fault`: `code`, `message`; `WriteLine`: `text`) and matches the `control` intrinsic's `outcome` input.
- `ExecuteStructureAsync`:
  - Faults with `activity.graph.complete.outcome-required` when `Outcome` is blank.
  - Otherwise stages the ancestor notification (consumer `elsa.graph-activity`, code `elsa.graph.complete`, payload
    `{ "outcome": <reference key> }`) and returns `Complete()` with outcome `Done`.
- It declares no outcome port. Its own completion never routes, because its parent's completion is always dropped
  (R2). Whether the catalog scanner needs an explicit empty outcomes facet to stop the Studio "Done" fallback (see
  the comment in `Break.cs`) is checked while implementing; Studio rendering is out of scope (D8).
- No enclosing boundary (FR-018): the ancestor walk fails in `ParentNotificationEvaluation.BuildAsync`, which runs
  inside the invoke callback boundary, so the evaluation faults the `Complete` node. The runtime raises a typed
  fault `runtime.ancestor-notification.target-not-found` whose message names the notifier and the consumer key
  (`elsa.graph-activity`). It never finishes the workflow.

## R4: Runtime outcome resolution in `GraphActivity`

**Decision**:

- `GraphActivity` implements `IRuntimeActivityChildNotificationHandler`. For code `elsa.graph.complete` it:
  1. Resolves the payload's reference key through a new descriptor field, `CompleteOutcomes` (reference key to
     boundary outcome name). An unknown key returns `Faulted("activity.graph.complete.outcome-unknown")` (FR-019).
  2. Sets `_selectedOutcomeName`. The activity is constructed fresh for each evaluation, and the notification
     evaluation calls `PrepareCompletionCheckpointAsync` on the same instance (handler L197-208).
  3. Calls `RequestChildSubtreeCancellation` for each live direct child from `GetLiveChildActivities()`, which is
     the entry execution. This is permitted during a notification evaluation (L176-184; the XML doc on
     `IRuntimeActivityExecutionContext` L76-78 is stale and is corrected in this unit).
  4. Returns `Complete(outcomeName)`.
- Other notification codes are ignored and the activity keeps deferring.
- `PrepareCompletionCheckpointAsync` prefers `_selectedOutcomeName` when it is set. Today it returns `Done` whenever
  `OutcomeMappings` is empty, which would drop an exit outcome in a schema-2 graph whose outcomes are all reached by
  `Complete`.
- Natural completion of the entry when `OutcomeMappings` is empty but `CompleteOutcomes` is not faults with
  `activity.graph.entry-completed-without-mapping`: the body ended without reaching a `Complete` node, and no mapping
  covers natural completion. Schema-1 and mapping-only schema-2 descriptors keep the current branch unchanged
  (FR-007).

**Why a reference-key payload plus a descriptor map, not a compiled outcome name in the node input**: rewriting an
occurrence's authored input during compilation would make the compiled template disagree with the authored node.
The descriptor map also gives the runtime safety net in FR-019 something to check against.

## R5: Design-time validation in the graph provider

**Recognising a `Complete` node**:

- At compile time, a node's resolved dependency has `DefinitionId ==
  ActivityCatalogStableIds.DefinitionId("Elsa.Activities.Graph.Runtime.Activities.Complete")`. CLR rows get their
  definition id from this formula (`CollectActivityVersions.cs` L108-111).
- At draft validation time (`ValidateAsync`, called by `ActivityDraftValidator` with no dependencies), the provider
  resolves each node's `activityVersionId` through `IActivityDefinitionLookup`. That interface is in
  Activities.Design.Core, which Graph.Design already references, and from there the provider reads the definition's
  type key. The provider gets the lookup through its constructor.

**Decision on rules**:

| Rule | Where | Code |
|---|---|---|
| A `Complete` node's `outcome` is one literal string | `ValidateAsync` and compile | `activity.graph.complete.outcome-literal-required` |
| It names an emitted boundary outcome reference key | `ValidateAsync` and compile | `activity.graph.complete.outcome-unknown` |
| Schema 1 rejects any `Complete` node | `ValidateAsync` and compile | `activity.graph.complete.schema-unsupported` |
| An emitted outcome is reachable through a mapping **or** a `Complete` node | relaxes `outcome-mapping-required` in `ValidateBoundaryOutcomeMappings` | existing code |

- The reachability rule is computed from the set of reference keys named by `Complete` nodes, which is passed into
  `ValidateBoundaryOutcomeMappings`.
- If the lookup cannot resolve a version id at draft time, that node is treated as "not a `Complete`" for draft
  diagnostics. The dependency-aware compile stage re-runs every rule exactly, so publication is never wrong.

**Compile output**: for schema 2, the boundary payload gains `completeOutcomes:
[{ referenceKey, boundaryOutcomeName }]`. It is emitted **only when** at least one `Complete` node exists, listing the
reference keys those nodes name. Graphs without one compile to byte-identical payloads, so their template hashes do
not change (FR-015). `GraphActivityDescriptor` gains the field through an additional `[JsonConstructor]` overload, as
`outcomeMappings` did; `RuntimeDescriptorSchemaVersion` stays `"1"`.

**Alternatives considered**:

- *Move all checks to the compile stage*. This loses draft-time diagnostics that Studio shows live.
- *Detect `Complete` heuristically by any node having an `outcome` input*. Rejected as imprecise.

## R6: Rejecting `Complete` in plain workflows

**Decision**: Graph.Design registers an `IDraftValidator`, `CompleteOutsideGraphDraftValidator`. It resolves each
draft node's version through `IActivityDefinitionLookup` and returns a `ValidationError` for every `Complete` node
(type `graph.complete.outside-graph`).

- `IDraftValidator` runs only for workflow definition drafts, never for activity-graph bodies, which are validated by
  the provider. So it cannot misfire on a graph body.
- Promotion throws `DraftHasValidationErrorsException` while errors exist, which satisfies FR-016 at publish.
- Precedent: `SwitchDuplicateCaseValidator`, registered by `ActivitiesControlFlowFeature`.
- Graph.Design gains a reference to `Workflows/Design/Validations/Core`, a `.Core`, as ControlFlow already has.

## R7: Durable cancellation evidence (SC-002)

**Decision**: Reuse the runtime's subtree cancellation (`ActivitySubtreeCancellationPlanner` plus
`ActivityScopeCleanupStore`) through `RequestChildSubtreeCancellation`; nothing new is added to the stores. Prove it
in an engine-driven test:

- A graph boundary whose Flowchart body forks into three branches: one suspended on a bookmark, one waiting on a
  durable timer, and one that reaches `Complete`.
- After the exit, assert:
  - `IBookmarkStateStore` and `IDurableTimerStore` hold nothing for the body;
  - no scheduler work item remains for it;
  - every body execution except `Complete` is `Cancelled`;
  - advancing a `FakeTimeProvider` past the due time fires nothing;
  - resuming the old bookmark id resumes nothing.
- Run it once on the in-memory stores and once on EF Sqlite. The building blocks are `WorkflowExecutionHarness`,
  the Behavioral `GraphActivityDrive`, and the Sqlite setup in `Scheduling/Tests/DurableTimerRestartCrashTests.cs`.
  No existing harness combines them, so the test composes them.

## R8: Contracts and documentation touched

- Runtime core: a new member on `IRuntimeActivityExecutionContext`. `SimpleActivityExecutionContext` is the only
  implementation, so adding the member breaks no other implementer. Neither project opts into `ElsaTrackPublicApi`.
  The member is still a public interface addition, recorded under §4.2 for the release note.
- Catalog baseline: `tests/Activities/Design/Tests/Contracts/activity-contract-surface.baseline.json`, plus the
  Behavioral `ActivityContractInventory` and `ActivityCoverageTests` lists.
- `EXTENSION_POINTS.md`:
  - Graph Runtime: the `Complete` activity, and notification handling in `GraphActivity`.
  - Graph Design: the new `IDraftValidator`, the provider's lookup dependency, and the stale schema row (only `1` is
    listed).
  - Workflows Runtime Core: the new context member.
- `docs/reference/activity-definition-authoring-composition.md` gets an "Ending a reusable activity from any depth"
  section. It documents the type key, input key, literal rule, validation codes and the runtime fault codes (FR-021).
- `docs/glossary/elsa.md`: one line on the Graph activity entry that links the new reference section.
- Generated maps are refreshed with `-- all`, and `git diff --stat docs/` is checked, because `-- check` always
  reports stale on Windows CRLF checkouts.
