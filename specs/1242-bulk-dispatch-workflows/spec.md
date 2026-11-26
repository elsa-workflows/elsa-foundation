# Feature Specification: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Feature Branch**: `agent/claude/3052-spec1242-bulk-dispatch`

**Created**: 2026-11-26

**Status**: Draft

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

**A. A dedicated `BulkDispatchWorkflows` activity (recommended).** It is a structural activity. For each item it
schedules one ordinary `DispatchWorkflow` child execution, with its own iteration id and a scope that holds the item. It
has optional `ChildCompleted` and `ChildFaulted` slots and completes with `Done`.

- Each item's dispatch is a separate activity execution, so the dispatch identity scheme, specs 096–104 and every
  dispatch consumer apply unchanged.
- Authors get one activity for "start this workflow per item". The converter maps the Elsa 3 node to it one to one.
- The runtime work is in the activity itself: scheduling the same dispatch node once per item at the same time, under
  distinct iteration ids, and counting the children. Spec 1243's parallel ForEach needs the same scheduling, so the two
  specs share that piece (FR-014).

**B. One stateful activity that dispatches N children itself**, with item-keyed identities.

- This breaks the "one dispatch per activity execution" contract (spec 096 FR-012). The stager, resume, cancellation,
  control boundary, audit and recovery would all change, and it needs an ADR. Rejected.

**C. No new activity: the converter lowers Elsa 3's node to a loop with a `DispatchWorkflow` body.** This was this
spec's first draft.

- Elsa 4 authors would still have no bulk dispatch activity.
- Every Elsa 3 rule would become converter machinery: outcome rewiring, current-item scope registration, per-pass
  re-evaluation of the wait.
- The waiting path would inherit spec 1243's cap and degree. Rejected on review (coordinator, 2026-11-26).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An author starts a workflow per item and waits for all of them (Priority: P1)

An author adds `BulkDispatchWorkflows` to `acme.order-received`:

- `Workflow`: `acme.fulfil-line`;
- `Items`: the order lines;
- `Inputs`: `{ sku: currentItem.sku, quantity: currentItem.quantity }`;
- `CorrelationId`: `` `order-17_${currentIndex + 1}` ``;
- `WaitForCompletion`: true;
- a `WriteLine` in `ChildFaulted`.

**Why this priority**: This is the feature. Without it, Elsa 4 has no way to start N children and wait for them as one
step.

**Independent Test**: Run the workflow with three lines, one of whose children faults. Three children start at once,
each with its own inputs and correlation id. `ChildFaulted` runs once, and the activity completes with `Done` after the
third child has ended.

**Acceptance Scenarios**:

1. **Given** three items, **When** the activity runs with waiting, **Then** three children start before any of them
   ends, and the activity completes with `Done` only after all three have ended.
2. **Given** a child completes, **When** it ends, **Then** `ChildCompleted` runs once for it, with `currentItem` and
   `currentIndex` of that item in scope.
3. **Given** a child faults, is cancelled, or cannot be dispatched (`DispatchFailed`), **When** it ends, **Then**
   `ChildFaulted` runs once for it. A cancelled child counts as faulted for this branch.
4. **Given** an empty slot, **When** a child ends, **Then** nothing runs for it and it only counts toward completion.
5. **Given** an empty or null `Items`, **When** the activity runs, **Then** it completes with `Done` at once.

### User Story 2 - Fire and forget (Priority: P1)

The same activity with `WaitForCompletion` false starts every child and completes with `Done` once all are dispatched.
The child slots are not run.

**Why this priority**: The second main use, and `performance/concurrency-tests.json` asserts it with 300 items.

**Independent Test**: Run with 300 items and no wait. The parent completes. All 300 children start with the given
correlation id and reach `Finished`.

**Acceptance Scenarios**:

1. **Given** `WaitForCompletion` false, **When** the activity runs, **Then** it completes with `Done` without waiting
   for any child, and no slot runs.
2. **Given** the parent instance is cancelled after the activity completed, **When** children are still running,
   **Then** the children are not cancelled. A child is cancelled only through `CancelChildOnParentCancellation`, which
   applies while the activity is still waiting, exactly as for `DispatchWorkflow`.

### User Story 3 - The converter maps Elsa 3 BulkDispatchWorkflows one to one (Priority: P1)

A migrating engineer converts an Elsa 3 definition that uses `BulkDispatchWorkflows`. It becomes an Elsa 4
`BulkDispatchWorkflows` with the same items, inputs, correlation ids, wait and branches.

**Why this priority**: It unblocks the 2 refused corpus definitions and their 6 suites.

**Independent Test**: Convert `performance-concurrency.json` with its child in the same import. It publishes unchanged.
Convert the composition root's approved adjusted copy (FR-022). It publishes, and the correlation-id, input, channels
and on-completed-callbacks suites pass.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 node with `getItem().correlationIdSuffix` in its correlation function, **When** it is
   converted, **Then** the Elsa 4 `CorrelationId` reads `variables.currentItem.correlationIdSuffix`.
2. **Given** map items and a target in the same import, **When** converted, **Then** the Elsa 4 `Inputs` passes each
   input the target declares that the item has, in the declared spelling.
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
  dispatched nothing; the converter documents the difference.
- **Two waiting activities in one parent instance**: each keeps its own children. Elsa 3's shared per-instance
  bookmark and counter is not reproduced.
- **A slot activity faults**, for example the `WriteLine` in `ChildFaulted`: the bulk activity faults, the same as when
  an `If` branch faults. Other children keep running and their branches are not run while the instance is faulted. The
  instance's fault strategy decides what happens next.

## Requirements *(mandatory)*

### Functional Requirements

**The Elsa 4 activity**

- **FR-001**: Elsa 4 MUST provide `BulkDispatchWorkflows` (type key `Elsa.BulkDispatchWorkflows`) in the
  DispatchWorkflow activity package, as a structural activity.
- **FR-002**: Inputs:
  - `WorkflowDefinitionId`: required, a literal pinned at publish, as for `DispatchWorkflow`;
  - `Items`: the collection, evaluated once;
  - `Inputs`: the child inputs, evaluated once per item with `currentItem` and `currentIndex` in scope;
  - `CorrelationId`: evaluated once per item with the same scope. A blank value inherits the parent's, as for
    `DispatchWorkflow`;
  - `WaitForCompletion`: bindable, evaluated once, default false, as for `DispatchWorkflow`;
  - `CancelChildOnParentCancellation`: default true, applied to each child, as for `DispatchWorkflow`.

  There is no channel, trace, degree or batch input.
- **FR-003**: Slots: `ChildCompleted` and `ChildFaulted`, each optional and single, as `If.Then` and `If.Else` are.
  Studio renders them through the generic slot rendering, with no Studio-specific code.
- **FR-004**: For each item, the activity MUST schedule one `DispatchWorkflow` child execution with:
  - its own iteration id (`StructuredExecutionIdentity.Iteration`, index = item position);
  - an iteration scope holding `currentItem` and `currentIndex`, as `ForEach` provides (ADR 0028);
  - the activity's `WorkflowDefinitionId`, `Inputs`, `CorrelationId`, `CancelChildOnParentCancellation` and evaluated
    `WaitForCompletion`.

  That dispatch node is part of the activity's authored structure but not a visible slot, as the BPMN importer
  generates its call-activity `DispatchWorkflow`. Publish therefore pins it, and the dispatch identity, stager, resume,
  cancellation, audit, test-run scope and recovery apply unchanged.
- **FR-005**: All dispatch child executions MUST be scheduled when the activity starts, so the children run at the same
  time, as in Elsa 3.
- **FR-006**: Completion:
  - **Without waiting**: each dispatch child ends with `Dispatched`, and the activity completes with `Done` once every
    item has been dispatched.
  - **With waiting**:
    - A dispatch child ending with `Completed` schedules `ChildCompleted`, when present, in that item's iteration scope.
    - A dispatch child ending with `Faulted`, `Cancelled` or `DispatchFailed`, or faulting itself (for example on an
      undeclared input name), schedules `ChildFaulted`, when present, in that item's iteration scope. The fault is
      handled there and does not fault the bulk activity.
    - The activity completes with `Done` once every item's dispatch child, and its branch when one runs, has ended.
  - With no items, the activity completes with `Done` at once.
- **FR-007**: The activity MUST fault with `BulkDispatchCapExceeded` before dispatching when `Items` has more than
  1,000 entries. That is the release 1 cap of `docs/architecture/large-fan-out.md` decision 1, which states that a body
  starting a child workflow per item stays inside the same cap. The cap is not an author-facing input. Whether it
  applies without waiting too is Q1.
- **FR-008**: A slot activity that faults MUST fault the bulk activity through the ordinary child-fault path, as for
  `If`. Retrying the faulted slot activity, where the runtime supports it, lets the bulk activity continue.
- **FR-009**: The activity has one outcome, `Done`.
- **FR-010**: A branch reads its item as `variables.currentItem` and `variables.currentIndex`. Reading the dispatched
  child's result in the branch is not part of this spec (Out of Scope).

**Converter**

- **FR-011**: The Elsa 3 activity map entry MUST change from `NoCounterpart` to a catalog entry for Elsa 4
  `BulkDispatchWorkflows`, with every Elsa 3 member classified once.
  - `ChildCompleted` and `ChildFaulted` are added to the Elsa 3 surface fixture as ports and mapped to the slots.
  - Converted branch activities keep their Elsa 3 ids.
- **FR-012**: `WorkflowDefinitionId`, `CancelChildOnParentCancellation`, `StartNewTrace` and `ChannelName` follow the
  existing Elsa 3 `DispatchWorkflow` member rules:
  - `WorkflowDefinitionId` is literal only and required;
  - `CancelChildOnParentCancellation` is synthetic false;
  - `StartNewTrace` and `ChannelName` are Removed, so a non-default binding is refused with
    `ELS3-ACTIVITY-REMOVED-INPUT-BOUND`; for `ChannelName` the default is null or an empty literal.
- **FR-013**: `WaitForCompletion` is carried.
  - Elsa 3's default is true, so a missing key becomes a literal true (`DefaultFlip`, as for `ExecuteWorkflow`).
  - `null` or a valueless literal reads as false, as in Elsa 3 `GetOrDefault`.
  - Literals are parsed regardless of case, so the corpus `"False"` counts.
- **FR-014**: `Items`:
  - an expression is carried;
  - `null` is left unbound;
  - any non-null literal is refused with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`. Elsa 3 read such a literal as raw
    JSON text and dispatched nothing; spec 1243 FR-017 refuses it the same way.
- **FR-015**: `CorrelationIdFunction` becomes `CorrelationId`.
  - `get{DefaultItemInputKey}()` and `args.<camelKey>` read the item and are resolved to `variables.currentItem`
    through the converter's name resolution.
  - Exception, as in Elsa 3: a workflow input or variable named like the generated accessor wins. Elsa 3's
    `EvaluatingJavaScript` handler overwrote the argument getter with it.
  - An absent function leaves `CorrelationId` unbound, as for Elsa 3 `DispatchWorkflow`.
- **FR-016**: `DefaultItemInputKey`, `Input` and the item become the Elsa 4 per-item `Inputs` expression.
  - **The target must be in the same import**, so its declared inputs are known. Otherwise the node is refused with
    `ELS3-ACTIVITY-BULK-DISPATCH-TARGET-UNKNOWN` (Q2).
  - **Contents**: the static `Input` first, then, for a map item, each declared input the item has, in the declared
    spelling; for any other item, `{DefaultItemInputKey: item}` when the target declares that name. The item wins on a
    clash.
  - **`DefaultItemInputKey`** must be a literal, null or absent (null and absent mean `Item`). Anything else is refused
    with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`.
  - **The static `Input`**: a literal or Object map is checked with the #5808 rules. A JavaScript map is embedded. Any
    other language is refused with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`.
  - **Script form**: the generated script names declared inputs literally and reads item members only through
    `Object.entries`/`filter`/`Object.fromEntries`. Elsa 4 publish refuses computed member access as workflow-state
    access (VF-STATE-002).
  - **`ParentInstanceId`** is not passed, because the parent link lives in the dispatch record.
  - **Warning**: a conversion raises warning `ELS3-BEHAVIOUR-BULK-DISPATCH-ITEM-INPUTS`. It says that only declared
    inputs are passed, that a child-declared `ParentInstanceId` is no longer filled, and that the static `Input` is now
    evaluated per item.
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
    `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED`; with an expression wait Elsa 4 skips them at run time.
- **FR-019**: Refusals:

| Elsa 3 use | Result |
|---|---|
| `WorkflowDefinitionId` not a literal | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` |
| `ChannelName` or `StartNewTrace` bound | `ELS3-ACTIVITY-REMOVED-INPUT-BOUND` |
| `Items` a non-null literal | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`, guidance: Elsa 3 read it as text and dispatched no child |
| `DefaultItemInputKey` not a literal, or static `Input` in another language | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` |
| Target workflow not in the import | `ELS3-ACTIVITY-BULK-DISPATCH-TARGET-UNKNOWN`, guidance: import the target in the same run |
| `Completed`/`Canceled` route with no faithful meaning (FR-017) | `ELS3-ACTIVITY-BULK-DISPATCH-OUTCOME-ROUTE` |
| A branch reading what Elsa 3 passed it: the `ChildInstanceId` variable, or the input `WorkflowOutput`, `WorkflowInstanceId`, `WorkflowStatus` or `WorkflowSubStatus` | `ELS3-ACTIVITY-BULK-DISPATCH-CHILD-RESULT-READ`, raised in name resolution. Guidance: remove the read; the child's result is not available in the branch yet |

- **FR-020**: Every conversion MUST raise warning `ELS3-BEHAVIOUR-BULK-DISPATCH-CAPPED`. It says that more than 1,000
  items fault in Elsa 4 (FR-007), where Elsa 3 dispatched them all, and points to batching with a sequential `ForEach`
  over batches.
- **FR-021**: New codes go to `Elsa3ActivityDiagnosticCodes` and the spec 236 diagnostic-code contract, each in the
  task that first raises it.
  - Errors: `…-BULK-DISPATCH-TARGET-UNKNOWN`, `…-BULK-DISPATCH-OUTCOME-ROUTE`, `…-BULK-DISPATCH-CHILD-RESULT-READ`.
  - Warnings: `…-BULK-DISPATCH-ITEM-INPUTS`, `…-BULK-DISPATCH-CAPPED`.

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
- **FR-024**: Spec 1243's parallel ForEach also schedules the same body node once per item under distinct iteration
  ids. The two specs MUST share one runtime mechanism for that, whichever is implemented first, instead of building
  two.

### Key Entities

- **BulkDispatchWorkflows**: the Elsa 4 structural activity. It has inputs (FR-002), two branch slots and an internal
  dispatch node.
- **Item pass**: one item's iteration. It holds the item scope, one `DispatchWorkflow` child execution and at most one
  branch execution.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author starts N children (N ≤ 1,000) with one activity. All N start before the first one ends, and
  with waiting the activity ends only after all N have ended.
- **SC-002**: `performance-concurrency.json` converts and publishes unchanged, and its 300 children reach `Finished`.
- **SC-003**: The approved adjusted composition root converts and publishes. The correlation-id, input, channels and
  on-completed-callbacks suites pass, and so does the no-wait dataset of await-completion.
- **SC-004**: No change to `WorkflowDispatchIdentity`, the dispatch stager, resume, cancellation, audit or recovery.
- **SC-005**: Every FR-017 and FR-019 row has a converter test with its code and guidance.

## Assumptions

- A structural activity can generate a `DispatchWorkflow` node into its authored structure without a visible slot,
  the way the BPMN importer generates its call-activity `DispatchWorkflow`, and publish then pins it.
- Scheduling the same node concurrently under distinct iteration ids is new runtime behaviour. No ADR forbids it:
  ADR 0028 gives each pass its own iteration id and scope, and large-fan-out.md plans per-item frames. No ADR is needed
  unless the implementation has to depart from those.
- The release 1 cap of 1,000 applies (FR-007, Q1).

## Out of Scope

- Reading the dispatched child's result (outputs, status) in a branch. Spec 1245 (#5838) gives an awaited
  `DispatchWorkflow` an `Output`; exposing it per item to the branches is a follow-up.
- Dispatch channels, trace options, a degree-of-parallelism or batch input.
- Editing the S-15 ledger or adjusted JTest copies.

## Open questions for the coordinator

1. **Q1**: Does the 1,000-item cap apply without waiting too? Recommendation: yes, one rule. A fire-and-forget run
   still writes one dispatch record and start intent per item in the parent's commit.
2. **Q2**: Converter: refuse a target outside the import (recommended), or warn? Without the target's declarations an
   undeclared item member faults the child's dispatch at run time.
3. **Q3**: `ChildFaulted` for cancelled and undispatchable children (FR-006, FR-018), where Elsa 3 ran nothing.
   Recommendation: yes. An author needs one place to see every child that did not complete.
