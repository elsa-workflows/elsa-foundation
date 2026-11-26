# Data Model: Expression Code Intelligence

All models are transient design-time contracts unless explicitly identified as existing persisted workflow data.

## ExpressionDocument

| Field | Type | Rules |
|---|---|---|
| `id` | string | Stable opaque identity derived from backend + draft + activity + property + Expression Type |
| `uri` | string | Collision-free editor URI; contains no source |
| `draftId` | string | Existing workflow draft identifier |
| `activityId` | string | Stable activity/node identifier |
| `propertyKey` | string | Descriptor reference key, falling back to descriptor name |
| `expressionType` | string | Exact selected Expression Type |
| `source` | string | Existing persisted expression value; never rewritten by tooling |
| `sourceVersion` | integer | Monotonic in-memory version incremented for each source change |
| `contextVersion` | string | Version returned with the authoring context |

**Identity rule**: Surface is not part of identity. Compact and expanded surfaces address the same document.

## ExpressionEditorSession

| Field | Type | Rules |
|---|---|---|
| `documentId` | string | Session key |
| `editorState` | opaque internal value | CodeMirror state; never exported through the Studio SDK |
| `activeSurface` | `compact \| expanded \| none` | At most one active surface per document |
| `toolingState` | `loading \| ready \| empty \| unavailable \| unauthorized \| incompatible` | Distinct user-visible state |
| `diagnostics` | ExpressionDiagnostic[] | Shared set for compact/expanded presentation |
| `catalogVersion` | string? | Current reusable catalog version |
| `updatedAt` | number | Used only for workflow-lifetime eviction |

**Lifecycle**: Created on first activation; retained while the workflow editor is mounted; removed on workflow close, identity change, authorization invalidation, or bounded cache eviction.

## ExpressionAuthoringContext

| Field | Type | Rules |
|---|---|---|
| `version` | string | Changes whenever visible scope/shape facts change |
| `target` | ExpressionTarget | Property identity and expected result shape |
| `workflowInputs` | ExpressionSymbol[] | Design-time metadata only |
| `visibleVariables` | ExpressionSymbol[] | Scoped to the activity location |
| `visibleActivityOutputs` | ExpressionSymbol[] | Scoped to predecessor/output visibility |
| `shapeReferences` | string[] | Lazy-resolvable shape IDs |

**Security rule**: Contains names, types, signatures, and documentation only; never runtime values or evaluated expression results.

## ExpressionToolingDescriptor

| Field | Type | Rules |
|---|---|---|
| `expressionType` | string | Provider dispatch key |
| `moduleId` / `moduleVersion` | string | Backend provider identity/version |
| `contractMinVersion` / `contractMaxVersion` | integer | Negotiation range |
| `capabilities` | capability map | Explicit boolean for highlighting, completion, hover, signatures, formatting, local diagnostics, semantic validation |
| `catalogVersion` | string? | Changes when built-ins/policy projection changes |
| `permissionRevision` | string? | Cache-isolation/invalidation input |
| `hostPolicyRevision` | string? | Cache-isolation/invalidation input |

## ExpressionSymbol

| Field | Type | Rules |
|---|---|---|
| `id` | string | Opaque within a catalog/context version |
| `name` | string | Language-neutral symbol name |
| `kind` | enum | value, function, filter, tag, namespace, member, keyword |
| `documentation` | Markdown string? | Sanitized before display |
| `shapeId` | string? | Reference to ExpressionValueShape |
| `signatures` | ExpressionSignature[] | Parameters and return shape references |
| `parentId` | string? | Hierarchical member relation |
| `sortText` | string? | Backend hint; expected-shape ranking remains additive |

## ExpressionValueShape

| Field | Type | Rules |
|---|---|---|
| `id` | string | Stable within module/catalog version |
| `kind` | enum | unknown, scalar, object, collection, callable |
| `displayName` | string? | User-visible type label |
| `nullable` | boolean | Nullability metadata |
| `scalarType` | string? | string, number, boolean, date/time, etc. |
| `elementShapeId` | string? | Collection element reference |
| `members` | ExpressionShapeMember[] | Bounded member page or inline small set |
| `additionalMembers` | boolean | Indicates searchable/lazy members |

Recursive graphs use IDs and references; they are never embedded without bounds.

## ExpressionDiagnostic

| Field | Type | Rules |
|---|---|---|
| `origin` | `local \| semantic` | Authority source |
| `severity` | `info \| warning \| error` | Error gates consequential actions |
| `code` | string? | Stable machine-readable code |
| `message` | string | Never emitted to telemetry |
| `range` | start/end line/column | Tied to exact source version |
| `documentId` | string | Target document |
| `sourceVersion` | integer | Required for stale-response rejection |
| `contextVersion` | string | Required for stale-response rejection |
| `catalogVersion` | string? | Required when catalog-dependent |

## ToolingResponseState

`supported-empty`, `ready`, `unavailable`, `unauthorized`, and `incompatible` are distinct states. Only `ready` and `supported-empty` may contain successful data. `unauthorized` must trigger removal of affected cached/source data.

## State Transitions

```text
unfocused preview -> focus -> loading -> ready | supported-empty | unavailable | unauthorized | incompatible
compact single-line -> newline introduced -> expanded (same session)
compact -> Enter without active completion -> expanded (same session)
compact/expanded -> source change -> sourceVersion++ -> cancel stale requests -> validate idle/blur
permission or Host Policy revision -> invalidate -> reauthorize -> ready | unauthorized
workflow close -> dispose editor sessions and tooling caches
```
