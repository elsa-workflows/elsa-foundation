# Feature Specification: Elsa 3 converter: workflow variables that can be published

**Feature Branch**: `agent/claude/3122-elsa3-workflow-variable-publish`

**Created**: 2026-10-21

**Status**: Implemented

**Issue**: [#3122](https://github.com/elsa-workflows/elsa-foundation/issues/3122)

**Input**: An Elsa 3 workflow that declares a workflow-level variable imports without a diagnostic, and publishing
the imported version then fails with `400 Activity node 'workflow' input '<variable>' does not declare an expression
type`. It fails whatever the variable's type, and even when nothing reads the variable.

**Depends on**:
- Spec 236 (#2706, merged): the converter, including the variable mapping.
- Spec 258 (#3107, merged): an Event's `Result` converts to `Payload`, which the integration test captures.

**Principle**: faithful or refuse. A variable converts only where it starts in Elsa 4 with the value it had in Elsa 3;
otherwise the converter refuses with a stable diagnostic and guidance on what to change in the Elsa 3 workflow.

**Boundary**: converter only. Publication and the runtime do not change.

## Root cause

The converter gives every variable a starting value without saying what kind of value it is: an untyped binding
holding the Elsa 3 value, or holding nothing when the Elsa 3 variable had no value. Publication compiles every
starting value it is given and reads an untyped one as an expression, which it refuses because no expression language
is named. Variables that Elsa 4 itself authors either carry no starting value or mark it as a literal.

How Elsa 3 (3.6) gave a workflow variable its starting value, from its source:
- The definition stores the value as text, or no value.
- No value: the variable starts as `null`.
- Text, for an `Object` variable: the text itself, unparsed.
- Text, for a primitive or `String` variable: the text converted to that type (`"5"` → `5`, `"true"` → `true`).
- Text that starts with `{` or `[`, for any other type (a list, a dictionary, a model type): the text parsed as JSON
  into that type.
- Text a primitive cannot parse (`"abc"` for an `Int32`): the type's default (`0`), silently. Elsa 3's converter ran
  in non-strict mode.
- Malformed JSON for a structured type: a logged warning, and the variable starts as `null`.

## Measured baseline (JTest corpus, 2026-10-21, `main` at `4d4f6d2b2`)

Two throwaway tests, deleted afterwards. One converts the 36 definitions with the real activity catalog, as in specs
247, 248 and 262. The other imports each definition that converts into the `Elsa3ImportHost` and publishes it.

- 8 of 36 definitions convert end to end (spec 256's expected number).
- 71 workflow variables in 28 definitions; **none has a starting value**. No container declares a variable.
- 5 of the 8 that convert declare a workflow variable, so each of them hits #3122.
- **2 of the 8 that convert publish** (performance-baseline-dispatch-sub, simple-write-hello-world). Publishing stops
  at the first error, and for the other 6 it reports a different one first:

| Publish error | Definitions |
|---|---|
| An activity input (`possibleOutcomes`) with no expression type | composition-bulk-dispatch-sub-workflow, performance-bigdata |
| A `Variable` binding with no reference key | events-listener, performance-commit-after-every-activity |
| `VF-COER-001`: `Headers` value `{}` cannot become `System.String[]` | javascript-array-methods, javascript-object-methods |

Found later, in the same family (2026-10-22): an imported RunJavaScript activity whose script takes no arguments
publishes, then faults at run time with `JavaScript activity arguments must be one JSON object`. The converter leaves
the Elsa 4 `arguments` input unset, and the evaluator requires a JSON object.

These four are converter defects outside this unit's scope (FR-005 and the boundary keep this unit to variable
starting values). They are reported to the maintainer as follow-ups. The conversion measurement cannot see any of them,
because it never publishes.

## Revised after review (2026-10-22)

Code Review approved PR #3180 with non-blocking notes. The maintainer settled them in a follow-up PR on this spec:
- **Collection items** are read with Elsa 3's JSON options: enums by name, property names case-insensitive. Elsa 3 had
  no "numbers from strings" option, so `["5"]` for `List<Int32>` stays refused, as in Elsa 3. The result is written for
  Elsa 4 with default options, so no Elsa 3 option reaches Elsa 4.
- **A type Elsa 4 does not know becomes `Object`.** That covers a nested collection, `ObjectDictionary[]`, a custom
  class or an unregistered enum. A JSON starting value (`{…}` or `[…]`) is held as parsed JSON. Other text is refused,
  because Elsa 3 converted it in a way the converter cannot know.
- **Resources are refused** with or without a starting value: `FormFile`, `IFormFile`, `Stream` and `byte[]`, alone or
  as a collection element. #3194 now covers them all. `HttpHeaders` is not a resource: it stays `Object` holding a JSON
  object.
- **The diagnostic code stays** `ELS3-ACTIVITY-VARIABLE-VALUE-UNCONVERTIBLE`, for workflow-level variables too.

## Measured after (JTest corpus, 2026-10-21, this branch)

Same throwaway method as the baseline, deleted afterwards.

| Measure | Baseline | After |
|---|---|---|
| Definitions that convert end to end | 8 | 8 (the same eight) |
| Workflow variables refused by the new rule | n/a | 1: `FormFiles` (`FormFile[]`, #3194) in http-endpoints, which already failed conversion. No corpus variable has a starting value |
| Converting definitions that publish | 2 | 2 |

The 6 that still do not publish stop at the same first errors as in the baseline, all outside this unit. Publishing
reports only its first error, so the corpus cannot show the #3122 fix on its own. The integration tests show it
(`WorkflowVariablePublishTests`), and the follow-ups above remove the errors that hide it.

## Clarifications

### Session 2026-10-21

- Q: The Elsa 3 variable has a value that cannot be converted to its type (Elsa 3 logged a warning and started the
  variable as `null`). Refuse, or convert with no starting value and a warning? → A: Refuse. The value is almost
  certainly an authoring mistake, and a refusal tells the author which variable to fix in the Elsa 3 workflow.
- Q (during implementation, after reading Elsa 3's converter): Elsa 3 silently started an `Int32` with `"abc"` as `0`,
  the type's default, with no warning. Only malformed JSON for a structured type gave a warning and `null`. Convert the
  first faithfully to the default, or refuse both? → A: Refuse both. The converter converts in strict mode.
- Found during implementation: Elsa 4 reserves bare aliases, so Elsa 3's `List<String>`, `ObjectDictionary` and
  `HttpHeaders` cannot be registered. Neither can an enum or CLR type that no host registers. Such a variable runs as
  `Object` in Elsa 4, and its value would stay text where Elsa 3 converted it. Under the same rule, a starting value on a
  type Elsa 4's registry does not know is refused. A variable with no starting value still converts.
- Q (2026-10-22, PR #3180): Elsa 3 collections and dictionaries with a starting value: refuse them, or convert them?
  → A (maintainer): convert, and never split text on commas.
  - Elsa 3 writes a list as `List<T>` and an array as `T[]`. When Elsa 4 knows `T`, the variable becomes `T` with
    collection kind List or Array, and every item converts to `T`.
  - A collection of a type Elsa 4 does not know (`FormFile[]`: uploaded files) is refused, with or without a starting
    value. It needs its own spec: #3194.
  - The starting value must be a valid JSON array. Anything else is refused with guidance showing the exact JSON array.
  - `ObjectDictionary` and `HttpHeaders` become `Object` holding a JSON object; anything else is refused.
  - This matches #3191's rule for Elsa 4 itself: a collection default is always a JSON array.

## User Scenarios & Testing *(mandatory)*

The actor is a workflow author or operator who brings Elsa 3 workflow definitions (JSON) into Elsa 4 through the
collection import, then publishes and runs them.

### User Story 1 - An imported workflow with a variable publishes and runs (Priority: P1)

An author imports an Elsa 3 workflow that declares a workflow variable with no starting value and captures an activity
output into it, for example an Event's payload. They publish the imported version and run it. They expect publishing
to succeed and the variable to hold the captured value, as it did in Elsa 3.

**Why this priority**: Every imported workflow with a variable cannot be published today, which is most real workflows.

**Independent Test**: Import an Elsa 3 workflow with a workflow variable and no starting value, whose Event output is
captured into the variable. Publish it, run it, publish the event, and read the variable's value back.

**Acceptance Scenarios**:

1. **Given** an imported Elsa 3 workflow with a workflow variable that has no value, **When** it is published, **Then** publishing succeeds.
2. **Given** that workflow published, **When** it runs and the Event receives a payload, **Then** the variable holds the payload.
3. **Given** a variable that nothing reads or writes, **When** the workflow is published, **Then** publishing succeeds.

---

### User Story 2 - A variable with a starting value starts with the value it had in Elsa 3 (Priority: P1)

An author imports an Elsa 3 workflow whose variables have starting values: a `String`, an `Int32`, a `Boolean`, an
`Object` holding JSON text, a list or dictionary holding JSON text. They expect each to start in Elsa 4 with the same
value as in Elsa 3, or the import to say which variable cannot be carried over and why.

**Why this priority**: A value that publishes but starts differently changes the workflow's behaviour silently.

**Independent Test**: Import a workflow with one variable per type above, each with a starting value, publish and run
it, and read every variable's starting value back; compare each with Elsa 3's rule in Root cause.

**Acceptance Scenarios**:

1. **Given** a `String`, number or `Boolean` variable with a starting value, **When** the workflow is imported, published and run, **Then** the variable starts with the value converted to its type, as in Elsa 3.
2. **Given** an `Object` variable whose starting value is JSON text, **When** the workflow runs, **Then** the variable starts with that text, unparsed, as in Elsa 3.
3. **Given** a list, dictionary or other structured variable whose starting value is JSON text, **When** the workflow runs, **Then** the variable starts with the parsed value, as in Elsa 3.
4. **Given** a variable whose Elsa 3 starting value cannot be converted to its type (for example `"abc"` for an `Int32`), or cannot be represented the same way in Elsa 4, **When** it is imported, **Then** the definition is refused with a stable diagnostic naming the variable, with guidance in Elsa 3 terms, and no version is produced that fails at publish.

---

### Edge Cases

- An empty-string starting value: a `String` variable starts as `""`; for other types it follows Elsa 3's conversion
  rule.
- A variable with an array flag (`isArray`) and a starting value.
- A variable whose type the converter cannot resolve already falls back to `Object`; its starting value follows the
  `Object` rule.
- A variable declared on a container activity instead of the workflow keeps its existing refusal
  (`ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE`).
- A reusable activity (`usableAsActivity: true`) with variables goes through the same variable mapping and must get the
  same result.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A converted variable whose Elsa 3 definition has no starting value MUST have no starting value in Elsa 4.
- **FR-002**: A converted variable whose Elsa 3 starting value converts in Elsa 4 to the same value as in Elsa 3 MUST
  carry it as authored data that publication accepts, never as an expression.
- **FR-003**: The starting value in Elsa 4 MUST follow Elsa 3's rule in Root cause for each kind of variable type. A
  collection's starting value MUST be a JSON array and a dictionary's a JSON object; text is never split on commas
  (clarification 2026-10-22).
- **FR-004**: The converter MUST refuse a variable whose Elsa 3 starting value cannot be converted to its type in
  strict mode, or whose type Elsa 4's type registry does not know, with a stable diagnostic that names the variable and
  gives guidance in Elsa 3 terms.
- **FR-005**: Publication, the runtime and the Elsa 4 variable model MUST NOT change.
- **FR-006**: An integration test MUST import an Elsa 3 workflow with a workflow variable, capture the Event payload
  into it, publish and run the workflow, and read the value back.
- **FR-007**: Every starting-value kind of FR-003 and every refusal of FR-004 MUST have a test.
- **FR-008**: The Mapping README's migration story MUST describe how variable starting values convert.

### Key Entities

- **Workflow variable**: a named, typed value that belongs to the workflow; it may have a starting value and may
  receive activity outputs.
- **Starting value**: the value a variable holds when the workflow starts, written as text in Elsa 3.
- **Conversion diagnostic**: a refusal with a stable code, a message and guidance in Elsa 3 terms.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An imported Elsa 3 workflow with a workflow variable publishes, runs, and the variable holds the captured
  Event payload.
- **SC-002**: For every variable type kind in User Story 2, the variable starts in Elsa 4 with the value Elsa 3 gave
  it, or the import reports a diagnostic; none fails at publish.
- **SC-003**: The existing Elsa 3 test suites stay green, and the JTest definitions that convert end to end today still
  convert.
- **SC-004**: Publishing every JTest definition that converts end to end and declares a workflow variable is measured
  before and after, and the result recorded in this spec.

## Assumptions

- Elsa 3's behaviour is taken from the 3.6 source (`VariableMapper`, `ObjectConverter`). The JTest corpus's tool
  version is 3.5; the variable rule did not change between them (to be confirmed in planning).
- A variable with no starting value starts as `null` in Elsa 4, as in Elsa 3.
- Diagnostics reuse an existing `ELS3-*` or `VF-IMP-*` code where one fits; a new code is added only if none does (to
  be confirmed in planning).
- Out of scope: Elsa 4 changes of any kind, a `null` bound to a non-nullable input (#3172), and engine-start
  reconciliation (spec 235).
