# Elsa.Workflows.Runtime.KernelHost

Runs workflows designed in Studio and published through the definitions API on the clean-slate kernel. It reads the
old core's compiled contracts (`Elsa.Workflows.Runtime.Core`), which layer 3 does not reference, and turns them into
the Native model's definitions.

- `NativeExecutableMapper` maps a `WorkflowExecutable` to a `Kernel.Native.Workflow`, or refuses it with a
  `NativeMappingRefusedException` (`activity-unsupported` or `binding-unsupported`) that names the node and, where it
  matters, the input. `MapStoredAsync` maps a stored artifact by its id, which a host passes as
  `NativeExecutionOptions.ResolveWorkflow`: after a restart the registry holds what the slots serve, and an instance
  started on an earlier version loads that version from the executable store at its next turn.
- `KernelActivationCoordinator` is a kernel host's `IWorkflowActivationCoordinator`. It maps first, checks the routes
  and schedules over the serving set, moves the slot through `IWorkflowActivationSwitch` with empty projections, and
  then serves. `ServeActiveSlotsAsync` serves every active slot when a host starts.
- `KernelServing` is the host singleton that holds what serves:
  - the Native `WorkflowRegistry`;
  - the serving set (slot to workflow);
  - the `HttpRoutes` and `NativeSchedules` built over the serving set;
  - `ServedWorkflow`, which a host passes as `NativeExecutionOptions.ServedWorkflow` so that a Dispatch reaches the
    version its definition serves.
- `IScheduleActors` is how the coordinator reaches the schedule actors. `IngressScheduleActors` implements it on the
  host's ingress.
- `KernelTriggerBindingExtractor` reports no old-core triggers, so the publication preflight checks only the slot.
- `WorkflowsRuntimeKernelFeature` (`WorkflowsRuntimeKernel`) composes the kernel into a shell. After every other feature
  has registered, it replaces the old core's default activation coordinator and trigger extractor with the kernel's.
  A shell that registers any other implementation of either is refused with that implementation named, since one
  implementation serves per shell. It drops the old core's HTTP route-table observer, and refuses a shell that composes
  the old core's HTTP feature without an observer of that name. It requires the host to share its `KernelServing`
  and `IScheduleActors` by instance. `IsComposedIn` tells a host whether a shell composes it: the Workbench hides the
  activities the kernel does not run from that shell's toolbox, since the toolbox's options are a design contract that
  a runtime project does not reference.
- `UnavailableScheduleActors` is for a host that runs no schedule actor. Such a host gives its `KernelServing` the same
  reason, so a Timer or Cron publish is refused at the serving check, before anything is minted. Setting a schedule
  throws the reason; stopping one and unregistering a run do nothing, so unpublishing never fails on them.

The contracts this project implements and the seams it adds are in [EXTENSION_POINTS.md](EXTENSION_POINTS.md).

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
  it reaches the one slot that does. When several other slots do, it reaches none: the child starts the id itself when
  the registry holds that workflow, and otherwise the Dispatch faults with `dispatch.unpublished` ("Definition '…' is
  not published on this host") before a child is minted. A Dispatch never loads a stored artifact through
  `ResolveWorkflow`, which serves turns and jobs of instances that exist.
- When a host starts, `ServeActiveSlotsAsync` finds the definitions with activations from the live activation source
  references, and their slots from the slot authority. It serves each active slot as `EnsureServingAsync` does. A host
  runs it before its node runs a turn, so a due turn of an instance in flight finds its workflow served.
- After a restart the registry holds the versions the slots serve. An instance started on an earlier version loads it at
  its next turn, through `NativeExecutionOptions.ResolveWorkflow` (`NativeExecutableMapper.MapStoredAsync` over the
  executable store). The loaded version joins the registry and serves no route or schedule. A migration's check reads
  the registry only, so it names such a version unavailable until a turn has loaded it.
- A definition serves one scheduled channel in release 1. Its schedule actors are keyed by definition and trigger, so a
  second slot that would schedule the same trigger is refused at the serving check, and the refusal says to publish
  the scheduled definition to one channel.
- After each committed activation, deactivation and repair, the coordinator tells the trigger observers, so that Studio's
  workflow library refreshes. A kernel host's composition (slice 7) leaves out the old core's HTTP route-table observer.
- A deactivation stops the schedules of what the slot serves, also when this host does not serve it (after a restart):
  it maps the slot's executable again to find them.
- A republish that drops or renames a Timer or Cron stops the old schedule after the new version serves. If the process
  stops between the switch and that stop, or a revert fails, the old schedule actor keeps firing on the old version,
  and a restart serves only the new version's schedules. Stopping a definition's stray schedules needs a read of its
  schedule actors by key, which comes later.
- The publish preview shows no route or schedule conflicts on a kernel host, because the kernel trigger extractor
  reports no triggers. A conflict surfaces as a failed publish (`trigger_conflict`). Showing it in the preview is later
  work.
- A deactivation stands even when a schedule cannot be stopped. The failure is in its diagnostic and its log, and an
  operator stops the schedule actor.
