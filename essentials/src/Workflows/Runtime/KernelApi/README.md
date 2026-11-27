# Elsa.Workflows.Runtime.KernelApi

Serves the workflow runtime API's Runs list, run summaries and activity timelines on a kernel shell.

- `WorkflowsRuntimeKernelApiFeature` (`WorkflowsRuntimeKernelApi`) depends on `WorkflowsRuntimeKernel` and
  `WorkflowsRuntimeApi`. After every other feature has registered, it replaces the runtime API's
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
  and its end is the actor's end. Activity and incident counts are 0, from empty stores in memory: the old core's stores
  hold nothing for a kernel run, and asking them would cost two database round trips a row.
- Filters, paging cursors and per-run authorization are the runtime API's own: the list service runs over an in-memory
  store holding the kernel's runs. A definition filter is applied in the foundation's read; status, dates, run kind and
  the rest in memory.
- The run details endpoint loads the exact committed actor header in the host's bound tenant and applies the runtime
  API's structure and sensitive-value authorization. It reports Balanced persistence and boundary-level inspection
  because one kernel turn can execute several activities before it commits. Studio authoring test runs continue to use
  the legacy details, browse and activity inspection with the normal runtime authorization and request tenant. Kernel summaries keep operator
  controls disabled until the runtime API routes those actions to the kernel.
- Run details mark the activity timeline for separate loading in Studio. Browse and point inspection use the Native model's committed Full journal,
  including Flowchart runs. The timeline uses turn times and shows activity order and nesting. For runs already in
  progress when this evidence was introduced, the timeline begins with the first recorded frame start.

## Coupling to the old core's read model

The kernel's list reuses the old core's `WorkflowExecutionState`, `InMemoryWorkflowExecutionStateStore` and
`WorkflowInstanceListService`. At K14 (the old core's removal), either keep `WorkflowExecutionState` as the runtime API's
read model, or replace this with a kernel list query that maps to `WorkflowInstanceSummaryView` directly, which comes
with paging in the store.

## Preview limit

A Runs list request reads every run of the tenant (or of the definition it names) from the foundation. A kernel list
query that pages in the store replaces it later. Activity browse replays the requested run's journal through the committed
revision captured by its first read and fixes that revision across cursors. Point inspection keeps only the requested frame
and can stop when that frame reaches a terminal status. Both confirm the exact run still exists before returning.

The kernel run summary defers activities and leaves incidents empty. The terminal-output qualification reads the original
named JSON results from the terminal workflow history checkpoint, after run inspection authorization. A caller with
structure-only access sees redacted output names. Retirement removes the result with its history. Neither actor headers nor
journal reads carry these values.

The Workbench switch `Elsa:CorePreview:TerminalOutputQualification` selects a dedicated preview database with
store schema 3, message protocol 112 and journal codec 1. PostgreSQL definitions revision 6 and SQLite definitions revision
10 use the shipped Park capability set of protocol 26 plus terminal output. The normal shipped profile keeps its existing
schema and returns empty outputs. Set `Elsa:CorePreview:TerminalOutputQualification=s6` to test terminal output alongside
actor-owned timers and timer rearming, on a separate database using protocol 113, PostgreSQL definitions revision 7 or
SQLite definitions revision 11. Both qualifications retain terminal results when diagnostic history capture is disabled.
Activity and variable value history, playback, and store migration are separate work.
