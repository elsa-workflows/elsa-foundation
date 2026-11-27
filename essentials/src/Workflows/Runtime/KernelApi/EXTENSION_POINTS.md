# Workflows runtime kernel API extension points

The per-project catalog of `Elsa.Workflows.Runtime.KernelApi`. The [README](README.md) describes the available run reads.

## Composition

`WorkflowsRuntimeKernelApiFeature` (`WorkflowsRuntimeKernelApi`) is a public, inheritable shell feature with virtual
`ConfigureServices` and `PostConfigureServices`. It depends on `WorkflowsRuntimeKernel` and `WorkflowsRuntimeApi`. In
`PostConfigureServices` it requires the host's `KernelRuns`, registered by instance on the root container, and puts the
kernel's implementations of the replacement contracts below in the place of the runtime API's defaults, refusing a shell
that registers any other implementation (constitution 2.6.2).

## Implemented replacement contracts

### `IWorkflowInstanceListService` *(API contract — `Elsa.Workflows.Runtime.Api`; kernel implementation here)*
- **Kind:** Replacement (one list serves a shell's runtime API).
- **Implementation:** `KernelWorkflowInstanceListService`, scoped. It reads the kernel's runs of the host's bound tenant
  through `KernelRuns`, maps each to a `WorkflowExecutionState`, and lists them through the runtime API's
  `WorkflowInstanceListService` over an in-memory store.

### `IWorkflowInstanceDetailsService` *(API contract — `Elsa.Workflows.Runtime.Api`; kernel implementation here)*
- **Kind:** Replacement (one details service serves a shell's runtime API).
- **Implementation:** `KernelWorkflowInstanceDetailsService`, scoped. It reads the exact run through `KernelRuns`
  and uses the runtime API's authorization and summary view.

### `IActivityExecutionBrowseSource` and `IActivityExecutionInspectionService` *(API contracts)*

- **Kind:** Replacement (each operation has one serving implementation per shell).
- **Implementations:** `KernelActivityExecutionBrowseSource` and `KernelActivityExecutionInspectionService`, scoped.
  They supply the exact run and its Full journal timeline to the normal runtime API browse and point routes.

## Host-composed state consumed

- `KernelRuns` (`Elsa.Workflows.Runtime.KernelHost`): the foundation's reads and the tenant the host binds them to.
