# Feature Specification: Elsa 3 Execute Workflow converts to an awaited Dispatch Workflow with the child's output

**Feature Branch**: `agent/claude/3052-spec1245-execute-workflow`

**Created**: 2026-11-26

**Status**: Draft

**Input**: Issue #5838 (part of initiative #3052, a row of #1152). On 2026-11-26 the coordinator chose an awaited
Dispatch Workflow that returns the child's output, rather than inlining the child as a reusable graph activity. Elsa 3's
Execute Workflow ran a separate child instance and waited for its output, so the dispatch keeps that identity and
lifecycle.

## Problem

An Elsa 3 workflow `acme.order-intake` checks credit, then an address, through two published child workflows.

1. **Execute Workflow** runs `acme.credit-check`, which declares the output `Decision`. It captures `Result` into the
   variable `Credit`, and a script reads `getCredit().Output.Decision`.
2. **Dispatch Workflow** with `WaitForCompletion` true runs `acme.address-check`, which declares the output
   `Normalized`. It captures `Result` into `Address`, and a script reads `getAddress().Normalized`.

Elsa 3 hands each child's output back to the caller. What `Result` holds depends on the activity:

| Elsa 3 activity | What `Result` holds |
|---|---|
| Execute Workflow | An `ExecuteWorkflowResult`: `WorkflowInstanceId`, `Status`, `SubStatus`, and `Output`, the child's output dictionary. If the child suspended and the parent waited, it holds the bare output dictionary instead. |
| Dispatch Workflow, awaited | The child's output dictionary (output name → value). |
| Dispatch Workflow, not awaited | Never set. |

In Elsa 3, a child that faults or is cancelled still counts as finished. The parent continues on `Done` with whatever
output the child wrote.

The converter maps both activities to an Elsa 4 Dispatch Workflow that waits, but it carries no `Result` over. Both
captures are refused with `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE`, so `acme.order-intake` cannot be imported. The
reason is that Elsa 4's `Result` has another shape: `childWorkflowExecutionId`, `status`, an `outputs` list of
`{name, value, declaredType, isRedacted}` entries, and `diagnosticMetadata`. A converted script would read `undefined`.

The S-15 JTest corpus (branch `agent/claude/3052-s15-jtest-impl`, ledger `extensions/Elsa3/tests/JTest/ledger/`) shows
the same gap:

- `composition-execute-workflow-root.json` is refused. Its suite `composition/execute-workflow-tests.json` asserts
  `body.Output.CustomOutput.property1`, which the root reads from the captured Execute Workflow result.
- `composition-dispatch-root-workflow.json` converts only as an adjusted copy without the `result` capture. Its suites
  `dispatch-workflow-awaitCompletion` and `dispatch-workflow-handling-input-output` assert the awaited output
  (`activityState.Value.CustomOutput…`), so they fail.

## Rule and boundary

The specification has two parts:

- **Elsa 4 runtime.** Dispatch Workflow gains one output, `Output`: the completed child's named outputs as one JSON
  object.
- **Converter.** The Elsa 3 converter carries an awaited Dispatch Workflow's `Result` to `Output`. It refuses an Execute
  Workflow `Result` capture with guidance, and it routes `Done` the way Elsa 3 did.

### Build on #4229, not beside it

main's clean-slate execution-kernel program (#4229) already models this. Its Native Dispatch Workflow completes with
`DispatchResult(Output, Status)`, where `Output` is the child's `output` object of named workflow outputs. Its mapping
from the current activity (`NativeActivityShapes`) refuses a `Result` read because the two shapes differ. The refusal
says to remove the read "until the shapes match".

This specification gives the current activity the kernel's shape: an `Output` that is the child's named outputs. It
leaves the current `Result` unchanged. Nothing about kernel dispatch is duplicated here. Three pieces belong to #4229,
and the coordinator raises them there (see [Raised with #4229](#raised-with-4229)).

The default runtime is still the current core. On main, the Workbench refuses Dispatch Workflow at publish only when the
opt-in Core Preview publications are on (`KernelHostCapabilities.Children` false, K10). So nothing here waits on #4229 to
pass on the default Workbench.

No ADR or ADR amendment is needed. This adds an activity output and converter rules. It changes no rule in an ADR. The
value model is still [ADR 0091](../../docs/adr/0091-json-data-passes-through-unchanged-and-every-serializer-derives-from-one-baseline.md):
output values are JSON and pass through unchanged.

Out of scope:

- A dynamic child: a `WorkflowDefinitionId` set by an expression. It stays refused as it is today (spec 236), because
  Elsa 4 pins the exact child at publish.
- `BulkDispatchWorkflows`: spec 1242.
- Running the child inline as a reusable graph activity. That option was rejected.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An awaited Dispatch Workflow returns the child's output (Priority: P1)

A workflow author authoring in Elsa 4 dispatches a child, waits for it, and reads the child's outputs by name without
unpacking a list.

**Why this priority**: The converter cannot carry the Elsa 3 result over until Elsa 4 has a place for it. It is also
the shape the kernel already uses.

**Independent Test**: Publish a child `acme.credit-check` that sets the output `Decision` to `"approved"`. Publish a
parent that dispatches it with `WaitForCompletion` true, captures `Output` into `Credit`, and returns
`variables.Credit.Decision`.

**Acceptance Scenarios**:

1. **Given** the parent above, **When** it runs and the child completes, **Then** `Credit` is
   `{ "Decision": "approved" }`, the script returns `"approved"`, and the dispatch selects `Completed`.
2. **Given** a child that declares two outputs and sets only one, **When** the parent captures `Output`, **Then** the
   object holds the output the child set and nothing for the other.
3. **Given** a child whose output the capture policy redacts (spec 099 FR-012), **When** the parent captures `Output`,
   **Then** the redacted output is left out of the object. `Result.outputs` still lists it with no value.
4. **Given** a child that faults or is cancelled, **When** the dispatch resumes, **Then** `Output` is absent and a
   capture of it leaves its variable unchanged, as for any other output an activity does not produce.
5. **Given** `WaitForCompletion` false, **When** the dispatch completes with `Dispatched`, **Then** `Output` is absent.

---

### User Story 2 - An awaited Elsa 3 Dispatch Workflow keeps its result (Priority: P1)

A migrating user imports an Elsa 3 workflow that captures an awaited Dispatch Workflow's `Result` and reads the child's
outputs by name.

**Why this priority**: The awaited result is the gap #1152 records. It blocks two S-15 suites.

**Independent Test**: Convert the `acme.address-check` node from the Problem section and run it. Separately, convert the
original `composition-dispatch-root-workflow.json` with only its `channelName` cleared.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 Dispatch Workflow node that captures `Result`, **When** it is converted, **Then** the Elsa 4 node
   captures `Output` into the same variable, and conversion raises no `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE`.
2. **Given** that conversion, **When** the workflow runs and the child completes, **Then** a script reading
   `getAddress().Normalized` (converted to read the variable) returns the child's `Normalized` output.
3. **Given** `WaitForCompletion` bound to an expression that evaluates to false, **When** the converted workflow runs,
   **Then** the dispatch selects `Dispatched`, and the captured variable keeps its value, as in Elsa 3.
4. **Given** the S-15 dispatch root with its `result` capture restored, **When** the
   `dispatch-workflow-awaitCompletion` and `dispatch-workflow-handling-input-output` suites run on Elsa 4, **Then** their
   output assertions pass. Their other blocker, #5818 (the child's busy-wait script), is separate.

---

### User Story 3 - Execute Workflow converts with Elsa 3's flow (Priority: P1)

A migrating user imports an Elsa 3 workflow that runs a child with Execute Workflow. The child runs as its own instance,
and the parent continues after it, as in Elsa 3.

**Why this priority**: Execute Workflow is the common Elsa 3 composition activity. Today a converted Execute Workflow
does not wait when `WaitForCompletion` is false or bound. It also stops its branch when the child faults.

**Independent Test**: Convert Execute Workflow nodes with `WaitForCompletion` unset, literal false, and bound to an
expression, each followed by a node on `Done`. Run each with a child that completes, one that faults, and one that is
cancelled.

**Acceptance Scenarios**:

1. **Given** any `WaitForCompletion` value, **When** an Execute Workflow node is converted, **Then** the Elsa 4 node has
   `WaitForCompletion` literal true, and the existing warning `ELS3-BEHAVIOUR-EXECUTE-WORKFLOW-DURABLE` is raised.
2. **Given** `WaitForCompletion` literal false or bound, **When** it is converted, **Then** the warning also says that
   Elsa 4 waits where Elsa 3 continued past a child that suspended.
3. **Given** a child that completes, faults or is cancelled, **When** the converted parent runs, **Then** the node on
   Elsa 3's `Done` runs in each case.
4. **Given** an Execute Workflow node that captures `Result`, **When** it is converted, **Then** it is refused with
   `ELS3-ACTIVITY-MEMBER-SEMANTICS-CHANGED` and the guidance in FR-010.

---

### User Story 4 - Elsa 3's flow after an awaited dispatch (Priority: P2)

A migrating user whose awaited Elsa 3 Dispatch Workflow is followed by a node on `Done` gets that node run whatever
way the child ended.

**Why this priority**: The converted workflow silently stops, but only when the child faults or is cancelled.

**Independent Test**: Convert an awaited Dispatch Workflow followed by a node on `Done`, then run it with a child that
faults.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 Dispatch Workflow whose `Done` leads to a node, **When** it is converted, **Then** `Done` maps to
   `Dispatched`, `Completed`, `Faulted` and `Cancelled`.
2. **Given** a child that faults, **When** the converted parent runs, **Then** the node on `Done` runs.

### Edge Cases

- **An Execute Workflow child suspends.** Elsa 3 with `WaitForCompletion` false continued at once, with a result of
  status `Running`. Elsa 4 always waits for the child. FR-008 warns about it, and no conversion keeps Elsa 3's early
  continue.
- **A child faults after writing outputs.** Elsa 3 resumed the parent with those outputs. Elsa 4 exposes outputs only for
  a completed child (spec 099 FR-010), so a capture keeps its previous value. FR-011 records this as an accepted
  difference.
- **The child start fails.** Elsa 3 threw and faulted the parent's activity. Elsa 4 selects `DispatchFailed`, which no
  Elsa 3 port maps to, so the branch ends. This is unchanged by this specification.
- **A blank `CorrelationId`.** Elsa 3 left the child uncorrelated. Elsa 4 gives it the parent's correlation id. This is
  unchanged (spec 236).
- **The child version.** Elsa 3 ran the child's published version at run time. Elsa 4 pins the exact executable at
  publish. This is unchanged (spec 236).
- **Output casing.** Output names pass through exactly as the child declares them. Elsa 3 matched them the same way: a
  JavaScript property read is case-sensitive in both.

## Requirements *(mandatory)*

### Functional Requirements

**Elsa 4 runtime**

- **FR-001**: Dispatch Workflow MUST declare a new output, `Output`. It is a JSON object with one property per output of
  the completed child: the output's name, exactly as the child declares it, mapped to its value.
- **FR-002**: `Output` MUST be produced only when the dispatch waited and the child completed (`Completed`). It MUST be
  absent on `Dispatched`, `Faulted`, `Cancelled` and `DispatchFailed`. An absent output leaves a capture's variable
  unchanged.
- **FR-003**: `Output` MUST be built from the same safe output entries that `Result.outputs` holds. A redacted entry
  MUST be left out of `Output`. A completed child with no outputs gives `{}`.
- **FR-004**: `Result` and `ChildWorkflowExecutionId` MUST keep their current shape and behaviour.
- **FR-005**: The Dispatch Workflow designer surface MUST show `Output` as a capturable output, as it shows the existing
  outputs.

**Converter: Dispatch Workflow**

- **FR-006**: An Elsa 3 Dispatch Workflow `Result` capture MUST convert to an Elsa 4 `Output` capture into the same
  variable, whatever `WaitForCompletion` is. This replaces the current `Lost("Result")` rule for Dispatch Workflow.
- **FR-007**: An Elsa 3 Dispatch Workflow's `Done` MUST map to `Dispatched`, `Completed`, `Faulted` and `Cancelled`.

**Converter: Execute Workflow**

- **FR-008**: An Elsa 3 Execute Workflow MUST convert to a Dispatch Workflow with `WaitForCompletion` literal true,
  whatever the Elsa 3 value. The existing `ELS3-BEHAVIOUR-EXECUTE-WORKFLOW-DURABLE` warning MUST be raised. When the
  Elsa 3 value is literal false or bound, the warning MUST also say that Elsa 4 waits for a child that suspends where
  Elsa 3 continued.
- **FR-009**: An Elsa 3 Execute Workflow's `Done` MUST map to `Completed`, `Faulted` and `Cancelled`.
- **FR-010**: An Elsa 3 Execute Workflow `Result` capture MUST be refused with `ELS3-ACTIVITY-MEMBER-SEMANTICS-CHANGED`.
  Elsa 4 Dispatch Workflow has a `Result` output, but it means something else, so the generic lost-output message
  ("has no such output") would be wrong. That code's guidance is HTTP-specific today, so it MUST become per-rule.
  - The message MUST say that Elsa 3's result wraps the child's output with its instance id and status, and that Elsa 4
    returns these separately.
  - The guidance MUST name both ways forward: replace the node in Elsa 3 with a Dispatch Workflow that waits for
    completion (its result is the child's output alone and converts per FR-006), or remove the capture, convert, and
    capture `Output` in Elsa 4.
  - The guidance MUST give the read mapping: `Result.Output.X` becomes `Output.X`, `Result.WorkflowInstanceId` becomes
    `ChildWorkflowExecutionId`, and `Result.Status` or `Result.SubStatus` becomes the selected outcome.

**Differences, tests and records**

- **FR-011**: A faulted or cancelled child's outputs are not carried (FR-002), where Elsa 3 resumed the parent with them.
  This is an accepted difference. The converter raises no warning for it, because it cannot know whether a child will
  fault. The difference MUST be recorded in the converter's mapping documentation (`extensions/Elsa3/src/Mapping/README.md`).
- **FR-012**: Converter tests MUST cover each FR-006 to FR-010 case. Runtime tests MUST cover FR-001 to FR-004 on the
  current core, including restart of a waiting parent, where the existing waited-dispatch tests already run.
- **FR-013**: The S-15 ledger MUST be updated in the S-15 session, not in this PR.
  - The adjusted dispatch root no longer needs its `result` capture removed.
  - The Execute Workflow root stays refused, now with the FR-010 code and guidance and the existing dynamic
    `WorkflowDefinitionId` refusal.
  - Any new adjusted copy needs the coordinator's approval.

### Key Entities

- **Dispatch output object** (`Output`): the completed child's named outputs, name → JSON value, without redacted
  entries. Its shape is the kernel's `DispatchResult.Output`.
- **Dispatch result** (`Result`): unchanged. It holds the child execution id, the terminal status, the output entries
  with type and redaction, and diagnostics.

## Raised with #4229

These pieces sit on the kernel path. This specification does not build them. Without them, the conversions here run on
the current core but are refused or behave differently on the kernel:

1. **Map `Output` to the Native result.** `NativeActivityShapes` maps the current Dispatch Workflow `Result` read to
   a refusal. An `Output` read has the same shape as `Native.DispatchResult.Output`, so it can pass through. The kernel
   also needs to confirm that `Output` is absent unless the child finished normally, which matches FR-002. Today
   `ChildDone.Output` is passed on for any status.
2. **Children on PostgreSQL, and `Children: true` on the Workbench Core Preview.** The #5794 description names this
   slice "PostgreSQL child workflows (Dispatch)". It is not filed yet. Until it lands, the Workbench refuses Dispatch at
   publish whenever the Core Preview publications are on.
3. **`CorrelationId` on kernel dispatch.** The kernel refuses any bound correlation id ("this host starts a child
   without a correlation id"). Both S-15 roots bind one, as does every converted Elsa 3 dispatch that sets it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For the two Problem section examples, the awaited Dispatch Workflow converts with no diagnostic other than
  existing ones, and the Execute Workflow capture is refused with `ELS3-ACTIVITY-MEMBER-SEMANTICS-CHANGED` and the
  FR-010 guidance. Converter tests pin each
  diagnostic.
- **SC-002**: With the `result` capture restored in the adjusted S-15 dispatch root, every output assertion in
  `dispatch-workflow-awaitCompletion` and `dispatch-workflow-handling-input-output` passes on the default Workbench,
  once #5818 is fixed.
- **SC-003**: 100% of the FR-001 to FR-004 cases pass on the current core, including a waiting parent that resumes
  after a restart.
- **SC-004**: No existing waited-dispatch, Dispatch Workflow contract or Elsa 3 converter test changes behaviour, except
  the tests that pin the old `Lost("Result")` rule, the old `Done` mapping, and the carried `WaitForCompletion` value of
  Execute Workflow.

## Assumptions

- The default runtime on the initiative branch, and on main until #4229's K13 switch, is the current core. Its waited
  Dispatch Workflow already captures safe child outputs (spec 099), so FR-001 to FR-003 project data that is already
  there.
- An output that is absent leaves its capture's variable unchanged (`RuntimeOutputCaptureProjector`). Elsa 3 behaved
  the same way when it never set `Result`.
- Pre-stable: the Dispatch Workflow contract gains an output with no compatibility work.
- Execute Workflow's in-process, synchronous run is not kept. The child is a durable dispatch (spec 236), and the
  existing behaviour warning says so.
