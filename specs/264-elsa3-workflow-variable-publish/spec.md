# Feature Specification: Elsa 3 converter: workflow variables that can be published

**Feature Branch**: `agent/claude/3122-elsa3-workflow-variable-publish`

**Created**: 2026-10-21

**Status**: Draft

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
- Text that cannot be converted: a logged warning, and the variable starts as `null`.

## Clarifications

### Session 2026-10-21

- Q: The Elsa 3 variable has a value that cannot be converted to its type (Elsa 3 logged a warning and started the
  variable as `null`). Refuse, or convert with no starting value and a warning? → A: Refuse. The value is almost
  certainly an authoring mistake, and a refusal tells the author which variable to fix in the Elsa 3 workflow.

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
- **FR-003**: The starting value in Elsa 4 MUST follow Elsa 3's rule in Root cause for each kind of variable type.
- **FR-004**: The converter MUST refuse a variable whose Elsa 3 starting value cannot be converted to its type, or
  cannot start with the same value in Elsa 4, with a stable diagnostic that names the variable and gives guidance in
  Elsa 3 terms.
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
