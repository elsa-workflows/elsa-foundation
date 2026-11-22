# The foundation contract

> **Approved design, with conditions.** A coordinator design under the owner's two cards of 19 November 2026 (retire the
> Orleans adapter; layer 3 adds Elsa's capabilities behind a strong contract into which foundations plug; "the contracts
> and domain language stay as clean and small as practically possible"), with the owner's six decisions of 19 November
> ([owner decisions](#owner-decisions-19-november)). The independent architecture review, run on Claude Fable at the
> owner's choice, returned APPROVE WITH CONDITIONS on 19 November at `2825b0ed1` ([verdict](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-7010917998)): 0 Blockers,
> 0 Majors, 8 Minors and 6 Nits (fixed on this page) and 4 Conditions, FC-C1 to FC-C4, tracked in the plan's
> [gate 2 conditions](../program-goals/clean-slate-kernel.md#gate-2-conditions). K2 builds from it. Register:
> [the foundation contract](decisions.md#the-foundation-contract-19-november).

A **foundation** provides what layers 1 and 2 provide today: placement, activation, single-writer turns, the durable
inbox, the atomic outcome write, timers, jobs, events, subscriptions, children, Park and its resolutions,
poison handling and the journal. The kernel ships one, the built-in foundation on the database. **Layer 3** adds Elsa's
capabilities: actor types, execution models, activities, Studio, the operator APIs and the read models' meaning. **This
contract** is what sits between them. It is written in plain domain words. The guard list, used on this page, in the
register and in the plan's K2 row: no partition, virtual partition, epoch, fence, lease, position, node record,
`IKernelStore` or SQL appears in it. Those stay inside the built-in foundation, which keeps `IKernelStore` as its own
way to swap EF Core, MongoDB and in-memory storage. It ships as two packages (owner, 19 November; the names are
provisional): **the foundation contract**, which a foundation adapter builds against (everything under "The contract"
below), and **the model contract**, which an execution model builds against ("The model contract, built on the actor
turn").

## Measure first

What layer 3 can reach today, counted from the prototype on main (`src/Kernel.Workflows`, the `Kernel.Durable` and
`Kernel.Runtime` types its models and `WorkflowInstance` use), against the contract below. Public types and their
public members, positional record fields and enum values included, static helpers and parameters excluded; `IActorType` has 12
members on main, and the read-model records and command payloads are counted.

| Surface | Types | Members |
|---|---:|---:|
| Built today: `ITurn`, `IExecutionModel`, the metadata interfaces, `Message`, `StartSpec`, `SendOptions`, `InboxKey`, `Kinds`, `IJob`, the commit clock, `IActorType`, `ITurnServices`, `InstanceRef`, `EffectKey` and the rest | 32 | 182 |
| Plus the gate 2 additions the pages approved and nobody built (`RestoreWaits` and its plan, `TimerSpec`, `JobSpec`, `SendOptions.DueAt`, `TerminalGrowth`, `IActorType.IsCommand`, `ITurn.Instance.Tenant`, `BoundScope`) | 37 | 198 |
| Beside it, what the HTTP adapter and hosts reach for ingress and reads: `IKernelStore` (30 members), `Worker.Post`, `Requests`, `ReplyTo`, `EventRoute`, `EventRow`, `SubscriptionRow` | 7 | 50 or more |
| **After the cut, the whole foundation contract:** actor side 18 types / 95 members, ingress 11 / 39 (the operator command payload and the admission enums counted here), read models 9 / 33 | **38** | **167** |
| Layer 3's own model contract, built on the actor turn (`ITurn`, `IExecutionModel`, `IModelMetadata`, `InstanceInfo`, `WorkflowStart`, `EffectKey`), counted apart | 6 | 44 |

The reviewed revision `2825b0ed1` counted 36 types and 154 members. The review's Minors add the release 1 job context
(`IJob.AttemptId`, `AttemptNumber` and `Deadline`, owner decision 6), `ActorView.AsOf`, `Admission.RetryAfter` and the
two admission enums, nine values in all, that the page had left as strings (N4), and drop `ParkView.Reason`: 38 types
and 167 members. Parameters are not members: `now` on `ProtectsDeletion` and `timeout` on `StartJob` change no count. Members that
today reach a model only through `Message` or `InstanceRef` (eighteen and nine) and the `IActorType` members that take
rows and deliveries are the largest cut.

## The contract

Every member below has one meaning and carries a guarantee. A guarantee's case id (FC-n) is defined under
"What a foundation adapter must supply". **The foundation clock** is the one clock a foundation's readings come from:
`Now`, `dueAt`, `TimerSpec.Due`, `EndedAt` and `AsOf` are its readings, in milliseconds since the Unix epoch, and a turn
reads it once. Its promise (condition FC-C1): the readings come from one source per foundation, or from node clocks kept
within a skew bound the foundation states; a reading may step back; a timer is due when the delivering node's reading
reaches its `dueAt`; and FC-4 checks the bound. A timer with a past due time is already eligible.
The built-in in-memory store shares one clock source across its owners. Readings of that source at the
same instant have zero skew. Its worker samples that source at admission and keeps the reading for the
whole turn. FC-C1 still requires the read and end adapters to use this source. Workers on remote stores
continue to read their elapsed-time estimate during the turn. K4 qualifies one captured reading and the
provider's skew bound for those stores.

### Words

| Type | Members | Meaning and guarantee |
|---|---|---|
| `ActorRef` | `Id` | An actor's address, an opaque string. Layer 3 stores and compares it ordinally and never parses it (FC-13). A ref the foundation issued (`Header.Ref`, `Start`, `Message.Sender`, `ChildFinished.Child`) is **exact**: it names one life of the actor, its string is stable across replay and owner change once that life commits, and differs between lives, so a message to it never reaches a later actor under the same name (FC-15) |
| `ActorStatus` | `Running`, `Finished`, `Faulted`, `Cancelled` | Whether an actor still runs and how it ended. The foundation writes `Faulted` on Park and reports the status at a request's end; the actor type sets the rest through `End` |
| `History` | `Full`, `State`, `Minimal` | What a commit leaves in the journal: the turn's entries, only that the state changed, or nothing (ADR 0099 section 6; FC-11) |
| `Message` | `Id`, `Kind`, `Payload`, `Sender`, `Event` | One inbox entry. `Id` is the string `Send` or `Publish` returned, or the caller's; `Sender` is an exact ref, or null for an outside add; `Event` is set on an event delivery, whose `Kind` is always `Kinds.Event`, so no published key can pose as a command or a notice. A timer is recognised by `Sender == Header.Ref` and its id. Delivered at least once, deduplicated per sender, target and id, in one sender's order (FC-3) |
| `EventDelivered` | `Key`, `Id`, `Wait` | The event's key and id and the wait that asked. Replaces parsing the delivery id |
| `StartSpec` | `Type`, `TypeData`, `Input`, `Ephemeral`, `History` | What creates an actor: its actor type (at most 1,024 UTF-8 bytes, the start name bound), the type's own opaque data of at most 16 KiB in UTF-8 (a workflow's model, definition and state version), the input, which arrives as the `Start` message's payload, the Ephemeral choice and the history level. How long an unwritten Ephemeral actor may wait in memory is the foundation's configuration, bounded by a request's deadline (walkthrough decision 4; FC-2) |
| `Placement` | `WithParent`, `Spread` | A hint for `Start`: keep the child where its parent is, so a call tree stays cheap, or spread a wide fan-out. The foundation places |
| `IJob` | `Actor`, `Key`, `Arguments`, `AttemptId`, `AttemptNumber`, `Deadline` | What `ExecuteJobAsync` receives (owner decision 6; [job workers](job-workers.md#release-1)): the attempt's opaque id, new for every execution, and its number, a lower bound on the executions so far; the deadline, the foundation clock's reading at which a `StartJob` timeout cancels the job, or null. A job may run more than once; its result changes state once (FC-5) |
| `JobFinished` | `Key`, `Result`, `Error` | The payload of kind `job-finished`, the one message a job's completion becomes |
| `ChildFinished` | `Child`, `Status`, `Output`, `Final` | The payload of kind `child-finished`, the foundation's notice to a parent. `Final` false is the parked notice (large fan-out decision 5): the parent keeps waiting for the final one, which the resolution decides (FC-8) |
| `TimerSpec`, `JobSpec`, `SubscriptionSpec`, `RestorePlan` | `(Id, Kind, Payload, Due)`, `(Key, Arguments)`, `(Key, Wait, Keep)`, `(Timers, Jobs, Subscriptions)` | The waits an actor's committed state holds, as `RestoreWaits` returns them; a recover re-arms each from the spec alone. A job's effect key travels inside its `Arguments`, as `StartJob` and `IJob` carry it (observability, [recover](observability.md#recover); FC-10) |
| `Kinds` | `Start`, `JobFinished`, `Event`, `ChildFinished`; the commands `Suspend`, `Resume`, `Terminate`, `Delete`, `Recover`, `Discard`, `Migrate`; `IsCommand(kind)` | The kinds the foundation produces or treats apart. The command list is closed and grows only by amendment (register, third review R3-M4). A command passes withholding, is delivered alone and reaches an ended actor. `Suspend`, `Resume`, `Migrate` and `Terminate` are executed by the actor type in its turn, except a `Terminate` of a parked actor, which is the foundation's own resolution; `Recover`, `Discard` and `Delete` are the foundation's resolutions, with the actor type asked only through `ProtectsDeletion` (FC-9, FC-10) |
| `ActorHeader` | `Ref`, `Type`, `Tenant`, `Status`, `Fault`, `Parent`, `EndedAt`, `TypeData`, `Labels` | What the foundation keeps of every actor beside its opaque state, present from the first turn, and what read models show. `EndedAt` is the foundation clock's reading at the actor's last ending, written by the foundation; the end mark the qualified delete names travels inside `Selection` (data lifecycle B1; owner decision 2). `TypeData` is the actor type's own opaque field, capped at 16 KiB in UTF-8. `Labels` are at most 4 indexable name-value pairs the type exposes (a definition, a version), each name and value within 1,024 bytes (beside R3-M3's `N`), projected into the read model, so `ListAsync` over them is eventually consistent; both set by `SetHeader` |

### What layer 3 supplies: `IActorType`

| Member | Meaning and promise |
|---|---|
| `Type` | The actor type's key, stored as `ActorHeader.Type` and named by `StartSpec.Type`; used to route a message to the type |
| `Withholds(ActorHeader)` | True when the actor may not be given ordinary messages now: suspended, or its `TypeData` says a newer version wrote it than this node has (F14). Commands still pass |
| `TurnAsync(IActorTurn)` | One turn. The actor type reads the inbox and state in memory and produces everything through the turn; nothing leaves before the commit. Its commands arrive here as messages. A throw parks the actor (FC-10) |
| `ExecuteJobAsync(IJob, token)` | A background job's code, outside the actor's turns; at least once |
| `RestoreWaits(ActorHeader, state)` | The plan of waits the committed state holds, for a recover |
| `TerminalGrowth(ActorHeader, state)` | An upper bound on what the type's end and terminate paths add to the state, in bytes of the opaque state string as handed over; the foundation scales it for its own encoding (seventh review R7-B1; FC-17) |
| `ProtectsDeletion(ActorHeader, state, now)` | True while the type still needs the ended actor kept (a retention it owes, measured from `EndedAt` against `now`, the foundation clock's reading, since the ask comes outside any turn); the foundation asks before every delete, a `ThenDelete` included (R3-M5) |

Layer 3 promises in return: a turn is a function of inbox, state and `Now` except through the turn's members; it reads
no other actor's state; it stores refs, handles, selections and page tokens as strings; it keeps its state readable
across its own versions; it never names a tenant; and its waits come back exactly from `RestoreWaits`.

### What a turn receives and returns: `IActorTurn`

| Member | Meaning and guarantee |
|---|---|
| `Header` | The actor's `ActorHeader` as committed, present on the first turn too |
| `Now` | The foundation clock, read once for the turn; the clock `dueAt` and `TimerSpec.Due` use |
| `Inbox` | The messages this turn is given, in delivery order (FC-3) |
| `State` get, set | The opaque state; what is set is what the commit writes |
| `Ambient` | What the request on this node gave: this activation only, never stored. A request fanned out to an event key gives it only to a waiter whose turn runs on the node that took the request (owner, 20 November) |
| `Emit(channel, item)` | Live output to the open requests whose message this actor consumed, first response wins; released when the commit lands, or at once when there is nothing to write, unless the request opted out (FC-14) |
| `Reply(message, channel, item)` | The answer to one consumed request, once, after the commit (FC-14, FC-16) |
| `Send(target, kind, payload, dueAt)` → id | A message recorded with this commit and delivered later, at least once, under the returned id; with `dueAt`, a timer delivered no earlier than that reading of the foundation clock, written unchanged (FC-2, FC-3, FC-4) |
| `Recall(id)` | Takes back an undelivered message to self (a timer); its row goes with this commit (FC-4) |
| `Start(spec, placement, detached)` → ref | A child whose exact ref is allocated in this commit; it reports back as `child-finished` and a terminate goes on to it, unless detached (FC-8) |
| `StartJob(key, arguments, timeout)`, `CancelJob(key)` | The job's intent is recorded with the commit and runs after it; with `timeout`, the foundation cancels the job at the deadline and gives the actor a `JobFinished` with a timeout error, one timer per job that asks (owner decision 6); after a cancel the result is never applied; past the poison threshold the job parks the actor (FC-5) |
| `Hold(message)` | Handed to the next turn again, in its place in the order (FC-18) |
| `Subscribe(key, wait, keep)`, `Unsubscribe(key, wait)` | A wait for events of the key from this commit on; `keep` also gives it the kept events (owner default: off) (FC-6, FC-7) |
| `Publish(key, payload, keepFor)` → id | An event, durable with this commit, delivered to every subscription committed before it and kept for `keepFor` for waits that ask (FC-6) |
| `Record(entry)` | A journal entry of this commit, at the actor's history level (FC-11) |
| `End(status, output)` | The actor's status becomes final with this commit: the parent is notified, waits are removed, the foundation writes `EndedAt` and the end mark. Later turns still run for commands and for the type's own messages to an ended actor (a metadata query), and none of them changes the status (FC-15). The foundation sends the terminate cascade to the registered children and the idempotent job cancel when the actor's turn consumes a `Terminate`, whatever that turn does, and on `End(Cancelled)`; never on `End(Finished)` or `End(Faulted)` (FC-8) |
| `SetHeader(typeData, labels)` | Replaces the actor type's bounded `TypeData` and `Labels` with this commit (a rebind, a suspend flag, a new state version). Exceeding either limit fails the turn |

### Ingress: `IIngress`

What the operator API, the HTTP adapter and the ingress adapters (brokers, webhooks, schedulers as idempotent adds)
call. Each operation takes a `BoundScope` (`Tenant`; internal constructor, issued only by the hosting adapter,
[tenancy](tenancy.md#where-the-tenant-comes-from)) and refuses without one (FC-12). A **`Target`** is one of
`Ref(ref)` (an actor), `Key(callerKey, spec)` (the actor the caller key resolves to in this tenant; with a `spec`,
created from it when absent, and refused as a repeat only when it would re-create an actor deleted inside the key's
window, walkthrough decision 7; with no `spec`, resolve only), `Event(key)` (every actor waiting on the event key: an
outside publish; a request to it with no live waiter and no start subscription on the key publishes nothing, FC-6) or `Start(spec)` (a new actor).

| Member | Meaning and guarantee |
|---|---|
| `SendAsync(scope, target, id, kind, payload, options)` → `Admission` | An outside add, with `AddOptions(DueAt, KeepFor)`, whose fields depend on the target: `DueAt` makes a send to an actor a timer, and `KeepFor` is a window. The same id to the same target twice within the deduplication window is one message, and the second answers `Duplicate`; the window is the foundation's configured one, or `KeepFor` when the send sets a smaller one. Past the actor's inbox bound it answers `Refused(InboxFull)`, over the tenant's rate `Refused(RateLimited)`, and on an overloaded node `Refused(NodeBusy)`, each with a `RetryAfter`; the load-shedding commands are exempt under an operator rate (FC-16). To `Event(key)` it is **the outside publish**: `kind` is ignored, `id` is the event id, `KeepFor` its retention window (with no live waiter the event is still `Accepted` and kept for it), `(key, id)` twice within the standard outside receipt window is one publication, and a caller id that could equal a turn's issued event id answers `Refused(InvalidEventId)` and writes nothing (owner, 18 November, R7-M1) (FC-6) |
| `RequestAsync(scope, target, id, kind, payload, add, request)` → `Reply` | A send whose answer is awaited: one `Reply`, the first live output (an `Emit` or `Reply`) of an actor the target names, or the end status if the actor ends without one, or the status and error at the deadline, under the plan's [HTTP contract](../program-goals/clean-slate-kernel.md#the-http-error-and-timeout-contract-k8-proposed). To `Event(key)` it is the outside publish above, fanned out to every live waiter and answered by the first live output of any of them: a coordinator design choice that replaces the prototype's one-waiter HTTP resume (`Live.cs` 108–110). Only a waiter whose turn runs on the node that took the request gets the `Ambient`; waiters elsewhere get none, and the first live output still answers (FC-14; owner, 20 November). A start subscription on the key counts as a live waiter: the actor it starts is reached like any other waiter, and the first live output of any waiter, the started one included, answers the request (FC-14), so `SendAsync` and `RequestAsync` to `Event(key)` reach the same actors (a coordinator design choice, 20 November). With no live wait and no start subscription matching the key it answers at once with `Error` "no waiter" and no status, which K8 maps (condition FC-C2). "No waiter" means the request published nothing: no wait consumes it, now or later, and keeping waits never see it, so a retry is safe. A caller who wants the event kept sends it with `SendAsync` to `Event(key)` with `KeepFor` (owner, 20 November). `add` is the same `AddOptions` as `SendAsync` takes, and a request with `DueAt` set is refused; `RequestOptions(Ambient, Deadline, ReleaseBeforeCommit, Inline)`: the request's ambient value for the actor's first turn on this node (on a fan-out to an event key, for the waiters whose turns run on this node only); the deadline, which also bounds an Ephemeral actor's in-memory wait; the per-route opt-out that releases output when the turn ends, before its commit lands; and the per-route choice to run the first turn in the caller's process when the foundation can (owner, 12 November) (FC-14, FC-16) |
| `SetStartAsync(scope, key, wait, spec)` | A start subscription: every event of the key starts an actor from `spec`; a null `spec` removes it (an unpublish) (FC-7) |

`Admission(Outcome, Actor, Reason, RetryAfter)`: an `AdmissionOutcome`, `Accepted`, `Duplicate` or `Refused`; on a refusal
a `RefusalReason`, `RateLimited` (the tenant's rate, HTTP 429), `NodeBusy` or `InboxFull` (503), each with `RetryAfter`
(fairness decision 1), `InvalidEventId`, `Repeat` or `StaleSelection`; and the ref the call created or resolved.
For `SendAsync` to `Event(key)`, `Admission.Publication` carries the stored correlation key in the caller’s tenant scope,
the stored event id, its incarnation and its current status (`Pending`, `Matched` or `DeadLettered`). `Accepted`
means the event and its work ticket committed. The owner can match subscriptions in later pages. A duplicate
returns the original spelling and incarnation. The receipt window and the event retention window are independent.
Each wait receives broadcasts in match commit order. Independently matched broadcasts can reach two waits in
different orders. Direct messages retain FIFO from one sender to one target (FC-3 and FC-6, owner, 22 November).
`Reply(Channel, Item, Status, Error)`. An operator's `Recover`, `Discard`, `Delete` or `Terminate` carries an
**`OperatorCommand(Selection, Mode, Handles, Reason, ThenDelete)`** with **`RecoverMode`** `Resume`, `Retry` or
`RetryAll`: `Selection` is the opaque token a read model handed out for one actor, one life and one Park or end mark,
and the foundation refuses a command whose selection no longer matches, with no change; `Handles` are the opaque
delivery handles of a retry, from `InboxAsync`; `ThenDelete` is accepted on an operator's terminate only (FC-9).

### Read models: `IActorReads`

In contract words, replacing the observability page's sketch, whose records name an `InstanceRef`, a creation, a Park
revision and a trial state ([read models](observability.md#read-models-how-layer-3-stays-off-partitions); that page is
reworded when this one is approved). Each takes a `BoundScope`; every page carries `AsOf`, the foundation clock's
reading it is current to; handles, selections and page tokens are opaque; no answer shows an uncommitted entry (FC-11).

| Member or record | Meaning |
|---|---|
| `GetAsync(scope, ref)` → `ActorView` | One actor's header, its `Selection` for an operator command, its `Park` when parked, and `AsOf`, the foundation clock's reading the answer is current to |
| `ListAsync(scope, query, from)` → `Page<ActorView>` | The tenant's actors. Omit `from` on the first read and use the previous page's opaque `Next` to continue with the same tenant and query. `ActorQuery(Type, Status, Labels, EndedBefore, Parked)` filters by actor type, status, the type's projected labels (a definition, a version), end time (retention) and whether a Park is unresolved; the parked-work list is this query. An actor type builds its own read models from its labels and the tail, with no tenant feed (owner decision 2) |
| `TailAsync(scope, ref, from)` → `Page<JournalEntry>` | One actor's journal entries after an opaque `PageToken`, in commit order |
| `InboxAsync(scope, ref, from)` → `Page<DeliveryView>` | An actor's inbox rows: pending, held, and the rows its Park keeps (`Consumed` or `Arrived`), each with the handle a retry names (at most P = 500 handles per retry, R2-M1) |
| `ActorView(Header, Selection, Park, AsOf)`; `ParkView(At, OpenResolution)`; `JournalEntry(Revision, Entry)`; `DeliveryView(Handle, Id, Kind, Sender, Due, State, Attempts, Size)` with `DeliveryState` `Pending`, `Held`, `Consumed`, `Arrived`; `Page<T>(Items, Next, AsOf)`; `PageToken` | The records. A Park's reason is `Header.Fault`; `OpenResolution` is the kind of the resolution under way, or null |

### The model contract, built on the actor turn

Layer 3's workflow actor type offers execution models `ITurn`: the actor turn's members except `Header`, `SetHeader`
and `End`, plus `Instance` (`Ref`, `Tenant`, `Model`, `Definition`, `Parent`), `Rebind(definition)` and
`SetStatus(status, subStatus, output)`; `Start` takes a `WorkflowStart(Model, Definition, Input, Ephemeral, History)`.
`IExecutionModel` keeps `Key`, `StateType`, `StateVersion`, `CreateState`, `UpgradeState`, `TurnAsync`,
`ExecuteJobAsync`, `RestoreWaits` and `TerminalGrowth`; `IModelMetadata` keeps `HandlesMetadata`, `MetadataAsync`,
`TerminateMetadata` and `ProtectsDeletion`. Ended metadata and deletion protection use the same persisted `EndedAt`. `EffectKey.For(origin, node, iterationPath, attempt)` and `RootPath` live
here: `{instance origin}/{node}/{iteration path}/{attempt}` (owner, 16 November), with `Instance.Ref.Id` as the origin.
Everything here is layer 3's own, ships as the model contract package (owner decision 4; the name is provisional) and
reaches the foundation only through `IActorTurn`.

## The vocabulary

### Terms the contract introduces

| Term | Why it is needed |
|---|---|
| foundation, foundation adapter, foundation clock | The owner's words of 19 November for what replaces layers 1 and 2 as a whole; the clock is the one word for what `Now`, `dueAt`, `EndedAt` and `AsOf` read |
| actor, actor type, `ActorRef`, `ActorHeader`, `TypeData`, `Labels`, `Withholds` | The three layers' own words (12 and 15 November). `ActorRef` replaces `InstanceRef` because job and plan actors are not instances; the header is what the foundation keeps beside the opaque state; `TypeData` and `Labels` are the actor type's two fields in it |
| exact ref, `EndedAt`, `Selection` | One word for "this life of this actor", so creation numbers never cross the contract; the time of its last ending; and the opaque token that names actor, life and Park or end mark for an operator command |
| `EventDelivered`, `SubscriptionSpec`, `RestorePlan` | The event delivery's own fields in place of a parsed id; the subscription entry named like `TimerSpec` and `JobSpec`; the plan that holds the three |
| `IIngress`, `Target`, `AddOptions`, `RequestOptions`, `Admission`, `AdmissionOutcome`, `RefusalReason`, `Reply`, duplicate | The one way in from outside, its four target shapes, an add's two options (a due time for an actor, a window for an event or a send's deduplication), a request's options and the two answers with their outcome and refusal values; the storage model already says "admission" |
| `AttemptId`, `AttemptNumber` | The release 1 job context's attempt id and number on `IJob`, so activity code written for release 1 runs unchanged on a remote worker (owner decision 6) |
| `OperatorCommand`, `RecoverMode`, `Final` | An operator command's payload, the recover's three modes, and the parked notice's flag |
| `IActorReads`, `ActorView`, `ActorQuery`, `ParkView`, `OpenResolution`, `JournalEntry`, `DeliveryView`, `Page`, `AsOf`, `PageToken` | The read models' words, replacing records that named instances, creations and trials |

### Terms the current contract or seams use

| Term | Disposition | Reason |
|---|---|---|
| partition, virtual partition, epoch, position, fence, lease, node record, heartbeat, take, release, retake, poll, seen, trial, `seq`, owned set, pass, database time | Into the built-in foundation | How that foundation gives one writer per actor and an atomic commit; a foundation on Orleans or Proto.Actor has none of them |
| `IKernelStore`, `CommitBatch`, `TurnCommit`, `PartitionFence`, `Committed`, `Stored`, `Polled`, `IMembership`, `Cluster`, the projector's cursor, `Viewer.ReadAsync(partition, …)`, `SweepAsync` | Into the built-in foundation | Its storage contract; still how it swaps EF Core, MongoDB and the in-memory store |
| `IActorRuntime`, `IScopeHost`, `IScopeActivation`, `IScopeCount`, `IWakeSignal`, `Worker`, `DurableHost`, `PartitionRuntime` | Into the built-in foundation | Its layer 1 seam, kept with its conformance suite (owner, 19 November) |
| `InboxKey`, scope, `Match`, the derived-id grammar `{origin}#{revision}.{ordinal}`, `TicketId`, `CreationProtocol` (origin, fingerprint, terminal notice id, retire), `StoredState`, `DurableKeyJson`, the cascade origin mark, the terminal mark's encoding | Into the built-in foundation | Identity encodings and the foundation's own authority marks; the end mark reaches layer 3 only inside `Selection`. Layer 3 compares ids as strings; the guarantee that issued and outside ids never coincide is FC-3 |
| `KeptBy`, `ReleasedBy`, `Cohort`, `P_cascade`, `kernel.drain`, `kernel.park-expiry`, `kernel.cascade`, `Incarnation`, `CatchUpId`, `CatchUpCover`, `EventHorizon`, publish and meet tickets, `kernel.job`, `kernel.meet`, `kernel.publish`, `JobGeneration`, `ParkRecord` | Into the built-in foundation | The gate 2 mechanisms behind Park, recovery, termination and event matching. The guarantees stay (FC-6, FC-9, FC-10); the mechanisms are one foundation's. `KeptBy`'s two reasons surface as `DeliveryState` `Consumed` and `Arrived` |
| `Requests`, `ReplyTo.Of`, `ReplyTo.Parse`, `IReplyTransport`, `Worker.Post`, `RespondBeforeCommit`, `Events.PublishAsync`, `Events.SubscribeAsync`, `IKeyAdmission` | Into the built-in foundation, behind `SendAsync`, `RequestAsync` and `SetStartAsync` | The reply route between nodes is a transport detail; the per-route opt-out is `RequestOptions.ReleaseBeforeCommit`; the outside publish is a send to `Target.Event` |
| the metrics (`elsa.kernel.*`, [observability](observability.md#metrics)) | Split: the neutral ones (turns, commits, jobs, Parks, inbox depth, the recovery gauges) every foundation reports under the stable names; the partition-, lease- and fence-named ones are the built-in foundation's | The names are approved for release 1 (K11); a foundation adapter reports what the contract can see |
| the dead-letter read model and replay command (a start subscription's or an outside publish's failed matching, fifth review R5-M2) | The built-in foundation's own operator surface in release 1 (K11), reached beside `IActorReads`; the contract gains a dead-letter read and replay only when a second foundation needs them | A dead letter names no actor, so it has no place in `IActorReads` yet; the K11 evidence shows where an operator sees and replays it |
| `Flaw`, `ITurnServices.Has` | Dropped from the contract | Mutations are the built-in foundation's tests; models keep their own flaw enums |
| `ITurnServices` (`Partitions`, `ShortWait`, `CommitClockAvailable`, `CreationIdentityAvailable`, `Cancelling`) | Dropped | Capability flags are refused by the profiles decision; the short wait is foundation configuration; exact refs are every foundation's (FC-15) |
| `InstanceRef.Partition`, `In`, `Spread`, `ForChild`, `ForEvent`, `EventStartAddress`, `ChildAddress`, `GeneratedAddress` | Dropped | Address parsing (the plan's K2 row). `Placement` is the hint that replaces `Spread` |
| `Message.Target`, `Start`, `Due`, `KeepUntil`, `ReplyTo`, `TargetCreation`, `SenderCreation`, `InboxScope`, `EventMatch`, `InboxKey`, `WithInboxScope`, `AsIssued`, `AsOrdinary`, `Read<T>` | Dropped | Row fields and encodings, and a JSON helper that becomes a layer 3 extension. The target is `Header.Ref`; the input is the `Start` message's payload; the reply route is behind `Reply`; creations are inside exact refs. The row columns `TargetCreation` and `SenderCreation` stay in the built-in foundation unchanged |
| `Events.Delivered(message)` | Replaced by `Message.Event` | Parsing a delivery id was friction F-N3 already; every event delivery has kind `Kinds.Event` and its key in `Event.Key` |
| `SendOptions` (`Delay`, `Start`, `TargetCreation`, `DueAt`), `ChildAddress(child)`, `CreationAddress`, `CreationStart`, `InstanceInfo.Creation`, `ParentCreation`, `StartSpec.ShortWait` | Dropped | One `dueAt` argument carries the approved absolute due; exact refs carry the creation; a send creates nothing, `Start` does; the short wait is configuration bounded by a request's deadline |
| `Redelivered(message)` | Dropped | A hint with no guarantee ("false proves nothing"); no model's logic reads it, only a mutation |
| `IStateFreeTerminalMetadata`, `TerminalIdentity`, `IIdentityMetadataTurn`, `IdentityMetadataUnavailable`, `CanUseIdentityOnly`, `ProtectsIdentityDeletion`, `IdentityMetadataAsync`, `ModelMetadataUnavailable`, `InstanceRow.IdentityOnly`, `IdentityMetadataRequired`; `ICommitClock`, `ICommitClockObservation`, `CommitClockFacts`, `WorkflowCommitClock`, `InstanceRow.CommitTimes` | Dropped (owner decision 3) | Two optimisations of the prototype's admission experiments: an ended keyed Ephemeral row that keeps no state, and commit-time facts a model stamps and resolves. Both are on no approved page, and EF Core and MongoDB refuse clock-bearing writes today. `IModelMetadata` with the state and the foundation's `EndedAt` keep retention and deletion protection |
| `IActorType.SingleMessage` (both), `SelectsAlone`, `RevisitsHeld`, `Command`, `Park` (both), `Write`, `SelfReference`, `TurnInput`, `TurnOutcome`, `Delivery`, `InstanceRow`, `DurableTurn` | Replaced by `IActorTurn`, `Kinds.IsCommand` and the foundation's own Park | Delivery batching, row building and the Park write belong to the foundation; a command is a message like any other to the actor type, with the closed list saying which are delivered apart and which the foundation executes |
| `InstanceStatus` | Kept as `ActorStatus` | Generic (FINDINGS L2): the foundation writes `Faulted` and reports the status at a request's end |
| `StartSpec.Model`, `Definition`, `Parent`; `InstanceRow.Model`, `Definition`, `Children`, `Suspended`, `StateVersion` | Into `TypeData` and `Labels` (model, definition, suspended, state version) and the foundation's child registry (children) | FINDINGS L1 and L4: the actor record keeps actor-generic columns only (owner, 15 November); `Parent` is generic |
| `WorkflowKinds.ChildFinished` | Kept as `Kinds.ChildFinished`, the foundation's, with `Final` | The parent notice, the parked notice, the child registry and the terminate cascade are the foundation's once the fifth review made the cascade a paged layer 2 obligation (owner decision 2) |
| `EffectKey.For`, `RootPath` | Into the model contract | Activity words; the foundation's part of the key is the exact ref's stable string (FC-15) |
| turn, job, inbox, message, send, recall, hold, start, child, detached, timer, event, subscription, wait, keep, publish, record, journal, history, Ephemeral, Park, fault, recover, discard, terminate, suspend, resume, delete, migrate, effect key, tenant, bound scope, live output, ambient, handle, page token | Kept | The approved pages' words, already plain |
| `RestoreWaits`, `TimerSpec`, `JobSpec`, `TerminalGrowth`, `DueAt` (as `dueAt`) | Kept, plus `SubscriptionSpec` | Approved recovery and envelope obligations, already in contract terms; `JobSpec.EffectKey` moves into `Arguments`, as `StartJob` and `IJob` carry it |
| `IActorType.IsCommand` | Replaced by `Kinds.IsCommand` | The list is closed by decision; a callback would let an actor type widen it |

## How the built-in foundation implements it

| Contract item | Prototype today (`prototypes/kernel-model`) |
|---|---|
| `ActorRef`, exact ref | `InstanceRef` with the partition in the id; `CreationProtocol.Origin(id, creation)` and `CreationAddress` |
| `IActorType`, `IJob.AttemptId` | `IActorType` in `Kernel.Durable/Turns.cs` and `WorkflowInstance`. The built-in worker supplies one opaque id per physical job entry and counts entries it observes for that delivery. See [job workers](job-workers.md#release-1) for the external worker protocol. |
| `IActorTurn` | `DurableTurn` plus `TurnInput`, with `Turn` in `Kernel.Workflows` adding the workflow part and `Seal` building `InstanceRow` and `TurnOutcome` |
| `End`, `SetHeader`, `ActorHeader` | `InstanceRow`'s columns `Status`, `Model`, `Definition`, `Suspended`, `Children`, `Fault`, `Parent`, `StateVersion`; `EndedAt` and the end mark are the terminal mark the data lifecycle page adds; `Labels` are new projected columns (K3) |
| The atomic outcome | `Worker` flush → `IKernelStore.CommitAsync(CommitBatch)` with the k-row fence |
| Single-writer turns | Node lease, partition epochs and the fence (`Worker`, `DurableHost`, `PartitionRuntime`, `IKernelStore.TakeAsync`) |
| Inbox, dedup, sender order | Message rows keyed `InboxKey(Id, Scope, Match)`, issued ids at scope −1 apart from outside ids at 0, `seq` per sender, `Seen`/`Trial` marks, `PollAsync` |
| Timers | Message rows with `Due`; `Recall` deletes by derived id |
| Jobs | `kernel.job` tickets, `Worker`'s job drivers, `JobFinished` ahead of other input, `JobGeneration` |
| Events and subscriptions | `kernel.publish` and `kernel.meet` tickets, `Events`, `SubscriptionRow`, `EventRow`, the match digests, the `~` prefix and `InvalidEventId` |
| Children, notices, cascade | `ChildIssuer`, `Children` on the row, `WorkflowInstance.Finished`, the terminate's `Sent`, the cascade obligation and its origin mark |
| Commands, `OperatorCommand` | `Kinds.IsControl`, `WorkflowInstance.Command`, `Withholds`; `ParkSelection`, `DeleteSelection`, `ThenDelete`, the drains under `ParkRecord.Resolution` |
| Park and recover | `IActorType.Park`, `ParkRecord`, the drains and `kernel.drain` |
| `IIngress` | `IKernelStore.AddAsync`, `Events.PublishAsync`, `Events.SubscribeAsync`, `IKeyAdmission`, `Worker.Post` with `ambient`, `Requests`, `ReplyTo` and `RespondBeforeCommit`; the HTTP resume's one-waiter delivery (`Live.cs` 108–110) becomes the fan-out |
| Read models | `Viewer.ReadAsync`, `Projector`, `ReadViewsAsync`, `ReadJournalOfAsync`; `KeptBy`'s reasons |
| The foundation clock | The owner's `Now`, anchored on database time |
| Tenant | Not yet in the prototype; the tenancy page's `Q(t, n)` and `tenant` column |

## What a foundation adapter must supply

Each case is checkable on the contract alone. The built-in foundation runs the K2 clauses on the in-memory store
and in the simulation, with a negative control for each case. Clauses that depend on later work qualify at the
milestones in the [K2 evidence table](../program-goals/clean-slate-kernel.md#k2-foundation-contract-evidence).
K4 repeats the store-dependent cases on every provider as their mechanisms become available. A future adapter passes
the full suite and runs the simulation workloads with the mutations the contract can express. The built-in workload
runs are the reference (owner decisions 5 of 19 November and K2 qualification of 21 November).

| Case | Guarantee |
|---|---|
| FC-1 | Single writer: each owner serializes turns for a given actor. During takeover an already-running turn on the former owner can overlap a turn on its successor. The successor's first turn sees every committed outcome. A stale owner cannot commit any state, history or staged effects (owner, 21 November) |
| FC-2 | Atomic outcome: state, header, journal entries, sends, timers, job starts, subscriptions, publishes and child starts of one turn land together or not at all; nothing is delivered, and nothing is released except to a request that opted out with `ReleaseBeforeCommit` (FC-14), before they land. An Ephemeral actor that ends in its first turn without sending writes nothing; one that waits only on its own timers and jobs may stay in memory up to the configured short wait and a request's deadline, and is written as it is afterwards (ADR 0099 section 6) |
| FC-3 | Inbox: a message is delivered at least once and consumed only by a committed turn; two messages from one sender to one target with one id are one message; an id the foundation issued (`Send`, `Publish`, timers, job starts, notices) never equals an outside id; direct messages from one sender to one target arrive in the order sent (owner, 22 November); every message the foundation produces carries the W3C trace context of the turn or ingress call that produced it, and the foundation starts the turn, commit and job spans ([observability](observability.md#trace-context-on-every-message)) |
| FC-4 | Timers: a send with `dueAt` is delivered no earlier than `dueAt` on the delivering node's reading of the foundation clock, which lies within the stated skew of every other node's reading (FC-C1), with no upper bound on when it is delivered; it survives owner loss and process death, and `Recall` prevents delivery of a message not yet given |
| FC-5 | Jobs: a committed `StartJob` runs at least once after any owner change; its result changes state once; after `CancelJob` no result applies; an earlier run's result never applies after a restart; past the poison threshold the actor parks with the job's error |
| FC-6 | Events, for a turn's `Publish` and for `SendAsync` or `RequestAsync` to `Event(key)` alike, over live waits (a wait whose removal has not committed): a publish committed after a wait's commit always reaches it; one whose matching finished before the wait's commit reaches it only when the wait keeps; one still being matched when the wait committed may reach it, with no clock bound (owner, 18 November); every delivery has kind `Kinds.Event` with the key in `Event.Key`; a turn's event id and a caller's never coincide, and the outside publish refuses one that could (owner, 18 November); an outside publish with the same `(key, id)` within the standard outside receipt window is one publication; a request to `Event(key)` is answered by the first live output of any waiter it reached, and only the waiters whose turns run on the node that took the request get its `Ambient`; a start subscription on the key counts as a live waiter, and the first live output of the actor it starts may answer (a coordinator design choice, 20 November); with no live wait and no start subscription matching it answers at once with "no waiter" and publishes nothing, so no wait, keeping or not, ever receives it (FC-C2; owner, 20 November); a start subscription starts one actor per event; each wait receives broadcasts in match commit order (owner, 22 November) |
| FC-7 | Subscriptions: a wait exists from its commit; `Unsubscribe`, Park and `End` remove it, and a publish that commits after the removing commit never reaches the removed wait; the same wait subscribed twice is one wait; a start subscription takes events from `SetStartAsync` with a spec until `SetStartAsync` with none, and none after |
| FC-8 | Children: `Start` yields an exact ref before the commit; the child's first turn runs only after the parent's commit lands; a child's Park sends its parent one notice with `Final` false, and the parent receives one final notice per child, as the resolution decides (discard and expiry `Faulted`; terminate `Cancelled`; delete `Faulted`, `Cancelled` when it takes over a terminate, none after a recover's end; recover none, then the ordinary notice at the child's end); the foundation cascades a terminate to every child not yet ended, with the idempotent job cancel, when the parent's turn consumes a `Terminate`, whatever the turn does, and on the parent's `End(Cancelled)`, and never on `End(Finished)` or `End(Faulted)`; the cascade reaches the children across a parent that ended first and across owner changes. Bounded in-process progress requires that a running job cooperates with cancellation and exits when asked, including a recovered attempt whose execution must yield to allow termination (owner, 21 November). A detached child receives none of this |
| FC-9 | Commands: a command is delivered alone, passes `Withholds`, reaches an ended actor, is never discarded as poison, and every other kind is withheld while `Withholds` is true. Who executes: `Suspend`, `Resume`, `Migrate` and `Terminate` the actor type in its turn; `Terminate` of a parked actor, `Recover`, `Discard` and `Delete` the foundation as its own resolutions. Authority: a turn may send `Terminate` to its own registered children only, and every other command kind from a turn is refused at the commit (FC-C3); the commit checks only the parent's child registry, and a `Terminate` that reaches a child that has already ended is the ordinary delivery of a command to an ended actor, which changes nothing (owner, 20 November); `ThenDelete` is accepted on an operator's terminate only, a turn's `Send` of one is refused, and the foundation deletes in the commit of the turn that ends the actor unless `ProtectsDeletion` holds; a cascade's terminate is accepted only from the child's recorded parent; an `OperatorCommand` whose `Selection` no longer matches the actor's life, Park or end mark is refused with no change |
| FC-10 | Park and recover: a throwing turn leaves the actor `Faulted` with its state and journal intact, its inputs consumed and kept, its waits removed and its parent told; a message or job counts toward the poison threshold only when its owner died while trying it, and past the threshold the actor parks; a recover re-arms exactly the `RestorePlan`, each timer at its recorded due, each job afresh so that an earlier run's result never applies, each subscription with its keep flag, and no wait outside the plan |
| FC-11 | Journal and reads: every `Record` entry appears once in the actor's history in commit order, at `Full`; at `State` a commit leaves only that the state changed and at `Minimal` nothing, except the foundation's lifecycle entries (start, end, migrate, Park and its resolutions, delete), which every history level writes (data lifecycle decision 1), so a Park is visible to the read models at `Minimal`; a read model shows no uncommitted entry and every page says in `AsOf` the foundation clock's reading it is current to; a tail read after a token returns only later entries; `ListAsync` filters by every `ActorQuery` field, with `Labels` eventually consistent with the commit that set them. On an unchanged collection, follow `Next` over at least three filtered pages, including tied sort values. Each matching actor appears exactly once, the last `Next` is null, and a different tenant, query or read kind cannot use the token. Each page reports its own `AsOf` |
| FC-12 | Tenancy: an ingress call without a `BoundScope` is refused; a turn's sends, starts, publishes, subscriptions, jobs and entries carry the actor's tenant; names chosen by callers never collide across tenants; an exact ref, caller key or selection of another tenant answers not found, in ingress and in every read model alike, and never names the other tenant ([tenancy](tenancy.md#every-operation)) |
| FC-13 | Opaque identity: an actor type, a model and the operator API compile against the contracts package alone and run on refs, handles, selections and page tokens that carry no parseable structure |
| FC-14 | Live output: `Emit` and `Reply` reach the open requests whose message the actor consumed, first response wins, only after the commit lands, or at once when the turn has nothing to write, unless the request opted out with `ReleaseBeforeCommit`; a turn whose commit is refused emits and replies nothing unless the request opted out; a request fanned out to an event key is answered by the first live output of any waiter, whether or not that waiter's turn got the `Ambient` (owner, 20 November) |
| FC-15 | Ended actors and exact refs: after `End` no turn changes the status; a delete names the actor, its life and its end mark through its `Selection` and is refused when they no longer match or `ProtectsDeletion` holds; a message to a deleted actor's exact ref is dropped; a generated name never comes back; a caller key with a spec refuses to re-create an actor deleted inside its window; a committed life's exact ref is the same under replay and after an owner change and differs between two lives of one name. An aborted start leaves its proposed ref unavailable |
| FC-16 | Ingress: `SendAsync` with an id already seen for the same target within the deduplication window (the foundation's, or the send's `KeepFor` when smaller) answers `Duplicate` and adds nothing, and after the window the id is new; past the inbox bound it answers `InboxFull`, over the tenant's rate `RateLimited`, and on an overloaded node `NodeBusy`, each with a `RetryAfter`, with `Suspend`, `Terminate`, `Delete` and `Discard` exempt under an operator rate and `Resume` obeying it (fairness decision 4); `RequestAsync` returns one `Reply`, the status at its deadline otherwise, and answers at once with `Faulted` when the consuming turn parks (owner, 17 November) |
| FC-17 | Envelope and caps: a live checkpoint leaves room for `TerminalGrowth`, the maximum legal `TypeData` and `Labels`, and the foundation's own fields. A turn over the envelope or send cap fails and parks (fairness decision 3). An actor type above 1,024 UTF-8 bytes or `TypeData` above 16 KiB in UTF-8 is refused at start admission and event-start registration, and is a model fault in `Start` or `SetHeader` inside a turn. `Labels` beyond 4 pairs or 1,024 UTF-8 bytes a name or value are a model fault. The actor can end or terminate at the envelope boundary with its full state growth and maximum legal header |
| FC-18 | Hold: a held message is given to the next turn in its place in the order, is not consumed, and counts as consumed only by the turn that does not hold it |

## Leaks to fix

An inventory on main; no code changes here. Lines are those of main at `9583f4da34`.

### Layer 3 reaching below the contract

| File:line | What | Fix | Milestone |
|---|---|---|---|
| `src/Kernel.Workflows/Turn.cs:57,59,114` | `Instance.Ref.Partition`, `InstanceRef.Spread(name, Services.Partitions)`, `Id.StartsWith($"g{partition}/key/")`: placement and key detection by parsing the id | `Placement` hint; the key check goes with the identity-only family | K2 |
| `Turn.cs:88-91,135-137` | Builds `InstanceRow` with `CreationProtocol.Fingerprint`, `StartIntent`, `Taken[From]`; edits `CommitTimes` through `WorkflowCommitClock.End` | The foundation builds its record from `End`, `SetHeader`, `State`; the end mark and time are its own | K2 |
| `src/Kernel.Workflows/WorkflowInstance.cs:14-29` | `SingleMessage`, `SelectsAlone`, `RevisitsHeld`, `Withholds` read `InstanceRow`, `Delivery.IsDurableJobTicket`, `Flaw` | `Withholds(ActorHeader)`; batching rules move into the foundation | K2 |
| `WorkflowInstance.cs:43-47,50,61,211-212` | `CreationProtocol.HasTerminalNoticeId`, `Refuse`, `TerminalNoticeId`, `TargetCreation`, `SenderCreation` checks on the parent notice | The notice and its checks are the foundation's (`Kinds.ChildFinished`) | K2 |
| `WorkflowInstance.cs:57-58,72-73,81-90,129,148,165-171,192-199` | Builds `TurnOutcome` with `Expected`, `Revision`, `Journal`, `Retire(row)`, `Delete`, `Unsubscribe`, composes terminate ids `{origin}#{revision}.{i}` and `InboxKey.Ticket` cancels (FINDINGS L9) | `End` and `Record`; the foundation's cascade and job cancel when a `Terminate` is consumed or on `End(Cancelled)`, its delete with `ProtectsDeletion` | K2 |
| `WorkflowInstance.cs:138-143` | `RemainingJobs` matches tickets by `InboxKey.Ticket` and `IsWorkerJobCompletion` | The foundation keeps the job registry | K2 |
| `src/Kernel.Workflows/EffectKey.cs:42` | `CreationProtocol.Origin(instance, creation)` | `Instance.Ref.Id` is the origin | K2 |
| `src/Kernel.Workflows/Contracts.cs:8,62` | `InstanceInfo.Ref` is `InstanceRef` (plan K2 row); `TerminalIdentity.From(InstanceRow)` | `ActorRef`; the identity-only family goes | K2 |
| `src/Kernel.Workflows/IdentityMetadataTurn.cs:7-8,28` | `TurnOutcome`, `DurableTurn`, `InboxKey` | Goes with the identity-only family | K2 |
| `models/Native/Live.cs:61-65,87,91,100,103,108,110-111` | `RespondBeforeCommit` per route; `HttpHost` takes `IKernelStore` and `Worker`; `InstanceRef.In(local.Partition, …)`, `local.Post(…, ambient:)`, `store.FindSubscriptionsAsync` and a delivery to `waiting[0]`, `EventRoute`, `EventRow`, `store.AddEventAsync` | `RequestAsync` with `Target.Start` or `Target.Event` (fanned out to every waiter) with `AddOptions` and `RequestOptions(Ambient, Deadline, ReleaseBeforeCommit, Inline)` | K2 defines, K8 runs |
| `models/Native/Incidents.cs:108` | `CreationProtocol.Origin` for the operation key | `turn.Instance.Ref.Id` | K6 |
| `models/Native/InvocationLifetime.cs:69` | `CommitClockFacts.ModelPrefix` | Goes with the commit clock (owner decision 3); retention reads `Now` and `EndedAt` | K6 |
| `models/Native/NativeModel.cs:267,537`, `models/Bpmn/BpmnTurn.cs:57,61,64,223,256` | `Events.Delivered(m)`, `InboxKey.Issued(key) == m.InboxKey`, `ChildAddress(…).Creation`, `SendOptions(TargetCreation: …)` | `Message.Event`; a timer is `Sender == Header.Ref` with the id `Send` returned, which no outside id can equal (FC-3); exact refs | K6 (Native), after release 1 (BPMN) |
| `models/Values/ValueModel.cs:44,49`, `models/Agent/AgentModel.cs:37-69`, `models/Etl/EtlModel.cs:51` | `Kinds.Start`, `JobFinished`, `Event` | None: these kinds stay in the contract's `Kinds` | — |
| `models/Etl/EtlModel.cs:52` | `WorkflowKinds.ChildFinished` | The rename to `Kinds.ChildFinished` | After release 1 (ETL) |

### Layer 2 knowing a workflow-specific type

| File:line | What | Fix | Milestone |
|---|---|---|---|
| `src/Kernel.Durable/Store.cs:13` | `InstanceRow` carries `Model`, `Definition`, `Suspended`, `Children`, `StateVersion` (FINDINGS L1) | `Type`, `TypeData` and the projected `Labels` beside the generic columns, `EndedAt` and the end mark added; `Children` as the foundation's own registry | K2 defines, K3 builds (a store change, two reviews) |
| `src/Kernel.Durable/Messages.cs:10,18` | `InstanceStatus` (L2); `StartSpec(Model, Definition, Parent, …)` (L4) | `ActorStatus`; `StartSpec(Type, TypeData, Input, Ephemeral, History)` with the parent set by `Start` | K2 |
| `Messages.cs:70-73,81` | The command kinds and `IsControl` (L3) | Kept as the contract's closed `Kinds`; the foundation knows them as commands only | K2 |
| `src/Kernel.Durable/KeyAdmission.cs:29` | The literal `"child-finished"` | `Kinds.ChildFinished` | K2 |
| `src/Kernel.Durable/Turns.cs:173,401` | `TurnInput.Model`; `JobTicket(model, key, arguments)` | The job's record carries the actor type's `Type`; the model is the type's business inside `Arguments` | K2 |
| `src/Kernel.Durable/Replies.cs:9,62`, `Worker.cs:1461-1473,1498,1510,2104-2105,2164-2165`, `Creation.cs:94,139,171`, `StoreBatchRules.cs:76` | `InstanceStatus` and `Parent` | None: both are generic under the 15 November card | — |

Leaks that stay inside the built-in foundation and stop being a contract concern: L5 (the take is lease and fence),
L6 (`IKernelStore : IMembership`), L7 (`Flaw`), L8 (`TrialGate`), L10 (`IReplyTransport`), L11 (`InstanceRef` embeds
the partition), L12 and L13.

## What K2 builds

The plan's K2 row, restated against this page ([plan](../program-goals/clean-slate-kernel.md#milestones)), with the
review's conditions FC-C1 (the clock's promise and FC-4's bound), FC-C2 (the no-waiter answer, run at K8) and FC-C3
(a turn's command kinds) settled in the text above and FC-C4 (a `StartedAt`) checked at K9: two
packages (owner decision 4; names provisional), the foundation contract with every type under "The contract" and the
model contract with the model-facing types, and nothing else; an architecture guard that
fails any reference from layer 3 to the built-in foundation's packages and any member of the contracts package whose
name or type names a partition, virtual partition, epoch, fence, lease, position, node record, `IKernelStore` or SQL;
representative activities, the native model, a second model and the runtime API compiled against the package alone
and run on refs, handles, selections and page tokens with no parseable structure (FC-13); the K2 clauses of FC-1 to FC-18
as executable cases on the built-in foundation with the in-memory store and the simulation, with negative controls.
The [K2 evidence table](../program-goals/clean-slate-kernel.md#k2-foundation-contract-evidence) assigns the remaining
clauses. The simulation's workloads run through the contract against the built-in foundation, with the mutations that the
contract can express, as the reference run a future adapter repeats (a coordinator design choice under owner decision 5); and `IKernelStore` kept as
the built-in foundation's internal storage contract, with the store conformance suite passing on the in-memory store.
Tenancy's T1 stays at K2. The projected `Labels` columns are a K3 store change.

## Owner decisions (19 November)

The five questions this page asked, and the review's M7, were decided by the owner on 19 November, each recorded as an
owner card in the [register](decisions.md#the-foundation-contract-19-november).

1. **Wording.** Layer 3 depends on the foundation contract (the actor turn, ingress and read models) and on the model
   contract built on it; commit and ownership are the built-in foundation's own business. ADR 0099 sections 2, 3, 8
   and 9 carry this wording; the 16 November rule is superseded.
2. **The actor record.** `model` leaves the table and goes into `TypeData`. Children, the notices with the parked form
   and the terminate cascade belong to the foundation. An actor type builds its read models from bounded labels plus
   the per-actor tail, with no tenant feed.
3. **Cut both** the identity-only ended row and the commit-time facts.
4. **Two packages**, one that a foundation adapter builds against and one that an execution model builds against (option b; the page had recommended option a, one package with two namespaces). Provisional names: the foundation contract and the
   model contract.
5. **Adapter acceptance.** FC-1 to FC-18, plus a contract-level run of the simulation's workloads against the adapter
   with the mutations that the contract can express.
6. **The release 1 job context** (review M7): `IJob.AttemptId`, `IJob.AttemptNumber`, `IJob.Deadline` and
   `StartJob(key, arguments, timeout)`, as the job workers page approved for release 1.
