# Output Binding Coercion Uses Pinned Value Representations

Status: proposed (output-capture slice accepted — see Decision status)

Amendment proposed (2026-10-29, [epic #3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052) analysis
A-1): one core value model, an adapter with a written contract at every boundary, and the agreed adapter rules. If
accepted, it settles the representation taxonomy that this ADR leaves proposed. See *Amendment* below.

Product requirements are captured in [Typed Output Binding Coercion PRD](../plans/output-binding-coercion-prd.md).

## Decision status

The broader coercion model remains **proposed**. The **output-capture slice** — that author-time activity
output → workflow-variable captures are compiled, engine-owned binding edges resolved through the shared
coercion seam (this ADR's paragraphs 3-4) rather than activity-code writes — is **accepted** for
implementation. This scoped acceptance unblocks extending the existing capture pipeline from reusable-activity
boundaries to ordinary activities; see [Author-time activity output → variable capture](../plans/activity-output-capture-authoring.md).
Acceptance of this slice does not ratify the wider `ValueRepresentation` taxonomy, `Auto` conversion policy,
or profile-registration decisions, which stay proposed pending the specs 060/061 reconciliation called out in
[ADR 0045](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md).
[ADR 0048](0048-container-output-captures-resolve-through-producer-visible-variable-frames.md)
extends the accepted slice with declaration-relative, producer-visible container-frame targets; it
does not broaden the proposed coercion taxonomy.

Elsa output bindings will support general-purpose, target-aware coercion without treating every CLR object as workflow data. Activity result projections declare a `ValueRepresentation` such as `TypedValue`, `StructuredValue`, `TextValue`, `FormattedContent`, `BinaryContent`, `DurableReference`, or `TransientResource`; publication validates and pins the allowed conversion profile, while runtime applies that profile using runtime facts. `Any` remains a canonical durable JSON projection, not a general-purpose CLR object container.

The initial implementation scope is durable coercion with JSON and XML content decoding. `Auto` permits only deterministic, schema-guided conversions such as identity, safe numeric widening, recursive collection conversion, typed-value/JSON projection, and recognized formatted-content decoding. Ambiguous, lossy, ordinary-text, binary, or live-resource conversions require explicit configuration or are rejected. Transient activity-to-activity flow is a separate explicitly modeled path and is not captured into durable workflow variables.

Converters are Elsa-built-in or registered named profiles with stable identifiers and versions. Publication verifies their availability and records them in the executable; runtime does not discover arbitrary converters. The same coercion seam is used for output capture and direct activity-result input materialization.

`Auto` recognizes only producer-declared or runtime-supplied formats; it never sniffs arbitrary bytes or text. A recognized conversion failure is terminal, while unknown ordinary content targeting `Any` remains text. `Any` accepts any canonical JSON shape and `JsonObject` is a distinct object-shaped target. Typed, durable values may project into `Any` as canonical JSON, but live services, streams, connections, and other transient resources cannot enter durable workflow state. Conversion policy is owned by each binding edge and applies uniformly to activity results, workflow requests, variable reads, literals, expression parameters, and output captures. Existing published executables retain their pinned behavior when activity representation contracts evolve.

## Amendment (epic #3052, analysis A-1, 2026-10-29)

The analysis is @FransVanEk's: the
[A-1 outcome](https://github.com/elsa-workflows/elsa-foundation/issues/3052#issuecomment-6466068155) and its
[document](https://github.com/elsa-workflows/elsa-foundation/issues/3052#issuecomment-6466075108), with tests run on
2026-10-28. @j03y-nxxbz drafted this text from it and from his review. Rules V1 to V10 follow the analysis's
principles P1 to P10; V11 is @j03y-nxxbz's addition.

**Why.** Elsa 4 already runs on one value model in practice. Every value at rest is JSON with a type label
(`DurableValueState`), every value in flight travels in a `ValueEnvelope`, and scripts only ever see JSON. What is
missing is a written definition and a contract per boundary. The tests and a search of the value path found where
values change silently today:
- a camelCased JSON member does not bind to a PascalCase activity input;
- a `DateTime` with an offset becomes server-local time;
- integers beyond 2^53 and long decimals lose digits in scripts and in ForEach;
- activity results are written with different serializer options than the next activity reads them with
  ([ADR 0091](https://github.com/elsa-workflows/elsa-foundation/pull/3408), Context).

### The model

**V1. One core value model.** During a run, the engine knows one value model. A value is a value envelope:
- a **type**: an alias from the registry (ADR 0035), a collection kind and an optional JSON Schema;
- a **presence**: absent, explicit null, present, or withheld;
- its **content**: valid JSON, or a reference to content stored elsewhere;
- a **protection policy**: sensitive, requires encryption, redaction mode, retention.

Only core values are stored. This is the `ValueEnvelope` and `DurableValueState` that exist today, written down as the
definition.

**V2. Valid JSON is the definition.** The designer decides how data is represented in JSON. Elsa stores it as received
(ADR 0091) and invents no notation of its own for the core. Notation rules exist only where JSON enters something with
its own types, and they belong to that adapter.

**V3. An adapter at every boundary.** API, storage, JavaScript, Liquid, activity inputs and outputs, and formats such
as XML. Each adapter converts to and from the core model, and nothing else crosses the boundary. For each adapter it is
written down:
- what JSON it accepts for a typed target;
- what it produces;
- which conversions are lossless, which lose detail, and which are refused;
- the diagnostic the user sees when a conversion fails.

This ADR's representations (`TypedValue`, `StructuredValue`, `TextValue`, `FormattedContent`, `BinaryContent`,
`DurableReference`, `TransientResource`) become the vocabulary of those contracts. Conversion plans are how adapters are
implemented, and they stay pinned at publish. Every adapter serializes through ADR 0091's baseline.

**V4. Logic beyond the boundary is allowed.** A .NET type may run its own logic when it is created, set or read:
constructors, setters, functions implemented in .NET (D-5). The engine cannot prevent it and does not try. The designer
owns the consequences, as with any external effect (D-2).

**V5. Returning to the core is the contract.** Whatever crosses back must be a valid core value, or the conversion
fails with a clear diagnostic. Nothing that cannot be expressed in the core model enters workflow state.

**V6. Lossy conversion is declared, never silent.** Losing precision or detail is a documented property of an adapter.
The designer can see it and chooses it, and the result is still a valid core value (in line with Sipke's R5 on explicit
selection). A silent loss is a defect.

**V7. Live objects stay on their side.** A .NET instance lives only for the attempt that created it. After a pause,
restart or retry it is rebuilt from the core value, never restored. Recovery runs forward from the core model (D-2).

**V8. Null, missing and undefined.** Null and missing are two states of the core model (`ExplicitNull`, `Absent`).
`undefined` exists only in JavaScript, so it is a rule of the JavaScript adapter: the visible policy of D-11
([#3388](https://github.com/elsa-workflows/elsa-foundation/pull/3388),
[#3390](https://github.com/elsa-workflows/elsa-foundation/pull/3390)). NaN and Infinity become null with a diagnostic.

**V9. A script works on its own copy.** A script gets a changeable copy of what it reads (today the objects are frozen).
Changing the copy changes nothing outside the script. Whether and how a script may write a change back is A-3 and
[ADR 0090](0090-activities-change-variables-as-a-unit-of-work-committed-with-the-step.md); input-binding expressions
never write (D-3). Where a write is allowed, it commits with the step and a fault discards it. A write of a value the
script read is a read-modify-write under [ADR 0092](https://github.com/elsa-workflows/elsa-foundation/pull/3411).
The copy is an overlay on the JSON-backed view of *JavaScript*: the script's changes are kept beside the original JSON,
which stays untouched. This changes the frozen-value contract of spec 095
(`specs/095-value-flow-redesign/contracts/expression-and-import-contract.md`, lines 105-107) and the note in ADR 0090
that a loop item is frozen. Approving this amendment approves that change; `ScriptValueIsolationTests` changes with it.

**V10. Live .NET objects in scripts are opt-in.** By default a script sees JSON and registered .NET functions that
exchange JSON (D-5). A host may opt in to exposing objects it explicitly registers, each a transient resource for that
attempt (V7). General access to .NET types (Jint's `AllowClr`) is never offered.

**V11. A kind is a JSON shape, not a .NET type.** A variable's type is a kind from the alias registry (ADR 0035): a
JSON representation plus validation. `DateOnly` is an ISO 8601 string, not the .NET struct with its methods. Workflow
engineers see and use kinds, in Studio and in scripts (JavaScript, Liquid, and C# scripting once it is added). The
.NET type exists only inside the activity input and output adapter and inside registered functions (D-5).
- Working with a value as a .NET object, with its properties and methods, is the opt-in of V10 and never the default.
  A .NET engineer who opts in may also register their own aliases and expose their properties and methods to scripts.
- Code-first authors (`WorkflowBuilder.Variable<T>`) are .NET developers by definition. They may use .NET types in
  their own code; their values are still stored as core values (V7).

*Why.* Elsa has two audiences. Workflow engineers are its consumers: Studio and scripts are their front door, and they
should not need to know .NET. .NET engineers maintain an Elsa environment and customize it through extension points.
A company whose workflow engineers do know .NET can open those extension points, with clear documentation, but the
default stays plain. *Dates* shows what goes wrong otherwise: Elsa 3 exposed dates as .NET `DateTime` values, so the
same workflow behaved differently on each server.

**Defaults first, extensions defined.** The default implementations are kept simple and built now: JSON values, the
kinds, registered functions. The extension points above (the object opt-in of V10, registered aliases with properties
and methods) are defined in this ADR but not built until a real use case asks for them. S-8 builds the default, not
the opt-in.

### Adapter rules

**Activity inputs and outputs** (JSON to a declared .NET type and back):

| Value | Today (tested or read) | Rule |
|---|---|---|
| `{"customerName":…}` into a class with `CustomerName` | property stays empty, no error | Matched case-insensitively; two members that match one property fail with a diagnostic (ADR 0091 decision 2) |
| A .NET-typed result read into the next activity's input | written camelCased, read case-sensitively, so properties can be lost (inferred) | Both directions use the same baseline copy (ADR 0091 decision 5) |
| `"5"` into `Int32`, `"true"` into `Boolean`, `5.0` into `Int32` | refused with the defaults; `"5"` accepted where Web options are used | Refused everywhere: no text parsing or truthiness (R5) |
| A 20-digit integer into `Int64` or `Decimal`, a 29-digit decimal into `Decimal` | exact at the input; ForEach turns every item number into a `double` (#3421, a bug that can be fixed now) | Exact; no adapter narrows a number unless the target type asks for it |
| Upper-case GUID | accepted, written lower case | Keep, stated |
| Dates | see *Dates* | |
| `"01:30:00"` and `"PT1H30M"` into `TimeSpan` | accepted, refused | ISO 8601 durations are the standard and are written out; `hh:mm:ss` is still read for Elsa 3; `P1M` and `P1Y` are refused (no fixed length) |

**JavaScript** (and any later script adapter, such as C# scripting, which follows the same contract):

| Value | Today | Rule |
|---|---|---|
| Integer within ±2^53 | exact | Keep |
| Integer beyond ±2^53, or a `Decimal` with more digits than a double holds | digits lost on the way in, also for values the script never touches: the whole input is converted into JavaScript values up front, and Jint holds every number as a double | Values the script does not read pass through exactly. The adapter gives the script a JSON-backed view and writes untouched parts back as their original text. A number the script reads is a JavaScript number, a double (V6, stated). A designer who needs exact digits holds them as a string, and can write them out as a number with `JSON.rawJSON`; exact money arithmetic goes to a .NET function (D-5) or an activity (reviewer question 4) |
| `0.1 + 0.2` | `0.30000000000000004` | Stated: script arithmetic is double arithmetic |
| `2.0`, `1e21` | come back as `2`, `1e+21` | Stated: the notation may change, the number does not |
| Dates | ISO 8601 strings | Stay strings; a JavaScript `Date` (once D-1 enables it) leaves as UTC (`…Z`) |
| `undefined`, NaN, Infinity | key dropped, or null | V8 |
| Key order and casing | kept | Keep |

**API and storage:** data keeps its exact text, key casing and nulls; the camelCase naming policy applies only to
Elsa's own contract types. ADR 0091 is the rule; this ADR does not restate it.

**Liquid** reads JSON natively. Its number type is decimal (inferred), so it does not narrow. Its date filters stay
blocked until D-1 is implemented.

**Formats** (XML and others) are adapters at the edge that convert to `Any` and declare what they lose. XML to JSON
loses the difference between attributes and elements, and mixed content (V6).

### Dates

**The rule.** A date means the same instant on every server.
- **`DateTime`** is normalized to UTC at every adapter. A value without an offset (`"2026-10-28T09:15:00"`) is read as
  UTC; a value with `Z` or an offset is converted to UTC. It is written out with `Z`.
- **`DateTimeOffset`** keeps its offset. A value without an offset gets `+00:00`. An activity that needs the offset
  declares `DateTimeOffset`.
- **`DateOnly`** takes the date as written. A value with a time and an offset takes its date in UTC.
- **Publish-time literals and variable defaults follow the same rule.** Today they pin the publishing server's offset
  (ADR 0091, Context).
- **Scripts** keep dates as ISO 8601 strings (see *JavaScript*).

**Converted Elsa 3 workflows.** Elsa 3 (3.6.3) read dates in the server's time zone, so the same workflow behaved
differently per server. Every literal, input and variable default went through `ObjectConverter.ConvertTo`, which
parses with `DateTime.Parse` and no `DateTimeKind` handling. Tested on a UTC+1 server:

| Elsa 3 input | Into `DateTime` | Into `DateTimeOffset` | Under this rule |
|---|---|---|---|
| `"2026-10-28T09:15:00"` | 09:15, kind Unspecified, read as server time | 09:15 +01:00 | 09:15Z: a different instant on a non-UTC server |
| `"2026-10-28T09:15:00Z"` | 10:15, kind Local | 09:15 +00:00 | Same instant; the wall-clock hour, the date near midnight and the text differ for `DateTime` and `DateOnly` |
| `"2026-10-28T09:15:00+05:00"` | 05:15, kind Local | 09:15 +05:00 | As above |

`TimeSpan`, Timer, Delay and Cron (UTC in both) are unaffected. A JavaScript `Date` result left Elsa 3 as UTC, the same
as here.

The converter decides, at conversion time, because only it knows the definition came from Elsa 3; publish and run time
do not. It follows D-11's model: a visible option on the Elsa 3 converter feature, recorded on each converted workflow.
- **Source time zone not set:** a literal or variable default without an offset, bound to a `DateTime`,
  `DateTimeOffset` or `DateOnly` member, is refused with a stable diagnostic. The guidance says to set the source time
  zone or to add an offset to the value.
- **Source time zone set** (for example `Europe/Amsterdam`): the converter writes the value with that zone's offset on
  that date, so daylight saving is respected, and records the zone on the converted workflow.
- **A value with `Z` or an offset** converts as written. On a `DateTime` or `DateOnly` member it gets a behaviour
  warning, because the wall-clock hour or the date may differ from what Elsa 3 showed on a non-UTC server.
- **A non-literal expression** bound to a date member gets a behaviour warning, because the converter cannot see its
  value.
- **JavaScript `Date`** stays refused (`VF-IMP-005`) until D-1 enables it in Elsa 4. Elsa 3 parsed dates in scripts in
  the server's time zone, so converting such scripts then needs the same source time zone.

The converter already has the hooks: it parses literals bound to date members
(`Elsa3ValueFlowLowerer.cs:917`) and attaches behaviour warnings to member rules (`Elsa3ActivityNormalizer.cs:292-298`).
None of the 36 JTest definitions has a date-typed variable, input or literal, so the rule changes nothing in the corpus
today. Its date-heavy scripts are already refused.

### Kinds

- **One dynamic kind, shown in Studio as JSON.** It holds any valid JSON value: an object, an array, a string, a
  number, a boolean or null (`JsonNode` in memory, `JsonElement` at rest, ADR 0036). Its alias is `Any`; workflow
  engineers only see the name JSON. Today dynamic data has seven names, and two of them overlap:
  - Studio's variable type list offers `Object`, which maps to .NET `System.Object`
    (`DefaultVariableTypeDescriptorProvider.cs:30`);
  - the engine uses `Elsa.Any` for the same dynamic data (ForEach, BPMN, the input materializer), and about ten places
    test for either name (`Alias is "Object" or "Elsa.Any"`);
  - tooling says `Any`, and activities use the `JsonNode`, `JsonElement`, `JsonObject` and `ExpandoObject` .NET types.

  `JsonNode`, `JsonElement` and `JsonObject` stay .NET types an activity may declare; `ExpandoObject` leaves with the
  HTTP body parser.
- **`Object` is retired.** `System.Object` means "any .NET object", which is the door V10 and V11 keep closed by
  default. "Object" is also misleading as a name: in JSON it means `{}` only, while the kind holds arrays and strings
  too. JSON names exactly what it holds, and anyone with the smallest technical background knows it. The `Object` alias
  and its mapping to `object` go, and a definition that names `Object` reads it as `Any`. This
  **supersedes part of spec 264**
  ([Elsa 3 workflow variables](../../specs/264-elsa3-workflow-variable-publish/spec.md)). Spec 264 converts an Elsa 3
  type that Elsa 4 does not know, and `ObjectDictionary` and `HttpHeaders`, to `Object`, and keeps an `Object`
  variable's text starting value as text. Under this amendment those become `Any`: a JSON starting value stays that
  JSON, and text becomes a JSON string with the same text, so converted workflows keep the behaviour spec 264 chose.
  Approving this amendment approves the supersession, and spec 264 records it in the same pull request. Elsa 4 has no
  released definitions, so its own definitions need no migration.
- **New kinds `DateOnly` and `TimeOnly`,** written as ISO 8601 (`"2026-10-28"`, `"09:15:00"`).
- **Files and large content** are a reference the activity opens as a transient resource for one attempt (V7): storage
  profile, locator, name, content type and size. A script sees the reference, never the bytes. Small binary content may
  be inline as base64. This is the model for #3194.
- **Secrets and protection** stay as built: a secret is `Withheld`, resolved at every activation and never written back.
  A script result inherits the strongest protection policy of what the script actually read. That needs the reads to be
  recorded, which the [ADR 0089 amendment](https://github.com/elsa-workflows/elsa-foundation/pull/3420) (A3) and
  ADR 0092 (decision 3) provide.

### What this settles

- Matrix rows 10 (live .NET objects in scripts: V10), 19 (value model: V1, V3), 21 (exact JSON casing: ADR 0091),
  22 (binary content and secrets: *Kinds*) and 23 (XML and other formats: *Formats*), and R6 (V8).
- The positions already on the board, joined: "complex objects are always JSON" (Joey) is V1, V2 and V11; "keep conversion
  plans, loosened" (Sipke) is V3; "objects rebuilt on each attempt" (Sipke) is V7.
- All eight A-1 scenarios work:
  - S-a, an HTTP body changed in a script: V9;
  - S-b, an uploaded file: *Kinds*;
  - S-c, a domain object from a context provider: `Any` or a registered alias, with behaviour in an activity or a
    registered function;
  - S-d, durable versus transient: V7;
  - S-e, casing: ADR 0091;
  - S-f, the same value in JavaScript, Liquid, and C# scripting once it is added;
  - S-g, XML: *Formats*;
  - S-h, secrets: *Kinds*.

### What it touches

- **ADR 0035** alias registry: `Object` leaves, `DateOnly` and `TimeOnly` join.
- **ADR 0091:** the serializer every adapter uses. Case-insensitive reads and the data rules are stated there.
- **Spec 264:** superseded in part (*Kinds*).
- **Defects found by A-1:** #3421, numbers that lose digits silently in ForEach, JavaScript and diagnostic snapshots
  (V6). The ForEach and snapshot parts are plain bugs and are fixed without waiting for this amendment. The JavaScript
  part is fixed by the JSON-backed view (*JavaScript*). Nothing recorded decides that numbers become doubles: ADR 0036
  chose `JsonNode` because it is lossless on numbers.
- **Spec 095:** the frozen-value contract changes (V9).
- **ADR 0090:** detects a change by comparing serialized values. Exact write-back of untouched values is what keeps a
  variable that a script only read from looking changed.
- **ADR 0036:** assumes Jint reads `JsonNode` through a `JsonElementConverter`; that converter is gone from main, and
  the closed engine converts values itself. The JSON-backed view brings the engine back in line with its intent.
- **Specs and issues:** S-8 is defined against this amendment. It unblocks #3196, #3202 (model part), #3194,
  #3287 (type part) and the custom-function part of #3026.

### Questions for reviewers

1. @FransVanEk: does this text carry A-1 as you meant it? Two changes from your write-up: V9 no longer assumes scripts
   may write (that is A-3), and retiring `Object` records the spec 264 supersession. V11 is new (question 6).
2. **A model version.** The analysis says the model is versioned and a saved workflow records the version. Executables
   already pin their conversion plans and expression profiles (ADR 0083), and the artifact hash covers both. This text
   proposes no separate model version. Do you see a case that needs one?
3. **Dates for converted Elsa 3 workflows.** Refuse a date without an offset unless the source time zone is set, and
   convert it with that zone's offset when it is (*Dates*)? Or only warn?
4. **Numbers in scripts.** Pass-through exact through a JSON-backed view, and a number the script reads is a double
   (*JavaScript*). Two limits, both from Jint 4.9.1: a number the script copies (`{ id: x.id }`) is read, so it becomes
   a double; and a lazy array cannot be a real JavaScript array, so arrays of large numbers stay lossy unless we accept
   an array-like view where `Array.isArray` is false. `BigInt` is no way out: `JSON.stringify` throws on it, and mixing
   it with numbers throws. Agreed?
5. @sfmskywalker: `Object` retired in favour of one dynamic kind, alias `Any`, shown in Studio as JSON, with the spec
   264 supersession. Agreed? @FransVanEk asked for one generic name; Joey proposes JSON as the name workflow engineers
   see.
6. **V11 and defaults first.** This is @j03y-nxxbz's position: kinds instead of .NET types for workflow engineers
   everywhere, and only the default implementations built now. If you think V11 goes too far, he accepts limiting it to
   scripts (V10) for now. Where do you stand?
