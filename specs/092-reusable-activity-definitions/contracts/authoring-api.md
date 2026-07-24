# Contract: Activity Definition Authoring and Publication API

All paths are relative to the Elsa shell route. The contract extends the existing `design/activities` and `publishing` domains. JSON uses camel case. Every error response uses [validation-errors.md](validation-errors.md).

## 1. Route summary

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/design/activities/definitions` | Page authorization-safe definition management summaries. |
| `GET` | `/design/activities/authoring-capabilities` | Read the authorization-filtered provider, contract type, storage-driver, and server key rules snapshot. |
| `POST` | `/design/activities/definitions` | Create a Design-owned definition and initial draft. |
| `GET` | `/design/activities/definitions/{definitionId}` | Read one definition workbench header and bounded relationship summaries; drafts and versions remain separate pages. |
| `PATCH` | `/design/activities/definitions/{definitionId}` | Change presentation metadata only. |
| `PUT` | `/design/activities/definitions/{definitionId}/recommendation` | Replace or explicitly clear the exact recommended active version under reviewed preconditions. |
| `GET` | `/design/activities/definitions/picker` | Page one authorization-safe exact recommended active version per definition. |
| `POST` | `/design/activities/definitions/{definitionId}/fork-previews` | Durably reserve and review an exact source-owned fork candidate without creating authoring state. |
| `POST` | `/design/activities/fork-candidates/{candidateId}/apply` | Atomically apply an unchanged, unexpired reviewed fork candidate. |
| `GET` | `/design/activities/forks/{idempotencyKey}` | Read a durable terminal fork receipt after an uncertain response. |
| `POST` | `/design/activities/definitions/{definitionId}/drafts` | Create a fresh draft or clone an exact version. |
| `GET` | `/design/activities/definitions/{definitionId}/drafts` | Page authorization-safe draft management summaries for one definition. |
| `GET` | `/design/activities/drafts/{draftId}` | Read one draft, state, layout, revision, and current validation. |
| `PUT` | `/design/activities/drafts/{draftId}` | Replace complete draft state and layout under an expected revision. |
| `PATCH` | `/design/activities/drafts/{draftId}/presentation` | Replace the optional draft presentation label under the same expected revision stream. |
| `POST` | `/design/activities/drafts/{draftId}/conflict-copies` | Atomically preserve reviewed full state as a new parallel draft after a stale-revision conflict. |
| `DELETE` | `/design/activities/drafts/{draftId}` | Discard an active draft under an expected revision. |
| `POST` | `/design/activities/drafts/{draftId}/validate` | Revalidate an exact draft revision. |
| `POST` | `/design/activities/drafts/{draftId}/publication-preflight` | Review the exact authoritative publication evidence and valid SemVer choices. |
| `POST` | `/design/activities/drafts/{draftId}/publish` | Atomically publish the exact reviewed draft/head state as an immutable version. |
| `GET` | `/design/activities/publications/{idempotencyKey}` | Read the durable terminal outcome of an idempotent activity publication. |
| `POST` | `/design/activities/drafts/{draftId}/migrate-provider` | Clone/migrate a provider manifest into a new draft. |
| `POST` | `/design/activities/drafts/{draftId}/contract-proposals` | Compute a typed, read-only provider proposal for one exact draft binding. |
| `POST` | `/design/activities/drafts/{draftId}/contract-proposals/apply` | Apply explicitly selected changes from an unchanged exact proposal. |
| `GET` | `/design/activities/definitions/{definitionId}/versions` | Page authorization-safe immutable version management summaries. |
| `GET` | `/design/activities/versions/{versionId}` | Read one version and its public publication facts. |
| `POST` | `/design/activities/versions/{versionId}/retire` | Block new direct selection. |
| `POST` | `/design/activities/versions/{versionId}/restore` | Restore direct selection after retirement. |
| `POST` | `/design/activities/versions/{versionId}/revoke` | Apply the stronger revocation policy. |
| `POST` | `/publishing/activity-drafts/{draftId}/test-runs` | Compile and execute an exact draft revision through a synthetic wrapper workflow. |
| `POST` | `/publishing/preflight` | Check Runtime requirements for retained active artifacts. |

Version diff, dependency, upgrade, and runtime inspection routes are defined in their focused contracts.

### Activity Design capability relations

The `elsa.api.activity-design` capability advertises these canonical entry and resource relations.
Clients use them to discover the supported API surface rather than probing legacy alternates;
subordinate operations follow this capability-major contract.

| Relation | Template | Purpose |
|---|---|---|
| `activity-catalog` | `design/activities/catalog` | Activity catalog projection. |
| `activity-authoring-capabilities` | `design/activities/authoring-capabilities` | Authorization-filtered provider and contract capabilities. |
| `activity-definitions` | `design/activities/definitions` | Definition collection and creation. |
| `activity-definition` | `design/activities/definitions/{definitionId}` | One stable definition detail. |
| `activity-definition-drafts` | `design/activities/definitions/{definitionId}/drafts` | Draft collection and creation for one definition. |
| `activity-definition-draft` | `design/activities/drafts/{draftId}` | One mutable draft detail/update/discard target. |
| `activity-draft-validation` | `design/activities/drafts/{draftId}/validate` | Explicit validation for one exact draft revision. |
| `activity-draft-contract-proposals` | `design/activities/drafts/{draftId}/contract-proposals` | Read-only typed contract proposal for one exact draft binding. |
| `activity-draft-contract-proposals-apply` | `design/activities/drafts/{draftId}/contract-proposals/apply` | Apply selected changes from an unchanged reviewed proposal. |
| `activity-definition-versions` | `design/activities/definitions/{definitionId}/versions` | Immutable version collection for one definition. |
| `activity-definition-version` | `design/activities/versions/{versionId}` | One immutable version detail. |
| `activity-definition-fork-preview` | `design/activities/definitions/{definitionId}/fork-previews` | Reserve and review one source-owned fork candidate. |
| `activity-definition-fork-apply` | `design/activities/fork-candidates/{candidateId}/apply` | Apply one reviewed candidate. |
| `activity-definition-fork-status` | `design/activities/forks/{idempotencyKey}` | Durable terminal fork outcome. |
| `activity-draft-conflict-copies` | `design/activities/drafts/{draftId}/conflict-copies` | Conflict-copy recovery for one draft. |
| `activity-definition-recommendation` | `design/activities/definitions/{definitionId}/recommendation` | Exact recommendation replacement or clearing. |
| `recommended-activity-definitions` | `design/activities/definitions/picker` | One recommended active version per visible definition. |
| `activity-availability` | `design/activities/availability/settings` | Activity availability settings. |
| `activity-availability-diagnostics` | `design/activities/availability/diagnostics` | Activity availability diagnostics. |

Templated relations declare `templated: true`. A missing relation means the Contribution is
unavailable; it is never permission to fall back to the removed workflow-as-activity contract.

### Bounded management collection contract

Definition, draft, and version management collections share one bounded cursor contract. The
supported first-page query shapes are:

```http
GET /design/activities/definitions?limit=25&cursor={opaque}&search={term}&authority={authority}&providerKey={providerKey}&sort=identity-asc
GET /design/activities/definitions/{definitionId}/drafts?limit=25&cursor={opaque}&search={term}&providerKey={providerKey}&status={status}&sort=identity-asc
GET /design/activities/definitions/{definitionId}/versions?limit=25&cursor={opaque}&search={term}&providerKey={providerKey}&lifecycle={lifecycle}&sort=identity-asc
```

- `limit` is a requested page size between `1` and `100`. Omitting it uses the bounded server
  default of `25`.
- Omitting `cursor` starts a new authorization-filtered snapshot at the current activity management
  projection sequence. The opaque continuation binds tenant/visibility scope, caller authorization
  profile, collection/definition scope, normalized query inputs, requested limit, snapshot sequence,
  snapshot time, and continuation offset. The cursor is signed and
  cannot be altered or forged without rejection.
- `search`, collection-specific filters, `sort`, and `limit` must remain compatible with the
  cursor binding. An invalid or mismatched cursor returns
  `400 activity.management.cursor-invalid` with recovery instruction `restart-without-cursor`; the
  server does not silently restart the page chain.
- `identity-asc` is the supported deterministic ordering for these management pages.

```json
{
  "items": [],
  "count": 0,
  "totalCount": 123,
  "hasMore": true,
  "continuation": "opaque",
  "snapshot": {
    "snapshotId": "activity-management-snapshot-42",
    "asOf": "2026-07-15T12:00:00Z"
  }
}
```

`totalCount` is the exact count of resources visible under the bound query and authorization
snapshot, never a global or pre-authorization count. `count` is the number of items in this
response. `hasMore` and `continuation` describe only the same `[validFrom, validTo)` projection
view, even when concurrent authoring or publication advances the current watermark. Search,
tenant/global visibility, filters, ordering, paging, and exact total count are evaluated by the
selected persistence provider against that same snapshot; inaccessible rows do not influence
totals or continuation behavior. A terminal page returns `hasMore: false` and `continuation: null`.

The snapshot binding is replayable only while retained and only for the original query and
authorization scope. A malformed or mismatched binding returns
`400 activity.management.cursor-invalid`. A valid binding below the retention floor returns
`410 activity.cursor.expired` with recovery instruction `restart-without-cursor`; the server never
silently restarts the list from the newest snapshot.

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
`409 activity.definition.content-authority`; an out-of-scope definition returns a privacy-safe
authorization or not-found response. Success returns `200 OK` with
`ActivityDefinitionIdentityView`.

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
  "headVersionId": "activity-ver-2",
  "recommendedVersionId": "activity-ver-2"
}
```

`forkedFrom` is null for definitions that were not created by a fork. It is audit provenance only: it does not grant authority over, or establish version lineage with, the source definition.
Disclosure-safe management projections also return it as null because source lineage identities may
no longer be visible to the caller; mutation responses may include it when the fork was authorized.

### Management summaries, detail, and action availability

Collection items are compact management summaries. A definition management view contains
`definition`, a `lifecycle` summary with authorized draft/version counts plus head and
recommendation references, `actions`, and `updatedAt`. A draft management view contains `draft`
summary plus `actions`; its draft summary includes `draftId`, `definitionId`, revision, source
version, status, provider key/schema, `updatedAt`, and optional `presentationLabel`. A version
management view contains `version`, provider key/schema, `isRecommended`, and `actions`. None of
these summaries contains contract state, provider payload, layout, or diagnostic free text.

`GET /design/activities/definitions/{definitionId}` returns one
`ReusableActivityDefinitionManagementView`. It contains `definition`, `lifecycle`, `actions`, and
`updatedAt`. It **does not** embed draft or version arrays. Clients follow
`activity-definition-drafts` and
`activity-definition-versions` and page those collections independently.

Every visible management resource carries a typed `actions` array. Each entry has this shape:

```json
{
  "action": "edit-draft",
  "allowed": true,
  "unavailableCode": null
}
```

`action` and `unavailableCode` are stable codes; clients never parse explanatory text. When
`allowed` is true, `unavailableCode` is null. The current action vocabulary includes definition
actions `edit-definition`, `create-draft`, `set-recommendation`, and `fork-definition`; draft
actions `edit-draft`, `edit-draft-label`, `discard-draft`, `validate-draft`, `publish-draft`,
`migrate-draft-provider`, `propose-contract`, `apply-contract-proposal`, and
`create-conflict-copy`; and version actions `clone-draft`, `fork-definition`,
`set-recommendation`, `retire-version`, `restore-version`, and `revoke-version`.

Action availability is an authorization-safe convenience projection, not a write precondition.
Every command rechecks authority, lifecycle, provider availability, and optimistic preconditions.
Actions and counts are returned only for resources already visible to the caller; errors never
echo an action map for a hidden target.

### `ActivityContractView`

```json
{
  "contractSchemaVersion": "1",
  "inputs": [
    {
      "referenceKey": "order",
      "name": "Order",
      "type": { "alias": "acme.orders.order", "collectionKind": "Single" },
      "isRequired": true,
      "isNullable": false,
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
      "type": { "alias": "Decimal", "collectionKind": "Single" },
      "isRequired": true,
      "isNullable": false,
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
  "manifestFingerprint": "sha256:...",
  "payload": {}
}
```

The payload is returned only to callers authorized to author that provider. The fingerprint is
always returned because it safely binds proposals and updates without disclosing the payload.
Catalog and dependency APIs never need the payload.

## 3. Create a definition and initial draft

```http
POST /design/activities/definitions
Content-Type: application/json
```

```json
{
  "category": "Orders",
  "displayName": "Calculate order total",
  "description": null,
  "activityTypeKey": "elsa.user.calculate-order-total.custom",
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

Response: `201 Created` with `ReusableActivityDefinitionMutationView` and `Location` pointing to
the definition. The response contains the one atomically created draft, not embedded collection
pages.

```json
{
  "definition": {},
  "draft": {}
}
```

Rules:

- The API always creates `ContentAuthority.Kind = Design`; source-owned definitions enter through trusted reconciliation commands, not this endpoint.
- The definition and initial draft are created atomically.
- `activityTypeKey` is optional. When omitted, the authoritative key policy generates it from the
  display name and new definition identity.
- Clients offer the advanced pre-creation override only when
  `activityTypeKeyRules.allowsPreCreationOverride` is `true`. A supplied value when the active
  policy disallows overrides returns `400 activity.request.invalid` and writes nothing.
- A supplied override is trimmed, Unicode Form C normalized, invariant-lowercased, and then
  validated by the authoritative key policy against its advertised prefix, pattern, and maximum
  length. Invalid values return `400 activity.definition.key-invalid` and write nothing.
- The normalized key is unique within the advertised `tenantId + activityTypeKey` collision scope.
  A collision returns `409 activity.definition.key-conflict`; the server never appends a suffix.
- The persisted `activityTypeKey` is immutable. No update route accepts it after creation.
- The initial draft has no required author-supplied name. Its optional presentation label may be
  set later and is neither identity nor a uniqueness boundary.

## 4. Review and apply a source-owned fork

```http
POST /design/activities/definitions/{definitionId}/fork-previews
```

```json
{
  "idempotencyKey": "fork-preview-01J...",
  "sourceVersionId": "activity-ver-clr-3",
  "category": "Orders",
  "displayName": "Calculate order total (custom)",
  "description": null,
  "targetProviderKey": "elsa.activity-graph",
  "targetProviderSchemaVersion": "1"
}
```

Response: `200 OK` with an `ActivityForkPreviewView`. It contains an opaque signed `candidateId`,
the normalized presentation, exact source identity and lifecycle, reserved target identities, the complete target
contract, source/target provider and contract fingerprints, safe ordered migration diagnostics,
access-binding evidence, and `createdAt`/`expiresAt`. Preview persists only the bounded reservation;
it does not create a definition, authoring state, draft, layout, or catalog projection.

The preview `idempotencyKey` is actor- and tenant-scoped. Concurrent or lost-response retries with
the same key and exact normalized material return the first reservation and the same server-created
target identities. Reusing the key for different material returns
`409 activity.fork.preview-idempotency-conflict`. An expired reservation returns
`410 activity.fork.preview-expired`; it never silently allocates replacement identities under the
same reviewed key.

```http
POST /design/activities/fork-candidates/{candidateId}/apply
```

```json
{
  "requestFingerprint": "sha256:...",
  "idempotencyKey": "fork-apply-01J..."
}
```

Response: `200 OK` with an `ActivityForkReceiptView`. Apply rechecks the signed candidate, actor,
tenant, access profile, expiry, exact source version/lifecycle/authority, provider migration, and activated
contract capabilities. One atomic commit consumes the reservation and creates the exact reserved
definition, Design authority state, draft, layout, management projection, and append-only receipt.
An exact replay returns `AlreadyApplied`; the receipt is never updated.

```http
GET /design/activities/forks/{idempotencyKey}
```

The status operation reads the actor- and tenant-scoped append-only terminal receipt. Clients use
it after an uncertain apply response before deciding whether any recovery action is needed.

Rules:

- The source exact version must be visible to the caller and remains unchanged.
- The new definition belongs to the caller's tenant and has `ContentAuthority.Kind = Design`.
- The source public contract is copied. Provider source is cloned or deterministically migrated through the requested target provider; if no supported conversion exists, the request returns `422 activity.provider.migration-unsupported` and creates nothing.
- The new definition records fork provenance for audit/inspection, but it is not part of the source definition's lineage or content authority.
- The fork receives a new server-generated immutable activity type key. Its initial draft does not
  require a user-supplied unique name.
- Candidates have explicit expiry and retention deadlines. A bounded admitted retention query
  deletes candidates after retention; the self-contained append-only receipt remains sufficient
  for status reconciliation.

## 5. Create or clone a draft

```http
POST /design/activities/definitions/{definitionId}/drafts
```

Fresh draft:

```json
{
  "sourceVersionId": null,
  "presentationLabel": null,
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
  "sourceVersionId": "activity-ver-2",
  "presentationLabel": "Try the new tax contract"
}
```

Response: `201 Created` with `ReusableActivityDraftView`. Cloning deep-copies contract, provider
manifest, and version layout. The source version remains immutable.

`presentationLabel` is optional, non-unique, and stored on the draft header outside behavior
state. Multiple drafts under one definition may use the same label or no label.

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
  "presentationLabel": "Try the new tax contract",
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
  "layout": [],
  "presentationLabel": "Try the new tax contract"
}
```

Response: `200 OK` with the complete draft at revision `8`.

Rules:

- This is full-state-always, consistent with workflow draft mutation; there is no JSON Patch contract.
- Contract, provider, layout, optional `presentationLabel`, and revision persist atomically.
  Autosave, label edits, provider proposals, and ordinary editing all share this one draft revision
  stream; there is no separate presentation revision or ETag. Validation remains a derived sibling
  keyed to an exact revision, so a new revision has no current validation until it is revalidated.
  Provider-neutral internal options are not a public wire field and remain unchanged.
- Every successful full-state update increments `revision` exactly once, including a label-only or
  layout-only update. A rejected write increments nothing.
- `providerKey` may change only for a Design-owned lineage; changing it is a behavioral change later classified by the version diff.
- A stale revision returns `409 activity.draft.stale-revision` with typed `ActivityRecoveryView`
  metadata. The source draft is unchanged.
- Every input and output member carries required boolean `isNullable`; omission is a malformed
  canonical request rather than an implicit default. Requiredness and nullability remain independent.
- Every mutable contract write accepts only activated catalog aliases, canonical collection-kind names, compatible storage drivers, nullability, and durability. `isNullable: true` is admitted only when the selected type advertises `supportsNull: true`; a null default additionally requires `isNullable: true`. Unsupported facts return `422 activity.contract.capability-rejected`; immutable historical reads remain exact.

### Presentation-label update

```http
PATCH /design/activities/drafts/{draftId}/presentation
Content-Type: application/json

{
  "expectedRevision": 8,
  "presentationLabel": "Alternative tax approach"
}
```

Response: `200 OK` with the complete draft at revision `9`. The label is optional and non-unique.
This route is a convenience mutation over the same autosave revision stream: it increments the
draft revision exactly once, updates the sibling layout revision atomically, and follows the same
stale-revision recovery contract as the full `PUT`. Full-state autosave also carries
`presentationLabel`, so clients do not need a second save stream. The new revision has no current
validation until it is revalidated.

### Conflict-copy recovery

After receiving an authorized stale-revision response, a client may preserve its reviewed full
local state as a new parallel draft:

```http
POST /design/activities/drafts/{draftId}/conflict-copies
Content-Type: application/json
```

```json
{
  "expectedSourceRevision": 8,
  "contract": {},
  "provider": {},
  "layout": [],
  "presentationLabel": "Recovered autosave copy"
}
```

Response: `201 Created` with the complete new `ReusableActivityDraftView`, revision `1`, and a
`Location` for that exact server-generated draft.

Rules:

- `expectedSourceRevision` must still equal the source draft's current revision. If another write
  wins before copy creation, the command returns `409 activity.draft.stale-revision` and writes
  nothing.
- The server rechecks current authorization, Design content authority, and source draft ownership.
  It does not overwrite or merge the source draft.
- The submitted contract, provider, layout, and optional `presentationLabel` are the complete
  reviewed public desired state. The new draft inherits the source draft's definition, tenant,
  immutable `SourceVersionId`, and provider-neutral internal options; the presentation label
  remains non-unique metadata outside behavior state.
- Creation of the draft header, behavior state, and layout is atomic. Failure writes nothing. The
  new revision has no current validation until it is validated. No endpoint accepts
  force-overwrite as conflict recovery.

## 7. Provider authoring capabilities and contract proposals

`GET /design/activities/authoring-capabilities` returns only providers the caller may author. The
snapshot includes provider schema authorability and migration metadata, required outcomes, the
provider-neutral type descriptor catalog, compatible storage drivers, canonical collection kinds,
nullability/durability facts, and server-owned activity type key rules. The deterministic
`snapshotFingerprint` lets clients invalidate cached editor configuration without inspecting an
opaque provider manifest.

`activityTypeKeyRules` advertises `serverGenerated`, `allowsPreCreationOverride`, `immutable`,
`prefix`, `pattern`, `maximumLength`, and `collisionScope`. Clients use
`allowsPreCreationOverride` to gate the create-only advanced control and never probe by submitting
an override when it is false.

Compatible storage-driver keys are intersected with Runtime's actually activated driver registry
through the Publishing bridge. A descriptor declaration alone cannot advertise or authorize an
unavailable driver.

This is a clean break for the pre-release authoring surface: normal create, clone, replace,
proposal-apply, and conflict-copy commands accept only the current capability-catalog contract
types. There is no legacy alias fallback, compatibility request shape, or workflow-definition-as-
activity ingress.

Proposal requests bind all mutable facts explicitly:

```json
{
  "expectedRevision": 7,
  "expectedProviderKey": "elsa.activity-graph",
  "expectedProviderSchemaVersion": "1",
  "expectedManifestFingerprint": "sha256:..."
}
```

The response contains typed `Add`, `Replace`, or `Remove` changes for public inputs, outputs, or
outcomes, plus diagnostics and a deterministic `proposalFingerprint`. It never returns the opaque
manifest and never mutates the draft. Apply submits the same exact binding, the reviewed proposal
fingerprint, and unique `selectedChangeIds`. The server reloads and recomputes the proposal before
applying only those changes. A stale revision, provider binding, manifest, proposal, or selection
fails closed with `409 activity.contract.proposal-stale`; there is no implicit provider mutation.
The proposal fingerprint covers both typed changes and the ordered safe diagnostics reviewed by the
author.

## 8. Validate a draft revision

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

## 9. Publish a draft

Publication is a two-step reviewed operation. Preflight binds all evidence to the exact draft
revision and definition head:

```http
POST /design/activities/drafts/{draftId}/publication-preflight
Content-Type: application/json
```

```json
{
  "expectedDraftRevision": 8,
  "expectedDefinitionHeadVersionId": "activity-ver-1",
  "version": "2.0.0"
}
```

The `200 OK` response contains one ordered diagnostic set, the impact-first diff, exact dependency
evidence, provider/storage/Runtime readiness, `minimumVersion`, suggested `validVersions`, and an opaque
`reviewToken`. `reviewedVersion` identifies the exact semantic version whose compiled template, diff,
and readiness evidence the token binds. The request's optional `version` selects that exact version.
When omitted, preflight selects and reviews the first available `validVersions` choice, recompiling
until the version-dependent evidence and suggestion agree. A provider whose suggestions do not
converge is rejected with guidance to preflight an explicit exact version. Clients choosing a higher
version also run preflight with that version before publishing. A first publication reports
`hasBaseline: false`, compares against the explicit definition baseline, and normally offers
`1.0.0`. Unknown change kinds and additive fields are forward-compatible; clients render their
supplied impact and safe description without rejecting the response. Provider payloads, Runtime
descriptors, values, expressions, and exception details are never included.

```http
POST /design/activities/drafts/{draftId}/publish
Content-Type: application/json
```

```json
{
  "expectedDraftRevision": 8,
  "expectedDefinitionHeadVersionId": "activity-ver-1",
  "version": "2.0.0",
  "reviewToken": "sha256:...",
  "idempotencyKey": "publish-operation-42"
}
```

For a first publication, `expectedDefinitionHeadVersionId` is `null`. Publish `version` must equal
the preflight response's `reviewedVersion`. The reviewed version may be any unique, exact SemVer with
precedence at or above `minimumVersion`; `validVersions` provides convenient presets rather than an
exhaustive finite set. The idempotency key is tenant-owned and bound to the complete reviewed
request, so the same textual key may be used independently in another tenant but cannot be reused
for different material in the same tenant. Receipt ownership follows the caller's current operation
tenant, including when that tenant is authorized to publish a global definition.

Response: `201 Created` after atomic publication, with `Location` pointing to
`/design/activities/publications/{idempotencyKey}`:

```json
{
  "idempotencyKey": "publish-operation-42",
  "status": "Applied",
  "draftId": "activity-draft-1",
  "expectedDraftRevision": 8,
  "expectedDefinitionHeadVersionId": "activity-ver-1",
  "reviewToken": "sha256:...",
  "requestedVersion": "2.0.0",
  "outcome": {
    "definitionId": "activity-def-1",
    "definitionVersionId": "activity-ver-2",
    "draftId": "activity-draft-1",
    "version": "2.0.0",
    "templateId": "activity-template-sha256-...",
    "templateHash": "sha256:...",
    "sourceReferenceId": "source-ref-...",
    "publishedAt": "2026-07-15T12:20:00Z"
  },
  "errorCode": null,
  "diagnostics": [],
  "updatedAt": "2026-07-15T12:20:00Z"
}
```

`GET /design/activities/publications/{idempotencyKey}` returns the same durable receipt. Terminal
statuses are `Applied`, `Rejected`, `Stale`, `Failed`, and `OutcomeUnknown`. A retry with the same
key and identical request returns the recorded outcome and never creates a second version. A key
bound to different material returns `409 activity.publication.idempotency-conflict`.

Publication rejection after semantic validation uses `422` Problem Details and includes all deterministic diagnostics that can be reported safely in one pass. Examples: contract mismatch, dependency cycle, insufficient SemVer, provider compilation failure, missing required Runtime consumer declaration, or tenant-invalid dependency.

Concurrency conflicts use `409`:

- `activity.draft.stale-revision`
- `activity.definition.stale-head`
- `activity.publication.review-stale`
- `activity.publication.idempotency-conflict`
- `activity.version.conflict`
- `activity.definition.content-authority`

The command recomputes the authoritative evidence inside the publication lock and compares the
review token before entering the transaction. The Applied receipt is committed atomically with the
version, head, template, Source Reference, layout, and dependencies. No rejected or stale
publication creates a version, advances a head, or exposes partial publication state.

## 10. Read an immutable version

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

## 11. Provider-manifest migration

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

Upgrade-plan updates and clones are mutable ingresses too: immediately before their atomic apply,
they recheck exact provider-schema authorability and the current contract capability catalog. An
immutable historical contract whose type or driver is no longer activated remains readable but
cannot silently become a new mutable draft.

## 12. Lifecycle commands

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

When the target is currently recommended, retire and revoke also require one explicit atomic recommendation decision:

- `Clear`: bind the exact definition head and current recommendation and leave the definition with no recommendation.
- `Replace`: additionally bind an exact same-definition replacement and its expected `Active` lifecycle.

Omitting that decision returns `409 activity.definition.recommendation-required`. Restore never changes recommendation.

### Recommendation command

```http
PUT /design/activities/definitions/{definitionId}/recommendation
Content-Type: application/json

{
  "expectedDefinitionHeadVersionId": "activity-ver-2",
  "expectedRecommendedVersionId": "activity-ver-1",
  "recommendedVersionId": "activity-ver-2",
  "expectedRecommendedVersionLifecycle": "Active",
  "reason": "Promote the reviewed version."
}
```

A null `recommendedVersionId` with a null expected target lifecycle is an explicit clear. Stale head, recommendation, or target lifecycle returns a stable `409` Problem Details code. The bounded picker never substitutes head/latest and omits null, retired, revoked, hidden, or inconsistent recommendations.

## 13. Activity draft test run

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

## 14. Runtime requirement preflight

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

## 15. Clean-break impact

The existing public “add a definition with an immediate version” and “add arbitrary version” authoring commands are not the Design-owned authoring contract for this feature. Trusted reconciliation retains an internal source-owned creation path. Public authoring flows through definition + draft + publication so validation, SemVer, dependencies, content authority, and template creation cannot be bypassed.
