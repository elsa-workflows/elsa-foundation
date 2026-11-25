# Elsa.Workflows.Runtime.KernelHost

Runs workflows designed in Studio and published through the definitions API on the clean-slate kernel. It reads the
old core's compiled contracts (`Elsa.Workflows.Runtime.Core`), which layer 3 does not reference, and turns them into
the Native model's definitions.

- `NativeExecutableMapper` maps a `WorkflowExecutable` to a `Kernel.Native.Workflow`, or refuses it with a
  `NativeMappingRefusedException` (`activity-unsupported` or `binding-unsupported`) that names the node and, where it
  matters, the input.
- `KernelActivationCoordinator` is a kernel host's `IWorkflowActivationCoordinator`. It maps first, checks the routes
  and schedules over the serving set, moves the slot through `IWorkflowActivationSwitch` with empty projections, and
  then serves. `ServeActiveSlotsAsync` serves every active slot when a host starts.
- `KernelServing` is the host singleton that holds what serves:
  - the Native `WorkflowRegistry`;
  - the serving set (slot to workflow);
  - the `HttpRoutes` and `NativeSchedules` built over the serving set;
  - `ServedWorkflow`, which a host passes as `NativeExecutionOptions.ServedWorkflow` so that a Dispatch reaches the
    version its definition serves.
- `IKernelScheduleHost` is how the coordinator reaches the schedule actors. `ScheduleActorHost` implements it on the
  host's ingress.
- `KernelTriggerBindingExtractor` reports no old-core triggers, so the publication preflight checks only the slot.

The mapping rules are in [definitions on the kernel](../../../../../docs/architecture/definitions-on-the-kernel.md#mapping-rules).

## Limits

Each is refused with the node and input named, and the refusal says what to change.

- Delay takes a literal duration only, in the form publishing writes a time span (`00:00:05`). Publishing reads an
  authored bare number such as `5` as days and writes `5.00:00:00`, so both cores wait five days.
- HttpEndpoint takes one HTTP method.
- HttpEndpoint and Event take `CanStartWorkflow` (Start new workflows) as a literal: false makes them waits, also where
  a run begins. Unbound, an HttpEndpoint only waits and an Event may start a run, as on the old core.
- A loop body reads its own loop's item or index, or an outer loop's when no loop between declares the same name. A read
  of an outer loop's item from inside an inner ForEach is refused.
- A Flowchart's start node is placed first among its children, which is where Native starts. A start node that a
  connection leads to, while another child has none, is refused, since Native would start at that other child.
- Result reads whose shapes differ on this host are refused until they match: an HttpEndpoint request's path, headers,
  query and body, its RouteData, SendHttpRequest's ResponseHeaders and ResponseBody, and DispatchWorkflow's Result.
  An HttpEndpoint's ParsedContent reads alike for a JSON or text body; for a body with no content type the old core
  reads null and Native the body.
- Container-scoped variables, other than a loop's item and index, and a Parallel with a threshold and an empty branch.
- Workflow variables named `input`, `output`, `currentItem`, `currentIndex` or `index`.
- A Dispatch Workflow names a definition; the Native model asks the host's `NativeExecutionOptions.ServedWorkflow` for
  the artifact that serves it when the child starts. It is read inside the turn, so a turn that runs again after a lost
  commit may start a version published in between.

## Serving

- Activations, deactivations and serving checks run one at a time on a host, so each one checks its routes and
  schedules against the serving set it changes.
- A Dispatch reaches the workflow its definition's `default` slot serves. When no `default` slot serves the definition,
  it reaches the one slot that does. When several other slots do, it reaches none, and the child starts the id itself.
- When a host starts, `ServeActiveSlotsAsync` finds the definitions with activations from the live activation source
  references, and their slots from the slot authority. It serves each active slot as `EnsureServingAsync` does.
- A deactivation stands even when a schedule cannot be stopped. The failure is in its diagnostic and its log, and an
  operator stops the schedule actor.
