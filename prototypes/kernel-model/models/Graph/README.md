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
| `RuntimeStructuralContinuation` (Defer, Complete, Faulted) with the private state staged on it | `FlowchartStep`: kind, the state main stages (pruned for persistence, #382), the scheduled nodes, outcome or fault | A pure step: `(graph, state, one callback) -> (state, schedules, continuation)` |
| `StartAsync`, `OnChildCompletedAsync`, `OnChildFaultedAsync` read the graph from the executable node and load the state from the private state | `Start`, `OnChildCompleted`, `OnChildFaulted` take the graph and the state | The host holds both |
| `FlowchartGraph.From(ExecutableNode)` reads the activities slot and the structure payload | `FlowchartGraph.From(children, FlowchartStructure)`; `FlowchartNode` is an id and a trigger flag | No executable node |
| `ActivityOutcomes.Done`, `ActivityOutcomes.Break` | `FlowchartOutcomes` | No Elsa constants |
| `FlowchartStructure.Variables`, `FlowchartAuthoredStructure`, the structure handler, start-entry provider, replay-safe routing probe, child-result analysis | Not taken | Design time and Elsa runtime plumbing, not the engine |
| `ActivitiesFlowchartFeature` (DI) | `FlowchartEngines.Create(options, extraPolicies)` | The same collaborators and built-in policies, without a container |

Everything else (the join coordinator, policy applier, scope resolver, state mutator, diagnostics, pruning, the
policies and the models) is main's code with its namespace changed.

## The corpus

`models/Graph.Tests` runs main's conformance corpus and join tests against this engine through `GraphHarness`, a
host that does what main's workflow execution harness does for a Flowchart: probe leaves with a fixed outcome,
children run first in, first out, execution ids from a pool, the state carried between callbacks as main stages it.
That includes main's two determinism byte-locks (dead-path and reachability): the persisted state is byte-identical
to main's, so the extracted engine makes the same state mutations in the same order.

```
dotnet test models/Graph.Tests -c Release
```
