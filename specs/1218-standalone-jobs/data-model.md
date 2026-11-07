# Proposed job data model

Status: proposed; no schema or public ABI is implemented or agreed. All keys include trusted
partition/tenant scope. Names below describe responsibility, not a mandate for one table per row.

| Record | Identity and retained fields | Authority/invariants |
|---|---|---|
| Handler registration | Type/version, schema aliases/versions, trusted deployment implementation identity | One supported registration per identity; no assembly-name durable key or silent semantic repin |
| Acceptance | Client key, canonical request digest, JobId, accepted-at, advertised minimum recognition-until | Unique scoped key while protected; equal replay recovers one JobId, unequal replay conflicts |
| Durable origin receipt | Trusted model/schedule/outbox origin, immutable digest, original JobId or retired-origin refusal tombstone | Separate from finite ordinary producer-key retention; exact late origin replay never becomes fresh work |
| Job | JobId, handler/input/result pins, bounded input, queue, immutable policy and association | Immutable accepted request; mutable lifecycle projections never replace retained attempts/results |
| Attempt | AttemptId/number, JobId, owner incarnation, fence/revision, granted-at, lease and absolute timeout | Start transaction checks every gate and permit; no stale-owner mutation or successor permit release |
| Physical receipt | AttemptId plus receipt identity, authenticated origin, outcome and bounded payload/digest | Retain late completion as evidence; receipt alone cannot select authoritative result |
| Result | ResultId, JobId, selected terminal revision, outcome, schema/payload/digest | Immutable result; redelivery never returns to physical execution |
| Delivery | ContinuationId, ResultId, exact model input identity, owed/settled proof | Retire only on durable consumption/equivalence/terminal-parent proof |
| Pause hold | Logical scope key, HoldId, revision, actor/reason/timestamps | All active holds compose; a resume releases only explicitly addressed hold identities |
| Capacity policy/permit | Scope key, policy revision, AttemptId/fence/lease ownership | Transactional authorized-attempt ceiling; stale physical overlap is a separate observed fact |
| Schedule | ScheduleId/revision, pinned request template, UTC recurrence, next cursor, missed policy | Owns unmaterialized responsibility; acceptance and cursor/coalescing receipt commit together |
| Occurrence receipt | Schedule identity/revision plus scheduled UTC instant, JobId, coalescing range/count | Unique deterministic occurrence independent of fire/retry identity; accepted JobIds never coalesce |
| Model association | Model kind/pin, logical definition/version, exact execution/invocation/wait, cancellation mode | Validated by owning adapter; API callers cannot forge trusted workflow association |
| Continuation permission | Exact ContinuationId, ResultId/model input, grant receipt and scope/gate versions | Proposed cutoff serializes grant with instance/definition holds; replay resolves the same grant |
| Operation receipt | Operation key/digest, actor, expected revision, outcome and audit reason | Mutations reconcile lost acknowledgements without cross-tenant leakage |

## State boundaries

Eligibility is a derived, explainable view over due time, compatible deployed handler, cancellation,
pause holds and capacity. `Held` is not a running attempt and reserves no execution slot. Scheduling
and accepted work remain durable independently of this view.

Job progress can be `Accepted`, `Waiting`, `AttemptActive` or `Terminal`; inspection separately shows
delay/backoff, hold reasons, start permission versus physical running, retained results and owed delivery.
Cancellation requested, timeout reached, lost ownership and physical exit are separate facts, rather
than one misleading completed status. Final enum/API vocabulary is reviewed before implementation.

Attempt numbers change for new authorized physical attempts. Lease renewal changes ownership revision
without creating another physical attempt. Takeover records a new authorization/fence and preserves
the old attempt/receipts. A successor never inherits an old start permission through a pause.

Automatic retries belong to one immutable job policy. Proposed operator **Retry as new run** creates
a linked JobId and preserves the original terminal result; it cannot silently reattach an old model wait.
Model-owned incident/retry policy is distinct and must not duplicate the runner's active retry budget.

## Version and retention validation

Canonical fingerprints include handler/schema/input, queue, due/schedule options, retry/timeout policy
and validated association. Define exact canonical serialization and bounded lengths in the public
contract before storing production rows; do not rely on unspecified JSON property order.

Provider time orders lease/due/timeout transitions; host time schedules polling and display. Scope keys
are ordinal and collision-safe; cursor tokens bind tenant, filter/order and retention revision. Every
read and mutation independently checks trusted scope even when the submitted ID exists.

Retention protects accepted inputs, authoritative results and all still-owed delivery/consumer proof.
Terminal input/result payload durations are explicit host configuration captured at acceptance; the
default and legacy policy retains payloads indefinitely. Cleanup records positive expired states only
after terminal settlement and all required delivery proof. Compact request/acceptance/result/association,
attempt, grant/ACK and origin metadata is retained indefinitely; there is no metadata TTL or proof purge.
Payload cleanup does not rewrite request-v1 digests, selected results or terminal revisions, and late
physical receipts cannot restore expired payload bytes. Acceptance advertises a minimum key
recognition window (proposed default 30 days); pending job/delivery obligations extend protection.
Purge cannot turn a pending obligation into NotFound or allow duplicate acceptance while protected.
After the window and settled obligations, recognition cleanup may retire a key, and later reuse can
create a new JobId. This expiry behavior is documented, observable and tested separately from payload
expiry; no indefinite key-recognition promise is made. Retained JobId lookup remains independent.
This finite policy never retires trusted durable-origin recognition. Such origins include independent
model submissions without a result consumer. Retain their receipt until exact owner proof covers all
retained retries and in-flight duplicates, or keep a compact tombstone rejecting retired-origin replay.
Sender Delivered, parent terminality or receiver result settlement alone is not sufficient proof.
Missing old schema/handler support is unavailable, not corrupt user input or successful completion.
