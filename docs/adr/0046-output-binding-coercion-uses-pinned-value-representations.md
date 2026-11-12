# Output Binding Coercion Uses Pinned Value Representations

Status: proposed (output-capture slice accepted — see Decision status)

Amendment proposed (analysis A-1): one core value model, an adapter with a written contract at every boundary, and the agreed adapter rules. If
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

## Amendment (analysis A-1)

This amendment follows analysis A-1 and the tests run for it. Rules V1 to V10 follow the analysis's principles P1 to
P10; V11 is an addition to them.

**Why.** Elsa 4 already runs on one value model in practice. Every value at rest is JSON with a type label
(`DurableValueState`), every value in flight travels in a `ValueEnvelope`, and scripts only ever see JSON. What is
missing is a written definition and a contract per boundary. The tests and a search of the value path found where
values change silently today:
- a camelCased JSON member does not bind to a PascalCase activity input;
- a `DateTime` with an offset becomes server-local time;
- integers beyond 2^53 and long decimals lose digits in scripts and in ForEach;
- activity results are written with different serializer options than the next activity reads them with
  (ADR 0091, Context).

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
implemented, and they stay pinned at publish. Every adapter serializes through options derived from ADR 0091's
baseline; conversion into a user type uses that type's scoped conversion contract (ADR 0091 decision 2).

**An adapter rule never changes in place.** Any change to an adapter
rule ships as a new, pinned conversion-plan or profile version, never as an edit of an existing one, so one name keeps
one behaviour (as with `binding-readonly-v1`, ADR 0083). A published executable keeps the versions it was published
with; only republishing moves it to a newer one. That is why the model needs no separate model version. Before 4.0,
reset and republish is enough (reconciliation row 12).

**V4. Logic beyond the boundary is allowed.** A .NET type may run its own logic when it is created, set or read:
constructors, setters, functions implemented in .NET (D-5). The engine cannot prevent it and does not try. The designer
owns the consequences, as with any external effect (D-2).

**V5. Returning to the core is the contract.** Whatever crosses back must be a valid core value, or the conversion
fails with a clear diagnostic. Nothing that cannot be expressed in the core model enters workflow state.

**V6. Lossy conversion is declared, never silent.** Losing precision or detail is a documented property of an adapter.
The designer can see it and chooses it, and the result is still a valid core value (in line with R5 on explicit
selection). A silent loss is a defect.

**V7. Live objects stay on their side.** A .NET instance lives only for the attempt that created it. After a pause,
restart or retry it is rebuilt from the core value, never restored. Recovery runs forward from the core model (D-2).

**V8. Null, missing and undefined.** Null and missing are two states of the core model (`ExplicitNull`, `Absent`).
`undefined` exists only in JavaScript, so it is a rule of the JavaScript adapter: the visible policy of D-11. An
`undefined`-valued property that the default drops gives a diagnostic, and NaN and Infinity become null with a diagnostic, because a lossy conversion is declared,
never silent (V6).

**V9. A script works on plain JavaScript values; only what it changes is written.** A script gets ordinary
JavaScript objects and real arrays (today the objects are frozen). Changing them changes nothing outside the script by
itself. Writing back is a general capability of script activities (A-3, ADR 0090 amendment), so S-a
works; input-binding expressions never write workflow state (D-3). A write commits with the step and a fault discards it. A write of a
value the script read is a read-modify-write under ADR 0092.

This ADR states the behaviour. How the engine detects and writes a script's changes is left to the implementing spec
(S-8), which chooses the mechanism and validates it against these behaviours, with evidence for member mutation, whole
replacement and precision handling:
- **A read never looks like a change.** Reading a variable, alone, never rewrites its stored value. A number rounded on
  its way into JavaScript is not a change, and a variable the script never reads is never touched.
- **A member change keeps the other members.** `variables.order.total = 20` changes that member, and the members the
  script did not change keep their stored values, large numbers included (ADR 0090 amendment, C3).
- **A whole assignment replaces.** `variables.order = { total: 20 }` replaces the value (ADR 0090 amendment, replace
  or partial update).
- **Write-back keeps the data's meaning** under the selected conversion contract. Its notation may change (`2.0` may
  come back as `2`); its values do not, except under the precision rules below.
- **Arrays are real JavaScript arrays.** A changed array is written as a whole for now. A later implementation may keep
  unchanged elements as stored, without changing this rule.
- **Lost precision at a write is refused, not just reported.** A number that lost digits on its way into JavaScript is
  known as such: the JavaScript number, written as JSON, differs from the stored one. When such a value reaches
  anything the step **writes**
  (copied, returned, or inside a replaced array), the write is **refused by default with a diagnostic** naming the path
  and both values; a warning alone is insufficient, because silent precision loss corrupts data. A designer who intends
  the loss accepts it explicitly to allow the write. That acceptance is a deliberate choice **recorded in the published
  conversion contract**; the implementing spec defines the surface. A script that only **computes** with such a value,
  without writing it, still gets a warning — the engine cannot prevent a lossy computation
  (`9007199254740993 + 1` is still `9007199254740992`), only its silent persistence.
- **How a loss is handled is a replaceable policy; the default refuses.** The handling of a precision loss is one
  policy, provided by a feature and set for the whole host. It belongs to the JavaScript engine integration, because
  the loss happens where a value crosses from .NET into the JavaScript engine; the engine can tell there which values
  exceed what a JavaScript number holds, before the script runs. Other expression languages do not have this problem.
  The default policy refuses, as above. A host that wants different handling, for example a warning, installs another
  policy. There is no per-workflow override; one can be added later if a real need shows up.
- **A lossy value passed to a host function is not a write.** When a script passes a value that lost precision as an
  argument to a registered .NET function (for example `lookupOrder(order.id)`), the engine logs a warning and does not
  refuse the call. Working with such values is the script author's responsibility.
- **The refusal of a whole-array write says what was lost.** When a changed array is written as a whole and an element
  in it lost precision, the refusal diagnostic:
  - names the array path and says that the array was written as a whole;
  - lists each element that lost precision, with its stored and its written value (for example `lines[2].qty`: stored
    `9007199254740993`, written `9007199254740992`);
  - gives the remedy: pass integers beyond 2^53 as strings, or avoid changing the array;
  - fires only on a real loss: `10.50` → `10.5` is the same value and is no loss;
  - is visible, as an instance diagnostic and in Studio, not only in a log.

  The trade-off: an element the script did not touch can still lose precision when its array changes. That loss is
  prevented by default, and accepting it is explicit, as above.
- **Accepted gap.** A deliberate write of a value equal to the JavaScript value the script read (for example setting
  `…992` over a stored `…993`) is treated as unchanged, and the stored value is kept. The script cannot tell the two
  apart. Writing the value as a string forces the write.
- **The cost is accepted by default.** Finding a script's changes may cost time linear in the data the script reads.
  Integrity comes before performance in the default; a faster implementation may come later through V12, only if it
  behaves identically.

This changes the frozen-value contract of spec 095
(`specs/095-value-flow-redesign/contracts/expression-and-import-contract.md`, lines 105-107) and the note in ADR 0090
that a loop item is frozen. Approving this amendment approves that change; `ScriptValueIsolationTests` changes with it.

**V10. Live .NET objects in scripts are opt-in.** By default a script sees JSON and registered .NET functions that
exchange JSON (D-5). A host may opt in to .NET interop: objects it exposes, each a transient resource for that attempt
(V7), and, deliberately, general access to .NET types (Jint's `AllowClr`). It is never the default. The opt-in is one
replaceable engine strategy that the host enables through a feature, not a set of named profiles chosen per script or
per expression. This follows the platform principle: guardrails and diagnostics, not restrictions.

**One switch for scripts and input-binding expressions.** When a host enables .NET interop for JavaScript, script
activities and JavaScript input-binding expressions get it alike: a JavaScript input expression takes the same input
as a script in a JavaScript script activity. The default for both stays JSON. The opt-in comes with these guardrails:
- **Visible.** A publish warning and a Studio marker on every node that uses general .NET access (ADR 0083).
- **Stored value.** A binding may use .NET objects while it evaluates. What it returns is a storable value under this
  ADR, or it is refused. No live object enters the input snapshot (V7).
- **Elsa state.** Direct writes to Elsa-owned workflow state stay blocked in input bindings (D-3).

The opt-in's own design, in its implementing spec, settles two further points: whether an input-binding expression
that uses .NET access is treated as possibly effectful, with a commit before and after its evaluation, and how a
published executable records that it needs .NET access, so it never runs silently on a host without it.

**JSON values and .NET objects may coexist.** The default representation is a convenience, not an exclusive mode.
Under an opt-in that allows .NET objects, a script or an input-binding expression may work with plain JavaScript
values, host-exposed .NET objects, or both in the same script or expression, converting explicitly where needed.
Enabling .NET interop does not turn every value into a .NET proxy. The implementing spec defines how an author obtains
each representation. The boundaries above still apply: the host controls which .NET capabilities it exposes, durable
values follow their conversion, persistence and write-back contracts, input bindings never write Elsa-owned workflow
state, and a known precision loss at a write follows the host's precision policy (V9).

**V12. One seam for how values reach a script, with pinned profiles.** How a value enters a script and how its
changes come back is one seam with named, pinned implementations (V3's rule: one name keeps one behaviour):

| Profile | The script sees | Changes come back by |
|---|---|---|
| JSON (default) | Plain JavaScript objects and real arrays | The changes the script makes (V9) |
| .NET objects (opt-in) | Plain JavaScript values as in the default, alongside live .NET objects the host exposes through Jint interop | JSON values as in the default (V9); a live object only by explicit write-back (`variables.order = …`), because changes to a live object are not observed |

- The engine sets the default, JSON out of the box. A host that enables .NET interop (V10) enables it for script
  activities and input-binding expressions alike (reconciliation rows 17a and 17b).
- An input-binding expression under the opt-in follows V10's guardrails: a storable result or a refusal, and no writes
  to Elsa-owned state; such a binding has no write-back (row 17a).
- Publication and Studio show a clear warning when a script activity or an input-binding expression uses .NET objects
  (row 17).
- A faster implementation of the JSON profile, such as a view that converts each value only when it is read, may be
  added later if measurements ask for it, and only if it behaves identically. If it differs in any way, it is a new
  profile with its own name.

**V11. Workflow engineers work with kinds, not .NET types; custom types and typed access stay available.** A
variable's declared type and the representation a particular script receives are separate choices. The declared type
is a kind from the alias registry (ADR 0035): a JSON representation plus validation. `DateOnly` is an ISO 8601
string, not the .NET struct with its methods. Workflow engineers see and use kinds, in Studio and in scripts
(JavaScript, Liquid, and C# scripting once it is added). By default the .NET type is used only inside the activity
input and output adapter and inside registered functions (D-5).
- A custom registered type, such as `Order`, keeps its name and its data contract. It is stored as JSON under that
  contract and does not force every consumer into .NET interop: JavaScript receives its JSON representation by
  default.
- Explicitly selected typed access stays available: working with a value as a .NET object, with its properties and
  methods, through the host-enabled opt-in (V10, V12), never by default. It follows the explicit conversion,
  persistence and write-back rules of this ADR. A .NET engineer may also register their own aliases and expose their
  properties and methods to scripts.
- This is not a rule that only JSON kinds exist across Studio and every scripting language; durable storage is
  governed by the JSON data contract, and typed access is an explicit choice on top of it.
- Code-first authors (`WorkflowBuilder.Variable<T>`) are .NET developers by definition. They may use .NET types in
  their own code; their values are still stored as core values (V7).

*Why.* Elsa has two audiences. Workflow engineers are its consumers: Studio and scripts are their front door, and they
should not need to know .NET. .NET engineers maintain an Elsa environment and customize it through extension points.
A company whose workflow engineers do know .NET can open those extension points, with clear documentation, but the
default stays plain. *Dates* shows what goes wrong otherwise: Elsa 3 exposed dates as .NET `DateTime` values, so the
same workflow behaved differently on each server.

**Defaults first; the .NET opt-in is defined here and built separately.** JSON stays the durable contract and the
default JavaScript representation. S-8 builds the defaults: JSON values, the kinds and registered functions, with .NET
objects disabled in scripts. The guarded opt-in of V10, for host-exposed .NET objects in script activities and in
input bindings, is specified in this ADR and implemented separately; nothing in S-8 depends on it. Live .NET objects
are never required to solve a core engine problem: core needs are met by registered functions that exchange plain
values, for example a function that reads the bytes of an uploaded file from an activity's output and returns them as
bytes. The opt-in is an addition for .NET developers who want one domain language shared between .NET and JavaScript.
When it is enabled, it follows the persistence, per-attempt materialization (V7), workflow-state write and
external-effect rules above, records the author's representation choice in the published contract and needs explicit
write-back for durable data; selecting it gives no rollback or exactly-once execution for arbitrary .NET methods (V4).
Until it is implemented, an Elsa 3 script that uses .NET members is refused at conversion with guidance. Unrestricted
.NET type access (Jint's `AllowClr`) and broader conveniences, such as registered aliases with properties and methods,
are likewise defined in this ADR and built separately when a use case asks for them.

### Adapter rules

**Activity inputs and outputs** (JSON to a declared .NET type and back):

| Value | Today (tested or read) | Rule |
|---|---|---|
| `{"customerName":…}` into a class with `CustomerName` | property stays empty, no error | Matched under the user type's scoped conversion contract (ADR 0091 decision 2). The baseline's camelCase convention applies to Elsa's own contracts, not to user types. A user type uses a documented default conversion contract, with exact (case-sensitive) matching by default and scoped options for its naming policy, case-insensitive matching and explicit member mappings (or `[JsonPropertyName]`); the selected behaviour is recorded in the published conversion contract, and no host-wide option changes. A key that matches no member under the effective contract gives a recorded warning, not a refusal, so nothing is lost silently; the Elsa 3 converter rewrites a literal key that differs from the contract's name only in case and refuses only ambiguous ones (ADR 0091 decision 2, reconciliation row 9) |
| A .NET-typed result read into the next activity's input | written camelCased, read case-sensitively, so properties can be lost (inferred) | Both directions use the same effective conversion contract for that type (ADR 0091 decisions 2 and 3) |
| `"5"` into `Int32`, `"true"` into `Boolean`, `5.0` into `Int32` | refused with the defaults; `"5"` accepted where Web options are used | Refused everywhere: no text parsing or truthiness (R5) |
| A 20-digit integer into `Int64` or `Decimal`, a 29-digit decimal into `Decimal` | exact at the input; ForEach turns every item number into a `double` (a defect found by A-1, a bug that can be fixed now) | Exact; no adapter narrows a number unless the target type asks for it |
| Upper-case GUID | accepted, written lower case | Keep, stated |
| Dates | see *Dates* | |
| `"01:30:00"` and `"PT1H30M"` into `TimeSpan` | accepted, refused | ISO 8601 durations are the standard and are written out; `hh:mm:ss` is still read for Elsa 3; `P1M` and `P1Y` are refused (no fixed length) |

**JavaScript** (and any later script adapter, such as C# scripting, which follows the same contract):

| Value | Today | Rule |
|---|---|---|
| Integer within ±2^53 | exact | Keep |
| Integer beyond ±2^53, or a `Decimal` with more digits than a double holds | digits lost on the way in, also for values the script never touches: the whole input is converted into JavaScript values up front, and Jint holds every number as a double | Values the script does not change keep their stored value: only what the script changes is written (V9). A number the script reads is a JavaScript number, a double (V6, stated), and a lost digit that reaches a write is refused by default with a diagnostic (V9), unless the designer explicitly accepts the loss, recorded in the published conversion contract. Elsa never turns numbers into strings by itself. A designer who needs exact digits holds them as a string, the usual JSON convention for integers beyond 2^53; exact money arithmetic goes to a .NET function (D-5) or an activity |
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

This section owns every date and time-zone rule. The amendments of ADR 0045 §5
and ADR 0083 state only the engine's clock (UTC, from an injectable clock) and point here.

**The rule.** A date means the same instant on every server.
- **`DateTime`** is normalized to UTC at every adapter. A value with `Z` or an offset is converted to UTC and written
  out with `Z`. A value without an offset (`"2026-10-28T09:15:00"`) is **not silently read as UTC**: it needs an
  explicitly configured interpretation — a declared source time zone, or an explicit assume-UTC policy — before it
  becomes an instant. Without one, the conversion is refused with a diagnostic rather than guessing.
- **`DateTimeOffset`** keeps its offset. A value without an offset follows the same rule as `DateTime`: it does not
  silently get `+00:00`; it needs a source time zone or an explicit assume-UTC policy, and without one it is refused.
  Choosing the type does not choose the zone. An activity that needs the offset declares `DateTimeOffset`.
- **The interpretation is explicit and stable.** The source time zone or the assume-UTC policy is set on the
  conversion and recorded in the published conversion contract, so a published executable reads the same value as the
  same instant on every server.
- **`DateOnly`** takes the date as written. A value with a time and an offset takes its date in UTC.
- **Publish-time literals and variable defaults follow the same rule.** Today they pin the publishing server's offset
  (ADR 0091, Context).
- **Scripts** keep dates as ISO 8601 strings (see *JavaScript*).
- **Time zones** are IANA time zone IDs (for example `Europe/Amsterdam`). UTC is the engine's clock, not a restriction
  on what a workflow stores: a workflow may store a time zone ID as a string, or a time with its offset as
  `DateTimeOffset`, and convert explicitly with a built-in date helper (D-1; S-1 defines the helpers). The converter's
  source time zone below is the same kind of value.

**Converted Elsa 3 workflows.** Elsa 3 (3.6.3) read dates in the server's time zone, so the same workflow behaved
differently per server. Every literal, input and variable default went through `ObjectConverter.ConvertTo`, which
parses with `DateTime.Parse` and no `DateTimeKind` handling. Tested on a UTC+1 server:

| Elsa 3 input | Into `DateTime` | Into `DateTimeOffset` | Under this rule |
|---|---|---|---|
| `"2026-10-28T09:15:00"` | 09:15, kind Unspecified, read as server time | 09:15 +01:00 | Refused without a source time zone or assume-UTC policy; with assume-UTC, 09:15Z: a different instant on a non-UTC server |
| `"2026-10-28T09:15:00Z"` | 10:15, kind Local | 09:15 +00:00 | Same instant; the wall-clock hour, the date near midnight and the text differ for `DateTime` and `DateOnly` |
| `"2026-10-28T09:15:00+05:00"` | 05:15, kind Local | 09:15 +05:00 | As above |

`TimeSpan`, Timer, Delay and Cron (UTC in both) are unaffected. A JavaScript `Date` result left Elsa 3 as UTC, the same
as here.

The converter decides, at conversion time, because only it knows the definition came from Elsa 3; publish and run time
do not. It follows D-11's model: a visible option on the Elsa 3 converter feature, recorded on each converted workflow.
- **Source time zone not set:** a literal or variable default without an offset, bound to a `DateTime`,
  `DateTimeOffset` or `DateOnly` member, is refused with a stable diagnostic. The guidance says to set the source time
  zone or to add an offset to the value.
- **Source time zone set** (an IANA time zone ID, for example `Europe/Amsterdam`): the converter writes the value with that zone's offset on
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
  be inline as base64. This is the model for Elsa 3 resource variables
  (`FormFile`, `Stream`, `byte[]`).
- **Secrets and protection** stay as built: a secret is `Withheld`, resolved at every activation and never written back.
  A script result inherits the strongest protection policy of what the script actually read. That needs the reads to be
  recorded, which the ADR 0089 amendment (A3) and
  ADR 0092 (decision 3) provide.

### Alternatives considered

- **General .NET access for script activities only, never in input-binding expressions.** Not chosen. A registered
  function is already .NET code that a binding calls, so the restriction takes a capability away without keeping .NET
  out of bindings. And under faithful-or-refuse, an Elsa 3 input expression that uses .NET access would have to be
  refused at conversion. The guardrails of V10 keep the default plain and make the opt-in visible and bounded instead.
- **Named .NET profiles, selected per script or per expression and pinned by the executable.** Not chosen. One
  switch that applies to scripts and input-binding expressions alike is simpler: a JavaScript expression behaves as
  the same JavaScript in a script activity does, and an author never has to know which profile an expression uses.
- **A fixed refuse-or-warn rule for precision loss, or a per-workflow override.** Not chosen. Teams may want
  different handling, so the handling is a replaceable host policy with refusal as the default (V9). A per-workflow
  override adds a second level of configuration without evidence that anyone needs it.
- **Shipping a minimal .NET interop together with the defaults.** Not chosen: the interop is not needed to deliver
  the defaults, and core needs go through registered functions that exchange plain values. Building it alongside them
  would put an optional capability on the path of required work. .NET objects stay disabled in scripts until the
  opt-in of V10 is built on its own.
- **Frozen or JSON-backed views of each value instead of plain JavaScript values.** Not chosen: a view needs a custom
  Jint object for every value and cannot give real arrays. `BigInt` is no way out either: `JSON.stringify` throws on
  it, and mixing it with numbers throws.
- **Fixing the change-detection mechanism in this ADR.** Not chosen: the ADR states the behaviour of V9, and the
  implementing spec chooses the mechanism and validates it against that behaviour.

### What this settles

- Matrix rows 10 (live .NET objects in scripts: V10), 19 (value model: V1, V3), 21 (exact JSON casing: ADR 0091),
  22 (binary content and secrets: *Kinds*) and 23 (XML and other formats: *Formats*), and R6 (V8).
- The positions already stated, joined: "complex objects are always JSON" is V1, V2 and V11; "keep conversion plans,
  loosened" is V3; "objects rebuilt on each attempt" is V7.
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
- **ADR 0091:** the serializer every adapter uses. The scoped conversion contracts for user types, exact member reads
  by default, the diagnostic for unmatched keys and the data rules are stated there.
- **Spec 264:** superseded in part (*Kinds*).
- **Defects found by A-1:** numbers that lose digits silently in ForEach, JavaScript and diagnostic snapshots
  (V6). The ForEach and snapshot parts are plain bugs and are fixed without waiting for this amendment. The JavaScript
  part is fixed by writing only what a script changes (V9). Nothing recorded decides that numbers become doubles: ADR 0036
  chose `JsonNode` because it is lossless on numbers.
- **Spec 095:** the frozen-value contract changes (V9).
- **ADR 0090** (proposed, not yet reviewed) detects a change by serializing each tracked variable at commit and
  comparing it with its base (§2, §6). Every adapter in this ADR may lose detail (V6), so that comparison turns a read
  into a spurious write and corrupts untouched fields on a real write. Today a read cannot write: there is no
  tracking, and a test with a large number left the stored text and revision unchanged. The ADR 0090 amendment
  replaces the comparison of stored text with explicit writes. In a script, a change the script makes is such a write (V9), and a read never looks
  like a change.
- **ADR 0036:** assumes Jint reads `JsonNode` through a `JsonElementConverter`; that converter is gone from main, and
  the closed engine converts values itself. Writing only what a script changes (V9) brings the engine back in line
  with its intent.
- **Specs:** S-8 is defined against this amendment.
