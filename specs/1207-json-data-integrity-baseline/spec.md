# Feature Specification: JSON data integrity baseline

**Feature Branch**: `agent/claude/3052-s9-json-integrity`

**Created**: 2026-11-03

**Status**: Draft — **provisional**. Approval waits on the ADR PRs listed under *Approval*.

**Initiative**: part of [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052), spec row **S-9**.

**Issues**: none yet. The work comes from analysis
[A-5](https://github.com/elsa-workflows/elsa-foundation/issues/3052#issuecomment-6451104603) (decision-matrix row 21,
scenario S-e) and the [value-path search](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6491919492).

**Input**: Elsa writes workflow data exactly as it received it. Every JSON serializer derives from one read-only
baseline that transforms no data, as ADR 0091 records it.

## Approval

> **Provisional.** This spec may be drafted now (see [initiatives](../../docs/contributing/initiatives.md#phases)).
> It cannot be approved until every ADR it applies is agreed. If an ADR changes before agreement, this spec follows it.

**Which ADR text this spec applies.** ADR 0091 is not on `main`: it exists only as the draft on #3408. This spec
applies the drafts on these PRs, at the heads named below, as if they were agreed. Where a draft does not touch an
ADR, the version on `main` applies (ADR 0035, 0036 and 0038). The answers in *Open questions* hold only on that
basis: if the drafts change, the answers are rechecked.

| ADR PR | What this spec takes from it | Draft applied | State on 2026-11-04 |
|---|---|---|---|
| [#3408](https://github.com/elsa-workflows/elsa-foundation/pull/3408), ADR 0091 (A-5) | Decisions 1–8: the baseline, no data transformation, raw passthrough, one encoder, JSON at every boundary, no host-option changes, the deviation register, nulls kept | `725bd63c5` | In review. @FransVanEk's answers are in the text (fe0bf8419); no approving review yet. Open questions 1–5 wait for @sfmskywalker; question 6 answered by all three |
| [#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422), ADR 0046 amendment (A-1), *Dates* only | The `DateTime`-as-UTC rule that ADR 0091 decision 5 applies through the baseline copy (FR-016). A dependency because Q8 keeps it in S-9 | `680e3f9ee` | In review (copy, compare, patch and V12 proposed on 3 Nov) |

**Related, not blocking:**
- [#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422) apart from *Dates*: the JavaScript adapter
  (copy, compare, patch, V9) keeps untouched values' text inside scripts. That is S-8's work; this spec covers the
  serializer only.
- [#3425](https://github.com/elsa-workflows/elsa-foundation/pull/3425) (ADR 0090 review, C4) and ADR 0092
  ([#3411](https://github.com/elsa-workflows/elsa-foundation/pull/3411)) use the canonical form of FR-021 for value
  equality. They consume it; this spec defines it.
- [#3726](https://github.com/elsa-workflows/elsa-foundation/pull/3726) (S-2, spec 1201) owns the null representation
  (ADR 0091 decision 8). This spec only requires that the serializer keeps it.

**Approval plan**: the initiative's approvers, @sfmskywalker and @FransVanEk (technical architects), approve the final
text; @j03y-nxxbz is the author and records agreement in a PR comment. **Implementation**: not before every spec of
#3052 is defined, as #3052 agreed.

## Rule and boundary

**Rule**: data is JSON that users author or send. Elsa carries it and does not own its meaning, so Elsa writes it as
it received it: the same keys, the same casing, the same text. Only Elsa's own contracts follow Elsa's naming.

**Terms** (from ADR 0091): *data*, *own contract*, *name key*, *baseline* and *deviation* as ADR 0091 defines them.
*Protected settings* are the dictionary key policy, number handling, null handling, the encoder, and converters or
type-info modifiers that change or reorder data.

**In scope**: every JSON serializer under the source roots (module contexts, API wire contexts, `IPayloadSerializer`,
the host HTTP options, local option sets on the value path); data-to-.NET conversions at activity inputs, results,
literals, defaults and stateful state; content hashes and fingerprints over serialized text; the runtime durable
format (stamped migration); the Elsa 3 converter's rewrite of case-only key differences; the architecture test and
the deviation register.

**Out of scope**:
- The null representation for declared members (S-2, spec 1201). FR-018 only requires that it survives.
- How a script sees values and which paths it writes back (S-8, ADR 0046 V9 and V12). A value a script changed is a
  new value; this spec only fixes the format it is written in.
- Number handling inside the script engine (ADR 0046 V6, V9).
- Secrets `json` / `jsonb` columns, which the database normalizes (ADR 0091 decision 7).
- Related, not a dependency: [#3799](https://github.com/elsa-workflows/elsa-foundation/issues/3799), a general Elsa 4
  defect, fixed on 2026-11-04 by #3801 (`4a81500f8`). The completion projector and the input hydrator now share one
  options instance, `ActivityValueJson.Options` (Web, camelCase, strict numbers), so a .NET-typed result keeps its
  members on the next activity's input. S-9 replaces that instance with the data-boundary copy (FR-012), keeping
  #3801's tests. One change follows from ADR 0091 decision 2: `ActivityValueJson.Options` reads member names
  case-insensitively (Web), and the data-boundary copy reads them exactly. A key that differs only in case then gives
  the `json-unmatched-member` warning instead of filling the member.
- Related, not a dependency: [#3728](https://github.com/elsa-workflows/elsa-foundation/issues/3728), fixed by #3731
  (`4c002dadb`). Every start now applies the input contract through `WorkflowExecutableInputValidator`, so that
  validator's conversion (FR-012) governs every start path: API, triggers, BPMN and child starts.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Names keep their casing (Priority: P1)

A workflow author names an output `OrderTotal`, an input `CustomerId` or an expression parameter `Amount`. Every API
returns those names as authored. Today the Runtime API returns `orderTotal`, and Publishing and Activities Design
write input names camelCased, because their options set a dictionary key policy.

**Why this priority**: a renamed key is a changed contract the author never asked for (matrix row 21). It is the
cheapest fix and the first step of this spec (ADR 0091, *Alternatives*).

**Independent Test**: run a workflow with an output `OrderTotal`; the instance-detail response has the key
`OrderTotal`.

**Acceptance Scenarios**:

1. **Given** a workflow output named `OrderTotal`, **When** the instance is read through the Runtime API, **Then** the
   outputs map has the key `OrderTotal`.
2. **Given** an expression binding with parameter `Amount`, **When** the executable view is returned, **Then** the
   parameter key is `Amount`.
3. **Given** a workflow input `SomeInput`, **When** Publishing or Activities Design writes it, **Then** the key is
   `SomeInput`; a key that starts with a lowercase letter is unchanged.
4. **Given** a host with several modules, **When** a module that does not own a response writes it, **Then** its key
   policy, encoder and enum handling are the host's, not another module's.

---

### User Story 2 - Data comes back as it was sent (Priority: P1)

A client sends a variable default, a workflow input or an HTTP body with its own escaping, number text, key order and
inner whitespace. Every API and store returns it byte-identical while nothing changed it.

**Why this priority**: data integrity is the point of A-5; today every written `JsonElement` is re-escaped
(`René` → `René`), and modules disagree on the escaping.

**Independent Test**: the two round trips of ADR 0091's *Required evidence*: a Runtime output and a Design variable
default come back byte-identical.

**Acceptance Scenarios**:

1. **Given** an output `OrderTotal` holding `{"CustomerId":1,"Name":"René","N":1.0}`, **When** the instance is
   read back, **Then** the key is `OrderTotal` and the value text is byte-identical.
2. **Given** a variable default with capitalized keys, escaped characters, inner whitespace and `1.0`, **When** the
   definition is saved and read back, **Then** the default is byte-identical.
3. **Given** a workflow that suspends with such a value in its state, **When** it is resumed after a restart,
   **Then** the value reaches the next activity byte-identical (stamped migration, FR-023).
4. **Given** an indented writer, **When** it writes a received value, **Then** it does not re-indent that value.
5. **Given** a value a script or an activity changed, **When** it is written, **Then** it uses the default output
   format (FR-009).

---

### User Story 3 - A .NET-typed value survives from one activity to the next (Priority: P1)

An activity returns a .NET object; the next activity takes the same type as input. Every property arrives. Until
#3801 the result was written camelCased and read back case-sensitively, so properties were lost silently (#3799).
#3801 fixed that pair with one shared options instance. S-9 moves that pair, and every other conversion on the value
path, onto one data-boundary copy.

**Why this priority**: it was the highest-risk finding of the value-path search, and the other conversions still use
differing options.

**Independent Test**: a .NET-typed result bound to a .NET-typed input of the same type arrives with every property
set. #3801's `ActivityResultRoundTripTests` already covers this; it stays green.

**Acceptance Scenarios**:

1. **Given** a .NET-typed activity result, **When** it is bound to the next activity's input of the same type,
   **Then** every property is set.
2. **Given** `{"customerName":…}`, **When** it is read into a type with `CustomerName`, **Then** the property is set
   (exact match under the baseline's naming policy).
3. **Given** `{"CustomerName":…}` and no annotation, **When** it is read, **Then** the property stays unset, the step
   runs, the stored JSON keeps the key, and a recorded warning names the key and the target type.
4. **Given** a 20-digit integer and a 29-digit decimal, **When** they pass through ForEach and an activity input,
   **Then** their text is unchanged unless the target type narrows them.
5. **Given** `"2026-10-28T09:15:00+05:00"` read into a `DateTime`, **When** it runs on a UTC and a non-UTC server, at
   run time and in a publish-time literal, **Then** both give the same instant (ADR 0046 *Dates*).
6. **Given** `"5"` read into an `Int32`, **When** it is read on any path, **Then** every path gives the same result
   (numbers are never read from strings).

---

### User Story 4 - Content hashes stay stable (Priority: P2)

A definition reconciled from Git, or stored in the design store, keeps its content hash after the serializer starts
writing raw text and a different encoder. Equal values hash equal, whatever their escaping or whitespace.

**Why this priority**: without it, every definition read back from Git hashes as changed the moment US2 lands
(intake check 7).

**Independent Test**: write definitions to files, read them back, and compare every content hash.

**Acceptance Scenarios**:

1. **Given** a definition with escaped characters in a variable default, **When** it is written to Git and read
   back, **Then** its content hash is unchanged and the file holds the default's text as received.
2. **Given** design-store rows written before this change, **When** their fingerprints are checked, **Then** they
   are still valid.
3. **Given** any stored workflow executable or activity definition, **When** its hash is recomputed after this
   change, **Then** it is unchanged.
4. **Given** two values that differ only in escaping or inner whitespace, **When** they are hashed or compared,
   **Then** they are equal.

---

### User Story 5 - Explicit nulls are kept (Priority: P2)

A client stores `{"x":null}`. The payload serializer writes the `null` member instead of dropping it, so S-2's
representation of explicit null survives storage. Rows written before still read the same.

**Why this priority**: S-2 depends on it (ADR 0091 decision 8).

**Independent Test**: serialize a payload with an explicit null member and read it back; read an old row without the
member.

**Acceptance Scenarios**:

1. **Given** a payload with an explicit null member, **When** it is serialized, **Then** the output has the member
   with `null`.
2. **Given** a row written before this change (member missing), **When** it is read, **Then** it reads as before.

---

### User Story 6 - One output format, safe to serve (Priority: P2)

Every API and store writes Elsa's own strings and changed values with one encoder. Every response carrying raw or
relaxed-encoded data is `application/json` and is never sniffed as HTML.

**Why this priority**: today the same value leaves Runtime and Publishing with different text; the relaxed encoder
is only safe when responses are never rendered as HTML.

**Independent Test**: the same value written by the Runtime, Design and Publishing APIs has identical text; every
data-carrying response has `Content-Type: application/json` and `X-Content-Type-Options: nosniff`.

**Acceptance Scenarios**:

1. **Given** a changed value with `é`, `<` and `+`, **When** Runtime, Design and Publishing write it, **Then** the
   text is identical.
2. **Given** any endpoint that returns data, **When** it responds, **Then** the headers are `application/json` and
   `nosniff`.

---

### User Story 7 - Deviations are visible (Priority: P3)

A module author who needs a different serializer setting can have one, but only as a listed, justified deviation.
An architecture test fails on any deviation that is not in the register.

**Why this priority**: it keeps the next module from reintroducing a key policy or a host-wide change unnoticed.

**Independent Test**: add an unregistered deviation of each scanned kind; the test fails. Add a register entry; it
passes.

**Acceptance Scenarios**:

1. **Given** a new `JsonSerializerOptions` that sets a protected setting, **When** the architecture test runs,
   **Then** it fails and names the file and setting.
2. **Given** the same deviation with a register entry, **When** the test runs, **Then** it passes.
3. **Given** a module resolver that describes a type the module does not own, or overlaps another module's resolver
   in the host options, **When** the test runs, **Then** it fails.

---

### User Story 8 - Converted Elsa 3 literals keep binding (Priority: P3)

Elsa 3 read member names case-insensitively. A converted workflow's literal `{"CustomerName":…}` for an input whose
JSON name is `customerName` is rewritten to the exact name, so it binds as it did in Elsa 3.

**Why this priority**: exact member reads (US3) would otherwise silently drop converted literals; D-13 requires
faithful-or-refuse.

**Independent Test**: convert an Elsa 3 workflow with a case-only key difference and with an ambiguous pair.

**Acceptance Scenarios**:

1. **Given** a literal key that differs from the exact JSON name only in case, **When** it is converted, **Then** the
   key is rewritten to the exact name.
2. **Given** `Name` and `NAME` in one object, **When** it is converted, **Then** conversion refuses with guidance.

### Edge Cases

- A value that passes through a script unchanged may still change today, in the JavaScript adapter (ADR 0091
  decision 3). That is S-8's fix; this spec's round trips do not pass through a script.
- Two equal values can have different stored text. Nothing that decides whether a value changed compares stored text
  (FR-021).
- A stored row written before this change keeps its normalized text; it is not rewritten.
- A key that starts with a lowercase letter is byte-identical before and after US1.
- A `JsonElement` of kind `Undefined` (a default struct) is not valid JSON to write raw; S-2's writer omits it.
- An HTTP body that stays a raw string is data already; it is not re-parsed.

## Requirements *(mandatory)*

### Functional Requirements

**Baseline (decision 1)**
- **FR-001**: `Elsa.Serialization.Core` MUST expose one baseline `JsonSerializerOptions`, read-only.
- **FR-002**: Every serializer under every source root (Q2) MUST derive from the baseline, as a copy
  (`new JsonSerializerOptions(baseline)`). That covers module serializer contexts, API wire contexts,
  `IPayloadSerializer`, the host HTTP options and the local option sets on the value path. A source-generated context
  MUST be constructed with such a copy, and owner code MUST use that instance, not the context's `Default`. Hash
  preimages are the exception: they keep frozen options (FR-021, Q4).
- **FR-002a**: Serializer calls that pass neither options nor type info MUST be migrated on the value path. Elsewhere
  they MUST be listed in a ratchet file that may only shrink (Q3).

**No data transformation (decision 2)**
- **FR-003**: The baseline MUST set no dictionary key policy and no number handling, MUST keep nulls, MUST have no
  converter that changes or reorders data, and MUST camelCase only the member names of Elsa's own types.
- **FR-004**: Member reads MUST be exact: a JSON member fills a .NET member only when its name is the member's JSON
  name (the naming policy's name, or an annotation's).
- **FR-005**: A JSON key that matches no member MUST give a recorded warning naming the key and the target type; the
  step runs and the stored JSON keeps the key. An activity MAY declare strict reads for itself.
- **FR-006**: Every `DictionaryKeyPolicy` setting under the source roots MUST be removed; the contract tests that
  assert camelCased keys MUST be inverted to assert verbatim keys.

**Raw passthrough (decision 3)**
- **FR-007**: The baseline MUST write a `JsonElement` as its original text, without re-encoding or re-indenting it.
- **FR-008**: A value an endpoint accepted MUST be stored and returned byte-identical while nothing changed it, in
  every API and store, including the runtime durable state (FR-023).

**One output format (decision 4)**
- **FR-009**: Everything FR-007 does not cover (Elsa's own strings and member names, created or changed values) MUST
  be written with one encoder in every API and store, `UnsafeRelaxedJsonEscaping` as ADR 0091 proposes (its open
  question 2).
- **FR-010**: Every response of Elsa's own APIs that carries data MUST have `Content-Type: application/json` and
  `X-Content-Type-Options: nosniff`. A response a workflow composes (`WriteHttpResponse`) keeps the content type and
  headers the workflow chose, as a register entry. When the workflow sets no content type, a structured body is
  `application/json` and a text body is `text/plain`, as spec 256 already delivers (Q6).

**JSON at every boundary (decision 5)**
- **FR-011**: At API, persistence and messaging boundaries, data MUST be held as `JsonElement` or `JsonNode`, never a
  .NET dictionary, `ExpandoObject` or POCO.
- **FR-012**: Every conversion between data and a .NET type, in either direction, MUST use the same baseline copy:
  activity inputs, expression results coerced to a declared type, publish-time literals and variable defaults,
  workflow-input validation, activity results, stateful activity state, transitions, seeded values and code-first
  literals.
- **FR-013**: No conversion MUST narrow a number to `long` or `double` unless the target type asks for it.
- **FR-014**: No path MUST read a number from a string.
- **FR-015**: The Elsa 3 converter MUST rewrite a literal key that differs from the exact JSON name only in case to
  that name, and MUST refuse with guidance when the rewrite is ambiguous.
- **FR-016**: A `DateTime` MUST be read as UTC at every conversion of FR-012, at run time and in publish-time
  literals, as ADR 0046 *Dates* states (see *Approval*).

**Host options (decision 6)**
- **FR-017**: A module MUST NOT change the host's naming, key policy, encoder, null handling or converters. It MAY add
  a type-info resolver to the host options that describes only types it owns.

**Nulls (decision 8)**
- **FR-018**: `IPayloadSerializer` MUST keep explicit nulls, and every serializer MUST keep the representation S-2
  chooses for explicit null and absent. Rows written before MUST read the same.

**Deviations (decisions 6 and 7)**
- **FR-019**: `docs/serialization.md` MUST gain a "Baseline deviations" register listing, per deviation, the file,
  the setting, the reason and the approving ADR or issue. A deviation that touches data at an API boundary MUST cite
  an ADR.
- **FR-020**: An architecture test MUST fail on any unregistered deviation. It MUST combine a source scan (option
  constructions, `JsonSerializerOptions.Web` / `.Default`, source-generation attribute settings, serializer calls
  without options or type info) with reflection over every module's constructed contexts and the composed host HTTP
  options, including resolver overlap.

**Canonical hashing (decision 7)**
- **FR-021**: Every content hash and fingerprint over JSON MUST hash a canonical re-serialization, never stored or
  transmitted text. Each preimage MUST keep its own frozen options, as a register entry, so that no ADR 0038 artifact
  hash or stored definition hash moves (Q4). Equality of values, including every `Ordinal` comparison of a stored
  value with a fresh serialization, MUST use the canonical form.
- **FR-022**: Git definition files MUST keep data text as received, and the Git reconciliation content hash MUST be
  built on the canonical form, so a Git round trip keeps every hash (Q5). The design-store fingerprint is already
  canonical and MUST stay so. No stored hash needs recomputing (research R-7).

**Runtime durable state (decision 7, question 6)**
- **FR-023**: The runtime EF writers MUST adopt FR-007 and FR-018. The stored format stays readable by old and new
  code, so this needs no migration and no change to a schema family's stamp. A byte-level golden fixture MUST pin a
  stored data value's text. Before 4.0, state saved earlier is not read back (reset and republish). From 4.0 on, a
  writer change that keeps every row readable both ways needs no new family version; a change to what a row means
  is a new family version under spec 180 (Q7).

**Docs and housekeeping**
- **FR-024**: `docs/serialization.md` MUST state the baseline and the data rule, and its "HTTP boundary" exception
  MUST stop naming FastEndpoints.
- **FR-025**: Every warning and refusal in this spec MUST have a stable diagnostic code.

### Key Entities

- **Baseline**: the one read-only options instance; every serializer is a copy of it.
- **Deviation**: a serializer whose protected settings differ from the baseline; listed in the register.
- **Baseline deviation register**: the section in `docs/serialization.md` the architecture test reads.
- **Canonical preimage**: the fixed re-serialization a content hash or equality check is taken over.
- **Durable format stamp**: the marker that identifies the runtime store's serialization format version.

## Open questions for the approvers

ADR 0091 leaves Q1 to the spec, Q8 sits between ADR 0091 and ADR 0046, and the code survey added Q2 to Q7 (research
R-0). Q3 to Q7 are also raised as review comments on #3408 (findings F-1 to F-5), because the code shows that ADR 0091
is incomplete or wrong there. The requirements below follow the proposals. If #3408 decides otherwise, this spec
follows #3408. Each question has a proposal and @j03y-nxxbz's answer. All three approvers answer them on the PR, and
the PR's reconciliation table records the answers.

| # | Question | Proposal | @j03y-nxxbz |
|---|---|---|---|
| Q1 | ADR 0091 decision 2 leaves to S-9 how an unmatched key is detected. How? | A pre-scan of the JSON keys against the target's `JsonTypeInfo` before every data-to-.NET conversion, with one `json-unmatched-member` warning per key in the invocation record (research R-13). Rejected: a synthetic extension-data member (changes user types), and deserializing twice with `Disallow` (two passes, first key only). | Agrees |
| Q2 | Which source roots does "every serializer" (decision 1) and the architecture test cover? | Every source root: Essentials, Extensions and Studio's C#. Extension-internal options on their own contracts are register entries (R-10). | Agrees |
| Q3 | About 267 serializer calls pass neither options nor type info (R-10). ADR 0091 counts 32 constructions and does not count these. List each one in the register, or migrate them all? | Migrate every value-path site (FR-012) in S-9. List the rest, which serialize Elsa's own contracts, in a ratchet file that may only shrink. The register keeps one row per reason, not one per call. Finding F-5 on #3408. | Agrees |
| Q4 | Hash preimages: decision 1 makes every serializer derive from the baseline, but several hash preimages use other options, and the activity contract fingerprint feeds every ADR 0038 artifact hash (R-1). | Every hash preimage keeps its own frozen options and is a register entry; it never derives from the baseline. Direct text hashes and `Ordinal` comparisons of serialized values move to the canonical form (FR-021). Finding F-2 on #3408. | Agrees |
| Q5 | Git files: ADR 0091 expects the Git hash to break, but it does not (R-7). Instead, Git files never hold data text as received, because the exporter re-emits them through `JsonNode`. | Git files keep data text (the exporter writes with the baseline), and the Git hash is built on the canonical form. Rejected: registering Git files as a normalized rendering (users diff them). Finding F-1 on #3408. | Agrees |
| Q6 | Decision 4 relies on "every Elsa response is `application/json`", but workflow HTTP responses use the content type the workflow chose, and `nosniff` is set nowhere (R-12). | `application/json` plus `nosniff` on Elsa's own APIs; workflow HTTP responses keep the author's content type and headers, as a register entry. Finding F-3 on #3408. | A workflow response uses the content type the workflow sets. When none is set, it is `application/json`, except a text body, which stays `text/plain` as in Elsa 3 (spec 256). |
| Q7 | The runtime durable state change (question 6): ADR 0091 says regenerated migrations and golden fixtures, but no model change and no runtime-row golden exist (R-9). | No migration and no stamp bump. The writers change, and S-9 adds the runtime store's first byte-level golden. From 4.0 on, a writer change that keeps every row readable both ways needs no new family version; a change to what a row means does (spec 180). Finding F-4 on #3408. | Agrees |
| Q8 | Who implements "a `DateTime` is read as UTC": S-9, through the data-boundary copy, or S-8 with ADR 0046? | S-9, as a converter in the data-boundary copy, because ADR 0091 decision 5 applies it through that copy and lists its test. This makes #3422 *Dates* a dependency of S-9. | Agrees, with #3422 *Dates* as a dependency |

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: No authored name key (output, input, variable, parameter, node id) changes casing between request and
  response on any API.
- **SC-002**: Both round trips of US2 return byte-identical text, through every store a value is persisted in.
- **SC-003**: Zero properties are lost when a .NET-typed result is bound to an input of the same type.
- **SC-004**: Zero content hashes change in a Git round trip of every definition in the repository's fixtures.
- **SC-005**: The architecture test reports zero unregistered deviations, and fails on each scanned kind when one is
  added.
- **SC-006**: The same changed value has identical text from every API.

## Assumptions

- Elsa 4 has no released runtime store, so before 4.0 a format change costs regenerated migrations and fixtures, not
  an upcaster (ADR 0091 question 6).
- Studio parses JSON and copies `errors` keys verbatim without mapping them to field paths, so no Studio change is
  needed (research R-3).
- No client inside the repository depends on camelCased output names (research R-4). Clients outside it cannot be
  checked from here, and the FastEndpoints-era wire shape is not kept.
- The host HTTP options already use the relaxed encoder (research R-8). For Elsa's own APIs, decision 4 changes the
  Runtime and Design owner contexts and `IPayloadSerializer`, which use the default encoder today.
- Rows stored today were normalized on first write and stay as they are.
