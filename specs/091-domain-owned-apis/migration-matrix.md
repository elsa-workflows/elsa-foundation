# Groundwork Migration Matrix: Domain-Owned Management APIs

Inventory date: 2026-07-13. This matrix records the persisted baseline before spec 091 changes any
Runtime document shape or adds Publishing-owned persistence. It is the migration gate for T034 and
T042; it does not itself change a storage manifest.

## Authoritative evolution rules

- `ElsaRuntimeStorageManifest.SchemaVersion` is the frozen manifest stamp `1.0.0`; it is not a
  per-document migration knob.
- `ElsaRuntimeDocumentVersions` carries the current integer version for each Runtime document kind.
  `workflowExecutable`, its source reference, trigger binding, and recurring schedule are now version 2;
  the new publication-projection marker starts at version 1 and unchanged kinds remain version 1.
- A persisted JSON shape change requires, in the same implementation change: incrementing that kind's
  version, registering a concrete `IGroundworkRuntimeDocumentUpcaster` for every newly required step,
  adding the new current-version golden fixture, and retaining the historical fixture.
- Adding an index over an already persisted field does not change the document JSON and therefore does
  not require a document-version increment. Groundwork detects and backfills physicalized index-set
  changes. The new query and its backfill still require provider coverage.
- The committed `Fixtures/v1/*.json` files are the pre-change contracts. They must never be regenerated
  to contain spec 091 fields.
- The production Runtime upcaster set contains `WorkflowExecutableDocumentV1ToV2Upcaster`. The
  default registration contributes it through `IEnumerable<IGroundworkRuntimeDocumentUpcaster>`;
  serializer tests continue to exercise generic chain validation with test-only implementations.

Authoritative sources:

- `src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs`
- `src/Elsa/Persistence/Groundwork/Serialization/ElsaRuntimeDocumentVersions.cs`
- `src/Elsa/Persistence/Groundwork/Serialization/GroundworkRuntimeDocumentUpcasterRegistry.cs`
- `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeDocumentFixtureTests.cs`
- `tests/Elsa/Persistence/Groundwork/Tests/Fixtures/v1/`

## Existing Runtime document kinds

| Domain record | Document kind and current version | Current persisted shape | Current indexes and portable queries | Upcaster and fixture baseline | Spec 091 impact and migration gate |
|---|---|---|---|---|---|
| Workflow executable | `workflowExecutable`, v2 | Envelope `{ collection, executable, rootWriteLeases, deletionGuard }`. Leases are keyed by logical writer ID and carry an opaque fencing token plus expiry; the optional deletion guard carries operation ID, fencing token, and expiry. | `by-collection` on `collection`; `list-all` equality query. Retention transitions load the raw envelope and use its Groundwork document version as `ExpectedVersion` for every CAS update or conditional delete. | `Fixtures/v1/workflowExecutable.json` remains the historical artifact-only envelope. `WorkflowExecutableDocumentV1ToV2Upcaster` adds an empty lease set and null deletion guard. `Fixtures/v2/workflowExecutable.json` is the current golden shape. | T018's race-safe deletion state machine is provider-owned and persisted with the artifact. Root acquisition and deletion reservation are mutually exclusive CAS transitions; expired states are recoverable; stale fencing tokens cannot renew, release, cancel, or delete; guarded state survives restart. This closes the root-write/GC check-then-delete race without making source references or execution records share a transaction with the artifact document. |
| Workflow execution state | `workflowExecutionState`, v1 | Envelope `{ collection, state }`. The pinned artifact is nested at `state.pinnedExecutable.artifactId`; no artifact ID is lifted to the envelope. | `by-collection` on `collection`; `list-all` equality query. The retained-root query reads only the stable nested artifact-ID JSON fragment from current envelopes and applies normal deserialization/upcasting only to historical versions, then returns distinct IDs. | No production upcaster. `Fixtures/v1/workflowExecutionState.json` exists and includes the complete pinned executable identity. | T018 satisfies FR-066 without changing the wire shape: it does not call `ListAsync()` or materialize complete workflow execution states. Save and removal update the same execution document that the projection reads, so there is no duplicate lifecycle record or backfill requirement. |
| Executable source reference | `workflowExecutableSourceReference`, v2 | Envelope `{ collection, artifactId, reference }`. `reference` now carries nullable publication and slot provenance in addition to its source/artifact/definition, scope, expiry, retirement, and layout facts. | `by-collection` on `collection` with `list-all`; `by-artifact` on lifted `artifactId` with `list-by-artifact`. | The v1 fixture is retained. `WorkflowExecutableSourceReferenceDocumentV1ToV2Upcaster` adds neutral null publication/slot markers; the v2 fixture carries real publication and default-slot identities. | TestRun references remain slotless. A null publication on an upcast Published reference means `legacy-unadopted`, never implicit start authority; the deterministic adoption reconciliation below assigns authority. |
| Workflow trigger binding | `workflowTriggerBinding`, v2 | Direct binding document with publication ID, slot ID, provider cardinality, and prepared/active visibility in addition to artifact/node/stimulus identity. | Existing artifact/stimulus indexes remain; publication-scoped query/index operations are added with the T036 store implementation. | The v1 fixture is retained. `WorkflowTriggerBindingDocumentV1ToV2Upcaster` adds null publication/slot, marks the projection inactive, and preserves provider semantics by mapping legacy `HttpEndpoint` stimuli to `Exclusive` and other legacy stimuli to `FanOut`. The v2 fixture is publication-scoped and active. | Upcast legacy bindings are deliberately non-serving. Binding identity now has a publication-scoped builder so named slots sharing one artifact cannot collapse. Authoritative bindings are rebuilt from the adopted publication. |
| Recurring trigger schedule | `recurringTriggerSchedule`, v2 | Envelope `{ collection, artifactId, schedule }`; the schedule now includes publication ID, slot ID, and prepared/active visibility. | Existing collection/artifact indexes remain; publication-scoped query/index operations are added with the T036 store implementation. | The v1 fixture is retained. `RecurringTriggerScheduleDocumentV1ToV2Upcaster` adds null publication/slot and marks the legacy schedule inactive. The v2 fixture is publication-scoped and active. | Upcast legacy schedules cannot fire. Authoritative schedules are rebuilt from the adopted publication rather than assigned authority by document-local inference. |
| Publication projection state | `publicationProjectionState`, v1 | Lightweight marker `{ projectionKind, publicationId, isActive }`, including zero-row prepared publications. | Deterministic document ID from projection kind and length-prefixed publication ID; mutated atomically with the owning binding/schedule documents. | New v1 fixture covers a prepared, inactive trigger-binding projection; no upcaster is required for a new kind. | Makes prepare/activate explicit and durable even when a publication contributes no rows. Activation requires the marker, so a missing or never-prepared publication cannot silently succeed. |

### Legacy publication adoption required before v1-to-v2 upcasters

Version 1 persisted three independent facts: Published source references, artifact-owned trigger
bindings, and artifact-owned recurring schedules. It did not persist which publish attempt or slot was
authoritative. A serializer upcaster sees one document at a time and therefore cannot safely infer a
global slot winner.

The accepted adoption policy is **latest persisted publish wins the default slot; every other legacy
publish remains historical and non-authoritative**. Reconciliation performs these restart-safe steps:

1. group live v1 Published references by workflow definition;
2. deterministically select the maximum `(PublishedAt ?? CreatedAt, CreatedAt, SourceReferenceId)` using
   ordinal ID comparison as the final tie-breaker and adopt it into `default`;
3. retain the other references as historical provenance but do not synthesize named slots or start authority;
4. create the default slot and adopted publication through an idempotent reconciliation keyed by the
   deterministic legacy publication identity derived from the winning source-reference ID; and
5. rebuild trigger bindings and recurring schedules from that authoritative publication before new
   routing authority is exposed.

The migration is fail-closed and restart-safe. The v1-to-v2 upcasters add only neutral legacy markers:
null publication/slot provenance and inactive serving projections. They never independently decide
publication authority. If reconciliation stops after upcasting, no legacy projection is accidentally
promoted; replay selects the same winner and rebuilds the same publication-scoped projections.

## Planned Publishing document kinds

The following records do not exist in any current Groundwork manifest, version registry, upcaster set,
or fixture directory. They are Publishing-owned and belong in the new provider-neutral Publishing
Groundwork module from T042, not in `Elsa.Persistence.Groundwork` merely because Runtime projections
also use Groundwork.

| Planned record | Initial identity/index requirements | Initial fixture/version requirement |
|---|---|---|
| Publication policy | Host default or deterministic workflow-definition ID; query by definition where applicable; optimistic revision | New document kind starts at v1 with a real golden fixture produced from the implemented store shape. |
| Publication slot | Deterministic ID from `(WorkflowDefinitionId, SlotName)`; unique slot identity; queries by definition; optimistic revision/CAS | New document kind starts at v1 with a fixture covering empty/active authority and revision fields. |
| Publication record | `PublicationId`; indexes by slot and lifecycle status; query history by slot/definition | New document kind starts at v1 with a fixture covering immutable source/artifact identity and lifecycle facts. |
| Publication trigger claim | Deterministic claim ID; indexes by publication and normalized exclusive stimulus identity | New document kind starts at v1 with a fixture covering `Exclusive`/`FanOut` cardinality. |
| Publication projection intent | Idempotent intent ID; indexes by publication, delivery status, and due/retry partition as supported | New document kind starts at v1 with a fixture covering durable retry/delivery state. |

The Publishing manifest must declare schema history and optimistic concurrency where its stores rely on
them. T042 must prove restart behavior and compare-and-swap semantics. If a provider cannot atomically
activate slot, reference, binding, and schedule units, the durable projection-intent protocol keeps the
old publication authoritative until candidate preparation completes.

## Fixture and upcaster gate for implementation

The four required pre-change fixtures already exist and are enforced by
`GroundworkRuntimeDocumentFixtureTests` through both shape-drift and legacy-stamp read tests. Creating
duplicate v1 files would add no coverage. Creating placeholder upcasters before the v2 shapes and legacy
adoption policy exist would be actively unsafe: the registry treats an upcaster as executable migration
logic and validates that every registered chain reaches the declared current version.

Therefore T011 is deferred to the first stored-shape implementation:

- T034 must preserve the four v1 fixtures, bump each changed Runtime kind, add concrete upcasters, add
  current-version fixtures, and make fixture tests enumerate historical and current versions.
- T018 applies that rule to the executable envelope: `workflowExecutable` v2, its concrete v1-to-v2
  upcaster, and both historical/current fixtures are now present. The retained execution-root query did
  not change the `workflowExecutionState` wire shape.
- T042 must add v1 fixtures for the new Publishing-owned document kinds once their real store envelopes
  are known.

## Validation checklist

- [x] All affected document kinds are present in `ElsaRuntimeDocumentVersions`; `workflowExecutable`
  is v2 and the remaining pre-existing kinds are v1.
- [x] Manifest index fields and equality queries are recorded above.
- [x] Existing v1 fixtures are present for all four kinds.
- [x] The production upcaster set contains complete v1-to-v2 chains for the executable and all three
  changed Runtime publication projections.
- [x] Planned Publishing records are confirmed absent from the current Runtime manifest.
- [x] T018 records the provider-CAS executable retention guard, v2 envelope, migration, and fixtures.
- [x] T034 records the deterministic fail-closed legacy publication-adoption policy.
- [x] T034 preserves the three changed Runtime v1 fixtures and adds concrete v2 fixtures/upcaster tests.
- [ ] T042 adds concrete fixtures for the new Publishing-owned document kinds.
