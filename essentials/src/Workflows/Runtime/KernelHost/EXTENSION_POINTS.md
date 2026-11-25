# Workflows runtime kernel host extension points

The per-project catalog of `Elsa.Workflows.Runtime.KernelHost`, which serves a shell's publications on the clean-slate
kernel. The [README](README.md) describes the serving rules and limits; this catalog lists what the project implements,
what it adds, and what it consumes.

## Composition

`WorkflowsRuntimeKernelFeature` (`WorkflowsRuntimeKernel`) is a public, inheritable shell feature with virtual
`ConfigureServices` and `PostConfigureServices`. It depends on `WorkflowsPublishing` and `WorkflowsRuntimeTriggers`. In
`PostConfigureServices`, after every other feature has registered, it:

- requires the host's `KernelServing` and `IScheduleActors`, registered by instance on the root container so that each
  shell resolves the host's own, and refuses the shell without them;
- puts the kernel's implementations of the two replacement contracts below in the place of the old core's defaults, and
  refuses a shell that registers any other implementation of either, naming it (constitution 2.6.2);
- leaves the old core's `RouteTableTriggerIndexObserver` out of the shell, and refuses a shell that composes the old
  core's HTTP feature (`IHttpEndpointRouteTableSynchronizer`) without an observer of that name.

`WorkflowsRuntimeKernelFeature.IsComposedIn(shellServices)` tells a host whether a shell composes the feature. The
Workbench uses it to hide the `kernel-unsupported` set (`NativeActivityCatalog`) from that shell's toolbox only, as an
`IPostConfigureOptions<ActivityAvailabilityOptions>` that runs after the shell's own availability rules. The feature
cannot do this itself, since runtime projects take no design reference.

## Implemented replacement contracts

### `IWorkflowActivationCoordinator` *(Core contract — `Elsa.Workflows.Runtime.Core`; kernel implementation here)*
- **Kind:** Replacement (one coordinator owns the activation lifecycle of every slot in a shell).
- **Implementation:** `KernelActivationCoordinator`, scoped. It maps the executable (`NativeExecutableMapper`), checks
  routes and schedules over the serving set (`KernelServing`), moves the slot through `IWorkflowActivationSwitch` with
  empty projections, then serves. A mapping refusal fails at `Mapping`, a serving refusal at `ServingCheck`.
  `ServeActiveSlotsAsync` serves every active slot when a host starts.
- **Replaces:** `WorkflowActivationCoordinator`, the [Runtime catalog's](../Elsa.Workflows.Runtime/EXTENSION_POINTS.md) default, through the feature only.

### `IWorkflowTriggerBindingExtractor` *(Core contract — `Elsa.Workflows.Runtime.Core`; kernel implementation here)*
- **Kind:** Replacement (one extractor derives trigger bindings from a published executable).
- **Implementation:** `KernelTriggerBindingExtractor`, a singleton as the default is: it is stateless. It reports no
  old-core triggers, since the kernel serves routes and schedules itself, so the publication preflight checks only the slot.
- **Replaces:** `WorkflowTriggerBindingExtractor`, through the feature only.

## Owned contracts

### `IScheduleActors` *(Feature contract — `Elsa.Workflows.Runtime.KernelHost`)*
- **Kind:** Replacement owned by the host (one per host, shared with its shells by instance).
- **Signature:** `RegisterRunAsync(startKey, run)`, `SetAsync(key, revision, schedule, startKey)`, `StopAsync(key, revision)`.
- **Usage:** how `KernelActivationCoordinator` reaches the kernel's schedule actors for a served Timer or Cron.
- **Implementations:** `IngressScheduleActors`, on a host whose foundation hosts the schedule actor, through its
  ingress. `UnavailableScheduleActors`, on a host that runs none: setting a schedule or registering a run throws its
  reason, and stopping and unregistering do nothing. Such a host gives its `KernelServing` the same reason, so a Timer
  or Cron is refused at the serving check first.

## Host-composed state and seams consumed

- `KernelServing` is a sealed host singleton, not a contract: the Native `WorkflowRegistry`, the serving set, and the
  `HttpRoutes` and `NativeSchedules` built over it. A host passes `ServedWorkflow` as
  `NativeExecutionOptions.ServedWorkflow`, and `NativeExecutableMapper.MapStoredAsync` over its executable store as
  `NativeExecutionOptions.ResolveWorkflow`, so an instance started on a version no slot serves after a restart still runs.
- The coordinator notifies the shell's `IWorkflowTriggerIndexObserver` set (Studio's workflow library among them) when
  what a definition serves changes.
