# Feature Specification: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Feature Branch**: `agent/claude/3052-spec1242-bulk-dispatch`

**Created**: 2026-11-26

**Status**: Draft

**Issue**: #5833, part of initiative #3052 (Elsa 3 to Elsa 4 conversion). Parity record: #1152.

**Input**: Elsa 3's `BulkDispatchWorkflows` activity has no Elsa 4 counterpart, so the converter refuses every definition
that uses it with `ELS3-ACTIVITY-NO-COUNTERPART`. Define the Elsa 4 counterpart, how the converter maps the Elsa 3
activity to it, and which Elsa 3 behaviours are refused with guidance instead of emulated. The sibling gap,
ParallelForEach, is spec 1243.

## Problem

Elsa 3 (elsa-core 3.6.3) `BulkDispatchWorkflows` starts one instance of a published workflow for each item of a
collection. It can wait until every child has finished. While it waits it runs a `ChildCompleted` branch for each child
that finishes and a `ChildFaulted` branch for each child that faults. Elsa 4 has `DispatchWorkflow` (one child), a
sequential `ForEach` and no parallel loop. The converter has no target for the Elsa 3 activity, so it refuses the whole
definition.

**Example.** Workflow `acme.order-received` gets an order with three lines and must start `acme.fulfil-line` once per
line. Each child gets the line's members as inputs and the correlation id `order-17_1`, `order-17_2` or `order-17_3`.
The parent continues only after all three children have finished, and it logs every child that faulted. In Elsa 3 that
is one `BulkDispatchWorkflows` node:

- `Items`: the order lines;
- `CorrelationIdFunction`: `` `order-17${getItem().suffix}` ``;
- `WaitForCompletion`: true;
- `ChildFaulted`: a `WriteLine` node.

Converted to Elsa 4, the definition is refused. To get it back, the author has to work out the Elsa 4 shape by hand,
and a sequential `ForEach` around an awaited `DispatchWorkflow` waits for each child before it starts the next.

In the S-15 JTest corpus (branch `agent/claude/3052-s15-jtest-impl`, ledger `extensions/Elsa3/tests/JTest/ledger/`)
this refuses 2 definitions, `composition-bulk-dispatch-root-workflow.json` and `performance-concurrency.json`. Those 2
definitions block 6 suites: the five `composition/bulk-dispatch-workflow-*` suites and `performance/concurrency-tests.json`.

### What Elsa 3 actually does

The facts below are from the elsa-core 3.6.3 source: `BulkDispatchWorkflows`, `ResumeBulkDispatchWorkflowActivity` and
the component tests.

| Aspect | Elsa 3 behaviour |
|---|---|
| Inputs | `WorkflowDefinitionId` (any expression, resolved to the published version at run time), `Items`, `DefaultItemInputKey` (default `Item`), `CorrelationIdFunction` (evaluated once per item), `Input` (an extra dictionary), `WaitForCompletion` (default true), `StartNewTrace`, `ChannelName` |
| Items | Evaluated once. Null gives zero items. A value that is not a collection also gives zero items, silently |
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

- `DispatchWorkflow` dispatches one child. Its target is pinned when the parent publishes, so it must be a literal. It
  has `Inputs`, a bindable `WaitForCompletion`, `CorrelationId` (a blank value inherits the parent's) and
  `CancelChildOnParentCancellation`.
  - Outcomes: `Dispatched`, `Completed`, `Faulted`, `Cancelled`, `DispatchFailed`.
  - Every dispatch id is derived from the parent activity execution, so one activity execution yields exactly one
    child.
  - Elsa 4 refuses a child input name that the child does not declare.
  - There are no channels and no trace option. Activation channels are trigger slots, not dispatch queues.
- `ForEach` is sequential. Its body is a single child slot that reads `currentItem` and `currentIndex`, and each pass is
  its own iteration frame, so it has its own activity execution.
- There is no parallel loop yet. Spec 1243 (#5832) defines one:
  - input `Collection`, read once;
  - every pass is scheduled at start, and each pass can suspend on its own bookmark;
  - `Done` once all passes have completed;
  - a faulted pass does not fault the loop, and the other passes keep running, as in `Sequence`;
  - the body reads `variables.currentItem` and `variables.currentIndex`;
  - there is no parallelism setting and no cap.

  That shape is proposed, not yet approved.
- `docs/architecture/large-fan-out.md` (decision 1) planned a different parallel ForEach: an `Items` input, a cap of
  1,000 items, and `MaxDegreeOfParallelism` (default 16). Spec 1243 departs from that, and this spec follows spec 1243,
  the newer and more specific source. If spec 1243's approval brings back a cap or a degree, FR-010 applies.
- Background eligibility (ADR 0093, spec 1230) does not apply: dispatching fails criterion C6, waiting fails C2 and a
  loop fails C1.

## Options

**A. A new `DispatchWorkflows` activity.** It dispatches N children and keeps one bookmark and a count, with
child-completed and child-faulted slots.

- Cost: a new dispatch identity per item, because `WorkflowDispatchIdentity` yields one child per activity execution.
- Cost: an aggregate wait, and re-deciding the audit, cancellation, test-run scope and recovery rules that specs
  096–104 settled for one child.
- Cost: a second fan-out mechanism next to the parallel ForEach. The identity change needs an ADR.

**B. No new activity: the converter lowers it to a loop with a `DispatchWorkflow` body.**

- If the activity does not wait, the loop is a `ForEach` around a fire-and-forget `DispatchWorkflow`. The children
  still run at the same time, because a fire-and-forget dispatch does not block the loop.
- If it waits, the loop is spec 1243's parallel ForEach around an awaited `DispatchWorkflow`, whose `Completed` and
  `Faulted` outcomes lead to the converted child branches.
- Each pass has its own activity execution, so each child gets its own dispatch identity without any change.
- A child that faults leads to its `ChildFaulted` branch, or to nothing, and its pass completes. That matches Elsa 3,
  which counted a faulted child toward completion.
- Every rule for one dispatch carries over as it is: pinning, input validation, audit, cancellation and recovery.

**C. Keep refusing, with guidance** that tells the author to build the loop by hand.

- The rewrite is mechanical and the gap is real, so C leaves work to every migrating team.

**Recommendation: B.** Elsa 4's model for "one child per item" is a loop with a dispatch body, as large-fan-out.md
already describes. B adds no runtime surface, needs no ADR and no Studio work, and closes the gap once spec 1243 lands. The
fire-and-forget path does not need spec 1243 at all.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Fire-and-forget bulk dispatch converts and runs (Priority: P1)

A migrating engineer converts an Elsa 3 workflow whose `BulkDispatchWorkflows` has `WaitForCompletion` set to a
literal false. The converted workflow publishes and starts one child per item, each with its inputs and correlation
id, and continues without waiting.

**Why this priority**: It needs nothing beyond what Elsa 4 has today, and it unblocks `performance-concurrency.json`.

**Independent Test**: Convert `performance-concurrency.json` unchanged. It publishes. Run it with `ConcurrencyCount`
300: the root finishes with its output, and 300 children with the root's dispatch correlation id reach `Finished`.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 node with literal `WaitForCompletion` false, **When** it is converted, **Then** the result is a
   `ForEach` over `Items` whose body is a `DispatchWorkflow` with `WaitForCompletion` false. The Elsa 3 `Done`
   connection becomes the loop's `Done` connection.
2. **Given** that conversion, **When** the workflow runs with 3 items, **Then** 3 children start, and the parent
   continues without waiting for them.
3. **Given** the node has `ChildCompleted` or `ChildFaulted` branches, **When** it is converted, **Then** the branches
   are dropped with info `ELS3-BULK-DISPATCH-UNREACHABLE`, because Elsa 3 never runs them without waiting.

### User Story 2 - Waiting bulk dispatch converts and runs per-child branches (Priority: P1)

A migrating engineer converts an Elsa 3 node that waits, or whose `WaitForCompletion` is an expression. The converted
workflow starts every child, runs the converted `ChildCompleted` branch for each child that finishes and the
`ChildFaulted` branch for each child that faults, and continues once every child has ended.

**Why this priority**: It is the main behaviour of the activity and of 4 of the 6 suites.

**Independent Test**: Convert the example from the Problem section: 2 items, one of whose children faults. It
publishes. The `ChildFaulted` `WriteLine` runs once, the `ChildCompleted` `WriteLine` runs once, and the parent
continues once both children have ended.

**Acceptance Scenarios**:

1. **Given** `WaitForCompletion` is missing, a literal true or an expression, **When** the node is converted, **Then** the result
   is a parallel ForEach over `Items`. Its body is a flowchart with a `DispatchWorkflow`, whose `WaitForCompletion`
   carries the Elsa 3 value. `Completed` leads to the converted `ChildCompleted` activity and `Faulted` to the
   converted `ChildFaulted` activity.
2. **Given** `WaitForCompletion` evaluates to false at run time, **When** the converted workflow runs, **Then** no
   branch runs and the parent continues after dispatching, as in Elsa 3.
3. **Given** a child is cancelled, or its branch is empty, **When** it ends, **Then** its pass ends and no branch runs.
4. **Given** spec 1243 has not landed, **When** such a node is converted, **Then** it stays refused with
   `ELS3-ACTIVITY-NO-COUNTERPART` and guidance naming spec 1243.

### User Story 3 - Each child gets the inputs and correlation id it got in Elsa 3 (Priority: P1)

The child of every item gets the item's members, or the item under `DefaultItemInputKey`, plus the static `Input`, under
the names the child declares. It also gets the correlation id that `CorrelationIdFunction` computes for that item.

**Why this priority**: The correlation-id and input suites assert exactly this, and real workflows correlate children
this way.

**Independent Test**: Use items `[{ "ShouldFault": true, "correlationIdSuffix": "_abc" }]` with a child that declares
only `ShouldFault`, and a function that returns the parent correlation id plus the suffix. The child's input
`ShouldFault` is `true`, its correlation id is the parent's id plus `_abc`, and the dispatch is not refused for the
undeclared `correlationIdSuffix`.

**Acceptance Scenarios**:

1. **Given** a map item and a target in the same import, **When** the child is dispatched, **Then** it gets each of its
   declared inputs that the item has, in the declared spelling. Other item members are not passed.
2. **Given** an item that is not a map, **When** the child is dispatched, **Then** it gets the item under
   `DefaultItemInputKey`, provided the child declares that name.
3. **Given** `CorrelationIdFunction` uses `getItem()`, or `get{DefaultItemInputKey}()` for a non-default key, **When**
   it is converted, **Then** that call reads the loop's current item, and the function becomes the body dispatch's
   `CorrelationId`.

### User Story 4 - Elsa 3 behaviours with no faithful Elsa 4 meaning are refused with guidance (Priority: P2)

When a definition depends on an Elsa 3 behaviour that Elsa 4 does not have, the converter refuses that use with a
diagnostic that says what to change, instead of emulating it.

**Why this priority**: The initiative's stance is refuse-with-guidance over workarounds. The engineer must know what to
change.

**Independent Test**: Bind `ChannelName` to a non-empty value: the converter refuses with
`ELS3-ACTIVITY-REMOVED-INPUT-BOUND`. Bind `WorkflowDefinitionId` to an expression: it refuses with
`ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`. Each diagnostic's guidance names the change.

**Acceptance Scenarios**: each row of the refusal table in FR-012 has one scenario: the input gives the code and the
guidance.

### Edge Cases

- `Items` is null or empty: the loop completes at once with `Done`, as in Elsa 3.
- `Items` is not a collection, for example a string: Elsa 3 silently dispatched nothing. The same happened for a
  collection of value types, such as `[1, 2, 3]` held as `int[]`, because Elsa 3 only enumerates `IEnumerable<object>`.
  In Elsa 4, both `ForEach` and the parallel ForEach fault the activity on a value that is not a collection, and they
  iterate value-type collections. The converter refuses a literal non-collection it can see with
  `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`, as spec 1243 does. The documentation states the run-time difference.
- A child whose dispatch fails (`DispatchFailed`, for example after delivery retries): Elsa 3 faulted the activity. The
  body dispatch's `DispatchFailed` stays unconnected, so the pass ends. That is the rule the converter already applies
  to Elsa 3 `DispatchWorkflow`; see question Q3 on PR #5839.
- `CorrelationIdFunction` is absent or returns blank: Elsa 3 gave the child no correlation id, while Elsa 4 gives it the
  parent's. That is Elsa 4's existing `DispatchWorkflow` rule, and the converter already accepts the same difference
  for Elsa 3 `DispatchWorkflow` without a diagnostic. This spec follows that and adds none.
- `CorrelationIdFunction` in a language other than JavaScript or Literal, for example Liquid or C#, is handled by the
  converter's existing expression rules, the same as any other input.
- The child declares an input named `ParentInstanceId`: Elsa 4 does not fill it, because the parent link lives in the
  dispatch record. The converter warns.
- Elsa 3 reused one `Input` dictionary across items, so the members of one map item could leak into later children.
  That is an Elsa 3 defect. Elsa 4 builds the inputs for each item separately and the converter does not emulate the
  leak.
- Elsa 3 kept one bookmark per parent instance, so two waiting `BulkDispatchWorkflows` running at the same time in one
  parent shared one counter. Each Elsa 4 loop has its own passes, so this is corrected, not emulated.

## Requirements *(mandatory)*

### Functional Requirements

**Shape**

- **FR-001**: No new Elsa 4 activity is added. The Elsa 4 counterpart of `BulkDispatchWorkflows` is a loop with a
  `DispatchWorkflow` body (option B). The activity map entry changes from `NoCounterpart` to a lowering, done by a node
  rewrite that adds child nodes, as `Elsa3SwitchValueSynthesisRewrite` and `Elsa3HttpTriggerResponseModeRewrite`
  already do.
- **FR-002**: The converter reads `WaitForCompletion` the way Elsa 3 3.6.3 did:
  - **Missing**: the property key is absent from the JSON. The constructor default applies, which is true.
  - **Literal false**: `null`, a literal with no value, or a literal that parses as false. Elsa 3 reads a null input as
    `false` (`GetOrDefault`). The parse ignores case, so the corpus literal `"False"` counts.
  - **Literal true**: a literal that parses as true.
  - **Expression**: anything else.

  For a literal false, the converter MUST emit a `ForEach` whose `Collection` is the converted `Items` and whose body is
  one `DispatchWorkflow` with `WaitForCompletion` false.
- **FR-003**: When `WaitForCompletion` is missing, a literal true or an expression, the converter MUST emit spec 1243's
  parallel ForEach. Its `Collection` is the converted `Items`, and its body is a flowchart
  holding a `DispatchWorkflow` whose `WaitForCompletion` carries the Elsa 3 value.
  - The dispatch's `Completed` outcome connects to the converted `ChildCompleted` activity, when there is one.
  - Its `Faulted` outcome connects to the converted `ChildFaulted` activity, when there is one.
  - Until spec 1243 is implemented, this case stays `ELS3-ACTIVITY-NO-COUNTERPART`, with guidance that names spec 1243.
- **FR-004**: The emitted loop keeps the Elsa 3 node's id, name and position. Converted branch activities keep their
  Elsa 3 ids, so journal reads by activity id keep working.

**Members**

- **FR-005**: `WorkflowDefinitionId` follows the existing `DispatchWorkflow` rule: literal only and required, pinned at
  publish.
- **FR-006**: The body dispatch's `Inputs` MUST be built for each item.
  - Start from the static `Input` map.
    - A literal or Object-expression map is checked with the existing dispatch-input rules from #5808.
    - A JavaScript map is embedded in the generated expression, after the existing JavaScript rewrite.
    - A map in any other expression language is refused with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`.
  - When the target workflow is published by the same import, add:
    - for a map item, each declared input the item has, in the declared spelling;
    - for any other item, `DefaultItemInputKey` with the item, when the target declares that name.
  - The item wins over `Input` on a name clash, as in Elsa 3.
  - When the target is not in the import, the item is passed the Elsa 3 way (every member, or the item under the key),
    with the existing `ELS3-BEHAVIOUR-DISPATCH-INPUT-NAMES` warning.
  - `ParentInstanceId` is not passed.
- **FR-007**: `DefaultItemInputKey` MUST be a literal. Any other binding is refused with
  `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`.
- **FR-008**: `CorrelationIdFunction` MUST become the body dispatch's `CorrelationId`.
  - Each call of `get{DefaultItemInputKey}()` in it is rewritten to `variables.currentItem`, the current-item read of
    `ForEach` and of spec 1243's parallel ForEach.
  - The rest of the expression goes through the existing JavaScript rewrite.
  - When the function is absent, `CorrelationId` stays unbound, as for Elsa 3 `DispatchWorkflow`.
- **FR-009**: `StartNewTrace` and `ChannelName` are R3 "Removed", as for `DispatchWorkflow`. A non-default binding is
  refused with `ELS3-ACTIVITY-REMOVED-INPUT-BOUND`; for `ChannelName` the default is null or an empty literal.
  `CancelChildOnParentCancellation` is set to false, as for `DispatchWorkflow`, because Elsa 3 did not cancel the
  children.

**Behaviour diagnostics**

- **FR-010**: Spec 1243 schedules every pass at start with no cap, so the waiting path needs no behaviour warning for
  throughput. If spec 1243 adds a cap or a parallelism limit, a conversion through FR-003 raises a new warning
  `ELS3-BEHAVIOUR-BULK-DISPATCH-PARALLEL-LIMIT` that states it.
- **FR-011**: A conversion through FR-006 with a known target MUST raise a new warning,
  `ELS3-BEHAVIOUR-BULK-DISPATCH-ITEM-INPUTS`. It says that only the child's declared inputs are passed. When it applies,
  it also says that `ParentInstanceId` is no longer filled.

**Outcomes and refusals**

- **FR-012**: Outcomes and the uses with no faithful Elsa 4 meaning MUST map as follows.

| Elsa 3 use | Elsa 4 result |
|---|---|
| `Done` connection | the loop's `Done` connection |
| `Canceled` connection | dropped, with info `ELS3-BULK-DISPATCH-UNREACHABLE`: Elsa 3 never emits it |
| `Completed` connection, wait a literal false | dropped, with info `ELS3-BULK-DISPATCH-UNREACHABLE`: never emitted |
| `Completed` connection, any other wait, every target also reached from `Done` | dropped, with info `ELS3-BULK-DISPATCH-UNREACHABLE`: `Done` covers it. No duplicate connection is emitted |
| `Completed` connection, any other wait, a target that `Done` does not reach | refused with new error `ELS3-ACTIVITY-BULK-DISPATCH-COMPLETED-ROUTE`. Elsa 3 emitted `Completed` only when it waited and had items, and `Done` always. Guidance: connect the follow-up to `Done`; if it must not run for an empty collection or without waiting, test that in a decision |
| `ChildCompleted` or `ChildFaulted` branch, wait a literal false | dropped, with info `ELS3-BULK-DISPATCH-UNREACHABLE`: Elsa 3 never runs it |
| static `Input` in a language other than JavaScript, Literal or Object | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` (FR-006) |
| `DefaultItemInputKey` not a literal | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` (FR-007) |
| `Items` a literal that is not a collection | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` (Edge Cases) |
| A branch reading what Elsa 3 passed it: the `ChildInstanceId` variable, or the input `WorkflowOutput`, `WorkflowInstanceId`, `WorkflowStatus` or `WorkflowSubStatus` | refused with new error `ELS3-ACTIVITY-BULK-DISPATCH-CHILD-RESULT-READ`. Guidance: the branches get no child result in Elsa 4; remove the read; once spec 1245 (#5838) gives an awaited `DispatchWorkflow` an `Output`, the branch can read the body dispatch's `Output` |
| `WorkflowDefinitionId` not a literal | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` (FR-005) |
| `ChannelName` or `StartNewTrace` bound | `ELS3-ACTIVITY-REMOVED-INPUT-BOUND` (FR-009) |

- **FR-013**: Every new code MUST be added to `Elsa3ActivityDiagnosticCodes` and to the spec 236 diagnostic-code
  contract:
  - two errors: `ELS3-ACTIVITY-BULK-DISPATCH-COMPLETED-ROUTE` and `ELS3-ACTIVITY-BULK-DISPATCH-CHILD-RESULT-READ`;
  - one warning: `ELS3-BEHAVIOUR-BULK-DISPATCH-ITEM-INPUTS`, plus `…-PARALLEL-LIMIT` only under FR-010;
  - one info: `ELS3-BULK-DISPATCH-UNREACHABLE`.

  Each new error MUST carry guidance text, and each info names what was dropped.

**Documentation and evidence**

- **FR-014**: The converter's mapping README and the spec 236 member map MUST describe the lowering, the member rules and
  the refusals.
- **FR-015**: The `DispatchWorkflow` documentation MUST show the Elsa 4 pattern for one child per item, with and without
  waiting.
- **FR-016**: The JTest effect MUST be handed to S-15 rather than edited on this branch, because the ledger lives on the
  S-15 branch. The hand-over has three parts.
  - `performance-concurrency.json` converts as it is.
  - The composition root needs an adjusted copy, for coordinator approval: a literal `WorkflowDefinitionId` and a
    cleared `ChannelName`, the same precedent as `composition-dispatch-root-workflow.json`. It then converts once spec
    1243 is implemented.
  - In the wait dataset of `…-await-completion`, three expectations come from Elsa 3's HTTP-context switch:
    `WriteHttpResponse` runs after the resume with no HTTP context, so it suspends, the root stays
    `Running`/`Suspended`, and the caller gets 200. That is `WriteHttpResponse` behaviour, not bulk dispatch. The ledger
    has no disposition for it yet. S-15 classifies the dataset against Elsa 4's response-after-resume behaviour, with
    coordinator approval, as either an expectation change or an excluded dataset. This spec does not decide it.

### Key Entities

- **Lowered bulk dispatch**: the loop node (a `ForEach` or a parallel ForEach) that replaces the Elsa 3 node. It holds
  one `DispatchWorkflow` body, plus up to two branch activities wired from the dispatch's outcomes.
- **Per-item dispatch inputs**: the inputs that the body dispatch's `Inputs` computes for each pass. They come from the
  static `Input`, the current item and the target's declared inputs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `performance-concurrency.json` converts and publishes without adjustment. Its suite's 300 children all reach
  `Finished` on the Elsa 4 host.
- **SC-002**: Once spec 1243 is implemented, the approved adjusted copy of `composition-bulk-dispatch-root-workflow.json`
  converts and publishes. The correlation-id, input, channels and on-completed-callbacks suites pass, as does the
  no-wait dataset of await-completion. The wait dataset follows S-15's classification (FR-016).
- **SC-003**: Every Elsa 3 use in FR-012 gives exactly the listed result in converter tests. Each refusal carries
  guidance that names the change.
- **SC-004**: The Elsa 4 runtime, the activity catalog and Studio are unchanged. The work is limited to the converter,
  its tests and the documentation.

## Assumptions

- Spec 1243 provides a parallel ForEach as its session described it on 2026-11-26: input `Collection`, a single body
  slot whose passes can suspend, `Done` once every pass has completed, a faulted pass does not fault the loop, and the body
  reads `variables.currentItem`. If spec 1243 changes when it is approved, FR-003, FR-008 and FR-010 follow it.
- Each pass of a loop has its own activity execution, so `DispatchWorkflow`'s identity scheme yields one child per item
  without any change. This is true for `ForEach` today, and spec 1243 keeps it.
- A sequential `ForEach` of fire-and-forget dispatches starts all the children after the parent's commit. The children
  run at the same time, as in Elsa 3.
- No ADR is needed for option B. Option A would need one, for the multi-child dispatch identity.

## Out of Scope

- A dedicated multi-dispatch activity (option A), dispatch channels and trace options.
- Mapping the branches' reads of the child result to the body dispatch's `Output` from spec 1245 (#5838). That is a
  possible follow-up once spec 1245 is implemented; until then such reads are refused (FR-012).
- Elsa 4's `WriteHttpResponse` behaviour after a resume (await-completion's HTTP expectations).
- Editing the S-15 ledger or the adjusted JTest copies. S-15 owns them.
