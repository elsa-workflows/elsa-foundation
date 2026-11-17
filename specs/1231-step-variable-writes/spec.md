# Feature Specification: Activities change variables, committed with the step

**Feature Branch**: `claude/3052-s7-define-spec`

**Created**: 2026-11-17

**Status**: Draft — waits for the coordinator's answers to Q1, Q2 and Q4–Q7 (*Open questions*; Q3 was decided on #4725). Not provisional: every ADR it
applies is accepted and merged.

**Initiative**: part of [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052), spec row **S-7**,
decisions **D-2** and **D-3**, analysis **A-3** (closed).

**Issues**: none filed yet. The S-7 row names #3023 as "possibly" in scope; this spec leaves it out (Q7).

**Input**: A script or C# activity may read and change workflow variables while it runs. Its changes are explicit
writes, saved when the step commits and discarded when the step faults or is cancelled. Script names are resolved
at publish; C# activities bind variables through typed references, or opt in to free names with a visible marker.
Undeclared variables are refused. A write on a stale base meets the lost-update rule of ADR 0092. A step that touches
variables this way is refused a background placement until a background session exists (ADR 0093 C9). Elsa 3's
`setVariable`, generated `set<Name>()` and nested mutation convert inside `RunJavaScript`.

## Approval

**Which ADR text this spec applies.** All are accepted and merged into the initiative branch.

| ADR | What this spec takes from it | Text applied | State |
|---|---|---|---|
| **ADR 0090** (activities change variables as a unit of work committed with the step) | Decisions 1–7 as amended (C1–C5): input bindings stay pure; a step is a unit of work; RunJavaScript reads and writes by name resolved at publish; C# bound references and the free-name opt-in; undeclared variables refused; changes apply in the step's checkpoint commit, intents before captures; read-then-write conflicts follow ADR 0092. The A-3 section (writes are a general capability). Converter consequences. Decision 8 (host interop) is out of scope (D-16, #4103) | `87e55ebc7` | accepted 2026-11-16 |
| **ADR 0092** (variable reads and writes share one versioned lifecycle) | Decision 1 (the session is the second surface over one mechanism), 3 (session reads), 4 (bases, the older dependency, own writes advance the base), 5–6 (the commit rule and the lost-update mode), 11 (a resumed step starts a fresh session), 13 (write history, source "session change") | `f52a661df`, decision 8 amended by #4316 | accepted 2026-11-12 |
| **ADR 0093** (background execution eligibility is one set of criteria) | C9 and its *Consequences*: the change that introduces `[AccessesUndeclaredVariables]` and writable `VariableReference<T>` extends C9's check in the same change, with a test per signal | `875aa14c7`, amended `dc32983a2` (#4726) | accepted 2026-11-12 |
| **ADR 0045** §4–§6 | §4's concurrent-write rule covers step writes; §5 input-binding purity; §6's one sanctioned exception `context.Variables.Set` | merged | accepted |
| **ADR 0046** (value model) | V6 declared loss, V9 copy, compare, patch for scripts (built by S-8), `Any` is plain JSON | `da4dddbb9` | accepted 2026-11-16 |

**Approval plan**: @j03y-nxxbz alone (coordinator decision of 2026-11-16: the ADRs were resolved together, so a spec
needs only the coordinator's approval). Approval needs answers to Q1, Q2 and Q4–Q7 (Q3 is decided). If an answer changes an ADR (Q2's
alternative, Q5's alternative), that change goes in its own small amendment PR reviewed by @sfmskywalker and
@FransVanEk. With the recommended answers this spec needs **no ADR amendment**.

## Rule and boundary

**What this spec builds.**

1. **The variable session** for a step body (a leaf CLR activity's `ExecuteAsync`/resume, and a `RunJavaScript`
   body): reads of committed values at the moment of the read, read-your-writes, explicit writes, and nothing saved on
   fault or cancel.
2. **Session reads and bases**, moved here from S-10 (spec 1221 FR-003, the session half of FR-005 and FR-006,
   T015, T016): each session read enters the attempt's session reads; a session write takes the base it was built on;
   an invocation's own applied writes advance its own base, including across suspension.
3. **The session write site** under S-10's commit rule (spec 1221 FR-009, FR-010), with write-history source
   "session change".
4. **`RunJavaScript` writes**: `variables.x = v`, `variables['x'] = v`, `setVariable('x', v)`, compound forms and
   member changes of a read value, resolved and pinned at publish; the may-write set.
5. **C# references**: `VariableReference<T>` and `ReadOnlyVariableReference<T>` inputs bound by the workflow author,
   `context.Variables.Get<T>` / `Set`, and the free-name opt-in `[AccessesUndeclaredVariables]`.
6. **Publish checks**: undeclared, computed, read-only and transient targets; unbound references; the may-write set
   fed to the concurrent-write rule; the warning when a may-write set overlaps the step's own capture.
7. **Background eligibility C9 extended** in `BackgroundEligibility` (S-11): an activity type marked
   `[AccessesUndeclaredVariables]` and an activity with a variable-reference input are refused a background
   placement, at publication and at the worker. `RunJavaScript` is not `[BackgroundExecutionSafe]` (coordinator
   decision on #4725, `eb9b1b1a4a`), so script writes never reach a worker and need no rule here.
8. **The Elsa 3 converter**: `setVariable`, `set<Name>()` and nested mutation inside `RunJavaScript` convert; in
   input expressions they stay refused.

**Built elsewhere, reused here.**

- **S-10 (spec 1221, PR #4316)**: per-variable versions and digests, the input read set, `VariableReadSetEntry`,
  `LostUpdateMode` and `[ActivityLostUpdateMode]`, the commit rule (`VariableCommitRule`), the write history and the
  commit order. ADR 0090's `[VariableConflictPolicy]` *is* S-10's per-type lost-update mode (ADR 0092 *Consequences*);
  this spec adds no second attribute.
- **S-4 (spec 1222)**: the `RunJavaScript` body read surface (`variables.X`, `variables['X']`, `getVariable`, the
  run-time fallback for computed and opaque reads, the body read plan). S-4 T047/T050 wait on this spec's session.
- **S-8 (spec 1228)**: the script value bridge (copy, compare, patch): entering values, finding a script's changes,
  the precision check and the C2 visibility warning. S-8 hands its changes to this spec's write path (spec 1228 Q1);
  S-8 US3's end-to-end test lands with whichever of the two arrives second.
- **S-11 (spec 1230, PR #4725)**: `BackgroundEligibility`, its subject and the `VF-BG-001 (Cn)` refusal.
- **S-5 (spec 266, PR #4669)**: the concurrent-write warning that the may-write set feeds.

**Out of scope.**

- Host-interop mode and live .NET objects in scripts (ADR 0090 decision 8): #4103 (D-16).
- Step changes to workflow outputs, correlation id and instance name (Q1); outcome selection is S-3.
- A variable session for structural activities (Q4); they keep `IRuntimeScopedVariableReader`.
- A variable session for background steps: C9 stays a refusal (ADR 0093 *Limits*). ADR 0090 open question 7 is
  therefore not reached.
- Studio presentation of variable-reference inputs, the may-write set and the free-name marker (ADR 0090 open
  question 2): its own elsa-foundation-studio work against the authoring descriptor this spec extends.
- Liquid writes (ADR 0090 open question 5). Merge and Reduce semantics. #3023 (Q7).

## Clarifications

### Session 2026-11-17

Settled from the ADR text during the clarify pass; the decisions that need the coordinator stay in *Open questions*.

- Q: Is there a separate read-only read (`GetReadOnly<T>`, a script `readVariable`)? → A: No. Under the amendment
  every read is read-only (C1); ADR 0090 open question 1 is gone. C# has `Get<T>` and `Set` only.
- Q: Are free-name reads recorded? → A: Yes, as session reads (FR-007), like every instrumented read.
- Q: What does `context.Variables` do on an activity with no reference inputs and no marker? → A: It exists; every
  free-name call throws (FR-025) and there is nothing to bind, so it reads and writes nothing.
- Q: What is a "suspending activity" for the transient refusal (FR-027)? → A: one that fails ADR 0093's C2: it
  implements `IStatefulActivity` or takes part in checkpoints.
- Q: Where does `[VariableConflictPolicy]` come from? → A: It is S-10's `[ActivityLostUpdateMode]` and the per-node
  setting (ADR 0092 *Consequences*); the default is last write wins with a warning, `Fault` by opt-in (C5).

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A script changes several variables in one step (Priority: P1)

A workflow author writes a `RunJavaScript` step that updates an order total and a counter. The changes are saved
together when the step completes; if the script throws, neither is saved.

**Why this priority**: it removes the cost ADR 0090 names first (a capture or `Set` per variable) and is the core
promise: change what you read, or assign what you did not read; saved with the step, or not at all.

**Independent test**: publish and run a workflow whose script assigns two variables, then one whose script assigns
both and then throws; inspect the committed values and the write history.

**Acceptance scenarios**:

1. **Given** variables `total = 10` and `count = 0`, **When** a script runs `variables.total = variables.total + 5;
   variables.count++;` and completes, **Then** `total = 15` and `count = 1` are committed in the step's checkpoint
   commit, each with a write-history entry of source "session change".
2. **Given** the same script followed by `throw new Error()`, **When** the step faults, **Then** neither variable
   changes and no write-history entry exists for them.
3. **Given** a script that reads `variables.count`, assigns it, and reads it again, **Then** the second read returns
   the assigned value (read-your-writes).
4. **Given** a script that only reads `variables.order`, **Then** nothing is written and the stored text and version
   of `order` are unchanged (C1).
5. **Given** a script that assigns `variables.count = 1` while `count` is already 1, **Then** nothing is written and
   the version stays (C4).
6. **Given** a script that changes `variables.order.total = 1` without assigning `order`, **Then** `order` is written
   as its base plus that change, untouched members keep their data, and the C2 visibility warning is recorded (S-8).

---

### User Story 2 — A C# activity reads and writes the variables the author bound (Priority: P1)

A module author writes an activity with a `VariableReference<decimal> Balance` input. The workflow author binds it to
a variable in the designer, as they bind a capture target. The activity reads the balance, adds to it and sets it.

**Why this priority**: the default C# surface; it keeps both the read set and the write set static.

**Independent test**: publish a workflow with such an activity bound to a declared variable; run it; publish one with
the reference unbound, bound to an undeclared name, or writable and bound to a loop item.

**Acceptance scenarios**:

1. **Given** `Balance` bound to `balance = 100`, **When** the activity runs `var b = context.Variables.Get(Balance);
   context.Variables.Set(Balance, b + 5);`, **Then** `balance = 105` commits with the step.
2. **Given** the activity changes a copy it read without `Set`, **Then** nothing is written (C2).
3. **Given** a `ReadOnlyVariableReference<decimal>` input, **When** the activity calls `Set` on it, **Then** the call
   throws and the step faults.
4. **Given** the reference is unbound, bound to an undeclared variable, or writable and bound to a loop item or index,
   **Then** publication is refused with a diagnostic naming the input and the reason.
5. **Given** an `Any` variable read with `Get<JsonNode>` (the default for `Any`), changed and set back, **Then** members
   the activity did not touch keep their data (C3). Read as a narrower class and set back, **Then** a warning names
   the members that type dropped (ADR 0046 V6).
6. **Given** the activity's descriptor, **Then** each variable-reference input shows its direction (read or
   read-write), and the node's may-write set lists the bound writable targets.

---

### User Story 3 — Lost updates on step writes are detected like any other write (Priority: P1)

Two branches of a `Parallel` each run a script that increments `count`. A branch that suspends between its read and
its write meets the lost-update rule.

**Why this priority**: without it, session writes would be the one write surface that silently loses updates
(ADR 0092 decision 1: one mechanism, no holes).

**Independent test**: drive a read-then-write session step whose base becomes stale before its commit (a suspension
between read and write, or an operator alteration); check the default warning and the `Fault` opt-in.

**Acceptance scenarios**:

1. **Given** two inline script branches that each run `variables.count++`, **Then** `count = 2` (no inline gap,
   spec 1221 FR-021).
2. **Given** a step that reads `count` (version 3), suspends, another branch writes `count` (version 4), and the step
   resumes and writes `count` from a value computed before suspension, **Then** the write's base is version 3, it is a
   lost update: it applies with a lost-update warning naming both writers and keeping the overwritten value.
3. **Given** the same under `[ActivityLostUpdateMode(Fault)]` or a node set to `Fault`, **Then** the step faults with a
   diagnostic naming the variable, both versions and the writers, and nothing it staged is saved.
4. **Given** a step that writes `count` without reading it (a blind write), **Then** it is never a lost update; it
   follows D-8 and the concurrent-write warning.
5. **Given** an input read `count = 0`, a later session read of `count = 10` in the same step, and a capture computed
   from the input, **Then** the capture's base is still the input's version (the older dependency is kept).
6. **Given** a step that writes `x` in its session when it suspends and captures `x` on completion, **Then** the
   capture does not conflict with the step's own earlier write (own writes advance the base).
7. **Given** a step that writes `x` in its session and captures `x` in the same commit, **Then** the session write
   applies first and the capture last, the capture wins, and publish warned about the overlap.

---

### User Story 4 — Generic module activities opt in to free names, visibly (Priority: P2)

A module author writes a generic "copy variable" activity that takes variable names as plain strings. They mark it
`[AccessesUndeclaredVariables]`. Publication, inspection and the descriptor show that it may touch any visible
variable.

**Why this priority**: serves Elsa 3 habits and generic activities without hiding the loss of static knowledge.

**Independent test**: run a marked activity using `Get<T>("name")` and `Set("name", v)`; run an unmarked activity
doing the same; run a marked activity naming an undeclared variable.

**Acceptance scenarios**:

1. **Given** a marked activity, **When** it calls `Set("total", 5)`, **Then** the name resolves to the nearest visible
   declaration at run time and the write commits with the step.
2. **Given** an unmarked activity, **When** it calls a free-name method, **Then** the call throws and the step faults.
3. **Given** a marked activity naming an undeclared variable, **Then** the step faults before its commit; nothing is
   created (decision 5).
4. **Given** a marked activity on a branch that runs concurrently with a write of any visible variable, **Then** the
   concurrent-write rule treats the marked activity as touching every visible variable.
5. **Given** the activity's descriptor and the published node, **Then** the marker is shown.

---

### User Story 5 — A step that touches variables is not placed in the background (Priority: P2)

A workflow author places a node in the background. If the activity type is marked `[AccessesUndeclaredVariables]`,
or has a variable-reference input, publication refuses it
with `VF-BG-001 (C9)`, and so does the worker if a placement got past publication.

**Why this priority**: ADR 0093's *Consequences* make it a hard requirement of the change that introduces these
signals; without it a background body would read nothing and lose its writes.

**Independent test**: publish each of the signals with `ExecutionPlacement = Background`; call the worker's
re-check with each.

**Acceptance scenarios**:

1. **Given** a `[BackgroundExecutionSafe]` activity type also marked `[AccessesUndeclaredVariables]`, placed in the
   background, **Then** publication is refused with `VF-BG-001 (C9)`, and the run-time re-check (admission and worker)
   refuses it with the same text.
2. **Given** a `[BackgroundExecutionSafe]` activity with a writable `VariableReference<T>` input, **Then** the same.
3. **Given** a `[BackgroundExecutionSafe]` activity with a `ReadOnlyVariableReference<T>` input, **Then** the same
   (Q2).
4. **Given** a background `RunJavaScript`, **Then** it stays refused as S-11 built it (it is not
   `[BackgroundExecutionSafe]`); this spec adds nothing for it (Q3, decided).
5. **Given** a template placement in a shell without the runtime type (S-11's deferred type check), **Then** the
   contract-visible signals (variable-reference inputs, the descriptor marker) are refused at
   placement and the type-only parts at admission and on the worker.

---

### User Story 6 — Elsa 3 scripts that set variables convert (Priority: P2)

An Elsa 3 workflow uses `setVariable('Total', x)`, `setTotal(x)` and `getVariable('Order').Status = 'Paid'` inside a
`RunJavaScript`. It converts to an Elsa 4 script that does the same through `variables`.

**Why this priority**: lifts a refusal that blocks real Elsa 3 workflows (spec 247), under faithful-or-refuse.

**Independent test**: convert fixtures with each form inside `RunJavaScript` and inside an input expression; publish
and run the converted scripts.

**Acceptance scenarios**:

1. **Given** `setVariable('Total', x)` with a literal name of a declared variable, **Then** it converts to
   `variables.Total = x` (or `variables['Total'] = x` when the name is not an identifier) (Q6).
2. **Given** a generated setter `setTotal(x)`, **Then** it converts to `variables.Total = x`.
3. **Given** `getVariable('Order').Status = 'Paid'`, **Then** it converts to `variables.Order.Status = 'Paid'`, a
   member change (C2).
4. **Given** `setVariable(name, x)` with a computed name, **Then** conversion is refused with a diagnostic and
   guidance to use a literal name.
5. **Given** a setter on a variable the workflow does not declare (Elsa 3 created it on first write), **Then**
   conversion is refused with guidance to declare the variable in the Elsa 3 workflow first.
6. **Given** any of these forms in an input expression, **Then** conversion stays refused with the existing
   guidance, now also naming a `RunJavaScript` step as the place for writes.

---

### Edge Cases

- A loop item or index (ADR 0028) is read-only: in a script it is frozen and assigning it or a member throws; in C#
  a read returns an untracked copy, and `Set` on it throws. Publish refuses an assignment or a direct nested
  assignment to it, and a writable reference bound to it.
- A withheld (secret) value is refused on read with `VF-ACT-010`, as `IRuntimeScopedVariableReader` refuses it.
- A script names a transient variable: refused at publish (decision 3; host interop is out of scope). A C# reference
  to a transient variable: `Get` returns the live instance and `Set` replaces it in memory; nothing is committed or
  recorded; publish refuses such a reference on a background-placed or suspending activity, and a free-name transient
  access in such a step faults.
- A step suspends after writing: its changes up to the suspension commit with it; the resumed step starts a fresh
  session with no session reads; pending intents are not carried across.
- A new attempt (retry, re-execution, crash redelivery) starts a new session and reads committed values again.
- A re-driven coalesced burst after a crash re-runs only steps whose commit never became durable; an intent never
  applies twice.
- A script lets a read value escape into a local, a helper call or a closure: the variable is in the may-write set
  (conservative), even if the helper does not change it.
- A script in which the `variables` root or `setVariable` escapes (aliased, passed, spread) and that also writes:
  refused at publish (Q5); a body that only reads through an escaped root keeps S-4's opaque read fallback.
- A changed array is written whole; one whose untouched elements would lose precision is refused with ADR 0046's
  whole-array diagnostic (S-8).
- A free-name C# read of a name that resolves to nothing: the step faults (undeclared), matching the write rule.

## Requirements *(mandatory)*

### Functional Requirements

**The variable session (ADR 0090 decisions 1, 2; C1, C2)**

- **FR-001**: Input-binding expressions MUST stay pure: they MUST NOT write variables, and MUST NOT see a step's
  pending writes.
- **FR-002**: A step body (a leaf CLR activity's invoke or resume evaluation, and a `RunJavaScript` body) MUST get a
  variable session for that step only; the session MUST NOT be available to input-binding expressions, structural
  evaluations (Q4) or background workers (FR-030).
- **FR-003**: A session read MUST return the variable's committed value at the moment of the read, never the step's
  pinned input snapshot, plus the step's own pending writes (read-your-writes).
- **FR-004**: A read MUST NOT create a write. A script gets plain JavaScript values (ADR 0046 V9); C# gets a copy.
- **FR-005**: A write MUST be explicit: in a script an assignment (`variables.x = v`, `variables['x'] = v`,
  `setVariable('x', v)`, compound forms) or a change to a member of a read value; in C# `context.Variables.Set`. In C#,
  changing a copy without `Set` MUST NOT be a write.
- **FR-006**: Loop items and indexes MUST be read-only: frozen in a script (mutation throws), an untracked copy in C#
  (`Set` throws). A withheld value MUST be refused on read with `VF-ACT-010`.
- **FR-006a**: The session MUST offer the read S-4's body surface builds on (spec 1222 FR-033–FR-035, T050): by
  declaring scope and key (the pinned target), returning the committed value plus the step's own pending writes.
- **FR-006b**: A session read of a transient variable (spec 1222 FR-017) or a withheld value MUST be recorded on the
  host side when the read happens and MUST fault the step even when the script catches the JavaScript exception
  (spec 1222 FR-017a).

**Session reads and bases (ADR 0092 decisions 3, 4, 11; moved from spec 1221)**

- **FR-007**: Every session read MUST be recorded in the attempt's session reads with the version read, the
  variable's protection policy (spec 1222 FR-035; spec 1228 US7 relies on it), plus the stored digest when the node's
  lost-update mode is `Fault` (spec 1221 FR-003). Session reads MUST NOT be carried to a
  new attempt or across a suspension. A visible variable that was not read MUST NOT appear.
- **FR-008**: A session write MUST take as its base the read it was built on: the session read of that variable, or,
  when the step did not read it in its session, the input read set's entry (spec 1221 FR-005). A write to a variable
  read by neither is blind.
- **FR-009**: A later session read MUST NOT erase an older input dependency still in use; where provenance is
  ambiguous the oldest in-use dependency is the base (spec 1221 FR-006 and research R-4, session half).
- **FR-010**: When an invocation's own write to X applies, its base for X MUST advance to the version that write
  produced, and the advanced base MUST be persisted with the invocation so that a session write on suspension and a
  capture on completion do not conflict (spec 1221 FR-007, T016). This includes advancing bases on resume (moved from
  spec 1221 T033).

**Committing with the step (ADR 0090 decision 6, C3, C4; ADR 0092 decisions 5, 6, 13)**

- **FR-011**: A step's writes MUST become whole-value intents (base plus the step's changes, C3) and MUST apply in the
  same checkpoint commit as the step's transition, outputs and captures, through the same scoped frame resolution and
  assignment validation as `Set` and captures (ADR 0048).
- **FR-012**: Each intent MUST pass S-10's commit rule (spec 1221 FR-009) at the session write site, with the node's
  lost-update mode, before the no-op check: a no-op is judged by meaning in ADR 0091's canonical form (ADR 0091 is
  still a draft, #3408: a forward reference, as in ADR 0090; until it lands, S-10's `VariableValueDigest` canonical
  writer is the form) for a whole
  assignment, and JavaScript against JavaScript for a script member change (C4); a no-op writes nothing and keeps the
  version.
- **FR-013**: Within one commit, intents MUST apply first and captures last; the capture wins.
- **FR-014**: A failed type check, an undeclared free name or a serialization error MUST fault the step before its
  commit. A faulted or cancelled step MUST save nothing.
- **FR-015**: On suspension, the step's changes so far MUST commit with the suspension; pending intents MUST NOT be
  carried across it.
- **FR-016**: Each applied session write and each lost update MUST leave a write-history record with source "session
  change" (S-10's `VariableWriteSource.SessionChange`), the writer (node, invocation, attempt), base and new versions,
  and the lost-update outcome.
- **FR-017**: A whole-value write from C# of a value read as a type that dropped members of the stored value MUST
  record a warning naming the dropped members (C3, ADR 0046 V6). `Get<T>` on an `Any` variable MUST default to a JSON
  node so that no member is lost.

**`RunJavaScript` writes (ADR 0090 decision 3)**

- **FR-018**: Publish MUST resolve every written name to its nearest declaring scope with the same resolver as S-4's
  reads, and pin the target (scope and key).
- **FR-019**: Publish MUST compute the node's may-write set: every variable the script assigns, changes a member of, or
  lets escape into a local, a function argument or a closure. Read-only targets are frozen, so their escapes are not
  writes.
- **FR-020**: Publish MUST refuse a script that: writes through a computed name (`variables[k] = v`,
  `setVariable(k, v)`); uses `setVariable` as a value; writes in a body where the `variables` root escapes (Q5); writes
  an undeclared name; reads or writes a transient variable; or assigns, or directly assigns into, a read-only target.
  Each refusal names the script position and the target.
- **FR-021**: The runtime MUST NOT provide generated `set<Name>()` accessors.

**C# references and free names (ADR 0090 decisions 4, 5)**

- **FR-022**: An activity MAY declare inputs of type `VariableReference<T>` (read and write) and
  `ReadOnlyVariableReference<T>` (read only). The workflow author binds them to a declared variable. The authoring
  descriptor MUST show each such input with its direction and value type.
- **FR-023**: `context.Variables.Get<T>(reference)` MUST return a copy, and `Set(reference, value)` MUST record an
  intent; `Set` on a read-only reference MUST throw. `context.Variables` MUST expose no other state (it is not a service
  locator; ADR 0045 §6's sanctioned exception).
- **FR-024**: Publish MUST refuse a variable-reference input that is unbound, bound to an undeclared variable, or
  writable and bound to a read-only target. The node's may-write set is its writable bound references.
- **FR-025**: `Get<T>("name")` and `Set("name", value)` MUST work only on an activity type marked
  `[AccessesUndeclaredVariables]`; on an unmarked type they MUST throw, faulting the step. Names resolve at run time to
  the nearest visible scope; an undeclared name MUST fault the step before its commit. Nothing is created implicitly.
- **FR-026**: The marker MUST be part of the activity descriptor and the published node, so publication, inspection
  and (later) Studio can show it.
- **FR-027**: A C# reference to a transient variable MUST return the live instance on `Get` and replace it in memory
  only on `Set`; nothing about it is committed or recorded. Publish MUST refuse a transient reference on a
  background-placed or suspending activity; a free-name transient access in such a step MUST fault.

**Publish-time write analysis (ADR 0090 decision 3, 4, 6; ADR 0045 §4)**

- **FR-028**: The concurrent-write rule (`ValueFlow/ConcurrentWrite`, a warning once spec 266 lands) MUST include step
  writers through their may-write set, and MUST treat an `[AccessesUndeclaredVariables]` activity as touching every
  visible variable.
- **FR-029**: Publish MUST warn when a node's may-write set and one of its own output captures name the same variable.

**Background eligibility C9 (ADR 0093 *Consequences*)**

- **FR-030**: `BackgroundEligibility`'s C9 check MUST refuse a background placement, at publication and in the
  run-time re-check at admission and on the worker, with `VF-BG-001 (C9)` and a reason naming the signal, for:
  (a) an activity type marked `[AccessesUndeclaredVariables]`; (b) an activity with a writable `VariableReference<T>`
  input; (c) an activity with a `ReadOnlyVariableReference<T>` input (Q2). `RunJavaScript` needs no rule: it is not
  `[BackgroundExecutionSafe]` (Q3, decided on #4725). The existing
  `IRuntimeScopedVariableReader` refusal stays. These checks MUST land in the same change that introduces the marker
  and the reference types.
- **FR-031**: Where placement cannot see the CLR type (S-11's `TypeCheckedAtRunTime`), the signals visible in the
  pinned contract (reference inputs, the marker) MUST still be checked at placement; the
  rest at admission and on the worker.

**Elsa 3 conversion (ADR 0090 *Consequences*, converter)**

- **FR-032**: Inside `RunJavaScript`, the converter MUST rewrite `setVariable('<literal>', v)` and a generated
  `set<Name>(v)` of a declared variable to an assignment through `variables` (Q6), and keep a member change of a read
  value (`getVariable('o').x = v`, `variables.o.x = v`) as a member change through `variables`.
- **FR-033**: The converter MUST refuse, with a stable diagnostic and guidance: a computed setter name; a setter of an
  undeclared variable; a setter of a variable that converts to a transient one; and every write form in an input
  expression (the existing refusal, with guidance naming a `RunJavaScript` step).
- **FR-034**: `setOutput`, `setInput`, `setResult`, `setCorrelationId` and `setWorkflowInstanceName` MUST stay refused
  (Q1).

**Cost**

- **FR-035**: The cost of a step write MUST be measured against a large payload and a hot loop: serialization of
  written variables only (a read serializes nothing), the canonical comparison of FR-012, and the session-read record.

### Key Entities

- **Variable session**: per step; holds the step's session reads, its pending intents and the read-your-writes view.
- **Session read**: a `VariableReadSetEntry` (S-10) recorded per attempt.
- **Write intent**: variable (scope, key), the whole value (base plus changes), its base, its kind (whole assignment
  or member change), the writer.
- **May-write set**: the variables publish determines a node can change; pinned with the node.
- **Variable access marker**: `[AccessesUndeclaredVariables]` on an activity type, shown in the descriptor.
- **Variable reference input**: an input of type `VariableReference<T>` or `ReadOnlyVariableReference<T>`, bound to
  a declared variable like a capture target.
- **Body touch set**: for a `RunJavaScript` node, the pinned reads and writes plus whether it uses the fallback;
  read by inspection and the concurrent-write rule.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every item of ADR 0090's *Required evidence* that this spec covers (all except host interop) has a test
  that passes, listed in tasks.md.
- **SC-002**: A step that only reads variables leaves the stored text and version of every variable unchanged in 100%
  of the tested value shapes (large integers, decimals with trailing zeros, escaped strings, unknown members).
- **SC-003**: A faulted or cancelled step saves none of its writes in every tested path (inline, suspended then
  faulted, re-driven burst).
- **SC-004**: Two parallel inline script branches that each increment a counter give 2; a stale-base session write
  is never silent: it either warns (default) or faults (`Fault`).
- **SC-005**: Each of the three C9 signals is refused with `VF-BG-001 (C9)` both at publication and at the worker.
- **SC-006**: The Elsa 3 JTest corpus is re-measured: every definition refused only because of `setVariable`,
  generated setters or nested mutation inside `RunJavaScript` converts, publishes and runs; the count is posted on
  #3052.
- **SC-007**: A write adds no serialization for variables the step only read; the measured cost of a write of a
  1 MiB value is recorded in research.md.

## Assumptions

- S-10 (#4316) and S-11 (#4725) are merged before this spec is implemented; if not, the implementing PR depends on
  them. S-4's read surface (T045–T050) is built with or before this spec's script writes.
- S-8's bridge is the mechanism that finds a script's changes; if this spec lands first, a minimal assignment-only
  path (whole assignments) is acceptable until the bridge lands, and member changes land with S-8.
- Inline steps have no gap between reads and commit (spec 1221 FR-021, proven in #4316), so inline session writes meet
  the lost-update rule only across suspension or operator alterations.
- `[ActivityLostUpdateMode]` (S-10) is ADR 0090's `[VariableConflictPolicy]`; no rename.

## Open questions

Each has a recommended answer; the coordinator approves or changes it.

| # | Question | Recommended answer | Alternative |
|---|---|---|---|
| Q1 | **Which engine-owned state.** ADR 0090's A-3 section says a step may change variables, workflow outputs, correlation id, instance name and outcome selection, but its decisions specify a surface for variables only. Does S-7 build all of them? | **Variables only.** Outcome selection is S-3. Workflow outputs, correlation id and instance name from step bodies get their own spec row (proposed S-13), which designs their surface and the correlation rule (never rebind bookmarks). The converter keeps refusing their Elsa 3 setters. | Build all four here; needs a surface design the ADR does not give. |
| Q2 | **`ReadOnlyVariableReference<T>` under C9.** ADR 0093's enforcement sentence names only writable references. | **Refuse it too.** C9's criterion text says the body does not *read or write* workflow variables, and the existing C9 signal (`IRuntimeScopedVariableReader`) is itself read-only. On a worker the read would see durable state that may lag (ADR 0090 open question 7). No ADR change: the criterion already covers it; recorded in research R-6. | Allow it (stale reads on the worker); needs an ADR 0093 amendment. |
| Q3 | **`RunJavaScript` in the background.** | **Decided 2026-11-17 by the coordinator (#4725, `eb9b1b1a4a`):** `RunJavaScript` is not `[BackgroundExecutionSafe]`; a background `RunJavaScript` is refused. No rule needed here. | — |
| Q4 | **Structural activities** (ADR 0090 open question 6). | **No session for structural activities in this spec.** They keep `IRuntimeScopedVariableReader` reads; a write from a structural evaluation is a later spec if a case appears. | Each structural evaluation is its own step with a session (the ADR's recommendation, but no current consumer). |
| Q5 | **Writes when `variables` escapes.** S-4 lets a body read through an escaped `variables` root (opaque fallback). A write through it cannot be resolved at publish. | **Refuse at publish** a body that writes when the `variables` root or `setVariable` escapes (ADR 0090 decision 3). Read-only bodies keep S-4's opaque reads. | Allow and treat the may-write set as every visible variable, resolved at run time; needs an ADR 0090 amendment of decision 3. |
| Q6 | **The converted form of `setVariable('x', v)`.** | **Rewrite to `variables.x = v`**, matching S-4's `variables.X` for reads: one form in converted scripts. | Keep `setVariable('x', v)`, which the runtime supports. |
| Q7 | **#3023** (intrinsics inside graph activities) is listed as "possibly" S-7. | **Out of S-7.** It is about `Set`/`SetOutput` nodes inside reusable graph activities, not step writes; it stays its own issue. | Include it; adds graph-runtime intrinsic support to this spec. |
