# Research: production run history

**Provenance**: The decisions below record the 5–6 November prototype investigation retained from closed #3850. They guide the resumed design, but paths, compatibility claims and results must be revalidated per current-main slice. [The slice ledger](slices.md) records the 12 November baseline. Historical passes do not accept the new stack.

Source baseline: main 074259b3565e046701acbce69613120d7bed5f64; feature head initially 1fedc927dd73dbec0104818c10f086ac9aca28de. Three read-only Spec Kit research passes examined capture/coalescing, persistence/API/access and Studio/acceptance. These findings are design evidence, not implementation acceptance.

## R1 — Capture before state folding, persist with the existing commit

**Decision**: Add an optional typed write sidecar to RuntimeCheckpointCommit, separate from RuntimeCheckpointStateChangeSet and RuntimeStateCategory. IRuntimeCheckpointCommitEnricher derives deterministic candidates before coalescing; RuntimeCoalescingSession retains their original ordered groups in a separate buffer while RuntimeCheckpointFold folds continuation changes only. Existing native/EF writers persist queryable history atomically. No execution decision reads a history event, bookmark-history projection or history query result.

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

## R10 — Implementation decisions under delivery authority

The delivery agent, acting on behalf of @sfmskywalker within the authorized end-to-end scope, selected these bounded implementation refinements on 5 November. These are implementation choices, not a claim that the owner personally inspected a candidate.

- Keep closed typed source facts on the logical checkpoint contribution for facts whose actual owner precedes an existing outcome/suspension/fault checkpoint. An actual continuation entry can be carried to that checkpoint without another physical boundary. Activation/input failure before entry cannot create a continuation-start fact. Source facts participate conditionally in replay identity; absent facts retain historical shapes.
- Keep explicit unknown execution ancestry. Null is a known root only when actual typed source provenance establishes it; absent ancestry is a visible scope limitation. Retained scoped ancestry and bookmark-count indexes support bounded reads after native cleanup. These are internal projections owned by Runtime.
- Pin nullable admission capture provenance: true with enabled capture, false when newly admitted with capture off, null for legacy/unknown. Retained null never inherits a new host setting. This permits an honest disabled-at-admission state without fabricating an empty captured head.
- Preserve the timer-before-bookmark guarantee. A timer is independently durable before the suspension checkpoint, so later inspection of that checkpoint cannot establish atomic history for the timer write. The optional timer owner instead retains bounded typed scheduled/terminal history responsibility on its existing timer document, in the same owner write. Retirement remains non-fireable until its pending facts are acknowledged. The existing bounded pump handles these responsibilities through guarded observation append; no generic materialization queue is added. Legacy timer providers do not advertise this capability. Exact owner/claim/deletion guards and restart qualification remain required.
- Explicit purge removes event content but retains compact semantic replay receipts, allocation/policy and a purge boundary through run deletion. Otherwise a delayed retry under another physical commit could resurrect purged history. Exact kind/category/identity indexes and bounded safe text windows serve different query needs.
- Current eligibility observes native handle, workflow/test scope and activity ownership/status. It remains separate from historical disposition and command authority. Changed native versus recorded metadata yields unknown eligibility rather than combining two observations into a false fact.

## R11 — CI-enforced continuation separation and lazy admission qualification

Published foundation CI found a real ownership error: placing HistoryBatches on RuntimeCheckpointStateChangeSet violated the existing unchanged RuntimeContinuationState_DoesNotDependOnHistoryOrAuditRecords invariant. Move the write contribution to RuntimeCheckpointCommit, preserve it independently through cloning/coalescing/flush, and leave continuation state and RuntimeStateCategory free of history/audit records. The conditional fingerprint wrapper remains byte-compatible; absence still emits no history wrapper. This correction does not amend or weaken the invariant.

The same CI run found eager writer/reader qualification while constructing WorkflowStartDispatcher. Generic feature composition has no execution scope, so service resolution must remain possible without one. Resolve the pinned policy and selected atomic writer/reader pair lazily at actual new admission, before its payload is enqueued. Retained admission policy and legacy absent-policy replay remain unchanged. An actual captured admission with an unqualified pairing still refuses.

Runtime-owned policy/provenance and closed typed source contributions are write inputs, not diagnostic read-model authority. The capture module converts those inputs into the checkpoint sidecar. Required capture refusal is limited to runs that pinned that contract; optional/disabled ordinary execution cannot acquire a dependency on querying history. A temporary module or store outage does not authorize an unsafe callback replay or replace existing poison/uncertain-effect handling.

Status: corrective implementation and independent source review in progress. Published failing checks remain recorded; no passing rerun is claimed here.

## R12 — Immutable owner deltas for raw replay

Actual358 composed evidence found repeated IncidentRecorded labels when the strategy checkpoint merely updated an existing incident. Source review also found ValueCaptured delta classification read the current inspection projection. A fresh process re-enriching the same original raw queued checkpoint after a landed write can then choose a different delta and fingerprint. Retaining an already-enriched in-memory object is insufficient proof.

Use the final admitted runtime-history-v3 profile with immutable typed value delta/provenance on the existing raw checkpoint inspection contribution. The accumulator knows its explicit added snapshots: bind their evidence IDs to LastCheckpointId, including a known-empty delta for reprojection. Actual invocation/resume/output/structural owners supply closed work/phase/evaluation provenance before merging. Capture validates and consumes that immutable list without querying the prior inspection state. Actual incident owners contribute Created, Evaluated and terminal Resolved facts; generic upsert does not invent a new fault or resolution.

Do not add a native marker-payload/restore port to preserve private, unmerged intermediate profiles. The existing EF native marker has IDs, time, the full fingerprint and work receipts, not original source/batches. Duplicating full history content there would add retention/reconstruction complexity. Existing main no-history raw wire/fingerprint and markers must remain exact. Private v1/v2 rows remain readable; unavailable future capture support is disclosed, without claiming an intermediate production rollout. Final raw-payload/new-capture replay, purge, changed-source and coalescing controls remain required before acceptance.

Decision made as routine implementation under delegated delivery authority, consistent with ADR0080's pre-GA cleanup and smaller footprint. No execution authority moves to history/query records and no existing invariant is weakened.

## R13 — No-record timer source completion

An actual successful Ephemeral no-record completion may have created then retired a timer before any native head existed. At the existing proven discard branch, a narrow optional method on the timer-owner port validates the exact Completed decision, scope/tenant and current fence, then proves native/head absence. Reject every active captured timer before clearing any bounded retired prefix. Keep compact first-wins timer receipts with the actual completion checkpoint/time, no payload/meta; advance the queue only after all owed source responsibility is discharged. Unknown/failing discharge leaves the original work owed and creates no native checkpoint/history append.

Source creation and discharge serialize on the same exact native liveness owner before their first consistent reads, including MySQL's default repeatable-read behavior. Existing deterministic timer IDs retain first-winner replay. Under native/head absence, an explicit completed no-record receipt refuses a new captured timer ID; absence alone is not completion proof. Once real native authority exists, its current status, policy and fence govern later sources, including legitimate recovered nonterminal work. Source receipts are not permanent execution authority.

Extend the new pending-run index with the retired equality key for bounded active refusal/receipt discovery. First native history after a partial discharge uses explicit source completion proof to retain HasGaps, including a fault before timer reexecution. Later coverage keeps that gap. There is no additional table, generic queue or arbitrary TTL. Successful no-record runs have no native deletion candidate; compact timer receipts remain source-owned until explicit owned cleanup/store lifecycle. No public orphan-purge feature is implied.

Status: source implementation and controls in progress; normal compilation/provider/race/migration qualification is pending.

## R14 — Current-main boundary review for the resumed stack

Revalidated source at `dff7c490d0e5501b22d4ba89beb389c956d4f423`, with S01 documentation at `9d7b8d3ee6145fc8846628352413fccadb0b3853`. These are source observations, not passing runtime/provider/UX receipts.

- **Capture/folding:** `RuntimeCheckpointCommitter.CommitAsync` still enriches and validates before persistence policy; `RuntimeCheckpointFold` still folds continuation state. `RuntimeCoalescingSession.CanDiscardCompletion` still permits never-persisted Ephemeral completion only without surviving responsibilities. Keep history in the checkpoint write sidecar/separate buffer and preserve this discard rule. New model/native admission conditions on `RuntimeCheckpointCommit` remain authoritative and are not replaced by history.
- **Replay/provider:** #4166's `EfRuntimeCheckpointCommitStore` uses a conditional marker-first read, preserving a plain successful checkpoint's reduced read path. A history participant must qualify its own replay/allocation path without reverting that optimization for history-free commits. Existing transaction, marker-last save, fence and ambiguous-failure reconciliation remain the atomic boundary. The in-memory `InMemoryRuntimeCheckpointStoreState` still owns the shared write gate; a separate history singleton would not prove atomicity.
- **Scope/deletion:** `EfWorkflowExecutionDeletionStore` qualifies the actual complete selected provider family, including decorators and background-work support. New history projections must join the existing scoped deletion transaction and actual provider qualification. Same-provider booleans alone cannot establish shared context/state. In-memory deletion uses the checkpoint state owner too.
- **API/access:** `ActivityExecutionInspectionService` and `ActivityExecutionStateViewService` resolve the run and call `CanInspectStructureAsync` before child reads, with null for missing/denied. Value inspection/payload grants remain separate. History must use this authority, exact execution hierarchy and authenticated purpose-specific continuation codec. The existing `RuntimeStorePage` rejects empty pages with a continuation; history needs its own scanned-window result, so an empty search window can still advance.
- **Studio/proof:** current `WorkflowActivityExecutionDetails` builds compact input rows from declarations, authored sources, compiled bindings and runtime snapshots, with protected-source handling. Reuse these #4108/#4174 components inside Data; do not restore the prototype's old input rendering. `WorkflowInstances` still owns exact activity/evidence selection, so new event/bookmark selection must stay separate. Current composed proof remains `tools/ux-acceptance/run.py` plus `Studio/tests/acceptance/journeys.spec.ts`; the old feature's receipts do not qualify this stack.

S02 adds only immutable participant/query models, validation/identity helpers, read/observation/retention ports and compatibility/invariant tests in existing Runtime projects. The ports are public because third-party atomic providers need the same semantic boundary; no provider, capture registration, API capability or Studio behavior is enabled. Public API additions are Minor-compatible and retain the existing envelope constructor/category ordinals. Provider unavailability uses the Runtime domain exception with original cause/run/operation, separately from hard integrity failure; write failure does not prove rollback. The new creation-evidence enum uses an Evidence noun rather than State, preserving the PriorState member key and enum values. Writer pairing, source proof, raw replay-after-enable, Required-policy refusal, deletion races, unknown creation history and empty-window traversal remain the fixed contracts in [atomic history](contracts/atomic-history.md) and [query inspection](contracts/history-inspection.md), with implementation proof required in their later slices.


S02 source R3 converged at `cf821356b07d0d725ae3a621dc2d025713a6500d` and normal ready CI43408672384 began. Main then advanced through #4230/#4231 draft kernel design/walkthrough and #4250 isolated prototype to `4cf060e634710a3503fe2f0aec2f9ef8512b1727`. The additive merge takes those main-owned files unchanged; no S02 model/test/API blob changes. A normal ledger reconciliation records this boundary; full-unit source R4 and fresh exact-head required CI follow. Original main-push CI43406610930 was cancelled after advancement, not passed or failed; protected S01 merge-group success remains separate. All24 S02 cases remain NOT RUN at this receipt; parent implementation/provider/UX acceptance remains open.
