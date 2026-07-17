# Contract: Problem Details and Activity Diagnostics

Elsa's shared FastEndpoints layer already returns RFC 7807 `application/problem+json`. This feature extends that envelope; it does not introduce a second error format.

## 1. Canonical envelope

```json
{
  "type": "https://elsa.dev/problems/activity-publication-invalid",
  "title": "Activity publication was rejected",
  "status": 422,
  "detail": "The draft cannot be published until 3 errors are resolved.",
  "instance": "/design/activities/drafts/activity-draft-1/publish",
  "errorCode": "activity.publication.invalid",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "diagnostics": []
}
```

Rules:

- Standard RFC 7807 members retain their standard meaning.
- `errorCode` is the stable machine-action code for the failed operation. Clients branch on this value, not on `title` or `detail`.
- `traceId` correlates the response to logs/traces and is not a domain identity.
- `diagnostics` is an ordered array. It may be empty for failures that have no safe structured detail.
- `recovery` is an optional typed recovery object. It is omitted when the server has no safe
  machine-actionable recovery to advertise.
- Existing framework validation members may remain additive, but activity authoring clients use `diagnostics` as the cross-domain location model.
- Unknown top-level extensions and unknown diagnostic fields must be ignored by clients.

### Typed recovery

```json
{
  "currentRevision": 8,
  "relation": "activity-draft-conflict-copies",
  "href": "design/activities/drafts/activity-draft-1/conflict-copies",
  "instruction": "review-current-revision-and-create-conflict-copy"
}
```

All four fields are optional for forward-compatible recovery kinds. Clients branch on stable
`relation` and `instruction` values rather than parsing `detail`, diagnostic messages, or URLs. For
a stale draft revision, `currentRevision` is the revision the server actually observed, `relation`
identifies the capability relation, and `href` is the resolved recovery target.

## 2. `ActivityDiagnosticView`

```json
{
  "code": "activity.dependency.cycle",
  "severity": "Error",
  "message": "Publishing this draft would create a dependency cycle.",
  "subject": {
    "kind": "ActivityDraft",
    "id": "activity-draft-1",
    "definitionId": "activity-def-a",
    "versionId": null,
    "revision": 8
  },
  "location": {
    "providerKey": "elsa.activity-graph",
    "jsonPointer": "/rootActivity/structure/payload/activities/3",
    "referenceKey": null,
    "nodeOrigin": [
      { "kind": "ActivityDraft", "id": "activity-draft-1" },
      { "kind": "AuthoredNode", "id": "node-b" }
    ],
    "dependencyPath": [
      {
        "definitionId": "activity-def-a",
        "versionId": "activity-ver-a2",
        "version": "2.0.0",
        "templateHash": "sha256-a"
      },
      {
        "definitionId": "activity-def-b",
        "versionId": "activity-ver-b1",
        "version": "1.0.0",
        "templateHash": "sha256-b"
      },
      {
        "definitionId": "activity-def-a",
        "versionId": "activity-ver-a2",
        "version": "2.0.0",
        "templateHash": "sha256-a"
      }
    ]
  },
  "remediation": "Choose an acyclic exact version or remove the reference.",
  "metadata": {
    "cycleLength": "2"
  }
}
```

### Fields

| Field | Required | Contract |
|---|---|---|
| `code` | yes | Stable lower-case namespaced identifier. Platform codes begin `activity.`; provider codes begin with the provider's namespace. |
| `severity` | yes | `Info`, `Warning`, or `Error`. Only errors block publication unless a stricter provider rule explicitly promotes a warning. |
| `message` | yes | Human-readable, localized in future, never parsed by clients. |
| `subject` | yes | Identity of the primary object being validated. |
| `location` | no | Structured location inside a contract, provider manifest, graph, or dependency chain. |
| `remediation` | no | Safe concise next action; advisory, not executable code. |
| `metadata` | yes | Allowlisted string-to-string values only. Empty object when unused. |

### `DiagnosticSubjectView`

```json
{
  "kind": "ActivityDraft",
  "id": "activity-draft-1",
  "definitionId": "activity-def-1",
  "versionId": null,
  "revision": 8
}
```

Allowed `kind` values in this feature:

- `ActivityDefinition`
- `ActivityDraft`
- `ActivityVersion`
- `ActivityTemplate`
- `WorkflowDraft`
- `WorkflowVersion`
- `WorkflowArtifact`
- `WorkflowExecution`
- `ActivityExecution`
- `MigrationCollection`

Fields that do not apply are omitted or null. `id` is always the primary subject identity.

### `DiagnosticLocationView`

```json
{
  "providerKey": "elsa.activity-graph",
  "jsonPointer": "/contract/inputs/0/default/value",
  "referenceKey": "order",
  "nodeOrigin": [
    { "kind": "AuthoredNode", "id": "node-7" }
  ],
  "dependencyPath": []
}
```

Rules:

- `jsonPointer` is RFC 6901 relative to the authorized public request/view, never to a hidden persistence document.
- `referenceKey` identifies a public input, output, or outcome independently of display name or list order.
- `nodeOrigin` is an ordered provider-neutral path of `{kind,id}` segments. It is diagnostic provenance, not the executable-node identity algorithm.
- `dependencyPath` contains complete exact-version/template identities when a dependency failure is causal. The first and last entries may be equal to show a cycle.
- Provider-specific line/column facts may be added as optional scalar fields later; clients ignore unknown fields.

## 3. Deterministic ordering

Responses order diagnostics by:

1. severity (`Error`, `Warning`, `Info`),
2. `code` ordinal,
3. subject kind and id ordinal,
4. `jsonPointer`/`referenceKey` ordinal,
5. dependency path identity ordinal.

This makes CI output and publication retries stable. Providers return findings; the publication coordinator performs final ordering.

## 4. HTTP status and operation error codes

| Status | Error code | Meaning |
|---|---|---|
| `400` | `activity.request.invalid` | Malformed route/body, invalid enum/syntax envelope, or impossible request combination. |
| `400` | `activity.management.cursor-invalid` | Management continuation is malformed or belongs to another authorization/filter snapshot. Recovery instructs the client to restart without a cursor. |
| `403` | `activity.authorization.denied` | Caller lacks authoring/lifecycle/inspection permission. |
| `403` | `activity.tenant.reference-denied` | An exact identifier resolves but is outside allowed tenant/global visibility. The response does not disclose unauthorized target details. |
| `404` | `activity.definition.not-found` | Definition absent in authorized scope. |
| `404` | `activity.draft.not-found` | Draft absent in authorized scope. |
| `404` | `activity.version.not-found` | Version absent in authorized scope. |
| `404` | `activity.execution.not-found` | Runtime execution or requested boundary absent in authorized scope. |
| `409` | `activity.definition.key-conflict` | Duplicate tenant/key identity. |
| `409` | `activity.definition.content-authority` | General authoring command attempted against a source-owned lineage. |
| `409` | `activity.draft.stale-revision` | Expected optimistic revision differs from current revision. |
| `409` | `activity.definition.stale-head` | Expected definition head differs under publication lock. |
| `409` | `activity.publication.review-stale` | Draft, head, compiled evidence, readiness, diagnostics, or valid version choices changed after preflight. Run preflight again. |
| `409` | `activity.publication.idempotency-conflict` | The operation key is already bound to different reviewed publication material. |
| `409` | `activity.version.conflict` | Requested semantic version already exists for the definition. |
| `409` | `activity.version.stale-lifecycle` | Lifecycle command observed a different current state. |
| `409` | `activity.upgrade.stale-plan` | At least one pinned draft revision/head changed before apply. |
| `409` | `activity.cursor.binding-mismatch` | A non-management opaque cursor belongs to another tenant, root, query, or authorization profile. |
| `410` | `activity.cursor.expired` | A retained management snapshot, non-management cursor snapshot, or watermark is no longer valid. Recovery may instruct the client to restart without a cursor. |
| `422` | `activity.publication.invalid` | One or more deterministic publication diagnostics block publication. |
| `422` | `activity.contract.capability-rejected` | A mutable contract uses a type, collection kind, storage driver, durability, or nullability fact unavailable in the activated authoring capability catalog. |
| `422` | `activity.version.choice-invalid` | Requested version was not one of the exact choices in the reviewed preflight. |
| `422` | `activity.runtime.consumer-missing` | A required Runtime consumer is not registered. Usually included under publication invalid. |
| `422` | `activity.runtime.consumer-schema-unsupported` | A Runtime consumer exists but not for the required exact schema. Usually included under publication invalid. |
| `422` | `activity.runtime.storage-driver-missing` | A required durable value storage driver is not registered. Usually included under publication invalid. |
| `422` | `activity.version.bump-insufficient` | Requested SemVer does not meet the calculated minimum. Usually included under publication invalid. |
| `422` | `activity.dependency.cycle` | Exact dependency cycle detected. Usually included under publication invalid. |
| `422` | `activity.provider.compilation-failed` | Provider rejected or could not deterministically compile valid source. |
| `422` | `activity.provider.migration-unsupported` | No deterministic migration exists for the requested provider schema transition. |
| `422` | `activity.admission.rejected` | Host/tenant policy rejects measured resource requirements. |
| `500` | `activity.operation.failed` | Unexpected domain operation failure after infrastructure exceptions are wrapped/logged. Details remain non-disclosing. |
| `500` | `activity.publication.outcome-unknown` | The server cannot prove whether an operation was applied; query the receipt before retrying with a new key. |
| `503` | `activity.runtime.requirement-unavailable` | Requested activation cannot proceed because a required Runtime consumer/schema is unavailable. Runtime also records an activation incident. |

An endpoint may choose the operation-level code (`activity.publication.invalid`) while individual diagnostics carry more specific codes (`activity.dependency.cycle`, `activity.version.bump-insufficient`).

Nullability-specific mutable-contract diagnostics are:

- `activity.contract.nullability-unavailable` at the member's `/isNullable` location when a nullable
  member is requested for a type whose capability has `supportsNull: false`.
- `activity.contract.null-default-not-allowed` at the input default value when a null default is
  requested for a member whose own `isNullable` value is false.

Both are deterministic errors. The rejected operation writes no draft, version, proposal
application, conflict copy, or reconciliation publication.

## 5. Validation response versus error response

The explicit validation operation returns `200 OK` and a validation result even when `isValid` is false, because findings are the requested representation.

```json
{
  "draftId": "activity-draft-1",
  "revision": 8,
  "isValid": false,
  "validatedAt": "2026-07-15T12:15:00Z",
  "diagnostics": []
}
```

Publication returns `422` when those or additional in-lock findings block the state transition.

## 6. Conflict example

```json
{
  "type": "https://elsa.dev/problems/activity-draft-stale-revision",
  "title": "Activity draft revision is stale",
  "status": 409,
  "detail": "The draft changed after the submitted revision was read.",
  "instance": "/design/activities/drafts/activity-draft-1",
  "errorCode": "activity.draft.stale-revision",
  "traceId": "00-...",
  "recovery": {
    "currentRevision": 8,
    "relation": "activity-draft-conflict-copies",
    "href": "design/activities/drafts/activity-draft-1/conflict-copies",
    "instruction": "review-current-revision-and-create-conflict-copy"
  },
  "diagnostics": [
    {
      "code": "activity.draft.stale-revision",
      "severity": "Error",
      "message": "Expected revision 7 but the current revision is 8.",
      "subject": {
        "kind": "ActivityDraft",
        "id": "activity-draft-1",
        "definitionId": "activity-def-1",
        "revision": 8
      },
      "location": null,
      "remediation": "Reload the draft and reapply the intended change.",
      "metadata": {
        "expectedRevision": "7",
        "actualRevision": "8"
      }
    }
  ]
}
```

## 7. Disclosure rules

Diagnostics and Problem Details MUST NOT include:

- opaque provider manifest payloads,
- compiled Runtime descriptor payloads,
- captured sensitive input/output values,
- stack traces or infrastructure exception messages,
- unauthorized tenant identifiers or target metadata,
- secrets embedded in expression source or reference resolution.

Provider validators are given a diagnostic builder that accepts the stable fields above; raw provider exceptions are wrapped into provider-scoped domain failures before crossing the feature boundary.

Management and authoring failures apply the same disclosure boundary:

- `404` means the resource is absent in the caller's authorized scope and does not confirm whether
  a hidden resource exists.
- `403` means the visible operation or exact tenant reference is denied. Its title, detail,
  diagnostics, and recovery remain generic and do not reveal hidden resource facts.
- Neither status returns hidden display names, identifiers, collection counts, action availability,
  provider facts, or recovery links whose presence would disclose a protected target.
