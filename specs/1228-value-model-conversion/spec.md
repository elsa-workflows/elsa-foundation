# Feature Specification: Value model and conversion

**Feature Branch**: `claude/3052-s8-define-cc74d4`

**Created**: 2026-11-16

**Status**: Draft — **provisional**. Waits on one open ADR 0046 point: retiring `Object` for `Any`, shown as JSON (question Q1).

**Initiative**: part of [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052), spec row **S-8**,
analysis **A-1**, decisions **D-12**, **D-16** and **D-17**.

**Issues**: [#3196](https://github.com/elsa-workflows/elsa-foundation/issues/3196) (an Elsa 3 script result into a
typed input), [#3202](https://github.com/elsa-workflows/elsa-foundation/issues/3202) (model part: a dictionary of
arrays has no type), [#3287](https://github.com/elsa-workflows/elsa-foundation/issues/3287) (type part: the dynamic
kind has several names), and the JavaScript part of
[#3421](https://github.com/elsa-workflows/elsa-foundation/issues/3421) (numbers lose digits in scripts).

**Input**: build the defaults of the ADR 0046 amendment. Scripts get plain JavaScript values and only what a script
changes is written. A number a script cannot hold exactly is refused at a write by one replaceable host policy. The
dynamic kind has one name. Elsa 3 values convert faithfully or are refused at import, never at publish. .NET objects
stay out of scripts.

## Approval

> **Provisional.** This spec applies the ADRs below as if they were agreed. It is approved when the open ADR point in
> Q1 is agreed. If an ADR text changes before then, this spec follows it.

| ADR | What this spec takes from it | Text applied |
|---|---|---|
| ADR 0046 amendment, [#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422) | V1–V9, V11, V12 (JSON profile only), the adapter rules for activity inputs and outputs, JavaScript and Liquid, and *Kinds* | `ba99ca02d` (the meeting outcomes of 2026-11-12, on top of `6a851d8ab`) |
| ADR 0090 amendment, [#3425](https://github.com/elsa-workflows/elsa-foundation/pull/3425) | A script may write variables; a write commits with the step | `9e4a679f5` |
| ADR 0092 | A write of a value the script read is a read-modify-write; the read set | on the initiative branch |
| ADR 0035 | The alias registry the kinds live in | on the initiative branch |

**The one open point.** #3422 Q5: `Object` is retired in favour of one dynamic kind, alias `Any`, shown in Studio as
JSON, with the spec 264 supersession. This waits on the ADR 0046 reviewers. User Story 4 depends on it. The other
stories do not.

**Approval plan**: the coordinator approves this spec. An ADR change found here goes to its own small ADR PR with the
initiative's approvers as reviewers.

**Implementation**: on the initiative branch, once this spec is approved and the ADRs it applies are agreed (D-18).

## Rule and boundary

**Rule**: a value crosses a boundary only through an adapter with a written contract. A read never changes a stored
value. A loss is declared or refused, never silent.

**Terms**:
- *Core value*: valid JSON with a type and a presence, as stored today (V1).
- *Adapter*: the code that converts between the core value and one side, such as JavaScript or an activity's .NET input.
- *Adapter contract*: what an adapter accepts, what it produces, which conversions are lossless, lossy or refused,
  and the diagnostic for each refusal (V3).
- *Dynamic kind*: the kind that holds any valid JSON value. Alias `Any`, shown as JSON.
- *Precision loss*: a JSON number whose value differs once held as a JavaScript number. Only integers beyond
  2^53 − 1 and decimals with more digits than a double holds can lose. `10.50` → `10.5` is not a loss.
- *Precision policy*: the one host-wide component that decides what happens to a precision loss at a write (D-17).

**In scope**:
- The JavaScript adapter: plain values in, changes detected, only changes written (V9).
- The precision policy and its default, which refuses (D-17).
- One dynamic kind; `Object` and `Elsa.Any` read as `Any`; the kinds `DateOnly` and `TimeOnly` (*Kinds*).
- One binding rule for the dynamic kind: `Any` binds to `JsonElement`, `JsonNode` and their nullable forms (#3287).
- A type for nested collections, such as a dictionary of string arrays (#3202).
- An Elsa 3 untyped result into a typed input: faithful or refused at import (#3196).
- A script result inherits the strongest protection of what the script read (*Kinds*, secrets).
- The written adapter contracts for JavaScript, activity inputs and outputs, and Liquid.

**Out of scope**:
- .NET objects in scripts or bindings: the opt-in is #4103 (D-16). S-1 refuses Elsa 3 scripts that need them.
- Files and other resources (#3194). It is a linked defect, not S-8.
- How dates are interpreted at the JSON to .NET boundary, unmatched keys, member casing and the shared serializer
  baseline: S-9 (spec 1207). S-8 uses those rules and adds none.
- `undefined`, `NaN` and `Infinity`: S-2 (spec 1216).
- Host functions, the clock and `Date` in scripts: S-1 (spec 1223).
- Versions, lost updates and the write history: S-10 (spec 1221).
- Which activities may write engine-owned state, beyond a script's own variable writes: S-7.
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
2. **Given** a stored value, **When** a script reads it, **Then** it gets plain JavaScript objects and real arrays it
   can change locally, and changing them alone writes nothing.
3. **Given** a variable the script never reads, **When** the script runs, **Then** that variable is not touched.

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
   value, or inside a written array), **Then** the default policy refuses the write. The diagnostic names the path,
   the stored and the written value, and says the value exceeds `9007199254740991`.
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
7. **Given** a script activity whose author accepted the loss (Q4), **When** the loss reaches a write, **Then** the
   write applies and an instance diagnostic records it.

---

### User Story 3 — A script changes one member and keeps the rest (Priority: P2)

A script sets `variables.order.total = 20`. Only `total` changes. The other members keep their stored values, large
numbers included. `variables.order = { total: 20 }` replaces the whole value.

**Why this priority**: scenario S-a of A-1 (change an HTTP body in a script). It needs the script write surface of
the ADR 0090 amendment.

**Independent Test**: store an order with a large id, run `variables.order.total = 20`, and compare: only `total`
differs. Run the whole-assignment form and check the value is replaced.

**Acceptance Scenarios**:

1. **Given** a member change, **When** the step commits, **Then** only that member is written and the other members
   keep their stored text.
2. **Given** a whole assignment, **When** the step commits, **Then** the value is replaced.
3. **Given** a changed array, **When** the step commits, **Then** the array is written as a whole.
4. **Given** a write of the value the script read (for example `…992` over a stored `…993`), **When** the step
   commits, **Then** it counts as unchanged and the stored value is kept (the accepted gap). Writing it as a string
   forces the write.
5. **Given** a write of a value the script read, **When** it commits, **Then** it is a read-modify-write under ADR 0092
   with that read as its base.
6. **Given** the step faults, **When** it ends, **Then** none of its writes are kept.

---

### User Story 4 — One name for dynamic data (Priority: P2)

A workflow engineer picks the type JSON for a variable. It holds any JSON value. A definition or an Elsa 3 import that
names `Object` reads as JSON. `DateOnly` and `TimeOnly` are kinds too.

**Why this priority**: today dynamic data has seven names. **Depends on Q1** (#3422 Q5).

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

**Why this priority**: *Kinds* (secrets). Needs the read set of ADR 0092, which exists.

**Independent Test**: mark a variable sensitive, return a member of it from a script, and check the stored result is
sensitive and redacted where the variable is.

**Acceptance Scenarios**:

1. **Given** a script that read a sensitive value, **When** its result is stored, **Then** the result has the
   strongest protection policy among the values it read.
2. **Given** a script that read no protected value, **When** its result is stored, **Then** it has no protection
   policy added.

### Edge Cases

- A script that reads a lossy number and writes it back unchanged as part of an unchanged object writes nothing: the
  object is unchanged.
- A lossy number in a JSON string (`"9007199254740993"`) is a string. It is never a loss.
- Negative integers below −(2^53 − 1) lose like positive ones.
- `1e21` and `2.0` come back as `1e+21` and `2`. The notation changes, the number does not; no loss.
- A script that deletes a member writes the object without it.
- Key order and key casing are kept.
- A changed array with no lossy element writes as a whole with no diagnostic.
- Liquid reads JSON numbers as decimals and does not narrow; it needs no precision policy.
- A JavaScript input-binding expression gets plain values too, but never writes (D-3). A lossy value it returns into an
  input is a write to the input snapshot and follows the policy.

## Requirements *(mandatory)*

### Functional Requirements

**JavaScript adapter (V9, V12 JSON profile)**

- **FR-001**: Values MUST enter scripts as plain, changeable JavaScript objects and real arrays. The frozen-value
  contract of spec 095 is replaced.
- **FR-002**: Reading a value MUST NOT change its stored text or version.
- **FR-003**: After a script runs, the adapter MUST find what the script changed by comparing each value it read with
  what entered (Q2). Only changed paths are written. Unchanged members keep their stored text.
- **FR-004**: A whole assignment MUST replace the value. A changed array MUST be written as a whole.
- **FR-005**: A value equal, as a JavaScript value, to what the script read MUST count as unchanged (the accepted gap).
- **FR-006**: Write-back MUST keep the data's meaning. Its notation may change.
- **FR-007**: Script variable writes MUST use the script write surface of the ADR 0090 amendment and commit with the
  step (Q3). A fault discards them.
- **FR-008**: No .NET object, CLR type or CLR member MUST be reachable from a script or a JavaScript input-binding
  expression (D-16).

**Precision (V9, D-17)**

- **FR-009**: The adapter MUST know, when a number enters, whether JavaScript holds it exactly (Q5).
- **FR-010**: Precision handling MUST be one replaceable host-wide policy, provided by a feature, at the .NET → Jint
  boundary. There MUST be no per-workflow override.
- **FR-011**: Elsa MUST ship one policy, with no options, that refuses a write that carries a precision loss. The
  refusal is an instance diagnostic visible in Studio, not only a log line. It names the path, the stored value and
  the written value, and says the number exceeds the largest integer a JavaScript number holds exactly
  (2^53 − 1, `9007199254740991`).
- **FR-012**: For a whole-array write, the refusal MUST name the array path, say it was written as a whole, list each
  lossy element with both values, and give the remedy.
- **FR-013**: A loss that is only computed with MUST give a warning, not a refusal. A loss passed to a host function
  MUST log a warning and not refuse the call.
- **FR-014**: A script activity MUST let its author accept precision loss for that node. The acceptance MUST be
  recorded in the published executable. The default policy honours it and records an instance diagnostic (Q4).
- **FR-015**: Elsa MUST never turn a number into a string by itself.

**Kinds (*Kinds*, depends on Q1)**

- **FR-016**: The dynamic kind MUST have one alias, `Any`, registered in the alias registry and shown as JSON in
  Studio. It holds any valid JSON value.
- **FR-017**: `Object` and `Elsa.Any` MUST read as `Any` when a definition loads. The mapping from `Object` to .NET
  `object` MUST go. Studio MUST NOT offer `Object`.
- **FR-018**: The Elsa 3 converter MUST emit `Any` where it emits `Object` today. A JSON starting value stays that
  JSON; a text starting value becomes a JSON string with the same text (spec 264 supersession).
- **FR-019**: `DateOnly` and `TimeOnly` MUST be kinds, stored as ISO 8601 text. Their interpretation at the .NET
  boundary follows S-9.

**Binding and types (#3287, #3202)**

- **FR-020**: Publication MUST treat `Any`, `JsonElement`, `JsonElement?`, `JsonNode` and their full .NET names as
  the same kind when it checks an expression's declared type against an input's. `JsonObject` accepts `Any` and
  checks the shape at run time.
- **FR-021**: A type MUST be able to describe nested collections, such as a dictionary of string arrays, and
  diagnostics MUST show the whole type (Q6).

**Elsa 3 conversion (#3196)**

- **FR-022**: An Elsa 3 untyped result bound to a typed input MUST convert only where Elsa 4 produces the same typed
  value Elsa 3 did, per JSON kind. The rule per kind is recorded from the Elsa 3 3.6.3 source (Q7).
- **FR-023**: Where FR-022 does not hold, the converter MUST refuse at import with one stable code naming the
  activity, the input and what to change. An untyped result MUST NOT count as compatible on its own.
- **FR-024**: A JSON value whose kind matches a scalar target (boolean, number in range, string) MUST convert to that
  target in Elsa 4 (D-12). Text MUST NOT be parsed into booleans or numbers.

**Protection**

- **FR-025**: A script result MUST carry the strongest protection policy of the values the script read, taken from
  its read set (Q8).

**Contracts**

- **FR-026**: The adapter contracts for JavaScript, activity inputs and outputs, and Liquid MUST be written down: what
  each accepts, what it produces, what is lossless, lossy or refused, and the diagnostic for each refusal.
- **FR-027**: A change to an adapter rule MUST ship as a new pinned version, never as an edit of an existing one. A
  published executable keeps the version it was published with.

### Key Entities

- **Core value**: type (alias, collection kind, optional JSON Schema), presence, JSON content or a reference,
  protection policy.
- **Type reference**: an alias and a collection kind, whose element may itself be a type reference (Q6).
- **Entry record**: per script run, what entered for each value read, used to find changes and lossy numbers.
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
- **SC-007**: A script run with change detection on a 1 MB input takes no more than twice as long as today. Integrity
  comes before speed (V9); the number is a guard against a surprise, not a goal.
- **SC-008**: The JTest corpus converts and publishes at least as many definitions as before this spec.

## Spec decisions

Questions this spec settles, with the recommended answer. The requirements follow the recommendations.

- **Q1 — Retire `Object` for `Any`, shown as JSON?** (#3422 Q5, open with the ADR 0046 reviewers.) **Recommended:
  yes,** as the ADR text says. If the answer is no, User Story 4 and FR-016 to FR-018 change; the rest of this spec
  stands.
- **Q2 — How does the adapter find what a script changed?** **Recommended: copy, compare, patch.** Keep what entered,
  compare each read value with it after the run, and write only the changed paths. Rejected: a JavaScript proxy per
  value (needs a custom object per value and cannot give real arrays; the ADR rules out views), and writing every value
  read (breaks FR-002).
- **Q3 — Who builds the variable write surface?** **Recommended:** the write surface (`variables.x = …` commits with
  the step) is the ADR 0090 amendment's. S-8 builds it for script activities as part of User Story 3, because the
  change detection is the only way to know what to write. S-7 builds the other activity writes.
- **Q4 — How does an author accept a precision loss?** The ADR says acceptance is explicit and recorded in the
  published contract; D-17 says the policy has no options and no per-workflow override. **Recommended:** one setting
  on a script activity node, "accept precision loss", recorded in the executable. It is a node choice, not a policy
  option, so D-17 holds. Alternative: no acceptance in S-8; the remedy is a string. That needs an ADR 0046 wording
  change.
- **Q5 — When is a number a loss?** **Recommended:** when the decimal value of the stored JSON number differs from the
  decimal value of the double it becomes. `10.50` and `2.0` are no loss; `9007199254740993` is. Checked once at entry.
- **Q6 — How does a type describe a dictionary of arrays?** **Recommended:** a type reference's element may itself be
  a type reference, so `IDictionary<string, string[]>` is a dictionary of arrays of `String`. Rejected: describing it
  only by JSON Schema (the binding checks would not see it).
- **Q7 — Elsa 3 result into a typed input.** **Recommended:** convert per JSON kind where Elsa 3's coercion is
  reproduced exactly, refuse at import otherwise. Fix #3167 first and measure again, as it may make more bindings
  convert.
- **Q8 — Is protection inheritance in S-8?** **Recommended: yes, P3,** on the read set ADR 0092 provides. It is a
  value model rule and has no other spec.

## Assumptions

- ADR 0046 amendment at `ba99ca02d` is the text applied. If it changes, this spec is rechecked.
- The ForEach and snapshot parts of #3421 are fixed already. Only the script part remains.
- S-9 delivers date interpretation, user type conversion contracts and the shared serializer baseline. S-8 uses them.
- S-1 delivers the host function mechanism and the refusal of Elsa 3 scripts that use .NET members.
- Elsa 4 has no released definitions, so reading `Object` as `Any` needs no migration.
- The Elsa 3 coercion rules per kind come from the Elsa 3 3.6.3 source, outside this repository, as in spec 267.
