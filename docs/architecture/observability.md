# Observability and operator actions

> **Proposed.** The kernel design awaits owner approval; this page answers design-now question 6 and does not
> authorize product implementation.

[Design-now question 6](../reports/clean-slate-kernel/design-sketch-draft-5.md#to-answer-in-the-design-proposal-before-the-fable-review-owner-decision-12-november)
asks for observability as a contract: trace context on every message, stable metric names, and operator actions on
parked messages, inboxes and partitions. Release 1 ships trace context and metrics "in a basic form", and its parked-work
tooling must support inspect, retry and discard
([release 1 scope](../reports/clean-slate-kernel/design-sketch-draft-5.md#release-1-scope-owner-decision-amended-15-november)).
This page proposes the contract, where each part lives in the three layers, and what release 1 builds. Retry of parked
work needs an extension of the owner's Park decision; the page marks it and puts it to the owner.

## Terms

| Term | Meaning on this page |
|---|---|
| Park | The owner's decision of 13 November, as built: a fenced commit faults the instance, consumes its deliveries and removes every active subscription, keeping the last committed state, the fault and the configured history. The parent receives the faulted-child notice; later arrivals are dropped. |
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

Gauges are sampled off the turn path, once per pass or poll. A counter records what this node saw: a commit whose
answer was lost counts as an unknown outcome.

## Operator actions

| Action | How | Release 1 |
|---|---|---|
| Inspect parked work | The parked-work read model: instance, actor type, model, definition and version, fault, reason, when, and what the Park kept (decision 2). Payloads need a separate permission | Yes |
| Retry parked work | A `kernel.recover` message in `retry` mode: as `resume`, and the deliveries the Park consumed are delivered again | Yes, under decisions 1 and 2 |
| Recover without retry | `kernel.recover` in `resume` mode: waits restored, and the arrivals kept while parked (decision 2) delivered; the deliveries the Park consumed, timers among them, are neither delivered again nor re-armed, so a wait whose message the Park consumed (the poison among them) keeps waiting, which is what `retry` is for. Under decision 2b nothing was kept, so a wait for a child completion, a reply or a direct resume that arrived while parked never ends | Yes, under decision 1 |
| Discard parked work | A `kernel.discard` message | Yes |
| Inspect an inbox | Pending deliveries of one instance, in delivery order: id, kind, sender, due, state (pending, held or on trial), attempts, size; paged | Metadata only |
| Suspend, resume, terminate, delete | The built control kinds | Yes |
| Release a partition, drain a node | Host administration: asks the owner to release, through the runtime | Yes |
| Force a handover from an unresponsive owner | Host administration: a forced take, a conditional update at the expected owner and epoch that names the new owner and **increments the epoch**, so the old owner's next commit fails the fence. Ending the lease alone would not do: with the fence in the batch, the old owner's next commit at the same epoch would still land and renew its lease | Later |

### What Park keeps: an extension of the owner's decision

Today Park consumes its deliveries (deletes them, or marks done those that carry a `keep_until`), and later arrivals are
dropped. A retry then has nothing to deliver again: only `resume` is possible. FINDINGS 25.9 records that Park does not
keep consumed deliveries in a dead-letter queue. Retry of a parked message needs two additions, which **extend** the
13 November decision and are therefore decision 2:

1. **Park keeps the deliveries it consumes, and the arrivals while parked,** as done rows marked as kept: a nullable
   `KeptBy` column on the message row holds the revision of the Park that kept it. Park's commit and every later
   commit in which the faulted instance consumes an arrival set it in the same update that marks the row done, so it
   costs no statement, only the column (8 bytes where set, a null-bitmap bit elsewhere). Ordinary done markers,
   outside commands the instance handled before the Park and still within their window, have no `KeptBy` and are
   never restored, so none is applied twice (invariant 4). A list of kept keys in the `ParkRecord` on the instance
   row would do the same, but the parked instance's row would then grow with every arrival and be rewritten whole
   each time. Their `keep_until` follows the [data lifecycle](data-lifecycle.md) page's `Parked` window: the Park's
   time plus that window, or no expiry when it is null (the default: kept until recovered or discarded).

   **Invariant: `KeptBy` is non-null only while its Park is unresolved.** Every step that resolves a Park clears
   `KeptBy` on all the rows that Park kept, restored or not, in the same commit: recover in either mode, in the same
   update that sets their ordinary outside-command window; discard; and delete of a parked instance, which gives the
   rows the ordinary window and clears `KeptBy`, or removes them, as the data lifecycle page says. Revisions start
   again for a new creation under the same id, so the rule keeps a later Park of a new run at the same revision from
   ever finding a predecessor's rows; an ordinary delete then needs nothing extra.

   A child completion that arrives while parked is kept with the rest, and its entry stays in the parent's child
   registry; today such a notice closes the entry without the model.
2. **Park writes a journal row at every history level**, so the projector can maintain the parked-work read model at
   Minimal. If the data lifecycle page's format item 4 (lifecycle transitions journalled at every level) is adopted, this
   row is its "ended" entry and adds nothing.

If the extension is adopted, the data lifecycle page's format item 6 (done markers keep only the id and window) must
exempt a parked instance's kept deliveries, or a retry finds no payload.

### Recover

`kernel.recover {operation, expected revision, mode, deliveries}` is a control kind: the actor type handles it alone, in
one commit, and calls one new model hook.

- **The contract gains a hook,** beside `UpgradeState`:
  `RestorePlan RestoreWaits(object state, ParkRecord park, IReadOnlySet<string> pendingTimers)`. It returns the
  subscriptions, timers and jobs the model's committed state still waits on. Native, Flowchart and StateMachine each
  implement it.
- **Checks.** The instance is parked at the expected revision and, per decision 1, recoverable; otherwise the command is
  refused and the refusal recorded.
- **Subscriptions** were removed by Park and are restored from the plan. A wait that asked for kept events receives the
  events published while it was parked, within their keep window; a wait that did not misses them, as the owner
  decided.
- **Timers.** The owner reads the instance's inbox rows (one read by target, of pending rows and of rows with
  `KeptBy` equal to this Park's revision) and passes the ids of timers still pending.
  A pending timer stays as it is. A timer that came due while parked and was kept (decision 2a) is restored as its own
  row, below, which arms it with its original id and due time, so it fires at once. A timer whose row is gone
  (decision 2b) is armed again by the plan with its original due time. A timer the Park itself consumed is restored
  only in `retry`, as one of the consumed deliveries; `resume` neither restores nor re-arms it, so it cannot fire the
  poison again. No timer is armed twice.
- **Jobs.** A job still in the row's jobs with a live ticket keeps it. A kept ticket row is restored like any kept
  delivery, below, with its original id. A job whose ticket is gone (decision 2b, or a ticket the Park consumed under
  `resume`) starts again under a new ticket generation: the kernel derives the ticket id from instance, key, creation and the recovery's ordinal `n`
  (`Worker.TicketId` takes the first three today), so it collides with neither the old ticket nor its kept done row.
  The model never sees the generation: the job context and the `job-finished` result carry the model's own key, and the
  kernel maps the generation back. A result from the old generation matches no live ticket and is dropped.
- **Deliveries are restored, never copied** (under decision 2). Both modes restore every kept arrival from while
  parked; `retry` also restores the selected deliveries the Park consumed. Recover's commit, under the partition fence,
  updates each row to be restored by its key with the predicate `Done AND KeptBy = :park` (the row is still there,
  still done, and kept by this Park): it clears `Done` and `KeptBy`, resets `Attempts` to 0 and `Trial` to none, sets
  `Seen` to the current epoch and `KeepUntil` to the ordinary outside-command window from now. In the same commit, the
  rows this Park kept that recover does not restore (the Park-consumed rows under `resume`) get the ordinary window and
  a cleared `KeptBy`, by the predicate `KeptBy = :park`, so no row keeps the marker after the Park is resolved. Resetting the poison counters matters: the owner parks any delivery whose
  attempts reached the threshold (the `MaxAttempts` check in
  [Worker.cs](../../prototypes/kernel-model/src/Kernel.Durable/Worker.cs)), so a retried poison message or job ticket
  would otherwise park the instance again at once. Each row's update has its count checked; a row that matched nothing
  (the sweep removed it first) goes on the "could not deliver" list and does not fail the commit. Only rows whose update
  landed are handed to the instance in memory after the commit, in their original `seq` order, so nothing is in memory
  that is not on disk (invariant 9). A restored row keeps its id, sender, kind, payload and
  creations. That matters because the kernel authenticates some kinds: a child completion to a creation-qualified
  parent must carry a terminal-notice id, `Sender` equal to the finished child and matching creations
  (`WorkflowInstance.cs`, the `ChildFinished` check), and a copy under a new id would be refused by
  `CreationProtocol.Refuse` and consumed ([Creation.cs](../../prototypes/kernel-model/src/Kernel.Durable/Creation.cs)).
  Job, meet and publish tickets keep their kernel-issued ids the same way. A restored row is marked done again when
  consumed, and the sweep removes it after its new window.
- **A store contract change.** Clearing `Done` is a new message transition: today a row goes from pending to seen,
  trial and done, and never back. The `KeptBy` column is new too, set only by Park and by a parked instance's
  consuming commits, and cleared on all of a Park's rows by every step that resolves it (recover, discard, delete of
  the parked instance), under the invariant above. Both are kernel behaviour, and every provider and the store
  conformance suite need them.
- **Expired kept rows.** A row whose `Parked` window has passed but that the sweep has not yet removed still matches,
  and is restored under the ordinary window set above. The sweep's delete and the restore update meet on the row: the
  one that runs second finds nothing, and the restore's count says which happened. A row the sweep removed goes on the
  list. So when a non-null `Parked` window has passed, recover delivers what remains and its answer lists the ids it
  could not deliver. `retry` of a Park whose consumed deliveries are all
  gone is refused; `resume` stays possible.
- **Side effects** of the retried turn may repeat, with the same effect keys.

**Discard** `{operation, expected revision}` gives the kept deliveries the ordinary window and clears their `KeptBy`,
or removes them, in the same commit, and records the disposition. The instance stays faulted, and retention or a delete removes it.

**Every operator message** is an outside add whose id is the operation id, with a `keep_until`, so a retried command
gets a duplicate-key answer and no second effect. The kernel's tenant check on sends applies. Inspecting metadata,
reading payloads, recovering, discarding and handing over are separate permissions, and each action is audited with
its principal, reason and outcome.

## Read models: how layer 3 stays off partitions

Studio and the operator API depend on the turn contract, the four seams and read models only. They name an instance by
its `InstanceRef` and never see a partition, a position, an epoch, an order key or SQL:

```csharp
interface IParkedWork      // read model, written by the projector from Park, recover and discard journal rows
{
    ValueTask<Page<ParkedItem>> ListAsync(ParkedQuery query, CancellationToken cancellationToken);
    ValueTask<ParkedDetail?> GetAsync(InstanceRef instance, CancellationToken cancellationToken);
}

interface IInboxInspection // delivery seam: rows only, eventually consistent
{
    ValueTask<Page<PendingDelivery>> PendingAsync(InstanceRef target, PageToken? from, CancellationToken cancellationToken);
}

record PendingDelivery(string Id, string Kind, string? Sender, DateTimeOffset? Due,
                       DeliveryState State, int Attempts, int Size);   // State: Pending, Held, OnTrial
```

- **Ids are opaque to layer 3.** Today an `InstanceRef` id embeds its partition (`3/order-17` in
  [Addresses.cs](../../prototypes/kernel-model/src/Kernel.Runtime/Addresses.cs)), and `InstanceRef.Partition` parses it.
  The contracts package layer 3 builds against exposes no such property: layer 3 compares and stores ids as strings and
  never parses them; only layer 2 derives a partition from an id.
- **No order keys or epochs.** A page comes in delivery order; its position travels as an opaque `PageToken`. `seq`,
  `seen` and `trial` stay in layer 2; inspection reports them as the `State` above.
- Today's `Viewer.ReadAsync` takes a partition argument; its layer 3 facade drops it.
- Commands travel through the delivery seam's add, the path every API command takes; their outcome is visible in the
  read models, or through a reply address for a synchronous API call.
- Each answer says how fresh it is: read models lag the projector, and inbox inspection sees rows only, so messages an
  owner holds unwritten in memory do not appear.
- Partition release, drain and handover are host administration of layers 1 and 2. Studio shows the kernel's health
  through the metrics above, never through a partition API.

## Release 1

| In release 1 | Later |
|---|---|
| The `trace` column and propagation on every message; turn, commit and job spans | Trace-horizon tuning per route; richer span attributes |
| The metric set above, aggregated per node, with the partition backlog as a histogram | Per-partition series by default |
| The parked-work read model; inspect and discard; recover and retry as decisions 1 and 2 settle them | Bulk actions; incident annotations |
| Inbox inspection, metadata only | Payload inspection in Studio |
| Release a partition and drain a node | Forced handover from an unresponsive owner |

## Costs

| Item | Cost | Status |
|---|---|---|
| Trace context | About 27 bytes per message row write in binary; 16 bytes per sampled journal row | Estimated, unmeasured |
| Metrics | In memory, from poll answers and the owner's memory, except the depth beyond a full poll, which reuses question 3's capped count (once per behind virtual partition per sample interval); the parked gauge reads the read model's count | No database work of their own on the hot path |
| Park keeping its deliveries (decision 2) | A done mark with `KeptBy` in place of a delete for each consumed delivery and each arrival while parked, a journal row, and rows kept until the window ends; one nullable column on every message row | Only when an instance parks |
| Recover or discard | One outside add, its seen mark and one commit, plus one read of the instance's inbox rows | Per operator action |

## Not verified

- The whole contract: no trace column, instrument, recovery or inspection exists in the prototype.
- That `RestoreWaits` covers every model's waits: Native, Flowchart and StateMachine each need it shown, with kept
  events in the gap, timers that came due while parked, and jobs still in flight.
- The byte cost of the trace column, against WRITES.md's cost run.
- That restoring a kept row (done mark cleared, poison counters reset, seen at the current epoch) passes every
  provider's consume and authentication paths unchanged, and that the new transition keeps invariants 9 and 14.

## Decisions for the owner

### 1. Whether a parked instance can be recovered, and which instance continues

The parent received the faulted-child notice when its child parked, and may have moved on.

| Option | Behaviour | For | Against |
|---|---|---|---|
| **a. In place** | The same instance id and creation continues at its next revision. The parent is not told again; a later completion of the child reaches a parent that no longer registers it, and is consumed and dropped | External references, bookmarks and caller keys keep working; matches "recovery restores waits" | A terminal row becomes running again, which the viewer and retention must show as a new episode. Spec 1210's recovery contract says reopening the Park row does not implement its continuing-coordinator recovery |
| b. A linked successor | A new instance starts from the parked state with fresh waits; the parked one stays faulted | Terminal rows stay terminal | Every outside reference to the old id breaks; caller keys need re-binding |
| c. Refuse recovery of a terminal run | Today's rule: `RetryFaultedActivity` refuses a terminal run or an ended parent ([spec 1210 recovery contract](../../specs/1210-state-machine/contracts/recovery.md)). Parked work offers inspect and discard; "retry" starts a fresh run with the original start input | Nothing terminal ever changes | Release 1's retry becomes a restart; the parked run's progress is lost |
| d. Intervention without Park | Faults an operator can handle keep the instance Running with a blocking incident, as the native intervention experiment does; Park stays terminal for poison and definite commit errors, with c's rules | Matches spec 1210; parents keep waiting | Covers only faults the model can hold; poison still needs a, b or c |

**Recommendation: a for parked work, with d for authored faults the model can hold.** The viewer shows the Park and the
recovery as journal entries of the same run.

### 2. Whether Park keeps what a retry needs

| Option | Behaviour |
|---|---|
| **a. Extend Park** as described under "What Park keeps": it keeps consumed deliveries and arrivals while parked as done rows under the `Parked` window, and journals at every history level. The data lifecycle page's format item 6 then exempts a parked instance's kept deliveries | `resume` delivers what arrived while parked; `retry` also redelivers the poisoning message |
| b. Keep Park as decided on 13 November | Only `resume` is possible; release 1's "retry" restores waits on the committed state and delivers nothing again. Arrivals while parked were dropped, so a wait for a child completion, a reply or a direct resume that arrived in that time never ends after recovery |

**Recommendation: a.** It costs nothing on a commit that does not park.
