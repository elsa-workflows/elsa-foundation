# Elsa3.Mapping Extension Points

## Replacement/import boundary contracts

### `IElsa3WorkflowDefinitionImporter` *(Feature contract — `Elsa3.Mapping`)*

- **Kind:** Replacement/import boundary contract.
- **Purpose:** Imports accepted Elsa 3 authored workflow definition inputs into Elsa 4 workflow definition versions, returning migration diagnostics for unsupported or invalid input.
- **Default implementation:** `Elsa3WorkflowDefinitionImporter`.
- **Runtime boundary:** This is a Design-side migration/import surface. `Elsa.Workflows.Runtime.*` projects must not consume it.
