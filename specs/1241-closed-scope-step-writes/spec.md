# Feature Specification: A step writes its reusable body's own variables

**Feature Branch**: `agent/claude/3052-closed-scope-session-writes`

**Created**: 2026-11-25

**Status**: Implemented (PR #5786)

**Input**: A step inside a reusable (graph) activity body changes that body's own variables through its variable
session, as a step in a workflow already can. Graph bodies get the same step-write checks at publish as workflow
drafts. The Elsa 3 converter then converts script writes in a reusable workflow instead of refusing them.

## Problem

An Elsa 3 reusable workflow `ApplyDiscount` (usable as an activity) declares a variable `Total` (`Int32`). Its body
runs one Run JavaScript step:

```js
setTotal(getTotal() * 0.9);
return getTotal();
```

Elsa 3 ran this on every call. Today:

- **Import.** The converter refuses the script with `ForbiddenCapability`: Elsa 4 cannot set a reusable workflow's
  variables from a script yet.
- **Native Elsa 4.** The same body authored in Elsa 4 (`variables.Total = variables.Total * 0.9`), or a C# activity in
  the body that sets a `VariableReference<int>` input bound to `Total`, faults the step on every run: a step session
  cannot write a closed scope's variables. Only an output capture writes them.
- **Publish.** The body publishes without a diagnostic, because graph body validation counts captures only. It does not
  check step writers, step-write overlaps or reference bindings, so the failure shows only at run time.

## Rule and boundary

This specification applies [ADR 0090](../../docs/adr/0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md)
(a step changes variables as a unit of work committed with the step) and
[ADR 0092](../../docs/adr/0092-variable-reads-and-writes-share-one-versioned-lifecycle.md) decision D-9: the commit rule
is the same for captures and session writes, and for frames and rows. It completes [spec 1231](../1231-step-variable-writes/spec.md)
for closed scopes and builds on the closed-scope rows of [spec 1221](../1221-variable-lifecycle-model/spec.md) and the
write ordering of [spec 266](../266-capture-concurrency/spec.md). It needs no ADR amendment.

A closed scope stays closed. A step in a body reaches the variables of its own nearest body only. It never reaches the
variables of the workflow or the body that called the reusable activity: values still flow in through inputs and back
through outputs.

Excluded: intrinsic writes to closed-scope rows; session writes of workflow outputs, correlation or instance name from a
body (spec 1234 keeps refusing them there); variables that require encryption or are stored externally (still refused
for every session); background placement (ADR 0093 C9 is unchanged).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A step changes its body's variable (Priority: P1)

An activity author builds a reusable activity whose body keeps a running value in a body variable and changes it from a
script or a C# step, as in a workflow.

**Why this priority**: It is the parity gap itself. Without it, the converter refusal cannot be lifted and native
authors must restructure every such body into captures.

**Independent Test**: Publish a reusable activity whose body holds `Total = 100` and runs a script
`variables.Total = variables.Total * 0.9; return variables.Total;`, then a step that reads `Total`. Run a workflow that
calls it and maps `Total` to an output.

**Acceptance Scenarios**:

1. **Given** the body above, **when** a workflow calls the activity, **then** the script returns `90`, the next body
   step reads `90`, and the activity's output is `90`.
2. **Given** a C# activity in the body with a `VariableReference<int>` input bound to `Total`, **when** it sets `Total`
   to `5`, **then** the next body step reads `5`.
3. **Given** an activity type marked `[AccessesUndeclaredVariables]` in the body, **when** it sets `Total` by name,
   **then** the body's `Total` changes.
4. **Given** the calling workflow also declares a variable `Total`, **when** the body's step writes `Total`, **then** only
   the body's `Total` changes; the caller's `Total` keeps its value.
5. **Given** the same reusable activity called twice in one workflow (two activations), **then** each activation's
   `Total` changes on its own.

---

### User Story 2 - Body writes follow the commit rule (Priority: P1)

An operator inspecting an instance sees body variable writes made by steps exactly as workflow variable writes: who wrote
them, from which version, and whether one overwrote another.

**Why this priority**: D-9 promises one rule. A row write that skipped versioning or history would make reusable
bodies the one place where lost updates go unseen.

**Independent Test**: In a body, run two parallel branches whose scripts both read and write `Total`, with lost-update
mode left at the default and again set to `Fault`.

**Acceptance Scenarios**:

1. **Given** a step write into a body variable, **then** the row's version advances, and the write history records it
   with source `SessionChange`, the writer node and invocation, the base version and the new version.
2. **Given** two branches that both read `Total` at the same version and then write it, **then** the last commit wins
   and records a lost-update warning; with `Fault`, the second step faults with a lost update unless the digest matches,
   as for a frame.
3. **Given** a step that writes `Total` and also captures an output into `Total`, **then** both commit with the step,
   the capture last, and the capture wins without a lost-update conflict between them.
4. **Given** a step that writes `Total` and then suspends, **then** the write commits with the suspension and the
   resumed evaluation reads it.
5. **Given** a step that writes `Total` and then faults or is cancelled, **then** `Total` is unchanged.
6. **Given** a step that writes the stored value back unchanged, **then** nothing is written and the version stays.

---

### User Story 3 - Graph bodies are checked at publish (Priority: P2)

An activity author publishing a reusable activity gets the same step-write warnings and refusals as when publishing a
workflow.

**Why this priority**: Once writes work, the publish checks catch the remaining mistakes before run time.

**Independent Test**: Validate graph drafts with each case below and compare with the same body as a workflow draft.

**Acceptance Scenarios**:

1. **Given** two unordered body nodes where one has a writable `VariableReference<T>` input bound to `Total` and the
   other captures into `Total`, **then** the graph's concurrent-write warning counts both writers.
2. **Given** a body node of a type marked `[AccessesUndeclaredVariables]`, **then** it counts as writing every body
   variable visible to it, and the warning says so.
3. **Given** a body node whose step writes `Total` and that also captures into `Total`, **then** the step-write overlap
   warning is raised.
4. **Given** a writable reference input bound to an undeclared variable, or to a variable of another type, **then**
   validation refuses the draft, as for a workflow draft.
5. **Given** a body script that writes a name the body does not declare, **then** validation refuses it with the same
   script-write diagnostic as in a workflow.

---

### User Story 4 - Elsa 3 reusable scripts that set variables convert (Priority: P2)

A team migrating from Elsa 3 imports reusable workflows whose scripts set their own variables, and they run as before.

**Why this priority**: It is why the gap was found; it depends on User Story 1.

**Independent Test**: Convert and run `ApplyDiscount` from the problem statement.

**Acceptance Scenarios**:

1. **Given** `ApplyDiscount`, **when** it is imported, **then** the script converts to
   `variables.Total = variables.Total * 0.9; return variables.Total;` with no refusal, and a call returns the
   discounted total.
2. **Given** a reusable script that changes a member of a variable (`getVariable('Order').Status = 'Paid'`) or passes it
   to a helper that may change it, **then** it converts as it does in a non-reusable workflow.
3. **Given** a reusable script that sets one of the reusable workflow's inputs (`setAmount(5)` where `Amount` is an
   input, which Elsa 3 held as a local variable), **then** import refuses it with guidance to declare a variable,
   initialize it from the input, and write that variable instead.

### Edge Cases

- A body nested in another body: a step writes its own nearest body's variables only, never the outer body's.
- A loop inside a body: a write to a body variable from an iteration lands in the body's row; a loop item or index
  stays read-only.
- A body variable never stored yet: it reads as null at version 0, and a write creates the row value at version 1.
- A body variable holding a sensitive value: the write keeps the destination's protection, and the history hides the
  value as it does for a frame.
- A write whose value does not match the variable's type, or a transient variable written by a step that can suspend:
  the step faults, as in a workflow.
- An operator retry or re-execution of a faulted body step runs the step again, and its writes meet the same rule. A
  refused completion that wrote body variables is not recorded for acceptance, as for a capture into a body variable
  today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A step session inside a reusable activity body MUST let a step write a variable declared by its nearest
  enclosing body, through every write surface a workflow step has: writable variable references, free names on a marked
  type, and script assignments.
- **FR-002**: A step session MUST NOT expose or write any variable outside its nearest enclosing body (the caller's
  workflow, an outer body, or another activation of the same reusable activity).
- **FR-003**: Body variable writes MUST commit with the step's checkpoint commit, before the step's captures, and save
  nothing when the step faults or is cancelled; a suspension commits them.
- **FR-004**: Each body variable write MUST be judged by the commit rule against the version the row carries, with the
  node's lost-update mode, and MUST store the resulting version, digest and writer on the row.
- **FR-005**: Each applied, overwriting or refused body variable write MUST leave a write-history record with source
  `SessionChange`, as a frame write does, readable through the existing variable-lifecycle inspection routes.
- **FR-006**: A step's own body variable write MUST advance its base for that variable, so its own later capture or
  write into the same variable does not conflict with it.
- **FR-007**: The writes of one step commit into the same body variable MUST be judged in order (session writes, then
  captures), each against the row as the previous one left it. A capture into a body variable the step also wrote
  through its session therefore wins without a conflict between the two, and two captures of one completion into the
  same body variable no longer both judge against the stored row.
- **FR-008**: Reads of a body variable within the step MUST return the step's own pending write (read-your-writes), and
  later body steps MUST read the committed value.
- **FR-009**: Graph body validation and compilation MUST count step writers (writable references, marked types and
  analyzed scripts) in the concurrent-write warning, raise the step-write overlap warning, and refuse reference bindings
  that are unbound, undeclared, not a variable, or of the wrong type, with the same diagnostics as workflow drafts.
- **FR-010**: The Elsa 3 converter MUST convert script writes to a reusable workflow's declared variables as it does in
  a non-reusable workflow.
- **FR-011**: The Elsa 3 converter MUST refuse a script write to a reusable workflow's input, with guidance to declare a
  variable initialized from the input and write that variable.
- **FR-012**: The runtime lifecycle and write-order documentation and their catalog entries MUST describe body variable
  writes as supported and drop the closed-scope "not yet" limitations this spec removes.
- **FR-013**: Studio MUST show the graph body's step-write warnings and refusals where it shows a graph draft's other
  validation results.

### Key Entities

- **Closed-scope variable row**: the stored value of one body variable for one activation of a reusable activity; it
  carries a version, digest, writer and protection.
- **Step write intent**: a pending whole-value write of one variable made by a step, with the base it was built on.
- **Write-history record**: one judged write (applied, overwrite with warning, refused), with its source, writer and
  versions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The `ApplyDiscount` example imports with zero refusals and returns the discounted total on every call.
- **SC-002**: Every reusable-script write case the converter tests refuse today only because the workflow is reusable
  now converts; each write to an input is refused with guidance.
- **SC-003**: For each acceptance scenario of User Story 2, a body variable produces the same write-history outcomes as
  the same scenario on a workflow variable.
- **SC-004**: A graph draft and a workflow draft with the same body give the same step-write warnings and refusals.
- **SC-005**: No step in a body changes a caller's variable in any test, including same-named variables.

## Assumptions

- Closed-scope rows already carry versions and write records for captures (spec 1221). Body variable writes reuse
  that, and inherit the row limitations the lifecycle documentation lists: no value snapshot for a row write, a row has
  no protection policy of its own, and a row record's commit order is the version it produced.
- A reusable activity's inputs are not body variables in Elsa 4, so a write to an input is refused rather than lowered
  into a hidden variable.
- Graph body validation reuses the step-write facts workflow validation derives from the activity catalog.
