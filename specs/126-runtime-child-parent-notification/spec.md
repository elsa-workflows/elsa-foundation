# 126 — Runtime child-to-parent notifications and combined fault outcomes

## Goal

The original nonterminal notification contract remains below. The [#2967](https://github.com/elsa-workflows/elsa-foundation/issues/2967) amendment in D6 adds an intentional-fault outcome without changing ordinary notification delivery. Historical source observations describe their stated baseline, not the current tree.

Give a **running structural child** a way to raise a **coded, durable, spoof-proof notification to
its own parent** — triggering a structural evaluation of the parent **while the child keeps
running**. This is the third child↔parent runtime seam, completing the family: seam A cancels a
child's subtree (parent→child, spec 112), seam B absorbs a child's fault (parent→child at fault
time, spec 115), and seam C — this spec — lets a child signal upward mid-flight (child→parent,
non-terminal).

The motivating consumer is BPMN escalation (spec 127 next): an escalation throw inside a nested
`BpmnProcess` must reach the escalation boundary on the subprocess element in the **parent** scope
while the nested process continues past the throw (non-interrupting), or so the parent can tear the
child down via seam A (interrupting). Event subprocesses need the same channel. Today **no such
channel exists**: every child→parent path is terminal (completion or fault), the stimulus system is
external/global and unreachable from activity execution, and seams A/B point the other way.

The seam is general runtime surface, BPMN-unaware, marker-gated, and shaped by the existing
completion/fault machinery — a new work-item kind riding the same durable post-commit outbox, a
third callback interface parallel to the completion/fault handlers, and the same late-absorption
discipline the completion path already uses.

## Context (what exists today, origin/main = b3d34c628; line numbers drift — verify at implementation)

- **The completion template.** Child→parent completion is a chain of `CompleteActivity` work items
  (`RuntimeSchedulerWorkItem`, `WorkflowExecutionCommandKind` — 17 kinds today) discriminated by
  `SchedulerCompletionKind` in `RuntimeCompleteActivityCommandPayload` (`ActivityCompleted` →
  `ParentCompletionEvaluation`) plus **metadata** flags (`runtime.childFaulted`, `IncidentId`) —
  NOT separate command kinds. The parent-directed item is built by `ChildFaultParentEvaluation`
  (fault flavor) / `WorkflowCompleteActivitySchedulerWorkHandler.EnqueueParentCompletionEvaluationAsync`
  (completion flavor) with ids derived deterministically via `RuntimeChainId.Derive`, and lands in
  `WorkflowParentActivityCompletionSchedulerWorkHandler`, which dispatches to
  `IRuntimeActivityChildCompletionHandler.OnChildCompletedAsync` or
  `IRuntimeActivityChildFaultHandler.OnChildFaultedAsync`.
- **Atomic staging exists.** A child's own commit can carry parent-directed follow-up work:
  `RuntimeCheckpointCommit.PostCommitIntents` (`EnqueueSchedulerWork` intents, built via
  `SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent`) fold into the durable
  `PostCommitOutbox` and are dispatched after commit by `RuntimePostCommitOutboxProcessor` /
  `RuntimeSchedulerPostCommitIntentDispatcher`. The fault path already stages parent evaluations
  this way (`ActivityFaultIncidentRecordRequest.PostCommitSchedulerWorkItemsOrNull`); the invoke
  handler rides child schedules and the completion item the same way. The child's **Defer/suspend
  commit** is the natural carrier for a notification staged by a still-running child.
- **Single-writer FIFO.** `WorkflowSchedulerDrainer` drains one execution's queue sequentially
  (claim-based exclusivity); evaluations for one execution never run concurrently. A notification
  and the notifying child's own later completion are independent chain items — the notification MAY
  be processed after the child has completed/faulted, so delivery must adopt the existing
  late-absorption guards (`parentState.Status != Running → ack`; non-handling parent → ack-bypass,
  the `callbackBypassParentState` pattern; terminal-target skips as legal races).
- **Handler registration + poison.** `IWorkflowSchedulerWorkHandler` implementations register via
  `TryAddEnumerable` (`RuntimeCoreServiceCollectionExtensions` / `ActivitiesRuntimeFeature`); the
  drainer's crash path records to the poison store with the handler's `HandlerName` as a **frozen
  wire value**. Idempotency = deterministic `WorkItemId`/`IdempotencyKey` derivation + fence-checked
  consumes. A new kind inherits all of this.
- **ADR 0047 fusion is leaf-only.** Structural children never fuse; the completion cascade is
  explicitly not fused. A notification riding the ordinary outbox has no fusion interaction; it
  must never be enqueued out-of-band.
- **Parent evaluation preconditions** (reused): pinned-executable read + validation, parent state
  load + `Running` guard, activation claim (`ClaimStructuralCallbackAsync`), marker-gated
  projections — live children for `IRuntimeLiveChildActivityConsumer` (spec 119), scoped variables
  for `IRuntimeScopedVariableReader` (spec 123).
- **Original alternatives for ordinary notifications**: overloading `CompleteActivity` with a new
  `SchedulerCompletionKind` touches the load-bearing, wire-serialized
  `RuntimeCompleteActivityCommandPayload` invariants and risks the two `CompleteActivity` handlers'
  `CanHandle` predicates disagreeing; `DeliverSignal` (kind 9) exists but has zero
  payload/handler/test machinery — as much work as a new kind with less clarity. Ordinary
  notifications keep the separate route chosen here. D6 deliberately revisits the completion-kind
  choice for an indivisible fault-plus-notifications outcome, with coordinated payload, routing,
  pipeline, fusion and poison discriminators; the original rejection is not a constraint on D6.

## Design decisions

### D1 — Staging: `RequestParentNotification` on `IRuntimeActivityExecutionContext`

- New member: `void RequestParentNotification(string code, JsonElement? payload = null)` +
  `IReadOnlyList<RuntimeParentNotificationRequest> GetParentNotificationRequests()` — the
  spec-112/115 staging shape (private list on `SimpleActivityExecutionContext`, harvested by the
  handler after the callback returns).
- **Staging rules**, validated at plan time in the child's own evaluation (each violation = a
  deterministic evaluation fault, the seam-A/B precedent):
  1. The staging activity must have a parent (`ParentActivityExecutionId` non-null on its committed
     execution state). A root activity staging a notification is rejected.
  2. `code` is required, non-empty, ≤ 128 chars (the `RuntimePostCommitIntent.ValidateKind`
     discipline); `payload` is optional and size-bounded by the same limit policy as
     `RuntimeStructuralStateUpdate` payloads.
  3. Notifications compose with `Defer` or `Complete` through the existing notification path.
     An intentional `Fault` carries its ordered notifications through D6. `Cancel` with staged
     notifications remains an evaluation fault. Multiple notifications preserve staging order.
- **Addressing is derivation, not input**: the target is always the notifying child's OWN committed
  `ParentActivityExecutionId` — the child cannot name a target. This is the seam's spoof-proofing
  core, mirroring `ChildFaultParentEvaluation` deriving the parent from committed state.
- Staged notifications are honored on **all three child evaluation kinds** (invoke,
  child-completion/child-fault of the child's own children, bookmark-resume) — each handler's
  Defer/Complete commit gains the same harvest → build → `EnqueueSchedulerWork` post-commit intent
  step, so the notification is durable **atomically with the child's own state commit**.

### D2 — Wire shape: new command kind + dedicated handler (Shape B)

- `WorkflowExecutionCommandKind.NotifyParentActivity` (new enum value, appended).
- `RuntimeNotifyParentCommandPayload`: `ParentActivityExecutionId` (the target evaluation's
  activity), `NotifyingChildActivityExecutionId`, `Code`, `Payload` (nullable JSON), plus the
  notifying child's executable node id and iteration id (loaded once at build time from the child's
  committed state — the `ActivityChildFaultedContext` field set, minus the incident). Constructor
  invariants: parent ≠ child, ids non-empty, code bounds.
- Work-item identity: `RuntimeChainId.Derive(sourceWorkItemId, "notify-parent:{ordinal}")` — one
  derived id per staged notification, ordinal = staging order, so redelivery and replay are
  idempotent and multiple notifications from one evaluation never collide.
- New handler `WorkflowNotifyParentActivitySchedulerWorkHandler` (Activities.Runtime, registered in
  `ActivitiesRuntimeFeature` beside the parent-completion handler; its `HandlerName` becomes a new
  frozen wire value — name it deliberately). `CanHandle`: exactly `NotifyParentActivity`. No other
  handler's `CanHandle` changes.

### D3 — Delivery: `IRuntimeActivityChildNotificationHandler`

- New callback interface beside the completion/fault pair:
  `IRuntimeActivityChildNotificationHandler` with
  `ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification)`.
  `ActivityChildNotifiedContext`: `NotifyingChildActivityExecutionId`,
  `NotifyingChildExecutableNodeId`, `NotifyingChildIterationId`, `Code`, `Payload`.
- The handler mirrors the parent-completion handler's setup: pinned-executable read + validation,
  parent state load, **`Running` guard (not Running → ack, no fault)**, activation claim,
  marker-gated projections (live children for `IRuntimeLiveChildActivityConsumer` — populated for
  notification evaluations exactly like completion evaluations, so a consumer can resolve the
  notifying child's live aei by `(node, iteration)`; scoped variables for
  `IRuntimeScopedVariableReader`).
- **Non-implementing parent → ack-bypass** (the `callbackBypassParentState` pattern): the
  notification is consumed without invoking any callback and without fault. The seam is opt-in by
  interface implementation, like the completion/fault handlers.
- **Late notifications deliver.** The notifying child's CURRENT status is not a delivery gate — a
  child that completed or faulted after staging still gets its notification delivered (the consumer
  decides what a late signal means; BPMN escalation semantics require post-throw-completion
  delivery to still fire a non-interrupting boundary). Only the PARENT's terminal state acks the
  item away. Workflow-terminal drops (queued items not dispatched after terminal status) are the
  existing drainer behavior, unchanged and documented.
- **Continuation rules for the notification evaluation**: `Defer` and `Complete` are legal;
  `Fault`/`Cancel` are legal (a parent may decide the notification is fatal). Child-schedule
  staging and seam-A `RequestChildSubtreeCancellation` staging are legal exactly as in a
  child-completion evaluation (this is how an interrupting consumer tears the notifying child down
  in the same commit); seam-B absorption stays child-fault-evaluation-only (unchanged rule). All
  existing continuation/staging exclusion rules apply verbatim.

### D4 — Ordering and consistency (documented, tested)

- FIFO per execution: a notification staged before the child's completion item is delivered before
  the parent's completion evaluation of that child **when staged on an earlier commit**; a
  notification staged on the SAME evaluation that completes the child may deliver after the
  completion evaluation (independent derived items) — consumers must tolerate both orders. The
  runtime makes no cross-item ordering promise beyond the queue's per-execution FIFO of enqueue
  order.
- Idempotent redelivery: same derived id → fence-checked consume; re-processing is a no-op.
- Activation claims serialize callback evaluation. Before a final decision is accepted, a temporary
  runtime dependency outage may retry callback code with its retained source; the fenced final commit
  and processed marker prevent repeating accepted effects. Callback code must stage its effects through
  the runtime context. Background admission keeps its separate atomic claim/publication contract.

### D5 — Stated cuts

Broadcast/multi-level notifications (one hop only — child to direct parent; bubbling is the
consumer's recursion); notification replies (one-way); cross-workflow-instance signaling (the
stimulus system's job); delivery-order guarantees beyond D4; wiring `DeliverSignal`;
payload schemas (opaque JSON, consumer-defined); any BPMN consumption (spec 127).

### D6 — One intentional fault and its ordered notifications (#2967)

A successfully returned structural `Fault` may include outward notifications. The child fault,
its exact incident identity, and **one** combined parent work item commit atomically. The batch
is indivisible for outbox paging, dispatch and queue claims; separate rows and sequence numbers
are not an ordering barrier. This applies to initial execution and structural child callbacks,
including a parent's notification/outcome callback that bubbles outward. A throw, failed
validation or activation disposal failure discards staged effects and follows the existing
exception path. An empty batch retains the legacy fault route.

`RuntimeCompleteActivityCommandPayload` gains an optional `ParentOutcome` v1 envelope containing
the ordered notifications, immutable masked `ActivityFault`, and incident ID. The envelope never
contains an exception or stack trace. Its child identities agree with the payload and committed
parentage. `SchedulerCompletionKind.ParentOutcomeEvaluation` is appended without changing old
ordinals. The old constructor and JSON shape remain available; a null envelope is omitted.
Unsupported envelope versions are rejected explicitly. The existing parent evaluation handler
owns the new route; its persisted `HandlerName` remains unchanged. Routing, pipeline selection,
fusion exclusion and poison classification must agree on the discriminator.

`IRuntimeActivityChildOutcomeHandler.OnChildOutcomeAsync(ActivityChildOutcomeContext)` receives
one batch against current parent state and returns one continuation. The runtime does not invoke
two legacy callbacks in sequence or invent an intermediate parent state.

| Parent capability | Combined outcome behavior |
|---|---|
| Combined handler | One callback with ordered notifications, original fault and exact incident. |
| Fault handler, no notification handler | Existing fault callback once; notifications have no consumer as under D3. |
| Neither handler | Existing non-handling fault behavior; original incident remains blocking. |
| Legacy notification handler, with or without fault handler, no combined handler | Visible durable incompatibility; neither legacy callback runs. Retain the original child fault, incident and combined payload as evidence. Never silently acknowledge the batch as handled. |

Compatibility refusal occurs at parent dispatch, after the child incident is committed. Rejecting
at child staging would replace its intentional fault with a validation exception. Ordinary
notification-only and fault-only events keep their original contracts. Acknowledgement by a non-Running parent
remains a legal late-delivery race; a combined fault does not fabricate a live child.

A combined callback may request spec 115 absorption of the **exact** incident with `Defer` or
`Complete`, together with descendant reclaim and sibling cancellation. These changes, the parent
continuation and the processed-event marker share one checkpoint. The child's historical state
remains `Faulted`. Without absorption its incident remains blocking. A returned `Fault` or
`Cancel` cannot absorb the child incident. Existing terminal-versus-scheduled-child checks apply.
Spec 132 metadata hygiene also applies: the envelope is not inherited by newly scheduled children
or ordinary upward completion work.

The processed-event evidence must survive a crash after the parent checkpoint and before source
acknowledgement. Replay must not re-invoke an already committed callback or duplicate its effects.
This is a durable convergence guarantee, not exactly-once execution of physical external work.

The original incident also stores the combined work item's ID in
`runtime.parentOutcomeWorkItemId`, in the same checkpoint as the child fault and outbox intent.
Both ordinary incident-strategy resolution and the blocking-incident backstop wait while this
incident is unresolved, its child remains `Faulted`, its parent is `Running`, and the child's
processed marker does not equal that exact work item ID. A failed dispatch cannot make an empty
local queue terminate the workflow before the outbox retries. Hold the entire incident batch:
an outstanding ancestor outcome can still decide a bubbled descendant fault. The hold ends when
the parent evaluation commits its processed marker or the parent is no longer eligible to consume
the event (the existing handler requires `Running`). Missing/corrupt parentage follows the normal
validation/incident path. A poisoned item retains its visible intervention incident. No new store
or mutable pending flag is introduced; existing durable state determines whether evaluation is pending.
The hold also survives exhausted enqueue retries: a `FailedFinal` outbox row does not count as a
committed parent decision. The original child incident stays blocking and the workflow stays
`Running`; no notifications are silently discarded to make it terminal. This is an intervention
boundary, not automatic recovery. The existing enqueue-delivery limitation remains: the failed
row is retained and logged as event 68103, but has no dedicated failure incident or generic redrive
API. See [durable resumption](../../docs/runtime-durable-resumption.md#when-the-sweep-takes-a-live-drains-continuation).

**Rollout and rollback:** deploy the readers to every host that can drain the execution provider
before admitting producers of v1 outcomes; stop admission/drain as necessary during the upgrade.
Old binaries can have a broad fallback that acknowledges unknown work, so the new discriminator
alone does not make a mixed-version deployment safe. Rollback stops new v1 production and retains
compatible readers until all accepted v1 items have drained or been explicitly reconciled. There
is no database schema migration and no silent conversion of accepted combined items to legacy
fault work. Library adoption and actual BPMN combined semantics remain #2735.

**Proof:** cover the capability matrix; immutable ordered batch and committed-parent addressing;
original fault masking/code and incident; throw/validation/disposal/Cancel exclusions; absorption,
reclaim and propagation; legacy serialization and unknown versions; metadata hygiene; provider
reload, fail-first dispatch through the real drain observers and concurrent/split claims; and replay after commit before ack.
Use in-memory and EF evidence plus a mutation that breaks the claimed safeguard. Runtime and
BPMN compatibility suites, architecture, generated maps and relevant rebuilt-host REST e2e are
required. Record commands and actual results in the PR; this amendment alone is not pass evidence.

## In scope (original nonterminal slice)

- Runtime Core: `WorkflowExecutionCommandKind.NotifyParentActivity`;
  `RuntimeNotifyParentCommandPayload`; `RuntimeParentNotificationRequest` model;
  `IRuntimeActivityChildNotificationHandler` + `ActivityChildNotifiedContext`;
  `IRuntimeActivityExecutionContext.RequestParentNotification`/`GetParentNotificationRequests` +
  `SimpleActivityExecutionContext` backing.
- Activities Runtime: harvest/build/enqueue in the three child-evaluation handlers'
  Defer/Complete commit paths (post-commit intents, derived ids);
  `WorkflowNotifyParentActivitySchedulerWorkHandler` (registration, `Running` guard, ack-bypass,
  claim, marker-gated projections, callback dispatch, continuation commit); staging validation
  (root reject, code/payload bounds, Cancel exclusion; intentional Fault follows D6).
- Tests (runtime-level, `StructuralExecutionTestSupport` doubles + the scheduler-work handler test
  recipes): stage-on-defer → callback invoked with code/payload; multiple notifications ordered;
  non-implementing parent ack-bypass; root staging rejected; intentional Fault+staged follows D6;
  parent-not-Running ack; late delivery after child completion (both orders tolerated); seam-A
  staging from within a notification evaluation lands in the same commit; idempotent redelivery;
  deterministic derived ids; poison-path sanity (handler crash → poison record with the new frozen
  name).
- Docs: runtime `EXTENSION_POINTS.md` seam-C entry (beside the seam-A/B/live-children/
  scoped-variable entries).

## Out of scope

Everything in D5. No BPMN-module semantic changes in this runtime slice. D6 deliberately extends
the completion payload, routing and fusion exclusion; `DeliverSignal` remains outside scope.

## Functional requirements

**FR-1 — Staging.** A structural child with a parent can stage `RequestParentNotification(code,
payload)` during any of its own evaluations; staged notifications commit atomically with the
child's Defer/Complete state as durable post-commit work items with deterministically derived ids.
An intentional Fault uses the indivisible D6 outcome. Root staging, empty/oversized codes, oversized
payloads, and Cancel co-staging fault the
evaluation deterministically.

**FR-2 — Addressing.** The notification targets the notifying child's committed parent — always and
only. No API surface accepts a target.

**FR-3 — Delivery.** A parent implementing `IRuntimeActivityChildNotificationHandler` receives
`OnChildNotifiedAsync` with the code, payload, and the notifying child's identity (aei, node,
iteration id), with the spec-119/123 marker-gated projections populated. One fenced final decision
accepts its effect set per work item; temporary dependency failure before that decision may retry
the callback with its retained source. The child's continued execution is unaffected by delivery.

**FR-4 — Tolerance.** A non-implementing parent acks silently; a non-Running parent acks silently;
a notification whose child has since completed/faulted still delivers; redelivery is idempotent.

**FR-5 — Consumer powers.** A notification evaluation may return any continuation and may stage
child schedules and seam-A subtree cancellations under the existing exclusion rules; seam-B
absorption remains rejected outside child-fault evaluations.

**FR-6 — Determinism.** Identical runs produce identical work-item ids and identical delivery
ordering per D4; the ordinal-suffixed derivation never collides.

**FR-7 — Non-regression.** All existing scheduler-work handling, completion/fault chains, payload
invariants, and suites are byte-identical for workflows that never stage a notification.

## Invariants that MUST survive

- The seam is one-hop, child→direct-parent, derivation-addressed, opt-in (interface-gated),
  durable-first (outbox-ridden, never out-of-band), and idempotent. Ordinary notifications are
  nonterminal; D6 preserves the child's intentional fault rather than changing its verdict.
- Existing frozen `HandlerName` values, enum ordinals and legacy payloads remain unchanged.
  D6 deliberately adds a versioned route and audits every relevant `CanHandle` predicate.
- Existing seam rules unchanged: seam-A/B validation, continuation exclusions, `CancelLiveWork`
  logical-only, activation claims, poison/retry policy.
- Spec 112/115/119/123 runtime suites pass unmodified. Full projects at verification, never
  filtered subsets.

## Success criteria

- All FR tests green at the runtime level with test doubles (no BPMN involvement).
- Full test projects green: Activities Runtime, Workflows Runtime, BPMN (untouched, regression
  only), BPMN Interchange (untouched), ControlFlow, Architecture. Full solution build clean.
- Runtime EXTENSION_POINTS documents the seam beside its siblings.

## Deviations from the ratified plan

- **Resume-handler harvest not wired (tripwire 1 outcome).** D1 lists bookmark-resume as one of the three
  child-evaluation kinds that harvest staged notifications. On inspecting the code, the bookmark-resume path
  (`WorkflowResumeBookmarkSchedulerWorkHandler`) has **no structural staging surface**: it hard-throws for any
  activity that is not an `IStatefulActivity` (leaf stateful activities only), structural activities
  (`IRuntimeStructuralActivity`, e.g. `BpmnProcess`) never suspend on bookmarks and so never reach it, and the
  `SimpleActivityExecutionContext` it constructs is never passed to the resumed activity — so
  `GetParentNotificationRequests()` there is unreachable and always empty. A structural child's evaluations
  that *can* stage a notification therefore occur only in the invoke handler (its own `ExecuteStructureAsync`)
  and the parent-completion handler (its `OnChildCompletedAsync`/`OnChildFaultedAsync` over its own children) —
  both of which are wired. Wiring the resume handler would add dead, untestable code (no structural activity
  resumes via bookmark; no way to stage there), so it was intentionally not added. The two live harvest points
  fully cover the seam and the motivating BPMN-escalation consumer (which drives through structural
  `BpmnProcess` evaluations only). The resume path is byte-identical (non-regression).
- **Payload size bound.** D1 rule 2 bounds the payload "by the same limit policy as `RuntimeStructuralStateUpdate`
  payloads", but `RuntimeStructuralStateUpdate` carries no explicit byte cap (only presence/policy checks). To
  satisfy FR-1's "oversized payloads fault" requirement deterministically, `RuntimeParentNotificationRequest`
  enforces a concrete cap of 8 KiB (UTF-8) on the serialized payload; the code cap is the ratified 128 UTF-16
  code units. Both are validated at staging time (in the request constructor), so a violation surfaces as a
  deterministic evaluation fault through the existing callback try/catch — the same effect the ratified plan
  specifies, reached one layer earlier than plan time.
- **Tripwires 2–5: none tripped.** `ClaimStructuralCallbackAsync` claims a notification evaluation cleanly via a
  new field-level overload (the claim key shape is the parent's activity-execution id + a fresh attempt, which a
  notification evaluation satisfies exactly like a completion evaluation). The shared subtree-cancellation
  planner was invoked from the new handler without duplication (extracted into
  `StructuralParentEvaluationSupport`, shared with the completion handler). Delivering to a parent whose
  notifying child has terminalized tripped no guard (the child's status is never read at delivery; only the
  parent's `Running` guard applies). No spec-vs-code contradiction required weakening any invariant.
