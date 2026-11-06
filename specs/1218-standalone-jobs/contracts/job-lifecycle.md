# Proposed standalone job lifecycle contract

Status: reviewed implementation design; public ABI and runtime guarantees remain subject to concrete consumer qualification. Domain admission is accepted through ADR0096 on the owner's scoped authority; no multi-architect vote or runtime acceptance is asserted.
Source audit: `e0ba87ecfc3b1e774c509ed274b19cd3f34a1124` (5 November 2026).
Canonical request: [#3586](https://github.com/elsa-workflows/elsa-foundation/issues/3586).
Owner selections include the durable permission cutoff and default coalesced catch-up described below.

## Authority and deployment boundary

- One selected job authority owns accepted invocations, schedules, attempts, retry eligibility, pause gates, concurrency permits and retained results.
- Registered .NET handlers run without a workflow graph, workflow authoring stack or Studio. Independently hosted workers and optional native/BPMN adapters use the same handler contract.
- A provider adapter must preserve this contract; Elsa and a third-party engine must not independently own the same schedule, retry budget or result selection.
- Optional `Elsa.Jobs` admission is recorded in [ADR0096](../../../docs/adr/0096-jobs-owns-durable-handler-invocations.md) under the owner's explicit scoped adoption of 6 November 2026.
- Native/BPMN models retain parent state, cancellation and continuation semantics. There is no global kernel model-name switch or new graph language.

## Durable identity and acceptance

| Identity | Proposed meaning |
|---|---|
| Scope | Trusted `(PersistencePartition, TenantId)`; included in durable keys, uniqueness, cursors and authorization. |
| Handler | Explicit ordinal `(JobType, HandlerVersion)` with registered input/result schema aliases and versions; never an assembly-qualified CLR name. |
| Submission | Scope plus client idempotency key and canonical request fingerprint. |
| JobId | One immutable accepted invocation, handler pin, bounded serialized input, policy snapshot and association. |
| AttemptId | One authorized execution attempt with monotonic attempt number; separate from its worker ownership fence. |
| ResultId | Immutable authoritative terminal result with outcome, schema identity and payload hash. |
| Physical receipt | Bounded immutable report of a particular attempt's physical completion; does not itself select a terminal result. |
| OccurrenceId | Schedule identity/revision plus scheduled UTC instant; independent of fire time, worker and retry attempt. |
| ContinuationId | Exact model association plus retained ResultId and deterministic model input/resume identity. |

- Acceptance receipts advertise a minimum key-recognition-until instant, pinned by the accepted policy (proposed default 30 days). Within that window, matching replay returns the same JobId and a different request under the key conflicts.
- Recognition records cannot be retired while that job or its delivery/consumer responsibility remains pending, even after the minimum window. Payload retention never silently shortens recognition.
- After the advertised window and all obligations are settled, explicit recognition cleanup may retire the key. Reuse after that cleanup may create a new JobId; callers must not assume indefinite deduplication or use expired keys to reconcile an uncertain submission. Direct lookup by retained JobId remains separate from key recognition.
- The finite window applies to ordinary producer keys. Trusted model, schedule and other durable outbox origins use a separate exact-origin admission receipt/tombstone; ordinary key cleanup cannot retire their recognition.
- A durable-origin receipt remains protected until exact owner proof covers all retained retry paths and already-dispatched/in-flight duplicates. If that cannot be proved, retain a compact origin tombstone that resolves the original JobId or rejects the retired origin; never treat replay as fresh work. Sender Delivered, parent terminal or receiver job/result settlement alone is insufficient retirement proof.
- Independent model submissions retain the same origin recognition even without an awaited result. Only a validated trusted adapter can supply a durable origin, rather than an arbitrary tenant request forging it.
- Acknowledgement means accepted state committed durably, including while paused. An uncertain ordinary submission is reconciled using its protected key; an expired/cleaned key cannot safely resolve that uncertainty. Durable-origin replay uses its separately protected receipt.
- Immediate, delayed and recurring invocations share acceptance semantics; acceptance does not reserve an execution slot.
- Payloads contain bounded values and authorized secret/connection references, never delegates, services or plaintext credentials.
- Handler version/schema support is pinned. Missing deployed support leaves work unavailable and observable without spending a business attempt.
- Handler context exposes stable invocation idempotency identity, JobId/AttemptId, cooperative cancellation and bounded progress reporting.
- Progress, logs and discovery projections are not lifecycle authority. Tenant input cannot supply a trusted parent association or persistence partition.

## States and transitions

- Job inspection distinguishes accepted, delayed/backoff, held, start-permitted, physically running, terminal and delivery-owed states; these may have independent persisted dimensions.
- Acceptance → eligible start → authorized attempt → terminal result or eligible retry is controlled only by guarded authority transitions.
- Terminal result → model delivery permission → model consumption proof is a separate path; it never returns to physical execution.
- Pause changes eligibility, not accepted input, attempt history or completed physical effects.
- Every command has idempotent identity, expected revision where applicable, trusted actor and a durable, inspectable outcome.

## Four pause scopes and the start cutoff

- Initial scopes are queue, workflow instance, workflow definition and job type. All matching active holds must allow an attempt start.
- Queue/job-type holds gate attempt starts. Workflow instance/definition holds additionally gate result-driven parent continuation.
- A definition hold covers all associated definition versions; a job-type hold covers all registered handler versions unless a later explicit contract adds version-specific scope.
- Associations use stable definition identity and exact model execution identity; shared physical queues require no workflow-specific queue.
- Job-associated workflow holds have one authoritative gate set shared by attempt and model-delivery permission decisions; adapters must not rely on asynchronous shadow pause state.
- Independent hold IDs remain effective until individually released. Resume of one hold cannot clear another scope or actor's hold.
- Pause/resume commits are durable across nodes and restart, authorized, idempotent, revisioned and audited.
- **Selected cutoff:** a durable start permission is the linearization point, serialized with matching pause changes.
- After Pause commits, no new matching attempt permission can commit. A permission granted earlier counts as started/in flight.
- A previously permitted worker may enter handler code after Pause returns; the response is not a guarantee about physical instruction timing.
- Running attempts finish normally, subject to their existing timeout/cancellation rules. Pause neither suspends their process nor cancels them.
- Held work occupies no execution permit or local physical execution slot. Indexed discovery skips it so unrelated same-queue work progresses.
- Retry eligibility, future submissions and future schedule occurrences remain subject to every applicable hold.

## Atomic start and deployment-wide limits

- One authority transaction checks trusted scope, due/backoff time, handler support, cancellation, all pause scopes and all applicable concurrency limits.
- It atomically grants AttemptId/start permission, owner incarnation, fencing token and lease, and reserves deployment-wide permits; otherwise it grants nothing.
- Queue, job-type, workflow-instance and workflow-definition limits use authoritative keyed rows/permits with a consistent acquisition order.
- Concurrent pause/start and competing starts must serialize through shared guarded records or an equivalent demonstrated provider primitive; a callback read before an unrelated CAS is insufficient.
- Every retry needs a new start decision. A previous attempt's permission cannot authorize a successor after expiry, cancellation or pause.
- Local worker capacity protects that process's physical resources; it is separate from deployment-wide authorized-attempt limits.
- The worker reserves local capacity only for dispatchable work and releases a tentative reservation immediately if the durable start is refused.
- Deployment-wide limits bound **currently authorized leased attempts**, not every physical handler that may still exist after losing authority.
- On lease expiry a survivor may receive a successor permit while stale physical work continues. Report this possible overlap explicitly.
- Fail closed on unavailable gate/permit storage. Reconcile an unknown permission outcome before invoking handler code.

## Claims, completion and recovery

- Ownership transitions compare persisted owner incarnation, fence, revision and applicable lease deadline inside the authoritative transaction.
- Renew immediately before handler invocation and during execution; heartbeat I/O uses its own persistence context.
- A stale owner cannot renew, select a result, advance retry state, consume successor-owned work or release a successor's permits.
- Lost lease requests cooperative cancellation; it does not prove thread termination or undo external effects.
- A current owner's completion atomically records the immutable authoritative result, releases its durable permits and records owed result delivery, or schedules the next retry.
- Physical receipts are retained separately. An authenticated late receipt may be appended for its exact recorded AttemptId without reviving ownership or replacing the selected result.
- Completion, cancellation and timeout races choose an authoritative transition once. The losing physical outcome remains inspectable as a receipt.
- A committed successful result is not rerun because its delivery is paused, retried, unavailable or duplicated.
- A crash after external effects and before durable completion can cause repeated physical execution; downstream idempotency obligations remain with the handler/integrator.
- Do not promise exactly-once external effects. Qualify acceptance deduplication, occurrence identity, result selection and consumer acknowledgement separately.
- Takeover, restart and handover recover accepted state and owed delivery from durable records, not process-local task lists.

## Retry, timeout, cancellation and shutdown

- One runner retry authority applies the accepted attempt budget, failure classification and bounded backoff/jitter policy.
- Persist eligible-at and attempt history. A paused retry retains its original eligible-at time and waits for a fresh start permission.
- Infrastructure unavailability before a start grant does not spend a business attempt. A permitted attempt lost after effects may have begun remains recorded.
- Attempt timeout is an absolute deadline from the durable start grant; pausing never extends an already granted deadline.
- Cancellation before start commits a terminal decision, preventing later start grants. Running cancellation/timeout requests cooperative stopping.
- Requested cancellation, worker acknowledgement, authority loss and actual physical exit are distinct observable facts.
- A timed-out/cancelled local handler retains its **physical local slot until it exits**, even if its durable lease/permit has expired or another node can recover.
- Retries preserve the invocation idempotency identity so external systems can suppress duplicate effects where supported.
- Proposed manual retry requires permission, reason, idempotency key and expected terminal revision; it creates a linked new invocation preserving prior results.
- That proposed retry does not redirect an old model wait, rewrite a consumed result or resurrect a completed parent; model retry remains model-owned.
- Graceful shutdown stops new permissions, keeps renewing running work during the grace period, then requests cancellation and waits for physical exit while the process remains alive.
- Process death recovers through expired authority. Shutdown cannot claim physical completion, rollback or termination beyond the evidence available.

## Recurring schedule materialization

- A schedule owns a pinned handler/request template, revision, recurrence, next due cursor and explicit missed-occurrence policy.
- Job schedules have one authority; coordinating expression/identity mechanics with [#3582](https://github.com/elsa-workflows/elsa-foundation/issues/3582) does not create dual schedulers.
- Materialization checks all applicable queue/type/association holds and is ordered with pause commits in the same authoritative transaction or equivalent provider primitive.
- While held, it accepts **no new occurrence jobs** and retains the due cursor. This differs from a client explicitly submitting a job while paused, which is durably accepted.
- **Selected default:** after pause/outage, materialize one catch-up job for the oldest owed cursor, record the coalesced interval/count, then advance to the first recurrence strictly after the materialization cutoff.
- Occurrences between that cursor and cutoff are coalesced only while they are still unmaterialized schedule responsibility.
- **Explicit opt-in:** replay every occurrence. Materialize bounded pages in scheduled order and retain the cursor until each owed occurrence has an accepted JobId; never silently truncate backlog.
- Acceptance of an occurrence job and cursor advancement/coalescing receipt commit together; repeated materialization converges on the same OccurrenceId/JobId.
- If pause wins first, retain the cursor without accepting a job. If materialization wins first, the accepted job remains durable and its attempt still needs a start permission.
- **Already accepted jobs are never coalesced, dropped or replaced**, even if still held, overdue or derived from the same schedule.
- Schedule updates/deletion use expected revision, preserve accepted jobs and explicitly account for unmaterialized due responsibility; they cannot hide a catch-up backlog change.
- Recurrence materialization consumes no handler execution permit. Resume dispatch remains bounded by normal start/concurrency decisions.
- Recurring association is explicit; a single awaited parent bookmark must not be reused as the result address for multiple occurrences.

## Model-owned result continuation

- Awaited native/BPMN invocation retains an exact tenant-scoped model, definition/version, execution, invocation/wait and result association.
- Independent submission returns JobId without requiring a saved wait. Explicit detached association does not inherit parent cancellation; awaited association records model-owned cancellation responsibility.
- The Jobs-specific `SubmitJob` handoff returns a typed durable model-submission receipt with stable JobId and explicit `PendingAdmission` state. It is not Jobs-store `JobAcceptance`: native completion/outbox durability precedes the separately committed Jobs admission. Exact protected-origin reconciliation must prove canonical acceptance with that same JobId; an unexpected different winner remains an owed conflict. No atomic cross-store promise is made.
- Independent submission survives normal parent completion/fault but inherits explicit parent cancellation. A separate required owner-lifetime source retains the exact completed native invocation while its owning parent is active, including after admission is Delivered. Detached submission skips that inherited lifetime/cancellation responsibility while preserving its captured four-scope pause association. Neither mode creates a result wait or Jobs result-delivery acknowledgement.
- A model bridge consumes `SubmitJob` helper completion only after public native inspection proves actual canonical Jobs admission (or its existing terminal cleanup path positively proves cancellation before admission). Helper completion and a proposed identifier alone cannot advance the live model. The model's exact root lifetime governs independent cancellation, rather than the helper's lifetime.
- Native uses a real BookmarkCreated checkpoint plus outbox admission responsibility, idempotent job admission and typed result delivery to the exact saved wait.
- BPMN records its own invocation/result input identity and consumes the result through its normal fenced model input/checkpoint protocol.
- Native/BPMN adapters preserve completed ResultId/payload durably while workflow instance/definition holds block new continuation permission.
- **Reviewed engineering continuation cutoff:** model-delivery permission is serialized with those holds; a grant before Pause counts as an already started continuation. The owner selected the attempt cutoff and held result-driven continuation; this analogous model cutoff is an engineering design choice after independent review, not a separate owner selection. Actual model qualification remains required.
- A previously granted continuation can finish after Pause returns. This is not a physical-stop or atomic cross-store checkpoint guarantee.
- Continuation permission is bound to exact ContinuationId, ResultId, model pin/association and deterministic model input/resume identity.
- Repeating a granted model-delivery request resolves that **same permission, result and model input**; it never submits or executes a new physical job.
- After a grant, recovery may redeliver that exact started continuation while paused; it cannot mint another continuation identity to bypass a hold.
- The model's durable consumed-input/trigger receipt deduplicates replay. A new result-driven continuation identity requires a new permission decision.
- Queue/job-type holds do not stop an already completed result's continuation. Every matching workflow instance/definition hold must permit a new continuation grant.
- Native `ActivityResumeValidating` is a proposed adapter validation seam; a read there alone cannot establish the cutoff or replace durable permission proof.
- The BPMN adapter validates permission before consuming its job-result input; shared runtime code does not interpret BPMN tokens or add model-name switches.
- Parent cancellation/completion retires delivery only with exact durable proof. Missing state, an enqueue response, HTTP acceptance, `Duplicate` or `NotFound` is not acknowledgement.
- The runner owns job retries; the model must not independently retry the same physical work while its accepted runner invocation is still active.

## Cross-store responsibility and evidence limits

- Job result commit and parent checkpoint are separately owned. No atomic transaction across arbitrary stores is asserted.
- Durable parent outbox → origin-protected idempotent job acceptance, and durable result outbox → model permission → exact consumption proof, preserve responsibility across failure windows, including acknowledgement loss beyond the ordinary producer-key window.
- The gate authority commits model permission first; the model durably consumes the exact permitted input in its own store. Failure between them leaves recoverable owed delivery.
- Pause after that grant allows the already started continuation to finish; this proposed model cutoff avoids pretending that pause and a remote model checkpoint commit atomically.
- Same-database EF may enlist narrow operations only when existing shared-transaction target checks prove one physical database/provider; qualification must name the actual profile.
- Proposed initial qualification is SQLite for development/restart and PostgreSQL for independent-process contention/handover. Other engines and deployment shapes are not implied.
- Retention protects accepted inputs, authoritative results, owed delivery and consumption proof until obligations are settled; tenant-authorized purge follows explicit policy afterward.
- Inspection and recovery commands use trusted tenant/permission boundaries, bounded redacted results and audit records; secrets are resolved only through authorized host bindings.

## Concrete reuse and review boundaries

- [Scheduler queue](../../../Essentials/src/Workflows/Runtime/Core/Contracts/IWorkflowSchedulerWorkQueue.cs) supplies existing durable identity/fence mechanics; its per-workflow FIFO and dependencies are not the proposed standalone job queue.
- [Fenced renewal](../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Claims/FencedClaimLease.cs), [EF write retry](../../../Essentials/src/Persistence/EntityFramework/EfWriteRetry.cs) and [shared transactions](../../../Essentials/src/Persistence/EntityFramework/EfSharedTransaction.cs) are concrete reuse candidates; use `TimeProvider`, not another clock authority.
- [ETL checkpoint admission](../../../Extensions/Etl/src/Workflows/Runtime/Services/EtlCheckpointEnricher.cs) and [result proof](../../../Extensions/Etl/src/Workflows/Runtime/Services/EtlNativeResultDelivery.cs) demonstrate model adapter responsibility transfer.
- [Native resume validation](../../../Essentials/src/Workflows/Runtime/Core/Events/ActivityResumeValidating.cs) and [BPMN driver](../../../Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnDirectExecutionDriver.cs) identify proposed model-owned continuation integration points.
- [Current pause mapping](../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Scheduler/WorkflowSchedulerPauseGate.cs) does not implement this four-scope job contract; [outbox/coalescing settlement](../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Scheduler/WorkflowDrainOrchestrator.cs) must retain its delivered-elsewhere refresh behavior.
- [ADR 0087](../../../docs/adr/0087-etl-owns-its-execution-domain.md) and [ADR 0088](../../../docs/adr/0088-rules-owns-its-execution-domain.md) preserve domain-owned semantics; this proposal does not claim job-domain admission from those decisions.
- Implementation selects the Elsa-owned default, scope-gate revision arbitration and linked-invocation Retry as new run described here; domain admission is accepted. Concrete public ABI, recovery proofs and UX still require their consumer/provider/composed acceptance before stabilization.
- Qualify all pause overlaps/races, recurrence policies, accepted backlog preservation, lease/physical overlap, duplicate delivery, timeout/cancellation races, exact model consumption and tenant denial against real packaged and composed hosts.
