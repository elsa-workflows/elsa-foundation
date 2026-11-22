# Observability and operator actions

> **Approved design, with conditions.** Gate 2 granted design approval on 18 November 2026 at
> `addffea7c850063d1ebe710b07866ff1640a93cb` ([verdict](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6990560103)). The implementation evidence is owed at K1 to K12,
> and the [gate 2 conditions](../program-goals/clean-slate-kernel.md#gate-2-conditions) stay open until it lands.
> This page answers design-now question 6.

[Design-now question 6](../reports/clean-slate-kernel/design-sketch-draft-5.md#to-answer-in-the-design-proposal-before-the-fable-review-owner-decision-12-november)
asks for observability as a contract: trace context on every message, stable metric names, and operator actions on
parked messages, inboxes and partitions. Release 1 ships trace context and metrics "in a basic form", and its parked-work
tooling must support inspect, retry and discard
([release 1 scope](../reports/clean-slate-kernel/design-sketch-draft-5.md#release-1-scope-owner-decision-amended-15-november)).
This page proposes the contract, where each part lives in the three layers, and what release 1 builds. Retry of parked
work extends the owner's 13 November Park decision; the owner adopted the extension on 17 November (decision 2).

## Terms

| Term | Meaning on this page |
|---|---|
| Park | The owner's decision of 13 November, as built: a fenced commit faults the instance, consumes its deliveries and removes every active subscription, keeping the last committed state, the fault and the configured history. The parent receives the faulted-child notice; later arrivals are dropped. The 17 November decisions change two parts, not yet built: Park keeps its deliveries and later arrivals (decision 2), and the parent's notice says "parked" while the parent keeps waiting ([large fan-out](large-fan-out.md), decision 5). |
| Parked work | A parked instance, with what its Park kept, until an operator recovers or discards it. |
| Read model | A projection that non-owners read: lists, counts, the run viewer. Layer 3 reads only these and never an owner's rows. |
| Operator action | A command an authorized person or tool sends to the kernel: a message to an instance, or a host administration call. |

## What is built

- **No trace context.** `Message` in [Messages.cs](../../prototypes/kernel-model/src/Kernel.Durable/Messages.cs) has no
  headers; the sketch's `message` table lists `headers`, and its contract says the kernel owns trace headers on
  messages.
- **No metric instruments.** The harnesses count commands, round trips, takes, resets and deadlocks
  ([COMMIT-SQL.md](../../prototypes/kernel-model/COMMIT-SQL.md), [ENGINES.md](../../prototypes/kernel-model/ENGINES.md)),
  which shows where each number comes from.
- **Park**, in `WorkflowInstance.Park` ([WorkflowInstance.cs](../../prototypes/kernel-model/src/Kernel.Workflows/WorkflowInstance.cs);
  [DESIGN.md](../../prototypes/kernel-model/DESIGN.md), "Park"). Unrelated jobs already entered keep their tickets and
  completion path (`RemainingJobs`). A timer not yet due stays a row; when it comes due it is a message to a faulted
  instance and is dropped.
- **Control kinds** `kernel.suspend`, `kernel.resume`, `kernel.terminate`, `kernel.delete` (`Kinds` in Messages.cs). The
  actor type handles each alone and in order; no model sees them.
- **The inputs of projector lag:** the partition position and the projection cursor (`IKernelStore.ProjectedAsync` in
  [Store.cs](../../prototypes/kernel-model/src/Kernel.Durable/Store.cs)), and the viewer's tail read
  ([Viewer.cs](../../prototypes/kernel-model/src/Kernel.Durable/Viewer.cs)).
- **Releasing a partition:** `IKernelStore.ReleaseAsync` and `Worker.ReleaseAsync`, used by balancing.
- **Not built:** recovery or discard of parked work, inbox inspection, an operator API, a handover command. The
  [native intervention experiment](../../prototypes/kernel-model/models/Native.Recovery.Run/README.md) retries a fault
  inside a still-running model; it does not recover a parked instance.

## Trace context on every message

**Contract.** Every message carries W3C trace context: `traceparent` (version `00`) and an optional `tracestate` of at
most 32 entries and 512 bytes, dropping whole entries past that. Baggage is not carried. The kernel sets it in layer 2
at every send, so no model or activity has to:

| Message | Its context comes from |
|---|---|
| A turn's sends, `continue` to itself, child starts, publish and meet tickets, replies | The turn that sent it |
| A job ticket and its `job-finished` result | The turn that started the job |
| A timer | The turn that set it |
| An outside add (API, HTTP route, broker or webhook adapter) | The caller's current activity, or the source's own trace header in an ingress adapter |

**Spans** (ActivitySource `Elsa.Kernel`):

| Span | Parent and links | Attributes |
|---|---|---|
| `elsa.kernel.turn` | Parent: the context of the turn's first message in inbox order. Links: the other messages' contexts, up to 32, with a dropped-link count | actor type, model, definition, instance id, outcome |
| `elsa.kernel.commit` | A new trace per flush; links to the turns it carries, up to 32 | outcome, instance commits, engine |
| `elsa.kernel.job` | The turn that started the job | job key, outcome |
| `elsa.kernel.take`, `elsa.kernel.poll` | Their own short traces | outcome |

- A timer that comes due long after it was set would keep one trace open for days. A turn whose first message was due
  more than a trace horizon (proposed default: one minute) after it was sent starts a new trace and links to the sender.
- An Ephemeral instance that finishes unwritten keeps its context in memory and writes nothing, so request-response
  workflows stay at zero database calls.
- The history envelope records the turn's trace id when its span was sampled, so the run viewer can link a step to its
  trace. Unsampled turns record nothing extra. Sampling never changes what executes.
- An ingress adapter may drop context from an untrusted caller and start a fresh trace.

**Storage.** The kernel's context gets its own nullable `trace` column on `message`, in binary: 26 bytes (version,
trace id, span id, flags) plus a one-byte length header on PostgreSQL, 55 bytes as text; `tracestate` only when present.
Application headers stay in the sketch's `headers` column, set through the turn contract's send options; this page does
not change them. PostgreSQL writes a row's bytes again at every tuple rewrite, so the trace column is paid at the insert
and at each seen or done mark. Against WRITES.md's rows
([cost run](../../prototypes/kernel-model/WRITES.md#the-cost-run-bytes-per-commit-without-chaos)): a message to self
(insert and delete, about 365 B) gains about 27 B, 7 %; an outside message kept until its window (about 1.56 kB with
its marks) gains about 81 B, 5 %. These are estimates; release 1's cost run measures them with the same harness.

## Metrics

Meter `Elsa.Kernel`, instrument names in Elsa's existing style (`elsa.runtime.admission.*` in
`RuntimeAdmissionTelemetry.cs`). Attributes are small fixed sets. Instance ids, keys and payloads never become
attributes; tenant appears only where a row says so, with a cap on distinct values. A per-partition attribute is
opt-in, since 1,024 virtual partitions per gauge is too many for most backends; the distribution over partitions is
reported as a histogram instead.

| Name | Instrument, unit | Meaning | Why |
|---|---|---|---|
| `elsa.kernel.inbox.depth` | gauge, {message} | Due, unconsumed messages in the partitions this node owns. Exact while a partition's last poll came back short; beyond that, question 3's capped estimate | Backlog |
| `elsa.kernel.partition.backlog` | histogram, {message} | Each owned virtual partition's due-depth estimate (question 3's `backlog` field), one sample per partition per sample interval; per-partition series opt-in | The bound of question 3, per virtual partition |
| `elsa.kernel.inbox.oldest_age` | gauge, s | Time since the oldest due, unconsumed message became due | The alerting signal for a stuck or slow partition |
| `elsa.kernel.admission.refusals` | counter, {request} | Requests refused at admission, by reason and by tenant (capped) | Question 3's refusals, named here as the [fairness page](fairness-and-backpressure.md#release-1-the-basic-form) asks |
| `elsa.kernel.timer.lateness` | histogram, s | Delivery time minus due time, for timers | Timers are messages; lateness is what users notice |
| `elsa.kernel.turn.duration` | histogram, s | Wall time of a turn, by actor type, model and outcome (committed, held, parked, threw) | Slow models and activities |
| `elsa.kernel.commit.duration` | histogram, s | The fenced store call, from send to answer, by outcome (landed, fenced, unknown, transient, definite) | Commit latency, the database's share |
| `elsa.kernel.commit.turns` | histogram, {turn} | Instance commits per flush | Batching is the cost model: 100 instance commits cost the round trips of one |
| `elsa.kernel.resets` | counter | Owner resets, by cause (fenced, unknown outcome) | A stale owner, or connections breaking mid-commit |
| `elsa.kernel.lease.takeovers` | counter | Takes of a partition whose other owner's lease had expired | Failovers |
| `elsa.kernel.partitions.owned` | gauge, {partition} | Partitions this node owns | Balance across nodes |
| `elsa.kernel.partition.releases` | counter | Releases, by reason (balance, drain, operator) | Handovers |
| `elsa.kernel.poison.trials` | counter | Messages and job tickets put on trial | Poison counting is the weakest part of the design (sketch, "Earlier open questions") |
| `elsa.kernel.park.transitions` | counter | Parks, by reason (poison threshold, turn threw, definite commit error) | Why instances park |
| `elsa.kernel.parked` | gauge, {instance} | Parked work not yet recovered or discarded, from the parked-work read model | The operator's queue |
| `elsa.kernel.projector.lag` | gauge, {position} | Partition position minus projection cursor, maximum and sum over the partitions this node projects | How stale lists and the viewer's projected rows are |
| `elsa.kernel.projector.round.duration` | histogram, s | One projector round | Projector cost |
| `elsa.kernel.jobs.active`, `elsa.kernel.job.duration` | gauge {job}; histogram, s, by outcome | Background jobs in flight and their run time | Job load |
| `elsa.kernel.instances.loaded` | gauge, {instance} | Instances held in memory | The bound on instances in memory (FINDINGS section 19) |
| `elsa.kernel.drain.held` | gauge, {message} | Rows restored by recover drains that their models hold | The part of a drain's addition to due depth and memory that the progress gate does not bound |
| `elsa.kernel.drain.pending`, `elsa.kernel.drain.pending.bytes` | gauges, {message} and By | Restored rows of open recover drains that this owner has not yet handed off (neither delivered into a committed model turn nor held; a row withheld by a suspension stays counted; ticket kinds, job, meet and publish, are kernel work and never counted, so the gauges count model backlog only), and their stored size with payloads; attribute `aggregate`: `sum` over this node's drains, or `max`, the largest single instance's value (proposed, fifth review R5-M3) | The part the handoff credit bounds, which the held gauge alone did not show |
| `elsa.kernel.cascade.pages` | counter, {page} | Commits that discharge a page of a termination cascade, by where (instance row, retirement marker) (proposed, fifth review R5-B2) | A large registry ending in pages |

Gauges are sampled off the turn path, once per pass or poll. A counter records what this node saw: a commit whose
answer was lost counts as an unknown outcome.

## Operator actions

| Action | How | Release 1 |
|---|---|---|
| Inspect parked work | The parked-work read model: instance, actor type, model, definition and version, fault, reason, when, the Park's first revision, and the handles of the deliveries the Park consumed, from its journal entry. Every kept row with its reason comes from `IInboxInspection.KeptAsync`. Payloads need a separate permission | Yes |
| Retry parked work | A `kernel.recover` message in `retry` mode: as `resume`, and the deliveries the Park consumed that the operator selects, by delivery handle, are delivered again | Yes (decisions 1 and 2) |
| Recover without retry | `kernel.recover` in `resume` mode: waits restored, and the arrivals kept while parked (decision 2) delivered; the deliveries the Park consumed, timers among them, are neither delivered again nor re-armed, so a wait whose message the Park consumed (the poison among them) keeps waiting, which is what `retry` is for. Both modes run as a bounded drain (below) | Yes (decision 1) |
| Discard parked work | A `kernel.discard` message | Yes |
| Inspect an inbox | Pending deliveries of one instance, in delivery order: handle, id, kind, sender, due, state (pending, held or on trial), attempts, size; and, for a parked instance, its kept rows with their handle and reason (`KeptAsync`); paged | Metadata only |
| Suspend, resume, terminate, delete | The built control kinds | Yes |
| Release a partition, drain a node | Host administration: asks the owner to release, through the runtime | Yes |
| Force a handover from an unresponsive owner | Host administration: a forced take, a conditional update at the expected owner and epoch that names the new owner and **increments the epoch**, so the old owner's next commit fails the fence. Ending the lease alone would not do: with the fence in the batch, the old owner's next commit at the same epoch would still land, and under the node lease its next heartbeat renews the lease of every partition it owns | Later |

### What Park keeps

Before K3, Park deleted its consumed deliveries or marked them done if they carried a `keep_until`. Later arrivals
were dropped or withheld. FINDINGS 25.9 describes the original Park without a retry queue. Retry of a parked message
needs two additions, which **extend** the
13 November decision. The owner adopted them on 17 November (decision 2). The in-memory foundation keeps the durable
inputs consumed by Park and other durable arrivals while it is unresolved. The in-memory sweep skips these rows.
An in-flight completion for a parked public actor lands as an authenticated result before a later turn keeps it.
`--park-kept-check` exercises both reasons, result conversion, bounded discard and finite expiry on the
in-memory Foundation. Park's fenced commit arms the timer at database time. An owner that sees its timer early
replaces it at the same deadline. Takeover preserves the deadline and, once a drain opens, its cursor.
Restore and recovery, the remaining resolution kinds and the physical providers retain their milestone cases.

1. **Park keeps the deliveries it consumes, and the arrivals while parked,** as done rows with a kept reason. A
   nullable `KeptBy` column on the message row holds `Consumed` (the Park's commit, or a later re-park, consumed it)
   or `Arrived` (the faulted instance consumed it while parked). The commit that consumes the row sets it in the same
   update that marks the row done, so it costs no statement, only a small column. An instance has at most one
   unresolved Park, so the value needs no Park identity. The built code can park an already-faulted row again (the
   `MaxAttempts` check in `Worker.cs`, or a definite commit error); that re-park continues the same Park, and the rows
   it consumes get `Consumed`. Ordinary done markers, outside commands the instance handled before the Park and still
   within their window, have no `KeptBy` and are never restored, so none is applied twice (invariant 4).

   **Rows are told apart by the kept reason and by their full key, never by id.** The store keys a message by
   `(Id, Scope, Match)` ([Messages.cs](../../prototypes/kernel-model/src/Kernel.Durable/Messages.cs), `InboxKey`), so
   two rows can share a wire id: a private delivery and an ordinary one, or two event matches. The conformance suite
   creates such twins and consumes them one at a time
   ([PhysicalRetirementChecks.cs](../../prototypes/kernel-model/conformance/Kernel.Store.Conformance/PhysicalRetirementChecks.cs)).
   Every place that names a kept row uses a **delivery handle**: an opaque string that layer 2 encodes from the full
   key (`Id`, `Scope` and the exact `Match`) and only layer 2 decodes. The Park's journal entry lists the handles of
   the rows it consumed (bounded by the parking turn's inbox), operators select by handle, the waits lookup below goes
   by handle, and every update's predicate names the full key. The id-based `ParkRecord.Consumed` list of the earlier
   text is withdrawn: the row's own reason replaces the membership test.

2. **Kept rows leave only through their instance.** The sweep's predicate becomes
   `Done AND KeepUntil < :now AND KeptBy IS NULL`, so it never removes a kept row. The `Parked` window still applies:
   Park's commit arms a `kernel.park-expiry` timer for the Park's time plus the window (none when the window is null,
   the default), and records that deadline and the armed timer's full key in `ParkRecord.Expiry`
   ([internal controls](#every-park-operation-names-its-park)). **The window's expiry is an automatic discard:** when
   the timer fires, the instance's own drain
   resolves the Park as discard does (below). It clears the kept rows, giving them the ordinary window and clearing
   `KeptBy`; records the disposition "expired"; sends the parent its final Faulted notice; leaves the instance
   Faulted; and writes the terminal mark, so the `Faulted` retention window runs from it. After the expiry the Park is
   resolved, so recover is refused in both modes.

3. **Park writes a journal row at every history level**, so the projector can maintain the parked-work read model at
   Minimal. The data lifecycle page's format item 4 is adopted too, so this row is its "ended" entry and adds nothing;
   the two land as one rule. The data lifecycle page's format item 6 (payload-free done markers) exempts kept rows, so
   a retry finds their payloads.

**Invariant: `KeptBy` is non-null only while its Park is unresolved.** A Park is resolved by recover, discard, the
expiry of its window (an automatic discard), terminate or the delete of the parked instance. Each of these is a
**drain** (below) that clears `KeptBy` on every row the Park kept, restored
or not, and the Park counts as resolved when its drain completes. Revisions start
again for a new creation under the same id, so no later Park of a new run can find a predecessor's rows, and an ordinary
delete needs nothing extra.

A child completion that arrives while parked is kept with the rest, and its entry stays in the parent's child registry;
today such a notice closes the entry without the model.

### Every Park operation names its Park

A Park is identified by the instance's immutable **creation** and the **Park mark**: the revision and time that the
Park's commit records as the row's terminal mark ([data lifecycle](data-lifecycle.md#retention-and-deletion-of-finished-instances)).
`ParkRecord` keeps it as the Park identity from the first Park until the Park resolves, and a re-park continues the
same Park under it. The row's terminal mark follows one rule, which the data lifecycle page states too:

- a recover's Open clears the Park's mark on the row, and `ParkRecord` still holds the identity;
- a mark the model's end writes during the drain stays through Complete, and Complete does not clear it;
- a recover that completes with the model still running leaves no mark, and the run's next end, Park or discard
  writes one;
- a re-park that interrupts the drain, which can happen only while the model runs, writes the Park's mark back.

Once the Park resolves, the identity is gone, and the next Park of the same creation writes a new mark. Revisions restart
for each creation, so the mark identifies a Park only together with the creation. This mirrors the qualified delete of
the owner's decision 9A: every Park operation is qualified, and none has a force form.

```csharp
public sealed record ParkSelection(string Tenant, string Instance, long Creation, long ParkRevision, long ParkAt);  // all required

// kernel.recover   {operation, park: ParkSelection, mode, selection}
// kernel.discard   {operation, park: ParkSelection}
// kernel.terminate {operation, instance, creation, park: ParkSelection?, thenDelete}
//                  // an operator's: instance always (checked right after the tenant); park required when the row
//                  // is parked, and checked whenever present, whatever the status; thenDelete (default false) only
//                  // on an operator's terminate (below)
// private controls, minted by layer 2 only, under the control scope (below):
// kernel.drain       {creation, parkRevision, parkAt, generation, serial}
// kernel.park-expiry {creation, parkRevision, parkAt, generation, serial}
// kernel.cascade     {creation, serial}   // fifth review R5-B2: the next page of a termination cascade
```

- **The tenant** comes from the operator's bound scope ([tenancy](tenancy.md#every-operation)), never from the payload.
- **The target instance** (proposed, fourth review R4-M5). `Instance` is the instance's canonical tenant-qualified
  actor id, the id its row is stored under; for a caller key, the actor id its binding row holds (fourth review
  R4-M6, [the caller-key binding](tenancy.md#names-one-namespace-per-tenant)). A creation is unique per tenant and id, so two distinct instances of one
  tenant, under two caller keys, can both be on creation 1 with equal Park marks; only the id tells their selections
  apart. Where an API passes a selection as an opaque handle, the handle encodes `Instance` with the other fields, and
  the read model exposes it (below), so a selection used with another target still names the instance it was read
  for. The same binding covers every operator command on an instance: a Park operation, a delete
  ([data lifecycle](data-lifecycle.md#proposed-the-qualified-delete)) and an operator's terminate, with or without
  `ThenDelete`, each carry `Instance`, and the owner compares it right after the tenant, refusing a mismatch with
  reason target.
- **The target creation is the row's at the store's acceptance (proposed).** A reused caller key keeps its instance
  id, `{vp}/Q(t, key)` ([tenancy](tenancy.md#names-one-namespace-per-tenant)), under a higher creation, since a
  creation is unique per tenant and id across deletes, with the high-water mark kept for every id kind ([data
  lifecycle](data-lifecycle.md#proposed-the-qualified-delete)). The Park binding rests on that: A and B under one key
  share the id that `ParkSelection.Instance` names, so its creation check tells A from B only because B's creation is
  higher. An operator
  command addressed by key gets its target and `TargetCreation` pinned when the
  store accepts its add, to the instance and creation the key resolves to at that moment. The operator path never
  copies the `ParkSelection`'s creation into `TargetCreation`. So a command whose add lands after the key was reused
  targets the successor B and passes the built `TargetError` at B, and the check that refuses it is the
  `ParkSelection` creation check below. A command whose add lands while A still exists targets A, and if it is
  delivered after A's retirement, the built protocol refuses it as `RetiredCreation` before the reuse and as
  `WrongCreation` after it (`Creation.cs` 103–109; case P-1c). P-1, P-1b, P-1c and P-3 hold under this same-id
  addressing.

  Nothing builds this yet. No operator path for Park commands exists, the stores' public add writes the message as
  supplied (`InMemoryStore.cs` 227–232, `EfStore.cs` 593, `MongoStore.cs` 390), and today's key admission binds each
  creation of a key to a fresh run id, `g{n}/key/{serial}`, and sets no `TargetCreation` (existing prototype
  behaviour, `KeyAdmission.cs` 59–73). Under fresh run ids A and B could both hold creation 1 with equal Park marks,
  and a command pinned to B would pass every Park check. Release 1's key admission replaces that behaviour with the
  same-id rule above, and admits each key under the name limits ("Where names enter", below). A caller key is not a
  generated id, so walkthrough decision 7, "a generated id never comes back", is untouched; its other half, a caller
  key refusing a repeat within `keep_until`, holds as built.
- **The owner's checks before Open,** in the actor's command turn, refusing on the first that fails: the tenant equals
  the row's; the selection's `Instance` equals the row's stored id (reason target, fourth review R4-M5); the
  creation equals the row's and both are known; the row is parked and its Park mark equals the
  selection's. An operator's terminate that reaches a parked row without a `ParkSelection` is refused as ambiguous. A refusal
  changes nothing, is recorded in the operation's outcome and counts in `elsa.kernel.park.refused` by reason (tenant,
  target, run, Park, ambiguous, resolution open, control, and parent, for a terminate refused by the parent binding in
  any state, fifth review R5-M5).
- **A selection binds the command whatever the row's status when it arrives.** An operator's terminate that carries a
  `ParkSelection` is a Park operation: the checks above run on it even when the row is no longer parked. So a terminate
  selected against a Park and delivered after that Park's recover completed, with the model now Running, is refused
  (reason Park): the Park it names is resolved, and the run the operator would cancel is one the operator never saw.
  The operator reads the current state and sends a new terminate; one with no `ParkSelection` is the ordinary
  terminate of a running instance. Delivered after an interrupting re-park, the Park mark is back and the same
  terminate is accepted. The selection is checked every time it is present: by the checks above, or, while a recover
  drain is open, by the comparison with `ParkRecord`'s Park identity in the next bullet.
- **A terminate of a recovering instance.** While a recover drain is open the row is Running, so an operator's
  terminate needs no `ParkSelection`: with none, it is the ordinary terminate of a running instance and interrupts the
  drain. With one, the row's Park mark is cleared, so the selection is compared with `ParkRecord`'s Park identity,
  which the drain still holds: the tenant, `Instance` and creation must equal the row's and the Park mark must equal
  `ParkRecord`'s. A match is accepted and interrupts the drain; a mismatch is refused (reason Park). Only a recover
  drain can be interrupted this way; with a discard, expiry, terminate or delete drain open, every terminate is
  refused (reason resolution open), as the bullet on those drains below states. Once the recover completes,
  `ParkRecord` holds no identity and the selected terminate is refused, as above.
- **A terminate after the model ended during an open recover drain.** Once the model has ended, the row has its end
  status and the mark the end wrote, so the bullet above does not apply: interruption happens only while the model
  runs, nothing reopens the Park after the end, and the end's own turn already sent the parent its one notice. A
  selected terminate is refused (reason resolution open), as the `MaxAttempts` table's second row treats a late
  input. When its selection equals `ParkRecord`'s Park identity it has authority over the run, so the refusing commit
  still sends terminate to the children and cancels the jobs, clearing both, as the cascade gate below states; it
  keeps the end status and mark and sends the parent nothing. With another selection it is refused with no cascade.
  An unselected one is the built terminate of an ended row (existing prototype behaviour): an ended row that
  still has children or jobs goes to the control path (`WorkflowInstance.cs` 63–64, then `Terminate` at 158), whose
  turn (177–203) raises the revision, clears and cancels the jobs and sends terminate to the children, keeps the end
  status and sends the parent no notice; an ended row with neither is consumed with no change (78–90). Either way it
  opens no drain, and the recover drain runs on. A terminate with `ThenDelete` keeps its own contract
  ([`ThenDelete` follows its own contract](#delete-during-a-drain)): it takes the qualified-delete path of an ended
  run and takes the drain over, in the order that contract gives: checks 1 and 2; "in progress", when a delete
  already owns the drain's deletion step; then, if it carries a `ParkSelection`, the selection against
  `ParkRecord`'s Park identity, refused on a mismatch (reason Park); then check 5. Refused as "in progress" or taking
  the drain over, it ends the run's subtree when the ended row still has a registry or an outstanding job ("The subtree on this path" under
  [`ThenDelete` follows its own contract](#delete-during-a-drain), fifth review R5-B2).
- **A terminate while a discard, expiry, terminate or delete drain is open** is refused (reason resolution open),
  whatever its origin: an operator's, selected or not, with `ThenDelete` or without, a parent's tree terminate and a
  combiner's FailFast alike. The test comes after the tenant check and the creation check (an operator's against its
  bound scope, the instance and the creation it names, a parent-bound one's by its `TargetCreation`) and before the Park mark, the
  ambiguity rule and the parent binding. The refusal leaves the drain alone: it opens no drain and replaces no
  Resolution, its `ThenDelete` is not added, and the open drain keeps its kind, its operation, its outcome and the
  row's status and mark, and sends the parent the one notice it owes (Faulted for discard, expiry and the delete of a
  parked instance, Cancelled for terminate). An operator's refusal is recorded in its operation's outcome; a parent's
  or a combiner's send has no operation and is counted in `elsa.kernel.park.refused` only. A parent that has ended
  consumes and drops the drain's notice. A drain a delete took over keeps its kind for this rule: a taken-over
  discard, expiry or terminate is one of these drains, and a taken-over recover whose model ended follows the bullet
  above.
  - **The subtree still ends.** Park leaves an instance's children and jobs alone, and discard, expiry and delete
    never terminate them, so a refusal that dropped the terminate would leave a live subtree under a terminated root,
    against invariant 17 ([FINDINGS.md](../../prototypes/kernel-model/FINDINGS.md), section 13, guarded by the
    `TerminateRootOnly` mutation) and large fan-out's FailFast. So the refusing commit does the subtree part of the
    built terminate of an ended row (`WorkflowInstance.cs` 63–64, then `Terminate` at 158 and 179–198): it sends
    terminate to every child in the instance's registry, cancels its jobs and clears both from the row. Holding the
    terminate until Complete would not do, since a delete drain's Complete removes the row first. The children were
    started by this instance, so each cascade terminate is a send it composes with its own id and creation as `Sender`
    and `SenderCreation` to a child whose recorded `Parent` and `ParentCreation` are its own: at each child it passes
    the parent binding below, and at a child with its own drain open it is refused there and cascades again. After a
    terminate drain's Open, which already terminated the children and cancelled the jobs, the registry is empty and
    the cascade sends nothing. Every job cancel in this cascade is the idempotent cancel of "A termination cascade is
    paged and idempotent" (below, fifth review R5-B1), and a registry larger than one page is discharged in pages
    under the same rule (fifth review R5-B2).
  - **Only a terminate with authority over the instance cascades.** The cascade runs when the terminate would have
    been accepted with no drain open: a parent-bound one that passes the parent binding (issued origin,
    `TargetCreation`, and `Sender` and `SenderCreation` equal to the row's `Parent` and `ParentCreation`), or an
    operator's whose `ParkSelection` equals the Park identity in `ParkRecord`. Any other terminate, an application
    send from a non-parent or an operator's with no selection or another one, is refused with no cascade. This gate is
    a coordinator design choice ([register](decisions.md#the-gate-2-fix-round-17-november)). The terminates a cascade
    sends carry the cascade origin mark, and every recipient checks the parent binding on them in every state, a
    running one included ("Cascade terminates are parent-bound in every state" below, fifth review R5-M5).
  - **The retirement marker keeps the obligation after a delete** (proposed, fourth review R4-B2). A parent-bound
    terminate committed while a delete drain is open can be delivered after that drain's Complete has retired the row.
    Existing prototype behaviour: the built delete retires the row and terminates none of its children
    (`WorkflowInstance.cs` 161–166, `CreationProtocol.Retire` at `Creation.cs` 119–120), and the worker refuses every
    later delivery to the marker as `RetiredCreation` before the actor type sees it (`Worker.cs` 1050–1055,
    `Creation.cs` 105). So the marker keeps the row's recorded `Parent` and `ParentCreation` and, as they were at
    retirement, its children (each `InstanceRef` with its creation) and its jobs (each job key with its current
    `JobGeneration`), in the layer-2-neutral list the actor type hands the creation protocol when it retires the row.
    A parent-bound terminate that reaches the marker (issued origin, `TargetCreation` equal to the marker's creation,
    `Sender` and `SenderCreation` equal to the recorded `Parent` and `ParentCreation`) is still refused with
    `RetiredCreation`, and in the same commit the worker's refusal path composes the cascade from that list: a
    terminate from the marker to every listed child and a cancel of each listed job's current-generation ticket,
    whose key it derives from the marker's id and creation, clearing the list. Each cancel is idempotent: a ticket
    that is absent, already consumed or `Done` is a no-op, never a counted consume (fifth review R5-B1), and a list
    longer than one page moves from Held to Discharging and is sent in pages of at most `P_cascade` entries (fifth
    review R5-B2; both under "A termination cascade is paged and idempotent" below). Any other terminate gets the
    plain refusal, with no cascade, and the marker sends the parent nothing in either case. An operator's terminate carrying A's
    selection is among them: delivered while A's delete drain is open, it is refused as "resolution open" and
    cascades (J-1), and delivered after Complete it meets the marker and gets the plain refusal, `RetiredCreation`. That
    difference is accepted, a coordinator design choice: the marker cascades only for the recorded parent, and the
    operator is not that parent. For a child, the obligation stays on the marker, so the parent's own terminate still
    ends the subtree (DT-7). For a root, the marker keeps nothing and its children keep running, as delete semantics
    give; the operator terminates each child by its own selection. The marker's shape, its bound and when the
    obligation clears are on the data lifecycle page ([the qualified
    delete](data-lifecycle.md#proposed-the-qualified-delete)).
- **A terminate from the child's own parent needs no `ParkSelection`.** A parent's tree terminate is composed by the
  actor type (`WorkflowInstance.cs`, the terminate path); a combiner's FailFast under large fan-out decision 5 sends
  `kernel.terminate` through `ITurn.Send`, an application send. Neither carries a Park identity, and requiring one
  would strand the child: the parent ends, the child stays parked under the default null window, and an unresolved
  Park is never eligible for retention, so it would stay for good. Both are sends from a committing actor:
  - **A model's send is stamped.** `DurableTurn.Send`, behind `ITurn.Send`, gives the message an issued id and sets
    `Sender` to the committing actor's id and `SenderCreation` to its creation (`Turns.cs`), so a model cannot choose
    either.
  - **An actor type's composed send is checked.** The actor type can set a sender itself: the tree terminate is built
    in `WorkflowInstance.cs` with the actor's own id and creation, `SendFromReceivedAddress` sets `Sender` to the
    received address, and `Add(Message)` keeps a supplied `Sender` and `SenderCreation` when the sender is another
    actor (`Turns.cs`). The rule: a `kernel.terminate` an actor type composes, by any of these paths, must carry the
    committing actor's own id and creation as `Sender` and `SenderCreation`; the commit refuses any other as a
    definite error.
  - **An outside add** never carries the issued scope (outside entry points discard a supplied scope, `Messages.cs`).
  - **`ThenDelete` comes from an operator only** (proposed). A model's `ITurn.Send` of a `kernel.terminate` that
    carries `ThenDelete` throws at the call, so the turn fails as a model fault, and the commit refuses an actor
    type's composed one as a definite error. So a parent's tree terminate and a combiner's FailFast never delete the
    child; an operator's terminate, an outside add in the operator's bound scope, is the only one that can.
  - **The operator's authority is checked at ingress** (proposed). Being in a tenant's bound scope grants no
    operator action by itself: the tenancy page gives each operator action its own permission per tenant
    ([tenancy](tenancy.md#every-operation), under ADR 0097's permission model, case T9). An outside
    `kernel.terminate` needs the terminate permission in the bound scope, and one that carries `ThenDelete` also
    needs the delete permission there, the same permission an operator's `kernel.delete` needs. A caller without
    it is refused at the entry point with reason "permission", before any row is written: no message, no change at
    the instance, and nothing recorded beyond the audited refusal.

  So a terminate is accepted at a parked child without a `ParkSelection` when no discard, expiry, terminate or delete
  drain is open (above), it has an issued origin, its `TargetCreation` equals the row's creation, and its `Sender` and
  `SenderCreation`, stamped or checked as above, equal the row's recorded `Parent` and `ParentCreation`. Any other
  sender, an application send from a non-parent included, needs a `ParkSelection`. A send from an earlier creation of
  the parent fails the binding and is refused.
- **Cascade terminates are parent-bound in every state** (proposed, fifth review R5-M5). The kernel tells an
  internal cascade apart from every other authorized terminate:
  - **The cascade origin mark.** Every terminate the kernel composes to end a subtree carries it: the actor type's
    tree terminate (`WorkflowInstance.cs` 196–197) and every page of a termination cascade, sent by a `kernel.cascade`
    discharge from an ended instance, by an open-drain refusal or by a retirement marker ("A termination cascade is
    paged and idempotent" below). It is a flag on the message row, one byte among the fixed-width fields, on a
    message with an issued origin. Only the kernel sets it. No turn method can: `ITurn.Send`, `SendFromReceivedAddress`,
    `Add` and `AddTerminalNotice` never set it, and `SendOptions` has no field for it. Every outside entry point clears
    it, as it resets a supplied scope (`Messages.cs` 49). The commit refuses, as a definite error, a cascade-marked
    message that is not a `kernel.terminate` with an issued origin whose `Sender` and `SenderCreation` are the
    committing row's own. So a model cannot forge it.
  - **The binding.** A recipient checks every cascade-marked terminate before it acts, in every state: running,
    parked, draining, ended with a registry or an obligation, and retired. It compares the terminate's `Sender` and
    `SenderCreation` with its recorded `Parent` and `ParentCreation`. A mismatch is a plain refusal: the message is
    consumed with no other change, no cascade and no notice, and it counts in `elsa.kernel.park.refused` with reason
    parent. The order of the checks is unchanged where it is already stated: the tenant and creation checks first,
    then "resolution open" while a discard, expiry, terminate or delete drain is open, then the binding.
  - **A model's application terminate keeps its built authorization.** A `kernel.terminate` a model sends through
    `ITurn.Send`, a combiner's FailFast included, carries no mark. Existing prototype behaviour: at a running row,
    or an ended row that still has children or jobs, it goes to the control path (`WorkflowInstance.cs` 63–64), whose
    `Control` sends every terminate to `Terminate` (155–158). `Terminate` (177–203) ends the row, cancels its jobs and
    terminates its children with no check on the sender, and that stays as built. At a parked or draining row P-7's
    rule applies (above): the parent's terminate is accepted, any other sender needs a `ParkSelection`, and an open
    discard, expiry, terminate or delete drain refuses it as "resolution open". At a retirement marker it gets the
    plain refusal unless it is parent-bound (DT-8).
  - **What the review's trace needed.** The fourth round's RT-1 supplied the binding in its fixture before its live
    termination (`ParentBound`, `experiments/Review4.Blockers/Rt1.cs` 329–330, used at 352–357). The proposal puts
    that check in the actor type's control path, ahead of `Terminate`, for cascade-marked terminates.
  - **An operator's terminate is not parent-bound.** It is an outside add, whose scope the entry point resets to the
    ordinary one (`Messages.cs` 49), so it never has an issued origin or the mark, whatever `Sender` it names. Its
    authority is the terminate permission checked at ingress, and at a parked or draining row its `ParkSelection`,
    as above.
  - **Retirement keeps no membership check.** The retirement marker's validation still checks the list's shape and
    its jobs against the previous row's `Jobs`, and it never decodes the child registry ([data
    lifecycle](data-lifecycle.md#proposed-the-qualified-delete)). A listed instance that is not the retired row's
    child refuses that cascade's marked terminate under this binding, whatever its state, so a wrong entry ends
    nothing.
- **A termination cascade is paged and idempotent** (proposed, fifth review R5-B1 and R5-B2). Four paths end a
  subtree: the live actor's terminate (`WorkflowInstance.cs` 177–203, which terminates every child at 196–197 and
  cancels every job at 198), a terminate drain's Open ("Terminate as a resolution", below), the refusing commit
  while a drain is open (J-1, above), and a retirement marker's cascade (above). All four follow one rule.
  - **The obligation.** The ending turn, the refusing commit or the marker's refusal path hands layer 2 the
    obligation list, in the layer-2-neutral shape the retirement marker keeps: each child as its `InstanceRef` and
    creation, each job as its job key and `JobGeneration`. Layer 3 states the obligation once; layer 2 discharges it.
    The row carries it as `Cascade {State, Entries, Cursor, Control, Serial}`, on the instance row or on the
    marker. `Held` is a marker's state while it awaits its parent's terminate; `Discharging` is the state while
    pages are sent, with the cursor.
  - **The page.** Each discharge commit sends at most `P_cascade` entries from the cursor, in list order: a terminate
    to each child entry and the idempotent cancel (below) of each job entry. The list puts every job entry before the
    first child entry, and the first page's commit also sends the in-process cancel of every listed job, so no job of
    a Cancelled instance keeps running while later pages are pending; each ticket's counted consume still comes in its
    own page. A pending ticket that a later page has not reached cannot start meanwhile: a job is entered only for a
    running row, so an ended row or a marker stands it down (existing prototype behaviour, `StartJob`, `Worker.cs`
    1168–1177), and a row whose terminate Resolution is open withholds every physical job entry although it is still
    Running ("No job is entered while a terminate drain is open" under "Terminate as a resolution" below, sixth
    review R6-M8). `P_cascade` = 1,000 entries (proposed),
    well under the 10,000 send cap; the owned commit splits its statements by rows and bytes, so a page is a few
    statements in one fenced transaction. The commit rewrites the row at its revision plus one (the instance row by a
    kernel write at the expected revision, as the `Cohort` mark's clear is written, or the marker), with the cursor
    advanced, or the list cleared after the last entry, and while entries remain it sends the next `kernel.cascade`.
    The ending turn or the refusing commit sends the first page in its own commit, so a registry of at most
    `P_cascade` entries ends in one commit, as today. A page's sends are therefore at most its `P_cascade` entries'
    terminates, one `kernel.cascade` while entries remain, and, on the first page of a live terminate, the ending
    turn's own sends: the parent's one notice. The page's terminates take issued ids derived from the row's
    origin and the page commit's revision, as the built tree terminate derives them (`WorkflowInstance.cs` 196), so
    no two pages share an id, and each carries the cascade origin mark (above).
  - **`kernel.cascade` is a private control.** It lives in the control identity domain (control scope 2^62, no
    `Match`, a private kind), is minted by layer 2 only, in the commit that records its full key in
    `Cascade.Control`, with the row's own id and creation as `Sender` and `SenderCreation` and its creation as
    `TargetCreation`, and its id derives from the row's origin and `Cascade.Serial`, which grows by one per control.
    It acts only when its full key equals `Cascade.Control`; any other is consumed and dropped (reason control). It
    passes withholding, is never a suspect and is exempt from the poison check, as `kernel.drain` is. At a marker the
    worker's refusal path consumes it as `RetiredCreation` and, for the outstanding control, composes the next page
    in the same commit, as it composes the parent's cascade. It never waits behind a `Cohort` mark: a cascade starts
    only at a row that has ended, at a marker, or under a discard, expiry, terminate or delete drain, whose Open
    cleared any mark; a model end clears the mark in its own commit, and so does a terminate's commit at a running
    row, which ends the model.
  - **The idempotent cancel** (R5-B1). A job entry names the ticket whose key derives from (instance id, creation,
    job key, `JobGeneration`). The owner decides the ticket's presence under its own fence: a job ticket of instance
    A lives in A's partition inbox, which only A's current owner consumes, so what the owner reads in its memory, or
    by a point read in the store under the same epoch, is stable until its commit; a ticket that an earlier turn of
    the same flush consumes counts as absent. A pending ticket is consumed, counted 1, and the in-process cancel is
    sent as built (`WorkflowInstance.cs` 200, `Worker.cs` 1642–1648). A `Done` ticket that a Park keeps has its
    `KeptBy` cleared with the ordinary window, by its full key with the count checked, as a drain page clears a kept
    row. A `Done` ticket with no `KeptBy`, or an absent one, gets nothing. So the commit counts exactly the inputs it
    consumes: the parent's terminate or the `kernel.cascade` (1), each pending ticket (1 each), and nothing for an
    absent one. The marker's revision fence and the partition fence are unchanged. Existing prototype behaviour: the
    built commit puts every cancelled ticket among the consumed keys (`Worker.cs` 1450–1455, at 1452), and the store
    refuses the whole batch when one of them is missing or done (`InMemoryStore.cs` 116–117, "consumed message … is
    not there"), so a single settled ticket rolls back every child's terminate in the same commit. A model's own
    `CancelJob` uses the same cancel, with one addition for a job whose result row is pending ("An ordinary cancel of
    a converted job" under [Recover](#recover), sixth review R6-M5).
  - **Arrival orders.** A second delivery of the parent's terminate is the same row by its wire id, so it is consumed
    once, as built. A takeover between the ticket's settle and the parent's terminate is covered by the presence
    read, which the new owner makes under its own epoch.
  - **Turns and the send cap.** The cap on sends per turn applies to every turn unchanged, model turns and
    actor-type turns alike: a turn above it fails and parks ([fairness](fairness-and-backpressure.md#a-bound-on-inbox-growth),
    decision 3). A cascade page is a kernel commit, never a turn, bounded by `P_cascade` entries plus one
    `kernel.cascade`; the ending turn that carries the first page stays within the cap, since its sends are that page
    and the parent's one notice. The owner's card stands as it is.
  - **The ending commit always fits** (proposed, sixth review R6-B1, seventh review R7-B1). A model's terminal state can be larger than
    its running state. Existing prototype behaviour: the live terminate runs the model's `TerminateMetadata` and
    writes the result (`WorkflowInstance.cs` 183–190), and Native's closes every open invocation inbox
    (`NativeModel.cs` 171–178, `InvocationLifetime.cs` 29–45), which `RegisterInbox` admits in any number
    (`InvocationInbox.cs` 162–171). So each model exposes a pure layer 3 hook beside `TerminateMetadata`,
    `TerminalGrowth(state)`: an upper bound, in bytes of its own state encoding, on how much its terminate and end
    paths grow that state. A model with no terminal growth returns 0. Native's counts each open inbox at its
    worst-case closing cost (its `Closed` field and the receipts it finalizes). Executed in this repair
    (`experiments/Review6.Blockers`, #4798): `nt1-growth` measures it with the real `TerminateMetadata` and end-path
    close at 7 bytes per open inbox and a conservative 23 per non-final receipt (the receipt's gross growth before
    the close clears its pending input; its net growth is negative), with frames at 0. SQL Server's UTF-16 doubles
    these (derived), and each provider's stored encoding is qualified at K4. The envelope check of every commit of a live row counts
    the projected terminal row (seventh review R7-B1): the state, plus `TerminalGrowth(state)`, plus the registries
    at the marker's per-entry sizes, plus every other variable-length field of the row at its encoded size as the
    end path writes it, plus a fixed reserve of 16,384 bytes, must fit the envelope ([data
    lifecycle](data-lifecycle.md#proposed-the-qualified-delete), "The bound, and when it clears"). The variable
    fields beside `State`, `Jobs` and `Children` are `CustomHandles`, the model entries of `CommitTimes`, `Model`
    and `Definition`, each counted once ([storage model](storage-model.md#physical-records), `CommitTimes` under "Optional commit-time
    facts"). The fixed reserve covers only the bounded fields: identity, status, revision, `StateVersion`,
    `Suspended`, `History`, `Parent`, `Fault`, whose bound is checked under the sixth-round C2 condition (K3),
    the creation header fields (`Creation`, `ParentCreation`,
    `StartIntent` with its 67-byte digest, and `PendingStart`), the live row's `Cascade` header and control key,
    and the clock's kernel-written terminal entry. Under the [owner's K2 header decision](decisions.md#owner-decision-on-the-terminal-header-21-november),
    the live actor envelope also reserves the provider's encoded size for a full 16 KiB of UTF-8 opaque
    `TypeData` and four labels with names and values up to 1,024 UTF-8 bytes each. A terminal write counts
    the header it actually writes. The actor type's growth hook still covers state alone. Provider encoding
    and upgrade headroom are qualified at K4 and K6. Existing prototype behaviour: a detached start leaves its creator handle in `CustomHandles` without a registry entry
    (`Turn.cs` 53–61, `Turns.cs` 364–374), remote consumption need not prune it (`Turns.cs` 490–498), and the live
    terminate keeps the field as it is and adds no handle (`WorkflowInstance.cs` 179–201), so the end path's own
    growth stays in `TerminalGrowth` and the fixed reserve. A turn whose result would exceed the envelope fails and
    parks, as any over-envelope turn does. So the live terminate's ending commit and a protected `ThenDelete`'s
    live-row write always fit, and the first page's child terminates go out in that commit. The forensic state and
    the parent's one notice are unchanged. Each provider's stored encoding of the row (UTF-16 on SQL Server, BSON on
    MongoDB) is a condition, qualified at K4, with upgrades at K6 (gate 2 condition [C1](../program-goals/clean-slate-kernel.md#gate-2-conditions)). Cases TG-1, TG-2 and PC-5.
  - **Completion.** The parent's one final notice is unchanged: the live terminate's own turn sends it
    (`WorkflowInstance.cs` 201), a drain sends it at Complete, and a marker sends nothing. Each child cascades through
    its own terminate. A child's Cancelled notice meets a registry that no longer lists it and is refused as
    `UnknownChildCreation`, as built (`WorkflowInstance.cs` 48–50). A further terminate at a row whose obligation is
    `Discharging` changes nothing: at an ended row the registries are empty, so the built path consumes it (78–90);
    at a row with a drain open it is refused as "resolution open" with nothing left to cascade; and at a marker the
    list is already `Discharging`, so the parent-bound terminate gets the plain refusal.
  - **Terminate and delete in one commit.** A terminate with `ThenDelete` on a running instance ends and retires the
    row in one commit ([data lifecycle](data-lifecycle.md#proposed-the-qualified-delete), "Terminate, then delete").
    On an ended run that still has a registry it cascades as well, since a terminate at such a row goes to the
    control path and terminates its children as built (existing prototype behaviour, `WorkflowInstance.cs` 63–64, "a
    terminate still goes on to the children of a finished instance"), and PB-1 and PB-2 rely on it; on an ended run
    with an empty registry and no outstanding job it is the plain qualified delete, checks 1, 2 and 5 and the deletion. With more than
    `P_cascade` entries, the commit retires the row with a `Discharging` obligation whose list equals the previous
    row's child registry and jobs, sends page 1 and records the cursor after it (at most `P_cascade` entries) and the
    outstanding `kernel.cascade`; the pages go on at the marker. A row that `ProtectsDeletion` protects is terminated
    and kept, and its live row carries the same `Discharging` obligation. Validate admits that marker shape only in a
    commit that starts a terminate's cascade, at a running row or at an ended row with a registry or an outstanding job (case PC-5).
  - **Delete while Discharging.** A delete of an ended instance whose obligation is `Discharging` keeps it on the
    marker, still `Discharging`, with its cursor, `Control` and `Serial`, so the next `kernel.cascade` meets the
    marker and the pages continue there with no parent terminate. This holds for a root too: a root's marker keeps
    a `Discharging` obligation, and it never has a `Held` one, since no terminate can pass the parent binding
    there. While a retired creation's cascade is still `Discharging`, a successor start at its id is refused, retryably,
    until the last page clears the list, at most ⌈n / `P_cascade`⌉ page turns after the delete. In valid operation only
    a keyed root meets it, since a successor at a generated child id cannot be produced (DT-9), and key admission
    decides it before it writes a receipt: it answers `in-progress` with reason `ObligationOutstanding`, writes nothing,
    and the caller retries ([tenancy](tenancy.md#names-one-namespace-per-tenant), "A start while the predecessor's
    cascade still pages"). The creation protocol's `ObligationOutstanding` stays as a defence in depth below ingress.
  - **Crash and takeover.** The cursor, the list, `Control` and `Serial` commit with each page, and the outstanding
    `kernel.cascade` is a pending row, so a new owner polls it and continues from the cursor. A page whose commit
    outcome is unknown is reset and recomputed from the committed row, as every commit is: if it landed, the cursor
    has moved on; if not, the same revision and serial give the same ids and key.
  - **The check.** Termination is feasible for every admitted registry: each page is bounded by `P_cascade` and moves
    the cursor by at least one entry, so a list of n entries ends in ⌈n / `P_cascade`⌉ page commits. The review's
    10,001 children end in 11.
- **Which path each fan-out takes.** A combiner's FailFast is the application send above. Release 1's parallel
  ForEach runs its bodies as frames inside one instance ([large fan-out](large-fan-out.md)), so its FailFast cancels
  frames in the model and sends no kernel terminate; a body's child workflow is cancelled through its parent's
  terminate of that child, the same parent-bound send. An accepted one opens the terminate drain: the child resolves as
  Cancelled, and its Complete sends the parent the one final notice every terminate resolution sends, and no other; a
  parent that has already ended consumes and drops it.
- **Internal controls are minted by the kernel and bound to what it armed.** `kernel.drain` and `kernel.park-expiry`
  are **private controls**. Their payload carries the creation and the Park mark, which the parked-work read model
  shows, and a small generation, so the payload alone grants nothing. Six rules, all proposed, give them an issuer
  and an outstanding instance. `kernel.cascade` (fifth review R5-B2) is a third private control under the same
  domain, issuer and consumer rules; its outstanding key is `Cascade.Control` on the row, in place of a `ParkRecord`
  field ("A termination cascade is paged and idempotent" above):
  - **The kinds are reserved.** Every kind under `kernel.` other than the public commands (`kernel.suspend`,
    `kernel.resume`, `kernel.terminate`, `kernel.delete`, `kernel.recover`, `kernel.discard`, and K2's `kernel.migrate`)
    is reserved for layer 2.
    `Kinds.RequireApplicationSend` refuses a reserved kind with a definite error. The prototype applies this prefix
    rule to a future private kind as soon as someone tries to send it.
    The public list grows only by an explicit amendment of this rule. `kernel.archive`, the outside command
    proposed after release 1 ([data lifecycle](data-lifecycle.md#proposed-archiving-after-release-1)), joins it when
    it is built, and until then the prefix rule refuses it. The producers outside layer 2, and how each meets the rule:
    - **Calls that already run the check** (existing call sites): the stores' public add (`InMemoryStore.cs`
      227–232, `EfStore.cs` 593, `MongoStore.cs` 390), the worker's in-memory posts (`Worker.cs` 546 and 561), a
      model's `ITurn.Send` through `DurableTurn.Send`, and an actor type's `SendFromReceivedAddress`, `Add` and
      `AddTerminalNotice` (`Turns.cs` 313–361). An outside add is answered with the error and writes no row; a turn
      that calls one of these throws at the call, so the sender's own turn fails by the ordinary fault rule and its
      commit carries nothing to the target.
    - **Key admission**, `IKeyAdmission.AcceptAsync`, calls `RequireApplicationSend` from `KeyRequest.Validate`.
      This applies the same prefix rule before it changes a binding, receipt or delivery. The bound key ingress
      makes the same check for inputs to an existing actor.
    - **Direct outcome composition.** An actor type can build `TurnOutcome.Sent` itself, with no call that could
      throw: existing prototype behaviour, `Turns.cs` 208, and the tree terminate is built that way
      (`WorkflowInstance.cs` 196). The commit-side check below refuses such a message as a definite error, so the
      commit lands nothing and the sending turn fails by the commit's failure rule.

    **Prototype evidence.** `--private-kinds-check` runs the prefix checks for store adds, local posts, both key ingress
    paths and turn sends. It also verifies the public commands, a trusted job ticket, and the refusal of a copied
    ticket submitted through a directly composed outcome. Issuer authority belongs to the final emitted envelope.
    P-4's persisted control identity, outstanding-key and delayed-control cases still need the private controls themselves.
  - **They live in a protected identity domain.** Layer 2 writes a private control only in the commit that writes the
    `ParkRecord` field naming it, with the instance itself as `Sender` and `SenderCreation`, its own creation as
    `TargetCreation` when positive and none otherwise, as the built self-send sets it (`Turns.cs` 335), and no `Start`. So the built creation protocol
    checks every private control too: the worker runs `CreationError`, then `CreationProtocol.TargetError`, on every
    delivery, controls included, and refuses a differing creation as `WrongCreation` and a missing one on a
    creation-qualified row as `IdentityRequired` (existing prototype behaviour, `Worker.cs` 1051–1055 and 1076–1081,
    `Creation.cs` 103–109). Its
    identity domain is the tuple (control scope, `Match` null, private kind), with the control scope proposed as
    2^62. No provider allocates that value. Existing prototype behaviour, per range:
    - 0 is the ordinary scope, and outside entry points reset a supplied scope to it (`AsOrdinary`, `Messages.cs` 49,
      applied at `InMemoryStore.cs` 231); −1 is the issued scope (`Turns.cs` 327–330).
    - Every scope at or below −2 is an event slot. Every provider's event envelope starts at −2 (`Store.cs` 214), and
      the EF event insert gives each further match of one wire id the next lower scope, `MIN(InboxScope ≤ −2) − 1`, so
      −3, −4 and below are reachable without bound (`EventInsert.cs` 25, 49–50, 76–77 and 97; read back at
      `EfStore.cs` 582–585; conformance `Semantics.cs` 705). Event slots always carry a non-null `Match`.
    - Tickets take the origin's length plus one, below 2^31 (`Messages.cs` 31); custom ids take `int.MaxValue + 2`
      plus the actor's length, below 2^32 + 1 (`Messages.cs` 30); child creators take `long.MaxValue`
      (`Messages.cs` 27). A .NET string is shorter than 2^31 units, which bounds both lengths.
    - On PostgreSQL and SQLite, an issued key whose id holds a NUL or an unpaired UTF-16 unit is stored base64-encoded
      under an escaped scope: 2^33 plus its scope, and `long.MaxValue − 1` for an escaped child creator. The read path
      decodes every scope in [2^33 − 1, 2^33 + 2^32] and `long.MaxValue − 1` (`IssuedMailKey.cs` 10–23;
      [storage model](storage-model.md#inbox-identity-and-public-names)).

    2^62 is above every one of these ranges and below `long.MaxValue − 1`. The control scope is positive, so it counts
    as an issued origin (`Messages.cs` 28). A control id derives from the instance's actor id (below). Admission
    refuses an unpaired unit in a caller-chosen actor id on every provider and a NUL on PostgreSQL ("Where names
    enter" below), and the kernel's generated actor ids, such as `g{n}/child/{issuer}.{epoch}.{ordinal}`
    (`Addresses.cs` 17), hold neither. So the only admitted control id that takes the escape is one whose actor id
    holds a NUL on SQLite. The mapping below covers the escape on both providers, as a defence for a row written below
    admission. Proposed: the escape maps the control scope to its own escaped value, 2^62 + 1, as it
    maps the child creator to `long.MaxValue − 1`, and the read path maps 2^62 + 1 back to 2^62. Without that rule
    the existing escape would store the control at 2^33 + 2^62, which the read path does not decode. A positive
    value also leaves the event slot allocation unchanged, since that allocation takes the minimum, and PostgreSQL's
    form, `least(-1, min(...)) - 1` over every row of the wire id (`EventInsert.cs` 25), would overflow near
    `long.MinValue`. A row matches the domain only with all three parts, so even a scope collision could not
    make an event or ticket a control. No turn method mints a private control, and the commit refuses, as a
    definite error, any message of a private control kind or in the control scope that layer 2's resolution step did not mint in that commit. Job and event
    tickets keep their own minting turn methods and scopes (`StartJob`, `Subscribe`, `Publish`, `Turns.cs` 394–487).
  - **Each control has one outstanding identity.** `ParkRecord.ControlSerial` grows by one at every private control
    layer 2 mints for this Park. A control's id derives from the instance's origin (actor and creation), the Park mark,
    the generation and that serial, so successive pages and every delayed gate retry within one generation have
    distinct full keys, and the store's deduplication by full key never merges two of them. A page commit retaken
    after an unknown outcome reads the serial from the committed `ParkRecord`, so it mints the same key it would have
    minted. `ParkRecord.Resolution.Drain` holds the full key of the one outstanding `kernel.drain`: every commit that
    sends a drain (Open, a page, a gate retry) consumes the previous one and records the new key. `ParkRecord.Expiry`
    holds `At`, the deadline the Park's commit computed (the Park's time plus the window; null for the null window,
    and then null for the whole Park), and `Armed`, the full key of the armed `kernel.park-expiry`, if any. Park's
    commit arms one when `At` is set. Every Open recalls it and clears `Armed`. A re-park arms a fresh one, due at
    `At` (at once if it has passed) under the current generation, only when `At` is set and nothing is armed;
    otherwise it keeps the armed one.
  - **The consumer checks the issuer and the outstanding key first.** A private control acts only if it is in the
    control identity domain (control scope, no `Match`, a private kind), its `Sender` and `SenderCreation` are the instance's own id and creation, and its full key equals
    the one `ParkRecord` records as outstanding: `Resolution.Drain` for a drain, `Expiry.Armed` for an expiry. A
    `kernel.park-expiry` also needs `Expiry.At` set and the turn's database time at or after it. Then the payload
    checks of the second review: a `kernel.drain` acts only if its creation equals the row's, its Park mark equals
    `ParkRecord`'s, and its generation equals the open Resolution's; a `kernel.park-expiry` acts only if its creation
    and Park mark match, no resolution is open, and its generation equals `ParkRecord.ResolutionGeneration`, which
    grows by one at every Open of this creation. Both compare the Park mark with `ParkRecord`'s Park identity, never
    the row's terminal mark, which a recover drain clears. A control that fails any check is consumed and dropped
    with no other change and counts as refused (reason control). This extends a built check: the worker already
    discards a job or event ticket kind that arrives in the ordinary scope as a supplied ticket (existing prototype
    behaviour, `Turns.cs` 110–111, used at `Worker.cs` 640–654). One exception keeps a genuine expiry from being
    lost: the armed expiry itself, taken before `At`, is dropped and re-armed at `At` under the next serial in the
    same commit, which records the new key in `Expiry.Armed`. Only a defect or a clock fault can deliver it early,
    since the worker offers a turn only due rows (existing prototype behaviour, `Worker.cs` 830, `Eligible`).
  - **The null window never discards.** With no window, `Expiry.At` is null for the whole Park, so no
    `kernel.park-expiry`, minted or injected, can open a discard.
  - **The generation lives as long as the creation.** `ParkRecord.ResolutionGeneration` is monotonic for the
    creation's lifetime: it grows by one at every Open of any Park of this creation, and it never resets. When a Park
    resolves, its commit clears the Park's own fields (the Park identity, `Resolution`, `Dispositions`, `Expiry` and
    `ControlSerial`) and keeps `ResolutionGeneration` on the instance row. A later Park of the same creation
    continues from it, so a generation value names at most one Open of the creation. A private control left over
    from an earlier Park therefore matches nothing in a later one. A restored row's `ReleasedBy` tag (creation and
    generation) from an earlier Park never equals a later resolution's (creation, generation), so it never makes a
    row deliverable during that resolution; such a row, if no turn consumed it, is a `Stale` row of the later Park
    (a lower generation of this creation, `NOT Done`): withheld while that Park's resolution is open, restored by a
    recover's page through the `Stale` arm, and consumed and dropped at Complete by any other kind. A new creation under the same id is a new row identity and starts again, and the creation
    in every control key and tag keeps its values apart. This claim covers the private controls and `ReleasedBy`
    only; a restarted job's ticket generation is its own counter, defined under "Retained dispositions"
    ([Recover](#recover)).

  Every Open bumps the generation and clears `Expiry.Armed`, so an expiry already delivered or in flight when a
  recover's Open recalled it is dropped, and it can never open a discard against an active recovery. The controls of
  a later resolution, a later Park or a successor creation never share a full key with a delayed one, since the
  generation, the Park mark or the origin differs.
- **Operators read the identity** from the parked-work read model (`ParkedItem` carries the instance id, creation,
  Park revision and Park time) and from `KeptAsync`, whose pages carry the `ParkSelection` they were read under,
  `Instance` included.
- **Release 1 has no unqualified intent** such as "the current Park of key K". A command always names the Park the
  operator saw.

### Resolving a Park: a bounded drain

An indefinitely parked instance can keep any number of rows, so no resolution touches them in one transaction. Each
resolution opens in one commit and then drains the kept rows in pages, one page per turn of the instance, under a
durable cursor. This is the bounded-recovery option the review offers; it was chosen over an enforced cap with an
overflow path because a cap would either refuse internal arrivals, which the store must never do
([fairness](fairness-and-backpressure.md#a-bound-on-inbox-growth)), or leave them pending, which changes the owner's
Park decision.

| Step | What its commit does |
|---|---|
| **Open** (the `kernel.recover`, `kernel.discard` or `kernel.terminate` turn, the delete of a parked instance, or the expiry timer's turn) | Checks the operation (below); recalls the expiry timer; increments `ParkRecord.ResolutionGeneration` and writes `ParkRecord.Resolution {operation, kind, mode, generation, cursor}` on the instance row, with the cursor before the first row; for a `retry` with an explicit selection, marks each selected row `KeptBy = Selected` by its full key, with its count checked; sends the instance a `kernel.drain` bound to the generation. For recover it also restores the waits (below) and sets the instance running, so restored rows are delivered as the drain goes. From this commit on, arrivals are no longer kept, and while a recover drain is open the instance receives only control kinds and the rows this resolution released (below). |
| **Page** (each `kernel.drain` turn) | Reads the next page of metadata: `Target = :instance AND (KeptBy IS NOT NULL OR Stale) AND (Seq, Id, Scope, Match) > :cursor`, ordered by that tuple, where `Stale` selects a row an earlier resolution of this creation released and nobody has consumed (`ReleasedBy` of this creation and a lower generation, `NOT Done`; see "Interruption" below), at most `P_page` rows (below), never a payload. Updates each row by its full key, with its count checked (restore, which also marks the row released by this resolution, or clear, below), moves the cursor to the page's last tuple, and sends the next `kernel.drain`. |
| **Complete** (the page that finds no row) | Clears `ParkRecord.Resolution`, journals the outcome with its counts, and does the kind's last step: recover ends the withholding of the rows it did not release, and the rows it released that are still unconsumed keep their delivery precedence under the `Cohort` mark it sets ("Release cohort precedence" below); discard and expiry record the disposition ("discarded" or "expired"), leave the instance Faulted and write its terminal mark; terminate sets the end status Cancelled; delete finishes the built delete. Discard, expiry, terminate and delete each send the parent one final notice (Faulted, or Cancelled for terminate), since a parent keeps waiting for a parked child ([large fan-out](large-fan-out.md), decision 5). |

- **The cursor is a tuple.** `seq` alone is not a total order: MongoDB can give two rows one `seq` from its separate
  allocation clocks and sorts by id as well ([storage model](storage-model.md#invariants-carried-by-the-records)).
  A cursor on `seq` alone would skip the rest of a tie at a page boundary, and those rows would stay kept for good,
  since the sweep skips kept rows. So the cursor and the page order use `(Seq, Id, Scope, Match)`, the store's poll
  order with the full key as its tie-break. The cursor orders the paging only. It never decides which rows the
  instance may receive (below).

- **Restore or clear, per row.** Both recover modes restore `Arrived` rows; `retry` also restores the rows its
  selection marked `Selected` at Open (or every `Consumed` row, for retry-all). Restoring sets `Done = false`, `KeptBy = null`, `Attempts = 0`,
  `Trial` to none, `Seen = 0`, `KeepUntil` to the ordinary outside-command window from now, and `ReleasedBy` to the
  open Resolution's `(creation, generation)`, by the predicate
  `Id = :id AND Scope = :scope AND Match IS NOT DISTINCT FROM :match AND Done AND KeptBy = :reason`. Every other kept
  row, and every row under discard, expiry, terminate or delete, is cleared: `KeptBy = null` and the ordinary window, by the same
  key predicate. Resetting the poison counters matters: the owner parks any delivery whose attempts reached the
  threshold, so a retried poison message or job ticket would park the instance again at once.
  - **A kept publish ticket is kernel work** (proposed, sixth review R6-M7). A publish ticket a Park consumed, such
    as one whose step failed until its publisher parked, is never cleared as an unselected `Consumed` row: every
    recover mode (plain `resume`, `retry` with or without it selected, retry-all), before or after the model ends,
    restores it with its committed cursor, and its remaining steps run, after the end as an ended publisher's
    ticket's steps do (SB-7 run 6); a delivery a step repeats is a duplicate by its digest identity. Only discard,
    expiry, terminate and delete, which end the Park without recovery, make it a dead letter, as an ended
    publisher's ticket becomes one ("When a step fails or stalls" under [Recover](#recover)), visible with its
    undelivered remainder and the replay command. A turn's dead letter keeps no event row, and its ticket carries no
    payload after its first step, so its replay delivers E once to the remaining waits while the event row exists,
    and otherwise answers "gone". A kept meet ticket is cleared in every mode, recover included: Park removed
    the subscriptions it served, and a catch-up never matches a re-created incarnation (SB-5), so restoring it would
    deliver nothing. The Open's lookup table gives the row.
- **Restored rows are delivered by the ordinary poll.** They are unseen, due rows that keep their id, sender, kind,
  payload, creations and `seq`, so an authenticated kind still passes its check: a child completion to a
  creation-qualified parent needs its terminal-notice id, `Sender` and creations (`WorkflowInstance.cs`, the
  `ChildFinished` check), and a copy under a new id would be refused by `CreationProtocol.Refuse`
  ([Creation.cs](../../prototypes/kernel-model/src/Kernel.Durable/Creation.cs)). Nothing is handed over in memory
  ahead of the disk, so invariant 9 holds.
- **Eligibility during a recover drain: released rows only.** The instance runs from the open commit. While the
  recover Resolution is open, the owner gives the instance a row only when the row is a control kind (below) or its
  `ReleasedBy` equals the open Resolution's `(creation, generation)`. Every other row for the instance waits, withheld
  as for a suspended instance, until Complete, whatever its `seq`: rows that arrive during the drain, rows that were
  pending at the Open and not yet kept, the instance's own new sends, the tickets the open restarted, and the timers
  the open re-armed (see "The re-arm" below).
  - **Why the cursor cannot decide.** No `seq` is an arrival watermark. One sender's rows get increasing `seq`, but
    on MongoDB each sender's partition clock, and the separate outside-add clock, allocate independently
    ([storage model](storage-model.md#values-and-embedded-shape)), so a row committed during the drain can get a `seq`
    below the cursor, or the cursor's `seq` with a lower tie-break. On the engines that generate `seq`, a transaction
    can draw its value before a page reads and become visible after the cursor has passed it. A test on the tuple
    would let such a row through, and if it ended the model, the pages after the end would clear the kept rows
    the cursor had not reached, which were never delivered.
  - **The marker is durable and generation-bound.** The page sets `ReleasedBy` in the same update that restores the
    row, so it commits with the cursor; a new owner reads it from the polled row and the generation from the instance
    row, and an ownership change neither releases nor withholds a row. The generation is monotonic for the creation's
    lifetime and kept when a Park resolves ([the internal controls' rules](#every-park-operation-names-its-park)),
    so a row released by an earlier resolution, of this Park or an earlier one, matches no later one, and the creation
    keeps a row of an earlier creation of the same id from matching. A stale marker on a consumed row stays and
    matches nothing; an unconsumed one rejoins the Park ("Interruption" below).
  - **Order.** Restored rows keep their `seq`, and the poll delivers released rows in that order, so no message from
    one sender overtakes an earlier one from the same sender: a sender's kept rows are released during the drain, and
    its later rows only after Complete. A timer due at once that the open re-armed fires after the drain.
- **Release cohort precedence** (proposed, fourth review R4-B1). Withholding ends at Complete, and Complete does not
  wait for the model to consume the last released rows, so the order must outlive the drain:
  - **The cohort.** A row whose `ReleasedBy` carries the latest resolution generation of its creation
    (`ParkRecord.ResolutionGeneration`, kept on the instance row) and which is not `Done` is a cohort row. While a
    recover drain is open the cohort is the set of rows it has released and nobody has consumed; after its Complete it
    is what remains of that set. A ticket never counts as a cohort row, for precedence or for the mark: a restored
    `kernel.job` row comes back as a ticket that the owner starts as a job (`Worker.cs` 654–658 and 677), and the owner
    never offers a job, meet or publish ticket to the model (`Worker.cs` 830), so such a row has nothing to deliver
    and stays not `Done` for the job's whole runtime. The job's result reaches the model in memory, or, when the
    instance cannot take it, as a new, untagged result row that the owner's fenced conversion writes ("A completed
    job's result is a fenced, authenticated row" under [Recover](#recover), fifth review R5-M4). So a terminate
    reaching the instance cancels the job as built (`WorkflowInstance.cs` 198), and a suspend applies, whatever the
    job is doing (fourth review R4-B1, case E-11). The exclusion covers the ticket kinds only, rows the owner runs as
    work. A job result is input to the model: a kept `Arrived` job result, and any result a restore delivers without
    running the job again, stays an ordinary cohort row, with its precedence and its place in the mark's read.
  - **The order.** A cohort row keeps delivery precedence over every non-control row of the instance, during the drain
    and after Complete, until it is consumed; consuming a row clears its precedence. While the recover drain is open,
    the owner offers control kinds first, then cohort rows in tuple order, and withholds every other row. After
    Complete, while the `Cohort` mark (below) is set, the remaining non-held cohort rows come first, in tuple order,
    and every other row waits, control kinds included, until the mark's read finds no non-held cohort row; only then
    does a control kind take its built place, alone after the rows before it. So a later terminate, a parent's tree
    terminate included, cannot overtake and drop a cohort row unless the instance is suspended ("Suspension passes the
    mark" below), even one no poll has returned yet, and a cohort row on
    trial after a takeover, or polled again after a reset, is never held by a control's recovery cut ("Cohort rows are
    exempt from the recovery boundary" below). The drain's own
    controls, `kernel.drain` and `kernel.park-expiry`, never arise after Complete. Every other row follows in tuple
    order. With no drain open and no `Cohort` mark (below), the built order
    holds: the instance's rows in `seq` order, a control kind taken alone after the rows before it (existing prototype
    behaviour, `Worker.cs` 680–686 and 825–874).
  - **The durable cohort mark.** The owner cannot tell from memory alone that no cohort row remains: after a takeover
    its poll is bounded (`PollLimit`, `Worker.cs` 14 and 516), so an untagged row A can come back in one poll and the
    cohort row R only in a later one. So the instance row carries `Cohort (creation, generation)`. A recover's Complete
    sets it to the drain's own `(creation, generation)` whenever a row it released may still be unconsumed, which is
    every time unless the owner has seen every released row consumed. While it is set, the owner offers no
    non-cohort row of the instance, control kinds included, until a read finds no offerable, non-held cohort row:
    `Target = :instance AND ReleasedBy = :cohort AND NOT Done AND Kind NOT IN (:ticketKinds)`, in tuple order, served
    by the partial index of the store changes below. It reads pages of at most `P_page` rows and stops at the first
    row whose full key is not among the held cohort keys, the cohort rows the owner holds for the model; a provider
    that can bind that set may pass it as `AND (Id, Scope, Match) NOT IN (:heldCohort)` and read one row. With h held
    cohort rows, one offer costs at most ⌈(h + 1) / `P_page`⌉ reads, one read whenever h < `P_page`. Rows the model
    holds count as passed over through that read only: when it finds no offerable, non-held cohort row, the next row
    is offered with the held ones handed back. A read that returned only the lowest
    cohort row would let a model-ending row through behind a held R1 while an unpolled R2 remains. The Open of a later
    resolution clears the mark, since its rows become `Stale`; a model end clears it in the end's commit.
  - **Clearing the mark.** The mark is cleared when a read finds no cohort row at all, held or not (case E-8 run 3), by a kernel write
    of the instance row inside the owner's fenced commit, at the expected revision: it raises the revision and
    journals nothing. A consuming turn that writes the row anyway may carry the clear in its own row write. Either way
    the clear names the `(creation, generation)` its read was taken under and clears only a mark equal to it. The
    commit that consumes the last cohort row need not know it was the last: the next offer's read finds none, and that
    read's commit clears the mark. The clear is safe: once a drain has completed, no row can join its `(c, g)` cohort,
    since only that drain's pages set `ReleasedBy = (c, g)` and every one of them committed before its Complete, and a
    later resolution tags its rows with a higher generation. So an empty read stays empty, and a clear fenced at the
    expected revision never clears a later `(c, g + 1)` mark: the later Open and Complete raise the revision, so a
    delayed clear fails its fence, and its comparison with `(c, g)` fails as well.
  - **Suspension passes the mark.** While the instance is suspended, its cohort rows are withheld as every non-control
    row is, and no trial runs (existing prototype behaviour, `Worker.cs` 756 and 830–831), so none of them is
    offerable: the mark's read counts no cohort row of a suspended instance, and every control kind takes its built
    place, resume, terminate and a further suspend alike. Without this, a held cohort row R1 lets a suspend apply
    after Complete; a takeover or a reset then makes R1 a non-held cohort row the suspension withholds, and resume,
    waiting under the mark for R1, would never apply while R1 waits for resume (integration review of the fourth
    round). The mark stays set through the suspension: once resume has applied, the cohort rows again precede every
    non-control row of the instance while the mark is set. A terminate that reaches a suspended instance ends it as
    built, and the rows the suspension withheld, cohort rows included, are dropped under the finished-instance rule,
    as a suspended instance's pending inputs are today. Executed in this repair: `fr1-held-suspend-crash` (#4786), case
    E-8 run 4.
  - **Trials follow the cohort order.** A row seen by a lost owner is a suspect (`Worker.cs` 577–578), and the built
    trial selection takes suspects in `seq` order, one at a time by default (`TrialGroup` 1, `Worker.cs` 35 and
    736). The lost owner may have polled and marked A even while A was withheld (`Worker.cs` 1397; S-9 relies on
    it). So while a recover drain is open or the `Cohort` mark is set, the selection takes the instance's cohort
    suspects first, in tuple order, and tries no non-cohort suspect of that instance ahead of a cohort row or in a
    group with one. A withheld suspect is passed over without putting its instance in the selection's skipped set
    (`Worker.cs` 762), so it never holds back the instance's eligible cohort rows, and it is shelved, as a suspended
    instance's suspect is (finding F12, `Worker.cs` 691–699): marked seen at this epoch, neither on trial nor cleared
    to run, so a bounded poll moves on to the cohort rows behind it.
  - **Cohort rows are exempt from the recovery boundary.** The built boundary is the cut of the earliest due control
    kind (existing prototype behaviour, `RecoveryBoundary`, `Worker.cs` 796–820, and `BeforeRecoveryControl`,
    718–719). It applies in two places: the trial selection skips a suspect beyond the cut (`Worker.cs` 751), and
    ordinary delivery offers only rows in front of it (`InFront`, `Worker.cs` 829–832 and 842). Outside a trial the
    owner computes it whenever a row of the instance is a suspect or carries `RecoveryOrdering`, which a row polled
    with an earlier `Seen` or `Trial` marker gets even when it is no suspect, after an unknown-outcome reset as after a
    crash (`Worker.cs` 617, 636 and 800; the reset at 480–502). A control K below a cohort row R would then wait for R
    under the mark while R waits behind K's cut, and neither could move, under a healthy owner as after a crash. So
    while a recover drain is open or the `Cohort` mark is set, the boundary does not apply to the instance's cohort
    rows, suspect or not, in either place: the selection tries cohort suspects first, in tuple order, ahead of any
    control's cut, and delivery offers cohort rows whatever the cut. A control takes its built place only once no
    non-held cohort row remains, as "The order" states; while the drain is open, control kinds still come first among
    the rows the owner offers outside a trial. The boundary keeps holding every non-cohort row, as built.
  - **The poison comparison follows the cohort order** (proposed, fifth review R5-M6). Existing prototype behaviour:
    an input whose attempts reached `MaxAttempts`, a job ticket included, is poison input (`PoisonInput`,
    `Worker.cs` 705–707; an exhausted `kernel.job` row is not classified as a ticket, 654–659), its due time sets the
    slot's poison boundary (709–711), and while that boundary is due a suspect gets its trial only if it precedes
    every due poison input in the slot's inbox order (`PrecedesPoison`, 714–716, applied in the trial selection at
    753–754). So
    while a recover drain is open or the `Cohort` mark is set, that comparison uses the cohort order: a non-cohort
    row, a job ticket on the poison-input path included, never precedes a cohort row there, and a cohort
    suspect gets its trial before an exhausted non-cohort row takes the poison path (the `MaxAttempts` check that parks
    the instance). The exhausted row then waits under the mark as every non-cohort row does, and takes that path once
    the mark's read finds no offerable cohort row. Between cohort rows the complete persisted tuple decides (below);
    outside the cohort context the built
    comparison holds, so a genuinely earlier poison still poisons first and the physical-job exclusion at 753–754
    (a job ticket on a slot with due poison gets no physical trial) is kept. A meet or publish ticket's own
    step-failure park, its `MaxAttempts` over steps without progress ("When a step fails or stalls" under
    [Recover](#recover)), is outside this deferral: it is no model input and never enters the poison comparison.
  - **Ties use the complete persisted tuple** (proposed, sixth review R6-M6). Whenever cohort precedence applies,
    both the trial comparison and the delivery order compare `(Seq, Id, Scope, Match)`, so two cohort rows that share
    a `seq` from different clocks still have one order (existing prototype behaviour: `PrecedesPoison` compares
    `InboxOrder` alone, the row's `seq` when its `CompletedOrder` is 0, `Worker.cs` 714–716; `Turns.cs` 88); outside the cohort context the built comparison stays. Cases TT-1, PP-1 and PP-2.
  - **How a new owner takes R up.** The new owner's poll returns R. R was marked at the lost epoch, so it is a suspect:
    the fenced trial commit puts it on trial at the new epoch, the trial delivers it alone and ahead of A, and it is
    handed off once that trial turn commits. A cohort row the lost owner never marked is ordinary: the new owner's
    flush marks it `Seen = e`, as for any polled row.
  - **Held rows never block.** A row the model holds is in the owner's memory, so the poll skips it, and the order
    passes over it: the next row is offered, and the held row is handed back with it as the built `Hold` does (FINDINGS
    section 14). A held cohort row can still receive the later input it waits for, an unreleased row included.
  - **What entitles Complete.** The page that finds no row completes when the persisted cursor has passed the last
    kept row and the progress gate admits it: the previous page's last restored row is handed off and the credit
    leaves room for a row ("The progress gate" below, fifth review R5-M3).
    Complete does not wait for every released row to be consumed. After Complete the order rests on the durable `ReleasedBy` tag of each unconsumed
    released row, the generation on the instance row and the `Cohort` mark, so it survives a takeover: a new owner
    reads all three.
  - **Interactions.** A later Park's Open bumps the generation, so undone cohort rows become `Stale`, as "Interruption"
    below defines. A model end drops the remaining cohort rows under the finished-instance rule, as for any row of a
    finished instance.
- **Controls during a drain.** Suspend and resume act as usual. `kernel.drain`, `kernel.park-expiry` and
  `kernel.cascade` are kernel control kinds: like the other control kinds they pass withholding, are never suspects,
  and are exempt from the poison check before Park (`Worker.cs`, the `MaxAttempts` check), so a drain or a cascade is
  never parked by its own messages.
- **Interruption.** A recover drain ends as interrupted when the instance, while its model still runs, parks again or
  is terminated. Its
  `ParkRecord.Resolution` is cleared in the interrupting commit, and nothing is dropped:
  - **A re-park** continues the same Park, keeps its Park mark and its retained dispositions (below), reverts
    `Selected` rows to `Consumed`, and resumes keeping. The rows of the parking turn get `Consumed`; rows the
    drain restored that were not yet delivered, and rows that arrived during the drain, are consumed while parked as
    any arrival and get `Arrived`; kept rows the drain had not reached keep their reasons. The re-park re-arms
    `kernel.park-expiry` for the window's remaining time (from the Park's time in the `ParkRecord`; due at once if it
    has passed), under the current generation, since the recover's open had recalled the old one and bumped the
    generation past it. A new recover then drains every row still kept.
  - **Released rows rejoin the Park.** Rows the interrupted drain released and nobody consumed belong to the Park
    again, by one mechanism: the parked instance consumes those it reaches as `Arrived` arrivals, and any still
    unconsumed when the next recover opens are read by that drain's pages through the `Stale` arm of the page
    predicate. A page restores such a row as an `Arrived` row, by the same update with `NOT Done AND ReleasedBy =
    :stale` in place of `Done AND KeptBy = :reason`, giving it the new `ReleasedBy`. So a released row and a later
    kept row of its sender are restored in tuple order, and neither can overtake the other: in the trace R1, R2, K of
    one sender, with R2 released and pending when an operator's recover, given a lower `seq` by MongoDB's outside-add
    clock, opens the next drain, the pages restore R2 before K. Discard, expiry, terminate and delete pages, and recover
    pages after the model's end, skip the `Stale` arm, and at Complete those rows meet the ordinary rule for the
    instance that drain leaves (consumed and dropped). **The bound:** the progress gate's handoff credit bounds the
    restored rows the owner has not handed off ("The progress gate" below); rows handed off that no turn has consumed
    (held by the model) can be more, up to every row the interrupted drain restored. All
    of them are paged with the kept rows, at most `P_page` per page, so no commit touches them all at once.
  - **A landed page refreshes the rows the owner has in memory** (proposed, fourth review R4-M4). The ordinary poll
    skips a full key the owner already holds (existing prototype behaviour, `Worker.cs` 627–628), so a `Stale` row
    the same owner had in memory before the interruption would keep its old `ReleasedBy` there after a page retags
    it: eligibility would withhold it, and its reset `Seen` would never reach the current epoch. So after a page commit
    lands, the owner refreshes from the committed values every row that commit retagged and that it has in memory
    (held, on trial, or polled and not yet delivered), and treats each as newly polled at its current epoch: its next
    flush marks the row `Seen = e`, as for every polled row (`Worker.cs` 1397), and the gate counts it from then on. A
    poll that was in flight when the commit landed and read pre-commit versions discards those keys, and the next poll
    reads the committed rows. Hold and trial semantics are unchanged, and so is the page's server-side update, with
    its predicate and its count check. After an unknown commit outcome the owner resets and re-polls, as for any
    commit; after a takeover the new owner polls the committed rows.
  - **A terminate** opens its own drain over the rows still kept, which clears them (below).
- **The model ends during a recover drain.** The instance runs from the open, so its model can finish, be cancelled or
  end faulted by its own outcome before the last page. That end does not stop the drain: `kernel.drain` is a control
  kind and keeps its turns after the end. From the end on, each page clears the rows it reaches (`KeptBy = null` and
  the ordinary window), restores none of the model's rows, since a finished instance would drop them, and skips the
  `Stale` arm. A kept publish ticket it reaches is restored with its committed cursor, as before the end, and its
  remaining steps run as an ended publisher's ticket's steps do (SB-7 run 6); a kept meet ticket is cleared ("A kept
  publish ticket is kernel work" above, sixth review R6-M7). Rows released before the end
  stay eligible and meet the ordinary rule for a finished instance (consumed and dropped). When a terminal page
  settles a kept, released or Open-created job ticket, or an authenticated result, it also removes that exact generation's
  registry entry. A ticket's still-pending issued deadline is settled in the same page unless an earlier terminal
  page has already settled it. The metadata and row limit count both writes. At Complete the withholding
  ends, so the unreleased rows meet the same rule, and
  `ParkRecord.Resolution` is cleared, so the Park is resolved and no row keeps `KeptBy`. The end itself is the built
  one: its own turn writes the end status and terminal mark and sends the parent its one notice, so Complete sends
  none. A turn that throws before the end is a re-park, handled under "Interruption" above.
  - **After the end, nothing reopens the Park.** The end's turn records the model's end in the open
    `ParkRecord.Resolution`, in the same commit as the built end, since `ParkRecord` is on the instance row. The
    kernel needs a discriminator, because the built `WorkflowInstance.Park` treats every status other than Running as
    ended, and a parked row and a model that ended Faulted share a status. For a delivery that reaches `MaxAttempts`:
    exactly one of these states holds, and each has its own outcome:

    | State of the row | A delivery that reaches `MaxAttempts` |
    |---|---|
    | A recover drain is open and its Resolution records no model end | Parks the instance and interrupts the drain (above) |
    | A recover drain is open and its Resolution records the model's end | A failed late input: the row records the failure and keeps its status, the drain continues, no Park or keeping resumes |
    | A discard, expiry, terminate or delete drain is open | The same failed late input |
    | No resolution is open and the Park is unresolved | A re-park, which continues the Park |
    | The row has ended and has no unresolved Park (after a completed discard or expiry, a recovered instance that later ended, one that never parked) | A failed late input, the built behaviour; it never opens a Park or keeps rows |
  - **A delete after the end takes the drain over** when its selection names the mark the end wrote, as it does for
    discard, expiry and terminate ([Delete during a drain](#delete-during-a-drain)). Without
    that, the trace is: the model finishes, rows above the cursor still carry `KeptBy`, a delete removes the ended row,
    the next `kernel.drain` finds no target and is dropped, and the remaining rows keep `KeptBy` for good, since the
    sweep skips them; a later creation under the same id could then meet them. With the takeover, the drain runs on
    from its cursor, and its Complete runs the built delete. The end's own turn already sent the parent its notice,
    so the delete sends none.
- **Terminate as a resolution.** A terminate of a parked instance, or one that interrupts a recover drain, splits the
  built terminate's one turn in two:
  - **Its open** does everything the built terminate does except the end: it stops delivery to the model at once,
    removes the instance's waits, cancels its jobs and sends terminate to its children, as the paged, idempotent
    cascade's first page and the pages after it ("A termination cascade is paged and idempotent" above). No model turn
    runs after the terminate is accepted. Pending rows, restored ones included, are withheld from then on: the terminate's Open is a
    new generation, and its pages release nothing.
  - **No job is entered while a terminate drain is open** (proposed, sixth review R6-M8). The row can stay Running
    until Complete, and a job's physical entry is decided apart from turn selection. Existing prototype behaviour:
    every entry gate reads `Withholds` (`WorkflowInstance.cs` 28–29), true only for a suspended row or a newer state
    version: `StartJob` (`Worker.cs` 1166–1180), the driver's entry under the slot lock (1224–1234) and the
    restriction a withholding commit holds while in flight (1130–1147). The rule reuses withholding: while a terminate
    Resolution is open, `Withholds` is true, so the row withholds every pending input and every physical job entry,
    new and scheduled entries alike, after a fresh load and during the Open's commit, and a failed Open rolls back
    as the built restriction does. A driver that entered before the Open keeps the ordinary cancellation semantics.
    Discard, expiry and delete drains open only at a parked or ended row, which already stands jobs down. Case WJ-1.
  - **Its Complete** records the end status Cancelled, so the pending rows meet the ordinary rule for a finished
    instance (consumed and dropped, as for any terminated instance), and sends the parent one final notice.
  - **Between the two** the row keeps its current status, with the terminate's `ParkRecord.Resolution` open: Faulted
    for a parked instance, Running for one whose recover's open had set it running. Status lists and read models show
    that status with the sub-state "terminating", taken from that Resolution, and the parked-work read model shows the
    terminate's operation as in progress. The built
    terminate sends a notice only when the child is running (`WorkflowInstance.cs`, the terminate path); this one sends
    none at its open, so the parent hears once.
  - **The row's terminal mark depends on where the terminate came from.** A terminate of a parked instance with no resolution open leaves the
    Park's mark on the row, since its Open writes no mark. A terminate that interrupts a running recover finds no mark,
    since the recover's Open cleared it. In both cases Complete writes the Cancelled mark, and from then on the row
    carries that mark.
  - **`ThenDelete` is part of the terminate.** Its Open records `ThenDelete` in the terminate's Resolution, and its
    Complete does the deletion as the last step of the same commit ([Delete during a drain](#delete-during-a-drain)).
  - **A further terminate while a terminate drain is open**, from an operator, the parent or a combiner, changes
    nothing, since the drain's Open already handed the subtree to the cascade, and is refused as "resolution open", as during a discard, expiry or delete drain
    ([the rule](#every-park-operation-names-its-park)). Its `ThenDelete`, if any, is not added to the open drain.
- **Delete during a drain.** A delete takes a drain over only when its qualified checks pass against the row's
  current terminal mark ([Delete during a drain](#delete-during-a-drain), below).
- **What a page reads, and its metadata bound.** A page reads and writes metadata only: each row's full key (`Id`,
  `Scope`, `Match`), `Seq`, `Kind`, `Sender`, `KeptBy`, `ReleasedBy` and the payload's size. Restoring and clearing
  set flags by full key and never carry the payload. `B` counts the encoded metadata, so a row's payload size plays
  no part in its page. Every projected field is bounded at admission (proposed limits), so a row's encoded metadata
  has a maximum `M` strictly below `B`.
  - **The unit.** Lengths are WTF-8 bytes: UTF-8, with NUL as one byte and each unpaired UTF-16 unit as three bytes,
    so every string the kernel accepts has a length (`"high\uD800"` is 7). `N` applies to the caller's or model's
    own name, before tenant qualification and the virtual-partition prefix; `Q` adds at most 65 bytes and `{vp}/` at
    most 5 ([tenancy](tenancy.md#names-one-namespace-per-tenant)), and the derived limits include them.

  | Field | Limit |
  |---|---|
  | A name an application or model supplies: an actor id (instance id or caller key), an ordinary wire id, an operation id, a job key, a wait id, a subscription key, an event id | `N` = 1,024 bytes each, before qualification |
  | `Kind` | 256 bytes |
  | `Id` of an issued message or a ticket (qualified origin, key, creation, generation and ordinal) | At most 2`N` + 160, from bounded parts |
  | `Id` of an event delivery | 67 bytes: `ev:` and a 64-digit digest ("The event match is a fixed digest", below) |
  | `Match` | 64 bytes: a digest (below) |
  | `Sender` (a qualified actor id, or an address the instance received) | At most `N` + 160 |
  | `Scope`, `Seq`, `KeptBy`, `ReleasedBy`, payload size | Fixed width, 64 bytes together |

  The variable fields come to at most 3`N` + 640 = 3,712 bytes. `M` is set by the widest provider encoding: SQL
  Server's UTF-16 at most doubles a length, to 7,424, and the reversible binary path of PostgreSQL and SQLite for
  names with NUL or unpaired units ([storage model](storage-model.md#inbox-identity-and-public-names)) stores UTF-16
  bytes as base64, at most 8/3 of a length, 9,899. Admission confines that path to issued ids, whose chosen parts
  (a job key, a custom id) may hold such units (below), so 9,899, taken over every variable field, over-estimates,
  and `M` stays a safe bound. With the fixed 64 bytes and an allowance for the provider's own framing (BSON names
  and type bytes, row headers), `M` = 24 KiB, kept at its earlier value, so `P_page` is unchanged. The limits are checked where a row is first
  written, at every ingress class, before any write:
  - **An outside add** (a command, a start, a publish, an operator message): the entry point refuses it with a
    validation error, and no row exists.
  - **A turn's sends** (`ITurn.Send`, `SendFromReceivedAddress`, `Start`, `StartJob`, `Subscribe`, `Publish`): the call
    throws a typed `MetadataLimitExceeded` before the message joins the turn's output, so the turn fails as any model
    fault does. The message is never written, and a Park that follows keeps only the turn's consumed deliveries,
    which were admitted under the same limits. A received address enters a turn inside a message that was itself
    admitted, and `SendFromReceivedAddress` checks it again as the new row's `Sender`.
  - **An actor type's composed `Add`** (a parent's notice, a tree terminate) and every kernel-composed row (tickets,
    `kernel.drain`, `kernel.park-expiry`, job results, child notices): the commit checks the same limits and refuses
    an over-limit row as a definite error. These rows are built from fields already bounded, so the check is a
    defence.

  Where a provider has a tighter limit of its own, admission applies that one, so the refusal comes before the write
  and never from the store: SQL Server's 450 UTF-16 units and MySQL's 255 scalar values
  ([storage model](storage-model.md#inbox-identity-and-public-names)), and PostgreSQL's B-tree entry limit of about
  2,704 bytes on each indexed key ([tenancy](tenancy.md#names-one-namespace-per-tenant)), and PostgreSQL's refusal
  of NUL in text (SQLSTATE 22021, `Kernel.Ef.Conformance` Program.cs), so on PostgreSQL admission refuses a NUL in
  `Kind`, `Sender`, an ordinary `Id` and `Match` (issued ids take the reversible binary path). On every provider,
  admission refuses an unpaired UTF-16 unit in an ordinary text field (`Kind`, `Sender`, an ordinary `Id`, `Match`):
  only issued ids have the reversible encoding (`IssuedMailKey.cs`), and a UTF-8 text column on PostgreSQL, MySQL or
  MongoDB would make the client throw or replace the unit with U+FFFD, merging two keys. **Where names enter
  (a proposed admission rule).** These refusals apply at the ingress of every caller- or model-chosen name that later
  becomes a `Sender`, part of a `Match` or a stored actor id: a caller key and an actor id (at a start or an outside
  add naming it), a wait id, a subscription key and an event id (at `Subscribe`, `Publish` and an outside publish),
  a message's `Target` (at every turn send, where a refusal throws as a model fault, at every composed send and at
  every outside add), and the caller-chosen name in a job-queue actor id `queue/Q(t, name)/{shard}`
  ([tenancy](tenancy.md#names-one-namespace-per-tenant)), wherever that name is configured or admitted.
  An unpaired UTF-16 unit is refused there on every provider, and a NUL wherever a provider refuses it (PostgreSQL),
  so no admitted instance meets a refusal at its own later sends or at the instance insert. Each name is checked
  in its maximum stored form: a generation or revision counts as 20 digits, an ordinal as 10,
  and a notice or ticket suffix at its fixed width, so a later restart's generation suffix, or any other suffix a
  kernel-composed row adds, can never grow an admitted name past a provider limit. Today's fixtures above `N` become refusal
  fixtures (S-6): SQL Server's `LargestInboxId`, `"0/a!"` with 446 × `界` (1,342 bytes, `Kernel.Ef.Conformance`
  Program.cs). Keyed starts are not exempt. Release 1's caller-key actor id is `{vp}/Q(t, key)`
  ([tenancy](tenancy.md#names-one-namespace-per-tenant)), the same id across every creation of the key, so the
  caller's key is part of a stored actor id and meets `N` and the unit refusals above. The keys of `UnicodeKeys.cs`
  (Recreate.Run, lines 10–12) therefore become S-6 refusal fixtures: the 4,096-unit random key (units 0–65535, above
  `N`, with unpaired units), `"high\uD800"` and `"low\uDFFF"` on every provider, and `"nul\0x"` on PostgreSQL. The
  operation ids of those requests reach the store only hashed into the wire id and base64-encoded in the receipt
  (`KeyRequest.WireId` and `EncodedOperation`, `KeyAdmission.cs` 14–17), so only `N` applies to them: the
  4,096-unit operation is refused, and the short operations holding a NUL and an unpaired unit stay accepted. Today
  key admission maps each creation of a key to a generated run id, `g{n}/key/{serial}`, so the caller's units reach
  no projected field (existing prototype behaviour, `KeyAdmission.cs` 65–66); release 1 replaces it. The wide-key
  sample in
  [WRITES.md](../../prototypes/kernel-model/WRITES.md#15-november-card4-physical-identity-experiment-on-landed-park)
  measured 11,008 encoded octets for a 4,096-unit key: one measured row, no maximum, and a key above `N`. The third
  review's SQLite probe stored a 5,242,882-byte key; under these limits such a key never reaches the store. So no row
  a page can meet exceeds `M`, and a page always has room for its first row. **Measured at K4:** each provider's
  encoded size of a row with every field at its limit in that provider's widest encoding, which must not exceed `M`.
  The doubling, the base64 factor and the framing allowance are unmeasured. A restored row's payload is read later by the ordinary poll that delivers it, under
  the poll's own limits, as for any accepted message; a cleared row's payload is never read. So a kept delivery of
  5 MiB, which MongoDB's 16 MiB document admits, moves through a page like any other. A message row is at most 16 MiB
  at admission, its `Payload` and `Start.Input` together at most 16 MiB less `M` and `F`, under the row cap ("The
  envelope bound and its headroom", below). A page never reads a payload, so values by reference play no part in the
  page bound; the plan's byte bound under Recover does rely on them, since a model passes a large timer value by
  reference ("The plan's byte bound", below).
  - **The event match is a fixed digest (proposed, fourth review R4-M1).** An event delivery's full key is its `Id`,
    its negative scope and its `Match`. Both variable parts are SHA-256 digests over one canonical encoding, so
    neither grows with the names it covers:
    - **The canonical encoding,** version 1: a domain byte (`M` for a match, `D` for a delivery id) and a version byte,
      then each part in a fixed order. A string is a 4-byte big-endian count of its UTF-16 code units followed by the
      units, big-endian (the exact units `KeyRequest.WireId` already hashes, `KeyAdmission.cs` 14–15); a creation is 8
      bytes, big-endian. The length prefixes make the encoding injective, and the domain byte keeps a match and an id
      apart. Version 2 (sixth review R6-B2, below) adds one fixed-width part, a publication incarnation of exactly 16
      bytes written as they are; a fixed width needs no length prefix, so the encoding stays injective. The same
      16-byte form applies in every domain that names an incarnation, the start registrar's `meet:` id (domain `S`)
      included.
    - **`Match`** is the lowercase hex of SHA-256 over `(Q(t, key), event id, incarnation)` as the event row stores
      them, in version 2: the identity of one publication of the event.
    - **The delivery `Id`** is `ev:` and the lowercase hex of SHA-256 over `(event id, incarnation, instance, creation,
      wait)`, in version 2, the event id and the incarnation as the event row stores them and the rest as the
      subscription row stores them: the wait one publication was delivered to. A start subscription's delivery uses an
      empty instance and creation 0.
    - **A publication incarnation (proposed, sixth review R6-B2).** Every event row carries `Incarnation`, an opaque
      16-byte id, unique and never reused: a random 128-bit value the kernel generates on every provider before the
      row's insert. An outside publish's insert-or-read returns the existing row's incarnation, so a duplicate
      within the deduplication window is the same publication; an admission after the sweep removed the row inserts a
      new row with a new incarnation, so it is a new publication. A turn's first match step inserts its event row
      with a new incarnation. Both digests include it, so a delivery or `Done` row left by an earlier publication of
      the same `(Q(t, key), event id)` never has a later publication's full key, and it can neither suppress the later
      delivery nor be protected by the later event row. This changes R4-M1's formula (fourth review): `Match` was
      over `(Q(t, key), event id)` and the delivery `Id` over `(event id, instance, creation, wait)`, both in
      version 1. The stored sizes below are unchanged, since a digest has a fixed length. The publisher, the catch-up
      path and the sweep read the incarnation from the event row, never from a caller, as they read the other
      parts.
    - **Together they name the whole tuple.** Two waits on one key differ in `Id`. One wait under two keys receives
      two publications, each event row with its own incarnation, so its two deliveries differ in `Match` and in `Id`
      (seventh review R7-m1). Two complete handles that share a wire id arise only when a test builds them at the
      store level, and B2-3 checks that they stay two handles. Collisions are cryptographically negligible, as for
      the dispositions' digests.
    - **The inputs are the persisted spellings.** The publisher and the catch-up path compute both digests from the
      stored event and subscription rows they selected by provider equality
      ([storage model](storage-model.md#inbox-identity-and-public-names)), never from a caller's spelling, so both
      sides of a match write the same row and the store keeps one.
    - **Stored size.** `Match` is 64 ASCII bytes and the `Id` 67 on every provider (128 and 134 bytes in SQL Server's
      UTF-16), within every provider's key widths. Neither holds a NUL or an unpaired unit, so no escape path applies.
    - **The components travel in a header, ahead of the unchanged payload.** The delivery's `Payload` is a
      length-prefixed header followed by the event's payload bytes exactly as published, with no escaping. The header
      is `1|` (its version), then, for the subscription key, the event id and the wait, the decimal count of the
      part's UTF-16 units, `:` and the units themselves, then a presence flag: `0` when the event has no payload,
      with nothing after it, or `1` followed by the payload. Version 2 (sixth review R6-B2) starts with `2|` and
      has the parts in the order key, event id, incarnation, wait, the incarnation as 32 lowercase hex characters
      framed as `32:` and the characters, 35 ASCII bytes. So a null payload and an empty one stay apart: Native
      publishes null for a `Publish` node with no payload binding (`NativeModel.cs` 548), `Values.Parse(null)` is
      null and `Values.Parse("")` is the JSON string `""` (`Authoring.cs` 337–342). Layer 2 strips the header: `Events.Delivered` returns the
      event id and the wait from it (today it parses the `Id` or decodes `Match`, `Store.cs` 257–278), and the message
      the model receives carries the published payload unchanged, which Native reads as the event's value
      (`NativeModel.cs` 267). The header counts against `C` = 40 KiB (proposed). Executed in this repair, the size
      probe writes the header for a key, an event id and a wait of 1,024 bytes each, of ASCII, of U+0001 or of `<`,
      in 3,155 bytes, 6,310 in SQL Server's UTF-16; it strips the header from a payload of quotes, `<`, backslashes and
      U+0001 to give that payload back unchanged, and gives back null for an absent payload and `""` for an empty
      one. The probe wrote version 1; version 2's incarnation part adds 35 bytes, inside `C`, and `Events.Delivered`
      returns the incarnation beside the event id and the wait.
    - **A turn's publish ticket carries the event in the same framing (proposed, fifth review R5-M1).** A turn's
      `Publish` writes a durable publish ticket to the instance itself, and the owner turns that ticket into the event
      row and its deliveries after the commit. The ticket's `Payload` is a ticket header in the delivery header's
      framing, followed by the event's payload bytes exactly as published, with no JSON re-encoding: `1|`, then, for
      the subscription key as it will be stored (`Q(t, key)`), the generated event id and the keep window in
      milliseconds, the decimal count of the part's UTF-16 units, `:` and the units, then the presence flag, `0` with
      nothing after it or `1` followed by the payload. The owner decodes the ticket into the event row with the
      payload unchanged, so a null payload and an empty one stay apart here too. The acceptance time travels beside
      the payload, in the ticket row's `AcceptedAt`, which the commit stamps ("Event eligibility" under
      [Recover](#recover), below). An outside publish writes a ticket in the same framing with the caller's event id, at
      most `N`, and no payload, since it writes its event row with the publish ("An outside publish writes the same
      publish ticket", below). The incarnation travels in the same framing (sixth review R6-B2): an outside publish's
      ticket carries its event row's incarnation from the start, and a turn's first step, which inserts the event
      row, rewrites the ticket without its payload and with the new incarnation, so every later step knows which
      publication it is matching. Every step after a turn's first step, and every replay of a ticket that carries an
      incarnation, reads the event row by `(key, id, ticket incarnation)`; a row with another incarnation counts as
      gone, and the existing gone-row rule for that step or replay applies (qualified at K3 and K4). A replay of a
      first step that never committed carries no incarnation and inserts the row, as SB-7 run 6 states. So when i1's ticket is dead-lettered, the sweep removes its row, and
      i2 is published and delivered, an operator's replay of i1 answers "gone" and never matches i2's row (EI-1,
      run (g)). Since the kernel generates the incarnation before any row is written, the `pub:` id and its
      placement are known up front, and the partition high-water check before any row is written holds unchanged (a
      condition, qualified at K4).
      - **A generated event id counts inside `C`.** A turn's `Publish` takes no event id from the model: the event id
        is `~` followed by the ticket's own issued id, `~{origin}#{revision}.{ordinal}` (seventh review R7-M1, "Turn
        and caller event ids are disjoint" below; today the bare issued id, `Turns.cs` 429–433, the ordinal
        `SendNext` then takes, 327–330), whose origin is `c{units}_{actor}_{creation}` (`Creation.cs` 14). At the
        admissible origin limit, an actor id of `N` + 70 bytes (`Q` and `{vp}/` added), a unit count of 4 digits, a
        creation and a revision counted as 20 digits each and an ordinal as 10, the id is 1 + 1 + 4 + 1 + (`N` + 70)
        + 1 + 20 + 1 + 20 + 1 + 10 = `N` + 130 = 1,154 bytes, within an issued id's 2`N` + 160. So the ticket header
        is at most 2 + (5 + 1,089) + (5 + 1,154) + (3 + 20) + 1 = 2,279 bytes, and a delivery header that carries such
        an id at most 2 + (5 + 1,089) + (5 + 1,154) + (5 + 1,024) + 1 = 3,285 bytes, 130 more than the size probe's
        3,155 for a 1,024-byte event id. Doubled in SQL Server's UTF-16 they are 4,558 and 6,570 bytes, both inside
        `C` = 40 KiB. These figures are derived, and EM-4 measures them at K4. The incarnation part (sixth review R6-B2) adds 35 bytes to each, 70 in SQL Server's UTF-16, still
        inside `C`, and the event row's 16-byte `Incarnation` is one of the fixed fields `F` counts (measured at
        K4). On a provider whose composite key limits the event row (SQL Server's `Events (Key, Id)` within 450
        units, [tenancy](tenancy.md#names-one-namespace-per-tenant)), a turn's `Publish` checks its key jointly with
        the generated id in that maximum stored form and refuses past it by throwing as a model fault, so no accepted
        publish meets a key refusal at the event insert: on SQL Server, (raw key + 65) + (caller key + 70 + 59) ≤ 450,
        a raw key and a caller key of at most 256 units together, where 59 is the id's prefix and suffix at a 3-digit
        unit count, the reserved `~` included (derived from the declared key widths, unmeasured; measured at K4).
      - **So the ticket fits the row cap.** A publish within the source limit (below) has a payload of at most 16 MiB
        less `M`, `F`, `C` and `I`; its ticket's `Payload` is at most that plus `C`, and the ticket row, its metadata
        within `M` and its other fields within `F`, is at most 16 MiB less `I`, under the row cap. The event row it
        becomes and every delivery derived from it fit by the bounds above, so an accepted publish meets no size
        refusal at its own ticket either.
      - **Existing prototype behaviour.** The ticket's `Payload` today is `Json.Write` of an `EventRow` (`Turns.cs`
        429–433, through `Send`, 332–336), the owner reads it back with `Read<EventRow>()` in `Worker.MeetAsync`
        (`Worker.cs` 1315–1329), and MongoDB stores that JSON string as the row's `payload` (`MongoStore.cs`
        629–637). The JSON writer escapes `<` and U+0001 as six bytes each. The review's witness, an accepted event of
        16,621,568 bytes of `<`, gave a ticket payload of **99,729,457 bytes** before row framing, against the
        16,777,216-byte row cap, so the commit of an accepted publish would be refused; the size probe's header round
        trip never took this producer path. Case EM-4.
    - **A start subscription's input is bounded where it registers.** An event-start delivery carries the
      subscription's `Start` (`Store.cs` 251), and the start's `Input` counts in the row's payload budget beside the
      `Payload`. So the start-subscription registrar (`Events.SubscribeAsync`, `Store.cs` 302) is a source boundary
      too: it applies the per-name limits (`N` for the key, the wait, the model and the definition) and refuses a
      `Start.Input` above `I` = 64 KiB (proposed), measured in the provider's stored encoding (the serialized input, doubled
      in SQL Server's UTF-16), before any write; a larger input goes by reference under the values
      rule ([write volume](write-volume.md#decisions-owner-1617-november-2026), decision 1a).
    - **Accepted or refused at the source.** Subscribe, `Publish` and an outside publish keep their per-name limits
      (`N` for the subscription key and the wait, and for the event id an outside publish supplies, with the unit
      refusals above; a turn's event id is generated and bounded by its origin, above). A publish whose payload
      is above 16 MiB less `M`, `F`, `C` and `I` is refused there: an outside publish by the entry point, with no event
      row, and a turn's `Publish` by throwing as a model fault. The publish cannot know whether a start subscription
      will match, now or at a later catch-up, so every publish reserves `I`. So every derived delivery, to a wait or to
      a start, at publish or at catch-up, fits its row cap and its page, and no accepted subscription, registration or
      publish meets a size refusal at the derived insert.
    - **Today's encoding** (existing prototype behaviour): `Match` is `EventMatch.Encode`, the JSON of four names and
      the creation through `DurableKeyJson` (`Store.cs` 230–239), and the delivery id is
      `evq:{length}:{event id}:{origin}:{wait}` (`Store.cs` 247–250). Executed in this repair, the size probe
      ([experiments/Astra4.Sizes](../../prototypes/kernel-model/experiments/Astra4.Sizes/README.md)) measured the real
      encoder: four names of 1,024 bytes of U+0001, or of `<`, give a `Match` of 24,787 bytes, above `M`, because the
      JSON escape writes each such byte as six, and a delivery id of 3,179 bytes, above 2`N` + 160. The review's
      800-byte wait gave 4,868 bytes with the probe's short names (4,877 with the review's). Cases EM-1 to EM-3.
- **The selection is bounded.** An explicit `retry` selection names at most `P` handles and at most 256 KiB of
  encoded handles (proposed); a larger one is refused with no change, and the operator uses retry-all, which is one
  flag. Open marks the selected kept rows `Selected` and re-arms the selected suppressed timers (see "Checks" below), so
  the Resolution never stores the selection and Open writes at most `P` rows beside the plan's waits. An interruption reverts `Selected` to `Consumed` by `Target AND KeptBy = Selected`,
  at most `P` rows, so the next recover starts from the Park's own reasons.
  The in-memory implementation supports older deliveries whose full key exceeds the direct handle encoding,
  including JSON event matches. Their sealed handle contains a digest of the full key and the row's sequence.
  Open resolves it within the selected creation under the store lock, requires one matching retained row, then
  marks that row by its full key. A physical provider's bounded lookup for those sequences belongs to K4.
- **The page bound.** Proposed: `P` = 500 rows, the default `MaxBatch`, below the 1,000 rows per statement of the
  owned commit, and `B` = 4 MiB of metadata. A page takes at most `P_page` = min(`P`, ⌊`B` / `M`⌋) = 170 rows, so its
  metadata is at most `B` by construction: a plain row limit that every provider supports, with no byte count in the
  query. An explicit selection keeps its own bound of `P` handles (above). A page is one turn's share of the partition's flush, far inside MongoDB's 16 MiB
  document and 48 MiB message limits and short against its transaction lifetime
  ([MONGO.md](../../prototypes/kernel-model/MONGO.md), M10). Pages are ordinary turns under the turn budget per pass,
  so other instances of the partition keep their share.
- **The progress gate** (revised, fifth review R5-M3). Restored rows become due depth and owner memory, so a drain
  must not restore faster than the instance's model takes them. A poll's `Seen` mark alone no longer admits a page;
  a page is admitted on handoff, and its size is cut by a credit:
  - `ParkRecord.Resolution` records the handle of the last row the previous page restored. Restored rows are offered
    in tuple order within the cohort, so that row is the last of its page to be offered.
  - **Handed off.** A restored row is handed off, at the current owner, when it was delivered into a model turn that
    committed (consumed, or handed back by `Hold`), when the model holds it, or, for a row of a ticket kind (job, meet or publish),
    once it is restored. Ticket kinds are kernel work, outside the model's backlog: the job runner and the meet path take them
    whatever the drain does, so they are outside the credit and the bound below (fifth review R5-M1, R5-M2). They are
    outside `B_credit` too, so a drain's owner memory is bounded by `B_credit`, the held rows and the restored kernel
    tickets: job tickets bounded by `W_max` and `B_plan`, and publish and meet tickets by what the Park kept, a
    payload-carrying publish ticket up to the row cap. Suspension hands nothing off: every page waits while the instance is suspended (below, M3-5), so a row
    withheld by a suspension stays not handed off. The Worker's `Shelved` flag on a suspect marked seen and not yet
    tried (`Turns.cs` 104) is no handoff either. A suspect on trial counts as handed off once its trial turn commits. A row that is `Done` or absent (consumed, or done and
    swept after its window) counts as handed off. A `Seen = e` mark by itself does not count.
  - **The gate.** A page waits, by sending its `kernel.drain` with a delay, until the gate row is handed off. The
    Complete page waits on the same test and, as every page does, while the instance is suspended (M3-5). A gate row
    handed off and not yet consumed (held by the model) is delivered after Complete ahead of every untagged row, by the
    release cohort precedence above (fourth review R4-B1).
  - **The credit.** Let u be the open Resolution's restored rows that the owner has not handed off, and b their
    stored size: each row's metadata plus its payload length, the bytes of `Payload` and `Start.Input` in the
    provider's stored encoding. An admitted page restores at most `P_page` − u rows, and stops before a row that
    would take b above the byte credit `B_credit` = 4 MiB (proposed, a coordinator design choice, about the metadata
    bound of a full page). A page with u = 0 always restores its first row, whatever its size, as every page has room
    for its first row, so a row at the 16 MiB row cap is restored alone and the next page waits until it is handed
    off. So the drain admits a page only while the current owner's u is below `P_page` and b is below `B_credit`
    (u = 0 always admits the first row). When the credit allows no row (u ≥ `P_page`, which a takeover can cause,
    or b ≥ `B_credit`, or the next row would take b past `B_credit` while u > 0), the page restores nothing, moves no cursor and waits as for the gate, so a page cut to zero by the credit is never
    taken for the page that finds no row and never completes the drain.
  - **Where the payload length comes from.** The page never reads a payload, so the length is a stored field: a
    `PayloadLength` column on the message row, written at insert from the encoded `Payload` and `Start.Input` and
    never changed, which the page projects with the other metadata (proposed, a K3 store field; it is one of the
    fixed-width fields the metadata bound already counts). Existing prototype behaviour: no store keeps a length
    today; the EF row holds `Payload` and `Start` as text with no length column (`MessageEntity`, `EfStore.cs` 60–82)
    and the in-memory row holds the message itself (`MessageRow`, `InMemoryStore.cs` 23–29).
  - **The owner's count.** A healthy owner knows u and b from memory: which restored rows it has polled and handed
    off. The rows of the open Resolution are the not-`Done` rows with its `ReleasedBy`; where the owner needs the
    count from the store, it reads them in tuple order, at most `P_page` rows past the keys it has handed off, with
    the cohort read's partial index, so one read decides the credit.
  - **Ownership loss resets the credit** to the durable state. A new owner, or the same process after a reset, has
    handed off nothing: it counts every restored, not-`Done` row of the open Resolution as not handed off, so after a
    takeover the next page restores only as many rows as the credit leaves, none while a full page or more is
    pending, until the new owner hands them off again, by delivery, trial, `Hold` or job start; rows the lost owner's
    model held count among them, so u can exceed `P_page` by those rows until they are handed off again. It reads the
    gate row with one point lookup by full key: `Done` or absent counts, anything else waits for its own handoff. So
    a takeover adds no free page, and a mark the lost owner made counts for nothing.
  - **While the instance is suspended the gate waits.** Resume is a control kind that passes withholding, so it always
    reaches the instance and releases the gate; suspension cannot deadlock the drain, and after Complete it cannot
    deadlock the cohort either, since suspension passes the mark ("Suspension passes the mark" above).
  - **A row the model holds counts as handed off**, because a held row may wait for a later row, perhaps an
    unreleased one that only Complete lets through. Counting it otherwise could stall the drain for good.
  - **The gate row is always deliverable.** It is a row the previous page restored, so it carries the open
    Resolution's `ReleasedBy`; and a kept row was consumed, so it was due. Only suspension withholds it, and resume
    passes. A row whose delivery needs Complete (an unreleased arrival, a timer the open re-armed, a restarted ticket)
    is never a gate row, since no page restores it. So the gate never waits for a row that only Complete can release.
  - **The bound, stated honestly** (fifth review R5-M3). The drain's own rule is the one above: it admits a page only
    while the restored rows the current owner has not handed off number fewer than `P_page` and their bytes stay
    under `B_credit`, and u = 0 always admits the first row. The resulting bound holds at all times, across any
    number of takeovers: the drain's restored rows that are not `Done` (other than ticket kinds (job, meet and publish), which are kernel work)
    are at most `P_page` plus the restored rows the model holds or has held and not since consumed, and their stored
    bytes, payloads included, are at most `B_credit`, plus those held rows' bytes, plus one row larger than
    `B_credit` restored alone (a message row is at most the 16 MiB row cap). The reason: a restored row the model
    has never held and nobody has consumed is not handed off, so at any page admission the current owner counts it
    in u; a page adds at most `P_page` − u such rows within the byte
    credit, and such rows only leave the set afterwards, by consumption or by a hold. A held row survives a takeover
    as a not-`Done` row, and the new owner counts it as not handed off until it hands it off again, so after one
    takeover or several in a row u can exceed `P_page`, and no page is admitted until it falls below. Held rows are
    the model's choice, and `elsa.kernel.drain.held` already counts them. Held
    rows stay due and unconsumed, and the built `Hold` hands them back to every turn (FINDINGS section 14), so a model
    that defers everything (a StateMachine during a suspended action) can hold the whole backlog in memory, as it
    would hold the same rows arriving live. A cap on held restored rows was rejected: reaching it with no progress
    would need its own incident path, and its cost would fall on exactly the models that hold for a later row. The
    `elsa.kernel.drain.held` gauge reports held restored rows and `elsa.kernel.drain.pending` the rows not yet handed
    off, with their bytes, so an operator sees both parts.
  - **Existing prototype behaviour the gate replaces.** The built poll marks every row it returns at the owner's
    epoch in the next flush (`Worker.cs` 1397), whether or not a model turn has received it, and the poll skips keys
    the owner already holds (627–628). So a gate on `Seen = e` admitted the next page as soon as the previous page had
    been polled, and a control with priority could restore page after page before any model work (the fifth review's
    R5-M3 trace; FR-1's page tested its gate only on the empty-page branch, `experiments/Review4.Blockers/Fr1.cs`
    725–749).
  - **The partition's backlog is no part of the gate.** The trace it avoids: rows that arrive during a recover drain
    are withheld as unreleased rows, yet they count in the partition's backlog; a gate on that backlog above the high
    water mark would make every page wait, the withheld rows would never be released, and the backlog would never
    fall. Unrelated ingress is still refused by the ingress threshold alone, which counts the withheld rows until
    Complete releases them.
- **Crash and takeover.** The cursor, the rows' `ReleasedBy` and the next `kernel.drain` commit with each page, so a
  new owner continues from the cursor and withholds exactly what the old owner withheld. A page whose commit outcome is unknown is reset and taken as any commit is; rows a landed page restored
  or cleared no longer match the page read, so the drain neither repeats nor skips a row.
- **When it is complete.** The open's reply says the resolution started; the operation's outcome (complete, with
  restored, cleared, refused and already re-armed counts, or interrupted) appears in the parked-work read model by operation id, and a
  synchronous API call can wait for it through a reply address until its deadline.

#### Delete during a drain

This is proposed. A delete is the data lifecycle page's [qualified delete](data-lifecycle.md#proposed-the-qualified-delete)
in every state of the row, a drain included. Its checks run in the actor's command turn against the row as it is then,
its current terminal mark included. When they pass while a drain is open, the delete takes the drain over. The
takeover never lets a delete name a mark the row does not carry, so decision 9A holds: every delete is qualified, and
none has a force form. A delete compares the row's terminal mark only. `ParkRecord`'s Park identity outlives the row's
mark during a recover, and a delete never reads it.

| Open drain | Row status | The row's terminal mark | A separate delete |
|---|---|---|---|
| Recover, model running | Running | None: the recover's Open cleared it | Refused as "transition": no selection can name a mark the row lacks |
| Recover, model ended | The end's status | The mark the end wrote | Naming that mark, reason Operator: takes over |
| Discard or expiry | Faulted | The Park's mark | Naming the Park's mark, reason Operator: takes over |
| Terminate of a parked instance | Faulted, "terminating" | The Park's mark | Naming the Park's mark, reason Operator: takes over |
| Terminate that interrupted a running recover | Running, "terminating" | None until Complete writes the Cancelled mark | Refused as "transition"; after Complete, a delete naming the Cancelled mark is an ordinary delete of an ended row |
| Delete, a taken-over drain, or a terminate with `ThenDelete` | As in the rows above | As in the rows above | Checks 1 to 3 as in the rows above; a delete that passes them changes nothing and is refused as "in progress", with the operation that owns the deletion step named in its outcome |

**Where "in progress" sits.** It is tested after checks 1 to 3 and before check 4, so a delete from another tenant, for
another creation or naming another mark gets the reason of the check it fails (tenant, target, run or transition) and learns
nothing of the drain. Two consequences follow. A terminate with `ThenDelete` that interrupted a running recover leaves
the row with no mark, so a separate delete during it fails check 3 as "transition", the same answer as during any
terminate of that origin (DT-5). And "in progress" comes before check 5, so a separate delete during a drain that
already ends in a delete is refused as "in progress" even while `ProtectsDeletion` protects the row; during a drain
that does not end in a delete, the same delete is refused at check 5 as "protected" (DT-6).

A Retention delete that passes check 3 while a drain is open is refused as "in progress" when a delete already owns
the drain, and otherwise at check 4 as "not eligible", since the Park stays unresolved. In the rows with no mark it
fails check 3 as "transition". The retention worker selects the run again after Complete, under the mark Complete
leaves.

- **The takeover.** The delete's command turn records its operation and selection in the open
  `ParkRecord.Resolution`. The kind, the cursor, the generation and the `kernel.drain` in flight stay as they are, so
  the drain runs on from its cursor and no page is repeated or skipped.
- **Complete of a drain that ends in a delete** (the delete of a parked instance, a taken-over drain, or a terminate
  with `ThenDelete`) does three things in one commit:
  1. the kind's own end, where the drain has not yet ended the instance: the disposition, Faulted and the terminal
     mark for discard and expiry; Cancelled and the terminal mark for terminate. A recover whose model ended has
     nothing left to end. The delete of a parked instance has no end of its own while the delete goes ahead; when
     step 3 finds the row protected, it ends as a discard would (below);
  2. the one parent notice the drain owed: Faulted for discard, expiry and the delete of a parked instance, Cancelled
     for terminate, and none after a model end, whose own turn sent it;
  3. `ProtectsDeletion` once more, at Complete's time, since it reads the state and the owner's clock
     (`WorkflowInstance.cs`). An unprotected row gets the built delete, whose single journal row is the deletion
     marker. The retirement marker it leaves keeps the row's recorded parent, child registry and job keys, so a
     parent-bound terminate delivered after this Complete still ends the subtree ([the
     rule](#every-park-operation-names-its-park), fourth review R4-B2). A protected row stays, ended under the mark
     of step 1, which is journalled as any end, or, for a recover whose model ended, under the mark that end wrote;
     the delete's outcome is "protected".

  **A protected Complete of a parked instance's delete.** Protection can begin between the delete's command turn and
  its Complete. By then the drain has cleared every kept row, so the Complete resolves the Park as a discard would:
  the row stays Faulted with a new terminal mark, the parent gets the one Faulted notice the drain owed, the journal
  records the delete refused as protected and the Park discarded, and the row's `Faulted` retention window starts from
  the new mark. This is a coordinator design choice ([register](decisions.md#the-gate-2-fix-round-17-november)); it
  fills a gap in decision 9A's protection rule, which never deletes a protected row.

  The deletion marker carries the drain's counts and the outcome of each operation the drain served, so the
  parked-work read model keeps them by operation id after it removes the view: the taken-over operation as complete
  (for a terminate, Cancelled and then deleted by the named delete), and the delete as deleted.
- **`ThenDelete` follows its own contract.** The deletion is the terminate operation's own last step, so it names no
  terminal mark and waits for none ([data lifecycle](data-lifecycle.md#proposed-the-qualified-delete)). On a parked
  row with no resolution open, or during a recover whose model runs, the terminate is a drain, and its Complete runs
  steps 1 to 3; with a discard, expiry, terminate or delete drain open, it is refused as "resolution open"
  ([the rule](#every-park-operation-names-its-park)). A parked row has not ended for this purpose, since its Park is
  unresolved. During a recover drain whose model has ended, the run has ended, so the command takes the
  qualified-delete path of an ended run, in this order: checks 1 and 2; then "in progress", refused with no change
  when a delete already owns the drain's deletion step (a separate delete took the drain over, or an earlier
  `ThenDelete` did), with that operation named in the outcome; then the Park check of a selected terminate, its
  `ParkSelection` against `ParkRecord`'s Park identity (reason Park on a mismatch); then check 5; and then it takes
  the drain over. A
  takeover records a single operation, so testing "in progress" there keeps the first owner's outcome (DT-3).
  **The subtree on this path** (fifth review R5-B2). An ended run that still has a registry or an outstanding job is terminated as built
  ("Terminate and delete in one commit" above), so on this path the command ends the subtree in either order:
  - **Refused as "in progress".** The command is still a terminate with authority over the run (its
    `ParkSelection`, when it carries one, equal to `ParkRecord`'s Park identity), so the refusing commit cascades
    exactly as J-1's "resolution open" refusal does: it sends the cascade's first page from the live row and records
    the rest as `Discharging` there, and the drain's Complete carries that obligation to the marker. The owning
    delete keeps its outcome. A command whose selection names another Park gets the plain refusal, with no cascade.
  - **Taking the drain over.** The cascade starts in the takeover command's own commit, on the live row, with its
    first page and a `Discharging` obligation for the rest, and the drain's Complete carries the obligation to the
    marker it leaves.

> **Third review: counterexample and answer** ([third gate 2 review, R3-M5](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6960856712)).
> The trace: A parks at mark r1. A recover opens and clears the row's mark, while `ParkRecord` keeps r1. A terminate
> interrupts the still-running model: its Open stops the model's turns, and the status stays Running. Its Complete
> will write the Cancelled mark r2. In between, a separate operator delete naming r1 fails the mark check, a delete with
> no mark is ambiguous, and no selector can name r2 yet, while M3-6 required every delete during a terminate drain to
> take it over. `ThenDelete` is a different command.
>
> The answer, step by step:
> 1. A takeover needs the delete's checks to pass against the row's current mark. In this interval the row has none,
>    so the delete naming r1 is refused as "transition" and changes nothing. M3-6 now asks for exactly that.
> 2. The terminate drain runs on, clears every kept row and completes: Cancelled, mark r2, one Cancelled notice to the
>    parent.
> 3. A new delete selected under r2 passes the mark check. Operator needs no policy; Retention waits for the
>    `Cancelled` window from r2's time; a run that `ProtectsDeletion` protects is refused as "protected".
> 4. A terminate of A while it is parked, with no recover between, leaves r1 on the row. A delete naming r1 passes and
>    takes the drain over; its Complete ends A Cancelled under r2, sends the one Cancelled notice and deletes.
> 5. An operator who wants the run gone without waiting sends the terminate with `ThenDelete`, whose deletion is that
>    operation's own last step and needs no mark.
> 6. Comparing the delete with `ParkRecord`'s r1 when the row has no mark would accept step 1's delete, which was
>    selected before the recover ran. DT-2's mutation is that comparison.

**Worked example: a recovered child is terminated, its parent hears once, and it is deleted.** Parent P waits on
child A.

1. A parks: Faulted, with Park mark r1 on the row and in `ParkRecord`; 1,200 rows are kept over time. An operator reads
   r1 from `ParkedItem` and sends delete D1 (Operator, r1), which is delayed.
2. A `resume` recover R opens: the row's mark is cleared, `ParkRecord` keeps r1, and A runs. Page 1 reads up to
   `P_page` = 170 rows, restores the `Arrived` ones and clears the rest, and the model takes some of the restored rows.
3. A terminate T of A is accepted. Its Open clears R's Resolution and opens its own, stops the model's turns, removes
   the waits, cancels the jobs and terminates A's children. A shows Running, "terminating", with no mark.
4. D1 arrives. The row has no mark, so check 3 fails: D1 is refused as "transition", nothing changes, and
   `elsa.kernel.delete.refused` counts it.
5. T's pages clear the 1,030 rows still kept, at most 170 a page, and the page that finds none is its Complete.
   Complete records Cancelled with mark r2, resolves the Park and sends P one Cancelled notice. The restored rows A had
   not consumed meet the rule for a finished instance and are dropped.
6. P consumes the notice once; the instance list shows A Cancelled under r2.
7. An operator sends delete D2 (Operator, r2). Every check passes and `ProtectsDeletion` does not protect A, so A is
   deleted, with the deletion marker as the commit's journal row. P receives nothing more.

Had T reached A while it was parked, without R, D1 would have named the row's mark at step 4 and taken the drain over,
and T's Complete would have deleted A after the same single Cancelled notice.

### Recover

`kernel.recover {operation, park, mode, selection}` is a control kind: the actor type handles it alone and
calls one new model hook.

- **The contract gains a hook,** beside `UpgradeState`: `RestorePlan RestoreWaits(object state, ParkRecord park)`. It
  returns the subscriptions, timers and jobs the model's committed state still waits on: each subscription with its
  key, wait and keep flag, each job as a full spec, `JobSpec(string Key, object? Arguments, string EffectKey)` ("The
  job spec", below), and each timer as a full spec, `TimerSpec(string Id, string Kind, object? Payload, long Due)`: the id
  `ITurn.Send` returned, the kind and typed payload the model sent, and the absolute due time the row was written
  with. Native, Flowchart and StateMachine each implement it.
  - **One read of the due time.** Today `DurableTurn.Send` computes `Due` from its own read of the clock plus the
    delay (Turns.cs, `Send`), and the owner's clock advances between reads (Worker.cs, `Now`, anchored on a monotonic
    tick), so a value the model computed separately could differ. `SendOptions` therefore gains `DueAt`, an absolute
    time on `ITurn.Now`'s clock, and the kernel writes it as the row's `Due` unchanged. A model arming a timer it may
    need to restore reads `ITurn.Now` once, passes `DueAt`, and records that same value. A `DueAt` already in the past makes the row due at
    once, under the ordinary due rule. A send that gives both `Delay` and `DueAt` throws, so the turn fails as a model fault and the instance parks.
  - **The layer 3 contract change.** A model keeps, for every timer it waits on, the kind, payload and due time it
    sent, and returns them in the plan. The spec uses only the turn contract's own terms (an id `Send` returned, a
    kind, a payload, a time on `ITurn.Now`), so layer 3 stays host-neutral: no partition, position or SQL appears.
    Native's `WaitEntry` holds frame, kind, key and creation, and its timer arm stores only the send's id
    (NativeModel.cs, `WaitEntry` and `Register`), so it gains `long Due`: the arm reads `turn.Now` once, adds the
    timer's milliseconds, sends with that value as `DueAt` and stores the same value. Its plan returns `TimerSpec(entry.Key, NativeKinds.Timer, new TimerFired(wait), entry.Due)`
    for each timer entry that still has a key, with the `TimerFired(wait)` payload its timer handler reads. Native's
    state version steps from 13 to 14, one step for both new `WaitEntry` fields (`Due` here, `Job` in the next
    bullet), neither of which is built; release 1 starts with no stored state (the switch basis), so no older entry
    needs a due or a job spec.
  - **The job spec (proposed, fourth review R4-M3).** A restart must run the invocation the model started, and the
    only durable home of its arguments today is the ticket the restart replaces. At arm, the model records an
    immutable `JobSpec` in its wait record, as it records a timer's spec: its job key, the arguments it passes to
    `StartJob` (for a workflow activity: the definition and node ids that name the activity, the inputs captured at
    arm, the frame and the iteration path; Native's `BackgroundArgs`, `NativeModel.cs` 54) and the effect key it computed for that invocation (a standalone job's is `{job origin}/job/-/0`, [job
    workers](job-workers.md#the-effect-key-the-worker-receives)). Native's `WaitEntry` gains `Job`, the
    `BackgroundArgs` its background arm passes to `StartJob`, and its plan returns `JobSpec(entry.Key, entry.Job,
    entry.Job.EffectKey)` for each job entry. Today `WaitEntry` holds frame, kind, key and creation only
    (`NativeModel.cs` 129), the arm evaluates the bindings and passes the inputs, path and effect key only to the
    ticket (`NativeModel.cs` 347–355), and the job's driver reads them back from the ticket's arguments
    (`NativeModel.cs` 185–195), as `Turns.cs` 394–402 writes them (existing prototype behaviour). Executed in this repair, the size probe ran
    this arm on the real Native model: the `WaitEntry` was `{"Frame":"f4","Kind":"job","Key":"w6","Creation":0}`,
    the ticket's arguments held `Service = alpha`, the iteration path and the effect key, and after a sibling set the
    bound variable to `beta`, the state held no `alpha`. So a restart rebuilt by evaluating the bindings again would
    send `beta`, a different invocation, and a payload-free marker or a swept row leaves nothing to copy
    (`ExecuteJobAsync` throws `ArgumentNullException` on a ticket with no arguments). The spec counts toward the plan's
    byte bound and toward the instance envelope at admission ("The plan's byte bound", below), and the restart writes
    its ticket from the spec alone ("A restarted job", below).
  - **The plan bound.** A plan names at most `W_max` = 4,096 waits, subscriptions, timers and jobs together
    (proposed; fourth review R4-M2 adds the subscriptions). A model refuses to establish a wait past it (Native faults
    the activity that would arm it or subscribe it, TR-4, SB-1), and the kernel checks it at Open: a larger
    plan refuses the recover with reason "plan" and changes nothing, and discard stays available.
  - **The plan's byte bound.** `W_max` caps the entries but not their bytes, and an Open writes every re-arm,
    restart and restored subscription in one commit. So a plan's restorable bytes are bounded too: the encoded `Payload` of every timer, as
    `Send` encodes it, plus the encoded payload of every job ticket a restart would write (its `JobSpec`'s arguments
    and effect key), plus 5,406 bytes per timer or job
    for the fields every re-armed or restarted row stores, at their admission limits: `Id` 2`N` + 160, `Sender` `N` +
    160, `Target` `N` + 70 (a qualified actor id), `Kind` 256, the fixed fields 64, `TargetCreation`, `SenderCreation`
    and `Due` 24, and the trace 576 (a 55-byte `traceparent` and a `tracestate` of at most 512 bytes, rounded up).
    Each subscription counts 7,168 bytes, its restore write at the admission limits. The Open writes each restored
    subscription by a plain insert: the Park, and a re-park that continues it, removed every subscription of the
    instance (the built Park's commit sets `Unsubscribe`, `WorkflowInstance.cs` 129, which MongoDB applies as one
    delete by instance, `MongoStore.cs` 255–256), so the insert meets an empty set, and a key violation can only come
    from a defect and is a definite error, as for a restarted ticket. The insert carries the document once. On MongoDB
    that document repeats the key `Q(t, key)` (`N` + 65), the instance (`N` + 70) and the wait (`N`) in its composite
    `_id` beside the top-level fields (`MongoStore.cs` 648–654). Executed in this repair, MongoDB.Bson 3.9.0, the
    prototype driver's encoder, writes it at the name limits, with the proposed tenant, creation and keep fields, in
    6,613 bytes, and as one bulk-write insert entry in 6,640 bytes, so 7,168 is that measured size with headroom.
    Today's replace-upsert repeats `_id` once more in its filter (`MongoStore.cs` 252) and measures 9,905 bytes as an
    entry, so the restore does not use it. When any
    restored subscription asked for kept events, the one meet ticket the Open writes ("Subscriptions", below)
    counts `M` + `F` + `C`, its cursor, its catch-up id and its `P0` being within `C`. A model counts the same terms
    when it subscribes as when it arms.
    **Every stored write of a keeping subscription is charged (proposed, sixth review R6-M2).** Beside its insert, a
    subscription that keeps events writes a coverage entry (below) and, on MongoDB, a conflict-document write for its
    key; these are charged 2,560 bytes together (proposed; each provider's sizes measured at K4), so a keeping
    subscription counts 9,728 bytes and a non-keeping one 7,168, and a plan with any keeping subscription adds the
    meet ticket's `M` + `F` + `C` = 90,112 bytes once. A model charges the same when it subscribes.

    They come to at most `B_plan` = 24 MiB (proposed, unchanged). At `W_max` timers and jobs the allowance alone is
    21.1 MiB, so a full plan of small timers (Native's `TimerFired(wait)`) still fits; the subscription allowance lets
    a plan hold at most 3,510 subscriptions (⌊`B_plan` / 7,168⌋) and no other wait, or 2,577 when they keep events
    (⌊(`B_plan` − 90,112) / 9,728⌋; the fifth round's 3,498 charged the insert alone). `W_max` = 4,096 is
    unchanged. The number keeps one Open inside MongoDB's limits:
    the instance envelope with its `ParkRecord` (at most 16 MiB), at most `B_plan` of envelopes stored as UTF-8 in
    BSON, and the selection's 256 KiB of handles come to about 42 MiB with framing, inside the 48 MiB message limit,
    and each envelope fits its own 16 MiB document.
  - **Statement size on the relational providers** (proposed). MySQL caps every statement packet, single rows
    included, at the server's `max_allowed_packet`, as ADR 0084 already records for the old core's commits ([ADR
    0084](../adr/0084-runtime-commit-journal.md)), and a split by row count alone would let one statement carry close
    to `B_plan`, which a server set at 16 MiB or less would refuse on every attempt of that Open. So the owned commit
    splits its statements by encoded bytes as well as by rows: no statement exceeds a budget of 4 MiB or the
    provider's own statement limit, whichever is lower, which keeps a commit's shape independent of server settings.
    The split changes only statement boundaries: every statement still runs inside the commit's single fenced
    transaction, so the commit lands whole or not at all. A single row above the budget goes in a statement of its
    own. So every statement has one of two ceilings: a statement of several rows is at most the budget, and a
    statement above it holds one admitted row and the provider's framing, within the provider's own limit (TR-5 run 4,
    after the fourth review's R4-m2). The largest row is 16 MiB under the row cap ("The envelope bound and its headroom", below), so MySQL's
    `max_allowed_packet` must be at least 17 MiB, the cap plus framing; the kernel reads it at startup and refuses to
    start below that floor, and MySQL 8.0's default of 64 MiB passes. **Measured at K4:** the Open at `B_plan` on
    every provider, its encoded size, duration and transaction behaviour, MySQL at its default `max_allowed_packet`
    and at the 17 MiB floor, SQL Server's batch size (65,536 network packets) and 2,100 parameters per statement,
    PostgreSQL's 65,535 bind parameters per statement and its protocol message limit, and SQLite's
    `SQLITE_MAX_SQL_LENGTH` (1,000,000 bytes by default) and its bound-parameter limit (`SQLITE_MAX_VARIABLE_NUMBER`).
    The owned commit binds payloads and every other value as parameters, so SQLite's SQL-length limit applies to the
    statement text only, and each bound value falls under `SQLITE_MAX_LENGTH` (1,000,000,000 bytes by default), far
    above the 16 MiB row cap.
  - **Enforced at arm and at Open.** A model enforces the bound when it arms a wait or subscribes, as it enforces
    `W_max`: a wait that would take its plan past `B_plan` faults the activity that would arm or subscribe it, a model
    fault (TR-4, SB-1), and a large
    value goes by reference under the values rule ([write volume](write-volume.md#decisions-owner-1617-november-2026),
    decision 1a), so the envelope carries only the reference. A value row is removed with its instance (that
    decision), so a value referenced only from a pending timer's payload lives until the instance is deleted, and a
    re-armed timer finds it. At Open the kernel computes the same sum from the envelopes it is about to write, and a
    plan over `B_plan` refuses the recover with reason "plan" and changes nothing, as at `W_max` + 1 (TR-5).
- **Checks.** The `ParkSelection` matches (above), no resolution is open, and the instance is recoverable under
  decision 1. After the window's expiry the Park is resolved, so recover is refused in
  both modes. Open splits the selection before it writes anything, reading the disposition first, as the lookup does,
  and gives each handle one meaning:
  - a handle that names a timer this Park retains as `Suppressed` (below) goes to the re-arm path (below), even while
    its row is still kept, and is never marked `Selected` nor counted in the marking;
  - a handle that names a timer this Park retains as `Rearmed`, whose row is pending or kept `Arrived`, is an
    authorized idempotent no-op: it writes nothing, is counted "already re-armed" in the outcome, and the rest of the
    selection proceeds. The timer is armed once already. A pending one with a null `ReleasedBy`, armed by an Open,
    waits for Complete. A pending one with an older non-null `ReleasedBy`, released by a page of an earlier resolution
    and consumed by nobody before a further interruption, is `Stale`, and a page of this drain releases it, in tuple
    order within the cohort (fourth review R4-m3). An `Arrived` one is restored by the drain as every `Arrived` row is;
  - a `Rearmed` timer whose row a later re-park consumed is kept `Consumed`, and its handle is marked `Selected` like
    any other `Consumed` handle;
  - a handle that names a kept job ticket or job result of an obsolete generation (not the registry's current
    generation for its key, or a key the registry no longer holds) refuses the whole command before Open: restoring
    it would hand the owner an invocation the model no longer waits for ("Obsolete job generations", below);
  - any other handle that names a row this Park keeps with reason `Consumed` goes to the `Selected` marking and its
    count check;
  - a handle that is none of these (an `Arrived` row with no disposition, an unknown handle, another instance's, a
    timer whose disposition has retired, below) refuses the whole command before Open.

  A refusal changes nothing and is recorded. **Retry-all** restores every `Consumed` row, except a job row of an
  obsolete generation, which its page clears, and re-arms, once each, every
  plan timer retained as `Suppressed`, before or after the sweep, recording `Rearmed`; a timer already `Rearmed` is
  not re-armed again, so retry-all writes at most one re-arm per plan timer.
- **Subscriptions** were removed by Park and are restored from the plan in the open commit. A wait that asked for kept
  events receives the events published while it was parked, within their keep window. A wait that did not is never
  matched to an event whose match step finished before the Open, as the owner decided that events are not kept by
  default; an event whose match step was still outstanding when the Open committed reaches it once ("Event
  eligibility", below).
  - **The restore counts them (proposed, fourth review R4-M2).** Each restored subscription is one of the plan's
    `W_max` waits and counts its 7,168 bytes toward `B_plan`, 9,728 when it keeps events, with the meet ticket once
    ("The plan's byte bound", above, sixth review R6-M2), so the Open's subscription, coverage and conflict writes are
    bounded with its re-arms and restarts, and a plan over either bound is refused with reason "plan".
  - **Kept-event catch-up runs after the Open, on the ordinary event path, in pages.** The Open reads no event. Each
    restored subscription row records its keep flag (`Subscriptions.Keep`, proposed), and when any of them asked for
    kept events the Open writes one meet ticket to the instance (`Kinds.Meet`) that names them by reference: the
    instance's creation and a cursor before the first match, in place of a list of rows. After the Open lands, the
    owner's meet path takes the ticket as it takes a meet ticket today (existing prototype behaviour: the turn's commit
    writes a meet ticket listing its kept subscriptions, `Turns.cs` 483–488, and `Worker.MeetAsync` reads the kept
    matches and adds each delivery after the commit, `Worker.cs` 1314–1339). Each step reads at most `P_page` kept
    matches of the instance's `Keep` subscriptions, in `(subscription key, wait, event id)` order after the cursor,
    adds each delivery by its digest identity (a delivery already present is a duplicate), within the step's byte
    allowance `B_step` ("The match step, in pages", below), and commits the next meet ticket with the advanced cursor
    in the commit that consumes the current one. A step whose commit outcome is unknown is taken again from the
    committed cursor, and a delivery it adds a second time is a duplicate. Each step also deletes the chain's
    coverage entries (`CatchUpCover`, below) for the subscriptions its cursor has passed, the step that writes no
    next meet ticket deletes all that remain, matches or none, and every next meet ticket carries the same entitlement
    point `P0` (fifth review R5-M2).
  - **One catch-up id across a meet chain (proposed, sixth review R6-M1).** The meet tickets of one catch-up form a
    chain, and the chain has one stable `CatchUpId`, which every coverage entry and every step uses:
    - **The id.** `CatchUpId` is the first meet ticket's identity in a fixed form: the lowercase hex SHA-256 over the
      canonical encoding (domain byte `C`, version 1) of that ticket's full key, 64 ASCII bytes on every provider,
      whether the first ticket is an Open's or a turn's (an issued id) or the start registrar's (`meet:`, below). Its
      fixed width keeps the coverage key within every provider's key widths. The first ticket records it in its
      header, and each step copies it unchanged into the next ticket.
    - **Coverage by the chain.** `CatchUpCover` is keyed by `(Incarnation, CatchUpId)` (below). Each step matches a
      subscription only through the chain's entry for that subscription's incarnation, deletes the chain's entries
      for the subscriptions its cursor has passed, and the step that writes no next ticket deletes every entry left
      with that `CatchUpId`. So coverage never depends on which ticket of the chain is current.
    - **A new key for each successor.** A successor's inbox key is new, never its predecessor's: `meet:` and the
      lowercase hex SHA-256 over the canonical encoding (domain byte `N`, version 1) of `(CatchUpId, step number)`,
      the step number 8 bytes big-endian from 1, 69 ASCII bytes, in the issued scope and addressed to its
      predecessor's target, so it stays in the same partition. The step that consumes a ticket inserts its successor
      in the same commit, so no consume-and-insert of one key occurs; the built commit refuses a sent row whose key
      exists (`InMemoryStore.cs` 118–119, existing prototype behaviour), which reusing the predecessor's key would
      meet. A retaken step derives the same successor key, so a repeat is a duplicate by key (case CC-1).
  - **Concurrent publishes.** Each side looks after its own commit: an event's match step (below) reads the
    subscriptions only after the event row's insert has committed, and the restore inserts its subscriptions in the Open and then reads
    the events, so every event a `Keep` wait is owed reaches it through at least one side, and the shared digest
    identity collapses the second. A delivery the catch-up adds during a recover drain carries no `ReleasedBy`, so it
    is withheld until Complete like any arrival during the drain.
  - **Event eligibility: what orders acceptance against registration (proposed, fifth review R5-M2).** The fourth
    round left the outcome to timing: a non-keeping wait restored while a publisher was paused between its event
    insert and its subscription read received an event that the same publish, undelayed, never delivered, and a
    keeping wait's catch-up compared the event's window with the time the catch-up ran, so a later page or a
    takeover lost an event the wait had been entitled to. The order comes from the event's match step, with no clock;
    a keeping wait adds an entitlement point that a monotone sweep horizon protects:
    - **The match step, in pages.** Every publish, a turn's or an outside one, is a publish ticket ("A turn's
      publish ticket carries the event in the same framing" above, and "An outside publish writes the same publish
      ticket" below). Its match step is the processing of that ticket by the owner of the ticket's partition, and it
      is paged, so a broadcast to any number of waits needs no single long step. The ticket carries a subscription
      cursor, starting before the key's first subscription in the key's `(instance, wait)` order. Each step reads at
      most `P_page` committed subscriptions of the key after the cursor, adds their deliveries within its byte
      allowance (below), and commits the next cursor with them; the step that reads a short page, fewer than `P_page`
      subscriptions, or finds none, and writes a delivery for every subscription it read, knows the key is exhausted
      and consumes the ticket. A turn's first step only
      inserts the event row from the ticket and rewrites the ticket without its payload, since the event row now
      holds it, and reads no subscription; the subscription pages start at the next step, after that insert has
      committed (an outside publish has written its event row already, below). So each side writes and commits before
      it reads: a keeping registration that commits after the event row's insert finds the event in its catch-up, and
      one that commits before it is read by a later page. On MongoDB this holds under the kernel's existing commit
      regime, majority-acknowledged commits and snapshot reads on the primary, so a committed step is in every later
      step's snapshot. It costs a turn's publish one extra commit, the insert-only first step, and one poll of latency
      before its first delivery (SB-5 run (x)). Each step is at-least-once: a step
      whose owner is lost runs again from the committed cursor, and a delivery it adds again is a duplicate by its
      digest identity. The step deadline and attempt count below apply per step.
      - **Broadcast ordering (owner, 22 November).** Each wait receives broadcasts in match commit order.
        Overlapping publications can commit their deliveries in different orders at different waits. The publish
        receipt acknowledges acceptance. Direct messages retain FIFO from one sender to one target.
        The SB-6 ordering run places two publication tickets on separate partitions. The first publication's
        byte allowance lets it reach one wait before the second matches both. The first publication's next page
        reaches the remaining wait. The test reads opposite broadcast orders from the two actors and confirms
        that two subsequent direct sends from one actor arrive in authored order. The in-memory run is in
        [FoundationEventSendChecks](../../prototypes/kernel-model/sim/FoundationEventSendChecks.cs). K4 also
        races two transactions matching the same wait. Pause the first while it assigns inbox order and let the
        second try to commit. The resulting delivery order must follow commit visibility. If the provider serializes
        order assignment for that wait, verify that the second transaction waits and that the first delivery stays first.
      - **A byte allowance per step (proposed, sixth review R6-M4).** `P_page` counts rows, and a delivery carries
        the whole payload, so 170 deliveries of a payload at the source limit would write about 2.6 GiB in one step.
        So each match step, which pages subscriptions, and each meet step, which pages matches, writes deliveries in
        cursor order while their stored bytes stay within `B_step` = 4 MiB (proposed, a coordinator design choice),
        and always writes at least one, so a larger delivery goes alone. The cursor advances to the last delivery
        written, a step consumes its ticket only when it has written every subscription or match of a short page, and the step deadline applies to each step. The number of steps follows
        from the payload: 100,000 small deliveries take 589 subscription pages, and deliveries above `B_step` one
        step each. Step duration and the partition's turn share are measured at K4 and K12 (case BA-1).

      Existing prototype behaviour: a
      turn's ticket is processed after its commit (`Worker.cs` 1323–1325), in one unpaged read: the publisher inserts
      the event before it reads every subscription of the key (`Store.cs` 287–295, `InMemoryStore.cs` 263–267 and
      281–285), and the commit that consumes the tickets follows the delivery inserts (`Worker.cs` 1326–1339), so a
      lost step leaves its ticket outstanding.
    - **Which waits a paged step must include.** Every subscription committed before the publish ticket's commit
      (for an outside publish, before the transaction that inserts the event row) is matched: it is a committed row
      at every step of the match step, which starts after that commit, so the step whose cursor reaches its position
      in the key's order reads it, unless its own instance removed it first, in which case it no longer waits. A
      subscription committed after that commit may or may not be matched: a page reads it only if it commits before
      the cursor reaches its position. That is the same latitude as today's single read, which sees a later
      subscription only if it commits before the read.
    - **A non-keeping wait** receives exactly the events whose match step reads its committed subscription row. An
      event accepted after the wait committed is matched, by the rule just stated, so invariant 13's "an event
      published after the wait committed is delivered once" holds with no clock. An event accepted before the wait
      registered reaches it, once, only when its match step is still outstanding when the wait commits and its cursor
      has not yet passed the wait's position: that window is the publish ticket's outstanding time, normally the
      publisher partition's poll and the steps themselves ("The match step, in pages" above), and unbounded under
      partition loss, a takeover or a backlog, since it lasts as long as the ticket is outstanding. A non-keeping wait
      has no catch-up, so nothing else reaches it. This is existing prototype behaviour for a live subscription: the
      publisher reads whichever subscriptions are committed when it reads (`InMemoryStore.cs` 281–285), and the turn
      contract says the waits that exist when an event is published get it either way (`ITurn.Publish`,
      `Contracts.cs` 128–134). So Park's rule above, that a restored wait which did not ask for kept events misses
      what was published while it was parked, reads exactly (owner decision, 18 November (sixth review)): it is
      never matched to an event whose match step finished before the Open, and it receives at most once an event
      whose match step was still outstanding when the Open committed. Against the 12 November card, "Events are
      not kept by default; keeping is per subscription" ([register](decisions.md#kernel-defaults)): nothing is
      retained for a non-keeping wait beyond the outstanding match step; an event row outlives its step only for
      its keep window and the catch-ups of keeping waits.
      - **The operational consequence (owner decision, 18 November (sixth review)).** No clock bounds this window.
        A match step delayed for any length of time delivers its event to a non-keeping wait registered at any
        later time while the ticket is outstanding, across a whole Park window included: a publisher whose ticket
        stalls until the publisher parks, and whose Park keeps the ticket for days before a resolution restores it
        (sixth review R6-M7), delivers the event, when the restored steps run, to every non-keeping wait on the key
        that committed in those days and whose position the cursor has not yet passed, although the event was published long
        before those waits existed. The same holds for a ticket held back by partition loss, a takeover or a backlog.
        The kernel adds no bound. Sipke accepted the rule with this consequence on 18 November, so it replaces the
        earlier coordinator choice.
    - **A keeping wait** has an entitlement point `P0` = max(`RegisteredAt`, `H` + 1 ms), where `RegisteredAt` is
      the store's time at the commit that registers the wait (the subscribing turn's commit, the recover Open that
      restores it, or the start registrar's write, below) and `H` is the sweep's horizon for the tenant (below), read
      in that commit. The commit records `P0` on the wait's meet ticket and in its coverage entry (below). The wait is
      entitled to the events of its keys with `KeepUntil` ≥ `P0` that its catch-up finds, and to every event whose
      match step reads its row. A clock that steps back cannot make it claim a swept event: every event the sweep
      deleted has `KeepUntil` ≤ `H` < `P0`. One millisecond past `H` is needed because the sweep deletes events whose
      `KeepUntil` equals `H`. The same bound has a cost after a step-back: a live event whose `KeepUntil` ≤ `H`, one
      published after the clock stepped back (accepted at 98 with `KeepUntil` 99 while `H` = 100, say), is excluded
      from a new wait's catch-up and treated as expired. It still reaches every wait whose registration precedes its
      match step, through the publisher side. Each event's `KeepUntil` is `AcceptedAt` plus its window, where `AcceptedAt` is the
      store's time at the commit that accepts the event (below), carried in the publish ticket row's `AcceptedAt` (a
      nullable column set only on publish tickets) and copied into the event row; today a turn computes `KeepUntil`
      from `ITurn.Now` (`Turns.cs` 432). `AcceptedAt` is used for nothing else. A repeated idempotent subscribe keeps
      the row's first `RegisteredAt`.
      - **A zero keep window.** Such an event has `KeepUntil` = `AcceptedAt`. Both rules above apply to it unchanged:
        it reaches a keeping wait through the publisher side when the wait registered before its match step, and
        through the catch-up only when `KeepUntil` ≥ `P0`. Without a clock step-back that second case needs the
        wait's registering commit to be stamped no later than the event's acceptance, and so in practice it does not
        arise. After a step-back it can: an event accepted at 100 whose match step finished before the wait
        registered, and a registration stamped 99 with `H` = 0, give `P0` = 99, and the catch-up delivers the event
        once. By the stored stamps the wait registered inside the window, so that delivery is within the rule
        (SB-5, run viii).
      - **Coverage entries, one per catch-up chain.** Each subscription row has an `Incarnation`, a 16-byte id the
        store generates at its insert, so a row removed and created again is a new incarnation. Each catch-up chain
        has one coverage entry per subscription it covers, a row `CatchUpCover (Incarnation, CatchUpId, Key, P0)`
        keyed by `(Incarnation, CatchUpId)`, `CatchUpId` being the chain's stable id ("One catch-up id across a meet
        chain" above; sixth review R6-M1 replaces the fifth round's key by ticket). The entry is inserted in the same
        transaction as the chain's first ticket, and only when that ticket row was actually inserted, so a repeated
        registration whose ticket is a duplicate adds no entry. A catch-up step matches a subscription only through
        its chain's entry, with that entry's `P0`, so a chain never matches a later incarnation it did not register.
        The chain's steps delete its entries, by its `CatchUpId`, as the cursor passes each subscription, and the
        step that writes no next meet ticket deletes all that remain, matches or none; Park, unsubscribe and
        termination delete a removed incarnation's entries with it. The sweep's coverage is an entry for E's key with
        `P0` ≤ `E.KeepUntil` (SB-5, runs vii and ix).
        - **At most one entry per incarnation (proposed, sixth review R6-M2).** A registering commit adds an entry
          only for an incarnation that holds none, and writes no meet ticket when it adds no entry; the start
          registrar also starts no chain while its earlier chain's ticket is a dead letter, whose replay continues
          that chain. Nothing is lost: an existing entry means its chain has not yet passed the subscription, and
          its `P0` is at most the new one (`RegisteredAt` is the row's first and `H` never decreases), so the sweep
          keeps, and that chain delivers, every event the new registration would be entitled to. So an unsubscribe
          deletes at most one entry, and a Park or a termination at most one per keeping subscription, 2,577 at the
          plan bound; the delete's cost is measured at K4 (case OA-1).
    - **The sweep's horizon and coverage check.** `H` is one row per tenant, written only by the sweep: the greatest
      `KeepUntil` it has deleted, never decreasing.
      - **Batches, filter then limit.** The sweep deletes in batches of at most `P_page` events of a tenant, each
        batch in its own transaction, so the horizon lock is held for one batch only. Each batch locks the tenant's
        horizon row (`FOR UPDATE` or the provider's equivalent), deletes in one statement, raises `H` to at least the
        greatest `KeepUntil` it deleted and commits. The statement filters first and limits after: `DELETE` of the
        events whose `(Key, Id)` is in (`SELECT` of the tenant's events `WHERE KeepUntil < now AND NOT EXISTS` (a live
        row of E's publish ticket: pending, within its window, Park-kept, or an outside dead letter within its
        deduplication window) `AND NOT EXISTS` (a coverage entry for E's key with `P0` ≤ `E.KeepUntil`) `ORDER BY
        KeepUntil` limited to `P_page`), in each provider's form (`TOP` on SQL Server, a derived table on MySQL),
        measured at K4. So a front of covered events never stalls the uncovered ones behind it, and the coverage check
        and the delete read one snapshot. The events row has the `tenant` column every tenant-keyed record gets
        ([tenancy](tenancy.md#the-tenant-column)), and the batch reads it through an index on `(tenant, KeepUntil)`
        on every provider. The publish-ticket probe is one point lookup: the event row stores its ticket's full key,
        `Events.Ticket` (the issued id for a turn's publish, the `pub:` id for an outside one), and the check counts
        the ticket row while it is pending, `Done` within its deduplication window or kept by a Park. A turn's ticket
        set aside as a dead letter does not count; an outside publish's dead-lettered ticket counts until its
        deduplication window ends, so it holds its event row, the receipt, that long (below).
      - **Lock order and isolation.** Every registering transaction of a keeping wait takes, at its start and before
        it writes any subscription, ticket or coverage row, the horizon share locks of every tenant it registers for,
        in order of tenant id (`FOR SHARE` or the provider's equivalent); a commit batch can carry turns of several
        tenants. A sweep batch takes its one tenant's exclusive lock first. So no two transactions wait on horizon
        rows in opposite orders. Both run at read committed, and each statement after the locks reads a snapshot taken
        after they were granted: PostgreSQL's read committed takes a snapshot per statement; on SQL Server either
        locking read committed or RCSI serves, since RCSI's statement snapshot is taken when the `DELETE` starts,
        after the lock; MySQL's InnoDB at `READ COMMITTED` reads the latest committed rows for each statement; SQLite
        serializes writers. So either the registration commits first and the sweep's delete sees its coverage entry,
        or the sweep's batch commits first and the registration's `P0` is above the new `H`. A K4 run checks the
        isolation and the absence of deadlocks on every provider (SB-9).
      - **MongoDB (revised, sixth review R6-M3).** There is no `NOT EXISTS`. Coverage is per key, so the conflict
        document is per tenant and key hash, `eventConflict (tenant, hash of key)`, beside the tenant's horizon
        document, which also holds a sweep cursor, a position in `(KeepUntil, Match)` order. A batch is a transaction
        that writes the horizon document, reads at most `P_page` expired candidates past the cursor, probes each
        one's publish ticket and coverage at the transaction's snapshot, passes over the held or covered ones with no
        write, writes the conflict documents of the keys it is about to delete and only those, deletes them, raises
        `H`, and records the cursor after the last candidate it read. A batch that reads fewer than `P_page`
        candidates sets the cursor back to the start. So every batch deletes or moves on, a covered front never stalls
        the sweep, and a batch writes at most one horizon document and `P_page` conflict documents. A registration on a
        deleted key that commits after the snapshot conflicts on that key's document, so the batch aborts and runs
        again; one on a passed-over key needs no conflict, since the batch writes nothing for it. The relational
        filter-then-limit statement above is unchanged. Qualified at K4, with the batch work and abort rate measured
        at K12 (case MW-1).

        A registration writes the conflict
        document of each key it registers inside its transaction. So a registration and a batch that touch one key
        conflict, and so do two registrations of one key; registrations on other keys do not. The server aborts one
        of two conflicting transactions. A `TransientTransactionError` means nothing landed, and the owner retries
        the whole transaction; an `UnknownTransactionCommitResult` means the commit's outcome is unknown, and the
        owner retries the commit itself. Every retried transaction runs the fence again, so a retry after the lease
        has expired or another owner has taken the partition answers not landed. Retries back off and are bounded;
        when they run out, the commit counts as not landed, or as unknown for an unknown commit, and the owner resets
        as for any such commit. Liveness: a commit can lose to repeated conflicts on one key until its retries run
        out; nothing bounds that beyond the retry count, and K12 measures it. A conflict aborts the whole partition
        commit, so the other instances' turns in that batch, of any tenant, are retried with it (SB-9).
      - **Cost.** Two indexed probes per candidate event inside the batch statement, one horizon-row write per batch,
        and one horizon-row read per tenant (on MongoDB, one conflict-document write per key) per registering commit
        of a keeping wait; on MongoDB, also up to `P_page` conflict-document writes per batch, only for the events it
        deletes. Indexes: `Events (tenant, KeepUntil)`, on MongoDB `(tenant, KeepUntil, Match)` for the sweep cursor
        (a condition, qualified at K4), `CatchUpCover (Key, P0)` and the ticket row by its full key. Today the sweep deletes every event whose window has passed (`InMemoryStore.cs` 348–356).
    - **Holds are bounded.** The meet and publish tickets are kernel work that runs whatever the instance's state.
      Existing prototype behaviour: a polled ticket goes to the meet path before any instance slot (`Worker.cs`
      640–644), and `Worker.MeetAsync` checks only ownership and the ticket's mark (`Worker.cs` 1315–1320, `Awaits` at
      771). The Worker withholds a suspended instance's deliveries (`Withheld`, `Worker.cs` 725, applied in the due
      selection at 827–831), and the meet path does not consult it. A step only adds deliveries, and those are
      withheld by suspension or an open drain like any input. So a coverage entry lasts only while its catch-up is
      being processed, at most `P_page` matches per step; an owner loss resumes from the committed cursor; and Park,
      unsubscribe and termination remove the subscription row and its entries with it. A suspended instance's
      catch-up runs to its end and releases its keys.
      - **When a step fails or stalls.** Today `MeetAsync` runs every pending ticket inside one `try` (`Worker.cs`
        1317–1334): one failing ticket leaves all of them for the next call, so one bad ticket can hold every other
        ticket of the partition. Proposed: each ticket's step is isolated, and each step has a deadline (proposed: one
        lease, 10 s). Only a step that commits no progress, one that throws or reaches its deadline before its commit,
        counts as a failed attempt, in a fenced write to the ticket's `Attempts`; a step that commits its page costs
        no attempt, so a large broadcast or catch-up runs to its end however many steps it takes.
        At `MaxAttempts`:
        - an instance's meet ticket parks its instance, and Park removes the subscriptions, so their coverage entries
          and holds go with them. This park is outside the cohort deferral of the poison comparison, since the
          ticket is no model input: a stalled catch-up parks its instance at `MaxAttempts` even while a recover drain
          is open, as a re-park of the open recover, as defined. The in-memory recovery path records failed steps by
          comparing the pending ticket key and its previous attempt count under the fence. When the owner stops
          waiting, a committed page and the failed-attempt write compete on that exact row. A page that lands removes
          the old ticket. A failed-attempt write that wins invalidates a late page with the previous count. Only a
          chain retaining coverage can re-park the instance. An exhausted superseded chain settles without touching
          the instance or its new waits. The step budget is capped at one lease and leaves a reserve inside the
          current owner's local lease deadline for the failure write. The owner marks the ticket's trial at the
          start of its isolated step in place of the ordinary seen mark. A take or retake charges a reservation
          that still has its pending ticket at the previous epoch. A landed page removed that ticket and leaves the
          successor uncharged. A known failed step clears the trial when it records the attempt, so a later take
          cannot charge it again. The physical-provider deadline and query-cost cases remain at K4 and K12.
        - a start subscription's meet ticket has no instance to park: it is set aside as a dead letter with an alert,
          and its coverage entries are released, so the sweep may delete the events it still owed;
        - a turn's publish ticket parks its publishing instance. Park keeps the ticket, so the sweep's ticket check
          keeps its event for the life of the Park, and it shows in the parked-work read model; resolving the Park
          ends the hold, a recover running the remaining steps and a discard, expiry, terminate or delete turning the kept ticket into a dead letter (sixth review R6-M7). A publisher that has
          already ended (Finished, Faulted or deleted, as when a turn publishes and ends in the same turn) has nothing
          to park, so its ticket becomes a dead letter as an outside publish's does, with the alert, the dead-letter
          read model and replay, and its event's hold ends with its keep window. If its first step never committed, the
          event row does not exist yet, and the ticket still carries the payload, so a replay runs that first step from
          the ticket and inserts the event row;
        - an outside publish's ticket is set aside as a dead letter with an alert, and its event row, with its
          `Done` deliveries, is kept until the later of its keep window and the outside deduplication window, which
          runs from the publish's acceptance, then swept as usual.
        A dead letter is the stated exception to invariant 13: an event it held back is delivered to none of the
        waits the remaining steps would have reached, until an operator acts. Dead letters show in a dead-letter read
        model, by tenant, kind, key and attempts, and an operator replay command runs the ticket's remaining steps
        again from its committed cursor (proposed, K11). A start subscription's replay is a registering write: it
        takes the tenant's horizon share lock first (on MongoDB, it writes the key's conflict document), then reads
        `H` and writes its ticket and its coverage entries again with `P0` = max(the original `P0`, the current `H` +
        1 ms), so it never claims an event swept while it was set aside or while it replays; an outside publish's replay matches only while its event row of the ticket's incarnation exists
        and answers "gone" after the sweep. Both count in `elsa.kernel.events.ticket_failures` (proposed, by ticket kind), which alerts. A restored meet or publish ticket is
        outside the drain's credit and bound, as every ticket kind is (above), so a broadcast's restored ticket runs
        its steps while the drain's pages and Complete proceed. So a
        stalled step holds its key's expired events for at most `MaxAttempts` deadlines plus the polls between them,
        and the events retained behind it are those published on that key in that time (SB-7, runs 3 to 5; SB-8,
        run 5).
    - **Done deliveries are kept with their events.** An event row may now be held after its window, and a later
      catch-up page must not re-add it to a wait that consumed it and stays subscribed. So the ordinary sweep deletes
      a `Done` event delivery only when no event row has its `Match`: each event row stores its match digest
      (`Events.Match`, proposed, indexed), and the message sweep's predicate gains `NOT EXISTS` over it, one indexed
      probe per delivery row it removes. A catch-up page that reaches such an event finds the delivery's `Done` row and
      adds a duplicate, which the store refuses by full key. Since `Match` includes the incarnation (sixth review
      R6-B2), the `Done` row is kept while its own event row, the same publication, exists, and a later publication of
      the same `(Q(t, key), event id)` neither keeps it nor meets it: its deliveries have other full keys.
    - **A start subscription is a keeping wait.** Today the registrar inserts the subscription and then reads the
      kept events of its key in its own retry loop (`Events.SubscribeAsync` and `CatchUpAsync`, `Store.cs` 302–315),
      so an event matched before the insert and swept before the read never starts its instance. Proposed: the
      registrar's write is one transaction, under the same horizon share lock, that inserts the subscription with
      its `RegisteredAt` and `Incarnation`, a kernel-issued meet ticket and its coverage entry with `P0`. The
      ticket's id is `meet:` and the lowercase hex SHA-256 over the canonical encoding (domain byte `S`, version 2) of
      `(Q(t, key), wait, incarnation)`, 69 ASCII bytes, addressed by placement of that digest; a repeated
      registration of the same incarnation derives the same id, so while the first ticket is outstanding its insert
      is a duplicate and it adds no coverage entry. Its successors take new keys under the chain's one `CatchUpId`
      ("One catch-up id across a meet chain" above, sixth review R6-M1). The owner of that partition pages the catch-up on the meet path,
      so the sweep's coverage check holds the start's events until its catch-up passes them. Case SB-8.
    - **Against invariant 13 and the owner's card.** Invariant 13
      ([DESIGN.md](../../prototypes/kernel-model/DESIGN.md), "Invariants") says that for waits that ask for kept
      events, an event and a wait both committed within the window are delivered once, and for the others an event
      published after the wait committed is delivered once. The second half holds by the match step, with no clock.
      For the first: if E's match step reads the wait's row it delivers E; otherwise the step finished before the
      wait committed, E's row was still within its window then, so `KeepUntil` ≥ `P0`, unless a step-back put the
      registration at or below a horizon that had already passed E, and the coverage check keeps E until the
      catch-up finds it. Either way E is delivered once, by whichever side matches first. A dead letter is the one
      stated exception ("When a step fails or stalls" above). The card holds as stated
      above. Cases SB-4 to SB-9.
    - **An outside publish writes the same publish ticket.** Today an outside publish inserts the event row and then
      matches in its caller's own retry loop (`Events.PublishAsync`, `Store.cs` 287–295), and every provider's match
      reads the stored event row and builds each route from it, returning nothing when the row is gone
      (`InMemoryStore.cs` 281–285, `EfStore.cs` 690–699, `MongoStore.cs` 461–462); routes must come from the persisted
      rows (R4-M1, above). So the caller depends on the row, and a caller that pauses between insert and match past
      the window could find it swept (`InMemoryStore.cs` 348–356). Proposed: the entry point writes the publish ticket
      a turn's `Publish` writes, in the same framing, and inserts the event row itself:
      - **Identity.** The ticket is issued by the kernel in its issued scope, so ingress cannot supply one (an outside
        row of a ticket kind in scope 0 is a supplied ticket and is discarded, `Turns.cs` 110–111, `Worker.cs` 640).
        Its id is `pub:` and the lowercase hex SHA-256 over the canonical encoding (domain byte `P`, version 2) of
        `(Q(t, key), event id, incarnation)`, the incarnation its event row's: 68 ASCII bytes on every provider, 136 in
        SQL Server's UTF-16, inside every key width. With the fifth round's version 1, over `(Q(t, key), event id)`
        alone, an admission after the sweep had removed the event row, while the earlier ticket's `Done` row still
        waited for the message sweep, would have derived that row's key, and the new ticket's insert would have been
        refused as a duplicate (sixth review R6-B2).
        A verbatim key and event id would not fit: they alone can fill SQL Server's 450 units, and MySQL's text id
        allows 255.
      - **Placement and admission.** It is addressed to the kernel's publisher address in the virtual partition that
        the placement hash gives its digest. The publish takes its tenant's `Publish` admission, as today
        ([fairness](fairness-and-backpressure.md#admission-at-the-edge)), and, being one outside row in one known
        virtual partition, the partition high-water rule as a keyed command does: above the mark a new insertion is
        refused with `InboxFull` before any row is written, while a duplicate is answered from its existing receipt
        first and is never refused by the mark (seventh review, condition [C2](../program-goals/clean-slate-kernel.md#gate-2-conditions), K4). Its fan-out into waiting
        instances is never refused.
      - **Processing, and one event per provider-equal key.** On SQL Server and MySQL two spellings such as `Order-7`
        and `order-7` name one event key under the pinned collation, while the ticket id hashes the supplied
        spelling, so the ticket cannot be the receipt. The event row is: the entry point's one transaction inserts the
        event row by insert-or-read under provider equality on `(Q(t, key), id)`, as the caller-key binding row does
        ([tenancy](tenancy.md#names-one-namespace-per-tenant)), with its `AcceptedAt` (that transaction's time), its
        `KeepUntil`, `Ticket` and a new `Incarnation`, and only when the insert added the row it also writes the
        ticket. When an equivalent row exists, the publish is a duplicate: it writes nothing and answers with the
        existing event's identity and incarnation. Two
        concurrent first uses serialize on the unique key, and the second reads the first's row. The ticket then
        carries the header and no payload, since the match step reads the stored event row, as routes must. The
        partition's owner takes the ticket on the meet path as it takes a turn's (`Worker.cs` 640–644, 1323–1325):
        its match step reads the event row and the committed subscriptions, adds the deliveries and consumes the
        ticket.
      - **Turn and caller event ids are disjoint (owner decision, 18 November (seventh review), R7-M1).** A turn's
        event id starts with the reserved code unit `~` ("A generated event id counts inside `C`" above), and the
        turn's `Publish` returns it with the prefix. The outside publish refuses with `InvalidEventId` any caller event
        id that is provider-equal, under the `Events.Id` column's pinned equality, to an id starting with `~`: nothing
        is written, no receipt is issued, and the refusal is not retryable. On the in-memory store, PostgreSQL, SQLite
        and MongoDB that is the first code unit; on SQL Server and MySQL the comparison runs under the pinned collation
        ([tenancy](tenancy.md#names-one-namespace-per-tenant)), so a fullwidth `～` (U+FF5E) or a leading character
        the collation ignores is refused too (a condition, qualified at K4). So the two producers never claim one
        `(Q(t, key), id)` row. Lookups, digests, the `pub:` id and the sweep keep their shape, outside retries and
        post-sweep reuse keep their rules, and no cross-producer duplicate rule exists. `~` is a plain character in
        the stored rows: the kernel's key codec tags a leading `~` only in a JSON property name (`DurableKeyJson.cs`
        18–25), and an event id is written as a value (`EventRow`, `Store.cs` 36–37). The prefix adds one code unit
        to a generated id, two bytes on SQL Server, so EM-4's figures move by that, measured at K4. Case MX-1.
      - **Receipt and retries.** The ticket is an outside add, so its consumption leaves a `Done` row for the standard
        outside-message deduplication window, the `keep_until` that outside commands and operator messages use
        ([ADR 0099](../adr/0099-clean-slate-execution-kernel.md), the tenant-scoped idempotent submission row), and the
        sweep keeps the event row while that ticket row exists, so the event row stays the receipt for the whole
        window. A retry within the window, in any equivalent spelling, finds the event row and is a duplicate. A retry
        within the window after the ticket was set aside as a dead letter finds the event row too, since the sweep
        keeps a dead-lettered outside ticket's event row until the window ends, and the insert-or-read under the pin
        finds it in any equivalent spelling: the retry is answered as a duplicate with the dead-lettered status and
        writes no new event and no new ticket. After
        the window a retry is out of contract, and the caller must not retry; if one arrives while the event row still
        exists (a keeping wait's catch-up holding it), it is a duplicate the same way and the existing row's
        `AcceptedAt` and `KeepUntil` win; after the sweep it is a new publish.
      - **The receipt names the publication (proposed, sixth review R6-B2).** The answer carries the stored event
        identity, `(Q(t, key), event id)` in its persisted spelling, its incarnation, and whether this request
        accepted it or found it (with the dead-lettered status where that applies). A duplicate within the window
        answers the existing incarnation; an admission after the sweep answers a new one, so a caller can tell a
        new publication from the old one, and every waiting subscription, a continuing keeping wait included,
        receives the new publication once, whatever is left of the old one's deliveries (case EI-1).
      - **In-memory qualification.** The typed outside send inserts the event and its payload-free ticket under one
        store lock. On each page the owner reserves the ticket, compares its sequence and attempt under the fence,
        and commits the delivery rows, any start creators and references, and the cursor together. A continuing
        ticket keeps its key and receives a new sequence, so a delayed request for the previous page cannot use
        a reservation on the next one. At exhaustion the bound receipt reports `DeadLettered`. The focused
        `--paged-publication-check` runs the real Worker. Provider locks and sizes remain at K4, step and fairness
        costs at K12, and the alert, dead-letter listing and replay at K11. The turn publisher and the request/reply
        route have separate cases.
      - **Cost.** One small ticket row per outside publish, inserted and consumed and kept `Done` for the window; the
        payload is written once, in the event row, which moves from after the match to the publish's own
        transaction. The delivery waits for the owner's next poll. The deliveries move from the caller to the owner
        and stay one per matching wait. Case SB-6.
    - **Rejected options (coordinator design choices).**
      - **Stamps with a clock allowance.** This round's first answer stamped `AcceptedAt` and `RegisteredAt` from the
        store's time and delivered to a non-keeping wait when `AcceptedAt` ≥ `RegisteredAt` − ε, ε a lease. It cannot
        give commit order, as CodeRabbit's review of #4787 showed by three traces. A relational stamp is taken at a
        statement, so a publish stamped at 100 that commits at 130, after a subscription committed at 120, is refused
        under ε = 10 s although it was published after the wait committed, which breaks invariant 13. A sweep at
        database time 101 deletes an event kept through 100, the clock steps back to 99, and a keeping wait registered
        at 99 claims the swept event. And one suspended instance's outstanding ticket held a global low-water mark,
        so retention grew without bound. The match step, the horizon `H` and the per-key coverage check replace it.
      - **A catch-up for every subscription.** Every non-keeping subscription would also catch up recently accepted
        events, so its outcome would not depend on whether a match step was outstanding. Each subscribing commit would
        write a meet ticket, the owner would make one kept-match read per subscribing commit, and every event would be
        retained past its match step. Rejected for that cost: one ticket row and one read for every `Subscribe`, and
        retention for every event, which the 12 November card does not ask for.
- **Timers and jobs are decided by point lookups that cannot go stale.** For each timer and job ticket in the plan,
  layer 2 maps the model's id to the full key of the instance's own send and looks that row up. Only the instance's own
  turns change its kept rows (the sweep skips them), and its turns are serial under the partition fence, so what the
  lookup sees is what the open commit and the later drain pages write against. The writes still carry the lookups as
  preconditions, as a defence against a defect: a restarted ticket is a create-only insert under its new generation's
  key, a re-arm is the count-checked delete-and-insert, or the insert, below, and each drain update has its count checked. A count mismatch answers "not landed", so the owner resets and
  computes the step again from the rows as they are, under the same operation id; a failed create-only insert is a
  definite error, which parks the instance again under the commit's failure rule, and the operator can repeat the
  recover.

  **Retained dispositions.** A decision a resolution takes about a timer or ticket must survive an interruption, or a
  second resume could undo it. `ParkRecord.Dispositions` keeps, for each timer and ticket of the plan, the decision
  taken under this Park: `Suppressed` (a Park-consumed timer that a resume left unarmed), `Rearmed` (a timer this Park
  has armed once) or `Restarted(g)` (a job restarted under ticket generation `g`). Open writes them; a re-park keeps
  them; the Park's resolution clears them.
  - **Job ticket generations.** The instance row keeps a per-creation `JobGeneration` counter, which no Park
    resolution resets, and its job registry (`Jobs`) records each in-flight job's key with its generation. Every
    `StartJob`, fresh or a recover's restart, takes the next value of the counter, and the ticket id carries it beside
    instance, key and creation. So a ticket never reuses a generation, across Parks or across fresh starts of the
    same key, and never collides with an earlier ticket's done row still within its window. The model never sees the
    generation: the job context and `job-finished` carry the model's own key.
  - **The ticket key is injective in (instance, creation, job key, generation)** (proposed, fifth review). Existing
    prototype behaviour: for a positive creation a ticket's id is `{origin}!{key length}_{key}`, under the scope of the
    origin's length plus one, so the scope fixes where the origin ends and the length prefix fixes where the key ends;
    for creation 0 it is `{instance}!{key}`, with no length prefix (`Worker.TicketId`, `Worker.cs` 295–296;
    `InboxKey.Ticket`, `Messages.cs` 31). Generations exist only on rows with a positive creation: a restart under a
    positive `JobGeneration` happens only at a creation-qualified row, and release 1's key admission gives every row
    a positive creation once K3 lands ([data lifecycle](data-lifecycle.md#proposed-the-qualified-delete), "What the
    store must hold"). A creation-0 row, which only the prototype has, never has a positive generation, so its
    tickets keep the built `{instance}!{key}` id. At a positive creation, generation 0, a creation's first ticket,
    keeps the built id exactly, and a positive generation g appends `#g` and g's decimal digits after the
    length-prefixed key: `{origin}!{key length}_{key}#g{g}`. Since the length prefix ends the key, no job key can
    spell that component, so (`j.1`, 0) gives `…!3_j.1` and (`j`, 1) gives `…!1_j#g1`, and no two (job key,
    generation) pairs share an id. The result row's `/result` suffix
    comes after both (R5-M4). The cascade's cancel and every restart derive the key by this one rule.
  - **Each disposition names the exact wait.** A timer's disposition is keyed by its issued id, unique per send
    (instance origin, revision and ordinal, `SendNext` in Turns.cs). A job's is keyed by the model's job key, the
    name the plan uses, and `Restarted(g)` holds `g` in plain. At Open a job disposition applies only while the
    registry's current generation for that key is `g`; a later `StartJob` under the same key has a higher generation,
    so the old disposition is obsolete and the new job is decided by its own ticket. Layer 2 stores each key as the
    SHA-256 digest of its canonical encoding (a timer's full key as the delivery handle encodes it, or the job key),
    whose collisions are cryptographically negligible. A packed entry (32-byte digest, 1-byte tag, 8-byte generation) is 41 bytes, and the
    proposed `D` = 64 bytes allows for the provider's framing; a natural BSON layout is near 69 bytes, so the K4
    measurement may raise `D`, and `R` with it.
  - **Obsolete dispositions retire at each Open.** Every recover Open replaces the set, in its own commit, with the
    dispositions of the waits its plan names. One whose timer the plan no longer names, or whose job key the plan no
    longer names or names at a newer generation (the model finished or cancelled that wait during an earlier drain),
    is dropped. So the set never exceeds the current plan, at most
    `W_max` entries, however many times the drain is interrupted.
  - **A retired handle revives nothing.** A handle of a retired timer names no disposition. If its row is ordinary
    done or swept, the handle is refused before Open. If its row is still kept `Consumed`, it is an ordinary
    `Consumed` handle: the drain restores that delivery, and the model, which no longer waits for it, resumes no frame
    (Native's timer handler finds no wait for it). No handle can name a newer wait, since a handle encodes the full key.
  - **The envelope bound and its headroom.** `ParkRecord` encodes in at most `R` = 56 KiB plus `D` per disposition:
    the Park identity, the Resolution with its cursor and gate handle (each at most `M`), the operation id, the
    generation and the controls' keys fit in 56 KiB. With `W_max` entries that is 312 KiB (proposed, like `D`). **One
    row cap on every provider** (proposed, measured at K4): every instance row and every message row is at most
    16 MiB, MongoDB's document limit, measured in the provider's stored encoding of the row (SQL Server's UTF-16 doubles
    JSON text, so the same payload counts twice there). For the instance envelope, `ParkRecord` is included: every
    commit, on every provider, refuses as a definite error an instance envelope, measured without `ParkRecord`, above
    16 MiB less 312 KiB, and `ParkRecord` counts only against its own 312 KiB. So a recover Open's `ParkRecord` always
    fits beside the committed state on every provider, MySQL included, where an instance just under
    `max_allowed_packet` would otherwise park and then fail every recover Open by the 312 KiB the Open adds. Within
    that bound, a live row's check also reserves its model's terminal growth, its registries at the marker's
    sizes and its other variable-length fields at their encoded size, with the fixed reserve, so its ending commit
    always fits ("The ending commit always fits" above, sixth review R6-B1, seventh review R7-B1). For a
    message row, admission caps the whole encoded row: the projected fields are at most `M` (above); the other fields
    are at most `F` = 24 KiB in the widest stored encoding (proposed), from `Target` (`N` + 70), `ReplyTo` (`N` +
    160), the trace (576), the start's `Model` and `Definition` (`N` each), its `Parent` (`N` + 70) and its creation
    intent (`N`), and fixed-width fields (64), 7,084 bytes in WTF-8; and `Payload` and `Start.Input` together are at
    most 16 MiB less `M` less `F`. A larger value goes by reference ([write
    volume](write-volume.md#decisions-owner-1617-november-2026), decision 1a). Measured at K4: the encoded
    `ParkRecord` at `W_max` on each provider, an envelope at the reserve on each, and a message row at the cap in each
    provider's stored encoding (D-3, S-6). At typed ingress the `StartSpec.Type` uses the same `N` bound.
    For a typed start, replace the legacy 1 KiB definition allowance in this budget with a reserve for the provider's
    maximum encoded 16 KiB of UTF-8 `TypeData`. K3 applies this envelope at admission and when composing a start delivery.
    K4 qualifies the resulting message, event payload and start input budgets together (EM-3 and S-6), so a maximum-size
    admitted event can still be delivered to a maximum-metadata event-start subscription.

  The lookup reads the disposition first, then the row. For a job the row is its ticket and, when the ticket is done
  or absent, the ticket's minted result row `{ticket id}/result`, found by a second point lookup by full key; a
  current-generation result row decides by the rows below as a model input, and the job is not restarted (fifth
  review, integration review):

  | What the lookup finds for a timer or ticket the plan waits on | `resume` | `retry` |
  |---|---|---|
  | Pending | Stays | Stays |
  | Kept, `Arrived` (a timer that fired, or a job result that came, while parked) | Restored by the drain | Restored by the drain |
  | A job whose ticket is done or absent and whose result row `{ticket id}/result` of the registry's current generation is pending | Stays; the job is not restarted | The same |
  | A job whose ticket is done or absent and whose result row of the registry's current generation is kept `Arrived` | Restored by the drain; the job is not restarted | The same |
  | Kept, `Arrived`, a job ticket of the registry's current generation g (the parked row consumed it before it was entered, fifth review) | The job restarts under g + 1 and is recorded `Restarted(g + 1)`; ticket g is cleared as obsolete by its page | The same |
  | Kept, `Consumed` or `Selected`, and the selection names it, with no `Suppressed` disposition | Not applicable | Restored by the drain |
  | Kept, `Consumed`, not selected | Cleared; a timer stays unarmed and is recorded `Suppressed`; a job restarts and is recorded `Restarted(g)` | The same |
  | Disposition `Suppressed`, whatever the row now is (still kept because an interruption came before its page, ordinary done after a page cleared it, or swept); the disposition takes precedence over the row | Stays unarmed | Stays unarmed, unless the selection names it or the mode is retry-all: then re-armed once, and recorded `Rearmed` |
  | Disposition `Rearmed` | The re-arm never runs again; the timer's row decides by the rows above (pending: stays, waiting for Complete with a null `ReleasedBy`, or released by a page as `Stale` with an older one; kept after a re-park consumed it: as a kept row) | The same; a selection naming it is accepted as a no-op ("already re-armed") while its row is pending or kept `Arrived`, and marks it `Selected` if a re-park consumed it |
  | Disposition `Restarted(g)`, and the registry's current generation for the key is `g` | The generation-`g` ticket's row decides, by the rows above | The same |
  | No disposition and no `KeptBy`: ordinary done or absent (for a job, its ticket and its result row both) | A timer is re-armed and recorded `Rearmed`; a job restarts. Done and absent are treated alike, so the sweep's timing cannot change the outcome | The same |
  | A publish or meet ticket kept `Consumed` (kernel work in no plan, found by the page that reaches it; sixth review R6-M7) | A publish ticket is restored by the drain with its committed cursor, before or after the model ends; a meet ticket is cleared | The same, whether or not the selection names it, and under retry-all. Only discard, expiry, terminate and delete make a kept publish ticket a dead letter and clear a kept meet ticket |

  The Open's restart decisions are in force before any page decides what is obsolete: the Open's commit records
  each `Restarted(g + 1)` and moves the registry's entry for that key to g + 1, so every page that later reaches
  ticket g finds it of an obsolete generation and clears it. Ticket g is therefore never restored beside g + 1, and
  the job runs at most twice, once as g before the Park and once as g + 1 (fifth review).

  A kept deadline may sort before its current-generation ticket, whether retry-all restores the ticket or an explicit
  retry selects it. The page that releases the deadline also releases the paired ticket in the same fenced commit. The match
  uses the creation, generation, issued deadline id and its original due time. Both rows count against the page
  limit. If only one position remains, the page ends before the deadline. The cursor advances to the deadline's
  position, and the later ticket is already released when a subsequent page reaches that position. The ticket is
  exempt from handoff credit under the usual ticket rule. This lets the deadline settle the pending ticket as a
  timeout while preserving the absolute due time and the current job generation. The in-memory reference store
  projects the linkage from the metadata saved with each row at insertion. Physical provider projection and its
  cost are part of K4 qualification. If the deadline is absent or its ordinary done marker remains, the fenced Open
  re-arms it from the retained ticket at the same issued key and original absolute time. A private deadline kept
  `Consumed` that the current retry would otherwise clear also re-arms. The plan charges the re-arm and the worker
  refreshes that key so a stale cached copy cannot replace the reissued deadline.

  **The re-arm.** A timer is re-armed under its own full key from one durable source, whichever branch the
  disposition step's point lookup chose. The plan's `TimerSpec`, from the model's committed state, gives `Kind`,
  `Payload` (encoded as `Send` encodes it) and `Due`, the original absolute deadline. The instance row gives the rest:
  layer 2 maps the spec's id to the full key (the id, the issued scope, no match) and refuses as a definite error an id
  whose origin is not this instance's creation; `Target` is the instance, `Sender` its id, `TargetCreation` and
  `SenderCreation` its creation, with no reply address and no start, and the trace is the recover operation's. Every
  branch writes that envelope as a new row, with `Done = false`, `KeptBy` and `ReleasedBy` null, `Attempts = 0`,
  `Trial` none, `Seen = 0`, `KeepUntil` as a newly inserted timer has, and a new `seq`.
  - **The row is present and done:** a conditional delete by full key, `WHERE ... AND Done`, with a count check of
    1, then, in the same commit, a create-only insert under the same full key of the envelope rebuilt from the
    plan's `TimerSpec` and the instance row, as above. Nothing is copied from the deleted row, so a payload-free done
    marker (a page cleared it, and [data lifecycle](data-lifecycle.md) format item 6 dropped its payload) is replaced
    by a row with its payload. A still-kept row is done with `KeptBy` set, so the delete matches it too, and the
    rebuilt envelope's kind, payload, `Due`, full key, `Sender` and creations equal the values it held; its `seq`, its
    trace, its counters (`Attempts`, `Trial`, `Seen`) and its `KeepUntil` window are the new row's, as for every re-arm.
  - **The row is absent:** the same create-only insert of the rebuilt envelope.

  So both branches write one and the same row, with a new `seq` and no marker; it carries no trace of the page that
  cleared it or of an earlier drain that released it. An update could not give the row a new `seq` (SQL Server does
  not update an identity column), which is why the present branch deletes and inserts. A re-armed timer is therefore,
  as the re-arm writes it, unreleased, so no page restores it, and it waits for Complete in both branches; only after a
  page releases it can a later interruption make it `Stale` (above). These are
  plain statements on every engine, with one meaning per count: a delete by key reports one matched row on each
  (MySQL with `UseAffectedRows=false` reports matched rows; MongoDB's `deletedCount`). A pending row needs no
  re-arm, since the lookup's "Pending" row leaves it. A count of zero on the delete, the sweep having removed the row
  after the lookup, answers "not landed", so the owner resets and recomputes the step, and the lookup then finds the
  row absent and inserts it. A key violation on the insert can only come from a defect; it is a definite error, and
  the instance re-parks under the commit's failure rule.

  **When a re-armed timer fires.** A re-arm is a write of the open, never a page's restore, so it sets no
  `ReleasedBy`, and the released rule withholds the timer until Complete. Both branches give it a new `seq`, so it
  fires at the same point after Complete, and in the same order against the instance's other pending rows, whichever
  branch armed it: the sweep's timing changes neither whether, when, nor in what order it fires. A timer
  with a `Rearmed` disposition that a later page restores (it fired while parked after an interrupting re-park, so it
  is kept `Arrived`) is released by that page like any restored row and is delivered during the drain; no rule
  withholds a released row, so the progress gate cannot wait for it. If a further interruption comes before anyone
  consumes that released timer, it is pending with the older `ReleasedBy` when the next recover opens: it is `Stale`,
  so a page of that drain releases it again, in tuple order within the cohort, and it is delivered during the drain
  (fourth review R4-m3). Only a pending re-armed timer with a null `ReleasedBy` waits for Complete. Re-arming under
  the same full key keeps the model's own timer id, so `Recall` by that id still works; a new timer generation mapped
  back, as tickets have, was rejected because `Recall` and the timer's id would then need the same mapping.

  **A restarted job** is written from its `JobSpec` alone (proposed, fourth review R4-M3), whichever branch the lookup
  chose: the old ticket kept `Consumed`, a payload-free done marker, or no row after the sweep. The new ticket's
  payload is `JobTicket(model, key, arguments)` with the spec's arguments, its effect key is the spec's, and nothing is
  copied from the old row, so the restart runs the invocation the model started, with the inputs captured at arm,
  whatever the bound variables now hold. The restart is a retry of that invocation, so it keeps its effect key, as a
  retry by a job actor does ([job workers](job-workers.md#the-effect-key-the-worker-receives)); its attempt id is new,
  since it derives from the new ticket id and the take's epoch ([job workers](job-workers.md#release-1)).

  A restarted job's ticket id derives from instance, key, creation and the next `JobGeneration` (`Worker.TicketId`
  takes the first three today), so it collides with neither the old ticket nor its kept row, nor with a ticket of an
  earlier Park. The kernel maps the generation back to the model's key through the registry. A result of an older
  generation matches no live ticket and is dropped.

  **A completed job's result is a fenced, authenticated row** (proposed, fifth review R5-M4). Existing prototype
  behaviour: when an entered job returns, the worker builds its result from the ticket itself (`original with { Kind
  = JobFinished, ... }`, `Worker.cs` 1247–1252), so the result keeps the ticket's full key; the owner swaps it in for
  the ticket in memory and sets `CompletedJob`, the worker-only flag that tells it from a supplied completion, which
  is discarded (`Worker.cs` 1298–1306; `Turns.cs` 107–116). The durable row stays the ticket: a consuming commit, the
  Park's `RemainingJobs` (`WorkflowInstance.cs` 120–143) and an unwritten instance's promotion, which writes the
  finished job as its ticket so a later owner runs it again (`Worker.cs` 1586–1594), all name the ticket. With Park
  keeping, a parked row would keep that ticket `Arrived`, and a recover would restore a ticket that the owner starts
  again as a job; persisting the changed kind alone would lose the in-memory flag at a take, and the row would be
  discarded as a supplied completion. So the owner converts the ticket into a result row of its own:
  - **Its identity.** The result row's id is the ticket's id with the suffix `/result`, under the ticket's scope
    (the origin's length plus one, `Messages.cs` 31), so its full key derives from the ticket's (instance, creation,
    job key, `JobGeneration`). The ticket's job key keeps its length prefix in the id (`Worker.TicketId`,
    `Worker.cs` 295–296), so the suffix never makes the id equal to another key's ticket. **No producer outside layer
    2 can write that scope** (existing prototype behaviour for every producer named here):
    - the only turn method that writes a ticket scope is `StartJob`, which mints the ticket itself, of kind
      `kernel.job`, under `InboxKey.Ticket` (`Turns.cs` 401; `Messages.cs` 31);
    - every other turn send takes the issued scope, −1 (`SendNext`, `Turns.cs` 327–330), and an actor type's `Add`
      takes the issued, the custom (`int.MaxValue` + 2 plus the actor's length) or the ordinary scope (`Turns.cs`
      347–351; `Messages.cs` 30);
    - the stores' public add and the worker's in-memory posts reset a supplied scope to the ordinary one
      (`InMemoryStore.cs` 231, `Worker.cs` 548 and 563, through `AsOrdinary`, `Messages.cs` 49), and key admission
      writes the ordinary or the child-creator scope (`KeyAdmission.cs` 71–73);
    - an actor type that composes `TurnOutcome.Sent` directly can set any scope (`Turns.cs` 208), so the commit check
      below covers it.

    Its kind is `job-finished`, its `Sender` and `SenderCreation` the instance's own, and its `TargetCreation` the
    instance's creation. The commit refuses, as a definite error, a `job-finished` row in a ticket scope that layer
    2's conversion did not mint in that commit, as it refuses an unminted private control.
  - **The reader side changes too** (proposed, K3). Existing prototype behaviour: a `job-finished` without the
    in-memory `CompletedJob` flag is a supplied completion (`SuppliedCompletion` and `DiscardedInput`, `Turns.cs`
    109–111), which the worker consumes and drops before any turn (`Worker.cs` 1030–1037), and `RemainingJobs`
    matches a consumed completion only by the ticket's own key (`WorkflowInstance.cs` 135–144). So, as built, a
    durable result row would be discarded as supplied and would leave its registry entry behind. The proposal: a
    polled `job-finished` row whose full key is a minted `/result` key (the ticket scope, the `/result` suffix of a
    ticket id of this instance's creation) is the worker's own completion, so `SuppliedCompletion` and
    `DiscardedInput` do not discard it, and `RemainingJobs` clears the job's registry entry, by the ticket key the
    result key derives from, when a model turn of a running row consumes it, and, as built, when an ended row with no
    unresolved Park consumes the ticket or the result as a late input (`WorkflowInstance.cs` 78–90, 135–144); only a
    parked row, one with an unresolved Park, keeps the entry ("A parked row keeps the registry entry" below).
  - **A ticket being cancelled is not converted.** When the same flush both cancels a job (a terminate, a cascade
    page or a Park's cleanup puts its ticket in the outcome's `Cancelled` list and the worker's cancel set,
    `Worker.Cancelling`, `Worker.cs` 227–229) and receives its result, the
    cancel wins: the commit consumes the ticket once, as the cancel, inserts no result row, and drops the in-memory
    result, so the ticket is never consumed twice in one batch.
  - **An ordinary cancel of a converted job** (proposed, sixth review R6-M5). A model can cancel a job in a later
    flush than the conversion: Native's `Unregister` calls `CancelJob` (`NativeModel.cs` 530–536) while the registry
    still holds (J, g) and the ticket is already consumed. Existing prototype behaviour: `DurableTurn.CancelJob` adds
    the ticket to the counted cancel set (`Turns.cs` 405–411), so the store refuses the batch (`InMemoryStore.cs`
    116–117) on every attempt. The rule: `CancelJob` on the model path is the cascade's idempotent cancel, presence
    decided under the fence, so a pending ticket is consumed and an absent one adds no counted consume; and a
    pending, unkept `{ticket id}/result` row of that generation is consumed as discarded, never delivered to the
    model, while a kept, unreleased one is left to the page that reaches it (case CJ-1 (e)). A
    same-key `StartJob` after the cancel arms the next `JobGeneration`.
  - **A running turn recognises the result row** (proposed, sixth review R6-M5). Existing prototype behaviour: the
    running path clears a job through `DurableTurn`'s constructor and `Jobs()` (`Turns.cs` 249–258, 454–462), whose
    `CompletedTicket` matches only the ticket's own key (464–470), so a `/result` delivery leaves J registered and a
    later `StartJob` of J throws (394–397). The rule: `CompletedTicket` and `RemainingJobs` also recognise the
    current generation's `/result` key, so the entry is removed before the model turn, and a held result re-adds it
    as built. A `/result` row that matches no registry entry, of an older generation or of a cancelled key, is
    consumed as discarded and never delivered to the model, leaving a replacement generation and every other job in
    place.
  - **Its contents.** The `JobFinished` payload (the model's job key, the result and the error), the generation and
    the original job key, so a page reads the generation from the id and a delivery checks it without a lookup.
  - **Its size is bounded where the job produces it** (proposed). The result, in the provider's stored encoding with
    the row's framing, must fit the message row cap (`Payload` at most 16 MiB less `M` and `F`, "The envelope bound
    and its headroom" above). The worker checks it when the job returns, before it publishes the result
    (`Worker.cs` 1247–1252). A larger result becomes the job's fault: a `JobFinished` with the error "result too
    large", in the same result row, so the conversion commit consumes the ticket as for any result, the job is
    never run again for that reason, and the model receives the error as it receives any job fault. Values by
    reference, after release 1, lift the bound.
  - **When it is written.** While the instance is deliverable, the model turn that receives the result consumes the
    ticket in its commit, as built, and no result row is needed; a turn that holds the result converts it in its own
    commit. While the instance is not deliverable (parked, suspended, withheld or draining), the owner commits a
    standalone fenced write that consumes the ticket by its full key, counted 1, and inserts the result row in the
    same commit. The registry keeps the job's (key, generation) entry until the result row is consumed, so the
    instance row is not rewritten.
  - **After it commits** the result row is ordinary authenticated input. A parked instance consumes it as any
    arrival, kept `Arrived`; recover restores it, and its delivery is accepted only while the registry's current
    generation for its key is the row's (otherwise it is consumed and dropped as obsolete); it is never a ticket kind,
    so it is never entered again as a job; a released result row is an ordinary cohort row. A terminate before its
    consumption finds the ticket absent, so its cancel is the idempotent no-op ("A termination cascade is paged and
    idempotent" above), and the row meets the finished-instance rule.
  - **Before it commits** the guarantee stays at least once: the ticket is still pending, so a crash, a takeover or
    an unknown outcome before the conversion or the consuming turn lands loses only the in-memory result, and the job
    runs again with the same effect key. While the instance runs, the next owner starts the pending ticket. A parked
    instance's next owner does not start it, since `StartJob` stands a job down at a row that is not running
    (existing prototype behaviour, `Worker.cs` 1168–1177), and the parked row consumes the ticket as a late input.
  - **A parked row keeps the registry entry of an undone ticket and of a result row** (proposed, the delivery
    harness's `jr1-crash-before`, and the re-review of JC-1's crash-after run). Existing prototype behaviour: a row
    that is not running consumes a late input through `RemainingJobs` (`WorkflowInstance.cs` 78–90), which drops the
    job's key from the registry whenever the consumed delivery is its ticket (135–144). After that, the recover's
    Open finds the kept row under a key the registry no longer holds and clears it as obsolete ("Obsolete job
    generations" below), so the result is lost and the job never runs again. The rule: while the instance is
    parked, meaning its Park is unresolved, consuming a late input keeps the registry entry of a current-generation
    ticket that is not done and of a current-generation result row, each kept `Arrived` as any arrival. At such a
    row the entry clears only when a model turn of a running row consumes the result, or when a resolution clears or
    discards the row. An ended row with no unresolved Park keeps the built rule and clears the entry when it consumes
    the ticket or the result, so a finished run keeps no stale (J, g) that a later marker would have to list. The obsolete check reads
    that registry, so a kept current-generation result row passes it and is restored, and the model receives the
    result once. For a kept ticket, the recover's Open restarts the job from its `JobSpec` under the next
    `JobGeneration` (R4-M3, "A restarted job" above), and the old ticket, now of an obsolete generation, is cleared by
    its page and never restored. One ticket's job is therefore
    invoked at most once per owner lost before its conversion lands, plus once, and never after the conversion; in
    the crash-before schedule on a parked instance that is two invocations, one before the crash and one after the
    recover.

  **Obsolete job generations are cleared, never restored** (proposed, fourth review R4-M3). A job's physical entry
  checks the target's status, its creation and withholding, and no generation (`Worker.StartJob`, `Worker.cs`
  1166–1203). Once entered, the activity runs, and its result is published under the ticket's id after only the
  owner's own take generation and the active creation are checked (`Worker.cs` 1247–1252), so a filter on the job's
  generation can act on the result only after the side effect. Restoring an old ticket would therefore run a second
  invocation (existing prototype behaviour for the entry; the generation is proposed). So every Open decides, before
  any page, that a kept job ticket or job result whose generation is not the registry's current one for its key, or
  whose key the registry no longer holds, is cleared: each page that reaches such a row clears it by its full key, in
  every mode, retry-all included, and never restores it, and a selection that names it is refused ("Checks",
  above). Layer 2 reads the generation from its own ticket id and the registry from the instance row, so the page
  still reads no payload. "Cleared" keeps the lookup table's meaning, decided at Open and carried out by the page that
  reaches the row: dispositions keep only the current generation, so the Open has no key by which to find an older
  one in its own commit.
- **Side effects** of a retried turn may repeat, with the same effect keys.

**Discard** `{operation, park}` makes the same Park checks and opens a discard drain. The instance stays
faulted, and retention or a delete removes it. A publish ticket the Park kept becomes a dead letter with its
undelivered remainder and replay command, and a kept meet ticket is cleared ("A kept publish ticket is kernel work"
above, sixth review R6-M7).

**Store and kernel changes.** All of this is kernel behaviour and changes the store contract; every provider and the
store conformance suite need it:

- `KeptBy` as a kept reason (`Consumed` or `Arrived`), set by the consuming commit and cleared by a drain;
- a new transition that clears `Done` on a restored row, with its poison counters reset;
- the sweep's predicate excluding kept rows;
- a paged read of an instance's kept rows after a cursor, and point lookups by full key, served by an index on
  `(Target, Seq)` partial on `KeptBy IS NOT NULL OR (ReleasedBy IS NOT NULL AND NOT Done)` where the engine supports it (MySQL takes the full index);
- the delivery handle, encoded and decoded by layer 2 only;
- `ParkRecord.Resolution` with the handle of the previous page's last restored row, which the progress gate reads, and
  its cursor on `(Seq, Id, Scope, Match)`, the store's poll order with the full key as
  tie-break;
- `ReleasedBy` on the message row, the `(creation, generation)` of the resolution whose page restored it, the
  eligibility rule that gives a recovering instance only control kinds and the rows the open resolution released, and
  the page's `Stale` arm that returns rows an interrupted drain released and nobody consumed;
- release cohort precedence: the latest generation's unconsumed released rows ahead of every non-control row, during
  a recover drain (control kinds first) and after its Complete (controls in their built place behind the non-held
  cohort rows), with held rows passed over; the instance row's `Cohort (creation, generation)` mark, set by a
  recover's Complete, holding back every non-cohort row, control kinds included, until a read that excludes the held
  keys finds no cohort row, passing every control kind while the instance is suspended, and cleared, when a read finds
  no cohort row at all, held or not, by a revision-fenced kernel write of the row that names the `(c, g)` it read;
  trial selection in the cohort order, with withheld suspects shelved;
- the owner's refresh, after a page commit lands, of every retagged row it has in memory, and the discard of those keys
  from a poll in flight;
- the retirement marker's retained parent, child registry and job keys, and the cascade of a parent-bound terminate
  refused there as `RetiredCreation` ([data lifecycle](data-lifecycle.md#proposed-the-qualified-delete));
- the termination cascade as a durable, paged obligation on the instance row or the marker, `Cascade {State, Entries,
  Cursor, Control, Serial}` (`Held`, then `Discharging`), discharged by layer 2 in pages of at most `P_cascade` =
  1,000 entries under the private control `kernel.cascade`, the first page in the ending or refusing commit, each
  page at the row's revision plus one, the obligation kept on the marker by a delete while `Discharging`, a root
  included (fifth review R5-B2), and in the simulation, invariant 17 widened so that at quiescence no `Cascade`
  obligation remains `Discharging` on any instance row or marker and no `kernel.cascade` is pending (today I17 checks
  only that no terminated tree instance runs or lists `Children`, existing prototype behaviour, `sim/Checks.cs`
  676–680);
- the idempotent cancel of a job ticket by its exact key (instance, creation, job key, `JobGeneration`), shared by
  the live terminate, the open-drain refusal cascade and the marker's cascade: a pending ticket consumed and counted,
  a kept `Done` ticket's `KeptBy` cleared, a `Done` or absent one left alone and never counted, presence decided by
  the owner under its fence (fifth review R5-B1), and the model's own `CancelJob` on the same cancel, with a pending
  result row of the cancelled generation consumed as discarded and never delivered (sixth review R6-M5);
- the cascade origin mark on every kernel-composed tree terminate and cascade page, which only the kernel sets and
  the commit checks, and the parent binding of every marked terminate, checked before any effect in every recipient
  state, running and ended included, with a mismatch a plain refusal; a model's application terminate keeps its built
  authorization (fifth review R5-M5);
- the fenced conversion of a completed job into a result row, `{ticket id}/result` in the ticket's scope, minted only
  by layer 2, written by the consuming turn's commit or by a standalone fenced write while the instance is not
  deliverable, skipped for a ticket the same flush cancels, with the result's size checked against the row cap where
  the job returns ("result too large" otherwise), and the reader side: `SuppliedCompletion` and `DiscardedInput`
  accept a minted `/result` row and `RemainingJobs` clears its registry entry, and a parked row's late-input
  consumption keeps the registry entry of an undone current-generation ticket, so the recover restarts that job from
  its `JobSpec` (fifth review R5-M4); `CompletedTicket` and `RemainingJobs` recognising the current generation's
  `/result` key, so a running turn removes the entry before the model turn and a held result re-adds it (sixth
  review R6-M5);
- the model hook `TerminalGrowth(state)` and the envelope check of every commit of a live row counting it beside the
  registries at the marker's sizes and the reserved header (sixth review R6-B1);
- the complete persisted tuple `(Seq, Id, Scope, Match)` in the trial comparison and the delivery order whenever
  cohort precedence applies (sixth review R6-M6);
- a kept publish ticket restored with its committed cursor by every recover mode, before or after the model ends, and set aside as a dead letter by
  discard, expiry, terminate and delete, and a kept meet ticket cleared in every mode (sixth review R6-M7);
- `Withholds` true while a terminate Resolution is open, so the built entry gates withhold every physical job
  entry (sixth review R6-M8);
- `Validate`'s third retirement shape, a new `Discharging` obligation in a commit that also terminates the row (a
  terminate with `ThenDelete`), and key admission's read of the bound actor's marker, answering `in-progress` while
  its cascade pages (fifth review R5-B2);
- the poison comparison in the cohort order while a recover drain is open or the `Cohort` mark is set (fifth review
  R5-M6);
- the progress gate on handoff, and the credit that cuts each page to at most `P_page` − u rows and `B_credit` =
  4 MiB of stored bytes, payloads included, of restored rows not handed off (u the rows pending), reset to the
  durable state at ownership loss, the `PayloadLength` column it reads, and the pending-row gauges (fifth review
  R5-M3);
- three kernel control kinds, `kernel.drain`, `kernel.park-expiry` and `kernel.cascade` (fifth review R5-B2), which
  pass withholding, are never suspects and are exempt from the poison check;
- the consume rule that keeps no arrival after a resolution opens, and the interruption rules that resume keeping;
- the per-creation `JobGeneration` counter that every `StartJob` and restart draws from, carried in the ticket id and
  the job registry;
- the incident binding: `ParkSelection` on every Park operation, checked against the row's tenant, stored id,
  creation and Park mark, in that order, and `ParkRecord.ResolutionGeneration` binding `kernel.drain` and
  `kernel.park-expiry`;
- the private controls: the reserved `kernel.` kinds refused at every producer outside layer 2, the control scope,
  `ParkRecord.ControlSerial`, `ParkRecord.Resolution.Drain` and `ParkRecord.Expiry`, checked by the consumer;
- `ParkRecord.Dispositions`, keyed by the digest of each wait's full key, pruned at every recover Open to the waits
  of its plan and cleared when the Park resolves, with the envelope reserve that keeps `ParkRecord` within the
  16 MiB row cap every provider applies to the instance envelope, and the same cap on the whole message row at
  admission, in the provider's stored encoding, with `Payload` and `Start.Input` together at most 16 MiB less `M`
  and `F`;
- the `TimerSpec` plan entries, from which every re-arm writes the timer's kind, payload and original due, the plan
  bounds `W_max` and `B_plan`, the selection's no-op for a `Rearmed` timer and retry-all's re-arm of `Suppressed`
  timers;
- the input bounds: metadata-only pages of at most `P_page` rows, the `Selected` reason and the selection's maximum;
- the owned commit's statements split by encoded bytes as well as rows, at most 4 MiB or the provider's own limit,
  and MySQL's `max_allowed_packet` floor checked at startup;
- the admission limits on every projected metadata field, checked at every ingress class before any write;
- the event match and the event delivery id as fixed SHA-256 digests over the canonical encoding (64 and 67 bytes),
  computed from the persisted spellings, with the components in a length-prefixed header ahead of the unchanged
  event payload, the start subscription's input bounded by `I` at registration, and the publish payload limit that
  reserves `C` and `I` (fourth review R4-M1);
- subscriptions among the plan's `W_max` waits and in `B_plan` at 7,168 bytes each, restored by plain insert,
  `Subscriptions.Keep`, and the
  restore's meet ticket by reference, whose catch-up adds at most `P_page` deliveries per step after the Open (fourth
  review R4-M2);
- the `JobSpec` plan entries, from which every restart writes its ticket, and the rule that clears a kept job row of
  an obsolete generation and never restores it (fourth review R4-M3).

**Every operator message** is an outside add in the operator's bound scope. Its id is the tenant-qualified operation
id `Q(t, operation)`, with a `keep_until`, so a retried command gets a duplicate-key answer and no second effect, and
the same operation id in another tenant is a different message. An operator acts only within its bound scope: another
tenant's instance answers not found, as a missing one does. Partition release, drain and handover are host
administration under host-admin permissions. The administrator's own writes go to `partition` and `node` rows and read
no tenant payload. The ownership change they cause then runs the ownership protocol's bookkeeping on tenant message
rows, as the [tenancy page's host-administration row](tenancy.md#every-operation) states: a take by another node after
an expired lease adds one attempt to each row on trial at the old epoch, a take after a release or by the same node
adds none (`InMemoryStore.TakeAsync`), and the new owner marks what it polls `Seen` and puts suspects on trial. After that, the new owner's ordinary commits carry each tenant's work in its own
tenant. None of these steps changes a row's tenant, ids or payload. T12 checks this in release 1, and T12b with the
forced handover after it. Cross-tenant support uses ADR 0097's support context
([tenancy](tenancy.md#every-operation)). Inspecting metadata,
reading payloads, recovering, discarding and handing over are separate permissions, and each action is audited with
its principal, reason and outcome.

> **Third review: counterexample and answer** ([third gate 2 review, R3-m2](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6960856712)).
> The trace: this paragraph said that release, drain and handover touch no tenant rows, while the tenancy page, T12 and
> the built take let ownership bookkeeping change `Seen`, `Trial` and `Attempts` on tenant message rows after a
> crash and a cross-node take. The answer: the paragraph now separates the administrator's own writes (`partition`
> and `node` rows only), the protocol bookkeeping those writes lead to, and the new owner's later tenant-scoped
> commits, and it points to the tenancy page's row as the rule. T12 stays a release 1 case at K5 and K11, and T12b
> stays with the forced handover after release 1.

## Read models: how layer 3 stays off partitions

Studio and the operator API depend on the foundation contract and the model contract only. They name an actor by its
exact `ActorRef` and never see a partition, a position, an epoch, an order key or SQL. Their read models are the
contract's `IActorReads` ([the foundation contract](foundation-contract.md#read-models-iactorreads), approved with
conditions on 19 November), which replaces this section's earlier `IParkedWork` and `IInboxInspection` sketch. Each
call takes a `BoundScope`:

| Read | What it returns |
|---|---|
| `GetAsync(scope, ref)` | One `ActorView`: the header, the `Selection` an operator command names, the `ParkView` when parked, and `AsOf` |
| `ListAsync(scope, query, from)` | A `Page<ActorView>` filtered by `ActorQuery(Type, Status, Labels, EndedBefore, Parked)`. Pass the previous page's opaque `Next` as `from` for the same tenant and query. The parked-work list is this query with `Parked` |
| `TailAsync(scope, ref, from)` | One actor's `JournalEntry` pages after an opaque `PageToken`, in commit order |
| `InboxAsync(scope, ref, from)` | One actor's `DeliveryView` pages: pending, held, and the rows its Park keeps (`Consumed` or `Arrived`), each with its handle |

- **Ids are opaque to layer 3.** Layer 3 stores and compares an `ActorRef` as a string and never parses it (FC-13).
  Today's `InstanceRef` embeds its partition (`3/order-17` in
  [Addresses.cs](../../prototypes/kernel-model/src/Kernel.Runtime/Addresses.cs)); that stays inside the built-in
  foundation, and K2 removes it from layer 3's reach.
- **Where an operator finds a handle.** `InboxAsync` lists an actor's rows, in pages, each with its handle and state; a
  retry names at most 500 handles from there. It is eventually consistent: a row a drain has just restored or cleared
  can still appear, and the recover refuses a selection that no longer matches (FC-9).
- **Delivery handles are opaque too.** A handle encodes the store's full message key, so two rows with one wire id
  never share a handle; layer 3 passes handles back unchanged and never builds or parses one.
- **No order keys or epochs.** A page comes in delivery or commit order; its position travels as an opaque
  `PageToken`. `seq`, `seen` and `trial` stay in the built-in foundation.
- **Labels, and no tenant feed.** An actor type builds its own read models, the instance list and plan outcomes
  included, from at most four bounded labels in the header plus the per-actor tail. There is no tenant feed (owner
  decision 2, 19 November). `ListAsync` over labels is eventually consistent with the commit that set them (FC-11).
- `TailAsync` replaces today's `Viewer.ReadAsync`, whose partition argument stays inside the built-in foundation.
- Commands travel through ingress: an operator's `Recover`, `Discard`, `Delete` or `Terminate` is a `SendAsync` with an
  `OperatorCommand`. Their outcome is visible in the read models, or in the `Reply` of a `RequestAsync`.
- Each page says how fresh it is in `Page.AsOf`, the foundation clock's reading it is current to. Read models lag the
  commit, and inbox rows show only what is written, so messages an owner holds unwritten in memory do not appear.
- Elsewhere on this page, `ParkedItem`, `KeptAsync` and `ParkSelection` name the built-in foundation's own reads and
  tokens behind these; through the contract they surface as `ActorView`, `InboxAsync` and `Selection`.
- Partition release, drain and handover are host administration of the built-in foundation. Studio shows the kernel's
  health through the metrics above, never through a partition API.

## Release 1

| In release 1 | Later |
|---|---|
| The `trace` column and propagation on every message; turn, commit and job spans | Trace-horizon tuning per route; richer span attributes |
| The metric set above, aggregated per node, with the partition backlog as a histogram | Per-partition series by default |
| The parked-work read model; inspect and discard; recover in place and retry (decisions 1 and 2) | Bulk actions; incident annotations |
| Inbox inspection, metadata only | Payload inspection in Studio |
| Release a partition and drain a node | Forced handover from an unresponsive owner |

## Costs

| Item | Cost | Status |
|---|---|---|
| Trace context | About 27 bytes per message row write in binary; 16 bytes per sampled journal row | Estimated, unmeasured |
| Metrics | In memory, from poll answers and the owner's memory, except the depth beyond a full poll, which reuses question 3's capped count (once per behind virtual partition per sample interval); the parked gauge reads the read model's count | No database work of their own on the hot path |
| Park keeping its deliveries (decision 2) | A done mark with `KeptBy` in place of a delete for each consumed delivery and each arrival while parked, a journal row, and rows kept until their Park is resolved; one nullable column on every message row, and one partial index written only for kept rows | Only when an instance parks |
| Recover, discard, delete of a parked instance, expiry | The open: one outside add (none for expiry), its seen mark and one commit, with one point lookup per timer and job the plan names and at most `P` selection marks. Then one turn per page of up to `P_page` rows: one paged read and the page's share of a commit. Completion takes one more page turn | Per resolution; proportional to the kept rows, never in one transaction |

## Not verified

- The whole contract: no trace column, instrument, recovery or inspection exists in the prototype.
- That `RestoreWaits` covers every model's waits: Native, Flowchart and StateMachine each need it shown, with kept
  events in the gap, timers that came due while parked, and jobs still in flight.
- The byte cost of the trace column, against WRITES.md's cost run.
- That restoring a kept row (done mark cleared, poison counters reset, unseen) passes every provider's consume and
  authentication paths unchanged, and that the new transition keeps invariants 4, 9 and 14.
- The drain: its page bounds, the released-row eligibility, and its cost on a busy partition (acceptance cases
  M3-1 to M3-14 and E-1 to E-6 below).
- The admission limits on projected metadata and the maximum `M` on every provider (cases S-5 and S-6).
- The timer restore source, the repeated selection and the disposition lifetime (cases TR-1 to TR-4, S-7 to S-9 and
  D-1 to D-4), and an Open at the plan bound on every provider (TR-5).
- The Park identity checks, the bound controls, the retained dispositions and the metadata-only pages (cases P-1 to
  P-5 and S-1 to S-4), the Park mark through an interrupted recover (P-6), the parent-bound terminate and the refusal
  of every terminate during a discard, expiry, terminate or delete drain (P-7), and the private controls' issuer,
  outstanding key and selected terminate (cases P-1b, P-1c and P-8 to P-12).
- The delete takeover under the row's current terminal mark and `ThenDelete` on a drain (cases DT-1 to DT-6).
- The fourth review's transitions in a real store and worker: release cohort precedence and the owner's refresh
  (E-7 to E-11), the two pending `Rearmed` branches (RA-1), the retirement marker's obligation (DT-7 to DT-9 and the
  data lifecycle page's RM-1) and the selection's target (PS-1). The FR-1 and RT-1 harness runs drive the real Native
  model and the real `CreationProtocol` under a fixture schedule; no Worker, provider or simulation implements these
  rules yet.

## Counterexamples and answers

The [gate 2 review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6949612272) found three
faults in the Park and recovery design. Each trace is restated, then followed through the revised design.

**B2: Park lost delivery identity.**

1. Trace: a private delivery with wire id X causes the Park. An ordinary arrival with wire id X reaches the parked
   instance later. Both are kept. On resume, X's membership in `Consumed` excluded the later arrival too, and the commit
   cleared its marker, so it could never be restored. Selecting X for retry could select both rows.
2. Revised: the Park's commit marks `(X, private scope)` with `KeptBy = Consumed`. The later commit that consumes
   `(X, 0)` while parked marks it `Arrived`. No step tests membership by id.
3. `resume`: the drain restores every `Arrived` row by its full key, so `(X, 0)` is restored, and clears
   `(X, private scope)`.
4. `retry` naming the handle of `(X, private scope)`: Open marks that row `Selected` by its full key, so the drain
   restores it, and restores `(X, 0)` too, because every mode restores `Arrived` rows. A second, unselected
   `Consumed` twin of X stays cleared. A handle cannot name two rows, because it encodes the scope and the match.
5. The other order, and a re-park: a row of X consumed by the Park or a re-park gets `Consumed`, a row of X consumed
   while parked gets `Arrived`, and an X handled before the Park has no `KeptBy` at all. Each is classified by its own
   row, whatever the order. Two event matches with one wire id differ in `Match`, so their handles differ too.

**M1: a sweep could strand a timer during recovery.**

1. Trace: a finite `Parked` window expires. Resume reads a kept timer that fired while parked, so the plan does not
   re-arm it. The sweep deletes the row before the restore update. Zero matches counted as success with a missing
   entry, so the instance ran with no armed timer and no restored delivery. A deletion just before the read would have
   re-armed it. The job-ticket branch had the same gap.
2. Revised: the sweep's predicate excludes `KeptBy IS NOT NULL`, so it cannot delete a kept row, before the read,
   between the read and the write, or at any other time.
3. The window's expiry is an automatic discard, run as a drain of the instance itself. It is one of the instance's
   serial turns, and recover's open recalls its timer, so it cannot interleave with a recover; after it, recover is
   refused, so no recover ever plans over cleared or swept rows.
4. So the open's point lookups see exactly the rows its commit writes against. A count mismatch can no longer come
   from the sweep. If one comes from anything else, the commit answers "not landed" and the open is recomputed under
   the same operation id.
5. After the commit, restored rows are not done, so the sweep ignores them. Cleared rows carry the ordinary window and
   go with the sweep as intended.
6. A lost acknowledgement: the operator's retry meets the duplicate operation id at the insert, and the read model
   shows the one outcome.

**M3: an indefinitely parked instance could outgrow the atomic resume.**

1. Trace: with the null window, a steady trickle while parked turns into done rows that leave the due-depth count, and
   resume had to read, update and hand over all of them in one transaction, with no bound in rows, bytes or
   transaction lifetime.
2. Revised: no resolution touches the kept rows in one transaction. The open is bounded by the model's waits and the
   selection. Each page is bounded by `P_page` rows and `B` bytes (third review), and nothing goes into memory ahead of the disk.
3. Arrivals stop being kept at the open, so the drain ends. The cursor survives crashes and takeovers.
4. Restored rows rejoin due depth one page at a time. A page waits while the previous page's last restored row is
   not yet taken up, read from durable state, and while the instance is suspended, so the drain adds at most one page of
   rows not yet taken up plus the rows the model holds. The gate never waits on the partition's backlog, which the drain's
   own withheld rows inflate; waiting on it could block the drain for good. Unrelated
   ingress stays under the [ingress threshold](fairness-and-backpressure.md#a-bound-on-inbox-growth).

## Second review: counterexamples and answers

The [second gate 2 review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6959058264)
found four more faults on this page. Each trace is restated, then followed through the revised design.

**R2-B1: a stale Park command could act on a later creation.**

1. Trace: A, under caller key K, parks at first revision 7. An operator prepares `discard {expected revision: 7}`.
   Another operator resolves and deletes A. B reuses K with a higher creation and parks at first revision 7. The old
   command arrives, binds K to B, and every check passes, so B's kept rows are cleared and B's parent gets a final
   Faulted notice. A resume with an empty selection, or a retry-all, needs no handle and passes the same way.
2. Revised: the command carries `ParkSelection(tenant, creation, ParkRevision, ParkAt)` with A's creation. B's creation
   differs, so the owner refuses it before Open and nothing of B changes. Equal first revisions do not matter.
3. If A itself recovered and parked again, the creation matches but the Park mark differs, so the stale command is
   refused as naming a different Park.
4. A delayed `kernel.drain` or `kernel.park-expiry` carries the creation, Park mark and resolution generation it was
   sent for, so it cannot act on B, on a later Park of A or on a later resolution of the same Park.

**R2-M1: a page and Open had undefined inputs.**

1. Trace: a kept `Arrived` row of 5 MiB sits next after the cursor. With `B` = 4 MiB, an empty page would complete
   the drain with the row still kept, waiting would never advance, and loading it would break the bound. An explicit
   selection of every handle a Park accumulated over many re-parks could exceed a document or request limit at Open.
2. Revised: pages read and write metadata only, and `B` counts metadata, bounded by the provider's key limits. A page
   always takes its first row, so a 5 MiB row is one ordinary metadata row; its payload is read by the ordinary poll
   when the row is delivered. A cleared row's payload is never read, under discard, delete or a takeover alike.
3. An explicit selection has at most `P` handles and 256 KiB; a larger one is refused with no change, and retry-all
   stays a flag. Open marks the selected rows on the rows themselves, so the Resolution never holds the list.

**R2-M2: an interrupted resume forgot why a timer stayed unarmed.**

1. Trace: timer T is consumed by the Park, and the model still waits for it. A resume opens, and a page clears T
   without re-arming it. A later page restores arrival X, whose turn throws: a re-park continues the same Park. T is
   now an ordinary done row with no `KeptBy`, and the sweep may remove it. A second resume finds T absent and re-arms
   it, so resume has become retry.
2. Revised: the first resume's Open records `Suppressed` for T in `ParkRecord.Dispositions`. The re-park keeps the
   dispositions, and they are cleared only when the Park resolves.
3. The second resume's lookup reads the disposition first, so T stays unarmed, whether its row is ordinary done or
   swept. Only a retry that names T re-arms it.
4. The dispositions are on the instance row and commit with Open, so a takeover between any two steps sees them.

**R2-m1: B2-1's retry oracle was wrong.** `retry` naming the private X restores that row and also the `Arrived`
ordinary X, by the general rule. B2-1 now says so, adds an unselected `Consumed` twin that must stay cleared, and
asserts full keys and delivery counts.

## Third review: counterexamples and answers

The [third gate 2 review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6960856712)
found more faults in the recovery drain and in the Park controls and their cases. Each trace is restated, then
followed through the revised design.

**R3-B2: a late low-order arrival could run ahead of the recovery backlog.**

1. Trace: the kept prefix holds rows from a sender whose MongoDB clock is ahead. Page 1 moves the cursor to
   `(1000, …)`, and a kept suffix still waits for page 2. During the drain another sender, or the outside-add clock,
   gives a newly committed arrival A `seq` 900 (or 1000 with a lower tie-break). The only test was the tuple against
   the cursor, so A passed. A ends the model; page 2 runs after the end and clears the suffix, which is never
   delivered.
2. Revised: page 1 sets `ReleasedBy = (c, g)` on each row it restores. A is a new row with no `ReleasedBy`, so while
   the recover Resolution is open it is withheld, whatever its `seq` ("Eligibility during a recover drain" above).
3. Page 2 restores the suffix, and those rows are released and delivered. Complete ends the withholding, and only
   then is A delivered. If A ends the model, it does so after every kept row was delivered once.
4. A takeover between the pages changes nothing: the new owner reads `ReleasedBy` from the rows and the generation
   from the instance row. The relational case, a `seq` drawn before page 1 and visible after it, is the same: the
   row has no marker.
5. An interrupting re-park and a later recover bump the generation, so rows the first drain released and nobody
   consumed are unreleased under the second. They rejoin the Park: the second drain's pages read them through the
   `Stale` arm and restore them in tuple order with the kept rows, so in the trace with R1 and R2 released,
   K kept and a low-`seq` recover command opening the next drain while R2 is pending, R2 is restored and delivered
   before K, and a model end at K drops nothing kept.

**R3-M3: the metadata page could exceed its byte bound.**

1. Trace: a message with a short key, a small payload and a `Kind` above 4 MiB is admitted, since only the payload
   and the provider's key limits were bounded. It is kept. A page must take its first row, so the singleton page
   exceeds `B` = 4 MiB. The review's SQLite probe stored a 5,242,882-byte key in the provider's key shape, so a key
   alone could do it too.
2. Revised: every projected field has an admission limit, checked at every ingress class before any write, so a
   row's encoded metadata is at most `M` = 24 KiB, strictly below `B` ("What a page reads, and its metadata bound"
   above). The over-limit kind or key is refused where it is first written, so it is never kept.
3. A page takes at most `P_page` = 170 rows, so its metadata is at most `B` by construction, and its first row always
   fits. A large payload still moves through a page as metadata (S-1).

**R3-B1: a revived timer could have no deliverable payload.**

1. Trace: a Native frame F waits on timer T, whose row carries `TimerFired(w)`. The Park consumes T. A resume
   suppresses T, and an early page clears it, leaving an ordinary done marker that format item 6 makes
   payload-free. Arrival X re-parks. A retry names T before the sweep, and the update restores flags, due and window
   but no payload. After Complete, Native's timer handler reads `TimerFired` from an empty payload, and F never
   resumes. After the sweep the insert had no source for the envelope either: `WaitEntry` holds no deadline.
2. Revised: `RestoreWaits` returns `TimerSpec(t, NativeKinds.Timer, TimerFired(w), Due)`, with `Due` recorded at the
   arm. Both re-arm branches write `Kind`, `Payload` and `Due` from the spec and the key, target, sender and creations
   from the instance row ("The re-arm" above).
3. Before the sweep the delete and insert replace the marker with the full envelope; after it the insert writes the
   same envelope. A takeover
   between Open and Complete finds the committed row. After Complete the handler reads `TimerFired(w)` and resumes F
   once, at the original deadline. The still-kept row, revived before its page clears it, gets the kind, payload,
   `Due`, full key, `Sender` and creations it held, with a new `seq`, the recover's trace and reset counters.

**R3-M1: the validator and the repeat-retry case disagreed.**

1. Trace: retry R1 names suppressed T and re-arms it (`Rearmed`). X interrupts before Complete. A fresh retry R2 names
   T and a valid `Consumed` Y. T is pending (or, after firing while parked, kept `Arrived`): neither `Suppressed` nor
   kept `Consumed`, so the whole of R2 was refused, and Y was never selected.
2. Revised: a handle of a `Rearmed` timer whose row is pending or `Arrived` is an accepted no-op, counted "already
   re-armed" ("Checks" above). R2 is accepted; Y is marked `Selected`; T is not armed again.
3. If T is pending with a null `ReleasedBy`, it stays unreleased and fires once after Complete (a pending T with an
   older tag is `Stale`: fourth review R4-m3). If T is kept `Arrived`, a page restores and
   releases it, so it is delivered during the drain, once; the gate can wait for it, since it is released
   ("The gate row is always deliverable" above). A timer the Open re-armed is unreleased and never a gate row. An
   unreleased row that the owner has polled and holds in memory, seen at the current epoch, is never consulted by the
   gate, and a re-armed timer not yet due cannot be polled at all; neither can stall a page.
4. Retry-all re-arms every `Suppressed` timer once, before or after the sweep, and nothing `Rearmed`.

**R3-M2: retained dispositions grew across a Park's successive wait sets.**

1. Trace: during each recover drain the model finishes its wait on T_k and establishes T_k+1, then a later input
   re-parks. Each Open decided a new timer, and dispositions cleared only at resolution, so after n interruptions the
   set held T_1 … T_n while at most two waits were live. On MongoDB the envelope could pass 16 MiB and defeat every
   later recover commit.
2. Revised: each recover Open replaces the dispositions with those of the waits its plan names, in its own commit
   ("Retained dispositions" above), so after interruption n the set holds the dispositions of T_n and its live
   neighbours only. Entries are keyed by the SHA-256 digest of a timer's full key or of a job's key (with `g` in plain); `D` = 64
   bytes is proposed, which K4 may raise; and `ParkRecord` is at
   most 312 KiB (proposed), which every commit reserves.
3. A handle of T_1 names no disposition: refused if its row is done or swept, an ordinary stale delivery if still
   kept. It can never name T_n, since keys are unique per send.

**Worked example: one timer under the combined rules.** A Native frame F waits on T (id t, payload `TimerFired(w)`,
due d0).

1. *Suppression.* A turn consuming T and X throws: Park, mark r1; T and X are kept `Consumed`. A resume opens with
   generation 1; the plan names `TimerSpec(t, …, d0)`; T is kept `Consumed` and unselected, so `Dispositions` = {t:
   `Suppressed`}.
2. *Marker clearing.* Page 1 clears T to an ordinary done marker with no payload, and restores an `Arrived` Y with
   `ReleasedBy` = (c, 1). Y's turn throws: a re-park, mark r1 back, dispositions kept.
3. *Explicit retry.* A retry naming T opens with generation 2, after the sweep removed T's marker. The plan still
   names t, so pruning keeps t; the disposition is `Suppressed`, so the re-arm inserts T from the spec: kind, payload
   `TimerFired(w)`, due d0, the instance's key, target, sender and creations. `Dispositions` = {t: `Rearmed`}. T has no
   `ReleasedBy`, so it waits until Complete.
4. *Re-park.* An arrival Z, kept while parked after step 2 and restored by this drain with `ReleasedBy` = (c, 2),
   throws before Complete: a re-park; T is still pending. T is already due (d0 has passed), so the parked instance
   consumes it while parked: kept `Arrived`.
5. *Takeover.* Ownership moves. The new owner reads `ParkRecord`: mark r1, {t: `Rearmed`}, no Resolution.
6. *Complete.* A resume opens with generation 3. The plan names t, so t's disposition stays; it is `Rearmed`, so no
   re-arm. A page restores T as an `Arrived` row with `ReleasedBy` = (c, 3). Ownership moves again before T is
   polled; the new owner reads the marker and the open generation and delivers T. Native's handler reads
   `TimerFired(w)` and resumes F once. A retry naming T in this Open would have been a no-op. Complete clears
   the Resolution and the dispositions. T fired once, from one envelope, and F resumed once.

The Park controls and their cases follow. All of them are proposed; the prototype has no `ParkRecord` and no
private control.

**R3-M4: a private control had no issuer.**

1. Trace: A parks under the default null window. Its creation and Park mark are on the parked-work read model, and
   its generation is a small counter. An application, a model through `ITurn.Send`, or an actor type through `Add` or
   `SendFromReceivedAddress` sends A a `kernel.park-expiry` with those values. Today's `RequireApplicationSend`
   refuses only job and event tickets, so the send is written. At A, the second review's checks pass: creation and
   mark match, no resolution is open, the generation is current. The expiry opens a discard that the null window
   never scheduled. A forged `kernel.drain` with the open Resolution's generation runs an extra page past the gate.
2. Revised, at the producer: every `kernel.` kind outside the public commands is reserved, and every existing call
   site of `RequireApplicationSend` (the stores' adds, the worker's posts, the model's and actor type's send paths)
   refuses it with a definite error, and key admission's `Validate` applies the same rule. The outside add writes no
   row; a turn that calls a send method throws at the call and fails by the ordinary fault rule. An actor type that
   composes `TurnOutcome.Sent` directly reaches the commit-side check, which refuses a private kind, or the control
   identity domain, that layer 2's resolution step did not mint. Either way nothing reaches A.
3. Revised, at the consumer, for a row that exists anyway (P-10 writes one directly into the store): the control must
   be in the control identity domain (control scope 2^62, no `Match`, a private kind; the EF event slots at −3 and
   below and the PostgreSQL and SQLite text-key escape scopes cannot meet it), sent by A itself, and its full key must equal the outstanding one in
   `ParkRecord`. Under
   the null window `Expiry.At` is null for the whole Park, so no expiry acts. With a finite window an injected
   expiry has another key and is dropped, and the armed one acts only at or after `At`; taken early by a clock fault,
   it is dropped and re-armed at `At` in the same commit. A forged drain is not `Resolution.Drain` and runs no page.
4. Delayed legitimate controls are still dropped by the payload checks of the second review, and also by the
   outstanding key: a stale drain after a second Open, an expiry recalled by a recover's Open, an expiry of a resolved
   Park after a later Park, and A's controls at a successor creation (P-4, each injected in its own run).
5. Successive pages and gate retries within one generation take successive values of `ParkRecord.ControlSerial`, so
   each has its own full key and the store's deduplication never merges two; exactly one drain is outstanding (P-11).

**R3-m1: P-3 could pass with the creation check removed.**

1. Trace: A and B both park at revision 7, at different times. Remove the creation check: A's late command still
   fails at B on `ParkAt`, so P-3's mutant is refused and the case passes without testing the creation check.
2. Revised: P-1 pins the clock so that A and B have equal tenant, caller key, Park revision and `ParkAt`, and asserts
   that precondition. The late command is routed to B at the transport layer with A's `ParkSelection`. The creation
   check is then the only one that refuses, so the mutant clears B and fails P-3; a mutant that drops only the
   Park-mark check still passes, which shows the isolation.
3. A command handled before retirement is P-1's positive case. One enqueued before the reuse and delivered after it is
   the separate case P-1b, and it is refused.

**Architecture section: a selected terminate delivered after recovery.**

1. Trace: an operator sees A parked and sends a terminate with A's `ParkSelection`. Before it is delivered, a resume
   completes and A runs. The earlier rule required the selection only while the row is parked, so the terminate,
   meeting a Running row, could act as an ordinary terminate and cancel a run the operator never saw.
2. Revised: a selection, when present, is checked whatever the row's status. A's Park is resolved, so the terminate is
   refused (reason Park) and A keeps running. After an interrupting re-park the mark is back and the terminate is
   accepted. A terminate with no selection is unchanged (P-8).
3. While the recover drain is still open, the row's mark is cleared and `ParkRecord` still holds the Park identity.
   A selected terminate is checked against that identity and accepted, and it interrupts the drain; one with no
   selection is the ordinary terminate of a running instance (P-8).
4. If the model ended while the drain is open, the run has ended and its end already sent the one notice. A selected
   terminate is refused (reason resolution open) and, when its selection names the Park, still terminates the children
   and cancels the jobs; an unselected one is the built terminate of an ended row and opens no drain, and a terminate
   with `ThenDelete` keeps its own ended-run path (P-8).

## Joint review of the third fix round: counterexamples and answers

An independent [joint review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6963302110)
of the four merged third-round fixes found places where they disagreed. The two traces
and the two consistency faults are restated, then followed through the revised text. All of it is proposed.

**J-1: a parent-bound terminate during a discard, expiry or delete drain had no rule.**

1. Trace: an operator's discard drain is open on child A. Parent P is terminated, and its tree terminate reaches A.
   Only selected terminates and terminates during a terminate drain were refused as "resolution open", so the
   parent-bound acceptance applied: the terminate replaced the open Resolution, the discard got no outcome, A ended
   Cancelled where the discard said Faulted, and a qualified delete in progress was dropped.
2. Revised: every terminate that reaches an open discard, expiry, terminate or delete drain, whatever its origin, is
   refused as "resolution open" after the tenant and creation checks; it opens no drain, replaces no Resolution and
   adds no `ThenDelete` ([the rule](#every-park-operation-names-its-park)).
3. The refusal keeps the subtree part of the built terminate. A has a live child G, which Park left alone, and
   discard never terminates children. P's tree terminate has authority over A (it passes the parent binding), so
   the refusing commit sends G a terminate from A, cancels A's jobs and clears both from A's row. G ends Cancelled,
   and invariant 17 holds for P's tree; a delete drain's later Complete finds the cascade already sent.
4. The discard drain runs on, ends A Faulted with the disposition "discarded" and sends P one Faulted notice, which P,
   now ended, consumes and drops. A delete drain runs on to its deletion. P-7 runs each drain kind, with a live child
   and a job on A, and a mutation that drops the cascade.

**J-2: `ThenDelete`'s ended-run path had no "in progress" step.**

1. Trace: A's model ends during a recover drain and writes mark r2. A delete D naming r2 takes the drain over. An
   operator's terminate with `ThenDelete` then ran checks 1, 2 and 5 and took the drain over again; a takeover
   records a single operation, so D's outcome was lost.
2. Revised: on the ended-run path, "in progress" is tested after checks 1 and 2 and before check 5
   ([`ThenDelete` follows its own contract](#delete-during-a-drain)). The terminate is refused with D named in its
   outcome, and nothing changes.
3. The drain runs on; its Complete deletes A under D, D's outcome is deleted, and P has received only the end's
   notice (DT-3).

**J-3: two rules disagreed on the id key admission gives a caller key.** One rule mapped each key to a generated
run id; the Park binding, the tenancy page and the admission limits put the caller key in a stored actor id. Revised:
release 1's caller-key actor id is the tenancy page's `{vp}/Q(t, key)`, the same id across creations of the key, so
keyed starts meet the name limits; the prototype's generated run ids are described as prototype behaviour only, and
the wide and malformed `UnicodeKeys` keys are S-6 refusal fixtures. A caller key is not a generated id, so
walkthrough decision 7 is untouched.

**J-4: the unit refusals left escape fixtures unreachable.** A control id derives from the actor id, and admission
refuses an unpaired unit on every provider and a NUL on PostgreSQL, so an admitted control id can hold a NUL only on
SQLite. Revised: the control-scope text says so; P-12 runs on SQLite with a NUL actor id admitted normally, and its
other combinations run as a below-admission defence fixture, labelled as one; S-5 applies the base64 path only to
the issued ids that can take it, and `M` stays a safe over-estimate.

## Fourth review: counterexamples and answers

The [fourth gate 2 review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6967291576)
found faults in the release ordering, the retirement of a deleted instance, the owner's cached rows, the re-armed
timer's timing and the Park selection; it also found that two derived sizes and one restart source were not bounded by
the third round's rules, and that two case oracles did not match their rules. Each entry below is a "Fourth review: counterexample and answer": the trace is
restated, then followed step by step through the revised rule. Everything here is proposed. Where an entry says
"executed in this repair", it means a named run of the FR-1 or RT-1 harness case in the harness, #4781 with its follow-ups #4785 and #4786
(`prototypes/kernel-model/experiments/Review4.Blockers`, on main at `b09c32996` from #4786's head `7986f5bc1`, with
the runs made at `c4e0d75c6` and recorded in its `results.txt`), under the reviewed rules, the repaired rules and a negative control. The harness drives the real Native model and the real `CreationProtocol`
under a fixture schedule; no Worker, provider or simulation implements these rules yet, and the page cases below
qualify them at their milestones.

For the size and identity entries (R4-M1, R4-M2, R4-M3, R4-m1 and R4-m2 below; R4-M6 is on the [tenancy
page](tenancy.md#fourth-review-counterexamples-and-answers)), "executed in this repair" means the size probe in
[experiments/Astra4.Sizes](../../prototypes/kernel-model/experiments/Astra4.Sizes/README.md), which drives the
unchanged prototype code and executes no proposed transition.

**R4-B1: a final restored input could lose to a late arrival.**

1. Trace: R, at `seq` 1100, is the last row the final page restored, so it is the gate row. A, an end-producing
   arrival at 900 from another clock, has no release tag and is withheld. The next `kernel.drain` C is at 950, valid
   on MongoDB because R came from a faster sender. The owner has polled R and marked it `Seen = e`, but has not
   offered it to a model turn; R is neither held nor on trial. C precedes R, the gate accepts R's mark, C finds no
   further kept row, and Complete clears the Resolution. Withholding is over, plain tuple order puts A before R, and
   a model that ends on A drops R undelivered. A takeover and a re-mark before C give the same outcome.
2. Revised: control kinds come first, so C still runs first, and Complete is entitled as before: the cursor has
   passed the last kept row and the gate row is taken up ("Release cohort precedence" above).
3. After Complete, R still carries `ReleasedBy` (c, g), g is the instance row's latest generation, and R is not
   `Done`, so R is a cohort row, and Complete has set the instance row's `Cohort` mark to (c, g). The owner offers R
   before A, whatever their `seq`, and offers A only once a read finds no cohort row left.
4. The model handles R, and R's frame completes once. A then ends the model, and no restored row is lost.
5. A takeover after R's mark and before the model handoff, before or after Complete: the lost owner had marked both
   A and R, so the new owner's poll returns both as suspects. The trial selection follows the cohort order, under the
   open drain or the `Cohort` mark, so R's fenced trial commit puts R on trial at the new epoch and the trial delivers
   R alone; A's trial waits, and no group joins A to R. If the new owner's bounded poll returns A and not yet R, the
   mark withholds A, A is shelved so the polls move on, and R is polled and delivered first.
6. A held row never blocks: if the model holds R for a later input, the poll skips R and the order passes over it, so
   A is offered with R handed back, and R receives the input it waits for. The mark's read excludes the held keys, so
   a held R1 lets A through only when no other cohort row remains: with R2 still unpolled, the read finds R2 and A
   waits. A parent's tree terminate waits behind the cohort the same way.
7. Executed in this repair: FR-1, with the real Native model (R a released timer firing for frame F, A a lower-`seq`
   `native.cancel`), asserts F's completion count and the order of the model's transitions in the reviewed,
   repaired and negative runs: `fr1-base`, `fr1-takeover-before-complete`, `fr1-takeover-after-complete` (with the
   `memory-precedence` mutant), `fr1-model-end`, `fr1-held` (with the `held-blocks` mutant), `fr1-trial-takeover`,
   `fr1-poll-limit`, `fr1-held-poll-limit` (with the `one-row-read-counts-held` mutant) and
   `fr1-control-after-complete` (with the `mark-ignores-controls` mutant), `fr1-control-crash` (with the
   `boundary-holds-cohort` mutant) and, for E-11, `fr1-ticket-control`, `fr1-ticket-control-parent` and
   `fr1-ticket-control-suspend` (each with the `controls-wait-on-ticket` mutant), and for E-7's remaining runs
   `fr1-control-reset` (with the `boundary-holds-cohort-nonsuspect` mutant), `fr1-trial-takeover-open` (with the
   `skip-target` mutant), `fr1-trial-takeover-open-poll-limit` (with the `skip-target` and `no-shelf` mutants) and
   the model-end control `fr1-model-end-recall`, and for E-8's runs 3 and 4 `fr1-held-crash` (with the
   `clear-with-held` mutant) and `fr1-held-suspend-crash` (with the `controls-wait-while-suspended` mutant). The page
   cases are E-7, E-8 and E-11.

**R4-M4: a stale cached row could block the same owner.**

1. Trace: the same owner holds R2 from generation g (polled, seen, not consumed). The interruption consumes another
   input, and the next recover opens generation g + 1 before the parked actor consumes R2. The g + 1 page retags R2,
   resets its `Seen` and makes it the gate row. The poll skips R2's full key, since the owner holds it; the cached copy
   still shows g, so eligibility withholds R2, and its reset durable `Seen` never reaches the current epoch. Only a
   takeover would heal it.
2. Revised: once the page commit lands, the owner refreshes R2 from the committed values and treats it as newly polled
   at its epoch e ("A landed page refreshes the rows the owner has in memory" above).
3. Its next flush marks R2 `Seen = e`. R2 now carries (c, g + 1), so it is a cohort row, eligible, and delivered
   during the drain before K, the later kept row of its sender. The gate counts it from the mark.
4. A model-held R2 stays held and counts as taken up under the held rule; it is handed back with the next input. A
   poll that was in flight when the commit landed and read R2 at g discards that key, and the next poll reads the
   committed row.
5. An unknown commit outcome resets the owner, which re-polls whichever version landed; a takeover gives a new owner
   that polls the committed rows. The page's update and its count check are unchanged. The page cases are E-9 and
   E-10.

**R4-m3: a re-armed timer's timing after a further interruption.**

1. Trace: T was re-armed (`Rearmed`) and later kept `Arrived`. A page of generation g restores it with (c, g).
   Before anyone consumes T, an interruption comes, and the next recover opens generation g + 1 while T is pending
   with that older, non-null tag. A selection naming T is still an idempotent no-op, but the text said a pending
   `Rearmed` T fires after Complete, which fits only a pending T with a null tag.
2. Revised: a pending `Rearmed` T has two branches. With a null `ReleasedBy` it was armed by an Open and waits for
   Complete. With an older non-null `ReleasedBy` it is `Stale`, and a page of this drain releases it, in tuple order
   within the cohort ("Checks" and "When a re-armed timer fires" above).
3. So in the trace a page of g + 1 restores T through the `Stale` arm, with (c, g + 1), and T is delivered during the
   drain, ahead of every unreleased row; its frame resumes once.
4. B2-5 now states its exception: an `Arrived` row whose timer is retained `Rearmed` is the accepted no-op.
5. S-9's stall mutant now makes the gate row explicitly unpolled: withholding T from the model alone still lets the
   poll mark it `Seen = e`, which passes the gate. The page case is RA-1.

**R4-B2: a delete could strand a live subtree.**

1. Trace: P-7's fixture, P → A → G → G2, with G live and job J in flight. A is in its unprotected parked-delete drain.
   P's terminate commits, including its parent-bound send to A, while A still exists. That send is delayed until just
   after A's Complete, which writes A's Faulted notice and its retirement marker. The delete does no subtree work,
   `Retire` keeps no registry, and `TargetError` refuses the delayed send as `RetiredCreation`. G and G2 keep
   running, no owner is obliged to cancel them, P has its one Faulted notice, and invariant 17 fails.
2. Revised: A's marker keeps A's recorded `Parent` (P) and `ParentCreation`, its child registry ({G}) and its job
   keys ({J}), as they were at retirement ([data lifecycle](data-lifecycle.md#proposed-the-qualified-delete)).
3. The delayed terminate reaches the marker and is refused as `RetiredCreation`, as built. It has an issued origin,
   its `TargetCreation` is the marker's creation, and its `Sender` and `SenderCreation` are the recorded parent's, so
   the same commit sends G a terminate from A, cancels J and clears both from the marker.
4. G accepts the terminate under its parent binding to A and ends Cancelled, and its own cascade ends G2. G's notice
   to A and J's late result meet the marker and are refused there. P receives nothing more, and invariant 17 holds.
5. The other order: delivered before Complete, the terminate is refused as "resolution open" and cascades from A's
   row, as P-7 states; Complete then retires A with no children and no jobs, and the marker retains nothing.
6. Any other sender at the marker gets the plain refusal, and the obligation stays for the parent. A delete that took
   over a discard ends in the same built delete and leaves the same marker. A child that was itself deleted cascades
   from its own marker by the same rule, so the cascade finishes inductively. Delete semantics stay as they are:
   deleting an instance terminates none of its children.
7. No successor takes the obligation over: a marker with a recorded parent belongs to a generated child id, and a
   start to a retired child id is a supplied creator and is consumed with no change (existing prototype behaviour,
   `Messages.cs` 52–54, `Turns.cs` 112, `Worker.cs` 1030–1037), while a keyed start has no parent
   (`KeyAdmission.cs` 31–32). As a defence in depth, the creation protocol also refuses a new creation at an id whose
   marker still holds an obligation, with reason `ObligationOutstanding`; in valid operation it never fires for a
   `Held` obligation. (Corrected by the fifth review's R5-B2: a successor start is refused in valid operation, as a
   retryable refusal, while a retired creation's cascade is still `Discharging`, for at most ⌈n / `P_cascade`⌉ pages;
   key admission decides it before writing a receipt, and this refusal stays the defence below ingress; case PC-3.)
8. Executed in this repair: RT-1, with the real `CreationProtocol.TargetError` and `CreationProtocol.Retire`
   extended in the harness only as proposed, runs order (a) under the reviewed rules (G, G2 and J survive), the
   repaired rules (all three terminated) and the negative control (the marker drops the registry): `rt1-a` and
   `rt1-a-g-deleted`. Order (b), `rt1-b`, passes under the reviewed rules too, since #4756's open-drain cascade
   already covers a delivery before Complete. It also runs a non-parent's terminate at the marker (`rt1-nonparent`)
   and the successor cases (`rt1-successor-unreachable`, `rt1-successor-gap` and `rt1-successor`). The page cases are DT-7 to DT-9
   and the data lifecycle page's RM-1.

**R4-M5: a Park selection omitted the actor identity.**

1. Trace: A and B, under two caller keys of tenant t, are each on creation 1 and park at first Park revision 7 at the
   same database time. A selection read for A is used with a request whose target is B. The store pins B, and every
   owner check passes: tenant, creation and both mark components are equal. B is discarded, or resumed or retried with
   no handles.
2. Revised: `ParkSelection.Instance` carries A's canonical tenant-qualified actor id, and the owner checks tenant,
   then `Instance`, then creation, then the mark.
3. At B the `Instance` check fails, so the command is refused (reason target) and B is unchanged.
4. The opaque handle encodes `Instance`, and `ParkedItem` and `KeptPage` expose it, so the read model, the API and
   the owner carry one selection that cannot be separated from its target. Same-key reuse (P-1) still turns on the
   creation check, since A and B there share one id. The page case is PS-1.
5. The same rule extends to the qualified delete, whose selection had the same shape: `DeleteSelection` gains the
   canonical tenant-qualified actor id, checked right after the tenant (reason target), so a delete read for A and
   sent to B, with a colliding creation and terminal mark, is refused and B is unchanged ([data
   lifecycle](data-lifecycle.md#proposed-the-qualified-delete), case RD5b). An operator's terminate carries it too,
   so a terminate with `ThenDelete` read for A and sent to B, both ended on creation 1, is refused as target before
   its ended-run checks and B is not deleted (case RD8d).

**R4-M1: the event match outgrew the metadata bound.**

1. Trace: a model subscribes a wait of 800 bytes of U+0001 under a short key, actor and event, which admission
   accepts. An outside publish is accepted. The derived delivery's `Match` is the JSON of four names and the
   creation, whose escape writes each U+0001 as six bytes, so it is 4,877 bytes (4,868 with the probe's names),
   above the claimed 4`N` + 192 = 4,288. At four names of 1,024 bytes the probe measured a `Match` of 24,787 bytes,
   above `M` itself, and a delivery id of 3,179 bytes with three variable parts. Refusing the derived row defeats an
   accepted match, and admitting it breaks the page bound.
2. Revised: `Match` is the 64-byte hex SHA-256 over the canonical encoding of the stored event's `(Q(t, key), event
   id)`, and the delivery id is `ev:` and the 64-byte hex SHA-256 over `(event id, instance, creation, wait)` ("The
   event match is a fixed digest" above). Both sizes are fixed, whatever the names, and the page's variable fields
   come to at most 3,712 bytes, well inside `M`.
3. The names keep their limits where they enter: `Subscribe`, `Publish` and an outside publish refuse a name above
   `N` and a payload above 16 MiB less `M`, `F`, `C` and `I`, before any write, and the start-subscription registrar
   refuses a start input above `I`. The components travel in a length-prefixed header ahead of the unchanged event
   payload, within `C`, so every derived delivery, at publish or at catch-up, is admitted, and no refusal happens
   after the source accepted. Both sides compute the digests from the persisted rows, so a publish and a
   catch-up write one row. Cases EM-1 to EM-3 qualify the real route at K3 and K4.

**R4-M2: subscription restoration escaped both Open limits.**

1. Trace: a model acquires subscriptions over many turns and arms no timer or job. Park removes the subscriptions,
   and recover restores all of them in its one commit. `W_max` and `B_plan` counted timers and jobs only, so a plan
   of zero counted waits could write, for 65,536 short waits under a 900-byte MongoDB actor id, over 56 MiB of
   subscription writes, and the kept-event catch-up added unbounded work beside them.
2. Revised: a subscription is one of the plan's `W_max` waits, restored by a plain insert, and its write counts
   7,168 bytes toward `B_plan`, the measured MongoDB insert entry at the name limits with headroom ("The plan's byte
   bound" above). The model enforces both bounds at `Subscribe`, as at arm, and the Open refuses an over-bound plan
   with reason "plan". The trace's model faults at its 3,511th subscription, by bytes, before it reaches `W_max`
   (at its 3,499th when the subscriptions keep events and the meet ticket is charged; its 2,578th since the sixth review's R6-M2 charges the coverage and conflict writes), and with the check skipped its
   recover is refused with no change.
3. The Open reads no event. It writes the subscriptions and, when any asked for kept events, one meet ticket by
   reference; the catch-up runs after the commit on the ordinary meet path, at most `P_page` deliveries per step
   under a committed cursor ("Subscriptions" above). A publish racing the restore reaches each wait through one side
   or both, and the digest identity collapses the second. Cases SB-1 to SB-3; TR-5's timer and job runs stay as they
   were.

**R4-M3: a job restart had no durable invocation recipe.**

1. Trace: Native evaluates a background activity's bindings at arm and passes the inputs, iteration path and effect
   key only to the ticket; `WaitEntry` keeps frame, kind and key. The probe ran this on the real model: the arm sent
   `Service = alpha` and the effect key `c9_0/subject_1/c/-/0`, a sibling then set the bound variable to `beta`, and
   the state held no `alpha`. The lookup restarts a planned job when its ticket is kept `Consumed`, ordinary done or
   absent. A payload-free marker or a swept row leaves nothing to copy (`ExecuteJobAsync` throws
   `ArgumentNullException` on a ticket with no arguments, executed in this repair), and evaluating the bindings again would send
   `beta`.
2. Revised: the model records an immutable `JobSpec` at arm, Native's `WaitEntry` gains it under state version 14,
   and every restart writes its ticket from the spec under the next `JobGeneration`, keeping the invocation's effect
   key ("The job spec" and "A restarted job" above). Run from the arm-time arguments, the probe's real background
   activity received `alpha` and the original effect key.
3. The second trace: an interruption leaves the old `Consumed` g0 beside the restarted g1, and the next command is
   retry-all. The physical entry has no generation check (`Worker.cs` 1166–1203), so a restored g0 would be polled,
   classified as a ticket (`Worker.cs` 654–658) and entered, and the generation filter on results would act only
   after the second invocation (`Worker.cs` 1247–1252). Revised: the Open decides that a kept job row of an obsolete
   generation is cleared; retry-all's page clears g0 and never restores it, and a selection naming g0 is refused.
   g1 runs once. Cases JS-1 to JS-3.

**R4-m1: S-5 asked for a page no legal page can reach.**

1. Trace: with `M` = 24,576 a page of `P_page` = 170 rows is at most 170 × `M` = 4,177,920 bytes, and `B` − 1 is
   4,194,303, 16,383 bytes more, so rows summing to `B` − 1 cannot both meet the row bound and fit one page.
2. Revised: S-5 takes the largest attainable page, 170 rows each at the largest admitted encoded size, at most
   4,177,920 bytes, and a 171st such row on a second page of one. S-6 keeps the over-limit refusals.

**R4-m2: TR-5's statement oracle contradicted the singleton exception.**

1. Trace: the commit rule gives a row above the 4 MiB budget a statement of its own, while TR-5 required every
   captured statement to be at most 4 MiB, and its 16 MiB row was a kept row whose payload no Open statement carries.
2. Revised: TR-5 run 4 asserts two ceilings, every multi-row statement at most 4 MiB (or the provider's lower limit),
   and every statement above 4 MiB a singleton of one admitted row plus the measured protocol framing, within the
   provider's own limit; its large rows are the instance envelope the Open updates and a re-armed timer at the
   message row's cap, both transmitted by the Open. The kept 16 MiB row stays as a control that no statement
   carries.

## Fifth review: counterexamples and answers

The [fifth gate 2 review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6973554094)
found two schedules that leave descendants running after their parent's termination commits, a registry too large
for one cascade commit, and gaps in the drain's backpressure, the poison comparison, a completed job's durable
identity, the parent binding at a running recipient, the publish ticket's size and event eligibility across a
restore. Each entry below is a "Fifth review: counterexample and answer": the reviewer's trace is restated, then
replayed step by step under the revised rule. Everything here is proposed. This part answers R5-B1, R5-B2, R5-M3,
R5-M4, R5-M5 and R5-M6; the event and size findings (R5-M1, R5-M2 and R5-m1 to R5-m3) are answered in "Event and size findings"
below. "Executed in this repair" names the runs of the two harnesses written for this round, each case run under
the reviewed rules and the repaired rules, with a negative control that restores the faulty rule or mutations of the
repaired rules where they bite. `prototypes/kernel-model/experiments/Review5.Termination` (RT-2, RT-3 and RT-4, with
R5-m3's runs) merged in #4789 as `72f0aaff2` and ran its 140 runs at `74d873e3c` (`--all` output SHA-256
`8fda737a3b3736d6df53ee3ec875fc67aa58b00d1e7c50a48f020ccf8c4a8581`), and
`prototypes/kernel-model/experiments/Review5.Delivery` (EV-1, FR-2, JR-1 and PO-1) merged in #4790 as `5f5320a04` and, with the
Open's full lookup added to JR-1 in #4793, merged as `1aaf7992b`, ran its 112 runs at `97efc2803` (`--all` output SHA-256
`2116e79c210a888b57f26768acc1fb71f764784b3399ef3fcefea4ca3d6679e8`); each harness's README holds its run table and
`results.txt` its output. A part of a case that no run exercises is labelled "proposed, qualified at" its milestone.
No Worker, provider or simulation implements these rules yet, and the page cases below qualify them at their
milestones.

**R5-B1: a settled job ticket could stop a retired child's cascade.**

1. Trace: P commits its tree terminate while A's delete drain is open. A's Complete retains G and job (J, g) on the
   retirement marker. Before P's delayed terminate arrives, a fresh owner polls J; A is retired, so `StartJob`
   stands the ticket down (`Worker.cs` 1166–1177) and the fenced dispatch refuses and consumes it as
   `RetiredCreation` (1050–1055). The marker still lists (J, g). P's terminate then needs one commit that cancels J,
   sends G a terminate and clears the list. The built cancel is a counted consume (`Worker.cs` 1452), and the store
   refuses a batch whose consumed key is gone (`InMemoryStore.cs` 116–117). The reviewer sent that batch to the real
   in-memory store: with J pending the child's send landed; with J absent the commit raised "consumed message
   c3_0/a_1!1_j is not there", P's terminate stayed pending and no child send was stored. Every retry asks for the
   same missing key, so G and G2 stay live.
2. Revised: every job cancel of a cascade is the idempotent cancel by exact key ("A termination cascade is paged and
   idempotent" above). The owner of A's partition decides J's presence under its own fence, since J's ticket lives
   in A's partition inbox, which only that owner consumes.
3. Replay: P's terminate reaches the marker and is refused as `RetiredCreation`. It is parent-bound, so the refusal
   path composes the cascade from the list. J's ticket is absent, so its cancel is a no-op and is not counted. The
   commit consumes exactly P's terminate (1), sends G a terminate from A and writes the marker at its revision plus
   one with the list cleared, and the store's count check passes.
4. G accepts the terminate under its parent binding to A and ends Cancelled, and its own terminate ends G2. P has the
   one Faulted notice from A's Complete and nothing more, and invariant 17 holds.
5. The other states of J: pending, it is consumed (counted 1) and cancelled in process, as built; `Done` and kept by a
   Park, its `KeptBy` is cleared, a state the open-drain refusal can meet and a marker cannot, since a delete drain
   clears every kept row before its Complete; `Done` with no `KeptBy`, nothing. A mixed list takes each entry by its
   own state in the one commit.
6. The arrival orders: delivered before A's Complete, P's terminate is refused as "resolution open" and cascades from
   A's row under the same cancel (J-1); delivered twice, it is one row by its wire id and is consumed once; with a
   takeover between J's settle and P's terminate, the new owner reads J's presence under its own epoch and the
   outcome is the same. The live terminate (`WorkflowInstance.cs` 196–198) shares the rule, so a ticket the fenced
   job-result conversion already consumed (R5-M4 below) is a no-op there too.
7. Executed in this repair: RT-2's `rt2-witness` (the reviewer's store witness), `rt2-pending`, `rt2-done-kept`,
   `rt2-absent` and `rt2-mixed` (a ticket pending, done and kept, absent and mixed), `rt2-settle-after` and
   `rt2-open-drain-mixed` (the other arrival orders), `rt2-duplicate` (a duplicate parent delivery after an unknown
   outcome), `rt2-takeover` (a takeover between the settle and the parent's terminate), and `rt2-live-pending` and
   `rt2-live-pending-g0` (the live terminate with a pending ticket), each under the reviewed, repaired and negative
   rules, with `presence-from-memory` on `rt2-absent` and `rt2-takeover` (#4789). Each hands the composed cascade
   batch to the real `InMemoryStore.CommitAsync` with counted inputs. The live terminate of a ticket the job-result
   conversion already consumed (step 6) is proposed, qualified at K3. The page cases are IC-1 and IC-2, and the data
   lifecycle page's RM-2.

**R5-B2: one cascade could need more sends than a turn may make.**

1. Trace: A starts 10,001 children that keep running over many legal turns, one per turn being enough. A's registry
   holds them within the row limit: 398,936 UTF-8 bytes with the real `ChildRegistry`
   (`src/Kernel.Workflows/ChildRegistry.cs` 15–17). A is deleted while P's terminate is in flight, and there are no
   jobs. A's marker must send 10,001 terminates and clear its list in one commit. Enforcing the 10,000 send cap stops
   it on every attempt; exempting the kernel's refusal leaves its work unbounded, and the live terminate of the same
   registry meets the same cap.
2. Revised: termination is a durable, paged kernel obligation ("A termination cascade is paged and idempotent"
   above). A's marker keeps the list `Held` until P's terminate arrives.
3. Replay: P's terminate reaches the marker and is refused as `RetiredCreation`. The refusal path moves the list to
   `Discharging` and, in the same commit, sends terminates to entries 1 to 1,000 (`P_cascade`), writes the marker at
   its revision plus one with the cursor after entry 1,000, and sends `kernel.cascade` with serial 1.
4. Each `kernel.cascade` reaches the marker, is refused as `RetiredCreation`, matches `Cascade.Control`, and its commit
   sends the next 1,000 terminates, moves the cursor, rewrites the marker at its next revision and sends the next
   control. The eleventh page sends entry 10,001, clears the list and sends no control.
5. No page carries more than 1,000 cascade entries, and its sends are those entries plus one `kernel.cascade`. A
   page is a kernel commit, never a turn; the send cap still binds every turn, model and actor-type alike, so a model
   turn that sends 10,001 messages fails and parks (fairness decision 3). Each child receives one terminate from A, under
   an id derived from its page's revision, and cascades through its own.
6. A takeover between pages: the new owner polls the outstanding `kernel.cascade`, reads the cursor from the marker
   and continues. A page whose commit outcome is unknown is reset and recomputed from the committed marker, with the
   same ids if it had not landed. P received its notice from A's Complete, and the marker sends nothing.
7. The other paths: a running A with 10,001 children that P terminates ends Cancelled in its terminate turn, which
   sends P the one Cancelled notice and the first 1,000 terminates and records the rest as `Discharging` on A's row,
   and the pages follow there. A delete of A while `Discharging` keeps the obligation on the marker, and the pages go
   on with no further terminate. The open-drain refusal starts the same pages from A's row.
8. Executed in this repair: RT-3's `rt3-live-p`, `rt3-live-p-plus-1` and `rt3-live-10001` (a running A at
   `P_cascade`, `P_cascade` + 1 and 10,001 children), `rt3-marker-10001` (this trace), `rt3-open-drain-10001`,
   `rt3-jobs-marker` (B1 and B2 together), `rt3-takeover` (after page 3), `rt3-crash-landed` and `rt3-crash-lost` (an
   unknown outcome at page 5), `rt3-delete-discharging` and `rt3-key-reuse-discharging`, each under the reviewed,
   repaired and negative rules, with `page-above-cap`, `no-next-control` and `delete-drops-discharging` (#4789),
   asserting every descendant's terminal state and one terminate per child; its `rt3-thendelete-*` runs execute
   ThenDelete on running, protected and ended rows (PC-5). A model turn of 10,001 sends failing under the cap (step
   5) and PC-4's Native tree are proposed, qualified at K3: the harness's stub model takes no turn. The page cases
   are PC-1 to PC-5, and the data lifecycle page's RM-3.

**R5-M5: a running recipient had no parent check.**

1. Trace: RM-1's negative fixture lists, in A's retirement list, a same-tenant running H with the right creation whose
   recorded parent is Q. Validation at retirement checks no membership and relies on H's refusal. P discharges A's
   marker, so H receives a correctly issued terminate from A. H's creation check passes, and the running-actor
   control path cancels H and its children (`WorkflowInstance.cs` 63–64, 155–158, 177–203). RT-1 passed only because
   its fixture checked `ParentBound` before its live termination (`experiments/Review4.Blockers/Rt1.cs` 329–330,
   352–357).
2. Revised: the kernel's cascade terminates carry the cascade origin mark, which no model can set, and every
   recipient checks the parent binding on a marked terminate in every state ("Cascade terminates are parent-bound in
   every state" above). Other authorized terminates keep their own rules.
3. Replay: the marker's cascade composes H's terminate with the mark. H is running. Before it acts it compares the
   terminate's `Sender` (A) and `SenderCreation` with its recorded `Parent` (Q) and `ParentCreation`. They differ, so
   the message is consumed with no other change. H and its child keep running, no job is cancelled and no notice is
   sent. A's marker still clears its list.
4. An ended H that still lists children: the same check runs on the ended row's control path, so its children keep
   running. A parked or draining H refuses as before.
5. A genuine child G of A passes the check in every state and is terminated as built; a retired G cascades from its
   own marker under the same binding.
6. What keeps working as built: a model's application `kernel.terminate`, sent through `ITurn.Send` with no mark,
   still ends a running instance it addresses, its child or not (`WorkflowInstance.cs` 63–64, 155–158, 177–203), and
   P-7's rule still governs it at a parked or draining row. An operator's terminate has no issued origin, so the
   ingress permission and its `ParkSelection` keep governing it.
7. Executed in this repair: RT-4's `rt4-running-unrelated` (a running unrelated H with a child),
   `rt4-ended-registry` (an ended H with a registry), `rt4-genuine-child`, `rt4-operator`, `rt4-model-send` (a
   model's application terminate applying as built) and `rt4-forgery` (a model's marked sends refused at the
   commit), through the actor type's control path, each under the reviewed, repaired and negative rules, with
   `binding-running-only`, `bind-every-issued-terminate` and `commit-accepts-mark` (#4789). A parked recipient with no
   drain open, and P-7's rule for an unmarked terminate there, are proposed, qualified at K3. The page cases are PB-1
   and PB-2.

**R5-M4: a completed job's result had no durable identity of its own.**

1. Trace: J finishes while its parked instance still waits for that generation. The page called the result new,
   untagged input and promised to restore a kept `Arrived` result. The built driver turns the in-memory delivery into
   a `JobFinished` that keeps the ticket's full key, and a worker-only `CompletedJob` flag authenticates it
   (`Worker.cs` 1247–1252, 1298–1306). The durable consume and the promotion path still name the ticket
   (`Worker.cs` 1452, 1586–1594; `WorkflowInstance.cs` 120–143). Adding `Done` and `KeptBy` alone keeps the job's
   arguments, and a recover re-enters the driver; persisting the changed kind alone loses the flag at a take, and the
   row is refused as a supplied completion. Filtering an obsolete g0 does not help the current g1.
2. Revised: the owner converts the ticket into a result row, `{ticket id}/result` in the ticket's scope, which only
   layer 2 mints ("A completed job's result is a fenced, authenticated row" under [Recover](#recover)).
3. Replay: J, at the current generation g1, finishes while A is parked. A is not deliverable, so the owner commits the
   standalone fenced write: it consumes J's ticket (counted 1) and inserts the result row with the payload, g1 and
   J's key. The parked A consumes the result row as an arrival, kept `Arrived`.
4. A takeover: J's ticket is gone, so the new owner does not run J again; the kept result row carries its own
   identity, so no in-memory flag is needed.
5. Recover: the Open's lookup finds J's ticket absent, probes the result row `{ticket id}/result`, finds it kept
   `Arrived` at the current generation, and does not restart J, so the registry stays at g1 (integration review of
   the fifth round); the drain restores the result row, the registry's current generation for J's key is g1, so its delivery is
   accepted, and the model receives the original value once. It is never a ticket kind, so the driver is never
   entered again.
6. A crash before the conversion lands leaves the ticket pending. The new owner does not start it at the parked A,
   and A consumes it as a late input, keeping it `Arrived` and, under the new rule, keeping J's registry entry; the
   recover's Open restarts J from its `JobSpec` under the next generation, clears the old ticket, and J runs again
   with the same effect key; its result then takes the conversion path. A crash after the conversion lands causes no further invocation. So J runs at most once per
   owner lost before its conversion, plus once.
7. Executed in this repair: JR-1's `jr1-crash-after` and `jr1-crash-after-keep` (a crash after the conversion
   write, before and after the parked instance kept the result), `jr1-crash-before`, `jr1-obsolete` (a kept g0 result
   row), `jr1-result-parked-consume` (a parked consumption of the result row, then recover) and the control
   `jr1-running`, with Park, takeover and recover, each under the reviewed, repaired and negative rules and, except
   `jr1-result-parked-consume`, `reviewed-kind`, with `reviewed-keeps-registry` on `jr1-crash-after` and
   `generation-blind` on `jr1-obsolete` (#4790), asserting one model result, the original value and the physical
   invocation count. The page cases are JC-1 and JC-2; JC-2's runs other than (c) and (g) are proposed, qualified at
   K3.

**R5-M6: an exhausted ticket could block the cohort's trial.**

1. Trace: an early page restores a hanging job J at 900. Before an ordinary cohort row is released, owners are lost
   during J's physical trial until J reaches `MaxAttempts`, and the next owner marks J seen. With the recover drain
   open the controls have priority: release timer R at 1100, mark it, and Complete. A takeover comes before either is
   dispatched. R is a suspect that must have its trial first. J keeps the excluded job kind and is due and exhausted,
   so it is poison input (`Worker.cs` 705–707), and `PrecedesPoison` (714–716) rejects R's trial because J sorts
   first (753–754). The `Cohort` mark keeps J from going first, R needs its trial before it can run, and neither
   moves.
2. Revised: while a recover drain is open or the `Cohort` mark is set, the poison comparison uses the cohort order
   ("The poison comparison follows the cohort order" above). Under the handoff gate of R5-M3 Complete waits for R's
   handoff, so the takeover in this trace comes while the drain is still open, or after Complete with R held; the
   rule covers both.
3. Replay: R is a cohort row and J is not, so J does not precede R and `PrecedesPoison` passes for R. R's fenced
   trial commit puts it on trial, the trial delivers R alone, and frame F completes once.
4. The open drain then completes, or the mark's read finds no offerable cohort row and the mark clears. J takes its
   built place: an exhausted input takes the `MaxAttempts` path, which re-parks the instance while the drain is open
   and parks it under a new Park after Complete.
5. The variants: the last failed owner, where J reaches `MaxAttempts` at the takeover itself; a cold takeover with no
   control kind in the inbox; and the ordinary earlier poison with no drain and no mark, where an exhausted input
   ahead of a later suspect still poisons first, as built, and no physical job starts on that slot meanwhile.
6. Executed in this repair: PO-1's `po1-open` (the drain still open, its last control pending), `po1-cohort` (a cold
   takeover after Complete with no control kind) and `po1-ordinary` (the ordinary earlier poison, the same history
   also run on a real `Worker`), an exhausted ticket against a cohort suspect, each under the reviewed, repaired and
   negative rules (#4790). In every run J reaches `MaxAttempts` at the take before the owner that marks J and R, so
   the last failed owner variant, where the takeover before R's trial is the one that exhausts J, is proposed,
   qualified at K3. The page cases are PP-1 and PP-2.

**R5-M3: the `Seen` gate let the backlog in with no model handoff.**

1. Trace: restore 170 rows, poll them and commit their `Seen` marks (`Worker.cs` 1397). Before the model receives
   one, the next eligible `kernel.drain` runs; it has priority, and the last row satisfies the gate. Restore and mark
   another 170, and repeat. The owner has 340 or more pending ordinary deliveries, the model holds none, and the held
   gauge reads zero. Each page meets its byte cap, and the bound across pages does not follow. FR-1's page checked
   its gate only on its empty-page branch (`experiments/Review4.Blockers/Fr1.cs` 725–749).
2. Revised: a page waits until the previous page's gate row is handed off, and an admitted page restores at most
   `P_page` − u rows, u being the drain's restored rows not yet handed off ("The progress gate" above).
3. Replay: page 1 restores 170 rows, and the owner polls and marks them. The next `kernel.drain` runs first, by
   priority. Its gate row is marked and not handed off, so it restores nothing and sends its control again with a
   delay. The model's turns take the rows in tuple order, and each committed turn hands its rows off. Once the gate
   row's turn has committed, page 2 restores 170 − u rows, all 170 when every row of page 1 has been handed off.
4. Held rows count as handed off, so a model that holds every row still lets the drain proceed, and the held gauge
   shows them. Suspension holds the gate as before, and resume releases it.
5. A takeover between pages resets the credit: the new owner counts the restored, not-`Done` rows as pending and
   hands them off again, by trial, delivery or `Hold`; until it does, the next page restores only what the credit
   leaves, so the lost owner's marks give no free page.
6. The bound, across any number of takeovers: the drain's restored rows not `Done` (other than ticket kinds (job,
   meet and publish), which are kernel work) are at most `P_page` plus the restored rows the model holds or has held and not since consumed, and their
   bytes at most `B_credit` plus those held rows' bytes plus one row larger than `B_credit` restored alone;
   `elsa.kernel.drain.pending` and its byte gauge report the part not handed off beside `elsa.kernel.drain.held`.
7. R4-B1's schedule: Complete also waits for its gate row's handoff, so in that base trace R reaches its model turn
   before C completes, and the outcome is unchanged (F completes once, then A ends the model). The cohort precedence
   after Complete stays for the rows the gate lets through unconsumed (held by the model) and for
   every row a takeover after Complete leaves unconsumed. FR-1's recorded runs stay the fourth round's evidence under
   the `Seen` gate.
8. Executed in this repair: FR-2, built from a copy of FR-1's machinery, as `fr2-base` (three full pages, controls
   winning before any model work), `fr2-hold`, `fr2-suspend`, `fr2-takeover` (between pages 2 and 3),
   `fr2-hold-takeover`, `fr2-bytes` and `fr2-zero-cut`, each under the reviewed, repaired, negative and `seen-gate`
   rules, with `held-gate-waits`, `pages-while-suspended`, `p-page-whatever-u`, `credit-metadata-only` and
   `zero-cut-completes`, and R4-B1's two traces re-staged under the handoff gate as `fr2-r4b1-base` and
   `fr2-r4b1-held`, with `seen-gate-no-precedence` (#4790). Each run asserts the harness's own peak of pending rows
   and bytes and its held count, and reports a neighbour's turns as a count, unasserted, since one turn per instance
   per pass holds the share by construction; the gauges, an asserted turn share and HC-2's runs (d) to (i) are
   proposed, qualified at K3. The page cases are HC-1 to HC-3.

### Event and size findings

The publish ticket's size, event eligibility across a restore, and three case fixtures that could not run as
written (R5-M1, R5-M2 and R5-m1 to R5-m3) are answered here, in the same form; EV-1 is the delivery harness's case.

**R5-M1: an admitted turn publish outgrew the row cap in its ticket.**

1. Trace: a turn publishes a valid JSON string of `<` at the source limit, 16,621,568 bytes. Before any routing, the
   turn writes its durable publish ticket, whose payload is `Json.Write` of an `EventRow` (`Turns.cs` 327–335,
   429–433). The JSON writer escapes each `<` as six bytes, so the review's witness measured a ticket payload of
   99,729,457 bytes, against the 16,777,216-byte row cap. The commit would refuse the row, and the turn fails although
   its `Publish` was accepted. The size probe's header round trip never took this path.
2. Revised: the ticket's payload is a ticket header in the delivery header's framing (version, the length-prefixed
   key, generated event id and keep window, the presence flag) followed by the payload bytes exactly as published
   ("A turn's publish ticket carries the event in the same framing" above). Nothing is re-encoded.
3. The same publish now gives a ticket of at most 2,278 header bytes plus 16,621,568 payload bytes, inside `C` plus
   the payload limit and so under the row cap. The owner decodes the ticket into the event row with the payload
   byte for byte, and the delivery carries it unchanged.
4. The generated event id counts inside `C`: at the admissible origin limit it is `N` + 129 = 1,153 bytes, so a
   delivery header that carries it is at most 3,284 bytes, 6,568 in SQL Server's UTF-16. Where a composite key limits
   the event row, a turn's `Publish` checks its key jointly with that id and refuses past it as a model fault, so no
   accepted publish meets a refusal at the event insert. Case EM-4.

**R5-M2: the timing of a publisher against a restore decided delivery.**

1. Trace A: E is persisted while W is parked and its wait absent. The publisher pauses before its subscription read.
   A recover restores a wait that declined kept events. The publisher's read finds the restored wait and inserts a
   delivery, so on the in-memory publisher path the outcome moved from none to one only by delaying the read. The
   digest identity deduplicates the wrong match.
2. Revised: the fact that orders acceptance against registration is the event's match step, the processing of its
   publish ticket, which reads committed subscriptions after the publish committed ("Event eligibility" under
   [Recover](#recover) above). A non-keeping wait receives exactly the events whose match step reads its committed
   row, and nothing reaches it by catch-up.
3. In trace A the paused publisher's match step was still outstanding when the Open committed, so it reads the
   restored row and delivers E once; the undelayed step had finished before the Open and is never matched to the
   wait. Both outcomes follow the stated rule, and the window in which the second can happen is the publish ticket's
   outstanding time, normally the publisher partition's poll and unbounded under partition loss, a takeover or a
   backlog. This resolution of trace A, and Park's "a wait that did not ask for kept events misses them" restated to
   say exactly that, were a coordinator design choice, and are an owner decision since 18 November (sixth review). A replayed publish ticket after a takeover
   runs its steps again from the committed cursor: each reads the subscriptions committed by then, and each delivery
   it repeats is a duplicate. The match step is paged, so a broadcast to any number of waits never needs one long
   step, and a step's deadline costs an attempt only when the step commits no progress.
4. Trace B: the publish completes during the Park with E kept through 100. The Open restores a keeping wait at 99.
   A catch-up at 99 delivers E; after a sweep at 101 the same catch-up finds nothing, because the catch-up compared
   the window with its own time and the sweep had deleted E.
5. Revised: a keeping wait's entitlement point is `P0` = max(`RegisteredAt`, `H` + 1 ms), with `H` the tenant's
   sweep horizon read under a share lock in the registering commit, and recorded on the meet ticket and in the
   ticket's coverage entry. The wait is entitled to the events of its keys with `KeepUntil` ≥ `P0` that its
   catch-up finds. The sweep deletes E only when its window has passed, no live publish ticket row names it and no
   coverage entry on E's key has `P0` ≤ `E.KeepUntil`, in batches of `P_page` filtered before they are limited, and
   raises `H` in each batch's transaction.
6. In trace B, `P0` is 99, so the sweep at 101 keeps E, and the catch-up at 101 delivers it once, as the one at 99
   does. A later page after a takeover finds the same events by the same stored values. Once the catch-up has passed
   the wait, its coverage entry is deleted, and the sweep deletes E and raises `H` to 100. A clock that then steps back to 99
   gives a new keeping wait `P0` = 101, so it never claims the swept E. A suspended instance's catch-up runs to its
   end, since meet tickets are kernel work, so no wait holds events beyond its own catch-up on its own keys.
7. The outside path had the same dependency: every provider's match reads the stored event row (`InMemoryStore.cs`
   281–285, `EfStore.cs` 690–699, `MongoStore.cs` 461–462). So an outside publish writes the same kernel-issued
   publish ticket, and its event row, inserted by insert-or-read under provider equality with the publish, is the
   receipt for equivalent spellings; the cost is one small ticket row (SB-6). The start registrar's catch-up
   becomes a kernel-issued meet ticket (SB-8).
8. Against invariant 13 and the 12 November card: a non-keeping wait receives every event accepted after its
   commit, once, with no clock, and is never matched to an event whose match step finished before it registered; a
   keeping wait receives every event whose window was open at its registration, once. The rejected options, a clock
   allowance and a catch-up for every subscription, are recorded with their traces and costs. Executed in this repair:
   EV-1's trace A as `ev1-a` (under the reviewed and repaired rules, with `match-at-acceptance` and
   `split-no-row-hold` in place of a negative control) and its control `ev1-a-after`; trace B as `ev1-b`,
   `ev1-b-paged` and `ev1-b-stepback`; and `ev1-b-suspended`, `ev1-replay`, `ev1-replay-atomic` and
   `ev1-replay-consumed`, each under the reviewed and repaired rules, a negative control where the run has one, and
   `match-at-now`, `p0-at-page-commit`, `no-horizon`, `meet-waits-while-suspended`, `split-no-row-hold` and
   `no-publish-coverage` where they bite (#4790). They back SB-4's runs (a), (a′) and (b) and part of (c), SB-5's
   runs (i), (ii) and (iv) and part of (iii), the suspended catch-up of SB-7's run 1 (`ev1-b-suspended`), and the
   sweep's publish-ticket and delivery-row holds behind SB-6 and SB-5's run (vi) (`ev1-replay-consumed`); every other
   run of SB-4 to SB-9 is proposed, qualified at the case's milestones. Cases SB-4 to SB-9.

**R5-m1: EM-1 and EM-2 asked for names the narrower providers refuse.**

1. Trace: a caller key of 1,024 ASCII units plus its qualified prefix exceeds SQL Server's 450 units and MySQL's 255
   scalar values before any match digest exists, so EM-1's positive fixture and EM-2's success at `N` cannot run
   there. PostgreSQL's B-tree entry limit refuses three 1,024-byte names in the subscription key too (3,207 bytes).
2. Revised: EM-1 and EM-2 use each provider's largest jointly admissible names, counting the composite keys and the
   suffix of every issued id (on SQL Server a raw key, caller key and wait of 105 units each and an event id of 280,
   derived from the declared widths, unmeasured, measured at K4), and admit a caller key only at the issued-id bound,
   its issued ids being the caller key plus 128 characters at a 3-digit unit count (at most 322 units on SQL Server and 127 characters on
   MySQL, derived and unmeasured), and on each narrower provider the 1,024-byte sets are a source-refusal control. The full digest-route case stays
   where it is admitted: the in-memory store, SQLite and MongoDB.

**R5-m2: TR-5's singleton fixture could not persist its own setup.**

1. Trace: run 4's test model stored the transmitted timer payload in its state. The payload alone is 16,728,064
   bytes, and the instance before `ParkRecord` is limited to 16,457,728, a deficit of 270,336 bytes before the rest of
   the state is serialized.
2. Revised: the model keeps a compact deterministic recipe, a seed and a length, from which the arm and
   `RestoreWaits` rebuild exactly the armed bytes, and pads its state independently to the reserve limit. Run 4
   asserts the two genuine large singleton writes, the envelope update and the timer insert, and a kept control whose
   payload no statement transmits; the old fixture is its negative control. The multi-row and singleton ceilings are
   unchanged.

**R5-m3: RM-1's foreign-job mutation could not produce its outcome.**

1. Trace: a retirement entry holds a job key and a generation, and the cascade derives the ticket from the marker's
   own id and creation. Naming unrelated B's job key in A's list derives A's ticket of that key, never B's, so the
   mutation "Validate admits a foreign ticket" cannot cancel B's job. The fourth round's harness represented jobs by
   string keys, so its job assertion said nothing about generations.
2. Revised: RM-1 asserts that a retirement listing an off-registry pair, a key `Jobs` does not hold or a generation
   other than the one it holds for the key, is refused, and that the cascade's cancel of (J, g) leaves a newer
   same-origin ticket (J, g + 1) unchanged. The foreign cancellation is tested by mutating the derivation itself, to
   take B's id and creation. RM-1's fixture represents each job by its key and generation ([data
   lifecycle](data-lifecycle.md#proposed-the-qualified-delete), RM-1).

## Sixth review: counterexamples and answers

The [sixth gate 2 review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6980291737)
found two Blockers, eight Majors and three Minors against `d49aad9cd`: an ending commit that can outgrow its
envelope, a publication lost to an earlier receipt, and gaps in the cancel of a converted job, tied sequence numbers,
publications interrupted by a Park, the terminate drain's job entry, the catch-up chain, the Open's admission,
MongoDB's sweep, the per-step byte allowance and several fixtures. Each entry below is a "Sixth review:
counterexample and answer": the reviewer's trace is restated, then replayed step by step under the revised rule.
Everything here is proposed unless it says otherwise. Implementation-level items (byte budgets, provider transaction
mechanics, fixture arithmetic and cost measurements) are stated as conditions with the milestone that qualifies them
(owner direction, 18 November). "Executed in this repair (`experiments/Review6.Blockers`, #4798)" names runs of
`prototypes/kernel-model/experiments/Review6.Blockers`, written for this round and merged in `23029ed8f`. Every other part of a case is
proposed, qualified at its milestone, and no Worker, provider or simulation implements these rules yet. R6-B1,
R6-M5, R6-M6, R6-M7, R6-M8 and R6-m2 are answered first; the event findings follow under "Event findings".

**R6-B1: an admitted Native parent could outgrow its envelope before sending the first terminate.**

1. Trace: a running Native parent A has one registered child G. Its state holds 3,500 open empty invocation inboxes
   with matching frames and padding, 16,436,864 UTF-8 bytes, which the fifth round's check admits with 4,096 bytes to
   spare after the one-child charge of 384 and the 16,384-byte header. A plain terminate runs `TerminateMetadata`
   (`WorkflowInstance.cs` 183–190), which closes every inbox (`NativeModel.cs` 171–178, `InvocationLifetime.cs`
   29–45) and grows the state by 24,500 bytes to 16,461,364, 3,636 above the 16,457,728-byte envelope. The ending
   commit is refused on every attempt, and G's terminate, which rides in it, is never sent.
2. Revised: every model reports `TerminalGrowth(state)`, and the envelope check of every commit of a live row counts
   it beside the registries at the marker's sizes and the header ("The ending commit always fits" under [Every Park
   operation names its Park](#every-park-operation-names-its-park)).
3. Replay: the turn that would bring A to the witness state is checked at 16,436,864 + 24,500 + 384 + 16,384 =
   16,478,132 bytes, above the envelope, so it fails and parks as an over-envelope turn; the state never commits.
4. The largest state the check admits with those inboxes and that child is 16,416,460 bytes under the sixth round's
   check (16,416,449 under the current projection, TG-1(b)). A terminate there
   writes at most 16,440,960 bytes of state, with the registry replaced by the list within the charge already
   counted, so the commit fits. A ends Cancelled with its inboxes closed, G receives its terminate in that commit,
   and P receives its one notice.
5. A protected `ThenDelete` keeps the live row by the same commit, with the same bound. A model with no terminal growth
   returns 0, so nothing else changes for it.
6. Executed in this repair (`experiments/Review6.Blockers`, #4798): NT-1's `nt1-witness`, `nt1-at-bound`,
   `nt1-thendelete-protected` and `nt1-no-inboxes` run a synthesized Native state close to the witness through the
   real `TerminateMetadata` and a real `WorkflowInstance` terminate, under the reviewed check (the pre-terminate
   state admitted, the ending commit over the envelope, no child terminate sent), the repaired check (the
   over-growth turn refused, or the terminate commit fits) and the negative control (the bound removed), asserting
   the first counted child send and the envelope sizes, with a protected `ThenDelete` variant. `nt1-growth` measures
   Native's bound at 7 bytes per open invocation inbox and a conservative 23 per non-final receipt; SQL Server's
   UTF-16 doubles these (derived), and the stored encoding is qualified at K4. Cases TG-1, TG-2 and PC-5's
   terminal-growth run, which is proposed, qualified at K3.

**R6-M5: an ordinary cancel and a running turn still named the pre-conversion ticket.**

1. Trace: J finishes while A cannot take it, and the conversion commits J's consumed ticket and a pending result row.
   After resume an earlier input X comes first, and its turn drops J's wait; Native's `Unregister` calls `CancelJob`
   (`NativeModel.cs` 530–536). `DurableTurn.CancelJob` adds the absent ticket to the counted cancel set (`Turns.cs`
   405–411), and the reviewer's store probe raised "consumed message … is not there", leaving X and the result
   pending.
2. Second trace: the result row reaches a running turn. `CompletedTicket` compares the key with the ticket itself
   (`Turns.cs` 464–470), so J stays registered, and a re-arm of J throws (394–397).
3. Revised: `CancelJob` is the cascade's idempotent cancel, and `CompletedTicket` and `RemainingJobs` recognise the
   current generation's `/result` key ("An ordinary cancel of a converted job" and "A running turn recognises the
   result row" under [Recover](#recover)).
4. Replay of the first trace: the owner finds J's ticket absent, so the cancel adds no counted consume; it finds the
   result row pending, so the commit consumes it as discarded. The commit consumes X and the result row, writes A
   without J, and lands. The model never receives J's result, and a later `StartJob` of J arms the next generation.
5. Replay of the second trace: the turn removes J before the model turn, the model consumes the result, and the
   commit writes no J; a held result keeps J until it is consumed. A result row that matches no registry entry, of
   an older generation or of a cancelled key, is consumed as discarded and never delivered, and a replacement
   generation stays.
6. Proposed, qualified at K3, K4 and K6. Cases CJ-1 and CJ-2.

**R6-M6: tied sequence numbers could prevent an eligible cohort trial.**

1. Trace: cohort rows R and Z share `seq` 42 from different clocks, and R sorts first by its full tuple. After a
   takeover R is a suspect and Z is exhausted. R must be tried first, but `PrecedesPoison` compares `InboxOrder` (`seq`, with `CompletedOrder` 0) strictly
   (`Worker.cs` 714–716), so R's trial is refused; delivery in tuple order stops at the unresolved suspect (845–856),
   and skipping R parks on Z before R's turn.
2. Revised: whenever cohort precedence applies, the trial comparison and the delivery order both use `(Seq, Id,
   Scope, Match)` ("Ties use the complete persisted tuple" above).
3. Replay: R precedes Z by the tuple, so R's trial runs first and alone and the model consumes R; Z then takes the
   poison path. In the reversed tie Z is the earlier poison and parks the instance first, and R is kept for a later
   recover. Outside the cohort context the built comparison holds.
4. A rule replay, proposed, qualified at K3 (injected `seq`) and K4 (MongoDB's independent sender clocks). Case TT-1.

**R6-M7: a plain resume could clear a partially delivered publication.**

1. Trace: a turn commits a publication. Its ticket commits its first page and then fails its next step until the
   publisher parks, and Park keeps the ticket `Consumed`. A plain resume cleared unselected `Consumed` rows, so the
   outstanding matches were lost without a trace.
2. Revised: a kept publish ticket is kernel work. Every recover mode, before or after the model ends, restores it
   with its committed cursor; discard, expiry, terminate and delete make it a dead letter. A kept meet ticket is cleared in every mode, since
   Park removed the subscriptions it served ("A kept publish ticket is kernel work" above).
3. Replay: the plain resume's page reaches the ticket and restores it with its cursor. The next step reads the
   subscriptions after the cursor and delivers E to each once; the first page's deliveries are not repeated, since
   a repeat is a duplicate by its digest identity. A takeover after the restore polls the pending ticket and goes on
   from the committed cursor.
4. Selected and unselected retry and retry-all do the same. Discard, expiry, terminate and delete set the ticket
   aside as a dead letter with its undelivered remainder and replay command, and the waits already delivered keep
   their deliveries; the replay delivers E once to the remaining waits while the event row exists, and otherwise
   answers "gone". A kept meet ticket is cleared by every mode and delivers nothing.
5. Proposed, qualified at K3, K4 and K11. Case KP-1. SB-7's run 7 now has a stated mode: any recover mode restores
   the ticket.

**R6-M8: a terminate drain could leave a later-page job free to start.**

1. Trace: a terminate interrupts a running recover, so the row keeps Running until Complete. Its Open's first page
   consumes 1,000 of 1,001 job tickets and cancels the jobs in process on owner A. A new owner B takes the partition
   before page 2. `Withholds` is false for the Running row (`WorkflowInstance.cs` 28–29), so B's `StartJob` and driver
   entry (`Worker.cs` 1166–1180, 1224–1234) can start ticket 1,001's body before page 2 consumes it.
2. Revised: while a terminate Resolution is open, `Withholds` is true ("No job is entered while a terminate drain is
   open" above).
3. Replay: B loads the row, finds the terminate Resolution open, and stands ticket 1,001 down. Page 2 consumes it,
   counted, and Complete ends the row Cancelled. No body is entered after the Open's acceptance.
4. A callback queued on A before the Open reaches entry after it and is refused under the slot lock; while the Open's
   commit is in flight entry is restricted; a failed Open rolls back. A driver that had already entered is cancelled
   by the first page, as built.
5. Proposed, qualified at K3, K4 and K6. Case WJ-1.

**R6-m2: PC-4's sequential Native loop built no child registry.**

1. Trace: one `DispatchWorkflow` per sequential iteration uses its default `WaitForCompletion = false`, a detached
   start (`models/Native/Activities.cs` 585–595, `NativeModel.cs` 550–554), so no child is parent-bound; with
   `WaitForCompletion = true` each iteration waits for its child, so the loop never holds 10,001 running children.
2. Revised: PC-4 builds the 10,001 children with a dedicated test model that makes one parent-bound start per turn,
   and keeps a smaller real Native cross-check, a `Parallel` (`Activities.cs` 158) of waiting `DispatchWorkflow`
   branches; the 1,000-item ForEach cap does not bind a `Parallel`, and its branch cap is qualified at K6. Both
   assert the registry and each child's recorded parent before the terminate. Case PC-4.

**Conditions** (non-blocking, from the independent review of this repair; each holds at its milestone):

- C1 (K4), now recorded: Native's measured terminal growth, 7 bytes per open inbox and 23 per non-final receipt
  (`nt1-growth`), is stated on this page; each provider's stored encoding is measured at K4.
- C2 (K3): the kernel-written terminal fields, such as the `CommitTimes` "workflow:end" entry and the revision's
  digits, fit inside the reserved 16,384-byte header.
- C3 (K6): `TerminalGrowth` is taken on the state after `UpgradeState`, or the upgrade's growth is bounded.
- C4 (K3): within one flush, a result row consumed as discarded leaves the slot's inbox.

### Event findings

The publication incarnation, the catch-up chain, the Open's admission, MongoDB's sweep, the per-step byte allowance
and the event fixtures (R6-B2, R6-M1 to R6-M4, R6-m1 and R6-m3) are answered here, with the owner's decision on
the match-step window.

**R6-B2: a new outside publication could lose its delivery to the previous publication's receipt.**

1. Trace: keep wait W registered on K. Publish (K, E), let W consume its delivery, let the receipt window end, and
   sweep the old publish ticket and event row, leaving W's `Done` delivery for a later message-sweep batch. Admit
   (K, E) again with another payload: the entry point treats it as a new publish, but its match for W derives the old
   delivery key, the surviving `Done` row suppresses the insert (`InMemoryStore.cs` 240–241), and the new event row
   then protects that row from the sweep. Had the message sweep run first, W would have received the event once. The
   reviewer's SQLite model of the unique-key and sweep predicates gave zero and one new deliveries in the two orders.
2. Revised: every event row carries a kernel-generated 16-byte `Incarnation`, a random 128-bit value, unique and never reused. The outside
   publish's insert-or-read returns the existing row's incarnation, so a duplicate within the window is the same
   publication; an admission after the sweep gets a new one, and a turn's first match step inserts its row with a new
   one. `Match` is SHA-256 over `(Q(t, key), event id, incarnation)`, the delivery `Id` over `(event id,
   incarnation, instance, creation, wait)`, and the `pub:` ticket id over `(Q(t, key), event id, incarnation)`, in
   version 2 of the canonical encoding. This changes R4-M1's formula; the digests keep their stored sizes, and the
   incarnation adds 35 bytes to the ticket and delivery headers, inside `C` ("The event match is a fixed digest"
   above). The receipt names the incarnation. The round-5 rule stays: a `Done` delivery is kept while its own event
   row, the same incarnation, exists.
3. Replay: the second admission inserts the row with i2; its ticket `pub:`H(K, E, i2) and W's delivery
   `ev:`H(E, i2, W) are new keys, so W receives the second payload once in both sweep orders, and the old `Done` row,
   whose `Match` no event row has any more, goes at the next message sweep. A slow consumer receives both
   publications, once each. A duplicate within the new window answers i2 and writes nothing. A crash after the entry
   transaction leaves the ticket for the next owner, which delivers once, and the caller's retry answers i2.
   Provider-equal spellings follow the insert-or-read, so a retry under the pin within a window answers that
   window's incarnation. A start subscription starts one instance per publication.
4. Executed in this repair (`experiments/Review6.Blockers`, #4798): PI-1's `pi1-event-first`, `pi1-gap`, `pi1-slow-consumer`
   and `pi1-duplicate` (one retry before consumption, with no owner-side `pub:` ticket), on the real `InMemoryStore`
   paths, the reviewed rule losing the second publication in the reviewer's order and the repaired rule delivering
   it in both. Every other run, the crash in the gap, the spellings, the start subscription and the replay by
   incarnation included, is proposed, qualified at K3 and K4. Cases EI-1 and EI-2.

**R6-M1: a successor meet ticket could lose its predecessor's coverage.**

1. Trace: W's one incarnation has 171 entitled events. Ticket T0 with coverage (i, T0) delivers 170 and writes T1.
   The cursor is still within W's subscription, so the coverage stays keyed by T0. T1 matches only through its own
   id, finds no entry, and finishes; event 171 is lost and T0's entry can hold expired events. Reusing T0's inbox key
   for T1 in one consume-and-insert commit meets the built existing-key check (`InMemoryStore.cs` 118–119).
2. Revised: a meet chain has one stable `CatchUpId`, the first ticket's identity in a fixed 64-character form,
   copied to every successor. `CatchUpCover` is keyed by `(Incarnation, CatchUpId)`; each step matches and clears
   through it, and the final step deletes what remains. Each successor gets a new inbox key derived from the
   `CatchUpId` and the step number ("One catch-up id across a meet chain" under [Recover](#recover)).
3. Replay: step 1 delivers 170, consumes T0 and inserts T1 with the same c, and (i, c) stays; step 2 matches W
   through (i, c), delivers event 171 once, writes no successor and deletes (i, c). A takeover between the steps
   changes nothing, since the new owner reads c from T1. Registration-incarnation checks are unchanged: a chain still
   matches only the incarnations it registered.
4. Proposed, qualified at K3, and the real coverage table at K4. Case CC-1.

**R6-M2: the keeping Open left coverage and conflict writes out of its admission budget.**

1. Trace: SB-3's 3,498 keeping subscriptions on distinct maximum-width keys come to 27,036,042 bytes for the
   subscription writes and only the raw key bytes of their coverage entries, above `B_plan` by 1,870,218.
2. Revised: a keeping subscription is charged for its insert, its coverage entry and its MongoDB conflict write,
   9,728 bytes, and the meet ticket once ("The plan's byte bound" under [Recover](#recover)). `W_max` and `B_plan`
   are unchanged; a plan holds at most 2,577 keeping subscriptions, and SB-3's largest fixture is restated to that.
   A registration adds a coverage entry only for an incarnation that holds none, so an unsubscribe deletes at most
   one entry and a Park at most one per keeping subscription. The sizes are measured at K4. Cases SB-1, SB-3 and
   OA-1.

**R6-M3: MongoDB's sweep could not both pass a covered front and bound its conflict writes.**

1. Trace: give the first 340 expired events distinct covered keys. A batch that reads only the first 170 deletes
   nothing for ever; one that walks to the 341st writes 340 conflict documents before its first deletion, above the
   stated 170.
2. Revised: a MongoDB batch reads at most `P_page` candidates past a persistent cursor, passes over held and covered
   ones without writing, writes conflict documents only for the events it deletes, validates them at its snapshot,
   and wraps at the end, so every batch deletes or moves on. The relational statement is unchanged. Qualified at K4
   and measured at K12. Case MW-1.

**R6-M4: a full match page could require more than 2.6 GiB of payload writes within one lease.**

1. Trace: one admitted payload of 16,621,568 bytes and 170 short matching subscriptions put 2,825,666,560 payload
   bytes in one step, over 26 seconds at 100 MiB/s, and every retry repeats it.
2. Revised: each match and meet step writes deliveries up to `B_step` = 4 MiB (a coordinator design choice) and
   always at least one; the cursor moves to the last delivery written and the deadline applies per step. The trace
   now takes 170 steps of one delivery each. SB-7's step counts are restated. Qualified at K3 and K4, step duration
   and fairness measured at K12. Case BA-1.

**The match-step window (owner decision, 18 November (sixth review)).** A non-keeping wait receives an event whose
match step was still outstanding when the wait registered, with no clock bound. Sipke accepted the rule with its
consequence: a match step delayed for any length of time, a publish ticket kept across a whole Park window among
them, delivers its event to a non-keeping wait registered at any later time while the ticket is outstanding and
its paged match has not yet passed that wait's position ("A non-keeping wait" under [Recover](#recover)). The rule is
unchanged.

**R6-m1: some EM-1, EM-2 and EM-4 boundaries could not be reached through their producer.** EM-1's refusal
controls are now per operation and provider, at the first excess the producer can reach: on PostgreSQL an outside
publish cannot reach the B-tree limit, so its control is `N` + 1. EM-2 and EM-4 use the first attainable excess, one
UTF-16 unit on SQL Server. Raw payload controls (raw U+0001, an empty wire payload) use a direct `ITurn.Publish`
producer, and the Native runs are sized after `ToJsonString()` (`NativeModel.cs` 548). EM-4 takes a jointly
admissible origin and key on SQL Server, 192 and 65 units, refuses at 258, and its physical negative control is
sized from the live generated id. Exact provider points are measured at K4.

**Conditions (non-blocking, owner rubric of 18 November).** TR-5 run 2's test model holds its wait records and job
specs as a compact recipe, since 1,324 job entries and the header leave 2,560 bytes of state (K3, K4). MongoDB's
sweep cursor reads through an index on `(tenant, KeepUntil, Match)` (K4). The incarnation is a 16-byte value the
kernel generates on every provider, a random 128-bit value, unique and never reused, so no store read-back is needed
(K4). Each registration's meet chain has its own `CatchUpId` and successor keys, so a re-registration of the same
incarnation after its chain finished reuses no key (K3). MongoDB's sweep cursor wraps only when it is exhausted, so a
covered front whose coverage clears later waits for the next wrap; that cost is measured at K12. The outside publish's
insert-or-read answers a duplicate with its existing receipt first, and the high-water check applies only to a new
insertion (proposed, K4; this replaces an earlier allowance to refuse a duplicate `InboxFull`, seventh review, condition [C2](../program-goals/clean-slate-kernel.md#gate-2-conditions),
under [Seventh review](#seventh-review-counterexamples-and-answers)). `Subscriptions.Incarnation` is 16 bytes, so on
MongoDB, whose `ObjectId` is 12 bytes, the kernel or the driver generates it (K4).

**R6-m3: three fixtures had incompatible oracles.** TR-5 run 2 uses 1,324 jobs, the most a row's registries admit,
and 2,772 timers. SB-7 run 5's turn publish runs 590 steps, its insert-only first step included, and the outside
publish 589. SB-8's fifth run gives E `KeepUntil` 100 and E2 300.

## Seventh review: counterexamples and answers

The [seventh gate 2 review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6987785355)
found one Blocker, one Major and one Minor against `cfce7a2c1`, with five milestone conditions: a retained checkpoint
field that the terminal projection left out, an outside publication that can claim a committed turn's event row,
and a cross-key example that still described version 1. It closed 19 of the 22 earlier findings, and R6-B1, R5-B2
and R4-B2 are partly closed until R7-B1 is answered. Each entry below restates the reviewer's trace, then replays it
step by step under the revised rule. The owner's rubric of 18 November applies: each fix states its rule and
milestone and adds no other mechanism. Everything here is proposed unless it says otherwise. "Executed in this
repair (`experiments/Review7.Blockers`, #4803)" names runs of `prototypes/kernel-model/experiments/Review7.Blockers`,
written for this round, which hold the proposed rules in the harness. The reviewer's own probes are the review's
evidence and are cited as such. No Worker, provider or simulation implements these rules yet.

**R7-B1: the terminal projection left out the retained checkpoint handles.**

1. Trace: a running Native instance A, a child of P, has one registered child G and the sixth round's boundary
   state of 3,500 open invocation inboxes. A issues 1,500 detached child starts in three fenced commits of 500, each within the send
   cap. Existing prototype behaviour: each detached start writes its creator handle and adds no registry entry
   (`Turn.cs` 53–61, `Turns.cs` 364–374), and the commit keeps the handle in `CustomHandles`, which remote
   consumption need not prune (`Turns.cs` 490–498). The sixth round's check counted the state, `TerminalGrowth`,
   the registries and the 16,384-byte header, so the next growth commit landed at exactly 16,457,728 bytes by that
   formula. P's terminate then ran the real `WorkflowInstance` terminate (179–201), which closes the inboxes, keeps
   the 27,001-byte handle field and composes G's terminate. The review measured the terminal row at 16,468,176
   bytes in BSON (the document shape of `MongoStore.cs` 612–618, encoded offline), 10,448 bytes over the envelope;
   NT-2 measures the same row at 10,599 bytes over in JSON UTF-8. Both are qualified at K4. So the ending commit is
   refused and G's terminate is never stored.
2. Revised: the envelope check of every commit of a live row counts the projected terminal row: the state plus
   `TerminalGrowth(state)`, the registries at the marker's sizes, every other variable-length field at its encoded
   size as the end path writes it (`CustomHandles`, the model entries of `CommitTimes`, `Model` and `Definition`, each once), and a fixed
   reserve of 16,384 bytes that now covers only the bounded fields ("The ending commit always fits" under [Every
   Park operation names its Park](#every-park-operation-names-its-park)).
3. Replay: the growth turn's projection adds the 27,001-byte handle field to the sixth round's 16,457,728 bytes, so
   it is 16,484,729 before `Model` and `Definition` are counted (16,484,740 with this harness's 11 bytes of names), over
   the envelope. The turn fails and parks as an over-envelope turn, and A keeps its last
   admitted state. In general the first commit whose projection with the handles exceeds the envelope is the one
   that parks, whichever commit that is. P's terminate then reaches the parked row, and the built terminate of an
   ended row (`WorkflowInstance.cs` 63–64) sends G its terminate.
4. From any admitted live state the terminate writes at most the projection already checked: the state grows within
   `TerminalGrowth`, the registries are replaced by the list within their charge, the handle field is written as it
   is, since the terminate adds no handle, and the fixed fields stay within the reserve. So at the largest state the
   check admits with the handles, the live terminate's ending commit lands, G's terminate is stored in it as the
   first counted child send, A ends Cancelled and P receives its one notice. A protected `ThenDelete` writes its
   live row under the same bound.
5. A larger fixed reserve alone would only move the failure to a larger handle count, so the handle field is counted
   at its size. Each provider's stored encoding of the row, SQL Server's UTF-16 and MongoDB's BSON, is gate 2 condition [C1](../program-goals/clean-slate-kernel.md#gate-2-conditions).
6. Executed in this repair (`experiments/Review7.Blockers`, #4803): NT-2's `nt2-handles` (the reviewer's schedule), `nt2-at-bound` (the largest state each
   check admits with the handles), `nt2-thendelete-protected` and the control `nt2-no-handles` run real `Turn.cs`
   detached starts through real commits, with one registered child and the 3,500-inbox boundary state, then the
   real `WorkflowInstance` terminate, under the reviewed rule (the growth commit lands, the ending row is over the
   cap, no child terminate is stored), the repaired rule (in `nt2-handles` the growth turn parks and the terminate
   stores G's terminate; at the bound the ending commit lands and A ends Cancelled; G ends Cancelled in both) and
   the negative control (handles not counted). It measures the row's fields in JSON UTF-8, and BSON and UTF-16 are
   qualified at K4. Everything else is proposed, qualified at K3, K4 and K6.
   Cases TG-1 run (c) and TG-2's third run. With this answer R6-B1, R5-B2 and R4-B2 rest on the same projection.

**R7-M1: a committed turn publication and a later outside admission could claim one event row.**

1. Trace: W is registered on `acme_topic`. A workflow's turn commits a publish ticket for `from-turn`, and its
   `Publish` returns the event id, today the ticket's issued id (`Turns.cs` 326–330, 429–433; the turn contract,
   `Contracts.cs` 128–134). Before the owner takes the ticket's first step, an outside publisher in the same tenant
   submits `from-outside` on the same key with that id. The outside transaction finds no event row and admits it.
   The turn's insert-only first step then meets an occupied unique key. The reviewer's probe on the real
   `DurableTurn.Publish` and `InMemoryStore` paths (`InMemoryStore.cs` 263–268, 281–285) stored one event and
   delivered only `from-outside`; with a distinct outside id it stored two and delivered both.
2. Revised (owner decision, 18 November (seventh review)): the two producers' event ids are disjoint. A turn's event
   id is `~` followed by the ticket's issued id, stored and returned with the prefix, and the outside publish refuses
   with `InvalidEventId` any caller event id that is provider-equal, under the `Events.Id` column's pinned
   equality, to an id starting with `~`. Nothing is written, no receipt is issued and the refusal is not retryable
   ("Turn and caller event ids are disjoint" under [Recover](#recover)).
3. Replay: A's `Publish` returns `~E`. The outside publish under `~E` is refused, with no row and no receipt, and its
   retry is refused the same way. The turn's first step inserts `(Q(t, key), ~E)` and W receives `from-turn` once.
4. In the other order, a caller that publishes first under the id A's next turn publish will return is refused
   the same way, and W then receives `from-turn` once. An outside publish under any id it is admitted with is one no
   `~` id equals, so the turn's later insert never meets it, and W receives both. An owner change between the
   ticket's commit and its first step changes nothing: the new owner takes the pending ticket and inserts the same
   row. On SQL Server and MySQL the refusal compares under the pinned collation, so a fullwidth `～`
   (U+FF5E) or a leading character the collation ignores is refused too (a condition, qualified at K4). `~` is a
   plain character in the stored rows: the key codec tags a leading `~` only in a JSON property name
   (`DurableKeyJson.cs` 18–25), and an event id is a value.
5. Unchanged: lookups, digests, the `pub:` id and the sweep keep their shape, outside retries and post-sweep reuse
   keep their rules, and no cross-producer duplicate rule exists. The prefix adds one code unit to a generated id,
   two bytes on SQL Server: the id is at most `N` + 130 = 1,154 bytes, the ticket header 2,279 (2,314 after the
   first step's rewrite), the delivery header 3,320 (6,640 in SQL Server's UTF-16), and SQL Server's joint bound
   256 units of raw key and caller key, so EM-2's and EM-4's SQL Server points move to 256 accepted and 257
   refused (EM-4: a caller key of 192 and a raw key of 64). These are derived and measured at K4.
6. Executed in this repair (`experiments/Review7.Blockers`, #4803): MX-1's `mx1-ticket-first`, `mx1-outside-first` and `mx1-owner-change` run the reviewed rule
   (one stored event, only `from-outside` delivered), the repaired rule (the outside publish refused, W receives
   `from-turn`) and both negative controls (the prefix without the refusal, the refusal without the prefix), and
   `mx1-distinct` is the distinct-id control, on the real `DurableTurn.Publish` and `InMemoryStore` publish paths
   with the prefix and refusal held by the harness. The retry of a refused publish is proposed, qualified at K3, and
   the pinned-collation runs at K4. Case MX-1.

**R7-m1: the cross-key example and EM-3 still described version 1.**

1. Trace: one wait W subscribes under K1 and K2, and the same public id E is published once on each. Each new event
   row has its own incarnation, and the version-2 delivery `Id` includes it, so the two routes have distinct wire
   ids as well as distinct `Match` values. The page said the two deliveries share one id, and EM-3 asked for that,
   so an implementation following the oracle could drop the incarnation from the digest. The reviewer's
   independent digest check found the two ids unequal (`logs/model-witness.out`).
2. Revised: "Together they name the whole tuple" under [Resolving a Park](#resolving-a-park-a-bounded-drain) and
   EM-3 now give the wait two ids and two `Match` values. B2-3 stays as a store-level test that builds two complete
   handles sharing a wire id and restores or clears each alone, so the full-key rule keeps its test without an
   unreachable pair of ordinary publications. P-10's slot −3 row is already written directly into the store and is
   unchanged.
3. A rule replay, qualified at K3 and K4. Cases EM-3 and B2-3.

**Conditions** (non-blocking, from the review). The review's five conditions, C1 to C5, and this repair's K4 check of
R7-M1's refusal under the pinned collation on SQL Server and MySQL (MX-1 run (e)) are tracked with their milestones,
status and owed evidence in the plan's [gate 2 conditions](../program-goals/clean-slate-kernel.md#gate-2-conditions)
(C1 to C5 and RP).

## Acceptance cases

Milestones are those of the [program plan](../program-goals/clean-slate-kernel.md#milestones). K3 holds each case on
the in-memory store and in the simulation, with a mutation that fails it; K4 holds the same case on every provider
(PostgreSQL, SQL Server, SQLite and MySQL in both fence modes, and MongoDB on a replica set).

| Case | What it shows | Milestones |
|---|---|---|
| B2-1 | A private X parks the instance, an ordinary X arrives, and a second private twin of X (another scope) is also consumed by the Park. `resume` restores `(X, 0)` and clears both private rows. `retry` naming one private handle restores that row and `(X, 0)`, and clears the unselected twin. Each case asserts the full key of every row and that each restored row is delivered exactly once and each cleared row never | K3, K4 |
| B2-2 | The other order: an ordinary X is pending at the Park and consumed by it, a private X arrives later. Each row gets its own reason, and each mode restores exactly the rows the table says | K3, K4 |
| B2-3 | Two event matches with one wire id, built as two complete handles at the store level, since ordinary version-2 publications under two keys have distinct wire ids (seventh review R7-m1): distinct handles, each restored or cleared alone | K3, K4 |
| B2-4 | A re-park (`MaxAttempts`, and a definite commit error) consumes Y. Y is `Consumed`; `resume` clears it; `retry` can select it | K3, K4 |
| B2-5 | A `retry` naming an `Arrived` row with no disposition, an unknown handle or a handle of another instance is refused with no change. The `Rearmed` exception (fourth review R4-m3): a handle of an `Arrived` timer this Park retains as `Rearmed` is the accepted no-op of "Checks", counted "already re-armed", and the rest of the selection proceeds (S-7) | K3, K11 |
| B2-6 | Mutation: classify by id alone. B2-1 and B2-2 fail | K3 |
| M1-1 | With a finite window passed and the expiry not yet run, the sweep runs before recover's read. No kept row is removed; the resulting wait is armed once; the job's durable ticket exists once | K3, K4 |
| M1-2 | The same sweep injected between the read and the commit, and after the commit. The open lands; the wait and the ticket are as in M1-1; restored rows survive the later sweep | K3, K4 |
| M1-3 | The recover's acknowledgement is lost after its commit. The operator's retry with the same operation id is a duplicate; the read model shows one outcome; no second drain runs | K3, K11 |
| M1-4 | The expiry fires before a recover arrives: recover is refused in both modes; the expiry drain clears every kept row, records "expired", sends the parent one final Faulted notice, and the `Faulted` retention window starts from its terminal mark. A recover arrives first: the expiry is recalled | K3 |
| M1-5 | Mutation: drop `KeptBy IS NULL` from the sweep's predicate. M1-1 or M1-2 fails | K3 |
| M3-1 | A Park with the null window receives 100,000 arrivals, then `resume`. Every page stays within `P_page` rows and `B` bytes, the drain completes, and every arrival is delivered once, in per-sender order | K3, K12 |
| M3-2 | Ownership changes at a page boundary, and a page commit has an unknown outcome. The drain continues from the committed cursor, with no row restored twice or skipped | K3, K4 |
| M3-3 | Recovery while another partition of the same node is busy, and while the same partition is busy. Other instances keep their turn share; their latency is recorded | K12 |
| M3-4 | During a drain, a sender with kept rows sends again. The new row is delivered after all of that sender's kept rows | K3 |
| M3-5 | A recover drain under a virtual partition above its high water mark: a page waits only while its previous page's last restored row is not yet handed off, or while the instance is suspended, and restores at most `P_page` less the restored rows not yet handed off (fifth review R5-M3, HC-1), and never on the partition's backlog; unrelated ingress is refused only by the threshold's own rule | K3, K12 |
| M3-6 | A re-park during a recover drain: the Resolution is cleared; restored-but-undelivered rows and drain-time arrivals are kept as `Arrived`, the parking turn's rows as `Consumed`, unreached rows keep their reasons; `kernel.park-expiry` is re-armed for the window's remaining time; a second recover drains every row still kept and delivers each once; the re-park keeps the Park mark and the retained dispositions and reverts `Selected` rows to `Consumed`. A terminate during a recover drain, and a terminate of a parked child: no model turn runs after the terminate is accepted; its open removes the waits, cancels the jobs and terminates the children; the kept rows are cleared, the status is Cancelled at Complete, and the parent receives exactly one final notice. A delete during a recover drain whose model runs is refused with no change; a delete whose checks pass against the row's current terminal mark during a discard, expiry or terminate drain takes the drain over (DT-1), and a terminate that interrupted a running recover leaves no mark to name until its Complete (DT-2); at its Complete a taken-over terminate is first ended Cancelled with its terminal mark, then deleted, with one parent notice; while a terminate drain runs, lists show the row's current status (Faulted for a parked child, Running after a recover's open) with the sub-state "terminating"; discard and delete drains of a parked child send the parent one final Faulted notice | K3, K6 |
| M3-7 | One page on MongoDB within its document, message and transaction-lifetime limits | K4 |
| M3-8 | MongoDB: kept rows with one `seq` straddle a page boundary. The tuple cursor restores or clears every one, and none stays kept; mutation: a cursor on `seq` alone leaves rows kept. The prototype's MongoDB store sorts by `(seq, _id)` (`MongoStore.cs`), so the tuple cursor is a provider contract implemented and qualified here | K4 |
| M3-9 | A drain page crashes the process, before and after its commit. The next owner re-runs the page from the committed cursor; each row is restored or cleared once; the drain message is never put on trial or parked | K3, K4 |
| M3-10 | The model finishes, is cancelled or ends faulted by its outcome during a recover drain: the drain runs to Complete; pages after the end clear and restore none of the model's rows; a kept publish ticket they reach is restored with its committed cursor and its remaining steps run, and a kept meet ticket is cleared ("A kept publish ticket is kernel work", sixth review R6-M7); withheld rows are consumed and dropped; every `KeptBy` is cleared and the Park resolved; the parent receives exactly one notice, from the end's own turn. A delete naming the end's mark after the end takes the drain over, deletes at Complete and sends no notice, and no row keeps `KeptBy`; one naming the Park's mark is refused as naming an earlier transition. The end's turn records the model's end in the Resolution; a late delivery reaching `MaxAttempts` after it records a failed input, opens no Park and leaves the drain running, and the same delivery before the end, with no end recorded, interrupts the drain as a re-park. During a discard, expiry, terminate or delete drain, a late delivery reaching `MaxAttempts` is likewise a failed late input: the instance keeps its status, the drain continues, and no Park or keeping resumes. On an ended row with no unresolved Park (after a completed discard or expiry, after a recovered instance ends, or on one that never parked) it is a failed late input that opens no Park and keeps no row. A turn that throws before the end is a re-park (M3-6) Each of the five `MaxAttempts` states is asserted on its own. The mark: when the model ends mid-drain, its end writes a terminal mark that stays through Complete, and a qualified delete naming that mark after Complete is accepted; a recover that completes with the model running leaves no mark until the next end. | K3, K6 |
| M3-11 | The deadlock trace: during a recover drain, unreleased arrivals withheld until Complete push the virtual partition above its high water mark. The drain still completes, each page waiting only for its previous page's restored rows; the withheld rows are then released and the backlog falls. Mutation: gate on the partition's backlog, and the drain never completes | K3, K12 |
| M3-12 | `KeptAsync` returns every kept row with its handle and reason; a `retry` built from its handles restores exactly those rows; a handle a drain has since cleared is refused, except a timer's handle that its Park retains as `Suppressed` (re-armed) or `Rearmed` (accepted as a no-op) | K9, K11 |
| M3-13 | The gate's bounds. A suspended recovering instance restores no page until resumed, then the drain completes. A model that holds every restored row (a StateMachine during a suspended action) lets the drain complete, with at most `P_page` rows not handed off (fifth review R5-M3) plus the held rows, as `elsa.kernel.drain.pending` and `elsa.kernel.drain.held` report; K12 measures the whole owner's memory with the intended holding models, since the page bound alone does not bound it. A partition taken over repeatedly during a drain adds no page per take: each new owner reads the gate row by one point lookup, counts every restored, not-`Done` row as not handed off until it hands it off itself, and gives a lost owner's `Seen` mark no weight; a gate row consumed and swept counts as handed off (HC-2) | K3, K7, K12 |
| R-1 | `RestoreWaits` for Native, Flowchart and StateMachine: subscriptions, timers and jobs restored per the table above, with kept events in the gap, retained dispositions, and the interrupted-resume trace of M3-14; each plan's `TimerSpec` equals the timer row the model sent (TR-2) | K6, K7 |
| M3-14 | The interrupted-resume trace: timer T consumed by the Park is cleared unarmed in an early page; arrival X re-parks in a later page; the sweep runs before the second Open and, in a second run, after it; a takeover is injected between every pair of steps. A second `resume` leaves T unarmed in every run; a `retry` naming T re-arms it once, with the sweep before and after that retry, and a second retry after an interrupted one is accepted and leaves exactly one armed T and one firing; in both sweep orders T fires at the same point, after Complete, and the revived row's `KeepUntil` equals an inserted row's; a job restarted by the first resume is not restarted again. A further schedule: a drain releases T, the consuming turn throws, and the re-park keeps T `Consumed` with that marker; a second resume suppresses T and clears it; a retry revives T before the sweep, and in a second run inserts it after the sweep. In both runs T has no `ReleasedBy`, no page restores it, and it fires once after Complete. A message Y pending at that Open, added after an earlier R, is delivered in the same order against T in both sweep orders. Mutation: the revive keeps the old marker; the `Stale` arm restores T and it fires during the drain in the revive run only. Mutation: revive by update, keeping the old `seq`; T runs before Y in the revive run and after it in the insert run | K3, K4, K6, K7 |
| P-1 | The stale-Park trace with the clock pinned: A and B share tenant t and caller key K, and both park at first revision 7 at the same `ParkAt`, so their Park marks are equal and only the creation differs; the test asserts this precondition before injecting anything. A discard is prepared from A's `ParkSelection`. Handled before retirement: its turn runs while A is still parked, before A is resolved and deleted, and discards A only. Submitted after B exists: refused for its creation (reason run); B's kept rows and their reasons, status, Park mark, waits and parent notices are unchanged, and the outcome records the refusal under its operation id | K3, K4 |
| P-1b | Enqueued before the reuse, delivered after it, a separate case from P-1's handled-before-retirement one. P-1's pinned preconditions. The operator API accepts the discard while A is parked; the fixture holds it at the transport layer, between the API's acceptance and the store's add, while another operator resolves and deletes A and B parks under K; then the add lands addressed to K with A's `ParkSelection`, so the store pins `TargetCreation` to B's creation, and it is delivered to B. It passes `TargetError` at B (the test asserts this) and is refused by the `ParkSelection` creation check, B unchanged as in P-1, the refusal recorded under its operation id. Its mutation is P-3 | K3, K4 |
| P-1c | Landed while A exists, delivered after A's retirement. P-1's pinned preconditions. A discard prepared from A's `ParkSelection` is added while A is parked, so the store pins its target and `TargetCreation` to A; the fixture holds its delivery while another operator resolves and deletes A. Run 1: it is delivered before the reuse and refused by the built protocol as `RetiredCreation`, before any Park check. Run 2: it is delivered after B parks under K, which keeps A's instance id under a higher creation, and is refused as `WrongCreation`; B is unchanged as in P-1 in both runs. The case runs under same-id addressing, as P-1, P-1b and P-3 do; today's fresh run ids per creation are prototype behaviour that release 1's key admission replaces. Mutation: pin at delivery time; the command is retargeted to B and reaches the Park checks, so the asserted built refusal changes to the Park-check refusal, and with P-3's mutant as well it discards B | K3, K4 |
| P-2 | The same trace for `resume` with an empty selection, retry-all and terminate of a parked instance; and a stale command after A recovers and parks again (same creation, new Park mark) is refused | K3, K4 |
| P-3 | Mutation: the owner skips the creation check and keeps every other check. With P-1's pinned clock B's tenant and Park mark equal the selection's, and the late command's `TargetCreation`, pinned when the store accepted its add after the reuse, is B's creation, so the built `TargetError` passes at B and the `ParkSelection` creation check is the only one that refuses; the test asserts both preconditions. P-1's late command and P-1b's delayed one, each addressed to B at the transport layer with A's `ParkSelection`, discard B in the mutant: B's kept rows are cleared and B's parent receives a Faulted notice, so the invariant "a Park operation changes only the Park it names" fails; the conforming owner refuses both and B is untouched. Negative control: a second mutant that drops only the Park-mark check leaves P-1 and P-1b passing, which shows the case isolates the creation check | K3 |
| P-4 | Delayed legitimate controls, each injected in its own run; the checks are layered, so each run lists every check that refuses it alone, and its mutant drops all of them. (1) Stale drain: a `kernel.drain` of an interrupted resolution, held in transit, is delivered after a second Open; refused alone by the generation check and by the outstanding key (`Resolution.Drain`). (2a) Recalled expiry during the drain: an expiry delivered or in flight when a recover's Open recalled it is delivered while that drain runs; refused alone by the generation check, by "no resolution is open" and by the outstanding key (`Expiry.Armed` is cleared). (2b) The same expiry delivered after an interrupting re-park has armed a new one, with a finite window and delivery at or after `At`; refused alone by the generation check and by the outstanding key, and only the new expiry acts. (3) Later Park: an expiry of a resolved Park, held in transit, is delivered after the same creation parks again under a new mark, with a finite window pinned and delivery at or after the new Park's `At`, so the `Expiry.At` check passes; refused alone by the Park-mark check, by the generation check (the generation is monotonic for the creation) and by the outstanding key. (4) Successor creation, under P-1's clock pin so that A's and B's Park marks are equal. (4a) A's expiry, armed at A's Park under generation 0 and held in transit, is delivered to B, which is parked with a finite window, no resolution open, generation 0 and delivery at or after B's `At`. (4b) A's drain, minted by a recover's Open at A's generation 1 and held in transit while a terminate interrupts that recover and A is resolved and deleted, is delivered to B while B's own first recover is open at generation 1. With the mark, generation, open-resolution and `At` checks pinned to pass, each is refused alone by the built `TargetError` (its `TargetCreation` is A's creation, so `WrongCreation` at B), by the payload creation check, by the issuer check on `SenderCreation` and by the outstanding key. Each is consumed and dropped and no row changes. Mutations, one per run, each dropping every check listed for it: the stale drain runs a page outside its resolution (1); the recalled expiry opens a discard against the recovery (2a); after the re-park no recovery is active, so the old expiry opens the expiry drain in place of the new one, an outcome otherwise the same as the genuine expiry's, which only the assertion that the acting key equals `Expiry.Armed` and the old key is refused catches (2b); the old expiry discards the new Park (3); A's expiry discards B and A's drain runs a page of B's recover (4a, 4b). A mutant that keeps any one of a run's checks leaves that run passing, the intended redundancy, which the case records | K3, K4 |
| P-5 | `ParkedItem` and `KeptAsync` expose creation and Park mark; a command built from them lands; one built before a recovery and a second Park is refused; refusals count by reason | K9, K11 |
| P-6 | The interrupted recover and the Park mark: an operator delete naming the Park mark, and separately a discard naming the `ParkSelection`, are prepared while A is parked, before a recover opens. Delivered while the recover drain runs, each is refused (the mark is cleared and a resolution is open). Delivered after an interrupting re-park, the mark is back, so the delete opens the delete drain and the discard opens the discard drain. After a recover that completes, both are refused as naming an earlier transition | K3, K11 |
| P-7 | A parent's tree terminate (composed by the actor type) and a combiner's FailFast (an application send through `ITurn.Send`) reach a parked child: each is accepted without a `ParkSelection`, opens the terminate drain, and the child resolves as Cancelled with every `KeptBy` cleared; the parent receives the one final notice and no other. An application send of `kernel.terminate` from a non-parent, an outside add naming the parent as sender, a terminate from an earlier creation of the parent, and an actor-type `Add` of a `kernel.terminate` whose `Sender` is another actor (refused at commit as a definite error) are refused. `ThenDelete` from an operator only: a combiner's FailFast that carries `ThenDelete` throws at `ITurn.Send` and its turn fails as a model fault, and a test actor type's composed tree terminate that carries it is refused at the commit as a definite error; the child stays parked and unchanged in both. Open drains (the joint review's J-1 and its follow-up C-1): P waits on child A, parked at r1 with more than `2P` kept rows; A has a live child G (itself with a child G2) and an in-flight job J, which the Park left alone; in its own run each of these drains is open on A: an operator's discard, the window's expiry, a terminate (once an operator's selected one, once P's tree terminate), the delete of the parked instance, and a discard taken over by an operator delete naming r1. Interleaving: between two pages, and once after an ownership takeover, P's tree terminate, a combiner's FailFast, an operator's selected terminate, an unselected one, a selected one with `ThenDelete`, and an application send of `kernel.terminate` from a non-parent arrive, each in its own run. Outcome: each is refused (reason resolution open) after the tenant and creation checks, and leaves the drain alone: no drain opens, the open Resolution keeps its kind, operation, cursor and generation, and no `ThenDelete` is recorded; the operator's refusals are recorded under their operation ids, and the parent's and the combiner's count in `elsa.kernel.park.refused` only; the open drain completes with its own outcome (discard: Faulted, "discarded"; expiry: Faulted, "expired"; terminate: Cancelled; the delete and the taken-over discard: deleted, after the discard's Faulted end), no row keeps `KeptBy`, and P receives exactly the one notice that drain owes (Faulted, Faulted, Cancelled, Faulted, Faulted) and no other. The subtree: for every terminate with authority over A (P's, the combiner's, the operator's selected ones), the refusing commit sends G a terminate from A and cancels J, and clears both from A's row; G is accepted under its parent binding to A and ends Cancelled, its own cascade ends G2 Cancelled, J's ticket is cancelled and its late result dropped, and in the runs where P's tree is terminated invariant 17 holds: no tree instance runs, none still lists children, no wait of a terminated instance is left; in the delete runs the cascade is sent before A's Complete removes A, and G and G2 still end Cancelled; in the terminate runs the drain's Open already ended G and cancelled J, and the cascade sends nothing. The unselected operator terminate and the non-parent's send are refused with no cascade, and G and J stay as the open drain leaves them. Mutation: the refusal drops the cascade; G keeps running after its root P was terminated, and invariant 17 fails. Mutation: cascade for every refused terminate; the non-parent's send terminates G. Mutation: test the open drain after the parent binding; P's tree terminate replaces the discard's Resolution, A ends Cancelled where the discard said Faulted, the discard's operation gets no outcome, and in the delete runs the delete is dropped and A stays. Mutation: add a refused terminate's `ThenDelete` to an open terminate drain; that run's Complete deletes A | K3, K6 |
| P-8 | A terminate selected against a Park and delivered after that Park's recovery. A is parked at mark (7, t0); an operator's terminate carries A's `ParkSelection`; the fixture holds it at the transport layer while a `resume` runs to Complete with the model still running, so A shows Running with no Park mark and no Park identity. Delivered then, the terminate is refused (reason Park): A keeps running, its waits, jobs and children are untouched, its parent receives no notice, and the operation's outcome records the refusal. Delivered instead while the recover drain is open, it matches `ParkRecord`'s Park identity and is accepted: it interrupts the drain and opens the terminate drain, and a terminate selected against another Park of A is refused there; delivered after an interrupting re-park, it is accepted and opens the terminate drain; delivered after A later parks again under a new mark, it is refused. Model ended during the drain: A's model finishes while the recover drain is still open, then the held selected terminate is delivered and refused (reason resolution open); in a fixture where A has a child and a job, the refusing commit sends the child a terminate from A and cancels the job, the child ends Cancelled, A keeps its end status and mark and P receives nothing, and a terminate selected against another Park of A is refused with no cascade; the drain runs to Complete, the parent has received exactly the one notice from the end's turn, and no Park reopens; an unselected terminate delivered at the same point, with the fixture pinned to an ended row that has no children and no jobs, is consumed with no change, and in a second fixture with a child and a job it raises the revision, cancels the job and sends the child a terminate, keeping the end status and sending the parent nothing; a terminate with `ThenDelete` at the same point takes the qualified-delete path of an ended run and takes the drain over, as DT-3 requires. Negative control: an operator terminate with no `ParkSelection`, delivered after the recovery, is the ordinary terminate of a running instance and is accepted. Mutation: check the selection only while the row is parked, and the held terminate cancels the recovered run; a second mutant that compares the selection with the row's mark alone refuses the terminate during the open recover drain; a third mutant that accepts the selected terminate after the model's end reopens a terminate drain on an ended run and sends the parent a second notice; a fourth mutant that drops the cascade from that refusal leaves the child running | K3, K11 |
| P-9 | No producer outside layer 2 creates a private control. For each of `kernel.drain`, `kernel.park-expiry` and an unknown `kernel.x`: an outside add on each store, `Worker.Post`, `PostRequest` and an operator command fail with a definite error and write no row; a keyed request through `IKeyAdmission.AcceptAsync` is refused by `KeyRequest.Validate` before any row is written; a model's `ITurn.Send` and an actor type's `SendFromReceivedAddress`, `Add` and `AddTerminalNotice` throw at the call, the sending turn fails by the ordinary fault rule, and the addressed parked instance's rows, `ParkRecord` and status are unchanged. A test actor type that composes `TurnOutcome.Sent` directly, the existing path the tree terminate uses, with a private-kind message, and separately with an ordinary kind in the control scope, is refused at the commit as a definite error, and nothing of its commit lands. An outside add with an ordinary kind and a supplied control scope lands in scope 0. Mutations: today's list check, which refuses only job and event tickets, lets the outside add of `kernel.park-expiry` write a row; key admission without the reserved-kind rule admits a keyed `kernel.park-expiry` in scope 0 | K3, K4, K6 |
| P-10 | The consumer refuses a control the kernel did not arm. A fixture writes each row directly into the store, below every producer, with payload fields (creation, Park mark, current generation) that match. Under the null window: a `kernel.park-expiry` in the control scope with the instance as sender is dropped, and with the clock advanced by ten years no expiry ever discards; kept rows, reasons and status stay. With a finite window: an expiry with another serial, due before `Expiry.At`, is dropped; the armed expiry delivered with the turn's database time before `At` (an injected clock fault) is dropped and re-armed at `At`, and exactly one expiry drain opens, at or after `At`; an expiry while a recover's Resolution is open is dropped. A drain with matching fields and another serial is dropped and runs no page. A private kind in scope 0, or with another sender, is dropped. On the EF providers (K4): an event delivery in slot −3 (the first further match of one wire id, its second match overall) beside a private control of the same instance is delivered as an event under its `Match`, and the control acts as a control; neither is classified as the other, and a control row with a non-null `Match` is dropped. Each refusal counts with reason control. Mutation: the second review's consumer, which checks only creation, Park mark and generation, opens a discard under the null window and an extra page for the injected drain | K3, K4 |
| P-11 | Successive controls within one generation have distinct identities. A resume drain of at least three pages, whose progress gate waits twice with delayed retries, with a takeover between two pages and a page commit of unknown outcome retaken: every `kernel.drain` row has a distinct full key, the store accepts every insert with no duplicate answer, at most one drain of the instance is pending at any time, the retaken page mints the key the committed serial gives, and an earlier consumed drain re-injected with its original key is dropped. Across Parks: Park 1 of creation c resolves, and c parks again (Park 2). Park 2's first Open takes a generation above every value Park 1 used, and a stale drain or expiry of Park 1 matches nothing in Park 2. A row Park 1's drain released with `ReleasedBy` = (c, g1) and no turn consumed is withheld while Park 2's resolution is open, since its tag never equals Park 2's (c, g2); with a `resume` as Park 2's resolution, a page restores it once through the `Stale` arm as an `Arrived` row with the new tag, in tuple order; with a discard, it is skipped by the pages and consumed and dropped at Complete. Mutations: derive the id from the generation alone, and either the next page's insert is merged with an earlier row and the drain stalls, or the re-injected drain is taken as outstanding and a second drain chain outruns the gate; reset `ResolutionGeneration` when a Park resolves, and Park 2's first Open reuses Park 1's first generation, so the Park 1 tag equals Park 2's open generation and the row is delivered at once, ahead of Park 2's kept rows, while the `Stale` arm, which reads only lower generations, never selects it | K3, K4 |
| P-12 | The control scope survives the text-key escape. Preconditions, on SQLite: an instance whose actor id holds a NUL, admitted by an ordinary start (SQLite admits a NUL; admission refuses an unpaired unit on every provider and a NUL on PostgreSQL), parked with a recover drain open, so the outstanding `kernel.drain` id holds the NUL; beside it, an escaped child creator under the same actor. A below-admission defence fixture, labelled as one, repeats the run on PostgreSQL with a NUL and on PostgreSQL and SQLite with an unpaired unit, writing the instance row directly into the store below admission, since no admitted actor id reaches those combinations (the joint review's J-4). Interleaving: the drain is written, polled and taken, and the page commit is retaken after an unknown outcome. Outcome: the control is stored at the escaped control scope 2^62 + 1 and read back at 2^62 with its exact id, matches `Resolution.Drain` and runs its page; the child creator is read back at `long.MaxValue` as an escaped child and is never classified as a control; no insert throws. Mutations: a control scope of `long.MaxValue − 1`, which with a NUL or unpaired-unit id makes `ToTextKey` throw `OverflowException` at the insert (`checked(2^33 + scope)`, `IssuedMailKey.cs` 15–16), so the commit minting the drain fails; with a further plain-id fixture for this mutant, a control id with no NUL and no unpaired unit is stored unescaped at `long.MaxValue − 1` and read back as an escaped child, so the read base64-decodes its id: an id that is not valid base64 (any id with a `#` separator) fails the read with `FormatException`, valid base64 that decodes to an odd number of bytes fails it with `JsonException` (`DurableKeyJson.cs` 39), and any other decodes to another id under scope `long.MaxValue`, no longer matches `Resolution.Drain`, and the drain stalls; the fixture asserts either failure; and the existing escape without the control mapping, which stores 2^33 + 2^62, is not decoded on read and stalls the same way | K4 |
| S-1 | Kept rows whose payloads are `B` − 1, `B` and `B` + 1 bytes, and an oversized first row of 5 MiB: every page progresses on metadata alone and each restored payload is read only on delivery. Run under resume, discard and delete, with a takeover between pages | K3, K4, K12 |
| S-2 | A kept row whose handle is at the admission limits (every key field at its limit), as the first row of a page: the page takes it and the drain completes | K3, K4 |
| S-3 | A `retry` selection of `P` handles lands; one of `P` + 1 handles, or above 256 KiB, is refused with no change; retry-all lands for any number of `Consumed` rows; an interruption reverts every `Selected` row; a handle that is neither a kept `Consumed` row nor a timer this Park retains as `Suppressed` or `Rearmed` is refused before Open; a selection that names a `Suppressed` timer re-arms it once and leaves the marking's count unchanged, with no reset, both with the sweep run before the retry and after it, each giving one armed timer and one firing; a second retry after an interruption, naming the now `Rearmed` timer, is accepted, re-arms nothing and is counted "already re-armed" (S-7); a timer that is `Suppressed` and still kept `Consumed` (a re-park came before the page that would clear it), named by a retry, is re-armed once by the re-arm's delete and insert, which also removes its `KeptBy`, fires once, is never marked `Selected`, and is skipped by the later pages; a suppressed timer that an earlier drain had released, revived by a retry, has no `ReleasedBy` and fires after Complete, as an inserted one does (mutation: the revive keeps the old marker, and the timer fires during the drain) | K3, K4, K11 |
| S-4 | MongoDB: a page next to a kept row of 5 MiB stays within the document, message and transaction-lifetime limits | K4 |
| R-2 | Inspect, retry, resume and discard through the operator API on the parked-work read model, by handle | K9, K11 |
| DT-1 | **A delete takes over the terminate drain of a parked child.** Preconditions: parent P waits on child A, parked at mark r1 with more than `2P` kept rows; a terminate T of A opens the terminate drain, once as P's tree terminate and once as an operator's naming A's `ParkSelection`. Interleaving: an operator delete D naming A's tenant, creation and r1 arrives after T's Open, once before the first page and once between pages, with an ownership takeover between D's commit and the next page; a Retention delete naming r1 arrives once before D and once after D's takeover; a second operator delete arrives after D's takeover; during the drain, a delete from another tenant with A's id and one naming another creation of A's key arrive, before and after D. Outcome: D takes the drain over, which continues from its cursor with no row repeated or skipped; Complete ends A Cancelled under a new mark r2, sends P exactly one Cancelled notice and deletes A in the same commit, whose only journal row is the deletion marker; no row keeps `KeptBy`; T's outcome is complete (Cancelled, then deleted by D) and D's is deleted; the Retention delete before D is refused as not eligible and after D as in progress, the second operator delete as in progress, the other tenant's as tenant and the other creation's as run, never as in progress, each with no change. Protection: with `ProtectsDeletion` true when D arrives, D is refused as protected and T completes alone; with a test model whose protection starts between D's takeover and Complete, A ends Cancelled under r2 and is kept, and D's outcome is protected. Mutations: the takeover's Complete skips `ProtectsDeletion` (the protected run is deleted); the taken-over Complete also sends the Faulted notice of a parked delete (P receives two) | K3, K4, K11 |
| DT-2 | **No separate delete qualifies while a terminate that interrupted a running recover drains.** Preconditions: P waits on A, parked at r1 with more than `2P` kept rows; an operator delete D1 naming r1 is prepared while A is parked; a `resume` recover R opens (the row's mark cleared, A Running), its first page restores rows, and the model still runs; a terminate T is accepted and opens its drain, interrupting R. Interleaving: D1 arrives after T's Open before its first page, between its pages, and after an ownership takeover between pages; a delete naming creation 0 arrives in the same window. Outcome: D1 is refused as "transition" and the creation-0 delete as ambiguous, each with no change and counted by reason; T's drain runs on and its Complete writes the Cancelled mark r2, leaves no row with `KeptBy` and sends P exactly one Cancelled notice; R's outcome is interrupted and T's is complete. After Complete: a new delete naming r1 is refused as "transition"; an operator delete D2 naming r2 deletes A, and P receives no further notice; in a second run with `ProtectsDeletion` true, D2 is refused as protected and A stays under r2; a Retention delete under r2 is refused before the `Cancelled` window has elapsed and deletes A after it. Mutation `DeleteAcceptsParkRecordMark`: when the row has no mark, check 3 compares the selection with `ParkRecord`'s Park identity; D1 then takes the drain over and deletes A, and the case fails | K3, K4, K11 |
| DT-3 | **`ThenDelete` on both origins.** Preconditions: A parked at r1 as in DT-1, and separately a running recover of A as in DT-2. Interleaving: an operator's terminate with `ThenDelete`, accepted under the terminate's checks, opens the terminate drain; in the parked origin a separate operator delete naming r1 arrives mid-drain; an ownership takeover comes between pages; each runs once with `ProtectsDeletion` false and once true. Outcome: `ThenDelete` is recorded at Open; the separate delete is refused as in progress with no change; Complete ends A Cancelled under r2, sends P exactly one Cancelled notice and, unprotected, deletes A in the same commit, whose only journal row is the deletion marker; protected, A is kept under r2 and the answer reaches the API request's reply address; no row keeps `KeptBy` in either case. A `ThenDelete` reaching a recover drain whose model has ended takes the drain over through checks 1, 2 and 5, and P receives only the end's notice. Ended-run order (the joint review's J-2): A's model ends during a recover drain, writing mark r2 and sending P its notice; an operator delete D naming r2 takes the drain over first; then an operator's terminate with `ThenDelete` arrives, once before the next page and once after an ownership takeover, and once with `ProtectsDeletion` true. It passes checks 1 and 2 and is refused as "in progress" with no change, its outcome naming D; the takeover keeps D's operation and selection; Complete deletes A (protected: keeps A under r2, with D's outcome protected), D's outcome is deleted, and P receives only the end's notice. The reverse order, `ThenDelete` first and D second, refuses D as "in progress" and keeps the terminate's outcome. In these runs A's ended row lists no child and no job, so the "in progress" refusal changes nothing. Subtree runs (fifth review R5-B2): the same two orders, with A's ended row still listing a live child G (itself with a child G2) and an in-flight job J. Outcome: with D first, the refused `ThenDelete`, a terminate with authority, cascades in its refusing commit from A's live row, as J-1's refusal does; with the `ThenDelete` first, its takeover commit starts the cascade on A's live row; in both, Complete carries any `Discharging` remainder to A's marker, G and G2 end Cancelled, J's ticket is cancelled by the idempotent cancel, the obligation clears, and P receives only the end's notice. Mutation: the "in progress" refusal changes nothing even with a registry; with D first, G and G2 keep running after A is deleted. Mutation: drop "in progress" from the ended-run path; the `ThenDelete` takes the drain over again, D's operation and outcome are overwritten, and the deletion marker records no outcome for D. Mutation: test it after check 5; in the protected run the `ThenDelete` answers "protected" in place of "in progress". Mutation `ThenDeleteAtOpen`: the terminate deletes the row in its Open, as RD8's proposed one-turn path does for a running instance; the next `kernel.drain` finds no target, kept rows keep `KeptBy`, and the case fails | K3, K4, K11 |
| DT-4 | **Protection begins before a parked instance's delete completes.** Preconditions: P waits on A, parked at r1 with more than `2P` kept rows, under a test model whose `ProtectsDeletion` is false when the delete D (Operator, r1) arrives and turns true before the drain's Complete. Interleaving: D opens the delete drain; an ownership takeover comes between pages; protection turns true after the last page and before Complete. Outcome: no row keeps `KeptBy`; Complete leaves A Faulted under a new mark r2, sends P exactly one Faulted notice, and journals D refused as protected with the Park discarded; D's outcome is protected; A's `Faulted` retention window runs from r2, and a Retention delete under r2 after it is refused as protected while protection holds. Mutations: the protected Complete deletes A anyway; the protected Complete leaves A with no mark, so no later delete can name it | K3, K4, K11 |
| DT-5 | **The refusal order when a `ThenDelete` terminate interrupted a running recover.** Preconditions: P waits on A, parked at r1 with more than `2P` kept rows; a `resume` recover R opens, so the row has no mark, and the model still runs; an operator's terminate with `ThenDelete` is accepted and opens its drain, interrupting R. Interleaving: between pages, an Operator delete naming r1, a Retention delete naming r1, a delete from another tenant with A's id and one naming another creation arrive, with an ownership takeover between pages. Outcome: the r1 deletes are refused as "transition" (check 3 fails before "in progress" or check 4 is reached), the other tenant's as tenant and the other creation's as run, each with no change; Complete ends A Cancelled under r2, sends P exactly one Cancelled notice and deletes A, with no row keeping `KeptBy`; the terminate's outcome is deleted. Mutation: "in progress" tested before check 3; the r1 deletes then answer "in progress", and the case fails | K3, K4, K11 |
| DT-6 | **Protection against a separate delete during a drain.** Preconditions: A parked at r1 with more than `2P` kept rows, under a test model whose `ProtectsDeletion` is true throughout. Interleaving: in one run an operator's terminate without `ThenDelete` opens the terminate drain; in a second, a terminate with `ThenDelete`; in each, an Operator delete naming r1 arrives between pages. Outcome: in the first run the delete is refused at check 5 as "protected", and Complete ends A Cancelled under r2 and keeps it; in the second it is refused as "in progress", and Complete ends A Cancelled under r2 and keeps it, with the terminate's answer protected on the API request's reply address; in both, P receives exactly one Cancelled notice and no row keeps `KeptBy`. Mutations: check 5 tested before "in progress" (the second run answers "protected"); a separate delete during a protected terminate drain takes it over and deletes A | K3, K4, K11 |
| E-1 | The R3-B2 trace on the in-memory store, with A's `seq` injected below the cursor (the K3 allocation hook, plan): two pages of kept rows; after page 1's commit, an arrival A from another sender is committed with a `seq` below the cursor; A's delivery ends the model. A is delivered only after Complete; every kept row of page 2 is restored and delivered exactly once, before A; no `KeptBy` remains. Run with A committed just before page 1's commit and just after it. Mutation: eligibility by the tuple against the cursor; A runs first, the model ends, page 2 clears the suffix, and its rows have zero deliveries | K3 |
| E-2 | As E-1, with A injected at the cursor's `seq` and a lower `(Id, Scope, Match)` tie-break, at a page boundary. The same outcome; the same mutation fails | K3, K4 |
| E-3 | As E-1 (injected `seq` at K3), with an ownership change between page 1 and page 2, and separately between A's commit and its first poll. The new owner withholds A and delivers the suffix; each kept row is delivered once and A after Complete. Mutation: the new owner recomputes eligibility from the cursor | K3, K4 |
| E-4 | MongoDB: A is an outside add whose `clocks` value is behind the partition clock, and in a second run a send from another partition whose clock is behind. Each A gets a `seq` below the cursor, is withheld until Complete, and the suffix is delivered first. Mutation: eligibility by cursor comparison | K4 |
| E-5 | Relational providers that generate `seq` (PostgreSQL, SQL Server, MySQL): while the instance is parked, a transaction draws A's `seq`, then the kept rows that will end page 1 are allocated and committed, and A's transaction commits only after page 1's commit, so A becomes visible with a `seq` below the cursor. A is withheld until Complete, and the suffix is delivered first. SQLite, which assigns `seq` explicitly under one writer, runs as a control. Mutation: eligibility by cursor comparison; A is delivered after page 1 and, as a model-ending input, the suffix is cleared undelivered | K4 |
| E-6 | Interrupted release and generations. (a) Sender S has kept rows R1, R2 and K in `seq` order; page 1 of generation g restores R1 and R2, and K stays kept; a re-park interrupts before R2 is polled; an operator recover arrives, on MongoDB as an outside add whose outside-add clock gives it a `seq` below R2's (injected at K3), and opens generation g + 1 while R2 is pending with the stale marker. The pages restore R2 before K; each is delivered once, in `seq` order. A model-end variant, where K ends the model, delivers R2 before K and drops nothing kept. Mutation: no `Stale` arm (a stale-released row waits for Complete); K overtakes R2, and in the model-end variant R2 is never delivered. (b) After a recover completes and the same creation parks again, a row the first Park's drain released carries a lower generation than every Open of the second Park. Mutation: reset the generation when a Park resolves; the old marker matches the second Park's drain and the row reaches the model ahead of its sender's kept rows. (c) A terminate interrupting a recover drain delivers no released row to the model. (d) The model ends during the second drain before its pages reach R2: later pages skip the `Stale` arm, R2 meets the finished-instance rule at Complete, and the drain completes. Mutation: a page after the end clears a `Stale` row by the kept-row predicate; the count check fails and the owner resets without end. | K3, K4 |
| S-5 | The metadata bound. Rows with every projected field at its limit, in each provider's widest encoding (SQL Server: single-byte characters, doubled by UTF-16; PostgreSQL and SQLite: the base64 path for the fields that can take it, the issued ids built from a job key or a custom id holding NUL or unpaired units, and three-byte characters in every other field, which admission keeps free of unpaired units, and of NUL on PostgreSQL; MySQL and MongoDB: three-byte characters), and a small payload are admitted and kept: a page of `P_page` = 170 such rows, the largest attainable admitted page (each row at its provider's largest admitted encoded size, so at most 170 × `M` = 4,177,920 bytes; `B` − 1 = 4,194,303 bytes is 16,383 more, and no legal page reaches it), is at most `B` and is taken whole, and a 171st such row is taken by a second page of one (the fourth review's R4-m1); the largest accepted row is the first row of a page and is taken. Run under resume, discard, expiry, terminate and delete, with a takeover at a page boundary: every page's encoded metadata is at most `B` and every drain completes. At K4 each provider's encoded size of the limit row is measured and is at most `M`. Mutation: drop the `Kind` limit; a row with a 4 MiB + 1 byte kind is kept, and its singleton page exceeds `B` | K3, K4 |
| S-6 | Admission at each ingress class: an outside add, each turn send (`Send`, `SendFromReceivedAddress`, `Start`, `StartJob`, `Subscribe`, `Publish`) and an actor type's composed `Add`. A field at its limit is accepted; one byte over is refused before any write, as is a row whose encoded metadata would be `B` − 1, `B` or `B` + 1 bytes (a long kind, small payload): no row exists and no Park keeps it; the turn-send case fails the turn as a model fault, and the composed case is a definite error. On SQLite, the probe's 5,242,882-byte key is refused at admission; SQL Server's `LargestInboxId` (1,342 bytes, measured in WTF-8) is refused on every provider, and the `UnicodeKeys` fixtures (the joint review's J-3) run under same-id addressing: the 4,096-unit random key is refused at key admission (above `N`), `"high\uD800"` and `"low\uDFFF"` are refused on every provider and `"nul\0x"` on PostgreSQL, each with no binding, no instance row and no message, and the 4,096-unit operation is refused (above `N`); `"plain"`, `"replacement\uFFFD"`, `"astral🌐"` and, off PostgreSQL, `"nul\0x"` are accepted with their short NUL and unpaired-unit operations, each creation of an accepted key gets the actor id `{vp}/Q(t, key)`, and a second creation after the first is deleted gets the same id under a higher creation; a start whose caller key holds an unpaired unit, or on PostgreSQL a NUL, is refused at the start with no row, so no later send of that instance is refused; a turn send, a composed send and an outside add whose `Target` holds an unpaired unit, or on PostgreSQL a NUL, are refused before any write (the turn send throws as a model fault), so no commit fails with 22021 and no message is redirected; a job-queue name with such a unit, or a NUL on PostgreSQL, is refused where it is configured or admitted; on PostgreSQL a key whose index entry would pass the B-tree limit is refused at admission and never reaches the engine. The whole-row cap (the joint review's C-14): a start through each outside ingress whose `Payload` and `Start.Input` together are one byte over 16 MiB less `M` and `F` is refused at admission before any write, and at the cap it is admitted. On PostgreSQL a NUL in `Kind`, `Sender`, an ordinary `Id` or `Match` is refused at admission. On every provider an unpaired UTF-16 unit in `Kind`, `Sender`, an ordinary `Id` or `Match` is refused at admission, while an issued id holding one is accepted through its reversible encoding and stored and read back unchanged. A job key admitted at the largest form SQL Server's 450-unit limit allows, counting the widest generation suffix, is restarted by a recover, and the recover's Open lands. Mutation: admit names in their first form; the restart's ticket passes the limit and the Open fails as a definite error on every attempt. Mutation: exempt keyed starts from the name limits, as the prototype's generated run ids did; the wide key is admitted, its actor id `{vp}/Q(t, key)` exceeds `N` and SQL Server's 450 units, and on SQL Server the instance insert fails at the store, which the case asserts never happens. Mutation: a fresh actor id per creation of a key; the second creation's id differs, and the same-id assertion fails. Mutation: remove the check at one ingress class; the over-limit row lands, a Park keeps it, and S-5's page assertion fails | K3, K4 |
| TR-1 | The R3-B1 trace with Native: frame F waits on T, a second frame F2 on its own timer; the Park consumes T; a resume suppresses T and a page clears it to a payload-free marker; X re-parks; a retry names T. Three runs: before the sweep (revive the payload-free marker), after it (insert), and the control, a retry before the clearing page (still-kept row). A takeover between Open and Complete, and between Complete and the firing. In every run the re-armed row's kind, payload, `Due` (the original absolute deadline), full key, `Sender` and creations equal the row the model first sent (`seq` aside), T fires once, Native's handler deserializes `TimerFired(w)` and resumes F exactly once, and F2 is untouched. Mutation: the present branch updates the marker's flags only; the first run delivers no payload and F never resumes. Mutation: `Due` = now + delay; the deadline assertion fails | K3, K4, K6, K7 |
| TR-2 | The plan contract: for Native, Flowchart and StateMachine, over waiting states with timers armed at different times, each `TimerSpec` equals the timer row the model sent (kind, payload bytes, `Due`), under a test clock that advances at every read. Mutation: the arm sends with `Delay` and records `turn.Now` plus the delay from a second read; the spec's due differs from the row's | K6, K7 |
| TR-3 | A plan whose timer id has another instance's or another creation's origin is a definite error: the recover re-parks and no row is written. A plan above `W_max` refuses the recover with reason "plan" and no change; a discard then lands. Mutation: map the id without the origin check; another instance's row is revived | K3 |
| TR-4 | The model-side plan bound: a Native workflow that would arm its `W_max` + 1st timer or job faults that activity, and the instance's plan stays at `W_max`; a later Park recovers. The same for bytes: a timer whose payload would take the plan's counted bytes past `B_plan` faults its activity, and the same value passed by reference arms and is restored. Mutation: no model-side refusal; after a later Park the recover is refused with reason "plan", for the count and for the bytes alike | K6 |
| TR-5 | **An Open at the plan bound** (the joint review's N8). Preconditions: a test model whose plan names `W_max` waits. Run 1: every wait is a timer retained `Suppressed` by an earlier resume that a re-park interrupted, half still kept and half swept, and a retry-all opens, so the Open re-arms `W_max` timers, each a full envelope whose payload is the largest a reference model's timer sends (Native's `TimerFired(wait)`). Run 2: a `resume` whose plan of `W_max` waits is 2,774 timers with no disposition and absent rows (re-armed by insert) and 1,322 jobs whose tickets the Park kept `Consumed` (restarted by create-only insert), with small job arguments so that the counted bytes stay within `B_plan`. The test model holds its 4,096 wait records and job specs as a compact recipe, as run 4 does. In the in-memory encoding, 1,322 × 12,416 bytes for the jobs, 16,384 bytes for fixed fields and 24,576 bytes for the maximum type data and labels leave 2,816 bytes for state and the other variable fields. At K4 use the same 4,096 waits with each provider's largest encodable job share and timers filling the remainder. The separate marker bound still limits registries ([data lifecycle](data-lifecycle.md#proposed-the-qualified-delete)). Interleaving: each Open's commit once with a takeover right after it and once with an unknown outcome retaken. Outcome: each Open is one commit that writes every re-arm, restart and disposition, or none; the retaken commit lands once; after Complete each timer fires once and resumes its wait once, and each restarted job runs once; on every provider the commit stays within the owned commit's per-statement row limit and the provider's transaction and message limits (MongoDB: 16 MiB a document, 48 MiB a message, the transaction lifetime). Measured at K4 and K12: the Open's encoded size and duration on each provider, and at K12 the turn share of the partition's other instances while it commits. Run 3, the byte bound (CodeRabbit's review of #4756): a test model whose plan's timers carry non-reference payloads summing, with the per-wait allowance, to exactly `B_plan`, retained `Suppressed` and re-armed by a retry-all; in a second variant the sum is `B_plan` + 1 byte, with the model-side check skipped so the plan reaches Open. At the bound the Open commits on every provider, inside MongoDB's 16 MiB document and 48 MiB message limits, and each timer fires once; at `B_plan` + 1 the recover is refused with reason "plan", nothing changes and a discard then lands. Run 4, statement size (the joint review's C-8 and C-11): a plan at `B_plan`, as in run 3, on MySQL with `max_allowed_packet` at 64 MiB (8.0's default) and at the 17 MiB floor, and on SQL Server, PostgreSQL and SQLite, with two rows the Open genuinely transmits (the fourth review's R4-m2): the instance envelope at its reserve limit, 16 MiB less 312 KiB without its `ParkRecord` (16,457,728 bytes in the provider's stored encoding), which the Open's own update writes with its `ParkRecord`, and a re-armed timer whose payload is at the message row's cap (16 MiB less `M` and `F`, 16,728,064 bytes in the provider's stored encoding), which the Open inserts, with the plan's other timers carrying the rest, so that its counted bytes are exactly `B_plan`. The test model never stores the large payload (fifth review R5-m2): its wait record holds a compact deterministic recipe, a seed and a length in the provider's stored encoding, from which both the arm's `Send` and `RestoreWaits` rebuild exactly the armed bytes, so the `TimerSpec` equals the armed row byte for byte (TR-2), and an independent filler field pads its state so that the envelope reaches the reserve limit within the instance bound. A message row at the message cap that the Park keeps stays beside them as a control whose payload no statement of the Open or of the drain's pages transmits, since a page never reads a payload. The Open commits on each. From the statements it captures, the fixture asserts two ceilings: every statement that carries more than one row is at most 4 MiB, or the provider's own limit where that is lower; and every statement above 4 MiB carries exactly one admitted row, at most the 16 MiB cap, and is at most that row plus the provider's protocol framing as measured in the run, within the provider's own limit (MySQL's `max_allowed_packet`, SQL Server's batch size, PostgreSQL's protocol message limit, SQLite's `SQLITE_MAX_LENGTH` for the bound value). The two transmitted rows each go in such a singleton statement above 4 MiB: the fixture finds both, the envelope update carrying the recipe and the filler and no timer payload, and the timer insert carrying the rebuilt payload, which it compares byte for byte with the armed row's; the kept control's payload bytes appear in no captured statement of the Open or of the drain's pages; all of the Open's statements run in its one fenced transaction, and a crash between two of them lands none; a MySQL server whose `max_allowed_packet` is 17 MiB less one byte refuses at startup. Mutation: split statements by row count only; at the 17 MiB floor the MySQL Open's statement exceeds `max_allowed_packet` and the commit fails on every attempt. Negative control (R5-m2): the fourth round's fixture, whose model state holds the 16,728,064-byte payload itself; the instance before `ParkRecord` would need more than its 16,457,728-byte bound, a deficit of 270,336 bytes before the rest of the state is serialized, so the commit that arms the timer is refused as a definite error and the setup never reaches the Open. Negative control: a plan of `W_max` + 1 waits is refused with reason "plan" (TR-3). Mutation: no byte bound at Open; with payloads totalling 40 MiB the MongoDB Open exceeds the message limit, fails as a definite error on every attempt and re-parks the instance, so it never recovers. Mutation: split run 1's Open over two commits, the re-arms first and the dispositions second; a crash between them leaves pending re-armed timers still retained `Suppressed`, the next retry-all's delete matches no done row, and that Open answers "not landed" on every attempt, so the drain never opens | K4, K12 |
| S-7 | The R3-M1 trace: retry R1 re-arms suppressed T; X interrupts before Complete; retry R2 names T and a `Consumed` Y. Runs: T still pending; T fired while parked and kept `Arrived`; T consumed by a re-park and kept `Consumed`. A takeover between R2's Open and Complete. R2 is accepted; T counts "already re-armed" (or, in the third run, is marked `Selected`); Y is delivered once; one armed T row exists; T fires once, and the model resumes T's frame once. Mutation: the earlier validator (a handle neither `Suppressed` nor kept `Consumed` refuses the command); R2 is refused and Y is never delivered. Mutation: re-arm a `Rearmed` timer again; in the pending run the delete matches no done row, so the Open answers "not landed" and never commits, and in the `Arrived` run the delete and insert drop its `KeptBy`, so no page releases it and T is delivered after Complete in place of during the drain | K3, K4, K6, K11 |
| S-8 | Retry-all with suppressed timers: a resume suppresses T1 (still kept when a re-park interrupts) and T2 (cleared, then swept). A retry-all re-arms T1 (present: delete and insert) and T2 (absent: insert), once each, records `Rearmed` and restores every `Consumed` row; after another interruption a second retry-all re-arms nothing. Each timer fires once and resumes its frame once. Mutation: retry-all skips `Suppressed` timers; neither fires | K3, K4, K6 |
| S-9 | The gate never waits for a row only Complete releases: the previous page's last restored row is a `Rearmed` T kept `Arrived`, which is released and delivered, and the drain completes; a timer the Open re-armed, due at once, and an unreleased arrival polled and seen at the current epoch are never the gate row, and the drain completes before either is delivered. Mutation (fourth review R4-m3): the poll skips every `Rearmed` timer while a Resolution is open, so the gate row T is explicitly unpolled and its `Seen` stays 0, which the test asserts; the first run never completes. Under the fifth review's handoff gate (R5-M3), withholding T from the model alone is also a stall mutant: the poll still marks T `Seen = e`, and a T that is seen and kept from the model (with no suspension) is not handed off, so the next page and Complete wait and the first run never completes; the test asserts T's `Seen = e` and that the drain makes no progress. (Under the fourth round's `Seen` gate that mark passed the gate, which is why the R4-m3 mutant had to leave T unpolled.) | K3, K6 |
| D-1 | Rotating waits: a model with at most two simultaneous waits finishes T_k and arms T_k+1 in each drain, then re-parks, 1,000 times, with takeovers at random Open and page boundaries. After every Open the dispositions name only the plan's waits, `ParkRecord` encodes within 56 KiB plus 64 bytes per entry, and on MongoDB the envelope stays within the reserve; the handle of every earlier T_k is refused or, if still kept, delivered as a stale input that resumes no frame; none selects a newer wait. Mutation: clear dispositions only at resolution; the encoded size grows with the interruptions and the bound fails | K3, K4, K6, K12 |
| D-2 | Job generations: (a) a job restarted at a first Open as `Restarted(g)` finishes during the drain, the model starts a new job under the same key, which takes a higher generation, and a re-park follows; the next Open decides the new job by its own ticket and prunes the old disposition. (b) Cross-Park: Park 1 restarts job J and resolves; Park 2 restarts J again while Park 1's done ticket is still within its window; the restart's create-only insert lands under a new generation, and the instance does not re-park. Mutation for (a): key the disposition by the job key with no generation check; the new job is decided by the old ticket's row. Mutation for (b): reset the counter when a Park resolves; the insert collides with Park 1's done ticket and re-parks the instance on every retry | K3, K6 |
| D-3 | Headroom under the row cap, on every provider (MySQL with `max_allowed_packet` at the 17 MiB floor): an instance whose envelope, measured without `ParkRecord`, is at 16 MiB less 312 KiB, with `W_max` timers, parks, and a recover Open commits a `ParkRecord` at its maximum, so the envelope is 16 MiB; a commit one byte over the reserve is refused as a definite error; a start whose `Payload` and `Start.Input` together are 16 MiB less `M` and `F`, in the provider's stored encoding, is admitted, and one whose two together are one byte over is refused at admission with no row, on SQL Server counted in UTF-16. Mutation: cap `Payload` alone; a start with a 15 MiB `Payload` and a 15 MiB `Input` is admitted as a row of about 30 MiB, which MongoDB refuses at the store and MySQL at the 17 MiB floor refuses on every attempt. Mutation: apply the reserve on MongoDB only; on MySQL the instance parks one byte under the packet limit and every recover Open fails. Measured: the encoded `ParkRecord` at `W_max` on each provider | K4, K12 |
| D-4 | A restart survives an interruption: a resume restarts job J at Open 1 (`Restarted(g)`); a re-park comes while the job is in flight; a resume at Open 2 finds the generation-`g` ticket pending and leaves it, so the job is not restarted again and its result is delivered once. Mutation: the disposition loses `g` (only a digest of the ticket key is kept); Open 2 falls back to the original ticket, kept `Consumed`, and restarts the job a second time | K3, K6 |
| E-7 | **The R4-B1 trace: the final released row after Complete.** Preconditions: a Native instance whose frame F waits on timer R; R fired while parked and is kept; a resume's last page restores R with (c, g) at `seq` 1100 (injected at K3), so R is the gate row; an arrival A, a `native.cancel` from another sender at `seq` 900, commits during the drain and is withheld; the next `kernel.drain` C is at 950, its order bound by the fixture. Interleaving, in two schedules. (1) The fourth round's `Seen`-gate schedule, as #4781's FR-1 executed it: the owner polls R and marks it `Seen = e`; before R reaches a model turn, C runs, finds no kept row and completes. (2) The acceptance schedule under the fifth review's handoff gate (R5-M3): the owner polls R and marks it; C runs first and waits, since R is not handed off; in the base run R then reaches its model turn and is consumed, and C's retry completes; in the after-Complete runs, re-staged on E-8's holding test model in place of Native's timer handler, since a Native timer is never held and Complete waits while the instance is suspended, the model holds R at its handoff, so R is an unconsumed cohort row when C completes, and the model consumes R once the input it waits for arrives. Runs, in each schedule (in schedule (2), every run past Complete uses the holding test model): as stated; a takeover after R's mark and before the model handoff, once before Complete and once after it (in schedule (2) the after-Complete takeover comes after R's hold, since Complete waits for the handoff, and the new owner tries the held R as a suspect); A also polled and marked by the lost owner while withheld, with a takeover after Complete, so A and R are both suspects of the new owner (`TrialGroup` 1); a `PollLimit` that splits A from R, so the new owner's first poll returns A and only a later one returns R; and a model-end variant where an earlier cohort row ends the model and no late arrival exists. Outcome in schedule (2)'s base run: R is handed off and consumed before Complete, F completes once, and A ends the model after Complete. Outcome in schedule (1), and in schedule (2)'s after-Complete runs: Complete sets the `Cohort` mark (c, g); in every run that has A, after Complete the owner offers R before A; in the suspect run R's trial comes first and alone, and A is neither tried before R nor grouped with it; in the poll-limit run A is withheld and shelved until R is polled and delivered, and the mark clears once a read finds no cohort row; in every run except the model-end variant, the model's transitions are R's (F completes once) and then A's (the model ends); in the model-end variant there is no late arrival, an earlier released `native.cancel` Q ends the model in its real turn, R (a `native.resume` the model never recalls) is dropped under the finished-instance rule, and the drain still completes; no `KeptBy` remains in any run. A control run in the poll-limit case: P's tree terminate reaches the instance before R is polled and waits until R has been delivered and the mark's read finds no non-held cohort row. Executed in this repair: `fr1-base`, `fr1-takeover-before-complete`, `fr1-takeover-after-complete` and `fr1-model-end`, each under the reviewed, repaired and negative rules, and `fr1-trial-takeover`, `fr1-poll-limit` and, for the control run, `fr1-control-after-complete` (#4781). Mutation (the reviewed rule): plain tuple order after Complete; A is offered first, the model ends, and F completes zero times. Mutation: precedence kept in the owner's memory only (`memory-precedence`); in the takeover runs A is offered first. Mutation: trial selection in plain `seq` order; in the suspect run A is tried alone first and F completes zero times. Mutation: no `Cohort` mark; in the poll-limit run A is offered before R is polled, and F completes zero times. Mutation: control kinds pass the mark; in the control run P's tree terminate overtakes the unpolled R, and R is dropped undelivered. Crash-takeover control run: after Complete, a crash takeover leaves a control K and R, which the lost owner marked and whose `seq` is above K's; R's trial runs first, exempt from K's recovery-boundary cut, R is delivered once, and K then takes its built place and applies; executed in this repair: `fr1-control-crash` (#4781). Mutation: the recovery boundary holds a cohort suspect; R waits behind K's cut while K waits behind R under the mark, and the instance stalls. Reset run: after Complete, with the mark set, the owner has marked R (`seq` 1100) in an epoch it still counts as its own (`_mine`), P's tree terminate K arrives at `seq` 1000, and an unknown commit outcome resets the owner, which retakes the partition; R is polled again with its earlier `Seen`, so it carries `RecoveryOrdering` and is no suspect, and K becomes the boundary; R is offered whatever K's cut, delivered once, and K then applies; executed in this repair: `fr1-control-reset` (#4781). Mutation: the boundary holds a non-suspect cohort row (`boundary-holds-cohort-nonsuspect`); R fails K's cut while the mark holds K behind R, and nothing is offered under a healthy owner. Open-drain takeover run: another process takes over before Complete, with A and R both suspects and A, withheld by the open drain, sorting first; A is passed over without skipping its instance, R is tried ahead of C's cut as a cohort suspect, and C completes after R; with the new owner's `PollLimit` at 1, A is also shelved, so the next polls reach C and R and the drain completes; executed in this repair: `fr1-trial-takeover-open` and `fr1-trial-takeover-open-poll-limit` (#4781). Mutation: a withheld suspect skips its target, as `NextTrial` is built (`skip-target`); R is never tried, and the drain stalls. Mutation: the drain-withheld suspect is not shelved (`no-shelf`); in the poll-limit run every poll returns A again and nothing moves. Model-end control: the ending turn itself recalls R, so R is gone before the drop rule applies; executed in this repair: `fr1-model-end-recall` (#4781). Every run named "executed in this repair" above is schedule (1), the `Seen`-gate schedule of the fourth round (#4781); schedule (2) is the acceptance schedule at K3, K4 and K6, executed in this repair as `fr2-r4b1-base` (the base run) and `fr2-r4b1-held` (the after-Complete takeover with R held, R a composite signal that the real `NativeModel` holds in place of E-8's test model, tried first by the new owner), each under the reviewed, repaired and negative rules, with `seen-gate-no-precedence` (#4790); schedule (2)'s other runs (the takeover before Complete, A also a suspect, the poll limit, the model end and the control runs) are proposed, qualified at K3, and HC-1 and HC-2 cover the handoff gate itself | K3, K4, K6 |
| E-8 | **Held rows never block** (R4-B1). Preconditions: as E-7, with a model that holds R until a later input and A is that input (a test model at K3, a StateMachine during a suspended action at K7). Run 2, under a poll limit after a takeover: the bounded poll returns A (900) and R1 (1000), and R2 (1100) only later; R1 is delivered and held. Outcome: after Complete the poll skips the held R, A is offered with R handed back, R receives A, and nothing waits for good; with the drain still open, a held cohort row likewise lets the next row through. In run 2 the mark's read excludes R1 and finds R2, so A waits until R2 is polled and delivered, and then A is offered with R1 handed back; no cohort row is dropped. Executed in this repair: `fr1-held`, with the `held-blocks` mutant, and for run 2 `fr1-held-poll-limit`, with the `one-row-read-counts-held` mutant (#4781). Mutation: count a held cohort row in the precedence or in the `Cohort` mark's read; A waits behind R, R waits for A, and neither is ever offered. Mutation: a one-row read counts the held row; in run 2 the read returns R1, which the owner holds, so A is offered, ends the model, and R2 is dropped. Run 3, the mark under a held row: the final page restores only R1, a composite signal to a `HeldGate` H, and A (`native.cancel`, `seq` 900) is withheld; after Complete the model holds R1, the read finds no non-held cohort row, and before A is offered another process takes over with `PollLimit` 1, so A and R1 are both suspects and the first poll returns A alone. Outcome: the mark is still set, since a held cohort row remains, so the new owner's read finds R1; A is shelved, R1 reaches the model again, and A then ends the model with R1 handed back (model inputs R1, R1, A). Executed in this repair: `fr1-held-crash` (#4785). Mutation: clear the mark as soon as no non-held cohort row remains (`clear-with-held`); the new owner sees no mark, A is tried first and ends the model, and R1 is dropped under the finished-instance rule without its second turn. Run 4, suspension under the mark: as run 3, but after the model holds R1 an operator's `kernel.suspend` applies, then another process takes over, then a `kernel.resume` arrives. Outcome: while the instance is suspended its cohort rows are not offerable, so the resume applies; R1 then reaches the model again before A, and A ends the model with R1 handed back. Executed in this repair: `fr1-held-suspend-crash` (#4786). Mutation: controls wait under the mark while the instance is suspended (`controls-wait-while-suspended`); the resume waits for R1 and R1, withheld by the suspension, waits for the resume, and the instance stays Running for good | K3, K7 |
| E-9 | **Same-owner cached row** (R4-M4). Preconditions: E-6(a)'s rows R1, R2 and K of sender S; page 1 of generation g restores R1 and R2; the same owner polls R2 and marks it `Seen = e`; an interruption consumes another input and re-parks; the next recover opens generation g + 1 before R2 is consumed; no ownership change throughout. Runs: R2 polled and not yet delivered; R2 held by the model; a poll in flight when the g + 1 page commit lands, having read R2 at g. Outcome: after the commit lands, the owner's copy of R2 carries (c, g + 1) and the committed `Seen`; its next flush marks `Seen = e`; R2 is delivered during the drain before K (in the held run it stays held, counts as taken up and is handed back with the next input); the in-flight poll's copy is discarded; the drain completes with the same owner, and each row is delivered once. Mutation: no refresh; R2 keeps tag g in memory and is withheld during the drain, so K is delivered before R2, and in the variant where K ends the model R2 is never delivered; where the gate reads the durable `Seen`, the drain also waits until a takeover. Mutation: keep the in-flight poll's pre-commit copy; the same failure | K3 |
| E-10 | **Unknown commit and takeover** (R4-M4). Preconditions: E-9's. Runs: the g + 1 page commit has an unknown outcome, once landed and once not; a takeover after the commit lands and before R2 is marked again. Outcome: after the reset the owner re-polls whichever version landed: landed, R2 carries g + 1 and is delivered in the drain; not landed, the page is recomputed and retags R2; after the takeover the new owner polls R2 at g + 1 and marks it at its own epoch. Each row is delivered once, R2 before K. Mutation: after the unknown outcome, keep the in-memory rows without a reset; the landed run fails as in E-9 | K3, K4 |
| E-11 | **A restored ticket never holds the cohort** (R4-B1). Preconditions: a retry restores the `Consumed` ticket of job J with the drain's (c, g) tag, so the owner starts J as a job, and J hangs; the drain completes, and Complete sets the `Cohort` mark (c, g); no other cohort row remains. Interleaving, each in its own run after Complete: an operator's terminate; a parent's tree terminate; a suspend, then a resume. Outcome: the mark's read skips J's ticket as a ticket kind and finds no offerable cohort row, so the mark clears and each control applies at once: the terminates end the instance Cancelled and cancel J as built, and J's late result is dropped; the suspend withholds the instance and the resume lifts it while J still runs. Executed in this repair: `fr1-ticket-control` (the operator's terminate), `fr1-ticket-control-parent` (the parent's tree terminate) and `fr1-ticket-control-suspend` (the suspend and resume), each with the `controls-wait-on-ticket` mutant (#4781). Mutation: controls wait behind an in-flight ticket (the read counts J's ticket); every control waits for as long as J runs, and without bound when J hangs | K3, K6 |
| IC-1 | **The idempotent cancel at a retirement marker** (fifth review R5-B1). Preconditions: DT-7's order (a), with P, A, G (with child G2) on three partitions; A's marker retains P, P's creation, {G} and {(J, g)}; P's tree terminate to A committed while A's delete drain was open and is held in flight. Runs, each on its own: J pending; J absent, because a fresh owner polled J after A's retirement and the fenced dispatch refused and consumed it as `RetiredCreation`; J `Done` and kept by a Park (`KeptBy` set), an injected below-ingress state, since a delete drain clears every kept row before its Complete, so no valid schedule reaches a marker with one (the reachable kept case is IC-2's open-drain run); J `Done` with no `KeptBy`; a mixed list (J1 pending, J2 absent, J3 `Done` with no `KeptBy`); P's terminate delivered before A's Complete (order (b)); P's terminate delivered twice; a takeover between J's settle and P's terminate; a key-collision run, where A's registry holds job `j.1` at generation 0 and job `j` at generation 1, `j`'s ticket is absent and `j.1`'s pending; and a creation-0 run, a row with creation 0 that starts jobs `j` and `j#g1`. Interleaving: as stated, with each commit handed to the real count-enforcing `CommitAsync`. Outcome: in every run the store accepts the cascade's commit, which consumes P's terminate (1) and each pending ticket (1 each) and nothing else, clears a kept ticket's `KeptBy`, sends G a terminate from A and writes the marker at its revision plus one with the list cleared (in order (b), A's row, under J-1); a pending ticket receives the in-process cancel and an absent one none; G and G2 end Cancelled; the duplicate delivery is consumed once; in the key-collision run the two tickets have distinct ids (`…!3_j.1` and `…!1_j#g1`), the cancel of (`j`, 1) is the no-op and only `j.1`'s ticket is consumed; in the creation-0 run no positive generation is issued at that row: both tickets keep the built ids `…!j` and `…!j#g1`, and the registry records generation 0 for each; P has received exactly one Faulted notice and nothing more. Executed in this repair: `rt2-pending`, `rt2-absent`, `rt2-done-kept`, `rt2-mixed` (its third ticket `Done` and kept, in place of `Done` with no `KeptBy`), `rt2-open-drain-mixed` (order (b)), `rt2-duplicate` and `rt2-takeover`, each under the reviewed, repaired and negative rules, the reviewed rule failing every run but the pending one; `presence-from-memory` on `rt2-absent` and `rt2-takeover`, which decides presence from the owner's memory and fails the takeover run; and the two distinct ids as `rt2-ticket-collision`, with `generation-inside-key` as the third mutation, where A's registry holds `j` at generation 1 with its ticket pending and a pending generation-0 ticket of `j.1` is a fixture beside it that the cascade leaves alone (#4789). The run with J `Done` and no `KeptBy`, the key-collision run as stated (`j.1` in the registry, `j`'s ticket absent), the creation-0 run and the mutation that ignores the flush's own earlier consume are proposed, qualified at K3. Mutation (the reviewed rule): the cancel is a counted consume; the absent and done runs fail at the store with "consumed message … is not there", P's terminate stays pending, no child send is stored, and G and G2 stay live. Mutation: the presence read ignores the flush's own earlier consume; the takeover run diverges in the same way. Mutation: the generation appended to the key without a separate component (`…!{length}_{key}.{g}` with the length of the key and generation together); (`j.1`, 0) and (`j`, 1) share one id, and the cancel of (`j`, 1) consumes `j.1`'s ticket The in-process cancel and the marker's next-revision write under the widened `Validate` are proposed, qualified at K3. | K3, K4, K6 |
| IC-2 | **One cancel rule on every cascade path** (R5-B1). Preconditions: A, a child of P, with child G and two jobs: J1 pending, and J2, whose result the owner converted while A was suspended (R5-M4), so its ticket is absent while A's registry still lists (J2, g). Runs: P's tree terminate of running, suspended A (the live terminate, `WorkflowInstance.cs` 196–198); P-7's open-drain refusal at parked A with a discard drain open, where a third job J3's ticket is kept `Consumed` by A's Park and the discard's pages have not yet reached it; the marker cascade after A's delete. Outcome: each commit consumes the terminate and J1's ticket only, J1 receives the in-process cancel, J2's cancel is a no-op, in the open-drain run J3's `KeptBy` is cleared with the ordinary window by its full key and J3 is not counted as consumed, J2's pending result row is dropped under the finished-instance rule, G ends Cancelled, and P hears once. Mutation: the live terminate keeps the counted cancel; its commit diverges on J2's missing ticket and A keeps running with G | K3, K6 |
| PC-1 | **The configured boundary of a paged cascade** (fifth review R5-B2). Preconditions: A, a child of P, starts n children over many turns, one per turn, each with a child of its own, and no jobs; n is `P_cascade` (1,000), `P_cascade` + 1 and 10,001. Runs, for each n: (a) P terminates running A; (b) A is deleted while P's terminate is in flight, so the cascade runs from the marker (DT-7's order (a)); (c) P-7's open-drain refusal at parked A. Outcome: the first page is in the ending or refusing commit; no page commit carries more than `P_cascade` cascade entries (child terminates plus job cancels), and its sends are at most its child terminates plus one `kernel.cascade` while entries remain, plus, on the first page of a live terminate, the parent's one notice; the page commits number ⌈n / `P_cascade`⌉ (1, 2 and 11), as `elsa.kernel.cascade.pages` counts; every child and grandchild ends Cancelled, and each child receives exactly one terminate from A; P receives exactly one notice (Cancelled in (a), Faulted from A's Complete in (b), the open drain's in (c)); A's obligation ends cleared; a model turn that sends 10,001 messages still fails and parks under the send cap. K12 runs n = 10,001 under load and records the time to the last descendant's end and the other instances' turn share. Executed in this repair: run (a) at the three sizes as `rt3-live-p`, `rt3-live-p-plus-1` and `rt3-live-10001` (the boundary runs asserting 1 and 2 cascade commits), run (b) at 10,001 as `rt3-marker-10001` and run (c) at 10,001 as `rt3-open-drain-10001` (with Complete after page 3), each under the reviewed, repaired and negative rules, with `page-above-cap` on `rt3-live-10001` and `rt3-marker-10001` (#4789). Runs (b) and (c) at `P_cascade` and `P_cascade` + 1, the `elsa.kernel.cascade.pages` count and the model turn of 10,001 sends are proposed, qualified at K3; the harness's stub model takes no turn, and under the reviewed rule A's own terminate turn above the cap parks through the real `Park`. Mutation (the reviewed rule): one commit for the whole list under the send cap; at 10,001 it fails on every attempt and every child stays live | K3, K4, K12 |
| PC-2 | **A takeover and an unknown outcome partway through a cascade** (R5-B2). Preconditions: PC-1 run (b) with n = 10,001. Interleaving: a takeover after page 3 commits and before page 4; separately, page 6's commit has an unknown outcome, once landed and once not. Outcome: the new owner polls the outstanding `kernel.cascade`, reads the cursor from the marker and continues with page 4; after the unknown outcome the owner resets and recomputes from the committed marker, so a landed page is not sent again and an unlanded one is sent under the same ids and control key; in every run each child receives exactly one terminate and the list clears after page 11; a `kernel.cascade` whose key is not `Cascade.Control` is consumed and dropped (reason control). Executed in this repair, from a running A's row in place of the marker: `rt3-takeover` (a takeover after page 3), with `no-next-control`, and `rt3-crash-landed` and `rt3-crash-lost` (an unknown outcome at page 5, landed and not), each under the reviewed, repaired and negative rules, the negative being this mutation (#4789). The same schedules at the marker and the drop of a `kernel.cascade` whose key is not `Cascade.Control` are proposed, qualified at K3. Mutation: the cursor kept in the owner's memory only; after the takeover the new owner starts the list again, and children 1 to 3,000 receive a second terminate, which the count fails | K3, K4 |
| PC-3 | **A delete while Discharging** (R5-B2). Preconditions: a root R (no parent) with 10,001 live children, terminated by an operator; A, a child of P, with 10,001 live children, terminated by P; a keyed root K-R under caller key K with 2,001 children, terminated by an operator. Interleaving: after page 2 of each cascade has committed, an operator deletes the row naming its Cancelled mark; for K-R, a keyed start under K with operation op-1 is sent before the last page, then retried with op-1 and with a new operation op-2 before the last page, and both are retried again after it. Outcome: each marker keeps the obligation, still `Discharging`, with its cursor, `Control` and `Serial`; the pages continue at the marker with no further terminate, and every descendant ends Cancelled; R's marker keeps the obligation although R has no parent; key admission answers each keyed start before the last page `in-progress` with reason `ObligationOutstanding`, in its own transaction, writing no receipt and no message, so the op-1 retry finds no stored `accepted` and is refused like the op-2 one; after the last page, the third of ⌈2,001 / `P_cascade`⌉ = 3 and one page after the delete, the op-1 retry is admitted under the next creation and the op-2 one answers `in-use`, as a second start on a live binding does; no start reaches the worker's `ObligationOutstanding` refusal; no marker sends a parent anything. Executed in this repair: A's run as `rt3-delete-discharging` (the delete added after page 2 and delivered after page 3), with `delete-drops-discharging` as the first mutation; and K-R's run in part as `rt3-key-reuse-discharging` (2,001 children, the delete after page 1, R's marker keeping the obligation; while it discharges a new operation op-2 and its retry are answered `in-progress` with `ObligationOutstanding` and write no receipt, and after the last page op-2 is admitted and starts creation 2), whose `negative` is the third mutation; each under the reviewed, repaired and negative rules (#4789). R's run with 10,001 children, the op-1 retries and the `in-use` answer, and the mutation where a root's marker keeps nothing are proposed, qualified at K3. Mutation: `Retire` drops a `Discharging` obligation, as it keeps no list today; children 2,001 to 10,001 stay live. Mutation: a root's marker keeps nothing, as for a `Held` list; R's remaining children stay live. Mutation: admission does not read the marker; it answers op-1 `accepted` and writes the receipt, the worker refuses the start with `ObligationOutstanding`, the caller hears nothing, and the op-1 retry gets the stored `accepted` while no run exists | K3, K4, K6 |
| PC-4 | **Real models end their whole tree, and the cap still binds models** (R5-B2). Preconditions: a dedicated test model that makes one parent-bound start per turn over 10,001 turns, each child waiting on a timer (sixth review R6-m2); a smaller real Native cross-check, a `Parallel` of `P_cascade` + 1 = 1,001 branches, each a `DispatchWorkflow` with `WaitForCompletion = true`, whose children start parent-bound (the 1,000-item ForEach cap does not bind a `Parallel`; its branch cap is qualified at K6); and a combiner tree whose combiner under FailFast holds C outstanding children. Runs: the root is terminated by an operator; one leaf faults under FailFast; a test model that sends 10,001 messages in one turn. Outcome: before the terminate the root's registry lists every child, 10,001 and 1,001, and each child's recorded `Parent` and `ParentCreation` are the root's; the root's terminate ends it Cancelled and its cascade pages end every child, each child's Cancelled notice refused at the root as `UnknownChildCreation`; the FailFast turn sends at most C terminates, a model turn within the cap, and the tree ends as large fan-out states; the 10,001-send turn fails and the instance parks (fairness decision 3). Mutation: the cascade's exemption widened to every turn, model turns included; the 10,001-send model turn commits. Mutation: the root's terminate sends its whole registry in one turn under the cap; it fails on every attempt and the 10,001 children stay live. Fixture control: the sequential loop with the default `WaitForCompletion = false` starts detached children, so the registry is empty and the terminate ends none of them | K3, K6 |
| PC-5 | **Terminate and delete on a multi-page registry** (R5-B2). Preconditions: RD8's terminate with `ThenDelete`, on a running root R and on a running child A of P, each with `P_cascade` + 1 = 1,001 entries (1,000 children and one job, the job listed first); in a third run A is protected by `ProtectsDeletion`; in a fourth and a fifth run the root R and the child A have already ended (Finished) with that registry still listing 1,001 entries; in a sixth an ended root has an empty registry. Interleaving: the operator's terminate with `ThenDelete` is delivered; a takeover follows after page 1. Outcome: the one commit ends the run Cancelled, sends the job's in-process cancel and the first page (the job's counted cancel and 999 child terminates), and, unprotected, retires the row with a `Discharging` obligation whose list equals the previous row's registry and jobs, cursor after entry 1,000, and its outstanding `kernel.cascade`; `Validate` accepts that marker in this commit, which also terminates the row; its single journal row is the deletion marker; page 2 runs at the marker after the takeover and clears the list; every descendant ends Cancelled; P receives exactly one Cancelled notice from A; in the protected run A is terminated and kept, the obligation is on its live row, and the pages run there; in the ended-row runs the terminate takes the control path as built, the commit keeps the end status, sends page 1, retires the row with the same `Discharging` obligation (`Validate` accepts it as a commit that starts a terminate's cascade at an ended row with a registry or an outstanding job), every descendant ends Cancelled, and P receives nothing more, since the ended A already sent its notice; in the empty-registry run the command is the plain qualified delete and the commit sends nothing. Mutation: `Validate` as widened for a carried-over obligation only; the terminate-and-retire commit is refused on every attempt and the run neither ends nor is deleted. Mutation: `Validate` admits a new `Discharging` obligation in a delete that starts no cascade; a fixture's delete of an ended row with an empty registry and a fabricated list is accepted. Mutation: `ThenDelete` on an ended run skips the cascade; in the ended-row runs the 1,000 children keep running after the row is deleted Executed in this repair: the running root, child and protected child as `rt3-thendelete-root-*`, `rt3-thendelete-child-*` and `rt3-thendelete-kept-*`, and the ended root and child as `rt3-thendelete-ended-root-*` and `rt3-thendelete-ended-child-*`, each at `P_cascade` + 1 entries (`P_cascade` − 1 children and two jobs, jobs first) and at 10,001 children and two jobs, under the reviewed, repaired and negative rules, the running and ended runs' negative rules being the first and third mutations (#4789); the takeover after page 1, the empty-registry run and the second mutation are proposed, qualified at K3. A terminal-growth run (sixth review R6-B1, seventh review R7-B1): after the last child start, A's Native turns open invocation inboxes until the row's envelope check, which counts the projected terminal row (the state, its `TerminalGrowth`, the registries at the marker's per-entry sizes, `CustomHandles`, the model entries of `CommitTimes`, `Model` and `Definition` at their encoded size, and the fixed 16,384-byte reserve), admits nothing more; each parent-bound `StartChild` also leaves a creator handle (`Turns.cs` 490–498), so the 1,000 children leave 1,000 creator handles in `CustomHandles`, about 18 bytes each at NT-2's id lengths and more for longer child ids, measured at K3; the ThenDelete commit then fits and lands (proposed, qualified at K3). This run is the protected multi-page success case and asserts no rejecting mutation of the projection (eighth review R8-m2): its 1,000 children are charged 384 bytes each and stored in at most 192, so at least 192,000 bytes of registry slack separate the charged and stored rows, more than the about 18,001 bytes of the 1,000 short handles, and a mutant that omits a charge can land as well. The rejecting mutations are carried where the one-child fixture leaves less slack than the omitted charge: handles not counted in TG-1 run (c1) and TG-2's third run, and `TerminalGrowth` omitted in TG-1's negative. The former mutation that counts the registries as stored is removed: no fixture in the JSON UTF-8 measure distinguishes it, since the stored list replaces the stored registries, and a provider-encoding fixture may distinguish it at K4 (condition C1). | K3, K4, K11 |
| PB-1 | **The parent binding of a cascade terminate at a running and an ended recipient** (fifth review R5-M5). Preconditions: RM-1's negative fixture: A's retirement list names H, same tenant, right creation, whose recorded parent is Q; H has a live child H1 and a pending job JH. Runs: H running; H ended with a registry still listing H1; H parked with no drain open; and, as a forgery control, a test model that tries to send a cascade-marked `kernel.terminate` through every turn method, and a test actor type that composes a marked terminate whose `Sender` is another actor. Interleaving: P discharges A's marker, so H receives A's marked terminate. Outcome: through the actor type's control path, H refuses the marked terminate with no effect: H keeps its status and revision, H1 keeps running, JH's ticket is not cancelled, no notice is sent, and `elsa.kernel.park.refused` counts reason parent; A's marker still clears its list; no turn method can set the mark, so the model's sends arrive unmarked, and the composed foreign-sender terminate is refused at the commit as a definite error. Executed in this repair: the running and the ended H as `rt4-running-unrelated` and `rt4-ended-registry`, with `binding-running-only`, and the forgery control as `rt4-forgery` (three model turns of an unrelated X: a marked `kernel.terminate` and a marked application message through `DurableTurn.Send`, and a marked terminate composed through `Add` naming Q as `Sender`), with `commit-accepts-mark`, each under the reviewed, repaired and negative rules, the reviewed rule terminating the running and the ended H (#4789). H's pending job JH, the `elsa.kernel.park.refused` count, the parked H with no drain open, the turn methods other than `Send` and `Add`, a test actor type's foreign-sender terminate and the mutation that leaves the mark off the pages are proposed, qualified at K3. Mutation (the reviewed rule): the binding checked only at a parked or draining recipient; the running and the ended H are terminated, and H1 and JH are cancelled. Mutation: the mark left off the marker's cascade pages; the running H is terminated | K3, K6 |
| PB-2 | **A genuine child, a model's application terminate and an operator's terminate still apply** (R5-M5). Preconditions: A with a genuine child G (with child G2), and an unrelated running instance X with a child X1. Runs: G running, parked with no drain open, ended with a registry, and retired with a `Held` obligation, each receiving A's tree terminate or A's marker cascade; a model of an unrelated running instance B that sends `kernel.terminate` to X through `ITurn.Send` (X is not B's child); the same application send from B to a parked X with no `ParkSelection`; an operator's terminate of X with the terminate permission, and one without it. Outcome: G accepts in every state and its subtree ends Cancelled; B's application terminate ends the running X as built (`WorkflowInstance.cs` 63–64, 155–158, 177–203), and X's own cascade, marked and sent from X, ends X1 under X1's binding to X; at the parked X, B's send is refused under P-7's rule, since it is not the parent and names no Park; the operator's terminate ends X, since it has no issued origin and no binding applies; the one without the permission is refused at ingress and writes no row. Executed in this repair: a running G as `rt4-genuine-child` (P's terminate ends the running A, whose marked terminate ends G), the application terminate of a running non-child as `rt4-model-send` (sent through `DurableTurn.Send`, it ends the target and the target's own marked cascade ends its child), with `bind-every-issued-terminate` as the first mutation, and the operator's terminate with the permission as `rt4-operator`, each under the reviewed, repaired and negative rules (#4789). G parked, ended and retired, the application send to a parked target, the operator's terminate without the permission and the second mutation are proposed, qualified at K3. Mutation: bind every terminate with an issued origin, application sends included; B's terminate of the running X is refused and the built behaviour is lost. Mutation: bind every terminate, an operator's outside add included; the operator's terminate of X is refused and X keeps running | K3, K6 |
| JC-1 | **A completed job across Park, takeover and recover** (fifth review R5-M4). Preconditions: a Native workflow arms background job J at the current generation g1, whose activity returns a fixed value V and counts its physical invocations; the instance then parks on another input while J runs. Interleaving: J finishes while the instance is parked; the owner's standalone conversion commits; a takeover; a `resume`. Crash runs: a crash just before the conversion's commit, and one just after it. Outcome: in the crash-after run J's ticket is gone after the conversion, the result row `{ticket id}/result`, in the ticket's scope, is kept `Arrived` and the parked consumption keeps J's (key, g1) registry entry, no owner starts J after the takeover, and the recover restores the result row, whose generation check against the registry's current g1 accepts it; in the crash-before run the new owner does not start J at the parked row, which consumes the still-pending ticket as a late input, keeps it `Arrived` and keeps J's registry entry, and the recover's Open restarts J from its `JobSpec` under the next generation, which re-registers (J, next generation) in the registry, and clears the old ticket, so J runs again under the same effect key and its result is converted and delivered; in both the model receives V exactly once and resumes its frame once, and the physical invocations are one and two, never more. Executed in this repair: the crash-after run as `jr1-crash-after` and `jr1-crash-after-keep` (the crash after the conversion, and after the parked instance kept the result) and the crash-before run as `jr1-crash-before`, each under the reviewed, `reviewed-kind`, repaired and negative rules; the first mutation as `reviewed-keeps-registry` on `jr1-crash-after`, the second as the reviewed and negative rules of `jr1-crash-before`, and the third as the reviewed and negative rules of `jr1-result-parked-consume`, a run with no takeover (#4790). The fourth mutation as stated is proposed, qualified at K3: `reviewed-kind` persists the kind, and the Open then clears the row as one the registry no longer holds. Mutation (the reviewed rule): Park keeps the ticket row with `Done` and `KeptBy`; the recover restores a ticket, the owner starts J again, and the invocation count fails. Mutation: the parked row's late-input consumption drops J's registry entry, as `RemainingJobs` is built; in the crash-before run the Open clears the kept ticket as obsolete, J never runs again and the model never receives V. Mutation: the entry is cleared on a parked consume of the result row; in the crash-after run the Open clears the kept result as a key the registry no longer holds, and the model never receives V. Mutation: persist only the changed kind under the ticket's key; after the takeover the row is discarded as a supplied completion and the model never receives V A result-pending run: the conversion's result row is still pending when the Open runs; the Open's lookup finds the ticket absent and the result row pending, does not restart J, and the result reaches the model once after Complete. Mutation: the lookup probes the ticket only; in the crash-after and result-pending runs the Open restarts J under g2, the pages clear the g1 result as obsolete, the model never receives the original (g1) result, only the g2 re-run's, and J runs twice. Executed in this repair: the crash-after lookup as `jr1-result-kept-lookup` and the result-pending run as `jr1-result-pending-lookup`, each with `ticket-only-lookup`, under the reviewed and repaired rules (#4793). A cancel of J by the model after the resume, before its result is delivered, is CJ-1, and the delivery of the result row to a running turn without the fixture's manual registry clear (`experiments/Review5.Delivery/Jr1.cs` 404–405) is CJ-2 (sixth review R6-M5). | K3, K4, K6 |
| JC-2 | **Only the kernel writes a result row, and the reader accepts it** (R5-M4). Preconditions: a Native instance with background job J at the current generation g1 (and, for run (c), a kept g0 row of an earlier generation); the activity returns V and counts its invocations. Runs, each with its own interleaving: (a) an outside add of a `job-finished` naming J's key, in the ordinary scope, while J runs; (b) a test actor type composes a `job-finished` in J's ticket scope in an ordinary turn; (c) a recover over a Park that kept g0's result row beside g1's; (d) the instance is suspended, J finishes and the conversion commits, then a terminate arrives before the resume; (e) the same flush carries a cancel of J and J's in-memory result, once from a terminate and once from the model's own `CancelJob` (`Turns.cs` 405–410) in a running instance that goes on (a cancel in a later flush than the conversion is CJ-1); (f) a running instance's model turn holds J's result, its commit converts it, and a takeover follows; (g) the parked instance consumes a converted result row and a recover restores it; (h) J's activity returns a value whose stored encoding is one byte above the message row's payload limit, once while the instance is parked and once while it is deliverable; (i) the model finishes while J runs, with no Park, so the entered driver keeps its completion path (existing prototype behaviour, `Worker.cs` 1173–1174), J's result reaches the Finished row through the finished-instance path, and an operator later deletes the row. Outcome: (a) the outside add is consumed and dropped as a supplied completion, as built (`Turns.cs` 109–111, `Worker.cs` 1030–1037); (b) the composed row is refused at the commit as a definite error; (c) g0's row is cleared by the page that reaches it and never restored (JS-2's rule), and g1's is delivered; (d) the terminate's cancel of J is the idempotent no-op, since the conversion consumed the ticket, and the pending result row is dropped under the finished-instance rule; (e) the commit consumes J's ticket once, as the cancel, inserts no result row, and the store accepts the batch; in the `CancelJob` run the model never receives a result for J; (f) after the takeover J does not run again and the model receives the held result once; (g) the restored row is accepted as the worker's own completion, the model receives V once, and the registry entry for (J, g1) is cleared by that commit; (h) in both runs the worker turns the result into the fault "result too large", the conversion or the consuming turn consumes the ticket once, no row exceeds the cap, J is invoked once and never re-run, and the model receives the fault as a job error; (i) the Finished row, which has no unresolved Park, consumes the result as a late input and clears (J, g1) from its registry, as built (`WorkflowInstance.cs` 78–90, 135–144), so the later delete's retirement marker lists no J and no cascade ever cancels it. Mutation: accept a `job-finished` in a ticket scope that the conversion did not mint; in (b) the composed row reaches the model as J's result. Mutation: the reader as built; in (g) the restored row is discarded as a supplied completion and the model never receives V. Mutation: keep the entry at every ended row, parked or not; in (i) the Finished row keeps a stale (J, g1), and the delete's marker must list a job that no longer exists. Mutation: no size check where the job returns; in (h) the result row exceeds the cap, the conversion commit fails on every attempt, and after a takeover J runs again. Mutation: convert a ticket being cancelled; in (e)'s `CancelJob` run a result row for the cancelled J is written and the model later receives the result of a job it cancelled Executed in this repair: run (c) as `jr1-obsolete` (with `generation-blind`) and run (g) as `jr1-result-parked-consume`, under the reviewed, repaired and negative rules (#4790); the other runs are proposed, qualified at K3. | K3, K4 |
| PP-1 | **A cohort suspect against an exhausted ticket** (fifth review R5-M6). Preconditions: an early page restores job ticket J (`seq` 900) and the owner starts it; J hangs, and owners are lost during its physical trial until J reaches `MaxAttempts`; the next owner marks J; a later page restores timer R (`seq` 1100) for frame F. Runs: (a) a takeover with the drain still open, R and J both suspects; (b) a takeover after Complete with the `Cohort` mark set, R held at the lost owner by a model that waits for a later input; (c) the last failed owner, where J reaches `MaxAttempts` at that takeover itself; (d) a cold takeover with no control kind in the inbox. Outcome: in every run R's trial comes first and alone, before J takes the poison path. In (a), (c) and (d) the model consumes R and F completes once; then the drain completes, or the mark's read finds no offerable cohort row, and J takes the `MaxAttempts` path, which re-parks the instance while the drain is open and parks it under a new Park after Complete, with J `Consumed`. In (b) the trial hands R back to the model, which holds it again, the mark's read passes the held R, and J's poison parks the instance, which keeps R and J `Consumed`, so no restored row is lost and a later recover delivers R again. Executed in this repair: run (a) as `po1-open` (the drain open, its last control pending) and run (d) as `po1-cohort` (a cold takeover after Complete with the mark set and no control kind), each under the reviewed, repaired and negative rules, the reviewed rule leaving F at zero completions in both (#4790). Run (b), which needs R held (PO-1's R is a Native timer, never held), and run (c) on its own, J reaching `MaxAttempts` at the takeover before R's trial, are proposed, qualified at K3. Mutation (the reviewed rule): `PrecedesPoison` in plain inbox order; R's trial is never selected, J waits behind the drain or the mark, and F completes zero times. Two cohort rows that share a `seq` are compared by the complete tuple (TT-1, sixth review R6-M6) | K3, K4 |
| PP-2 | **Cohort progress and the ordinary earlier poison** (R5-M6). Preconditions: for (a), PP-1's fixture (J exhausted at `seq` 900, R restored at 1100 for frame F); for (b), a running instance with no Park, no drain and no `Cohort` mark. Interleaving: (a) a cold takeover after Complete with the mark set and R and J both suspects; (b) the owner polls E and S, E having reached `MaxAttempts` and S marked by a lost owner. Runs: (a) cohort progress, then a successful Park: PP-1's run (d) taken after Complete, where the model consumes R, the mark's read finds no cohort row and the mark clears, and J's poison parks the instance, a Park that keeps J `Consumed` so that a later `retry` can select it and a `resume` restarts it under the next generation; (b) no drain and no mark: an exhausted input E at `seq` 900 and a later suspect S at 1100 of one instance, with a pending job ticket on the same slot. Outcome: (a) R's trial runs first and alone, the model consumes R and F completes once, the mark clears, and J's poison then parks the instance with J kept `Consumed`, its attempts counted once; (b) E poisons first and the instance parks before S's trial, as built, and no physical job or job trial starts on the slot while E's poison is due (`Worker.cs` 753–754). Executed in this repair: run (a) as `po1-cohort` (R tried first and alone, F completes once, the mark clears, then J's poison parks the instance) and run (b) as `po1-ordinary` (the exhausted J parks the instance before R's trial, as built, the same history also run on a real `Worker`), each under the reviewed, repaired and negative rules (#4790). (a)'s later `retry` and `resume`, (b)'s pending job ticket on the slot, and this mutation are proposed, qualified at K3. Mutation: the cohort order applied with no drain or mark; S is tried before E, against the built order. TT-1's run (c) holds the built comparison for a tie outside the cohort context | K3, K4 |
| TG-1 | **A metadata-bearing live terminate at the envelope** (sixth review R6-B1). Preconditions: a running Native child A of P with one registered running child G and 3,500 open empty invocation inboxes with matching frames; a test model with no terminal growth as the control. Interleaving: (a) a turn that would bring A's state to the review's witness, 16,436,864 bytes; (b) A at the largest state the check admits with no retained handle, 16,416,449 bytes, which the projection takes to exactly 16,457,728 (24,500 terminal growth, 384 for the one child, the 16,384-byte reserve and the 11 bytes of this fixture's encoded `Model` and `Definition`), and, as a deliberately refused input, the sixth round's 16,416,460 bytes, which projects 11 bytes over (eighth review R8-m1); then P's terminate. Outcome: (a) the turn fails and parks as an over-envelope turn and its state never commits; (b) at 16,416,449 the state is admitted and the terminate commit lands within 16,457,728 bytes, A ends Cancelled with its inboxes closed, G receives its terminate in that commit, the first counted child send, and P receives one Cancelled notice; the turn that would bring A to 16,416,460 fails and parks as in (a); the control model admits the same states as before. Run (c), the handle-accumulating run (seventh review R7-B1): from the same boundary state with G registered, A issues 1,500 detached starts in three fenced commits of 500, each within the send cap and each leaving its creator handle in `CustomHandles` with no registry entry, then (c1) a growth turn to the review's state, 16,416,460 bytes, or (c2) one to the largest state the check admits with the handles, then P's terminate; outcome: in (c1) the growth turn, whose projected terminal row with `CustomHandles` at its encoded size exceeds the envelope, fails and parks, A keeps its last admitted state, and P's terminate reaches the parked row and stores G's terminate; in (c2) the terminate commit lands within 16,457,728 bytes and stores G's terminate, the first counted child send, and A ends Cancelled with its handles kept; G ends Cancelled in both; mutation: handles not counted (the sixth round's check), so the growth commit lands, the ending row exceeds the cap (10,599 bytes over in NT-2's JSON UTF-8 measure, 10,448 in the review's BSON, both qualified at K4) and no child terminate is stored. Negative: the check without `TerminalGrowth`; (a)'s state is admitted and the terminate commit exceeds the envelope on every attempt, so G is never terminated. Run (c) is executed in this repair (`experiments/Review7.Blockers`, #4803): NT-2's `nt2-handles` (c1), `nt2-at-bound` (c2) and the control `nt2-no-handles` run it with the real `Turn.cs` detached starts, real commits and the real `WorkflowInstance` terminate under the reviewed, repaired and negative rules, measuring the row's fields in JSON UTF-8; BSON and SQL Server's UTF-16 are qualified at K4 (gate 2 condition [C1](../program-goals/clean-slate-kernel.md#gate-2-conditions)). `nt2-no-handles` under the repaired rule is run (b)'s admitted side: it admits 16,416,449 bytes of state at exactly 16,457,728, and its ending row measures 16,441,315; the refused side at 16,416,460 is that projection's arithmetic, and is proposed as a run at K3. Executed in this repair (`experiments/Review6.Blockers`, #4798): NT-1's `nt1-witness` (run (a)), `nt1-at-bound` (run (b) under the sixth round's check, which admitted 16,416,460) and `nt1-no-inboxes` (the control) under the reviewed, repaired and negative rules, and `nt1-growth`, which measures Native's bound at 7 bytes per open invocation inbox and a conservative 23 per non-final receipt (SQL Server's UTF-16 doubles these, derived); the provider encodings, SQL Server's UTF-16 included, are qualified at K4, and an authored Native workflow at K6 | K3, K4, K6 |
| TG-2 | **A protected `ThenDelete` at the envelope** (R6-B1). Preconditions: TG-1(b)'s A at its admitted 16,416,449 bytes (eighth review R8-m1), protected by `ProtectsDeletion`; a second run with `P_cascade` + 1 registry entries at the same bound. Interleaving: the operator's terminate with `ThenDelete`. Outcome: A is terminated and kept, its live row fits the envelope, in the second run with its `Discharging` obligation, and every descendant ends Cancelled. A third run (seventh review R7-B1): TG-1(c)'s handle-accumulating A, protected, under the operator's terminate with `ThenDelete`; the live-row write fits and stores G's terminate, the first counted child send. Negative: the bound removed, with A at the largest state its own check admits; the live-row write is refused on every attempt. Mutation for the third run: handles not counted; the live-row write is over the cap and no child terminate is stored. Executed in this repair (`experiments/Review6.Blockers`, #4798): NT-1's `nt1-thendelete-protected`, at the sixth round's bound; the second run is proposed, qualified at K3; the third run is executed in this repair (`experiments/Review7.Blockers`, #4803), NT-2's `nt2-thendelete-protected` | K3, K4, K6 |
| CJ-1 | **An ordinary cancel of a converted job** (sixth review R6-M5). Preconditions: a Native instance with background job J at g1 and an earlier input X whose turn drops J's wait; J finishes while the instance is suspended, and the conversion commits (ticket consumed, `/result` pending). Interleaving: (a) resume, and X's turn cancels J; (b) a takeover before X's turn; (c) a later turn re-arms J; (d) the control: J's ticket still pending; (e) in an open recover drain, J's current-generation result row is kept `Arrived` on a later page, an earlier released X cancels J, and the page then restores the row. Outcome: X's commit consumes X and the result row as discarded, adds no consume for the absent ticket, writes the row without J and lands; the model never receives J's result; in (b) the new owner decides the same under its epoch; in (c) the re-arm takes g2 and J runs once more; in (d) the ticket is consumed and cancelled as built; in (e) the restored row matches no registry entry and is consumed as discarded, never delivered. Negative: `CancelJob` as built; the store refuses the batch with "consumed message … is not there" on every attempt, and X and the result stay pending. Mutation: the cancel leaves the result row; the model later receives a result for a job it cancelled. Mutation: in (e) the restored row is delivered to the model. Proposed, qualified at K3 through `DurableTurn` and `WorkflowInstance`, at K4 on every count-enforcing provider and at K6 with Native | K3, K4, K6 |
| CJ-2 | **A running turn consumes a result row** (R6-M5). Preconditions: a running Native instance with J at g1 and another job K; J's `/result` row is delivered to a model turn. Runs: (a) the model consumes it; (b) the model holds it, then consumes it later; (c) after (a), the model re-arms J; (d) J was cancelled and re-armed at g2 before g1's result row arrives. Outcome: (a) J is removed before the model turn and the commit writes no J; (b) the row keeps (J, g1) until the consuming turn; (c) the re-arm takes g2; (d) g1's row is consumed as discarded, never delivered, and (J, g2) and K stay. Mutation: `CompletedTicket` as built; J stays registered and (c)'s re-arm throws "in flight already", so the turn fails and parks | K3, K4, K6 |
| TT-1 | **Tied sequence numbers under cohort precedence** (sixth review R6-M6). Preconditions: cohort rows R and Z of one instance share `seq` 42 (injected at K3; MongoDB's independent sender clocks at K4), R first by its full tuple; R was held before and is a suspect after the takeover, and Z is exhausted. Runs: (a) a cold takeover after Complete with the `Cohort` mark set; (b) the reversed tie, Z first by its tuple; (c) the non-cohort job control: an exhausted job ticket and a suspect at one `seq`, with no drain and no mark. Outcome: (a) R's trial runs first and alone, the model consumes R, then Z takes the poison path; (b) Z parks the instance first and R is kept for a later recover; (c) the built comparison holds and the ticket poisons first. Mutation (the reviewed rule): `seq` alone between cohort rows; in (a) R is never tried, and delivery either stops at R or the instance parks on Z before R's turn | K3, K4 |
| KP-1 | **A partially published event across a Park** (sixth review R6-M7). Preconditions: instance W publishes E on key k with 2 × `P_page` + 1 subscribed waits; the publish ticket commits its first page, then its step fails until W parks, and Park keeps the ticket `Consumed`. Runs: plain `resume`; `retry` naming another row; `retry` naming the ticket; retry-all; discard; expiry; terminate; delete; a takeover after the restoring page in the `resume` run; an ended-model run, where the ticket lies beyond the first page of a plain `resume`, the model ends on page 1 and page 2 reaches the ticket; and a meet run, where a keeping wait's meet ticket is kept `Consumed` by its instance's Park, under `resume` and under discard. Outcome: in every recover mode the ticket is restored with its committed cursor and every wait receives E exactly once; in the ended-model run page 2 restores the ticket and every wait after the cursor receives E once; under discard, expiry, terminate and delete the ticket becomes a dead letter that shows its undelivered remainder, the waits after the cursor, and its replay delivers E to those once while the event row exists, and otherwise answers "gone", while the first page's waits keep their deliveries; in the meet run the kept meet ticket is cleared under both modes and adds no delivery. Mutation (the reviewed rule): the page clears the unselected `Consumed` ticket; the waits after the cursor never receive E and nothing shows it. Mutation for the ended-model run: page 2 makes the ticket a dead letter after the end; the next sweep removes the event row, and the waits after the cursor never receive E. Negative for discard, expiry, terminate and delete: the page clears the ticket; no dead letter shows and the remaining waits never receive E | K3, K4, K11 |
| WJ-1 | **No job entry during a terminate drain** (sixth review R6-M8). Preconditions: a recovering instance A, Running with its recover drain open, holds `P_cascade` + 1 = 1,001 pending job tickets; an operator's terminate interrupts the drain. Runs: (a) a cold owner B takes the partition after the Open's first page; (b) a driver callback queued on A before the Open reaches entry after it; (c) the Open's commit is in flight, and once it fails to land; (d) a driver entered before the Open. Outcome: (a) B stands ticket 1,001 down, page 2 consumes it and Complete ends A Cancelled; (b) entry is refused under the slot lock; (c) entry is restricted while the commit is in flight, and the failed Open releases the restriction with the recover drain still open; (d) the entered driver is cancelled by the first page, as built; in no run does a job body enter after the Open's acceptance. Mutation: `Withholds` as built; in (a) B enters ticket 1,001's body before page 2 | K3, K4, K6 |
| HC-1 | **Controls win before any model work** (fifth review R5-M3). Preconditions: a parked Native instance with 4 × `P_page` = 680 kept `Arrived` rows from one sender; a `resume`; a fixture that gives every `kernel.drain` priority over model turns. Interleaving: after each page the owner polls and marks its rows `Seen = e`, and the next `kernel.drain` runs before any model turn. A second run makes ten of the kept rows 1 MiB payloads each, so the byte credit binds before the row credit; a third places one row of 16 MiB, at the row cap, among them; a fourth run has the model hand off only some rows of a page before the gate row (the fixture holds the rest as suspects at a new owner), so a page is admitted with u > 0; a fifth has the credit cut a page to zero when no kept row remains to be read. Outcome: the second page waits, its control sent again with a delay and nothing restored, until the first page's gate row is handed off; an admitted page restores at most `P_page` − u rows and stops at the byte credit; at no time are more than `P_page` restored rows, or more than `B_credit` stored bytes with payloads counted, not handed off, except one row larger than `B_credit` that a page restored alone with nothing pending, and `elsa.kernel.drain.pending` and its byte gauge equal the fixture's measured count and stored bytes of the rows not handed off at every sample: in the second run at most three of the 1 MiB rows (each 1 MiB plus its metadata) are pending at once, and in the third the 16 MiB row is restored alone by a page with nothing pending, and the next page restores nothing until it is handed off; a page cut to zero restores nothing and waits, and only a later admitted page that reads no row completes the drain; every row is delivered once, in per-sender order, and the drain completes. Executed in this repair: the first run as `fr2-base` (three full pages, 510 kept rows, in place of four, the reviewed rule leaving 510 rows pending at the first model handoff), the second and third in one as `fr2-bytes` (ten 1 MiB rows, then the 16 MiB row alone), the fourth as `fr2-hold-takeover` (a page admitted with u = 5 after a takeover that loses five holds) and the fifth as `fr2-zero-cut` (a page cut to zero by a 4 MiB held row lost at a takeover), each under the reviewed, repaired, negative and `seen-gate` rules, with `p-page-whatever-u`, `credit-metadata-only` and `zero-cut-completes` as the second, third and fourth mutations (#4790). The harness asserts its own counts of the pending rows and bytes and of the held rows; the gauges' equality with those counts, per-sender order and the four-page fixture are proposed, qualified at K3. Mutation (the reviewed rule): the gate counts `Seen = e`; all four pages are restored before the first model turn and 680 rows are pending. Mutation: an admitted page restores `P_page` rows whatever u; in the fourth run more than `P_page` rows are pending. Mutation: the byte credit counts metadata only; in the second run all ten 1 MiB rows are pending before the first handoff. Mutation: a page cut to zero completes the drain; Complete runs with rows still kept | K3, K12 |
| HC-2 | **Hold, suspension, trial and takeover under the credit** (R5-M3). Preconditions: HC-1's parked instance with 680 kept `Arrived` rows and a `resume`; a test model that can hold rows (a StateMachine during a suspended action at K6); the fixture gives every `kernel.drain` priority, as in HC-1. Runs, each with its own interleaving: (a) the model holds every restored row as it receives it; (b) an operator suspends the instance after page 1 is restored and before its rows reach a model turn, and resumes it later; (c) a takeover between pages 2 and 3, after the lost owner marked page 2's rows and before any of them reached a model turn; (d) an unknown commit outcome on a model turn after page 2, so the owner resets and retakes the partition, as (c); (e) a page restores a `Consumed` job ticket under `retry` while a K3 test hook holds the job runner, so the owner starts it as a job only when the hook releases the runner, several pages later, with no Park and no poison path involved; (f) a Hold and a takeover together: the model holds all 510 rows of pages 1 to 3 at the lost owner, then a takeover comes before page 4; (g) the same with bytes: the model holds ten restored rows of 1 MiB payload each at the lost owner, then a takeover; (h) two takeovers in a row: after (g)'s takeover, the second owner is lost before it hands off any row, and a third owner takes over; (i) the model holds ten restored 1 MiB rows, a page with nothing pending then restores one 16 MiB row alone, and a takeover comes before that row reaches a model turn. Outcome: (a) every page is admitted once its gate row is held, `elsa.kernel.drain.held` reports the held rows and the pending gauge stays at most `P_page`; (b) no page is admitted from the suspend to the resume, page 1's rows withheld by the suspension stay counted as not handed off throughout, and after the resume pages are admitted again as those rows are handed off; (c) the new owner counts page 2's rows as pending, so page 3, once its gate row is handed off, restores only 170 − u rows, none while all 170 are pending, and the pending count never exceeds 170; (d) as (c); (e) the ticket counts as handed off once restored, before it starts, so the pages between its restore and its start are admitted as if it were absent; (f) the new owner counts the 510 held rows as pending, so u is above `P_page`, admits no page and restores nothing while u ≥ 170, hands the held rows off again as the model re-holds them, admits page 4 only once u < 170, and the restored rows not `Done` never exceed `P_page` plus the 510 held rows; (g) the new owner counts the ten rows' bytes as pending, above `B_credit`, so pages restore nothing while b ≥ `B_credit`, and the restored bytes not `Done` never exceed `B_credit` plus the ten held rows' bytes, that upper bound being the oracle, while the byte gauge equals the measured stored bytes of the rows pending at each sample, in (g), (h) and (i) alike; (h) the third owner again counts the ten rows, and their bytes, as not handed off and admits no page until it has handed them off, and the same bounds hold, since the ten rows are still rows the model has held and not consumed; (i) the new owner counts the ten held rows and the 16 MiB row as pending and admits no page until both counts are under the credit; the restored bytes not `Done` stay within `B_credit` plus the ten held rows' bytes plus the one 16 MiB row, and the byte gauge equals the measured stored bytes of the rows pending at each sample, that sum before the handoffs; in every run the drain completes with each row delivered once. Executed in this repair: run (b) as `fr2-suspend`, with `pages-while-suspended` as the second mutation, and run (c) as `fr2-takeover`, each under the reviewed, repaired, negative and `seen-gate` rules; run (a) in part as `fr2-hold`, where the model holds five restored rows, page 1's gate row among them, with `held-gate-waits`; and smaller forms of runs (f) and (g) as `fr2-hold-takeover` (five holds lost at a takeover, so u = 5) and `fr2-zero-cut` (one held 4 MiB row lost at a takeover cuts the next page to zero) (#4790). Runs (d), (e), (h) and (i), runs (a), (f) and (g) as stated, the byte gauge, and the first and third mutations are proposed, qualified at K3. Mutation: the credit survives ownership loss (the new owner trusts the lost owner's handoffs); in (c) page 3 restores 170 rows while 169 are pending at the new owner, 339 in all. Mutation: suspension counted as a handoff, with the suspend wait dropped; in (b) pages restore through the suspension and every kept row is pending at the resume, above `P_page`. Mutation: a ticket kind counts as pending until it starts; in (e) the delayed job ticket holds u at one and its bytes in b until the hook releases the runner, so the pages between are cut by one row each, and the assertion that they restore as if it were absent fails | K3 |
| HC-3 | **What the credit shows and costs** (R5-M3). Preconditions: HC-1's drain, and HC-2's runs (f), (h) and (i), beside a second instance B of the same partition with steady input of its own. Interleaving: the gauges are sampled once per poll while the drain's pages wait and B's turns run in the same passes. Outcome: the pending row and byte gauges and the held gauge equal the fixture's counts at every sample; the pending row gauge plus the held gauge never exceeds `P_page` plus the restored rows the model holds or has held and not since consumed (`P_page` in HC-1's run, `P_page` plus 510 in HC-2's run (f), and the same across HC-2's two takeovers in run (h)), and the byte gauges never exceed `B_credit` plus those held rows' bytes plus one row larger than `B_credit` restored alone (HC-2 run (i)); B keeps its turn share within the pass budget while the drain's pages wait, and the share is recorded; K12 measures the owner's memory for the drain at the intended holding models, restored kernel tickets included. Executed in this repair, in part: every FR-2 run asserts the harness's own peak of pending rows and bytes and its held count (HC-1's drain as `fr2-base`, held rows across a takeover as `fr2-hold-takeover` and `fr2-zero-cut`), and reports a neighbour instance's turns as a count, unasserted, since one turn per instance per pass holds the share by construction (#4790). The gauges themselves, HC-2's runs (f), (h) and (i), B's asserted turn share and this mutation are proposed, qualified at K3, and the memory at K12. Mutation: a gauge that omits rows polled and not handed off; it reads zero in HC-1's run while 170 rows are pending | K3, K12 |
| RA-1 | **The two pending `Rearmed` branches** (R4-m3). Preconditions: a Native frame F waits on T; a retry re-arms T (`Rearmed`); a re-park follows; T fires while parked and is kept `Arrived`; a resume of generation g opens, and a page restores T with (c, g); a further interruption comes before T is consumed, and the fixture keeps T unconsumed while parked; the next resume, of generation g + 1, opens while T is pending with the older tag, and a retry in a second run names T; an unreleased model-ending arrival A with a lower `seq` than T is pending. Outcome: the retry counts T "already re-armed"; a page of g + 1 restores T through the `Stale` arm with (c, g + 1), in tuple order within the cohort; T is delivered during the drain, before A, and F resumes once. Control run: a T the g + 1 Open itself re-armed (pending, null tag) is withheld until Complete and is never a gate row, as in S-7's first run. Mutation: every pending `Rearmed` timer waits for Complete (the page skips it); after Complete T is an ordinary row, A goes first and ends the model, and F resumes zero times | K3, K6 |
| PS-1 | **Distinct targets of one tenant** (R4-M5). Preconditions: tenant t; A under caller key K1 and B under K2, each on creation 1; both park at first Park revision 7 at the same pinned `ParkAt`, so the creation and both mark components are equal, which the test asserts. Interleaving, each in its own run: a discard, a resume with no handles and a retry-all, each prepared from A's `ParkSelection` (read once from `ParkedItem` and once from a `KeptAsync` page), are sent with B as the request's target; a control run sends each to A. Outcome: at B each is refused (reason target) before the creation and mark checks; B's kept rows and reasons, status, mark, waits and parent notices are unchanged, and the refusal is recorded under the operation id; at A each acts. `ParkedItem` and every `KeptPage` selection carry `Instance`, and the opaque handle passes it through the operator API unchanged (K9, K11). Mutation: drop the `Instance` check; at B the discard clears B's kept rows and sends B's parent a Faulted notice, and the resume and retry-all recover B | K3, K9, K11 |
| DT-7 | **The retirement cascade, in both orders** (R4-B2). Preconditions: P-7's fixture, P waiting on child A, parked at r1 with more than `2P` kept rows; A has a live child G, itself with a child G2, and an in-flight job J; P, A and G on three partitions. A's unprotected parked-instance delete drain is open, and in a second fixture an operator delete naming r1 has taken over a discard drain. P is terminated while A's drain is open, so its tree terminate to A commits while A exists. Order (a): the fixture holds that cross-partition send until after A's Complete has written the Faulted notice and the retirement marker. Order (b): it is delivered between two pages, before Complete. Variant: G itself has been deleted while G2 lives, so the cascade reaches G's marker. Outcome: (a) after Complete the marker holds P, P's creation, {G} and {J}; the delayed terminate is refused as `RetiredCreation`, and in the same commit the marker sends G a terminate from A, cancels J and clears its retained registry and job keys; (b) the terminate is refused as "resolution open" with P-7's cascade, and Complete leaves a marker that retains nothing. In every run G and G2 end Cancelled (in the variant, G's marker cascades on to G2), J's ticket is cancelled and its late result refused at the marker, G's notice to A is refused there, P has received exactly one Faulted notice and nothing more, and invariant 17 holds for P's tree. Executed in this repair: `rt1-a` and `rt1-a-g-deleted` for order (a), which fail under the reviewed rules and pass under the repaired ones, and `rt1-b` for order (b), which passes under both, since #4756's open-drain cascade already covers it (#4781). Mutation (the reviewed rule): the marker keeps nothing, as `Retire` is built; in order (a) G, G2 and J survive and invariant 17 fails | K3, K4, K6 |
| DT-8 | **Only the parent discharges the obligation** (R4-B2). Preconditions: DT-7's order (a), up to the marker. Interleaving, each in its own run before P's terminate arrives: an application send of `kernel.terminate` to A from a non-parent; a terminate from an earlier creation of P; an outside add naming P as sender; an operator's terminate carrying A's selection. A contrast run: the same operator's terminate, delivered while A's delete drain is open, is refused as "resolution open" and cascades (J-1, P-7), so G and G2 end Cancelled; delivered after Complete it is refused at the marker as `RetiredCreation` with no cascade, and the accepted difference is asserted as stated in "The retirement marker keeps the obligation after a delete". Outcome: each is refused as `RetiredCreation` with no cascade; the marker's retained registry and job keys, G and J are unchanged; P's delayed terminate then discharges the obligation as in DT-7. Executed in this repair: `rt1-nonparent`, with the `cascade-any` mutant. Mutation: cascade for every terminate refused at the marker; the non-parent's send terminates G | K3, K4 |
| DT-9 | **No successor takes the obligation** (R4-B2). Preconditions: DT-7's order (a) marker for A, a generated child id retaining {G}. Runs: (a) real ingress: a start naming A's id under the next creation is injected as an outside add; (b) below ingress: a fixture hands the creation protocol a successor start for A's id under the next creation directly, bypassing ingress; (c) a keyed root R under caller key K, with a live child H, is deleted; its marker records no parent and retains nothing; B starts under K with the next creation, starts a child H2, and an operator terminates B. Outcome: (a) the start is a supplied creator at a retired generated id and is consumed with no change (existing prototype behaviour, `Messages.cs` 52–54, `Turns.cs` 112, `Worker.cs` 1030–1037), so the successor is unreachable from real ingress; (b) the creation protocol refuses the successor with reason `ObligationOutstanding` (proposed defence in depth), writing nothing; in both, the marker and its obligation are unchanged and P's delayed terminate still discharges it, G and G2 ending Cancelled; (c) B's start is admitted, since R's marker holds no obligation; B's terminate cascades to H2 only, H keeps running, as delete semantics give, and H's notice to R's creation is refused at B as `WrongCreation`. Executed in this repair (#4781): (a) `rt1-successor-unreachable`; (b) `rt1-successor-gap`, with the `successor-admitted` mutant; (c) `rt1-successor`, with the `successor-inherits` mutant. Mutation: successor admitted despite obligation; in (b) the successor replaces the marker, P's terminate meets it, is refused as `WrongCreation` with no cascade, and G survives. Mutation: the successor keeps its predecessor marker's registry; in (c) B's terminate reaches H | K3, K4 |
| EM-1 | **A real event route at the name limits** (the fourth review's R4-M1). Preconditions, per provider (fifth review R5-m1): a tenant id of 64 characters; an instance whose caller key, a subscription key, a wait and an event id are the provider's largest jointly admissible names, made of U+0001, the widest JSON escape, and in a second set of `<`. Where the full set is admitted (the in-memory store at K3, SQLite and MongoDB) each name is 1,024 bytes. On a narrower provider the set is the largest that admission accepts together there, counting each column's width, the composite keys `Subscriptions (Key, Instance, Wait)` and `Events (Key, Id)`, and the maximum stored form of every id the instance issues (its origin with a prefix and suffix of up to 58 ASCII characters at a 3-digit unit count): on SQL Server, within 450 UTF-16 units, a raw subscription key, caller key and wait of 105 units each (315 together, the subscription key's room with a caller-key instance, [tenancy](tenancy.md#names-one-namespace-per-tenant)) and an event id of 280 (385 with the raw key), and a caller key of at most 322 units, since every id the instance issues is its caller key plus 128 characters (`Q` and `{vp}/`, 70, and the issued id's prefix and suffix, 58), so caller-key admission applies the issued-id bound; on MySQL, within the 255 characters of `Messages.Id`, a caller key of at most 127 by the same bound, and the composite widths recorded at K4; on PostgreSQL, the B-tree entry limit of about 2,704 bytes per key, which three 1,024-byte names exceed in the subscription key (3,207 bytes with `Q` and `{vp}/`), so the three share that limit. The SQL Server and PostgreSQL figures are derived from the declared widths and limits, unmeasured, and measured at K4. The fixture derives each set from the admission rule it tests and asserts that one more unit in any one name is refused at the source. The refusal controls are per operation, each at the first excess its own producer can reach (sixth review R6-m1), each refused before any write: the caller key at `N` + 1 bytes, or past 322 units on SQL Server and 127 characters on MySQL; `Subscribe`'s key or wait at `N` + 1 bytes, or one unit past the composite room on SQL Server, and on PostgreSQL past the B-tree limit, which three 1,024-byte names exceed; the outside publish's key or event id at `N` + 1 bytes, or one unit past 385 together on SQL Server. On PostgreSQL an outside publish cannot reach the B-tree limit (two 1,024-byte names with the qualification are 2,113 bytes), so its control there is `N` + 1. MySQL's composite points and the exact PostgreSQL figure are measured at K4. An outside publish carries its payload raw, so the payload runs below are raw bytes. Interleaving, each in its own run: (a) the model subscribes, then an outside publish lands; (b) the publish lands with a keep window, then the model subscribes with keep, so the catch-up adds the delivery; (c) the publisher and the registrar race, with an ownership takeover between the publish's insert and its match read; each run once more with the instance parked, the delivery kept and then restored by a recover drain. Outcome: each run stores exactly one delivery row, with a 64-byte `Match` and a 67-byte `Id`; `Events.Delivered` returns the event id and the wait from the payload's header; the payload the model receives is byte for byte the published one (a payload of quotes, `<`, backslashes and U+0001 in each run); two further runs of (a) publish with no payload, as Native's `Publish` node without a payload binding does, and with an empty payload, and the model receives null and the JSON string `""` respectively; the model resumes the wait once; the row's encoded metadata is at most `M` and its page within `B`; no store write is refused. Mutation: today's `EventMatch.Encode` and `evq:` id; the derived row's `Match` is 24,787 bytes and its `Id` 3,179 (measured by the size probe), so the composed-row check refuses it, or the key width does on SQL Server and MySQL, after the publish was accepted, and the wait never receives the event | K3, K4 |
| EM-2 | **Refusal at the source boundary** (R4-M1). An outside publish and a turn's `Publish` whose payload is at the first attainable excess over 16 MiB less `M`, `F`, `C` and `I` (16,621,568 bytes), counted in the provider's stored encoding, are refused before any write: the outside publish by a validation error with no event row, the turn's by a model fault. The first attainable excess is one byte where the payload is stored as UTF-8 and one UTF-16 unit, 2 bytes, on SQL Server, whose limit is 8,310,784 units (sixth review R6-m1). The turn's runs use two producers, kept distinct: a direct `ITurn.Publish` producer, a test model passing raw payload bytes, for the raw controls (raw U+0001, an empty wire payload); and a real Native `Publish` node, whose payload is the bound value's `ToJsonString()` (`NativeModel.cs` 548), which writes `<` and U+0001 as six-byte escapes and an empty string as the two bytes `""`, so its fixture is sized after serialization. At exactly that size, with a payload made of escapable characters (quotes, backslashes, `<` and, from the raw producers, U+0001), each is accepted, and its delivery, built at publish and, in a second run, at catch-up, is admitted with its header at the provider's largest jointly admissible names (EM-1's sets, fifth review R5-m1), delivered once with the payload unchanged and within the 16 MiB row cap; the turn's stored publish ticket on that path is EM-4's. A start-subscription run: a definition registers an event-start subscription whose `Start.Input`, made of escapable characters (quotes, backslashes, `<` and U+0001), is exactly `I` in the provider's stored encoding, and the at-limit publish then starts an instance whose delivery, `Payload` and `Start.Input` together, is within the row cap; a registration with an input at the first attainable excess over `I` (one byte, one UTF-16 unit on SQL Server) is refused at the registrar with no subscription row. A subscription key or wait one unit above the provider's limit (`N` + 1 bytes where the full set is admitted) is refused at `Subscribe`, at a turn's `Publish` (its key) and at the registrar, and an event id of that size at an outside publish, while names at EM-1's sets are accepted; a turn's `Publish` names no event id, since it generates one (R5-M1). On SQL Server a turn's `Publish` whose raw key and caller key exceed 256 units together (a derived, unmeasured figure, measured at K4; 257 before the seventh review's reserved `~`, R7-M1) is refused as a model fault, since the generated id would not fit `Events (Key, Id)`, and one at 256 is accepted and its event row stored. Mutation: no `C` reserve; the at-limit publish is accepted, its delivery is refused at the composed-row check, and an accepted publish never reaches its wait. Mutation: no `I` reserve or no input bound at the registrar; the start delivery passes the row cap and the accepted publish starts nothing | K3, K4 |
| EM-3 | **One delivery from both sides, under equivalent spellings** (R4-M1). Preconditions: on SQL Server and MySQL under the pinned collation, a subscription on `Q(t, Order-7)` and a publish on `Q(t, order-7)`, equal under the pin, raced against the restore's catch-up; on PostgreSQL, SQLite and MongoDB the same spellings as a control; at K3 the in-memory store's equality hook folds case (as for KB-1). Outcome: where the pin folds case, the publish matches the stored subscription, both sides compute the digests from the persisted event and subscription rows, and exactly one delivery exists; on the control providers the spellings are two keys and nothing matches. Two waits of one instance on one key get two deliveries with one `Match` and two ids, and one wait subscribed under two keys, with the same event id published once on each, gets two deliveries with two ids and two `Match` values, since each event row has its own incarnation (seventh review R7-m1; B2-3 builds a shared wire id at the store level). Mutation: compute the digests from the caller's spelling; the publish side and the catch-up side write two deliveries for one event and wait, and the wait runs twice | K3, K4 |
| EM-4 | **A turn's publish through its stored ticket** (the fifth review's R5-M1). Preconditions, per provider, a jointly admissible origin and publish key (sixth review R6-m1): where the full set is admitted and on PostgreSQL, a caller key of `N` = 1,024 bytes, so the generated event id, with its reserved `~` (seventh review R7-M1), is at its origin limit (1,154 bytes), and a raw key of 1,024 (on PostgreSQL the `Events (Key, Id)` entry is then 1,089 + 1,154 = 2,243 bytes, inside its limit); on SQL Server, a caller key of 192 units and a raw key of 64, 256 together, the positive point inside the joint bound (EM-1's 322-unit caller key leaves no room for any raw key); on MySQL the point its recorded composite width gives (K4); a second instance subscribed on the key without keep. Two producers, kept distinct: a real Native `Publish` node, whose payload is `ToJsonString()` of the bound value (`NativeModel.cs` 548), sized after serialization as in EM-2 (a string of `<` with quotes and backslashes); and a direct `ITurn.Publish` producer, a test model passing raw payloads at the source limit, one of `<` and one of U+0001, each with quotes and backslashes. Interleaving: a real turn runs the producer; its commit stores the publish ticket; the Worker takes the stored ticket after the commit; in a second run the owner is lost after the commit and a new owner decodes the stored ticket; each at exactly the source limit and at its first attainable excess, one byte, or one UTF-16 unit on SQL Server, counted in the provider's stored encoding. Outcome: at the limit, the stored ticket's `Payload` is the ticket header, at most 2,279 bytes (4,558 in SQL Server's UTF-16), followed by the payload bytes unchanged, and after the first step's rewrite, with the version-2 incarnation part and no payload, at most 2,314; the ticket row is within the 16 MiB row cap; the Worker decodes the event row with the payload byte for byte and its event id equal to `~` followed by the ticket's id (seventh review R7-M1); the delivery header is at most 3,320 bytes (6,640 in SQL Server's UTF-16), the fifth round's 3,284 plus version 2's 35-byte incarnation part and the reserved `~`; the wait receives the published payload once; at the first excess the turn's `Publish` throws `MetadataLimitExceeded` as a model fault, and no ticket, event row or delivery exists; on SQL Server a raw key of 65 units beside the 192-unit caller key, 257 together, is the first refused point of the joint check, refused at `Publish` as a model fault. Further runs: the Native node with no payload binding, whose wait receives null, and with an empty string value, whose wire payload is the two bytes `""` and whose wait receives `""`; the direct producer with a null payload and with an empty wire payload of zero bytes, whose waits receive null and `""` (`Values.Parse("")`, `Authoring.cs` 337–342). Mutation: today's ticket, `Json.Write` of an `EventRow` (`Turns.cs` 327–335, 429–433); the direct producer's raw `<` run's ticket payload is about six times the event (the review's witness: 99,729,457 bytes for a 16,621,568-byte event), the commit refuses the over-cap row as a definite error, the turn fails although its `Publish` was accepted, and the wait never receives the event. Mutation (K4, SQL Server): no joint key check at `Publish`. The check reserves the generated id's maximum, while a live id carries fewer digits, so 257 units need not meet the physical refusal; the negative control therefore sizes the raw key from the live generated id, read back from the stored ticket, so that the event's key is one unit past 450: the ticket commits, the event insert is refused by `Events (Key, Id)`, and an accepted publish never reaches its wait | K3, K4 |
| SB-1 | **A subscription-heavy model** (the fourth review's R4-M2). Preconditions: a Native workflow whose `Event` activities subscribe distinct short waits; a second one whose `Event` activities ask for kept events; a third in which they are mixed with `Delay` timers. Interleaving: each subscribes past the bounds over many turns, then parks. Outcome: with subscriptions only, the 3,511th subscription faults the activity that would subscribe it, by bytes, whatever the length of its names, since each counts its fixed 7,168 bytes; with subscriptions that keep events, each counts 9,728 bytes (its insert, its coverage entry and its MongoDB conflict write) and the meet ticket's `M` + `F` + `C` is charged once, so the 2,578th faults (sixth review R6-M2); in the mixed workflow, with 1,000 subscriptions and the rest timers, the `W_max` + 1st wait faults its activity by count; each plan stays within both bounds, and a later recover restores every subscription once in its Open, by plain insert. With the model-side check skipped, a plan of 3,511 subscriptions, one of 2,578 that keep, and a mixed plan of `W_max` + 1 waits each refuse the recover with reason "plan" and no change, and a discard then lands. Mutation: count timers and jobs only; 65,536 subscriptions reach the Open, which writes all of them in one commit, and the assertion on the Open's counted work (at most `W_max` waits and `B_plan` bytes) fails. Mutation: leave the meet ticket out of the count; the 2,578th keeping subscription is admitted and that plan's counted bytes exceed `B_plan`. Mutation (R6-M2): charge a keeping subscription its insert alone, as the fifth round did; 3,498 keeping subscriptions are admitted, and the Open's stored writes, measured in SB-3, exceed `B_plan` | K3, K6 |
| SB-2 | **Catch-up after the Open** (R4-M2). Preconditions: a parked instance whose plan restores subscriptions, some with keep and some without, on keys with more than 2 × `P_page` kept events published while parked. Interleaving: a recover opens; publishes on the same keys land before the Open, between the Open and the first meet step, and between meet steps; an ownership takeover comes between two meet steps; a meet step's commit has an unknown outcome and is retaken; in a second run the Open fails with a definite error, the instance re-parks, and a retried recover opens. Outcome: the Open reads no event and writes the subscriptions and one meet ticket; every meet step adds at most `P_page` deliveries; each kept event reaches each `Keep` wait exactly once, and no wait without keep is matched to an event whose match step finished before the Open (fifth review R5-M2); each event published after the Open reaches every restored wait once; deliveries added during the drain are withheld until Complete; the failed Open leaves no subscription, meet ticket or delivery, and the retried one behaves as the first run. Mutation: catch-up inside the Open; the Open reads every kept event of every key, and its work assertion fails at the first key with more than `P_page` events. Mutation: commit the next cursor before the page's deliveries are added; a crash between the two loses those matches, and a kept event is never delivered. The timing runs, the paused publisher, the replayed publish ticket and the swept later page, are SB-4 and SB-5, the outside publish's ticket is SB-6, the bounded hold SB-7, the start subscription SB-8 and the sweep's isolation SB-9 | K3, K4 |
| SB-3 | **Maximum Open work with subscriptions** (R4-M2). Preconditions: the largest attainable all-subscription plans at the name limits, 3,510 subscriptions counting 25,159,680 bytes (`B_plan` − 6,144) and 2,577 that keep events, each on a distinct key at the name limits, with their coverage entries, their conflict writes and the meet ticket, counting 25,159,168 bytes (`B_plan` − 6,656; sixth review R6-M2); and a mixed plan of `W_max` subscriptions, timers and jobs whose counted bytes are exactly `B_plan`, one timer's payload sized to fill the remainder. Interleaving: as TR-5 run 1, with a takeover right after the Open's commit and an unknown outcome retaken. Outcome: each Open is one commit, within TR-5 run 4's statement ceilings and MongoDB's 16 MiB document and 48 MiB message limits, and on MongoDB each subscription write is one insert entry whose encoded size, `_id` included, is at most its counted 7,168 bytes (6,640 measured by the size probe at the limits, before `RegisteredAt` and `Incarnation`), each keeping subscription's coverage entry and conflict write together at most their counted 2,560 bytes; the fixture sums every stored write the Open sends, subscriptions, coverage entries, conflict documents and the meet ticket, and asserts the sum is at most the plan's counted bytes. Measured at K4 and K12: the Open's encoded size and duration on each provider; at K12, the turn share of the partition's other instances during the Open and the catch-up steps. Mutation: restore subscriptions by today's replace-upsert; on MongoDB each write repeats `_id` in its filter, measures 9,905 bytes at the limits, and exceeds the counted bytes, and the assertion fails. Mutation (R6-M2): the fifth round's fixture, 3,498 keeping subscriptions on distinct keys at the name limits; its subscription writes and only the raw key bytes of its coverage entries already come to 27,036,042 bytes, above `B_plan` by 1,870,218 (the reviewer's arithmetic), and the sum assertion fails | K4, K12 |
| SB-4 | **Non-keeping waits by the match step** (the fifth review's R5-M2, trace A). Preconditions: a test clock; instance W parked, its plan restoring a non-keeping wait w on key k; publishes on k with no keep window; a live non-keeping wait w2 of another instance, subscribed at t = 500; a controllable owner for each publish ticket's partition. Interleaving, each run alone: (a) the paused match step, publish first: a turn's publish ticket for E commits at t = 1,000 while W is parked, its first match step inserts the event row and commits (reading no subscription), the recover Open restores w, and the next step, the first subscription page, then runs; (a′) the same with the step finished before the Open; (b) restore first: the Open restores w at t = 1,000, E's ticket commits at t = 1,001, its first step inserts the event row, and its first subscription page pauses before it reads; (c) the replayed publish ticket: E's ticket commits, its owner is lost before the step, the Open restores w, and a new owner runs the step; in a variant the lost owner had added w2's delivery and not consumed the ticket; (d) commit order without a clock: the database clock steps back 30 s between a non-keeping wait w3's subscribing commit and E's ticket commit, and, CodeRabbit's trace, E's ticket transaction takes its stamp at 100 and commits at 130 after a wait w4 committed at 120. Outcome: (a) w receives E once, since the step was outstanding when the Open committed; (a′) w is never matched to E; (b) w receives E once; (c) w and w2 each receive E once, the repeated delivery to w2 refused by its full key; (d) w3 and w4 each receive E once; w2 receives E once in every run, and no run gives any wait more than one delivery. The review's witness, on the built rules, gave (a′) no delivery and (a) one, the outcomes this rule states. Executed in this repair, on the real in-memory publisher path (`Store.cs` 287–295, `InMemoryStore.cs` 263–299): runs (a) and (a′) as `ev1-a` (three orders: the step before the restore, the publisher paused until after it, and the step at once with a 30 s pause before the restore, then a second processing of the same ticket that lands nothing), under the reviewed and repaired rules with `match-at-acceptance` and `split-no-row-hold` in place of a negative control; run (b) as `ev1-a-after` (the wait committed before E's acceptance, the step at once or 30 s later), under the reviewed and repaired rules; and run (c) in part as `ev1-replay` and `ev1-replay-atomic` (a lost owner's match step replayed by the next owner, with a live non-keeping wait in w2's role, the reviewed split's partial delivery being the variant, and in `ev1-replay` a restored keeping wait where (c) restores a non-keeping w), under the reviewed, repaired and negative rules, with `split-no-row-hold` on `ev1-replay-atomic` (#4790). Run (c) with a restored non-keeping w, run (d) and both mutations below are proposed, qualified at K3, and on the providers' clocks at K4. Mutation: the withdrawn clock rule, delivery when `AcceptedAt` ≥ `RegisteredAt` − 10 s with statement-time stamps; in (d) w4 is refused, 100 < 120 − 10, and invariant 13 fails. Mutation: a catch-up for every restored wait, keeping or not; in (a′) w receives E | K3, K4 |
| SB-5 | **Keeping waits: the entitlement point and the sweep horizon** (R5-M2, trace B). Preconditions: a test clock; tenant horizon `H` = 0; an event E on k accepted at t = 1 with `KeepUntil` 100 while W is parked; W's plan restores a keeping wait w on k; in a second set, more than 2 × `P_page` events on k, each with `KeepUntil` 100. Interleaving: (i) the Open restores w at t = 99, so `P0` = 99, and the catch-up runs at 99; (ii) a sweep runs at 101, then the catch-up; (iii) the paged set: the first page at 99, a sweep at 101, an ownership takeover, the second page at 102 and the rest after; (iv) after the last page a sweep at 103 deletes the events and raises `H` to 100, the database clock steps back to 99, and a new keeping wait w5 on k registers; (v) CodeRabbit's interleaving: a sweep batch at database time 101 locks the horizon row and deletes E, while, after a step-back to 99, a registration of a keeping wait at 99 reads `H`, in both commit orders; (vi) with `H` still 0, w consumes E's delivery at 100 and stays subscribed; another keeping wait w6 on k registers at 100, so its `P0` is 100, and its catch-up stays outstanding past 101; w subscribes again with keep at 102, keeping its `RegisteredAt` 99, so its new catch-up has `P0` = 99 and covers E; a sweep runs at 103; (vii) two catch-ups on one row: w's first catch-up T1 (`P0` 99) is outstanding when another key's sweep raises `H` to 100 and w subscribes again with keep; under the sixth round's rule (R6-M2) that registering commit finds T1's entry (i1, c1, 99) for w's incarnation i1, so it writes no meet ticket and no entry; a sweep runs at 103, then T1 passes w; (viii) a zero keep window: E2 on k is accepted at 100 with `KeepUntil` 100, its match step finishes before any wait on k exists, the database clock steps back, and a keeping wait w7 registers stamped 99 with `H` = 0; as a control, w8 registers at 101 with no step-back; (ix) a re-created row: w's catch-up T1 (`P0` 99, incarnation i1) is outstanding, w unsubscribes, so i1 and its entry go, then, after a step-back and with `H` = 100, w subscribes again with keep, stamped 98, as incarnation i2 with catch-up T2 at `P0` 101; an event E3 on k with `KeepUntil` 150 is published before the re-subscribe; T1's remaining steps run, then a sweep at 120, then T2. Outcome: (i) and (ii) each deliver E to w once; in (iii) every event reaches w exactly once, the second page finding its events because w's coverage entry, `P0` 99, holds them; in (iv) w5's `P0` is 101, its catch-up finds nothing and it is entitled to nothing the sweep removed; an event E4 published on k after the step-back, accepted at 98 with `KeepUntil` 99, is excluded from w5's catch-up as expired, since 99 < 101, and reaches the waits on k that registered before its match step, through the publisher side; in (v), registration first, the sweep's delete sees the coverage entry and keeps E, and sweep first, the registration reads `H` = 100 and its `P0` is 101, so in neither order is a wait entitled to a swept event; in (vi) the sweep keeps E (w6's coverage entry, `P0` 100) and w's `Done` delivery with it, w's second catch-up re-adds E as a duplicate that the store refuses, and w consumes E once; in (vii) the second registration writes no ticket and no entry, (i1, c1, 99) keeps E through the sweep at 103, T1 delivers E once, and its step then deletes the last entry, so at most one entry for i1 ever exists; in (viii) w7's `P0` is 99, its catch-up finds E2 (`KeepUntil` 100 ≥ 99) and delivers it once, as the rule states, and w8's `P0` is 101, so it is never matched to E2; in (ix) T1 has no entry for i2 and never matches it, T1's final step deletes no entry of T2's, the sweep at 120 keeps E3 by T2's entry (101 ≤ 150), T2 delivers E3 to i2 once and never E (`KeepUntil` 100 < 101); a non-keeping wait restored at 99 is matched to none of the events. The review's witness, on the built rules, gave (i) one delivery and (ii) none. Executed in this repair: runs (i) and (ii) as `ev1-b` (the catch-up at 99, and at 101 after the sweep), with `match-at-now` as the first mutation and its `negative`, the sweep without the meet ticket's coverage, as the second; run (iii) in part as `ev1-b-paged` (two events with `P_page` scaled to 1, a sweep before and between the pages, no takeover), with `negative` and `p0-at-page-commit`; and run (iv) as `ev1-b-stepback` (E1 swept so `H` is 100, a step back, a live E2 with `KeepUntil` 99 excluded from the new wait's catch-up and delivered to a wait registered before its match step), with `no-horizon` as the third mutation; each under the reviewed and repaired rules (#4790). `ev1-replay-consumed`'s `negative` shows the fifth mutation's duplicate on another schedule. Run (vi)'s kept `Done` delivery is executed as `ev1-replay-consumed` (with `no-publish-coverage` and its `negative`, #4790). Runs (v), (vii) to (ix), run (iii) as stated and the other mutations are proposed, qualified at K3, and the share lock at K4. Mutation: the catch-up compares the window with the time it runs, as built (`e.KeepUntil >= now`, `InMemoryStore.cs` 287–297); (ii) gives none. Mutation: the sweep without its coverage check, as built (`InMemoryStore.cs` 348–356); (ii) and the second page of (iii) find no event. Mutation: `P0` = `RegisteredAt` with no horizon; in (iv) w5 is entitled to the swept E, and the assertion that every entitled event is delivered fails. Mutation: the registration reads `H` without the share lock; in (v), registration first under snapshot reads, the sweep deletes E while the registration commits `P0` = 99, and the wait claims a swept event. Mutation: the message sweep removes a `Done` delivery at its own `KeepUntil`; in (vi) w receives E twice. Mutation: one shared mark per row, overwritten by a registration or cleared by the first catch-up that passes the row; in (vii) the sweep at 103 deletes E before T1 reaches it, and w never receives E. Mutation: a per-row counter for the coverage; in (ix) T1's final step takes away the count that T2 added and clears T2's coverage, the sweep at 120 deletes E3, and i2 never receives it Run (x), the registration against a publish in flight: a keeping wait w on k registers while a turn's publish of E on k is in flight and commits between the first step's event-row insert and the first subscription page, in both commit orders (w's commit before the insert, and after it); the same with an outside publish; on MongoDB with snapshot transactions. Outcome: w receives E exactly once in every order, through the page or through its catch-up. Mutation: the first step inserts the event row and reads the subscriptions in one transaction; in the order where w commits after that step's snapshot and before its commit, w never receives E. Run (x) is proposed, qualified at K3 and K4. | K3, K4 |
| SB-6 | **An outside publish through its ticket** (R5-M2). Preconditions: a test clock; a live non-keeping wait w on key k, subscribed at t = 500; an outside publish of E on k with no keep window; the standard outside deduplication window. Interleaving: (a) the entry point's one transaction inserts E's event row, stamped `AcceptedAt` 1,000, and E's publish ticket, and answers accepted; the ticket's owner reads the existing event row and pauses before its first subscription page; a sweep runs at t = 26,000; the owner resumes; (b) the owner is lost after the entry transaction commits and before the first step, a sweep runs, and a new owner takes the ticket; (c) the caller repeats the same publish, the same event id: (c1) before the ticket is consumed, (c2) after it, within the deduplication window, (c3) after the window, while a keeping wait's catch-up still holds the event row, and (c4) after the sweep removed the event row; (d) a fixture adds a row of kind publish in scope 0 through ordinary ingress; (e) the ticket's virtual partition is above the high-water mark; (f) on SQL Server and MySQL under the pinned collation (at K3, the in-memory store's equality hook folding case, as for EM-3), publishes of the same event id under `Order-7` and `order-7`, concurrently, then in sequence with the first ticket already consumed, then as a retry within the deduplication window after a sweep at t = 26,000, with a keeping wait on the key; (g) the ticket's match step fails `MaxAttempts` times and the ticket is set aside, the event's keep window passes and a sweep runs, and the caller retries the same publish within the deduplication window, once in the same spelling and, on SQL Server and MySQL under the pin (the equality hook at K3), once as `order-7` after `Order-7`. Outcome: in (a) and (b) w receives E once; the ticket's id is `pub:` and 64 hex digits, 68 bytes, on every provider; the event row's `AcceptedAt` is 1,000, from the entry transaction, and its `KeepUntil` 1,000; in (c1) and (c2) the repeat is a duplicate by wire id, so one ticket, one event row and one delivery exist; (c3) and (c4) are out of contract, and the fixture records what they do: in (c3) the event insert keeps the existing row, whose `AcceptedAt` and `KeepUntil` win, and w's delivery is a duplicate, its `Done` row kept with the event row; in (c4) the repeat is a new publish and w receives a second delivery; in (d) the row is a supplied ticket and is discarded with no event row (existing prototype behaviour, `Turns.cs` 110–111, `Worker.cs` 640); in (e) the publish is refused with `InboxFull` and no row is written; in (f) one event row, one ticket and one delivery per wait exist in every order, the second spelling being a duplicate that writes nothing, and the sweep keeps the event row while its ticket row exists, so the retry within the window is a duplicate too; in (g) the sweep keeps the event row and its `Done` deliveries until the deduplication window ends, so each retry, in either spelling, finds the event row, is answered as a duplicate with the dead-lettered status, and writes no event row and no ticket; mutation: the dead letter holds nothing past the keep window and the receipt is the `pub:` ticket id; the equivalent-spelling retry finds no receipt, republishes, and a wait that consumed the first delivery is matched again. The fixture counts one small ticket row inserted and consumed and the payload stored once, in the event row. Mutation: the built outside path, the caller inserting the row and matching in its own loop (`Store.cs` 287–295), with the caller paused past the window and a sweep between; the match reads no row (`InMemoryStore.cs` 281–285; `EfStore.cs` 690–699 and `MongoStore.cs` 461–462 at K4), and w is never matched to E. Mutation: the sweep without its publish-ticket check; (a) loses E the same way. Mutation (K4): a ticket id of the verbatim `(Q(t, key), id)`; on SQL Server at the name limits the insert exceeds 450 units and an admitted publish is refused. Mutation: the ticket id as the receipt, with no event-row insert at the publish; in (f) the second spelling gets its own ticket, the first ticket is consumed, the sweep deletes the event before the second ticket's match step, which inserts it again, and the keeping wait receives it twice Executed in this repair: the publish-ticket hold and the kept `Done` deliveries as `ev1-replay-consumed` (with `no-publish-coverage` and its `negative`) and the replayed ticket as `ev1-replay` and `ev1-replay-atomic` (with `split-no-row-hold`), under the reviewed and repaired rules (#4790); the outside publish ticket's runs are proposed, qualified at K3. | K3, K4 |
| SB-7 | **Holds are bounded per key** (R5-M2). Preconditions: tenant t; instance W with a keeping wait on k1 and an outstanding meet ticket with `P0` = 50 over 3 × `P_page` events on k1 with `KeepUntil` 60; other events of t on k2, with no catch-up, also with `KeepUntil` 60; the clock at 70. Interleaving: run 1, W is suspended; run 2, W's recover drain is open and withholds arrivals; in each, the owner's meet path takes W's ticket, a sweep runs between every two pages, and the owner is lost after the second page; run 3, the negative run: W's meet step throws on every attempt (a fixture fault) while another instance V's ticket waits in the same partition, once with no drain and once while W's recover drain is open; run 4, a persistent stall: W's step blocks without failing and without an owner loss, while publishes on k1 continue; run 5, a large broadcast: 100,000 non-keeping waits of t on key k3, then a turn's publish and an outside publish on k3; run 6, an ended publisher: instance X publishes on k4 and finishes in the same turn, and its ticket's match step throws on every attempt, its first step never committing; run 7, a parked broadcast: instance Y publishes to 2 × `P_page` = 340 waits on k5 (sixth review R6-M4: above `B_step` each step writes one delivery, so the fixture uses 340 waits) with a payload above `B_credit`, its ticket's step fails before any progress until Y parks and the Park keeps the ticket, and a recover then opens, the fault now gone, once in each mode (resume, retry and retry-all), and in every mode its drain restores the ticket with its committed cursor among other rows. Outcome: the catch-up proceeds while W is suspended or draining, at most `P_page` matches per step, and resumes from the committed cursor after the owner loss; its deliveries are withheld until W resumes or the drain completes; every sweep deletes the k2 events at once and keeps k1's until the catch-up's cursor passes W's wait; after the last page no entry is left and the sweep deletes k1's events; in run 3, V's ticket proceeds at once, W's ticket counts a failed attempt per step, and at `MaxAttempts` W parks, with the drain open too, as a re-park of the open recover, the cohort deferral of the poison comparison not applying to the ticket, its subscriptions and their coverage entries go, the next sweep deletes k1's expired events, and `elsa.kernel.events.ticket_failures` counts the failures; in run 4 each blocked step is abandoned at its one-lease deadline and counted, W parks at `MaxAttempts`, and the expired k1 events retained meanwhile are no more than those published in that time; in run 5, with a small payload (at most 24,672 bytes a delivery, so the byte allowance cuts no page), the outside publish's match step runs 589 steps, 588 full pages of `P_page` and a last page of 40, and the turn's publish 590, its insert-only first step and the same 589 pages (sixth review R6-m3), and the step that reads that short page consumes the ticket, so there is no empty step; each step commits within its deadline, so no attempt is counted, the publisher never parks, the outside event is never set aside, and each of the 100,000 waits receives each event once; in run 6, at `MaxAttempts` the ticket becomes a dead letter with the alert, it shows in the dead-letter read model, no event row exists, since the first step never committed, and an operator replay runs the first step from the ticket, which still carries the payload, inserts the event row and runs the remaining steps; in run 7 the restored ticket counts as handed off once restored and is never in `elsa.kernel.drain.pending`, its 341 steps, the insert-only first step and then one delivery per step by the byte allowance, run on the meet path while the drain's later pages and its Complete proceed by the credit over Y's model rows alone, each step writing at most one row at the row cap, and each wait receives the event once. Existing prototype behaviour: a polled ticket goes to the meet path before any instance slot (`Worker.cs` 640–644), and the meet path checks only ownership and marks (`Worker.cs` 1315–1320). Mutation: the withdrawn global low-water mark; the k2 events stay while W's ticket is outstanding, and the k2 assertion fails. Mutation: the meet path waits for W to resume; the k1 events are held for the whole suspension, and the bounded-hold assertion fails. Mutation: today's single `try` over every ticket (`Worker.cs` 1317–1334); in run 3 V's ticket never proceeds and the k1 events are held for good. Mutation: apply the cohort deferral to the meet ticket; in run 3's drain-open variant W does not park while the drain is open, and its coverage holds the k1 events until the cohort is consumed, and the bounded-hold assertion fails. Mutation: no step deadline; in run 4 the hold never ends. Mutation: an unpaged match step; in run 5 every attempt reaches its deadline before it commits, the publisher parks and the outside ticket becomes a dead letter at `MaxAttempts`, and the waits never receive the events, against invariant 13. Mutation: count ticket kinds in the drain's credit; in run 7 the ticket alone takes b above `B_credit`, so no further page is admitted until its last step consumes it. Mutation: the ended publisher's ticket waits for a Park; in run 6 nothing can park X, the ticket stays outstanding with its attempts spent, and its event is held for good Executed in this repair: run 1's suspended catch-up as `ev1-b-suspended` (with `meet-waits-while-suspended`), under the reviewed and repaired rules (#4790); the other runs are proposed, qualified at K3. | K3, K4 |
| SB-8 | **A start subscription's catch-up through a meet ticket** (R5-M2, the independent review of #4787). Preconditions: an event E on k accepted at t = 40 with `KeepUntil` 100, whose match step at 45 finds no subscription. Interleaving: a definition registers an event-start subscription S on k; the registrar's write commits at 50 with S, its `RegisteredAt` 50, its `P0` 50, a kernel-issued meet ticket and its coverage entry; the ticket's owner pauses before the catch-up; a sweep runs at 101; the catch-up runs at 105; in a second run the owner is lost between the registrar's commit and the catch-up; in a third, the registrar repeats the same registration at 60 while the first ticket is outstanding; in a fourth, a second start subscription S2 registers on a key with no events; in a fifth, a stalled start: S's events are E, with `KeepUntil` 100, and E2, with `KeepUntil` 300 (sixth review R6-m3), its catch-up step blocks on every attempt until it is set aside at t = 200, a sweep runs at 210, and an operator replays the dead letter at 250; an event E3 on k with `KeepUntil` 240 is stored at that time, and a sweep batch at 250 deletes it and raises `H` to 240 while the replay registers, in both commit orders. Outcome: the sweep keeps E, since S's coverage entry, `P0` 50, is at or below 100; the catch-up starts exactly one instance for E; the repeated registration keeps `RegisteredAt` 50, finds S's entry and writes no ticket and no coverage entry (sixth review R6-M2; while the first ticket is outstanding its `meet:` id would also be a duplicate), so exactly one entry for S exists, and starts nothing more, the start delivery being a duplicate by its identity; S2's catch-up writes no next meet ticket after its one step, and no coverage entry of S2 remains after that step; in the fifth run, at `MaxAttempts` the ticket becomes a dead letter with an alert and its entry is released, the sweep at 210 deletes E (100 < 210) and raises `H` to 100, the dead-letter read model shows the ticket, and the replay writes its entry again with `P0` = max(50, 101) = 101, so its catch-up starts one instance for E2 and none for E, the stated exception to invariant 13; in the race the replay takes the horizon share lock first, so either the batch commits first and the replay reads `H` = 240, its `P0` is 241 and it is not entitled to E3, or the replay commits first and the batch's delete sees its entry (`P0` 101 ≤ 240) and keeps E3, which the catch-up then delivers; in neither order does the replay claim a swept E3. Mutation: the registrar without the ticket, as built (`Events.SubscribeAsync` and `CatchUpAsync`, `Store.cs` 302–315): it inserts S at 50 and pauses, the sweep at 101 deletes E, the catch-up at 105 finds nothing, and the start never happens. Mutation: a step that writes no next ticket deletes only the entries its cursor passed; S2's entry stays and holds its key's events for good. Mutation: a per-row counter for the coverage; in the third run the duplicate registration adds a count with no ticket, and S's coverage never clears. Mutation: the replay reuses the original `P0` 50; it claims E, which the sweep removed. Mutation: the replay reads `H` without the share lock; it reads 100 while the batch deletes E3 and raises `H` to 240, and its `P0` 101 entitles it to the swept E3 | K3, K4 |
| SB-9 | **Sweep isolation, lock order and the stall** (R5-M2). Preconditions: on every provider, a tenant with 10 × `P_page` expired events over many keys, some covered by coverage entries, and a stream of keeping registrations on those keys from several owners. Interleaving: sweep batches run while registrations commit, each pair in both orders at the horizon lock; on SQL Server once under locking read committed and once under RCSI; on MongoDB, two registrations of one key race each other and a batch, and two registrations of different keys race a batch; a two-tenant run: one commit registers keeping waits of t1 and t2, another of t2 and t1, while batches of t1 and of t2 queue between their locks; a covered front: the tenant's first 2 × `P_page` expired events by `KeepUntil` are covered by coverage entries, with uncovered expired events behind them. Outcome: no deadlock on any provider; each batch holds the horizon lock for one batch only and deletes at most `P_page` events; in every pair either the batch's delete sees the registration's coverage entry or the registration's `P0` is above the batch's `H`, so no wait is ever entitled to a swept event; on MongoDB the aborted transaction lands nothing and the owner's retry of the same commit lands once, registrations on different keys never conflict, and a retry after a takeover answers not landed at its fence; in the two-tenant run every transaction takes its horizon locks in tenant-id order and no deadlock forms; in the covered-front run each relational batch deletes up to `P_page` of the uncovered events behind the front, and on MongoDB the batches pass the front by the sweep cursor (MW-1, sixth review R6-M3). Measured at K4 and K12: the horizon lock's hold time per batch, registration latency under a running sweep, and MongoDB's abort and retry rate per tenant under concurrent keeping registrations. Mutation: a registration that writes its subscription before taking the share lock; the opposite lock order deadlocks against a batch on at least one provider, and the run fails. Mutation: one unbatched sweep transaction; the horizon lock is held for the whole sweep, and the registration latency assertion fails. Mutation: horizon locks taken in the order a commit's turns appear; in the two-tenant run the two registrations and the two queued batches deadlock on a provider whose lock queue is first come first served. Mutation: limit then filter, taking the first `P_page` events by `KeepUntil` and then dropping the covered ones; in the covered-front run every batch deletes nothing and the uncovered events are never swept | K4, K12 |
| EI-1 | **A new publication after the sweep reaches a continuing wait** (the sixth review's R6-B2). Preconditions: a test clock; a keeping wait W of instance X on key K, registered once and never removed (one subscription incarnation); the standard outside deduplication window; two outside publishes of the same `(K, E)`, the first with payload A and the second with payload B. Interleaving: (a) the first publish is accepted with incarnation i1, its match step adds W's delivery, W consumes it (`Done`), the deduplication window ends, a message-sweep batch removes the old ticket's `Done` row while the event row still protects W's `Done` delivery, and the event-sweep batch then removes the event row, leaving W's `Done` delivery for a later message-sweep batch; the second publish is then admitted, then the message sweep runs; (a′) the same, with the old ticket's `Done` row also left for the later message-sweep batch; (b) the message sweep runs in the gap, before the second admission; (c) a retry of the second publish with another payload within its window, before its delivery is consumed; (d) a slow consumer: W has not consumed the first delivery when the event row is swept; (e) a crash between the event-sweep batch and the message-sweep batch, with the second admission arriving while the sweep restarts; (f) a start subscription S on K in place of W, each publication starting an instance; (g) i1's ticket is dead-lettered, the sweep removes its event row, the second publish (i2) is admitted and delivered to W, a non-keeping wait V registers on K after i2's match step has finished, and an operator replays i1. Outcome: the second admission inserts a new event row with an incarnation i2 ≠ i1 and a new `pub:` ticket; in (a), (a′) and (b) W receives B exactly once, and in no order does an earlier `Done` row suppress the delivery; after (a) the next message sweep removes the old `Done` delivery, since no event row has its `Match`; in (c) the retry answers i2 as a duplicate, writes nothing and W receives nothing more; in (d) W receives A and then B, once each; in (e) the restarted message sweep removes only rows whose event row is gone, and W receives B once; in (f) each publication starts one instance; in (g) the replay reads by i1, finds no row, answers "gone" and adds no delivery, so V receives nothing; every receipt names the stored identity and the incarnation. Mutation: the fifth round's digests, without the incarnation; in (a) the second match derives the old delivery key, the `Done` row suppresses it, the new event row then protects that row, and W never receives B, while in (b) W receives B once, so the outcome depends on the sweep order; in (d) the pending first delivery suppresses the second; in (e) the crash leaves (a)'s state and B is lost. Mutation: the incarnation in the delivery `Id` and `Match` but not in the `pub:` id; in (a′) the second ticket's key equals the old `Done` ticket's, its insert is refused as a duplicate, and the publication is never matched. Mutation: a step or replay that reads the event row by `(key, id)` alone; in (g) the replay of i1 matches i2's row and delivers B to V, a wait no match step of i2 reached. Executed in this repair (`experiments/Review6.Blockers`, #4798): `pi1-event-first` (run (a)), `pi1-gap` (run (b)), `pi1-slow-consumer` (run (d)) and `pi1-duplicate` (one retry before consumption, with no owner-side `pub:` ticket, part of run (c)), on the real `InMemoryStore` paths, under the reviewed and repaired rules. Runs (a′), (c) as stated, (e), (f) and (g) and the second and third mutations are proposed, qualified at K3, and on the providers at K4 | K3, K4 |
| EI-2 | **Provider-equal spellings across publications** (R6-B2). Preconditions: on SQL Server and MySQL under the pinned collation (at K3, the in-memory store's equality hook folding case, as for EM-3), a keeping wait W on the key; an outside publish of event id E under `Order-7`. Interleaving: (a) a retry within the window under `order-7`; (b) after the window and the sweep of the event row, a publish under `order-7`; (c) a retry of (b) within its new window under `Order-7`; (d) during (b)'s match step, a second keeping wait W2 registers on `ORDER-7`, so the publisher and W2's catch-up race to write W2's delivery; on PostgreSQL, SQLite and MongoDB the same spellings as a control. Outcome: in (a) the insert-or-read finds the row under the pin and answers its incarnation as a duplicate; in (b) a new row is stored, in the spelling `order-7`, with a new incarnation, and W receives it once; in (c) the retry answers (b)'s incarnation and writes nothing; in (d) both sides compute W2's delivery from the persisted event and subscription rows, so one delivery exists and W2 runs once; every digest is computed from the persisted spelling and incarnation; on the control providers the spellings are two keys. Mutation: digests over each side's caller spelling, the publish's `order-7` and the registration's `ORDER-7`; in (d) the two sides write two deliveries and W2 runs twice | K3, K4 |
| MX-1 | **A turn's publication and an outside publication never share an event row** (seventh review R7-M1; owner decision, 18 November (seventh review)). Preconditions: a wait W registered on `Q(acme, topic)` without keep; an instance A whose turn publishes `from-turn` on that key; an outside publisher in the same tenant. Interleaving, each in its own run: (a) A's turn commits its publish ticket, and before the owner takes the ticket's first step an outside publish on the key uses the event id A's `Publish` returned, with payload `from-outside`, then retries it; (b) the control: the same with a distinct outside id, and the other order with an arbitrary outside id, the outside publish first, then A's turn publish; (c) before A's turn, an outside publish under the id A's next turn publish will return, then A's turn publish; (d) as (a), with an owner change between the ticket's commit and its first step; (e) at K4 on SQL Server and MySQL, outside ids provider-equal under the pin to a `~`-prefixed id: one led by a fullwidth `～` (U+FF5E), and one led by a character the pinned collation ignores. Outcome: in (a), (c), (d) and (e) the outside publish, and in (a) its retry, are refused `InvalidEventId`, with no row written and no receipt issued; the turn's first step inserts its event row and W receives `from-turn` once; in (d) the new owner takes the pending ticket under its epoch and the outcome is the same; in (b) two event rows exist in either order, one per producer, and W receives both payloads, once each. Mutation (the reviewed rule): no prefix and no refusal; in (a), (c) and (d) one event row exists, carrying `from-outside`, and W receives only `from-outside`. Negative: the prefix without the refusal, and the refusal without the prefix; each gives (a), (c) and (d) the reviewed outcome. Runs (a) as `mx1-ticket-first`, (b)'s distinct id as `mx1-distinct`, (c) as `mx1-outside-first` and (d) as `mx1-owner-change`, under the reviewed rule, the repaired rule and both negatives, are executed in this repair (`experiments/Review7.Blockers`, #4803), on the real `DurableTurn.Publish` and `InMemoryStore` publish paths, with the prefix and the refusal held by the harness. (a)'s retry and (b)'s other order are proposed, qualified at K3 and K4, and run (e) at K4 | K3, K4 |
| CC-1 | **One catch-up id across a meet chain** (the sixth review's R6-M1). Preconditions: an instance W with one keeping subscription, one incarnation i, and 171 entitled events on its key, so its catch-up needs two steps at `P_page` = 170; small payloads. Interleaving: the chain's first ticket T0 is written with its coverage entry (i, c); step 1 commits; in a second run the owner is lost after step 1 and a new owner takes the successor; in a third, step 2's commit has an unknown outcome and is retaken; a sweep runs between the steps with every event past its window. Outcome: step 1 delivers 170, consumes T0 and inserts T1 with the same `CatchUpId` c and a new inbox key `meet:`H(c, 1), and (i, c) stays; the sweep keeps event 171 by (i, c); step 2 matches W through (i, c), delivers event 171 once, writes no successor and deletes (i, c); no coverage entry remains, and the next sweep deletes the expired events; no commit ever consumes and inserts one key; the retaken step's successor or deletions land once. Mutation: the fifth round's key by ticket, `(Incarnation, Ticket)`; T1 reads no entry through its own id, finishes, event 171 is never delivered, and T0's entry holds the expired events. Mutation: reuse T0's inbox key for T1; the commit that consumes and inserts it is refused (`InMemoryStore.cs` 118–119) on every attempt. Proposed, qualified at K3; no harness run backs it | K3, K4 |
| OA-1 | **The Open's stored writes and the cleanup bound** (the sixth review's R6-M2). Preconditions: SB-3's 2,577 keeping subscriptions on distinct keys at the name limits, restored by an Open; in a second run the live model subscribes one of them again with keep in each of 10 turns while its first catch-up is paused; then a Park, and in a third run an unsubscribe and a termination. Outcome: the Open writes the subscriptions, one coverage entry each, on MongoDB one conflict write each, and one meet ticket, within the plan's counted bytes (sizes measured at K4); the repeated registrations add no entry and no meet ticket, so each incarnation holds at most one entry; the Park deletes at most 2,577 entries and the unsubscribe one. Mutation: each repeated registration adds its own chain and entry; one incarnation ends with 11 entries and the cleanup assertion fails | K3, K4 |
| MW-1 | **MongoDB's sweep passes a covered front** (the sixth review's R6-M3). Preconditions: on MongoDB, a tenant with 2 × `P_page` = 340 expired events at the front by `KeepUntil`, each on a distinct covered key, and uncovered expired events behind them; keeping registrations racing the batches, one on an uncovered event's key committing after a batch's snapshot. Outcome: the first two batches delete nothing, write no conflict document and move the cursor; the third deletes the uncovered events, writing conflict documents only for their keys; the racing registration aborts that batch, whose retry keeps the newly covered event; once the front's coverage goes and the cursor wraps, a later batch deletes the front; no wait is entitled to a swept event. Measured at K12: batches per pass and the abort rate. Mutation: the fifth round's batch, a conflict write for every candidate; it either deletes nothing for ever or writes 340 conflict documents before its first deletion. Mutation: no wrap; the front is never deleted after its coverage goes | K4, K12 |
| BA-1 | **A byte allowance per match and meet step** (the sixth review's R6-M4). Preconditions: a turn's publish and an outside publish with a payload at the source limit on a key with 170 non-keeping waits; a keeping wait whose catch-up meets three such events; another tenant's instances in the same partition. Interleaving: an owner loss after one step, and an unknown commit outcome retaken. Outcome: each match or meet delivery step writes exactly one delivery and advances the cursor to it, so the outside publish takes 170 delivery steps, the turn's publish 170 delivery steps plus its insert-only first step, which writes none, and the catch-up three; no step counts an attempt; each wait receives each event once; the other tenant keeps its turn share (measured at K12). Mutation: no byte allowance; the first step carries about 2.6 GiB, reaches its deadline on every attempt, and the publisher parks. Mutation: the cursor moves to the end of the page read; the unwritten deliveries are lost | K3, K4, K12 |
| JS-1 | **A restart runs the invocation the model started** (the fourth review's R4-M3). Preconditions: a Native workflow sets `svc` to `alpha`, then arms a background test activity whose input binds `svc` and which returns its input and `ActivityContext.EffectKey`, while a sibling in the same turn sets `svc` to `beta` (the size probe's workflow); the job's ticket then reaches `MaxAttempts`, so a Park keeps it `Consumed`. Interleaving: a `resume` restarts the job, with a takeover between the Open and the job's entry. Three runs of the restart's source: the old ticket kept `Consumed`, the traced branch; and, through a below-admission fixture labelled as one, since no traced schedule leaves a waited-for job's ticket ordinary done or absent, a payload-free done marker and an absent row, which the lookup table sends to the same restart. Outcome: in every run the new ticket's arguments equal the `JobSpec` recorded at arm, under the next `JobGeneration`; the real background activity runs once and receives `alpha` and the original effect key; its result resumes the frame once; nothing is read from the old row. Mutation: rebuild the arguments by evaluating the bindings at the restart; the activity receives `beta`. Mutation: copy the arguments from the old row; the marker run fails with `ArgumentNullException` in `ExecuteJobAsync` and the absent run has nothing to copy (both failure modes measured on the real model by the size probe) | K3, K6 |
| JS-2 | **Retry-all with an obsolete generation beside the current one** (R4-M3). Preconditions: J's ticket g0 is kept `Consumed` by the Park; a `resume` restarts J as g1 (`Restarted(g1)`); a re-park interrupts before the page that would clear g0, so g0 stays kept `Consumed` beside the pending g1. Interleaving: a retry-all opens, with a takeover between its pages; in a second run an explicit `retry` names g0's handle. Outcome: retry-all's page clears g0 and never restores it; g0 is never polled undone and never entered (the fixture counts entries and driver executions per ticket id: g0 zero, g1 one); the activity runs once and its result is applied once; the explicit retry is refused before Open with no change. Mutation: restore every `Consumed` row whatever its generation; g0 is restored and released, the poll classifies it as a ticket (`Worker.cs` 654–658), `Worker.StartJob` enters it with no generation check (`Worker.cs` 1166–1203), the activity runs beside g1's run, and the entry count fails, although the late result is dropped | K3, K4, K6 |
| JS-3 | **The spec's bytes** (R4-M3). A Native background arm whose captured inputs would take the plan's counted bytes past `B_plan` faults that activity at arm; the same input passed by reference arms, and a restart reads the referenced value. With the model-side check skipped, a plan whose job specs' encoded arguments sum, with the allowances, to exactly `B_plan` commits its Open, and one byte over refuses the recover with reason "plan" and no change. The instance envelope that holds the specs stays under the row cap with the `ParkRecord` reserve (D-3). Mutation: leave the spec out of the count; the over-bound plan reaches the Open, and TR-5's ceiling assertions fail | K3, K6 |

## Decisions (owner, 16–17 November 2026)

The owner took these on 17 November; the [decision register](decisions.md#the-seven-design-now-answers-1617-november) lists them
with the rest of the kernel's. Rejected options are kept for the record.

1. **A parked instance is recovered in place, plus intervention (a + d).** The same instance id and creation continues
   at its next revision, and the viewer shows the Park and the recovery as journal entries of the same run. Faults the
   model can hold keep the instance Running with a blocking incident. Because the parent keeps waiting for a parked
   child ([large fan-out](large-fan-out.md), decision 5), the recovered child's later completion reaches a parent that
   still registers it. Rejected: (b) a linked successor; (c) refusing recovery of a terminal run.
2. **Park is extended (a):** it keeps consumed deliveries and arrivals while parked as done rows under the `Parked`
   window, marked by `KeptBy` under the invariant "non-null only while its Park is unresolved" (after the gate 2
   review, `KeptBy` holds the row's kept reason, which tells the Park-consumed rows from later arrivals, in place of
   the id list `ParkRecord.Consumed`), journals at every
   history level, and the restore clears `Done`. This extends the 13 November Park decision. Rejected: (b) Park as
   decided on 13 November, where only `resume` is possible.
