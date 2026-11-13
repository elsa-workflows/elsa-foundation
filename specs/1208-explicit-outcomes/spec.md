# Feature Specification: Explicit outcomes

**Feature Branch**: `agent/claude/3052-s3-explicit-outcomes`

**Created**: 2026-11-04

**Status**: Draft — the ADR it applies is agreed and merged; approval waits on the approvers' agreement to this text, including the script form (Q1).

**Initiative**: part of [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052), spec row **S-3**,
decision **D-4**.

**Issues**: [#3285](https://github.com/elsa-workflows/elsa-foundation/issues/3285) (reframed by D-4: several outcomes
per completion instead of a one-outcome invariant), [#3295](https://github.com/elsa-workflows/elsa-foundation/issues/3295)
(fixed at its cause: `Done` → `Done` once routing by return value goes), and Elsa 3 Switch "match any" (no issue; refused
today with `ELS3-ACTIVITY-UNSUPPORTED-MODE`).

**Input**: An activity chooses its outcomes explicitly, and may choose several per completion. No choice means `Done`,
an explicitly empty choice means no outgoing path, and the choice takes effect only when the activity completes
successfully. A node's possible outcomes are known at design time and pinned when the workflow is published.

## Approval

**Which ADR text this spec applies.** The outcome contract amendment of ADR 0045 §6 and spec 095, as accepted on
2026-11-12 and merged into the initiative branch (`70399f75f`, with the review refinements of #3763 in `a5d22a454`).
Where it does not touch ADR 0045 or spec 095, the initiative branch's version applies.

| ADR PR | What this spec takes from it | Text applied | State |
|---|---|---|---|
| [#3389](https://github.com/elsa-workflows/elsa-foundation/pull/3389), ADR 0045 §6 and spec 095 outcome contract (D-4) | The outcome selection (unspecified, names, explicitly empty), the `Done` rule, selection on successful completion only, design-time outcome sets and the multi-outcome flag pinned per node, two-layer name validation, `VF-ACT-006`, connection ports checked at publication, unconnected outcomes, multi-outcome completion as an inclusive fork, the end of routing by return value and of `Unmatched`, the converter's `Done` mapping | merged `70399f75f`, `a5d22a454` | **Accepted 2026-11-12 (D-4; D-14 moved to #4193, so the default join only) and merged.** History: amended on 4 Nov (`18cccb00b`) with the `Done` rule (Q2), @sfmskywalker's non-API points from [#3763](https://github.com/elsa-workflows/elsa-foundation/pull/3763) (Q5) and this spec's findings (Q3, Q4, Q9, Q10); **reframed on 5 Nov (`9c2cc3894`)** around a returned completion/routing form after @sfmskywalker's [direction](https://github.com/elsa-workflows/elsa-foundation/pull/3389#issuecomment-6642477464), with the Q4 join and Q6 fallback refinements. The script API is now resolved (Q1: the returned form, no native setters). @FransVanEk approved the earlier text (30 Oct, `c9dcb326b`) and must re-approve the reframe; @j03y-nxxbz agrees |

**#3763** (@sfmskywalker's stacked proposal on #3389): points 2–6 are adopted in `18cccb00b` (Q5); point 1, the API, is
Q1. Its remaining refinements are merged in `a5d22a454` and applied here (FR-007a, FR-027).

**Related, not blocking:**
- [#3286](https://github.com/elsa-workflows/elsa-foundation/pull/3286) (spec 266, ADR 0048, decision D-2a) reads
  "the node's activity contract allows several outcomes per completion" to treat a node as an inclusive fork. This spec
  provides that declaration (FR-014); spec 266 consumes it. Neither waits for the other: no built-in activity declares
  it today.
- [#3425](https://github.com/elsa-workflows/elsa-foundation/pull/3425) and ADR 0092
  ([#3411](https://github.com/elsa-workflows/elsa-foundation/pull/3411)) define the step transaction (D-3) that a
  selection commits with. This spec only requires that the selection is part of the completion.

**Approval plan**: the initiative's approvers, @sfmskywalker and @FransVanEk (technical architects), approve the final
text; @j03y-nxxbz is the author and records agreement in a PR comment. **Implementation**: on the initiative branch
once this spec is approved (D-18, 13 Nov); it lands on `main` with the initiative.

## Rule and boundary

**Rule**: an activity says which outcomes it took; Elsa never guesses them from the data the activity returns.

**Terms** (from the #3389 draft):
- *Outcome selection*: what a successful completion carries. It is one of three states: *unspecified* (the activity's
  default outcome, `Done`), *named* (one or more declared outcome names) or *explicitly empty* (no outgoing path).
- *Outcome set* of a node: the outcomes the node may select, derived at design time from the activity's static
  outcomes and from design-time providers over the node's configuration, and pinned in the published executable.
- *Multi-outcome completion*: a completion whose selection names more than one outcome.
- *Script outcome API*: how a script makes a selection — the explicitly recognized **returned completion/routing
  form** (Q1, resolved on #3389), carrying the result and the selection on separate channels, with no native setters.
  Its spelling, settled here (FR-007): the value of Elsa's built-in pure constructor `complete(result, outcomes)`.

**In scope**: the outcome selection in the activity transition, the durable completion records and recovery; the
script outcome API in RunJavaScript (form per Q1); removal of RunJavaScript's routing by return value and of its
`Unmatched` outcome; the outcome set and its multi-outcome flag, derived at design time and pinned per node; name
validation at publish and at run time; Flowchart scheduling, joins and inclusive-fork treatment for multi-outcome
completions; Studio ports; the Elsa 3 converter (`Done` → `Done`, `setOutcome` / `setOutcomes`, Switch "match any",
Complete with several outcomes); the documents that state "one outcome per completion".

**Out of scope**:
- The setters-vs-returned-form decision (Q1, reconciliation row 14): settled on #3389 (the returned completion form,
  no native setters). This spec settles that form's spelling, `complete(result, outcomes)` (in scope, FR-007).
- Spec 266's unordered-write warning itself (#3286). It reads the multi-outcome flag this spec pins.
- The other half of `ELS3-BEHAVIOUR-HTTP-OUTCOME-SINGLE`: without expected status codes, a non-2xx response goes to
  `Failed` in Elsa 4. It is not about how many outcomes fire (Q8).
- The step transaction (D-3; #3425, ADR 0092).
- Related, not a dependency: [#3809](https://github.com/elsa-workflows/elsa-foundation/issues/3809), a general Elsa 4 defect. A Flowchart connection on a port its source node
  does not declare publishes and never fires (research R-8). If it is still open when S-3 is built, FR-019 fixes it.
  If it is fixed first, S-3 builds on that check.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A script chooses its outcomes explicitly (Priority: P1)

A workflow author writes a RunJavaScript that sends an order down `Approved`, `Rejected`, both `Approved` and `Audit`,
or nowhere. The author selects by returning the recognized completion/routing form. Ordinary returned business data is
the activity's result and plays no part in the routing.

**Why this priority**: this is D-4. Today RunJavaScript routes by the string or number it returns and falls back to
`Unmatched`, so returning data can change the route by accident (#1114, research R-1).

**Independent Test**: publish a workflow with a RunJavaScript whose outcome set is `Approved`, `Rejected`, `Audit`,
with a connection on each; run it once per selection below and check which successors run.

**Acceptance Scenarios**:

1. **Given** a script that selects `Approved`, **When** it completes, **Then** only the `Approved` successor runs.
2. **Given** a script that selects nothing, **When** it completes, **Then** the activity completes with `Done`, and a
   connection on `Done` is followed.
3. **Given** a script that selects `Approved` and `Audit` on a node whose outcome set allows several, **When** it
   completes, **Then** both successors run.
4. **Given** a script that makes an explicitly empty selection, **When** it completes, **Then** no outgoing connection
   is followed, the activity's result and the step's other changes still commit, and other running branches go on.
5. **Given** a script that returns the string `"Approved"` and selects nothing, **When** it completes, **Then** the
   result is `"Approved"` and the activity completes with `Done`.
6. **Given** a script that selects `Approved` and then faults, **When** the step ends, **Then** no successor runs and
   the selection is discarded.
7. **Given** a script that returns the string literal `'Approved'` on a node with a connection on `Approved`, **When**
   the workflow is published, **Then** publication warns that return values are data and that routing needs the
   returned completion form, naming the node and the outcome; it still publishes (FR-008a).

---

### User Story 2 - Converted RunJavaScript keeps its routing (Priority: P1)

An operator imports Elsa 3 workflows in which a RunJavaScript connects to the next activity on `Done`. After
conversion the next activity runs, as it did in Elsa 3.

**Why this priority**: #3295. The converter moves every such connection to `Unmatched`, a port the converted node never
completes with when it has no possible outcomes, so the next activity never runs (research R-7). The spec 236 member
map counts these nodes in the corpus.

**Independent Test**: convert, publish and run an Elsa 3 workflow with RunJavaScript → `Done` → WriteLine; the
WriteLine runs. Repeat with `PossibleOutcomes` set and the script selecting nothing.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 RunJavaScript without possible outcomes, connected on `Done`, **When** it is converted,
   published and run, **Then** the connection is on `Done` and is followed.
2. **Given** an Elsa 3 RunJavaScript with possible outcomes, connected on `Done`, whose script selects nothing,
   **When** it is converted, published and run, **Then** the `Done` connection is followed.
3. **Given** the corpus, **When** it is converted, **Then** no RunJavaScript connection is mapped to `Unmatched`.
4. **Given** an Elsa 3 SendHttpRequest with expected status codes, connected on `Done`, **When** it is converted,
   published and run against each expected code and an unexpected one, **Then** the `Done` connection's target runs
   every time (Q8).

---

### User Story 3 - An unknown outcome name is caught (Priority: P1)

An author mistypes an outcome name. A literal name is reported when the workflow is published; a name computed at run
time faults the step with `VF-ACT-006`.

**Why this priority**: an unknown name today becomes a silent `Unmatched`; under D-4 it must never silently route.

**Independent Test**: publish a script that selects the literal `Aproved` on a node whose set is `Approved`, `Rejected`;
publication reports it. Publish one that selects a name read from a variable, set the variable to `Aproved`, and run;
the step faults with `VF-ACT-006`.

**Acceptance Scenarios**:

1. **Given** a literal outcome name outside the node's outcome set, **When** the workflow is published, **Then**
   publication reports a diagnostic that names the node, the name and the node's outcome set.
2. **Given** a computed outcome name outside the node's pinned set, **When** the step completes, **Then** it faults
   with `VF-ACT-006` as a fault code, and no successor runs.
3. **Given** an activity whose contract maps unfamiliar business data to a declared catch-all (SendHttpRequest's
   unmatched status code, CreateUserTask's `Unmatched`, or a script that deliberately selects its declared `Review`),
   **When** the data is unfamiliar, **Then** the activity selects that catch-all. **Given** any node, **When** a
   computed outcome name is outside the pinned set, **Then** the step faults with `VF-ACT-006`; no node setting turns
   it into another outcome (Q6).
4. **Given** a selection of several names on a node whose outcome set does not allow several, **When** the step
   completes, **Then** it faults with `VF-ACT-006`.
5. **Given** a node whose outcome set has no `Done` and whose activity cannot guarantee a selection on every
   successful completion, **When** the workflow is published, **Then** publication reports it, naming the node and
   saying to declare `Done` or select on every path (FR-002b).

---

### User Story 4 - Several outcomes run as an inclusive fork (Priority: P2)

An author lets one activity take several outcomes, and joins the paths again later. The engine schedules every selected
outcome's connections, the join waits for exactly the paths that were taken, and the analysis that reports unordered
writes treats the node as an inclusive fork.

**Why this priority**: the runtime already schedules several outcome names (research R-3); what is missing is the
declaration that makes the fork visible to joins, analysis and inspection.

**Independent Test**: a node selects `A` and `B` of `A`, `B`, `C`; each path reaches one join. The join runs once,
after `A` and `B`; `C`'s path is dead.

**Acceptance Scenarios**:

1. **Given** a node that selects `A` and `B`, **When** it completes, **Then** both connections run and `C`'s connection
   is routed dead, so a join downstream waits only for `A` and `B` (ADR 0064).
2. **Given** a node whose outcome set allows several, **When** its outcome set is read by the unordered-write analysis,
   inspection or Studio, **Then** the node is marked as allowing several outcomes.
3. **Given** two selected outcomes whose connections lead to the same node, **When** the node completes, **Then** the
   engine does not fault, and the target runs as Q4 decides.
4. **Given** a selection of several outcomes, **When** the instance is inspected or recovered after a restart,
   **Then** every selected name is shown and kept, in order.

---

### User Story 5 - Elsa 3 multi-outcome constructs convert (Priority: P2)

An operator imports Elsa 3 workflows that use `setOutcome` / `setOutcomes`, a Switch in "match any" mode, or a Complete
with several outcomes. They convert and run with the same routing, or are refused with guidance.

**Why this priority**: these are refused today only because Elsa 4 completed with one outcome (research R-7).

**Independent Test**: convert, publish and run one fixture per construct; each takes the same paths as in Elsa 3.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 script that calls `setOutcome('Approved')`, **When** it is converted, **Then** the converted
   script returns `complete(<its result>, 'Approved')`, and `Approved` is in the node's outcome set.
2. **Given** an Elsa 3 script that calls `setOutcome` with a computed name and `PossibleOutcomes` is empty, **When**
   it is converted, **Then** it refuses with guidance that names the node and how to declare the outcomes (Q7).
3. **Given** an Elsa 3 Switch in "match any" mode, **When** it is converted and run, **Then** every matching case's
   connection is followed.
4. **Given** an Elsa 3 Complete with several outcomes, **When** it is converted and run, **Then** the parent sees all
   of them.
5. **Given** an Elsa 3 script that calls `setOutcome` with a computed name and a non-empty `PossibleOutcomes`, **When**
   it is converted and run with a name inside the known set, **Then** that outcome's connections run; **When** run
   with a name outside it, **Then** the converted script selects the converter-declared catch-all, which has no
   connection of its own, so the path ends without a fault, as in Elsa 3, and Elsa 3 `Done` behaviour is kept
   (FR-027). No warning stands in for this.

---

### User Story 6 - Ports are known at design time (Priority: P3)

An author configures a RunJavaScript's possible outcomes or a Switch's mode and sees the ports immediately. Publication
pins exactly those ports, and a connection on a port the node does not declare is refused.

**Why this priority**: the pinned set is what both validation layers check against.

**Independent Test**: add `Approved` to a node's possible outcomes; Studio shows `Done` and `Approved`, without
`Unmatched`. Remove `Approved` while it is connected; Studio flags the connection, and publication refuses it.

**Acceptance Scenarios**:

1. **Given** a RunJavaScript with possible outcomes `Approved` and `Rejected`, **When** Studio shows it and the
   workflow is published, **Then** both show and pin the set `Done`, `Approved`, `Rejected`.
2. **Given** a connection on a port outside its source node's outcome set, **When** the workflow is published, **Then**
   publication refuses it with a diagnostic that names the connection and the port.
3. **Given** a change to provider-relevant configuration, **When** the ports change, **Then** Studio shows the
   connections on ports that disappeared, so they can be repaired.

### Edge Cases

- A selection that names the same outcome twice: it is one outcome.
- An explicitly empty selection on a node with no outgoing connections: the path ends, as with any other outcome.
- An explicitly empty selection in a parent that does not route by outcomes, such as Sequence or the workflow root:
  see Q3.
- A selected outcome that is declared but unconnected: valid; that path ends.
- A suspended activity: a selection made before a suspension is not carried over. Only the completing attempt's
  selection counts.
- A retry: a new attempt starts with an unspecified selection.
- A background activity: the selection travels in the background completion receipt and is validated there too.
- An outcome name with surrounding whitespace in the possible outcomes: names are compared exactly; see research R-8.
- A computed possible-outcomes binding: publication refuses it, as today. Outcome sets are pinned at design time.

## Requirements *(mandatory)*

### Functional Requirements

**Outcome selection**

- **FR-001**: A successful completion MUST carry an outcome selection in one of three states: unspecified, named (one
  or more declared names, each once, in the order they were selected) or explicitly empty.
- **FR-002**: `Done` MUST mean "completed without choosing a branch" (Q2). An unspecified selection MUST mean `Done`.
  An activity whose completion may choose no branch MUST have `Done` in its outcome set; an activity whose every
  completion chooses a branch MUST NOT have `Done` in it. `Done` MUST NOT be selected together with a branch.
- **FR-002a**: An unspecified selection on a node whose outcome set has no `Done` MUST fault with `VF-ACT-006`, and the
  fault MUST say that the activity completed without selecting an outcome and declares no `Done`, so it points at the
  activity's code.
- **FR-002b**: Publication MUST check every node whose pinned outcome set has no `Done`: its activity's contract MUST
  guarantee a selection on every successful completion (an activity type declares the guarantee; for configurable
  activities it is derived from the node's configuration). A node whose activity cannot guarantee it MUST be reported
  at publication (`VF-AUTH-013`), naming the node and saying to declare `Done` or select on every path. The run-time
  fault of FR-002a stays as the backstop where the guarantee cannot be checked statically. The built-in activities
  without `Done` (If, Switch, SendHttpRequest with expected codes) get a conformance test that every completion
  selects (@FransVanEk, #3389 point 3).
- **FR-003**: Unspecified and explicitly empty MUST stay distinct through the activity transition, the completion
  record, parent and join handling, background completion, persistence, recovery and inspection.
- **FR-004**: An explicitly empty selection MUST follow no outgoing connection, MUST still commit the activity's result
  and the step's other changes, and MUST NOT cancel other running branches.
- **FR-005**: A selection MUST take effect only when the activity completes successfully, as part of its completion. A
  fault or cancellation MUST dispatch nothing, and nothing is dispatched while the activity is still running.
- **FR-006**: C# activities MUST be able to return each of the three states in their completion transition.

**Scripts**

- **FR-007**: A RunJavaScript script MUST select outcomes by **returning the value of Elsa's built-in pure
  constructor `complete(result, outcomes)`** (Q1). It carries the result and the selection on separate channels and
  needs no ambient `activity` object:
  - `return complete(order, 'Approved')` selects one outcome;
  - `return complete(order, ['Approved', 'Notify'])` selects several;
  - `return complete(order, [])` is an explicitly empty selection;
  - `return complete(order)` (outcomes omitted or `undefined`) leaves the selection unspecified, as `return order` does.

  `result` is the activity's result and is materialized like any returned value (ADR 0046). The script MUST complete
  with an unspecified selection when it returns ordinary data.

  The RunJavaScript body keeps its function-body contract (ADR 0083): it returns with a top-level `return`, and a
  final expression without `return` is not a returned value. A script that ends without `return` has no result and
  an unspecified selection.
- **FR-007a**: *Recognition.* `complete` is pure: calling it changes nothing, and only returning its value selects.
  - The value carries a brand held by the evaluator that script data cannot forge. A plain object of the same shape
    (`{ result, outcomes }`) is ordinary data (FR-008).
  - It is recognized by its brand on the script's own top-level returned value, wherever the value was created: `return
    f()`, where `f` returns `complete(...)`, selects. A completion value nested inside returned data, or stored as a
    result, faults the step with `VF-ACT-006`.
  - `complete` is installed for script activities only, not for input-binding expressions (they select no outcome).
    It is a reserved built-in name in every JavaScript surface (ADR 0045 §5, D-5), including input-binding expressions
    where it is not installed: a script or binding that declares `complete` gets the reserved-name publish
    diagnostic.
- **FR-007b**: *Validation.* `outcomes` MUST be omitted, `undefined` (both unspecified), a string or an array of
  strings. Anything else (`null`, a number, an object, a blank name) faults the step with `VF-ACT-006`; `null` is
  refused on purpose, so a missing selection is never confused with an empty one. An `undefined` `result`
  (`complete()`, `complete(undefined, 'A')`) follows the ADR 0045 §2 boundary rule exactly as a plain
  `return undefined` does: an error by default, `null` under the undefined-as-null policy. A name repeated in the array counts once, in the order of
  its first occurrence. String literals passed as `outcomes` are checked at publication (FR-015); computed names are
  checked at run time (FR-016, FR-017).
- **FR-008**: A script's ordinary returned value (a string, number, object, array or null) MUST be the activity's
  result only. Its content MUST NOT select an outcome.
- **FR-008a**: Publication MUST warn (`VF-AUTH-012`, a warning, not a refusal) when a RunJavaScript script returns a
  string literal equal to an outcome name connected on that node. The diagnostic MUST name the node and the outcome
  and say that return values are data and that routing needs the returned completion form (FR-007), so an existing
  workflow that relied on routing by return value (#1114) is not changed silently (@FransVanEk, #3389).
- **FR-009**: RunJavaScript's routing by return value (#1114) and its `Unmatched` outcome MUST be removed: from the
  activity, its contract and its outcome set, from Studio's ports, and from the activity surface baseline.

**Outcome sets**

- **FR-010**: Each node's outcome set MUST be resolved at design time from the activity's static outcomes and the
  design-time providers over the node's configuration. Studio and publication MUST use the same provider rules.
- **FR-011**: Publication MUST pin each node's outcome set in the executable. No outcome name is added at run time.
- **FR-012**: RunJavaScript's outcome set MUST be `Done` plus its authored possible outcomes, without `Unmatched` (Q2).
- **FR-012a**: A SendHttpRequest with expected status codes MUST NOT have `Done` in its outcome set: every response
  selects a code or the unmatched status code (Q8). Without expected codes its set is unchanged.
- **FR-013**: When provider-relevant configuration changes, Studio MUST refresh the node's ports and show the
  connections on ports that are gone.
- **FR-014**: Each node's outcome set MUST record whether a completion may select several outcomes, pinned with the
  set (Q10). RunJavaScript allows several; a Switch allows several only in "match any" mode.

**Validation**

- **FR-015**: A literal outcome name in a script that is not in the node's outcome set MUST be a publication
  diagnostic that names the node, the name and the set. A string literal, or an array of string literals, passed as
  the second argument of a `complete(...)` call counts as literal, wherever the call appears (including nested
  functions). An aliased `complete` or a computed name is checked at run time (FR-016).
- **FR-016**: A selected name outside the node's pinned declared set MUST fault the step with `VF-ACT-006`, carried as
  the fault code (Q6). No node-level setting turns an invalid outcome name into another outcome.
- **FR-017**: A selection of several names on a node whose set does not allow several MUST fault the step with
  `VF-ACT-006`.
- **FR-018**: S-3 MUST NOT add a generic node-level fallback; generic per-node fallbacks are deferred until a concrete
  use case justifies them (Q6). An activity or a script MAY deliberately map unfamiliar business data to an ordinary
  declared catch-all outcome in its contract (SendHttpRequest's unmatched status code, CreateUserTask's `Unmatched`, a
  script's declared `Review`). That is explicit routing: the catch-all MUST be in the node's pinned set, and it MUST NOT
  excuse an invalid outcome name or an undeclared result projection, which remain errors.
- **FR-019**: Publication MUST refuse a Flowchart connection whose source port is not in its source node's pinned
  outcome set (Q9).
- **FR-020**: A declared outcome with no connection MUST be valid; selecting it ends that path.

**Scheduling and joins**

- **FR-021**: The connections of every selected outcome MUST be scheduled; the connections of every other outcome MUST
  be routed dead (ADR 0064).
- **FR-022**: Two selected outcomes whose connections lead to the same node MUST NOT fault the engine. By default the
  target's synchronizing implicit join runs it once after both arrivals (two arrivals, one run). Loop re-entry and
  independent activations stay distinct executions (Q4). This spec defines no per-arrival join mode; join behaviour as
  a whole is designed in its own initiative.
- **FR-023**: Path analysis, joins, inspection and the unordered-write analysis MUST treat a node whose set allows
  several outcomes as an inclusive fork.
- **FR-024**: A parent that routes by outcomes MUST receive the whole selection. What a parent that does not route by
  outcomes does with it follows Q3.

**Persistence**

- **FR-025**: The completion record, background completion receipts and recovery MUST keep the selection's state and
  every selected name, and the completion document's version MUST go up (Q11). A supported legacy record with one
  recorded outcome MUST read as `Named([its recorded key])`; no unspecified or empty history that was never recorded is
  invented. Before 4.0 the recorded reset-and-republish policy applies, so no in-place data migration is needed; any
  backward-reader path that is claimed MUST have a fixed legacy-record fixture. S-9's writer-compatibility rule does
  not waive the version for this changed shape and meaning.

**Elsa 3 conversion**

- **FR-026**: The converter MUST map Elsa 3 RunJavaScript `Done` to Elsa 4 `Done`, whether or not possible outcomes
  are set. Spec 239's R8 rule (`Done` → `Unmatched`) MUST be removed. This fixes #3295.
- **FR-027**: The converter MUST convert Elsa 3 `setOutcome` / `setOutcomes` to the returned form
  `complete(result, outcomes)` (FR-007): each setter call replaces the pending selection, and the script returns
  `complete(<its returned value>, <pending selection>)` — Elsa 3 setters are conversion inputs, never native Elsa 4 host functions — and MUST **preserve the
  original routing or refuse with guidance** (Q7). A warning does not make changed routing faithful, so it MUST NOT be
  used to convert a case whose routing would change.
  - `PossibleOutcomes`, the literal names in the script and the connected ports are evidence for the converted
    outcome set, not proof that every computed name is covered.
  - **Faithful route for a computed name**: the converter declares an explicit catch-all outcome on the converted
    node, in its pinned set and with no connection of its own, and the converted script selects it for any name
    outside the known set. That path then ends without a fault, as an unknown name did in Elsa 3 (FR-020). Where Elsa
    3's always-firing `Done` is connected, it maps onto the catch-all as onto every other branch (FR-029a), so `Done`
    behaviour is kept. This is ordinary explicit routing in the converted script, not a generic fallback (FR-018).
  - A case with no faithful route is refused with guidance; refusal stays for a computed name with an empty
    `PossibleOutcomes`.
  - A behaviour-changing conversion exception needs a separate, explicit, approved and documented decision; S-3 adds
    none.
- **FR-028**: The converter MUST convert an Elsa 3 Switch in "match any" mode to an Elsa 4 Switch in "match any" mode.
- **FR-029**: The converter MUST convert an Elsa 3 Complete with several literal outcomes to a completion that selects
  all of them. An outcome given by an expression stays refused.
- **FR-029a**: The converter MUST map Elsa 3's always-firing `Done` onto every branch outcome of the converted node,
  as it already does for If, Switch and DispatchWorkflow. For SendHttpRequest with expected status codes, `Done` maps to
  every code and the unmatched status code; whether `Failed` and `Timeout` are included follows the Elsa 3 source
  (research R-7). `ELS3-BEHAVIOUR-HTTP-OUTCOME-SINGLE` then covers only the `Failed` difference.
- **FR-030**: Behaviour MUST be proven by conversion, publication and execution of fixtures, not by conversion alone.

**Switch and Complete**

- **FR-031**: The Elsa 4 Switch MUST offer a "match any" mode that selects every matching case, and `Default` when none
  matches. "Match first" stays the default mode.
- **FR-032**: The Graph `Complete` activity, the Finish intrinsic and the graph boundary MUST carry a selection of
  several outcomes where the graph declares it, instead of requiring exactly one mapped outcome.

**Documents**

- **FR-033**: Every document that states "one outcome per completion" or routing by return value MUST be updated in
  this spec's delivery (research R-10), and spec 247 FR-019's refusal of `setOutcome` MUST be replaced by FR-027.

### Key Entities

- **Outcome selection**: state (unspecified, named, empty) and the ordered, distinct names. Part of a successful
  completion.
- **Outcome set**: per node, the declared outcome names plus the multi-outcome flag, derived at design time and pinned
  at publication.
- **Activity-declared catch-all**: an ordinary declared outcome that an activity or script deliberately selects for
  unfamiliar business data (Q6); never a repair for an invalid outcome name.

## Open questions for the approvers

ADR 0045 §6 (#3389) leaves the API, the record shape, the multi-outcome declaration and the fallback to this spec. The
code survey added Q2, Q3, Q4, Q8, Q9 and Q10. Where an answer is ADR text (Q2, Q3, Q4, Q9, Q10) or adopts
@sfmskywalker's #3763 (Q5), it is applied to #3389 in `18cccb00b`, because the code showed the draft was incomplete
there; the `Done` rule (Q2) is a decision for #3389, not for this spec. The requirements follow @j03y-nxxbz's answers. If #3389 decides
otherwise, this spec follows #3389. All three approvers answer on the PR, and its reconciliation table records the
answers.

| # | Question | Proposal | @j03y-nxxbz |
|---|---|---|---|
| Q1 | **The script outcome API** (reconciliation row 14). Setters only (`setOutcome` / `setOutcomes`), or an explicitly recognized returned completion/routing value that carries the result and the selection separately? | **Resolved (4–5 Nov): the returned completion/routing form, no native setters.** @sfmskywalker [directed](https://github.com/elsa-workflows/elsa-foundation/pull/3389#issuecomment-6642477464) reframing around an explicitly recognized returned form — result and selection on separate channels, no ambient `activity` object; the native script surface gains no `setOutcome` / `setOutcomes`. The ADR §6 reframe (#3389 `9c2cc3894`) applies this and @j03y-nxxbz agrees; @FransVanEk's earlier "setters only" must be re-confirmed against the reframe. **Spelling (13 Nov): a pure constructor, `return complete(result, outcomes)`** (FR-007 to FR-007b): a returned data representation was rejected because it would have to recognize control instructions in a plain object, which ordinary business data can imitate. Ordinary business data still selects nothing (FR-008). | The returned completion form (row 14, reframed); "setters only" is withdrawn |
| Q2 | **The `Done` rule.** #3389 says an unspecified selection means `Done`, but not which activities have `Done`. Elsa 4 declares `Done` on activities that do not branch (WriteLine, Delay, Sequence, Flowchart) and not on activities that always branch (If: `True`, `False`; Switch: cases, `Default`). A RunJavaScript with possible outcomes pins no `Done` (research R-4). Elsa 3 instead fires `Done` on every completion, next to any branch. | **Model A, Elsa 4's shape:** `Done` means "completed without choosing a branch" and never fires with a branch. An activity whose completion may choose no branch has `Done` (RunJavaScript: `Done` plus its possible outcomes); one whose every completion chooses a branch has none. An unspecified selection without `Done` is a defect in the activity's code and faults saying so (FR-002a). Elsa 3's always-firing `Done` converts by mapping onto every branch, as the converter already does for If, Switch and DispatchWorkflow (research R-7). Rejected, model B: `Done` on every activity, firing with any branch, as in Elsa 3; it would make every node an inclusive fork. Applied to #3389 in `18cccb00b`. | Model A. Elsa 4 is the better source of truth |
| Q3 | What does a parent that does not route by outcomes (Sequence, the workflow root, a loop body) do with an explicitly empty selection? | It treats it like any other successful completion: a Sequence goes on, the root completes. Only parents that route by outcomes (Flowchart, the graph boundary) read the selection. Applied to #3389 in `18cccb00b`. | Agrees |
| Q4 | Two selected outcomes whose connections lead to the same node. The engine faults today on duplicate targets. | Each taken connection is an arrival at the target. **By default** the target's synchronizing implicit join (ADR 0064, dead-path join) accounts for one activation's arrivals and runs it once, after both arrivals (two arrivals, one run). Loop re-entry and independent activations stay distinct executions (@sfmskywalker, [card 17](https://github.com/elsa-workflows/elsa-foundation/pull/3389#issuecomment-6645085675)). Applied to #3389 in `9c2cc3894`. **The per-arrival mode is out (D-14, meeting of 2026-11-12):** join behaviour moves to its own initiative, and #3389 keeps the default join only (`fae896808`). | Agrees |
| Q5 | Adopt @sfmskywalker's non-API amendments in #3763 into #3389? They are: an empty selection does not cancel other branches; the runtime rejects several names when not allowed; the fallback is in the pinned set and never excuses a result projection; the `VF-ACT-006` wording; computed-name derivation left to S-3; `Done` → `Done` proven by execution. | Adopt all six into #3389 (applied in `18cccb00b`). They fit D-4 and are in FR-004, FR-017, FR-018, FR-016, FR-027 and FR-030. | Agrees: adopt all six |
| Q6 | The fallback outcome: who may configure one? | **Fault on unknown names; activity-defined catch-alls only; generic per-node fallbacks deferred** (FR-016, FR-018). An activity or script may deliberately map unfamiliar business data to a declared catch-all (SendHttpRequest's unmatched status code, CreateUserTask's `Unmatched`, a script's `Review`); no node setting repairs an invalid name. | Accepts @sfmskywalker's answer ([card 18](https://github.com/elsa-workflows/elsa-foundation/pull/3389#issuecomment-6645186613)) |
| Q7 | Elsa 3 computed `setOutcome(x)`: how is it converted? | **Preserve or refuse; a warning is not faithful** (FR-027). `PossibleOutcomes`, literal names and connected ports are evidence, not proof. Faithful route: a converter-declared catch-all with no connection, selected for names outside the known set, so the path ends as in Elsa 3, with `Done` mapped onto it like every branch. Refuse when no faithful route exists, and when the name is computed and `PossibleOutcomes` is empty. Behaviour-changing exceptions need a separate approved decision. The corpus has no `setOutcome` call (spec 247). | Accepts @sfmskywalker's ([card 19](https://github.com/elsa-workflows/elsa-foundation/pull/3389#issuecomment-6645285238)) and @FransVanEk's ([condition](https://github.com/elsa-workflows/elsa-foundation/pull/3810#issuecomment-6765435979)) rule; replaces "convert and warn" |
| Q8 | Elsa 3 SendHttpRequest fires `Done` next to the status-code outcome. Elsa 4 with expected codes completes with a code or the unmatched status code, never `Done`, yet declares `Done` statically, so its `Done` port is dead; the converter maps `Done` → `Done` and only warns (`ELS3-BEHAVIOUR-HTTP-OUTCOME-SINGLE`), so the path is lost. | Follows Q2 (model A), without multi-outcome: with expected codes, SendHttpRequest has no `Done` (FR-012a), and the converter maps Elsa 3 `Done` onto every response outcome (FR-029a). Whether Elsa 3's `Done` also fired on "Failed to connect" and "Timeout" is checked in the Elsa 3 source. | Agrees, with Q2 |
| Q9 | Publication does not check connection ports today. A connection on an undeclared port publishes and never fires. | Publication refuses it (FR-019). The general defect gets its own issue; S-3 fixes it if it is still open. Applied to #3389 in `18cccb00b`. | Agrees |
| Q10 | #3389 says the activity contract declares multi-outcome completion. A Switch allows several only in "match any" mode, which is per node. | The flag is per node, derived at design time and pinned with the outcome set; an activity type provides it statically or through a provider. Applied to #3389 in `18cccb00b`. | Agrees |
| Q11 | The persisted shape of the selection. | The completion record stores the state and the names; its document version goes up. A legacy one-outcome record reads as `Named([its recorded key])`; no unrecorded empty or unspecified history is invented. No in-place data migration before 4.0 (reset-and-republish policy); a claimed backward-reader path needs a fixed legacy-record fixture. S-9's writer-compatibility rule does not waive the version (FR-025). | Accepts @sfmskywalker's delegated answer ([comment](https://github.com/elsa-workflows/elsa-foundation/pull/3810#issuecomment-6646547680)) |

**Answers recorded on this PR and #3389** (accepted by @j03y-nxxbz, applied 2026-11-11): @sfmskywalker Q2
([agreed](https://github.com/elsa-workflows/elsa-foundation/pull/3810#issuecomment-6644734617)), Q4 (card 17, already
applied in FR-022; the per-arrival part was taken out after the 2026-11-12 meeting), Q6 (card 18), Q7 (card 19), Q11
(delegated); @FransVanEk Q7 ([condition](https://github.com/elsa-workflows/elsa-foundation/pull/3810#issuecomment-6765435979))
and, from his [#3389 re-review](https://github.com/elsa-workflows/elsa-foundation/pull/3389#pullrequestreview-5842388196),
the publish warning for a returned string that equals a connected outcome (FR-008a) and the publish-time `Done` check
(FR-002b).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A RunJavaScript's route never depends on what it returns: two scripts with the same selection and
  different return values take the same paths, in every fixture.
- **SC-002**: Every converted RunJavaScript connected on `Done` in the corpus is followed when run (#3295).
- **SC-003**: Every literal unknown outcome name in the fixtures is reported at publication; every computed unknown
  name faults with `VF-ACT-006`.
- **SC-004**: A selection of several outcomes reaches a downstream join exactly once, after every taken path.
- **SC-005**: Unspecified, named and empty selections survive a restart unchanged.
- **SC-006**: Zero converter refusals remain for Elsa 3 `setOutcome` / `setOutcomes` with a non-empty
  `PossibleOutcomes` or literal names, Switch "match any", and Complete with several literal outcomes; every converted
  computed selection runs the same paths as in Elsa 3, including names outside the known set.
- **SC-007**: Every fixture script that returns a string literal equal to a connected outcome name gets the FR-008a
  warning at publication.

## Assumptions

- S-3's convert-publish-run evidence (US2, US5, FR-030) needs converted RunJavaScript to run at all. Today it does
  not: [#3195](https://github.com/elsa-workflows/elsa-foundation/issues/3195) (no arguments: faults at run time) and
  [#3287](https://github.com/elsa-workflows/elsa-foundation/issues/3287) (reads a variable or input: refused at
  publication) must be fixed first, #3287 for the fixtures that read variables (research R-7). The requirements do
  not depend on them.
- Elsa 4 has no released runtime store, so the completion record's shape can change with a version bump and a read of
  older records, without an in-place data migration (reset-and-republish before 4.0; Q11).
- The Flowchart engine already accepts several outcome names and routes untaken connections dead (research R-3), so
  multi-outcome scheduling needs declarations and a duplicate-target rule, not a new engine.
- The Elsa 3 corpus has no `setOutcome` / `setOutcomes` call (spec 247), so Q7 matters for workflows outside it.
- Acornima, already used by the expression tooling and the Elsa 3 rewriter, can find literal outcome names in a
  script for FR-015.
