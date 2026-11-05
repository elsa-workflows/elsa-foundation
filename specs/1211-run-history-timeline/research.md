# Research: production run history

Source baseline: main 074259b3565e046701acbce69613120d7bed5f64; feature head initially 1fedc927dd73dbec0104818c10f086ac9aca28de. Three read-only Spec Kit research passes examined capture/coalescing, persistence/API/access and Studio/acceptance. These findings are design evidence, not implementation acceptance.

## R1 — Capture before state folding, persist with the existing commit

**Decision**: Add an optional typed history participant to RuntimeCheckpointStateChangeSet. IRuntimeCheckpointCommitEnricher derives deterministic candidates before coalescing; RuntimeCheckpointFold concatenates them without last-state collapse. Existing native/EF writers persist queryable history atomically.

**Rationale**: RuntimeCheckpointCommitter already enriches before persistence policy. Current fold keeps the last activity/inspection state, so inspecting only folded projections loses intermediate transitions. Existing QA ExecutionEvidenceCheckpointEnricher writes volatile data before commit and swallows failure. It is not authoritative production history.

**Alternatives**: Ordinary history outbox intents per logical step would prevent RuntimeCoalescingSession.CanDiscardCompletion from preserving Ephemeral RecordCompletion=false. A separate post-commit materializer adds unnecessary recovery/completeness complexity. After-commit observer callbacks cannot provide atomic capture.

Sources: Core/Models/RuntimeCheckpointCommit.cs; Services/Checkpoints/RuntimeCheckpointCommitter.cs; Services/Coalescing/RuntimeCheckpointFold.cs and RuntimeCoalescingSession.cs; QA ExecutionEvidenceCheckpointEnricher.

## R2 — Stable identity, sequence and compatibility

**Decision**: Derive candidate identity/ordinal from original logical checkpoint and semantic subject/kind, without TimeProvider or random IDs. Provider assigns sequence/physical commit group inside its existing atomic transaction after replay/fence validation. Preserve source logical checkpoint/observed instant separately.

**Rationale**: RuntimeCheckpointCommitFingerprint explicitly constructs historical shapes. Add a versioned wrapper only for nonempty history contributions; preserve every no-history byte shape. Preserve history through all With* helpers/overlays. Synthetic CoalescedFlush must not be recaptured.

**Alternatives**: Timestamp sorting, process counters, GUIDs or sequence allocation before commit cannot guarantee replay identity/local order. Treating every observed transition as its own persisted boundary changes Balanced semantics.

Existing RuntimeHistoryEvent.cs is a public observability envelope with category/name/sequence and legacy constructors, but has no storage/query pipeline. Reuse/extend compatibly where appropriate; do not claim this file implements durable history.

## R3 — One physical commit, one scoped head allocation

**Decision**: Add scoped head, immutable event and bounded bookmark-lifecycle projections to RuntimeDbContext. Replay marker lookup precedes allocation; staging and the existing marker-last writes share the transaction. In-memory history uses InMemoryRuntimeCheckpointStoreState's write gate and rollback/validation boundary.

**Rationale**: EF already stages participants, saves them, saves a create-only marker and commits. A head CAS revision allocates one contiguous range per physical commit. Rows use exact encoded scope/run agreement plus bounded hashes/indexes; do not rely on hashes alone or exceed MySQL key widths. Sequence numbers never enter the logical fingerprint.

**Failure policy**: Required staging failure rolls back every participant/head/marker. Ambiguous post-commit failure reconciles the existing marker before retry. Head-only contention needs a narrow bounded retry/retryable result distinct from a stale execution fence; do not broaden the existing fence-renewal retry blindly.

**Retention**: Extend EfWorkflowExecutionDeletionStore and DeleteRunOwnedHistoryAsync plus the in-memory deletion path. Default retention follows the retained run. Explicit purge preserves coverage/tombstone information; no opportunistic eviction.

## R4 — Mode/capture coverage remains truthful

**Decision**: History is an explicit production composition independent of QA sessions; payload capture remains separate. Pin the capture profile to new run/admission identity so replay of a legacy no-history commit does not acquire a new history fingerprint after deployment. Existing runs without that profile remain uncaptured unless a new explicit policy boundary enables future capture; never reinterpret old queued work. History-only candidates do not force a flush or defeat never-persisted Ephemeral discard. Supported writers declare capability; required capture refuses an unsupported writer.

**Coverage**: Retain supported observed transitions with the committed segment, while labeling one shared physical boundary. Head/coverage records distinguish captured-from-start, explicitly enabled late capture, disabled intervals, purge and invalid/unavailable information. A head retaining Required capture refuses a new checkpoint without its required batch, including after capture-module removal. Host configuration changes apply to new admissions only. Any supported active-run disable/late-enable has an explicit quiescent policy boundary; an unsupported policy change is reported as unsupported rather than silently creating a gap. No-history legacy replay shapes remain unchanged.

**Rationale**: Workflow completion is not proof of capture completeness. Direct atomic storage settles its own persisted range immediately; no artificial pending materialization is needed for that baseline path. Optional sources still expose honest availability/settlement.

## R5 — Bookmark transitions follow actual typed effects

**Decision**: Capture typed bookmark mutations and semantic claim metadata, not names alone.

- InitialTriggerClaim is named ActivityAttemptClaimed. It deletes the selected handle and siblings: selected handle consumed; siblings retired because that wait won. Actual continuation/result is later.
- RedeliveryClaim uses the same checkpoint name without consuming handles: record another attempt/disposition without another consumption.
- StaleBookmarkConsumption uses BookmarkConsumed for an already Completed execution: cleanup, not another activity continuation.
- Resume may suspend and create another handle. Preserve both identities.
- Passive expiry is current eligibility at an observation time, not an emitted timeout transition.

Sources: BookmarkConsumptionCheckpointModels.cs, BookmarkConsumptionCheckpointService.cs, WorkflowResumeBookmarkSchedulerWorkHandler.cs, WorkflowCreateBookmarkSchedulerWorkHandler.cs, ActivityCancellationCheckpointService.cs and incident resolution/fault recorders.

## R6 — Durable ingress is a distinct recording authority

**Decision**: Record actual attributable receipt/admission/disposition through the existing owned delivery/admission path, with stable observation IDs and source recording boundary. A runtime checkpoint may retain causal delivery metadata but cannot retroactively claim that this metadata was durably recorded when the request first arrived.

**Rationale**: BookmarkResumeDispatcher returns early for unmatched/rejected/missing targets without any runtime checkpoint. Add bounded exact-target observation recording where an authorized run association exists; unattributed/global diagnostics remain outside this feature's console scope. Duplicate deliveries/attempts and legitimate broadcast recipients remain distinct.

**Alternatives**: Guessing receipt from consumption, attributing by timestamps or recording raw request payloads would violate provenance/access requirements.

## R7 — Bounded authorized query contract

**Decision**: Apply the existing runtime:read plus CanInspectStructureAsync(workflow) check before history access. Keep missing/denied resource behavior equivalent. History contains allowlisted semantic metadata and references existing evidence; it never copies arbitrary checkpoint metadata, values or exception/request text.

Use purpose-separated IRuntimeRecoveryContinuationCodec payloads bound to exact scope/run, query, limit, tenant, audit subject and authorization profile. Reauthorize every read. Keyset by sequence within a captured upper watermark; exact event/bookmark/activity IDs and kind filters are indexed. No offset navigation or unrestricted filtered LongCount per page.

**Text search**: Exact identifier search takes indexed paths. Free text examines at most a configured bounded ordered window per read, matching only safe event summary fields; response reports matched rows and searched-through/continuation coverage. An empty window with more searchable range is not a definitive no-match result. UI counts describe loaded/scanned ranges instead of invented complete filtered totals.

**Alternatives**: SQL Contains over the full retained run, exact full filtered counts on every page, or client downloading all rows cannot satisfy bounded work.

## R8 — Extend the existing workbench and exact execution navigation

**Decision**: WorkflowInstances.tsx owns primary Timeline/Diagram state and separate inspector selection. Reuse ActivityExecutionBrowser, useAttachedExecutionSelection, prepareAttachedExecution, exact ancestry, existing value disclosure and query context isolation. Add history-specific serial observation that retains stale rows and continues live catch-up through future appends; a settled prefix or terminal workflow alone is not permanent source closure.

**Rationale**: Current Timeline is only an activity-summary list in the inspector. useExecutionObservation stops at workflow completion and clears its value on error; it cannot satisfy history's completeness/stale-read behavior. Current selectedEvidenceId must not silently accept arbitrary history-event IDs.

Preserve runSelectionUrl.ts exact execution parameters and existing links. Explicit event/bookmark/view parameters must not reload/reset the run. Extend narrow layout intentionally to stack Timeline then inspector; reuse tabs/focus/public tokens and deferred chunks. A settled watermark describes a stable prefix, not permanent source closure: visible live history keeps catching up through future waits or late delivery observations unless explicitly paused/closed.

## R9 — Reconcile overlapping work and qualify real composition

Current checkout already displays persistenceMode/inspectionGranularity through WorkflowInstanceInspectionFacts; #3305's older omission is not current source behavior. Reuse this display/help.

Open adjacent PRs researched: #3828 head 8bc1129ec299b4b5359e31c6f6176a6ae1b1552d (toolbar/selection), #3847 head eeec9a5d915a5174e9106ab64cd9d8a1e960aa04 (User Tasks/run diagnostics). Do not copy branches wholesale; reconcile live heads before editing shared boundaries.

Use tools/ux-acceptance/run.py with owned hosts/stores and a new history journey/artifact inventory. Existing Connections/Agent passes do not prove this feature. Add spec-required 320px coverage (current general proof uses 360px). Commit source before final composed proof, retain downloadable exact-head evidence and independently inspect it. Four-provider migrations are additive using tools/ef/generate-module-migrations.sh --runtime-incremental RunHistory; preserve Runtime Initial and qualify frozen-store upgrade.
