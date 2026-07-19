# Data Model: Groundwork Design Persistence

**Work unit**: `093-groundwork-design-persistence`

## Modeling Rules

- The existing Elsa entities and store contracts remain authoritative. Physical tables and projected columns are implementation details and do not add members to core interfaces.
- Every stored type retains a canonical JSON representation containing its logical domain projection. Native columns are derived, versioned, and backfillable from stable serialized paths.
- Envelope identity, storage scope, document kind, schema version, concurrency version, created/updated timestamps, and canonical JSON are present in every relational physical form. MongoDB preserves the equivalent logical fields.
- Normal operations are scope-bound. Cross-scope access requires an explicit privileged session; a query flag alone cannot bypass isolation.
- Navigation properties and EF-only serialized shadow fields are not authoritative document content. Related aggregates are loaded through explicit named store calls.

## Physical Form Decisions

| Document kind | Logical content | Physical form | Native projected/query fields | Key invariants |
|---|---|---|---|---|
| `workflowDefinition` | `WorkflowDefinition` | Physical entity table | `id`, `tenantId`, `name`, `description`, `deletedAt` | Identity unique within scope; soft-delete metadata changes atomically; description may be absent. |
| `workflowDefinitionVersion` | `WorkflowDefinitionVersion` with logical `State`; excludes navigation and EF state-source representation | Physical entity table | `id`, `tenantId`, `definitionId`, `semVerSortKey`, `sourceCreatedAt` | `(definitionId, semVerSortKey)` unique within scope; version content and provenance immutable after insert. |
| `workflowDefinitionDraft` | Draft entity plus embedded layout records; validation results remain derived | Physical entity table | `id`, `tenantId`, `workflowDefinitionId`, `sourceVersionId`, `createdAt`, `lastModifiedAt` | Draft state/layout replace atomically; provenance write-once; current-draft order is deterministic. |
| `workflowDefinitionVersionLayout` | Immutable version layout records | Physical entity table | `id`, `tenantId`, `workflowDefinitionVersionId` | At most one layout per version within scope; content immutable after insert. |
| `activityDefinition` | `ActivityDefinition` | Physical entity table | `id`, `tenantId`, `activityTypeKey`, `category`, `displayName`, `description` | `activityTypeKey` unique within scope and write-once; descriptive fields may change. |
| `activityDefinitionVersion` | `ActivityDefinitionVersion` logical descriptor, inputs, outputs, and facets; excludes navigation and EF source columns | Physical entity table | `id`, `tenantId`, `definitionId`, `semVerSortKey`, `descriptorType`, `sourceKind`, `sourceId`, `hash` | `(definitionId, semVerSortKey)` unique; content/provenance immutable; hash mismatch is authoritative conflict. |
| `activityAvailabilitySettings` | Activity availability settings document | Dedicated document table | Envelope/scope fields only unless a measured query is added | Point-oriented mutable settings; no unproven query projection. |

The exact provider column names result from `feature default -> host naming policy -> explicit storage-unit override -> provider normalization`. Resolved names and the comparison-key algorithm/version participate in the schema fingerprint.

## Bounded Query Model

Each public store operation maps to a declared query identity. Stable paths below are relative to canonical JSON; the final manifest source owns the exact serialized casing.

### Workflow definitions

- Direct point read by envelope `id`.
- Equality or membership on `entity.id` and `entity.name`.
- Equality on `entity.description`.
- Disjunctive contains over `entity.name`, `entity.description`, and `entity.id`.
- Normal reads include scope; privileged reads use a separately acquired access context.

### Workflow versions

- Direct point read by envelope `id`.
- Equality on `entity.definitionId`.
- Compound equality on (`entity.definitionId`, `entity.semVerSortKey`) for existence.
- Descending `entity.semVerSortKey` with deterministic identity tie-break for latest version.
- List by definition is bounded by scope and definition identity.

### Workflow drafts

- Direct point read by envelope `id`.
- Equality or membership on `entity.workflowDefinitionId`.
- Current draft orders by `entity.lastModifiedAt`, `entity.createdAt`, and `entity.id`, all descending.
- List-projection input IDs are deduplicated and capped by the declared `IN` cardinality; larger caller sets are partitioned into deterministic bounded batches.

### Workflow version layouts

- Equality on `entity.workflowDefinitionVersionId`; first result only.

### Activity definitions

- Direct point read by envelope `id`.
- Equality or membership on `entity.id`, `entity.activityTypeKey`, `entity.category`, and `entity.displayName`.
- Contains on `entity.description`.
- Disjunctive contains across display name, activity type key, category, description, and id.
- Disjunctive equality for id-or-activity-type-key lookup.

### Activity versions

- Direct point read by envelope `id`.
- Equality or membership on `entity.definitionId`.
- Compound equality on (`entity.definitionId`, `entity.semVerSortKey`).
- Unfiltered listing is a declared bounded traversal with explicit paging, never a materialize-all adapter fallback.

## Relationships

```text
WorkflowDefinition 1 ─── * WorkflowDefinitionDraft
WorkflowDefinition 1 ─── * WorkflowDefinitionVersion
WorkflowDefinitionVersion 1 ─── 0..1 WorkflowDefinitionVersionLayout

ActivityDefinition 1 ─── * ActivityDefinitionVersion
```

Relationships are enforced by command orchestration, unique physical indexes, and atomic units of work. Document payloads do not embed mutable parent copies. The draft is the deliberate exception for its owned layout records because state and layout are one mutable aggregate boundary.

## State Transitions

### Workflow definition lifecycle

```text
Absent
  └─ Create/Add/Submit ─> Active definition + initial draft/version state (atomic)
Active
  ├─ Save metadata ─────> Active with updated metadata
  ├─ Soft delete ───────> Soft-deleted
  └─ Permanent delete ──> Absent, including related drafts/versions/layouts (atomic)
Soft-deleted
  ├─ Restore/save ──────> Active
  └─ Permanent delete ──> Absent
```

### Draft lifecycle

```text
Absent
  ├─ Create fresh ──────> Mutable draft (SourceVersionId = null)
  └─ Clone version ─────> Mutable draft (SourceVersionId fixed)
Mutable draft
  ├─ Update ────────────> Replaced state/layout after lock + validation gate
  ├─ Promote ───────────> Draft retained + immutable version/layout added atomically
  └─ Discard ───────────> Absent
```

### Activity catalog lifecycle

```text
Absent definition/version
  └─ Reconcile/add ─────> Definition + immutable version atomically
Existing logical version
  ├─ Same normalized SemVer + same hash ─> idempotent existing outcome
  └─ Same normalized SemVer + different hash ─> conflict; no write
```

## Validation Rules

- IDs, document kinds, query identities, stable paths, and physical logical names are non-empty and deterministic.
- SemVer strings are valid and their stored sort keys are produced by the one accepted algorithm.
- Normalized logical version identities ignore build metadata exactly as the public contract specifies.
- Projected values must be derivable from canonical JSON before provider I/O; unsupported or over-limit values fail validation rather than truncate.
- Unique indexes include storage scope so identical logical names may exist in distinct scopes.
- Every compound predicate/order query has a declared route and executable handler certification.
- Missing, null, and empty values retain the public query contract's distinctions.
- Multi-document commands require `CrossUnitAtomic`; unsupported deployments fail readiness.
- Stale expected versions return conflict and leave canonical JSON plus projected indexes unchanged.
- Deserialization or schema-version failure is a domain-scoped persistence error, not an empty result.

## Schema Evolution

1. The unified manifest source emits versioned physical definitions and bounded query declarations.
2. Groundwork resolves names and provider routes, includes algorithm versions, and fingerprints the target.
3. The schema tool reports additive tables/columns/indexes, backfills projected fields from canonical JSON, and validates current applied state.
4. Safe changes may be explicitly applied in deployment; destructive/semantic changes require exact plan-bound authorization.
5. Backfill completion and applied-state publication are restart-safe. Traffic is not served until every required route validates.

There is no EF-to-Groundwork data migration. Evolution begins from the Groundwork schema because the product is greenfield.
