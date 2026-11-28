# Elsa.Workflows.Runtime.KernelApi

Serves the workflow runtime API's Runs list, run summaries and activity timelines on a kernel shell.

- `WorkflowsRuntimeKernelRetainedApi` serves the Actor routes with the shared Runtime HTTP package.
- The host registers `KernelRuns` with the selected Actor store and tenant. An ordinary Runs list
  shows that tenant's Actor runs, filtered by definition, status, date and run kind. The shared Runtime
  HTTP projection applies paging and per-run authorization.
- To replay the earlier mixed preview, use the [pinned compatibility source](../../../../../docs/contributing/workbench-compatibility-tests.md).
- Each run maps to a `WorkflowExecutionState`. Its actor reference is the execution id. The executable it pins comes
  from the shell's executable store, by the workflow the run executes (an artifact on a publishing host); an artifact
  the store does not hold shows no version or hash. Its status
  maps Finished to Completed and a suspended run to Suspended. Its start is the time of the turn that created its state,
  which the workflow actor keeps in its header (a run started before the actor kept it shows the epoch: start unknown),
  and its end is the actor's end. The summary carries zero activity and incident counts. Clients can load the activity
  timeline separately.
- Run details authorize structure and sensitive values for the exact committed Actor run. The retained
  API admits an isolated Test Run only through its matching scope and Actor binding. Balanced runs
  report boundary-level inspection because one Actor turn can commit several activities.
- Run details mark the activity timeline for separate loading in Studio. Browse and point inspection use the Native model's committed Full journal,
  including Flowchart runs. The timeline uses turn times and shows activity order and nesting. For runs already in
  progress when this evidence was introduced, the timeline begins with the first recorded frame start.

## Read boundaries

A Runs list request reads the tenant's Actor runs, restricted to a definition when the request names one, before projecting the API page.
Activity browse replays the requested run's journal through the committed
revision captured by its first read and fixes that revision across cursors. Point inspection keeps only the requested frame
and can stop when that frame reaches a terminal status. Both confirm the exact run still exists before returning.

The kernel run summary defers activities and leaves incidents empty. Workbench retains named JSON results at the terminal
workflow history checkpoint, up to the checkpoint byte and JSON depth limits. Final results are available even when
optional diagnostic history capture is off. The run details authorize inspection before reading values. Structure-only
callers see redacted output names. Retirement removes the result with its history. Actor headers and journal reads carry
no output values.

The shipped store uses [`StoreFormat.RootShipped`](../../../ActorFoundation/BuiltIn/Store/EntityFrameworkCore/EfStore.Versions.cs)
with the selected provider's shipped definitions.
