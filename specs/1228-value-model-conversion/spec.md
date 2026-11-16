# Feature Specification: Value model and conversion

**Feature Branch**: `claude/3052-s8-define-cc74d4`

**Created**: 2026-11-16

**Status**: Draft — **provisional**. The coordinator approves it; implementation starts once the ADR 0046 amendment is merged into the initiative branch.

**Initiative**: part of [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052), spec row **S-8**,
analysis **A-1**, decisions **D-12**, **D-15**, **D-16** and **D-17**.

**Issues**: [#3196](https://github.com/elsa-workflows/elsa-foundation/issues/3196) (an Elsa 3 script result into a
typed input), [#3202](https://github.com/elsa-workflows/elsa-foundation/issues/3202) (model part: a dictionary of
arrays has no type), [#3287](https://github.com/elsa-workflows/elsa-foundation/issues/3287) (type part: the dynamic
kind has several names), and the JavaScript part of
[#3421](https://github.com/elsa-workflows/elsa-foundation/issues/3421) (numbers lose digits in scripts).

**Input**: build the defaults of the ADR 0046 amendment. Scripts and JavaScript input expressions get plain
JavaScript values; only what a script changes leaves it, and an input expression that tries to write fails loudly. A number JavaScript cannot hold exactly is refused at a write
by one replaceable host policy. The dynamic kind has one name, `Any`, shown as JSON. Elsa 3 values convert faithfully
or are refused at import, never at publish. .NET objects stay out of scripts.

## Approval

> **Provisional** until the coordinator approves it. Every decision it applies is agreed. If an ADR text changes
> before approval, this spec follows it.

| ADR | What this spec takes from it | Text applied |
|---|---|---|
| ADR 0046 amendment, [#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422) | V1–V9, V11, V12 (JSON profile only), the adapter rules for activity inputs and outputs, JavaScript and Liquid, and *Kinds* | `ba99ca02d` (the meeting outcomes of 2026-11-12) |
| ADR 0090, accepted as amended | C1–C4: a read creates no write; a write is explicit, member changes in scripts included; the write keeps what the step did not touch; the two equalities | on the initiative branch |
| ADR 0092 | The read set; a write of a value the script read is a read-modify-write | on the initiative branch |
| ADR 0035 | The alias registry the kinds live in | on the initiative branch |

**Settled decisions this spec applies:**
- **One dynamic kind.** `Object` is retired in favour of `Any`, shown in Studio as JSON, with the spec 264
  supersession (#3422 Q5). Agreed by the approvers in the initiative meetings; recorded by the coordinator.
- **D-17, precision.** One replaceable policy, provided by a feature, host-wide, at the .NET → Jint boundary. The
  default refuses. Elsa ships only that policy, with no options. No per-workflow or per-node override; a host that
  wants other handling installs its own policy.
- **When a number is lossy (ADR 0046 V9).** A number has lost precision when the JavaScript number, written as JSON,
  differs in value from the stored one. `10.50` → `10.5` and `2.0` → `2` are the same value and no loss;
  `9007199254740993` → `9007199254740992` is. The engine knows it when the value crosses into JavaScript, before the
  script runs.
- **Host argument.** A lossy value passed to a host function is not a write: a warning is logged.
- **D-15.** A JavaScript input expression takes the same input as a script in Run JavaScript. One .NET switch for
  both (built in #4103).
- **D-16.** S-8 ships the default only. No .NET objects in scripts or input expressions.
- **ADR 0090 decision 1 and ADR 0083 (`binding-readonly-v1`).** Script and C# activity bodies may write variables.
  Input-binding expressions never write: they may be evaluated more than once (replay, each `While` pass, preflight),
  so a write would apply a variable number of times. An attempted write is refused loudly, never dropped silently
  (coordinator, 2026-11-16).

**Approval plan**: the coordinator approves this spec alone. An ADR change found here goes to its own small ADR PR
with the initiative's approvers as reviewers.

**Implementation**: on the initiative branch, once this spec is approved and the ADR 0046 amendment is merged there
(D-18).

## Rule and boundary

**Rule**: a value crosses a boundary only through an adapter with a written contract. A read never changes a stored
value. A loss is declared or refused, never silent.

**Terms**:
- *Core value*: valid JSON with a type and a presence, as stored today (V1).
- *Adapter*: the code that converts between the core value and one side, such as JavaScript or an activity's .NET input.
- *Adapter contract*: what an adapter accepts, what it produces, which conversions are lossless, lossy or refused,
  and the diagnostic for each refusal (V3).
- *Script value bridge*: the replaceable component that puts values into a JavaScript evaluation and finds what the
  evaluation changed (V12). Its first implementation is copy, compare, patch.
- *Change*: what the bridge reports for one value: a member change (partial update) or a whole assignment (replace).
- *Dynamic kind*: the kind that holds any valid JSON value. Alias `Any`, shown as JSON.
- *Precision loss*: a JSON number whose value differs once held as a JavaScript number. Only integers beyond
  2^53 − 1 and decimals with more digits than a double holds can lose. `10.50` → `10.5` is not a loss.
- *Precision policy*: the one host-wide component that decides what happens to a precision loss at a write (D-17).

**In scope**:
- The script value bridge for script activities and JavaScript input expressions alike (V9, V12, D-15): plain values
  in, changes found, as a replaceable contract with its cost measured.
- Refusing an attempted write from a JavaScript input expression, at publish where it is visible and at run time
  otherwise.
- The precision policy and its default, which refuses (D-17).
- One dynamic kind; `Object` and `Elsa.Any` read as `Any`; the kinds `DateOnly` and `TimeOnly` (*Kinds*).
- One binding rule for the dynamic kind: `Any` binds to `JsonElement`, `JsonNode` and their nullable forms (#3287).
- A type for nested collections, such as a dictionary of string arrays (#3202).
- An Elsa 3 untyped result into a typed input: faithful or refused at import (#3196).
- A script result inherits the strongest protection of what the script read (*Kinds*, secrets).
- The written adapter contracts for JavaScript, activity inputs and outputs, and Liquid.

**Out of scope**:
- .NET objects in scripts or input expressions: the opt-in is #4103 (D-16). S-1 refuses Elsa 3 scripts that need them.
- The step write path of ADR 0090: assembling the intent from the base (C3), committing with the step, conflicts
  (C5) and `setVariable`/C# `Set`. That is S-7. S-8 hands it the changes a script made (Q2).
- Files and other resources (#3194). It is a linked defect, not S-8.
- How dates are interpreted at the JSON to .NET boundary, unmatched keys, member casing and the shared serializer
  baseline: S-9 (spec 1207). S-8 uses those rules and adds none.
- `undefined`, `NaN` and `Infinity`: S-2 (spec 1216).
- Host functions, the clock and `Date` in scripts: S-1 (spec 1223).
- Versions, lost updates and the write history: S-10 (spec 1221).
- #3167 (the JSON alias inference). It is an S-0 defect that User Story 6 needs first.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A script reads data without changing it (Priority: P1)

A workflow engineer reads an order in a script, computes a total and returns it. The order in the workflow stays
exactly as stored, including an id beyond 2^53 that the script never touched.

**Why this priority**: today every script input is converted to doubles up front. A read must never change data.

**Independent Test**: store an order with `id: 9007199254740993` and `price: 10.50`, run a script that reads the
order and returns `order.price * 2`, then compare the stored order before and after.

**Acceptance Scenarios**:

1. **Given** a stored order with a large id, **When** a script reads it and returns something else, **Then** the
   stored order's text and version are unchanged.
2. **Given** a stored value, **When** a script or a JavaScript input expression reads it, **Then** it gets plain
   JavaScript objects and real arrays it can change locally.
3. **Given** a variable the script never reads, **When** the script runs, **Then** that variable is not touched.
4. **Given** a JavaScript input expression that assigns a variable (`variables.x = 1`), **When** the workflow is
   published, **Then** publication refuses it with a diagnostic naming the node, the input and the variable, and
   saying input expressions only read.
5. **Given** a JavaScript input expression that changes a member of a value it read in a way publication cannot see
   (for example through a local alias), **When** it is evaluated, **Then** the evaluation fails with the same
   diagnostic and the path, and nothing is written.
6. **Given** a JavaScript input expression that copies a value (`{...variables.order}`) and changes the copy, **When**
   it is evaluated, **Then** it succeeds: a local copy is not a write.

---

### User Story 2 — A lost digit never reaches storage silently (Priority: P1)

A script returns an order id it read, or a new object that contains it. The id is beyond 2^53 − 1. The write is
refused with a diagnostic that says JavaScript cannot hold this number exactly.

**Why this priority**: D-17. A silent loss makes an id point to another record.

**Independent Test**: run a script that returns `{ id: order.id }` with `order.id = 9007199254740993`. Check the step
fails with the refusal diagnostic and nothing is written. Run it again with the id stored as a string and check it
passes.

**Acceptance Scenarios**:

1. **Given** a value that lost precision on its way in, **When** it reaches a write (returned, copied into a written
   value, inside a written array, or returned by an input expression into an input), **Then** the default policy
   refuses it. The diagnostic names the path, the stored and the written value, and says the value exceeds
   `9007199254740991`.
2. **Given** a changed array with an element that lost precision, **When** it is written, **Then** the diagnostic
   says the array was written as a whole, lists each lossy element with both values, and gives the remedy: hold the
   number as a string, or do not change the array.
3. **Given** `10.50` read and returned, **When** it is written, **Then** it is not a loss and is written as `10.5`.
4. **Given** a script that only computes with a lossy value, **When** it runs, **Then** it gets a warning and is not
   refused.
5. **Given** a lossy value passed to a host function, **When** the call runs, **Then** a warning is logged and the
   call is not refused.
6. **Given** a host that installs its own policy, **When** a loss reaches a write, **Then** that policy decides, and
   Elsa's default does not run.

---

### User Story 3 — A script changes one member and keeps the rest (Priority: P2)

A script sets `variables.order.total = 20`. Only `total` changes. The other members keep their stored values, large
numbers included. `variables.order = { total: 20 }` replaces the whole value.

**Why this priority**: scenario S-a of A-1 (change an HTTP body in a script). S-8 finds the changes; S-7's step write
path commits them.

**Independent Test**: store an order with a large id, run `variables.order.total = 20`, and compare: only `total`
differs. Run the whole-assignment form and check the value is replaced.

**Acceptance Scenarios**:

1. **Given** a member change, **When** the bridge reports changes, **Then** it reports a partial update of that
   member, and the step write keeps every other member's stored text.
2. **Given** a whole assignment, **When** the bridge reports changes, **Then** it reports a replace.
3. **Given** a changed array, **When** the bridge reports changes, **Then** the array is reported as a whole.
4. **Given** a member set to a value equal, as a JavaScript value, to what was read (`…992` over a stored `…993`),
   **When** the bridge reports changes, **Then** the member is unchanged (ADR 0090 C2). Writing it as a string forces
   the write.
5. **Given** a whole assignment of such a value, **When** the bridge reports changes, **Then** it is a write, judged by
   meaning in the canonical form (ADR 0090 C4), and the precision policy applies.
6. **Given** a member change with no whole assignment, **When** the step commits, **Then** a warning records that the
   script changed a read value without assigning it (ADR 0090 C2, visibility).

---

### User Story 4 — One name for dynamic data (Priority: P2)

A workflow engineer picks the type JSON for a variable. It holds any JSON value. A definition or an Elsa 3 import that
names `Object` reads as JSON. `DateOnly` and `TimeOnly` are kinds too.

**Why this priority**: today dynamic data has seven names.

**Independent Test**: create variables of type JSON holding an object, an array, a string and a number; load a
definition with an `Object` variable; import an Elsa 3 workflow with an `ObjectDictionary` variable. Each reads as
`Any`, publishes and runs.

**Acceptance Scenarios**:

1. **Given** Studio's variable type list, **When** an engineer opens it, **Then** it offers JSON once and no `Object`.
2. **Given** a definition naming `Object` or `Elsa.Any`, **When** it loads, **Then** the type reads as `Any`.
3. **Given** an Elsa 3 variable of an unknown JSON type, `ObjectDictionary` or `HttpHeaders`, **When** it converts,
   **Then** it becomes `Any`; a JSON starting value stays that JSON, and a text starting value becomes a JSON string
   with the same text.
4. **Given** a `DateOnly` or `TimeOnly` variable, **When** it is stored, **Then** it is the ISO 8601 text
   (`"2026-10-28"`, `"09:15:00"`).

---

### User Story 5 — The dynamic kind binds across its .NET names (Priority: P1)

An expression declared `Any` binds to an input declared `JsonElement`, and the other way round. A dictionary of string
arrays has a type that names the whole shape.

**Why this priority**: #3287 and #3202. Today a name mismatch fails at publish although both sides hold the same
JSON.

**Independent Test**: publish a node whose `JsonElement` input is bound by an `Any` expression; publish a
`WriteHttpResponse` with a header map literal and run it.

**Acceptance Scenarios**:

1. **Given** an `Any` expression on a `JsonElement`, `JsonElement?` or `JsonNode` input, **When** it publishes,
   **Then** it is accepted with no conversion step.
2. **Given** a `JsonObject` input, **When** an `Any` expression is bound, **Then** it is accepted and a non-object
   value is refused at run time with a diagnostic (`JsonObject` is object-shaped).
3. **Given** an input of type dictionary of string arrays, **When** its contract is read, **Then** its type names the
   dictionary and its element arrays, and diagnostics show that whole type.
4. **Given** `{"X-Test": ["1"]}` bound to `WriteHttpResponse.Headers`, **When** the workflow runs, **Then** the
   response carries the header.

---

### User Story 6 — An Elsa 3 script result drives a typed input faithfully or is refused at import (Priority: P2)

An Elsa 3 workflow binds a script result to a Flow Decision condition. Where Elsa 4 gives the same boolean Elsa 3
gave, it publishes and branches as before. Where it cannot, the import refuses with guidance.

**Why this priority**: #3196. Publication must never be the first place this fails.

**Independent Test**: import fixture `reusable-reads-result`, publish it, and run both branches.

**Acceptance Scenarios**:

1. **Given** a script returning a JSON boolean into a `Boolean` input, **When** imported, published and run,
   **Then** it takes the branch Elsa 3 took.
2. **Given** a JSON string, number, null, object or array into a typed input, **When** imported, **Then** it converts
   only where Elsa 3 produced the same typed value, and is refused at import otherwise.
3. **Given** either case, **When** published, **Then** it never fails with `VF-COER-001` for that binding.
4. **Given** an Elsa 4 JSON value of the matching kind into a typed input (a JSON `true` into `Boolean`), **When** it
   runs, **Then** it converts (D-12). `"true"` into `Boolean` stays refused (no text parsing).

---

### User Story 7 — A script result is as protected as what it read (Priority: P3)

A script reads a sensitive variable and returns part of it. The result carries the same protection.

**Why this priority**: *Kinds* (secrets). The read set of ADR 0092 records the reads for protection (ADR 0090 C1).

**Independent Test**: mark a variable sensitive, return a member of it from a script, and check the stored result is
sensitive and redacted where the variable is.

**Acceptance Scenarios**:

1. **Given** a script that read a sensitive value, **When** its result is stored, **Then** the result has the
   strongest protection policy among the values it read.
2. **Given** a script that read no protected value, **When** its result is stored, **Then** it has no protection
   policy added.

### Edge Cases

- A script that reads a lossy number and leaves it unchanged inside an unchanged object writes nothing.
- A lossy number in a JSON string (`"9007199254740993"`) is a string. It is never a loss.
- Negative integers below −(2^53 − 1) lose like positive ones.
- `1e21` and `2.0` come back as `1e+21` and `2`. The notation changes, the number does not; no loss.
- A script that deletes a member reports a member change.
- Key order and key casing are kept.
- A changed array with no lossy element is reported as a whole with no diagnostic.
- Liquid reads JSON numbers as decimals and does not narrow; it needs no precision policy.
- A local copy (`{...variables.order}`) changed by a script or an input expression is not a change to `order`.
- An input expression that changes a value and changes it back reports no change and succeeds.

## Requirements *(mandatory)*

### Functional Requirements

**Script value bridge (V9, V12, D-15)**

- **FR-001**: Values MUST enter scripts and JavaScript input expressions as plain, changeable JavaScript objects and
  real arrays, through the same bridge. The frozen-value contract of spec 095 is replaced.
- **FR-002**: Reading a value MUST NOT change its stored text or version (ADR 0090 C1).
- **FR-003**: After a script runs, the bridge MUST report what it changed: for each value read, unchanged, a member
  change, or a whole assignment (Q2). Unchanged members are reported from the original JSON, not from JavaScript.
- **FR-004**: A changed array MUST be reported as a whole.
- **FR-005**: A member set to a value equal, as a JavaScript value, to what was read MUST count as unchanged. A whole
  assignment MUST be reported as a write; S-7 judges it by meaning in the canonical form (ADR 0090 C2, C4).
- **FR-006**: A script body's changes MUST go to the step write path of ADR 0090 (S-7).
- **FR-006a**: A JavaScript input expression MUST NOT write. An assignment to `variables`, or to a member of a value
  read from it, MUST be refused at publish where the expression shows it, and MUST fail the evaluation at run time
  when the bridge reports any change. The diagnostic names the node, the input, the variable and the path, and says
  input expressions only read; it suggests a script activity for writes. A change is never dropped silently (Q6).
- **FR-007**: A member change without a whole assignment MUST produce the visibility warning of ADR 0090 C2.
- **FR-008**: No .NET object, CLR type or CLR member MUST be reachable from a script or a JavaScript input expression
  (D-16).
- **FR-009**: The bridge MUST be a replaceable contract. The copy-compare-patch implementation is the default. A
  replacement that behaves identically keeps the JSON profile; one that differs is a new profile (V12).
- **FR-010**: The time the bridge spends converting values in and finding changes MUST be measured per evaluation and
  exposed as a metric, so its cost can be tracked in production and compared with a replacement.

**Precision (V9, D-17)**

- **FR-011**: The bridge MUST know, when a number enters, whether JavaScript holds it exactly (ADR 0046 V9).
- **FR-012**: Precision handling MUST be one replaceable host-wide policy, provided by a feature, at the .NET → Jint
  boundary. There MUST be no per-workflow or per-node override.
- **FR-013**: Elsa MUST ship one policy, with no options, that refuses a write that carries a precision loss. The
  refusal is an instance diagnostic visible in Studio, not only a log line. It names the path, the stored value and
  the written value, and says the number exceeds the largest integer a JavaScript number holds exactly
  (2^53 − 1, `9007199254740991`).
- **FR-014**: For a whole-array write, the refusal MUST name the array path, say it was written as a whole, list each
  lossy element with both values, and give the remedy.
- **FR-015**: A loss that is only computed with MUST give a warning, not a refusal. A loss passed to a host function
  MUST log a warning and not refuse the call.
- **FR-016**: Elsa MUST never turn a number into a string by itself.

**Kinds (*Kinds*)**

- **FR-017**: The dynamic kind MUST have one alias, `Any`, registered in the alias registry and shown as JSON in
  Studio. It holds any valid JSON value.
- **FR-018**: `Object` and `Elsa.Any` MUST read as `Any` when a definition loads. The mapping from `Object` to .NET
  `object` MUST go. Studio MUST NOT offer `Object`.
- **FR-019**: The Elsa 3 converter MUST emit `Any` where it emits `Object` today. A JSON starting value stays that
  JSON; a text starting value becomes a JSON string with the same text (spec 264 supersession).
- **FR-020**: `DateOnly` and `TimeOnly` MUST be kinds, stored as ISO 8601 text. Their interpretation at the .NET
  boundary follows S-9.

**Binding and types (#3287, #3202)**

- **FR-021**: Publication MUST treat `Any`, `JsonElement`, `JsonElement?`, `JsonNode` and their full .NET names as
  the same kind when it checks an expression's declared type against an input's. `JsonObject` accepts `Any` and
  checks the shape at run time.
- **FR-022**: A type MUST be able to describe nested collections, such as a dictionary of string arrays, and
  diagnostics MUST show the whole type (Q3).

**Elsa 3 conversion (#3196)**

- **FR-023**: An Elsa 3 untyped result bound to a typed input MUST convert only where Elsa 4 produces the same typed
  value Elsa 3 did, per JSON kind. The rule per kind is recorded from the Elsa 3 3.6.3 source (Q4).
- **FR-024**: Where FR-023 does not hold, the converter MUST refuse at import with one stable code naming the
  activity, the input and what to change. An untyped result MUST NOT count as compatible on its own.
- **FR-025**: A JSON value whose kind matches a scalar target (boolean, number in range, string) MUST convert to that
  target in Elsa 4 (D-12). Text MUST NOT be parsed into booleans or numbers.

**Protection**

- **FR-026**: A script result MUST carry the strongest protection policy of the values the script read, taken from
  its read set (Q5).

**Contracts**

- **FR-027**: The adapter contracts for JavaScript, activity inputs and outputs, and Liquid MUST be written down: what
  each accepts, what it produces, what is lossless, lossy or refused, and the diagnostic for each refusal.
- **FR-028**: A change to an adapter rule MUST ship as a new pinned version, never as an edit of an existing one. A
  published executable keeps the version it was published with.

### Key Entities

- **Core value**: type (alias, collection kind, optional JSON Schema), presence, JSON content or a reference,
  protection policy.
- **Type reference**: an alias and a collection kind, whose element may itself be a type reference (Q3).
- **Script value bridge**: the replaceable contract that enters values and reports changes.
- **Entry record**: per evaluation, the original JSON of each value read and its lossy numbers.
- **Change**: per value, unchanged, member change or whole assignment, with the lossy paths it carries.
- **Precision policy**: the host-wide component that decides on a lossy write.
- **Precision diagnostic**: path, stored value, written value, reason, remedy; refusal or warning.
- **Adapter contract**: the written rules of one adapter, with a version.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a test suite of reads with numbers beyond 2^53, long decimals, `10.50`, `2.0` and `1e21`, no stored
  value changes after a read-only script: 0 changed rows.
- **SC-002**: Every precision loss that reaches a write is refused by default: 0 silent losses in the precision suite.
- **SC-003**: A member change leaves 100% of the other members' stored text unchanged.
- **SC-004**: The fixture `reusable-reads-result` imports, publishes and runs both branches, and no Elsa 3 untyped
  result into a typed input fails at publish.
- **SC-005**: Studio and the alias registry offer one dynamic kind; no code compares against `Object` or `Elsa.Any`
  as a separate dynamic name.
- **SC-006**: A `WriteHttpResponse` with a header map publishes and sends the headers.
- **SC-007**: The bridge's cost is visible: the metric of FR-010 exists, and a benchmark records the time on 1 KB,
  100 KB and 1 MB inputs against today's engine. The first implementation stays within twice today's time on 1 MB;
  a larger cost is reported before the spec is marked implemented.
- **SC-008**: The JTest corpus converts and publishes at least as many definitions as before this spec.

## Spec decisions

These are about how this spec builds the settled decisions above. The coordinator accepted every recommended answer
on 2026-11-16; the requirements follow them.

- **Q1 — Where does the script value bridge stop and S-7 begin?** **Accepted:** S-8 owns the JavaScript side:
  entering values, finding changes, the precision check and the C2 warning. S-7 owns the step write path for every
  writer (`variables.x`, `setVariable`, C# `Set`): building the intent from the base (C3), committing with the step,
  and conflicts (C5). User Story 3's end-to-end test lands with whichever of the two arrives second.
- **Q2 — The first bridge implementation.** **Accepted: copy, compare, patch, behind a replaceable contract**, with
  its cost measured (FR-009, FR-010). Keep the original JSON per value read; compare after the run, JavaScript with
  JavaScript (ADR 0090 C2); report only changed paths. A later implementation, for example one built on proxies, may
  replace it if it behaves identically. Rejected for now: proxies (more complex, deferred), and reporting every read
  value as a change (breaks FR-002).
- **Q3 — How does a type describe a dictionary of arrays?** **Accepted:** a type reference's element may itself be
  a type reference, so `IDictionary<string, string[]>` is a dictionary of arrays of `String`. Rejected: describing it
  only by JSON Schema (the binding checks would not see it).
- **Q4 — Elsa 3 result into a typed input.** **Accepted:** convert per JSON kind where Elsa 3's coercion is
  reproduced exactly, refuse at import otherwise. Fix #3167 first and measure again, as it may make more bindings
  convert.
- **Q5 — Is protection inheritance in S-8?** **Accepted: yes, P3,** on the read set ADR 0092 provides. It is a
  value-model rule and has no other spec.
- **Q6 — Where does an attempted write from an input expression fail?** **Accepted: both.** At publish, a static
  check refuses a direct assignment to `variables.x` or to a member of `variables.x`, so most cases fail before the
  workflow runs. At run time, the bridge's change report catches what the static check cannot see (aliases, computed
  paths) and fails the evaluation. Rejected: run time only (fails late, maybe only on a rare branch), and freezing
  the values again (gives a JavaScript `TypeError` in strict mode without Elsa's diagnostic, and breaks D-15's
  "same values as a script").

## Assumptions

- The ADR 0046 amendment at `ba99ca02d` is the text applied. If it changes, this spec is rechecked.
- The ForEach and snapshot parts of #3421 are fixed already. Only the script part remains.
- S-9 delivers date interpretation, user type conversion contracts and the shared serializer baseline. S-8 uses them.
- S-1 delivers the host function mechanism and the refusal of Elsa 3 scripts that use .NET members.
- S-7 delivers the step write path of ADR 0090.
- Elsa 4 has no released definitions, so reading `Object` as `Any` needs no migration.
- The Elsa 3 coercion rules per kind come from the Elsa 3 3.6.3 source, outside this repository, as in spec 267.
