# Feature Specification: Null, missing and undefined

**Feature Branch**: `agent/claude/3052-s2-null-missing-undefined`

**Created**: 2026-11-03

**Status**: Draft — **provisional**. Approval waits on the ADR PRs listed under *Approval*.

**Initiative**: part of [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052), spec row **S-2**.

**Issues**: [#3280](https://github.com/elsa-workflows/elsa-foundation/issues/3280) (explicit null on intrinsics),
[#3288](https://github.com/elsa-workflows/elsa-foundation/issues/3288) (unsupplied workflow input),
[#3172](https://github.com/elsa-workflows/elsa-foundation/issues/3172) (null into a non-nullable input),
[#3170](https://github.com/elsa-workflows/elsa-foundation/issues/3170) (`undefined` object keys).

**Input**: Define how Elsa 4 treats an explicit `null`, an omitted value and a JavaScript `undefined`, for native and
converted workflows alike, as decisions D-10 and D-11 of #3052 and the ADRs below record it.

## Approval

> **Provisional.** This spec may be drafted now (see [initiatives](../../docs/contributing/initiatives.md#phases)).
> It cannot be approved until every ADR it applies is agreed. If an ADR changes before agreement, this spec follows it.

| ADR PR | What this spec takes from it | State on 2026-11-03 |
|---|---|---|
| [#3388](https://github.com/elsa-workflows/elsa-foundation/pull/3388), ADR 0045 §2 amendment (D-10, D-11) | Null, omission and `undefined` rules; the default-on-null and undefined-as-null policies; recording them on the workflow | Approved by @FransVanEk; waiting for @sfmskywalker |
| [#3390](https://github.com/elsa-workflows/elsa-foundation/pull/3390), ADR 0083 amendment (D-11 part) | Undefined-as-null at the binding boundary, for both binding forms | In review |
| [#3408](https://github.com/elsa-workflows/elsa-foundation/pull/3408), ADR 0091 decision 8 (A-5 P-8) | Explicit null stays distinct from missing; S-2 chooses the representation, the serializer keeps it | In review |
| [#3420](https://github.com/elsa-workflows/elsa-foundation/pull/3420), ADR 0089 amendment question 3 (row 10) | A computed variable name that matches nothing reads `undefined` with a warning | Row 10 agreed by all three (a8e138dff); the PR is in review |

**Related, not blocking:** [#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422) (ADR 0046 amendment,
V8) is where the JavaScript adapter contract will hold the boundary diagnostics. ADR 0045 §2 states the rules this
spec applies; if V8 moves them, FR-015 to FR-018 follow V8.

**Approval plan**: the initiative's approvers, @sfmskywalker and @FransVanEk (technical architects), approve the final
text; @j03y-nxxbz is the author and records agreement in a PR comment. **Implementation**: not before every spec of
#3052 it depends on is defined, as #3052 agreed.

## Rule and boundary

**Rule**: Elsa 4 behaves the same for native and imported workflows. Where a workflow needs Elsa 3 behaviour, it says
so through a **visible policy** recorded on the workflow, never through where the workflow came from (D-11).

**In scope**: declared members, meaning activity inputs, workflow-request (workflow input) members, variables and
workflow outputs, and the values of the `Set` / `SetOutput` intrinsics; the JavaScript-to-workflow boundary; computed
variable reads; the Elsa 3 converter option; the stored representation of explicit null and absent.

**Out of scope**:
- Inside a JSON value (`Any`), a missing property and a `null` property stay distinct, because the data is stored as
  received (ADR 0091 decision 3). Nothing here rewrites JSON data.
- Literal and computed variable names in general (S-4, ADR 0089). This spec defines only the outcome of a computed name
  that matches nothing.
- Absent or blank Elsa 3 members whose converted value differs (#3281, S-0).
- Applying the input contract on every start: fixed by
  [#3731](https://github.com/elsa-workflows/elsa-foundation/pull/3731) (#3728, merged 2026-11-04 as `4c002dadb`).
  `WorkflowStartDispatcher` validates every new start (API, triggers, BPMN, test runs, child workflows) with
  `IWorkflowExecutableInputValidator` in complete-bag mode, before the start policy and before the start command is
  enqueued. It fills declared defaults into the start payload and refuses a blank, unknown, duplicate or mistyped
  input, or a missing required one, with `WorkflowExecutableStartRejectedException` (category `Input`, rendered as
  `400`); the stimulus router reports such a start as `Refused`. S-2 extends that admission and does not add another
  one (see *Admission* below).
- The validator's type conversion (`WorkflowExecutableInputValidator` L119, System.Text.Json defaults today). S-9
  (spec 1207) moves it onto the shared data-boundary options; S-2 does not change it.
- Studio editing surfaces beyond what FR-024 names.

**Admission**: "admission" in this spec is #3731's input-contract step in `WorkflowStartDispatcher`. It already
delivers FR-008 for an omitted required input and the declared-default half of FR-006 for workflow inputs, on every
start path. S-2 adds three things to it:
- an omitted optional input without a declared default materializes as `null` when nullable, otherwise as its type
  default (FR-006, FR-009);
- workflow-input nullability is pinned in the input contract (FR-005), so admission refuses an explicit `null` for a
  non-nullable input unless default-on-null applies (FR-008, FR-013);
- FR-008's "before the start checkpoint" holds for these new outcomes too: they are decided where #3731 decides, before
  the start command is enqueued.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Set a variable or output to null (Priority: P1)

A workflow author sets a nullable variable or workflow output to `null` with `Set` or `SetOutput`, as a literal or from
an expression. Today publication refuses the literal (`VF-ACT-004`), so an Elsa 3 `SetVariable` of null cannot convert
(#3280).

**Why this priority**: explicit null is a value (D-10); refusing it blocks native authoring and conversion alike.

**Independent Test**: publish and run a workflow whose `Set` writes a literal `null` into a nullable variable; the
variable reads `null` afterwards.

**Acceptance Scenarios**:

1. **Given** a nullable variable, **When** a `Set` intrinsic writes a literal `null`, **Then** the workflow publishes
   and the variable holds an explicit null.
2. **Given** a non-nullable variable and no default-on-null policy, **When** a `Set` writes a literal `null`, **Then**
   publication fails with `VF-ACT-004`, naming the target.
3. **Given** an Elsa 3 `SetVariable` whose value is null and a nullable target, **When** it is converted, **Then** the
   converter emits a typed literal null instead of refusing (spec 267's refusal for this case is dropped).

---

### User Story 2 - Start a workflow without an optional input (Priority: P1)

A caller starts a workflow without supplying a declared optional input. The workflow reads its declared default, or
`null` or the type default, instead of failing on the first read (#3288).

**Why this priority**: today the run starts, then a scheduler work item is poisoned and the instance stays `Running`.

**Independent Test**: start `composition-bulk-dispatch-sub-workflow` from the JTest corpus with no input; the
FlowDecision reads `ShouldFault` as `null` and the flow continues.

**Acceptance Scenarios**:

1. **Given** an optional input with a declared default, **When** it is omitted, **Then** admission completes it with
   that default before the start checkpoint (already true since #3731).
2. **Given** an optional nullable input with no default, **When** it is omitted, **Then** it reads `null`.
3. **Given** an optional non-nullable input with no default, **When** it is omitted, **Then** it reads its type default.
4. **Given** a required input, **When** it is omitted, **Then** the start is refused with a diagnostic naming the input,
   no instance runs a step, and nothing is scheduled (already true since #3731).
5. **Given** a request that supplies an explicit `null` for a nullable input, **Then** the input reads `null`, not its
   declared default.

---

### User Story 3 - Null into a non-nullable input, by policy (Priority: P2)

A workflow that relies on Elsa 3's "null becomes the type default" for value-type inputs selects the
**default-on-null** policy. Without the policy, the current refusal stays (#3172).

**Why this priority**: parity for converted workflows without weakening the native contract.

**Independent Test**: bind a JavaScript expression returning `null` to an `Int32` input; without the policy the step
fails with `VF-ACT-004`; with the policy the input receives `0`.

**Acceptance Scenarios**:

1. **Given** no policy, **When** a binding produces `null` for a non-nullable input, **Then** the step fails with
   `VF-ACT-004`, as today.
2. **Given** the default-on-null policy, **When** a binding produces `null` for a non-nullable input, **Then** the
   input receives the default the policy selects and the invocation record shows a substituted value.
3. **Given** the policy, **When** the target is nullable, **Then** `null` is kept; the policy never replaces a valid
   null.

---

### User Story 4 - `undefined` at the JavaScript boundary (Priority: P2)

By default an `undefined` binding result stays an error, and an `undefined` object property is dropped **with a
diagnostic**; an `undefined` array element or sparse hole instead becomes a `null` placeholder, under either policy,
so positions never shift. A workflow may select the **undefined-as-null** policy, under which the result and object
properties become `null` too (#3170, #3172).

**Why this priority**: today the property is dropped silently, and Elsa 3 parity needs the policy, not a hidden marker.

**Independent Test**: evaluate `({ discount: undefined })` with and without the policy; the stored values are
`{}` plus a warning, and `{ "discount": null }`.

**Acceptance Scenarios**:

1. **Given** no policy, **When** a binding result is `undefined`, **Then** evaluation fails with the existing
   ADR 0083 guidance.
2. **Given** no policy, **When** a result object has an `undefined` property at any depth, **Then** the property is
   dropped and a warning names its path; an `undefined` array element or sparse hole instead becomes a `null`
   placeholder with a path diagnostic, so later indices never shift.
3. **Given** any policy, **When** a result contains `NaN` or `Infinity`, **Then** it becomes `null` and a warning names
   its path.
4. **Given** the policy, **When** a binding result is `undefined`, **Then** it materializes as `null` (and US3 applies
   when the target is non-nullable).
5. **Given** the policy, **When** a result object has an `undefined` property at any depth, **Then** it is kept as
   `null`; an `undefined` array element or sparse hole likewise becomes a `null` placeholder, positions preserved, and
   an element already `null` needs no diagnostic.
6. **Given** the policy, **When** a script returns a string from `JSON.stringify`, **Then** that string is not
   rewritten.
7. **Given** no policy and a function-body script (its result is only what `return` gives) whose result is bound to a
   variable or output, **When** the script has no `return` statement, **Then** publication refuses it with a fix
   instruction; **When** any script form with a bound result produces `undefined` at run time, **Then** the step faults
   with the same instruction.
8. **Given** a script activity whose result is not bound, **When** it returns nothing, **Then** it completes and nothing
   is written.
9. **Given** the policy and a bound result, **When** the script produces `undefined`, **Then** the result is `null` and
   the binding writes it (what Elsa 3 did).
10. **Given** a bound result, **When** an expression script or a Script activity produces its value as a completion
    value (for example a last expression statement `a + b`) without a `return` statement, **Then** publication accepts
    it and the completion value is written; no `return` is required.

---

### User Story 5 - Converted workflows carry their policies (Priority: P2)

A host enables the Elsa 3 converter. One converter option selects the policies Elsa 3 parity needs; each converted
workflow records them, so it behaves the same after edits, export, or on a host without the converter.

**Why this priority**: D-11 replaces the converted-definition marker considered for #3170 and #3172.

**Independent Test**: convert an Elsa 3 definition with the option on, export it, import it on a host without the
converter, publish, and run; the same inputs give the same outputs.

**Acceptance Scenarios**:

1. **Given** the option is on, **When** a definition converts, **Then** the workflow records the undefined-as-null and
   default-on-null policies.
2. **Given** the option is off, **When** a definition converts, **Then** no policy is recorded, and the conversion
   report says the workflow runs under Elsa 4's stricter rules.
3. **Given** a converted workflow, **When** its policies change, **Then** its artifact hash changes (ADR 0038).
4. **Given** a native workflow, **When** an author selects a policy, **Then** it behaves exactly like a converted
   workflow with that policy.

---

### User Story 6 - A computed variable name that matches nothing (Priority: P3)

A script reads a variable by a computed name (`getVariable(expr)`) that resolves to no variable. It reads `undefined`
with a warning instead of failing or reading `null` (reconciliation row 10).

**Why this priority**: agreed by all three approvers; small, and S-4 builds on it.

**Independent Test**: run `getVariable('no' + 'pe') === undefined`; the result is `true` and a warning is recorded.

**Acceptance Scenarios**:

1. **Given** a computed name that matches nothing, **When** the script reads it, **Then** JavaScript sees `undefined`,
   and a warning is recorded with the read marked absent.
2. **Given** a literal name that matches nothing, **When** the workflow is published, **Then** publication reports a
   diagnostic (unchanged).
3. **Given** Liquid, **When** a missing value is read, **Then** it is nil and the warning carries the difference.

---

### User Story 7 - Null and absent survive storage (Priority: P1)

An explicit null and an absent value stay distinguishable for declared members wherever they are stored and read back:
invocation records, the workflow request, variables and outputs (ADR 0091 decision 8).

**Why this priority**: every other story depends on telling the two apart after a suspend or restart.

**Independent Test**: suspend and resume a workflow whose input was supplied as explicit null and whose other input was
omitted; after resume, the first reads explicit null and the invocation record shows the second as defaulted.

**Acceptance Scenarios**:

1. **Given** an explicit null, **When** it is persisted and read back, **Then** it is still an explicit null.
2. **Given** an absent member, **When** it is persisted and read back, **Then** it is still absent, not null.

### Edge Cases

- An explicit `null` for a nullable input that has a declared default stays `null`; defaults apply only to omission.
- An input binding that produces `undefined` and a script activity that completes without returning a value are
  distinct cases (FR-019, FR-020).
- A function-body script that returns on some paths only (`if (x) return y;`) passes publication; the run-time check
  of FR-020 catches the path without a value.
- An expression script or a Script activity whose result is its completion value never needs a `return` statement;
  the undefined policy applies to the value it actually produces (FR-020).
- The default-on-null policy applies to a `null` produced by any source: a literal, a variable, an expression, or a
  request member.
- A required and nullable input accepts an explicit `null`; requiredness concerns presence, not value.
- A non-nullable type without a non-null default (`String`, a JSON object) has no type default, under omission or
  under default-on-null (FR-006, FR-013).
- An undeclared request member is not an omitted optional member. Today's refusal of unknown keys stays.
- A workflow edited after conversion keeps its recorded policies until the author changes them.
- Under undefined-as-null, a `null` that then reaches a non-nullable input still fails unless default-on-null is also
  selected.

## Requirements *(mandatory)*

### Functional Requirements

**Explicit null (D-10)**
- **FR-001**: The system MUST accept an explicit `null` for every nullable target: activity inputs, variables, workflow
  outputs and the `value` of the `Set` / `SetOutput` intrinsics, whether it comes from a literal, a variable or an
  expression.
- **FR-002**: An intrinsic's `value` input MUST take the nullability of its target variable or output, instead of
  always being non-nullable.
- **FR-003**: Without the default-on-null policy, `null` for a non-nullable target MUST fail with `VF-ACT-004`: at
  publication for a literal, at run time otherwise.
- **FR-004**: The Elsa 3 converter MUST emit a typed literal `null` for a null `SetVariable.Value` or
  `SetOutput.OutputValue` whose target is nullable, and MUST keep refusing when the target is non-nullable and no
  default-on-null policy is recorded.

**Omitted members (D-10)**
- **FR-005**: Requiredness and nullability MUST be separate, declared properties of an activity input and of a
  workflow input. A workflow input's nullability MUST be pinned in the executable's input contract and its hash
  (today only requiredness and the default are).
- **FR-006**: An omitted member MUST take its declared default; an optional member without one MUST materialize as
  `null` when nullable, and otherwise as its type's default. A non-nullable type without a non-null default (for
  example `String` or a JSON object) has no type default: such an optional member still needs a declared default, as
  publication requires today (`VF-ACT-003`).
- **FR-007**: An omitted required activity input MUST still fail publication (`VF-ACT-003`).
- **FR-008**: An omitted required workflow input, and an explicit `null` for a non-nullable workflow input without the
  default-on-null policy, MUST be refused at admission, before the start checkpoint, with a diagnostic naming the
  member. No step runs and no work item is scheduled. This holds on every start path (API, triggers, BPMN, test runs,
  child workflows). #3731 already does this for the omitted required input; S-2 adds the explicit-null refusal at the
  same point.
- **FR-009**: After admission, every declared workflow input MUST have a value, so a later read never fails because a
  member was omitted. #3731 fills declared defaults; S-2 adds `null` or the type default for an optional input without
  one (FR-006).

**Null and absent representation (ADR 0091 decision 8)**
- **FR-010**: Declared members MUST keep explicit null and absent distinct by reusing `ValueEnvelope`'s existing
  `Absent` and `ExplicitNull` states; no new representation is introduced.
- **FR-011**: Every store and wire format that persists a declared member MUST round-trip both states unchanged.

**Default-on-null policy (D-10)**
- **FR-012**: A workflow MUST be able to select the default-on-null policy, as one workflow-level setting (Q1). A
  per-input setting is added only once a case needs it.
- **FR-013**: Under the policy, a `null` for a non-nullable target MUST be replaced by the target type's default
  (`default(T)` for value types and enums, Elsa 3's behaviour) (Q2), independently of any declared default, and the
  invocation record MUST show that the value was substituted. A target type without a non-null default (FR-006) still
  fails with `VF-ACT-004`. Requiredness is checked before any default applies (FR-008).
- **FR-014**: The policy MUST never replace a `null` for a nullable target.

**`undefined` at the JavaScript boundary (D-11)**
- **FR-015**: By default, a binding result of `undefined` MUST stay an error with the ADR 0083 guidance, for both
  binding forms.
- **FR-016**: By default, an `undefined`-valued object property MUST be dropped and a warning MUST name its path.
- **FR-017**: `NaN`, `Infinity` and `-Infinity` MUST become `null`, and a warning MUST name their path, under any policy.
- **FR-018**: A workflow MUST be able to select the undefined-as-null policy. Under it, an `undefined` result
  MUST materialize as `null`, and an `undefined`-valued property MUST be kept as `null` at every depth. The policy acts
  only at the boundary: it MUST NOT change JavaScript semantics or rewrite a string `JSON.stringify` produced.
- **FR-019**: An input binding that produces no value MUST be governed by FR-015 and FR-018.
- **FR-020**: A script whose result is consumed (bound to a variable or output) MUST produce a value (Q3), and the
  undefined policy MUST apply to the value each script form actually produces. The forms differ:
  - an **expression script** (a binding, governed by FR-015 and FR-018) and a **Script activity whose result is its
    completion value** produce a value without a `return` statement, and MUST NOT be refused for lacking one;
  - only a **function-body script** (its result is only what a `return` statement gives) whose result is consumed and
    that has no `return` statement MUST be refused at publication.

  In every form, a consumed result that is `undefined` at run time (a path without a value, `return undefined`, or a
  completion value of `undefined`) MUST fault the step without undefined-as-null; under undefined-as-null it MUST be
  `null`, which the binding writes. The publication refusal and the fault name the activity and say to produce a value
  or remove the binding. A script whose result is not consumed MAY produce nothing, and nothing is written. No binding
  is required to contain a `return` statement.

**Policies recorded on the workflow (D-11)**
- **FR-021**: Both policies MUST be recorded on the workflow definition, pinned in the executable, and part of its
  artifact hash (ADR 0038). They MUST NOT depend on whether the workflow was converted.
- **FR-022**: The Elsa 3 converter feature MUST have exactly one option that selects the policies Elsa 3 parity needs
  (undefined-as-null and default-on-null), and MUST record them on every workflow it converts while the option is on.
  The option is on by default (Q4) and acts only on workflows the converter produces: Elsa 3 imports and Elsa 3 JSON
  reconciled at engine start. Native Elsa 4 workflows, new or reconciled, start with both policies off; an author may
  switch either on per workflow.
- **FR-023**: The undefined-to-null lowering spec 262 added to converted expressions MUST be replaced by the recorded
  policy, so the converter no longer rewrites scripts for this purpose.
- **FR-024**: The workflow definition API and the exported workflow JSON MUST expose both policies, so Studio and
  export show them. A dedicated Studio editor is out of scope.

**Computed variable reads (row 10)**
- **FR-025**: A computed variable name that matches no variable MUST read as `undefined` in JavaScript, never `null`,
  and MUST record a warning with the read marked absent.
- **FR-026**: In Liquid, a missing variable MUST read as nil, and the same warning MUST be recorded.
- **FR-027**: A literal variable name that matches nothing MUST remain a publish diagnostic.

**Inputs that cannot hold null (Q5)**
- **FR-029**: A `null` for an input whose type has no non-null default (for example a non-nullable `String`) MUST be
  refused with `VF-ACT-004` and guidance, under any policy. Where an Elsa 4 activity handles `null` correctly, its
  input MUST be declared nullable instead; the built-in activities whose Elsa 3 counterparts accepted `null` are
  audited for this.

**Diagnostics**
- **FR-028**: Every warning and refusal in this spec MUST have a stable diagnostic code and name the member, path or
  variable concerned.

### Key Entities

- **Value state**: for a declared member, one of *absent*, *explicit null* or *value* (`ValueEnvelope`).
- **Member contract**: type, nullability, requiredness and optional declared default of an activity input or workflow
  input.
- **Null policies**: the workflow-level *default-on-null* and *undefined-as-null* settings, recorded on the definition
  and pinned in the executable.
- **Converter parity option**: the one Elsa 3 converter option that records the null policies on converted workflows.
- **Boundary diagnostic**: a warning for a dropped `undefined` property, a `NaN` or `Infinity` made `null`, a
  substituted default, or a computed read that matched nothing.

## Open questions for the approvers

ADR 0045 §2 leaves Q1 to Q4 to this spec; Q5 came from the code survey. Each has a proposal and @j03y-nxxbz's position.
All three approvers answer them on the PR, and the PR's reconciliation table records the answers. @sfmskywalker's
answers were given on his behalf (delegated) on 5 Nov
([comment](https://github.com/elsa-workflows/elsa-foundation/pull/3726#issuecomment-6646543334)), following his
review of #3388; @j03y-nxxbz accepts them and the text applies them. @FransVanEk has not answered on this PR yet.

| # | Question | Proposal (as applied) | @j03y-nxxbz | @sfmskywalker |
|---|---|---|---|---|
| Q1 | Where is default-on-null set: workflow, per input, or both? | Workflow only, until a case needs per input (simplest default, advanced later). | Agrees | Agrees: workflow-level |
| Q2 | Which default does it use: declared or type default? | Type default, independently of declared defaults: it is what Elsa 3 did, and a declared default is about omission, not about a null value. A nullable target keeps explicit `null`; no usable type default means refusal; requiredness precedes defaults. | Agrees | Agrees, with those three conditions (applied in FR-013, FR-014) |
| Q3 | A script whose result is consumed produces nothing: what happens? | Distinguish the forms. An expression script or a Script activity's completion value needs no `return`; only a function-body script with a consumed result and no `return` is refused at publication. The undefined policy applies to the value each form actually produces: a fault without undefined-as-null, `null` under it. An unconsumed result may produce nothing. | Accepts @sfmskywalker's answer (replaces "refuse any bound script without `return`") | Proposed the form distinction |
| Q4 | Is the converter option on by default? | On, for workflows the converter produces only. Native Elsa 4 workflows always start with both policies off; the policies travel with the workflow and are pinned in its executable. | Agrees, with that scope | Agrees |
| Q5 | A `null` for an input that cannot hold null and has no type default (non-nullable `String`): what happens? | Refuse (`VF-ACT-004`) with guidance under any policy; declare the input nullable where an Elsa 4 activity handles `null`; an omitted optional input without a usable type default needs a declared default (FR-006); audit the built-in activities. Rejected: substituting `""` (changes what Elsa 3 did) and passing `null` through (breaks the contract). | Agrees | Agrees |

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All four issues (#3280, #3288, #3172, #3170) can be closed against this spec's acceptance scenarios.
- **SC-002**: `composition-bulk-dispatch-sub-workflow` starts with no input and completes, instead of leaving a
  poisoned work item.
- **SC-003**: The JTest measurement on #3052 converts and publishes at least as many definitions as before, and every
  null `SetVariable` / `SetOutput` refusal from spec 267 with a nullable target is gone.
- **SC-004**: A native workflow and a converted workflow with the same policies give the same outputs for the same
  inputs, in a test that covers every acceptance scenario of US3 and US4.
- **SC-005**: No `undefined` property, `NaN` or `Infinity` is changed at the boundary without a recorded diagnostic.
- **SC-006**: Explicit null and absent round-trip through every store a declared member is persisted in, in tests for
  each provider.

## Assumptions

- Elsa 3 workflow inputs carry no required flag that the converter must honour; converted inputs are optional unless
  research in the plan finds otherwise.
- `ValueEnvelope` already distinguishes absent and explicit null (ADR 0091 decision 8); the plan verifies every store
  keeps the distinction.
- Elsa 4 has no released executables, so changing the executable shape for the policies needs no migration path
  (as ADR 0083's amendment states for profiles).
- S-4 keeps FR-025 to FR-027 when it replaces `getVariable` with run-time reads by name.
