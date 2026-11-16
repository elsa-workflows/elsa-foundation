# Large fan-out and fan-in

> **Proposed.** The kernel design awaits owner approval. This page answers design-now question 4 of the
> [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md#open-questions); it does not authorize product
> implementation.

A ForEach over 100,000 items, or an ETL job split into 100,000 partitions of work, starts many pieces of work and
collects their results. On the kernel every piece that runs as a child instance reports to its parent with a message.
If all of them report to one parent, that parent takes 100,000 messages and up to as many turns, one at a time, in one
virtual partition. This page designs the aggregating pattern that removes the hot parent: a **tree of combiners** with
**credits**. It ships with ETL after release 1. Release 1 caps a parallel ForEach at a documented size instead.

Terms: **fanout F** is the most children a combiner starts in all; **credit C** is the most it has outstanding at once;
a **leaf** does one piece of work; a **combiner** starts children for a range of items and merges their results.

## What exists today

Built, in `prototypes/kernel-model` at main `433c79341`:

| Piece | Where | Relevance |
|---|---|---|
| `Start(spec, Placement.WithParent \| Placement.Spread)` | `src/Kernel.Workflows/Contracts.cs` | A child goes to its parent's partition by default; `Spread` places it by hash. |
| A child's terminal notice to its parent (`ChildFinished`, with status and output) | `WorkflowInstance`, the notice id from `CreationProtocol.TerminalNoticeId` | One message per child. A generation-tracked parent takes each notice in a turn of its own (`WorkflowInstance.SingleMessage`, `SelectsAlone`). |
| The parent's registry of children still running (`InstanceRow.Children`) | `ChildRegistry`, `WorkflowInstance` | One entry per outstanding child in the parent's row, removed when its notice is consumed. |
| Forced termination of a tree | `WorkflowInstance` (terminate) | A terminate goes to every child the instance started that has not finished; it recurses. |
| Native `Parallel` with a threshold join and fault counting | `models/Native/Activities.cs` | The join and fault rules a combiner reuses. |
| Native `ForEach`, sequential only; it copies its items into its state | `models/Native/Activities.cs`, NATIVE.md | No parallel ForEach exists in the prototype. Elsa 4 on main has none either (Elsa 3's `ParallelForEach` maps to "no counterpart", `extensions/Elsa3/src/Mapping/ActivityMap/Elsa3BuiltInActivityMap.Entries.cs`). |
| Effect keys `{instance origin}/{node}/{iteration path}/{attempt}`; ForEach's iteration is the body's `CurrentIndex` | `src/Kernel.Workflows/EffectKey.cs`, `ForEach.IterationOf`, [ownership and fencing](ownership-and-fencing.md#side-effects) | The key every leaf effect carries. |
| **The ETL fan-out spike:** a combiner tree with fanout 64 and credit 8, children spread over 4 partitions, against a direct baseline with the same credit | `models/Etl/EtlModel.cs`, `models/Etl.Run`, [ETL.md](../../prototypes/kernel-model/models/Etl/ETL.md) | The measured evidence below. In-memory store, real `Worker` and `WorkflowInstance`. |

## What one parent costs without the pattern

Take N children that each report to one parent. Measured rows are from the ETL spike; the other rows are arithmetic
from the prototype's rules and the measured commit shape, and are **unverified**.

| Cost at the parent | Direct, every child started at once | Direct, credit C | Tree, fanout F, credit C |
|---|---|---|---|
| Messages it starts in one commit | N start messages in one transaction | At most C per turn | At most C per turn, per combiner |
| Inbox depth from its children | Up to N | At most C | At most C |
| Child notices it consumes | N | N | At most F |
| Turns | Up to N: one per notice for a generation-tracked parent | Between N / C and N. **Measured, N = 4,096, C = 8: 1,413** | At most F. **Measured, N = 4,096, F = 64: 64 notices, 55 turns** |
| Children in its row's registry | N | At most C | At most C |
| Row bytes rewritten over the run | O(N²): N commits, each rewriting a registry of up to N entries | O(N × C) | O(F × C) |
| Instances in all | N + 1 | N + 1 | About N × F / (F − 1). **Measured, N = 4,096: 4,161** (4,097 direct) |
| Terminates sent by a cancel | Up to N | At most C | At most C per live combiner |

The measured rows are from `dotnet run -c Release --project prototypes/kernel-model/models/Etl.Run -- --size 4096` at
main `433c79341` (exit 0; every check `ok`): the tree took 0.82 s, the direct run 1.98 s, on a shared development
machine (timings are not a budget). ETL.md's earlier run recorded 38 root turns for the tree; the count of turns
depends on how notices batch into turns, and the injected recovery in the tree run moves that.

**What the arithmetic means at N = 100,000:**

- **Registry bytes.** An entry in the `Children` registry is a child address of about 25 bytes, or about 45 bytes with
  its creation number (`ChildRegistry.Write`). At 100,000 entries the registry alone is about 4.5 MB. Rewriting a
  registry that shrinks from 100,000 entries over 100,000 commits writes about 225 GB of row data before any TOAST
  amplification. WRITES.md measured an incompressible 4 kB state at about 6.9 kB of WAL per commit.
- **One writer.** The parent takes one turn at a time, so its turns cannot share a flush: each is at least one flush of
  its virtual partition. A flush is at least 2 round trips on PostgreSQL (the measured fence-in-batch shape), so
  100,000 notices cost at least 200,000 round trips in sequence on one virtual partition.
- **One transaction.** Starting all children at once writes 100,000 start messages in one transaction. That is a long
  transaction holding locks, and on SQL Server's 2,000-parameter limit many statements (COMMIT-SQL.md).

The tree keeps the same total number of messages and commits, about one per instance. It removes the hot spot: no
instance takes more than F notices or holds more than C children, and the combiners' commits batch with other
instances' commits in their own virtual partitions, in parallel over the nodes.

## The combiner tree

**Shape.** A combiner owns the items `[lo, hi)`. If the range holds one item, the combiner is a leaf and does the work.
Otherwise it splits the range into at most F consecutive sub-ranges of `span` items, where `span` is the smallest power
of F that leaves at most F sub-ranges (`EtlModel.SpanFor`, built). It starts at most C children at once, one per
sub-range, and starts the next sub-range each time a notice frees a credit. When its cursor reaches `hi` and nothing is
outstanding, it finishes with its merged result, which becomes one notice to its own parent.

For N = 100,000 and F = 64: the root has 25 children of 4,096 items, the next level 1,563 combiners of up to 64 items,
and below them the 100,000 leaves: 101,589 instances in all, three combiner levels deep. At 4,096 the tree has 64
combiners under the root and 4,161 instances, as measured.

**State per combiner (proposed shape, after the spike's `EtlState`):**

```csharp
public sealed record FanOutSpec(
    string LeafModel, string LeafDefinition,
    string ItemsRef,           // the items, by reference (decision 10); never copied into state
    long Lo, long Hi, int Fanout, int Credit,
    FaultPolicy Faults,        // FailFast, or Collect with a threshold
    string Reducer);           // the name of a registered IReducer

public sealed class CombinerState
{
    public long Cursor { get; set; }                                // the next item not yet given to a child
    public Dictionary<string, ItemRange> Outstanding { get; set; } = [];  // at most Credit entries: child id -> range
    public object? Partial { get; set; }                            // the merged results so far
    public long Failed { get; set; }
    public List<FaultSummary> FirstFaults { get; set; } = [];       // at most K: a bounded sample of the faults
}

public interface IReducer
{
    object Empty();
    object Merge(object left, object right);   // associative and commutative: notices arrive in any order
}
```

State is O(C + K) plus the size of `Partial`. A reducer that keeps every leaf's output (collect, not sum) makes
`Partial` O(N) at the root, so collected outputs go by reference: each combiner writes its range's results to the
payload store and passes a reference up. That is ETL's model ("state is stage cursors and references to data kept
elsewhere", the sketch).

**Placement (proposed).** Combiners above the bottom level spread by hash (`Placement.Spread`) so that subtrees run on
different nodes. A bottom combiner keeps its leaves in its own virtual partition (`Placement.WithParent`): their starts
and notices are handed over in memory after each commit, with no poll, and their commits batch with the combiner's in
one flush. The spike spreads every level; the mixed placement is **unmeasured**.

**Where it lives.** In layer 3, as a reusable combiner library on the turn contract. It uses only `Start`, the child
notice, `SetStatus` and the kernel's terminate. Layer 2 needs no change: no new message kind, no new table and no
partition knowledge in the model. ETL uses it for its partitions of work. Native gets it as a distributed form of
ForEach, below.

**Other fan-in.** Many unrelated instances that send to one actor (counts, metrics, a shared aggregate) do not form a
parent tree. The same idea applies as a sharded aggregator: senders address one of F shard actors by a hash of their
own id, and each shard forwards its partial result to the aggregate after a count or a timer. The due-depth bound of
[fairness and backpressure](fairness-and-backpressure.md) still bounds every single inbox. This variant is sketched
here and not designed further.

## Completion, cancellation, faults and retries

**Completion.** A leaf finishes with its output. Its notice goes to its combiner, which removes the child from
`Outstanding`, merges the output into `Partial` and starts the next sub-range, all in one turn and one commit. A
notice is consumed in the commit that changes the state, so its result changes state once, though the turn's code can
run again after a crash. A notice for a child that is not outstanding is refused: the spike throws on an unknown or
duplicate result, and its `--mutate-count` control (every root result counted twice) fails the count check (exit 1 at
`433c79341`; `--mutate-credit`, one child over the credit, fails the credit check, exit 1). Recovery reloads the
committed `Cursor`, `Outstanding` and `Partial`; the spike injects an unknown outcome after a landed commit, takes every
partition with new owners, and checks that the reloaded checkpoint matches (measured, `ok` at `433c79341`).

**Cancellation.** A cancel or terminate of the root uses the kernel's forced termination: each instance sends a
terminate to the children it started that have not finished, and the terminate recurses. With credits, only
outstanding children exist; sub-ranges past the cursor were never started and need nothing. A cancel of a
100,000-item fan-out therefore sends at most C terminates per live combiner. Each cancelled child still sends its
Cancelled notice (built: `Terminate` adds it); its combiner has already ended, so the kernel consumes and drops the
notice, as for any message to a finished instance.

**Faults.** A leaf that faults reports status Faulted. The combiner applies its policy:

- **FailFast:** the combiner sends a terminate to each outstanding child (at most C) and faults with the first fault,
  in one turn. Its parent receives a Faulted notice and does the same, so a fault reaches the root and cancels every
  live subtree. Whether a model may send the kernel's terminate through `ITurn.Send` is **unverified**; if not, the
  combiner library defines its own cancel message, which its leaves and combiners handle.
- **Collect:** the combiner counts the fault, keeps it in `FirstFaults` while there are fewer than K, and continues. It
  faults only when the faults pass a threshold, as Native `Parallel` faults when its threshold can no longer be met
  (built). The root's result carries the count and the sample.

The spike implements neither policy: a child fault throws in the coordinator, and the kernel parks it.

**Retries.** A leaf's activity retries inside the leaf: each retry is a new generation in its iteration path, so a new
effect key (the owner's rule of 16 November). A combiner may retry a failed sub-range by starting a new child for the
same range. That child is a new instance with a new origin, so its effects carry new keys: a subtree retry is a new
attempt by design. A body whose effect must happen once per item across subtree retries uses the item's business key
for its receiver as well.

## Effect keys inside the tree

Every external effect happens in a leaf. Combiners only send kernel messages, whose ids derive from sender and
revision, so they need no effect keys.

| Form | Effect key of an item's effect | Stable across replay and takeover | Distinct per item |
|---|---|---|---|
| In-instance parallel ForEach (release 1) | `{instance origin}/{body node}/{iteration path}/{attempt}`, whose path holds the ForEach's segment with the item index | Yes: the index is committed state, independent of agenda and completion order | Yes: each body has its own index |
| Combiner tree (after release 1) | `{leaf origin}/{node}/{iteration path in the leaf}/{attempt}` | Yes: a leaf's id is issued in its combiner's turn and published only by that turn's fenced commit | Yes: each leaf has its own origin |

The parallel form reuses the rule that is built for the sequential ForEach (`ForEach.IterationOf` returns the body's
`CurrentIndex`); several bodies at once each carry their own index, so the frame number stays out of the key as
decided. The parallel activity itself is not built, so this row is **proposed**. In the tree, the key does not name the
tree path: the leaf's origin is unique, and the item a leaf handles is in its start spec and journal.

## Release 1: a documented cap

Release 1 ships no tree. It adds a parallel ForEach that runs its bodies as frames inside one instance, with two
inputs and two limits:

| Input | Meaning | Limit (proposed) |
|---|---|---|
| `Items` | The collection | At most `MaxParallelItems`. The activity faults with `ParallelForEachCapExceeded` when the evaluated collection is larger. Designer validation flags a literal collection above the cap. |
| `MaxDegreeOfParallelism` | Bodies running at once: the ForEach's credit | Default 16; at most 64 |

Why a cap is needed in this form: the items and every running body live in one instance's state, rewritten at each of
its commits. Every body that waits (a job, a timer, an event) resumes through that one instance's inbox. With the
degree as its credit, the inbox holds at most the degree's worth of its results, and the registry of child workflows
that bodies start stays at most the degree. Items by reference (decision 10) would remove the O(N) copy; the turns
remain at least one commit per waiting body.

**The cap's value is the owner's decision** (below). The arithmetic for 1,000 items of about 50 bytes, unverified: a
state of about 50 kB, rewritten at up to 1,000 commits, about 50 MB of row writes per ForEach; at 10,000 items about
5 GB. The release 1 state-size benchmark (decision 10) measures it before the cap is published. A body that starts a
child workflow per item stays inside the same cap.

Larger work in release 1 is split by the author: a sequential ForEach over batches that each dispatch a bounded
parallel ForEach, or child workflows for batches. The documentation states the cap, the reason and these patterns.

**After release 1, with ETL:** the combiner library; ETL's partitions of work on it; and, for Native, an explicit
distributed form of ForEach (for example `Distribution = Tree`) that runs each item's body as a leaf workflow instance.
The distributed form changes semantics: a leaf runs in its own instance, sees no variables of the parent's scope other
than its inputs, and returns its output through the reducer. That is why it stays a choice the author makes.

**Verification planned** (none of it exists):

- The spike at 100,000 leaves. ETL.md records a run stopped after four minutes under shared-machine load. A rerun at
  `433c79341` (`--size 100000`, Release) was stopped after 9 min 56 s of one busy CPU core, also before it printed a
  result (exit 143 from the stop). Why the in-memory run grows faster than the 4,096 run suggests was not investigated.
- The FailFast and Collect policies with a forced leaf fault.
- A cancel of a large tree, counting the terminates.
- The mixed placement.
- A PostgreSQL run that records round trips and bytes per level against the direct baseline.

## Decisions for the owner

1. **The release 1 cap on a parallel ForEach.**
   - (a) 1,000 items, degree default 16 and maximum 64, confirmed or lowered by the state-size benchmark before
     release. *Recommended:* the arithmetic stays in tens of MB per ForEach.
   - (b) 10,000 items, accepting GB-scale writes per ForEach until values by reference land.
   - (c) No parallel ForEach in release 1.
2. **How the distributed form is chosen after release 1.**
   - (a) An explicit option on ForEach. *Recommended:* the body's scope and output semantics change, so the author
     decides.
   - (b) Automatic above the cap, which would change semantics without the author knowing.
3. **Fault policy default for the combiner library.**
   - (a) FailFast. *Recommended:* it matches a structured parallel block and needs no threshold.
   - (b) Collect with a threshold, which ETL batch jobs may prefer per job.

## Contract and evidence

The [design sketch](../reports/clean-slate-kernel/design-sketch-draft-5.md) states the question, the placement options
and ETL's fit. [Fairness and backpressure](fairness-and-backpressure.md) covers credits and the inbox bound. The
prototype's [ETL.md](../../prototypes/kernel-model/models/Etl/ETL.md), [NATIVE.md](../../prototypes/kernel-model/NATIVE.md),
[COMMIT-SQL.md](../../prototypes/kernel-model/COMMIT-SQL.md) and [WRITES.md](../../prototypes/kernel-model/WRITES.md)
hold the built and measured evidence cited here.
