# The foundation contract

> **Proposed.** A coordinator design under the owner's two cards of 19 November 2026 (retire the Orleans adapter;
> layer 3 adds Elsa's capabilities behind a strong contract into which foundations plug; "the contracts and domain
> language stay as clean and small as practically possible"). It awaits owner approval and authorizes no product
> code; K2 builds from it once approved. Register: [the foundation contract](decisions.md#the-foundation-contract-19-november-proposed).

A **foundation** provides what layers 1 and 2 provide today: placement, activation, single-writer turns, the durable
inbox, the atomic outcome write, timers, jobs, events, subscriptions, children, poison handling and the journal. The
kernel ships one, the built-in foundation on the database. **Layer 3** adds Elsa's capabilities: actor types, execution
models, activities, Studio, the operator APIs and the read models' meaning. **This contract** is what sits between
them. It is written in plain domain words. No partition, epoch, fence, lease, position, `IKernelStore` or SQL appears
in it; those stay inside the built-in foundation, which keeps `IKernelStore` as its own way to swap EF Core, MongoDB
and in-memory storage.

## Measure first

What layer 3 can reach today, counted from the prototype on main (`src/Kernel.Workflows`, the `Kernel.Durable` and
`Kernel.Runtime` types its models and `WorkflowInstance` use):

| Surface | Types | Members |
|---|---:|---:|
| Built today: `ITurn`, `IExecutionModel`, the metadata interfaces, `Message`, `StartSpec`, `SendOptions`, `InboxKey`, `Kinds`, `IJob`, the commit clock, `IActorType`, `ITurnServices`, `InstanceRef`, `EffectKey` and the rest | 32 | 183 |
| Plus the gate 2 additions the pages approved and nobody built (`RestoreWaits` and its plan, `TimerSpec`, `JobSpec`, `SendOptions.DueAt`, `TerminalGrowth`, `IActorType.IsCommand`, `ITurn.Instance.Tenant`, `BoundScope`) | 37 | 199 |
| Beside it, what the HTTP adapter and hosts reach for ingress and reads: `IKernelStore` (30 members), `Worker.Post`, `Requests`, `ReplyTo`, `EventRoute`, `EventRow`, `SubscriptionRow` | 7 | 50 or more |
| **After the cut, the whole foundation contract** (actor side, ingress and read models) | **29** | **126** |
| Of which the actor side alone, the fair comparison with the first two rows | 20 | 103 |
| Layer 3's own model contract, built on the actor turn (`ITurn`, `IExecutionModel`, `IModelMetadata`, `InstanceInfo`, `WorkflowStart`), counted apart | 5 | 48 |

Counting method: public types and their public members, positional record fields and enum values included, static
helpers excluded. Members that today reach a model only through `Message` or `InstanceRef` (seventeen and nine) are the
largest cut.

## The contract

Every member below has one meaning and carries a guarantee. A guarantee's case id (FC-n) is defined under
"What a foundation adapter must supply".

### Words

| Type | Members | Meaning and guarantee |
|---|---|---|
| `ActorRef` | `Id` | An actor's address, an opaque string. Layer 3 stores and compares it ordinally and never parses it (FC-14). A ref the foundation issued (`Self`, `Start`, `Message.Sender`, `ChildFinished.Child`) is **exact**: it names one life of the actor, so a message to it never reaches a later actor under the same name (FC-17). Its string is the **origin** the effect key uses |
| `BoundScope` | `Tenant` | The tenant bound at ingress by ADR 0097's binder; internal constructor, issued only by the hosting adapter ([tenancy](tenancy.md#where-the-tenant-comes-from)). Every ingress call takes one and refuses without it (FC-12) |
| `ActorStatus` | `Running`, `Finished`, `Faulted`, `Cancelled` | Whether an actor still runs and how it ended. The foundation writes `Faulted` on Park and reports the status at a request's end; the actor type sets the rest through `End` |
| `History` | `Full`, `State`, `Minimal` | What a commit leaves in the journal: the turn's entries, only that the state changed, or nothing (ADR 0099 section 6) |
| `Message` | `Id`, `Kind`, `Payload`, `Sender`, `Event`, `Read<T>()` | One inbox entry. `Id` is the string `Send` or `Publish` returned, or the caller's; `Sender` is an exact ref or null for an outside add; `Event` is set on an event delivery. Delivered at least once, deduplicated by id, in one sender's order (FC-3) |
| `EventDelivered` | `Key`, `Id`, `Wait` | What an event delivery is for: the key, the event's id and the wait that asked. Replaces parsing the delivery id |
| `StartSpec` | `Type`, `Header`, `Input`, `Ephemeral`, `History`, `ShortWait` | What creates an actor: its actor type, the type's own opaque header (a workflow's model and definition), the input, the persistence choice and history level, and how long an unwritten Ephemeral actor may wait in memory (walkthrough decision 4) |
| `SendOptions` | `Delay`, `DueAt` | A timer: due after a delay, or at an absolute time on `Now`'s clock written unchanged (gate 2 fix round). Both set is a model fault. Delivered no earlier than due, and after any owner change (FC-4) |
| `Placement` | `WithParent`, `Spread` | A hint for `Start`: keep the child near its parent so a call tree commits together, or spread a wide fan-out. A hint only; the foundation places |
| `IJob` | `Actor`, `Key`, `Arguments` | What `ExecuteJobAsync` receives. A job may run more than once; its result changes state once (FC-5) |
| `JobFinished` | `Key`, `Result`, `Error` | The payload of kind `job-finished`, the one message a job's completion becomes |
| `ChildFinished` | `Child`, `Status`, `Output` | The payload of kind `child-finished`: the foundation's notice to a parent that a child it started has ended (FC-8) |
| `TimerSpec`, `JobSpec`, `SubscriptionSpec`, `RestorePlan` | `(Id, Kind, Payload, Due)`, `(Key, Arguments, EffectKey)`, `(Key, Wait, Keep)`, `(Timers, Jobs, Subscriptions)` | The waits an actor's committed state holds, as `RestoreWaits` returns them; a recover re-arms each from the spec alone (observability, [recover](observability.md#recover)) (FC-10) |
| `Kinds` | `Start`, `JobFinished`, `Event`, `ChildFinished`; the commands `Suspend`, `Resume`, `Terminate`, `Delete`, `Recover`, `Discard`, `Migrate`; `IsCommand(kind)` | The kinds the foundation itself produces or treats apart. The command list is closed and grows only by amendment (register, third review R3-M4). A command passes withholding, is delivered alone and reaches an ended actor (FC-9) |
| `ActorHeader` | `Ref`, `Type`, `Tenant`, `Status`, `Fault`, `Parent`, `StateVersion`, `Header` | What the foundation keeps of every actor beside its opaque state, and what read models show. `Header` is the actor type's own opaque field (`StartSpec.Header`, changed by `SetHeader`) |
| `EffectKey` | `For(origin, node, iterationPath, attempt)`, `RootPath` | `{instance origin}/{node}/{iteration path}/{attempt}`, from committed facts only (owner, 16 November). `origin` is `Self.Id` (FC-15) |

### What layer 3 supplies: `IActorType`

| Member | Meaning and promise |
|---|---|
| `Kind` | The actor type's key, stored as `ActorHeader.Type` and used to route a message to the type |
| `Withholds(ActorHeader)` | True when the actor may not be given ordinary messages now: suspended, or its row was written by a newer version than this node has (F14). Commands still pass |
| `TurnAsync(IActorTurn)` | One turn. The actor type reads the inbox and state in memory and produces everything through the turn; nothing leaves before the commit. Commands arrive here as messages. A throw parks the actor |
| `ParkAsync(IActorTurn, error)` | The actor type's turn after the foundation parked the actor: no model code, only what the type records about the fault. A throw here parks with no actor-type part |
| `ExecuteJobAsync(IJob, token)` | A background job's code, outside the actor's turns; at least once |
| `RestoreWaits(ActorHeader, state)` | The plan of waits the committed state holds, for a recover |
| `TerminalGrowth(ActorHeader, state)` | An upper bound, in bytes of the state encoding, on what the type's end and terminate paths add; 0 when nothing (seventh review R7-B1) |

Layer 3 promises in return: a turn is a pure function of inbox, state and `Now` except through the turn's members; it
reads no other actor's state; it stores refs and handles as strings; it keeps its state readable across versions
(`StateVersion` and the model's `UpgradeState`); it never names a tenant; and its waits come back exactly from
`RestoreWaits`.

### What a turn receives and returns: `IActorTurn`

| Member | Meaning and guarantee |
|---|---|
| `Self` | This actor's exact ref |
| `Tenant` | The actor's tenant, bound for the turn's services; everything the turn produces carries it (FC-12) |
| `Header` | The actor's `ActorHeader` as committed |
| `Now` | Database time as the owner knows it; the one clock timers and `DueAt` use |
| `Inbox` | The messages this turn is given, in delivery order (FC-3) |
| `State` get, set | The opaque state; what is set is what the commit writes |
| `Ambient` | What the starter on this node gave: this activation only, never stored |
| `Spec` | The `StartSpec` that creates the actor, on its first turn only |
| `Emit(channel, item)` | Live output to whoever observes, released when the commit lands or at once when there is nothing to write (FC-16) |
| `Reply(message, channel, item)` | The answer to one consumed request, once, after the commit (FC-16, FC-18) |
| `Send(target, kind, payload, options)` → id | A message recorded with this commit and delivered later, at least once, deduplicated by the returned id (FC-2, FC-3, FC-4) |
| `Recall(id)` | Takes back an undelivered message to self (a timer); its row goes with this commit (FC-4) |
| `Start(spec, placement, detached)` → ref | A child whose exact ref is allocated in this commit; it reports back as `child-finished` and a terminate goes on to it, unless detached (FC-8) |
| `StartJob(key, arguments)`, `CancelJob(key)` | The intent is a ticket in the commit; after a cancel the result is never applied; past the poison threshold the job parks the actor (FC-5) |
| `Hold(message)` | Handed to the next turn again, in its place in the order (FC-20) |
| `Subscribe(key, wait, keep)`, `Unsubscribe(key, wait)` | A wait for events of the key from this commit on; `keep` also gives it the kept events (owner default: off) (FC-6, FC-7) |
| `Publish(key, payload, keep)` → id | An event, durable with this commit, delivered to every subscription committed before it and kept for `keep` for waits that ask (FC-6) |
| `Record(entry)` | A journal entry of this commit, at the actor's history level (FC-11) |
| `End(status, output)` | The actor ends with this commit: no ordinary turn follows, the parent is notified, waits are removed (FC-17) |
| `SetHeader(header)` | Replaces the actor type's opaque header with this commit (a rebind, a suspend flag) |
| `Stamp()` → reference, `Resolve(reference)`, `Forget(reference)` | A commit-time fact: the reference resolves, after the commit lands, to that commit's database time and never changes; `Forget` drops it (FC-13) |

### Ingress: `IIngress`

What the operator API, the HTTP adapter and the ingress adapters (brokers, webhooks, schedulers as idempotent adds)
call. Each takes a `BoundScope` first.

| Member | Meaning and guarantee |
|---|---|
| `SendAsync(scope, target, id, kind, payload, options)` → `Admission` | An outside add. The same id twice is one message; the second answers `Duplicate` (FC-18) |
| `StartAsync(scope, spec, key)` → `Admission` | Creates an actor, or, with a caller key, resolves the key to its actor in that tenant and refuses a repeat within its window (walkthrough decision 7) |
| `RequestAsync(scope, target, id, kind, payload, deadline)` → `Reply` | A send whose consuming turn's `Reply` is returned once, or the status and error at the deadline, under the plan's [HTTP contract](../program-goals/clean-slate-kernel.md#the-http-error-and-timeout-contract-k8-proposed) |
| `PublishAsync(scope, key, eventId, payload, keep)` → `Admission` | An outside event; `(key, id)` twice within the window is one publication (FC-6) |
| `RegisterStartAsync(scope, key, wait, spec)` | A start subscription: every event of the key starts an actor (FC-6) |

`Admission(Outcome, Actor, Reason)`: `Accepted`, `Duplicate` or `Refused` with the reason, and the ref the call
created or resolved. `Reply(Channel, Item, Status, Error)`.

### Read models

As the observability page defines them ([read models](observability.md#read-models-how-layer-3-stays-off-partitions)):
`IActorReads` (`GetAsync`, `ListAsync`, `TailAsync`: an actor's header and view, the tenant's list, and the journal
tail past a position carried as an opaque `PageToken`), `IParkedWork` (`ListAsync`, `GetAsync`) and `IInboxInspection`
(`PendingAsync`, `KeptAsync`). Every answer says how fresh it is; handles and page tokens are opaque; no answer ever
shows an uncommitted entry (FC-11).

### The model contract, built on the actor turn

Layer 3's workflow actor type offers execution models `ITurn`: the actor turn's members except `Header`, `SetHeader`
and `End`, plus `Instance` (`Ref`, `Tenant`, `Model`, `Definition`, `Parent`), `Rebind(definition)` and
`SetStatus(status, subStatus, output)`; `Start` takes a `WorkflowStart(Model, Definition, Input, Ephemeral, History)`.
`IExecutionModel` keeps `Key`, `StateType`, `StateVersion`, `CreateState`, `UpgradeState`, `TurnAsync`,
`ExecuteJobAsync`, `RestoreWaits` and `TerminalGrowth`; `IModelMetadata` keeps `HandlesMetadata`, `MetadataAsync`,
`TerminateMetadata` and `ProtectsDeletion`. Everything here is layer 3's own and reaches the foundation only through
`IActorTurn`.

## The vocabulary

### Terms the contract introduces

| Term | Why it is needed |
|---|---|
| foundation, foundation adapter | The owner's words of 19 November for what replaces layers 1 and 2 as a whole |
| actor, actor type, actor ref, header | The three layers' own words (12 and 15 November); `ActorRef` replaces `InstanceRef` because job and plan actors are not instances, and `Header` is the one opaque field an actor type keeps beside its state |
| exact ref, origin | One word for "this life of this actor", so creation numbers never cross the contract; the effect key's first segment |
| admission, duplicate | The ingress answer's words; the storage model already says "admission" |
| commit-time fact: stamp, resolve, forget | Plain verbs for the commit clock's capture, resolve and release |

### Terms the current contract or seams use

| Term | Disposition | Reason |
|---|---|---|
| partition, virtual partition, epoch, position, fence, lease, node record, heartbeat, take, release, retake, poll, seen, trial, `seq`, owned set, pass | Into the built-in foundation | How that foundation gives one writer per actor and an atomic commit; a foundation on Orleans or Proto.Actor has none of them |
| `IKernelStore`, `CommitBatch`, `TurnCommit`, `PartitionFence`, `Committed`, `Stored`, `Polled`, `IMembership`, `Cluster`, the projector's cursor, `Viewer.ReadAsync(partition, …)`, `SweepAsync` | Into the built-in foundation | Its storage contract; still how it swaps EF Core, MongoDB and the in-memory store |
| `IActorRuntime`, `IScopeHost`, `IScopeActivation`, `IScopeCount`, `IWakeSignal`, `Worker`, `DurableHost`, `PartitionRuntime` | Into the built-in foundation | Its layer 1 seam, kept with its conformance suite (owner, 19 November) |
| `InboxKey`, scope, `Match`, the derived-id grammar `{origin}#{revision}.{ordinal}`, `TicketId`, `CreationProtocol` (origin, fingerprint, terminal notice id, retire), `StoredState`, `DurableKeyJson` | Into the built-in foundation | Identity encodings. Layer 3 compares ids as strings |
| `KeptBy`, `ReleasedBy`, `Cohort`, `P_cascade`, `kernel.drain`, `kernel.park-expiry`, `kernel.cascade`, `Incarnation`, `CatchUpId`, `CatchUpCover`, `EventHorizon`, publish and meet tickets, `kernel.job`, `kernel.meet`, `kernel.publish` | Into the built-in foundation | The gate 2 mechanisms behind Park, recovery, termination and event matching. The guarantees stay (FC-6, FC-9, FC-10); the mechanisms are one foundation's |
| `Requests`, `ReplyTo.Of`, `ReplyTo.Parse`, `IReplyTransport`, `Worker.Post` | Into the built-in foundation, behind `RequestAsync` | The reply route between nodes is a transport detail |
| `Flaw`, `ITurnServices.Has` | Dropped from the contract | Mutations are the built-in foundation's tests; models keep their own flaw enums |
| `ITurnServices` (`Partitions`, `ShortWait`, `CommitClockAvailable`, `CreationIdentityAvailable`, `Cancelling`) | Dropped | Capability flags are refused by the profiles decision; every foundation supplies the clock and exact refs (FC-13, FC-17), and the turn's members carry the rest |
| `InstanceRef.Partition`, `In`, `Spread`, `ForChild`, `ForEvent`, `EventStartAddress`, `ChildAddress`, `GeneratedAddress` | Dropped | Address parsing (the plan's K2 row). `Placement` is the hint that replaces `Spread` |
| `Message.Target`, `Start`, `Due`, `KeepUntil`, `ReplyTo`, `TargetCreation`, `SenderCreation`, `InboxScope`, `EventMatch`, `InboxKey`, `WithInboxScope`, `AsIssued`, `AsOrdinary` | Dropped | Row fields and encodings. The target is `Self`; the start is `IActorTurn.Spec`; the reply route is behind `Reply`; creations are inside exact refs. The row columns `TargetCreation` and `SenderCreation` stay in the built-in foundation unchanged |
| `Events.Delivered(message)` | Replaced by `Message.Event` | Parsing a delivery id was friction F-N3 already |
| `SendOptions.Start`, `SendOptions.TargetCreation`, `ChildAddress(child)`, `CreationAddress`, `CreationStart`, `InstanceInfo.Creation`, `ParentCreation` | Dropped | Exact refs carry the creation; a send creates nothing, `Start` does |
| `Redelivered(message)` | Dropped | A hint with no guarantee ("false proves nothing"); no model's logic reads it, only a mutation |
| `IStateFreeTerminalMetadata`, `TerminalIdentity`, `IIdentityMetadataTurn`, `IdentityMetadataUnavailable`, `CanUseIdentityOnly`, `ProtectsIdentityDeletion`, `IdentityMetadataAsync`, `ICommitClockObservation`, `InstanceRow.IdentityOnly`, `IdentityMetadataRequired`, `ModelMetadataUnavailable` | Dropped (owner question 3) | An optimisation of the prototype's admission experiments: an ended keyed Ephemeral row that keeps no state. On no approved page. `IModelMetadata` with the state keeps the retention and deletion protection |
| `ICommitClock.Available`, `Require`, `CommitClockFacts.ModelPrefix` | Dropped | Every foundation supplies the clock; the fact's encoding is the foundation's |
| `IActorType.SingleMessage` (both), `SelectsAlone`, `RevisitsHeld`, `Command`, `Write`, `Park(…, pending)`, `SelfReference`, `TurnInput`, `TurnOutcome`, `Delivery`, `InstanceRow`, `DurableTurn` | Replaced by `IActorTurn`, `ParkAsync` and `Kinds.IsCommand` | Delivery batching and row building belong to the foundation; a command is a message like any other to the actor type, with the closed list saying which are delivered apart |
| `InstanceStatus` | Kept as `ActorStatus` | Generic (FINDINGS L2): the foundation writes `Faulted` and reports the status at a request's end |
| `StartSpec.Model`, `Definition`, `Parent`; `InstanceRow.Model`, `Definition`, `Children`, `Suspended` | Into `Header` (model, definition, suspended) and the foundation's child registry (children) | FINDINGS L1 and L4: the actor record keeps actor-generic columns only (owner, 15 November); `Parent` is generic |
| `WorkflowKinds.ChildFinished` | Kept as `Kinds.ChildFinished`, the foundation's | The parent notice, the child registry and the terminate cascade are the foundation's once the fifth review made the cascade a paged layer 2 obligation (owner question 2) |
| turn, job, inbox, message, send, recall, hold, start, child, detached, timer, event, subscription, wait, keep, publish, record, journal, history, Ephemeral, short wait, Park, fault, recover, discard, terminate, suspend, resume, delete, migrate, effect key, tenant, bound scope, state version, live output, ambient, handle, page token | Kept | The approved pages' words, already plain |
| `RestoreWaits`, `TimerSpec`, `JobSpec`, `TerminalGrowth`, `DueAt` | Kept, plus `SubscriptionSpec` | Approved recovery and envelope obligations, already in contract terms; the subscription entry is named like the other two |
| `IActorType.IsCommand` | Replaced by `Kinds.IsCommand` | The list is closed by decision; a callback would let an actor type widen it |

## How the built-in foundation implements it

| Contract item | Prototype today (`prototypes/kernel-model`) |
|---|---|
| `ActorRef`, exact ref, origin | `InstanceRef` with the partition in the id; `CreationProtocol.Origin(id, creation)` and `CreationAddress` |
| `IActorType` | `IActorType` in `Kernel.Durable/Turns.cs` (15 members) and `WorkflowInstance` |
| `IActorTurn` | `DurableTurn` plus `TurnInput`, with `Turn` in `Kernel.Workflows` adding the workflow part and `Seal` building `InstanceRow` and `TurnOutcome` |
| `End`, `SetHeader`, `ActorHeader` | `InstanceRow`'s columns `Status`, `Model`, `Definition`, `Suspended`, `Children`, `Fault`, `Parent` |
| The atomic outcome | `Worker` flush → `IKernelStore.CommitAsync(CommitBatch)` with the k-row fence |
| Single-writer turns | Node lease, partition epochs and the fence (`Worker`, `DurableHost`, `PartitionRuntime`, `IKernelStore.TakeAsync`) |
| Inbox, dedup by id, sender order | Message rows keyed `InboxKey`, `seq` per sender, `Seen`/`Trial` marks, `PollAsync` |
| Timers | Message rows with `Due`; `Recall` deletes by derived id |
| Jobs | `kernel.job` tickets, `Worker`'s job drivers, `JobFinished` ahead of other input, `JobGeneration` |
| Events and subscriptions | `kernel.publish` and `kernel.meet` tickets, `Events`, `SubscriptionRow`, `EventRow`, the match digests |
| Children, notice, cascade | `ChildIssuer`, `Children` on the row, `WorkflowInstance.Finished`, the terminate's `Sent`, the cascade obligation |
| Commands | `Kinds.IsControl`, `WorkflowInstance.Command`, `Withholds` |
| Park and recover | `IActorType.Park`, `ParkRecord`, the drains and `kernel.drain` |
| `IIngress` | `IKernelStore.AddAsync`, `Events.PublishAsync`, `Events.SubscribeAsync`, `IKeyAdmission`, `Worker.Post`, `Requests` and `ReplyTo` |
| Read models | `Viewer.ReadAsync`, `Projector`, `ReadViewsAsync`, `ReadJournalOfAsync` |
| Commit-time facts | `TurnClock`, `CommitClockFacts`, `InstanceRow.CommitTimes` |
| Tenant | Not yet in the prototype; the tenancy page's `Q(t, n)` and `tenant` column |

## What a foundation adapter must supply

Each case is checkable on the contract alone and runs first on the built-in foundation at K2 (in-memory store and the
simulation), then on every provider at K4. A future adapter passes the same suite.

| Case | Guarantee |
|---|---|
| FC-1 | Single writer: no two turns of one actor overlap or commit interleaved; after an owner change the new owner's first turn sees every committed outcome and a stale owner's outcome never lands |
| FC-2 | Atomic outcome: state, header, journal entries, sends, timers, job tickets, subscriptions, publishes and child starts of one turn land together or not at all; nothing is delivered or released before they land |
| FC-3 | Inbox: a message is delivered at least once and consumed only by a committed turn; two sends with one id are one message; one sender's messages to one target arrive in the order sent |
| FC-4 | Timers: a send with `Delay` or `DueAt` is delivered no earlier than due, survives owner loss and process death, and `Recall` prevents delivery of a message not yet given |
| FC-5 | Jobs: a committed `StartJob` runs at least once after any owner change; its result changes state once; after `CancelJob` no result applies; past the poison threshold the actor parks with the job's error |
| FC-6 | Events: a publish committed after a wait's commit always reaches it; one whose matching finished before the wait's commit reaches it only when the wait keeps; one still being matched when the wait committed may reach it, with no clock bound (owner, 18 November); an outside publish with the same `(key, id)` within its window is one publication; a start subscription starts one actor per event |
| FC-7 | Subscriptions: a wait exists from its commit; `Unsubscribe`, Park and `End` remove it; the same wait subscribed twice is one wait |
| FC-8 | Children: `Start` yields an exact ref before the commit; the child's first turn runs only after the parent's commit lands; the parent receives one `child-finished` per child that ends; a terminate of the parent reaches every child not yet ended, across a parent that ended first and across any owner change; a detached child receives none of this |
| FC-9 | Commands: a command is delivered alone, passes `Withholds`, reaches an ended actor, is never discarded as poison, and every other kind is withheld while `Withholds` is true |
| FC-10 | Park and recover: a throwing turn leaves the actor `Faulted` with its state and journal intact, its inputs consumed and kept, its waits removed, and `ParkAsync` run once; a recover re-arms exactly the `RestorePlan`, each timer at its recorded due, each job under a new generation, each subscription with its keep flag, and no wait outside the plan |
| FC-11 | Journal and reads: every `Record` entry appears once in the actor's history in commit order; a read model shows no uncommitted entry and says how fresh it is; a tail read after a token returns only later entries |
| FC-12 | Tenancy: an ingress call without a `BoundScope` is refused; a turn's sends, starts, publishes, subscriptions, jobs and entries carry the actor's tenant; names chosen by callers never collide across tenants |
| FC-13 | Commit-time facts: a `Stamp` of a turn whose commit lands resolves to that commit's database time in every later turn and never changes; one whose commit did not land resolves to nothing |
| FC-14 | Opaque identity: an actor type, a model and the operator API compile against the contracts package alone and run on refs, handles and page tokens that carry no parseable structure |
| FC-15 | Effect key: one activation re-derives one key under replay and after an owner change; two activations never share one; a key never leaves in two turns that both landed |
| FC-16 | Live output: `Emit` and `Reply` reach an observer only after the commit lands, or at once when the turn has nothing to write; a turn whose commit is refused emits and replies nothing |
| FC-17 | Ended actors: after `End`, `Delete` is the only command that changes the actor; a message to a deleted actor's exact ref is dropped; a generated name never comes back; a caller key refuses a repeat within its window |
| FC-18 | Ingress idempotence: `SendAsync` with an existing id answers `Duplicate` and adds nothing; `RequestAsync` returns at most one reply and the status at its deadline otherwise |
| FC-19 | Envelope: a turn whose state plus `TerminalGrowth` plus the foundation's own fields would exceed the record cap fails and parks; the actor's end and terminate always fit |
| FC-20 | Hold: a held message is given to the next turn in its place in the order, is not consumed, and counts as consumed only by the turn that does not hold it |

## Leaks to fix

An inventory on main; no code changes here. Lines are those of main at `9583f4da34`.

### Layer 3 reaching below the contract

| File:line | What | Fix | Milestone |
|---|---|---|---|
| `src/Kernel.Workflows/Turn.cs:57,59,114` | `Instance.Ref.Partition`, `InstanceRef.Spread(name, Services.Partitions)`, `Id.StartsWith($"g{partition}/key/")`: placement and key detection by parsing the id | `Placement` hint; `IActorTurn.Spec` tells a keyed start | K2 |
| `Turn.cs:88-91,135-137` | Builds `InstanceRow` with `CreationProtocol.Fingerprint`, `StartIntent`, `Taken[From]`; edits `CommitTimes` through `WorkflowCommitClock.End` | The foundation builds its record from `End`, `SetHeader`, `State`; the end stamp is its own | K2 |
| `src/Kernel.Workflows/WorkflowInstance.cs:14-29` | `SingleMessage`, `SelectsAlone`, `RevisitsHeld`, `Withholds` read `InstanceRow`, `Delivery.IsDurableJobTicket`, `Flaw` | `Withholds(ActorHeader)`; batching rules move into the foundation | K2 |
| `WorkflowInstance.cs:43-47,50,61,211-212` | `CreationProtocol.HasTerminalNoticeId`, `Refuse`, `TerminalNoticeId`, `TargetCreation`, `SenderCreation` checks on the parent notice | The notice and its checks are the foundation's (`Kinds.ChildFinished`) | K2 |
| `WorkflowInstance.cs:57-58,72-73,81-90,129,148,165-171,192-199` | Builds `TurnOutcome` with `Expected`, `Revision`, `Journal`, `Retire(row)`, `Delete`, `Unsubscribe`, composes terminate ids `{origin}#{revision}.{i}` and `InboxKey.Ticket` cancels (FINDINGS L9) | `End`, `Record` and the foundation's cascade and job cancel on `End(Cancelled)` and `Delete` | K2 |
| `WorkflowInstance.cs:138-143` | `RemainingJobs` matches tickets by `InboxKey.Ticket` and `IsWorkerJobCompletion` | The foundation keeps the job registry | K2 |
| `src/Kernel.Workflows/EffectKey.cs:42` | `CreationProtocol.Origin(instance, creation)` | `Self.Id` is the origin | K2 |
| `src/Kernel.Workflows/Contracts.cs:8,62` | `InstanceInfo.Ref` is `InstanceRef` (plan K2 row); `TerminalIdentity.From(InstanceRow)` | `ActorRef`; the identity-only family goes | K2 |
| `src/Kernel.Workflows/IdentityMetadataTurn.cs:7-8,28` | `TurnOutcome`, `DurableTurn`, `InboxKey` | Goes with the identity-only family | K2 |
| `models/Native/Live.cs:87,91,100,103,108,110-111` | `HttpHost` takes `IKernelStore` and `Worker`; `InstanceRef.In(local.Partition, …)`, `local.Post`, `store.FindSubscriptionsAsync`, `EventRoute`, `EventRow`, `store.AddEventAsync` | `IIngress.StartAsync`, `RequestAsync`, `PublishAsync` | K2 defines, K8 runs |
| `models/Native/Incidents.cs:108` | `CreationProtocol.Origin` for the operation key | `turn.Instance.Ref.Id` | K6 |
| `models/Native/InvocationLifetime.cs:69` | `CommitClockFacts.ModelPrefix` | `Forget(reference)` on every reference the model holds | K6 |
| `models/Native/NativeModel.cs:267,537`, `models/Bpmn/BpmnTurn.cs:57,61,64,223,256` | `Events.Delivered(m)`, `InboxKey.Issued(key) == m.InboxKey`, `ChildAddress(…).Creation`, `SendOptions(TargetCreation: …)` | `Message.Event`; compare `Message.Id` to the id `Send` returned; exact refs | K6 (Native), after release 1 (BPMN) |
| `models/Values/ValueModel.cs:44,49`, `models/Agent/AgentModel.cs:37-69`, `models/Etl/EtlModel.cs:51-52` | `Kinds.Start`, `JobFinished`, `Event` | None: these kinds stay in the contract's `Kinds` | — |

### Layer 2 knowing a workflow-specific type

| File:line | What | Fix | Milestone |
|---|---|---|---|
| `src/Kernel.Durable/Store.cs:13` | `InstanceRow` carries `Model`, `Definition`, `Suspended`, `Children` (FINDINGS L1) | `Type` and the opaque `Header` beside the generic columns; `Children` as the foundation's own registry | K2 defines, K3 builds (a store change, two reviews) |
| `src/Kernel.Durable/Messages.cs:10,18` | `InstanceStatus` (L2); `StartSpec(Model, Definition, Parent, …)` (L4) | `ActorStatus`; `StartSpec(Type, Header, Input, …)` with the parent set by `Start` | K2 |
| `Messages.cs:70-73,82` | The command kinds and `IsControl` (L3) | Kept as the contract's closed `Kinds`; the foundation knows them as commands only | K2 |
| `src/Kernel.Durable/KeyAdmission.cs:29` | The literal `"child-finished"` | `Kinds.ChildFinished` | K2 |
| `src/Kernel.Durable/Turns.cs:173,401` | `TurnInput.Model`; `JobTicket(model, key, arguments)` | The ticket carries the actor type's `Kind`; the model is the type's business inside `Arguments` | K2 |
| `src/Kernel.Durable/Replies.cs:9,62`, `Worker.cs:1461-1473,1498,1510,2104-2105,2164-2165`, `Creation.cs:94,139,171`, `StoreBatchRules.cs:76` | `InstanceStatus` and `Parent` | None: both are generic under the 15 November card | — |

Leaks that stay inside the built-in foundation and stop being a contract concern: L5 (the take is lease and fence),
L6 (`IKernelStore : IMembership`), L7 (`Flaw`), L8 (`TrialGate`), L10 (`IReplyTransport`), L11 (`InstanceRef` embeds
the partition), L12 and L13.

## What K2 builds

The plan's K2 row, restated against this page ([plan](../program-goals/clean-slate-kernel.md#milestones)): the
contracts package with every type above and nothing else; an architecture guard that fails any reference from layer 3
to the built-in foundation's packages and any member of the contracts package whose name or type names a partition,
epoch, fence, lease, position or SQL; representative activities, the native model, a second model and the runtime API
compiled against the package alone and run on refs, handles and page tokens with no parseable structure (FC-14);
FC-1 to FC-20 as executable cases on the built-in foundation with the in-memory store and the simulation, each with
a mutation that fails without it; and `IKernelStore` kept as the built-in foundation's internal storage contract, with
the store conformance suite passing on the in-memory store. Tenancy's T1 stays at K2.

## For the owner

1. **The 16 November rule's wording.** It says layer 3 depends on "the turn contract, the four logical seams (commit,
   ownership, delivery, read models) and read models". Under this page, commit and ownership are invisible to
   layer 3; the seams layer 3 sees are the actor turn, ingress and read models. ADR 0099 sections 3 and 8 and the
   register are rewritten so, marked proposed. Does the rule take the new wording?
2. **Children, the parent notice and the terminate cascade as foundation guarantees.** The fifth review already made
   the cascade a paged layer 2 obligation; this page moves the child registry and `child-finished` with it, so
   `Parent`, `Status` and `Fault` are actor-generic columns and `Model`, `Definition` and `Suspended` are the actor
   type's header. Is that the reading of the 15 November card ("only actor-generic columns")?
3. **The identity-only ended row.** The prototype's admission experiments gave a keyed Ephemeral instance a state-free
   ended row (`IStateFreeTerminalMetadata` and seven related types). It is on no approved page. The cut keeps the
   state on the ended row; the receipts' retention still works through `IModelMetadata`. Cut it?
4. **Package and namespace names.** The register says "a contracts package". Proposed: one package for this
   contract and the model contract, or two (a foundation-facing one and a model-facing one)?
5. **Adapter acceptance.** Is FC-1 to FC-20 the whole acceptance for a future foundation adapter, or must the
   simulation also run over the contract against it?
6. **`Redelivered`.** The sketch listed it; no model's logic reads it. Confirm the cut.
