# Migrating running instances

> **Proposed.** The kernel design awaits owner approval. This page answers design-now question 1 of the [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md#to-answer-in-the-design-proposal-before-the-fable-review-owner-decision-12-november); it does not authorize product implementation.

A running instance keeps its state across two kinds of change:

- **A code upgrade.** A release changes how a model or an activity serializes its state. The instance stays on its definition; its stored state must be read by the new code.
- **A definition migration.** An operator moves the instance to another version of its definition. Its state must be mapped onto the new version's nodes.

Both happen inside a turn of the instance and commit through the ordinary fenced commit. Neither adds a new kind of record, a table or a second store to the commit, and neither reads partitions, positions or SQL: the whole design sits in layer 3 on the turn contract. No store or provider changes, so EF Core and MongoDB are equally unaffected.

Terms used here: a **frame** is the native model's record of one live activity (its node, parent frame, private state, scope and attempt); a **container** is a composite activity whose private state names its children (structured containers, Flowchart, StateMachine); a **wait** is a frame's registered trigger (event, HTTP route, timer, child or job).

## What is built

| Piece | Status | Evidence |
|---|---|---|
| `IExecutionModel.StateVersion` and `UpgradeState(string state, int fromVersion)`; `StateVersion` on the instance row. An older row is upgraded on load and written at the model's version by the next commit | Built, simulated (rolling deploys with rollbacks in every third seed) | [Contracts.cs](../../prototypes/kernel-model/src/Kernel.Workflows/Contracts.cs), `ReadState` in [WorkflowInstance.cs](../../prototypes/kernel-model/src/Kernel.Workflows/WorkflowInstance.cs), [FINDINGS 15](../../prototypes/kernel-model/FINDINGS.md#15-state-versions-run) |
| A node behind the deploy never turns a row written by a newer model version: the instance is withheld like a suspended one until a newer owner takes the partition (invariant 18) | Built, mutation-checked (`StepNewerState`, `SkipUpgrade`) | `Withholds` in WorkflowInstance.cs, [FINDINGS F14](../../prototypes/kernel-model/FINDINGS.md#f14-a-node-behind-the-deploy-must-not-step-a-row-written-by-a-newer-model-invariant-18-run) |
| The native model at state version 13, with reader witnesses that upgrade each older shape and refuse mixed or unknown ones | Built, witnessed | `UpgradeState` in [NativeModel.cs](../../prototypes/kernel-model/models/Native/NativeModel.cs); `ReaderWitness.cs`, `RecoveryReader.cs`, `NativeInternalReaders.cs` under `models/` |
| A container's own state version: the StateMachine's `MachineState.Version` (1 to 7), restored on read and committed by the next checkpoint | Built, witnessed | `Restore` in [StateMachine.cs](../../prototypes/kernel-model/models/Statecharts.Native/StateMachine.cs) |
| `ITurn.Rebind(definition)`: the commit writes the new definition into the row; from the next turn on the instance belongs to it (invariant 19) | Built in the kernel and the simulation's counter model, mutation-checked (`RebindForgotten`) | `Rebind` in [Turn.cs](../../prototypes/kernel-model/src/Kernel.Workflows/Turn.cs), [FINDINGS 16](../../prototypes/kernel-model/FINDINGS.md#16-rebind-run) |
| Rebind in the native model, any frame or path mapping, a per-activity state version in the frame, a migration command | **Not built** | None. Everything below the "Proposed" headings is design on paper |

The Flowchart's state carries no version of its own in the prototype: the extracted graph engine dropped main's private-state envelope ([Graph README](../../prototypes/kernel-model/models/Graph/README.md), "What was adapted"), so the native model's whole-state version covers it.

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
- **Containers are activities.** A Flowchart's `FlowchartExecutionState` and a StateMachine's `MachineState` are the private state of the container's frame. Their engines version them through the same interface; the StateMachine's existing `MachineState.Version` moves into `V`.
- **Refusal.** An upgrade that throws faults the instance with an "activity state upgrade refused" incident naming the frame and node, and parks it as any failing turn does. An operator can retry after a fixed release, migrate it, or terminate it.
- **Newer than this node.** A frame whose `V` is above what this node's activity type knows is the F14 case at activity granularity. The native model reports it and the workflow actor withholds the instance exactly as it withholds a newer model version. This stays in layer 3: `Withholds` is the workflow actor's.
- **Removed activity type.** A frame whose activity type no longer exists faults the instance in the same way.

## Proposed: definition migration

### The command

A migration is a control message to the instance, handled by the workflow actor in its own turn, like suspend and delete:

```csharp
public sealed record Migrate(string From, string To, string Plan);  // definition versions; plan id
```

- **Order.** The workflow actor runs the instance's earlier messages first (under the source definition) and handles `Migrate` alone in its turn. Messages behind it run under the target. One sender's messages reach one target in order, so "suspend, migrate, resume" from an operator applies in that order.
- **Guard.** `From` must equal the row's definition. A migration aimed at another version, or repeated after it was applied, is consumed and answered "not at source" with no state change. An outside retry within `keep_until` meets the duplicate message id as for any command.
- **Who may migrate.** A running or suspended instance. A suspended instance stays suspended. An ended instance is refused.
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

The native model's implementation walks its frame tree and asks each container to map its own private state:

```csharp
// Layer 3, native model. Implemented by each container (composite) activity type.
public interface IContainerMigration
{
    ContainerMigration Migrate(ContainerMigrationContext context);  // Mapped(state) or Refused(findings)
}

public sealed record ContainerMigrationContext(
    Node Source, Node Target, JsonNode? State, int StateVersion,
    IReadOnlySet<string> LiveChildNodes);                           // children with frames, scheduled work or waits
```

A container that does not implement the hook can still migrate when the target node and its whole subtree are unchanged; otherwise it refuses.

### What migration means per model and container

| Model or container | What is live in its state | Release 1 simple case | Later |
|---|---|---|---|
| **Native frames** | `Frames` (node, parent frame, private state, scope, attempt), the `Agenda` of scheduled work (node ids), `Waits` (frame, kind, key), `Variables` and lexical scopes | Every node with a frame, scheduled work or a wait exists in the target with the same activity type, a readable activity state version and the same parent chain. Added nodes anywhere else are fine; removed or changed nodes must have nothing live | An explicit node map: move a frame to another node of the same type, or cancel a frame (its waits are unregistered in the same commit) |
| **Structured containers** (Sequence, If, ForEach, While, Parallel) | Child frames by node id; ForEach items and index | The running child keeps its node id. A Sequence keeps no cursor: it finds the next child from the completed child's node id in the bound definition ([Activities.cs](../../prototypes/kernel-model/models/Native/Activities.cs)), so after a migration it continues in the target's order. A ForEach keeps its items and index, and its single child must keep its node id | Inserting or reordering around a running child with an explicit map |
| **Flowchart** (graph engine) | `ExecutionPaths` (current node, incoming connection, iteration key), `Arrivals` at joins (source, target, connection, port), `ActiveChildren`, `Scopes`, `LoopIterationCounters` | Every node the state references exists, every connection it references exists with the same source, target and port, and every join with pending arrivals keeps the same set of inbound connections, because the dead-path join decides when to fire from that set. Loop counters are kept | Moving a path or a pending arrival to another node; changing a join's inbound set with a rule for its pending arrivals |
| **StateMachine** (statechart engine) | `Active` configuration (state ids), `Pending` reaction and its cursor, visits | Every active state exists with the same parent chain of regions and states, and no reaction is in progress (`Pending` null): the machine is between events | A state map (A in the source is B in the target), with entry and exit actions of a mapped change chosen explicitly |
| **BPMN**, after release 1 | Tokens in the Bpmn.Semantics token state, per element | None: BPMN is not in release 1 | Camunda-style migration plans: mapping instructions per element id, validated against the token state |
| Other models (ETL, agents, sample models) | Model-owned | No hook: refused | Each model's own hook |

Variables follow today's core: a variable added in the target gets its default in scopes that already exist; a variable removed in the target, or changed in type or policy, refuses the migration. This mirrors the strict validator of the current core's migrate alteration ([WorkflowMigrationCompatibilityValidator.cs](../../essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/WorkflowMigrationCompatibilityValidator.cs), [ADR 0049](../adr/0049-runtime-alterations-use-snapshotted-atomic-jobs.md)): every node of the source must exist in the target with the same activity type, version and contract, and every resume target must still point at its node. The kernel's simple cases accept one thing more, a removed node with nothing live, and check three things today's validator does not: unchanged trigger inputs on waiting nodes, unchanged inbound sets of joins with pending arrivals, and a StateMachine between events. Most migrations that pass today's validator are simple cases here; one that changes a waiting trigger or a pending join is refused.

### In-flight messages, timers, subscriptions and jobs

Nothing in flight is moved or rewritten by a simple-case migration, because nothing it refers to changes:

- **Inbox messages** address the instance. They stay where they are and are handled under the target after the migration's turn.
- **Timers** are messages with a future due; a native timer's payload names its wait (`TimerFired(wait)`), and the wait names its frame. The frame keeps its id, so the timer fires into the same frame at its original due. A target that changes a waiting timer's duration does not reschedule it; rescheduling is a later option (recall the old message and send a new one, both in the migration's commit).
- **Subscriptions** are keyed by the event name or HTTP route the waiting activity registered. A simple case requires a waiting node's trigger inputs to be unchanged, so its subscription rows stay valid and invariant 8 (the subscriptions are exactly the waits in committed state) holds without a write. Re-registering a changed trigger (unsubscribe the old key, subscribe the new one in the same commit) is later.
- **Background jobs in flight** keep running the activity they started: the job's arguments name the definition and node it started under and carry the inputs captured at its start (`BackgroundArgs` in NativeModel.cs). Its result returns to the frame by frame id. So the source definition version must stay loadable while any instance or job ticket refers to it.
- **Effect keys** are `{instance origin}/{node}/{iteration path}/{attempt}` ([ownership and fencing](ownership-and-fencing.md#side-effects)). The simple cases keep node ids and iteration paths, so a replayed activation after the migration derives the key its first sending carried. A later explicit node map must carry the source node id into the key of every activation in flight, or the receiver sees a new key and the effect repeats.
- **Children** keep their own definitions; a parent's child wait names the child's id and creation. A plan migrates children only when it selects them.
- **Ephemeral instances** in memory have no committed row and are not migrated.

### How a migration commits

The migration's turn ends in one fenced commit, batched with its partition's other turns. That commit holds the new definition (`Rebind`), the mapped state at the current state versions, any subscription and timer changes (none in the simple cases), the consumed `Migrate` message, an optional reply, and one journal entry recording source, target and plan. All of it lands or none of it does (invariant 2). A commit refused by the fence resets the owner as usual; the next owner finds `Migrate` in the inbox and maps the same committed state again, so the result is the same. A refusal is also a commit: the message is consumed and a journal entry and reply carry the findings, with the instance unchanged.

The viewer renders journal entries written before the migration against the source definition and later ones against the target; the migration's journal entry marks the boundary.

### Plans and bulk migration

A **plan** names the source and target versions (and, later, explicit maps). Creating a plan validates the two definitions against each other once, without reading any instance. It then selects instances from the instance-list read model (never by querying owner state) and adds one `Migrate` message per instance, with a message id derived from plan and instance so that a resumed bulk run adds nothing twice. The in-turn check against committed state is the only one that decides; the definition-level check only rejects plans that cannot succeed. Each instance's outcome (migrated, refused with findings, not at source) is recorded in its journal and gathered into the plan's read model.

## What release 1 ships

Release 1 is "migrating instances between definition versions for the simple cases" ([release 1 scope](../reports/clean-slate-kernel/design-sketch-draft-5.md#release-1-scope-owner-decision-amended-15-november)):

- model-level state versions and F14 withholding, as built;
- activity-level state versions in the frame, for built-in and custom activities, including Flowchart and StateMachine state;
- the `Migrate` command, the native model's hook and the container hooks for structured containers, Flowchart and StateMachine, limited to the simple cases in the table: same definition, another version, nothing live on a removed or changed node;
- migration of one instance and bulk migration by plan through the runtime API, with per-instance outcomes;
- refusal with value-free findings for everything else, leaving the instance as it was.

Later: explicit node, path and state maps; frame cancellation by map; re-registering changed triggers; rescheduling timers; per-instance dry runs; BPMN token migration; migration policies applied at publish.

## Costs

| Item | Cost | Status |
|---|---|---|
| Model or activity state upgrade | CPU in memory at load; no extra write (the next commit writes the new version) | Model level built; activity level proposed |
| One migration, instance in memory | One outside message add, one mark-before-run commit (it arrives by poll) and the migration's commit; both commits are batched with the partition's other turns | Proposed; derived from the measured commit path, not measured |
| One migration, instance not in memory | Plus one load by key, batched with other loads | Proposed, not measured |
| A bulk plan | One message add per instance, then per partition the flushes it takes: on the server engines 100 instance commits cost the round trips of one (2 on PostgreSQL with the fence in the batch) | Commit cost measured ([NATIVE.md](../../prototypes/kernel-model/NATIVE.md), [ENGINES.md](../../prototypes/kernel-model/ENGINES.md)); a bulk migration run is not |
| Bytes | One state rewrite and one journal row per instance, as for any commit at its history level | Per-commit bytes measured in [WRITES.md](../../prototypes/kernel-model/WRITES.md) |
| Source definitions | Kept loadable while an instance or job ticket refers to them | Proposed |

## Unverified

- No part of the activity-level versioning, the `Migrate` command, the model or container hooks, or native `Rebind` is built or simulated. The simulation's invariants 18 and 19 cover model-level versions and `Rebind` of a toy model only.
- The claim that the simple cases need no subscription, timer or job change rests on reading the native model's wait and job code; no run checks it.
- The comparison with today's migrate alteration validator is by inspection.
- Proposed checks for when it is built: a simulated migration under takeovers (the migration lands once, a refusal changes nothing, effect keys of replayed activations are unchanged, invariant 8 holds) and mutations that forget a frame's parent chain, map a ForEach to a target with a different child, or ignore a join's inbound set.

## Decisions for the owner

1. **Rolling back a release that bumps a state version.**
   - **A (as built):** a release writes the new version as soon as it runs. If it is rolled back, instances already written at the new version are withheld on the older nodes until the release is rolled forward again.
   - **B (reader first):** a release first ships the reader of the new shape while still writing the old one; the next release writes the new one. A rollback by one release is always safe, but each bump takes two releases and every upgrade needs a writer of the old shape.
   - **Recommendation: A for release 1,** with a metric for withheld instances and release notes that name every state version bump. Revisit B if operators need rollback across bumps.
2. **An instance a bulk plan refuses.**
   - **A:** it stays running on the source version and the plan reports it.
   - **B:** it is suspended for an operator.
   - **Recommendation: A.** A refusal changes nothing, which keeps a plan safe to run on a live system.
3. **The boundary of "the simple cases"** is the table above. **Recommendation:** confirm it as release 1's scope; release 1 refuses everything outside it.
