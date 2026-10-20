# Feature Specification: Expressions read the current time and unique values through recorded parameters

**Feature Branch**: `agent/claude/3026-deterministic-expression-capability`

**Created**: 2026-10-20

**Status**: Draft

**Issue**: [#3026](https://github.com/elsa-workflows/elsa-foundation/issues/3026)

**Decision record**: [ADR 0088](../../docs/adr/0088-expressions-read-time-and-unique-values-through-recorded-parameters.md),
decisions D1 to D4. D5 (the Elsa 3 converter) is a separate follow-up.

**Input**: Specify ADR 0088 D1 to D4: `Clock` and `UniqueId` expression parameter kinds recorded with the input
snapshot, the rule for when a value is fixed, one value per declared parameter, and a `binding-pure-v2` JavaScript
profile with a `Date` that cannot read the clock and whose removed members fail with clear guidance. Executables
stamped `binding-pure-v1` are unchanged and Liquid stays on `binding-pure-v1`.

## Why

Elsa 4 expressions have no clock and no randomness, so that the runtime can evaluate an expression again for the same
step (retry, replay, re-materialization) and get the same result. Workflows that need the current time or a fresh ID
have no sanctioned way to get one. In the JTest corpus, time or randomness is the only JavaScript refusal left for 19
of the 33 definitions that use JavaScript. ADR 0045 §5 allows these values only as pinned parameters or a separately
specified deterministic capability; this feature is that capability.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Stamp a step with the current time (Priority: P1)

A workflow author needs the time at which a step runs, for example to stamp a response or a record. They declare a
`Clock` parameter on the expression and read it as `args.now`. Every evaluation of that step's inputs sees one instant,
and a retry of the step sees the same instant as its first attempt.

**Why this priority**: It is the most common need ("now") and the one with no workaround today except a workflow input.

**Independent Test**: Run a workflow whose activity input is a JavaScript expression returning `args.now`, make the
activity fault once and retry, and compare the recorded input values of both attempts.

**Acceptance Scenarios**:

1. **Given** an expression with parameter `now` bound to `Clock`, **When** the activity's inputs are materialized,
   **Then** `args.now` is an ISO 8601 UTC string with millisecond precision equal to the snapshot's materialization
   time.
2. **Given** an expression with two `Clock` parameters, **When** inputs are materialized, **Then** both carry the same
   value.
3. **Given** an activity whose first attempt faults after its inputs were committed, **When** it is retried, **Then**
   the retry sees the same `Clock` value as the first attempt.

---

### User Story 2 - Generate unique IDs for a step (Priority: P1)

A workflow author needs one or more new unique IDs in a step, for example a correlation or tracking ID. They declare
one `UniqueId` parameter per ID they need. Each parameter gets its own value, and a retry sees the same values.

**Why this priority**: It covers 22 uses in 18 corpus definitions and is the other half of the capability.

**Independent Test**: Run an activity whose input declares two `UniqueId` parameters and returns both; check that they
differ, are valid lowercase UUID version 7 strings, and are unchanged on retry.

**Acceptance Scenarios**:

1. **Given** an expression with parameters `a` and `b` bound to `UniqueId`, **When** inputs are materialized, **Then**
   `args.a` and `args.b` are different lowercase hyphenated UUID version 7 strings.
2. **Given** an activity retried after its inputs were committed, **When** the retry evaluates, **Then** it sees the
   same IDs as the first attempt.

---

### User Story 3 - Loops see time move (Priority: P1)

A workflow author writes a `While` loop whose condition reads `args.now`, for example to stop after a deadline. Each
pass of the loop sees a new time and new IDs, as in Elsa 3, and a pass whose decision has committed is never evaluated
again.

**Why this priority**: Without it a deadline loop never ends, so freezing values per invocation is not an option
(ADR 0088 D2).

**Independent Test**: Run a `While` whose condition compares `args.now` with a deadline under a controllable clock;
advance the clock between passes and check that the loop exits; redeliver a child completion after a committed pass and
check that the condition is not evaluated again.

**Acceptance Scenarios**:

1. **Given** a `While` condition with a `Clock` parameter, **When** two passes run at different times, **Then** each
   pass's condition sees its own pass's time.
2. **Given** a `While` condition with a `UniqueId` parameter, **When** two passes run, **Then** each pass sees a
   different ID.
3. **Given** a pass whose decision has committed, **When** the child-completion message that triggered it is delivered
   again, **Then** the condition is not evaluated again and the committed decision stands.

---

### User Story 4 - Work with dates in JavaScript without reading the clock (Priority: P2)

A workflow author parses, compares, adds to and formats dates in JavaScript: `new Date(args.now)`,
`new Date(args.dueDate).getTime() - new Date(args.now).getTime()`, `toISOString()`. This works the same on every host,
because the time zone is UTC and only ISO 8601 strings parse.

**Why this priority**: Without `Date`, a `Clock` value is only a string; with it, the corpus's date uses convert.

**Independent Test**: Evaluate a set of expressions under `binding-pure-v2` that use every allowed `Date` member and
compare the results with expected UTC values, on hosts with different local time zones and cultures.

**Acceptance Scenarios**:

1. **Given** a `binding-pure-v2` expression `new Date("2026-10-20T08:30:00.000Z").toISOString()`, **When** it is
   evaluated, **Then** it returns `"2026-10-20T08:30:00.000Z"`.
2. **Given** a host whose local time zone is not UTC, **When** an expression calls `getHours()` on a date, **Then** the
   result equals `getUTCHours()`.
3. **Given** a non-ISO date string such as `"10/20/2026"`, **When** it is passed to `new Date(...)`, **Then** the
   result is an invalid date, and a later `toISOString()` reports that only ISO 8601 date-time strings parse.

---

### User Story 5 - Clear diagnostics for what is not allowed (Priority: P2)

A workflow author writes `new Date()`, `Date.now()`, `Date()` or `toLocaleString()` under `binding-pure-v2`. Instead of
a generic "undefined is not a function", they get a message that names the member, says why it is not allowed and
tells them what to write instead. Authoring validation reports the same message before publish where it can find the
form without running the source, and the documentation lists every allowed, removed and constrained member with its
rewrite.

**Why this priority**: The owner made clear diagnostics a condition of the `Date` decision.

**Independent Test**: Evaluate each removed form and check the exact message; validate each removed form in the
authoring validator and check that the same message is reported with its location.

**Acceptance Scenarios**:

1. **Given** `new Date()` with no argument, **When** it is evaluated, **Then** it fails with the documented message
   that suggests declaring a `Clock` parameter and writing `new Date(args.now)`.
2. **Given** `Date.now()`, **When** it is evaluated, **Then** it fails with the documented message that suggests
   `Date.parse(args.now)`.
3. **Given** `Date()` called as a function, **When** it is evaluated, **Then** it fails with the documented message
   for that form.
4. **Given** `toLocaleString()`, `toLocaleDateString()` or `toLocaleTimeString()`, **When** it is evaluated, **Then**
   it fails with the documented message that suggests `toISOString()` or the UTC getters.
5. **Given** a source containing `Date.now()`, **When** it is validated for authoring, **Then** the validator reports
   the same message at the authored line and column without evaluating the source.

---

### User Story 6 - Existing executables keep their behavior (Priority: P1)

An executable published before this feature is stamped `binding-pure-v1`. It keeps evaluating exactly as before:
`Date` stays undefined for it, and its fingerprint is unchanged. Newly published JavaScript expressions are stamped
`binding-pure-v2`. Liquid expressions keep `binding-pure-v1`.

**Why this priority**: ADR 0083 pins what an executable means at publish; a profile change must not alter pinned
executables.

**Independent Test**: Evaluate the same `Date`-using source under both profiles and check that v1 fails as before and
v2 succeeds; publish a new JavaScript and a new Liquid expression and check their stamped profiles.

**Acceptance Scenarios**:

1. **Given** an executable stamped `binding-pure-v1` whose expression uses `Date`, **When** it is evaluated, **Then**
   it fails exactly as it did before this feature.
2. **Given** a JavaScript expression published after this feature, **When** its definition is inspected, **Then** it
   is stamped `binding-pure-v2`.
3. **Given** a Liquid expression published after this feature, **When** its definition is inspected, **Then** it is
   stamped `binding-pure-v1`.
4. **Given** an existing `binding-pure-v1` expression without the new kinds, **When** its fingerprint is computed,
   **Then** it equals the fingerprint before this feature.

### Edge Cases

- **Replay of a segment that never flushed**: a coalesced segment lost in a crash is evaluated again and may get new
  `Clock` and `UniqueId` values; nothing from the lost segment was observable, so this is correct (ADR 0088 D2).
- **Inspection timestamps**: the inspection record of a `While` pass has its own timestamp; `Clock` must equal the
  materialization time, never that inspection timestamp.
- **Graph inputs and outputs**: `GraphActivity` evaluates its input and output mappings with literal parameters only,
  so `Clock` and `UniqueId` cannot reach it; it stays on `binding-pure-v1`.
- **A context without an instant**: a defensive failure with a stable message (FR-012), never a live clock read.
- **Plain-source JavaScript**: it declares no parameters, so it cannot use `Clock` or `UniqueId`, but under
  `binding-pure-v2` it can use the clockless `Date` on values it reads.
- **Liquid**: a Liquid expression may declare `Clock` and `UniqueId` parameters and receives the same string values;
  its time filters stay unavailable.
- **An older runtime**: a runtime that predates the new kinds or the new profile cannot read an executable that uses
  them; before GA this is accepted and documented, not mitigated.
- **`Date` setters and arithmetic overflow**: invalid results follow ECMAScript (an invalid date), not a host error.

## Requirements *(mandatory)*

### Functional Requirements

**Parameter kinds (ADR 0088 D1, D3)**

- **FR-001**: An expression parameter MUST be able to declare the kind `Clock` (`{"kind":"Clock"}`) with no other
  members.
- **FR-002**: An expression parameter MUST be able to declare the kind `UniqueId` (`{"kind":"UniqueId"}`) with no other
  members.
- **FR-003**: A `Clock` parameter MUST bind the materialization time of the inputs that contain it, as an ISO 8601 UTC
  string with millisecond precision (`yyyy-MM-ddTHH:mm:ss.fffZ`). All `Clock` parameters in one materialization MUST
  bind the same value.
- **FR-004**: A `UniqueId` parameter MUST bind a new UUID version 7 as a lowercase hyphenated string. Each `UniqueId`
  parameter in one materialization MUST bind a different value.
- **FR-005**: Both kinds MUST be valid under every capability profile and in every expression language that accepts
  parameters, MUST carry no dependency policy and MUST NOT be treated as sensitive.
- **FR-006**: Both kinds MUST round-trip through the canonical wire format, and MUST contribute to the expression
  fingerprint so that a definition using them differs from one that does not. Existing expression fingerprints MUST
  NOT change.

**Recording (ADR 0088 D2)**

- **FR-007**: `Clock` and `UniqueId` values MUST be pinned through the inputs evaluated from them: the input snapshot
  (or, for an intrinsic, its committed result) records the evaluated value, and no separate store records the
  parameter values.
- **FR-008**: A retry of a faulted CLR activity invocation (the `RetryFaultedActivity` alteration, or any redelivery
  of a started invocation) MUST read the committed input snapshot and therefore see the same values.
- **FR-008a**: An engine intrinsic that is retried before its completion commit has no committed result, so it MUST be
  evaluated again, with a new instant and new IDs; ADR 0088 D2 allows this because nothing depended on the lost
  values.
- **FR-009**: A committed input snapshot MUST NOT be rebuilt; reading it back MUST return the recorded values.
- **FR-010**: Each `While` pass MUST materialize its own values, and the `Clock` value of a pass MUST equal the
  materialization time used for that pass.
- **FR-011**: A child-completion message delivered again after the pass it triggered has committed MUST NOT evaluate
  the condition again.
- **FR-012**: Every runtime site that resolves declared expression parameters MUST supply the evaluation instant and
  MUST fix the evaluated result with a commit: activity input materialization (including each `While` and `Do` pass)
  and engine intrinsics (`Set`, `Control` and the other intrinsic inputs). Resolving a `Clock` or `UniqueId` parameter
  without an instant MUST fail with a stable inner message (`VF-EXP-CLOCK-001`) naming the parameter, node and input,
  wrapped like every other input-materialization failure, and MUST never fall back to reading the clock.

**`binding-pure-v2` (ADR 0088 D4)**

- **FR-013**: A JavaScript capability profile `binding-pure-v2` MUST exist and MUST provide everything
  `binding-pure-v1` provides plus `Date` as follows.
- **FR-014**: Under `binding-pure-v2`, these `Date` members MUST work as ECMAScript specifies: the constructor with an
  ISO string, with epoch milliseconds and with date parts; `Date.UTC`; `Date.parse`; the getters and setters; `getTime`;
  `valueOf`; `toISOString`; `toJSON`; `toString`; and comparison and arithmetic on dates.
- **FR-015**: Under `binding-pure-v2`, the engine time zone MUST be UTC, so local getters equal UTC getters and
  `toString` is the same on every host regardless of its time zone and culture.
- **FR-016**: Under `binding-pure-v2`, strings MUST parse as dates only in the ECMAScript date-time string format; any
  other string MUST produce an invalid date.
- **FR-017**: Under `binding-pure-v2`, these forms MUST fail with a `TypeError` whose message names the form, says why
  it is not allowed and gives the rewrite, surfaced to the caller as a `JavaScriptBindingEvaluationException` whose
  message is that text: `new Date()` with no argument, `Date()` called as a function, `Date.now()`,
  `toLocaleString()`, `toLocaleDateString()` and `toLocaleTimeString()`. The messages MUST be exactly the texts in
  [the contract](contracts/clock-uniqueid-and-binding-pure-v2.md#messages), which follow ADR 0088 D4's examples, and calling `toISOString()` on an invalid date MUST fail
  with a `RangeError` saying that only ISO 8601 date-time strings parse.
- **FR-018**: `Temporal`, `Intl` and `Math.random` MUST stay unavailable under `binding-pure-v2`.
- **FR-019**: Authoring validation MUST report the FR-017 messages, at the authored line and column, for each removed
  form it can identify without evaluating the source.
- **FR-020**: The editor's declared globals MUST NOT change. The editor's standard library already declares `Date`
  (including the removed members), and a declaration cannot remove members from it; the removed forms are reported by
  authoring validation (FR-019) and the documentation (FR-025) instead.

**Versioning (ADR 0088 D4)**

- **FR-021**: A JavaScript expression authored as plain source MUST be stamped `binding-pure-v2` when it is published. An
  expression authored as a full portable definition keeps the profile it declares, so `binding-pure-v1` remains
  selectable. Reading an existing executable MUST keep the profile it was stamped with. As a consequence, republishing
  an unchanged plain-source JavaScript definition once produces a new executable hash (the expression fingerprint
  of a definition that already declares its profile is unchanged).
- **FR-022**: Liquid expressions MUST stay stamped `binding-pure-v1`, and a Liquid expression stamped
  `binding-pure-v2` MUST be rejected.
- **FR-023**: The runtime MUST select the JavaScript sandbox surface from the stamped profile: an expression stamped
  `binding-pure-v1` MUST behave exactly as before this feature.
- **FR-024**: An unknown profile MUST be rejected with a message naming the profile, as today.

**Documentation**

- **FR-025**: The documentation MUST list, for `binding-pure-v2`, every allowed, removed and constrained `Date` member,
  with the rewrite for each removed form, and MUST describe the `Clock` and `UniqueId` kinds and the rule for when
  their values are fixed.

### Key Entities

- **`Clock` parameter binding**: a declared expression parameter whose value is the materialization time of the inputs
  it belongs to.
- **`UniqueId` parameter binding**: a declared expression parameter whose value is a new UUID version 7.
- **Input snapshot**: the committed record of an invocation's materialized inputs; it now also holds `Clock` and
  `UniqueId` values.
- **Capability profile**: the name stamped on every expression that fixes its sandbox surface; `binding-pure-v2` is
  new.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of retry, replay, `While` and redelivery test runs, every observer of a committed step sees the
  same `Clock` and `UniqueId` values.
- **SC-002**: Across at least 1,000 generated values in tests, no two `UniqueId` values are equal, and every value is a
  valid UUID version 7.
- **SC-003**: Every one of the six removed `Date` forms produces its documented message, verified by a test per form,
  and every allowed `Date` member is covered by at least one test.
- **SC-004**: The same `binding-pure-v2` date expressions give identical results under at least three different
  current cultures, and the v2 engine's time system never consults the host's local time zone (its default zone is
  UTC and its offset is always zero).
- **SC-005**: No stored executable's hash or behavior changes, and every existing `binding-pure-v1` evaluation test
  still passes unchanged. Tests that pin the profile stamped for newly compiled plain-source JavaScript, or a golden
  executable hash that includes it, are updated with that reason.
- **SC-006**: Each `Date` and ID pattern in the JTest corpus except the `wait(ms)` loops and `newShortGuid()` can be
  written as a `binding-pure-v2` expression with `Clock` and `UniqueId` parameters, verified by one test per pattern:
  stamp now, ISO round-trip, parse a request date, elapsed milliseconds between two dates, and one or more new IDs.

## Out of Scope

- The Elsa 3 converter (ADR 0088 D5): lowering `newGuid()`, `newGuidString()`, `new Date()` and `Date.now()`, refusing
  `newShortGuid()` and calls inside loops, and the mapping README. A needs-triage follow-up issue covers it.
- Liquid date filters.
- An activity that produces the time or an ID.
- A Studio control for choosing these parameter kinds; this spec covers the canonical definition, the runtime, the
  sandbox, validation and the editor's declared globals.

## Assumptions

- The runtime's injected time provider is the source of the materialization time, so tests can control it.
- UUID version 7 generation uses the platform's standard implementation; IDs are not secrets.
- Elsa 4 is pre-GA (ADR 0080), so an older runtime that cannot read the new kinds or profile is accepted.
- The `While` commit marker verified on `947cb78e1` (`ParentCompletionProcessedWorkItemId`) is the guard FR-011 relies
  on.
