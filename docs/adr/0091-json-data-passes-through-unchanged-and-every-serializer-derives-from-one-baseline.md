# JSON Data Passes Through Unchanged And Every Serializer Derives From One Baseline

Status: proposed (2026-10-27)

It answers whether JSON property names keep their exact casing everywhere.

Related:

- Builds on [ADR 0035](0035-serialization-unifies-on-the-alias-registry-and-retires-open-object-polymorphism.md)
  (one type story, designer bags as opaque JSON).
- Builds on [ADR 0036](0036-dynamic-any-expression-values-materialize-as-jsonnode-with-per-engine-adapters.md)
  (dynamic values as `JsonNode`).
- Extends the [serialization rule](../serialization.md): `IPayloadSerializer` stays the contract for domain payloads,
  and its options derive from the baseline defined here.

Not decided here:

- The value model and conversion, in
  [ADR 0046](0046-output-binding-coercion-uses-pinned-value-representations.md) and its amendment for the value model.
  That amendment defines the adapter at each boundary: what it accepts, produces and loses, including the rules for
  dates, numbers in scripts and property matching at activity inputs. This ADR is the serializer those adapters use,
  and it must agree with them (decisions 2 and 5). The two stay separate ADRs that refer to each other; rules that
  appear in both (no key policy on data, the default member matching and its scoped conversion contracts) are stated
  once, here.
- The representation of null versus missing, in spec S-2 under decision D-10.

This ADR only constrains how those serialize.

## Context

Workflow data is JSON that users author or send: variable defaults, workflow inputs and outputs, literals, activity
results, HTTP bodies and trigger payloads. Elsa does not own its meaning. Serializer settings have changed such data
before, through renamed keys, changed escaping, and the type wrappers that ADR 0035 removed. Data integrity requires
that Elsa write data exactly as it received it.

An analysis of `main` found:

- **No workflow value is renamed today.** Every value on the API paths it traced is a `JsonElement` (or a string)
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
- **Every module builds its own options.** Across all source roots (Essentials, Extensions and Studio's C# code) a
  later survey counted:
  - 187 option constructions in 175 files, 129 of them in Essentials;
  - 32 `[JsonSourceGenerationOptions]` attributes;
  - several uses of `JsonSerializerOptions.Web` and `.Default`;
  - about 267 serializer calls with neither options nor type info.

  Nothing states which settings may differ, or why.

A follow-up search over the value path (`main` at `6432ed240`) found that this list covered the
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
  becomes `1`, and `1e400` faults the loop.
- **No `DateTime` converter exists.** Date handling is the System.Text.Json default, or `Convert.ChangeType` for
  publish-time literals, which pins the publishing server's offset into the executable.

## Terms

- **Data**: a JSON value Elsa carries but does not own: workflow inputs, outputs, variable values and defaults,
  literals, activity inputs and results, payloads and bodies.
- **Own contract**: a JSON shape Elsa defines, such as DTOs, views, problem details and persisted envelopes.
- **User type**: a .NET type that a user or extension defines and that data is converted into, such as an activity
  input or a function parameter.
- **Conversion contract**: the naming, matching and member-mapping settings used to convert data into one user type,
  or at one conversion boundary. It is selected for that type or boundary, never through host-wide options.
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
- **Existing hash inputs are the exception.** A serializer whose output feeds an existing hash or fingerprint keeps
  its current options, frozen, and is listed in the deviation register (decision 7). A change to the baseline never
  silently changes an artifact's identity or a stored hash.

### 2. The baseline transforms no data

The baseline:

- sets no `DictionaryKeyPolicy`, so name keys and data keys are written verbatim;
- sets no `NumberHandling` (numbers are never read from or written as strings);
- has no converter that changes or reorders data;
- keeps nulls (no `WhenWritingNull` or `WhenWritingDefault`);
- camelCases only the member names of Elsa's own C# types (`PropertyNamingPolicy = CamelCase`). That shapes our own
  contract, not data.

**The baseline's naming and matching are the convention for Elsa's own contracts.** Their member-name reads are exact
(`PropertyNameCaseInsensitive = false`, unlike today's Web default), and they keep the camelCase naming policy in both
directions. That convention is not imposed on user types.

**Conversion into a user type uses a scoped conversion contract.**
- Elsa documents a default conversion contract and offers scoped options for a user type or a conversion boundary:
  the naming policy (for example CLR names, camelCase or snake_case), exact or case-insensitive matching, and explicit
  member mappings. A conversion never changes host-wide serializer options.
- The default matching is exact (case-sensitive). No read guesses which member a key was meant for: a JSON member
  fills a .NET member only when its name is exactly the name the effective contract gives that member, through its
  naming policy, an explicit mapping or an annotation (`[JsonPropertyName("customer_name")]`). A scoped contract may
  explicitly select case-insensitive matching instead.
- The default naming policy for user types is stated by that contract, not inferred from Elsa's own wire format. The
  implementing spec (S-9) states it, together with the import-rewrite details below.
- The selected conversion behaviour is explicit and stable in the published conversion contract and its version.
- Matching never changes data. It only decides which .NET member a JSON member fills; the stored JSON keeps its keys.
  Reading a typed copy does not rewrite the stored JSON. Writing a narrower typed copy back as a replacement follows
  the dropped-member rule of the ADR 0090 amendment.

**An unmatched member gives a warning, judged against the effective contract.**
- A JSON member is unmatched only when the selected conversion contract, with its naming policy, case sensitivity and
  explicit mappings, gives it no member. A member handled through a mapping or an extension-data member is not
  reported as unmatched.
- By default the conversion records a warning that names each unmatched key and the target type, and continues, so a
  key is never dropped silently. It is a warning, not a refusal: the step runs, and the stored JSON keeps the key. So
  System.Text.Json's `JsonUnmappedMemberHandling.Disallow`, which refuses, is not the default; how the warning is
  detected is left to S-9, and it honours the effective contract.
- A scoped contract may explicitly select stricter handling of unmatched members. A required member that is missing
  still fails under D-10.
- Under exact matching two keys cannot both fill one member. With `id` and `ID` in one object, the key that matches
  fills the member and the other gets the unmatched-member warning. A contract that selects case-insensitive matching
  states how it treats such a pair.

**Elsa 3 conversion rewrites case-only differences.** Elsa 3 read member names case-insensitively. The converter
rewrites a literal key that differs from the exact name in the effective contract only in case to that name, which
keeps Elsa 3's behaviour (D-13, faithful). It refuses with guidance only when the rewrite is ambiguous, for example
`Name` and `NAME` in one object. Data that only arrives at run time gets the warning above.

This replaces the case-insensitive reads this ADR first proposed.

### 3. Data passes through as the exact text received

The baseline writes a `JsonElement` as its original text, using `WriteRawValue(GetRawText(),
skipInputValidation: true)`. Skipping validation is safe because a `JsonElement` is always valid JSON, and it avoids
a re-parse per value.

- A value an endpoint accepted is stored and returned byte-identical: escaping, number text, key order, casing, and
  the whitespace inside the value. This holds on API, persistence and export paths that carry the value untouched.
- The guarantee covers the carried JSON value, not the enclosing HTTP message or export document.
- An indented writer does not re-indent such a value.
- Validating that value is the job of the endpoint's request contract.
- A value that a script or an activity changes is a new value and is written in the default format (decision 4). Its
  meaning is kept and any loss of precision is explicit, but its formatting may change: members inside an edited value
  that the edit left alone do not keep their original formatting on their own account.
- A value that passes through a script unchanged can change today: the adapter converts the whole input into
  JavaScript values up front, and Jint holds every number as a double, so an integer beyond ±2^53 or a long decimal
  loses digits (`IsolatedJintEngine.cs:168-169`). That loss is the JavaScript adapter's, not the
  serializer's. The ADR 0046 amendment fixes
  it there with copy, compare, patch (V9): only the paths the script changed are written, so values the script did
  not change keep their original text. Values the script changes or computes are new values and come back in the
  default format.
- Because stored text keeps whatever notation arrived, two equal values can have different text. Nothing that decides
  whether two values are equal compares their stored or freshly serialized text: it uses a defined semantic comparison
  of the values. That is the no-op test of the ADR 0090 amendment (C4) and the value digest of ADR 0092, and it covers
  the stores that today compare a fresh serialization with stored text exactly. Hashing is a separate concern: a hash
  uses its own defined input (decision 7).

### 4. One default output format

Everything not covered by decision 3 is written with one encoder for every API and store:

- Elsa's own strings and member names.
- Values that were created or changed.

The proposed encoder is `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`:

- Publishing and Activities Design already use it.
- It matches what decision 3 lets through: raw text bypasses any encoder.
- Elsa's own API responses are `application/json` with `X-Content-Type-Options: nosniff`, so the HTML-sensitive
  characters this encoder leaves unescaped are harmless there: such a response is never rendered as HTML. The
  implementing spec proves this (see Required evidence).

That reasoning covers Elsa's own APIs only. A workflow-authored HTTP response uses the content type its author
selected, so explicitly authored HTML stays `text/html`. When the author selects none, a text body is `text/plain`
and any other body is `application/json`. Its body serialization and delivery follow that activity's contract.

Today the Runtime and Design owner contexts use the default encoder. The same value therefore leaves them with
different text than it leaves Publishing.

### 5. Data is JSON at every boundary

At API, persistence and messaging boundaries, data is held as `JsonElement` or `JsonNode`. It is never a .NET
dictionary, an `ExpandoObject` or a POCO. The analysis in Context found no data held otherwise on the API paths it traced; this decision
makes it a rule. Data is deserialized into .NET types only inside an activity or a function that asks for it
(ADR 0046).

That deserialization is a boundary too. It uses the conversion contract for the target type (decision 2), in both
directions: a copy of the baseline whose naming and matching that contract selects, with the protected settings of
decision 6 unchanged. Today it does not (see Context): activity inputs, expression-result coercion, publish-time
literals and stateful activity state use System.Text.Json's defaults, while activity results are written with Web
options. Every conversion between data and a given .NET type, in or out, uses the same effective contract, so a value
written by one activity reads back the same in the next. The ADR 0046 amendment's adapter rules for these conversions
(member matching with a warning for unmatched members, a `DateTime` read as UTC) are applied through that contract,
not as special cases at each call site. A number is never narrowed to `long` or `double` on the way, unless the target
type asks for it.

### 6. Modules never change host-wide options; deviations are a red flag, not a ban

- **Host options belong to the host.** A module configures only its own context. Only the host sets the host's
  naming, key policy, encoder, null handling and converters, from the baseline.
- **Resolvers cover owned types only.** A module may add a type-info resolver to the host options, as Workflows Design
  and Workflows Runtime do. Because a resolver inserted first can shadow others, it may describe only types the module
  owns.
- **Protected settings** are `DictionaryKeyPolicy`, `NumberHandling`, `DefaultIgnoreCondition`, `Encoder`, and
  converters or type-info modifiers that change or reorder data.
- **An architecture test finds deviations.** It combines a source scan and reflection:
  - The scan covers every source root (Essentials, Extensions and Studio's C# code) for `new JsonSerializerOptions`,
    `JsonSerializerOptions.Web`/`.Default`, `[JsonSourceGenerationOptions]` settings, and `JsonSerializer` calls
    without options or type info.
  - Reflection covers each module's constructed serializer contexts and the composed host HTTP options. It also
    checks that host resolvers do not overlap.

  The test fails on any deviation that is not listed in the **baseline deviation register**. That is a new
  "Baseline deviations" section in [docs/serialization.md](../serialization.md), listing the file, the setting, the
  reason and the approving ADR or issue. It sits beside the existing "Approved exceptions", which lists code that does
  not use `IPayloadSerializer` at all.
- **Enforcement is staged through a legacy list that may only shrink.**
  - Every conversion path for workflow data is fixed in the implementing spec, using the baseline or the applicable
    conversion contract. Workflow-data paths are never deferred to the legacy list.
  - Existing option-less calls that handle only Elsa's own contracts are tracked in a legacy list. An entry leaves
    the list when its call is migrated; no entry is added. A new call that is neither derived from the baseline, nor
    in the register, nor on the list fails the test.
  - The legacy list is not the register. It holds calls still to migrate, not justified deviations.
- **The bar depends on what is touched.** A deviation that touches **data** at an API boundary needs an ADR as well as
  its register entry. A deviation in an internal serializer of our own contracts needs a register entry with its
  reason. This follows the platform principle that Elsa gives guardrails and diagnostics, not restrictions.
- **A user's conversion contract is configuration, not a deviation.** Selecting the supported scoped naming, matching
  or mapping options for a user type (decision 2) needs neither an ADR nor a register entry.

### 7. Expected entries in the baseline deviation register

| Deviation | Why it is legitimate | Condition |
|---|---|---|
| `IPayloadSerializer` sorts dictionary keys (`DeterministicDictionaryConverter`) and object members (`SortObjectMembers`) | Persisted output must be byte-stable for hashing, and neither a .NET dictionary nor reflection gives a meaningful order | Applies only to our own types and maps. By decision 5 no data is a .NET dictionary, and a `JsonElement` is written raw (decision 3). Keys are written verbatim |
| Hash inputs: every existing serializer whose output feeds a hash or fingerprint, for example the artifact hash and the activity contract fingerprint it includes, `DefaultActivityDefinitionHasher`, `GitCanonicalJson` (Git reconciliation content hash), the design-store fingerprints (`EfDesignSupport.Fingerprint`) and hashes taken directly over serialized text | A baseline change must not silently change an artifact's identity or a stored hash | Each keeps its current options, frozen, and its input contract is recorded in the register. Never on the wire. Changing a hash input is a separate, explicit decision that assesses its identity, versioning and migration consequences |
| Runtime EF Core durability options (frozen, see [serialization rule](../serialization.md)) | The durable format of suspended state changes only through compatible writer changes or migrations and golden fixtures | Decision 3 applies there too. A writer change that is shown to be compatible and to keep the meaning, so old and new code read old and new rows with the same meaning, needs no format-version bump or migration. S-9 shows that with fixed serialized examples and compatibility checks; if that proof fails, S-9 makes the change a stamped format change through migrations and golden fixtures. A change to the meaning of rows, or to reader and writer compatibility, always takes that stamped path. Before 4.0 a stamped change means regenerated migrations and updated golden fixtures, not an upcaster |
| Secrets `json`/`jsonb` columns | The database normalizes the text, not our serializer | Secret payloads are not workflow data |

### 8. Explicit null is kept, and the value model must preserve it

The baseline keeps nulls (decision 2). That alone does not let readers tell an explicit `null` from a missing member,
because `JsonElement?` and `object?` merge them on read. Members that must distinguish them under D-10 need a
representation that keeps the difference. One example is a non-nullable `JsonElement` where `Undefined` means missing,
with a writer that omits it. For declared members, S-2 reuses the `Absent` and `ExplicitNull` states that
`ValueEnvelope` already has, rather than inventing a new representation. Spec S-2 chooses that representation. This ADR only requires that the serializer keep
whatever S-2 chooses.

## Consequences

- **Wire changes**, all of them fixes:
  - Name keys that start with a capital keep their casing: instance outputs, expression parameters, input names, and
    any capitalized metadata or error key. Keys that start with a lowercase letter are unchanged.
  - Runtime and Design responses stop escaping non-ASCII characters, `<>&'` and `+`.
  - Values come back exactly as sent.

  Studio parses JSON and reads `errors` maps by value, so no Studio change is expected. Clients that compare text, or
  look up camelCased output names, see the difference.
- **Hashes and fingerprints.**
  - Existing hash inputs keep their frozen options and are listed in the register (decisions 1 and 7), so the
    baseline moves no artifact identity, content hash or fingerprint, and no stored hash is recomputed.
  - Equality checks that compare serialized text move to a semantic comparison of values (decision 3).
  - Changing a hash input later is a separate, explicit decision with its identity, versioning and migration
    consequences assessed.
- **Stored data.** Rows written today were normalized when first written and stay as they are. New rows keep the
  received text and explicit nulls. Readers are unaffected, because a missing member still reads as null. Exported
  artifacts gain `null` members, so their bytes differ from earlier exports.
- **Code.**
  - The option constructions and attributes become either "derived from the baseline" or register entries. Existing
    option-less calls on Elsa's own contracts may instead stay on the legacy list until migrated (decision 6).
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
  and covers the composed host HTTP options and resolver overlap. It fails on a new option-less call that is not on
  the legacy list, passes when an entry is removed from the list after its call is migrated, and the list holds no
  workflow-data path.
- **Runtime round trip.** An output `OrderTotal` holding
  `{"CustomerId":1,"Name":"Ren\u00e9","N":1.0}` comes back with the key `OrderTotal` and the value text
  byte-identical.
- **Design round trip.** A variable default with capitalized keys, escaped characters, inner whitespace and `1.0`
  comes back byte-identical.
- **Changed values.** A value a script changed is written in the default format of decision 4.
- **Hash stability.**
  - A Git round trip (write files, read them back) keeps every content hash.
  - Design-store fingerprints stay valid.
  - Every registered hash input gives the same hash for the same input before and after the baseline is introduced.
  - Each direct-text hash and each check that compares serialized text is listed, and each text comparison that
    decides equality of values uses the semantic comparison.
- **Runtime durable state.** Fixed serialized examples show that old and new code read old and new rows with the same
  meaning. If they do not, the change is a stamped format change (decision 7).
- **Nulls.** `IPayloadSerializer` keeps explicit nulls, and existing rows still read the same.
- **.NET-typed values round trip.** A .NET-typed activity result bound to a .NET-typed input of the same type arrives
  with every property set. Test this first: it reproduces today's suspected loss (Context).
- **Activity input matching and dates.**
  - Under the default contract, a key spelled exactly as the contract's name for `CustomerName` fills it; a key that
    differs only in case does not, and gives the unmatched-member warning while the step continues.
  - `id` and `ID` in one object: one fills the member and the other gives the warning.
  - A scoped contract that selects case-insensitive matching or an explicit mapping fills the member without a
    warning, and a member handled through a mapping or extension data is not reported.
  - A scoped contract that selects strict handling refuses an unmatched member; a missing required member fails.
  - Selecting a scoped contract changes no host-wide option.
  - `"2026-10-28T09:15:00+05:00"` into `DateTime` gives the same instant on a UTC and a non-UTC server, at run time
    and in publish-time literals.
- **Numbers.** A 20-digit integer and a 29-digit decimal pass through ForEach and an activity input unchanged.
- **Content type.**
  - Every Elsa API response carrying raw or relaxed-encoded data has `Content-Type: application/json` and
    `X-Content-Type-Options: nosniff`.
  - A workflow-authored response keeps the content type its author selected, for example `text/html`. With none
    selected, a text body such as `Hello` is `text/plain` and a JSON object is `application/json`.
- **S-9 intake checks:**
  - hash, digest and byte-comparison consumers;
  - producers of metadata and error keys;
  - Studio's use of validation-error keys;
  - external clients from the FastEndpoints era;
  - whether NativeEndpoints binds and writes with the group context;
  - the encoder the composed host options actually use;
  - Studio preference and structured-log responses after the host options change;
  - every data-to-.NET conversion on the value path found by the follow-up search (see Context), each moved to
    the baseline or its conversion contract, or registered.
