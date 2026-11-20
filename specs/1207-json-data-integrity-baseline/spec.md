# Feature Specification: JSON data integrity baseline

**Feature Branch**: `agent/claude/3052-s9-json-integrity`

**Created**: 2026-11-03

**Status**: Approved — agreed 2026-11-20 by @j03y-nxxbz as coordinator (the approval plan), with Q9 to Q11 decided as recommended and Q10 as answered; applies ADR 0091 and the ADR 0046 amendment as merged on the initiative branch. Initiative #3052 spec **S-9**.

**Initiative**: part of [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052), spec row **S-9**.

**Issues**: none yet. The work comes from analysis
[A-5](https://github.com/elsa-workflows/elsa-foundation/issues/3052#issuecomment-6451104603) (decision-matrix row 21,
scenario S-e) and the [value-path search](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6491919492).

**Input**: Elsa writes workflow data exactly as it received it. Every JSON serializer derives from one read-only
baseline that transforms no data, as ADR 0091 records it.

## Approval

**Which ADR text this spec applies**, all merged into `initiative/3052-elsa3-to-elsa4-conversion`:

| ADR | What this spec takes from it | Merged |
|---|---|---|
| ADR 0091 (A-5, [#3408](https://github.com/elsa-workflows/elsa-foundation/pull/3408)) | Decisions 1–7: the baseline, no data transformation, scoped conversion contracts for user types, raw passthrough and the canonical form, one encoder, JSON at every boundary, no host-option changes with staged enforcement, the deviation register, and the explicit hash-input decision decision 1 allows (FR-021); decision 8 only as far as the serializer keeps nulls | `771ba6679f` (20 Nov) |
| ADR 0046 amendment (A-1, [#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422)), *Adapter rules* and *Dates* | Member matching at activity inputs and outputs, no text-to-number parsing, no narrowing, and the date rule: an offset, or an explicit interpretation, never implicit UTC or server-local time; the Elsa 3 converter's literal rule (FR-016) | 16 Nov |

**Specs this one meets**, merged or in implementation on the initiative branch:
- S-1 (spec 1223) FR-034 defines the Elsa 3 converter's one *date interpretation* setting (`Refuse`, `AssumeUtc`,
  `SourceTimeZone`) and leaves its use at the JSON-to-.NET boundary to this spec. FR-016 uses that setting; it adds no
  second one.
- S-8 (spec 1228): one dynamic kind, `Any`, held as `JsonElement`; `Object` reads as `Any`. S-8 leaves date
  interpretation, member matching and the baseline to this spec and adds no rules of its own. FR-028's amendment
  (19 Nov: one adapter version, nothing stamped or checked at run time) is the model for FR-004a.
- S-10 (spec 1221) FR-001 and S-7 (spec 1231) FR-012 use the canonical form of FR-021a; `VariableValueDigest` already
  meets it.
- #4832 (#3167): `JsonElement`-backed aliases are `StructuredValue`. Consistent with FR-011; nothing to change.
- S-2 (spec 1216) owns the null representation (ADR 0091 decision 8). This spec only requires that the serializer
  keeps it.

**Compatibility** is owed only from Elsa 4's first stable release. Before it, a changed stored format means stored
fixtures and migrations regenerated in place, with no run-time shim, upcaster or format-version check (FR-023).

**Approval plan**: the coordinator, @j03y-nxxbz, approves this spec alone (the board's *Spec approval once the ADRs
are agreed* rule); no approver review is requested. **Implementation** starts on a new branch from the initiative
branch once this spec is merged (D-18, per spec).

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
- The null representation for declared members (S-2, spec 1216). FR-018 only requires that it survives.
- How a script sees values and which paths it writes back (S-8, ADR 0046 V9 and V12). A value a script changed is a
  new value; this spec only fixes the format it is written in.
- Number handling inside the script engine (ADR 0046 V6, V9).
- Precision-loss acceptance (ADR 0046 V9, D-17): a host-wide policy at the .NET-to-Jint boundary, owned by S-8. It is
  not a conversion contract setting here.
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
(`Ren\u00e9` → `Ren\u00E9`), and modules disagree on the escaping.

**Independent Test**: the two round trips of ADR 0091's *Required evidence*: a Runtime output and a Design variable
default come back byte-identical.

**Acceptance Scenarios**:

1. **Given** an output `OrderTotal` holding `{"CustomerId":1,"Name":"Ren\u00e9","N":1.0}`, **When** the instance is
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
   non-UTC server, at run time and in a publish-time literal, **Then** both give the same instant: a `DateTime` holds it in UTC and is written with `Z`, a `DateTimeOffset`
   keeps `+05:00`. `"2026-10-28T23:30:00-02:00"` into a `DateOnly` gives 29 October; `"2026-10-28"` gives 28 October.
   **Given** `"2026-10-28T09:15:00"` (no offset), **When** it is converted to an instant, **Then** the conversion is
   refused with `json-datetime-offset-missing` and guidance to send `Z` or an offset, on any server.
6. **Given** `"5"` read into an `Int32`, **When** it is read on any path, **Then** every path gives the same result
   (numbers are never read from strings). `"true"` into `Boolean` and `5.0` into `Int32` are refused; `PT1H30M` and
   `01:30:00` into `TimeSpan` both give 90 minutes, written `PT1H30M`, and `P1M` is refused (FR-014a).
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
11. **Given** a workflow with a `DateTime` input and Studio's time zone left at its default in a browser on Amsterdam
    time, **When** a user picks 28 October 2026, 09:15 in the Run dialog, **Then** the start request carries
    `2026-10-28T09:15:00+01:00`, the engine holds `2026-10-28T08:15:00Z`, and the instance view shows
    `28 Oct 2026, 09:15 +01:00`; with the setting on UTC it shows `08:15 +00:00` (FR-016b).
12. **Given** a start request whose `DateTime` input is `"2026-10-28T09:15:00"`, **When** it reaches the API, **Then**
    it is refused with 400 and `json-datetime-offset-missing` naming the input (FR-016c).

---

### User Story 4 - Content hashes stay stable (Priority: P2)

Every hash over JSON is taken one way, over the canonical form, so a definition reconciled from Git or stored in
the design store keeps its hash whatever the serializer's escaping, whitespace or member order. Equal values compare
equal. The hashes change once, in this spec, before the first stable release.

**Why this priority**: without it, every definition read back from Git hashes as changed the moment US2 lands
(intake check 7), and about twenty hashes each depend on their own, partly implicit, serializer settings.

**Independent Test**: write definitions to files, read them back, and compare every content hash; run the golden
test of every hash.

**Acceptance Scenarios**:

1. **Given** a definition with escaped characters in a variable default, **When** it is written to Git and read
   back, **Then** its content hash is the same and the file holds the default's text as received.
2. **Given** a new hash call over serialized JSON that does not use `JsonHashInput`, **When** the architecture test
   runs, **Then** it fails and names the file (FR-021).
3. **Given** any hash over JSON, **When** its input is written with other escaping, whitespace, member order or
   number notation, **Then** the hash is the same, and its golden test pins its value (FR-021).
4. **Given** `{"total":1.0,"name":"Ren\u00e9"}` and `{"name":"René","total":1}`, **When** a check compares them,
   **Then** they are equal and assigning one over the other writes nothing; `9007199254740993` and
   `9007199254740992` are not equal; `{}` and `{"x":null}` are not equal; and the value digest of ADR 0092 agrees on
   every pair (FR-021a).
5. **Given** an unchanged definition exported to Git before and after this change, **When** both files are compared,
   **Then** they differ only by the null members `IPayloadSerializer` now keeps (FR-022a).

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
3. **Given** a variable default `"2026-10-28T09:15:00"` of type `DateTime`, **When** it is converted under `Refuse`,
   **Then** conversion refuses with guidance to set the source time zone or add an offset; under `SourceTimeZone`
   `Europe/Amsterdam` the default becomes `2026-10-28T09:15:00+01:00` (and `+02:00` for a July date); under
   `AssumeUtc` it becomes `2026-10-28T09:15:00Z` with a behaviour warning (FR-016a).

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
  MUST be constructed with such a copy, and owner code MUST use that instance, not the context's `Default`. Hashes are
  the exception: they use `JsonHashInput`'s own option set, the one register entry for hashes (FR-021, Q11). A user's selection of a supported scoped
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
  handling. The selected behaviour MUST be explicit and stable (FR-004a). Matching never rewrites the stored JSON keys.

  The default naming is camelCase on purpose. Since #3801 an activity input reads `{"customerName":…}` into
  `CustomerName`, case-insensitively. Under ADR 0091 decision 2 the default matching becomes exact, so that JSON keeps
  filling `CustomerName` only if the default naming policy for user types is camelCase. With CLR names as the
  default, every camelCase payload, and every activity result Elsa writes, would stop filling PascalCase members. What
  changes is the other spelling: `{"CustomerName":…}` no longer fills `CustomerName` under the default contract. It
  gives the `json-unmatched-member` warning, and a type that expects PascalCase input selects CLR names or
  case-insensitive matching.
- **FR-004a**: A scoped conversion contract MUST be declared in code that belongs to the user type or the activity:
  annotations on the type or the input member (`[JsonPropertyName]`, and one Elsa attribute, `[JsonConversion]`, for
  naming, matching and unmatched-member handling). It is part of that code, so it changes only when that package
  changes, and the activity version follows the assembly. Nothing extra is stamped into a published executable and
  nothing is checked at run time, as with S-8 FR-028's amendment (Q9). Elsa documents the default contract of FR-004
  and the attribute's options.
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
  every API and store: the design store, the runtime store (durable state and bookmarks, FR-023), the activities
  design store, Git definition files and exports.

**One output format (decision 4)**
- **FR-009**: Everything FR-007 does not cover (Elsa's own strings and member names, created or changed values) MUST
  be written with one encoder in every API and store: `UnsafeRelaxedJsonEscaping` (ADR 0091 decision 4). Its safety
  argument rests on Elsa's own API responses only (FR-010).
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
  defaults, workflow-input validation, activity results, stateful activity state, background completions
  (`BackgroundActivityCompletionCodec`), parent resumes (`ParentResumeExecutor`), seeded state
  (`RuntimeWorkflowStateSeed`) and code-first literals (`WorkflowBuilder`); research R-10 lists every site.
- **FR-013**: No conversion MUST narrow a number to `long` or `double` unless the target type asks for it.
- **FR-014**: No path MUST read a number from a string.
- **FR-014a**: The remaining activity-input rules of ADR 0046 *Adapter rules* MUST hold on every path: `"true"` into
  `Boolean` and `5.0` into `Int32` are refused (no text parsing or truthiness); an upper-case GUID is accepted and
  written lower case; a `TimeSpan` reads an ISO 8601 duration (`PT1H30M`) and `hh:mm:ss` (for Elsa 3), is written as
  ISO 8601, and `P1M` and `P1Y` are refused (no fixed length).
- **FR-015**: The Elsa 3 converter MUST rewrite a literal key that differs from the exact JSON name only in case to
  that name, and MUST refuse with guidance when the rewrite is ambiguous.
- **FR-016**: Converting a date/time value into an instant at a conversion of FR-012 (supported literals, inputs and
  defaults, at run time and at publication) MUST NOT use an implicit assume-UTC or the server's local time zone (Q8,
  ADR 0046 *Dates*):
  - a value that carries `Z` or an offset establishes its instant directly;
  - a `DateTime` or `DateTimeOffset` value without an offset MUST be refused with a diagnostic
    (`json-datetime-offset-missing`) that names the path and target type and says to send `Z` or an offset. S-9 builds
    no source-time-zone or assume-UTC setting for run-time values: ADR 0046 *Dates* allows one, and it is added when a
    user asks for it. There is no host-wide run-time date setting (Q10);
  - once the instant is established, a `DateTime` is normalized to UTC and written with `Z`; a `DateTimeOffset` keeps
    its offset;
  - a `DateOnly` takes the date as written; a value with a time and an offset takes its date in UTC. A `TimeOnly`
    takes the time as written. Neither is made an instant;
  - a raw JSON date string is carried unchanged until a conversion is requested.

  S-9 implements this at the JSON-to-.NET boundary; S-8 uses it and adds no second rule.
- **FR-016a**: The Elsa 3 converter MUST apply ADR 0046 *Dates* (*Converted Elsa 3 workflows*) to every literal and
  variable default bound to a `DateTime`, `DateTimeOffset` or `DateOnly` member, using S-1's one date interpretation
  setting (spec 1223 FR-034), so a host sets it once:
  - `Refuse` (the default): a value without an offset is refused with a stable diagnostic whose guidance says to set
    the source time zone or add an offset;
  - `SourceTimeZone`: the converter writes the value with that zone's offset on that date (daylight saving included),
    for example `2026-10-28T09:15:00` in `Europe/Amsterdam` becomes `2026-10-28T09:15:00+01:00`;
  - `AssumeUtc`: the converter writes the value with `Z` and a behaviour warning that the instant differs from Elsa 3
    if its server was not on UTC;
  - a value with `Z` or an offset converts as written, with a behaviour warning on a `DateTime` or `DateOnly` member;
    a non-literal expression bound to a date member gets a behaviour warning.

  The converter records the interpretation on the converted workflow, as S-1 does for scripts (the
  `DateInterpretationApplied` import diagnostic). The converted value carries its offset, so run time needs nothing
  more.
- **FR-016b**: Studio MUST have one **time zone** setting, under Settings, that every date Studio shows or sends uses.
  Elsa 4 itself always works in UTC; the setting only decides how Studio presents and enters dates.
  - Default: the time zone of the browser running Studio. The user can choose UTC or any IANA time zone instead
    (`Europe/Amsterdam`). The choice is kept with Studio's other appearance preferences.
  - Every date and time Studio shows (instance lists, timelines, incidents, definitions, logs) is converted to that
    zone and shows its offset, for example `28 Oct 2026, 09:15 +01:00`. One shared formatter replaces the
    `toLocaleString()` calls that show a local time with no offset today.
  - Every date Studio sends carries the offset of that zone on that date: the Run dialog's `datetime-local` control,
    whose text has none (`2026-10-28T09:15`), sends `2026-10-28T09:15:00+01:00`, and `+02:00` for a July date. The
    control shows the zone it uses.
- **FR-016c**: Elsa's own APIs MUST accept a date-time only with `Z` or an offset, and MUST return the instants Elsa
  produces in UTC. That covers their own contract fields and the values of date-typed workflow inputs at a start.
  An offset-less value (`"2026-10-28T09:15:00"`) is refused with a 400 problem response carrying
  `json-datetime-offset-missing` and the field path. Today System.Text.Json reads such text into a `DateTimeOffset`
  with the server's offset, so the baseline (FR-001) carries strict `DateTime` and `DateTimeOffset` converters for
  Elsa's own contracts too. Studio (FR-016b) and every other client send offsets.

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
- **FR-021**: Every hash or fingerprint whose input is JSON MUST go through **one hash input**, `JsonHashInput` in
  `Elsa.Serialization.Core` (Q11). This is the explicit decision ADR 0091 decision 1 requires for changing hash
  inputs, taken once, before the first stable release:
  - `JsonHashInput` serializes a .NET value with its own option set, every setting written out (camelCase members,
    nulls kept, string enums, maximum depth 1000). It never refers to `JsonSerializerOptions.Web`, `.Default` or the
    baseline, whose defaults can change with a .NET upgrade or a baseline change.
  - It hashes (SHA-256) the canonical form (FR-021a) of that JSON, so escaping, whitespace, member order and number
    notation never move a hash. A `JsonElement` is hashed through its canonical form directly.
  - Every existing hash over JSON moves to it: the ADR 0038 artifact hash and the activity contract fingerprint, the
    activity definition hash, the template behaviour hash, the design-store and library operation fingerprints, the
    Git content hash, the ETL artifact hash, the fork-material and authoring-schema fingerprints, and every other site
    research R-1 and R-14 list. Hand-written writers keep their writer and hash its output through it.
  - The values of those hashes change once. Before the first stable release that costs regenerated fixtures and
    goldens in place, and no run-time shim; `EfDesignSupport.LegacyFingerprint`, kept only to replay older markers,
    is deleted.
  - A golden test pins each hash for a fixed input, so any later change to `JsonHashInput` or to a hashed type shows
    up as a failing test, not as a silent mismatch. The goldens cover every variant the canonical form absorbs
    (escaping, whitespace, member order, number notation) and every one it keeps apart (missing versus `null`, array
    order, a different number).
  - **From Elsa 4's first stable release, hashes are frozen again.** Until then a hash may change when its golden and
    fixtures are regenerated in the same change. From that release on, changing `JsonHashInput`, a hashed type or a
    pinned golden is an explicit decision that assesses identity, versioning and migration (ADR 0091 decision 1); it
    is never done by regenerating the golden. Every golden's failure message states this rule, and
    `docs/serialization.md` and `AGENTS.md` state it, so any session that meets a failing hash golden reads it there.
  - The architecture test (FR-020) finds every SHA-256 or other hash call whose input is serialized JSON and fails
    unless it goes through `JsonHashInput`, so a new hash cannot bypass it.

  A check that decides whether two values are equal (including every `Ordinal` comparison of a stored value with a
  fresh serialization) MUST use the canonical form, never stored or freshly serialized text.
- **FR-021a**: That comparison MUST be ADR 0091's **canonical form** (decision 3): two values are equal when they
  mean the same JSON:
  - objects have the same members, in any order, compared by their unescaped names;
  - strings are compared after unescaping (`"Ren\u00e9"` equals `"René"`);
  - numbers are compared by exact decimal value, without rounding (`1.0`, `1` and `1e0` are equal;
    `9007199254740993` and `9007199254740992` are not);
  - arrays have the same elements in the same order;
  - a missing member and a `null` member are different (`{}` is not `{"x":null}`).

  The canonical form defines equality, not an algorithm, and is never stored or sent: stored and returned data keeps
  its received text. One implementation in `Elsa.Serialization.Core` serves every equality check of FR-021 and the
  value digest of ADR 0092 (`VariableValueDigest`, spec 1221 FR-001), which already normalizes this way and moves
  onto it with every digest unchanged. ADR 0090 C4's no-op test (spec 1231 FR-012) uses the same equality, and
  `JsonHashInput` (FR-021) hashes it.
- **FR-022**: Git definition files MUST keep data text as received (Q5). The Git content hash is `JsonHashInput` over
  the file (FR-021), so a Git round trip keeps every hash whatever the file's indentation or escaping.
- **FR-022a**: The Git file is written from `IPayloadSerializer` output. Once that keeps nulls (FR-018), an unchanged
  definition re-exported after S-9 writes its null members, for example `"description": null`. That is a real change
  of content (missing and `null` differ), made in the same one-time move as FR-021; the repository's Git fixtures are
  regenerated with it.

**Runtime durable state (decision 7, question 6)**
- **FR-023**: The runtime EF writers MUST adopt FR-007 and FR-018 (Q7, card 10). Compatibility and meaning, not a
  textual writer change, are the threshold:
  - fixed serialized examples MUST show that new code reads a row in today's format with the same meaning, and that
    today's reader options read a new row with the same meaning; then no migration is needed;
  - if they do not, the change is a stamped format change. Before Elsa 4's first stable release that means
    regenerated migrations and golden fixtures, updated in place, with no upcaster, family-version bump or run-time
    check.

  A byte-level golden fixture MUST pin a stored data value's text. Hashes are pinned separately (FR-021).

**Docs and housekeeping**
- **FR-024**: `docs/serialization.md` MUST state the baseline and the data rule; its "camelCase, case-insensitive on
  read" note MUST say exact reads; its "HTTP boundary" exception MUST stop naming FastEndpoints.
- **FR-024a**: `IPayloadSerializer` reads Elsa's own contracts case-insensitively today and exactly once it derives
  from the baseline (ADR 0091 *Consequences*). S-9 MUST confirm with a test that every stored-format fixture and seed
  file in the repository uses the camelCase names it writes. A hand-written file with `"Name"` instead of `"name"`
  then loses that member; that is accepted, because an own-contract document is Elsa's format.
- **FR-025**: Every warning and refusal in this spec MUST have a stable diagnostic code.

### Key Entities

- **Baseline**: the one read-only options instance; every serializer is a copy of it.
- **Deviation**: a serializer whose protected settings differ from the baseline; listed in the register.
- **Baseline deviation register**: the section in `docs/serialization.md` the architecture test reads.
- **Conversion contract**: the naming, matching, member-mapping, unmatched-member and date/time-interpretation
  settings for one user type or conversion boundary; a default one and scoped ones declared in code on the type or
  input member (FR-004a).
- **Canonical form**: the equality of FR-021a that decides whether two values are equal, independent of their stored
  text. Never stored or sent, and never applied to an existing hash input.
- **Legacy list**: the ratchet file of option-less calls on Elsa's own contracts still to migrate; it may only shrink.

## Questions

ADR 0091 left Q1 to this spec, Q8 sat between ADR 0091 and ADR 0046, and the code survey added Q2 to Q7 (research
R-0). Q1 to Q8 were answered by the approvers before ADR 0091 and ADR 0046 were accepted, and both ADRs now carry
those answers. Q9 to Q11 come from the recheck against the merged ADRs and the specs merged since. The coordinator
answered Q9, Q10 and Q11 on 2026-11-20, as recommended.

**Answered by the coordinator on 2026-11-20**

| # | Question | Concrete problem and example | Answer, as applied (the recommendation, accepted) |
|---|---|---|---|
| Q9 | Where does a scoped conversion contract live, and is it recorded with a version? | ADR 0091 decision 2 says the selected behaviour is explicit and stable "in the published conversion contract and its version", and ADR 0046 *Dates* records a date interpretation there. No such artifact exists in code, and S-8 FR-028 was amended on 19 Nov to stamp and check nothing. Example: a connector's `Invoice` input selects case-insensitive matching; a published workflow passes `{"InvoiceNo":…}`. If the connector later drops that option, the same workflow leaves `InvoiceNo` unset with a warning. | The contract is code on the type or input member (FR-004a), so it changes only with that package, like the type's members; nothing is stamped into the executable and nothing is checked at run time. Rejected: a contract hash in each executable's activity contract, which would move the activity contract fingerprint and every ADR 0038 artifact hash (FR-021). This reads ADR 0091 decision 2's "published conversion contract and its version" as the type's published package; if the coordinator reads it as an executable record, the ADR wording goes through the spec-to-ADR loop instead. |
| Q11 | Do the existing hashes stay frozen with their own options, or move once to one hash input? | About twenty hashes over JSON each use their own settings: no options, `JsonSerializerOptions.Web`, or an own context. The list is kept by hand, and the review found one it missed (`EfWorkflowLibraryStore`). Example: the design-store operation fingerprint hashes `JsonSerializerOptions.Web` text; if a .NET upgrade changes how `Web` escapes, or someone moves it to the baseline, a retried save no longer matches its stored marker, and nothing tells you why. | Move every hash over JSON once, now, to `JsonHashInput`: explicit settings, hashed over the canonical form, a golden test per hash, and an architecture test that catches any hash that bypasses it (FR-021). The hash values change once, which before the first stable release costs regenerated fixtures only. Rejected: keeping each frozen, which leaves twenty option sets to guard forever. This replaces the "frozen" answer of Q4, given before the first-stable compatibility rule. |

**Answered**

| # | Question | Answer as applied | @j03y-nxxbz | @sfmskywalker |
|---|---|---|---|---|
| Q1 | ADR 0091 decision 2 leaves to S-9 how an unmatched key is detected. How? | A contract-aware pre-scan before every data-to-.NET conversion: judged against the effective conversion contract (naming, case sensitivity, mappings, dictionaries, extension data); custom converters supply their matching information or the type is reported as not covered; warn and continue by default, stricter handling through a scoped contract, a missing required member fails (FR-005, research R-13). Rejected: a synthetic extension-data member (changes user types), and deserializing twice with `Disallow` (two passes, first key only). | Accepts | Warn and continue, judged against the effective contract ([card 13](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6644301401)); contract-aware pre-scan selected under delegation ([comment](https://github.com/elsa-workflows/elsa-foundation/pull/3805#issuecomment-6646544429)) |
| Q2 | Which source roots does "every serializer" (decision 1) and the architecture test cover? | Every source root: Essentials, Extensions and Studio's C#, with staged enforcement (FR-002a, FR-020). Extension-internal options on their own contracts are register entries (R-10). | Agrees | Agrees ([card 11](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6644185619)) |
| Q3 | About 267 serializer calls pass neither options nor type info (R-10). ADR 0091 counts 32 constructions and does not count these. List each one in the register, or migrate them all? | Staged: every workflow-data path is fixed in S-9; option-less calls on Elsa's own contracts go on a legacy list that may only shrink and never holds a data path; a new unlisted call fails; justified deviations keep reasoned register entries (FR-002a). Finding F-5 on #3408. | Agrees | Agrees ([card 11](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6644185619)) |
| Q4 | Hash preimages: decision 1 makes every serializer derive from the baseline, but several hash preimages use other options, and the activity contract fingerprint feeds every ADR 0038 artifact hash (R-1). | Every existing hash input keeps its frozen options and is registered with its input contract; changing one is a separate decision with its identity, versioning and migration consequences assessed. Equality checks use the semantic comparison, never text (FR-021). Finding F-2 on #3408. Superseded by Q11. | Accepts (replaces "direct text hashes move to the canonical form") | Agrees with that qualification ([card 9](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6644084116)) |
| Q5 | Git files: ADR 0091 expects the Git hash to break, but it does not (R-7). Instead, Git files never hold data text as received, because the exporter re-emits them through `JsonNode`. | Git files keep carried data text; the Git hash keeps its current, registered input; any change to it is an explicit FR-021 decision (FR-022). Rejected: registering Git files as a normalized rendering (users diff them). Finding F-1 on #3408. | Agrees | Exact carried text until edited, exports included ([card 7](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6643745916)); Git/hash preimage changes need the explicit assessment |
| Q6 | Decision 4 relies on "every Elsa response is `application/json`", but workflow HTTP responses use the content type the workflow chose, and `nosniff` is set nowhere (R-12). | `application/json` plus `nosniff` on Elsa's own JSON APIs; a workflow-authored response uses its author's content type; with none, text is `text/plain` and anything else `application/json` (FR-010). Finding F-3 on #3408. | Agrees | Agrees ([card 8](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6644048963)) |
| Q7 | The runtime durable state change (question 6): ADR 0091 says regenerated migrations and golden fixtures, but no model change and no runtime-row golden exist (R-9). | No format bump or migration if S-9 shows the writer change compatible and meaning-preserving with fixed serialized examples and compatibility checks; otherwise a stamped format change. A change to row meaning or compatibility always takes the stamped path (FR-023). Finding F-4 on #3408. | Accepts | Agrees ([card 10](https://github.com/elsa-workflows/elsa-foundation/pull/3408#issuecomment-6644124489)); @FransVanEk's condition on #3408 is the same |
| Q8 | Who implements date/time conversion: S-9, at the JSON-to-.NET boundary, or S-8 with ADR 0046? | S-9 implements the agreed rule: an offset, or an explicit interpretation (source time zone or assume-UTC policy) in the conversion contract, else refused; never implicit UTC (FR-016). The Elsa 3 converter uses S-1's one date setting for literals (FR-016a). S-8 uses the same contract. | Accepts (replaces "a `DateTime` is read as UTC") | S-9 ([card 24](https://github.com/elsa-workflows/elsa-foundation/pull/3805#issuecomment-6645506583)) under the [card 14](https://github.com/elsa-workflows/elsa-foundation/pull/3422#issuecomment-6644338442) rule |
| Q10 | Is there a host-wide date interpretation for run-time values? | No. Elsa works in UTC. Its APIs accept a date-time only with `Z` or an offset and refuse an offset-less one (FR-016c); clients such as Studio must send offsets, and Studio presents dates in a configurable time zone (FR-016b). Dates inside workflow data (scripts, HTTP bodies) follow ADR 0046 *Dates* and S-8. No source-time-zone or assume-UTC setting is built for run-time values (FR-016). | Answered 2026-11-20 | — |

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: No authored name key (output, input, variable, parameter, node id) changes casing between request and
  response on any API.
- **SC-002**: Both round trips of US2 return byte-identical text, through every store FR-008 lists.
- **SC-003**: Zero properties are lost when a .NET-typed result is bound to an input of the same type.
- **SC-004**: Zero content hashes change in a Git round trip of every definition in the repository's fixtures, and
  every hash over JSON goes through `JsonHashInput` with a golden test.
- **SC-005**: The architecture test reports zero unregistered deviations, and fails on each scanned kind when one is
  added.
- **SC-006**: The same changed value has identical text from every API.

## Assumptions

- Compatibility is owed from Elsa 4's first stable release. Before it, a stamped format change, if FR-023's examples
  fail, costs regenerated migrations and fixtures, not an upcaster (ADR 0091 decision 7).
- Studio parses JSON and copies `errors` keys verbatim without mapping them to field paths, so the wire changes need
  no Studio change (research R-3). The one Studio change is FR-016b.
- No client inside the repository depends on camelCased output names (research R-4). Clients outside it cannot be
  checked from here, and the FastEndpoints-era wire shape is not kept.
- The host HTTP options already use the relaxed encoder (research R-8). For Elsa's own APIs, decision 4 changes the
  Runtime and Design owner contexts and `IPayloadSerializer`, which use the default encoder today.
- Rows stored today were normalized on first write and stay as they are.
