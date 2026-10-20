# Feature Specification: Elsa 3 converter: undefined results and strict mode

**Feature Branch**: `agent/claude/3012-elsa3-undefined-strict`

**Created**: 2026-10-20

**Status**: Draft

**Issue**: [#3012](https://github.com/elsa-workflows/elsa-foundation/issues/3012). The scope is the
[triage comment](https://github.com/elsa-workflows/elsa-foundation/issues/3012#issuecomment-6291828214): its decision,
acceptance criteria and open questions.

**Input**: Two differences between the Elsa 3 and Elsa 4 JavaScript engines that let a converted expression pass
conversion and then fail or behave differently at run time: Elsa 4 rejects an `undefined` binding result where Elsa 3
returned `null`, and Elsa 4 evaluates in strict mode where Elsa 3 used sloppy mode.

**Depends on**:
- Spec 236 (#2706, merged): the converter and its JavaScript lowering (plan D-14 to D-16).
- Spec 247 (#2717, merged): top-level `return` (research R-1), host functions and the analyzer's scope tracking.
- Spec 248 (#3051, merged): the JTest measurement method this spec reuses.

**Principle**: faithful or refuse. A construct converts only where Elsa 4 behaves the same; otherwise the converter
refuses with a stable diagnostic and guidance on what to change in the Elsa 3 workflow. No complex workarounds.

**Boundary**: converter only. Elsa 4 does not change: ADR 0083 keeps an `undefined` binding result an error, and
binding evaluation stays strict.

## Measured baseline (JTest corpus, 2026-10-20, `main` at `d628fc0da`)

The 36 JTest definitions (not committed) were measured with a throwaway harness, deleted afterwards, using the spec
247/248 method:

1. **End to end** (`ConversionServices.ConvertAsync` against the real activity catalog): 6 of 36 convert, as spec 248
   recorded. 5 have no diagnostic; performance-commit-after-every-activity carries the For step-0 behaviour warning.
2. **Every JavaScript source fed to the rewriter directly**, with every name resolving: 217 sources in 33 definitions
   (200 expressions, 17 RunJavaScript scripts). 121 rewrite (112 expressions, 9 scripts). Every refusal is a known
   spec 247 refusal (`getCorrelationId` ambiguity, `Date`, `newGuid*`, `toJson`, byte helpers, unknown calls). None is
   about `undefined` or strict mode.
3. **Strict-mode hazards in the original sources**: none. No assignment to an undeclared name, no `this` outside a
   method, no `arguments`, no `with`, octal, `delete name`, duplicate parameters or reserved words. Every original source
   parses as strict.
4. **Strict parse of the 121 rewritten sources, wrapped as Elsa 4 wraps them**: 119 pass. The 2 failures are not about
   strict mode: both are RunJavaScript bodies (javascript-specialized-methods-workflow) that end in a `//` line comment
   without a newline, so Elsa 4's one-line script wrapper `(() => { … })()` loses its closing `})()` to the comment.
   They convert today and cannot run in Elsa 4.
5. **Run in the real Elsa 4 evaluators**: with every parameter `{}`, **16 expressions in 10 definitions fail with the
   `undefined` error**. All 16 read a member of a request value that is missing (for example
   `return getHttpRequestBody().subWorkflowDefinitionId;`, `getQueryParameters()['queryValue']`). Elsa 3 gave `null`
   for each. No `undefined` result comes from the shape of the source (`void`, a bare `return`, a function without a
   return). Script mode turns `undefined` into `null` and does not fail.

**What this means for the scope**: Part 1 fixes a real, data-dependent run-time failure in 10 of the 33 definitions with
JavaScript. Part 2 refuses nothing in the corpus today; it is a guard for workflows outside it. The trailing-comment
case is a third gap of the same kind (converts, then cannot run) and is covered by User Story 4.

## Clarifications

### Session 2026-10-20

- Q: Refuse an assignment to an undeclared name, or lower it by hoisting `let name;`? → A: Refuse. The corpus has no
  use, and hoisting is not faithful when the name is read before its first assignment (Elsa 3 throws a ReferenceError,
  the hoisted form returns `null`).
- Q: Refuse `this` outside a method and writes through `arguments`, or leave both as run-time failures? → A: Refuse
  what a static check proves: `this` at the top level (outside every non-arrow function), and a write that relies on
  `arguments` aliasing (a write to `arguments[…]`, or to a parameter in a function that reads `arguments`). `this`
  inside a function stays a documented difference, because whether it is called as a method is not static.
- Q: `{ discount: undefined }` loses the key in Elsa 4 where Elsa 3 kept it as `null`: document it, or file an Elsa 4
  issue? → A: Both. The Mapping README documents it in this unit; an Elsa 4 materializer issue is drafted, shown to the
  maintainer, and filed as `needs-triage` only after approval.
- Q: Must tests prove that a `null` bound to a typed, non-nullable input behaves as in Elsa 3? → A: No tests in this
  unit. The plan's research records how Elsa 3 and Elsa 4 treat it; a difference becomes its own issue (shown to the
  maintainer first), because it applies to every `null` binding, not only to this rewrite.
- Q: File Elsa 4's one-line script wrapper (a trailing line comment breaks it) as its own issue? → A: Yes. Draft it,
  show it to the maintainer, and file it as `needs-triage` after approval. FR-011 stays the converter-side guard.

## User Scenarios & Testing *(mandatory)*

The actor is a workflow author or operator who converts Elsa 3 workflow definitions (JSON) to Elsa 4, through the
collection import or, later, engine-start reconciliation (spec 235).

### User Story 1 - A converted expression whose value is undefined gives null, as in Elsa 3 (Priority: P1)

An Elsa 3 expression bound to an activity input sometimes produces `undefined`: it reads a property that is missing
(`getVariable('order').discount`), it calls a function that falls through without returning, or it ends in `void x`.
Elsa 3 turned that into `null`. Today the same expression converts and then fails in Elsa 4 when it runs, because
Elsa 4 refuses an `undefined` binding result. The author expects the converted expression to give `null`, as it did
in Elsa 3, and every other value to stay exactly what it was.

**Why this priority**: The failure is silent at conversion and shows up only at run time, in any workflow that reads
an optional value. It is also the cheaper half: one rule, applied where the result leaves the expression.

**Independent Test**: Convert expressions that produce `undefined` in each way above, evaluate the converted
expression in the real Elsa 4 evaluator, and compare with Elsa 3's result (`null`). Repeat for values that must not
change (`0`, `""`, `false`, `null`, objects).

**Acceptance Scenarios**:

1. **Given** an Elsa 3 expression that reads a missing property, **When** it is converted and evaluated in Elsa 4, **Then** the result is `null`.
2. **Given** an Elsa 3 expression that calls a function that returns nothing, or ends in `void x`, **When** it is converted and evaluated, **Then** the result is `null`.
3. **Given** a statement-form expression whose last expression is `undefined`, or a top-level `return` whose value is `undefined`, **When** it is converted and evaluated, **Then** the result is `null`.
4. **Given** an expression whose value is `0`, `""`, `false`, `null`, a string, a number, an object or an array, **When** it is converted and evaluated, **Then** the result is the same value as before this feature.
5. **Given** an expression `a || b` or a comma expression `a, b`, **When** it is converted, **Then** it still parses and evaluates to the same value it did in Elsa 3 (or `null` where Elsa 3 gave `null` for `undefined`).
6. **Given** a `return` inside a function the expression declares (not at the top level), **When** it is converted, **Then** that `return` is left as written.
7. **Given** a RunJavaScript body (Script mode), **When** it is converted, **Then** its output is unchanged by this story, because Elsa 4 already turns an `undefined` script result into `null`.

---

### User Story 2 - JavaScript that only runs in sloppy mode is refused at conversion (Priority: P1)

Elsa 3 ran scripts in sloppy mode; Elsa 4 runs them in strict mode. Some sloppy-only syntax (a `with` statement, a
legacy octal literal such as `010`, duplicate parameter names, `delete name`, a strict-mode reserved word used as a
name) converts today and then fails in Elsa 4 because it does not parse. The author expects the converter to refuse
such a definition with a diagnostic that says the construct only works in Elsa 3's sloppy mode and what to change in
the Elsa 3 workflow.

**Why this priority**: A definition that cannot run must not convert. One check covers the whole class.

**Independent Test**: Convert one expression and one RunJavaScript body for each construct above; each is refused
with the strict-mode diagnostic. Convert sources that are valid in both modes; they convert as before.

**Acceptance Scenarios**:

1. **Given** an expression or script that uses `with`, a legacy octal literal or escape, duplicate parameter names, `delete` of a plain name, or a strict-mode reserved word as a name, **When** it is converted, **Then** the definition is refused with the strict-mode diagnostic, whose message names the parser's reason and whose guidance says what to change in the Elsa 3 workflow.
2. **Given** a source that parses in both modes, **When** it is converted, **Then** the result is the same as before this feature.
3. **Given** a source that does not parse even in sloppy mode, **When** it is converted, **Then** the existing parse refusal is reported, not the strict-mode one.
4. **Given** `this` outside every non-arrow function (top-level `this`, including inside a top-level arrow function), **When** it is converted, **Then** the definition is refused with guidance to read the value through a variable or input instead.
5. **Given** a function that reads `arguments` and writes to `arguments[…]` or to one of its parameters, **When** it is converted, **Then** the definition is refused with guidance to copy the value into a local `let` instead.
6. **Given** `this` inside a non-arrow function, or `arguments` that is only read, **When** it is converted, **Then** it converts; the README documents that `this` in a plainly called function is the Elsa 3 global object in Elsa 3 and `undefined` in Elsa 4.

---

### User Story 3 - An assignment to an undeclared name is refused (Priority: P1)

In Elsa 3, `total = 1` with no `let`, `const` or `var` silently created a global. In Elsa 4's strict mode it throws a
ReferenceError when it runs. This is the strict-mode difference most likely to appear in real workflows. The author
expects the converter to refuse it, with guidance to declare the name with `let`.

**Why this priority**: The strict parse cannot see it: the source parses in both modes and fails only when it runs.

**Independent Test**: Convert expressions and scripts that assign to an undeclared name with `=`, a compound operator
(`+=`), `++`/`--`, a destructuring assignment and a `for (k in …)` / `for (k of …)` head; each is refused. Convert the
same sources with the name declared (at the top level, in an enclosing function, as a parameter or catch variable);
each converts.

**Acceptance Scenarios**:

1. **Given** an assignment, compound assignment or update to a name that no enclosing scope of the source declares, **When** it is converted, **Then** the definition is refused with the undeclared-assignment diagnostic, which names the variable and says to declare it with `let`.
2. **Given** the same assignment where the name is declared with `var`, `let` or `const`, as a function or class name, as a parameter or as a catch variable in an enclosing scope, **When** it is converted, **Then** it converts.
3. **Given** a destructuring assignment or a `for … in`/`for … of` head that assigns to an undeclared name, **When** it is converted, **Then** it is refused in the same way.
4. **Given** an assignment to a property (`o.total = 1`), **When** it is converted, **Then** this story does not refuse it (ambient mutation keeps its existing refusal).

---

### User Story 4 - A RunJavaScript body that ends in a line comment still runs (Priority: P2)

Elsa 3 ran a script that ends in a `// …` comment without trouble. Elsa 4 wraps a script body on one line, so a
trailing line comment swallows the end of the wrapper and the script does not parse. The corpus has 2 such scripts.
The author expects the converted script to run, since the comment carries no behaviour.

**Why this priority**: Found by the baseline. Without it, the strict-parse check of User Story 2 would refuse these
scripts for a reason that has nothing to do with the Elsa 3 workflow.

**Independent Test**: Convert a RunJavaScript body ending in `// comment` with no newline; it converts, and Elsa 4
runs it with the same result as without the comment.

**Acceptance Scenarios**:

1. **Given** a RunJavaScript body whose last line is a line comment, **When** it is converted, **Then** it converts and the Elsa 4 script runs.
2. **Given** a RunJavaScript body without a trailing line comment, **When** it is converted, **Then** the output is unchanged.

---

### Edge Cases

- An expression that ends with a line comment: the added `?? null` must not be swallowed by the comment.
- A top-level `return` with no argument already becomes `return null;` (spec 247); it stays that way.
- `a || b ?? null` is a syntax error, so the original expression is always kept intact inside parentheses.
- A sequence (comma) expression keeps its value as a whole: `(a, b) ?? null`, not `a, (b ?? null)`.
- An undeclared assignment inside a nested function is still an undeclared assignment; a name declared later in the
  same scope with `var` or a function declaration counts as declared (hoisting), as it does in JavaScript.
- An assignment to a name Elsa 3 treated as a global (`undefined = 1`, `NaN = 1`, `JSON = …`) is an assignment to an
  undeclared name for this rule.
- Strict-parse refusals and undeclared-assignment refusals can both apply; the author sees every diagnostic, not only
  the first.
- The strict check runs on the source Elsa 4 will evaluate (after rewriting), so a construct the converter itself
  introduces can never slip past it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: In Expression mode, the converter MUST make every value that leaves a converted expression at the top level `null` when it is `undefined`, and MUST keep every other value unchanged. This covers the single expression, the trailing completion expression of a statement-form expression, and the argument of every top-level `return`.
- **FR-002**: The rule in FR-001 MUST keep the original expression intact as one unit, so operator precedence, `||`/`&&` mixing and comma expressions are unaffected.
- **FR-003**: A `return` inside a function declared in the source MUST NOT be changed.
- **FR-004**: Script mode (RunJavaScript bodies) MUST NOT get the FR-001 rule; its output stays as today.
- **FR-005**: After rewriting, the converter MUST parse the source the way Elsa 4 will evaluate it (strict mode, as an expression or a function body as appropriate). If that parse fails, the converter MUST refuse with a stable diagnostic that names the parser's reason and gives guidance on what to change in the Elsa 3 workflow.
- **FR-006**: The converter MUST refuse an assignment, compound assignment, update, destructuring assignment or `for … in`/`for … of` head that writes to a name not declared in any enclosing scope of the source, with a stable diagnostic that names the variable and gives the guidance "declare it with `let`" in Elsa 3 terms. Expression and Script mode are both covered.
- **FR-007**: The converter MUST NOT change Elsa 4's evaluators, the binding grammar, or the `undefined` rule of ADR 0083.
- **FR-008**: Every behaviour of FR-001 MUST be proven by an equivalence test that evaluates the converted expression in the real Elsa 4 evaluator: a missing property, a function that falls through, `void x`, `a || b`, a comma expression, an explicit `null`, and the falsy values `0`, `""` and `false`.
- **FR-009**: Every construct of FR-005, FR-006 and FR-013 MUST have a refusal test, and every counterpart that still converts (a declared name, `this` in a function, read-only `arguments`) a conversion test.
- **FR-010**: The Mapping README's migration story MUST move the two differences from "known differences that convert without a diagnostic" into the "what converts" and "what is refused" tables in the same unit, and MUST keep two remaining differences documented: `this` in a plainly called function, and an `undefined` value nested in an object result (the key is dropped in Elsa 4, kept as `null` in Elsa 3).
- **FR-011**: A converted RunJavaScript body MUST end so that a trailing line comment cannot swallow the end of Elsa 4's script wrapper (for example, by ending with a newline).
- **FR-012**: The JTest corpus MUST be measured before and after with the spec 247/248 method, and the results recorded in this spec.
- **FR-013**: The converter MUST refuse `this` outside every non-arrow function, and a write to `arguments[…]` or to a parameter inside a function that reads `arguments`, with a stable diagnostic and guidance in Elsa 3 terms. `this` inside a non-arrow function and read-only `arguments` MUST still convert.
- **FR-014**: Two Elsa 4 issues MUST be drafted and shown to the maintainer, and filed as `needs-triage` only after approval: the nested-`undefined` difference (the README links it once filed), and the script wrapper that a trailing line comment breaks.

### Key Entities

- **Converted expression**: an Elsa 4 expression definition the converter produces from one Elsa 3 JavaScript source, in Expression mode (a binding) or Script mode (a RunJavaScript body).
- **Conversion diagnostic**: a refusal with a stable code, a message and guidance in Elsa 3 terms.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every converted expression in the FR-008 equivalence set gives Elsa 3's result when evaluated in Elsa 4; none fails with the `undefined` error.
- **SC-002**: Every sloppy-only construct and provable `this`/`arguments` case in User Story 2, and every undeclared-assignment shape in User Story 3, is refused at conversion; none converts and then fails at run time.
- **SC-003**: The existing Elsa 3 Mapping test suite stays green, and the definitions that convert end to end in the JTest corpus today still convert.
- **SC-004**: The JTest before and after numbers are recorded in this spec, including how many corpus sources each new refusal affects.
- **SC-005**: Measured again with the baseline method: the 16 corpus expressions that fail with the `undefined` error today give `null` instead, the 2 corpus scripts ending in a line comment parse and run, and no corpus source that rewrites today is newly refused (the corpus has no strict-mode hazard).

## Assumptions

- `null` and `undefined` cannot be told apart in a converted expression's result: the result is stored as JSON, which
  has no `undefined`, and Elsa 3's `ToObject()` returned `null`. This is why FR-001 is faithful. It is about the
  top-level result only; `undefined` nested inside an object or array is a documented
  difference (Clarifications, FR-010).
- How a `null` result reaches a typed, non-nullable input is not changed or tested by this unit; plan research records
  the Elsa 3 and Elsa 4 behaviour.
- Elsa 3 (3.5, the corpus's tool version) ran JavaScript in sloppy mode with top-level `return` allowed.
- Diagnostics reuse the existing `VF-IMP-*` codes; no code is added, removed or renamed (to be confirmed in planning).
- Elsa 4's one-line script wrapper breaks for any RunJavaScript body that ends in a line comment, not only converted
  ones. Fixing the wrapper is an Elsa 4 change and out of scope; FR-011 only keeps converted scripts clear of it, and
  the wrapper gets its own issue (FR-014).
- Out of scope: Elsa 4 changes of any kind; emulating sloppy mode; the time and unique-value capability (#3026); spec
  235's JTest suite conversion.
