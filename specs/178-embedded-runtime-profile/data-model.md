# Data model: First Embedded runtime profile

| Entity | Fields and relationship | Validation |
|---|---|---|
| Published profile | `id=embedded-runtime`, `version=1`, digest, 16 members and rationale | Same ID/version with changed content refuses; no environment values |
| Selection catalog | Versioned digest and profile; consumed by planner and UI | Import checks canonical digest |
| Authored composition | Catalog/profile pins, accepted expansion, explicit add/remove | Accepted set must match plan before generation; removal wins |
| Selection plan | Exact IDs, provenance, required edges and evidence gaps | Known missing edge blocks candidate; unknown facts are not readiness |
| Host source snapshot | Base, selected overlay, protected siblings | Only selected object-map edits; stale source refuses |
| Candidate | Fresh bundle and redacted activation diff | Readback IDs equal accepted IDs before publication |
| Host prerequisites | Named resource, lock directory, signing, migration authorization, assemblies | Host-owned; excluded from profile and portable plan |

Progression: `authored → planned → accepted → candidate-reviewed → candidate-published`. Pin drift, missing edge, unsupported mapping, stale source or readback mismatch returns to review without publication. Deployment/live activation belong to [spec 177](../177-file-deployed-activation/spec.md).
