# Workflows runtime kernel API extension points

The per-project catalog of `Elsa.Workflows.Runtime.KernelApi`. The [README](README.md) describes what the kernel's
run list shows and its limits.

## Composition

`WorkflowsRuntimeKernelApiFeature` (`WorkflowsRuntimeKernelApi`) is a public, inheritable shell feature with virtual
`ConfigureServices` and `PostConfigureServices`. It depends on `WorkflowsRuntimeKernel` and `WorkflowsRuntimeApi`. In
`PostConfigureServices` it requires the host's `KernelRuns`, registered by instance on the root container, and puts the
kernel's implementation of the replacement contract below in the place of the runtime API's default, refusing a shell
that registers any other implementation (constitution 2.6.2).

## Implemented replacement contracts

### `IWorkflowInstanceListService` *(API contract — `Elsa.Workflows.Runtime.Api`; kernel implementation here)*
- **Kind:** Replacement (one list serves a shell's runtime API).
- **Implementation:** `KernelWorkflowInstanceListService`, scoped. It reads the kernel's runs of the host's bound tenant
  through `KernelRuns`, maps each to a `WorkflowExecutionState`, and lists them through the runtime API's
  `WorkflowInstanceListService` over an in-memory store.

## Host-composed state consumed

- `KernelRuns` (`Elsa.Workflows.Runtime.KernelHost`): the foundation's reads and the tenant the host binds them to.
