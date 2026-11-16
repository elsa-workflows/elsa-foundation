# Job workers outside the process

> **Proposed.** The kernel design awaits owner approval; this page answers design-now question 5 and does not
> authorize product implementation. The worker protocol ships in 1.x. Its contract is designed now so that release 1's
> jobs are built to it.

[Design-now question 5](../reports/clean-slate-kernel/design-sketch-draft-5.md#to-answer-in-the-design-proposal-before-the-fable-review-owner-decision-12-november)
asks for job workers that run outside the kernel's process, written in any language, that pull jobs over gRPC or HTTP
with a lease, a heartbeat and a timeout, as Zeebe's job workers do. This page proposes one job contract for the
in-process jobs of release 1 and the remote workers of 1.x: who owns an attempt, how it times out and retries, the key
the worker receives, and how a completion re-enters the fenced commit. It ends with the release placement of standalone
Jobs, which is the owner's decision.

## Terms

| Term | Meaning on this page |
|---|---|
| Job | Work that runs outside an actor's turn and whose result comes back as a message. It may execute more than once; its result changes state once. |
| Ticket | The kernel's record of a job in flight: a message of kind `kernel.job` to the instance, written in the commit of the turn that started it. Built. |
| Invocation | One accepted job request: its immutable input, policy and result route. A workflow's background job has one per ticket. |
| Attempt | One grant of an invocation to one worker. A retry or an expired lease starts a new attempt of the same invocation. |
| Queue actor | Proposed layer 3 actor type that holds a queue's ready jobs and its attempts in flight, and grants attempts. |
| Job actor | Proposed layer 3 actor type for one standalone invocation: Elsa.Jobs rebuilt on the kernel. |
| Worker gateway | Proposed edge adapter that serves the HTTP and gRPC protocol and turns each request into a message. |
| Attempt lease | How long an attempt stays its worker's without a heartbeat. The partition lease of [ownership and fencing](ownership-and-fencing.md) is a different lease. |

## What is built

- **Background jobs inside an instance.** `StartJob` writes a ticket (`Kinds.Job` in
  [Messages.cs](../../prototypes/kernel-model/src/Kernel.Durable/Messages.cs); `JobTicket(Model, Key, Arguments)` in
  [Worker.cs](../../prototypes/kernel-model/src/Kernel.Durable/Worker.cs)). The owner calls the actor type's
  `ExecuteJobAsync` outside the turn (`Worker.DriveJob`), on `WorkerOptions.JobScheduler` when the host sets one
  ([FINDINGS](../../prototypes/kernel-model/FINDINGS.md) section 27, R14). The outcome comes back as `job-finished`
  with a result or an error. A result is handled ahead of other messages, a cancelled job's result is never applied
  (invariant 12), and a new owner finds the ticket by polling and starts the work again.
- **Job poison.** Tickets are tried like messages; a ticket on trial at the threshold becomes an error result
  (FINDINGS section 7). Decision 9 (park the job and fault the instance) is recorded in the sketch as not built.
- **The effect key.** `EffectKey.For` in [EffectKey.cs](../../prototypes/kernel-model/src/Kernel.Workflows/EffectKey.cs),
  checked by the simulation's N7 ([NATIVE.md](../../prototypes/kernel-model/NATIVE.md), section 6) and described in
  [ownership and fencing](ownership-and-fencing.md#side-effects).
- **Reply addresses.** `Requests` and `IReplyTransport` in
  [Replies.cs](../../prototypes/kernel-model/src/Kernel.Durable/Replies.cs): a request waits for a reply that an
  instance emits, released by default after the commit of the turn that wrote it, up to its deadline.
- **Not built:** the queue actor, the job actor, attempt leases, the worker gateway, any remote protocol, and the
  Elsa.Jobs API on the kernel. The standalone Job/Queue/Reply spike is not on main.

The semantic reference for the protocol is Elsa.Jobs at `a786c52f`
(`Extensions/Jobs/src/Core/JobAttempts.cs`: claim request, attempt lease, renewal, physical receipt, selected result,
attempt states). Its own store measured, on PostgreSQL 17, separate operations of 11 (admission), 14 (claim), 13
(renewal), 8 (physical start) and 14 (completion) wire round trips
([#4229 comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6867483419)). They count
the bridge's own store and serve as a comparison point only; they set no kernel budget.

## Where it lives

```
worker (any language) --HTTP/gRPC--> worker gateway --message--> queue actor --fenced commit--> grant
        ^                                  |                         (layer 3, in a partition)
        +---------- reply after commit ----+
```

| Part | Layer | Depends on |
|---|---|---|
| Worker gateway | Edge adapter: ingress plus reply | Adding a message by id (the delivery seam), reply addresses, authentication. It never sees partitions, positions or SQL. |
| Queue actor and job actor | Layer 3 actor types | The turn contract only: state, sends, timers as messages, replies, background jobs. |
| Tickets, the fenced commit, timers, polling, poison | Layer 2 | Unchanged. |

The durable path needs no new layer 2 concept: an attempt is state of the queue actor, a lease expiry is a timer
message in the queue's commit, and a completion is an ordinary inbox message. The gateway addresses a queue by its actor
id (`queue/{tenant}/{name}/{shard}`); layer 2 maps the id to a partition. A busy queue is split into a fixed number of
shards, each its own actor; a worker's activate names the queue, and the gateway spreads activates over its shards.

## The job contract after this change

| Aspect | Today (in process, built) | Proposed (in process and remote) |
|---|---|---|
| Who owns an attempt | The partition owner, under its partition lease | In process: unchanged. Remote: the queue actor grants an attempt with its own lease and a grant token. |
| Timeouts | None in the kernel; the model decides | An attempt deadline (hard), an attempt lease (renewed by heartbeat) and an optional invocation deadline. Each is a timer message in the queue's commit, recalled when the attempt ends. In process, the deadline cancels the job's token. |
| Retries | The model receives `job-finished` with an error and decides | The worker classifies a failure: retryable with an optional backoff, or terminal (Zeebe's fail-job command). The queue keeps the count, enforces the attempt limit and applies the backoff as a due time. An expired lease or a passed deadline is a failed attempt that the queue records itself. |
| What the code receives | Key, arguments, creation (`JobContext`) | Also the effect key, the attempt number, the attempt id, the deadline and a cancellation signal. |
| Completion | Published in memory, applied in the owner's next commit | In process: unchanged. Remote: a message from the gateway; the queue's turn validates it and sends the result in its commit. |
| What a worker reports | Not applicable | Evidence. A stale worker's report is recorded and never replaces the selected result (an Elsa.Jobs idea the sketch keeps). |

The first grant pins the implementation (handler type, version and the worker's declared implementation identity) on
the invocation. A later attempt goes only to a worker that offers that pin, so a retry never runs on a different build.

### Wire contract, version 1

The same fields travel as JSON over HTTP and as protobuf over gRPC. Every mutating request carries an id chosen by the
worker; a resend with the same id gets the original answer.

```csharp
record Activate(string RequestId, string Worker, string Queue, IReadOnlyList<HandlerSupport> Handlers,
                int MaxJobs, TimeSpan Lease, TimeSpan LongPoll);
record HandlerSupport(string Type, string Version, string Implementation);
record Grant(string JobId, string AttemptId, int Attempt, long GrantToken, string EffectKey, string Type,
             string Implementation, JsonElement Input, DateTimeOffset LeaseUntil, DateTimeOffset Deadline,
             string? TraceParent);
record Renew(string RequestId, string AttemptId, long GrantToken);
record Complete(string RequestId, string ReceiptId, string AttemptId, long GrantToken, JsonElement Result);
record Fail(string RequestId, string ReceiptId, string AttemptId, long GrantToken, bool Retryable,
            TimeSpan? Backoff, string ErrorCode, string Message);
enum Disposition { Granted, Empty, Accepted, Duplicate, Stale, Refused, Unknown }
```

| Operation | What the queue does |
|---|---|
| Activate | Picks up to `MaxJobs` due jobs whose type, version and pin the worker supports and whose holds and capacity allow it. Records each attempt and arms its lease and deadline timers in one commit, then replies with the grants. With nothing due it keeps the request in memory until a job arrives or `LongPoll` passes (the reply deadline, decision 6); an empty answer records nothing. |
| Renew | Checks attempt id, grant token and lease; extends `LeaseUntil`; returns a cancellation flag when the invocation was cancelled. An expired attempt cannot be revived. |
| Complete | Checks attempt id and grant token under a live attempt; selects the result; releases the attempt's capacity permit; sends the result along the invocation's result route. |
| Fail | As Complete, then either schedules the next attempt (due after the backoff, within the attempt limit) or selects a failed result. A business error code is kept on the result, so a BPMN error boundary can match it later. |
| Late report | A Complete or Fail for an attempt that expired or was replaced is recorded as stale evidence and answered `Stale`. It changes no result and no permit. |

Every answer distinguishes granted, empty, accepted, duplicate, stale, refused and unknown. A worker whose request ends
in a broken connection resends the same request id; it never invents a new attempt id.

## The effect key the worker receives

`Grant.EffectKey` names the logical effect of the invocation. It is the same on every attempt of that invocation, so a
receiver that keeps the keys it has seen drops the repeat of a call whose first attempt succeeded before its worker was
lost.

- **A job submitted by a workflow activity** carries that activity's effect key,
  `{instance origin}/{node}/{iteration path}/{attempt}` from `EffectKey.For`. The final segment is the Durable-mode
  attempt (0 or 1) of the activity. A model-level retry of the activity is a new generation in the iteration path, so it
  gets a new key; a queue-level retry of the same invocation keeps the key.
- **A standalone job** carries its job actor's origin (its id with its creation, the rule `EffectKey` uses for an
  instance origin). One job actor is one invocation.
- **The attempt number and attempt id** travel separately, for logs and for receivers that want them. A worker sends
  the effect key where its receiver looks for a repeat: the `Idempotency-Key` header for HTTP, the local part of a
  `Message-ID` for email. Deduplication remains the receiver's job; no mode makes a side effect exactly once.

## How a completion re-enters the fenced commit

1. The worker sends `Complete`. The gateway adds a message of kind `job.complete` to the queue shard, with a fresh
   message id and the request id in its payload. The queue records request ids and receipt ids with its decisions, so
   a resend is answered from that record (`Duplicate`, with the original disposition). The sketch gives every message
   from outside the kernel a `keep_until`, so that a client's retry is refused at the insert; here the queue's record
   does that job, and a gateway message is deleted when consumed, which saves its done mark and the sweep.
2. The queue shard's owner reads it by poll (a wake hint makes that prompt) and marks it seen in a fenced commit, as for
   every message that arrives by poll.
3. The queue's turn checks the attempt, selects the result, recalls the lease and deadline timers, releases the permit,
   and sends the result: to the job actor of a standalone job, or to the continuation of a workflow job, which is a
   `job-finished` message to the waiting instance. These writes are one commit under the partition fence.
4. After that commit the reply reaches the gateway, which answers `Accepted`. A stale owner's commit is refused by the
   fence, its reply is never released, and the worker's resend reaches the new owner.
5. The waiting instance consumes the result in its own turn: delete and state change in one transaction, so the result
   is applied once, as for every message. A cancel that the instance committed first makes the queue's later result a
   message to a finished wait, which is consumed and dropped.

No bridge, consumption proof or second store is involved: the outbox is the recipient's inbox, as for every send.

## Heartbeats

A renewal must reach the queue's owner. If every heartbeat is a durable message, a worker pays an inbox row per
heartbeat: on PostgreSQL an outside insert alone writes about 0.5 kB of WAL, before its seen mark and delete
([WRITES.md](../../prototypes/kernel-model/WRITES.md#the-cost-run-bytes-per-commit-without-chaos), the outside-start
setup row). A thousand workers renewing every three seconds would write that 333 times a second. The alternative delivers a
renewal to the owner over the runtime's transport, as the wake signal already travels, and keeps it in memory. A new
owner then cannot know the renewals its predecessor received, so on takeover it extends every attempt in flight by one
full lease from the take. A lost renewal shortens a lease; the worker learns it from its next renewal (`Stale`) and stops.
The owner decides between the two below.

## Standalone Jobs

The sketch's [mapping](../reports/clean-slate-kernel/design-sketch-draft-5.md#standalone-jobs-elsajobs-on-the-kernel-owner-decision-12-november)
stands: one job concept, Elsa.Jobs's public API kept (`IJobClient`, `IJobHandler<TInput, TResult>`,
`JobExecutionContext`, idempotent submission, policies, schedules, holds), its own store and workflow bridge dropped. On
this contract:

- A submission is a message to the queue shard; the queue starts the job actor as its child in its own partition, so
  the job actor and the queue commit together. The idempotency key and the request digest decide a duplicate or a
  conflict.
- A local `IJobHandler` takes grants in the queue's own turn as a background job of the queue, with no gateway and no
  row per request. A remote worker takes them through the gateway. Both see the same attempt, effect key and result
  rules.
- A workflow's awaited job stays the kernel's background job in the instance's partition. A workflow job meant for
  remote workers is a submission to a queue, sent in the instance's commit, with the instance as its continuation.

## Release 1

- Background jobs inside workflows, in process, as built: the ticket, `ExecuteJobAsync`, the result applied once, job
  poison per decision 9.
- The job context carries the 1.x fields now: the effect key, the attempt number, the attempt id, the deadline and the
  cancellation token. Activity code written for release 1 then runs unchanged when its job moves to a remote worker.
- `StartJob` accepts an optional timeout. The kernel writes a timer message to the instance in the same commit and
  recalls it when the result arrives; on expiry it cancels the job and gives the model a timeout result. That costs one
  timer row per job that asks for a timeout.
- No queue actor, gateway, remote protocol or attempt lease. Standalone Jobs per the decision below.

## Costs

Reasoned from the commit shape of [ownership and fencing](ownership-and-fencing.md#why-the-check-costs-a-round-trip)
(two round trips per PostgreSQL commit with the fence in the batch); none of it is measured.

| Remote operation | Round trips before batching | Inbox rows |
|---|---|---|
| Activate with a grant | 1 insert, the poll's share, 2 for the seen mark, 2 for the turn's commit | 1 request |
| Complete or Fail | The same | 1 request, plus 1 result message to the recipient |
| Renew, durable | The same | 1 request |
| Renew over the transport | 0 | 0 |

The seen mark and the turn's commit are shared by every message of the same poll and pass, so a busy queue pays them
once for many requests. An idle worker with a 30-second long poll costs one activate per 30 seconds. The spike the
sketch asks for measures submit, claim, complete and result delivered against Elsa.Jobs's path.

## Not verified

- The whole remote design: no queue actor, gateway, lease or protocol exists in the prototype.
- That the in-order delivery of `job.complete` after the grant's commit holds when the gateway and owner are on
  different nodes (reasoned from per-target `seq` order and the reply released after the commit).
- The takeover grace for in-memory renewals, against the simulation's invariants.
- Throughput of one queue shard, and the shard count a queue needs.

## Decisions for the owner

### 1. Release placement of standalone Jobs

| Option | What ships | For | Against |
|---|---|---|---|
| A. Release 1 | The job actor, the queue actor and local handlers in release 1; the remote protocol in 1.x | Elsa.Jobs users get the kernel early | Builds the attempt model before its remote half is designed in code; adds a fourth actor type to release 1; the spike and the lookup card below must be settled before release 1's build |
| **B. 1.x, with the worker protocol** | Release 1 ships workflow background jobs only; 1.x ships the job actor, the queue actor, local handlers and the remote protocol together | One delivery builds one attempt model for local and remote workers; release 1's scope stays as decided; the spike and the card are settled on the way to 1.x | Standalone Elsa.Jobs users wait for 1.x |
| C. After 1.x | Workflow jobs only until then | Smallest scope | Leaves Elsa.Jobs without a home on the kernel |

**Recommendation: B.** Background jobs inside workflows ship in release 1 either way.

The open owner card on the cost of a standalone job's first-use self-reference lookup bears on A and B. The facts:

- **Built:** an actor type may name a self-reference in a delivery (`IActorType.SelfReference`,
  [Turns.cs](../../prototypes/kernel-model/src/Kernel.Durable/Turns.cs)). On a store with provider-equal actor aliases
  the worker resolves a spelling it has not seen with an identity lookup and keeps the answer for the slot's lifetime
  (`Worker.cs`, `SelfReferences`). Only MySQL and SQL Server have such aliases, through their case-insensitive
  collations (`HasActorAliases` in
  [EfStore.cs](../../prototypes/kernel-model/ef/Kernel.EntityFramework/EfStore.cs)); PostgreSQL, SQLite and MongoDB do
  not, and pay nothing.
- **Measured:** the workflow actor names no self-reference. Its load commands on MySQL and SQL Server matched main:
  1 cold, 0 first-hot and 0 repeat-hot lookups
  ([#4229 comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6878087759)).
- **Pending:** the card concerns one identity lookup on MySQL and SQL Server at the first hot use of the standalone
  job actor candidate, which names a self-reference
  ([#4229 comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6876735446)). The
  candidate is not on main, and this page did not measure it again. Under B the card is answered with the 1.x spike;
  under A it must be answered before release 1's build.

### 2. How a heartbeat reaches the queue

| Option | Cost | Behaviour |
|---|---|---|
| a. A durable message per renewal | One inbox row and its commits per heartbeat | Survives any failover; no new seam |
| **b. Over the runtime's transport, in memory, with a takeover grace of one lease** | No database work per heartbeat | Needs a volatile delivery to an actor's owner beside the wake signal; a lost renewal can expire an attempt early, which at-least-once already allows |

**Recommendation: b**, with a falling back to a when the gateway cannot reach the owner. Both are 1.x work; neither
changes release 1.
