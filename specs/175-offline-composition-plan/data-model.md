# Data model: Offline composition plan

The existing catalog, authored composition, inventory, persistence summary, and plan types are defined in [spec 174](../174-profile-selection-planner/contracts/selection-planner-v1.md). This unit adds only the following.

| Record | Fields | Rules |
|---|---|---|
| Dependency evidence | `featureId`, `dependencyId`, `mode`, `evidenceKind`, `targetSelected` | Sorted ordinally; mode is required/optional; kind is reviewed-definition/runtime-descriptor/package-manifest. Reviewed rationale is descriptive. Loaded descriptor edges govern, including an observed empty list; manifest edges govern only without a descriptor. No row changes selected IDs. |
| Inventory file v1 | `schemaVersion`, `inventoryId`, `targetId`, `observedAt`, `source`, `features[]` | Strict JSON; schema `1`; unique feature IDs; every typed row field explicit, with null for unavailable descriptor/manifest/package evidence. ISO 8601 timestamp with offset. Validate via the same typed inventory rules. |
| Resource-hint file v1 | `schemaVersion`, `source`, `resourceReferences[]` | Strict JSON; schema `1`; unique safe names. No caller-controlled status/provider/connection/secret fields. Adapter creates unchecked persistence evidence with supplied-file provenance. |
| Command plan projection v1 | `schemaVersion`, `kind`, `evidenceScope`, `catalog`, `inventory`, `candidate`, `accepted`, `reasons`, `dependencyEvidence`, `findings`, `observedLocks`, `persistence` | Stable property/array ordering; safe IDs and fixed human text only. Raw authored settings/resources, custom rationale, and exception messages are absent. No runtime-ready flag. |

The candidate is calculated from profile/group members, explicit additions, then removals. The accepted set remains the authored historical lock. Optional evidence attaches to the candidate; it never mutates the accepted lock or feature set. File-only source/time identifies supplied evidence and does not authenticate the host. Secret-bearing input remains in memory only for parsing and is never written.
