# Observability and operator actions

> **Proposed.** The kernel design awaits owner approval; this page answers design-now question 6 and does not
> authorize product implementation.

[Design-now question 6](../reports/clean-slate-kernel/design-sketch-draft-5.md#to-answer-in-the-design-proposal-before-the-fable-review-owner-decision-12-november)
asks for observability as a contract: trace context on every message, stable metric names, and operator actions on
parked messages, inboxes and partitions. Release 1 ships trace context and metrics "in a basic form", and its parked-work
tooling must support inspect, retry and discard
([release 1 scope](../reports/clean-slate-kernel/design-sketch-draft-5.md#release-1-scope-owner-decision-amended-15-november)).
This page proposes the contract, where each part lives in the three layers, and what release 1 builds.

## Terms

| Term | Meaning on this page |
|---|---|
| Park | The owner's decision of 13 November, as built: a fenced commit faults the instance, consumes its deliveries and removes every active subscription, keeping the last committed state, the fault and the configured history. The parent receives the faulted-child notice. |
| Parked work | A parked instance with the deliveries its Park consumed, until an operator recovers or discards it. |
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
  [DESIGN.md](../../prototypes/kernel-model/DESIGN.md), "Park").
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
| `elsa.kernel.turn` | Parent: the context of the turn's first message in `seq` order. Links: the other messages' contexts, up to 32, with a dropped-link count | actor type, model, definition, instance id, outcome |
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

**Storage.** One nullable `trace` column on `message` (the sketch's `headers`). In binary it is 26 bytes (version,
trace id, span id, flags), in text 55; `tracestate` only when present. A PostgreSQL outside insert writes about 497 B of
WAL and a message to self about 365 B for its insert and delete
([WRITES.md](../../prototypes/kernel-model/WRITES.md#the-cost-run-bytes-per-commit-without-chaos)), so the binary form
adds roughly 5 to 7 % to those rows. That is an estimate; release 1's cost run measures it with the same harness.

## Metrics

Meter `Elsa.Kernel`, instrument names in Elsa's existing style (`elsa.runtime.admission.*` in
`RuntimeAdmissionTelemetry.cs`). Attributes are small fixed sets. Instance ids, keys and payloads never become
attributes; tenant is opt-in with a cap. Per-partition series are opt-in, since 1,024 virtual partitions per gauge is
too many for most backends; by default a node reports the partitions it owns in aggregate.

| Name | Instrument, unit | Meaning | Why |
|---|---|---|---|
| `elsa.kernel.inbox.depth` | gauge, {message} | Due, unconsumed messages in the partitions this node owns, as of its last poll and its memory | Backlog, and the bound of question 3 |
| `elsa.kernel.inbox.oldest_age` | gauge, s | Time since the oldest due, unconsumed message became due | The alerting signal for a stuck or slow partition |
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

Question 3's admission controls add their own `elsa.kernel.admission.*` instruments. Gauges are sampled off the turn
path, once per pass or poll. A counter records what this node saw: a commit whose answer was lost counts as an unknown
outcome.

## Operator actions

| Action | How | Release 1 |
|---|---|---|
| Inspect parked work | The parked-work read model: instance, actor type, model, definition and version, fault, reason, when, and the deliveries the Park consumed (id, kind, sender, `seq`, size). Payloads need a separate permission | Yes |
| Retry parked work | A `kernel.recover` message in `retry` mode, naming the deliveries to deliver again | Yes |
| Recover without retry | `kernel.recover` in `resume` mode: waits restored, nothing delivered again | Yes |
| Discard parked work | A `kernel.discard` message | Yes |
| Inspect an inbox | Pending deliveries of one instance: id, kind, sender, `seq`, due, seen, trial, attempts, size; paged | Metadata only |
| Suspend, resume, terminate, delete | The built control kinds | Yes |
| Release a partition, drain a node | Host administration: asks the owner to release, through the runtime | Yes |
| Force a handover from an unresponsive owner | Host administration: a conditional update that ends the named owner's lease at the expected epoch; the fence then stops any late commit | Later |

**Park keeps what a retry needs.** Today Park consumes its deliveries: it deletes them, or marks done those that carry a `keep_until`. Proposed: Park marks them
done with a `keep_until` set by the parked-work retention (question 2's retention policy owns its length), the same
columns an outside message uses, so their payloads stay and the sweep removes them later. Park also writes a journal row at every history level, as a delete does, so the
projector can maintain the parked-work read model even at Minimal history. Neither adds work to a commit that does not
park.

**Recover.** `kernel.recover {operation, expected revision, mode, deliveries}` is handled by the actor type alone, like
the other control kinds. In one commit it checks that the instance is still parked at that revision, asks the model to
restore its waits (the owner's decision: explicit recovery restores waits), sets the status back to running and, in
`retry` mode, sends the selected deliveries again as new messages whose ids derive from the operation id. A wait that
asked for kept events receives the events published while it was parked, within their keep window; a wait that did
not misses them, as the owner decided. Kernel tickets (job, meet, publish) are never copied: the model's restore hook
issues fresh ones, as the kernel refuses an application send of those kinds. Side effects of the retried turn may
repeat, with the same effect keys.

**Discard** `{operation, expected revision}` deletes the kept deliveries and records the disposition. The instance stays
faulted, and retention or a delete removes it.

**Every operator message** is an outside add whose id is the operation id, with a `keep_until`, so a retried command
gets a duplicate-key answer and no second effect. The kernel's tenant check on sends applies. Inspecting metadata,
reading payloads, recovering, discarding and handing over are separate permissions, and each action is audited with
its principal, reason and outcome.

## Read models: how layer 3 stays off partitions

Studio and the operator API depend on the turn contract, the four seams and read models only. They name an instance by
its `InstanceRef` and never by a partition, position or SQL statement:

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
```

- The provider derives the partition from the `InstanceRef` inside layer 2. Today's `Viewer.ReadAsync` still takes a
  partition argument; its layer 3 facade drops it.
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
| The metric set above, aggregated per node | Per-partition series by default |
| The parked-work read model; inspect, retry, resume and discard | Bulk actions; incident annotations |
| Inbox inspection, metadata only | Payload inspection in Studio |
| Release a partition and drain a node | Forced handover from an unresponsive owner |

## Costs

| Item | Cost | Status |
|---|---|---|
| Trace context | 26 to 55 bytes per message row; 16 bytes per sampled journal row | Estimated, unmeasured |
| Metrics | In memory; gauges come from poll answers and the owner's memory; the parked gauge reads the read model's count | No database work on the hot path |
| Park keeping its deliveries | A done mark in place of a delete for each consumed delivery, a journal row, and rows kept until the window ends | Only when an instance parks |
| Recover or discard | One outside add, its seen mark and one commit, plus one read of the kept deliveries | Per operator action |

## Not verified

- The whole contract: no trace column, instrument, recovery or inspection exists in the prototype.
- That restoring waits through a model hook covers every model's waits; Native, StateMachine and Flowchart each need it
  shown, with kept events in the gap.
- The byte cost of the trace column, against WRITES.md's cost run.

## Decisions for the owner

### 1. Which instance a recovery continues

The parent received the faulted-child notice when its child parked, and may have moved on.

| Option | Behaviour | For | Against |
|---|---|---|---|
| **a. In place** | The same instance id and creation continues at its next revision. The parent is not told again; a later completion of the child reaches a parent that no longer registers it, and is consumed and dropped | External references, bookmarks and caller keys keep working; matches "recovery restores waits" | A terminal row becomes running again, which the viewer and retention must show as a new episode |
| b. A linked successor | A new instance starts from the parked state with fresh waits; the parked one stays faulted | Terminal rows stay terminal | Every outside reference to the old id breaks; caller keys need re-binding |

**Recommendation: a**, with the viewer showing the Park and the recovery as journal entries of the same run.
