# Feature Specification: Engine intrinsics in a reusable activity body

**Feature Branch**: `agent/claude/3023-intrinsics`

**Created**: 2026-11-26

**Status**: Approved — coordinator, 2026-11-26

**Input**: Issue [#3023](https://github.com/elsa-workflows/elsa-foundation/issues/3023), part of initiative
[#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052). A reusable (graph) activity body holds the engine
intrinsics Set Variable, Set Output, Correlate and Set Name, as a workflow does. The Elsa 3 converter then converts
reusable workflows that use them instead of refusing them.

## Problem

An Elsa 3 reusable workflow `Enrich` (usable as an activity) declares an input `Order`, an output `Result` and a variable
`Draft` (`Object`). Its flowchart runs:

1. **Set Variable** `Draft` = `var d = getOrder(); d.checked = true; return d;`
2. **Set Output** `Result` = the variable `Draft`

A workflow that calls `Enrich` reads `Result`. Elsa 3 ran this on every call. Today:

- **Import.** The converter refuses both nodes with `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED` (spec 246 D-7), and
  every workflow that calls `Enrich` is refused with `ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY`. The caller cannot be
  imported at all.
- **Native Elsa 4.** An author who builds the same body in Studio can publish the Set Output (the graph compiler
  supports it), but not the Set Variable: a graph body Set may only target a variable of a native container *below* the
  graph boundary. A Set of the body's own variable (inside a body addressed as scope `workflow`, or Studio's legacy
  `activity-graph`) is refused with
  `activity.graph.set-local-target-invalid`. At run time the intrinsic executor drops the closed boundary from the write
  targets, so even a forged body would fault ("not visible to intrinsic execution").
- **Correlate and Set Name** in a body are refused at publish with `activity.graph.intrinsic-unsupported`, and at import
  with the same converter refusal. The readiness guide tells the engineer to move them into every caller by hand.

This blocks two S-15 JTest acceptance definitions directly (`composition-workflow-as-activity-sub.json`,
`storage-drivers-variables-sub-workflow.json`: both use Set Output, the second also Set Variable on its own variables)
and their two root workflows through the dependency. Spec 236 SC-006 ("a caller receives the output a converted reusable
workflow set") has waited on this since spec 246.

## What already exists

| Body node | Publish (graph compiler) | Run | Elsa 3 import |
|---|---|---|---|
| Set Output naming a declared output | supported | supported | refused (D-7) |
| Set of a variable of a native container inside the body | supported | not proven end to end (compile tests only) | refused (D-7) |
| Set of the body's own variable (`workflow` / legacy `activity-graph`) | refused | faults | refused (D-7) |
| Correlate, Set Name | refused | — | refused (D-7) |
| Finish | refused | — | refused (D-7) |
| A step's script/C# write of the body's own variable | supported (spec 1241) | supported (spec 1241) | converts (spec 1241) |

So the gap is the Set intrinsic on the body's own variables, Correlate and Set Name in a body, and lifting the
converter refusal for everything the body can now hold.

## Rule and boundary

This specification applies [ADR 0090](../../docs/adr/0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md)
(A-3: what a step may change is engine-owned state — variables, workflow outputs, correlation ID and instance name —
committed with the step) and [ADR 0092](../../docs/adr/0092-variable-reads-and-writes-share-one-versioned-lifecycle.md)
(one commit rule for every variable write, frames and rows alike). It uses the closed-scope rule of
[spec 265](../265-graph-activity-scope-reads/spec.md) and [spec 1241](../1241-closed-scope-step-writes/spec.md): a node
in a body reaches the variables of its own nearest body only; values come in through inputs and go back through
outputs. It needs no ADR and no ADR amendment: every write it adds is one ADR 0090 already names, and the scope rule is
the one specs 265 and 1241 already apply.

- **Set Variable** in a body writes a variable of its nearest body (the graph's own variables, or a native container
  inside it). It never reaches the caller's variables. It is the same write as spec 1241's step write to a closed-scope
  row, made by an intrinsic instead of a step session.
- **Set Output** in a body writes the reusable activity's declared output for this invocation (as today). It never
  becomes the calling workflow's output (spec 1234 FR-016).
- **Correlate** and **Set Name** in a body change the correlation ID and instance name of the *running workflow*, which
  is the workflow that called the reusable activity. That is what Elsa 3 did ("Correlate, Set Name and Finish act on that
  calling workflow", readiness guide), and it is what spec 1234 already says these values are: they belong to the
  running workflow. The ADR 0090 correlation rule holds: existing waits keep the correlation they captured.
  *(Decision Q1.)* Elsa 3 workflows that correlate or name the instance inside a reusable activity with a Correlate or
  Set Name node therefore import unchanged (a script's `setCorrelationId` stays refused, see Excluded).

Excluded:

- **Script identity writes in a body** (`setCorrelationId`, `setWorkflowInstanceName` from a Run JavaScript step) stay
  refused, as spec 1234 FR-011 and spec 1241 decided. This spec lifts the refusal for the Correlate and Set Name
  *nodes* only; an Elsa 3 reusable that correlates from a script is still refused with that spec's code and guidance.
- **Finish** in a body: in Elsa 3 it ends the whole calling workflow from inside an activity. Elsa 4 does not carry that
  behaviour over; it is a deliberate Elsa 4 behaviour, not a gap. Publish keeps refusing it, and the import refuses it
  with its own code and guidance that explains why and what to do instead (FR-013). *(Decision Q2.)*
- Merge and Reduce intrinsics in a body (Elsa 3 has no equivalent; no case needs them).
- Body Set writes whose value needs encryption or external storage: refused at run time (FR-015), as spec 1241 refuses
  them for a step write.
- Background placement of a body intrinsic (ADR 0093 C9 unchanged).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A reusable activity sets its own variable (Priority: P1)

An activity author builds a reusable activity whose body keeps a value in a body variable and sets it with a Set
Variable node, as in a workflow.

**Why this priority**: It is the remaining design and run-time gap; the Elsa 3 import (User Story 3) depends on it.

**Independent Test**: Publish a reusable activity with input `Amount` (`Int32`), variable `Total = 0`, output `Total`
mapped to the variable, and a body: Set Variable `Total` = `args.Amount * 2`, then a step that returns `Total`. Run a
workflow that calls it with `Amount = 21`.

**Acceptance Scenarios**:

1. **Given** the body above, **when** a workflow calls the activity with `Amount = 21`, **then** the next body step reads
   `42` and the caller reads output `Total = 42`.
2. **Given** the calling workflow also declares a variable `Total`, **when** the body's Set runs, **then** only the
   body's `Total` changes.
3. **Given** the same reusable activity called twice in one workflow, **then** each activation's `Total` changes on its
   own.
4. **Given** a Set in a body whose value faults, **then** the body variable keeps its previous value (the write commits
   with the step or not at all).
5. **Given** a Set that targets the caller's variable, an outer body's variable, or an undeclared variable, **then**
   publish refuses it with `activity.graph.set-local-target-invalid` (whose message now names the body's own variables
   as valid targets).
6. **Given** two parallel body branches that Set the same body variable, **then** publish warns as for a workflow
   (`activity.graph.unordered-variable-write`) and the run applies the ADR 0092 commit rule, leaving write history with
   the intrinsic as the writer.

---

### User Story 2 - A reusable activity sets the caller's correlation ID and name (Priority: P2)

An activity author puts Correlate or Set Name in a reusable body, so every workflow that uses the activity is correlated
or named the same way.

**Why this priority**: Elsa 3 parity named in #3023; no S-15 case needs it, and a manual workaround exists today.

**Independent Test**: Publish a reusable activity whose body runs Correlate `order-` + `args.OrderId`, then Set Name
`Order ` + `args.OrderId`. Run a workflow that calls it with `OrderId = 7`.

**Acceptance Scenarios**:

1. **Given** the body above, **when** the caller runs it, **then** the caller's instance has correlation ID `order-7`
   and name `Order 7` once the step commits, and a later step of the caller reads them.
2. **Given** the body suspends on a wait created before the Correlate, **then** that wait keeps the correlation it
   captured (ADR 0090).
3. **Given** the Correlate's value faults, **then** the caller's correlation ID is unchanged.

---

### User Story 3 - Elsa 3 reusable workflows with these nodes import (Priority: P1)

An engineer imports Elsa 3 reusable workflows that use Set Variable, Set Output, Correlate or Set Name, and the workflows
that call them.

**Why this priority**: It is why the gap was found and what unblocks the S-15 corpus; it depends on User Stories 1 and 2.

**Independent Test**: Convert, publish and run `Enrich` from the problem statement and a caller that reads `Result`
(spec 236 SC-006).

**Acceptance Scenarios**:

1. **Given** `Enrich`, **when** it is imported with its caller, **then** both publish and the caller reads `Result` with
   `checked = true`.
2. **Given** a reusable Set Output that names an output the reusable workflow does not declare, **then** conversion
   refuses it with `ELS3-ACTIVITY-REUSABLE-UNDECLARED-OUTPUT` and guidance (declare the output, or remove the Set Output). It
   never becomes the caller's workflow output.
   A Set Output whose name differs from the declared output only in case (`result` for `Result`) converts to the
   declared output, as in Elsa 3.
3. **Given** a reusable body with an HTTP Endpoint whose Headers or Query String Data is captured, **then** the
   synthesized Set converts and the body variable holds the projection.
4. **Given** a reusable body with Finish, **then** conversion refuses it with `ELS3-ACTIVITY-REUSABLE-FINISH-UNSUPPORTED`, and the
   guidance explains why Elsa 4 does not support it and how to restructure (FR-013).
5. **Given** the two S-15 definitions above, **then** they and their two root workflows no longer stop on
   `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED` or `ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY` (SC-002; the JTest ledger
   records the result, spec 1235 owns the acceptance count).

---

### User Story 4 - Studio authors these nodes in a reusable body (Priority: P2)

An activity author adds Set Variable, Correlate or Set Name to a reusable activity in the Studio graph designer.

**Why this priority**: Studio lives in this repository; a native capability is not done until an author can use it.

**Independent Test**: In the activity definition editor, add a Set Variable to the body and pick the target; add a
Correlate; publish.

**Acceptance Scenarios**:

1. **Given** the graph designer, **then** the Set Variable target picker lists the body's own variables (labelled
   *Activity graph*) and the variables of enclosing containers in the body, and nothing outside the body.
2. **Given** Correlate and Set Name, **then** they are available in the graph designer and publish.
3. **Given** a refused node (for example Finish), **then** the publish diagnostic focuses that node, as other graph
   diagnostics do.

### Edge Cases

- A Set and a step session write the same body variable in one run: each commits with its own step, ordered and judged
  as in a workflow (ADR 0092).
- A Set Output and an output mapping for the same output: still refused (`activity.graph.set-output-mapping-conflict`).
- A reusable activity used inside another reusable activity: each Set reaches its own nearest body only; Correlate and
  Set Name still act on the running workflow.
- A graph published with an older graph compiler: unchanged; pre-stable, it is republished, not migrated.
- A body Set whose value needs external storage or encryption: the step faults at run time (FR-015).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Graph draft validation and compilation MUST accept a Set intrinsic that targets a variable declared by the
  graph, addressed inside the body as scope `workflow` (or Studio's legacy `activity-graph`), in addition to today's
  native-container targets inside the body. The converter emits `workflow` for every Set it makes.
- **FR-002**: Graph draft validation MUST keep refusing a Set whose target is outside the nearest body, undeclared,
  or declared more than once. The `activity.graph.set-local-target-invalid` message and its
  reference-doc descriptor MUST name the graph's own variables as valid targets.
- **FR-003**: At run time a Set in a body MUST write the variable of its nearest closed boundary (the boundary's variable
  row) when that is the declaring scope, and MUST NOT write any frame above the boundary.
- **FR-004**: A body Set MUST follow the same commit rule, versioning, lost-update mode and write history as any other
  variable write (ADR 0092), with the intrinsic as the recorded writer, and MUST save nothing when it faults.
- **FR-005**: Graph draft validation and compilation MUST accept the Correlate (`elsa.intrinsic.set-correlation-id@1`)
  and Set Name (`elsa.intrinsic.set-instance-name@1`) intrinsics in a body; at run time they MUST change the running
  workflow's correlation ID and instance name with the step's commit, under ADR 0090's correlation rule.
- **FR-006**: A graph body MUST keep refusing every other intrinsic (including Finish, Merge, Reduce) with
  `activity.graph.intrinsic-unsupported`, whose message (today "Only Set Output and Set of a declared enclosing
  body-local variable…") MUST list what is now supported.
- **FR-007**: The graph body write-order analysis MUST count body Set intrinsics as writers, as workflow validation does.
- **FR-008**: Placement of a reusable activity into a workflow MUST carry every supported body intrinsic: the placement
  allow-lists that today match only Set and Set Output (visible scopes, original result-read facts) MUST also cover
  Correlate and Set Name, and a placed body Set's visible scopes MUST include the boundary so its remapped target
  resolves to the placed boundary.
- **FR-009**: The Elsa 3 converter MUST stop refusing reusable-body Set Variable, Set Output, Correlate, Set Name and
  HTTP Endpoint projections. `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED` is retired (pre-stable, no alias).
- **FR-010**: The converter MUST convert a reusable Set Output that names a declared output to the graph's Set Output of
  that output, with no synthesized variable or output mapping (Q3). It matches the name case-insensitively, as Elsa 3
  did, and writes the declared output's exact name into the Set Output's name literal (the graph compiler compares
  exactly). One that names an undeclared output is refused with `ELS3-ACTIVITY-REUSABLE-UNDECLARED-OUTPUT` and guidance.
  Both new conversion codes (this one and FR-013's) live in `Elsa3ActivityDiagnosticCodes` and are emitted by the
  converter, like the code they replace.
- **FR-011**: Studio's graph designer MUST offer Set Variable (with body targets only), Correlate and Set Name, and MUST
  focus their publish diagnostics on the node.
- **FR-012**: The readiness guide, the Elsa 3 mapping README, the converter's guidance strings (`Elsa3ReusableGuidance`,
  and every `Elsa3ExpressionRewriter` text that cites the retired code or #3023, including the script-state guidance that
  says closed reusable bodies cannot change running-workflow state), the graph authoring reference
  (`docs/reference/activity-definition-authoring-composition.md`), `docs/runtime-variable-lifecycle.md` (its "not covered
  yet" notes on intrinsic closed-scope writes and #3023) and the extension-point catalogs MUST describe the new support and drop the #3023 workarounds.
- **FR-013**: The converter MUST refuse a reusable-body Finish with `ELS3-ACTIVITY-REUSABLE-FINISH-UNSUPPORTED`, listed in the
  readiness guide as an Elsa 4 behaviour (not a gap). Its guidance and the readiness guide MUST say: what Elsa 3 did (the
  activity ended the whole calling workflow, cutting off the caller's other branches, and the activity never completed
  or returned its outputs); why Elsa 4 does not (an activity's effect on its caller is visible in the caller's own graph
  through outcomes, so a caller's control flow never ends by something its own graph does not show); the supported approach
  (replace Finish with a Complete that names a new declared outcome, for example `Finished`, and in each caller connect
  that outcome to a Finish); and that moving the Finish into a workflow called by Dispatch or Execute Workflow is not
  equivalent (it would end that called workflow, not the caller).
- **FR-014**: The graph compiler fingerprint (`elsa.activity-graph/compiler/…`) MUST be bumped, since compiled output
  changes. The new fingerprint MUST be added to the compilers `GraphReleaseOccurrenceInspector` accepts, and the Graph
  `EXTENSION_POINTS.md` rows that name the fingerprint updated. Graphs compiled before are republished, not migrated
  (pre-stable).

- **FR-015**: At run time a body Set whose value's combined policy requires encryption or external storage MUST fault
  the step with a clear message and write nothing, as spec 1241's session write to a closed-scope row does. Publish does
  not check it: a graph variable declares no encryption, and the policy is only known at run time.
### Key Entities

- **Body intrinsic node**: a Set, Set Output, Correlate or Set Name node inside a graph body, compiled as a local
  executable node (no publication).
- **Boundary variable row**: the durable value row a closed boundary keeps for each graph variable (spec 1221); the
  target of a body Set.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The `Enrich` example (and spec 236 SC-006) runs end to end: a caller reads an output an imported reusable
  workflow set.
- **SC-002**: `composition-workflow-as-activity-sub.json`, `storage-drivers-variables-sub-workflow.json` and their root
  workflows no longer stop on `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED` or `ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY`
  (any later refusal is a different, tracked gap).
- **SC-003**: Every acceptance scenario above has an xunit test (vitest for Studio), and the existing graph, intrinsic
  and converter suites stay green.
- **SC-004**: No reusable body Set changes a variable outside its nearest body in any test.

## Assumptions

- Spec 1241's closed-scope row write path (versioning, history, commit order) is reused, not duplicated.
- An Elsa 3 reusable's variables convert to graph variables (specs 264/265), so its Set Variable targets the graph's
  variables, addressed inside the body as scope `workflow`.
- Reading the correlation ID in a body expression (`getCorrelationId()`, used by the storage-drivers sub) already works
  under spec 1234's read rules; if not, it is a separate gap filed against the ledger, not fixed here.
- A storage driver an Elsa 3 variable names (the storage-drivers sub) is handled by #5683/#5698; this spec does not
  change it.

## Decisions (coordinator, 2026-11-26)

1. **Q1 – Correlate and Set Name in a body act on the caller: yes.** They change the running workflow's (the caller's)
   correlation ID and instance name, as in Elsa 3. Real Elsa 3 workflows correlate inside reusable activities with a
   Correlate node; they import unchanged, and the per-caller workaround goes away.
2. **Q2 – Finish stays refused, as an Elsa 4 behaviour: yes.** Ending the whole caller from inside an activity is not a
   desirable behaviour. The import refuses it with its own code and guidance that is very clear about why and what to do
   (FR-013).
3. **Q3 – Converter Set Output becomes the graph's native Set Output: yes.** A reusable Set Output converts to the graph
   Set Output intrinsic; the synthesized `output.{hash}` variable and output mapping that `Elsa3ReusableOutputWriteRewrite`
   adds today are removed. Pre-stable, no shim.
