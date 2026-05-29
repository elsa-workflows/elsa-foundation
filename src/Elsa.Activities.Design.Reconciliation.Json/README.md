# Elsa.Activities.Design.Reconciliation.Json

JSON-file source for the activity catalog reconciliation lifecycle. Reads a JSON file of activity definitions and contributes them to each reconciliation pass via `OnActivityVersionsReconciling`.

## What this feature provides

- **`JsonActivityCatalogReader`** — opens the configured file, deserialises to `IReadOnlyList<JsonCatalogEntry>`. Missing file → warning + empty list (the reconciliation pass proceeds with zero contributions). Malformed JSON → wrapped into `InvalidJsonCatalogEntryException` with file context (raw `JsonException` never escapes).
- **`JsonActivityVersionsReconcilingHandler`** — handles `OnActivityVersionsReconciling`. Reads the file, validates each entry inline (kind known via registry, descriptor structurally valid, kind/descriptor agree), and contributes one `IActivityDefinitionVersion` per entry. Validation throws `InvalidJsonCatalogEntryException` with entry index + activity-type-key + kind so failures localise.

## Cross-feature contributions (handlers this feature registers)

- **`IDomainEventHandler<OnActivityVersionsReconciling>`** → `JsonActivityVersionsReconcilingHandler`.

## Cross-feature contributions (events this feature publishes)

None.

## Startup tasks

None.

## Options (`JsonReconciliationOptions`)

- **`FilePath`** *(string, required)* — filesystem path to the JSON catalog file.
- **`SourceId`** *(string, default `"JsonFile"`)* — source-instance identifier tagged as `SourceId` on every contributed `ActivityDefinition`. Override when multiple JSON sources coexist (e.g. `"PrimaryCatalog"`, `"OverridesCatalog"`) so audit queries can localise which file produced a given row.

## Well-known values

- **`JsonReconciliationConstants.SourceKind = "Json"`** — the `SourceKind` string tagged on every row this source contributes. The kind value is owned by this module (per the rename of smart-enum records to plain strings; the framework does not enumerate the legal set).

## Provenance fields on every contributed row

- `SourceKind = "Json"`
- `SourceId = options.SourceId` (default `"JsonFile"`)
- `ReconciledAt = clock.UtcNow`
- `ReconciledBy = Environment.MachineName`
- `ImplementationKind = entry.implementationKind` (from JSON)
- `ImplementationDescriptor` — deserialised from `entry.implementationDescriptor` JSON via the `IImplementationDescriptorRegistry`

## Exception surface

Per framework §2.23.5 (infrastructure-exception containment), raw `JsonException` never escapes this feature's boundary. Failures throw:

- **`InvalidJsonCatalogEntryException(entryIndex, activityTypeKey, implementationKind, message)`** — carries enough context to localise the failure in the source file. `entryIndex = -1` means file-level parse failure.

## JSON shape (per entry)

```json
{
  "implementationKind": "Clr",
  "implementationDescriptor": { "typeInfo": { ... } },
  "version": 1,
  "executionType": "Action",
  "definition": {
    "activityTypeKey": "Elsa.Event",
    "category": "Primitives",
    "displayName": "Event",
    "description": "Wait for an event to be triggered."
  },
  "inputs":  [ ... ],
  "outputs": [ ... ],
  "ports":   [ ... ]
}
```
