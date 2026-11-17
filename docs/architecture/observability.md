# Observability and operator actions

> **Proposed.** The kernel design awaits owner approval; this page answers design-now question 6 and does not
> authorize product implementation.

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
| Force a handover from an unresponsive owner | Host administration: a forced take, a conditional update at the expected owner and epoch that names the new owner and **increments the epoch**, so the old owner's next commit fails the fence. Ending the lease alone would not do: with the fence in the batch, the old owner's next commit at the same epoch would still land and renew its lease | Later |

### What Park keeps

Today Park consumes its deliveries (deletes them, or marks done those that carry a `keep_until`), and later arrivals are
dropped. A retry then has nothing to deliver again: only `resume` is possible. FINDINGS 25.9 records that Park does not
keep consumed deliveries in a dead-letter queue. Retry of a parked message needs two additions, which **extend** the
13 November decision; the owner adopted them on 17 November (decision 2). This section is **proposed**; none of it is
built.

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
   the default). **The window's expiry is an automatic discard:** when the timer fires, the instance's own drain
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
public sealed record ParkSelection(string Tenant, long Creation, long ParkRevision, long ParkAt);  // all required

// kernel.recover   {operation, park: ParkSelection, mode, selection}
// kernel.discard   {operation, park: ParkSelection}
// kernel.terminate {operation, creation, park: ParkSelection?}   // an operator's: park required when the row is parked
// kernel.drain       {creation, parkRevision, parkAt, generation}
// kernel.park-expiry {creation, parkRevision, parkAt, generation}
```

- **The tenant** comes from the operator's bound scope ([tenancy](tenancy.md#every-operation)), never from the payload.
- **The owner's checks before Open,** in the actor's command turn, refusing on the first that fails: the tenant equals
  the row's; the creation equals the row's and both are known; the row is parked and its Park mark equals the
  selection's. An operator's terminate that reaches a parked row without a `ParkSelection` is refused as ambiguous. A refusal
  changes nothing, is recorded in the operation's outcome and counts in `elsa.kernel.park.refused` by reason (tenant,
  run, Park, ambiguous, resolution open).
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

  So a terminate is accepted at a parked child without a `ParkSelection` when it has an issued origin, its
  `TargetCreation` equals the row's creation, and its `Sender` and `SenderCreation`, stamped or checked as above, equal
  the row's recorded `Parent` and `ParentCreation`. Any other sender, an application send
  from a non-parent included, needs a `ParkSelection`. A send from an earlier creation of the parent fails the binding
  and is refused.
- **Which path each fan-out takes.** A combiner's FailFast is the application send above. Release 1's parallel
  ForEach runs its bodies as frames inside one instance ([large fan-out](large-fan-out.md)), so its FailFast cancels
  frames in the model and sends no kernel terminate; a body's child workflow is cancelled through its parent's
  terminate of that child, the same parent-bound send. An accepted one opens the terminate drain: the child resolves as
  Cancelled, and its Complete sends the parent the one final notice every terminate resolution sends, and no other; a
  parent that has already ended consumes and drops it.
- **Internal controls are bound too.** `ParkRecord.ResolutionGeneration` grows by one at every Open of this creation.
  Both controls check the Park mark against `ParkRecord`'s Park identity, never the row's terminal mark, which a
  recover drain clears. A `kernel.drain` acts only if its creation equals the row's, its Park mark equals
  `ParkRecord`'s, and its generation equals the open Resolution's. A `kernel.park-expiry` carries the generation
  current when it was armed, and acts only if its creation equals the row's, its Park mark equals `ParkRecord`'s, no
  resolution is open, and its generation equals `ParkRecord.ResolutionGeneration`. Every Open bumps the generation, so
  an expiry already delivered or in flight when a recover's Open recalled it is dropped, and it can never open a
  discard against an active recovery. Any other control is consumed and dropped. Their message ids derive from those fields, so the controls of a
  later resolution or a successor creation never share an id with a delayed one.
- **Operators read the identity** from the parked-work read model (`ParkedItem` carries creation, Park revision and
  Park time) and from `KeptAsync`, whose pages carry the `ParkSelection` they were read under.
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
| **Open** (the `kernel.recover`, `kernel.discard` or `kernel.terminate` turn, the delete of a parked instance, or the expiry timer's turn) | Checks the operation (below); recalls the expiry timer; increments `ParkRecord.ResolutionGeneration` and writes `ParkRecord.Resolution {operation, kind, mode, generation, cursor}` on the instance row, with the cursor before the first row; for a `retry` with an explicit selection, marks each selected row `KeptBy = Selected` by its full key, with its count checked; sends the instance a `kernel.drain` bound to the generation. For recover it also restores the waits (below) and sets the instance running, so restored rows are delivered as the drain goes. From this commit on, arrivals are no longer kept. |
| **Page** (each `kernel.drain` turn) | Reads the next page of metadata: `Target = :instance AND KeptBy IS NOT NULL AND (Seq, Id, Scope, Match) > :cursor`, ordered by that tuple, at most `P` rows and `B` bytes of metadata (below), never a payload. Updates each row by its full key, with its count checked (restore or clear, below), moves the cursor to the page's last tuple, and sends the next `kernel.drain`. |
| **Complete** (the page that finds no row) | Clears `ParkRecord.Resolution`, journals the outcome with its counts, and does the kind's last step: recover ends the withholding above the cursor; discard and expiry record the disposition ("discarded" or "expired"), leave the instance Faulted and write its terminal mark; terminate sets the end status Cancelled; delete finishes the built delete. Discard, expiry, terminate and delete each send the parent one final notice (Faulted, or Cancelled for terminate), since a parent keeps waiting for a parked child ([large fan-out](large-fan-out.md), decision 5). |

- **The cursor is a tuple.** `seq` alone is not a total order: MongoDB can give two rows one `seq` from its separate
  allocation clocks and sorts by id as well ([storage model](storage-model.md#invariants-carried-by-the-records)).
  A cursor on `seq` alone would skip the rest of a tie at a page boundary, and those rows would stay kept for good,
  since the sweep skips kept rows. So the cursor, the page order and the withholding all use `(Seq, Id, Scope, Match)`,
  the store's poll order with the full key as its tie-break.

- **Restore or clear, per row.** Both recover modes restore `Arrived` rows; `retry` also restores the rows its
  selection marked `Selected` at Open (or every `Consumed` row, for retry-all). Restoring sets `Done = false`, `KeptBy = null`, `Attempts = 0`,
  `Trial` to none, `Seen = 0`, and `KeepUntil` to the ordinary outside-command window from now, by the predicate
  `Id = :id AND Scope = :scope AND Match IS NOT DISTINCT FROM :match AND Done AND KeptBy = :reason`. Every other kept
  row, and every row under discard, expiry, terminate or delete, is cleared: `KeptBy = null` and the ordinary window, by the same
  key predicate. Resetting the poison counters matters: the owner parks any delivery whose attempts reached the
  threshold, so a retried poison message or job ticket would park the instance again at once.
- **Restored rows are delivered by the ordinary poll.** They are unseen, due rows that keep their id, sender, kind,
  payload, creations and `seq`, so an authenticated kind still passes its check: a child completion to a
  creation-qualified parent needs its terminal-notice id, `Sender` and creations (`WorkflowInstance.cs`, the
  `ChildFinished` check), and a copy under a new id would be refused by `CreationProtocol.Refuse`
  ([Creation.cs](../../prototypes/kernel-model/src/Kernel.Durable/Creation.cs)). Nothing is handed over in memory
  ahead of the disk, so invariant 9 holds.
- **Order during a recover drain.** The instance runs from the open commit, but receives only rows at or below the
  cursor in the tuple order; a row above it waits, withheld as for a suspended instance. Restored rows keep their `seq`
  and the cursor passes kept rows in that order, so no message from one sender overtakes an earlier one from the same
  sender. Rows that arrive during the drain, and the tickets the open restarted, are new rows above the cursor and are
  withheld until Complete. A re-armed timer is withheld until Complete as well, whatever its `seq` (see "The re-arm"
  below), so a re-armed timer due at once fires after the drain.
- **Controls during a drain.** Suspend and resume act as usual. `kernel.drain` and `kernel.park-expiry` are kernel
  control kinds: like the other control kinds they pass withholding, are never suspects, and are exempt from the
  poison check before Park (`Worker.cs`, the `MaxAttempts` check), so a drain is never parked by its own messages.
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
  - **A terminate** opens its own drain over the rows still kept, which clears them (below).
- **The model ends during a recover drain.** The instance runs from the open, so its model can finish, be cancelled or
  end faulted by its own outcome before the last page. That end does not stop the drain: `kernel.drain` is a control
  kind and keeps its turns after the end. From the end on, each page clears the rows it reaches (`KeptBy = null` and
  the ordinary window) and restores none, since a finished instance would drop them. At Complete the withholding ends,
  so the rows above the cursor meet the ordinary rule for a finished instance (consumed and dropped), and
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
    removes the instance's waits, cancels its jobs and sends terminate to its children. No model turn runs after the
    terminate is accepted. Pending rows, restored ones included, are withheld from then on.
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
  - **A further terminate while a terminate drain is open** changes nothing and is refused as "resolution open". Its
    `ThenDelete`, if any, is not added to the open drain.
- **Delete during a drain.** A delete takes a drain over only when its qualified checks pass against the row's
  current terminal mark ([Delete during a drain](#delete-during-a-drain), below).
- **What counts toward `B`.** A page reads and writes metadata only: each row's full key (`Id`, `Scope`, `Match`),
  `Seq`, kind, sender, `KeptBy` and payload size. Restoring and clearing set flags by full key and never carry the
  payload. `B` counts the encoded metadata, so a row's payload size plays no part in its page. A row's metadata is
  bounded by the provider's declared key limits ([storage model](storage-model.md#inbox-identity-and-public-names);
  the wide-key sample in [WRITES.md](../../prototypes/kernel-model/WRITES.md#15-november-card4-physical-identity-experiment-on-landed-park)
  measured 11,008 encoded octets for a 4,096-unit key), far below `B`, and a page always takes at least its first
  row, so no row can stop a drain. A restored row's payload is read later by the ordinary poll that delivers it, under
  the poll's own limits, as for any accepted message; a cleared row's payload is never read. So a kept delivery of
  5 MiB, which MongoDB's 16 MiB document admits, moves through a page like any other. Payload size is bounded at
  admission and by the provider, and write volume's values by reference concern instance state, so nothing here
  depends on them.
- **The selection is bounded.** An explicit `retry` selection names at most `P` handles and at most 256 KiB of
  encoded handles (proposed); a larger one is refused with no change, and the operator uses retry-all, which is one
  flag. Open marks the selected kept rows `Selected` and re-arms the selected suppressed timers (see "Checks" below), so
  the Resolution never stores the selection and Open writes at most `P` rows beside the plan's waits. An interruption reverts `Selected` to `Consumed` by `Target AND KeptBy = Selected`,
  at most `P` rows, so the next recover starts from the Park's own reasons.
- **The page bound.** Proposed: `P` = 500 rows, the default `MaxBatch`, below the 1,000 rows per statement of the
  owned commit, and `B` = 4 MiB of metadata. A page is one turn's share of the partition's flush, far inside MongoDB's 16 MiB
  document and 48 MiB message limits and short against its transaction lifetime
  ([MONGO.md](../../prototypes/kernel-model/MONGO.md), M10). Pages are ordinary turns under the turn budget per pass,
  so other instances of the partition keep their share.
- **The progress gate.** Restored rows become due depth, so a drain must not restore faster than the instance takes
  them. The gate is read from durable state, so every owner evaluates it the same way:
  - `ParkRecord.Resolution` records the handle of the last row the previous page restored. Restored rows are delivered
    in their target's `seq` order, so that row is the last of its page to be seen.
  - A page waits, by sending its `kernel.drain` with a delay, until that row is **taken up**: done, or marked seen by
    a poll at the current owner's epoch (`Seen = e`). Taken up means a poll has reached it; a turn may not have
    consumed it yet. A row marked at a lost owner's epoch keeps its old non-zero `Seen`, since a take never resets it,
    so it does not count until the new owner's re-poll marks it at `e`. A row that is absent (consumed, done and swept
    after its window) counts as taken up. The owner knows this from memory; a new owner after a takeover reads it with
    one point lookup by full key, so a takeover adds no free page.
  - **While the instance is suspended the gate waits.** Resume is a control kind that passes withholding, so it always
    reaches the instance and releases the gate; suspension cannot deadlock the drain.
  - **A row the model holds counts as taken up**, because a held row may wait for a later row, perhaps one above the
    cursor that only Complete releases. Counting it otherwise could stall the drain for good.
  - **The bound, stated honestly:** the drain's own addition to due depth is at most one page of restored rows not
    yet taken up plus the rows the model holds. Held rows stay due and unconsumed, and the built `Hold` hands them
    back to every turn (FINDINGS section 14), so a model that defers everything (a StateMachine during a suspended action) can
    hold the whole backlog in memory, as it would hold the same rows arriving live. A cap on held restored rows was
    rejected: reaching it with no progress would need its own incident path, and its cost would fall on exactly the
    models that hold for a later row. The `elsa.kernel.drain.held` gauge reports held restored rows, so an operator
    sees that cost.
  - **The partition's backlog is no part of the gate.** The trace it avoids: rows that arrive during a recover drain
    are withheld above the cursor, yet they count in the partition's backlog; a gate on that backlog above the high
    water mark would make every page wait, the withheld rows would never be released, and the backlog would never
    fall. Unrelated ingress is still refused by the ingress threshold alone, which counts the withheld rows until
    Complete releases them.
- **Crash and takeover.** The cursor and the next `kernel.drain` commit with each page, so a new owner continues from
  the cursor. A page whose commit outcome is unknown is reset and taken as any commit is; rows a landed page restored
  or cleared no longer match the page read, so the drain neither repeats nor skips a row.
- **When it is complete.** The open's reply says the resolution started; the operation's outcome (complete, with
  restored, cleared and refused counts, or interrupted) appears in the parked-work read model by operation id, and a
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
| Delete, a taken-over drain, or a terminate with `ThenDelete` | As in the rows above | As in the rows above | Checks 1 to 3 as in the rows above; a delete that passes them changes nothing and is refused as "in progress", with the operation that will delete named in its outcome |

**Where "in progress" sits.** It is tested after checks 1 to 3 and before check 4, so a delete from another tenant, for
another creation or naming another mark gets the reason of the check it fails (tenant, run or transition) and learns
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
     marker. A protected row stays, ended under the mark of step 1, which is journalled as any end, and the delete's
     outcome is "protected".

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
  row, or during a recover whose model runs, the terminate is a drain, and its Complete runs steps 1 to 3. A parked
  row has not ended for this purpose, since its Park is unresolved. During a recover drain whose model has ended, the
  run has ended, so the command takes the qualified-delete path of an ended run (checks 1, 2 and 5) and takes the drain
  over.

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
2. A `resume` recover R opens: the row's mark is cleared, `ParkRecord` keeps r1, and A runs. Page 1 reads 500 rows,
   restores the `Arrived` ones and clears the rest, and the model takes some of the restored rows.
3. A terminate T of A is accepted. Its Open clears R's Resolution and opens its own, stops the model's turns, removes
   the waits, cancels the jobs and terminates A's children. A shows Running, "terminating", with no mark.
4. D1 arrives. The row has no mark, so check 3 fails: D1 is refused as "transition", nothing changes, and
   `elsa.kernel.delete.refused` counts it.
5. T's pages clear the 700 rows still kept, and the page that finds none is its Complete. Complete records Cancelled
   with mark r2, resolves the Park and sends P one Cancelled notice. The restored rows A had not consumed meet the rule
   for a finished instance and are dropped.
6. P consumes the notice once; the instance list shows A Cancelled under r2.
7. An operator sends delete D2 (Operator, r2). Every check passes and `ProtectsDeletion` does not protect A, so A is
   deleted, with the deletion marker as the commit's journal row. P receives nothing more.

Had T reached A while it was parked, without R, D1 would have named the row's mark at step 4 and taken the drain over,
and T's Complete would have deleted A after the same single Cancelled notice.

### Recover

`kernel.recover {operation, park, mode, selection}` is a control kind: the actor type handles it alone and
calls one new model hook.

- **The contract gains a hook,** beside `UpgradeState`: `RestorePlan RestoreWaits(object state, ParkRecord park)`. It
  returns the subscriptions, timers and jobs the model's committed state still waits on, each timer and job ticket by
  the model's own id. Native, Flowchart and StateMachine each implement it.
- **Checks.** The `ParkSelection` matches (above), no resolution is open, and the instance is recoverable under
  decision 1. After the window's expiry the Park is resolved, so recover is refused in
  both modes. Open splits the selection before it writes anything, reading the disposition first, as the lookup does:
  a handle that names a timer this Park retains as `Suppressed` (below) goes to the re-arm path (below), even while
  its row is still kept, and is never marked `Selected` nor counted in the marking; any other handle that names a row
  this Park keeps with reason `Consumed` goes to the `Selected` marking and its count check; a handle that is neither refuses the whole command before Open. A refusal
  changes nothing and is recorded.
- **Subscriptions** were removed by Park and are restored from the plan in the open commit. A wait that asked for kept
  events receives the events published while it was parked, within their keep window; a wait that did not misses them,
  as the owner decided.
- **Timers and jobs are decided by point lookups that cannot go stale.** For each timer and job ticket in the plan,
  layer 2 maps the model's id to the full key of the instance's own send and looks that row up. Only the instance's own
  turns change its kept rows (the sweep skips them), and its turns are serial under the partition fence, so what the
  lookup sees is what the open commit and the later drain pages write against. The writes still carry the lookups as
  preconditions, as a defence against a defect: a restarted ticket is a create-only insert under its new generation's
  key, a re-arm is the count-checked revive-or-insert below, and each drain update has its count checked. A count mismatch answers "not landed", so the owner resets and
  computes the step again from the rows as they are, under the same operation id; a failed create-only insert is a
  definite error, which parks the instance again under the commit's failure rule, and the operator can repeat the
  recover.

  **Retained dispositions.** A decision a resolution takes about a timer or ticket must survive an interruption, or a
  second resume could undo it. `ParkRecord.Dispositions` keeps, for each timer and ticket of the plan, the decision
  taken under this Park: `Suppressed` (a Park-consumed timer that a resume left unarmed), `Rearmed` (a timer this Park
  has armed once) or `Restarted(g)` (a job restarted under ticket generation `g`). Open writes them; a re-park keeps them; they are cleared only when the Park
  resolves. The set is bounded by the model's waits, the same bound as the plan. The lookup reads the disposition
  first, then the row:

  | What the lookup finds for a timer or ticket the plan waits on | `resume` | `retry` |
  |---|---|---|
  | Pending | Stays | Stays |
  | Kept, `Arrived` (a timer that fired, or a job result that came, while parked) | Restored by the drain | Restored by the drain |
  | Kept, `Consumed` or `Selected`, and the selection names it, with no `Suppressed` disposition | Not applicable | Restored by the drain |
  | Kept, `Consumed`, not selected | Cleared; a timer stays unarmed and is recorded `Suppressed`; a job restarts and is recorded `Restarted(g)` | The same |
  | Disposition `Suppressed`, whatever the row now is (still kept because an interruption came before its page, ordinary done after a page cleared it, or swept); the disposition takes precedence over the row | Stays unarmed | Stays unarmed, unless the selection names it: then re-armed, and recorded `Rearmed` |
  | Disposition `Rearmed` | The re-arm never runs again; the timer's row decides by the rows above (pending: stays; kept after a re-park consumed it: as a kept row) | The same; a selection naming it re-arms nothing |
  | Disposition `Restarted(g)` | The generation-`g` ticket's row decides, by the rows above | The same |
  | No disposition and no `KeptBy`: ordinary done or absent | A timer is re-armed and recorded `Rearmed`; a job restarts. Done and absent are treated alike, so the sweep's timing cannot change the outcome | The same |

  **The re-arm.** A timer is re-armed under its own full key, by the branch the disposition step's point lookup chose:
  - **The row is present and done:** an update by full key, `WHERE ... AND Done`, with a count check of 1. A still-kept
    row is done with `KeptBy` set, so it matches too, and the same update releases it. It sets `Done = false`,
    `KeptBy` null, `Attempts = 0`, `Trial` none, `Seen = 0`, the original due time, and `KeepUntil` to
    the value a newly inserted timer has, so the revived row carries no trace of the page that cleared it.
  - **The row is absent:** a create-only insert of the same row.

  These are plain statements on every engine, with one meaning per count: an update by key reports one matched row
  on each (MySQL with `UseAffectedRows=false` reports matched rows; MongoDB's `matchedCount`). A pending row needs no
  re-arm, since the lookup's "Pending" row leaves it. A count of zero on the update, the sweep having removed the row
  after the lookup, answers "not landed", so the owner resets and recomputes the step, and the lookup then finds the
  row absent and inserts it. A key violation on the insert can only come from a defect; it is a definite error, and
  the instance re-parks under the commit's failure rule.

  **When a re-armed timer fires.** A revived row keeps its original, low `seq`, at or below the drain cursor, and an
  inserted row gets a new one above it. So a re-armed timer is withheld until Complete whatever its `seq`: while the
  Resolution is open, a timer whose disposition is `Rearmed` is withheld. It fires at the same point, after Complete,
  whichever branch armed it, so the sweep's timing changes neither whether nor when it fires. Reviving the row keeps
  the model's own timer id, so `Recall` by that id still works; a new timer generation mapped back, as tickets have,
  was rejected because `Recall` and the timer's id would then need the same mapping.

  A restarted job's ticket id derives from instance, key, creation and the recovery's ordinal (`Worker.TicketId` takes
  the first three today), so it collides with neither the old ticket nor its kept row. The model never sees the
  generation: the job context and `job-finished` carry the model's own key, and the kernel maps the generation back. A
  result of the old generation matches no live ticket and is dropped.
- **Side effects** of a retried turn may repeat, with the same effect keys.

**Discard** `{operation, park}` makes the same Park checks and opens a discard drain. The instance stays
faulted, and retention or a delete removes it.

**Store and kernel changes.** All of this is kernel behaviour and changes the store contract; every provider and the
store conformance suite need it:

- `KeptBy` as a kept reason (`Consumed` or `Arrived`), set by the consuming commit and cleared by a drain;
- a new transition that clears `Done` on a restored row, with its poison counters reset;
- the sweep's predicate excluding kept rows;
- a paged read of an instance's kept rows after a cursor, and point lookups by full key, served by an index on
  `(Target, Seq)` partial on `KeptBy IS NOT NULL` where the engine supports it (MySQL takes the full index);
- the delivery handle, encoded and decoded by layer 2 only;
- `ParkRecord.Resolution` with the handle of the previous page's last restored row, which the progress gate reads, and
  its cursor on `(Seq, Id, Scope, Match)`, the store's poll order with the full key as
  tie-break, and the withholding of rows above a recover drain's cursor;
- two kernel control kinds, `kernel.drain` and `kernel.park-expiry`, which pass withholding, are never suspects and
  are exempt from the poison check;
- the consume rule that keeps no arrival after a resolution opens, and the interruption rules that resume keeping;
- the ticket generation that `Worker.TicketId` gains for jobs restarted by a recover;
- the incident binding: `ParkSelection` on every Park operation, checked against the row's tenant, creation and Park
  mark, and `ParkRecord.ResolutionGeneration` binding `kernel.drain` and `kernel.park-expiry`;
- `ParkRecord.Dispositions`, retained until the Park resolves;
- the input bounds: metadata-only pages, the `Selected` reason and the selection's maximum.

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
    ValueTask<KeptPage> KeptAsync(InstanceRef target, PageToken? from, CancellationToken cancellationToken);
}

record ParkedItem(InstanceRef Instance, long Creation, long ParkRevision, long ParkAt, /* model, definition, fault, ... */ string Reason);
record KeptPage(ParkSelection Park, IReadOnlyList<KeptDelivery> Rows, PageToken? Next);   // the Park these rows belong to

record PendingDelivery(string Handle, string Id, string Kind, string? Sender, DateTimeOffset? Due,
                       DeliveryState State, int Attempts, int Size);   // State: Pending, Held, OnTrial
record KeptDelivery(string Handle, string Id, string Kind, string? Sender, KeptReason Reason, int Size);
                       // Handle: the opaque full key (Id, Scope, Match); Reason: the row's KeptBy, Consumed, Arrived or Selected
record ParkedDetail(ParkedItem Item, IReadOnlyList<string> ConsumedHandles, ResolutionState? Resolution);
                       // ConsumedHandles from the Park's journal entries; no kept-row list and no reasons
```

- **Ids are opaque to layer 3.** Today an `InstanceRef` id embeds its partition (`3/order-17` in
  [Addresses.cs](../../prototypes/kernel-model/src/Kernel.Runtime/Addresses.cs)), and `InstanceRef.Partition` parses it.
  The contracts package layer 3 builds against exposes no such property: layer 3 compares and stores ids as strings and
  never parses them; only layer 2 derives a partition from an id.
- **Where an operator finds a handle.** `KeptAsync` lists an instance's kept rows, in pages, in the drain's tuple
  order, each with its handle and reason; a `retry` selection takes its handles from there. It reads rows on the
  delivery seam and is eventually consistent like `PendingAsync`: a row a drain has just restored or cleared can still
  appear, and the recover's checks refuse a selection that no longer matches.
- **Delivery handles are opaque too.** A handle encodes the store's full message key, so two rows with one wire id
  never share a handle; layer 3 passes handles back unchanged and never builds or parses one.
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
| The parked-work read model; inspect and discard; recover in place and retry (decisions 1 and 2) | Bulk actions; incident annotations |
| Inbox inspection, metadata only | Payload inspection in Studio |
| Release a partition and drain a node | Forced handover from an unresponsive owner |

## Costs

| Item | Cost | Status |
|---|---|---|
| Trace context | About 27 bytes per message row write in binary; 16 bytes per sampled journal row | Estimated, unmeasured |
| Metrics | In memory, from poll answers and the owner's memory, except the depth beyond a full poll, which reuses question 3's capped count (once per behind virtual partition per sample interval); the parked gauge reads the read model's count | No database work of their own on the hot path |
| Park keeping its deliveries (decision 2) | A done mark with `KeptBy` in place of a delete for each consumed delivery and each arrival while parked, a journal row, and rows kept until their Park is resolved; one nullable column on every message row, and one partial index written only for kept rows | Only when an instance parks |
| Recover, discard, delete of a parked instance, expiry | The open: one outside add (none for expiry), its seen mark and one commit, with one point lookup per timer and job the plan names and at most `P` selection marks. Then one turn per page of up to `P` rows: one paged read and the page's share of a commit. Completion takes one more page turn | Per resolution; proportional to the kept rows, never in one transaction |

## Not verified

- The whole contract: no trace column, instrument, recovery or inspection exists in the prototype.
- That `RestoreWaits` covers every model's waits: Native, Flowchart and StateMachine each need it shown, with kept
  events in the gap, timers that came due while parked, and jobs still in flight.
- The byte cost of the trace column, against WRITES.md's cost run.
- That restoring a kept row (done mark cleared, poison counters reset, unseen) passes every provider's consume and
  authentication paths unchanged, and that the new transition keeps invariants 4, 9 and 14.
- The drain: its page bounds, the withholding above the cursor, and its cost on a busy partition (acceptance cases
  M3-1 to M3-14 below).
- The Park identity checks, the bound controls, the retained dispositions and the metadata-only pages (cases P-1 to
  P-5 and S-1 to S-4).
- The delete takeover under the row's current terminal mark and `ThenDelete` on a drain (cases DT-1 to DT-6).

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
   selection. Each page is bounded by `P` rows and `B` bytes, and nothing goes into memory ahead of the disk.
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

## Acceptance cases

Milestones are those of the [program plan](../program-goals/clean-slate-kernel.md#milestones). K3 holds each case on
the in-memory store and in the simulation, with a mutation that fails it; K4 holds the same case on every provider
(PostgreSQL, SQL Server, SQLite and MySQL in both fence modes, and MongoDB on a replica set).

| Case | What it shows | Milestones |
|---|---|---|
| B2-1 | A private X parks the instance, an ordinary X arrives, and a second private twin of X (another scope) is also consumed by the Park. `resume` restores `(X, 0)` and clears both private rows. `retry` naming one private handle restores that row and `(X, 0)`, and clears the unselected twin. Each case asserts the full key of every row and that each restored row is delivered exactly once and each cleared row never | K3, K4 |
| B2-2 | The other order: an ordinary X is pending at the Park and consumed by it, a private X arrives later. Each row gets its own reason, and each mode restores exactly the rows the table says | K3, K4 |
| B2-3 | Two event matches with one wire id: distinct handles, each restored or cleared alone | K3, K4 |
| B2-4 | A re-park (`MaxAttempts`, and a definite commit error) consumes Y. Y is `Consumed`; `resume` clears it; `retry` can select it | K3, K4 |
| B2-5 | A `retry` naming an `Arrived` row, an unknown handle or a handle of another instance is refused with no change | K3, K11 |
| B2-6 | Mutation: classify by id alone. B2-1 and B2-2 fail | K3 |
| M1-1 | With a finite window passed and the expiry not yet run, the sweep runs before recover's read. No kept row is removed; the resulting wait is armed once; the job's durable ticket exists once | K3, K4 |
| M1-2 | The same sweep injected between the read and the commit, and after the commit. The open lands; the wait and the ticket are as in M1-1; restored rows survive the later sweep | K3, K4 |
| M1-3 | The recover's acknowledgement is lost after its commit. The operator's retry with the same operation id is a duplicate; the read model shows one outcome; no second drain runs | K3, K11 |
| M1-4 | The expiry fires before a recover arrives: recover is refused in both modes; the expiry drain clears every kept row, records "expired", sends the parent one final Faulted notice, and the `Faulted` retention window starts from its terminal mark. A recover arrives first: the expiry is recalled | K3 |
| M1-5 | Mutation: drop `KeptBy IS NULL` from the sweep's predicate. M1-1 or M1-2 fails | K3 |
| M3-1 | A Park with the null window receives 100,000 arrivals, then `resume`. Every page stays within `P` rows and `B` bytes, the drain completes, and every arrival is delivered once, in per-sender order | K3, K12 |
| M3-2 | Ownership changes at a page boundary, and a page commit has an unknown outcome. The drain continues from the committed cursor, with no row restored twice or skipped | K3, K4 |
| M3-3 | Recovery while another partition of the same node is busy, and while the same partition is busy. Other instances keep their turn share; their latency is recorded | K12 |
| M3-4 | During a drain, a sender with kept rows sends again. The new row is delivered after all of that sender's kept rows | K3 |
| M3-5 | A recover drain under a virtual partition above its high water mark: a page waits only while its previous page's last restored row is not yet taken up (done, or seen at the current epoch) or the instance is suspended, and never on the partition's backlog; unrelated ingress is refused only by the threshold's own rule | K3, K12 |
| M3-6 | A re-park during a recover drain: the Resolution is cleared; restored-but-undelivered rows and drain-time arrivals are kept as `Arrived`, the parking turn's rows as `Consumed`, unreached rows keep their reasons; `kernel.park-expiry` is re-armed for the window's remaining time; a second recover drains every row still kept and delivers each once; the re-park keeps the Park mark and the retained dispositions and reverts `Selected` rows to `Consumed`. A terminate during a recover drain, and a terminate of a parked child: no model turn runs after the terminate is accepted; its open removes the waits, cancels the jobs and terminates the children; the kept rows are cleared, the status is Cancelled at Complete, and the parent receives exactly one final notice. A delete during a recover drain whose model runs is refused with no change; a delete whose checks pass against the row's current terminal mark during a discard, expiry or terminate drain takes the drain over (DT-1), and a terminate that interrupted a running recover leaves no mark to name until its Complete (DT-2); at its Complete a taken-over terminate is first ended Cancelled with its terminal mark, then deleted, with one parent notice; while a terminate drain runs, lists show the row's current status (Faulted for a parked child, Running after a recover's open) with the sub-state "terminating"; discard and delete drains of a parked child send the parent one final Faulted notice | K3, K6 |
| M3-7 | One page on MongoDB within its document, message and transaction-lifetime limits | K4 |
| M3-8 | MongoDB: kept rows with one `seq` straddle a page boundary. The tuple cursor restores or clears every one, and none stays kept; mutation: a cursor on `seq` alone leaves rows kept. The prototype's MongoDB store sorts by `(seq, _id)` (`MongoStore.cs`), so the tuple cursor is a provider contract implemented and qualified here | K4 |
| M3-9 | A drain page crashes the process, before and after its commit. The next owner re-runs the page from the committed cursor; each row is restored or cleared once; the drain message is never put on trial or parked | K3, K4 |
| M3-10 | The model finishes, is cancelled or ends faulted by its outcome during a recover drain: the drain runs to Complete; pages after the end clear and restore nothing; withheld rows are consumed and dropped; every `KeptBy` is cleared and the Park resolved; the parent receives exactly one notice, from the end's own turn. A delete naming the end's mark after the end takes the drain over, deletes at Complete and sends no notice, and no row keeps `KeptBy`; one naming the Park's mark is refused as naming an earlier transition. The end's turn records the model's end in the Resolution; a late delivery reaching `MaxAttempts` after it records a failed input, opens no Park and leaves the drain running, and the same delivery before the end, with no end recorded, interrupts the drain as a re-park. During a discard, expiry, terminate or delete drain, a late delivery reaching `MaxAttempts` is likewise a failed late input: the instance keeps its status, the drain continues, and no Park or keeping resumes. On an ended row with no unresolved Park (after a completed discard or expiry, after a recovered instance ends, or on one that never parked) it is a failed late input that opens no Park and keeps no row. A turn that throws before the end is a re-park (M3-6) Each of the five `MaxAttempts` states is asserted on its own. The mark: when the model ends mid-drain, its end writes a terminal mark that stays through Complete, and a qualified delete naming that mark after Complete is accepted; a recover that completes with the model running leaves no mark until the next end. | K3, K6 |
| M3-11 | The deadlock trace: during a recover drain, arrivals withheld above the cursor push the virtual partition above its high water mark. The drain still completes, each page waiting only for its previous page's restored rows; the withheld rows are then released and the backlog falls. Mutation: gate on the partition's backlog, and the drain never completes | K3, K12 |
| M3-12 | `KeptAsync` returns every kept row with its handle and reason; a `retry` built from its handles restores exactly those rows; a handle a drain has since cleared is refused | K9, K11 |
| M3-13 | The gate's bounds. A suspended recovering instance restores no page until resumed, then the drain completes. A model that holds every restored row (a StateMachine during a suspended action) lets the drain complete, with due depth at most one page plus the held rows, as `elsa.kernel.drain.held` reports; K12 measures the whole owner's memory with the intended holding models, since the page bound alone does not bound it. A partition taken over repeatedly during a drain adds no page per take: each new owner reads the gate by one point lookup and treats a row seen at a lost owner's epoch as not taken up until its own re-poll marks it; a gate row consumed and swept counts as taken up | K3, K7, K12 |
| R-1 | `RestoreWaits` for Native, Flowchart and StateMachine: subscriptions, timers and jobs restored per the table above, with kept events in the gap, retained dispositions, and the interrupted-resume trace of M3-14 | K6, K7 |
| M3-14 | The interrupted-resume trace: timer T consumed by the Park is cleared unarmed in an early page; arrival X re-parks in a later page; the sweep runs before the second Open and, in a second run, after it; a takeover is injected between every pair of steps. A second `resume` leaves T unarmed in every run; a `retry` naming T re-arms it once, with the sweep before and after that retry, and a second retry after an interrupted one leaves exactly one armed T and one firing; in both sweep orders T fires at the same point, after Complete, and the revived row's `KeepUntil` equals an inserted row's; a job restarted by the first resume is not restarted again | K3, K4, K6, K7 |
| P-1 | The stale-Park trace: A parks at first revision 7 and a discard is prepared from A's `ParkSelection`; A is resolved and deleted; B reuses the key and parks at first revision 7. The command submitted after B exists is refused for its creation and changes nothing of B; submitted before the reuse, it discards A only | K3, K4 |
| P-2 | The same trace for `resume` with an empty selection, retry-all and terminate of a parked instance; and a stale command after A recovers and parks again (same creation, new Park mark) is refused | K3, K4 |
| P-3 | Mutation: the owner skips the creation check. P-1's late command discards B, and the invariant "a Park operation changes only the Park it names" fails | K3 |
| P-4 | A delayed `kernel.drain` of an interrupted resolution arrives after a second Open, a delayed `kernel.park-expiry` of a resolved Park arrives after the same creation parks again, and either reaches a successor creation: each is consumed and dropped, and no row changes. An expiry delivered or in flight when a recover's Open recalled it arrives during the drain and is dropped; after an interrupting re-park, only the expiry armed under the new generation acts | K3, K4 |
| P-5 | `ParkedItem` and `KeptAsync` expose creation and Park mark; a command built from them lands; one built before a recovery and a second Park is refused; refusals count by reason | K9, K11 |
| P-6 | The interrupted recover and the Park mark: an operator delete naming the Park mark, and separately a discard naming the `ParkSelection`, are prepared while A is parked, before a recover opens. Delivered while the recover drain runs, each is refused (the mark is cleared and a resolution is open). Delivered after an interrupting re-park, the mark is back, so the delete opens the delete drain and the discard opens the discard drain. After a recover that completes, both are refused as naming an earlier transition | K3, K11 |
| P-7 | A parent's tree terminate (composed by the actor type) and a combiner's FailFast (an application send through `ITurn.Send`) reach a parked child: each is accepted without a `ParkSelection`, opens the terminate drain, and the child resolves as Cancelled with every `KeptBy` cleared; the parent receives the one final notice and no other. An application send of `kernel.terminate` from a non-parent, an outside add naming the parent as sender, a terminate from an earlier creation of the parent, and an actor-type `Add` of a `kernel.terminate` whose `Sender` is another actor (refused at commit as a definite error) are refused | K3, K6 |
| S-1 | Kept rows whose payloads are `B` − 1, `B` and `B` + 1 bytes, and an oversized first row of 5 MiB: every page progresses on metadata alone and each restored payload is read only on delivery. Run under resume, discard and delete, with a takeover between pages | K3, K4, K12 |
| S-2 | A kept row whose handle is at the provider's key limit, as the first row of a page: the page takes it and the drain completes | K3, K4 |
| S-3 | A `retry` selection of `P` handles lands; one of `P` + 1 handles, or above 256 KiB, is refused with no change; retry-all lands for any number of `Consumed` rows; an interruption reverts every `Selected` row; a handle that is neither a kept `Consumed` row nor a suppressed timer is refused before Open; a selection that names a `Suppressed` timer re-arms it once and leaves the marking's count unchanged, with no reset, both with the sweep run before the retry and after it, each giving one armed timer and one firing; a second retry after an interruption, naming the now `Rearmed` timer, re-arms nothing; a timer that is `Suppressed` and still kept `Consumed` (a re-park came before the page that would clear it), named by a retry, is re-armed once by the revive, which also clears its `KeptBy`, fires once, is never marked `Selected`, and is skipped by the later pages | K3, K4, K11 |
| S-4 | MongoDB: a page next to a kept row of 5 MiB stays within the document, message and transaction-lifetime limits | K4 |
| R-2 | Inspect, retry, resume and discard through the operator API on the parked-work read model, by handle | K9, K11 |
| DT-1 | **A delete takes over the terminate drain of a parked child.** Preconditions: parent P waits on child A, parked at mark r1 with more than `2P` kept rows; a terminate T of A opens the terminate drain, once as P's tree terminate and once as an operator's naming A's `ParkSelection`. Interleaving: an operator delete D naming A's tenant, creation and r1 arrives after T's Open, once before the first page and once between pages, with an ownership takeover between D's commit and the next page; a Retention delete naming r1 arrives once before D and once after D's takeover; a second operator delete arrives after D's takeover; during the drain, a delete from another tenant with A's id and one naming another creation of A's key arrive, before and after D. Outcome: D takes the drain over, which continues from its cursor with no row repeated or skipped; Complete ends A Cancelled under a new mark r2, sends P exactly one Cancelled notice and deletes A in the same commit, whose only journal row is the deletion marker; no row keeps `KeptBy`; T's outcome is complete (Cancelled, then deleted by D) and D's is deleted; the Retention delete before D is refused as not eligible and after D as in progress, the second operator delete as in progress, the other tenant's as tenant and the other creation's as run, never as in progress, each with no change. Protection: with `ProtectsDeletion` true when D arrives, D is refused as protected and T completes alone; with a test model whose protection starts between D's takeover and Complete, A ends Cancelled under r2 and is kept, and D's outcome is protected. Mutations: the takeover's Complete skips `ProtectsDeletion` (the protected run is deleted); the taken-over Complete also sends the Faulted notice of a parked delete (P receives two) | K3, K4, K11 |
| DT-2 | **No separate delete qualifies while a terminate that interrupted a running recover drains.** Preconditions: P waits on A, parked at r1 with more than `2P` kept rows; an operator delete D1 naming r1 is prepared while A is parked; a `resume` recover R opens (the row's mark cleared, A Running), its first page restores rows, and the model still runs; a terminate T is accepted and opens its drain, interrupting R. Interleaving: D1 arrives after T's Open before its first page, between its pages, and after an ownership takeover between pages; a delete naming creation 0 arrives in the same window. Outcome: D1 is refused as "transition" and the creation-0 delete as ambiguous, each with no change and counted by reason; T's drain runs on and its Complete writes the Cancelled mark r2, leaves no row with `KeptBy` and sends P exactly one Cancelled notice; R's outcome is interrupted and T's is complete. After Complete: a new delete naming r1 is refused as "transition"; an operator delete D2 naming r2 deletes A, and P receives no further notice; in a second run with `ProtectsDeletion` true, D2 is refused as protected and A stays under r2; a Retention delete under r2 is refused before the `Cancelled` window has elapsed and deletes A after it. Mutation `DeleteAcceptsParkRecordMark`: when the row has no mark, check 3 compares the selection with `ParkRecord`'s Park identity; D1 then takes the drain over and deletes A, and the case fails | K3, K4, K11 |
| DT-3 | **`ThenDelete` on both origins.** Preconditions: A parked at r1 as in DT-1, and separately a running recover of A as in DT-2. Interleaving: an operator's terminate with `ThenDelete`, accepted under the terminate's checks, opens the terminate drain; in the parked origin a separate operator delete naming r1 arrives mid-drain; an ownership takeover comes between pages; each runs once with `ProtectsDeletion` false and once true. Outcome: `ThenDelete` is recorded at Open; the separate delete is refused as in progress with no change; Complete ends A Cancelled under r2, sends P exactly one Cancelled notice and, unprotected, deletes A in the same commit, whose only journal row is the deletion marker; protected, A is kept under r2 and the answer reaches the API request's reply address; no row keeps `KeptBy` in either case. A `ThenDelete` reaching a recover drain whose model has ended takes the drain over through checks 1, 2 and 5, and P receives only the end's notice. Mutation `ThenDeleteAtOpen`: the terminate deletes the row in its Open, as RD8's proposed one-turn path does for a running instance; the next `kernel.drain` finds no target, kept rows keep `KeptBy`, and the case fails | K3, K4, K11 |
| DT-4 | **Protection begins before a parked instance's delete completes.** Preconditions: P waits on A, parked at r1 with more than `2P` kept rows, under a test model whose `ProtectsDeletion` is false when the delete D (Operator, r1) arrives and turns true before the drain's Complete. Interleaving: D opens the delete drain; an ownership takeover comes between pages; protection turns true after the last page and before Complete. Outcome: no row keeps `KeptBy`; Complete leaves A Faulted under a new mark r2, sends P exactly one Faulted notice, and journals D refused as protected with the Park discarded; D's outcome is protected; A's `Faulted` retention window runs from r2, and a Retention delete under r2 after it is refused as protected while protection holds. Mutations: the protected Complete deletes A anyway; the protected Complete leaves A with no mark, so no later delete can name it | K3, K4, K11 |
| DT-5 | **The refusal order when a `ThenDelete` terminate interrupted a running recover.** Preconditions: P waits on A, parked at r1 with more than `2P` kept rows; a `resume` recover R opens, so the row has no mark, and the model still runs; an operator's terminate with `ThenDelete` is accepted and opens its drain, interrupting R. Interleaving: between pages, an Operator delete naming r1, a Retention delete naming r1, a delete from another tenant with A's id and one naming another creation arrive, with an ownership takeover between pages. Outcome: the r1 deletes are refused as "transition" (check 3 fails before "in progress" or check 4 is reached), the other tenant's as tenant and the other creation's as run, each with no change; Complete ends A Cancelled under r2, sends P exactly one Cancelled notice and deletes A, with no row keeping `KeptBy`; the terminate's outcome is deleted. Mutation: "in progress" tested before check 3; the r1 deletes then answer "in progress", and the case fails | K3, K4, K11 |
| DT-6 | **Protection against a separate delete during a drain.** Preconditions: A parked at r1 with more than `2P` kept rows, under a test model whose `ProtectsDeletion` is true throughout. Interleaving: in one run an operator's terminate without `ThenDelete` opens the terminate drain; in a second, a terminate with `ThenDelete`; in each, an Operator delete naming r1 arrives between pages. Outcome: in the first run the delete is refused at check 5 as "protected", and Complete ends A Cancelled under r2 and keeps it; in the second it is refused as "in progress", and Complete ends A Cancelled under r2 and keeps it, with the terminate's answer protected on the API request's reply address; in both, P receives exactly one Cancelled notice and no row keeps `KeptBy`. Mutations: check 5 tested before "in progress" (the second run answers "protected"); a separate delete during a protected terminate drain takes it over and deletes A | K3, K4, K11 |

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
