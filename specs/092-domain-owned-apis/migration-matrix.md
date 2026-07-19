# Groundwork Migration Matrix: Domain-Owned Management APIs

Inventory date: 2026-07-13; clean-break baseline updated 2026-07-16. This matrix records the Runtime
document persistence decisions used by spec 092 and the Publishing-owned persistence introduced by
T042. It also records the deliberate pre-GA current-only boundary for every Runtime document kind.

## Authoritative evolution rules

- `ElsaRuntimeStorageManifest.SchemaVersion` is the frozen manifest stamp `1.0.0`; it is not a
  per-document migration knob.
- `ElsaRuntimeDocumentVersions` carries the current integer version for each Runtime document kind and returns
  that same value as minimum-readable. `workflowExecutable`, `workflowExecutableSourceReference`, and
  `workflowExecutionState` are version 4; activity-execution state and inspection documents, scheduler work
  items, trigger bindings, and recurring schedules are version 2; unchanged kinds remain version 1.
- Before GA, every kind retains only its current golden fixture. The Runtime serializer is parameterless and
  passes an empty Groundwork `IDocumentJsonUpcaster` collection to `VersionedJsonDocumentCodec`; there is no
  Elsa upcaster interface, registry, concrete transformation, or historical compatibility chain.
- After a released shape exists, a compatible in-place or rolling upgrade may deliberately keep an older
  minimum-readable version, add Groundwork `IDocumentJsonUpcaster` contributions for every required step, and
  retain every supported historical fixture. That future composition requires an explicit implementation and
  does not weaken the pre-GA boundary.
- Adding an index over an already persisted field does not change the document JSON and therefore does
  not require a document-version increment. Groundwork detects and backfills physicalized index-set
  changes. The new query and its backfill still require provider coverage.
- An installation carrying any non-current Runtime document must atomically reset the complete Runtime and
  Publishing Groundwork persistence sets while preserving Design and Activities data, then republish workflows
  before serving traffic. A selective reset is unsafe:
  retained executions, continuations, publication authority, and serving projections can reference the
  removed artifacts. The v4 boundary is not an in-place migration or a compatibility promise.

Authoritative sources:

- `src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs`
- `src/Elsa/Persistence/Groundwork/Serialization/ElsaRuntimeDocumentVersions.cs`
- `src/Elsa/Persistence/Groundwork/Serialization/GroundworkRuntimeDocumentSerializer.cs`
- `Groundwork.Documents.Serialization.VersionedJsonDocumentCodec` (versioned Groundwork package contract)
- `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeDocumentFixtureTests.cs`
- `tests/Elsa/Persistence/Groundwork/Tests/Fixtures/v*/`

## Existing Runtime document kinds

| Domain record | Document kind and current version | Current persisted shape | Current indexes and portable queries | Upcaster and fixture baseline | spec 092 impact and migration gate |
|---|---|---|---|---|---|
| Workflow executable | `workflowExecutable`, current/minimum v4 | Envelope `{ collection, executable, rootWriteLeases, deletionGuard }`. Leases are keyed by logical writer ID and carry an opaque fencing token plus expiry; the executable carries its reusable-activity `inputContract` and direct `dependencies` snapshot, and compiled input bindings carry stable `inputKey` and `isSensitive` fields. | `by-collection` on `collection`; `list-all` equality query. Retention transitions load the raw envelope and use its Groundwork document version as `ExpectedVersion` for every CAS update or conditional delete. | `Fixtures/v4/workflowExecutable.json` is the sole supported fixture. There is no production upcaster; v1-v3 are rejected before content parsing. | T018's race-safe deletion state machine is provider-owned and persisted with the artifact. Root acquisition and deletion reservation are mutually exclusive CAS transitions; expired states are recoverable; stale fencing tokens cannot renew, release, cancel, or delete; guarded state survives restart. This closes the root-write/GC check-then-delete race without making source references or execution records share a transaction with the artifact document. |
| Workflow execution state | `workflowExecutionState`, current/minimum v4 | Envelope `{ collection, historySortTicks, state }`. The state includes `dispatchNestingDepth`; the pinned artifact remains nested at `state.pinnedExecutable.artifactId`, with no artifact ID lifted to the envelope. | `by-collection` on `collection`; `list-all` equality query. The retained-root query reads only the stable nested artifact-ID JSON fragment from admitted v4 envelopes, then returns distinct IDs. | `Fixtures/v4/workflowExecutionState.json` is the sole supported fixture. There is no production upcaster; v1-v3 are rejected before content parsing. | T018 satisfies FR-066 without duplicating the execution lifecycle record: it does not call `ListAsync()` or materialize complete workflow execution states. Save and removal update the same execution document that the projection reads. |
| Executable source reference | `workflowExecutableSourceReference`, current/minimum v4 | Envelope `{ collection, artifactId, reference }`. `reference` carries tenant scope, publication and slot provenance, authored input evidence, source/artifact/definition identity, scope, expiry, retirement, and layout facts. | `by-collection` on `collection` with `list-all`; `by-artifact` on lifted `artifactId` with `list-by-artifact`. | `Fixtures/v4/workflowExecutableSourceReference.json` is the sole supported fixture. There is no production upcaster; v1-v3 are rejected before content parsing. | TestRun references remain slotless. Published references are written in the complete current shape; no legacy publication authority is inferred from an older source-reference document. |
| Workflow trigger binding | `workflowTriggerBinding`, current/minimum v2 | Direct binding document with publication ID, slot ID, provider cardinality, and prepared/active visibility in addition to artifact/node/stimulus identity. | Existing artifact/stimulus indexes remain; publication-scoped query/index operations are added with the T036 store implementation. | `Fixtures/v2/workflowTriggerBinding.json` is the sole supported fixture. There is no production upcaster; v1 is rejected before content parsing. | Binding identity has a publication-scoped builder so named slots sharing one artifact cannot collapse. Authoritative bindings are rebuilt after the required reset and republish. |
| Recurring trigger schedule | `recurringTriggerSchedule`, current/minimum v2 | Envelope `{ collection, artifactId, schedule }`; the schedule includes publication ID, slot ID, and prepared/active visibility. | Existing collection/artifact indexes remain; publication-scoped query/index operations are added with the T036 store implementation. | `Fixtures/v2/recurringTriggerSchedule.json` is the sole supported fixture. There is no production upcaster; v1 is rejected before content parsing. | Authoritative schedules are rebuilt after the required reset and republish rather than assigning authority by document-local inference. |
| Publication projection state | `publicationProjectionState`, v1 | Lightweight marker `{ projectionKind, publicationId, isActive }`, including zero-row prepared publications. | Deterministic document ID from projection kind and length-prefixed publication ID; mutated atomically with the owning binding/schedule documents. | New v1 fixture covers a prepared, inactive trigger-binding projection; no upcaster is required for a new kind. | Makes prepare/activate explicit and durable even when a publication contributes no rows. Activation requires the marker, so a missing or never-prepared publication cannot silently succeed. |

### Older trigger and schedule projections are rejected

The trigger-binding and recurring-schedule kinds do not retain their v1 fixtures or transformations before
GA. V1 documents fail the v2 minimum-readable boundary before content parsing. Authoritative projections are
rebuilt from current publication records and clean-baseline source references after the complete dependent
Runtime/Publishing reset; no serializer migration assigns publication authority.

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

## Fixture and version-policy gate for implementation

`GroundworkRuntimeDocumentFixtureTests` freezes exactly one readable fixture per Runtime kind: the current
fixture. Minimum-readable equals current across the registry, and the Groundwork codec receives an empty
`IDocumentJsonUpcaster` collection. Groundwork retains generic chain validation for future released migrations,
but no historical Runtime chain or fixture is admitted before GA.

- T018's retention-coordination fields and the reusable-activity input contract and dependency snapshot are
  part of the `workflowExecutable` v4 baseline; no older executable envelope is supported.
- T034's source-reference publication/slot and authored-input fields, together with tenant scope, are part of
  the `workflowExecutableSourceReference` v4 baseline. Trigger bindings and recurring schedules likewise retain
  only their complete v2 fixtures; their v1 generations are rejected.
- Workflow execution dispatch nesting depth is part of the `workflowExecutionState` v4 baseline; no older
  workflow-execution envelope is supported.
- T042 adds v1 fixtures for the new Publishing-owned document kinds from their implemented store shapes.

## Validation checklist

- [x] All affected document kinds are present in `ElsaRuntimeDocumentVersions`; minimum-readable equals current
  for every kind.
- [x] Manifest index fields and equality queries are recorded above.
- [x] Exactly one current fixture is present for every Runtime kind; executable, source-reference, and
  workflow-execution use v4, while changed execution-scope and publication-projection kinds use v2.
- [x] The Runtime serializer is parameterless, receives an empty Groundwork upcaster set, and has no Elsa
  upcaster interface, registry, DI registration, or concrete transformation.
- [x] Planned Publishing records are confirmed absent from the current Runtime manifest.
- [x] T018 records the provider-CAS executable retention guard in the clean v4 envelope.
- [x] T034 records fail-closed rejection of legacy trigger/schedule projections without source-reference adoption.
- [x] Any non-current Runtime document requires a complete dependent Runtime/Publishing persistence reset,
  preserving Design/Activities data, followed by republish before traffic.
- [ ] T042 adds concrete fixtures for the new Publishing-owned document kinds.
