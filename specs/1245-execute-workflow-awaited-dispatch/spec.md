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

In Elsa 3, a child that faults still counts as finished, so the parent continues on `Done` with whatever output the
child wrote. A child that is cancelled never resumes an awaited parent, which stays suspended: cancellation raises
`WorkflowCancelled`, not `WorkflowExecuted`.

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

- **Elsa 4 runtime.** Dispatch Workflow gets one data output, `Output`: the completed child's named outputs as one
  JSON object, holding the child's committed values unchanged. The `Result` output is removed. Its status repeats the
  outcome, and its `outputs` were a diagnostics view of the same values, so an author would see two outputs holding
  nearly the same thing. The terminal status and the safe diagnostics stay in the activity's stored result. They are
  not a named output, but the whole-result read (`$result`) still reaches them, as it reaches `Result` today.
- **Converter.** The Elsa 3 converter carries an awaited Dispatch Workflow's `Result` to `Output`. It refuses an Execute
  Workflow `Result` capture with guidance, and it routes `Done` the way Elsa 3 did.

### Build on #4229, not beside it

main's clean-slate execution-kernel program (#4229) already models this. Its Native Dispatch Workflow completes with
`DispatchResult(Output, Status)`, where `Output` is the child's `output` object of named workflow outputs. Its mapping
from the current activity (`NativeActivityShapes`) refuses a `Result` read because the two shapes differ. The refusal
says to remove the read "until the shapes match".

This specification gives the current activity the kernel's shape: an `Output` that is the child's named outputs, and
no structured `Result`. The kernel's `Result` refusal then has nothing left to refuse. Nothing about kernel dispatch is
duplicated here. Four pieces belong to #4229,
and the coordinator raises them there (see [Raised with #4229](#raised-with-4229)).

The default runtime is still the current core. On main, the Workbench refuses Dispatch Workflow at publish only when the
opt-in Core Preview publications are on (`KernelHostCapabilities.Children` false, K10). So nothing here waits on #4229 to
pass on the default Workbench.

### Data, not diagnostics

`Output` cannot be built from the entries that today's `Result.outputs` holds. Those come from `IWorkflowOutputSource`,
which applies the runtime diagnostics capture policy. Its default level for workflow outputs is a diagnostic snapshot, so by default each
value arrives in one of two forms:

- a preview envelope, with strings over 256 characters truncated and objects cut at 32 properties;
- redacted, when the output's name looks sensitive (for example `token`), including the names of nested properties.

That suits an inspection view, but it is not data. `Output` therefore holds the child's committed output values
themselves, unaffected by any diagnostics setting. The diagnostics-view entries are removed with `Result`. The child's
outputs stay inspectable on the child's own execution.

A child's Set Output keeps the value protection policy of what it wrote. Two kinds of output are left out of `Output`:

- an output whose policy protects it: marked sensitive, required to be encrypted, or carrying a redaction mode or a
  retention policy;
- an output that is not stored inline: no inline payload, or a policy whose minimum storage is not inline.

Set Output refuses a withheld value, so an encryption-required output can only be an explicit null.

Carrying these raw would put them in the resume intent, the bookmark stimulus and the parent's variable, where the
child's policy no longer applies. [ADR 0045](../../docs/adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md) §9
forbids that downgrade. The current projection pipeline gives an output one static policy, so it cannot carry a
per-value policy.

Leaving a value out keeps §9: nothing is downgraded. ADR 0046 V6 then requires a loss to be declared, never silent.
So the omission is visible in two ways:

- **At design time.** The `Output` contract description states it: a declared loss under
  [ADR 0046](../../docs/adr/0046-output-binding-coercion-uses-pinned-value-representations.md) V6.
- **In each run.** When the dispatch resumes, it reports one execution warning that names every output it left out.
  The names are the child's declared contract names, not value data, so the warning carries names and never a value.

Today's `Result.outputs` carries a preview of a redaction-mode or retention-governed value (#5851). Removing those
entries closes that gap on the initiative branch.

Elsa 3 had no such policies, so no converted workflow loses a value by this rule. A host that wants protected child
outputs to reach a parent needs per-value policy propagation through captures. That is new ADR 0045 §9 work, and it is
not built here: no one needs it yet.

No ADR or ADR amendment is needed. This changes an activity's outputs and converter rules. It applies
[ADR 0091](../../docs/adr/0091-json-data-passes-through-unchanged-and-every-serializer-derives-from-one-baseline.md):
JSON data passes through unchanged. It changes no rule in an ADR.

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
3. **Given** default diagnostics settings, and a child whose outputs are `Summary` (a 1,000-character string) and
   `token` (a string), **When** the parent captures `Output`, **Then** both values arrive unchanged.
4. **Given** a child output marked sensitive, or required to be encrypted, or carrying a redaction mode or a retention policy, or not stored inline, **When** the parent
   captures `Output`, **Then** that output is left out of the object, and the dispatch reports one execution warning
   naming it, without its value. There is one test per kind; for encryption the value is an explicit null.
5. **Given** a child that faults or is cancelled, **When** the dispatch resumes, **Then** `Output` is absent and a
   capture of it leaves its variable unchanged, as for any other output an activity does not produce.
6. **Given** `WaitForCompletion` false, **When** the dispatch completes with `Dispatched`, **Then** `Output` is absent.

---

### User Story 2 - An awaited Elsa 3 Dispatch Workflow keeps its result (Priority: P1)

A migrating user imports an Elsa 3 workflow that captures an awaited Dispatch Workflow's `Result` and reads the child's
outputs by name.

**Why this priority**: The awaited result is the gap #1152 records. It blocks two S-15 suites.

**Independent Test**: Convert the `acme.address-check` node from the Problem section and run it. Separately, convert the
original `composition-dispatch-root-workflow.json` with only its `channelName` cleared. The S-15 session does this
part (FR-013).

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
expression, each followed by a node on `Done`. Run each with a child that completes and with a child that faults.

**Acceptance Scenarios**:

1. **Given** any `WaitForCompletion` value, **When** an Execute Workflow node is converted, **Then** the Elsa 4 node has
   `WaitForCompletion` literal true, and the existing warning `ELS3-BEHAVIOUR-EXECUTE-WORKFLOW-DURABLE` is raised once.
2. **Given** `WaitForCompletion` unset, null, literal false or bound, **When** it is converted, **Then** the warning also
   says that Elsa 4 waits where Elsa 3 continued past a child that suspended. Elsa 3 ran an unset value as false. Only
   a literal true omits the sentence.
3. **Given** a child that completes or faults, **When** the converted parent runs, **Then** the node on Elsa 3's `Done`
   runs in both cases.
4. **Given** an Execute Workflow node that captures `Result`, **When** it is converted, **Then** it is refused with
   `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE` and the guidance in FR-010.

---

### User Story 4 - Elsa 3's flow after an awaited dispatch (Priority: P2)

A migrating user whose awaited Elsa 3 Dispatch Workflow is followed by a node on `Done` gets that node run whatever
way the child ended.

**Why this priority**: The converted workflow silently stops, but only when the child faults.

**Independent Test**: Convert an awaited Dispatch Workflow followed by a node on `Done`, then run it with a child that
faults.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 Dispatch Workflow whose `Done` leads to a node, **When** it is converted, **Then** `Done` maps to
   `Dispatched`, `Completed` and `Faulted`.
2. **Given** a child that faults, **When** the converted parent runs, **Then** the node on `Done` runs.

### Edge Cases

- **An Execute Workflow child suspends.** Elsa 3 with `WaitForCompletion` false continued at once, with a result of
  status `Running`. Elsa 4 always waits for the child. FR-008 warns about it, and no conversion keeps Elsa 3's early
  continue.
- **A child faults after writing outputs.** Elsa 3 resumed the parent with those outputs. Elsa 4 exposes outputs only for
  a completed child (spec 099 FR-010), so a capture keeps its previous value. FR-011 records this as an accepted
  difference.
- **A child is cancelled.** In Elsa 3 an awaited parent stayed suspended. In Elsa 4 the dispatch selects `Cancelled`.
  No Elsa 3 port maps to it:
  - in a Flowchart parent the branch ends there instead of hanging;
  - in a Sequence parent, which continues after any outcome but Break, the next step runs.

  This is an accepted difference (FR-011).
- **The child reads `ParentInstanceId`.** Elsa 3 added `ParentInstanceId` to every child's input. Elsa 4 does not, so a
  converted child that reads it gets nothing. This is an accepted difference (FR-011). To keep it, pass it as an
  explicit input.
- **The child start fails.** Elsa 3 threw and faulted the parent's activity. Elsa 4 selects `DispatchFailed`, which no
  Elsa 3 port maps to:
  - in a Flowchart parent the branch ends;
  - in a Sequence parent the next step runs.

  This specification does not change that. It is an accepted difference (FR-011).
- **A Sequence parent.** The `Done` mappings in FR-007 and FR-009 matter only in a Flowchart. A Sequence runs its next
  step after any dispatch outcome, so a faulted child already continued there, as in Elsa 3.
- **A blank `CorrelationId`.** Elsa 3 left the child uncorrelated. Elsa 4 gives it the parent's correlation id. This is
  unchanged (spec 236).
- **The child version.** Elsa 3 ran the child's published version at run time. Elsa 4 pins the exact executable at
  publish. This is unchanged (spec 236).
- **Output casing.** Output names pass through exactly as the child declares them, and a read must match exactly. Elsa 3
  handed the output over as a dictionary that ignores case, so a read with other casing, such as
  `getAddress().normalized`, also worked there. In Elsa 4 it reads `undefined`. This is an accepted difference
  (FR-011).

## Requirements *(mandatory)*

### Functional Requirements

**Elsa 4 runtime**

- **FR-001**: Dispatch Workflow MUST declare a new output, `Output`, typed as a JSON object. It has one property per
  output the completed child set: the output's name, exactly as the child declares it, mapped to the child's committed
  value, unchanged. A converted Elsa 3 `Result` variable (`Object`, `ObjectDictionary`) MUST accept the capture at
  publish.
- **FR-002**: `Output` MUST be produced only when the dispatch waited and the child completed (`Completed`). It MUST be
  absent on `Dispatched`, `Faulted`, `Cancelled` and `DispatchFailed`. An absent output leaves a capture's variable
  unchanged.
- **FR-003**: `Output` MUST be built from the child's committed workflow output values. It MUST NOT be built from the
  diagnostics capture policy, so no diagnostics setting changes it.
  - An output marked sensitive, or required to be encrypted, or carrying a redaction mode or a retention policy (its value protection policy), or not stored inline, MUST be left out.
  - The `Output` contract description MUST state that protected and externally stored outputs are left out (ADR 0046
    V6: a declared loss).
  - When an output is left out, the dispatch MUST report one execution warning on resume (ADR 0045 §9: not silent):
    - code `dispatch.child-output-withheld`, severity Warning;
    - the left-out output names, sorted ordinally and joined with `", "`, under the `Details` key `outputs`, as
      `JavaScriptValueWrites` does for `paths`;
    - no value.
  - A completed child with no outputs gives `{}`.
  - The values MUST be replay-stable: a resumed or retried parent gets the same `Output` (spec 099 FR-008).
- **FR-004**: Dispatch Workflow MUST NOT declare a `Result` output. Its outputs are `ChildWorkflowExecutionId` and
  `Output`, and the outcome carries the terminal status.
  - The safe terminal result MUST stay in the activity's stored result as a member that is not an output: the status,
    the allow-listed fault and cancellation diagnostics (spec 100 FR-004 to FR-006), and the child-control transport.
    The whole-result read (`$result`) still reaches it, as it reaches `Result` today. That is accepted, because spec 100
    made these fields safe to cross to the parent.
  - It MUST no longer carry child output entries. The diagnostics-view outputs are removed, which closes #5851 on the
    initiative branch.
  - Spec 1245 supersedes the child-output and `Result`-output parts of three specs:
    - spec 096 (FR-003 and `contracts/dispatch-workflow.md`: the reserved `Result` output);
    - spec 099 (its child-output capture through the runtime capture policy, redacted entries kept with metadata, and
      the activity setting `Result`: User Story 2 scenarios 2 and 3, User Story 3 and its scenario 4, User Story 4
      scenario 4, FR-009, FR-011, FR-012, FR-021, Key Entities, SC-004 and the capture-policy assumption);
    - spec 100 (FR-004, FR-008 and FR-026, where they name `Result` or output capture).

    Each of those specs gets one note under its status line that cites spec 1245 and lists its superseded parts. Their
    text is otherwise unchanged.
- **FR-005**: The Dispatch Workflow designer surface MUST show `Output` as a capturable output and no longer show
  `Result`. Every consumer of the Dispatch outputs MUST be updated:
  - the activity contract surface baseline (`activity-contract-surface.baseline.json`, which
    `ActivityContractSurfaceSnapshotTests` checks);
  - the Dispatch Workflow `EXTENSION_POINTS.md` and `README.md`, which describe policy-safe outputs and redacted
    entries;
  - the dispatch tests that build or read `Result`.

**Converter: Dispatch Workflow**

- **FR-006**: An Elsa 3 Dispatch Workflow `Result` capture MUST convert to an Elsa 4 `Output` capture into the same
  variable, whatever `WaitForCompletion` is. This replaces the current `Lost("Result")` rule for Dispatch Workflow.
- **FR-007**: An Elsa 3 Dispatch Workflow's `Done` MUST map to `Dispatched`, `Completed` and `Faulted`.

**Converter: Execute Workflow**

- **FR-008**: An Elsa 3 Execute Workflow MUST convert to a Dispatch Workflow with `WaitForCompletion` literal true,
  whatever the Elsa 3 value. The existing `ELS3-BEHAVIOUR-EXECUTE-WORKFLOW-DURABLE` warning MUST be raised. When the
  Elsa 3 value is anything but literal true (unset, null, literal false or bound), the warning MUST also say that Elsa 4
  waits for a child that suspends where Elsa 3 continued.
- **FR-009**: An Elsa 3 Execute Workflow's `Done` MUST map to `Completed` and `Faulted`.
- **FR-010**: An Elsa 3 Execute Workflow `Result` capture MUST be refused with `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE`.
  Elsa 4 Dispatch Workflow has no `Result` output. The lost-output rule's message and guidance are generic today, so the
  rule MUST take per-rule text.
  - The message MUST say that Elsa 3's result wraps the child's output with its instance id and status, and that Elsa 4
    returns these separately.
  - The guidance MUST name both ways forward: replace the node in Elsa 3 with a Dispatch Workflow that waits for
    completion (its result is the child's output alone and converts per FR-006), or remove the capture, convert, and
    capture `Output` in Elsa 4.
  - The guidance MUST give the read mapping:
    - `Result.Output.X` becomes `Output.X`;
    - `Result.WorkflowInstanceId` becomes `ChildWorkflowExecutionId`;
    - `Result.Status` or `Result.SubStatus` becomes the selected outcome;
    - `Result.CorrelationId` and `Result.WorkflowDefinitionVersionId` were never set by Elsa 3, so they have no
      counterpart.

**Differences, tests and records**

- **FR-011**: The converter MUST NOT raise a warning for the following accepted differences, because whether they apply
  depends on how the child runs or how a script reads the value, not on the converted node. They MUST be recorded in the
  converter's mapping documentation (`extensions/Elsa3/src/Mapping/README.md`):
  - A faulted child's outputs are not carried (FR-002); Elsa 3 resumed the parent with them.
  - A cancelled child selects `Cancelled`, which ends a Flowchart branch and continues a Sequence; Elsa 3 left an awaited
    parent suspended.
  - A failed child start selects `DispatchFailed`, with the same Flowchart and Sequence behaviour; Elsa 3 faulted the
    parent's activity.
  - The child gets no `ParentInstanceId` input.
  - An output read must match the output name's case.
- **FR-011a**: Spec 236's member map (`specs/236-elsa3-activity-map/member-map.md`) MUST be amended in place for the
  `DispatchWorkflow.Result`, `DispatchWorkflow outcome Done` and `ExecuteWorkflow → DispatchWorkflow` rows, and the
  null-member row for `ExecuteWorkflow.WaitForCompletion`, citing spec 1245, as spec 258 amended the Event row.
- **FR-012**: Converter tests MUST cover each FR-006 to FR-010 case. Runtime tests MUST cover FR-001 to FR-004 on the
  current core, including restart of a waiting parent, where the existing waited-dispatch tests already run.
- **FR-013**: The S-15 ledger MUST be updated in the S-15 session, not in this PR.
  - The adjusted dispatch root no longer needs its `result` capture removed.
  - The Execute Workflow root stays refused, with the FR-010 guidance and the existing dynamic `WorkflowDefinitionId`
    refusal.
  - Any new adjusted copy needs the coordinator's approval.

### Key Entities

- **Dispatch output object** (`Output`): the completed child's named outputs, name → JSON value, without outputs whose
  value policy protects them (FR-003). Its shape is the kernel's `DispatchResult.Output`.
- **Terminal result** (stored, not an output): the child execution id, the terminal status, the allow-listed
  diagnostics and the child-control transport. It holds no child output entries.

## Raised with #4229

These pieces sit on the kernel path. This specification does not build them. Without them, the conversions here run on
the current core but are refused or behave differently on the kernel:

1. **Map `Output` to the Native result.** `NativeActivityShapes` refuses a Dispatch Workflow `Result` read. After this
   spec the current activity has no `Result`, so that rule can go. An `Output` read has the same shape as
   `Native.DispatchResult.Output`, so it can pass through. FR-002 also
   needs the Native `Output` to be absent, not null, unless the child finished normally. Today it is not:
   - `ChildDone.Output` is passed on for any status;
   - `Dispatched` completes with an explicit `null` (`DispatchResult(null, "Dispatched")`), which a capture writes.
2. **Children on PostgreSQL, and `Children: true` on the Workbench Core Preview.** The #5794 description names this
   slice "PostgreSQL child workflows (Dispatch)". It is not filed yet. Until it lands, the Workbench refuses Dispatch at
   publish whenever the Core Preview publications are on.
3. **`CorrelationId` on kernel dispatch.** The kernel refuses any bound correlation id ("this host starts a child
   without a correlation id"). Both S-15 roots bind one, as does every converted Elsa 3 dispatch that sets it.
4. **`CancelChildOnParentCancellation` false on kernel dispatch.** The converter always emits `false`, because Elsa 3 never
   cancelled a child with its parent (spec 236). The kernel accepts only null or true ("this host always cancels a waited
   child with its parent"), so it refuses every converted dispatch.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For the two Problem section examples, the awaited Dispatch Workflow converts with no diagnostic other than
  existing ones, and the Execute Workflow capture is refused with `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE` and the FR-010
  guidance. Converter tests pin each diagnostic.
- **SC-002**: With the `result` capture restored in the adjusted S-15 dispatch root, every output assertion in
  `dispatch-workflow-awaitCompletion` and `dispatch-workflow-handling-input-output` passes on the default Workbench,
  once #5818 is fixed.
- **SC-003**: 100% of the FR-001 to FR-004 cases pass on the current core with the real workflow output source and
  default diagnostics settings, including a waiting parent that resumes after a restart.
- **SC-004**: No existing waited-dispatch, Dispatch Workflow contract or Elsa 3 converter test changes behaviour, except
  the tests that read the removed `Result` output or its output entries, the tests that pin the old `Lost("Result")`
  rule, the old `Done` mapping, the carried `WaitForCompletion` value of
  Execute Workflow, and the Execute Workflow durable-warning tests (the warning moves into the rewrite and gains a
  sentence).

## Assumptions

- The default runtime on the initiative branch, and on main until #4229's K13 switch, is the current core. Its waited
  Dispatch Workflow reads the child's committed outputs when the child completes (spec 099 FR-009), so the values exist.
  FR-003 replaces the policy-filtered read with a read of the values themselves.
- An output that is absent leaves its capture's variable unchanged (`RuntimeOutputCaptureProjector`). Elsa 3 behaved
  the same way when it never set `Result`.
- Pre-stable: the Dispatch Workflow contract gains `Output` and loses `Result` with no compatibility work.
- Execute Workflow's in-process, synchronous run is not kept. The child is a durable dispatch (spec 236), and the
  existing behaviour warning says so.
