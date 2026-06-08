# Serialization rule

> All **domain-payload** JSON serialization and deserialization goes through `IPayloadSerializer`
> (`Elsa.Serialization.Core`). Do not hand-roll `System.Text.Json.JsonSerializer` / `JsonDocument`
> for data that another component reads.

## Why

`IPayloadSerializer` ([`JsonPayloadSerializer`](../src/Elsa.Serialization/Services/JsonPayloadSerializer.cs))
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
- **The reconciliation content hasher**
  ([`DefaultActivityDefinitionHasher`](../src/Elsa.Activities.Design.Persistence.Core/Services/DefaultActivityDefinitionHasher.cs))
  — it needs a canonical, sorted-key serialization that `IPayloadSerializer` does not produce, and only
  the SHA-256 of that JSON is ever persisted (the JSON itself is never read back).
