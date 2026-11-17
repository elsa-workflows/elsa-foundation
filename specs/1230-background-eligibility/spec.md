# Feature Specification: Background eligibility

**Feature Branch**: `claude/3052-s11-define-af1366`

**Created**: 2026-11-16

**Status**: Approved — agreed 2026-11-16 by @j03y-nxxbz (the approval plan), with all ten open questions answered as recommended and the built-in assessment confirmed; applies ADR 0093 (`875aa14c7`) and ADR 0092 (`f52a661df`) as merged.

**Initiative**: part of [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052), spec row **S-11**,
analysis **A-2**.

**Issues**: none filed yet. The S-11 intake on the #3052 board lists the five checks this spec settles.

**Input**: Background eligibility is one set of criteria (ADR 0093, C1–C10). One component in
`Elsa.Workflows.Runtime.Core` holds every criterion the engine enforces, and both publication and the background
worker call it, so a new criterion is one change. A refusal names the criterion it failed. An activity that reads
variables by name is refused a background placement instead of silently reading nothing on the worker. The two
criteria no code can prove (re-runs, cooperative cancellation) are documented on `[BackgroundExecutionSafe]`, where
the activity author makes the promise. Every built-in activity is assessed against the criteria, and those that
qualify are marked.

## Approval

**Which ADR text this spec applies.** Both ADRs are accepted and merged into the initiative branch.

| ADR | What this spec takes from it | Text applied | State |
|---|---|---|---|
| **ADR 0093** (background execution eligibility is one set of criteria) | Decisions 1–6: criteria, not a list; C1–C10 with their check kind (E, R, D); one component for the enforced criteria; C7 and C8 on the attribute; the built-in assessment and marking; causal order, not one order, across providers | merged as `875aa14c7` | accepted 2026-11-12 |
| **ADR 0092** (variable reads and writes share one versioned lifecycle) | The lost-update rule a background result meets (decision 6) and the refresh before background admission (decision 9). Both are built by S-10 (spec 1221), not here | merged as `f52a661df` | accepted 2026-11-12 |

**Approval plan**: @j03y-nxxbz alone (coordinator decision of 2026-11-16: the ADRs were resolved together, so specs
need no approval by the other architects). Approval needs answers to the open questions. This spec needs no new ADR
and no ADR amendment; if one turns out to be needed, it goes in its own small PR reviewed by @sfmskywalker and
@FransVanEk.

## Rule and boundary

**What this spec builds.**

1. **One eligibility component** that evaluates the enforced criteria: C1–C5, C9 and C10 at publication and again
   at run time, and C6 at run time only. It replaces the two lists that drifted apart today
   (`ExecutableNodeCompiler.ValidateBackgroundExecution` and `BackgroundActivityEligibility.Validate`).
2. **Refusals that name the criterion.** Every refusal keeps the code `VF-BG-001` and says which criterion failed
   (for example `C9`) and why, at publication and at run time.
3. **C9 enforced.** A background placement of an activity that reads variables by name (`IRuntimeScopedVariableReader`)
   is refused.
4. **C10 enforced.** A worker-bound input with no supported durable representation, that cannot be materialized on the
   worker, or that would lose its protection, is refused before admission. Secret references keep resolving at worker
   activation.
5. **C7 and C8 documented** on `[BackgroundExecutionSafe]`.
6. **The built-in activities assessed** against C1–C10 (see *Built-in assessment*), the qualifying ones marked, and an
   issue filed for each candidate that falls short only because of how it is implemented.
7. **No bypass.** Background nodes that an activity template provider compiles itself (today the agent's model-turn
   node) go through the same publication check when the template is placed (Q9).
8. **Evidence for ADR 0093 decision 6**: on every persistence provider, applying a background result keeps causal
   order, is atomic, follows the conflict rules, and leaves the actual commit order inspectable.

**Out of scope.**

- The lost-update rule for background results and the refresh before admission: S-10 (spec 1221, US5, PR #4316 and
  its follow-up).
- Lifting C3, C5 or C9 (the value model of S-8, a protected worker transport, a background variable session): each
  lift changes ADR 0093 first.
- A Studio control for setting placement on a node (ADR 0093 *Consequences*; no control exists today).
- Inspection surfaces for the commit order (S-6). This spec only shows that the order is recorded.
- ADR 0090's `[AccessesUndeclaredVariables]` and writable `VariableReference<T>` signals for C9. Neither exists on
  the branch; C9 grows when they land (Q3).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One set of rules at publish and at run time (Priority: P1)

A platform engineer adds or changes a background criterion once, and publication and the worker both apply it. A
workflow that publishes with a background node is never refused at run time for a criterion publication also checks,
unless the activity type changed between the two.

**Why this priority**: The drift between the two lists is the root problem ADR 0093 names. Every other story builds on
the single component.

**Independent Test**: For every enforced criterion, build a node that fails only that criterion and show that
publication refuses it and the run-time check refuses the same pinned node, both naming the same criterion.

**Acceptance Scenarios**:

1. **Given** a background node whose activity has an untyped `Any` result, **When** the workflow is published,
   **Then** publication refuses it with `VF-BG-001` naming C3; **and given** the same node pinned without that check
   (an executable built before this change), **When** the worker picks it up, **Then** the worker refuses it naming C3.
2. **Given** a background node whose result projection is a transient resource, **When** it is checked at run time,
   **Then** it is refused naming C4 (today run time checks only persistability).
3. **Given** a background body that stages a workflow dispatch, **When** the worker finishes the body, **Then** the
   result is refused naming C6 and becomes an incident, as today.

---

### User Story 2 - A variable reader cannot run in the background (Priority: P1)

A workflow author places an activity that reads variables by name in the background. Today publication accepts it
and the body silently reads nothing on the worker. Now publication refuses it and explains why.

**Why this priority**: A silent wrong value is the worst outcome the A-2 analysis found.

**Independent Test**: A test activity that is `[BackgroundExecutionSafe]` and implements `IRuntimeScopedVariableReader`
is placed in the background; publication refuses it naming C9; the run-time check refuses a pinned node of it naming C9.

**Acceptance Scenarios**:

1. **Given** a background-safe activity that reads variables by name, **When** a node of it is placed in the
   background and published, **Then** publication refuses it with `VF-BG-001` naming C9 and saying that variables reach
   a background step only through its inputs and output captures.
2. **Given** the same activity placed inline, **When** published, **Then** it publishes and reads its variables as today.

---

### User Story 3 - Worker-bound inputs are supported or refused early (Priority: P2)

A workflow author binds an input that cannot travel to a worker safely (no durable representation, cannot be
rebuilt on the worker, or would lose its protection). The step is refused before admission, naming C10, instead of
failing on the worker or leaking a secret into a stored snapshot.

**Why this priority**: C10 is the newest criterion; today only the general snapshot checks apply.

**Independent Test**: One test per C10 case (no durable representation, not materializable, protection lost) shows
the refusal before admission; a secret reference input is admitted, resolves at worker activation, and no raw secret
appears in any persisted snapshot; a JSON or `Any` input is admitted.

**Acceptance Scenarios**:

1. **Given** a background node with a stream-typed input, **When** published, **Then** publication refuses it naming C10.
2. **Given** a background node with a secret reference input, **When** it runs, **Then** the worker resolves the
   secret at activation and the stored input snapshot holds only the reference.
3. **Given** a background node with an `Any` input holding JSON, **When** published and run, **Then** it is admitted
   (an `Any` *result* stays refused by C3).

---

### User Story 4 - Activity authors see the promise where they make it (Priority: P2)

An activity author who adds `[BackgroundExecutionSafe]` reads, on the attribute itself, that the body may run more
than once (C7) and must observe its cancellation token (C8), and which criteria the engine checks for them.

**Why this priority**: C7 and C8 are declarations; documentation is their only guardrail.

**Independent Test**: The attribute's documentation states C7 and C8 in plain words and points to ADR 0093.

**Acceptance Scenarios**:

1. **Given** the attribute's documentation, **When** an author reads it, **Then** it states C7 (idempotent or keyed on
   `InvocationId`) and C8 (observe the cancellation token), and that C1–C6, C9 and C10 are checked by the engine.

---

### User Story 5 - Built-in activities that qualify are marked (Priority: P2)

A workflow author can place qualifying built-in activities in the background without writing their own. The spec
records, for every built-in activity, whether it qualifies and why not.

**Why this priority**: No shipped activity is marked today, so background execution is unusable without custom code.

**Independent Test**: Each marked activity publishes and runs in the background end-to-end; each assessed but
unmarked activity is refused naming its first failing criterion (or, for C7/C8, is simply not marked).

**Acceptance Scenarios**:

1. **Given** a marked built-in activity, **When** placed in the background, **Then** it publishes, runs on a worker and
   its result is applied on the coordinator.
2. **Given** a built-in activity the assessment did not mark, **When** placed in the background, **Then** publication
   refuses it naming the failed criterion, or "does not declare BackgroundExecutionSafe" when only C7/C8 are missing.

---

### User Story 6 - Results keep causal order on every provider (Priority: P3)

An operator runs the same workflow on the in-memory and the EF Core providers. Background results of unordered
branches may apply in a different position relative to other queued work, but causal order holds, every application
is atomic, and the actual commit order can be read back.

**Why this priority**: ADR 0093 decision 6 asks for evidence; the code reading in A-2 found the two queues order
differently.

**Independent Test**: The same two-branch workflow with background steps runs on both providers; tests check causal
order, atomic application, the conflict rule (last commit wins for unordered plain writes, ADR 0092 for
read-modify-write) and that the recorded commit order matches the order applied.

**Acceptance Scenarios**:

1. **Given** a background step followed by a dependent step, **When** run on either provider, **Then** the dependent
   step always runs after the result is applied.
2. **Given** two unordered background branches writing one variable, **When** run on either provider, **Then** the last
   commit wins and the recorded order says which commit was last.

---

### Edge Cases

- A node fails several criteria: the refusal names the **first** failing criterion in the order C1 … C10 (stable
  across publish and run time), so the message does not depend on check order.
- An executable published before this change pins a node that now fails a criterion (C3 or C4 at run time, C9, C10):
  the run-time check refuses it with an incident naming the criterion. No shipped activity is marked today, so this
  can only hit a custom activity (Q5).
- An activity type changes between publish and run (new deployment): the run-time check uses the activated object and
  the pinned contract, as today.
- A subclass of a marked activity inherits the attribute (`Inherited = true`): the assessment must say whether that is
  intended for each marked built-in (Q6).
- `ExecutionPlacement` holds an undefined enum value: refused at publication, as today (outside C1–C10; kept as a
  precondition of the component).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: There MUST be one eligibility component in `Elsa.Workflows.Runtime.Core` that evaluates C1–C5, C9 and
  C10 for publication and for the run-time re-check, and C6 for run time. Publication and the background coordinator
  and worker MUST call it; no other code may decide eligibility.
- **FR-002**: The component MAY expose one method for publication (with design-time information such as the CLR result
  type) and one for run time (with the pinned node and the activated activity), but the criteria MUST be defined
  once (ADR 0093 decision 3).
- **FR-003**: Every refusal MUST carry `VF-BG-001`, the criterion id (`C1` … `C10`) and a plain reason. When several
  criteria fail, the first in criterion order MUST be reported.
- **FR-004**: The run-time check MUST cover the same criteria as publication (C1–C5, C9, C10), closing today's gaps:
  C3 (typed result) and C4 (stream and transient-resource results) at run time.
- **FR-005**: C9: a background placement of an activity that implements `IRuntimeScopedVariableReader` MUST be refused
  at publication and at run time.
- **FR-006**: C10: a worker-bound input MUST be refused before background admission when it has no supported durable
  representation, cannot be materialized on the worker, or would lose its required protection. Secret references MUST
  resolve at worker activation and raw secret material MUST NOT appear in a persisted snapshot. A supported JSON or
  `Any` input MUST be admitted. Existing snapshot checks MUST be reused where they satisfy C10.
- **FR-007**: C6 MUST stay a run-time check on the worker, reported through the component with the criterion id.
- **FR-008**: `[BackgroundExecutionSafe]` MUST document C7 and C8, name the criteria the engine checks, say that a
  subclass inherits the promise (Q6), and say that an activity whose result is only used during the inline drain, or a
  trigger activity, is not marked (Q8).
- **FR-009**: Every built-in activity MUST be assessed against C1–C10 and the result recorded in this spec. Activities
  that meet every criterion, including C7 and C8 as their owner confirms, MUST be marked. An activity that misses a
  criterion only because of its implementation MUST get an issue, not an exception (ADR 0093 decision 4).
- **FR-010**: On every persistence provider, applying a background result MUST preserve causal dependencies, complete
  atomically, follow the agreed conflict rules and leave an inspectable record of the actual commit order. No order is
  imposed between unordered branches.
- **FR-011**: Spec 241's background contract MUST point to ADR 0093 for eligibility.
- **FR-012**: Every background node an activity template provider returns from its own compilation (today the agent
  template's model-turn node) MUST pass the same publication check when `ActivityTemplatePlacer` places it, before the
  executable is pinned. No path may set `supportsBackgroundExecution` or a background placement without it.

### Key Entities

- **Eligibility criterion**: one of C1–C10, with its id, its check kind (E, R or D) and a plain description.
- **Eligibility refusal**: `VF-BG-001`, the criterion id, the node id and the reason.
- **Background-safe declaration**: `[BackgroundExecutionSafe]` on an activity type, pinned as
  `SupportsBackgroundExecution` in its contract.

## Built-in assessment

Read in code at `2c84e802f` (research R-4); nothing was run. *Leaves* are CLR activities that pass C1 and C2. Every
structural activity and child handler fails C1 (16 types, including `BpmnProcess`, the only shipped
`IRuntimeScopedVariableReader`), every intrinsic fails C1 (9), and every stateful activity fails C2 (9).

| Verdict | Activities | Why |
|---|---|---|
| **Mark** (meets C1–C10; C7 and C8 hold by reading the code) | Primitives: `Break`, `Fault`, `WriteLine`, `WriteLines`. Scripting: `RunJavaScript`. BPMN: `BpmnDecision`. M365: `GetUser`, `ListEvents`, `ListMessages`, `DownloadFile`. Slack: `ListChannels`, `LookupChannel`, `LookupUserByEmail`, `UpdateMessage` | Pure, read-only or naturally idempotent bodies that observe the token or are synchronous. `RunJavaScript` is the main use: CPU-heavy scripts in a closed sandbox with no variable access. The connector reads are marked only after T022 (Q10). |
| **Already marked** | Agent: `AgentModelTurn` (internal, not discoverable) | Meets C1–C10 (journal keyed on `InvocationId`, linked token, no inputs). Its background node is built by hand in `AgentActivityProvider`, so publication never checks it (FR-012, Q9). |
| **Not marked: meets C1–C10, but background makes no sense** | `WriteHttpResponse`; triggers `Cron`, `Timer` | The HTTP response is delivered only from a result committed during the synchronous inline drain; a background result would arrive too late and be lost. A trigger's body gets no trigger payload on the worker. No criterion catches either; not marking them is enough (Q8). |
| **Not marked: C7 or C8 cannot be promised** | `SendHttpRequest`, `ReadLine`; M365 `SendMail`, `PostTeamsChannelMessage`; Slack `SendMessage`, `ReplyInThread`, `UploadFile` | Arbitrary or non-idempotent external effects (a re-run sends twice); `ReadLine` also ignores its token. |
| **Not marked: implementation gap → issue** (FR-009) | M365 `CreateEvent` (no idempotency key from `InvocationId`; #4721), M365 `UploadFile` (conflict behaviour `rename` duplicates on a re-run; its `byte[]` input needs a C10 answer; #4722), Slack `AddReaction` (a re-run faults on `already_reacted`; #4723), `EvaluateDecision` (the body re-reads coordinator stores and a live-burst overlay; unproven on a worker; #4724) | Each could qualify after a small change. |
| **Ineligible** | `Inline` (C3: `object` result; lifts with S-8), `PublishEvent` (C6, by design) | |

Unverified, checked before marking (T022): whether connection-based activities resolve their tenant partition on a
worker, whether a secret-bound input (for example `RunJavaScript`'s `Arguments`) resolves on the worker (C10), and
whether a large `DownloadFile` result hits the receipt size limit (`BG-ACT-006`).

## Open questions for the approver

Each question had a recommended answer. **@j03y-nxxbz accepted all ten on 2026-11-16**; the requirements follow
them.

| # | Question | Answer (accepted) |
|---|---|---|
| Q1 | **Scope.** The board row names C7–C9; ADR 0093 now has C1–C10, with C10 added in review. Does S-11 enforce C10 too? | **Yes.** ADR 0093 decision 3 puts C10 in the component and its *Required evidence* lists the C10 tests. Leaving it out would recreate the drift this spec removes. The board row's wording predates C10. |
| Q2 | **Diagnostic shape.** Keep one code `VF-BG-001` with the criterion in the message, or one code per criterion? | **One code, criterion as a structured field and in the message** (`VF-BG-001 (C9): …`). ADR 0093 decision 5 names `VF-BG-001`; a code per criterion would multiply codes without helping the author. |
| Q3 | **C9's other signals.** ADR 0093 says C9 later also covers ADR 0090's `[AccessesUndeclaredVariables]` and a writable `VariableReference<T>` input. ADR 0090 is now accepted as amended, but neither signal exists in code yet. | **Enforce `IRuntimeScopedVariableReader` now**; the component exposes C9 as one predicate so the later signals are one change when ADR 0090 is implemented. |
| Q4 | **Who confirms C7 and C8 for a built-in activity?** No code can prove them. | **The spec author proposes per activity in this spec's assessment table, and the approver confirms it** when approving the spec. An activity nobody can vouch for stays unmarked. |
| Q5 | **Already published executables.** A pinned background node that now fails a criterion at run time (for example C9): refuse, or let it run? | **Refuse with an incident naming the criterion.** No shipped activity is marked, so only custom activities can be affected, and running them would keep the silent wrong value C9 exists to stop. No migration. |
| Q6 | **Inheritance.** `[BackgroundExecutionSafe]` is `Inherited = true`, so a subclass of a marked built-in is marked without its author promising C7/C8. | **Keep inheritance**, but document on the attribute that a subclass inherits the promise. Changing it would break custom activities that derive from marked bases on purpose. |
| Q7 | **Commit-order evidence (FR-010).** Is S-10's write history (one record per applied write, in commit order, PR #4316) enough as "an inspectable record of the actual commit order", or is a separate record needed? | **S-10's write history plus the checkpoint sequence is enough**; no new store. The tests read it back on both providers. This makes US6 depend on PR #4316 being merged. |
| Q8 | **Two gaps no criterion catches.** `WriteHttpResponse` only works inline (its result is delivered during the inline drain), and a trigger activity (`Cron`, `Timer`) gets no trigger payload on the worker. Add criteria to ADR 0093, or only leave them unmarked? | **Leave them unmarked, no ADR change.** Only a marked type can be placed in the background, and marking is the author's declaration, so not marking closes both gaps for the built-ins. The attribute's documentation adds both as things an author checks before marking (FR-008). If a third-party activity hits them, a criterion is proposed then, in its own ADR 0093 amendment PR. |
| Q9 | **Template providers build their own background nodes.** The agent template compiles its model-turn node itself (`AgentActivityProvider.CompileAsync`, current on `main`), and `ActivityTemplatePlacer` copies the placement without an eligibility check, so any template provider can bypass publication. Route these nodes through the check (FR-012), or accept the bypass because the agent activity is internal? | **Route them through the check, in the template placer**, so it holds for every template provider, not only the agent. ADR 0093 decision 3 says no other code decides eligibility. The run-time check at admission and on the worker already applies to these nodes. |
| Q10 | **Which qualifying activities are marked?** 15 meet C1–C10 by reading; some (`Break`, `Fault`) have little use in the background. | **Mark all 15** once the unknowns are checked (T022): ADR 0093 decision 4 marks what qualifies, not what seems useful, and placement stays the workflow author's choice. A connector read whose partition does not resolve on a worker gets an issue instead. |

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Background eligibility is decided in exactly one place: adding a criterion changes one component and
  its tests, and no other file.
- **SC-002**: For each enforced criterion (C1–C6, C9, C10) there is a test showing the refusal at every point that
  checks it, and the refusal names the criterion.
- **SC-003**: A background placement of a variable-reading activity never reaches a worker.
- **SC-004**: Every built-in activity has a recorded verdict against C1–C10, and every activity that qualifies is
  marked.
- **SC-007**: No background node reaches a pinned executable without passing the publication check.
- **SC-005**: No raw secret value appears in any persisted background input snapshot in the test suite.
- **SC-006**: The causal-order tests pass on both the in-memory and the EF Core provider.

## Assumptions

- S-10's background admission refresh and lost-update handling for background results (spec 1221 US5) are built by
  S-10; this spec does not change them.
- The host still enables `BackgroundActivities` explicitly; this spec does not change who turns background execution on.
- The component's place (`Elsa.Workflows.Runtime.Core`) needs no new project reference: Publishing and
  `Elsa.Activities.Runtime` already reference it, and it already references `Elsa.Activities.Runtime.Core`, where the
  attribute and `IStatefulActivity` live.
- Workflow authors set placement in workflow JSON or code; there is no Studio control yet.

## Release note

Background activities: eligibility is now decided by one component (ADR 0093), and every refusal names its criterion
(`VF-BG-001 (C<n>): …`). Run time now checks the same criteria as publication, so a background node pinned before this
release whose **custom** activity has an untyped result (C3), a stream or transient-resource result or projection
(C4), reads variables by name (C9) or has a worker-bound input that is non-persistable, a live resource or sensitive
without a secret reference (C10) is refused at admission with an incident (Q5). No shipped activity was marked
before, so only custom activities are affected. Fifteen built-ins are now `[BackgroundExecutionSafe]`; the Microsoft
365 and Slack reads among them move to version 1.1.0.
