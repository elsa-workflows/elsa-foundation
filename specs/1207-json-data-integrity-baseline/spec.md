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

| ADR PR | What this spec takes from it | Draft applied | State on 2026-11-11 |
|---|---|---|---|
| [#3408](https://github.com/elsa-workflows/elsa-foundation/pull/3408), ADR 0091 (A-5) | Decisions 1–8: the baseline, no data transformation, raw passthrough, one encoder, JSON at every boundary with scoped conversion contracts for user types, no host-option changes with staged enforcement, the deviation register with frozen hash inputs, nulls kept | `46c3a40eb` | In review. @sfmskywalker's cards 6–13 and @FransVanEk's review conditions reconciled at `46c3a40eb`; no approving review yet |
| [#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422), ADR 0046 amendment (A-1), *Dates* only | The date/time interpretation rule (FR-016): an offset, or an explicit interpretation recorded in the published conversion contract (a source time zone or an explicit assume-UTC policy); never an implicit UTC or server-local default ([card 14](https://github.com/elsa-workflows/elsa-foundation/pull/3422#issuecomment-6644338442)). A dependency because S-9 implements it ([card 24](https://github.com/elsa-workflows/elsa-foundation/pull/3805#issuecomment-6645506583), Q8) | `680e3f9ee` plus card 14 | In review; the *Dates* section still states automatic UTC and is to be amended to card 14 |

**Related, not blocking:**
- [#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422) apart from *Dates*: the JavaScript adapter
  (copy, compare, patch, V9) keeps untouched values' text inside scripts. That is S-8's work; this spec covers the
  serializer only.
- [#3425](https://github.com/elsa-workflows/elsa-foundation/pull/3425) (ADR 0090 review, C4) and ADR 0092
  ([#3411](https://github.com/elsa-workflows/elsa-foundation/pull/3411)) use the semantic value comparison of FR-021
  for value equality. They consume it; this spec defines it.
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
format (a compatibility proof, else a stamped format change); the Elsa 3 converter's rewrite of case-only key
differences; the architecture test, the legacy list and the deviation register.

**Out of scope**:
- The null representation for declared members (S-2, spec 1201). FR-018 only requires that it survives.
- How a script sees values and which paths it writes back (S-8, ADR 0046 V9 and V12). A value a script changed is a
  new value; this spec only fixes the format it is written in.
- Number handling inside the script engine (ADR 0046 V6, V9).
- Secrets `json` / `jsonb` columns, which the database normalizes (ADR 0091 decision 7).
- Related, not a dependency: [#3799](https://github.com/elsa-workflows/elsa-foundation/issues/3799), a general Elsa 4
  defect, fixed on 2026-11-04 by #3801 (`4a81500f8`). The completion projector and the input hydrator now share one
  options instance, `ActivityValueJson.Options` (Web, camelCase, strict numbers), so a .NET-typed result keeps its
  members on the next activity's input. S-9 replaces that instance with the default conversion contract (FR-012),
  keeping #3801's tests. One change follows from ADR 0091 decision 2: `ActivityValueJson.Options` reads member names
  case-insensitively (Web), and the default conversion contract reads them exactly. A key that differs only in case then gives
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
   **Then** the value reaches the next activity byte-identical (FR-023).
4. **Given** an indented writer, **When** it writes a received value, **Then** it does not re-indent that value.
5. **Given** a value a script or an activity changed, **When** it is written, **Then** it uses the default output
   format (FR-009).

---

### User Story 3 - A .NET-typed value survives from one activity to the next (Priority: P1)

An activity returns a .NET object; the next activity takes the same type as input. Every property arrives. Until
#3801 the result was written camelCased and read back case-sensitively, so properties were lost silently (#3799).
#3801 fixed that pair with one shared options instance. S-9 moves that pair, and every other conversion on the value
path, onto the conversion contract of the target type: the default one unless a scoped one is selected.

**Why this priority**: it was the highest-risk finding of the value-path search, and the other conversions still use
differing options.

**Independent Test**: a .NET-typed result bound to a .NET-typed input of the same type arrives with every property
set. #3801's `ActivityResultRoundTripTests` already covers this; it stays green.

**Acceptance Scenarios**:

1. **Given** a .NET-typed activity result, **When** it is bound to the next activity's input of the same type,
   **Then** every property is set.
2. **Given** `{"customerName":…}`, **When** it is read into a type with `CustomerName` under the default conversion
   contract, **Then** the property is set (exact match under that contract's naming).
3. **Given** `{"CustomerName":…}`, no annotation and the default contract, **When** it is read, **Then** the property
   stays unset, the step runs, the stored JSON keeps the key, and a recorded warning names the key and the target type.
4. **Given** a 20-digit integer and a 29-digit decimal, **When** they pass through ForEach and an activity input,
   **Then** their text is unchanged unless the target type narrows them.
5. **Given** `"2026-10-28T09:15:00+05:00"` read into a `DateTime` or `DateTimeOffset`, **When** it runs on a UTC and a
   non-UTC server, at run time and in a publish-time literal, **Then** both give the same instant (its own offset).
   **Given** `"2026-10-28T09:15:00"` (no offset), **When** it is converted to an instant with no source time zone and
   no assume-UTC policy in the conversion contract, **Then** the conversion is refused with guidance; with an explicit
   assume-UTC policy it is UTC, and with a declared source time zone it is that zone's instant, on any server.
6. **Given** `"5"` read into an `Int32`, **When** it is read on any path, **Then** every path gives the same result
   (numbers are never read from strings).
7. **Given** a scoped conversion contract that selects case-insensitive matching or an explicit mapping, **When**
   `{"CustomerName":…}` is read, **Then** the member is filled with no warning, a key consumed by a mapping, dictionary
   or extension-data member is not reported, and no host-wide option changes.
8. **Given** a scoped contract that selects strict unmatched-member handling, **When** a key is unmatched, **Then** the
   conversion is refused; **Given** any contract, **When** a required member is missing, **Then** it fails.
9. **Given** `id` and `ID` in one object under the default contract, **When** it is read, **Then** the matching key
   fills the member and the other gets the unmatched-member warning.
10. **Given** a target whose custom converter implements another shape, **When** it is read, **Then** the pre-scan
    uses that converter's matching information or records that unmatched-key coverage is unavailable for the type;
    it reports no false unmatched keys.

---

### User Story 4 - Content hashes stay stable (Priority: P2)

A definition reconciled from Git, or stored in the design store, keeps its content hash after the serializer starts
writing raw text and a different encoder. Every existing hash input stays frozen, and equal values compare equal,
whatever their escaping or whitespace.

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
4. **Given** two values that differ only in escaping or inner whitespace, **When** a check compares them for
   equality, **Then** they are equal (semantic comparison, not text).

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

Every API and store writes Elsa's own strings and changed values with one encoder. Every response of Elsa's own APIs
carrying raw or relaxed-encoded data is `application/json` and is never sniffed as HTML. A workflow-authored HTTP
response uses the content type its author selected.

**Why this priority**: today the same value leaves Runtime and Publishing with different text; the relaxed encoder
is only safe when responses are never rendered as HTML.

**Independent Test**: the same value written by the Runtime, Design and Publishing APIs has identical text; every
data-carrying response has `Content-Type: application/json` and `X-Content-Type-Options: nosniff`.

**Acceptance Scenarios**:

1. **Given** a changed value with `é`, `<` and `+`, **When** Runtime, Design and Publishing write it, **Then** the
   text is identical.
2. **Given** any Elsa API endpoint that returns data, **When** it responds, **Then** the headers are
   `application/json` and `nosniff`.
3. **Given** a workflow-authored HTTP response, **When** its author selected `text/html`, **Then** it stays
   `text/html`; **When** none is selected, **Then** a text body such as `Hello` is `text/plain` and a JSON object is
   `application/json`.

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
  MUST be constructed with such a copy, and owner code MUST use that instance, not the context's `Default`. Existing
  hash inputs are the exception: they keep frozen options (FR-021, Q4). A user's selection of a supported scoped
  conversion contract (FR-004) is configuration, not a deviation.
- **FR-002a**: Enforcement MUST be staged (Q3, card 11). Every workflow-data and value conversion path MUST be fixed
  in S-9, using the baseline or the applicable conversion contract; none is deferred. Existing calls that pass
  neither options nor type info and handle only Elsa's own contracts MUST be tracked in a **legacy list** (a ratchet
  file) that may only shrink: an entry leaves when its call is migrated, no entry is added, and the list never holds a
  workflow-data path. A new call that is neither derived from the baseline, nor in the register, nor on the legacy
  list MUST fail the architecture test. The legacy list is not the register: it holds calls still to migrate, not
  justified deviations, which keep reasoned register entries.

**No data transformation (decision 2)**
- **FR-003**: The baseline MUST set no dictionary key policy and no number handling, MUST keep nulls, MUST have no
  converter that changes or reorders data, and MUST camelCase only the member names of Elsa's own types.
- **FR-004**: Conversion into a user type MUST use a **conversion contract** selected for that type or conversion
  boundary, never host-wide options (decision 2, card 6). Elsa's own contracts keep the baseline's camelCase naming and
  exact reads. S-9 documents a **default conversion contract**: camelCase naming (what activity results are written
  with since #3801) and exact, case-sensitive matching, so a JSON member fills a .NET member only when its name is
  exactly the name the contract gives that member (through its naming policy, an explicit mapping or an annotation).
  Scoped options MUST be offered per user type or conversion boundary: the naming policy (for example CLR names,
  camelCase or snake_case), exact or case-insensitive matching, explicit member mappings, and unmatched-member
  handling. The selected behaviour MUST be explicit and stable in the published conversion contract and its version.
  Matching never rewrites the stored JSON keys.
- **FR-005**: A JSON member is unmatched only when the effective conversion contract (its naming policy, case
  sensitivity, explicit mappings, dictionaries and extension-data handling) gives it no member. By default an
  unmatched key MUST give a recorded warning naming the key path and the target type, and the conversion continues:
  the step runs and the stored JSON keeps the key. A scoped contract MAY select stricter handling, which refuses. A
  missing required member still fails (D-10). Detection is a **contract-aware pre-scan** before the conversion (Q1):
  - it MUST inspect the effective contract, never hard-coded baseline naming or case-sensitive recursion for every
    type, and MUST NOT report a key consumed by a mapping, a dictionary or an extension-data member;
  - where a custom converter implements another shape, it MUST take matching information from that converter's
    contract, or record that unmatched-key coverage is unavailable for that type; it MUST NOT emit false
    unmatched-key warnings or claim full coverage;
  - it MUST NOT add synthetic members to user types or deserialize twice to find unknown keys.
- **FR-006**: Every `DictionaryKeyPolicy` setting under the source roots MUST be removed; the contract tests that
  assert camelCased keys MUST be inverted to assert verbatim keys.

**Raw passthrough (decision 3)**
- **FR-007**: The baseline MUST write a `JsonElement` as its original text, without re-encoding or re-indenting it.
- **FR-008**: A value an endpoint accepted MUST be stored and returned byte-identical while nothing changed it, in
  every API and store, including the runtime durable state (FR-023).

**One output format (decision 4)**
- **FR-009**: Everything FR-007 does not cover (Elsa's own strings and member names, created or changed values) MUST
  be written with one encoder in every API and store, `UnsafeRelaxedJsonEscaping` as ADR 0091 proposes (its open
  question 2). Its safety argument rests on Elsa's own API responses only (FR-010).
- **FR-010**: The content-type rule is scoped (Q6, card 8). Every response of Elsa's own JSON APIs that carries data
  MUST have `Content-Type: application/json` and `X-Content-Type-Options: nosniff`. A workflow-authored HTTP response
  (`WriteHttpResponse`) MUST use the content type its author selected, so explicitly authored HTML stays `text/html`.
  When the author selects none, a text body MUST be `text/plain` and any other body `application/json`, as spec 256
  already delivers. Its body serialization and delivery follow that activity's contract.

**JSON at every boundary (decision 5)**
- **FR-011**: At API, persistence and messaging boundaries, data MUST be held as `JsonElement` or `JsonNode`, never a
  .NET dictionary, `ExpandoObject` or POCO.
- **FR-012**: Every conversion between data and a given .NET type, in either direction, MUST use the same effective
  conversion contract for that type (FR-004): a copy of the baseline with the naming and matching the contract
  selects and the protected settings unchanged, so a value written by one activity reads back the same in the next.
  This covers activity inputs, expression results coerced to a declared type, publish-time literals and variable
  defaults, workflow-input validation, activity results, stateful activity state, transitions, seeded values and
  code-first literals.
- **FR-013**: No conversion MUST narrow a number to `long` or `double` unless the target type asks for it.
- **FR-014**: No path MUST read a number from a string.
- **FR-015**: The Elsa 3 converter MUST rewrite a literal key that differs from the exact JSON name only in case to
  that name, and MUST refuse with guidance when the rewrite is ambiguous.
- **FR-016**: Converting a date/time value into an instant at a conversion of FR-012 (supported literals, inputs and
  defaults, at run time and at publication) MUST NOT use an implicit assume-UTC or the server's local time zone (Q8,
  cards 14 and 24):
  - a value that carries an offset establishes its instant directly;
  - a `DateTime` or `DateTimeOffset` value without an offset needs an explicit interpretation recorded in the
    published conversion contract: a declared source time zone or an explicit assume-UTC policy. Without one, the
    conversion MUST be refused with a diagnostic naming the path and target type and saying to add an offset or
    declare the interpretation. Assume-UTC is never an implicit global default;
  - normalizing to UTC after the instant is established is separate; a calendar-only value is not made an instant by
    this rule; a raw JSON date string is carried unchanged until a conversion is requested;
  - an Elsa 3 converted workflow follows its declared source interpretation, recorded in its conversion contract,
    never an implicit server setting.

  S-9 implements this contract at the JSON-to-.NET boundary; S-8 uses the same contract and adds no second rule.

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
- **FR-020**: An architecture test MUST fail on any unregistered deviation. It MUST combine a source scan over every
  source root (Essentials, Extensions and Studio's C#: option constructions, `JsonSerializerOptions.Web` / `.Default`,
  source-generation attribute settings, serializer calls without options or type info) with reflection over every
  module's constructed contexts and the composed host HTTP options, including resolver overlap. It MUST fail on a new
  option-less call that is not on the legacy list, pass when an entry is removed after its call is migrated, and fail
  when the legacy list holds a workflow-data path (FR-002a).

**Hash inputs and value equality (decisions 1, 3 and 7)**
- **FR-021**: Every existing hash or fingerprint input MUST keep its current options and preimage, frozen, and MUST be
  listed in the register with its input contract, so a baseline change never silently moves an ADR 0038 artifact
  identity, a stored hash or a fingerprint (Q4, card 9). It never derives from the baseline. Changing a hash input is
  a **separate, explicit decision** that assesses its identity, versioning and migration consequences; S-9 makes no
  such change implicitly. S-9 MUST list every hash taken directly over serialized text and every check that compares
  serialized text. A check that decides whether two values are equal (including every `Ordinal` comparison of a
  stored value with a fresh serialization) MUST use one defined semantic comparison of the values, never stored or
  freshly serialized text. A hash uses its own defined input, not that comparison.
- **FR-022**: Git definition files MUST keep data text as received (Q5). The Git reconciliation content hash keeps its
  current input (`GitCanonicalJson.HashFile`, a `JsonNode` re-emit that does not depend on the file's escaping or
  whitespace, research R-7), frozen and registered, so a Git round trip keeps every hash. Any change to that input is
  an explicit decision under FR-021, not part of adopting the baseline. The design-store fingerprint is already
  canonical, is registered and MUST stay so. No stored hash is recomputed.

**Runtime durable state (decision 7, question 6)**
- **FR-023**: The runtime EF writers MUST adopt FR-007 and FR-018 (Q7, card 10). Compatibility and meaning, not a
  textual writer change, are the threshold:
  - the change needs no format-version bump and no migration only when S-9 shows it compatible and
    meaning-preserving: fixed serialized examples and compatibility checks show that old and new code read old and
    new rows with the same meaning;
  - if that proof fails, S-9 MUST make it a stamped format change through migrations and golden fixtures (before 4.0:
    regenerated migrations and updated golden fixtures, not an upcaster);
  - a change to what a row means, or to reader and writer compatibility, always takes the stamped path (a new family
    version under spec 180).

  A byte-level golden fixture MUST pin a stored data value's text. Hash-input contracts stay separately protected
  (FR-021).

**Docs and housekeeping**
- **FR-024**: `docs/serialization.md` MUST state the baseline and the data rule, and its "HTTP boundary" exception
  MUST stop naming FastEndpoints.
- **FR-025**: Every warning and refusal in this spec MUST have a stable diagnostic code.

### Key Entities

- **Baseline**: the one read-only options instance; every serializer is a copy of it.
- **Deviation**: a serializer whose protected settings differ from the baseline; listed in the register.
- **Baseline deviation register**: the section in `docs/serialization.md` the architecture test reads.
- **Conversion contract**: the naming, matching, member-mapping, unmatched-member and date/time-interpretation
  settings for one user type or conversion boundary; a default one and scoped ones, recorded with a version.
- **Semantic value comparison**: the defined comparison that decides whether two values are equal, independent of
  their stored text. Never applied to an existing hash input.
- **Legacy list**: the ratchet file of option-less calls on Elsa's own contracts still to migrate; it may only shrink.
- **Durable format stamp**: the marker that identifies the runtime store's serialization format version.

## Open questions for the approvers

ADR 0091 leaves Q1 to the spec, Q8 sits between ADR 0091 and ADR 0046, and the code survey added Q2 to Q7 (research
R-0). Q3 to Q7 are also raised as review comments on #3408 (findings F-1 to F-5), because the code shows that ADR 0091
is incomplete or wrong there. The requirements below follow the answers. If #3408 decides otherwise, this spec
follows #3408. All three approvers answer them on the PR, and the PR's reconciliation table records the answers.
@sfmskywalker's answers were recorded on his behalf on 4–5 Nov (cards 8–14 and 24 on #3408, #3422 and this PR, and
the delegated Q1 selection); @j03y-nxxbz accepts them, and the text applies them as of 2026-11-11. @FransVanEk's
conditions on #3408 (his 2026-11-10 review) are applied through ADR 0091 at `46c3a40eb`.

| # | Question | Answer as applied | @j03y-nxxbz | @sfmskywalker |
|---|---|---|---|---|
| Q1 | ADR 0091 decision 2 leaves to S-9 how an unmatched key is detected. How? | A contract-aware pre-scan before every data-to-.NET conversion: judged against the effective conversion contract (naming, case sensitivity, mappings, dictionaries, extension data); custom converters supply their matching information or the type is reported as not covered; warn and continue by default, stricter handling through a scoped contract, a missing required member fails (FR-005, research R-13). Rejected: a synthetic extension-data member (changes user types), and deserializing twice with `Disallow` (two passes, first key only). | Accepts | Warn and continue, judged against the effective contract ([card 13](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6644301401)); contract-aware pre-scan selected under delegation ([comment](https://github.com/elsa-workflows/elsa-foundation/pull/3805#issuecomment-6646544429)) |
| Q2 | Which source roots does "every serializer" (decision 1) and the architecture test cover? | Every source root: Essentials, Extensions and Studio's C#, with staged enforcement (FR-002a, FR-020). Extension-internal options on their own contracts are register entries (R-10). | Agrees | Agrees ([card 11](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6644185619)) |
| Q3 | About 267 serializer calls pass neither options nor type info (R-10). ADR 0091 counts 32 constructions and does not count these. List each one in the register, or migrate them all? | Staged: every workflow-data path is fixed in S-9; option-less calls on Elsa's own contracts go on a legacy list that may only shrink and never holds a data path; a new unlisted call fails; justified deviations keep reasoned register entries (FR-002a). Finding F-5 on #3408. | Agrees | Agrees ([card 11](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6644185619)) |
| Q4 | Hash preimages: decision 1 makes every serializer derive from the baseline, but several hash preimages use other options, and the activity contract fingerprint feeds every ADR 0038 artifact hash (R-1). | Every existing hash input keeps its frozen options and is registered with its input contract; changing one is a separate decision with its identity, versioning and migration consequences assessed. Equality checks use the semantic comparison, never text (FR-021). Finding F-2 on #3408. | Accepts (replaces "direct text hashes move to the canonical form") | Agrees with that qualification ([card 9](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6644084116)) |
| Q5 | Git files: ADR 0091 expects the Git hash to break, but it does not (R-7). Instead, Git files never hold data text as received, because the exporter re-emits them through `JsonNode`. | Git files keep carried data text; the Git hash keeps its current, registered input; any change to it is an explicit FR-021 decision (FR-022). Rejected: registering Git files as a normalized rendering (users diff them). Finding F-1 on #3408. | Agrees | Exact carried text until edited, exports included ([card 7](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6643745916)); Git/hash preimage changes need the explicit assessment |
| Q6 | Decision 4 relies on "every Elsa response is `application/json`", but workflow HTTP responses use the content type the workflow chose, and `nosniff` is set nowhere (R-12). | `application/json` plus `nosniff` on Elsa's own JSON APIs; a workflow-authored response uses its author's content type; with none, text is `text/plain` and anything else `application/json` (FR-010). Finding F-3 on #3408. | Agrees | Agrees ([card 8](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6644048963)) |
| Q7 | The runtime durable state change (question 6): ADR 0091 says regenerated migrations and golden fixtures, but no model change and no runtime-row golden exist (R-9). | No format bump or migration if S-9 shows the writer change compatible and meaning-preserving with fixed serialized examples and compatibility checks; otherwise a stamped format change. A change to row meaning or compatibility always takes the stamped path (FR-023). Finding F-4 on #3408. | Accepts | Agrees ([card 10](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6644124489)); @FransVanEk's condition on #3408 is the same |
| Q8 | Who implements date/time conversion: S-9, at the JSON-to-.NET boundary, or S-8 with ADR 0046? | S-9 implements the agreed rule: an offset, or an explicit interpretation (source time zone or assume-UTC policy) in the published conversion contract, else refused; never implicit UTC (FR-016). S-8 uses the same contract. #3422 *Dates* is a dependency. | Accepts (replaces "a `DateTime` is read as UTC") | S-9 ([card 24](https://github.com/elsa-workflows/elsa-foundation/pull/3805#issuecomment-6645506583)) under the [card 14](https://github.com/elsa-workflows/elsa-foundation/pull/3422#issuecomment-6644338442) rule |

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

- Elsa 4 has no released runtime store, so before 4.0 a stamped format change, if FR-023's compatibility proof fails,
  costs regenerated migrations and fixtures, not an upcaster (ADR 0091 decision 7).
- Studio parses JSON and copies `errors` keys verbatim without mapping them to field paths, so no Studio change is
  needed (research R-3).
- No client inside the repository depends on camelCased output names (research R-4). Clients outside it cannot be
  checked from here, and the FastEndpoints-era wire shape is not kept.
- The host HTTP options already use the relaxed encoder (research R-8). For Elsa's own APIs, decision 4 changes the
  Runtime and Design owner contexts and `IPayloadSerializer`, which use the default encoder today.
- Rows stored today were normalized on first write and stay as they are.
