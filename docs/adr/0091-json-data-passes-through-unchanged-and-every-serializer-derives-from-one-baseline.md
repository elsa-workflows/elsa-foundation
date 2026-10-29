# JSON Data Passes Through Unchanged And Every Serializer Derives From One Baseline

Status: proposed (2026-10-27). Joey Barten (@j03y-nxxbz) proposed it from analysis A-5 on
[epic #3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052). The facts, with file and line references,
are in the [A-5 analysis](https://github.com/elsa-workflows/elsa-foundation/issues/3052#issuecomment-6451104603).
Acceptance needs agreement from Sipke Schoorstra (@sfmskywalker) and Frans van Ek (@FransVanEk).

Part of epic #3052. Answers decision-matrix row 21 ("JSON property names keep their exact casing everywhere") and
A-1 scenario S-e.

Related:

- Builds on [ADR 0035](0035-serialization-unifies-on-the-alias-registry-and-retires-open-object-polymorphism.md)
  (one type story, designer bags as opaque JSON).
- Builds on [ADR 0036](0036-dynamic-any-expression-values-materialize-as-jsonnode-with-per-engine-adapters.md)
  (dynamic values as `JsonNode`).
- Extends the [serialization rule](../serialization.md): `IPayloadSerializer` stays the contract for domain payloads,
  and its options derive from the baseline defined here.

Not decided here:

- The value model and conversion, in
  [ADR 0046](0046-output-binding-coercion-uses-pinned-value-representations.md) and analysis A-1
  ([outcome](https://github.com/elsa-workflows/elsa-foundation/issues/3052#issuecomment-6466068155), proposed as an
  ADR 0046 amendment). A-1 defines the adapter at each boundary: what it accepts, produces and loses, including the
  rules for dates, numbers in scripts and property matching at activity inputs. This ADR is the serializer those
  adapters use, and it must agree with them (decisions 2 and 5).
- The representation of null versus missing, in spec S-2 under decision D-10.

This ADR only constrains how those serialize.

## Context

Workflow data is JSON that users author or send: variable defaults, workflow inputs and outputs, literals, activity
results, HTTP bodies and trigger payloads. Elsa does not own its meaning. Serializer settings have changed such data
before, through renamed keys, changed escaping, and the type wrappers that ADR 0035 removed. Data integrity requires
that Elsa write data exactly as it received it.

A-5 found on `main`:

- **No workflow value is renamed today.** Every value on the API paths A-5 traced is a `JsonElement` (or a string)
  when written. System.Text.Json never applies naming policies inside a `JsonElement`. This holds by accident, not
  by rule.
- **Names used as dictionary keys are renamed on write.** Keys are read verbatim and written camelCased.
  - `DictionaryKeyPolicy = CamelCase` is set in the Runtime, Publishing, Activities Design, Connections and Secrets
    API options, and in the `AuthoringSchemaExporter` wire options.
  - The Runtime API returns instance outputs keyed by the authored output name: `OrderTotal` comes back as
    `orderTotal`.
  - It also returns expression bindings keyed by parameter name.
  - Contract tests in Publishing and Activities Design assert that workflow-input and activity-input names are
    written camelCased (`SomeInput` → `someInput`).
- **One module's settings reach other modules.** Publishing and Activities Design call
  `ConfigureHttpJsonOptions(... Configure(...))`, which sets their key policy, encoder and enum converter on the
  host-wide HTTP JSON options. Every `Results.Json` call without its own type info then uses them.
- **Every written value is re-escaped.** Writing a `JsonElement` or `JsonNode` re-encodes all strings and property
  names with the writer's encoder.
  - With the default encoder, used by the Runtime and Design owner contexts and by `IPayloadSerializer`:
    `René` becomes `Ren\u00E9`, `<` becomes `\u003C`, and `+` becomes `\u002B`.
  - With the relaxed encoder, used by Publishing and Activities Design: `Ren\u00e9` becomes `René`,
    `a\/b` becomes `a/b`, and emoji are escaped.

  The value stays the same, but the text does not. Numbers keep their written text (`1.0`, `0.10`, a 23-digit
  integer) as long as they stay `JsonElement`. A converter that writes `GetRawText()` with `WriteRawValue` keeps
  the text byte-identical (probed on .NET 10).
- **Explicit null and missing merge.**
  - `IPayloadSerializer` drops nulls on write (`DefaultIgnoreCondition = WhenWritingNull`).
  - `JsonElement?` and `object?` members read `{"x":null}` and `{}` the same.
- **Every module builds its own options.** Under `src/` there are:
  - about 32 `new JsonSerializerOptions` constructions in 29 files;
  - 28 `[JsonSourceGenerationOptions]` attributes;
  - several uses of `JsonSerializerOptions.Web` and `.Default`.

  Nothing states which settings may differ, or why.

A-1 and a follow-up search over the value path (2026-10-29, `main` at `6432ed240`) found that A-5's list covered the
configured option sets but not the calls that use none. Wherever data stays `JsonElement` or `JsonNode` (stimulus and
event payloads, child workflow inputs, bookmark payloads, durable state, expression parameters), nothing is
transformed. The risk sits where data becomes a .NET type and back:

- **At least four option sets handle workflow values:** System.Text.Json defaults, about eight local
  `new(JsonSerializerDefaults.Web)` statics, `IPayloadSerializer`, and `ObjectConverter`'s own options.
- **Defaults are a pattern, not one call.** Activity inputs (`ActivityInputHydrator.cs:97`), expression results
  coerced to a declared type (`RuntimeActivityInputMaterializer.cs:575`, then `:189`), publish-time literals and
  variable defaults (`RuntimeInputBindingCompiler.cs:1019`, `:1044`, `:719`), workflow-input validation
  (`WorkflowExecutableInputValidator.cs:119`) and stateful activity state (`StatefulActivity.cs`) all use the defaults:
  case-sensitive member matching, PascalCase on write, and a `DateTime` with an offset converted to server-local time.
- **The two sides of one value disagree.** An activity result is written with Web options, so camelCased
  (`ActivityCompletionProjector.cs:16,63`), and read into the next activity's input with the defaults, so
  case-sensitively. A .NET-typed result bound to a .NET-typed input can silently lose its properties. Code-first
  literals follow the same pattern (`WorkflowBuilder.cs:334` writes Web, `RuntimeInputBindingCompiler.cs` reads with
  the defaults). This is inferred from the code and not yet reproduced by a test.
- **The same text is accepted on one path and refused on another.** Web options read numbers from strings (`"5"` into
  `Int32`); the defaults refuse them.
- **Numbers lose digits outside the script engine too.** ForEach turns every item number into a `double`
  (`ForEach.cs:261`: the conditional `long : double` has type `double`), so a large or precise number changes, `1.0`
  becomes `1`, and `1e400` faults the loop (#3421).
- **No `DateTime` converter exists.** Date handling is the System.Text.Json default, or `Convert.ChangeType` for
  publish-time literals, which pins the publishing server's offset into the executable.

## Terms

- **Data**: a JSON value Elsa carries but does not own: workflow inputs, outputs, variable values and defaults,
  literals, activity inputs and results, payloads and bodies.
- **Own contract**: a JSON shape Elsa defines, such as DTOs, views, problem details and persisted envelopes.
- **Name key**: a dictionary key that is an authored or stable name, such as an output, input, variable, parameter
  or node id, or a validation path built from them.
- **Baseline**: the one read-only `JsonSerializerOptions` instance defined by this ADR.
- **Deviation**: a serializer whose protected settings (decision 6) differ from the baseline.

## Decision

### 1. One read-only baseline in `Elsa.Serialization.Core`

`Elsa.Serialization.Core` defines one baseline `JsonSerializerOptions`, made read-only with `MakeReadOnly()`.

- Every serializer derives from it: module serializer contexts, API wire contexts, `IPayloadSerializer` and the host
  HTTP options. Code that needs more starts from a copy, `new JsonSerializerOptions(baseline)`, so every difference
  is local and visible.
- A source-generated context is constructed with such a copy. Its `[JsonSourceGenerationOptions]` attribute settings
  apply only to its `Default` instance, so owner code uses the constructed instance, not `Default`.
- Any project that works with JSON may reference `Elsa.Serialization.Core`, as it would `System.Text.Json`.

### 2. The baseline transforms no data

The baseline:

- sets no `DictionaryKeyPolicy`, so name keys and data keys are written verbatim;
- sets no `NumberHandling` (numbers are never read from or written as strings);
- has no converter that changes or reorders data;
- keeps nulls (no `WhenWritingNull` or `WhenWritingDefault`);
- camelCases only the member names of Elsa's own C# types (`PropertyNamingPolicy = CamelCase`). That shapes our own
  contract, not data.

Member-name reads are exact (`PropertyNameCaseInsensitive = false`, unlike today's Web default). No read guesses
which member a key was meant for. A JSON member fills a .NET member only when its name is exactly the member's JSON
name: the name the baseline's naming policy gives it, or the name an annotation gives it
(`[JsonPropertyName("customer_name")]`). Matching never changes data; it only decides which .NET member a JSON member
fills.
- **A JSON key that matches no member gives a diagnostic** that names the key and the target type
  (System.Text.Json's unmapped-member handling), so a key is never dropped silently. A required member that is missing
  already fails under D-10.
- Because the match is exact, two keys can no longer match one member (`id` and `ID`), so that ambiguity needs no
  rule.
- This applies to data becoming a .NET type (decision 5) and to Elsa's own contracts, which keep the camelCase naming
  policy in both directions.

This follows A-1 as corrected by @FransVanEk on 2026-10-29
([comment](https://github.com/elsa-workflows/elsa-foundation/issues/3052#issuecomment-6496154563)). It replaces the
case-insensitive reads this ADR first proposed.

### 3. Data passes through as the exact text received

The baseline writes a `JsonElement` as its original text, using `WriteRawValue(GetRawText(),
skipInputValidation: true)`. Skipping validation is safe because a `JsonElement` is always valid JSON, and it avoids
a re-parse per value.

- A value an endpoint accepted is stored and returned byte-identical: escaping, number text, key order, casing, and
  the whitespace inside the value.
- An indented writer does not re-indent such a value.
- Validating that value is the job of the endpoint's request contract.
- A value that a script or an activity changes is a new value and is written in the default format (decision 4).
- A value that passes through a script unchanged can change today: the adapter converts the whole input into
  JavaScript values up front, and Jint holds every number as a double, so an integer beyond ±2^53 or a long decimal
  loses digits (`IsolatedJintEngine.cs:168-169`, found by A-1). That loss is the JavaScript adapter's, not the
  serializer's. The ADR 0046 amendment ([#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422)) fixes
  it there: the script reads a JSON-backed view, and parts it does not read are written back as their original text.
  Values the script reads or computes are new values and come back in the default format.
- Because stored text keeps whatever notation arrived, two equal values can have different text. Nothing that decides
  whether a value changed compares stored text: equality of values uses the canonical form of decision 7. That is
  the no-op test of the ADR 0090 amendment ([#3425](https://github.com/elsa-workflows/elsa-foundation/pull/3425), C4) and the value digest of ADR 0092.

### 4. One default output format

Everything not covered by decision 3 is written with one encoder for every API and store:

- Elsa's own strings and member names.
- Values that were created or changed.

The proposed encoder is `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`:

- Publishing and Activities Design already use it.
- It matches what decision 3 lets through: raw text bypasses any encoder.
- Every Elsa response is `application/json`, so the HTML-sensitive characters this encoder leaves unescaped are
  harmless as long as responses are never rendered as HTML. The implementing spec proves this (see Required evidence).

Today the Runtime and Design owner contexts use the default encoder. The same value therefore leaves them with
different text than it leaves Publishing.

### 5. Data is JSON at every boundary

At API, persistence and messaging boundaries, data is held as `JsonElement` or `JsonNode`. It is never a .NET
dictionary, an `ExpandoObject` or a POCO. A-5 found no data held otherwise on the API paths it traced; this decision
makes it a rule. Data is deserialized into .NET types only inside an activity or a function that asks for it
(ADR 0046).

That deserialization is a boundary too, and it uses a copy of the baseline like every other serializer, in both
directions. Today it does not (see Context): activity inputs, expression-result coercion, publish-time literals and
stateful activity state use System.Text.Json's defaults, while activity results are written with Web options. Every
conversion between data and a .NET type, in or out, uses the same baseline copy, so a value written by one activity
reads back the same in the next. A-1's adapter rules for these conversions (exact member matching with a diagnostic for
unmatched keys, a `DateTime` read as UTC) are applied through that copy, not as special cases at each call site. A number is never narrowed to
`long` or `double` on the way, unless the target type asks for it.

### 6. Modules never change host-wide options; deviations are a red flag, not a ban

- **Host options belong to the host.** A module configures only its own context. Only the host sets the host's
  naming, key policy, encoder, null handling and converters, from the baseline.
- **Resolvers cover owned types only.** A module may add a type-info resolver to the host options, as Workflows Design
  and Workflows Runtime do. Because a resolver inserted first can shadow others, it may describe only types the module
  owns.
- **Protected settings** are `DictionaryKeyPolicy`, `NumberHandling`, `DefaultIgnoreCondition`, `Encoder`, and
  converters or type-info modifiers that change or reorder data.
- **An architecture test finds deviations.** It combines a source scan and reflection:
  - The scan covers `src/` for `new JsonSerializerOptions`, `JsonSerializerOptions.Web`/`.Default`,
    `[JsonSourceGenerationOptions]` settings, and `JsonSerializer` calls without options or type info.
  - Reflection covers each module's constructed serializer contexts and the composed host HTTP options. It also
    checks that host resolvers do not overlap.

  The test fails on any deviation that is not listed in the **baseline deviation register**. That is a new
  "Baseline deviations" section in [docs/serialization.md](../serialization.md), listing the file, the setting, the
  reason and the approving ADR or issue. It sits beside the existing "Approved exceptions", which lists code that does
  not use `IPayloadSerializer` at all.
- **The bar depends on what is touched.** A deviation that touches **data** at an API boundary needs an ADR. A
  deviation in an internal serializer of our own contracts needs a register entry with its reason. This follows the
  platform principle that Elsa gives guardrails and diagnostics, not restrictions.

### 7. Expected entries in the baseline deviation register

| Deviation | Why it is legitimate | Condition |
|---|---|---|
| `IPayloadSerializer` sorts dictionary keys (`DeterministicDictionaryConverter`) and object members (`SortObjectMembers`) | Persisted output must be byte-stable for hashing, and neither a .NET dictionary nor reflection gives a meaningful order | Applies only to our own types and maps. By decision 5 no data is a .NET dictionary, and a `JsonElement` is written raw (decision 3). Keys are written verbatim |
| Canonical hashing: `DefaultActivityDefinitionHasher`, `GitCanonicalJson` (Git reconciliation content hash), the design-store fingerprints (`EfDesignSupport.Fingerprint`) | A content hash should treat equal values as equal whatever their escaping or whitespace | The hash preimage is a canonical re-serialization with fixed options, never the stored or transmitted text. Never on the wire |
| Runtime EF Core durability options (frozen, see [serialization rule](../serialization.md)) | The durable format of suspended state changes only through migrations and golden fixtures | Adopting decision 3 there is a stamped format change made through that process. Until then, runtime state re-escapes data, and that exception is recorded |
| Secrets `json`/`jsonb` columns | The database normalizes the text, not our serializer | Secret payloads are not workflow data |

### 8. Explicit null is kept, and the value model must preserve it

The baseline keeps nulls (decision 2). That alone does not let readers tell an explicit `null` from a missing member,
because `JsonElement?` and `object?` merge them on read. Members that must distinguish them under D-10 need a
representation that keeps the difference. One example is a non-nullable `JsonElement` where `Undefined` means missing,
with a writer that omits it. For declared members, S-2 reuses the `Absent` and `ExplicitNull` states that
`ValueEnvelope` already has, rather than inventing a new representation. Spec S-2 chooses that representation. This ADR only requires that the serializer keep
whatever S-2 chooses.

## Consequences

- **Wire changes**, all of them fixes under matrix row 21:
  - Name keys that start with a capital keep their casing: instance outputs, expression parameters, input names, and
    any capitalized metadata or error key. Keys that start with a lowercase letter are unchanged.
  - Runtime and Design responses stop escaping non-ASCII characters, `<>&'` and `+`.
  - Values come back exactly as sent.

  Studio parses JSON and reads `errors` maps by value, so no Studio change is expected. Clients that compare text, or
  look up camelCased output names, see the difference.
- **Hashes and fingerprints.**
  - `GitCanonicalJson` hashes the compact `IPayloadSerializer` output, then recomputes the hash from files through a
    `JsonNode` re-emit with the default encoder. That only works while the serializer's output equals that re-emit.
  - Decisions 3 and 4 break that assumption, as does the design-store fingerprint over serializer output. Every
    definition read back from Git would hash as changed.
  - These hashes therefore move to a canonical preimage (decision 7) in the same change that introduces the baseline.
    Existing stored hashes are recomputed once.
- **Stored data.** Rows written today were normalized when first written and stay as they are. New rows keep the
  received text and explicit nulls. Readers are unaffected, because a missing member still reads as null. Exported
  artifacts gain `null` members, so their bytes differ from earlier exports.
- **Code.**
  - The option constructions and attributes become either "derived from the baseline" or register entries.
  - The `DictionaryKeyPolicy` settings and the two host-wide `Configure` calls are removed.
  - The unused Workbench `JsonSerializerOptionsCache` is deleted.
  - Two contract tests that assert camelCased keys are inverted, along with four assertions that pin
    `DictionaryKeyPolicy`. Their test helpers, `CompatibilityJson` and `AddDefinitionDeserializationTests` follow.
- **Docs.** [docs/serialization.md](../serialization.md) gains the baseline, the data rule and the deviation register.
  Its "HTTP boundary" exception still names FastEndpoints and is updated.
- **Constitution.** A shared baseline that all modules derive from is a new cross-cutting pattern. It goes through
  the framework constitution's §2.24.3 pattern gate together with this ADR.

## Alternatives considered

**Fix only the key policy.** Remove `DictionaryKeyPolicy` and stop the host-wide `Configure` calls. This is cheap and
fixes the renames. It leaves the re-escaping, the dropped nulls on write and the independent option sets, so the next
module could reintroduce any of them unnoticed. Rejected as the whole answer; it is the first step of the spec.

**One shared mutable options instance.** Simple to reach from anywhere, but any caller could change it for everyone.
That is exactly how the host-wide leak happened. Rejected in favour of a read-only baseline with explicit copies.

**Canonical output: normalize escaping and order everywhere.** Byte-stable output for every value, which is useful for
hashing. Rejected because it changes data the user sent. Canonicalization stays where it belongs: in the hash
preimage (decision 7).

**A hard ban on deviations.** Clear, but some deviations are legitimate (decision 7), and a ban would push them into
workarounds. Rejected in favour of a register: deviations stay possible, but each one is visible and justified.

## Required evidence for the implementing spec (S-9)

- **Architecture test.** It fails on an unregistered deviation of each kind it scans, passes with a register entry,
  and covers the composed host HTTP options and resolver overlap.
- **Runtime round trip.** An output `OrderTotal` holding
  `{"CustomerId":1,"Name":"Ren\u00e9","N":1.0}` comes back with the key `OrderTotal` and the value text
  byte-identical.
- **Design round trip.** A variable default with capitalized keys, escaped characters, inner whitespace and `1.0`
  comes back byte-identical.
- **Changed values.** A value a script changed is written in the default format of decision 4.
- **Hash stability.**
  - A Git round trip (write files, read them back) keeps every content hash.
  - Design-store fingerprints stay valid.
  - Stored hashes are recomputed once.
- **Nulls.** `IPayloadSerializer` keeps explicit nulls, and existing rows still read the same.
- **.NET-typed values round trip.** A .NET-typed activity result bound to a .NET-typed input of the same type arrives
  with every property set. Test this first: it reproduces today's suspected loss (Context).
- **Activity input matching and dates.** `{"customerName":…}` fills `CustomerName`; `id` and `ID` in one object fail
  with a diagnostic; `"2026-10-28T09:15:00+05:00"` into `DateTime` gives the same instant on a UTC and a non-UTC server,
  at run time and in publish-time literals.
- **Numbers.** A 20-digit integer and a 29-digit decimal pass through ForEach and an activity input unchanged.
- **Content type.** Every response carrying raw or relaxed-encoded data has `Content-Type: application/json` and
  `X-Content-Type-Options: nosniff`.
- **S-9 intake checks** on the epic board:
  - hash, digest and byte-comparison consumers;
  - producers of metadata and error keys;
  - Studio's use of validation-error keys;
  - external clients from the FastEndpoints era;
  - whether NativeEndpoints binds and writes with the group context;
  - the encoder the composed host options actually use;
  - Studio preference and structured-log responses after the host options change;
  - every data-to-.NET conversion on the value path found by the 2026-10-29 search (listed on this PR), each moved to
    the baseline copy or registered.

## Open questions

Answers so far: @FransVanEk answered all six on 2026-10-29 (review of this PR). Where his answer is what this ADR
proposes, @j03y-nxxbz agrees, so questions 1 to 5 wait only for @sfmskywalker:
1 bytes; 2 relaxed; 3 yes; 4 yes, with S-2 reusing `ValueEnvelope`'s `Absent` and `ExplicitNull` (now in decision 8);
5 separate. Question 6 is still open (see the end of the question).

1. **Byte-identical or value-identical.** Is decision 3 a promise of identical bytes for untouched values, including
   the sender's whitespace inside a value, or only of the same JSON value? This ADR proposes bytes.
2. **Encoder.** Relaxed (proposed) or the stricter default encoder for decision 4?
3. **Strength.** Is "register entry, or an ADR when data is touched" the right bar for deviations?
4. **Null representation.** Is it right to leave the representation to S-2, with this ADR only requiring that it be
   kept?
5. **Placement.** Keep this as a new ADR, or fold it into ADR 0035 or ADR 0046? A-1 now proposes an ADR 0046
   amendment for the value model and the boundary adapters. This ADR proposes to stay separate: 0046 says what each
   adapter accepts, produces and loses, and 0091 is the one serializer they all use. Each refers to the other, and
   rules that appear in both (no key policy on data, exact member reads) are stated once, here.
6. **Runtime durable state.** Should the frozen runtime durability format adopt decision 3 through a stamped
   migration, or keep re-escaping as a recorded exception? @FransVanEk: a stamped migration now, since there is no
   release yet and suspended state is the most important store. Not yet decided: whether S-9 includes that migration
   or the exception in decision 7 stays until a later spec.
