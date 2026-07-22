# 126 — Runtime seam C: non-terminal child→parent structural notification (BPMN Phase 3 prerequisite for escalation + event subprocesses)

## Goal

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
- **Rejected shapes** (recorded for the record): overloading `CompleteActivity` with a new
  `SchedulerCompletionKind` touches the load-bearing, wire-serialized
  `RuntimeCompleteActivityCommandPayload` invariants and risks the two `CompleteActivity` handlers'
  `CanHandle` predicates disagreeing; `DeliverSignal` (kind 9) exists but has zero
  payload/handler/test machinery — as much work as a new kind with less clarity.

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
  3. Notifications compose with a `Defer` or `Complete` continuation; a `Fault`/`Cancel`
     continuation with staged notifications is an evaluation fault (the seam-A/B rule, mirrored).
     Multiple notifications per evaluation are allowed and preserve staging order.
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
- The activation-claim discipline gives at-most-once callback invocation per work item.

### D5 — Stated cuts

Broadcast/multi-level notifications (one hop only — child to direct parent; bubbling is the
consumer's recursion); notification replies (one-way); cross-workflow-instance signaling (the
stimulus system's job); delivery-order guarantees beyond D4; wiring `DeliverSignal`;
payload schemas (opaque JSON, consumer-defined); any BPMN consumption (spec 127).

## In scope (this slice)

- Runtime Core: `WorkflowExecutionCommandKind.NotifyParentActivity`;
  `RuntimeNotifyParentCommandPayload`; `RuntimeParentNotificationRequest` model;
  `IRuntimeActivityChildNotificationHandler` + `ActivityChildNotifiedContext`;
  `IRuntimeActivityExecutionContext.RequestParentNotification`/`GetParentNotificationRequests` +
  `SimpleActivityExecutionContext` backing.
- Activities Runtime: harvest/build/enqueue in the three child-evaluation handlers'
  Defer/Complete commit paths (post-commit intents, derived ids);
  `WorkflowNotifyParentActivitySchedulerWorkHandler` (registration, `Running` guard, ack-bypass,
  claim, marker-gated projections, callback dispatch, continuation commit); staging validation
  (root reject, code/payload bounds, Fault/Cancel exclusion).
- Tests (runtime-level, `StructuralExecutionTestSupport` doubles + the scheduler-work handler test
  recipes): stage-on-defer → callback invoked with code/payload; multiple notifications ordered;
  non-implementing parent ack-bypass; root staging rejected; Fault+staged = evaluation fault;
  parent-not-Running ack; late delivery after child completion (both orders tolerated); seam-A
  staging from within a notification evaluation lands in the same commit; idempotent redelivery;
  deterministic derived ids; poison-path sanity (handler crash → poison record with the new frozen
  name).
- Docs: runtime `EXTENSION_POINTS.md` seam-C entry (beside the seam-A/B/live-children/
  scoped-variable entries).

## Out of scope

Everything in D5. Zero BPMN-module changes (spec 127). No changes to existing handlers' `CanHandle`
predicates, the `CompleteActivity` payload invariants, `DeliverSignal`, or the fusion driver.

## Functional requirements

**FR-1 — Staging.** A structural child with a parent can stage `RequestParentNotification(code,
payload)` during any of its own evaluations; staged notifications commit atomically with the
child's Defer/Complete state as durable post-commit work items with deterministically derived ids.
Root staging, empty/oversized codes, oversized payloads, and Fault/Cancel co-staging fault the
evaluation deterministically.

**FR-2 — Addressing.** The notification targets the notifying child's committed parent — always and
only. No API surface accepts a target.

**FR-3 — Delivery.** A parent implementing `IRuntimeActivityChildNotificationHandler` receives
`OnChildNotifiedAsync` with the code, payload, and the notifying child's identity (aei, node,
iteration id) exactly once per work item (activation-claimed), with the spec-119/123 marker-gated
projections populated. The child's continued execution is unaffected by delivery.

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
  durable-first (outbox-ridden, never out-of-band), idempotent, and non-terminal (the child's own
  lifecycle is untouched by staging or delivery).
- No changes to `RuntimeCompleteActivityCommandPayload`, `SchedulerCompletionKind`, existing
  `CanHandle` predicates, or existing frozen `HandlerName` values. The new `HandlerName` is chosen
  once and treated as frozen.
- Existing seam rules unchanged: seam-A/B validation, continuation exclusions, `CancelLiveWork`
  logical-only, activation claims, poison/retry policy.
- Spec 112/115/119/123 runtime suites pass unmodified. Full projects at verification, never
  filtered subsets.

## Success criteria

- All FR tests green at the runtime level with test doubles (no BPMN involvement).
- Full test projects green: Activities Runtime, Workflows Runtime, BPMN (untouched, regression
  only), BPMN Interchange (untouched), ControlFlow, Architecture. Full solution build clean.
- Runtime EXTENSION_POINTS documents the seam beside its siblings.
