# Contract: Activity Definition Authoring and Publication API

All paths are relative to the Elsa shell route. The contract extends the existing `design/activities` and `publishing` domains. JSON uses camel case. Every error response uses [validation-errors.md](validation-errors.md).

## 1. Route summary

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/design/activities/definitions` | List catalog definitions visible to the caller. |
| `POST` | `/design/activities/definitions` | Create a Design-owned definition and initial draft. |
| `GET` | `/design/activities/definitions/{definitionId}` | Read definition metadata, authority, fork provenance, head, drafts, and version lifecycle summaries. |
| `PATCH` | `/design/activities/definitions/{definitionId}` | Change presentation metadata only. |
| `POST` | `/design/activities/definitions/{definitionId}/forks` | Fork an exact source-owned version into a new Design-owned identity and draft. |
| `POST` | `/design/activities/definitions/{definitionId}/drafts` | Create a fresh draft or clone an exact version. |
| `GET` | `/design/activities/definitions/{definitionId}/drafts` | List drafts for a definition. |
| `GET` | `/design/activities/drafts/{draftId}` | Read one draft, state, layout, revision, and current validation. |
| `PUT` | `/design/activities/drafts/{draftId}` | Replace complete draft state and layout under an expected revision. |
| `DELETE` | `/design/activities/drafts/{draftId}` | Discard an active draft under an expected revision. |
| `POST` | `/design/activities/drafts/{draftId}/validate` | Revalidate an exact draft revision. |
| `POST` | `/design/activities/drafts/{draftId}/publish` | Atomically publish a draft as an immutable version. |
| `POST` | `/design/activities/drafts/{draftId}/migrate-provider` | Clone/migrate a provider manifest into a new draft. |
| `GET` | `/design/activities/definitions/{definitionId}/versions` | List immutable versions. |
| `GET` | `/design/activities/versions/{versionId}` | Read one version and its public publication facts. |
| `POST` | `/design/activities/versions/{versionId}/retire` | Block new direct selection. |
| `POST` | `/design/activities/versions/{versionId}/restore` | Restore direct selection after retirement. |
| `POST` | `/design/activities/versions/{versionId}/revoke` | Apply the stronger revocation policy. |
| `POST` | `/publishing/activity-drafts/{draftId}/test-runs` | Compile and execute an exact draft revision through a synthetic wrapper workflow. |
| `POST` | `/publishing/preflight` | Check Runtime requirements for retained active artifacts. |

Version diff, dependency, upgrade, and runtime inspection routes are defined in their focused contracts.

### Update definition presentation metadata

```http
PATCH /design/activities/definitions/{definitionId}
Content-Type: application/json

{
  "category": "Finance",
  "displayName": "Calculate invoice total",
  "description": "Calculates an invoice total and discount."
}
```

This is a full replacement of the three presentation fields. `category` and `displayName` are
required non-blank strings; `description` may be a string or `null`. No other body properties are
part of this request contract. `activityTypeKey`, tenant identity, content authority, fork
provenance, and head identity are immutable through this route.

Only a tenant-visible, Design-owned definition can be updated. A blank required field returns
`400 activity.request.invalid`; a source-owned definition returns
`409 activity.definition.content-authority`; an out-of-scope definition returns
`403 activity.tenant.reference-denied`. Success returns `200 OK` with the complete
`ReusableActivityDefinitionDetailsView`.

## 2. Shared views

### `ActivityDefinitionIdentityView`

```json
{
  "definitionId": "activity-def-order-total",
  "activityTypeKey": "acme.orders.calculate-total",
  "tenantId": "tenant-a",
  "category": "Orders",
  "displayName": "Calculate order total",
  "description": "Calculates an order total and discount.",
  "contentAuthority": {
    "kind": "Design",
    "authorityKey": "elsa.activity-graph",
    "sourceId": null
  },
  "forkedFrom": {
    "definitionId": "activity-def-clr",
    "versionId": "activity-ver-clr-3",
    "version": "3.0.0"
  },
  "headVersionId": "activity-ver-2"
}
```

`forkedFrom` is null for definitions that were not created by a fork. It is audit provenance only: it does not grant authority over, or establish version lineage with, the source definition.

### `ActivityContractView`

```json
{
  "contractSchemaVersion": "1",
  "inputs": [
    {
      "referenceKey": "order",
      "name": "Order",
      "type": { "alias": "acme.orders.order", "collectionKind": "None" },
      "isRequired": true,
      "default": null,
      "storageDriverKey": "elsa.json",
      "durability": "Required",
      "displayName": "Order",
      "description": null,
      "category": null,
      "order": 0,
      "uiHint": null,
      "uiSpecifications": null
    }
  ],
  "outputs": [
    {
      "referenceKey": "total",
      "name": "Total",
      "type": { "alias": "decimal", "collectionKind": "None" },
      "isRequired": true,
      "storageDriverKey": "elsa.json",
      "durability": "Required",
      "displayName": "Total",
      "description": null,
      "category": null,
      "order": 0,
      "uiHint": null,
      "uiSpecifications": null
    }
  ],
  "outcomes": [
    {
      "referenceKey": "done",
      "name": "Done",
      "description": null,
      "isEmitted": true
    }
  ]
}
```

An input default has this shape:

```json
{
  "syntax": "JavaScript",
  "value": "getInput('fallbackOrder')"
}
```

The value remains opaque to this API; the selected binding compiler owns the syntax.

### `ActivityProviderManifestView`

```json
{
  "providerKey": "elsa.activity-graph",
  "schemaVersion": "1",
  "payload": {}
}
```

The payload is returned only to callers authorized to author that provider. Catalog and dependency APIs never need it.

## 3. Create a definition and initial draft

```http
POST /design/activities/definitions
Content-Type: application/json
```

```json
{
  "activityTypeKey": "acme.orders.calculate-total",
  "category": "Orders",
  "displayName": "Calculate order total",
  "description": null,
  "provider": {
    "providerKey": "elsa.activity-graph",
    "schemaVersion": "1",
    "payload": {}
  },
  "contract": {
    "contractSchemaVersion": "1",
    "inputs": [],
    "outputs": [],
    "outcomes": [
      { "referenceKey": "done", "name": "Done", "isEmitted": true }
    ]
  },
  "layout": []
}
```

Response: `201 Created` with `ActivityDefinitionDetailsView` and `Location` pointing to the definition.

```json
{
  "definition": {},
  "drafts": [
    {
      "draftId": "activity-draft-1",
      "definitionId": "activity-def-1",
      "revision": 1,
      "sourceVersionId": null,
      "status": "Active",
      "providerKey": "elsa.activity-graph",
      "providerSchemaVersion": "1",
      "updatedAt": "2026-07-15T12:00:00Z"
    }
  ],
  "versions": []
}
```

Rules:

- The API always creates `ContentAuthority.Kind = Design`; source-owned definitions enter through trusted reconciliation commands, not this endpoint.
- The definition and initial draft are created atomically.
- Duplicate `(tenantId, activityTypeKey)` returns `409` with `activity.definition.key-conflict`.

## 4. Fork a source-owned version

```http
POST /design/activities/definitions/{definitionId}/forks
```

```json
{
  "sourceVersionId": "activity-ver-clr-3",
  "activityTypeKey": "tenant-a.orders.calculate-total-custom",
  "category": "Orders",
  "displayName": "Calculate order total (custom)",
  "description": null,
  "targetProviderKey": "elsa.activity-graph",
  "targetProviderSchemaVersion": "1"
}
```

Response: `201 Created` with a new `ActivityDefinitionDetailsView` containing one active draft.

Rules:

- The source exact version must be visible to the caller and remains unchanged.
- The new definition belongs to the caller's tenant and has `ContentAuthority.Kind = Design`.
- The source public contract is copied. Provider source is cloned or deterministically migrated through the requested target provider; if no supported conversion exists, the request returns `422 activity.provider.migration-unsupported` and creates nothing.
- The new definition records fork provenance for audit/inspection, but it is not part of the source definition's lineage or content authority.
- Duplicate tenant `activityTypeKey` returns `409 activity.definition.key-conflict`.

## 5. Create or clone a draft

```http
POST /design/activities/definitions/{definitionId}/drafts
```

Fresh draft:

```json
{
  "sourceVersionId": null,
  "provider": {
    "providerKey": "elsa.activity-graph",
    "schemaVersion": "1",
    "payload": {}
  },
  "contract": {
    "contractSchemaVersion": "1",
    "inputs": [],
    "outputs": [],
    "outcomes": [
      { "referenceKey": "done", "name": "Done", "isEmitted": true }
    ]
  },
  "layout": []
}
```

Clone exact version:

```json
{
  "sourceVersionId": "activity-ver-2"
}
```

Response: `201 Created` with `ActivityDefinitionDraftView`. Cloning deep-copies contract, provider manifest, and version layout. The source version remains immutable.

Source-owned definitions reject draft creation with `409 activity.definition.content-authority` unless the operation is the explicit “fork to new identity” command. Forking creates a new Design-owned definition; it never changes the source-owned lineage.

## 6. Read and replace a draft

### Read

```http
GET /design/activities/drafts/{draftId}
```

```json
{
  "draftId": "activity-draft-1",
  "definitionId": "activity-def-1",
  "tenantId": "tenant-a",
  "revision": 7,
  "sourceVersionId": "activity-ver-1",
  "status": "Active",
  "contract": {},
  "provider": {},
  "layout": [],
  "validation": {
    "revision": 7,
    "isValid": false,
    "validatedAt": "2026-07-15T12:10:00Z",
    "diagnostics": []
  },
  "createdAt": "2026-07-15T12:00:00Z",
  "updatedAt": "2026-07-15T12:09:00Z"
}
```

### Full-state update

```http
PUT /design/activities/drafts/{draftId}
Content-Type: application/json
```

```json
{
  "expectedRevision": 7,
  "contract": {},
  "provider": {},
  "layout": []
}
```

Response: `200 OK` with the complete draft at revision `8`.

Rules:

- This is full-state-always, consistent with workflow draft mutation; there is no JSON Patch contract.
- State, layout, revision, and derived validation outcome persist atomically.
- `providerKey` may change only for a Design-owned lineage; changing it is a behavioral change later classified by the version diff.
- A stale revision returns `409 activity.draft.stale-revision` with expected and actual revision in safe diagnostic metadata.

## 7. Validate a draft revision

```http
POST /design/activities/drafts/{draftId}/validate
```

```json
{
  "expectedRevision": 8
}
```

Response: `200 OK` whether valid or invalid:

```json
{
  "draftId": "activity-draft-1",
  "revision": 8,
  "isValid": false,
  "validatedAt": "2026-07-15T12:15:00Z",
  "diagnostics": [
    {
      "code": "activity.contract.required-output-unmapped",
      "severity": "Error",
      "message": "Required output 'total' has no boundary mapping.",
      "subject": { "kind": "ActivityDraft", "id": "activity-draft-1" },
      "location": { "referenceKey": "total" },
      "remediation": "Map a durable graph value to the output or make the output optional.",
      "metadata": {}
    }
  ]
}
```

`200` is used because validation findings are the requested result. Malformed requests, stale revisions, missing drafts, authority denials, or validator infrastructure failures use Problem Details.

## 8. Publish a draft

```http
POST /design/activities/drafts/{draftId}/publish
Content-Type: application/json
```

```json
{
  "expectedDraftRevision": 8,
  "expectedDefinitionHeadVersionId": "activity-ver-1",
  "version": "2.0.0"
}
```

For a first publication, `expectedDefinitionHeadVersionId` is `null`.

Response: `201 Created` after atomic publication:

```json
{
  "definitionId": "activity-def-1",
  "versionId": "activity-ver-2",
  "version": "2.0.0",
  "draftId": "activity-draft-1",
  "templateId": "activity-template-sha256-...",
  "templateHash": "sha256-...",
  "sourceReferenceId": "source-ref-...",
  "provider": {
    "providerKey": "elsa.activity-graph",
    "schemaVersion": "1",
    "fingerprint": "elsa.activity-graph/compiler/1.0.0"
  },
  "directDependencyCount": 2,
  "closedTemplateCount": 5,
  "runtimeRequirements": [
    { "consumerKey": "elsa.graph-activity", "schemaVersion": "1" }
  ],
  "diff": {
    "compatibility": "Breaking",
    "requiredBump": "Major",
    "behaviorChanged": true,
    "changes": []
  },
  "publishedAt": "2026-07-15T12:20:00Z"
}
```

Publication rejection after semantic validation uses `422` Problem Details and includes all deterministic diagnostics that can be reported safely in one pass. Examples: contract mismatch, dependency cycle, insufficient SemVer, provider compilation failure, missing required Runtime consumer declaration, or tenant-invalid dependency.

Concurrency conflicts use `409`:

- `activity.draft.stale-revision`
- `activity.definition.stale-head`
- `activity.version.conflict`
- `activity.definition.content-authority`

No rejected publication creates a version, advances a head, or exposes partial template/reference/dependency state.

## 9. Read an immutable version

```http
GET /design/activities/versions/{versionId}
```

```json
{
  "definition": {},
  "versionId": "activity-ver-2",
  "version": "2.0.0",
  "sourceDraftId": "activity-draft-1",
  "sourceVersionId": "activity-ver-1",
  "contract": {},
  "provider": {
    "providerKey": "elsa.activity-graph",
    "schemaVersion": "1",
    "payload": {}
  },
  "template": {
    "templateId": "activity-template-sha256-...",
    "templateHash": "sha256-...",
    "sourceReferenceId": "source-ref-...",
    "providerFingerprint": "elsa.activity-graph/compiler/1.0.0",
    "directDependencyCount": 2,
    "closedTemplateCount": 5,
    "runtimeRequirements": []
  },
  "lifecycle": "Active",
  "publishedAt": "2026-07-15T12:20:00Z"
}
```

Provider payload is omitted or redacted when the caller can read the catalog contract but cannot author the provider.

## 10. Provider-manifest migration

```http
POST /design/activities/drafts/{draftId}/migrate-provider
```

```json
{
  "expectedRevision": 8,
  "targetProviderKey": "elsa.activity-graph",
  "targetSchemaVersion": "2"
}
```

Response: `201 Created` with a **new** active draft whose `sourceVersionId` remains the exact immutable lineage source and whose manifest is migrated deterministically. The original draft/version is not rewritten. Unsupported source schemas return `422 activity.provider.migration-unsupported`.

## 11. Lifecycle commands

Retire, restore, and revoke use an expected current lifecycle value:

```json
{
  "expectedLifecycle": "Active",
  "reason": "Superseded by the tax-v2 activity."
}
```

- Retire/restore: `200 OK` with the new lifecycle. Retirement affects new direct catalog selection only.
- Revoke: `200 OK` after the stronger policy fact commits. Revocation does not delete the version, template, Source Reference, dependencies, or historical evidence.
- Stale lifecycle: `409 activity.version.stale-lifecycle`.

## 12. Activity draft test run

```http
POST /publishing/activity-drafts/{draftId}/test-runs
```

```json
{
  "expectedRevision": 8,
  "inputs": {
    "order": {
      "state": "Present",
      "value": { "id": "order-42" }
    }
  },
  "correlationId": "designer-test-42"
}
```

Accepted response: `202 Accepted`.

```json
{
  "testRunId": "activity-test-run-1",
  "draftId": "activity-draft-1",
  "draftRevision": 8,
  "artifactId": "workflow-artifact-sha256-...",
  "sourceReferenceId": "source-ref-test-...",
  "workflowExecutionId": "wfexec-1",
  "outerActivityExecutionId": null,
  "status": "DispatchAccepted",
  "commandDispatchStatus": "Accepted",
  "reason": null,
  "expiresAt": "2026-07-15T13:20:00Z"
}
```

The wrapper workflow and outer activity execution are created through normal dispatch. `outerActivityExecutionId` may remain null until scheduling commits and is then discoverable through workflow-instance inspection. The expiring Source Reference, not a second artifact store, controls lifetime.

## 13. Runtime requirement preflight

```http
POST /publishing/preflight
```

```json
{
  "scope": "ActiveRetainedArtifacts",
  "artifactIds": null
}
```

```json
{
  "checkedArtifactCount": 184,
  "isReady": false,
  "requirements": [
    {
      "consumerKey": "elsa.graph-activity",
      "schemaVersion": "1",
      "status": "Available",
      "affectedArtifactCount": 183
    },
    {
      "consumerKey": "acme.remote-operation",
      "schemaVersion": "2",
      "status": "Missing",
      "affectedArtifactCount": 1
    }
  ],
  "diagnostics": []
}
```

Preflight is a read/validation result and returns `200 OK` when missing requirements are found. Infrastructure failure uses Problem Details.

## 14. Clean-break impact

The existing public “add a definition with an immediate version” and “add arbitrary version” authoring commands are not the Design-owned authoring contract for this feature. Trusted reconciliation retains an internal source-owned creation path. Public authoring flows through definition + draft + publication so validation, SemVer, dependencies, content authority, and template creation cannot be bypassed.
