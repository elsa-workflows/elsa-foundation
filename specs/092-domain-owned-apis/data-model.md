# Data Model: Domain-Owned Management APIs

This document defines the durable and wire-level models needed by spec 092. Type names are proposals;
the ownership, identities, state transitions, and invariants are normative.

## Ownership summary

| Model | Owner | Persistence authority |
|---|---|---|
| API capability declaration/document | API Capabilities | Shell composition; declarations are normally static |
| Publication policy, slot, publication record, trigger claim, projection intent | Publishing | Publishing persistence |
| Workflow executable, trigger binding, recurring schedule | Runtime | Runtime persistence |
| Executable source reference | Runtime provenance; Publishing mutates Published/TestRun references | Runtime persistence |
| Workflow execution state and its executable retention root | Runtime | Runtime persistence |

Design definitions, drafts, and immutable definition versions remain Design-owned and are source
identities referenced by Publishing. They are not duplicated into the publication model.

## Capability discovery

### `ApiCapabilityDeclaration`

One stable client-visible promise contributed by an active shell feature.

| Field | Type | Rules |
|---|---|---|
| `CapabilityId` | `string` | Required, globally stable, ordinal identity; not derived from a feature or CLR type name |
| `ContractMajorVersion` | positive integer | Required; changes only for a breaking capability-contract revision |
| `Links` | collection of `ApiCapabilityLink` | Canonical shell-relative domain links; relation names are unique within the declaration |
| `SourceFeatureId` | `string` | Diagnostic composition identity; not part of the public capability identity |

`ApiCapabilityLink` contains a stable relation and a shell-relative URI template. Links do not carry
caller permissions, arbitrary domain state, or rich bootstrap payloads.

Static declarations are explicit metadata on active features. An `IApiCapabilityProvider` may contribute
the same shape for operationally conditional capabilities, but it must not infer declarations from feature
names. Duplicate `(CapabilityId, ContractMajorVersion)` declarations are valid only when bit-for-bit
equivalent; conflicting declarations fail shell startup.

### `ApiCapabilitiesDocument`

The aggregated response from the shell's single `/capabilities` endpoint.

| Field | Type | Rules |
|---|---|---|
| `Capabilities` | collection of `ApiCapabilityView` | Unique by capability ID and ordered ordinally |

Aggregation is shell-scoped and caller-neutral. Absence means unsupported by that shell. Endpoint-level
authorization remains authoritative after discovery.

## Publication policy and resolution

### `PublicationPolicy`

Publishing-owned policy controlling how an ordinary publish request is resolved.

| Field | Type | Rules |
|---|---|---|
| `WorkflowDefinitionId` | `string?` | Null for the host default; otherwise the per-workflow override |
| `DefaultAction` | `ReplaceDefaultSlot` or `RequireExplicitSlot` | Side-by-side publication is never silently selected |
| `DefaultSlotName` | `string` | Defaults to `default`; validated by the slot-name rules |
| `Revision` | non-negative integer | Optimistic-concurrency token for mutable per-workflow policy |
| `UpdatedAt` | timestamp | Audit fact |

An explicit publish request may provide a slot name and action. Resolution precedence is:

```text
explicit request > per-workflow policy > host policy
```

The result is a non-persisted `ResolvedPublicationAction` containing the definition/version, chosen slot,
replacement or named-side-by-side action, policy source, and policy revision. Studio displays this result
before confirmation. A side-by-side action requires a nonblank, meaningful slot name other than an
implicitly generated identifier.

## Publication authority

### `PublicationSlot`

The sole authority for which publication may start new executions in one named lane.

| Field | Type | Rules |
|---|---|---|
| `SlotId` | `string` | Deterministic identity derived without ambiguity from `(WorkflowDefinitionId, SlotName)` |
| `WorkflowDefinitionId` | `string` | Required |
| `SlotName` | `string` | Required; ordinal identity; `default` is reserved for ordinary replacement publication |
| `ActivePublicationId` | `string?` | Null only for an empty/unpublished slot |
| `Revision` | non-negative integer | Incremented by every successful activation/unpublish CAS |
| `UpdatedAt` | timestamp | Audit fact |

There is exactly one slot for a `(WorkflowDefinitionId, SlotName)` pair and at most one active publication
in it. A slot does not own or delete an executable; it selects a `PublicationRecord`.

### `PublicationRecord`

One immutable publication attempt plus its controlled lifecycle facts.

| Field | Type | Rules |
|---|---|---|
| `PublicationId` | `string` | Unique attempt identity |
| `SlotId` | `string` | Required target slot |
| `WorkflowDefinitionId` | `string` | Must match the slot |
| `WorkflowDefinitionVersionId` | `string` | Persisted Design version used as source |
| `ArtifactId` | `string` | Content-addressed Runtime executable |
| `SourceReferenceId` | `string?` | Assigned no later than activation; points back to this publication |
| `ExpectedSlotRevision` | non-negative integer | CAS value observed when the attempt began |
| `Status` | `Candidate`, `PendingProjection`, `Active`, `Retired`, or `Failed` | State machine below |
| `CreatedAt`, `ActivatedAt`, `RetiredAt` | timestamps | Lifecycle audit facts |
| `Failure` | structured code and safe message, optional | Present only for `Failed` |

Publication history is append-only except for state-transition fields. Reusing a content-addressed artifact
does not reuse a publication ID.

### `PublicationTriggerClaim`

Candidate and historical trigger ownership derived during publication preflight.

| Field | Type | Rules |
|---|---|---|
| `ClaimId` | `string` | Deterministic from publication, executable node, stimulus type, and stimulus hash |
| `PublicationId` | `string` | Required publication identity |
| `ArtifactId` | `string` | Required executable identity |
| `ExecutableNodeId` | `string` | Required trigger node |
| `StimulusType`, `StimulusHash` | `string` | Normalized stimulus identity |
| `Cardinality` | `Exclusive` or `FanOut` | Declared by the owning trigger provider |
| `Metadata` | string map | Provider-owned allowlisted routing metadata |

HTTP endpoint claims are `Exclusive`. Event and timer providers may declare `FanOut`. Preflight compares an
exclusive candidate against claims belonging to all authoritative slots, excluding only the active
publication being replaced in the candidate's own slot. Sharing a definition ID is not an exemption.

### `PublicationProjectionIntent`

A durable outbox/reconciliation instruction used when every serving projection cannot participate in the
publication transaction.

| Field | Type | Rules |
|---|---|---|
| `IntentId` | `string` | Idempotent delivery identity |
| `PublicationId` | `string` | Candidate publication |
| `ProjectionKind` | `TriggerBindings`, `RecurringSchedules`, `HttpRoutes`, or extension value | Identifies one projection owner |
| `Operation` | `Prepare`, `Activate`, or `Remove` | Projection lifecycle operation |
| `Status` | `Pending`, `Delivering`, `Delivered`, or `Failed` | Durable delivery state |
| `AttemptCount`, `NextAttemptAt`, `LastFailure` | retry facts | Follow the runtime outbox's bounded safe-diagnostic conventions |

Prepared candidate projections are not visible to stimulus routing. A publication remains
`PendingProjection`, and the prior slot publication remains authoritative, until every required preparation
has completed. The final authority switch and serving-projection visibility change must be atomic from a
starter's perspective. An API may report `Pending`; it must not report publication success early.

## Runtime projections and provenance

### `WorkflowTriggerBinding` (extended)

Existing artifact/node/stimulus fields remain. Add:

| Field | Type | Rules |
|---|---|---|
| `PublicationId` | `string` | Required for publication-created bindings |
| `SlotId` | `string` | Denormalized authority/provenance identity |

Binding identity includes `PublicationId`; artifact identity alone cannot distinguish two named slots pointing
to the same artifact. Store operations required by publication are list/delete/activate by publication ID.
Stimulus queries return bindings belonging only to authoritative active publications. Fan-out then occurs
across those active bindings in deterministic order.

`RecurringTriggerSchedule` gains the same `PublicationId` and `SlotId` identities and publication-scoped
replacement operations. In-memory HTTP routes remain derived projections, never publication authority.

### `WorkflowExecutableSourceReference` (extended)

Existing source, artifact, definition, scope, expiry, retirement, and layout fields remain. Add:

| Field | Type | Rules |
|---|---|---|
| `PublicationId` | `string?` | Required when `Scope == Published`; null for a TestRun |
| `SlotId` | `string?` | Required when `Scope == Published`; null for a TestRun |

A Published reference is live provenance only while it is not retired and its publication is active. A
TestRun reference remains live while not retired and not expired. A live Published reference does not by
itself grant start authority: start dispatch must resolve the active publication selected by its slot. This
prevents a staged, failed, or superseded publication from starting merely because its artifact exists.

Publishing creates/retires Published and TestRun references. Runtime API exposes them read-only. Physical
artifact deletion is not a normal publication operation.

## Workflow execution retention roots

`WorkflowExecutionState.PinnedExecutable.ArtifactId` is the retention root. No duplicate execution source
reference is created.

The Runtime persistence contract exposes a provider-efficient operation equivalent to:

```text
ListDistinctPinnedExecutableArtifactIds(retained executions)
```

It must not deserialize every execution record into application memory. Every retained execution protects its
artifact regardless of status: pending, running, suspended, completed, canceled, or faulted. Removing an
execution under the configured execution-retention policy removes that root; merely completing it does not.

The query/projection used to enumerate roots is derived from execution records and is not a second source of
lifecycle truth. Its consistency boundary must be the same as saving/removing the execution record.

## State transitions

### Publish or replace a slot

```text
request
  -> resolve policy and slot
  -> compile/validate executable and derive claims
  -> conflict preflight against authoritative slots
  -> save immutable artifact (staged; protected by creation grace)
  -> Candidate
       -> preparation failure -------------------------------> Failed
       -> projections require reconciliation -> PendingProjection
              -> retryable failure --------------------------> PendingProjection
              -> terminal failure ---------------------------> Failed
              -> all prepared + slot CAS succeeds -----------> Active
       -> atomic provider + slot CAS succeeds ----------------> Active
       -> slot revision changed ------------------------------> Failed (concurrency conflict)

old Active publication in replaced slot
  -> candidate activation commits ----------------------------> Retired
  -> candidate fails or loses CAS ----------------------------> Active (unchanged)
```

Activation establishes the new `ActivePublicationId`, creates its live Published source reference, retires the
old publication/reference, and changes serving trigger/schedule authority as one logical commit. Existing
workflow executions continue with their pinned old artifact.

### Unpublish and restore

```text
Active --unpublish + slot CAS--> Retired; slot becomes empty
Retired --restore + full preflight/projection preparation + slot CAS--> Active
```

Restore is a new authority transition, not clearing `RetiredAt` in place without validation. It must recheck
exclusive trigger conflicts and artifact availability. Implementations may create a new publication record for
restore; if they reactivate the historical record, the transition must retain a complete audit history.

## Invariants

1. `(WorkflowDefinitionId, SlotName)` identifies exactly one slot.
2. A slot selects zero or one active publication; a publication is active in at most one slot.
3. Only an active publication may contribute start-trigger bindings or recurring-start schedules visible to routing.
4. Replacing `default` retires its old authority; named slots are required for intentional coexistence.
5. An exclusive stimulus identity has at most one authoritative claimant per shell. Fan-out claims may coexist.
6. Definition soft deletion neither retires nor changes a publication slot.
7. Publication failure never removes or partially disables the prior active publication.
8. Executables are immutable and content-addressed; publication identity and lifecycle never alter artifact identity.
9. An artifact is retained while any live source reference or retained workflow execution points to it.
10. Trigger bindings, schedules, source references, and slots are projections/provenance of publication authority;
    none may independently make a retired publication authoritative.

## Concurrency semantics

- Slot activation and unpublish use compare-and-swap on `PublicationSlot.Revision`. Concurrent writers from the
  same observed revision produce at most one winner; losers do not mutate serving state.
- Conflict preflight is repeated or validated inside the activation boundary so two concurrent exclusive claims
  cannot both pass an earlier read and activate.
- Projection intent delivery is idempotent by `(PublicationId, ProjectionKind, Operation)` and safe after process
  restart. A reconciler may repeat preparation and cleanup.
- Garbage collection and publication must not race through a check-then-delete gap. Artifact deletion requires a
  conditional final root check or equivalent provider transaction immediately before deletion.

## Garbage-collection semantics

At one captured `now`, the retained artifact set is:

```text
artifact IDs from live source references
UNION
distinct artifact IDs pinned by retained workflow execution records
UNION
artifacts still inside the configured creation/staging grace period
```

GC may hard-delete expired/retired references according to reference-retention policy. It may delete an artifact
only when it is outside the grace period and absent from every root set. Before physical deletion it must recheck
roots conditionally so a concurrent publish, restore, test run, or execution checkpoint cannot lose its artifact.
An orphaned failed candidate becomes collectible after its grace period. Trigger projections for a retired
publication are reconciled independently and never count as artifact-retention roots.

## Persistence notes

### In-memory

- Maintain slots in a dictionary keyed by deterministic `SlotId`; perform revision checks and state replacement
  under one synchronization boundary.
- Keep publication-scoped bindings/schedules in copy-on-write sets so the active visibility switch is atomic to
  readers.
- Derive distinct execution pins from stored execution states without manufacturing source references.

### Groundwork

- Publishing-owned document kinds and stores belong in a Publishing Groundwork persistence module rather than
  adding Publishing dependencies to the Runtime Groundwork module.
- Publication slot storage requires a unique deterministic document ID and expected-version/CAS writes. Atomic
  providers should use a cross-unit unit of work for slot, publication, reference, and serving-projection changes.
  Providers without that boundary use the durable projection-intent protocol and keep the old slot authoritative
  until the candidate is fully prepared.
- Existing Runtime Groundwork documents need publication identity on source references, trigger bindings, and
  recurring schedules, plus publication-scoped indexes. Before GA, every persisted-shape change advances that
  kind's current and minimum-readable version together, replaces its golden fixture, and requires a complete
  dependent Runtime/Publishing persistence reset; there are no Elsa upcasters or historical fixtures.
  Executable v4 includes the reusable-activity input contract and direct dependency snapshot; source-reference
  v4 includes tenant scope; workflow-execution v4 includes dispatch nesting depth. After a released shape
  exists, a compatible in-place or rolling upgrade may instead add Groundwork `IDocumentJsonUpcaster`
  contributions and retain supported historical fixtures. Adding an index over an already persisted nested
  field alone does not require a bump.
- The current workflow-execution document has only a collection index and nests the pinned identity under state.
  FR-066 therefore requires a provider-side distinct/index projection or an equivalent lightweight root query
  maintained atomically with execution-state persistence. Loading `IWorkflowExecutionStateStore.ListAsync()` and
  applying `Distinct` in application memory is not compliant.
- New manifest indexes should cover publication by slot/status, source references and trigger projections by
  publication ID, and any provider-specific retained-executable-root query. Index backfill must be validated for
  every supported Groundwork provider.
