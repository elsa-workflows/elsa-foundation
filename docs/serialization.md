# Serialization rule

> All **domain-payload** JSON serialization and deserialization goes through `IPayloadSerializer`
> (`Elsa.Serialization.Core`). Do not hand-roll `System.Text.Json.JsonSerializer` / `JsonDocument`
> for data that another component reads.
>
> **Knowledge role:** focused reference. Link here from gates, specs, and skills instead of
> restating the rule.

## Why

`IPayloadSerializer` ([`JsonPayloadSerializer`](../src/Elsa/Serialization/SystemText/Services/JsonPayloadSerializer.cs))
is the single, configured contract for domain payloads: it applies the agreed naming policy
(camelCase, case-insensitive on read) and the registry of converters contributed at startup. When one
component serializes with it and another deserializes with raw `JsonSerializer` defaults, the round-trip
silently breaks (e.g. casing mismatches). Routing every domain payload through the one contract keeps
write and read symmetrical.

## What it covers

Anything persisted or handed across a component boundary as JSON:

- Entity `*Source` shadow columns (e.g. `InputsSource`, `StateSource`) — the saving/loading handlers
  already use `IPayloadSerializer`.
- The opaque activity `DescriptorPayload` — serialized on save and rehydrated on load via
  `IPayloadSerializer`; the owning runtime constructor also deserializes the descriptor through it.
- Reconciliation / import models that carry serialized values.

Inject `IPayloadSerializer` and use `Serialize` / `SerializeToElement` / `Deserialize<T>` rather than
touching `JsonSerializer` directly.

**Type identity is alias-based, everywhere.** A workflow Variable/Input/Output persists its type as a
`TypeReference { Alias, CollectionKind }` (plain data, serialized natively); the compiled-Type path
(`TypeJsonConverter`) is alias-only too; and a CLR activity's construction descriptor is a
`ClrActivityDescriptor { TypeAlias }`. Every alias resolves to a CLR type via `IWellKnownTypeRegistry`
under the shared `TypeAliasConvention` (a reserved bare alias for BCL primitives, otherwise the dotted
`Type.FullName`). No persisted shape carries an assembly name or version — the former decomposed
`TypeInformation` (namespace/assembly/version) has been removed, so a package bump never breaks
resolution or construction.

## Sanctioned exceptions

These deliberately do **not** use `IPayloadSerializer`, because the JSON never crosses a domain
boundary or the use needs options the payload serializer can't provide:

- **EF Core `ValueConverter`s** — a converter both serializes and deserializes a column within the
  persistence layer; nobody else depends on its format (e.g. the layout/validation converters).
- **HTTP boundary** — FastEndpoints request/response and `Elsa.Http` content factories serialize at the
  transport edge with their own options.
- **Expression / scripting** — JavaScript/Liquid helpers serialize within an expression's execution
  scope.
- **Custom `JsonConverter`s** — they participate in the `System.Text.Json` pipeline by definition.
- **The Groundwork runtime persistence bridge**
  ([`IGroundworkRuntimeDocumentSerializer`](../src/Elsa/Persistence/Groundwork/Serialization/IGroundworkRuntimeDocumentSerializer.cs))
  — the bridge both writes and reads its runtime state documents entirely within the persistence
  layer; no other component parses their `ContentJson`. It owns a frozen `JsonSerializerOptions`
  deliberately independent of `IPayloadSerializer`: this is the *durability* format of suspended
  workflow state, frozen by a golden-fixture suite and evolved only through explicit per-kind version
  bumps with upcasters (see **Schema evolution** below). Adopting the mutable, startup-contributed
  converter registry of `IPayloadSerializer` would itself be an unstamped format change — the exact
  hazard this bridge's versioning exists to eliminate. All bridge serialization goes through the one
  sealed serializer service, never raw `JsonSerializer`.
- **The reconciliation content hasher**
  ([`DefaultActivityDefinitionHasher`](../src/Elsa/Activities/Design/Persistence/Core/Services/DefaultActivityDefinitionHasher.cs))
  — it needs a canonical, sorted-key serialization that `IPayloadSerializer` does not produce, and only
  the SHA-256 of that JSON is ever persisted (the JSON itself is never read back).

## Schema evolution (Groundwork runtime bridge)

Runtime state persisted by the Groundwork bridge (bookmarks, executables, execution/scheduler/
operational/control-plane/incident/durable-value state, checkpoint commits, the post-commit outbox)
must be able to evolve without silently breaking already-suspended workflows. The contract:

- **Per-kind integer versions, hosted in the envelope.** Each of the 12 runtime document kinds has a
  current integer version declared in
  [`ElsaRuntimeDocumentVersions`](../src/Elsa/Persistence/Groundwork/Serialization/ElsaRuntimeDocumentVersions.cs)
  (all `1` today). The version is stamped into the Groundwork **envelope** `SchemaVersion` field on
  every write — never inside the content JSON and never on the domain state records, keeping
  persistence concerns out of `WorkflowExecutionState` et al. The legacy manifest-wide stamp
  `"1.0.0"` (everything written before versioning) parses as version `1` for every kind.
- **Loud enforcement on read.** The serializer parses the stamp: the current version deserializes
  directly; an older version is upcasted step-by-step to the current version first; an unrecognized,
  non-positive, or future version throws
  [`GroundworkRuntimeDocumentVersionException`](../src/Elsa/Persistence/Groundwork/Exceptions/GroundworkRuntimeDocumentVersionException.cs)
  naming the kind, the found version, and the supported version — never a silent default-valued hydrate.
- **Upcasters are a fan-in contribution.** Register an
  [`IGroundworkRuntimeDocumentUpcaster`](../src/Elsa/Persistence/Groundwork/Serialization/IGroundworkRuntimeDocumentUpcaster.cs)
  per `(DocumentKind, FromVersion)` step; it rewrites content JSON from `FromVersion` to
  `FromVersion + 1`. The registry validates the chain **eagerly at construction** (duplicate steps,
  gaps, steps at/beyond a kind's current version, and incomplete known-kind chains all fail at
  startup, not first read). Upcasting is lazy and read-time only; the write path always stamps the
  current version, so documents re-baseline naturally on their next save.
- **A CI fixture gate freezes the format.** Committed golden fixtures
  (`tests/Elsa/Persistence/Groundwork/Tests/Fixtures/v<n>/<kind>.json`) capture the exact content JSON
  each store writes for a canonical instance. A drift test re-serializes the canonical instance and
  compares it **semantically** (parsed and normalized, so incidental formatting differences are
  ignored) to the committed current-version fixture; a compatibility test loads every historical
  fixture through the real store read path. Any state-record field add/rename/remove/retype without a
  version bump fails the drift test.

### How to change a persisted runtime state record

In the same change:

1. Bump that kind's version in `ElsaRuntimeDocumentVersions`.
2. Register an `IGroundworkRuntimeDocumentUpcaster` from the previous version to the new one.
3. Add a golden fixture for the new version (`Fixtures/v<new>/<kind>.json`) and **keep** the historical
   fixtures so the compatibility test proves old documents still load.
