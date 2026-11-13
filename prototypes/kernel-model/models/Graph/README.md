# Kernel.Graph: the graph engine

Main's Elsa 4 Flowchart engine, lifted out of the Elsa runtime (owner decision, 12 November; design sketch draft 5,
"Containers and their semantics"). Token flow: execution paths with iteration keys, arrivals at joins, BPMN's
gateway kinds as policies, dead paths, and the dead-path join as the default (`FlowchartOptions.JoinPolicyKind`),
with the reachability join kept beside it. No reference to the kernel, to Elsa or to any package.

Copied from `Essentials/src/Activities/Flowchart` at `a3eb06e851`. Type names, file layout, messages and state shapes
are main's, so that the two can be compared file by file (the first commit of the PR that added this folder is the
verbatim copy). Main's product Flowchart is unchanged.

## What was adapted

| Main | Here | Why |
|---|---|---|
| `IRuntimeActivityExecutionContext`: the container's execution id, trigger, `ScheduleChildActivity` with command metadata and provenance | `FlowchartRun`: the container's execution id, the trigger, and the list of `FlowchartScheduledNode` the engine asked for | The engine decides; the host schedules |
| `ActivityChildCompletedContext` plus the work item's command metadata (path id, scope id) | `FlowchartChildCompletion`, which carries the path and scope ids the host was given | The same facts, handed back by the host |
| `RuntimeStructuralContinuation` (Defer, Complete, Faulted) with the private state staged on it | `FlowchartStep`: kind, the state main stages (pruned for persistence, #382), the scheduled nodes, outcome or fault. An empty graph completes with Done and returns the initial state (main stages nothing there) | A pure step: `(graph, state, one callback) -> (state, schedules, continuation)` |
| Main's runtime refuses a terminal decision (complete, fault) that also schedules children | `FlowchartStatePersister.StageState` throws the same `InvalidOperationException` | A host would otherwise run them |
| `FlowchartScheduler` passes command metadata and `ActivitySchedulingProvenance` (branch, iteration, workflow execution) to the runtime | The same facts as one `FlowchartScheduledNode`; branch and workflow ids are the host's | No runtime |
| `FlowchartStatePersister.LoadState` reads the private-state envelope | Not taken; the host passes the state back | No envelope |
| `StartAsync`, `OnChildCompletedAsync`, `OnChildFaultedAsync` read the graph from the executable node and load the state from the private state | `Start`, `OnChildCompleted`, `OnChildFaulted` take the graph and the state | The host holds both |
| `FlowchartGraph.From(ExecutableNode)` reads the activities slot and the structure payload, and checks its kind and schema version | `FlowchartGraph.From(children, FlowchartStructure)`; `FlowchartNode` is an id and a trigger flag; no slot, kind or version to check | No executable node |
| `ActivityOutcomes.Done`, `ActivityOutcomes.Break` | `FlowchartOutcomes` | No Elsa constants |
| `FlowchartStructure.Variables` (removed from the copied record), `FlowchartAuthoredStructure`, the structure handler, start-entry provider, replay-safe routing probe, child-result analysis | Not taken | Design time and Elsa runtime plumbing, not the engine |
| `ActivitiesFlowchartFeature` (DI) | `FlowchartEngines.Create(options, extraPolicies)` | The same collaborators and built-in policies, without a container |

Beyond the table, the files that take main's runtime context (the join coordinator, policy applier, scheduler and
engine) take `FlowchartRun` instead, with `context.ContainerExecutionId` where main reads the activity execution id;
the rest (scope resolver, state mutator, diagnostics, pruning, the policies and the models) is main's code with its
namespace changed. Main's metadata-key and state-type constants on the engine are kept, unused, so that the file
still reads as main's.

## The corpus

`models/Graph.Tests` runs main's conformance corpus and join tests against this engine through `GraphHarness`, a
host that does what main's workflow execution harness does for a Flowchart: probe leaves with a fixed outcome,
children run first in, first out, execution ids from a pool, the state carried between callbacks as main stages it.
That includes main's two determinism byte-locks (dead-path and reachability): the persisted state is byte-identical
to main's, so the extracted engine makes the same state mutations in the same order.

```
dotnet test models/Graph.Tests -c Release
```

What the port does not assert, because it belongs to Elsa's runtime: incident ids and activity execution statuses
(main's fault-join and implicit-join tests), and the flowchart's fault count (main's ambiguous-loopback test; here
the fault message and that nothing ran). The execution-id pools are main's, so each test still bounds how many
children may run. Not ported: main's `FlowchartExecutionEngineTests`, `FlowchartTriggerStartTests`,
`FlowchartExecutionStateGoldenTests`, `FlowchartStateGrowthTests`, `FlowchartRoutingTableDifferentialTests`,
`FlowchartPolicyContractTests`, `FlowchartIterationKeyDerivationTests` and the activity, structure-conversion,
scoped-variable and harness tests. Trigger starts and start-entry selection are covered only by the code being
main's; the native model starts every Flowchart at its start node.
