# Shared Runtime history shapes

Implementation baseline for the capture/provider/API workers. Existing public constructors and category ordinals remain compatible. The API projects these into authorized lower-camel-case views; storage/capture fields never become permission by themselves.

## Envelope and logical batch

Reuse RuntimeHistoryEvent with its existing constructor and Sequence/Name/OccurredAt meaning. Add init-only SchemaVersion (default 1), default-omitted Authority (Unspecified, CommittedTransition, DeliveryObservation), and null-omitted CheckpointOrdinal, BookmarkId, AttemptId, ParentActivityExecutionId, ActivityAncestryKnown, ExecutionScopeId, IterationId, Causation, PhysicalCommitId and RecordedAt. SchemaVersion is an additive serialized field; the optional occurrence/position fields remain absent on old envelopes. Old envelopes remain Unspecified authority, never silently promoted. Payload remains compatible but this metadata path emits no arbitrary payload. Existing no-history checkpoint fingerprint/wire preservation is a separate obligation in S03.

New Core models:

- RuntimeHistoryCapturePolicy(PolicyId, Version, Required, SupportedKinds): pinned immutable snapshot, null absent on legacy start/run wire.
- RuntimeHistoryCausation(HistoryEventId?, WorkflowExecutionId?, DeliveryId?, CommandId?, SchedulerWorkItemId?): known causal references only.
- RuntimeHistoryCandidate(Event, CheckpointOrdinal, BookmarkMutation?): Event.Sequence is zero; physical/recorded position is absent.
- RuntimeHistoryBatch(Policy, LogicalCheckpointId, OccurredAt, StartsAtRunStart, Candidates): ordered candidate group from the original logical checkpoint.
- RuntimeCheckpointCommit.HistoryBatches: optional ordered write-sidecar batches, absent on legacy wire; retained by every commit clone and by a separate coalescing buffer. RuntimeCheckpointStateChangeSet and RuntimeStateCategory contain no history records.

Stable IDs depend only on original logical identity/kind/subject/ordinal. Already supplied batches are validated/preserved on retry. Physical positions never enter the logical fingerprint. Wrong policy, run/checkpoint/subject or repeated identity with conflicting content refuses.

## Lifecycle and head

RuntimeHistoryBookmarkStatus: Active, Consumed, Removed, Superseded, Unknown. Retirement reasons distinguish cancellation, sibling winner, stale cleanup and other actual causes. Expired is eligibility at observation time, not a persisted lifecycle status.

RuntimeHistoryBookmarkMutation carries the existing safe ActivityExecutionBookmarkSummary, status, reason and selected-winner handle where known. No raw BookmarkState/payload is copied.

The creation-availability enum is `RuntimeHistoryCreationEvidence` (Unknown, CreationMetadata, CreationEvent); the bookmark model keeps its `PriorState` member key.

RuntimeHistoryBookmark retains that safe summary, optional node, status, nullable creation/end event references, last event/sequence, stable first-recorded sequence for inventory paging, optional end instant/reason/winner and an explicit creation/prior-state availability. Known creation metadata from the real handle may remain available without a captured creation event; neither is fabricated.

RuntimeHistoryHead retains exact run, first/last sequence, last covered physical checkpoint/instant, pinned policy, captured-from-start/has-gaps and purged-through position. Provider revision/scope are persistence details. Ingress appends can advance event sequence but cannot advance runtime-covered checkpoint or change Required policy.

## Provider boundary

The production enricher also retains a null-omitted `SourceFingerprint` on each logical batch. It identifies that logical contribution without its history participant. Final enrichment validation compares the source proof, then validates retained value-evidence links. This preserves original deltas on replay while refusing a later enricher that adds or changes source evidence after capture. Providers retain the full candidate fingerprint (event, logical ordinal and typed bookmark mutation) per event; an event-only fingerprint cannot prove bookmark mutation equivalence after the projection has advanced.

IRuntimeCheckpointCommitStore.SupportsHistory defaults false. CanCommitHistory(IRuntimeHistoryStore selectedStore) qualifies actual selected state/backend/context/scope ownership, not just two booleans; coalescing forwards the real pairing.

IRuntimeHistoryStore exposes separate read methods:

- FindHeadAsync(workflowExecutionId, cancellationToken)
- ReadPageAsync(RuntimeHistoryQuery, cancellationToken)
- FindEventAsync(workflowExecutionId, historyEventId, cancellationToken)
- ListBookmarksAsync(RuntimeHistoryBookmarkQuery, cancellationToken)
- FindBookmarkAsync(workflowExecutionId, bookmarkId, cancellationToken)

Committed transition append remains internal to checkpoint writers. IRuntimeHistoryObservationWriter.RecordAsync(RuntimeHistoryObservation, cancellationToken) is separate and accepts only policy-safe DeliveryObservation data, no bookmark mutation. It shares exact run/deletion guard and head allocation but never claims target execution/admission atomicity without the actual owner transaction.

Retention mutation is a separately named operation/port owned by existing retention/deletion integration; it cannot be invoked merely by inspecting history.

## Bounded result and cursor

RuntimeHistoryQuery uses existing request limits but its own scanned-window result. Inputs include exact run, optional execution/descendants/bookmark/category/stable kind/search, bounded limit and authenticated continuation. RuntimeHistoryPage carries Head/coverage, Items, UpperSequence, ScannedThroughSequence, ScannedCount, NextContinuationToken and CatchUpToken. Empty matches with advancing continuation are valid.

Bookmark inventory has its own query/page using first-recorded position plus identity; creation evidence can be absent. Exact identifiers/kinds take indexed paths. Free text has bounded safe-field windows and explicit searched-through coverage; no unbounded filtered total. Descendant resolution uses existing authorized execution hierarchy and remains bounded.

All public continuations are purpose-separated authenticated payloads bound to actual scope/run/query/limit/watermark, tenant/audit subject and authorization profile. Every read reauthorizes; tokens are not authority.

## API view baseline

Runtime capability relations are `workflow-history-page`, `workflow-history-event`, `workflow-history-bookmarks` and `workflow-history-bookmark`. They resolve beneath the exact run's `history` resource; clients use capability templates. Query continuations use `cursor`, alongside `limit`, exact activity/descendant/bookmark/category/kind-name/search filters. Enum query values use their string names. Exact response shapes are `{coverage,event,readAt}` and `{coverage,bookmark,readAt}`.

Coverage fields are `state`, `limitation`, `policyId`, `policyVersion`, `supportedKinds`, `capturedFromStart`, `hasGaps`, `firstSequence`, `lastSequence`, `settledSequence`, `purgedThroughSequence`, `lastPhysicalCommitId` and `lastCommittedAt`. States are `captured`, `partial`, `pending`, `disabled`, `uncaptured`, `unsupported` and `invalid`. `disabled` requires the run's explicit false admission capture marker; null remains legacy/unknown and must not be inferred from today's host options. A settled sequence describes an acknowledged prefix, not source closure.

Bookmark views contain the existing safe structural summary under `bookmark`, lifecycle fields and prior-state evidence, plus `eligibility`, nullable safe-code `eligibilityReason` and `eligibilityObservedAt`. Eligibility values are `eligible`, `expired`, `not-active`, `ineligible`, `unavailable` and `unknown`. Active/unknown historical entries consult the current native handle and owning activity under the same authorized run scope. Terminal runs, expired test scopes, non-waiting/superseded activities and handles no longer owned by the activity are ineligible. A changed native summary cannot be combined with older recorded metadata as if one observation; its current eligibility is unknown. Missing native state means unavailable eligibility, never an invented consumption event. Expiry is passive and produces no inspection-side event. Eligibility is an observation, not command permission or admission; delivery rechecks its actual conditions. Page/entry observation times come from the server. Nullable counts stay unknown.

Event views omit the legacy payload slot and arbitrary metadata. The reserved `history.valueEvidenceIds` metadata value is a JSON array of existing evidence identifiers; navigation uses the existing Data/value-policy boundary without implicit payload resolution. Unknown event kinds/versions retain their safe common envelope.

Coverage additionally carries optional nullable `captureAvailable`/`captureLimitation` and `sourceHistoryPending`/`sourceHistoryLimitation`. Current capture availability comes from the actual installed capture chain, retained profile support and selected atomic writer/reader pairing; disabling new admissions alone does not disable a required existing run. A missing/unsupported capture chain does not erase readable history or invent a past failed transition.

When the pinned profile promises timer-source observations, an indexed exact-run pending-responsibility read reports whether that owner still owes facts. Pending work produces pending coverage while retained rows remain readable; unavailable settlement inspection is a partial limitation. After observing no pending source work, refresh the head to account for a concurrent append/acknowledgement, preserving the page's original snapshot watermark/cursors. Null means not established/not applicable, not zero pending. Neither a false pending result nor a terminal workflow closes future external observations.

Delivery-owner inspection uses the existing indexed native-source kind queries (child dispatch requested, publication committed and child settled) and exact physical outbox lookup. At most100 source events per applicable kind and at most300 exact sources are examined; no continuation is followed and no global/due-only outbox scan is used. Each source must retain the matching origin event/run/outbox stamp. The actual parent-resume source is carried on waited child settlement; detached settlement has no such responsibility. More source pages, unknown versions, partial/purged coverage, missing physical sources, unsupported providers and concurrent head advancement leave settlement unknown with a visible limitation. Contradictory origin evidence is invalid. An undelivered matching source proves pending; an acknowledged negative observation covers only the inspected retained prefix and never promises future closure. Source inspection does not change response traversal bounds or native checkpoint coverage.

## Admission/replay policy

Pin profile on actual new admitted start payload and run state before durable admission. Legacy absent profile remains uncaptured on replay after upgrade. Host options apply to new admissions only. A retained Required profile makes a later new checkpoint without its batch refuse, including after module removal. An explicit supported quiescent policy change has its own new boundary; do not reinterpret old queued logical commits. Unsupported active changes remain explicit.

## Final admitted profile3 source contribution

New admissions use runtime-history-v3/version3. Existing main absent-policy bytes and native markers remain unchanged. Private unmerged v1/v2 event rows remain readable; their future capture support is reported honestly rather than retrofitted.

ActivityExecutionInspectionProjection.CheckpointValueCapture is an optional immutable RuntimeCheckpointValueCapture with exact CheckpointId, actual nullable SchedulerWorkItemId and ordered unique EvidenceIds. A known-empty contribution records reprojection. A truly empty raw ValueSnapshots list can omit it; a nonempty list requires the exact bound contribution. Actual source owners stamp invocation/resume/evaluation provenance before merge. History consumes this raw contribution and never queries a later projection to choose an earlier delta.

RuntimeHistoryIncidentFacts and the closed SourceFact catalog distinguish initial creation, evaluated strategy/decision, terminal resolution/suppression and reconciliation. Arbitrary incident upsert does not imply another fault or resolution. Lazy factories do not run on absent/older-policy paths and are resolved before raw checkpoint delivery.

RuntimePostCommitIntent.HistoryCausation is null-omitted and source-owned. Its origin history event/run/outbox identity is fingerprinted only when present. A stamped intent requires the actual persisted source even when a legacy handler declaration did not require one. Missing capture/source support is infrastructure unavailability before new effects or acknowledgement; ordinary unstamped delivery keeps its existing protocol.

Optional modules contribute through Core's `IRuntimeHistorySourceExtension`, without referencing the default Runtime implementation. Its policy-aware factory preserves absent and older pinned source identities. New code uses ADR 0080's Extension suffix; the prototype-only Contributor name is not a released API. A producer stamps causation only when the original policy includes its committed source kind and both promised owner-observation kinds. Installing or replacing a module later cannot retrofit already queued raw work.

Owner-observation support is bound to an exact post-commit intent kind. The selected `IRuntimePostCommitIntentDispatcher.CanRecordHistory` must qualify the actual selected receiver and writer/read pairing; its default is false. A declaration alone does not qualify an old constructor, custom dispatcher or replacement receiver. Stamped delivery rechecks that boundary before effects. Source policy-read IO, timeout or unavailable-provider failures remain infrastructure unavailability; malformed policy, conflicting facts and actual effect failures keep their distinct behavior.
