# Groundwork Migration Matrix: Domain-Owned Management APIs

Inventory date: 2026-07-13. This matrix records the persisted baseline before spec 091 changes any
Runtime document shape or adds Publishing-owned persistence. It is the migration gate for T034 and
T042; it does not itself change a storage manifest.

## Authoritative evolution rules

- `ElsaRuntimeStorageManifest.SchemaVersion` is the frozen manifest stamp `1.0.0`; it is not a
  per-document migration knob.
- `ElsaRuntimeDocumentVersions` carries the current integer version for each Runtime document kind.
  `workflowExecutable` is now version 2; the other pre-spec-091 kinds remain version 1 until their
  owning implementation tasks evolve them.
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
| Executable source reference | `workflowExecutableSourceReference`, v1 | Envelope `{ collection, artifactId, reference }`. `reference` contains source/artifact/definition identity, scope, expiry, retirement facts, and layout. It has no publication or slot identity. | `by-collection` on `collection` with `list-all`; `by-artifact` on lifted `artifactId` with `list-by-artifact`. | No production upcaster. `Fixtures/v1/workflowExecutableSourceReference.json` exists. | Adding `PublicationId` and `SlotId` changes the stored reference and requires v2, a concrete v1-to-v2 upcaster, a v2 fixture, and retained v1. Publication-scoped queries also require a lifted or nested indexed publication ID plus manifest/query declarations. Historical Published references cannot be assigned default-slot authority by a shape-only upcaster when several references exist; T034 must use the legacy-adoption decision below. TestRun references remain slotless. |
| Workflow trigger binding | `workflowTriggerBinding`, v1 | Direct binding document; no envelope. Identity is artifact/node/stimulus. It contains artifact/definition/stimulus metadata but no publication, slot, or cardinality. | `by-stimulus` on `stimulusHash` with `list-by-stimulus`; `by-artifact` on `artifactId` with `list-by-artifact`. | No production upcaster. `Fixtures/v1/workflowTriggerBinding.json` exists. | Publication/slot identity and trigger cardinality change the shape and require v2, a concrete v1-to-v2 upcaster, a v2 fixture, and retained v1. Add publication-scoped index/query operations. A binding cannot be mapped reliably to one historical source reference when several publications share an artifact; serving bindings must be rebuilt from adopted authoritative publication records instead of treating an upcast placeholder as authority. |
| Recurring trigger schedule | `recurringTriggerSchedule`, v1 | Envelope `{ collection, artifactId, schedule }`; artifact ID is lifted for indexing and repeated inside the schedule. The schedule contains stimulus/timing facts but no publication or slot identity. | `by-collection` on `collection` with `list-all`; `by-artifact` on lifted `artifactId` with `list-by-artifact`. | No production upcaster. `Fixtures/v1/recurringTriggerSchedule.json` exists. | Publication/slot identity changes the schedule shape and requires v2, a concrete v1-to-v2 upcaster, a v2 fixture, and retained v1. Add publication-scoped index/query operations. Historical schedules must be rebuilt or associated through the adopted publication; a synthetic upcast value alone must not make a non-authoritative schedule fire. |

### Legacy publication adoption required before v1-to-v2 upcasters

Version 1 persisted three independent facts: Published source references, artifact-owned trigger
bindings, and artifact-owned recurring schedules. It did not persist which publish attempt or slot was
authoritative. A serializer upcaster sees one document at a time and therefore cannot safely infer a
global slot winner.

T034 must implement and test one explicit adoption policy before registering the v1-to-v2 steps:

1. group live v1 Published references by workflow definition;
2. deterministically select the reference adopted into the `default` slot using persisted publication
   facts and a stable tie-breaker;
3. either retire the remaining references from start authority or adopt them into explicit deterministic
   legacy named slots, according to the publication-slot ADR's compatibility decision;
4. create publication records/slots through an idempotent migration or reconciliation operation; and
5. rebuild trigger bindings and recurring schedules from those authoritative publications before new
   routing authority is exposed.

The migration must be restart-safe. A v1-to-v2 document upcaster may add a neutral legacy marker needed
for deserialization, but it must not independently decide publication authority.

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
- [x] The production upcaster set contains the complete `workflowExecutable` v1-to-v2 chain.
- [x] Planned Publishing records are confirmed absent from the current Runtime manifest.
- [x] T018 records the provider-CAS executable retention guard, v2 envelope, migration, and fixtures.
- [ ] T034 records the legacy publication-adoption policy.
- [ ] T034/T042 adds concrete new-version/new-kind fixtures and migration tests with the implementation.
