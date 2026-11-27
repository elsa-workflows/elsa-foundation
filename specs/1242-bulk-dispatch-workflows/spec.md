# Feature Specification: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Feature Branch**: `agent/claude/3052-spec1242-bulk-dispatch`

**Created**: 2026-11-26

**Status**: Draft — coordinator answered Q1–Q5 on 2026-11-27

**Issue**: #5833, part of initiative #3052 (Elsa 3 to Elsa 4 conversion). Parity record: #1152.

**Input**: Elsa 3's `BulkDispatchWorkflows` activity has no Elsa 4 counterpart, so the converter refuses every definition
that uses it with `ELS3-ACTIVITY-NO-COUNTERPART`. Define the Elsa 4 counterpart activity, how the converter maps the
Elsa 3 activity to it, and which Elsa 3 behaviours are refused with guidance instead of emulated. The sibling gap,
ParallelForEach, is spec 1243.

## Problem

Elsa 3 (elsa-core 3.6.3) `BulkDispatchWorkflows` starts one instance of a published workflow for each item of a
collection. It can wait until every child has finished. While it waits it runs a `ChildCompleted` branch for each child
that finishes and a `ChildFaulted` branch for each child that faults. Elsa 4 has `DispatchWorkflow` (one child), a
sequential `ForEach` and no bulk dispatch. The converter has no target for the Elsa 3 activity, so it refuses the whole
definition.

**Example.** Workflow `acme.order-received` gets an order with three lines and must start `acme.fulfil-line` once per
line. Each child gets the line's members as inputs and the correlation id `order-17_1`, `order-17_2` or `order-17_3`.
The parent continues only after all three children have finished, and it logs every child that faulted. In Elsa 3 that
is one `BulkDispatchWorkflows` node:

- `Items`: the order lines;
- `CorrelationIdFunction`: `` `order-17${getItem().suffix}` ``;
- `WaitForCompletion`: true;
- `ChildFaulted`: a `WriteLine` node.

Converted to Elsa 4, the definition is refused. Elsa 4 has no activity for this. The nearest an author can build by
hand is a sequential `ForEach` around an awaited `DispatchWorkflow`, which waits for each child before it starts the
next.

In the S-15 JTest corpus (branch `agent/claude/3052-s15-jtest-impl`, ledger `extensions/Elsa3/tests/JTest/ledger/`)
this refuses 2 definitions, `composition-bulk-dispatch-root-workflow.json` and `performance-concurrency.json`. Those 2
definitions block 6 suites: the five `composition/bulk-dispatch-workflow-*` suites and `performance/concurrency-tests.json`.

### What Elsa 3 actually does

The facts below are from the elsa-core 3.6.3 source: `BulkDispatchWorkflows`, `ResumeBulkDispatchWorkflowActivity` and
the component tests.

| Aspect | Elsa 3 behaviour |
|---|---|
| Inputs | `WorkflowDefinitionId` (any expression, resolved to the published version at run time), `Items`, `DefaultItemInputKey` (default `Item`), `CorrelationIdFunction` (evaluated once per item), `Input` (an extra dictionary), `WaitForCompletion` (default true), `StartNewTrace`, `ChannelName` |
| Items | Evaluated once. Null gives zero items. A value that is not a collection also gives zero items, silently. A non-null literal is read as raw JSON text, so it also gives zero items |
| Child input | `Input`, then `ParentInstanceId` = parent id, then the item merged on top. A map item adds each member as its own top-level input. Any other item is passed as `{DefaultItemInputKey: item}`. Undeclared names are accepted |
| Correlation id | `CorrelationIdFunction` sees the item as `get{DefaultItemInputKey}()`, for example `getItem()`. When the function is absent, the child gets no correlation id |
| Dispatch | All children are dispatched in one go, before the activity waits. A missing published definition throws, so the activity faults |
| Waiting | One bookmark for each parent instance. Each finished child resumes the parent once, and the activity completes when every dispatched child has finished |
| Child branches | `ChildCompleted` runs for a child that finished normally and `ChildFaulted` for one that faulted. Each branch gets the child's output, id and status as input, plus a `ChildInstanceId` variable. A cancelled child, or one whose branch is empty, only counts toward completion. The branches run only while the activity waits |
| Outcomes | `Done` when it does not wait, or when it has no items. Both `Completed` and `Done` when it waits. `Canceled` is declared but never emitted |
| Channel, trace | `ChannelName` must name a channel registered in code. A stock server registers none, so a non-empty name faults (ledger B10). `StartNewTrace` only affects OpenTelemetry |

### What the JTest suites assert

All 5 composition suites use `composition-bulk-dispatch-root-workflow.json`. In that definition the
`WorkflowDefinitionId`, `WaitForCompletion` and `ChannelName` inputs are JavaScript expressions over the HTTP request
body, and `Done`, `Canceled` and `Completed` all connect to one `WaitAny` join.

| Suite | Asserts |
|---|---|
| `…-channels` | One child per channel dataset, `Finished`/`Faulted`. All channel values are `""` since B10 |
| `…-await-completion` | With wait: the root ends `Running`/`Suspended` at `WriteHttpResponse` and the HTTP status is 200 (the Elsa 3 HTTP-context switch). Without wait: 202, the root `Finished`, and the child `Finished` |
| `…-correlation-id-function` | The child's correlation id is the root's correlation id plus the item's `correlationIdSuffix` |
| `…-input` | The child's input `ShouldFault` equals the item's `ShouldFault`, so map items spread into top-level inputs |
| `…-on-completed-callbacks` | With wait: each branch's `WriteLine` was started and completed once (one faulted child, one finished). Without wait: zero times |
| `performance/concurrency` | 300 items, no wait, one shared correlation id: the root finishes with an output, then all 300 children reach `Finished` |

### What Elsa 4 already has

- **`DispatchWorkflow`** dispatches one child.
  - Its target is pinned when the parent publishes (`DispatchPinSource`), so it must be a literal.
  - It has `Inputs`, a bindable `WaitForCompletion` (default false), `CorrelationId` (a blank value inherits the
    parent's) and `CancelChildOnParentCancellation`.
  - Outcomes: `Dispatched`, `Completed`, `Faulted`, `Cancelled`, `DispatchFailed`.
  - Elsa 4 refuses a child input name that the child does not declare.
  - There are no dispatch channels and no trace option.
- **Dispatch identity** is derived from (parent workflow execution, parent activity execution)
  (`WorkflowDispatchIdentity`). Spec 096 FR-012 fixes one dispatch per activity execution, and the stager, the resume
  guard and executor, subtree cancellation, the control boundary, audit history and recovery all rely on it.
- **Structural activities** (`ForEach`, `Parallel`, `If`) schedule child executions through their slots.
  - Every scheduled child gets a fresh activity execution id.
  - `ForEach` gives each pass an iteration id (`StructuredExecutionIdentity.Iteration`) and a scope with `currentItem`
    (ADR 0028).
  - Child completion and fault reach the parent through `IRuntimeActivityChildCompletionHandler` and
    `IRuntimeActivityChildFaultHandler`.
  - `Parallel` keeps several children suspended at once, each on its own bookmark, and joins by counting them.
- **Studio** renders declared child slots generically: `If` has two optional single slots and no Studio-specific code.
- **Background eligibility** (ADR 0093, spec 1230) does not apply: dispatching fails criterion C6, waiting fails C2 and
  a structural activity fails C1.

## Options

**A. A dedicated `BulkDispatchWorkflows` activity (recommended).** It is a structural activity with `Items` and three
slots:
- `Dispatch`, which holds one `DispatchWorkflow` node;
- `ChildCompleted` and `ChildFaulted`, both optional.

For each item it runs the `Dispatch` node once, with its own iteration id and a scope that holds the item. It runs the
matching slot for each child that ends, and completes with `Done`.

- Each item's dispatch is a separate, ordinary `DispatchWorkflow` execution, so the dispatch identity scheme, specs
  096–104 and every dispatch service apply unchanged, pinning included.
- The `Dispatch` node is authored, so its per-item `Inputs` and `CorrelationId` read `currentItem` as descendants of
  the bulk node. The Elsa 4 runtime and designer never synthesize a node; only the converter generates the `Dispatch`
  child when it converts an Elsa 3 definition (FR-011).
- Authors get one activity for "start this workflow per item". The converter maps the Elsa 3 node to it one to one.
- The runtime already schedules one node several times under distinct iteration ids: BPMN multi-instance loops do it
  (`BpmnProcessRuntime`). The activity adds the scheduling policy and the counting. Spec 1243 shares those pieces
  (FR-024).

**B. One stateful activity that dispatches N children itself**, with item-keyed identities.

- This breaks the "one dispatch per activity execution" contract (spec 096 FR-012). The stager, resume, cancellation,
  control boundary, audit and recovery would all change, and it needs an ADR. Rejected.

**C. No new activity: the converter lowers Elsa 3's node to a loop with a `DispatchWorkflow` body.** This was this
spec's first draft.

- Elsa 4 authors would still have no bulk dispatch activity.
- Every Elsa 3 rule would become converter machinery: outcome rewiring, current-item scope registration, per-pass
  re-evaluation of the wait.
- Rejected on review (coordinator, 2026-11-26).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An author starts a workflow per item and waits for all of them (Priority: P1)

An author adds `BulkDispatchWorkflows` to `acme.order-received`:

- `Items`: the order lines;
- in `Dispatch`, a `DispatchWorkflow` with:
  - `WorkflowDefinitionId` `acme.fulfil-line`;
  - `Inputs` `{ sku: variables.currentItem.sku, quantity: variables.currentItem.quantity }`;
  - `CorrelationId` `` `order-17_${variables.currentIndex + 1}` ``;
  - `WaitForCompletion` true;
- a `WriteLine` in `ChildFaulted`.

**Why this priority**: This is the feature. Without it, Elsa 4 has no way to start N children and wait for them as one
step.

**Independent Test**: Run the workflow with three lines, one of whose children faults. Three children start at once
(three is below the window of 16, FR-005), each with its own inputs and correlation id. `ChildFaulted` runs once,
and the activity completes with `Done` after the third child has ended.

**Acceptance Scenarios**:

1. **Given** three items, **When** the activity runs with waiting, **Then** three children start before any of them
   ends, and the activity completes with `Done` only after all three have ended.
2. **Given** 40 items and waiting, **When** the activity runs, **Then** at most 16 children are outstanding at once.
   The next item's pass starts as one ends, and all 40 passes are scheduled in item order. The order in which the
   children actually start is not guaranteed.
3. **Given** a child completes, **When** it ends, **Then** `ChildCompleted` runs once for it, with `currentItem` and
   `currentIndex` of that item in scope.
4. **Given** a child faults, is cancelled, or cannot be dispatched (`DispatchFailed`), **When** it ends, **Then**
   `ChildFaulted` runs once for it. A cancelled child counts as faulted for this branch.
5. **Given** an empty slot, **When** a child ends, **Then** nothing runs for it and it only counts toward completion.
6. **Given** an empty or null `Items`, **When** the activity runs, **Then** it completes with `Done` at once.

### User Story 2 - Fire and forget (Priority: P1)

The same activity, with `WaitForCompletion` false on its `Dispatch` node, starts every child and completes with
`Done` once all are dispatched. The slots are not run.

**Why this priority**: The second main use, and `performance/concurrency-tests.json` asserts it with 300 items.

**Independent Test**: Run with 300 items and no wait. The parent completes. All 300 children start with the given
correlation id and reach `Finished`.

**Acceptance Scenarios**:

1. **Given** the `Dispatch` node does not wait, **When** the activity runs, **Then** each pass ends with `Dispatched`,
   the activity completes with `Done` without waiting for any child, and no slot runs.
2. **Given** the parent instance is cancelled after the activity completed, **When** children are still running,
   **Then** the children are not cancelled. A child is cancelled only through `CancelChildOnParentCancellation`, which
   applies while the activity is still waiting, exactly as for `DispatchWorkflow`.

### User Story 3 - The converter maps Elsa 3 BulkDispatchWorkflows one to one (Priority: P1)

A migrating engineer converts an Elsa 3 definition that uses `BulkDispatchWorkflows`. It becomes an Elsa 4
`BulkDispatchWorkflows` with the same items and branches. A `DispatchWorkflow` in its `Dispatch` slot carries the
target, per-item inputs, correlation ids and wait.

**Why this priority**: It unblocks the 2 refused corpus definitions and their 6 suites.

**Independent Test**: Convert `performance-concurrency.json` with its child in the same import. It publishes unchanged.
Convert the composition root's approved adjusted copy (FR-022). It publishes, and the correlation-id, input, channels
and on-completed-callbacks suites pass.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 node with `getItem().correlationIdSuffix` in its correlation function, **When** it is
   converted, **Then** the `Dispatch` node's `CorrelationId` reads `variables.currentItem.correlationIdSuffix`.
2. **Given** map items and a target in the same import, **When** converted, **Then** the `Dispatch` node's `Inputs` passes
   each input the target declares that the item has, in the declared spelling.
3. **Given** `ChildCompleted` and `ChildFaulted` branches, **When** converted, **Then** they become the Elsa 4 slots,
   with their activity ids kept.

### User Story 4 - Elsa 3 behaviours with no faithful meaning are refused with guidance (Priority: P2)

**Why this priority**: The initiative's stance is refuse-with-guidance over workarounds.

**Independent Test**: Each refusal row in FR-019 gives its code and guidance in a converter test.

### Edge Cases

- **More items than the cap** (FR-007): the activity faults with `BulkDispatchCapExceeded` before it dispatches
  anything. Elsa 3 had no cap, so the converter warns (FR-020).
- **A child of a waiting activity is still running when the parent instance is cancelled**: the child is cancelled
  when `CancelChildOnParentCancellation` is true. That is the `DispatchWorkflow` rule, applied per child.
- **`Items` evaluates to a value that is not a collection**: the activity faults, as `ForEach` does. Elsa 3 silently
  dispatched nothing. The converter README documents the difference (FR-023).
- **Two waiting activities in one parent instance**: each keeps its own children. Elsa 3's shared per-instance
  bookmark and counter is not reproduced.
- **A slot activity faults**, for example the `WriteLine` in `ChildFaulted`: the workflow's incident strategy decides
  (FR-008). Under the default Fault strategy the workflow faults. Under ContinueWithIncidents it becomes a retryable
  incident on that item's pass, the other passes keep running, and the bulk activity completes after a retry.
- **The `Dispatch` execution itself faults** (an input the child does not declare, the nesting depth, a missing pinned
  target, an error in the `Inputs` or `CorrelationId` expression): the incident strategy decides, as above.
  `ChildFaulted` does not run for it. Elsa 3 faulted the whole activity, which is what the default strategy does.
- **`Dispatch` left empty, or holding another activity**: publish refuses the bulk node (FR-003).

## Requirements *(mandatory)*

### Functional Requirements

**The Elsa 4 activity**

- **FR-001**: Elsa 4 MUST provide `BulkDispatchWorkflows` in the DispatchWorkflow activity package, as a structural
  activity. It has two names:
  - the runtime activity type `Elsa.BulkDispatchWorkflows`;
  - the catalog key, which is its CLR full name, `Elsa.Activities.DispatchWorkflow.Runtime.Activities.BulkDispatchWorkflows`.

  The converter targets the catalog key, as it does for `DispatchWorkflow`.
- **FR-002**: The bulk node's only input is `Items`: the collection, evaluated once. There is no channel, trace,
  degree, batch or wait input on the bulk node.
- **FR-003**: Slots, all single and rendered through Studio's generic slot rendering with no Studio-specific code:
  - **`Dispatch`**: required. It holds one `DispatchWorkflow` node, authored like any other node. Everything about the
    child is set on that node:
    - `WorkflowDefinitionId`, a literal pinned at publish by the existing `DispatchPinSource`;
    - `Inputs` and `CorrelationId`, which read `variables.currentItem` and `variables.currentIndex`;
    - `WaitForCompletion` and `CancelChildOnParentCancellation`.

    Publish refuses the bulk node when `Dispatch` is empty or holds anything other than a `DispatchWorkflow`, with
    draft-validation diagnostic `BulkDispatch/DispatchSlot`. In Studio a new bulk node starts with the slot empty, and
    that diagnostic tells the author to drop a Dispatch Workflow there.
  - **`ChildCompleted`** and **`ChildFaulted`**: optional, as `If.Then` and `If.Else` are.
- **FR-004**: For each item, the activity MUST schedule the `Dispatch` node once:
  - with its own iteration id (item-indexed);
  - with an iteration scope holding `currentItem` and `currentIndex`, as `ForEach` provides (ADR 0028).

  The `Dispatch` node and the slots are descendants of the bulk node, so they resolve those names at publish. Each
  scheduled `Dispatch` execution is an ordinary `DispatchWorkflow` activity execution. The dispatch identity, stager,
  pinning, resume, cancellation, audit, test-run scope and recovery therefore apply unchanged. No dispatch service
  changes.
- **FR-005**: Scheduling:
  - At most 16 passes are open at once, in item order, and the next item's pass starts as one ends. A pass is one
    item's `Dispatch` execution plus its branch, if one runs.
  - 16 is a fixed constant, not an author-facing input. It applies whether or not the child waits:
    - a fire-and-forget `Dispatch` ends its pass with `Dispatched` straight away, so the window refills at once;
    - a waiting one holds its place until the child ends.
  - Elsa 3 dispatched all at once; the converter warns (FR-020). 16 is the starting value. Tuning it for GA is
    follow-up #5980.
- **FR-006**: Routing by the `Dispatch` execution's outcome:
  - `Dispatched` (the node did not wait): the pass ends. No branch runs.
  - `Completed`: `ChildCompleted` runs in that item's scope, when present.
  - `Faulted`, `Cancelled` or `DispatchFailed`: `ChildFaulted` runs in that item's scope, when present.
  - With no slot for the outcome, the pass ends.

  When every pass has ended, the activity completes with `Done`. With no items, it completes with `Done` at once.
- **FR-007**: The activity MUST fault with `BulkDispatchCapExceeded` before dispatching when `Items` has more than
  1,000 entries.
  - This is the release 1 cap of `docs/architecture/large-fan-out.md` decision 1, which says that a body starting a
    child workflow per item stays inside the same cap.
  - The cap is not an author-facing input.
  - The cap itself is under question in #5875 (Elsa 3 had none). #5875 settles it for the parallel ForEach and this
    activity together, and this spec follows the outcome.
  - The same cap applies whether or not the children are awaited (Q1).
- **FR-008**: Faults of child executions are never absorbed. The bulk activity handles no child faults itself, as
  `ForEach` and `If` do not, so the workflow's incident strategy decides.
  - This covers a fault of the `Dispatch` execution itself, of a slot child, or of an activity deeper inside a slot.
  - Under the default Fault strategy the workflow faults, matching Elsa 3, where the activity faulted.
  - Under ContinueWithIncidents the fault is a retryable incident on its pass. The other passes go on, and retrying the
    faulted activity (`RetryFaultedActivity`) lets the pass end and the bulk activity continue.
  - `ChildFaulted` is for children that ended badly (FR-006 outcomes), not for faults of the `Dispatch` node. The node
    raises those as plain exceptions, so they cannot be told apart, and absorbing them would hide recoverable errors.
- **FR-009**: The activity has one outcome, `Done`.
- **FR-010**: A slot reads its item as `variables.currentItem` and `variables.currentIndex`. Reading the dispatched
  child's result in a slot is not part of this spec (Out of Scope). Publish MUST refuse a direct, unscoped result
  binding between two different children of the bulk node (`Dispatch`, `ChildCompleted`, `ChildFaulted`, in any
  direction), with `ValueFlow/EarlierProducerRun` for an optional binding and `ValueFlow/UnavailableProducer` for a
  required one. Passes run concurrently, so such a binding cannot tell which item's run it reads. Result bindings between activities inside one slot are not affected. Bindings that declare a scope, and script expression parameters,
  keep their existing rules (#4056).
- **FR-010a**: Passes run concurrently, so `Dispatch` and both slots are unordered with themselves across passes.
  - A variable write under the bulk node, from `Dispatch` (an output capture) or from either slot, raises spec 266's
    publish warning `ValueFlow/ConcurrentWrite`. Spec 266 warns and never refuses, and this spec follows that rule.
  - Today's analyzer also flags variables declared inside a slot, because it does not stop at the declaring container.
    This spec does not change the analyzer.
  - The activity README (`essentials/src/Activities/DispatchWorkflow/Runtime/README.md`) explains the lost-update risk and the alternative: record per-child results in the child workflow, or
    read them after the bulk activity.
- **FR-010b**: The same activity README also states:
  - that only `RetryFaultedActivity` ends a faulted pass. `RescheduleActivity` is refused, because the faulted pass
    has a blocking incident (`BlockingIncident`);
  - that `DispatchFailed` reaches `ChildFaulted` only when the `Dispatch` node waits. A fire-and-forget child that
    fails to start is not seen by the bulk activity;
  - that a slot holding a `Flowchart` or another composite that handles its own child faults reports the fault as the
    slot's direct child, so the activity to retry is that direct child;
  - that under ContinueWithIncidents a faulted pass keeps its place in the window of 16 until it is retried, so 16
    open incidents stall the activity.

**Converter**

- **FR-011**: The Elsa 3 activity map entry MUST change from `NoCounterpart` to a catalog entry for Elsa 4
  `BulkDispatchWorkflows`, with every Elsa 3 member classified once.
  - The converter emits the bulk node with `Items` and the slots.
  - It also generates a `DispatchWorkflow` node in `Dispatch`, with id `{elsa3NodeId}.dispatch`, whose members FR-012
    to FR-016 set.
  - The node is built by the converter's rewrite, not normalized through the Elsa 3 `DispatchWorkflow` entry. So the
    `DispatchMembers` rules are mirrored explicitly in that rewrite, not applied directly. Where FR-013 differs
    (`WaitForCompletion` missing → true), the rewrite follows FR-013.
  - `ChildCompleted` and `ChildFaulted` are added to the Elsa 3 surface fixture as ports and mapped to the slots.
  - Converted branch activities keep their Elsa 3 ids.
- **FR-012**: `WorkflowDefinitionId`, `CancelChildOnParentCancellation`, `StartNewTrace` and `ChannelName` follow the
  existing Elsa 3 `DispatchWorkflow` member rules:
  - `WorkflowDefinitionId` is literal only and required;
  - `CancelChildOnParentCancellation` is synthetic false;
  - `StartNewTrace` and `ChannelName` are Removed, so a non-default binding is refused with
    `ELS3-ACTIVITY-REMOVED-INPUT-BOUND`; for `ChannelName` the default is null or an empty literal.
- **FR-013**: `WaitForCompletion` is carried to the `Dispatch` node.
  - Elsa 3's default is true, so a missing key becomes a literal true (`DefaultFlip`, as for `ExecuteWorkflow`).
  - `null` or a valueless literal reads as false, as in Elsa 3 `GetOrDefault`.
  - Literals are parsed regardless of case, so the corpus `"False"` counts.
- **FR-014**: `Items`:
  - an expression is carried;
  - `null` is left unbound;
  - any non-null literal is refused with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`. Elsa 3 read such a literal as raw
    JSON text and dispatched nothing; spec 1243 FR-017 refuses it the same way.
- **FR-015**: `CorrelationIdFunction` becomes the `Dispatch` node's `CorrelationId`.
  - `get{DefaultItemInputKey}()` and `args.<camelKey>` read the item and are resolved to `variables.currentItem`
    through the converter's name resolution.
  - Exception, as in Elsa 3: a workflow input or variable named like the generated accessor wins. Elsa 3's
    `EvaluatingJavaScript` handler overwrote the argument getter with it.
  - An absent function leaves `CorrelationId` unbound, as for Elsa 3 `DispatchWorkflow`.
- **FR-016**: `DefaultItemInputKey`, `Input` and the item become the `Dispatch` node's per-item `Inputs`
  expression.
  - **The target's declared inputs must be known**, from either of two sources:
    - the same import;
    - a workflow already published in Elsa 4, imported earlier or authored natively, whose declared inputs the
      converter reads from the published definition.

    These target declarations are used only for bulk dispatch. The existing #5808 check for plain
    `DispatchWorkflow`/`ExecuteWorkflow` keeps reading same-import targets only, so its behaviour does not change.

    A target in neither place is refused with `ELS3-ACTIVITY-BULK-DISPATCH-TARGET-UNKNOWN` (Q2).
  - **Contents**: the static `Input` first, then, for a map item, each declared input the item has, in the declared
    spelling. Members match declared names regardless of letter case, as in the #5808 rules. When two members differ
    only in case and match one declared name, the script faults that pass with a clear message rather than pick one; for any other item, `{DefaultItemInputKey: item}` when the target declares that name. The item wins on a
    clash.
  - **`DefaultItemInputKey`** must be a literal, null or absent (null and absent mean `Item`). Anything else is refused
    with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`.
  - **The static `Input`**: a literal or Object map is checked with the #5808 rules and embedded as constant entries.
    An `Input` in any expression language, JavaScript included, is refused with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`.
    Guidance: put those values in the items, or use a literal map. The converter lowers Elsa 3 scripts only after the
    node rewrite that builds `Inputs`, so it cannot merge a script there. No corpus definition uses a static
    `Input`.
  - **Script form**: the generated script names declared inputs literally and reads item members only through
    `Object.entries`/`filter`/`Object.fromEntries`, never by computed member access, which the Elsa 4 read analysis
    treats as possible workflow-state access.
  - **`ParentInstanceId`** is not passed, because the parent link lives in the dispatch record.
  - **Warning**: a conversion raises warning `ELS3-BEHAVIOUR-BULK-DISPATCH-ITEM-INPUTS`. It says that only declared
    inputs are passed, that a child-declared `ParentInstanceId` is no longer filled, and that `ChildFaulted` now also runs for cancelled and undispatchable children (FR-018). It
    also says that without a correlation function the child now inherits the parent's correlation id, where Elsa 3
    bulk dispatch gave it none. It also says that an expression `WaitForCompletion` is now evaluated once per pass on
    the `Dispatch` node, where Elsa 3 evaluated it once.
- **FR-017**: Outcomes:
  - `Done` maps to `Done`.
  - `Completed` (Elsa 3 emitted it with `Done` when it waited and had items):
    - dropped without a diagnostic when `Done` reaches every one of its targets and none is a `WaitAll` join;
    - dropped with `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED` when the wait is a literal false;
    - otherwise refused with `ELS3-ACTIVITY-BULK-DISPATCH-OUTCOME-ROUTE`. Guidance: connect the follow-up to `Done`.
  - `Canceled` (never emitted): dropped with `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED`, or refused with
    `…-OUTCOME-ROUTE` when it leads to a `WaitAll` join, because dropping that edge would make the join run where Elsa
    3 never ran it.

  For the corpus root (Done, Canceled and Completed into one `WaitAny` join) this leaves one edge, and the join
  collapses.
- **FR-018**: Branch differences.
  - Elsa 3 ran `ChildFaulted` only for faulted children. Elsa 4 also runs it for cancelled and undispatchable
    children (FR-006), where Elsa 3 ran nothing. The `ITEM-INPUTS` warning text states this.
  - Elsa 3 never ran the branches without waiting. With a literal-false wait the converter drops them with
    `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED`. With an expression wait, a pass whose `Dispatch` did not wait ends with
    `Dispatched`, so no branch runs (FR-006).
- **FR-019**: Refusals:

| Elsa 3 use | Result |
|---|---|
| `WorkflowDefinitionId` not a literal | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` |
| `ChannelName` or `StartNewTrace` bound | `ELS3-ACTIVITY-REMOVED-INPUT-BOUND` |
| `Items` a non-null literal | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`, guidance: Elsa 3 read it as text and dispatched no child |
| `DefaultItemInputKey` not a literal, or a static `Input` that is an expression (JavaScript or another language) | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`, guidance: put the values in the items, or use a literal map |
| Target workflow neither in the import nor published in Elsa 4 | `ELS3-ACTIVITY-BULK-DISPATCH-TARGET-UNKNOWN`, guidance: import the target first, or in the same run |
| `Completed`/`Canceled` route with no faithful meaning (FR-017) | `ELS3-ACTIVITY-BULK-DISPATCH-OUTCOME-ROUTE` |
| A branch reading what Elsa 3 passed it: the `ChildInstanceId` variable, or the input `WorkflowOutput`, `WorkflowInstanceId`, `WorkflowStatus` or `WorkflowSubStatus` | `ELS3-ACTIVITY-BULK-DISPATCH-CHILD-RESULT-READ`, raised in name resolution. Guidance: remove the read; the child's result is not available in the branch yet |

- **FR-020**: Every conversion MUST raise warning `ELS3-BEHAVIOUR-BULK-DISPATCH-CAPPED`, as an entry-level behaviour
  warning on the map entry. It says that more than 1,000
  items fault in Elsa 4 (FR-007), and that at most 16 passes are open at once (FR-005), where Elsa 3 dispatched every
  child at once. It points to batching with a sequential `ForEach` over batches.
- **FR-020a**: A conversion where an activity inside a converted `ChildCompleted` or `ChildFaulted` branch writes a
  variable declared outside the bulk node MUST raise behaviour warning `ELS3-BEHAVIOUR-BULK-DISPATCH-OUTER-WRITE`.
  - An Elsa 3 composite inside a branch, for example a `Flowchart` or `Sequence`, can declare its own variables. Writes
    to those are declared inside the bulk node, are not outer writes, and raise no warning.
  - Writes in the correlation function or the static `Input` are already refused as input expressions
    (`ForbiddenCapability`). The generated `Dispatch` child has no output capture. Guidance: Elsa 3 ran
  the branches one at a time, and Elsa 4 runs them concurrently, so a shared counter or list can lose updates. Record
  per-child results in the child workflow, or compute the total after the bulk activity. The definition still
  converts, and publish adds Elsa 4's own `ValueFlow/ConcurrentWrite` warning (FR-010a).
- **FR-021**: New codes go to `Elsa3ActivityDiagnosticCodes` and the spec 236 diagnostic-code contract, each in the
  task that first raises it.
  - Errors: `…-BULK-DISPATCH-TARGET-UNKNOWN`, `…-BULK-DISPATCH-OUTCOME-ROUTE`, `…-BULK-DISPATCH-CHILD-RESULT-READ`.
  - Warnings: `…-BULK-DISPATCH-ITEM-INPUTS`, `…-BULK-DISPATCH-CAPPED`, `…-BULK-DISPATCH-OUTER-WRITE`.

**Documentation and evidence**

- **FR-022**: The JTest effect is handed to S-15, which owns the ledger.
  - `performance-concurrency.json` converts unchanged, with its child in the same import.
  - The composition root needs an adjusted copy, for coordinator approval: a literal `WorkflowDefinitionId` and a
    cleared `ChannelName`, the precedent of `composition-dispatch-root-workflow.json`.
  - `…-await-completion`'s wait dataset depends on `WriteHttpResponse` after a resume (Elsa 3's HTTP-context switch).
    S-15 classifies it with coordinator approval.
- **FR-023**: Documentation:
  - a README for the activity next to `essentials/src/Activities/DispatchWorkflow/Runtime/README.md`, with the cap and
    the batching advice;
  - the converter mapping README;
  - the spec 236 member map.
- **FR-024**: No new scheduler is needed. Every `ScheduleChildActivity` call gets its own activity execution id, and
  nothing deduplicates by node (`SchedulerWorkItems`), so one node can run once per item at the same time.
  - 1242 uses the shared runtime contracts `LoopIterationScopeRequest` and
    `ActivitySchedulingProvenance.From(…, iterationId)`, as `ForEach` and BPMN do.
  - It keeps two small helpers local in the DispatchWorkflow package rather than referencing
    `Elsa.Activities.ControlFlow` ("duplication beats dependency"): the item-indexed iteration id and the collection
    resolution.
  - If spec 1243 later moves those helpers to a shared runtime project, 1242 may switch to them. It does not wait for
    that.

### Key Entities

- **BulkDispatchWorkflows**: the Elsa 4 structural activity: `Items`, the `Dispatch` slot, and two branch slots.
- **Item pass**: one item's iteration. It holds the item scope, one `DispatchWorkflow` child execution and at most one
  branch execution.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author starts N children (N ≤ 1,000) with one activity. At most 16 passes are open at once, and the
  next starts as one ends. With waiting, the activity ends only after all N children have ended.
- **SC-002**: `performance-concurrency.json` converts and publishes unchanged, and its 300 children reach `Finished`.
- **SC-003**: The approved adjusted composition root converts and publishes. The correlation-id, input, channels and
  on-completed-callbacks suites pass, and so does the no-wait dataset of await-completion.
- **SC-004**: No change to any dispatch service: `WorkflowDispatchIdentity`, the stager, `DispatchPinSource`,
  resume, cancellation, audit and recovery.
- **SC-005**: Every FR-017 and FR-019 row has a converter test with its code and guidance.

## Assumptions

- Scheduling one node under distinct iteration ids already exists (BPMN multi-instance), and per-pass scopes follow
  ADR 0028, so no ADR is needed for the activity's mechanics.
- The cap and the window of 16 take their numbers from `docs/architecture/large-fan-out.md` decision 1. That decision
  was made for the clean-slate kernel's parallel ForEach, and it exposes the degree as an author input
  (`MaxDegreeOfParallelism`, 1–64), as spec 1243 does. This spec fixes 16 with no input, because of the
  no-author-knobs stance. That departs from decision 1's shape and from spec 1243. The coordinator kept 16 as the
  starting value; tuning it for GA is #5980 (Q4).
- The release 1 cap of 1,000 applies (FR-007, Q1, #5875).

## Out of Scope

- Reading the dispatched child's result (outputs, status) in a branch. Spec 1245 (#5838) gives an awaited
  `DispatchWorkflow` an `Output`; exposing it per item to the branches is a follow-up.
- Dispatch channels, trace options, a degree-of-parallelism or batch input.
- Editing the S-15 ledger or adjusted JTest copies.

## Coordinator answers (2026-11-27; all questions settled)

1. **Q1, the cap**: settled in #5875, which covers the parallel ForEach and this activity. Sipke is looking at it.
   FR-007 follows its outcome, with one rule for waiting and fire-and-forget children.
2. **Q2, the target outside the import**: a target already published in Elsa 4 (imported earlier or authored natively)
   is valid. The converter
   reads its declared inputs from the published Elsa 4 definition. It refuses only a target that is neither in the
   import nor published (FR-016).
3. **Q3, `ChildFaulted` for cancelled and undispatchable children**: agreed. A separate `ChildCanceled` slot can be
   added later if needed; it is not part of this spec.
4. **Q4, the window of 16**: kept as the starting value. Tuning it for GA is follow-up #5980.
5. **Q5, outer writes**: follow Elsa 4. Writing an outer variable from concurrent passes is deliberately allowed with a
   warning (spec 266: `ValueFlow/ConcurrentWrite`, "a warning, never a refusal"). So the converter does not refuse it.
   It raises behaviour warning `ELS3-BEHAVIOUR-BULK-DISPATCH-OUTER-WRITE` with guidance (FR-020a), because Elsa 3 ran
   the branches one at a time and a converted counter can now lose updates.
