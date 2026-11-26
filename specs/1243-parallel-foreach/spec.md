# Feature Specification: ParallelForEach runs one body pass per item at the same time

**Feature Branch**: `agent/claude/3052-spec1243-parallel-foreach`

**Created**: 2026-11-26

**Status**: Draft

**Input**: Elsa 3's `ParallelForEach` has no Elsa 4 counterpart, so the Elsa 3 converter refuses every definition that
uses it (#5832, part of initiative #3052). Elsa 4 gets a `ParallelForEach` activity, the converter maps Elsa 3's onto
it, and the Elsa 3 behaviours that Elsa 4 does not keep are refused or reported with guidance.

## Problem

An Elsa 3 workflow reads a list from the request body and sends one notification per entry:

```text
ParallelForEach  Items = getRequestBody().recipients        // ["acme-a", "acme-b", "acme-c"]
  Body: Sequence
          SendHttpRequest  Url = "https://acme.example/notify/" + getVariable("CurrentValue")
          WriteLine        Text = "notified " + getVariable("CurrentValue")
```

Elsa 3 starts all three bodies at once. While one body waits for its HTTP response, the others carry on, and the loop
completes when all three have completed. Today:

- **Import.** The converter refuses the definition with `ELS3-ACTIVITY-NO-COUNTERPART` ("Elsa 4 has no parallel
  for-each"). Nothing publishes, not even the activities around the loop.
- **Native Elsa 4.** An author can only pick `ForEach`, which runs the passes one after another, or `Parallel`, whose
  branches are fixed at design time. Neither runs one pass per item at the same time.
- **Evidence.** In the S-15 JTest corpus (branch `agent/claude/3052-s15-jtest-impl`),
  `workflow-definitions/single-outcome-activities.json` is refused for this reason alone (`ledger/dispositions.md`),
  which blocks `tests/single-outcome/single-outcome-tests.json`. On Elsa 3.6.3, that suite checks three things: the
  loop's status is `Completed`, its captured `Items` input holds both items, and the body `WriteLine` completed twice.

## What Elsa 3 does

From elsa-core 3.6.3 (`ParallelForEachT.cs`, `ItemSourceActivityExecutionContextExtensions.cs`, and the scheduler and
break behaviour); see [research.md](research.md) R-1 to R-4.

| Aspect | Elsa 3.6.3 |
|---|---|
| Members | `Items` input (object) and a `Body` port. No outputs. Unlike `ForEach`, there is no `CurrentValue` output. |
| Per pass | Each pass schedules the body with two variables of its own, `CurrentValue` (the item) and `CurrentIndex` (zero-based). The body reads them by name, for example `getVariable("CurrentValue")`. |
| Concurrency | Every pass is scheduled up front, in item order, on the instance's single FIFO queue. The passes interleave step by step on one thread. A pass that suspends, for example on a bookmark or a delay, lets the others go on. Nothing runs on parallel threads. |
| Completion | The loop completes once every pass has completed. With no items, it completes at once. |
| Null and odd items | Null `Items`: completes at once. A string, or any value that is not an `IEnumerable<object>` (a single object, for example): zero passes, completes at once, no error. |
| No body | With items but no body, nothing is scheduled. The loop never completes, which is a defect. |
| Faults | A faulted pass does not stop the others. The loop does not complete, and the incident strategy decides what happens to the instance. |
| Break | `ParallelForEach` does not handle `Break`. A `Break` in a pass passes through it and breaks the nearest enclosing `For`, `ForEach` or `While`. With no such loop, nothing happens. |
| Shared variables | Passes read and write the same workflow variables live and in memory. With no suspension, `count = count + 1` in every pass gives N. |
| Limits | No maximum degree of parallelism. |

## Rule and boundary

The new activity applies existing decisions and needs **no ADR and no ADR amendment**:

- [ADR 0028](../../docs/adr/0028-loop-body-runs-in-a-per-iteration-variable-scope.md): each pass runs in its own
  iteration frame, isolated by frame instance. Concurrent passes are what that isolation is for. The ADR's contract
  names the loop activities of its time, and a new loop follows the same contract.
- [ADR 0092](../../docs/adr/0092-variable-reads-and-writes-share-one-versioned-lifecycle.md) and
  [spec 266](../266-capture-concurrency/spec.md): passes are unordered with each other. A body write to a variable
  outside the loop is therefore an unordered write. It gets the `ValueFlow/ConcurrentWrite` warning at publish and,
  when the base it read is stale, the lost-update warning at run time, with last commit wins. None of this changes.
- [ADR 0090](../../docs/adr/0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md) and
  specs [1221](../1221-variable-lifecycle-model/spec.md), [1231](../1231-step-variable-writes/spec.md) and
  [1241](../1241-closed-scope-step-writes/spec.md): step writes, session writes and closed-scope writes in a body work
  as in any other body. The loop item and index stay read-only.
- [ADR 0093](../../docs/adr/0093-background-execution-eligibility-is-one-set-of-criteria.md): the loop is structural,
  so it is never a background candidate (C1). Its leaf body steps keep their own eligibility.
- The runtime's fault behaviour (`docs/runtime-fault-behavior.md`) and the rejoin of a retried faulted child (#3135)
  set the fault rule. Like `Sequence`, the loop keeps waiting for a faulted pass and accepts its retry. The instance's
  fault strategy, not the loop, decides what a fault does to the instance, as Elsa 3's incident strategy did.

**Excluded:**

- **Join semantics.** Wait-any, thresholds, and cancelling the other passes when one finishes all belong to the joins
  initiative #4193. This loop always waits for all passes.
- **A maximum degree of parallelism**, batching, or any other runtime knob for authors.
- **Running passes on parallel threads.** The instance queue stays single-threaded, as in Elsa 3.
- **BulkDispatchWorkflows**: spec 1242 (#5833) may map onto this activity. That mapping is decided there.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run one body pass per item at the same time (Priority: P1)

A workflow author has a list and wants the same steps done for every entry without waiting for one entry to finish
before the next starts. For example, they call a slow service once per entry.

**Why this priority**: This is the parity gap and the reason the activity exists. Without it, nothing else in this spec
has anything to act on.

**Independent Test**: Publish a workflow whose `ParallelForEach` runs over `["acme-a", "acme-b", "acme-c"]` with a body
of `Delay` followed by `WriteLine(currentItem)`. Run it. All three passes start before any delay ends. Three lines are
written, and the loop completes with `Done` once the last pass completes.

**Acceptance Scenarios**:

1. **Given** a `ParallelForEach` over three items whose body suspends on a bookmark, **When** the workflow runs,
   **Then** all three passes are started and suspended before any of them resumes. Each resumes on its own bookmark,
   and the loop completes with `Done` only after the third pass completes.
2. **Given** a body that reads `currentItem` and `currentIndex`, **When** the passes run, **Then** each pass reads its
   own item and index (ADR 0028), never another pass's.
3. **Given** `ExposeIndex = false`, **When** the workflow is published, **Then** a body read of `currentIndex` is
   refused at publish, as for `ForEach`.
4. **Given** an empty or null collection, **When** the loop runs, **Then** it completes with `Done` at once and the body
   never runs.
5. **Given** a collection that is a string, an object or a number, **When** the loop runs, **Then** it faults with the
   same message `ForEach` gives for that value.
6. **Given** a loop with no body, **When** it runs, **Then** it completes with `Done` at once.
7. **Given** a body that declares its own variables (a `Sequence` or `Flowchart` with variables), **When** passes run
   at the same time, **Then** each pass has its own copy of those variables.

---

### User Story 2 - A faulted pass can be retried on its own (Priority: P1)

An operator sees one pass fault, for example because one service call fails. They want the fault reported on that
pass, the other passes to finish, and a way to retry only the pass that failed.

**Why this priority**: A loop that hangs or hides a fault is worse than the refusal it replaces.

**Independent Test**: Run a loop over three items whose body faults for `acme-b`, under an instance fault strategy
that keeps the instance running. The `acme-b` pass has its own blocking incident, and the `acme-a` and `acme-c`
passes run to completion. The loop stays running. Retry the faulted pass after removing the cause. Its successor
completes, and the loop completes with `Done`. Under the default strategy (`Fault`), the instance faults as for a
fault anywhere else.

**Acceptance Scenarios**:

1. **Given** one faulted pass and two running, **When** the fault is recorded, **Then** the other passes are not
   cancelled and run on, and the loop stays running. It neither faults nor completes.
2. **Given** a faulted pass that is retried and then completes, **When** every pass has completed, **Then** the loop
   completes with `Done`, and the retried pass counts once, for its own item.
3. **Given** a faulted pass that is never retried, **When** nothing else can run, **Then** the instance ends as the
   runtime's fault behaviour decides for an undecided blocking incident. The loop never hangs silently.

---

### User Story 3 - Shared variable writes are visible at publish (Priority: P1)

An author writes a workflow variable from the body, for example a running total, and needs to know that passes do not
run in a fixed order.

**Why this priority**: Concurrency without a diagnostic is where lost updates come from (ADR 0092, item 1).

**Independent Test**: Publish a loop whose body captures `total + item` back into the workflow variable `total`. The
publish succeeds with one `ValueFlow/ConcurrentWrite` warning. The warning names the body node as unordered with
itself, says that the last commit wins, and names the remedies spec 266 lists.

**Acceptance Scenarios**:

1. **Given** a body step that writes a variable declared outside the loop, **When** the workflow is validated,
   **Then** a `ValueFlow/ConcurrentWrite` warning names that node once, as a writer that is unordered with itself.
2. **Given** a body that writes only variables declared inside the body, **When** it is validated, **Then** there is no
   warning.
3. **Given** two inline passes that each read `count` and write `count + 1` with no suspension in between, **When**
   they run, **Then** `count` ends 2 higher (ADR 0092 decision 10). **Given** the same with a suspension between the
   read and the write, **Then** the result follows ADR 0092: last commit wins and a lost-update warning is raised.
4. **Given** a body that assigns `currentItem` or `currentIndex`, **When** it is published, **Then** it is refused, as
   for `ForEach` (spec 1231 FR-006).

---

### User Story 4 - Elsa 3 ParallelForEach converts (Priority: P1)

A team migrating from Elsa 3 imports definitions that use `ParallelForEach` and gets a working Elsa 4 definition, or a
precise reason why not.

**Why this priority**: It lifts the refusal behind #5832 and unblocks the JTest `single-outcome` suite.

**Independent Test**: Convert `single-outcome-activities.json` from the S-15 corpus. `ELS3-ACTIVITY-NO-COUNTERPART`
for ParallelForEach is gone, and the loop is an Elsa 4 `ParallelForEach` with `Collection` bound to the converted
`Items` expression and the body in `ParallelForEach.Body`.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 `ParallelForEach` with `Items` and a `Body`, **When** it is converted, **Then** it becomes
   `ParallelForEach` with `Items` → `Collection`, `Body` → the `ParallelForEach.Body` slot, and `ExposeIndex` left at
   its default (`true`).
2. **Given** a body script that reads `getVariable("CurrentValue")` or `getVariable("CurrentIndex")`, and no variable of
   that name in a nearer scope, **When** it is converted, **Then** the read becomes `variables.currentItem` or
   `variables.currentIndex`, pinned to the loop node. The same holds for the other read forms the converter already
   accepts for a variable read by name (spec 1222).
3. **Given** a body script that writes `CurrentValue` or `CurrentIndex`, for example
   `setVariable("CurrentValue", x)`, **When** it is converted, **Then** it is refused with
   `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`. The guidance says that the loop item and index are read-only in Elsa 4 and
   suggests a body variable instead.
4. **Given** a `Break` inside a `ParallelForEach` body, **When** it is converted, **Then** the definition converts with
   `ELS3-BEHAVIOUR-BREAK-STOPS-AT-PARALLEL`: in Elsa 4 a `Break` ends its own pass only, while Elsa 3 broke the
   enclosing loop.
5. **Given** an Elsa 3 `ParallelForEach` with items but no body, **When** it is converted, **Then** it converts with a
   behaviour warning: Elsa 3 never completed this loop, while Elsa 4 completes it with `Done`.
6. **Given** an `Items` literal that is a string or a JSON object, **When** it is converted, **Then** it is refused with
   `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`: Elsa 3 ran zero passes, Elsa 4 faults. An `Items` expression converts
   without a warning. The run-time difference is documented with the converter's behaviour notes.
7. **Given** the converted definition, **When** it is published and run with the JTest request body, **Then** the
   loop completes, its captured `Collection` input holds both items, and the body `WriteLine` completes twice.

---

### User Story 5 - Studio authors and inspects the loop (Priority: P2)

An author builds the loop in Studio and an operator follows its passes in a run.

**Why this priority**: The runtime and converter make the activity usable. Studio makes it usable by authors.
Studio work ships in the same PR, because Studio lives in this repository.

**Independent Test**: In Studio, add `ParallelForEach` from the toolbox under Looping, drop a body into it, and complete
`variables.cur` in a body script to `currentItem`. Publish, run, and open the instance: each pass appears under the
loop with its own item and index.

**Acceptance Scenarios**:

1. **Given** the toolbox, **When** an author searches "parallel", **Then** `ParallelForEach` is offered, with a body
   slot and the same `Collection` and `ExposeIndex` inputs as `ForEach`.
2. **Given** a body expression, **When** the author types `variables.`, **Then** `currentItem` and `currentIndex` are
   offered, as in a `ForEach` body.
3. **Given** a run, **When** the operator opens the loop, **Then** each pass shows its item, index and status, and a
   faulted pass can be retried from there, as a faulted child of a `Sequence` can.

### Edge Cases

- **One item.** One pass. The loop completes with `Done` after it.
- **Large collections.** Every pass is scheduled at start. The collection is read once, indexed per pass, and never
  materialised per completion. Counting passes reads this loop's children only, as `Parallel` does (#413), so the
  cost grows linearly with the number of items.
- **Collection changes after start.** The collection is evaluated once, at start. A later write to the variable it
  came from does not add or remove passes.
- **Nested loops.** A `ParallelForEach` inside a `ParallelForEach` (or a `ForEach`) gets its own iteration frame per
  pass. An inner pass reads the inner item. It reaches the outer item through the outer loop's node, as for nested
  `ForEach`.
- **Break.** A body that completes with the `Break` outcome ends that pass only. The pass counts as completed. Other
  passes are not cancelled and the loop does not complete early (see *Excluded*: early completion is #4193).
- **Background body steps.** A leaf step in the body may still run in the background (ADR 0093). Its writes stay
  limited by C9.
- **Instance cancelled.** Cancelling the instance cancels every running pass, as it cancels `Parallel` branches. A pass
  cancelled on its own by an operator alteration counts as settled. The loop does not wait for it, because nothing
  would ever finish it, and completes with `Done` once every other pass has completed.
- **Restart mid-run.** After a restart, the passes resume from their durable state. Completion is derived from the
  durable pass states, not from an in-memory counter, so a pass completed before the restart is never counted again.
- **An Elsa 3 variable that shadows.** If a scope nearer than the loop declares `CurrentValue`, the converter leaves the
  read with that variable, nearest scope first.

## Requirements *(mandatory)*

### Functional Requirements

**Activity**

- **FR-001**: Elsa 4 MUST provide a `ParallelForEach` activity in the control-flow activities, with the inputs
  `Collection` and `ExposeIndex` (default `true`) evaluated as in `ForEach`, one optional body in the child slot
  `ParallelForEach.Body`, no outputs, and the single outcome `Done`.
- **FR-002**: On start, the activity MUST schedule one pass of the body per item, all at once, in item order. Each pass
  MUST get its own iteration identity and its own iteration frame holding `currentItem` and, when `ExposeIndex` is
  `true`, `currentIndex` (ADR 0028).
- **FR-003**: Passes MUST run interleaved on the instance's queue. A pass that suspends MUST NOT hold back the others.
- **FR-004**: The activity MUST complete with `Done` once every pass has completed, or was cancelled on its own (see
  Edge Cases). Completion MUST be derived from the
  durable state of its passes, counted per pass and not per node. A retried pass's successor MUST count for the same
  item, and a superseded attempt MUST NOT count.
- **FR-005**: A null or empty collection, or a missing body, MUST complete the activity with `Done` without scheduling
  any pass. A collection that is not an array, or is a string, MUST fault the activity with the `ForEach` messages.
- **FR-006**: A faulted pass MUST NOT fault or complete the activity and MUST NOT cancel the other passes. The
  activity MUST keep waiting and MUST accept a retry of the faulted pass (#3135), whose completion then counts for
  that pass's item. What a fault does to the instance stays with the instance's fault strategy.
- **FR-007**: A pass whose body completes with the `Break` outcome MUST count as completed. It MUST NOT end the loop
  or cancel other passes.
- **FR-008**: The activity MUST read its collection once per run. It MUST NOT materialise the whole collection, or
  list the workflow's activity states, on every pass completion.
- **FR-009**: The activity MUST NOT offer a maximum degree of parallelism, a join mode or any other concurrency setting
  to authors.

**Publish-time checks**

- **FR-010**: The structure handler MUST report the body as unordered with itself, so that a body write to a variable
  outside the loop raises the `ValueFlow/ConcurrentWrite` warning of spec 266, naming the node once.
- **FR-011**: Body reads of `currentItem` and `currentIndex`, the `ExposeIndex` switch, and the refusal of writes to
  them MUST behave as for `ForEach` (spec 1222 FR-004a, spec 1231 FR-006).

**Converter**

- **FR-012**: The Elsa 3 activity map MUST map `Elsa.ParallelForEach` to `ParallelForEach`, with `Items` → `Collection`
  (a null `Items` emits nothing, because both engines complete at once) and `Body` → the `ParallelForEach.Body` slot.
  `ELS3-ACTIVITY-NO-COUNTERPART` MUST no longer be raised for it.
- **FR-013**: A body read of the Elsa 3 per-pass variable `CurrentValue` or `CurrentIndex` by name MUST convert to
  `variables.currentItem` or `variables.currentIndex`, pinned to the loop node, when the loop is the nearest scope
  that declares that name. A computed name (`getVariable(expr)`) keeps the run-time lookup of spec 1222 FR-027 and
  MUST resolve the Elsa 3 names at run time too, or be refused if it cannot.
- **FR-014**: A body write to `CurrentValue` or `CurrentIndex` MUST be refused with
  `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT` and guidance to use a body variable.
- **FR-015**: `ELS3-BEHAVIOUR-BREAK-STOPS-AT-PARALLEL` MUST also be raised for a `Break` with a `ParallelForEach`
  ancestor.
- **FR-016**: A `ParallelForEach` with no body MUST convert with an `ELS3-BEHAVIOUR-*` warning that Elsa 3 never
  completed it and Elsa 4 completes it with `Done`.
- **FR-017**: An `Items` literal that is a string, an object or a number MUST be refused with
  `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`. The guidance says Elsa 3 ran zero passes and Elsa 4 faults.

**Studio**

- **FR-018**: Studio MUST offer `ParallelForEach` in the toolbox under Looping, edit its body slot as for `ForEach`,
  complete the loop variables in body expressions, and treat the loop as a loop wherever Studio lists loop structure
  kinds (block promotion, for example).
- **FR-019**: Instance inspection MUST show each pass under the loop with its item, index and status, and MUST allow
  retrying a faulted pass.

**Documentation and evidence**

- **FR-020**: The activity's README MUST state the execution model (interleaved, not threaded), the fault and Break
  rules, the concurrent-write warning, and the exclusions, and MUST link #4193 for join behaviour.
- **FR-021**: The activity contract surface baseline and the Elsa 3 parity evidence MUST list the new activity. The
  #1152 row for ParallelForEach MUST be closed by the implementing PR.

### Key Entities

- **ParallelForEach**: a loop activity. Its inputs are `Collection` and `ExposeIndex`, it has one body slot, and its
  outcome is `Done`.
- **Pass**: one body execution for one item, with its own iteration identity and iteration frame. Its status is one of
  running, suspended, completed, faulted or cancelled.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Converting `single-outcome-activities.json` raises no `ELS3-ACTIVITY-NO-COUNTERPART`. The converter
  test corpus has no ParallelForEach refusal left, other than the refusals FR-014 and FR-017 define.
- **SC-002**: On Elsa 4, `single-outcome/single-outcome-tests.json` passes its ParallelForEach steps: the loop completes,
  its captured collection holds both items, and the body completes twice. Other blockers recorded for that suite,
  such as value capture (#5647), are tracked against their own issues.
- **SC-003**: With a body that waits 1 second per item, a 10-item loop completes in under 3 seconds of wall time, where
  a `ForEach` takes at least 10.
- **SC-004**: A 1,000-item loop with an inline body completes, and the time spent evaluating pass completions grows
  linearly with the item count (1,000 items take at most about 10 times as long as 100 items).
- **SC-005**: Every publish of a body that writes an outer variable shows exactly one concurrent-write warning per
  variable. A body that writes only its own variables shows none.
- **SC-006**: In a run with one faulted pass, every other pass completes. After the faulted pass is retried
  successfully, the loop completes with `Done`. Under the default fault strategy, the instance faults as it does for
  any other activity fault.

## Assumptions

- The runtime already holds several in-flight executions of one node under one parent, told apart by execution id and
  iteration id. Nothing deduplicates them by node ([research.md](research.md) R-5).
- `Parallel`'s single-threaded scheduling and `Sequence`'s acceptance of a retried faulted child are the reference
  behaviour. This spec does not change them.
- The JTest suite's other assertions on the same definition (for example `WriteLine` with a null text, and value
  capture) are out of scope here. They are tracked by S-15 and its issues.
- Spec 1242 may express `BulkDispatchWorkflows` with wait-for-completion as a `ParallelForEach` whose body is an awaited
  `DispatchWorkflow`. Nothing in this activity is specific to that use.

## Open questions for the coordinator

1. **A separate activity, or a switch on `ForEach`?** Recommendation: a separate `ParallelForEach`. Running passes at
   the same time changes what the body may safely do (concurrent-write warnings, Break), so it should be visible on
   the canvas rather than hidden in a setting. It also keeps the Elsa 3 name, which makes the conversion one to one.
   A switch on `ForEach` would be the kind of runtime knob authors should not get.
2. **Fault rule: wait like `Sequence`, or fault like `Parallel`?** Recommendation: wait (FR-006). It is what Elsa 3 did
   (the other passes went on and the loop did not complete). It also keeps a retry of the single faulted pass
   possible, because the runtime refuses a retry once the parent has stopped running. The instance's fault strategy
   still decides the instance's fate. Faulting like `Parallel` would end the loop on the first fault and leave no
   way to retry the pass.
3. **Elsa 3's silent zero passes for a non-array `Items` expression.** Recommendation: convert without a warning and
   document the difference (FR-017 refuses only literals). A warning on every `Items` expression would fire on almost
   every definition and teach people to ignore it. In practice, the expression returns a list.
