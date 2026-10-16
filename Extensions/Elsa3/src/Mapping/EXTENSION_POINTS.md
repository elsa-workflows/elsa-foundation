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
one policy. A host that replaces only one of the two converters fails shell activation with
`PartialElsa3ConverterReplacementException` whenever the registrations show it, in two cases:

- a host `IElsa3NodeConverter` beside the default `IElsa3DefinitionConverter`;
- a host `IElsa3DefinitionConverter` whose type does not implement `IElsa3NodeConverter`, beside the default node
  converter.

A host definition converter registered through a factory hides its type, so the check cannot tell whether it converts
nodes. It passes activation, and if it does not implement `IElsa3NodeConverter`, resolving the default node converter
throws `InvalidOperationException`.

Replace both: implement both contracts on one class, or register a node converter alongside the definition converter.

### `IElsa3DefinitionConverter` *(Replacement contract — `Elsa3.Mapping`)*

- **Signature:** `ValueTask<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(Elsa3ConversionRequest, CancellationToken)`.
- **Purpose:** Converts one Elsa 3 definition into Elsa 4 state plus diagnostics. The result carries a reusable
  workflow's output mappings, boundary outcomes and outcome mappings. It never throws for input or catalog content;
  only cancellation escapes.
- **Default implementation:** `Elsa3DefinitionConverter` (scoped). It also implements `IElsa3NodeConverter`.
- **When to override:** a host that needs a different conversion policy as a whole. Map changes belong in
  `IElsa3ActivityMap`.
- **Depends on it:** `Elsa3WorkflowDefinitionImporter`, `Elsa3ReusableActivityImportMaterializer` and start-time
  reconciliation (spec 235). Flowchart graph conversion (spec 239) runs inside it.

### `IElsa3NodeConverter` *(Replacement contract — `Elsa3.Mapping`)*

- **Signature:**
  - `ValueTask<Elsa3MigrationResult<Elsa3NodeConversionContext>> PrepareAsync(Elsa3ConversionRequest, CancellationToken)`;
  - `ValueTask<Elsa3MigrationResult<ActivityNode>> ConvertNodeAsync(Elsa3NodeConversionContext, Elsa3Activity, CancellationToken)`.
- **Purpose:** Converts nodes one at a time, from a definition prepared as a whole.
  - `PrepareAsync` normalizes and lowers the whole definition, exactly as `IElsa3DefinitionConverter.ConvertAsync`
    does, including flowchart graph conversion (spec 239). A graph-level node a later stage owns would be deferred
    rather than stopping the rest, listed in the context's `Deferred`; no built-in shape is deferred since spec 239.
  - `ConvertNodeAsync` maps one node and its descendants from that lowered result; a flowchart arrives with its
    connections and start node. It refuses a `FlowFork`, `FlowJoin` or `Start` node (`ELS3-ACTIVITY-UNSUPPORTED-MODE`):
    those became connections or the start node of their flowchart and have no node of their own.
  - One rule for both paths: an Elsa 3 expression is lowered or refused the same way whichever converter reaches it.
  - `Elsa3NodeConversionContext` is abstract, with a protected `(Request, Deferred)` constructor: each node converter
    derives its own context to carry what its `PrepareAsync` computed, and its `ConvertNodeAsync` accepts only the
    contexts it created. The default converter's context holds the lowered plan internally, and it throws
    `ArgumentException` for a context another converter created.
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
