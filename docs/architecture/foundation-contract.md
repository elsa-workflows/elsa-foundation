# The foundation contract

> **Proposed.** A coordinator design under the owner's two cards of 19 November 2026 (retire the Orleans adapter;
> layer 3 adds Elsa's capabilities behind a strong contract into which foundations plug; "the contracts and domain
> language stay as clean and small as practically possible"). It awaits owner approval and authorizes no product
> code; K2 builds from it once approved. Register: [the foundation contract](decisions.md#the-foundation-contract-19-november-proposed).
> Revised after the independent review of 19 November (6 Majors, 8 Minors, 6 Nits, all applied).

A **foundation** provides what layers 1 and 2 provide today: placement, activation, single-writer turns, the durable
inbox, the atomic outcome write, timers, jobs, events, subscriptions, children, Park and the drains that resolve it,
poison handling and the journal. The kernel ships one, the built-in foundation on the database. **Layer 3** adds Elsa's
capabilities: actor types, execution models, activities, Studio, the operator APIs and the read models' meaning. **This
contract** is what sits between them. It is written in plain domain words. The guard list, used on this page, in the
register and in the plan's K2 row: no partition, virtual partition, epoch, fence, lease, position, node record,
`IKernelStore` or SQL appears in it. Those stay inside the built-in foundation, which keeps `IKernelStore` as its own
way to swap EF Core, MongoDB and in-memory storage.

## Measure first

What layer 3 can reach today, counted from the prototype on main (`src/Kernel.Workflows`, the `Kernel.Durable` and
`Kernel.Runtime` types its models and `WorkflowInstance` use), against the contract below. Public types and their
public members, positional record fields and enum values included, static helpers excluded; the review's correction
(`IActorType` has 12 members on main) is applied, and the read-model records are counted this time.

| Surface | Types | Members |
|---|---:|---:|
| Built today: `ITurn`, `IExecutionModel`, the metadata interfaces, `Message`, `StartSpec`, `SendOptions`, `InboxKey`, `Kinds`, `IJob`, the commit clock, `IActorType`, `ITurnServices`, `InstanceRef`, `EffectKey` and the rest | 32 | 182 |
| Plus the gate 2 additions the pages approved and nobody built (`RestoreWaits` and its plan, `TimerSpec`, `JobSpec`, `SendOptions.DueAt`, `TerminalGrowth`, `IActorType.IsCommand`, `ITurn.Instance.Tenant`, `BoundScope`) | 37 | 198 |
| Beside it, what the HTTP adapter and hosts reach for ingress and reads: `IKernelStore` (30 members), `Worker.Post`, `Requests`, `ReplyTo`, `EventRoute`, `EventRow`, `SubscriptionRow` | 7 | 50 or more |
| **After the cut, the whole foundation contract:** actor side 20 types / 99 members, ingress 6 / 21, read models 10 / 34 | **36** | **154** |
| Layer 3's own model contract, built on the actor turn (`ITurn`, `IExecutionModel`, `IModelMetadata`, `InstanceInfo`, `WorkflowStart`, `EffectKey`), counted apart | 6 | 44 |

The first version of this page gave 29 and 126 for the whole contract; it left the read-model records and the command
payloads uncounted. Members that today reach a model only through `Message` or `InstanceRef` (eighteen and nine) and
the `IActorType` members that take rows and deliveries are the largest cut.

## The contract

Every member below has one meaning and carries a guarantee. A guarantee's case id (FC-n) is defined under
"What a foundation adapter must supply". **The foundation clock** is the one clock every node of a foundation shares;
`Now`, `dueAt`, `TimerSpec.Due` and `EndedAt` are readings of it, in milliseconds, and a turn reads it once.

### Words

| Type | Members | Meaning and guarantee |
|---|---|---|
| `ActorRef` | `Id` | An actor's address, an opaque string. Layer 3 stores and compares it ordinally and never parses it (FC-13). A ref the foundation issued (`Header.Ref`, `Start`, `Message.Sender`, `ChildFinished.Child`) is **exact**: it names one life of the actor, its string is stable across replay and owner change and differs between lives, so a message to it never reaches a later actor under the same name (FC-15) |
| `ActorStatus` | `Running`, `Finished`, `Faulted`, `Cancelled` | Whether an actor still runs and how it ended. The foundation writes `Faulted` on Park and reports the status at a request's end; the actor type sets the rest through `End` |
| `History` | `Full`, `State`, `Minimal` | What a commit leaves in the journal: the turn's entries, only that the state changed, or nothing (ADR 0099 section 6; FC-11) |
| `Message` | `Id`, `Kind`, `Payload`, `Sender`, `Event` | One inbox entry. `Id` is the string `Send` or `Publish` returned, or the caller's; `Sender` is an exact ref, or null for an outside add; `Event` is set on an event delivery, whose `Kind` is the event's key. A timer or ticket is recognised by `Sender == Header.Ref` and its id. Delivered at least once, deduplicated per sender, target and id, in one sender's order (FC-3) |
| `EventDelivered` | `Id`, `Wait` | The event's id and the wait that asked. Replaces parsing the delivery id |
| `StartSpec` | `Type`, `TypeData`, `Input`, `Ephemeral`, `History` | What creates an actor: its actor type, the type's own opaque data (a workflow's model, definition and state version), the input, which arrives as the `Start` message's payload, the Ephemeral choice and the history level. How long an unwritten Ephemeral actor may wait in memory is the foundation's configuration, bounded by a request's deadline (walkthrough decision 4; FC-2) |
| `Placement` | `WithParent`, `Spread` | A hint for `Start`: keep the child where its parent is, so a call tree stays cheap, or spread a wide fan-out. The foundation places |
| `IJob` | `Actor`, `Key`, `Arguments` | What `ExecuteJobAsync` receives. A job may run more than once; its result changes state once (FC-5) |
| `JobFinished` | `Key`, `Result`, `Error` | The payload of kind `job-finished`, the one message a job's completion becomes |
| `ChildFinished` | `Child`, `Status`, `Output`, `Final` | The payload of kind `child-finished`, the foundation's notice to a parent. `Final` false is the parked notice (large fan-out decision 5): the parent keeps waiting for the final one, which the resolution decides (FC-8) |
| `TimerSpec`, `JobSpec`, `SubscriptionSpec`, `RestorePlan` | `(Id, Kind, Payload, Due)`, `(Key, Arguments)`, `(Key, Wait, Keep)`, `(Timers, Jobs, Subscriptions)` | The waits an actor's committed state holds, as `RestoreWaits` returns them; a recover re-arms each from the spec alone. A job's effect key travels inside its `Arguments`, as `StartJob` and `IJob` carry it (observability, [recover](observability.md#recover); FC-10) |
| `Kinds` | `Start`, `JobFinished`, `ChildFinished`; the commands `Suspend`, `Resume`, `Terminate`, `Delete`, `Recover`, `Discard`, `Migrate`; `IsCommand(kind)` | The kinds the foundation produces or treats apart. The command list is closed and grows only by amendment (register, third review R3-M4). A command passes withholding, is delivered alone and reaches an ended actor. `Suspend`, `Resume`, `Migrate` and `Terminate` are executed by the actor type in its turn; `Recover`, `Discard` and `Delete` by the foundation as drains, with the actor type asked only through `ProtectsDeletion` (FC-9, FC-10) |
| `Resolution`, `RecoverMode` | `(Selection, Mode, Handles, Reason, ThenDelete)`; `Resume`, `Retry`, `RetryAll` | An operator command's payload in contract words. `Selection` is the opaque token a read model handed out for one actor, one life and one Park or end mark; the foundation refuses a command whose selection no longer matches, with no change. `Handles` are the opaque delivery handles of a retry, from `KeptAsync`. `ThenDelete` is accepted only on an operator's terminate (FC-9) |
| `ActorHeader` | `Ref`, `Type`, `Tenant`, `Status`, `Fault`, `Parent`, `EndMark`, `EndedAt`, `TypeData`, `Labels` | What the foundation keeps of every actor beside its opaque state, present from the first turn, and what read models show. `EndMark` is the opaque mark of the actor's last ending and `EndedAt` its reading of the foundation clock, both written by the foundation (data lifecycle B1). `TypeData` is the actor type's own opaque field and `Labels` the few indexable name-value pairs it chooses to expose (a definition, a version), both set by `SetHeader` |

### What layer 3 supplies: `IActorType`

| Member | Meaning and promise |
|---|---|
| `Kind` | The actor type's key, stored as `ActorHeader.Type` and used to route a message to the type |
| `Withholds(ActorHeader)` | True when the actor may not be given ordinary messages now: suspended, or its `TypeData` says a newer version wrote it than this node has (F14). Commands still pass |
| `TurnAsync(IActorTurn)` | One turn. The actor type reads the inbox and state in memory and produces everything through the turn; nothing leaves before the commit. Its commands arrive here as messages. A throw parks the actor (FC-10) |
| `ExecuteJobAsync(IJob, token)` | A background job's code, outside the actor's turns; at least once |
| `RestoreWaits(ActorHeader, state)` | The plan of waits the committed state holds, for a recover |
| `TerminalGrowth(ActorHeader, state)` | An upper bound on what the type's end and terminate paths add to the state, in bytes of the opaque state string as handed over; the foundation scales it for its own encoding (seventh review R7-B1; FC-17) |
| `ProtectsDeletion(ActorHeader, state)` | True while the type still needs the ended actor kept (a retention it owes); the foundation's delete drain asks before deleting (R3-M5) |

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
| `Ambient` | What the request on this node gave: this activation only, never stored |
| `Emit(channel, item)` | Live output to the open requests whose message this actor consumed, first response wins; released when the commit lands, or at once when there is nothing to write, unless the request opted out (FC-14) |
| `Reply(message, channel, item)` | The answer to one consumed request, once, after the commit (FC-14, FC-16) |
| `Send(target, kind, payload, dueAt)` → id | A message recorded with this commit and delivered later, at least once, under the returned id; with `dueAt`, a timer delivered no earlier than that reading of the foundation clock, written unchanged (FC-2, FC-3, FC-4) |
| `Recall(id)` | Takes back an undelivered message to self (a timer); its row goes with this commit (FC-4) |
| `Start(spec, placement, detached)` → ref | A child whose exact ref is allocated in this commit; it reports back as `child-finished` and a terminate goes on to it, unless detached (FC-8) |
| `StartJob(key, arguments)`, `CancelJob(key)` | The intent is a ticket in the commit; after a cancel the result is never applied; past the poison threshold the job parks the actor (FC-5) |
| `Hold(message)` | Handed to the next turn again, in its place in the order (FC-18) |
| `Subscribe(key, wait, keep)`, `Unsubscribe(key, wait)` | A wait for events of the key from this commit on; `keep` also gives it the kept events (owner default: off) (FC-6, FC-7) |
| `Publish(key, payload, keep)` → id | An event, durable with this commit, delivered to every subscription committed before it and kept for `keep` for waits that ask (FC-6) |
| `Record(entry)` | A journal entry of this commit, at the actor's history level (FC-11) |
| `End(status, output)` | The actor's status becomes final with this commit: the parent is notified, waits are removed, the foundation writes `EndMark` and `EndedAt`. Later turns still run for commands and for the type's own messages to an ended actor (a metadata query), and none of them changes the status (FC-15) |
| `SetHeader(typeData, labels)` | Replaces the actor type's `TypeData` and `Labels` with this commit (a rebind, a suspend flag, a new state version) |

### Ingress: `IIngress`

What the operator API, the HTTP adapter and the ingress adapters (brokers, webhooks, schedulers as idempotent adds)
call. Each operation takes a `BoundScope` (`Tenant`; internal constructor, issued only by the hosting adapter,
[tenancy](tenancy.md#where-the-tenant-comes-from)) and refuses without one (FC-12). A **`Target`** is one of
`Ref(ref)` (an actor), `Key(callerKey, spec)` (the actor the caller key resolves to in this tenant, created from `spec`
when absent, refused as a repeat within the key's window), `Event(key)` (every actor waiting on the event key) or
`Start(spec)` (a new actor).

| Member | Meaning and guarantee |
|---|---|
| `SendAsync(scope, target, id, kind, payload, dueAt)` → `Admission` | An outside add. The same id to the same target twice is one message; the second answers `Duplicate`. Past the actor's inbox bound it answers `Refused(InboxFull)` with a `RetryAfter`; the load-shedding commands are exempt under an operator rate (FC-16) |
| `RequestAsync(scope, target, id, kind, payload, options)` → `Reply` | A send whose answer is awaited: the first live output (an `Emit` or `Reply`) of the actor the target names, then the actor's end status, or the status and error at the deadline, under the plan's [HTTP contract](../program-goals/clean-slate-kernel.md#the-http-error-and-timeout-contract-k8-proposed). `RequestOptions(Ambient, Deadline, ReleaseBeforeCommit, Inline)`: the request's ambient value for the actor's first turn on this node; the deadline, which also bounds an Ephemeral actor's in-memory wait; the per-route opt-out that releases output when the turn ends, before its commit lands; and the per-route choice to run the first turn in the caller's process when the foundation can (owner, 12 November) (FC-14, FC-16) |
| `PublishAsync(scope, key, eventId, payload, keep)` → `Admission` | An outside event; `(key, id)` twice within the window is one publication. A caller id that could equal a turn's issued event id answers `Refused(InvalidEventId)` and writes nothing (owner, 18 November, R7-M1) (FC-6) |
| `RegisterStartAsync(scope, key, wait, spec)`, `UnregisterStartAsync(scope, key, wait)` | A start subscription: every event of the key starts an actor from `spec`, until it is unregistered (an unpublish) (FC-7) |

`Admission(Outcome, Actor, Reason)`: `Accepted`, `Duplicate` or `Refused` with the reason (`InboxFull` with its
`RetryAfter`, `InvalidEventId`, `Repeat`, a stale selection), and the ref the call created or resolved.
`Reply(Channel, Item, Status, Error)`.

### Read models: `IActorReads` and `IInboxInspection`

In contract words, replacing the observability page's sketch, whose records name an `InstanceRef`, a creation, a Park
revision and a trial state ([read models](observability.md#read-models-how-layer-3-stays-off-partitions); that page is
reworded when this one is approved). Each takes a `BoundScope`; every answer says how fresh it is; handles, selections
and page tokens are opaque; no answer shows an uncommitted entry (FC-11).

| Member or record | Meaning |
|---|---|
| `GetAsync(scope, ref)` → `ActorView` | One actor's header, its revision, its `Selection` for an operator command, and its `Park` when parked |
| `ListAsync(scope, query)` → `Page<ActorView>` | The tenant's actors. `ActorQuery(Type, Status, Labels, EndedBefore, Parked)` filters by actor type, status, the type's labels (a definition, a version), end time (retention) and whether a Park is unresolved; the parked-work list is this query |
| `TailAsync(scope, ref, from)` → `Page<JournalEntry>` | One actor's journal entries after an opaque `PageToken`, in commit order |
| `FeedAsync(scope, from)` → `Page<JournalEntry>` | The tenant's journal after a token, in commit order per actor, from which an actor type builds its own read models (owner, 15 November) |
| `PendingAsync(scope, ref, from)`, `KeptAsync(scope, ref, from)` → `Page<DeliveryView>` | An actor's pending inbox, and the rows its Park keeps, each with the handle a retry names |
| `ActorView(Header, Revision, Selection, Park)`; `ParkView(Reason, At, Handles, Resolution)`; `JournalEntry(Actor, Revision, Entry)`; `DeliveryView(Handle, Id, Kind, Sender, Due, State)` with `DeliveryState` `Pending`, `Held`, `Kept`; `Page<T>(Items, Next)`; `PageToken` | The records. `ParkView.Handles` are the deliveries the Park consumed; `Resolution` is the open drain's kind, or null |

### The model contract, built on the actor turn

Layer 3's workflow actor type offers execution models `ITurn`: the actor turn's members except `Header`, `SetHeader`
and `End`, plus `Instance` (`Ref`, `Tenant`, `Model`, `Definition`, `Parent`), `Rebind(definition)` and
`SetStatus(status, subStatus, output)`; `Start` takes a `WorkflowStart(Model, Definition, Input, Ephemeral, History)`.
`IExecutionModel` keeps `Key`, `StateType`, `StateVersion`, `CreateState`, `UpgradeState`, `TurnAsync`,
`ExecuteJobAsync`, `RestoreWaits` and `TerminalGrowth`; `IModelMetadata` keeps `HandlesMetadata`, `MetadataAsync`,
`TerminateMetadata` and `ProtectsDeletion`. `EffectKey.For(origin, node, iterationPath, attempt)` and `RootPath` live
here: `{instance origin}/{node}/{iteration path}/{attempt}` (owner, 16 November), with `Instance.Ref.Id` as the origin.
Everything here is layer 3's own and reaches the foundation only through `IActorTurn`.

## The vocabulary

### Terms the contract introduces

| Term | Why it is needed |
|---|---|
| foundation, foundation adapter, foundation clock | The owner's words of 19 November for what replaces layers 1 and 2 as a whole; the clock is the one word for what `Now`, `dueAt` and `EndedAt` read |
| actor, actor type, `ActorRef`, `ActorHeader`, `TypeData`, `Labels`, `Withholds` | The three layers' own words (12 and 15 November). `ActorRef` replaces `InstanceRef` because job and plan actors are not instances; the header is what the foundation keeps beside the opaque state; `TypeData` and `Labels` are the actor type's two fields in it |
| exact ref, `EndMark`, `EndedAt` | One word for "this life of this actor", so creation numbers never cross the contract; the mark and time of its last ending, which the qualified delete names |
| `EventDelivered`, `SubscriptionSpec`, `RestorePlan` | The event delivery's own fields in place of a parsed id; the subscription entry named like `TimerSpec` and `JobSpec`; the plan that holds the three |
| `IIngress`, `Target`, `RequestOptions`, `Admission`, `Reply`, duplicate | The one way in from outside, its four target shapes, a request's options and the two answers; the storage model already says "admission" |
| `Resolution`, `Selection`, `RecoverMode`, `Final` | An operator command's payload, the opaque token a read model hands out, the recover's three modes, and the parked notice's flag |
| `IActorReads`, `ActorView`, `ActorQuery`, `ParkView`, `JournalEntry`, `DeliveryView`, `Page`, `PageToken` | The read models' words, replacing records that named instances, creations and trials |

### Terms the current contract or seams use

| Term | Disposition | Reason |
|---|---|---|
| partition, virtual partition, epoch, position, fence, lease, node record, heartbeat, take, release, retake, poll, seen, trial, `seq`, owned set, pass, database time | Into the built-in foundation | How that foundation gives one writer per actor and an atomic commit; a foundation on Orleans or Proto.Actor has none of them |
| `IKernelStore`, `CommitBatch`, `TurnCommit`, `PartitionFence`, `Committed`, `Stored`, `Polled`, `IMembership`, `Cluster`, the projector's cursor, `Viewer.ReadAsync(partition, …)`, `SweepAsync` | Into the built-in foundation | Its storage contract; still how it swaps EF Core, MongoDB and the in-memory store |
| `IActorRuntime`, `IScopeHost`, `IScopeActivation`, `IScopeCount`, `IWakeSignal`, `Worker`, `DurableHost`, `PartitionRuntime` | Into the built-in foundation | Its layer 1 seam, kept with its conformance suite (owner, 19 November) |
| `InboxKey`, scope, `Match`, the derived-id grammar `{origin}#{revision}.{ordinal}`, `TicketId`, `CreationProtocol` (origin, fingerprint, terminal notice id, retire), `StoredState`, `DurableKeyJson`, the cascade origin mark | Into the built-in foundation | Identity encodings and the foundation's own authority marks. Layer 3 compares ids as strings; the guarantee that issued and outside ids never coincide is FC-3 |
| `KeptBy`, `ReleasedBy`, `Cohort`, `P_cascade`, `kernel.drain`, `kernel.park-expiry`, `kernel.cascade`, `Incarnation`, `CatchUpId`, `CatchUpCover`, `EventHorizon`, publish and meet tickets, `kernel.job`, `kernel.meet`, `kernel.publish`, `JobGeneration`, `ParkRecord` | Into the built-in foundation | The gate 2 mechanisms behind Park, recovery, termination and event matching. The guarantees stay (FC-6, FC-9, FC-10); the mechanisms are one foundation's |
| `Requests`, `ReplyTo.Of`, `ReplyTo.Parse`, `IReplyTransport`, `Worker.Post`, `RespondBeforeCommit` | Into the built-in foundation, behind `RequestAsync` | The reply route between nodes is a transport detail; the per-route opt-out is `RequestOptions.ReleaseBeforeCommit` |
| `Flaw`, `ITurnServices.Has` | Dropped from the contract | Mutations are the built-in foundation's tests; models keep their own flaw enums |
| `ITurnServices` (`Partitions`, `ShortWait`, `CommitClockAvailable`, `CreationIdentityAvailable`, `Cancelling`) | Dropped | Capability flags are refused by the profiles decision; the short wait is foundation configuration; exact refs are every foundation's (FC-15) |
| `InstanceRef.Partition`, `In`, `Spread`, `ForChild`, `ForEvent`, `EventStartAddress`, `ChildAddress`, `GeneratedAddress` | Dropped | Address parsing (the plan's K2 row). `Placement` is the hint that replaces `Spread` |
| `Message.Target`, `Start`, `Due`, `KeepUntil`, `ReplyTo`, `TargetCreation`, `SenderCreation`, `InboxScope`, `EventMatch`, `InboxKey`, `WithInboxScope`, `AsIssued`, `AsOrdinary`, `Read<T>` | Dropped | Row fields and encodings, and a JSON helper that becomes a layer 3 extension. The target is `Header.Ref`; the input is the `Start` message's payload; the reply route is behind `Reply`; creations are inside exact refs. The row columns `TargetCreation` and `SenderCreation` stay in the built-in foundation unchanged |
| `Events.Delivered(message)`, `Kinds.Event` | Replaced by `Message.Event` | Parsing a delivery id was friction F-N3 already; an event delivery is one whose `Event` is set |
| `SendOptions` (`Delay`, `Start`, `TargetCreation`, `DueAt`), `ChildAddress(child)`, `CreationAddress`, `CreationStart`, `InstanceInfo.Creation`, `ParentCreation`, `StartSpec.ShortWait` | Dropped | One `dueAt` argument carries the approved absolute due; exact refs carry the creation; a send creates nothing, `Start` does; the short wait is configuration bounded by a request's deadline |
| `Redelivered(message)` | Dropped | A hint with no guarantee ("false proves nothing"); no model's logic reads it, only a mutation |
| `IStateFreeTerminalMetadata`, `TerminalIdentity`, `IIdentityMetadataTurn`, `IdentityMetadataUnavailable`, `CanUseIdentityOnly`, `ProtectsIdentityDeletion`, `IdentityMetadataAsync`, `ModelMetadataUnavailable`, `InstanceRow.IdentityOnly`, `IdentityMetadataRequired`; `ICommitClock`, `ICommitClockObservation`, `CommitClockFacts`, `WorkflowCommitClock`, `InstanceRow.CommitTimes` | Dropped (owner question 3) | Two optimisations of the prototype's admission experiments: an ended keyed Ephemeral row that keeps no state, and commit-time facts a model stamps and resolves. Both are on no approved page, and EF Core and MongoDB refuse clock-bearing writes today. `IModelMetadata` with the state and the foundation's `EndedAt` keep retention and deletion protection |
| `IActorType.SingleMessage` (both), `SelectsAlone`, `RevisitsHeld`, `Command`, `Park` (both), `Write`, `SelfReference`, `TurnInput`, `TurnOutcome`, `Delivery`, `InstanceRow`, `DurableTurn` | Replaced by `IActorTurn`, `Kinds.IsCommand` and the foundation's own Park | Delivery batching, row building and the Park write belong to the foundation; a command is a message like any other to the actor type, with the closed list saying which are delivered apart and which the foundation executes |
| `InstanceStatus` | Kept as `ActorStatus` | Generic (FINDINGS L2): the foundation writes `Faulted` and reports the status at a request's end |
| `StartSpec.Model`, `Definition`, `Parent`; `InstanceRow.Model`, `Definition`, `Children`, `Suspended`, `StateVersion` | Into `TypeData` and `Labels` (model, definition, suspended, state version) and the foundation's child registry (children) | FINDINGS L1 and L4: the actor record keeps actor-generic columns only (owner, 15 November); `Parent` is generic |
| `WorkflowKinds.ChildFinished` | Kept as `Kinds.ChildFinished`, the foundation's, with `Final` | The parent notice, the parked notice, the child registry and the terminate cascade are the foundation's once the fifth review made the cascade a paged layer 2 obligation (owner question 2) |
| `EffectKey.For`, `RootPath` | Into the model contract | Activity words; the foundation's part of the key is the exact ref's stable string (FC-15) |
| turn, job, inbox, message, send, recall, hold, start, child, detached, timer, event, subscription, wait, keep, publish, record, journal, history, Ephemeral, Park, fault, recover, discard, terminate, suspend, resume, delete, migrate, effect key, tenant, bound scope, live output, ambient, handle, page token | Kept | The approved pages' words, already plain |
| `RestoreWaits`, `TimerSpec`, `JobSpec`, `TerminalGrowth`, `DueAt` (as `dueAt`) | Kept, plus `SubscriptionSpec` | Approved recovery and envelope obligations, already in contract terms; `JobSpec.EffectKey` moves into `Arguments`, as `StartJob` and `IJob` carry it |
| `IActorType.IsCommand` | Replaced by `Kinds.IsCommand` | The list is closed by decision; a callback would let an actor type widen it |

## How the built-in foundation implements it

| Contract item | Prototype today (`prototypes/kernel-model`) |
|---|---|
| `ActorRef`, exact ref | `InstanceRef` with the partition in the id; `CreationProtocol.Origin(id, creation)` and `CreationAddress` |
| `IActorType` | `IActorType` in `Kernel.Durable/Turns.cs` (12 members on main) and `WorkflowInstance` |
| `IActorTurn` | `DurableTurn` plus `TurnInput`, with `Turn` in `Kernel.Workflows` adding the workflow part and `Seal` building `InstanceRow` and `TurnOutcome` |
| `End`, `SetHeader`, `ActorHeader` | `InstanceRow`'s columns `Status`, `Model`, `Definition`, `Suspended`, `Children`, `Fault`, `Parent`, `StateVersion`; `EndMark` and `EndedAt` are the terminal mark the data lifecycle page adds |
| The atomic outcome | `Worker` flush → `IKernelStore.CommitAsync(CommitBatch)` with the k-row fence |
| Single-writer turns | Node lease, partition epochs and the fence (`Worker`, `DurableHost`, `PartitionRuntime`, `IKernelStore.TakeAsync`) |
| Inbox, dedup, sender order | Message rows keyed `InboxKey(Id, Scope, Match)`, issued ids at scope −1 apart from outside ids at 0, `seq` per sender, `Seen`/`Trial` marks, `PollAsync` |
| Timers | Message rows with `Due`; `Recall` deletes by derived id |
| Jobs | `kernel.job` tickets, `Worker`'s job drivers, `JobFinished` ahead of other input, `JobGeneration` |
| Events and subscriptions | `kernel.publish` and `kernel.meet` tickets, `Events`, `SubscriptionRow`, `EventRow`, the match digests, the `~` prefix and `InvalidEventId` |
| Children, notices, cascade | `ChildIssuer`, `Children` on the row, `WorkflowInstance.Finished`, the terminate's `Sent`, the cascade obligation and its origin mark |
| Commands, `Resolution` | `Kinds.IsControl`, `WorkflowInstance.Command`, `Withholds`; `ParkSelection`, `DeleteSelection`, `ThenDelete`, the drains under `ParkRecord.Resolution` |
| Park and recover | `IActorType.Park`, `ParkRecord`, the drains and `kernel.drain` |
| `IIngress` | `IKernelStore.AddAsync`, `Events.PublishAsync`, `Events.SubscribeAsync`, `IKeyAdmission`, `Worker.Post` with `ambient`, `Requests`, `ReplyTo` and `RespondBeforeCommit` |
| Read models | `Viewer.ReadAsync`, `Projector`, `ReadViewsAsync`, `ReadJournalOfAsync` |
| The foundation clock | The owner's `Now`, anchored on database time |
| Tenant | Not yet in the prototype; the tenancy page's `Q(t, n)` and `tenant` column |

## What a foundation adapter must supply

Each case is checkable on the contract alone and runs first on the built-in foundation at K2 (in-memory store and the
simulation, each with a mutation that fails without it), then on every provider at K4. A future adapter passes the
same suite; whether the simulation must also run against it is owner question 5.

| Case | Guarantee |
|---|---|
| FC-1 | Single writer: no two turns of one actor overlap or commit interleaved; after an owner change the new owner's first turn sees every committed outcome and a stale owner's outcome never lands |
| FC-2 | Atomic outcome: state, header, journal entries, sends, timers, job tickets, subscriptions, publishes and child starts of one turn land together or not at all; nothing is delivered or released before they land. An Ephemeral actor that ends in its first turn without sending writes nothing; one that waits only on its own timers and jobs may stay in memory up to the configured short wait and a request's deadline, and is written as it is afterwards (ADR 0099 section 6) |
| FC-3 | Inbox: a message is delivered at least once and consumed only by a committed turn; two messages from one sender to one target with one id are one message; an id the foundation issued (`Send`, `Publish`, timers, job tickets, notices) never equals an outside id; one sender's messages to one target arrive in the order sent |
| FC-4 | Timers: a send with `dueAt` is delivered no earlier than due, survives owner loss and process death, and `Recall` prevents delivery of a message not yet given |
| FC-5 | Jobs: a committed `StartJob` runs at least once after any owner change; its result changes state once; after `CancelJob` no result applies; an earlier run's result never applies after a restart; past the poison threshold the actor parks with the job's error |
| FC-6 | Events: a publish committed after a wait's commit always reaches it; one whose matching finished before the wait's commit reaches it only when the wait keeps; one still being matched when the wait committed may reach it, with no clock bound (owner, 18 November); a turn's event id and a caller's never coincide, and `PublishAsync` refuses one that could (owner, 18 November); an outside publish with the same `(key, id)` within its window is one publication; a start subscription starts one actor per event |
| FC-7 | Subscriptions: a wait exists from its commit; `Unsubscribe`, Park and `End` remove it; the same wait subscribed twice is one wait; a start subscription takes events until `UnregisterStartAsync` and none after |
| FC-8 | Children: `Start` yields an exact ref before the commit; the child's first turn runs only after the parent's commit lands; a child's Park sends its parent one notice with `Final` false, and the parent receives one final notice per child, as the resolution decides (discard and expiry `Faulted`; terminate `Cancelled`; delete `Faulted`, `Cancelled` when it takes over a terminate, none after a recover's end; recover none, then the ordinary notice at the child's end); a terminate of the parent reaches every child not yet ended, across a parent that ended first and across any owner change; a detached child receives none of this |
| FC-9 | Commands: a command is delivered alone, passes `Withholds`, reaches an ended actor, is never discarded as poison, and every other kind is withheld while `Withholds` is true. Authority: `ThenDelete` is accepted on an operator's terminate only, and a turn's `Send` of one is refused; a cascade's terminate is accepted only from the child's recorded parent; a `Resolution` whose `Selection` no longer matches the actor's life, Park or end mark is refused with no change; `Recover`, `Discard` and `Delete` are executed by the foundation, the rest by the actor type |
| FC-10 | Park and recover: a throwing turn leaves the actor `Faulted` with its state and journal intact, its inputs consumed and kept, its waits removed and its parent told; a message or job counts toward the poison threshold only when its owner died while trying it, and past the threshold the actor parks; a recover re-arms exactly the `RestorePlan`, each timer at its recorded due, each job afresh so that an earlier run's result never applies, each subscription with its keep flag, and no wait outside the plan |
| FC-11 | Journal and reads: every `Record` entry appears once in the actor's history in commit order, at `Full`; at `State` a commit leaves only that the state changed and at `Minimal` nothing; a read model shows no uncommitted entry and says how fresh it is; a tail or feed read after a token returns only later entries; `ListAsync` filters by every `ActorQuery` field |
| FC-12 | Tenancy: an ingress call without a `BoundScope` is refused; a turn's sends, starts, publishes, subscriptions, jobs and entries carry the actor's tenant; names chosen by callers never collide across tenants |
| FC-13 | Opaque identity: an actor type, a model and the operator API compile against the contracts package alone and run on refs, handles, selections and page tokens that carry no parseable structure |
| FC-14 | Live output: `Emit` and `Reply` reach the open requests whose message the actor consumed, first response wins, only after the commit lands, or at once when the turn has nothing to write, unless the request opted out with `ReleaseBeforeCommit`; a turn whose commit is refused emits and replies nothing unless the request opted out |
| FC-15 | Ended actors and exact refs: after `End` no turn changes the status; a delete names the actor, its life and its `EndMark` and is refused when they no longer match or `ProtectsDeletion` holds; a message to a deleted actor's exact ref is dropped; a generated name never comes back; a caller key refuses a repeat within its window; an exact ref's string is the same under replay and after an owner change and differs between two lives of one name |
| FC-16 | Ingress: `SendAsync` with an existing id to the same target answers `Duplicate` and adds nothing; past the inbox bound it answers `InboxFull` with a `RetryAfter`, with `Suspend`, `Terminate`, `Delete` and `Discard` exempt under an operator rate and `Resume` obeying it (fairness decision 4); `RequestAsync` returns at most one reply, the status at its deadline otherwise, and answers at once with `Faulted` when the consuming turn parks (owner, 17 November) |
| FC-17 | Envelope and caps: a turn whose state plus `TerminalGrowth` plus the foundation's own fields would exceed the record cap fails and parks; a turn above the send cap fails and parks (fairness decision 3); the actor's end and terminate always fit |
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
| `WorkflowInstance.cs:57-58,72-73,81-90,129,148,165-171,192-199` | Builds `TurnOutcome` with `Expected`, `Revision`, `Journal`, `Retire(row)`, `Delete`, `Unsubscribe`, composes terminate ids `{origin}#{revision}.{i}` and `InboxKey.Ticket` cancels (FINDINGS L9) | `End` and `Record`; the foundation's cascade and job cancel on `End(Cancelled)`, its delete drain with `ProtectsDeletion` | K2 |
| `WorkflowInstance.cs:138-143` | `RemainingJobs` matches tickets by `InboxKey.Ticket` and `IsWorkerJobCompletion` | The foundation keeps the job registry | K2 |
| `src/Kernel.Workflows/EffectKey.cs:42` | `CreationProtocol.Origin(instance, creation)` | `Instance.Ref.Id` is the origin | K2 |
| `src/Kernel.Workflows/Contracts.cs:8,62` | `InstanceInfo.Ref` is `InstanceRef` (plan K2 row); `TerminalIdentity.From(InstanceRow)` | `ActorRef`; the identity-only family goes | K2 |
| `src/Kernel.Workflows/IdentityMetadataTurn.cs:7-8,28` | `TurnOutcome`, `DurableTurn`, `InboxKey` | Goes with the identity-only family | K2 |
| `models/Native/Live.cs:61-65,87,91,100,103,108,110-111` | `RespondBeforeCommit` per route; `HttpHost` takes `IKernelStore` and `Worker`; `InstanceRef.In(local.Partition, …)`, `local.Post(…, ambient:)`, `store.FindSubscriptionsAsync`, `EventRoute`, `EventRow`, `store.AddEventAsync` | `RequestAsync` with `Target.Start` or `Target.Event` and `RequestOptions(Ambient, Deadline, ReleaseBeforeCommit, Inline)` | K2 defines, K8 runs |
| `models/Native/Incidents.cs:108` | `CreationProtocol.Origin` for the operation key | `turn.Instance.Ref.Id` | K6 |
| `models/Native/InvocationLifetime.cs:69` | `CommitClockFacts.ModelPrefix` | Goes with the commit clock (owner question 3); retention reads `Now` and `EndedAt` | K6 |
| `models/Native/NativeModel.cs:267,537`, `models/Bpmn/BpmnTurn.cs:57,61,64,223,256` | `Events.Delivered(m)`, `InboxKey.Issued(key) == m.InboxKey`, `ChildAddress(…).Creation`, `SendOptions(TargetCreation: …)` | `Message.Event`; a timer is `Sender == Header.Ref` with the id `Send` returned, which no outside id can equal (FC-3); exact refs | K6 (Native), after release 1 (BPMN) |
| `models/Values/ValueModel.cs:44,49`, `models/Agent/AgentModel.cs:37-69`, `models/Etl/EtlModel.cs:51-52` | `Kinds.Start`, `JobFinished`, `Event` | `Kinds.Start` and `JobFinished` stay; `Kinds.Event` becomes `Message.Event != null` | K6 |

### Layer 2 knowing a workflow-specific type

| File:line | What | Fix | Milestone |
|---|---|---|---|
| `src/Kernel.Durable/Store.cs:13` | `InstanceRow` carries `Model`, `Definition`, `Suspended`, `Children`, `StateVersion` (FINDINGS L1) | `Type`, `TypeData` and `Labels` beside the generic columns, `EndMark` and `EndedAt` added; `Children` as the foundation's own registry | K2 defines, K3 builds (a store change, two reviews) |
| `src/Kernel.Durable/Messages.cs:10,18` | `InstanceStatus` (L2); `StartSpec(Model, Definition, Parent, …)` (L4) | `ActorStatus`; `StartSpec(Type, TypeData, Input, Ephemeral, History)` with the parent set by `Start` | K2 |
| `Messages.cs:70-73,81` | The command kinds and `IsControl` (L3) | Kept as the contract's closed `Kinds`; the foundation knows them as commands only | K2 |
| `src/Kernel.Durable/KeyAdmission.cs:29` | The literal `"child-finished"` | `Kinds.ChildFinished` | K2 |
| `src/Kernel.Durable/Turns.cs:173,401` | `TurnInput.Model`; `JobTicket(model, key, arguments)` | The ticket carries the actor type's `Kind`; the model is the type's business inside `Arguments` | K2 |
| `src/Kernel.Durable/Replies.cs:9,62`, `Worker.cs:1461-1473,1498,1510,2104-2105,2164-2165`, `Creation.cs:94,139,171`, `StoreBatchRules.cs:76` | `InstanceStatus` and `Parent` | None: both are generic under the 15 November card | — |

Leaks that stay inside the built-in foundation and stop being a contract concern: L5 (the take is lease and fence),
L6 (`IKernelStore : IMembership`), L7 (`Flaw`), L8 (`TrialGate`), L10 (`IReplyTransport`), L11 (`InstanceRef` embeds
the partition), L12 and L13.

## What K2 builds

The plan's K2 row, restated against this page ([plan](../program-goals/clean-slate-kernel.md#milestones)): the
contracts package, or packages (owner question 4), with every type above and nothing else; an architecture guard that
fails any reference from layer 3 to the built-in foundation's packages and any member of the contracts package whose
name or type names a partition, virtual partition, epoch, fence, lease, position, node record, `IKernelStore` or SQL;
representative activities, the native model, a second model and the runtime API compiled against the package alone
and run on refs, handles, selections and page tokens with no parseable structure (FC-13); FC-1 to FC-18 as executable
cases on the built-in foundation with the in-memory store and the simulation, each with a mutation that fails without
it; and `IKernelStore` kept as the built-in foundation's internal storage contract, with the store conformance suite
passing on the in-memory store. Tenancy's T1 stays at K2.

## For the owner

1. **The 16 November rule's wording.** It says layer 3 depends on "the turn contract, the four logical seams (commit,
   ownership, delivery, read models) and read models". Under this page, commit and ownership are invisible to
   layer 3; the seams layer 3 sees are the actor turn, ingress and read models. ADR 0099 sections 3, 8 and 9 and the
   register are rewritten so, marked proposed, with the approved text kept beside them. Does the rule take the new
   wording?
2. **Children, the parent notice and the terminate cascade as foundation guarantees, and the actor record's columns.**
   The 15 November card reads: "The instance table is an actor table, with an explicit `type` column beside `model`
   and only actor-generic columns; each actor type has its own read models." This page keeps `type` and puts `model`
   inside the actor type's `TypeData`, with `Parent`, `Status`, `Fault`, `EndMark` and `EndedAt` as the generic
   columns and `Labels` as the type's indexable pairs; the child registry, the `child-finished` notice with its parked
   form (`Final` false, large fan-out decision 5) and the cascade move to the foundation, continuing the fifth review's
   paged cascade. Is that the card's reading, and may `model` leave the table?
3. **The identity-only ended row and the commit-time facts.** The prototype's admission experiments gave a keyed
   Ephemeral instance a state-free ended row (`IStateFreeTerminalMetadata` and seven related types) and a commit clock
   a model stamps and resolves (`ICommitClock`, `Stamp`, `Resolve`, `Forget`). Neither is on an approved page; EF Core
   and MongoDB refuse clock-bearing writes today. The cut keeps the state on the ended row and gives retention the
   foundation's `EndedAt` and the model's own `Now` readings. Cut both?
4. **Package and namespace names.** The register says "a contracts package". One package for this contract and the
   model contract, or two (a foundation-facing one and a model-facing one)? The K2 row is neutral until this is
   answered.
5. **Adapter acceptance.** Is FC-1 to FC-18 the whole acceptance for a future foundation adapter, or must the
   simulation also run over the contract against it?
