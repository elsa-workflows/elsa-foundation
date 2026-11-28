# Workflows runtime kernel API extension points

The per-project catalog of `Elsa.Workflows.Runtime.KernelApi`. The [README](README.md) describes the available run reads.

## Composition

`WorkflowsRuntimeKernelRetainedApiFeature` (`WorkflowsRuntimeKernelRetainedApi`) depends on
`WorkflowsRuntimeKernel` and `ApiCapabilities`. Its post-configuration requires the host's
`KernelRuns` and selects one retained workflow read implementation for each contract.
The earlier mixed-preview feature and its classic read fallback remain in the
[pinned Workbench compatibility source](../../../../../docs/contributing/workbench-compatibility-tests.md).
Run the archived mixed reader tests from source `ef70e5acd7fa8b07cfad4a6c0ca9a229a039f20a`
with the locked `Elsa.Workflows.Runtime.KernelHost.Tests.csproj`.

## Implemented replacement contracts

### `IWorkflowInstanceListService` *(API contract — `Elsa.Workflows.Runtime.Api.Http`; kernel implementation here)*
- **Kind:** Replacement (one list serves a shell's runtime API).
- **Implementation:** `KernelWorkflowInstanceListService`, scoped. It reads the host's bound tenant through
  `KernelRuns`, maps each run to a `WorkflowExecutionState`, and uses the shared `WorkflowInstanceListProjection`
  for filtering, paging and per-run authorization.

### `IWorkflowInstanceDetailsService` *(API contract — `Elsa.Workflows.Runtime.Api.Http`; kernel implementation here)*
- **Kind:** Replacement (one details service serves a shell's runtime API).
- **Implementation:** `KernelWorkflowInstanceDetailsService`, scoped. It reads the exact run through `KernelRuns`
  and uses the runtime API's authorization and summary view.

### `IActivityExecutionBrowseSource` and `IActivityExecutionInspectionService` *(API contracts)*

- **Kind:** Replacement (each operation has one serving implementation per shell).
- **Implementations:** `KernelActivityExecutionBrowseSource` and `KernelActivityExecutionInspectionService`, scoped.
  They supply the exact run and its Full journal timeline to the normal runtime API browse and point routes.

## Host-composed state consumed

- `KernelRuns` (`Elsa.Workflows.Runtime.KernelHost`): the foundation's reads and the tenant the host binds them to.
