# Job workers outside the process

> **Proposed.** The kernel design awaits owner approval; this page answers design-now question 5 and does not
> authorize product implementation. The worker protocol ships in 1.x. Its contract is designed now so that release 1's
> jobs are built to it.

[Design-now question 5](../reports/clean-slate-kernel/design-sketch-draft-5.md#to-answer-in-the-design-proposal-before-the-fable-review-owner-decision-12-november)
asks for job workers that run outside the kernel's process, written in any language, that pull jobs over gRPC or HTTP
with a lease, a heartbeat and a timeout, as Zeebe's job workers do. This page proposes one job contract for the
in-process jobs of release 1 and the remote workers of 1.x: who owns an attempt, how it times out and retries, the key
the worker receives, and how a completion re-enters the fenced commit. It ends with the owner's decisions, among them
the release placement of standalone Jobs.

## Terms

| Term | Meaning on this page |
|---|---|
| Job | Work that runs outside an actor's turn and whose result comes back as a message. It may execute more than once; its result changes state once. |
| Ticket | The kernel's record of a job in flight: a message of kind `kernel.job` to the instance, written in the commit of the turn that started it. Built. |
| Invocation | One accepted job request: its immutable input, policy and result route. |
| Attempt | One grant of an invocation to one worker. A retry or an expired lease starts a new attempt of the same invocation. |
| Job actor | Proposed layer 3 actor type, one per invocation that remote workers may run. It holds the invocation, its attempts and its result. A standalone job (Elsa.Jobs rebuilt on the kernel) is one. |
| Queue actor | Proposed layer 3 actor type that matches waiting workers with ready jobs and counts capacity permits. |
| Worker gateway | Proposed edge adapter that serves the HTTP and gRPC protocol and turns each request into a message. |
| Attempt lease | How long an attempt stays its worker's without a heartbeat. The partition lease of [ownership and fencing](ownership-and-fencing.md) is a different lease. |

## What is built

- **Background jobs inside an instance.** `StartJob` writes a ticket (`Kinds.Job` in
  [Messages.cs](../../prototypes/kernel-model/src/Kernel.Durable/Messages.cs); `JobTicket(Model, Key, Arguments)` in
  [Worker.cs](../../prototypes/kernel-model/src/Kernel.Durable/Worker.cs)). The owner calls the actor type's
  `ExecuteJobAsync` outside the turn (`Worker.DriveJob`) with a `JobContext(Instance, Key, Arguments, Creation)`, on
  `WorkerOptions.JobScheduler` when the host sets one ([FINDINGS](../../prototypes/kernel-model/FINDINGS.md) section 27,
  R14). The outcome comes back as `job-finished` with a result or an error. A result is handled ahead of other messages,
  a cancelled job's result is never applied (invariant 12), and a new owner finds the ticket by polling and starts the
  work again.
- **Job poison (decision 9).** Tickets are tried like messages. A ticket past the attempt threshold parks its instance
  (`Worker.cs`, the `MaxAttempts` check before `_type.Park`). The older rule, an error result with the instance going on
  (FINDINGS section 7), survives only as the mutation `Flaw.JobPoisonAsError`
  ([Addresses.cs](../../prototypes/kernel-model/src/Kernel.Runtime/Addresses.cs)), which the simulation must catch.
- **The effect key.** `EffectKey.For` in [EffectKey.cs](../../prototypes/kernel-model/src/Kernel.Workflows/EffectKey.cs)
  (layer 3), checked by the simulation's N7 ([NATIVE.md](../../prototypes/kernel-model/NATIVE.md), section 6) and
  described in [ownership and fencing](ownership-and-fencing.md#side-effects).
- **Reply addresses.** `Requests` and `IReplyTransport` in
  [Replies.cs](../../prototypes/kernel-model/src/Kernel.Durable/Replies.cs): a request waits for a reply that an
  instance emits, released by default after the commit of the turn that wrote it, up to its deadline.
- **The local lease guard** ([LEASE.md](../../prototypes/kernel-model/LEASE.md)): an owner starts no new turn or job
  body after its conservative local lease deadline.
- **Not built:** the job actor, the queue actor, attempt leases, the worker gateway, any remote protocol, and the
  Elsa.Jobs API on the kernel. The standalone Job/Queue/Reply spike is not on main.

The semantic reference for the protocol is Elsa.Jobs at `a786c52f`
(`Extensions/Jobs/src/Core/JobAttempts.cs`: claim request, attempt lease, renewal, physical receipt, selected result,
attempt states). Its own store measured, on PostgreSQL 17, separate operations of 11 (admission), 14 (claim), 13
(renewal), 8 (physical start) and 14 (completion) wire round trips
([#4229 comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6867483419)). They count
that store's own operations and serve as a comparison point only; they set no kernel budget.

## Where it lives

The contract below (attempt lease, heartbeat, timeouts, worker-classified retries, the effect key, the wire records and
the way a completion re-enters the commit) is the design that this page proposes for adoption now. The actors that hold
it inside the kernel are a **candidate topology** for the 1.x spike, with three limits still open (listed below).

```
worker --HTTP/gRPC--> gateway --activate--> queue actor --grant--> job actor --commit--> reply --> gateway --> worker
worker --HTTP/gRPC--> gateway --renew, complete, fail--> job actor --commit--> reply --> gateway --> worker
```

| Part | Layer | Depends on |
|---|---|---|
| Worker gateway | Edge adapter: ingress plus reply | Adding a message by id (the delivery seam), reply addresses, authentication. It never sees partitions, positions or SQL. |
| Job actor and queue actor | Layer 3 actor types | The turn contract only: state, sends, timers as messages, replies, background jobs. |
| Tickets, the fenced commit, timers, polling, poison | Layer 2 | Unchanged. |

The durable path needs no new layer 2 concept. In the candidate, a job actor is placed in its queue shard's partition,
so the queue's grant reaches it in memory after the commit and both commit in the same partition's batches. The gateway
addresses a queue by its actor id (`queue/{tenant}/{name}/{shard}`) and a job by the
job id in its grant; layer 2 maps both to a partition, and the gateway treats both as opaque strings. A busy queue is
split into a fixed number of shards; the gateway spreads a worker's activates over them.

### What each actor keeps

| Actor | State |
|---|---|
| Queue shard | Capacity permits in use per job type; holds; the shard's settings. Waiting activates live in memory only. |
| Job actor | The invocation (input, or a reference to it under decision 10), policy, implementation pin, result route; its attempts (id, worker, grant token, request id, lease, deadline, outcome); the selected result. At most the policy's attempt limit of attempts. |
| Gateway | Nothing durable; open requests in memory until their reply deadline. |

The record that answers a resent request is the job actor's attempt list, so it lives as long as the invocation.

### Open limits of the candidate, to settle in the 1.x spike

Every commit rewrites an actor's whole row, so the queue shard is only cheap if its row stays small. In the candidate as
written it does not, and this page claims no bound for it:

1. **Children on the queue row.** A job actor started with `Start` is a kernel child: its id lands in the queue's
   `InstanceRow.Children` ([Store.cs](../../prototypes/kernel-model/src/Kernel.Durable/Store.cs),
   `ChildRegistry` in [WorkflowInstance.cs](../../prototypes/kernel-model/src/Kernel.Workflows/WorkflowInstance.cs)),
   which the queue rewrites at every commit. Job actors need a start that places them in the queue's partition
   without registering them as children; the turn contract has none today.
2. **Ready jobs as held inbox rows.** Selecting matching `job.ready` messages and leaving the rest means holding them in
   memory (`Hold`, FINDINGS section 14): the owner walks the whole backlog at every turn, and one type's backlog delays
   another's. Candidates: ready jobs kept per type in separate holders, or as due timers in their job actors that offer
   themselves when a worker waits.
3. **The backlog counts against question 3's threshold.** Ready rows are due rows of the queue's virtual partition, so a
   deep queue pushes that partition over the
   [fairness page](fairness-and-backpressure.md#a-bound-on-inbox-growth)'s ingress threshold and refuses unrelated starts placed there. Candidates: give queues their own virtual partitions,
   or count job readiness apart from ordinary due depth.

## The job contract after this change

| Aspect | Today (in process, built) | Proposed (in process and remote) |
|---|---|---|
| Who owns an attempt | The partition owner, under its partition lease | In process: unchanged. Remote: the job actor grants an attempt with its own lease and a grant token. |
| Timeouts | None in the kernel; the model decides | An attempt deadline (hard), an attempt lease (renewed by heartbeat) and an optional invocation deadline. Each is a timer message in the job actor's commit, recalled when the attempt ends. In process, the deadline cancels the job's token. |
| Retries | The model receives `job-finished` with an error and decides | The worker classifies a failure: retryable with an optional backoff, or terminal (Zeebe's fail-job command). The job actor keeps the count, enforces the attempt limit and applies the backoff as a due time. An expired lease or a passed deadline is a failed attempt that the job actor records itself. |
| What the code receives | Key, arguments, creation (`JobContext`) | Also the effect key, the attempt number, the attempt id, the deadline and a cancellation signal. |
| Completion | Published in memory, applied in the owner's next commit | In process: unchanged. Remote: a message from the gateway; the job actor's turn validates it and sends the result in its commit. |
| What a worker reports | Not applicable | Evidence. A stale worker's report is recorded and never replaces the selected result (an Elsa.Jobs idea the sketch keeps). |

The first grant pins the implementation (handler type, version and the worker's declared implementation identity) on
the invocation. A later attempt goes only to a worker that offers that pin, so a retry never runs on a different build.

### When a completion races its own timer

A lease or deadline timer of the job actor is its own message and fires from the owner's memory. A `Complete` arrives
from outside, by poll, behind a seen-mark commit. Left alone, a completion sent just before `LeaseUntil` would lose to
the timer and its work would run again. The rule:

- **Turn order decides.** Whichever of the completion's turn and the expiry timer's turn runs first wins. No timestamp
  from the worker or the gateway is trusted. When both are in the same turn's inbox (the same poll, or a poll and a
  timer handed over together), the job actor handles the completion first, as the kernel already handles a result
  ahead of other messages.
- **The expiry timer is due a grace after `LeaseUntil`, and the deadline timer a grace after `Deadline`.** The grace
  covers the gateway's insert-to-turn delay: the poll interval plus one mark-before-run commit (proposed default: twice
  the poll interval, at most a few seconds).
- **A takeover adds the grace again.** A new owner has not yet polled the completions that arrived while the partition
  had no owner, so it defers every lease and deadline expiry to at least one grace past its take. The grant tells the worker `LeaseUntil`; the worker plans to finish or renew before it, so a
  completion sent in time normally reaches its turn before the timer.
- **A completion after the expiry turn is `Stale`.** It is recorded as evidence and changes nothing. The effect key
  lets the receiver drop the next attempt's repeat.

### Wire contract, version 1

The same fields travel as JSON over HTTP and as protobuf over gRPC. Every mutating request carries an id chosen by the
worker; a resend uses the same id.

```csharp
record Activate(string RequestId, string Worker, string Queue, IReadOnlyList<HandlerSupport> Handlers,
                int MaxJobs, TimeSpan Lease, TimeSpan LongPoll);
record HandlerSupport(string Type, string Version, string Implementation);
record Grant(string JobId, string AttemptId, int Attempt, long GrantToken, string EffectKey, string Type,
             string Implementation, JsonElement Input, DateTimeOffset LeaseUntil, DateTimeOffset Deadline,
             string? TraceParent);
record Renew(string RequestId, string JobId, string AttemptId, long GrantToken);
record Complete(string RequestId, string JobId, string AttemptId, long GrantToken, JsonElement Result);
record Fail(string RequestId, string JobId, string AttemptId, long GrantToken, bool Retryable,
            TimeSpan? Backoff, string ErrorCode, string Message);
enum Disposition { Granted, Empty, Accepted, Duplicate, Stale, Refused, Unknown }
```

| Operation | What happens |
|---|---|
| Activate | The queue shard takes up to `MaxJobs` `job.ready` messages whose type, version and pin the worker supports and whose holds and capacity allow it, and sends each job actor a grant. Each job actor records its attempt, arms its lease and deadline timers and, after that commit, sends the queue a `granted` message with the grant. The queue collects them and answers the Activate once with the list, after the commit that consumed them; a job actor that refused (a cancel came first) sends `declined` instead. A built `Request` ends with its first response, so the gathering is the queue's, never the gateway's. With nothing ready, the queue keeps the request in memory until a job arrives or `LongPoll` passes (the reply deadline, decision 6). Either way the activate message itself was a row: an insert, a seen mark and the consuming commit. |
| Renew | The job actor checks attempt id, grant token and lease, extends `LeaseUntil` and returns a cancellation flag when the invocation was cancelled. An expired attempt cannot be revived. |
| Complete | The job actor checks the attempt under the rule above, selects the result, recalls its timers, sends the result along the result route and tells the queue to release the permit. |
| Fail | As Complete, then either schedules the next attempt (a `job.ready` to the queue, due after the backoff, within the attempt limit) or selects a failed result. A business error code is kept on the result, so a BPMN error boundary can match it later. |
| Late report | A Complete or Fail for an attempt that expired or was replaced is recorded as stale evidence and answered `Stale`. It changes no result and no permit. |

Every answer distinguishes granted, empty, accepted, duplicate, stale, refused and unknown. A worker whose request ends
in a broken connection resends the same request id; it never invents a new attempt id. The gateway turns a request id
into the message id `{worker}/{request id}` (decision 3). `{worker}` is the worker's authenticated principal, which
stays the same across reconnects; the gateway takes it from authentication for every request (Activate, Renew,
Complete, Fail), never from the connection or the request body, so the request records need no `Worker` field. A
resent Activate within its `keep_until` meets the duplicate id and is answered `Unknown` with no grants: the grants
of the lost answer are not replayed. They expire by their attempt lease, and the jobs are granted again, as in
Zeebe, so such jobs wait at most one attempt lease. A resent Renew, Complete or Fail gets its recorded disposition
through a read-only `job.status` message.

## The effect key the worker receives

`Grant.EffectKey` names the logical effect of the invocation. It is the same on every attempt of that invocation, so a
receiver that keeps the keys it has seen drops the repeat of a call whose first attempt succeeded before its worker was
lost.

- **A job submitted by a workflow activity** carries that activity's effect key,
  `{instance origin}/{node}/{iteration path}/{attempt}` from `EffectKey.For`. The final segment is the Durable-mode
  attempt (0 or 1) of the activity. A model-level retry of the activity is a new generation in the iteration path, so it
  gets a new key; a retry of the same invocation by its job actor keeps the key.
- **A standalone job** has no node or iteration path in the owner's sense. Its key is `{job origin}/job/-/0`: the
  owner's shape with fixed parts (decision 4).
- **The attempt number and attempt id** travel separately, for logs and for receivers that want them. A worker sends
  the effect key where its receiver looks for a repeat: the `Idempotency-Key` header for HTTP, the local part of a
  `Message-ID` for email. Deduplication remains the receiver's job; no mode makes a side effect exactly once.

## How a completion re-enters the fenced commit

1. The worker sends `Complete`. The gateway adds a message of kind `job.complete` to the job actor (keyed as decision 3
   says).
2. The job actor's owner reads it by poll (a wake hint makes that prompt) and marks it seen in a fenced commit, as for
   every message that arrives by poll.
3. The job actor's turn checks the attempt, selects the result, recalls the lease and deadline timers, and sends: the
   result along the result route (a `job-finished` to the waiting workflow instance, or nothing for a standalone job
   without a continuation), a permit release to its queue shard, and the reply. These writes are one commit under the
   partition fence.
4. After that commit the reply reaches the gateway, which answers `Accepted`. A stale owner's commit is refused by the
   fence and its reply is never released; the worker's resend reaches the new owner.
5. The waiting instance consumes the result in its own turn: delete and state change in one transaction, so the result
   is applied once. A cancel that the instance committed first makes the result a message to a finished wait, which is
   consumed and dropped.

No bridge, consumption proof or second store is involved: the outbox is the recipient's inbox, as for every send.

## Heartbeats

A renewal must reach the job actor's owner. If every heartbeat is a durable message, a worker pays an inbox row per
heartbeat: on PostgreSQL an outside insert alone writes about 0.5 kB of WAL, before its seen mark and consumption
([WRITES.md](../../prototypes/kernel-model/WRITES.md#the-cost-run-bytes-per-commit-without-chaos), the outside-start
setup row). A thousand workers renewing every three seconds would write that 333 times a second.

The alternative delivers a renewal to the owner over the runtime's transport, as the wake signal already travels, and
keeps it in memory. The rules that keep it inside the commit contract:

- **The expiry decision stays durable.** The job actor's lease timer is a message in its commit. When it fires, the turn
  compares it with the renewed `LeaseUntil` in memory: if the lease was extended, the turn arms the next timer (one
  commit per lease period per attempt) and expires nothing.
- **A renewal is answered only by an owner within its local lease guard.** An owner past its guard starts no new work
  and answers no renewal ([LEASE.md](../../prototypes/kernel-model/LEASE.md)). An extension it gave therefore ends no
  later than its partition lease plus one attempt lease.
- **A takeover grants a grace.** A new owner cannot know the renewals its predecessor received, so it extends every
  attempt in flight to one attempt lease past its take. That covers every extension the predecessor could have given.
- **A lost renewal shortens a lease.** The worker learns it from its next renewal (`Stale`) and stops; another attempt
  may already run, which at-least-once allows.

A renewal travels in memory over the runtime's transport under these rules, with a durable message as the fallback when
the gateway cannot reach the owner (decision 2).

## Standalone Jobs

The sketch's [mapping](../reports/clean-slate-kernel/design-sketch-draft-5.md#standalone-jobs-elsajobs-on-the-kernel-owner-decision-12-november)
stands: one job concept, Elsa.Jobs's public API kept (`IJobClient`, `IJobHandler<TInput, TResult>`,
`JobExecutionContext`, idempotent submission, policies, schedules, holds), its own store and workflow bridge dropped. On
this contract:

- A submission is a message to the queue shard. The queue starts the job actor in its own partition (open limit 1),
  with an id derived from the tenant, the queue and the idempotency key, so a repeated submission meets the existing actor. The job actor compares
  the request digest and answers duplicate or conflict.
- A local `IJobHandler` takes grants in the job actor's own turn as a background job, with no gateway and no row per
  request. A remote worker takes them through the gateway. Both see the same attempt, effect key and result rules.
- A workflow's awaited job stays the kernel's background job in the instance's partition. A workflow job meant for
  remote workers is a submission to a queue, sent in the instance's commit, with the instance as its continuation.

### A job actor's lifecycle

- **Cancel.** A workflow instance that submitted a remote job records its job id (derived from the submission) in its
  state. `CancelJob` and a terminate of the instance's tree send `job.cancel` to that job actor in the instance's
  commit. The job actor selects a cancelled result, recalls its timers, releases the permit and answers the worker's
  next renewal with the cancellation flag; a completion after that is `Stale`.
- **End.** Once its result is selected and sent, the job actor is ended. Its row stays under the retention policy of
  question 2 (the `Finished`, `Cancelled` or `Faulted` window) and is then deleted by the built delete.
- **Idempotency window.** A submission's id derives from tenant, queue and idempotency key, which makes it a caller key
  under walkthrough decision 7: a repeat is refused while the key is within its `keep_until`, and may start a fresh run
  after it. A resent worker request is recognised while its message row is kept (decision 3).

## Release 1

- Background jobs inside workflows, in process, as built: the ticket, `ExecuteJobAsync`, the result applied once, job
  poison per decision 9.
- The job context carries the 1.x fields now, so activity code written for release 1 runs unchanged when its job moves
  to a remote worker:
  - **the effect key**, computed by layer 3 (the workflow instance calls `EffectKey.For` when it starts the job) and
    carried by layer 2 as an opaque string on the ticket;
  - **the attempt id**, which in process layer 2 builds as `{ticket id}@{epoch}` from the epoch of the take under
    which the owner started the work. Every take increments the epoch, so a re-execution after any lost owner, in a
    trial or outside one, gets a new id. Layer 3 sees an opaque string and never parses it;
  - **the attempt number**, which in process is one plus the ticket's recorded attempts (owners lost while it was on
    trial). It is a lower bound on executions, since an owner lost outside a trial counts nothing;
  - **the deadline and the cancellation token.**
- `StartJob` accepts an optional timeout. The kernel writes a timer message to the instance in the same commit and
  recalls it when the result arrives; on expiry it cancels the job and gives the model a timeout result. That costs one
  timer row per job that asks for a timeout.
- No job actor, queue actor, gateway, remote protocol or attempt lease. Standalone Jobs ship in 1.x with the worker
  protocol (decision 1).

## Costs

Reasoned from the commit shape of [ownership and fencing](ownership-and-fencing.md#why-the-check-costs-a-round-trip)
(two round trips per PostgreSQL commit with the fence in the batch); none of it is measured.

| Remote operation | Round trips before batching | Rows |
|---|---|---|
| Activate with grants | 1 insert, the poll's share, 2 for the seen mark, 2 for the queue's commit that sends the grants; 2 for the job actors' commit, which records the attempts and sends `granted` or `declined` back; 2 for the queue's second turn, which consumes those and releases the answer. Both later commits are the same partition's next flushes, shared by every job actor and queue in them | 1 request; per job 1 grant and 1 `granted` or `declined` message, all handed over in memory within the partition and written as rows |
| Activate, nothing ready | 1 insert, the poll's share, 2 for the seen mark, 2 for the consuming commit | 1 request |
| Complete or Fail | 1 insert, the poll's share, 2 for the seen mark, 2 for the job actor's commit | 1 request, the result message, the permit release |
| Renew, durable | As Complete | 1 request |
| Renew over the transport | 0 per renewal; one timer re-arm commit per lease period per attempt | 1 timer row per lease period |

The seen mark and the commits are shared by every message of the same poll and pass, so a busy queue pays them once
for many requests. An idle worker with a 30-second long poll costs one activate row and its commits per 30 seconds. The
spike the sketch asks for measures submit, claim, complete and result delivered against Elsa.Jobs's path.

## Not verified

- The whole remote design: no job actor, queue actor, gateway, lease or protocol exists in the prototype.
- The completion-against-timer rule and the takeover grace, against the simulation's invariants.
- That the grant reaches the job actor before any completion for it, which rests on the reply being released after the
  grant's commit and on per-target `seq` order.
- Throughput of one queue shard, and the shard count a queue needs.
- The three open limits of the candidate topology, above.

## Decisions (owner, 16–17 November 2026)

The owner took these on 17 November; the [decision register](decisions.md#the-seven-design-now-answers-1617-november) lists them
with the rest of the kernel's. Rejected options are kept for the record.

1. **Standalone Elsa.Jobs ships in 1.x, together with the remote worker protocol (B).** Release 1 ships workflow
   background jobs only; 1.x ships the job actor, the queue actor, local handlers and the remote protocol together. The
   open card on the first-use MySQL/SQL Server identity lookup of a standalone job
   ([#4229 comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6876735446)) is settled in
   the 1.x spike. Rejected: (A) standalone Jobs in release 1; (C) after 1.x.
2. **A heartbeat reaches the job actor in memory over the runtime's transport (b),** with a durable message per renewal
   as the fallback when the gateway cannot reach the owner. Both are 1.x work. Rejected as the default: (a) a durable
   message per renewal.
3. **The gateway keys its messages `{worker}/{request id}` with a `keep_until` (a),** following the ingress rule.
   Rejected: (b) a fresh message id per request, deduplicated in actor state.
4. **A standalone job's effect key is `{job origin}/job/-/0` (a),** the owner's shape with fixed parts. Rejected: (b)
   the job actor's origin alone.
