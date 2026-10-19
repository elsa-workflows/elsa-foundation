# Data model: Publish imported Elsa 3 reusable activities

**Spec**: [spec.md](spec.md) · **Research**: [research.md](research.md)

No new table and no migration. Changes are to in-memory models, one JSON document field, and one optional request
field.

## Changed

### `PublishActivityDefinitionRequest` (Elsa.Workflows.Publishing.Api)

| Field | Change | Rule |
|---|---|---|
| `RequestedVersionId` | **new**, `string?`, trailing optional | Blank → request invalid. Present → the catalog version, publication, layout, edges and source reference use it. Already in use → `PublicationConflict` (existing). Included in the request fingerprint. |

### `ReusableActivityImportReceipt` (Imp)

| Field | Change | Rule |
|---|---|---|
| `Warnings` | **persisted** (the codec no longer strips it) | Additive, nullable. Rows at schema `1.0.0` without it read as `null`. No schema bump. |
| `Publications` | **new**, `IReadOnlyList<ReusableActivityPublicationState>?`, never stored | `WhenWritingNull`, stripped by the codec. Filled by apply and replay (publish phase results, in publish order) and by a status read (live: `AlreadyPublished` or `NotPublished`). Null when the selection has no reusable version. |

### `ReusableActivityImportApplyResult` (Imp)

| Field | Change | Rule |
|---|---|---|
| `Warnings` | on replay, read from the stored receipt | Same as the committing apply (FR-012). Doc comment "never persisted" removed. |

### Materializer output (Elsa3.Mapping → Imp)

| Model | Change |
|---|---|
| `ImportedActivity` | Drops the catalog `ActivityDefinitionVersion` row. Gains `Contract` (`ActivityContract`), `GraphManifest` (`JsonElement`), `ActivityDefinitionVersionId`, `Version` (`"{n}.0.0"`), `SourceVersionId`, `DependsOn` (version ids). |
| Authoring row | `HeadVersionId = null` for a new definition. No authoring write for an existing definition. |

### Analyzer (Imp)

| Change | Rule |
|---|---|
| New code `ELS3-REUSABLE-UNSUPPORTED-START-TRIGGER` | A reusable source with any `CanStartWorkflow` node. Blocking. `DirectStarts` is still inventoried. |

## New

### `ReusableActivityPublicationState` (Imp, record)

| Field | Type | Notes |
|---|---|---|
| `ActivityDefinitionVersionId` | string | The plan's deterministic id. |
| `SourceVersionId` | string | The Elsa 3 version. |
| `Status` | `ReusableActivityPublicationStatus` | `Published`, `AlreadyPublished`, `Refused`, `NotPublishedDependency`; `NotPublished` only on a status read |
| `ErrorCode` | string? | Set for `Refused`: the publisher's code, or `ELS3-REUSABLE-IDENTITY-COLLISION`. |
| `Diagnostics` | `IReadOnlyList<ActivityDiagnostic>` | The publisher's diagnostics for `Refused`; empty otherwise. |
| `BlockedBy` | string? | Set for `NotPublishedDependency`: the dependency's version id. |

**State transitions per version, per apply:**

```text
                ┌─ active publication with the plan's id and version ──► AlreadyPublished
start ──────────┼─ active publication with the plan's id, other version ─► Refused (IDENTITY-COLLISION)
                ├─ a dependency is not Published/AlreadyPublished ───────► NotPublishedDependency
                └─ draft → preflight → reviewed publish ─┬─ applied ─────► Published
                                                         └─ rejected ────► Refused (publisher code)
```

A later apply of the same plan starts again from `start` for every version (R-5, R-6).

### `IReusableActivityImportPublisher` (Imp, replacement contract, no default)

See [contracts/import-publisher-port.md](contracts/import-publisher-port.md).

### Draft written for an imported version

| Field | Value |
|---|---|
| `Id` | `ReusableActivityImportIdentity.Create("activity-draft", sourceVersionId)` |
| `DefinitionId` | the plan's definition id |
| `Revision` | 1 |
| `SourceVersionId` | the head at creation, or null |
| `PresentationLabel` | `Elsa 3 v{n}` |
| `Status` | `Active` (the publisher sets `Published`) |
| `State` | `ActivityDefinitionDraftState(contract, Provider("elsa.activity-graph", "2", graphManifest), options: default)` |
| Layout | `ActivityDefinitionDraftLayout(draftId, Revision 1, one empty record per graph node)` |
| Tenant | the import's tenant |

## Diagnostic codes

| Code | Severity | Where |
|---|---|---|
| `ELS3-REUSABLE-UNSUPPORTED-START-TRIGGER` | error | analyzer (D-6) |
| `ELS3-REUSABLE-PUBLISHING-UNAVAILABLE` | error | apply, when no `IReusableActivityImportPublisher` is registered and the selection has a reusable version |

Details: [contracts/diagnostic-codes.md](contracts/diagnostic-codes.md).
