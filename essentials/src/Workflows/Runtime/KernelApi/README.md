# Elsa.Workflows.Runtime.KernelApi

Serves the workflow runtime API's run reads from the clean-slate kernel on a kernel shell, so Studio's Runs page lists
the runs the kernel holds.

- `WorkflowsRuntimeKernelApiFeature` (`WorkflowsRuntimeKernelApi`) depends on `WorkflowsRuntimeKernel` and
  `WorkflowsRuntimeApi`. After every other feature has registered, it replaces the runtime API's
  `IWorkflowInstanceListService` with the kernel's, and refuses a shell that registers any other implementation. It
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

## Coupling to the old core's read model

The kernel's list reuses the old core's `WorkflowExecutionState`, `InMemoryWorkflowExecutionStateStore` and
`WorkflowInstanceListService`. At K14 (the old core's removal), either keep `WorkflowExecutionState` as the runtime API's
read model, or replace this with a kernel list query that maps to `WorkflowInstanceSummaryView` directly, which comes
with paging in the store.

## Preview limit

Each request reads every run of the tenant (or of the definition it names) from the foundation, once. This is fine for a
preview database and slows with the number of runs. A kernel list query that pages in the store replaces it later.

The run details, activity timeline and incidents are not served from the kernel yet.
