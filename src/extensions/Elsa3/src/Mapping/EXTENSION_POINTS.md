# Elsa3.Mapping Extension Points

## Replacement/import boundary contracts

### `IElsa3WorkflowDefinitionImporter` *(Feature contract — `Elsa3.Mapping`)*

- **Kind:** Replacement/import boundary contract.
- **Purpose:** Imports ordinary authored definitions and exposes collection-aware analyze/apply for reusable Elsa 3 workflows. A reusable workflow submitted through the single-definition path is rejected with guidance to use the collection boundary.
- **Default implementation:** `Elsa3WorkflowDefinitionImporter`.
- **Runtime boundary:** This is a Design-side migration/import surface. `Elsa.Workflows.Runtime.*` projects must not consume it.

### `IReusableActivityImportMaterializer` contribution

- **Implementation:** `Elsa3ReusableActivityImportMaterializer`.
- **Purpose:** maps exact planned reusable references to deterministic activity-version ids, creates graph-provider catalog material, and preserves each original workflow definition/version identity as a direct-start wrapper.
