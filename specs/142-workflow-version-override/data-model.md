# Data Model: Workflow Version Override

## Promotion request

The public promotion request is identified by the draft in the route and may contain:

| Field | Type | Required | Meaning |
|---|---|---:|---|
| `operationKey` | string | No | Opaque caller-generated retry identity. When absent, the server creates a fresh key, preserving existing non-deduplicated behavior. |
| `requestedVersion` | string | No | Exact requested SemVer label. Absent means automatic next-major assignment; present means exact assignment after normalization and validation. |

`draftId` is supplied by the route rather than a second body field.

## Version preflight assessment

`POST .../promotion-preflight` accepts `requestedVersion` but never accepts or creates an operation key. It is a read-only assessment with these fields:

| Field | Meaning |
|---|---|
| `isReady` | `true` only when the current draft passes promotion validation and the candidate currently passes SemVer, forward-precedence, and identity checks. |
| `assignmentMode` | `automatic` for an absent request, otherwise `exact`. |
| `requestedVersion` | Normalized explicit candidate, or `null` for automatic mode. |
| `resolvedVersion` | Automatic next-major or accepted exact candidate when one can be resolved; otherwise `null`. |
| `latestVersion` | Latest immutable version observed for the definition, or `null` when none exists. |
| `issues` | Stable machine-readable assessment issues with policy-safe messages; includes draft validation, invalid-version, or version-conflict details. |

The assessment is advisory. It neither reserves a version nor binds an operation key; promotion repeats every acceptance check atomically.

### Normalization and validation

| Rule | Preflight assessment | Promotion outcome |
|---|---|---|
| `requestedVersion` is absent | `automatic`; resolve `NextMajor(latest?.Version)`. | Commit the same automatic policy under lock. |
| `requestedVersion` is present | Trim surrounding whitespace once. The trimmed value is the exact label candidate. | Use the same normalization in durable request material. |
| Candidate is empty or `SemVer.TryParse` fails | `isReady: false`, `invalid-version` issue. | Invalid request; no writes. |
| Candidate sort key is less than or equal to latest | `isReady: false`, `not-forward` issue. | Invalid request; no writes. |
| Candidate sort key already exists | `isReady: false`, `version-conflict` issue. | Conflict; no writes. |
| Candidate passes all checks | `isReady: true` and resolved candidate. | Store accepted trimmed label and derived sort key. |

Leading zeroes are invalid under the shared SemVer parser. Prerelease identifiers are valid if their full precedence is forward. Build metadata is retained in an accepted label only when it does not collide by precedence; a build-metadata-only alternative has the same sort key and conflicts.

## Promotion operation material

The durable atomic-write ledger binds one operation key to canonical promotion material:

| Field | Meaning |
|---|---|
| `draftId` | The source mutable draft. |
| `assignmentMode` | `automatic` or `exact`; prevents automatic and exact requests from being treated as the same retry. |
| `requestedVersion` | Normalized explicit label for `exact`, otherwise `null`. |

The material deliberately contains selected intent, not the computed automatic value. A replay of an automatic request returns the original committed result even if later promotions changed the current latest version. Reusing a key with different mode or normalized version material conflicts.

## Workflow definition version

`WorkflowDefinitionVersion` remains immutable after promotion.

| Field | Existing role | Requirement for this feature |
|---|---|---|
| `DefinitionId` | Owner identity | Scope all ordering and uniqueness checks to this definition and tenant access context. |
| `Version` | Human-facing version label | Store the server-accepted automatic or trimmed exact label. |
| `SemVerSortKey` | Persisted precedence/identity key | Derive with the shared SemVer model; use for latest lookup and unique identity. |
| `SourceDraftId` | Authored source trace | Continue to record the promoted draft. |
| `State` / `StateSource` | Immutable authored workflow content | Continue to be created only after the existing validation gate passes. |

No new persistent entity is required. The existing unique index on `(definitionId, semVerSortKey)` provides semantic identity, while the definition-level lock prevents routine competing assignments.

## State transition

```text
Draft
  │  preflight(requestedVersion?) ─────► current advisory assessment; no write or reservation
  │
  │  promote(operationKey, requestedVersion?)
  ▼
Validate draft + recheck version under definition lock
  │
  ├─ invalid draft ───────────────► 409 validation outcome; no version
  ├─ malformed/non-forward request ► 400 invalid-version outcome; no version
  ├─ occupied/racing identity ─────► 409 version-conflict outcome; no version
  └─ accepted ─────────────────────► immutable Version + layout + operation marker committed
                                       │
                                       └─ identical replay returns this Version
```

Publication remains a separate state transition: a promoted version is not made executable or assigned to a publication channel by this feature.
