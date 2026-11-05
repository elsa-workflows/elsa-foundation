# Jobs contracts

`Elsa.Jobs.Core` carries the contracts used by an ordinary .NET producer, registered handler,
independent worker, persistence provider and optional model/operations adapters. It has no workflow,
authoring, EF, Studio, scheduler-engine or external runtime package dependency. `System.Text.Json`
is supplied by the .NET runtime. CLR registration types remain in the Jobs implementation.

The domain is admitted by [ADR 0096](../../../../docs/adr/0096-jobs-owns-durable-handler-invocations.md).
The [specification](../../../../specs/1218-standalone-jobs/spec.md) and
[lifecycle contract](../../../../specs/1218-standalone-jobs/contracts/job-lifecycle.md) define the
delivery scope. These declarations are not evidence that the product or a provider is qualified.

## Producer and handler

`IJobClient.SubmitAsync` accepts an explicit `JobScope` and a producer `JobSubmission`. The request
pins `JobHandlerIdentity`, input and result schemas, queue, retry/timeout policy, due time and optional
validated association. `JobPayload` clones the JSON value so disposing the producer's `JsonDocument`
does not invalidate accepted input. The implementation serializes, validates bounds and snapshots
the whole request before invoking the store; public record construction does not establish authorization.

A registered `IJobHandler<TInput,TResult>` receives ordinary typed input, a `JobExecutionContext` and
cooperative cancellation. Its stable invocation idempotency key survives physical retries. Progress
is informational, bounded and redacted; it is not durable completion. A handler must use downstream
idempotency controls where repeated external effects would be harmful. Jobs does not promise exactly-once effects.

Workers advertise `JobHandlerSupport` with exact semantic identity, schemas and deployed implementation
identity. The registry must reject duplicate identity registrations and refuse incompatible schemas;
an absent old handler leaves accepted work unavailable rather than consuming an attempt budget.
Neither a CLR type name nor an arbitrary caller's declaration selects trusted deployed code.

## Persistence commands and queries

`IJobCommands` is the one mutation authority; its results are confirmation identities, revisions,
ownership tokens and explicit statuses. `IJobQueries` returns bounded read models and never changes
ownership, eligibility, recovery or retention. Providers must distinguish unavailable storage from
missing data and must validate trusted scope on every operation. An unknown command acknowledgement
must be reconciled from the durable command identity before repeating effects.

All strings used as identities compare ordinally, including tenant and partition. Provider hashes
are indexed projections, never sufficient identity proof: validate full residual keys and immutable
digests before returning or changing a row. Validate positive versions, limits and durations; bounded
identity lengths, JSON sizes, pages and diagnostics; and finite retry/progress numeric values at the
boundary. Snapshot caller-owned collections before asynchronous provider work.

Admission fingerprints cover the immutable handler/schema/input, queue, due time, policy and validated
association. They exclude the proposed server-generated JobId, so two matching lost-ack submissions
return the winning original JobId. Different immutable content under a protected producer key or
durable origin is a conflict, never an equivalent replay. The provider recomputes `JobRequestDigest`
and rejects a supplied hash that differs; a caller's hash alone is never evidence of content equality.

The version-one digest protocol uses BCL `System.Text.Json` serialization of the declared Jobs model
with its fixed property names and no host-provided options/converters or CLR polymorphism. It
normalizes request `NotBefore` to UTC, recursively orders object properties by ordinal name, retains
array order and writes compact UTF-8 JSON. The SHA-256 preimage is the UTF-8 prefix
`elsa.jobs.request.v1` followed by one LF byte and that JSON. The result is lowercase hexadecimal.
Every immutable request field, including retry/timeout/recognition policy and association, participates.
If the wire model or framing changes, preserve the old reader/digest version for accepted records.

`JobPayloadDigest` uses the same framing with prefix `elsa.jobs.payload.v1` and one LF byte, over
the declared schema/value payload object. An absent payload hashes that prefix followed by JSON
`null`; a present payload whose Value is JSON null includes its schema and differs. Numeric values
inside JSON payloads retain their parsed raw numeric lexeme: `1`, `1.0` and `1e0` have different
digests. Equal-name duplicate object properties retain their relative source order; consumers must
not assume JSON semantic/numeric equivalence implies digest equality. Strings/property escaping
is written by the fixed BCL writer. Producers and providers share these exact helpers; providers
also recompute receipt/result payload digests before recognizing equivalent completion.

Ordinary producer recognition lasts at least `MinimumKeyRecognition` and extends while the job or its
consumer obligation is pending. `RecognizeUntil` advertises that minimum. Expiry/cleanup can permit a
new ordinary-key acceptance only after protection ends. Trusted model/schedule/outbox `JobOrigin`
recognition is separate: keep the exact origin receipt or a refusing retirement tombstone beyond
ordinary key expiry. Independent model submissions need this protection even without an awaited result.
Sender delivery, parent termination and receiver completion alone do not prove all late duplicates are impossible.

`CancelOriginAsync` atomically records an exact origin/request-digest cancellation tombstone before
admission, or cancels the already mapped original invocation. A subsequent matching origin cannot
become fresh work. Only a trusted adapter may supply origins and associations; exposing store command
records in a tenant API does not make their contents trusted.

`RetryAsNewRunAsync` atomically checks the expected terminal revision, writes the operation receipt
and accepts one linked new invocation. Matching operation replay returns that same new JobId even if
the caller proposes a different generated ID. Conflicting immutable operation content is refused.
Copy the original handler/input/policy and retain its validated model/definition/instance pause association,
but change it to independent mode and clear its continuation/result address. Retain all original results/history. The new run never silently reattaches an old wait or resurrects a completed parent.

## Attempts and capacity

`ClaimAsync` uses authoritative store time to check due/backoff, compatible handler support,
cancellation, every applicable hold and every deployment-wide capacity policy. One transaction
grants the attempt permission/lease and reserves all relevant queue/type/definition/instance permits,
or grants nothing. Allowed and initially absent gate rows participate in the same arbitration as held
rows so a newly created hold cannot race an unguarded read. A held same-queue job cannot hide unrelated eligible work.

The committed permission is the start cutoff. A pause committed afterward cannot revoke that grant;
the handler may physically enter later. Renewal compares exact owner incarnation, fence, revision and
unexpired authority. A new attempt after lease loss needs a fresh permission and cannot bypass a pause.
`RenewAsync` also reports cancellation and absolute deadline facts. `RecordPhysicalStartAsync` is
observational and cannot create a missing permission. Provider time governs deadlines; host clocks schedule polling.
`JobAttemptLease.Revision` is specifically the ownership revision changed by claim/renewal. Physical
start, progress and appended receipts use separate observational revisions and do not invalidate the
worker's current lease. `JobSnapshot.Revision` is the aggregate lifecycle revision, including terminal
selection and administrative mutations; it is not interchangeable with the lease revision.

`CompleteAsync` compares the current lease and receipt identity/digest, then atomically selects a
terminal result or persists retry eligibility and releases that attempt's permits. Matching completion
replay must not spend another retry or release a successor's capacity. `RecordPhysicalReceiptAsync`
may retain an authenticated stale attempt's receipt without changing result selection, retry, ownership
or capacity. A conflicting receipt ID/digest is refused. Retain late success evidence even when it cannot be authoritative.

Deployment limits bound live authorized leases. A stale process can continue physical effects after
losing a lease and overlap its successor. In a live worker, cooperative timeout/cancellation retains
the local physical slot until the handler actually exits. Authority expiry and physical exit are
different facts; no provider may claim that its persisted counter proves a strict physical cluster bound.

## Pause, schedules and continuation

Every active hold composes. A release changes only the named hold at its expected revision.
Queue/job-type holds cover all versions and gate attempts. Workflow instance/definition holds also
gate a new result-driven continuation permission, using the same authoritative hold set.

`MaterializeDueAsync` is bounded and orders schedule cursor changes and matching pause gates in one
transaction. A held schedule retains its due cursor and accepts no occurrence job. The default
`CoalesceOne` accepts one oldest owed occurrence, accounts for the coalesced unmaterialized interval
and advances after the materialization cutoff. Opt-in `ReplayEveryOccurrence` accepts every due
occurrence in bounded pages. Acceptance and cursor/receipt changes commit together. Accepted JobIds
are never coalesced or removed. Schedule disabling retains the cursor; a revision update cannot
silently discard due responsibility. `IJobRecurrenceCalculator` only supplies UTC arithmetic; it does
not own jobs, cursor advancement, pause policy or a second scheduler.

`GrantContinuationAsync` binds the exact retained result, association and deterministic model input.
Workflow holds block a new grant; repeating a prior grant resolves the same already-started permission
even while paused. No new job or physical execution is created. The owning native/BPMN adapter then
consumes that input in its own fenced model checkpoint. This is a proposed reviewed integration seam,
not a cross-store atomic transaction. Delivery replay must use the same ResultId/input identity.

`AcknowledgeDeliveryAsync` accepts only trusted exact consumed/equivalent-input or terminal-parent
proof. Transport acceptance, queue enqueue, `Duplicate`, `NotFound` and absent cache entries are not
consumer proof. Pending results and origin receipts survive worker restart and ordinary retention cleanup.

## Cross-domain contributions

This contracts package contributes no workflow activity, model interpreter, EF mapping, Studio module
or host feature. The optional Jobs native/BPMN adapters will implement their owning models' existing
checkpoint/outbox/consumption contracts. They must preserve model semantics without kernel model-name switches.

`JobException` supplies safe domain codes, `JobPersistenceException` preserves a provider cause behind
the dependency boundary, and `JobSubmissionConflictException` distinguishes protected-key content
conflicts. Normal caller-requested cancellation remains cancellation rather than a persistence failure.

