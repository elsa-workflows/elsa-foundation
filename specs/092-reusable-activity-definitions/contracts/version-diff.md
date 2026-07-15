# Contract: Activity Version Diff

The same diff model powers publication SemVer enforcement, version history, draft preview, upgrade planning, and frontend review. It compares provider-neutral contract facts and safe provider/template/dependency summaries; it never returns opaque manifests or compiled descriptor payloads.

## 1. Routes

### Compare two immutable versions

```http
GET /design/activities/versions/{fromVersionId}/diff/{toVersionId}
```

### Compare a draft candidate with an immutable base

```http
POST /design/activities/drafts/{draftId}/diff
Content-Type: application/json
```

```json
{
  "expectedRevision": 8,
  "baseVersionId": "activity-ver-1"
}
```
If `baseVersionId` is null, the current observed definition head is used and returned explicitly. A stale draft revision returns `409`; diffing never mutates the draft.

## 2. `ActivityVersionDiffView`

```json
{
  "from": {
    "kind": "ActivityVersion",
    "definitionId": "activity-def-1",
    "versionId": "activity-ver-1",
    "draftId": null,
    "revision": null,
    "version": "1.4.0",
    "templateHash": "sha256-old"
  },
  "to": {
    "kind": "ActivityDraft",
    "definitionId": "activity-def-1",
    "versionId": null,
    "draftId": "activity-draft-1",
    "revision": 8,
    "version": null,
    "templateHash": "sha256-candidate"
  },
  "compatibility": "Breaking",
  "requiredBump": "Major",
  "behaviorChanged": true,
  "provider": {
    "fromKey": "elsa.activity-graph",
    "fromSchemaVersion": "1",
    "toKey": "elsa.activity-graph",
    "toSchemaVersion": "2",
    "changed": true
  },
  "summary": {
    "breaking": 2,
    "additive": 1,
    "nonBehavioral": 1,
    "warnings": 0
  },
  "changes": [],
  "diagnostics": []
}
```

### Identity view

`kind` is `ActivityVersion` or `ActivityDraft`. The view always pins the exact compared material:

- immutable version: `versionId`, semantic `version`, and `templateHash`;
- draft: `draftId`, `revision`, and candidate `templateHash` when deterministic compilation succeeds.

Diff may still return contract/provider changes when candidate compilation fails. In that case `templateHash` is null and `diagnostics` explains why; publication remains blocked.

### Compatibility values

| Value | Meaning |
|---|---|
| `Identical` | No public, provider, dependency, behavior, or presentation change. |
| `NonBehavioral` | Presentation/provenance-only change; patch is sufficient. |
| `Compatible` | Additive compatible public change; minor is required unless a provider strengthens it. |
| `Breaking` | At least one baseline or provider-strengthened breaking change; major is required. |

### Required bump values

`None`, `Patch`, `Minor`, `Major`. The overall value is the maximum of all platform and provider change requirements.

## 3. `ActivityVersionChangeView`

```json
{
  "changeId": "contract:input:customer-id:requiredness-changed",
  "area": "Contract",
  "kind": "RequirednessChanged",
  "subject": {
    "memberKind": "Input",
    "referenceKey": "customer-id",
    "dependencyVersionId": null,
    "occurrenceId": null
  },
  "before": {
    "name": "CustomerId",
    "type": { "alias": "string", "collectionKind": "None" },
    "isRequired": false,
    "hasDefault": false,
    "storageDriverKey": "elsa.json",
    "durability": "Required"
  },
  "after": {
    "name": "CustomerId",
    "type": { "alias": "string", "collectionKind": "None" },
    "isRequired": true,
    "hasDefault": false,
    "storageDriverKey": "elsa.json",
    "durability": "Required"
  },
  "impact": "Breaking",
  "requiredBump": "Major",
  "message": "Input 'customer-id' changed from optional to required without a default."
}
```

### `area`

- `Contract`
- `Default`
- `Outcome`
- `Durability`
- `Provider`
- `Implementation`
- `Dependency`
- `Presentation`

### `impact`

- `NonBehavioral`
- `Additive`
- `Breaking`

### Stable platform `kind` values

Contract and outcome:

- `MemberAdded`
- `MemberRemoved`
- `MemberRenamed`
- `ReferenceKeyChanged`
- `TypeChanged`
- `RequirednessChanged`
- `OutcomeEmissionChanged`

Default and durability:

- `DefaultAdded`
- `DefaultRemoved`
- `DefaultChanged`
- `StorageDriverChanged`
- `DurabilityChanged`

Provider and behavior:

- `ProviderChanged`
- `ProviderSchemaChanged`
- `ImplementationChanged`
- `RuntimeRequirementAdded`
- `RuntimeRequirementRemoved`
- `RuntimeRequirementSchemaChanged`

Dependencies:

- `DependencyAdded`
- `DependencyRemoved`
- `DependencyVersionChanged`
- `DependencyOccurrenceMoved`

Presentation:

- `DisplayNameChanged`
- `DescriptionChanged`
- `CategoryChanged`
- `OrderChanged`
- `UiMetadataChanged`
- `LayoutChanged`

Providers may add namespaced kinds (for example `acme.script.ExportSignatureChanged`). Unknown kinds remain renderable through `area`, `impact`, `requiredBump`, and `message`.

## 4. Safe before/after projections

`before` and `after` use an area-specific projection:

- contract members: name, type reference, requiredness, default presence/hash, storage-driver key, durability;
- defaults: syntax and a safe value summary/hash, never secret-bearing expression source when policy marks it protected;
- outcomes: name and emitted state;
- provider: key and schema version only;
- implementation: template hash, provider fingerprint, node/resume-target counts;
- dependency: exact version/template identity plus occurrence id;
- presentation: public presentation values;
- layout: layout hash/count only, not the full layout.

Opaque provider manifests and Runtime descriptor payloads are never returned by the diff API.

## 5. Baseline compatibility matrix

| Change | Baseline bump |
|---|---|
| Remove or rename input/output/outcome | Major |
| Change stable reference key | Major |
| Incompatible type change | Major |
| Optional input becomes required without a compatible default | Major |
| Add required output | Major |
| Weaken durable-boundary policy or change required storage-driver semantics incompatibly | Major |
| Remove or change an existing default | Major |
| Begin emitting an existing or new outcome callers could not previously observe | Major |
| Add optional input with no default | Minor |
| Add required input with a compatible default | Minor |
| Add optional output | Minor |
| Add a non-emitted/documentation-only outcome | Minor unless provider strengthens |
| Add a default to an existing optional input | Minor unless provider strengthens |
| Presentation or layout only | Patch |
| No change | None |

Provider changes and dependency version changes are behavioral by default. The provider compiler may prove a stronger requirement (for example Major); it may not classify a baseline Major change as Minor/Patch.

## 6. Rename handling

Stable `ReferenceKey` is identity. Therefore:

- Same reference key + changed display/name field is `MemberRenamed` and Major under the agreed baseline.
- Removed old reference key + added new reference key is represented as `ReferenceKeyChanged` only when an explicit authoring rename mapping proves intent; otherwise it remains one removal plus one addition.
- The diff engine never guesses renames from similar names.

This keeps the result deterministic and prevents accidental compatibility claims.

## 7. Dependency change example

```json
{
  "changeId": "dependency:node-tax:activity-ver-tax-1->activity-ver-tax-2",
  "area": "Dependency",
  "kind": "DependencyVersionChanged",
  "subject": {
    "memberKind": null,
    "referenceKey": null,
    "dependencyVersionId": "activity-ver-tax-2",
    "occurrenceId": "node-tax"
  },
  "before": {
    "definitionId": "activity-def-tax",
    "versionId": "activity-ver-tax-1",
    "version": "1.1.0",
    "templateHash": "sha256-tax-1"
  },
  "after": {
    "definitionId": "activity-def-tax",
    "versionId": "activity-ver-tax-2",
    "version": "2.0.0",
    "templateHash": "sha256-tax-2"
  },
  "impact": "Breaking",
  "requiredBump": "Major",
  "message": "Placed dependency 'node-tax' changed to an activity version with a breaking public contract."
}
```

The owning version's bump is calculated from its observable contract/behavior, not mechanically copied from the dependency's SemVer. A provider may prove a dependency update behaviorally equivalent; the template hash and provider diagnostics make that claim explicit.

## 8. Publication enforcement

Publication returns the calculated diff in its success response. When the requested version is insufficient, it returns `422 activity.publication.invalid` with at least:

```json
{
  "code": "activity.version.bump-insufficient",
  "severity": "Error",
  "message": "Version 1.5.0 is insufficient; the candidate requires a Major increment from 1.4.0.",
  "subject": {
    "kind": "ActivityDraft",
    "id": "activity-draft-1",
    "definitionId": "activity-def-1",
    "revision": 8
  },
  "location": null,
  "remediation": "Publish as 2.0.0 or revise the breaking changes.",
  "metadata": {
    "baseVersion": "1.4.0",
    "requestedVersion": "1.5.0",
    "requiredBump": "Major",
    "minimumVersion": "2.0.0"
  }
}
```
