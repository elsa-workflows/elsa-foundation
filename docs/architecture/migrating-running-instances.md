# Migrating running instances

> **Approved design, with conditions.** Gate 2 granted design approval on 18 November 2026 at
> `addffea7c850063d1ebe710b07866ff1640a93cb` ([verdict](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6990560103)). The implementation evidence is owed at K1 to K12,
> and the [gate 2 conditions](../program-goals/clean-slate-kernel.md#gate-2-conditions) stay open until it lands. This page answers design-now question 1 of the [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md#to-answer-in-the-design-proposal-before-the-fable-review-owner-decision-12-november).

A running instance keeps its state across two kinds of change:

- **A code upgrade.** A release changes how a model or an activity serializes its state. The instance stays on its definition; its stored state must be read by the new code.
- **A definition migration.** An operator moves the instance to another version of its definition. Its state must be mapped onto the new version's nodes.

Both happen inside a turn of the instance and commit through the ordinary fenced commit. Neither adds a new kind of record, a table or a second store to the commit, and neither reads partitions, positions or SQL: the design sits in layer 3 on the turn contract, with two exceptions that are kernel behaviour. The migration command must pass withholding, which today only layer 2's closed list of control kinds can grant ([The command](#the-command)). Addressing a plan's instances by creation needs creation headers persisted on every provider ([Plans and bulk migration](#plans-and-bulk-migration)).

Terms used here: a **frame** is the native model's record of one live activity (its node, parent frame, private state, scope and attempt); a **container** is a composite activity whose private state names its children (structured containers, Flowchart, StateMachine); a **wait** is a frame's registered trigger (event, HTTP route, timer, child or job).

## What is built

| Piece | Status | Evidence |
|---|---|---|
| `IExecutionModel.StateVersion` and `UpgradeState(string state, int fromVersion)`; `StateVersion` on the instance row. An older row is upgraded on load and written at the model's version by the next commit | Built, simulated (rolling deploys with rollbacks in every third seed) | [Contracts.cs](../../server/essentials/tests/ActorFoundation/Legacy/Workflows/Contracts.cs), `ReadState` in [WorkflowInstance.cs](../../server/essentials/tests/ActorFoundation/Legacy/Workflows/WorkflowInstance.cs), [FINDINGS 15](../../prototypes/kernel-model/FINDINGS.md#15-state-versions-run) |
| A node behind the deploy never turns a row written by a newer model version: the instance is withheld like a suspended one until a newer owner takes the partition (invariant 18). Delete refuses such a row too | Built, mutation-checked (`TurnNewerState`, formerly `StepNewerState`; `SkipUpgrade`) | `Withholds` and the delete branch of `Control` in WorkflowInstance.cs, [FINDINGS F14](../../prototypes/kernel-model/FINDINGS.md#f14-a-node-behind-the-deploy-must-not-step-a-row-written-by-a-newer-model-invariant-18-run) |
| The legacy Native runner at state version 13, with reader witnesses for older shapes. The public Native execution model writes state version 14 | Built, witnessed | [Legacy reader](../../server/essentials/tests/Workflows/Runtime/Native/Legacy/NativeModel.cs), [public model](../../server/essentials/src/Workflows/Runtime/Native/Model/NativeExecutionModel.cs), and reader witnesses for [scope](../../server/essentials/tests/Workflows/Runtime/Native/Scopes/Run/ReaderWitness.cs), [recovery](../../server/essentials/tests/Workflows/Runtime/Native/Recovery/Run/RecoveryReader.cs) and [Statecharts](../../server/essentials/tests/Workflows/Runtime/Statecharts/Run/NativeInternalReaders.cs) |
| The StateMachine's `MachineState.Version`: a shape marker set when the machine begins. Versions 1 and 2 are upgraded on restore up to 3; 4 to 7 mark feature variants (input inbox, internal execution, outcomes) and are kept as they are | Built, witnessed | `Begin` and `Restore` in [StateMachine.cs](../../server/essentials/tests/Workflows/Runtime/Statecharts/Native/StateMachine.cs) |
| `ITurn.Rebind(definition)`: the commit writes the new definition into the row; from the next turn on the instance belongs to it (invariant 19) | Built in the kernel and the simulation's counter model, mutation-checked (`RebindForgotten`) | `Rebind` in [Turn.cs](../../server/essentials/tests/ActorFoundation/Legacy/Workflows/Turn.cs), [FINDINGS 16](../../prototypes/kernel-model/FINDINGS.md#16-rebind-run) |
| Commands, the contract's closed `Kinds` (`Suspend`, `Resume`, `Terminate`, `Delete`, `Recover`, `Discard`, `Migrate`), pass `Withholds`, go alone and are never discarded as poison (FC-9) | Built in the prototype for suspend, resume, terminate and delete | The prototype's `Kinds.IsControl` in [Messages.cs](../../server/essentials/src/ActorFoundation/BuiltIn/Durable/Messages.cs), [Worker.cs](../../server/essentials/src/ActorFoundation/BuiltIn/Durable/Worker.cs) |
| A public `Migrate` command rebinds a compatible structured Native checkpoint with unchanged declarations through the in-memory Actor Foundation | Built and witnessed for the structured subset | [Native migration application](../../prototypes/kernel-model/README.md#apply-a-compatible-native-migration) |
| Explicit frame or path mapping and per-activity state versions in the frame | Proposed | See the design below |

The Flowchart's state carries no version of its own in the prototype: the extracted graph engine dropped main's private-state envelope ([Graph README](../../server/essentials/src/Workflows/Runtime/Graph/README.md), "What was adapted"), so the native model's whole-state version covers it.

## Proposed: state versions at two levels

**The model level stays as built.** A model bumps `StateVersion` when its own structures change (the native model's agenda, waits, inboxes, scopes). `UpgradeState` takes the stored string, because the kernel cannot deserialize an old shape as the current type. Upgrades are lazy: a row is upgraded when it is next loaded, and a finished instance that is never loaded again is never rewritten. A bulk rewrite is never needed.

**The activity level is new.** An activity's private state (`Frame.State`) belongs to its activity type, which ships in a user's or Elsa's package on its own release cycle. Bumping the native model's version for every activity change would couple every package to the model. So an activity type that keeps state declares its own version:

```csharp
// Layer 3, native model. Optional on an activity type whose frame keeps private state.
public interface IVersionedActivityState
{
    int StateVersion { get; }                                        // 1 when absent
    JsonNode? UpgradeState(JsonNode? state, int fromVersion);       // pure; throws to refuse
}

public sealed partial class Frame
{
    public int V { get; set; } = 1;                                  // the activity state version; not written when 1
}
```

- **When.** The native model upgrades every frame whose `V` is below its activity type's version when it loads the instance, in the same pass as the model-level upgrade. The next commit writes the upgraded frames. Doing it at load gives one place where an upgrade can refuse, before any activity runs.
- **Containers are activities.** A Flowchart's `FlowchartExecutionState` and a StateMachine's `MachineState` are the private state of the container's frame, and their engines version them through the same interface. The StateMachine's `MachineState.Version` stays as the shape marker it is; `V` sits beside it and counts upgrades of the whole `MachineState`.
- **Refusal.** An upgrade that throws is a bug in the upgrade code. It faults and parks the instance with an "activity state upgrade refused" incident naming the frame and node, as any failing turn does. The parked-work tooling retries it after a release that fixes the upgrade, or discards it.
- **Newer than this node, or an unknown activity type.** A frame whose `V` is above what this node's activity type knows, or whose activity type this node does not have, is the F14 case at activity granularity, caused by a rolling deploy or a rollback. The instance is withheld until a node with the newer code owns it.
- **The signal path.** The foundation evaluates `Withholds(ActorHeader)` from the header alone, before it gives the actor ordinary messages ([the foundation contract](foundation-contract.md#what-layer-3-supplies-iactortype)). A newer model state version shows in the header's `TypeData`, but `V` lives inside opaque state, so the header cannot say it. The native model detects the case when it reads state (`UpgradeState` and the frame upgrade throw a typed `NewerStateException`). The workflow actor then holds the turn's messages (`Hold`, FC-18), changes nothing else, and remembers the actor's `Ref` and committed `TypeData` in memory. Its `Withholds` returns true for that header until the header changes or the actor is activated again. Every path that reads state applies the same check: a turn, the terminate path's metadata, delete's `ProtectsDeletion` and a migration. This stays in layer 3 and costs one load per activation to find out.

## Proposed: definition migration

### The command

```csharp
public sealed record Migrate(string From, string To, string Plan);  // definition versions; the plan's id
```

- **Delivery (kernel behaviour).** A suspended instance is withheld, and only commands pass `Withholds`, go alone and are never discarded as poison (FC-9). `Migrate` is one of the foundation contract's closed `Kinds` commands (`Kinds.IsCommand`), and the actor type executes it in its turn (FC-9), so the foundation holds no workflow-specific kind and no `IActorType.IsCommand` is needed ([the foundation contract](foundation-contract.md#words), approved with conditions on 19 November). The closed list grows only by amendment. It is a change to delivery, so it needs the two-review tier when built.
- **The turn.** `Migrate` arrives in the actor type's `TurnAsync` as a message of a closed `Kinds` command kind, delivered alone (FC-9). The workflow actor's command handling loads the state and runs the model's migration hook with the model's `ITurn` in that turn. This part is layer 3. In the prototype, the built control path returns an outcome without an `ITurn` and runs the hook through a `DurableTurn` over the command's input; that is the prototype's mechanism.
- **Order.** The instance's earlier messages run first, under the source definition. `Migrate` runs alone in its turn, and the messages behind it run under the target. One sender's messages reach one target in order, so "suspend, migrate, resume" from an operator applies in that order.
- **Guards.**
  - `From` must equal the row's definition. A migration aimed at another version, or one repeated after it was applied, is consumed and answered "not at source", with no state change.
  - A row written at a newer model state version than this node's, or a state whose activity versions this node cannot read, is left in the inbox with `Hold`, for an owner with the newer code, as in F14. Held messages wait for new input (Worker.cs), so a held `Migrate` is not offered again on every pass. Built delete consumes such a message unchanged; a migration is held instead, so it is not lost.
- **Who may migrate.** A running or suspended instance. A suspended instance stays suspended. An ended instance, including a parked one (Park faults it and removes its waits), is refused.
- **The hook.** The workflow actor calls the model's migration hook; a model without one refuses:

```csharp
// Layer 3. Optional on IExecutionModel.
public interface IMigratingModel
{
    // Map the loaded state onto the target definition through the turn: Rebind, set State, Subscribe/Unsubscribe,
    // Recall, Send, Record. Returns Refused with value-free findings, leaving the turn's effects empty.
    MigrationResult Migrate(ITurn turn, Migrate request);
}
```

The native model's implementation computes the live set (below), checks the rules, and asks each container on the path to a live frame to map its own private state:

```csharp
// Layer 3, native model. Implemented by each container (composite) activity type.
public interface IContainerMigration
{
    ContainerMigration Migrate(ContainerMigrationContext context);  // Mapped(state) or Refused(findings)
}

public sealed record ContainerMigrationContext(
    Node Source, Node Target, JsonNode? State, int StateVersion,
    IReadOnlySet<string> LiveChildNodes);                           // children in the live set
```

A container that does not implement the hook can migrate only when the target node is unchanged, including its children list. Otherwise it refuses.

### The simple-case boundary

**Unchanged** means two nodes have the same id, activity type, activity type version and authored definition: inputs, output bindings, trigger settings and container options. Children lists and designer layout are compared separately, by the container rules below. Comparison is on the canonical serialized node.

**The live set** of a native instance is every node that one of these names:

- a frame (`Frames[].Node`), scheduled work (`Agenda[].Node`), or a wait's frame;
- a stored result (`Results`, keyed by node id, which later expressions read);
- the trigger node (`TriggerNode`);
- a node or connection referenced by a Flowchart's or StateMachine's private state (table below);
- plus every ancestor of those nodes.

The simple case holds when all of these are true:

1. Every node in the live set exists in the target with the same id, activity type and activity type version, the same parent chain, and an activity state version that the node's code can read.
2. Every live node that is not a container is unchanged. This includes every waiting node, so an event name, route or timer duration that changed on a waiting node refuses the migration.
3. Every live container is unchanged apart from its children list, and its children list satisfies its rule in the table.
4. Every source variable exists in the target with the same type and policy (as today's validator requires). A variable added in the target is set to its declared default in scopes that already exist. This is proposed; today's core has no such rule.

Nodes outside the live set may be added, removed or changed freely.

| Model or container | What is live in its state | Children rule in the simple case | Later |
|---|---|---|---|
| **Native frames** | `Frames`, `Agenda`, `Waits`, `Results`, `TriggerNode`, `Variables` and lexical scopes | Rules 1 to 4 above | An explicit node map: move a frame to another node of the same type, or cancel a frame (its waits are unregistered in the same commit) |
| **Sequence** | No cursor: it finds the next child from the completed child's node id in the bound definition ([Activities.cs](../../server/essentials/src/Workflows/Runtime/Native/Model/Activities.cs)) | The children up to and including the running child are the same ids in the same order. Children after it may change. Moving a completed child after the running child (it would run again) or inserting one before it (it would never run) is refused | Reordering with an explicit map |
| **If** | The taken branch's frame | The taken branch's child keeps its id | Moving into another branch |
| **ForEach, While** | Items and index (ForEach); the single body child | The single child keeps its id | Changing the body with a map |
| **Parallel** | Its branch frames | The same set of branch ids: no branch added or removed while it runs | Adding or cancelling branches with a map |
| **Flowchart** (graph engine) | `ExecutionPaths` (current node, incoming connection, iteration key), `Arrivals` at joins (source, target, connection, port), `ActiveChildren`, `Scopes`, `LoopIterationCounters` | Every connection the state references exists with the same source, target and port, and every join with pending arrivals keeps the same set of inbound connections, because the dead-path join decides when to fire from that set. Other connections may change. Loop counters are kept | Moving a path or a pending arrival to another node; changing a join's inbound set with a rule for its pending arrivals |
| **StateMachine** (statechart engine) | `Active` configuration (state ids), `Pending` reaction and its cursor, visits | Every active state and its ancestor states exist with the same parent chain, and no reaction is in progress (`Pending` null): the machine is between events. Transitions may change | A state map (A in the source is B in the target), with entry and exit actions of a mapped change chosen explicitly |
| **BPMN**, after release 1 | Tokens in the Bpmn.Semantics token state, per element | None: BPMN is not in release 1 | Camunda-style migration plans: mapping instructions per element id, validated against the token state |
| Other models (ETL, agents, sample models) | Model-owned | No hook: refused | Each model's own hook |

**Compared with today's core.** The current core's migrate alteration ([WorkflowMigrationCompatibilityValidator.cs](../../server/essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/WorkflowMigrationCompatibilityValidator.cs), [ADR 0049](../adr/0049-runtime-alterations-use-snapshotted-atomic-jobs.md)) admits only a suspended, fully quiescent execution. It requires every source node to exist in the target with the same type, version and contract, every resume target to keep its node, every variable declaration to match, and runtime requirements, storage drivers and dependencies to match. The kernel's simple cases differ in both directions:

- **More permissive.** A node outside the live set may be removed or changed, and a running instance may be migrated (decision 4).
- **Stricter.** The children rules above, and unchanged waiting nodes.
- **Not carried over.** The kernel prototype's definitions have no runtime-requirement, storage-driver or dependency artifacts. If the product keeps them, their checks carry over unchanged.

### In-flight messages, timers, subscriptions and jobs

Nothing in flight is moved or rewritten by a simple-case migration, because nothing it refers to changes:

- **Inbox messages** address the instance. They stay where they are and are handled under the target after the migration's turn.
- **Timers** are messages with a future due; a native timer's payload names its wait (`TimerFired(wait)`), and the wait names its frame. The frame and its node are unchanged (rule 2), so the timer fires into the same frame at its original due. A changed duration on a waiting timer is refused; rescheduling (recall the old message and send a new one in the migration's commit) is later.
- **Subscriptions** are keyed by the event name or HTTP route the waiting activity registered. Rule 2 keeps them valid, so invariant 8 (the subscriptions are exactly the waits in committed state) holds without a write. Re-registering a changed trigger is later.
- **Background jobs in flight** keep running the activity they started: the job's arguments name the definition and node it started under and carry the inputs captured at its start (`BackgroundArgs` in NativeModel.cs). Its result returns to the frame by frame id.
- **Effect keys** are `{instance origin}/{node}/{iteration path}/{attempt}` ([ownership and fencing](ownership-and-fencing.md#side-effects)). The simple cases keep node ids and iteration paths, so a replayed activation after the migration derives the key its first sending carried. A later explicit node map must carry the source node id into the key of every activation in flight, or the receiver sees a new key and the effect repeats.
- **Children** keep their own definitions; a parent's child wait names the child's exact ref (FC-15). A plan migrates children only when it selects them.
- **Ephemeral instances** in memory have no committed row and are not migrated.

### How a migration commits

The migration's turn ends in one fenced commit, batched with its partition's other turns. That commit holds:

- the new definition (`Rebind`) and the mapped state at the current state versions;
- any subscription and timer changes (none in the simple cases);
- the consumed `Migrate` message;
- an outcome message to the plan (below);
- the journal entry its history level writes.

All of it lands or none of it does (invariant 2). A commit refused by the fence resets the owner as usual. The next owner finds `Migrate` in the inbox and maps the same committed state again, so the result is the same. A refusal is also a commit: the message is consumed and the outcome carries the findings, with the instance unchanged.

**The journal record.** At Full history the migration's journal entry records source, target and plan. The planned viewer renders earlier entries against the source definition and later ones against the target. State and Minimal carry the envelope-only `Migrated` lifecycle entry specified by [data lifecycle decision 1](data-lifecycle.md). The in-memory structured Native witness verifies one successful migration lifecycle entry at each history level.

### Plans and bulk migration

- **A plan** names the source and target versions (and, later, explicit maps). It is a durable actor of a layer-3 actor type. Creating it validates the two definitions against each other once, without reading any instance, and rejects plans that cannot succeed.
- **Selecting instances.** The plan selects instances from the instance-list read model (never by querying owner state), in batches. The plan actor's turn only records each batch and its progress in its state: the instances, the target and the batch number. It selects the next batch only after the previous one is dispatched, so it holds at most one undispatched batch, and it drops a batch's instance list once that batch is dispatched. A turn may send no command kind except `Terminate` to its own registered children (FC-9, FC-C3), so the plan's turn sends no `Migrate` (a coordinator design choice under FC-C3).
- **Dispatch.** The plan runner, a layer 3 operator service outside any turn, reads the committed batch and sends each `Migrate` with `SendAsync(scope, Ref(ref), id, Kinds.Migrate, payload)` under the operator's `BoundScope`, to the exact ref the batch recorded. Each carries a deterministic id built from the plan id, the batch and the instance, so a retried batch is deduplicated (FC-16). A send to a life that has since been deleted is dropped (FC-15), and an instance no longer at the source answers "not at source". The runner is layer 3's operator service, built at K11. It runs once per plan, from the plan actor's committed batches. When a batch's sends are done, it sends the plan a "batch dispatched" message, deduplicated by batch number (FC-16). After a restart it resends, with the same deterministic ids, the batch whose dispatch has not completed, and FC-16 deduplicates them. Results return to the plan actor as ordinary messages ("Outcomes" below).
- **Duplicates.** That id deduplicates within the deduplication window (FC-16), so within the window each instance sends at most one outcome. After the window a repeat `Migrate` is a new message. Each instance's state records the id of the last plan whose `Migrate` it answered, so a repeat from the same plan gets no second outcome: constant state per instance, and no growth in the plan (a coordinator design choice). That stops a repeat in the ordinary case. If another plan migrated the instance in between, a repeat of the first plan is answered again, and the read model's rule under "Outcomes" absorbs it.
- **Outcomes.** Each instance's turn sends the plan an outcome message in its own commit, so outcomes are durable at every history level. The plan is a fan-in target for N messages, so its own state keeps only counts per outcome kind (migrated, refused, not at source), the selection cursor and the one batch not yet dispatched. Its size stays bounded by the number of kinds and one batch, whatever N is. These counts are progress only: after a repeat beyond the deduplication window they may overcount. Keeping every outcome in the plan's state would grow it with N and rewrite it whole at each commit: N² bytes over a run, plus TOAST above about 2 kB. The per-instance outcomes go to a plan-outcomes read model, which the projector feeds from the plan's journal entries for its outcome messages. The plan always runs at Full history, so those entries exist. All three outcome kinds are sent to the plan: migrated, refused and not at source. In the read model, "not at source" is counted but never written over a decisive outcome (migrated or refused) for the same instance. The read model records one outcome per instance, by exact ref, with a decisive one never overwritten, and completion and the final totals are judged from it (a coordinator design choice). The projector also records each batch's instances from the plan's batch journal entries, so the read model keeps the selected set after the plan drops each batch's instance list, and both completion and the "no answer" list are computed from it. The runner sends each instance's `Migrate` under one deterministic id, and a retry within the window meets the duplicate id. It can send none: the read model lags, so a selected instance can end and be deleted before its `Migrate` arrives. Its exact ref then names a deleted actor, and the message is dropped with no reply (FC-15). Release 1 takes the simple path and does not reconcile:

- **Pending.** The plan reports pending = selected - (migrated + refused + not at source), as progress only ("Outcomes" above). An instance withheld on an older node (F14, or a newer activity version) stays pending until the roll-forward; a deleted one stays pending for good.
- **Closing.** A plan's run ends when the plan-outcomes read model shows every selected instance with an outcome of any kind, "not at source" included: the plan runner, outside any turn, reads the read model and then sends the plan a "complete" message, deduplicated by plan id, since a turn reads no read models. It also ends at the plan's deadline, or when an operator closes it ("close plan"); the plan-outcomes read model lists the instances that never answered as "no answer". Release 1 documents that a plan may stay open until it is closed. An outcome that arrives after the close is still counted and recorded.
- **Identity.** A selection names an instance by its exact ref, read from the instance-list read model; an exact ref names the actor's life (FC-15). A `Migrate` to a deleted instance is dropped (FC-15), and a later life under a reused caller key has another exact ref, so the old selection never reaches it. The instance stays pending.
- **Park.** The runner waits while the plan has an active Park or recovery drain. Recovery can re-arm the plan's own deadline at its original due time. Park discard, expiry and termination close the projection and keep outstanding selections as "no answer". The foundation refuses later ordinary input to that life after one of these dispositions completes.
- **Outcomes name the life that answered.** Each outcome message comes from the instance's own turn, whose sender is its exact ref (FC-3), so the plan-outcomes read model attributes every outcome to one life, and a later life under a reused caller key never answers for an old selection (FC-15).
- **Later.** Reconciling unanswered instances automatically (a job of the plan that reads both read models and returns the missing exact refs to a later plan turn, where a decisive outcome arriving afterwards replaces "target missing") is deferred.

The instance's own journal records a "not at source" answer only at Full history. After the plan consumes the outcome message, only the plan's journal entry and the read model remain. For very large plans, the aggregating pattern of question 4 (large fan-out and fan-in, PR #4708) applies: combiners between the instances and the plan.
- **The deciding check** is the in-turn check against committed state.

## What release 1 ships

Release 1 is "migrating instances between definition versions for the simple cases" ([release 1 scope](../reports/clean-slate-kernel/design-sketch-draft-5.md#release-1-scope-owner-decision-amended-15-november)):

- model-level state versions and F14 withholding, as built;
- activity-level state versions in the frame, with withholding for newer versions and unknown activity types, for built-in and custom activities, including Flowchart and StateMachine state;
- the `Migrate` command, with the command-kind seam in layer 2, the native model's hook and the container hooks for structured containers, Flowchart and StateMachine, limited to the simple-case boundary above;
- migration of one instance and bulk migration by plan through the runtime API, with durable per-instance outcomes;
- refusal with value-free findings for everything else, leaving the instance as it was.

Later: explicit node, path and state maps; frame cancellation by map; re-registering changed triggers; rescheduling timers; per-instance dry runs; BPMN token migration; migration policies applied at publish.

## Costs

| Item | Cost | Status |
|---|---|---|
| Model or activity state upgrade | CPU in memory at load; no extra write (the next commit writes the new version) | Model level built; activity level proposed |
| Detecting a newer activity version | One load per owner activation, then nothing while the row is unchanged | Proposed |
| One migration, instance in memory | One outside message add, one mark-before-run commit (it arrives by poll) and the migration's commit; both commits are batched with the partition's other turns. Plus one outcome message to the plan, in the same commit | Proposed; derived from the measured commit path, not measured |
| One migration, instance not in memory | Plus one load by key, batched with other loads | Proposed, not measured |
| A bulk plan | One message add per instance, then per partition the flushes it takes: on the server engines 100 instance commits cost the round trips of one (2 on PostgreSQL with the fence in the batch). The plan's own turns, which take the outcomes, are batched in the same way, and its state stays a few counters whatever N is | Commit cost measured ([NATIVE.md](../../prototypes/kernel-model/NATIVE.md), [ENGINES.md](../../prototypes/kernel-model/ENGINES.md)); a bulk migration run is not |
| Bytes | One state rewrite and one journal row per instance, as for any commit at its history level, plus one outcome message | Per-commit bytes measured in [WRITES.md](../../prototypes/kernel-model/WRITES.md) |
| Source definitions | Kept loadable while anything refers to them (decision 5) | Proposed |

## Unverified

- **Proposed:** activity-level versioning and its withholding signal, the product plan actor, and the full model and container mapping hooks. The structured Native command runs in memory and still needs the other applicable provider and model cases.
- **Event wait witnessed:** the Native command resumes the original event wait after rebind. The timer and background job cases still need their own execution evidence.
- **By inspection only:** the comparison with today's migrate alteration validator.
- **Checks to add when it is built:**
  - a simulated migration under takeovers: it lands once, a refusal changes nothing, effect keys of replayed activations are unchanged, invariant 8 holds, and an older owner never writes a newer state back;
  - mutations that forget a frame's parent chain, accept a reordered Sequence prefix, map a ForEach to a target with a different child, ignore a join's inbound set, drop a `Results` node, or consume a `Migrate` on an older owner.

## Acceptance cases

From the [Astra High gate 2 review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6949612272). Its note on this page asked for creation-qualified dispatch and visible fallback outcomes; under the foundation contract an exact ref names the actor's life (FC-15), which answers both. Milestones are those of the [program plan](../program-goals/clean-slate-kernel.md).

| Case | Order of events | Expected | Milestone |
|---|---|---|---|
| MP1 Exact-ref dispatch | A plan over selected running instances | Every `Migrate` is sent to the exact ref its batch recorded; every outcome names the life that answered | K3, K11 |
| MP2 Reused key | Select A under key K; delete A; start B under K at the source version; deliver A's `Migrate` | The message to A's exact ref is dropped (FC-15); B is not migrated and sends no outcome; A stays pending | K3, K4 |
| MP3 Retried batch | The runner sends a batch, loses the answers and sends the batch again within the deduplication window | Each repeat answers `Duplicate` (FC-16); each instance migrates once and sends one outcome | K11 |

The K3 in-memory witness (`Kernel.Sim --foundation-migration-dispatch-check`) commits a batch of two exact refs in a test plan, deletes one selected life, and reuses its caller key before the runner resumes. The runner rejects the retired ref, the Worker settles an earlier delayed command pinned to that life, and only the live peer moves and sends an outcome naming itself. Resolving the stale selection by caller key instead makes the successor migrate and fails both exact-dispatch assertions. The test actor stands in for the production migration hook and plan; K11 qualifies the plan service and its outcome read model.

## Decisions (owner, 16–17 November 2026)

The owner took these on 17 November; the [decision register](decisions.md#the-seven-design-now-answers-1617-november) lists them with
the rest of the kernel's. Rejected options are kept for the record.

1. **Rollback of a state version bump stays as built (A):** instances written at the new version are withheld on older
   nodes until the roll-forward, with a metric for withheld instances and release notes that name every bump. Rejected:
   (B) reader first, two releases per bump.
2. **An instance a bulk plan refuses stays running on the source version (A),** and the plan reports it. Rejected: (B)
   suspend it for an operator.
3. **The simple-case boundary above is release 1's scope;** release 1 refuses everything outside it.
4. **ADR 0049's quiescent-only rule is widened to running instances (B).** Release 1 migrates running and suspended
   instances at a turn boundary; a plan can still require suspension. ADR 0049 goes on the list of ADRs the new core
   supersedes. Rejected: (A) suspended instances only.
5. **A source definition version is kept while any running or suspended instance, job ticket or retained history refers to it
   (A).** Rejected: (B) running instances and tickets only, with a "definition removed" fallback for older history.
   Applied to the proposed `JobSpec` (observability, fourth review R4-M3; proposed, not an owner decision): a job spec
   in an instance's state names its definition and node (Native's `BackgroundArgs`, `NativeModel.cs` 54), so it is a
   reference that keeps its source version while it is in the state, as a ticket does, a parked instance's included,
   whose retained history keeps the same version too.

**Accepted with the recommendations:** until creation headers are persisted, a plan addresses instances by id only,
and release 1 documents the reused-caller-key risk ("The id-only fallback" above). Dispatch is creation-qualified wherever the provider and row allow it, and fallback outcomes are shown to the operator (added for the Astra High gate 2 review).
Moot under the foundation contract (FC-15): an exact ref names the life, so no id-only fallback arises (the id-only fallback bullet was removed with exact refs).
