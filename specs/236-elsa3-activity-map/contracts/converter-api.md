# Contract: converter API (spec 236)

The public surface that #2705 (Flowchart importer) and spec 235 (start-time reconciliation) build on. Assembly:
`Elsa3.Mapping`. Registered by `Elsa3MappingFeature` (scoped).

## `IElsa3DefinitionConverter` (FR-015)

```csharp
public interface IElsa3DefinitionConverter
{
    /// Converts one Elsa 3 workflow definition. Never throws for input- or catalog-caused failures (FR-012):
    /// they are Error diagnostics on a failed result. Warnings ride on a successful result (FR-004b).
    /// Throws only OperationCanceledException and ArgumentNullException.
    ValueTask<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(
        Elsa3ConversionRequest request,
        CancellationToken cancellationToken = default);
}
```

**Guarantees**:

- `request.Definition` is never mutated; the converter clones it.
- Output and diagnostics are deterministic for the same input and catalog content (FR-020).
- Diagnostics are complete and ordered by (JSON path, code) (FR-014).
- `IsReusable = true` applies the reusable-workflow rules: `SetOutput` → output variables plus `OutputMappings`
  (FR-004e). `Complete`/`End` boundary outcomes wait for #2750.

**Callers**:

- `Elsa3WorkflowDefinitionImporter.ImportAsync` calls it with `IsReusable = false` and no replacements, then maps
  `State` to the version as today.
- `Elsa3ReusableActivityImportMaterializer` calls it once per selected item with the plan's replacements:
  - if any item fails, it throws `ReusableActivityImportValidationException` with all items' diagnostics (FR-018);
  - otherwise it builds the manifest (`outputMappings`, synthesized variables) and wrapper from the result.

## `IElsa3ActivityMap` (FR-006)

```csharp
public interface IElsa3ActivityMap
{
    /// The built-in entry for an Elsa 3 type name (ordinal), or null.
    Elsa3ActivityMapEntry? Find(string elsa3TypeName);

    /// Every built-in entry, ordered by Elsa 3 type name (for coverage tests and spec 235's golden output).
    IReadOnlyList<Elsa3ActivityMapEntry> Entries { get; }
}
```

The default implementation is the static built-in table. It is a singleton because it holds an application-wide static
value (§2.5.1).

## `IElsa3NodeConverter` (FR-006, for #2705)

```csharp
public interface IElsa3NodeConverter
{
    /// Converts one node and its non-graph descendants into an ActivityNode within a conversion scope opened by
    /// IElsa3DefinitionConverter; graph-level members (connections, start, join and fork) are left to the caller.
    ValueTask<Elsa3MigrationResult<ActivityNode>> ConvertNodeAsync(
        Elsa3NodeConversionContext context,
        Elsa3Activity node,
        CancellationToken cancellationToken = default);
}
```

#2705 calls this for each Flowchart child, and replaces `ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING` by building the native
Flowchart structure from the converted children plus the outcome rules (`Elsa3OutcomeRule`).

## Unchanged public surface

- `IElsa3WorkflowDefinitionImporter` and the reusable collection importer contracts keep their signatures.
- `ReusableActivityImportApplyResult` and `ReusableActivityImportMutation` gain an optional `Warnings` member.
- `Elsa3ActivityToState` and `Elsa3MemoryReferenceGraph` stay public, but are reached through the converter. Their
  catalog lookups move to the shared `Elsa3ActivityResolver` (FR-011).
