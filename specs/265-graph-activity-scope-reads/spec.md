# Feature Specification: Graph activity body nodes read the activity's inputs, variables and results

**Feature Branch**: `agent/claude/3027-graph-scope-reads`

**Created**: 2026-10-21

**Status**: Draft

**Issue**: [#3027](https://github.com/elsa-workflows/elsa-foundation/issues/3027)

**Input**: A node inside a published graph activity body (`elsa.activity-graph`) cannot read the activity's own
input values, a graph variable or another body node's result, whatever binding it uses. An input read in the body
reads the *started workflow's* request, and a variable read reads the *started workflow's* variables. A read of a
body node's result is refused at workflow publish with `VF-COER-001`, because placement gives body nodes new ids but
leaves the references unchanged. Spec 246 refuses Elsa 3 reusables whose bodies read values
(`ELS3-ACTIVITY-REUSABLE-VALUE-READ-UNSUPPORTED`, decision D-8) until this lands.

**Depends on**:
- Spec 221 (merged): reusable activity definitions. FR-035 already says the nearest activity execution scope
  supplies read-only public inputs and graph-local variables, and that a graph activity breaks the caller's
  user-variable chain. The seam types it introduced (`IGraphActivityInputReader`, `GraphActivityScopeFactory.FindNearest`)
  exist, but only tests use them. This spec delivers that intent.
- Spec 237 (merged): graph boundary completion and output mappings at exit.
- Spec 246 (merged, #3031): publishes imported Elsa 3 reusables and adds the D-8 refusal that this spec lifts.

**Related**:
- [#3023](https://github.com/elsa-workflows/elsa-foundation/issues/3023): engine intrinsics (SetVariable,
  SetOutput, Correlate, SetName) in graph activities. #3023 covers intrinsic writes. This spec covers reads and
  captured body outputs (D-2), so #3023 builds on the graph scope defined here.
- ADR 0027 (scoped variable references include the declaring scope): a copied or imported subtree remaps its
  internal scoped references to the new declaring-scope node identities. Placing a template is such a copy.

## Decisions

| # | Decision | Why |
|---|----------|-----|
| D-1 | **A graph activity is a closed scope.** Inside a graph body, the started workflow's inputs and variables are not visible, and neither are an outer graph activity's. A body node reads only its activity's inputs, its variables and its body's results. A reference to anything else is refused at activity publication. An existing activity whose body relies on caller values is refused at its next publication. *(2026-10-21, owner)* | Spec 221 FR-035 intended this. A reusable that depends on its caller's names reads wrong data silently, as #3027 found. |
| D-2 | **Captured body outputs into graph variables are in scope.** A body node can capture an output into a graph variable, so a later body node or an output mapping can read it. All of spec 246 D-8 is lifted for bodies without intrinsics, including Elsa 3 memory-reference captures. Intrinsic writes (SetVariable, SetOutput) stay in #3023, which reuses this write path. *(2026-10-21, owner)* | Captures are output bindings, not intrinsics. Without them, most Elsa 3 reusables that read values would still be refused. |

## User Scenarios & Testing *(mandatory)*

The actors are activity designers who build graph activities in Studio or through the API, workflow authors who use
those activities, and operators who import Elsa 3 reusable workflows.

### User Story 1 - A body node computes from the activity's inputs (Priority: P1)

A designer builds "Add two numbers", with inputs `A` and `B` and output `Sum`. A body node computes `A + B` from the
activity's own inputs, and an output mapping returns the result as `Sum`. A workflow calls the activity with `A = 2`
and `B = 3` and receives `Sum = 5`. The started workflow has no inputs called `A` or `B`, and if it had, they would
not be read.

**Why this priority**: This is the core gap. Without it, a graph activity can only pass its inputs through output
mappings, and no body node can use them.

**Independent Test**: Publish the activity, call it from a workflow, and assert the caller's `Sum`. Run it again
from a workflow that has its own `A` input with a different value. The result is unchanged.

**Acceptance Scenarios**:

1. **Given** a graph activity whose body node binds an input to the activity's input `A`, **When** a caller passes
   `A = 2`, **Then** the body node receives `2`.
2. **Given** the same activity, **When** the started workflow has its own request input `A = 99`, **Then** the body
   node still receives the value the caller passed to the activity, not `99`.
3. **Given** a body node's JavaScript expression that reads the activity's input (as a named parameter or through
   the ambient accessor), **When** the activity runs, **Then** the expression sees the activity's input value.
4. **Given** an activity input that the caller did not pass and that has a default, **When** a body node reads it,
   **Then** it receives the default, as output mappings already do.

---

### User Story 2 - A body node reads a graph variable (Priority: P1)

A graph activity declares a variable `RunningTotal` with an initial value. A body node reads it, by binding or in
JavaScript, and gets the graph's value, not a workflow variable with the same name.

**Why this priority**: Graph variables exist today, but only boundary expressions can see them. Reading them is the
basis for #3023, where body nodes write them.

**Independent Test**: Publish an activity with a variable `RunningTotal = 10`, a body node that reads it and an
output mapping that returns the node's result. A caller that declares its own `RunningTotal = 0` receives the value
computed from 10.

**Acceptance Scenarios**:

1. **Given** a graph variable with an initial value, **When** a body node binds an input to that variable, **Then**
   the node receives the initial value.
2. **Given** a body JavaScript expression using `variables.RunningTotal` or `getVariable('RunningTotal')`, **When**
   it runs, **Then** it sees the graph's variable.
3. **Given** a caller workflow variable with the same name as a graph variable, **When** a body node reads it,
   **Then** the graph variable is read.
4. **Given** a container inside the body (for example a Sequence) that declares its own variable, **When** a node
   inside it reads that variable, **Then** the read resolves to the container's variable in this placement.

---

### User Story 3 - A body node reads another body node's result (Priority: P1)

Inside the body, node `compute` produces a result and the next node `format` reads it. The workflow that uses this
activity publishes without `VF-COER-001`, and at run time `format` receives `compute`'s result from the same
activity invocation.

**Why this priority**: A body that cannot chain results cannot do multi-step work.

**Independent Test**: Publish an activity whose body chains two nodes through an activity-result reference, place
it twice in one workflow and run it. The workflow publishes, and each invocation's `format` reads its own `compute`.

**Acceptance Scenarios**:

1. **Given** a body node that references another body node's result, **When** a workflow that uses the activity is
   published, **Then** it publishes without a missing-producer error.
2. **Given** the same activity placed twice in one workflow, or invoked repeatedly in a loop, **When** it runs,
   **Then** each invocation reads the result its own producer made.
3. **Given** a body reference to a node that is not in the body, **When** the activity is published, **Then**
   publication is refused with a diagnostic that names the missing node, and the workflow publish does not fail later.

---

### User Story 4 - The activity is a closed scope (Priority: P2)

Inside a graph body, the started workflow's inputs and variables are no longer visible. A body node reads only the
activity's inputs, its variables and its own body's results, so the activity behaves the same whatever workflow
calls it (D-1).

**Why this priority**: Spec 221 FR-035 intended this. Without it, a reusable activity silently depends on its
caller's names, as #3027 found in spec 246.

**Independent Test**: Publish an activity whose body reads a name the activity does not declare. Publication is
refused; it does not fall through to the caller.

**Acceptance Scenarios**:

1. **Given** a body node that reads an input or variable that the activity does not declare, **When** the activity
   is published, **Then** publication is refused with a diagnostic that names the reference.
2. **Given** a graph activity nested in another graph activity, **When** an inner body node reads an input, **Then**
   it reads the inner activity's input, never the outer activity's or the workflow's.
3. **Given** an outer graph body that passes one of its own inputs to an inner graph activity, **When** it runs,
   **Then** the inner activity receives the outer activity's value.

---

### User Story 5 - A body node captures an output into a graph variable (Priority: P2)

A body node `lookup` produces `Customer`, and the designer captures it into graph variable `CurrentCustomer`. A later
body node and an output mapping read `CurrentCustomer` and see the captured value (D-2).

**Why this priority**: Elsa 3 reusables usually pass values between activities through variables. Without captures,
Story 7 lifts only part of D-8.

**Independent Test**: Publish an activity whose body captures a node's output into a graph variable and returns it
through an output mapping. The caller receives the captured value, and a caller variable with the same name is
unchanged.

**Acceptance Scenarios**:

1. **Given** a body node output captured into a graph variable, **When** the node completes, **Then** later body
   nodes and output mappings read the captured value.
2. **Given** a caller workflow variable with the same name, **When** the capture runs, **Then** the caller's
   variable is unchanged.
3. **Given** a capture into a variable the activity does not declare, or of an incompatible type, **When** the
   activity is published, **Then** publication is refused with a diagnostic that names the capture.
4. **Given** a body that suspends after the capture, **When** it is resumed after a restart, **Then** the captured
   value is still there.

---

### User Story 6 - Designers can pick graph values in Studio (Priority: P2)

In Studio's graph activity editor, a body node's input picker offers the activity's inputs, its variables and the
results of earlier body nodes, and a body node's output capture offers the activity's variables. A picked value is
stored as a reference that publishes and runs as in Stories 1–3 and 5.

**Why this priority**: Without it, only API and import authors can use the feature. Today Studio offers graph
variables under a scope that no runtime frame has, and the option probably shows as unavailable.

**Independent Test**: In Studio, build "Add two numbers" by picking `A` and `B` in the body node, publish it and run
a caller. The caller receives `Sum`.

**Acceptance Scenarios**:

1. **Given** the graph editor, **When** a designer opens a body node's input picker, **Then** the activity's
   inputs and variables are listed and selectable.
2. **Given** a picked graph value, **When** the activity is saved, reopened and published, **Then** the reference
   is kept and resolves at run time.

---

### User Story 7 - Imported Elsa 3 reusables that read values convert and run (Priority: P2)

An Elsa 3 reusable workflow whose body uses `getInput("approve")`, `getVariable(...)`, `variables.x`, generated
`get{Name}()` accessors, an activity's output or a memory reference now converts, publishes and runs. It reads its
own inputs and values and captures into its own variables. `ELS3-ACTIVITY-REUSABLE-VALUE-READ-UNSUPPORTED` is no
longer raised.

**Why this priority**: It closes the spec 246 D-8 gap that started this issue.

**Independent Test**: Import the spec 246 fixtures `reusable-reads-input` and `reusable-reads-result`. Both convert,
publish and run, and a caller that passes `approve` gets the branch that value selects.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 reusable that reads `getInput("approve")`, **When** a caller passes `approve = true`, **Then**
   the body takes the approved branch, whatever the caller's own inputs are.
2. **Given** an Elsa 3 reusable whose body reads another activity's output, **When** it is imported, published and
   run, **Then** the reading activity receives that output.
3. **Given** an Elsa 3 reusable whose body captures an activity output into a variable through a memory reference,
   **When** it is imported, published and run, **Then** later activities read the captured value.
4. **Given** an Elsa 3 reusable whose body uses an intrinsic (#3023), **When** it is converted, **Then** it is still
   refused with `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED`.

### Edge Cases

- A body node reads a graph input that is declared optional, has no default and was not passed: it receives no
  value (`null`), and does not read the caller's input.
- Resume and retry: a body suspended on a bookmark or retried after a failure reads the same input and variable
  values after the instance is rehydrated.
- A body node's own input is named the same as an activity input: the binding target, not the name, decides what
  is read.
- An outcome mapping or output mapping and a body node read the same input: both see the same value.
- A Studio-designed graph activity published before this change, whose body read the started workflow's inputs or
  variables: its published versions keep running as compiled. Its next publication is refused with the FR-006
  diagnostic, which names the reference to rebind (D-1).
- The same activity is placed twice in one workflow, in a loop, or nested in itself through another activity: each
  placement reads its own values, and references are resolved per placement.
- Secrets: a graph input bound from a secret in the caller stays withheld in the body, as today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A body node MUST be able to bind an input to one of its graph activity's declared inputs and receive
  the value the caller passed, or the input's default when no value was passed.
- **FR-002**: A body node MUST be able to bind an input to one of its graph activity's declared variables and
  receive that variable's current value in this invocation.
- **FR-003**: Body JavaScript expressions MUST see the activity's inputs and variables through the same parameter
  and ambient accessors that a workflow node uses for its workflow's inputs and variables.
- **FR-004**: A body node MUST be able to reference another body node's result. Each such reference MUST resolve to
  the producer in the same activity invocation.
- **FR-005**: When a template is placed into an executable, every reference inside the body (to a producer node, to a
  body container's variables or to the graph's own scope) MUST be remapped to the identity of that placement.
  Placing one template several times MUST give independent references.
- **FR-006**: Graph activity publication MUST refuse a body reference to an input, variable or producer node that
  the activity does not declare or contain. The diagnostic MUST name the reference and the node that holds it. A
  workflow publish MUST NOT be the first place such a reference fails.
- **FR-007**: Inside a graph body, input and variable reads MUST resolve to the nearest enclosing graph activity.
  They MUST NOT fall through to the started workflow or to an outer graph activity (D-1).
- **FR-008**: Values read in the body MUST be the activity's durable values, so suspension, resume, retry and
  rehydration give the same results.
- **FR-009**: Studio's graph activity editor MUST offer the activity's inputs and its variables in body-node input
  pickers, and MUST store references that satisfy FR-001 to FR-006. Pickers for earlier body results are a follow-up
  (plan A-7).
- **FR-010**: The Elsa 3 converter MUST stop refusing reusable bodies for the reads this spec supports, and MUST
  convert them to graph-scoped references. Reads it still cannot carry MUST stay refused with a diagnostic and
  guidance.
- **FR-011**: Workflow nodes outside any graph activity MUST resolve inputs, variables and results exactly as they do
  today.
- **FR-012**: A body node MUST be able to capture an output into one of its graph activity's declared variables. The
  captured value MUST be durable and visible to later body nodes and to output mappings in the same invocation, and
  MUST NOT change a caller variable (D-2). Graph activity publication MUST refuse a capture into an undeclared or
  type-incompatible variable.
- **FR-013**: Activity publication MUST refuse a body node's capture into a variable of the started workflow or of
  an outer graph activity (D-1).

### Key Entities

- **Graph activity invocation scope**: one run of a graph activity. It owns the input values the caller passed, the
  graph variables and the body's results. Its identity is the placement plus the outer execution.
- **Graph-scoped reference**: a body node's binding to a graph input, a graph variable or a body producer, stated in
  terms of the template and remapped to the placement at compile time.
- **Placement**: one copy of a template inside a compiled workflow executable, with its own node identities.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: "Add two numbers", with `Sum` computed by a body node and not by an output mapping, returns `5` for
  `2 + 3`. It is built through the API and through Studio, and called from a workflow that has its own `A` and `B`.
- **SC-002**: A body that chains two nodes through a result reference publishes, and returns correct, independent
  results when placed twice in one workflow and when nested one graph activity deep.
- **SC-003**: Every body reference to an undeclared input or variable, or to a producer that is not in the body, is
  refused at activity publication. None reaches workflow publish or run time in the test suite.
- **SC-004**: The spec 246 fixtures `reusable-reads-input` and `reusable-reads-result`, and a memory-reference
  capture fixture, import, publish and run to the expected result. The D-8 refusal tests now assert conversion.
  *(Implementation note: met for input reads, which import, publish and run, including against a caller's own input.
  Running result reads and captures from an imported body is blocked by two existing limits of every converted
  RunJavaScript; they are converted and proven at run time in the Behavioral and placement tests instead. See plan
  A-8.)*
- **SC-005**: Existing workflow, graph boundary, placement and Elsa 3 converter tests pass, except the D-8 refusal
  tests that SC-004 replaces.
- **SC-006**: A body suspended mid-run and resumed after a restart reads the same input and variable values as
  before the suspension.

## Assumptions

- Body nodes setting graph variables or outputs through intrinsics belongs to #3023. #3023 reuses the scope and
  the capture write path that this spec defines.
- Graph inputs stay read-only for body nodes (spec 221 FR-035).
- Output mappings and input defaults keep working as they do. A body node's read uses the same durable values.
- The authored body keeps template-local node ids. Placement does the remapping, as for structure today (ADR 0027,
  line 33).
- The Elsa 3 converter's output-mapping syntax (`Variable`, which the graph runtime rejects today) belongs to #3023,
  since only SetOutput produces it.
- No Elsa 3 parity work beyond the D-8 reads is in scope.

