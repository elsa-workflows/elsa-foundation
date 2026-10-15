# Elsa3.Mapping Extension Points

## Replacement/import boundary contracts

### `IElsa3WorkflowDefinitionImporter` *(Feature contract — `Elsa3.Mapping`)*

- **Kind:** Replacement/import boundary contract.
- **Purpose:** Imports ordinary authored definitions and exposes collection-aware analyze/apply for reusable Elsa 3 workflows. A reusable workflow submitted through the single-definition path is rejected with guidance to use the collection boundary.
- **Default implementation:** `Elsa3WorkflowDefinitionImporter`.
- **Runtime boundary:** This is a Design-side migration/import surface. `Elsa.Workflows.Runtime.*` projects must not consume it.

### Replacing a mapping contract

`IElsa3DefinitionConverter`, `IElsa3NodeConverter` and `IElsa3ActivityMap` are replacement contracts: at most one
implementation each per container (framework §2.6.2). `Elsa3MappingFeature` registers its defaults with `TryAdd`, so a
host replaces one by registering its own **before** the feature, or with `services.Replace(...)`. Composing a second
registration of any of them fails shell activation with `MultipleElsa3MappingRegistrationsException`, which names every
registration. The default `IElsa3NodeConverter` is whichever `IElsa3DefinitionConverter` is composed, so the two follow
one policy; a host whose definition converter does not also implement `IElsa3NodeConverter` registers a node converter
as well, or resolving one fails with a message saying so.

### `IElsa3DefinitionConverter` *(Replacement contract — `Elsa3.Mapping`)*

- **Signature:** `ValueTask<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(Elsa3ConversionRequest, CancellationToken)`.
- **Purpose:** Converts one Elsa 3 definition into Elsa 4 state plus diagnostics. The result carries a reusable
  workflow's output mappings, boundary outcomes and outcome mappings. It never throws for input or catalog content;
  only cancellation escapes.
- **Default implementation:** `Elsa3DefinitionConverter` (scoped). It also implements `IElsa3NodeConverter`.
- **When to override:** a host that needs a different conversion policy as a whole. Map changes belong in
  `IElsa3ActivityMap`.
- **Depends on it:** `Elsa3WorkflowDefinitionImporter`, `Elsa3ReusableActivityImportMaterializer`, graph conversion
  (#2705) and start-time reconciliation (spec 235).

### `IElsa3NodeConverter` *(Replacement contract — `Elsa3.Mapping`)*

- **Signature:** `ValueTask<Elsa3MigrationResult<ActivityNode>> ConvertNodeAsync(Elsa3NodeConversionContext, Elsa3Activity, CancellationToken)`.
- **Purpose:** Converts one node and its non-graph descendants, for graph conversion (#2705) to call per Flowchart
  child. Definition-wide value-flow lowering is the definition converter's job.
- **Default implementation:** `Elsa3DefinitionConverter` (the same scoped instance).

### `IElsa3ActivityMap` *(Replacement contract — `Elsa3.Mapping`)*

- **Signature:** `IReadOnlyList<Elsa3ActivityMapEntry> Entries { get; }`, `Elsa3ActivityMapEntry? Find(string elsa3TypeName)`.
- **Purpose:** The Elsa 3 → Elsa 4 activity map: one entry per Elsa 3 type, giving its target and the rule for every
  member.
- **Default implementation:** `Elsa3BuiltInActivityMap` (singleton; the decision record `member-map.md` as data).
- **When to override:** to map additional Elsa 3 types centrally. For a single rebuilt custom activity, prefer
  `[ReplacesActivityType]` on the Elsa 4 activity. Any change alters converter output (spec 235's converter revision
  rule).

### `IReusableActivityImportMaterializer` contribution

- **Implementation:** `Elsa3ReusableActivityImportMaterializer`.
- **Purpose:** maps exact planned reusable references to deterministic activity-version ids, creates graph-provider catalog material, and preserves each original workflow definition/version identity as a direct-start wrapper.
