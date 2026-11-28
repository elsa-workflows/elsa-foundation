# Elsa.Workflows.Runtime.KernelApi

Serves the workflow runtime API's Runs list, run summaries and activity timelines on a kernel shell.

- `WorkflowsRuntimeKernelRetainedApi` serves the Actor routes with the shared Runtime HTTP package.
- The optional hybrid `WorkflowsRuntimeKernelApi` feature depends on `WorkflowsRuntimeKernel` and
  `WorkflowsRuntimeApi`. A host selecting the hybrid includes the `Elsa.Workflows.Runtime.Api` package
  and the base Runtime API feature. After every other feature has registered, the hybrid replaces the runtime API's
  list, details, activity browse and activity inspection services with the kernel's, and refuses a shell that registers any other implementation. It
  requires the host's `KernelRuns` (`Elsa.Workflows.Runtime.KernelHost`), registered by instance on the root container.
- The kernel's list shows the kernel's runs in the host's bound tenant (`KernelRuns.Tenant`), and no other runs: a
  caller reading another tenant, or across tenants, sees none, and old-core runs (Studio's test runs among them) are not
  listed on a kernel shell.
- Each run maps to a `WorkflowExecutionState`. Its actor reference is the execution id. The executable it pins comes
  from the shell's executable store, by the workflow the run executes (an artifact on a publishing host); an artifact
  the store does not hold shows no version or hash. Its status
  maps Finished to Completed and a suspended run to Suspended. Its start is the time of the turn that created its state,
  which the workflow actor keeps in its header (a run started before the actor kept it shows the epoch: start unknown),
  and its end is the actor's end. The summary carries zero activity and incident counts. Clients can load the activity
  timeline separately.
- The classic and Actor lists share the runtime API's filters, paging cursors and per-run authorization. A definition
  filter is applied in the foundation's read. Status, dates, run kind and other filters apply to the in-memory snapshot.
- The run details endpoint loads the exact committed actor header in the host's bound tenant and applies the runtime
  API's structure and sensitive-value authorization. It reports Balanced persistence and boundary-level inspection
  because one kernel turn can execute several activities before it commits. Studio authoring test runs continue to use
  the legacy details, browse and activity inspection with the normal runtime authorization and request tenant. Kernel summaries keep operator
  controls disabled until the runtime API routes those actions to the kernel.
- Run details mark the activity timeline for separate loading in Studio. Browse and point inspection use the Native model's committed Full journal,
  including Flowchart runs. The timeline uses turn times and shows activity order and nesting. For runs already in
  progress when this evidence was introduced, the timeline begins with the first recorded frame start.

## Preview limit

A Runs list request reads every run of the tenant (or of the definition it names) from the foundation. A kernel list
query that pages in the store replaces it later. Activity browse replays the requested run's journal through the committed
revision captured by its first read and fixes that revision across cursors. Point inspection keeps only the requested frame
and can stop when that frame reaches a terminal status. Both confirm the exact run still exists before returning.

The kernel run summary defers activities and leaves incidents empty. Workbench retains named JSON results at the terminal
workflow history checkpoint, up to the checkpoint byte and JSON depth limits. Final results are available even when
optional diagnostic history capture is off. The run details authorize inspection before reading values. Structure-only
callers see redacted output names. Retirement removes the result with its history. Actor headers and journal reads carry
no output values.

The shipped store uses [`StoreFormat.RootShipped`](../../../ActorFoundation/BuiltIn/Store/EntityFrameworkCore/EfStore.Versions.cs)
with the provider's shipped definitions. The separate Workbench qualification setting
`Elsa:CorePreview:TerminalOutputQualification` selects protocol 112, with PostgreSQL definitions revision 6 and SQLite
revision 10. Setting it to `s6` selects protocol 113 with PostgreSQL revision 7 and SQLite revision 11.
Both private formats enable capture for their dedicated qualification databases. Activity and variable value history,
playback, and store migration are separate work.
