# Feature Specification: Elsa 3 JavaScript host functions: convert what Elsa 4 runs the same, refuse the rest clearly

**Feature Branch**: `2717-elsa3-js-host-functions`

**Created**: 2026-10-19

**Status**: Draft

**Issue**: [#2717](https://github.com/elsa-workflows/elsa-foundation/issues/2717)

**Depends on**:
- Spec 236 (#2706, PR #2805, merged): the converter, the expression rewriter and plan decisions D-14 to D-16
  (research R-11), which refuse every JavaScript construct Elsa 4 cannot run.
- Spec 239 (#2705, PR #2857, merged): Flowchart connections. After it, no built-in map entry raises
  `ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING` (User Story 6).

**Tracking**: ADO 47607. The last prerequisite of spec 235 (Elsa3JsonReconciliation, #2707, draft PR #2708).

**Principle**: faithful or refuse. A construct converts only when Elsa 4 behaves the same. Otherwise the
definition is refused with a stable diagnostic that tells the author how to change the Elsa 3 workflow. No shims and
no complex or risky workarounds.

## Measured baseline (JTest corpus, 2026-10-19, `main` at `aa2cfb683`)

The JTest suite (36 Elsa 3 definitions, not committed) was measured with a throwaway harness, in two ways:

1. **End to end.** All 36 definitions are refused, but only on activity-level diagnostics (for example
   `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`, `ELS3-ACTIVITY-REQUIRED-INPUT-UNBOUND`). No definition reaches expression
   lowering, so the JavaScript refusals are masked. Those activity-level refusals are out of scope here (see
   Out of scope).
2. **Every JavaScript source fed to the expression rewriter directly** (217 sources in 33 definitions, with every
   name resolving):
   - **All 200 expressions fail to parse** (`VF-IMP-007`, "Illegal return statement"). Elsa 3 accepted a top-level
     `return` (`return { … }`, `return false`); the converter's parser does not.
   - With a top-level `return` accepted, the refusals that remain are:

| Construct | Uses | Definitions |
|---|---|---|
| Generated accessors `get{Variable}()` / `get{Input}()` (incl. `getHttpRequestBody()` 104 and `getRequestBody()` 14, which read variables of those names) | 201 | 26 |
| `getCorrelationId()` | 67 | 23 |
| `Date` (6 definitions use it for a `wait(ms)` busy loop) | 24 | 10 |
| `newGuid()`, `newGuidString()`, `newShortGuid()` | 22 | 18 |
| Expression ends with a statement (a trailing `return` or `if`) | 11 | 11 |
| Helpers `toJson`, `isNullOrEmpty`, `isNullOrWhiteSpace`, `parseGuid`, `bytesToString`, `streamToBytes` | 9 | 5 |
| Other unknown calls (`getConfiguration`, `base64Encode`, `setRunJavascriptOutputVariable`, …) | 6 | 3 |
| `setOutcome` / `setOutcomes` | 0 | 0 |

Definitions whose JavaScript would fully convert, cumulatively (first estimate, before research): top-level
`return`, accessors and helpers **4/33**; plus `getCorrelationId` **8/33**; plus `newGuid` **19/33**; plus `Date`
**29/33**. This spec delivers the first two steps; with exact Elsa 3 naming and `toJson` refused, research R-9 expects
**7/33**. The last two stay refused (User Story 5), and the documented migration story says what authors change.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Elsa 3 scripts written with a top-level `return` convert (Priority: P1)

A migrator converts an Elsa 3 definition whose JavaScript expressions use `return` at the top level, the way Elsa 3
allowed. Today every such expression is refused as unparseable. After this feature, the expression converts to an
Elsa 4 expression that returns the same value, including when the value comes from a `return` inside an `if`, or
when the script ends with a statement.

**Why this priority**: It blocks every JavaScript expression in the JTest corpus. Nothing else in this spec is
reachable without it.

**Independent Test**: Convert definitions whose expressions are `return false`, `return { a: 1 }`, and
`if (x) { return 1 } return 2`, and check that each converts and evaluates in Elsa 4 to the value Elsa 3 produced.

**Acceptance Scenarios**:

1. **Given** an expression `return false`, **When** it is converted, **Then** it converts and evaluates to `false`.
2. **Given** an expression whose result comes from `return` statements in different branches, **When** it is
   converted, **Then** each branch returns the same value as in Elsa 3.
3. **Given** an expression whose last statement is an expression (Elsa 3's completion value) and which also has an
   earlier top-level `return`, **When** it is converted, **Then** the converted expression returns what Elsa 3
   returned on every path.
4. **Given** an expression that ends with a statement and has no `return` and no completion value Elsa 4 can
   preserve, **When** it is converted, **Then** it is still refused with `VF-IMP-006` (unchanged from spec 236).
5. **Given** a RunJavaScript body with a top-level `return`, **When** it is converted, **Then** its returned value is
   the script result, as in Elsa 3.

---

### User Story 2 - Generated accessors convert like `getVariable` and `getInput` (Priority: P1)

Elsa 3 generates a function `get{Name}()` for every workflow variable and workflow input. A migrator's scripts call
`getHttpRequestBody()`, `getArrayVariable()` and similar. After this feature, each such call converts to the same
read `getVariable("Name")` or `getInput("Name")` converts to today.

**Why this priority**: 201 uses in 26 of 33 definitions; the largest category.

**Independent Test**: Convert a definition with a variable `HttpRequestBody` and a workflow input `OrderId`, whose
scripts call `getHttpRequestBody().text` and `getOrderId()`, and check that both convert to the same parameter
bindings the explicit `getVariable`/`getInput` forms produce.

**Acceptance Scenarios**:

1. **Given** a variable `HttpRequestBody`, **When** a script calls `getHttpRequestBody()`, **Then** it converts to
   the variable read, exactly as `getVariable("HttpRequestBody")` would.
2. **Given** a workflow input and a variable whose generated accessors have the identical name, **When** a script
   calls the accessor, **Then** it reads the input (Elsa 3 order: an input accessor shadows a variable accessor with
   the identical name; an activity output accessor shadows both and is refused).
3. **Given** a variable `orderId`, **When** a script calls `getOrderId()`, **Then** it converts, because Elsa 3
   generated the variable accessor with a Pascal-cased name. **When** a script calls `getorderId()`, **Then** it is
   refused, because Elsa 3 had no such function (JavaScript names are case-sensitive).
4. **Given** a call `getSomething()` where no variable or input named `Something` exists, **When** it is converted,
   **Then** it is refused as an unknown host function (`VF-IMP-005`), as today.
5. **Given** a script that assigns to a member of an accessor's result (`getOrder().total = 1`), **When** it is
   converted, **Then** it is refused as a mutation of an ambient value (`VF-IMP-005`), the same as
   `getVariable("Order").total = 1` today.
6. **Given** a script that calls a generated setter `set{Name}(…)`, **When** it is converted, **Then** it is refused
   as a mutation (`VF-IMP-005`), with guidance to use a Set activity.

---

### User Story 3 - Correlation ID and workflow instance ID reads convert (Priority: P2)

A migrator's scripts return `{ correlationId: getCorrelationId() }` in HTTP responses, or use `getWorkflowInstanceId()`.
Elsa 4 workflows have both values, but an Elsa 4 expression cannot read them today. After this feature, Elsa 4
expressions can declare a read of the running instance's correlation ID or instance ID, and the converter lowers
the two Elsa 3 functions to it.

**Why this priority**: 67 uses in 23 definitions. It is the single largest blocker after the accessors, and it
needs a small Elsa 4 core capability, not only converter work.

**Independent Test**: Run an Elsa 4 workflow whose expression declares an instance correlation ID read, with a known
correlation ID, and check the expression sees it. Separately, convert `getCorrelationId()` and check that it lowers
to that read.

**Acceptance Scenarios**:

1. **Given** an Elsa 4 workflow instance with correlation ID `c-1`, **When** an expression that declares a
   correlation ID read is evaluated, **Then** it receives `c-1`.
2. **Given** an instance without a correlation ID, **When** the same expression is evaluated, **Then** it receives
   `null`, as Elsa 3's `getCorrelationId()` returned for an uncorrelated instance.
3. **Given** a workflow that sets its correlation ID earlier in the same run, **When** a later expression reads it,
   **Then** it receives the new value, as in Elsa 3.
4. **Given** Elsa 3 scripts calling `getCorrelationId()` or `getWorkflowInstanceId()`, **When** they are converted,
   **Then** they lower to the instance read and evaluate to the same values.
5. **Given** a published Elsa 4 definition that uses the new read, **When** it is validated and executed, **Then** it
   keeps the determinism guarantees of the binding-pure capability profile: the value is bound once per evaluation,
   from instance state, never from ambient time, randomness or environment.

---

### User Story 4 - Pure Elsa 3 helpers convert when their Elsa 4 result is the same (Priority: P3)

A migrator's scripts call `isNullOrEmpty(x)`, `isNullOrWhiteSpace(x)`, `toJson(x)` or `parseGuid(s)`. These
helpers read no host state. After this feature, each converts when Elsa 4 produces the same result for every input
Elsa 3 accepts; a helper that cannot meet that bar stays refused with guidance.

**Why this priority**: 9 uses in 5 definitions, but each is cheap and removes a refusal outright.

**Independent Test**: For each helper, evaluate the converted expression on the same inputs in Elsa 4 and compare
against the results recorded from Elsa 3.

**Acceptance Scenarios**:

1. **Given** `isNullOrEmpty` / `isNullOrWhiteSpace` called on `null`, `""`, `"  "` and `"a"`, **When** converted,
   **Then** the results equal Elsa 3's.
2. **Given** `toJson(value)`, **When** converted, **Then** it stays refused with guidance to use `JSON.stringify`:
   Elsa 3's `toJson` escapes HTML-sensitive characters and keeps .NET property casing, so no Elsa 4 form is equal
   (research R-6).
3. **Given** `parseGuid(s)`, **When** converted, **Then** it stays refused with guidance: it returns a .NET `Guid` and
   accepts five formats, so no simple Elsa 4 form is equal (research R-6).
4. **Given** byte or stream helpers (`bytesToString`, `streamToBytes`, `bytesFromString`), **When** converted,
   **Then** they stay refused: Elsa 4 expression values are JSON and have no byte arrays or streams.

---

### User Story 5 - Constructs Elsa 4 cannot run stay refused, with specific guidance (Priority: P2)

A migrator converts a definition that uses `newGuid()`, `new Date()`, `setOutcome(…)`, lodash, moment, a CLR
global or Elsa 3 `args`. The definition is refused, as today, but the diagnostic names the construct and says what
to change in the Elsa 3 workflow, and a written migration story explains the options.

**Why this priority**: Faithful-or-refuse only works if the refusal is actionable. Today `setOutcome` and
`newGuid` get a generic "unknown or unapproved host function" message.

**Independent Test**: Convert one definition per refused construct and check its code, message and guidance, and
that the migration story covers every refused construct.

**Acceptance Scenarios**:

1. **Given** a call to `newGuid()`, `newGuidString()` or `newShortGuid()`, **When** converted, **Then** the definition
   is refused with `VF-IMP-005` and guidance to use `getWorkflowInstanceId()` when a per-run unique value is enough,
   or to supply the value as a workflow input.
2. **Given** a use of `Date` (construction, `Date.now`, a bare reference), **When** converted, **Then** the definition
   is refused with `VF-IMP-005` and guidance: replace wait loops with a Delay activity, and supply "now" or parsed
   dates as inputs.
3. **Given** a call to `setOutcome` or `setOutcomes` anywhere in a definition, **When** converted, **Then** the whole
   definition is refused with its own message, saying Elsa 4 scripts have no outcomes, with guidance to route with
   a Decision or Switch activity whose conditions carry the script's logic.
4. **Given** any refused construct, **When** converted, **Then** the definition is not converted at all (no partial
   output), as for every other `VF-IMP` error.
5. **Given** `getOutputFrom` with a computed name, lodash `_`, `moment`, a CLR global (`DateTime`, `Guid`,
   `TimeSpan`, `Random`), `Math.random`, or Elsa 3 `args`, **When** converted, **Then** each is refused exactly as
   spec 236 refuses it today (no behavior change).

---

### User Story 6 - Retire the pending-graph diagnostic (Priority: P3)

Since spec 239, no built-in map entry raises `ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING`. A maintainer reading the
diagnostic catalog should see it is retired, not live. Codes are never removed or reused, so the constant stays.

**Why this priority**: Housekeeping, independent of the JavaScript work.

**Independent Test**: Read the diagnostic-code catalog and the constant's documentation; both say "retired" with the
reason and date. The plan records a decision about the code paths that exist only for that diagnostic.

**Acceptance Scenarios**:

1. **Given** the diagnostic-code catalog, **When** a maintainer looks up `ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING`,
   **Then** it is marked retired, with spec 239 as the reason.
2. **Given** the constant in the code, **When** a developer reads its documentation, **Then** it says the code is
   retired, is kept only because codes are never reused, and is not raised by any built-in entry.
3. **Given** the code paths that exist only for the deferred graph conversion, **When** the plan is written, **Then**
   it proposes keeping or removing each, and removes no public surface without the maintainer's approval.

### Edge Cases

- A variable or input whose generated accessor name collides with an Elsa 3 built-in (for example a variable named
  `CorrelationId` makes `getCorrelationId` ambiguous): refused as ambiguous (`VF-IMP-002`), not guessed.
- Two variables whose names differ only in case: an accessor matching both is refused as ambiguous, as
  `getVariable` is today.
- A script declares its own function named like a host function (`function getOrderId() { … }`): it is a local
  call, unchanged from spec 236.
- An accessor called with arguments (`getOrderId(1)`): not an Elsa 3 generated accessor shape; refused.
- An accessor used as a value without a call (`var f = getOrderId;`): refused as a dynamic accessor (`VF-IMP-006`).
- `return` inside a nested function is already legal and must keep its meaning; only the top level changes.
- A Liquid expression is out of scope; Liquid lowering is unchanged.
- The measured corpus definitions also contain unknown calls not listed here (`getConfiguration`, `base64Encode`,
  `setRunJavascriptOutputVariable`): they stay refused as unknown host functions.

## Requirements *(mandatory)*

### Functional Requirements

**Top-level return (User Story 1)**

- **FR-001**: The converter MUST accept Elsa 3 JavaScript that uses `return` at the top level, in expressions and
  RunJavaScript bodies.
- **FR-002**: A converted expression MUST return, on every path, the value Elsa 3 returned: the value of a top-level
  `return`, or else Elsa 3's completion value where spec 236 already preserves it.
- **FR-003**: An expression that has neither a `return` on a path nor a preservable completion value MUST stay
  refused with `VF-IMP-006`.

**Generated accessors (User Story 2)**

- **FR-004**: A call `get{Name}()` with no arguments, where `{Name}` matches a declared workflow input or variable,
  MUST convert to the same parameter binding that `getInput("{Name}")` or `getVariable("{Name}")` produces.
- **FR-005**: Resolution MUST follow Elsa 3's registration order: an output accessor `get{Output}From{Activity}()`
  shadows everything and is refused; then an input accessor; then a variable accessor. An input accessor shadows a
  variable accessor only when the generated names are identical.
- **FR-006**: Names MUST match exactly as Elsa 3 generates them (case-sensitive): `get{Pascalize(variable)}` for
  variables whose name is all letters and digits, `get{input}` verbatim for workflow inputs in a standalone run, and
  `get{Pascalize(input)}` in a reusable run, where Elsa 3 declares inputs as variables. More than one match at the same
  step MUST be refused as ambiguous.
- **FR-007**: A generated name that collides with an Elsa 3 built-in host function MUST be refused as ambiguous.
- **FR-008**: Mutating an accessor's result, or calling a generated setter `set{Name}(…)`, MUST be refused as a
  mutation (`VF-IMP-005`).
- **FR-009**: Output accessors `get{Output}From{Activity}()` are NOT lowered by this spec (no corpus use; resolving
  activity names is a risk); they stay refused (`VF-IMP-005`) with guidance to capture the output into a variable.

**Instance metadata reads (User Story 3)**

- **FR-010**: Elsa 4 expressions MUST be able to declare a read of the running workflow instance's correlation ID
  and instance ID, bound at evaluation time from instance state.
- **FR-011**: The new read MUST keep the binding-pure determinism guarantees: no ambient time, randomness or
  environment; the bound value is part of the evaluation's recorded inputs like any other parameter.
- **FR-012**: An absent correlation ID MUST bind as `null`.
- **FR-013**: The converter MUST lower `getCorrelationId()` and `getWorkflowInstanceId()` to that read.
- **FR-014**: The new read MUST be documented where the other parameter bindings are documented, and listed as an
  extension point if the catalog rules require it.

**Pure helpers (User Story 4)**

- **FR-015**: `isNullOrEmpty` and `isNullOrWhiteSpace` MUST convert to forms that yield Elsa 3's result for every
  string or null input (whitespace as .NET defines it). `toJson` and `parseGuid` MUST stay refused, with guidance,
  because no simple Elsa 4 form is equal (research R-6).
- **FR-016**: Byte and stream helpers MUST stay refused, with guidance that Elsa 4 values have no byte arrays or
  streams.

**Refusals and guidance (User Story 5)**

- **FR-017**: `newGuid`, `newGuidString` and `newShortGuid` MUST be refused with `VF-IMP-005` and guidance that
  names `getWorkflowInstanceId()` and workflow inputs as the Elsa 3 rewrites.
- **FR-018**: Any use of `Date` MUST be refused with `VF-IMP-005` and guidance that names the Delay activity for
  wait loops and workflow inputs for "now" and date values.
- **FR-019**: `setOutcome` and `setOutcomes` MUST be refused with their own message and guidance naming Decision and
  Switch activities.
- **FR-020**: All other spec 236 refusals MUST keep their codes and behavior.
- **FR-021**: The converter documentation MUST carry an Elsa 3 JavaScript migration story: what converts, what is
  refused, and for each refused construct what the Elsa 3 author changes; and it MUST say that workflows which truly
  need the current time or a fresh unique value cannot migrate until Elsa 4 has an explicit capability for them.
- **FR-022**: A follow-up issue for that Elsa 4 time and unique-value capability MUST be proposed (filed only with the
  maintainer's approval).

**Diagnostics stability**

- **FR-023**: No existing diagnostic code may be removed, renumbered or reused. New refusals reuse `VF-IMP-002`,
  `VF-IMP-005` and `VF-IMP-006` with new messages; new codes only if the plan shows an existing code does not fit.

**Cleanup (User Story 6)**

- **FR-024**: `ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING` MUST be marked retired in the diagnostic-code catalog and in
  the constant's documentation, and kept.
- **FR-025**: The plan MUST decide, per item, whether the code paths that exist only for that diagnostic are kept or
  removed, checking spec 235 and the extension-point catalog first; public surface is removed only with the
  maintainer's approval.

### Key Entities

- **Instance metadata read**: a new kind of expression parameter that binds a named property of the running workflow
  instance (correlation ID, instance ID) at evaluation time.
- **Generated accessor**: an Elsa 3 function `get{Name}()` that Elsa 3 created per workflow variable and workflow
  input; converted to the existing variable and workflow-input reads.
- **Refusal guidance**: the stable code, message and Elsa 3 rewrite advice attached to each refused construct, and
  the migration story that collects them.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Measured again with the same method as the baseline, 0 of the corpus's 217 JavaScript sources are
  refused for a top-level `return` (baseline: 200).
- **SC-002**: The JavaScript of at least 7 of the 33 corpus definitions with JavaScript converts with no refusal
  (baseline: 0; research R-9), and every remaining JavaScript refusal in the corpus is one of the deliberate refusals of User
  Story 5 or an unknown call listed in Edge Cases.
- **SC-003**: Every converted construct in this spec has a test that evaluates the converted expression in Elsa 4
  and compares the result with Elsa 3's result for the same inputs.
- **SC-004**: Every refused construct in User Story 5 has a test that checks its code and that its guidance names the
  Elsa 3 rewrite.
- **SC-005**: A migrator can find, in one document, what to change in an Elsa 3 workflow for every refused
  JavaScript construct.
- **SC-006**: Converting the same definition twice gives identical output and diagnostics (spec 236's guarantee
  holds).

## Assumptions

- Elsa 3 (3.5, the corpus's tool version) evaluated JavaScript with top-level `return` allowed; the corpus ran on it.
  The plan verifies the exact Elsa 3 semantics of top-level `return`, generated accessors and each helper against
  the Elsa 3 source before lowering.
- Elsa 3 generated `get{Name}()` for variables and workflow inputs, with inputs shadowing variables (input from spec
  236's review on #2717).
- Elsa 4 workflow instances already carry a correlation ID and an instance ID; only the expression read is missing.
- The value-shape difference between Elsa 3 (mutable CLR values) and Elsa 4 (frozen JSON values) is a known residual
  risk for converted reads; this spec does not emulate CLR mutability, and mutation stays refused.
- Tests use the real activity catalog, as specs 236 and 239 do.

## Out of scope

- The activity-level refusals that currently stop all 36 corpus definitions before expression lowering (for example
  `WriteHttpResponse.content` typed `Object`, unbound `WriteLine.text`, `canStartWorkflow`, `BulkDispatchWorkflows`).
  They belong to spec 235 or their own issues.
- An Elsa 4 capability for current time or fresh unique values (proposed as a follow-up, FR-022).
- Output accessors `get{Output}From{Activity}()` (FR-009) and computed-name helpers.
- Liquid expressions.
