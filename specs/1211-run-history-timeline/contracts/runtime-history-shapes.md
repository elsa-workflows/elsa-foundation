# Shared Runtime history shapes

Implementation baseline for the capture/provider/API workers. Existing public constructors and category ordinals remain compatible. The API projects these into authorized lower-camel-case views; storage/capture fields never become permission by themselves.

## Envelope and logical batch

Reuse RuntimeHistoryEvent with existing constructor and Sequence/Name/OccurredAt meaning. Add null/default-omitted init-only SchemaVersion (default 1), Authority (Unspecified, CommittedTransition, DeliveryObservation), BookmarkId, AttemptId, ExecutionScopeId, IterationId, Causation, PhysicalCommitId and RecordedAt. Old envelopes remain Unspecified authority, never silently promoted. Payload remains compatible but this metadata path emits no arbitrary payload.

New Core models:

- RuntimeHistoryCapturePolicy(PolicyId, Version, Required, SupportedKinds): pinned immutable snapshot, null absent on legacy start/run wire.
- RuntimeHistoryCausation(HistoryEventId?, WorkflowExecutionId?, DeliveryId?, CommandId?, SchedulerWorkItemId?): known causal references only.
- RuntimeHistoryCandidate(Event, CheckpointOrdinal, BookmarkMutation?): Event.Sequence is zero; physical/recorded position is absent.
- RuntimeHistoryBatch(Policy, LogicalCheckpointId, OccurredAt, StartsAtRunStart, Candidates): ordered candidate group from the original logical checkpoint.
- RuntimeCheckpointStateChangeSet.HistoryBatches: optional ordered batches, absent on legacy wire; retained by every clone/fold.

Stable IDs depend only on original logical identity/kind/subject/ordinal. Already supplied batches are validated/preserved on retry. Physical positions never enter the logical fingerprint. Wrong policy, run/checkpoint/subject or repeated identity with conflicting content refuses.

## Lifecycle and head

RuntimeHistoryBookmarkStatus: Active, Consumed, Removed, Superseded, Unknown. Retirement reasons distinguish cancellation, sibling winner, stale cleanup and other actual causes. Expired is eligibility at observation time, not a persisted lifecycle status.

RuntimeHistoryBookmarkMutation carries the existing safe ActivityExecutionBookmarkSummary, status, reason and selected-winner handle where known. No raw BookmarkState/payload is copied.

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

Coverage fields are `state`, `limitation`, `policyId`, `policyVersion`, `supportedKinds`, `capturedFromStart`, `hasGaps`, `firstSequence`, `lastSequence`, `settledSequence`, `purgedThroughSequence`, `lastPhysicalCommitId` and `lastCommittedAt`. States are `captured`, `partial`, `pending`, `uncaptured`, `unsupported` and `invalid`; an absent historical policy must not be classified as historically disabled from today's host options. A settled sequence describes an acknowledged prefix, not source closure.

Bookmark views contain the existing safe structural summary under `bookmark`, lifecycle fields and prior-state evidence, plus `eligibility` and `eligibilityObservedAt`. Eligibility values are `eligible`, `expired`, `not-active`, `unavailable` and `unknown`. Active/unknown historical entries consult the current native handle under the same authorized run scope. Missing native state means unavailable eligibility, never an invented consumption event. Expiry is passive and produces no inspection-side event. Page/entry observation times come from the server. Nullable counts stay unknown.

Event views omit the legacy payload slot and arbitrary metadata. The reserved `history.valueEvidenceIds` metadata value is a JSON array of existing evidence identifiers; navigation uses the existing Data/value-policy boundary without implicit payload resolution. Unknown event kinds/versions retain their safe common envelope.

## Admission/replay policy

Pin profile on actual new admitted start payload and run state before durable admission. Legacy absent profile remains uncaptured on replay after upgrade. Host options apply to new admissions only. A retained Required profile makes a later new checkpoint without its batch refuse, including after module removal. An explicit supported quiescent policy change has its own new boundary; do not reinterpret old queued logical commits. Unsupported active changes remain explicit.
