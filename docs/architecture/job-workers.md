# Job workers outside the process

> **Proposed.** The kernel design awaits owner approval; this page answers design-now question 5 and does not
> authorize product implementation. The worker protocol ships in 1.x. Its contract is designed now so that release 1's
> jobs are built to it.

[Design-now question 5](../reports/clean-slate-kernel/design-sketch-draft-5.md#to-answer-in-the-design-proposal-before-the-fable-review-owner-decision-12-november)
asks for job workers that run outside the kernel's process, written in any language, that pull jobs over gRPC or HTTP
with a lease, a heartbeat and a timeout, as Zeebe's job workers do. This page proposes one job contract for the
in-process jobs of release 1 and the remote workers of 1.x: who owns an attempt, how it times out and retries, the key
the worker receives, and how a completion re-enters the fenced commit. It ends with the decisions it needs, among them
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

```
worker --HTTP/gRPC--> gateway --activate--> queue actor --grant--> job actor --commit--> reply --> gateway --> worker
worker --HTTP/gRPC--> gateway --renew, complete, fail--> job actor --commit--> reply --> gateway --> worker
```

| Part | Layer | Depends on |
|---|---|---|
| Worker gateway | Edge adapter: ingress plus reply | Adding a message by id (the delivery seam), reply addresses, authentication. It never sees partitions, positions or SQL. |
| Job actor and queue actor | Layer 3 actor types | The turn contract only: state, sends, timers as messages, replies, background jobs. |
| Tickets, the fenced commit, timers, polling, poison | Layer 2 | Unchanged. |

The durable path needs no new layer 2 concept. A job actor is started as a child of its queue shard, so it lives in the
queue's partition: the queue's grant reaches the job actor in memory after the commit, and both commit in the same
partition's batches. The gateway addresses a queue by its actor id (`queue/{tenant}/{name}/{shard}`) and a job by the
job id in its grant; layer 2 maps both to a partition, and the gateway treats both as opaque strings. A busy queue is
split into a fixed number of shards; the gateway spreads a worker's activates over them.

### What each actor keeps, and its bound

Every commit rewrites an actor's whole state, so each actor's state is kept small and bounded:

| Actor | State | Bound |
|---|---|---|
| Queue shard | Capacity permits in use per job type; holds; the shard's settings | Fixed size. Waiting activates live in memory only. Ready jobs are `job.ready` messages in the queue's inbox, so the backlog is inbox rows under question 3's inbox bound, outside the queue's state. |
| Job actor | The invocation (input, or a reference to it under decision 10), policy, implementation pin, result route; its attempts (id, worker, grant token, request id, lease, deadline, outcome); the selected result | One invocation, and at most the policy's attempt limit of attempts |
| Gateway | Nothing durable | Open requests in memory until their reply deadline |

The record that answers a resent request is the job actor's attempt list, so it lives exactly as long as the invocation
and its retention (question 2). No list of request ids grows without bound.

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
  from the worker or the gateway is trusted.
- **The expiry timer is due a grace after `LeaseUntil` and after `Deadline`.** The grace covers the gateway's
  insert-to-turn delay: the poll interval plus one mark-before-run commit (proposed default: twice the poll interval,
  at most a few seconds). The grant tells the worker `LeaseUntil`; the worker plans to finish or renew before it, so a
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
| Activate | The queue shard takes up to `MaxJobs` `job.ready` messages whose type, version and pin the worker supports and whose holds and capacity allow it, and sends each job actor a grant. Each job actor records its attempt, arms its lease and deadline timers and replies; the gateway gathers the grants. With nothing ready, the queue keeps the request in memory until a job arrives or `LongPoll` passes (the reply deadline, decision 6). Either way the activate message itself was a row: an insert, a seen mark and the consuming commit. |
| Renew | The job actor checks attempt id, grant token and lease, extends `LeaseUntil` and returns a cancellation flag when the invocation was cancelled. An expired attempt cannot be revived. |
| Complete | The job actor checks the attempt under the rule above, selects the result, recalls its timers, sends the result along the result route and tells the queue to release the permit. |
| Fail | As Complete, then either schedules the next attempt (a `job.ready` to the queue, due after the backoff, within the attempt limit) or selects a failed result. A business error code is kept on the result, so a BPMN error boundary can match it later. |
| Late report | A Complete or Fail for an attempt that expired or was replaced is recorded as stale evidence and answered `Stale`. It changes no result and no permit. |

Every answer distinguishes granted, empty, accepted, duplicate, stale, refused and unknown. A worker whose request ends
in a broken connection resends the same request id; it never invents a new attempt id. How the gateway turns a request
id into a message id is decision 3.

## The effect key the worker receives

`Grant.EffectKey` names the logical effect of the invocation. It is the same on every attempt of that invocation, so a
receiver that keeps the keys it has seen drops the repeat of a call whose first attempt succeeded before its worker was
lost.

- **A job submitted by a workflow activity** carries that activity's effect key,
  `{instance origin}/{node}/{iteration path}/{attempt}` from `EffectKey.For`. The final segment is the Durable-mode
  attempt (0 or 1) of the activity. A model-level retry of the activity is a new generation in the iteration path, so it
  gets a new key; a retry of the same invocation by its job actor keeps the key.
- **A standalone job** has no node or iteration path in the owner's sense. Its key is decision 4.
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

The owner decides between the two (decision 2).

## Standalone Jobs

The sketch's [mapping](../reports/clean-slate-kernel/design-sketch-draft-5.md#standalone-jobs-elsajobs-on-the-kernel-owner-decision-12-november)
stands: one job concept, Elsa.Jobs's public API kept (`IJobClient`, `IJobHandler<TInput, TResult>`,
`JobExecutionContext`, idempotent submission, policies, schedules, holds), its own store and workflow bridge dropped. On
this contract:

- A submission is a message to the queue shard. The queue starts the job actor as its child, with an id derived from the
  tenant, the queue and the idempotency key, so a repeated submission meets the existing actor. The job actor compares
  the request digest and answers duplicate or conflict.
- A local `IJobHandler` takes grants in the job actor's own turn as a background job, with no gateway and no row per
  request. A remote worker takes them through the gateway. Both see the same attempt, effect key and result rules.
- A workflow's awaited job stays the kernel's background job in the instance's partition. A workflow job meant for
  remote workers is a submission to a queue, sent in the instance's commit, with the instance as its continuation.

## Release 1

- Background jobs inside workflows, in process, as built: the ticket, `ExecuteJobAsync`, the result applied once, job
  poison per decision 9.
- The job context carries the 1.x fields now, so activity code written for release 1 runs unchanged when its job moves
  to a remote worker:
  - **the effect key**, computed by layer 3 (the workflow instance calls `EffectKey.For` when it starts the job) and
    carried by layer 2 as an opaque string on the ticket;
  - **the attempt id**, which in process is the ticket's id;
  - **the attempt number**, which in process is one plus the ticket's recorded attempts (owners lost while it was on
    trial). It is a lower bound on executions, since an owner lost outside a trial counts nothing;
  - **the deadline and the cancellation token.**
- `StartJob` accepts an optional timeout. The kernel writes a timer message to the instance in the same commit and
  recalls it when the result arrives; on expiry it cancels the job and gives the model a timeout result. That costs one
  timer row per job that asks for a timeout.
- No job actor, queue actor, gateway, remote protocol or attempt lease. Standalone Jobs per decision 1.

## Costs

Reasoned from the commit shape of [ownership and fencing](ownership-and-fencing.md#why-the-check-costs-a-round-trip)
(two round trips per PostgreSQL commit with the fence in the batch); none of it is measured.

| Remote operation | Round trips before batching | Rows |
|---|---|---|
| Activate | 1 insert, the poll's share, 2 for the seen mark, 2 for the queue's commit; the job actor's grant commit is the same partition's next flush (2) | 1 request; 1 grant message handed over in memory |
| Activate, nothing ready | The same up to the queue's commit | 1 request |
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

## Decisions for the owner

### 1. Release placement of standalone Jobs

| Option | What ships | For | Against |
|---|---|---|---|
| A. Release 1 | The job actor, the queue actor and local handlers in release 1; the remote protocol in 1.x | Elsa.Jobs users get the kernel early | Builds the attempt model before its remote half exists in code; adds two actor types to release 1; the spike and the lookup card below must be settled before release 1's build |
| **B. 1.x, with the worker protocol** | Release 1 ships workflow background jobs only; 1.x ships the job actor, the queue actor, local handlers and the remote protocol together | One delivery builds one attempt model for local and remote workers; release 1's scope stays as decided; the spike and the card are settled on the way to 1.x | Standalone Elsa.Jobs users wait for 1.x |
| C. After 1.x | Workflow jobs only until then | Smallest scope | Leaves Elsa.Jobs without a home on the kernel |

**Recommendation: B.** Background jobs inside workflows ship in release 1 either way.

The open owner card on the cost of a standalone job's first-use lookup bears on A and B. The facts:

- **Built:** an actor type may name a self-reference in a delivery (`IActorType.SelfReference`,
  [Turns.cs](../../prototypes/kernel-model/src/Kernel.Durable/Turns.cs)). On a store with provider-equal actor aliases
  the worker resolves a spelling it has not seen with an identity lookup and keeps the answer for the slot's lifetime
  (`Worker.cs`, `SelfReferences`). Only MySQL and SQL Server have such aliases, through their case-insensitive
  collations (`HasActorAliases` in [EfStore.cs](../../prototypes/kernel-model/ef/Kernel.EntityFramework/EfStore.cs));
  the default is false ([Store.cs](../../prototypes/kernel-model/src/Kernel.Durable/Store.cs)), so PostgreSQL, SQLite
  and MongoDB pay nothing.
- **Measured:** on the frozen Jobs foundation `cd5d08bd` (integrated with main `1c8557063d`), the existing workflow
  load-command counts on MySQL and SQL Server matched main: 1 cold, 0 first-hot and 0 repeat-hot. The comment calls
  them scoped method and count comparisons and sets no universal wire budget with them
  ([#4229 comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6878087759)).
- **Pending:** the card is recorded as "the one first-use MySQL/SQL Server identity lookup decision" for the standalone
  actor ([#4229 comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6876735446)).
  That the lookup comes from the self-reference path above is this page's reading; neither comment says so, the
  candidate is not on main, and this page measured nothing. Under B the card is answered with the 1.x spike; under A it
  must be answered before release 1's build.

### 2. How a heartbeat reaches the job actor

| Option | Cost | Behaviour |
|---|---|---|
| a. A durable message per renewal | One inbox row and its commits per heartbeat | Survives any failover; no new seam |
| **b. Over the runtime's transport, in memory, with the rules under "Heartbeats"** | No database work per renewal; one timer re-arm commit per lease period per attempt | Needs a volatile delivery to an actor's owner beside the wake signal; a lost renewal can expire an attempt early, which at-least-once already allows |

**Recommendation: b**, falling back to a when the gateway cannot reach the owner. Both are 1.x work.

### 3. How the gateway keys its messages

The owner's ingress rule is "an idempotent message insertion keyed by the source's event ID".

| Option | How a resend is recognised | Cost |
|---|---|---|
| **a. The request id as the message id, with a `keep_until`** | At the insert, as for every outside message: a resend gets a duplicate-key answer. The gateway then asks the job actor for the recorded disposition with a read-only `job.status` message. An Activate whose answer was lost answers `Unknown`; its grants expire by lease and the jobs are granted again, as in Zeebe | Each request keeps a done row until its window (about 1.6 kB of WAL per outside message with its marks on PostgreSQL, the outside-start row of WRITES.md); a resend after a lost answer costs a status message |
| b. A fresh message id per request, deduplicated in actor state | The job actor recognises the request id in its attempt list | About 0.5 kB less per request, since a consumed row is deleted; departs from the ingress rule |

**Recommendation: a.** It follows the adopted rule, and the window bounds how long a resend is recognised.

### 4. The effect key of a standalone job

The owner's shape is `{instance origin}/{node}/{iteration path}/{attempt}`. A standalone job has one effect and no
definition.

| Option | Key | Note |
|---|---|---|
| **a. The owner's shape with fixed parts** | `{job origin}/job/-/0`: the job actor's origin (its id with its creation), node `job`, the root path `-`, attempt 0 | Every key in the system keeps one shape; receivers parse nothing |
| b. The job actor's origin alone | `{job origin}` | Shorter; an extension of the owner's shape |

**Recommendation: a.**
