# Data model: pinned selection planning

This model describes immutable values passed to the pure planner. It is not a server store or a runtime activation manifest. Exact JSON field names are in [the v1 contract](contracts/selection-planner-v1.md).

| Entity | Owned fields | Validation and relationship |
|---|---|---|
| `SelectionCatalog` | `schemaVersion`, `id`, `version`, `publisher`, `digest`, profiles, groups | Foundation-owned snapshot. A definition ID has one kind across all versions. Duplicate ID/version or mismatched catalog digest refuses import. |
| `SelectionDefinition` | kind, stable ID, version, digest, unique feature-ID members, reviewed explanations, title/description | One immutable profile/group version. Flat membership only; no nested refs or settings. Workspace custom profile uses the same shape but has workspace origin and is not inserted into the Foundation catalog. |
| `DefinitionRef` | origin, kind, ID, version, digest | The authored document pins exactly one version. Foundation refs also pin the enclosing catalog; workspace refs resolve against separately supplied custom-profile documents. |
| `AuthoredComposition` | schema version, catalog pin, optional profile ref, group refs, explicit additions/removals, accepted exact IDs and feature locks, opaque settings/resources | User intent and previously accepted lock are separate. Unknown setting/resource JSON is retained as raw caller input and never projected into selection digests. Planning does not save or rewrite it. |
| `HostInventory` | target ID, observed-at timestamp, inventory ID, feature rows, evidence-source identifiers | Caller-supplied snapshot. A missing row means unknown availability, not a missing remote package. A stale snapshot remains an observation, not current readiness. |
| `InventoryFeature` | stable feature ID, availability, nullable runtime-descriptor required edges, nullable manifest edges/read state, explicit package identity or host-bundled marker, compatibility outcome/source | Non-null empty runtime edges mean authoritative no dependencies. Null means no descriptor evidence. Manifest optionality is only used when runtime evidence is null. Missing package identity is unresolved, never inferred to mean host-bundled. |
| `FeatureLock` | feature ID, package ID/version/manifest digest or explicit host-bundled marker, evidence source | Observation tied to an inventory. An absent identity is an unresolved finding rather than a fabricated lock. |
| `SelectionPlan` | catalog/inventory identities, exact sorted IDs, all selection/removal reasons, findings, feature locks, optional redacted persistence context | Pure output. Findings distinguish unresolved conditions and optional advice with stable codes and evidence source. It has no runtime-ready flag. |
| `ReresolutionDiff` | old/candidate pinned identities and exact IDs, added/removed IDs, changed reasons/edges/locks, settings/resource impact or unverified marker | Requested explicitly. Neither input nor accepted lock changes. |

## State and identity rules

1. Publication/import validates catalog and definition hashes before exposing a usable snapshot. A new digest for the same ID/version is a conflict, not an in-place update. Publication trust/signing is outside this slice.
2. Planning resolves the authored pins against supplied immutable snapshots. A missing/mismatched pin is reported with its exact ref and prevents that definition from contributing unknown members. The previous accepted exact selection stays visible.
3. Selection order is profile, groups, additions, then removals. Reasons are a sorted multiset of distinct source/ref facts; the exact feature set is a sorted set. Duplicate member IDs inside one definition are errors, whereas overlap across definitions is valid and must retain all reasons.
4. Inventory assessment follows expansion. A required edge missing from the exact set is unresolved, even if the target was explicitly removed. An optional edge is advice. Descriptor/manifest disagreement is separately visible.
5. Re-resolution computes old and candidate plans independently. The diff proposes changed pins and feature locks without altering either authored document. Unknown settings/resource impact is explicitly unverified.

## Privacy boundary

Catalog hashes cover only owned semantic definition fields. Feature locks omit secrets, connection strings, mutable enabled state and live shell feature revision. A supplied persistence summary exposes checked/unchecked status, provenance and resource reference IDs; it never holds a connection value. Opaque authored settings/resources are not echoed in the plan result.
