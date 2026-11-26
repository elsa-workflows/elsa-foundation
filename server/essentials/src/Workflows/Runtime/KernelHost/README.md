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
- `KernelRuns` is the host-shared run reads: the foundation's reads and the tenant the host binds them to. The
  `WorkflowsRuntimeKernelApi` feature (`Elsa.Workflows.Runtime.KernelApi`) lists a kernel shell's runs from it.
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
- A host says what its foundation runs with `KernelHostCapabilities` (`Schedules`, `Timers`, `Children`, `SelfSends`),
  which it gives its `KernelServing`. A workflow that needs what the host lacks is refused where it is mapped, before
  anything is minted, so publishing reports `definition_unsupported` naming the node by its display name:
  - without schedule actors, a Timer or Cron: "Timer and Cron triggers don't run on this host yet. Remove the Timer or
    Cron, or start the workflow from an HTTP endpoint, an event or a Dispatch."
  - without timers, a Delay: "Delay doesn't run on this host yet. Remove it, or wait for an event or an HTTP request
    instead."
  - without child workflows, a Dispatch Workflow, waited or not: "Dispatch Workflow doesn't run on this host yet. Remove
    it, or start the other workflow from an HTTP endpoint or an event."
  - with an edge that serves no wait routes (`HttpWaits: false`), an HTTP Endpoint that waits: one anywhere but where
    the workflow begins, or one there whose start-a-workflow option (`CanStartWorkflow`) is off, as it is by default on
    the old core: "An HTTP Endpoint that waits for a request doesn't run on this host yet: only one that starts the
    workflow does. Turn on its start-a-workflow option and put it where the workflow begins, or wait for an event
    instead."
  - without self-sends, nothing is refused: a turn that reaches the activity cap cannot continue in a self-send, so the
    host sets `NativeExecutionOptions.SelfSends` to false and the Native model runs a burst on to its next wait. The
    state and history size limits still bound the turn, and so does `MaxActivitiesPerBurst` (10,000 activities, the
    same however loaded the host is). `MaxBurstDuration` (30 s) is a backstop that keeps one turn from holding its node,
    not a correctness bound. Past either the workflow faults with `native.burst.limit`, saying what it ran and what to
    change. The 1,000-item ForEach cap stands. A PostgreSQL host reads whether its store format takes self-sends from
    `PostgresPreviewHosting.TakesSelfSends`.
- `KernelHttpEdge` is the framework-free half of a host's HTTP edge. `Route` matches a request's method and path,
  relative to the edge's base path, against the start routes of what serves now; a wait route or no route is not served,
  and an edge reads no body for it. `Start` then makes the start of the route's workflow, with the request (and its route
  values) as the trigger. `Match` does both. `Answer` maps a held start's
  reply to the HTTP answer: the designed response, 204, 404, 500, 503 or 504.
- `KernelServing.ServeHostWorkflowAsync` serves a workflow the host defines itself beside the publications, its routes
  checked against the serving set (the Workbench's opt-in HTTP probe is one).
- On a kernel shell the feature also unmounts the old core's HTTP endpoint middleware (`HttpEndpointMiddleware`): it is
  re-registered with options of its own, a new instance at its defaults with an empty base path, so it passes every
  request on and the host's kernel edge serves `/workflows/http`. The old core's standalone HTTP triggers still serve
  under that base path, in the shell's pipeline ahead of the edge: a request whose method and template a standalone
  trigger and a kernel route both match goes to the trigger. The HTTP activities' test runs (`/workflows/test-runs`), standalone HTTP triggers and
  options stay as they are; a shell with the test-run middleware and no endpoint middleware of the expected name is
  refused. Studio's HTTP test run therefore still runs on the old core (slice 8).
- `UnavailableScheduleActors` is for a host that runs no schedule actor. Such a host also says `Schedules: false`, so a
  Timer or Cron never reaches it. Setting a schedule throws the reason; stopping one and unregistering a run do nothing,
  so unpublishing never fails on them.

The contracts this project implements and the seams it adds are in [EXTENSION_POINTS.md](EXTENSION_POINTS.md).

The mapping rules are in [definitions on the kernel](../../../../../../docs/architecture/definitions-on-the-kernel.md#mapping-rules).

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
  it reaches the one slot that does. The host's answer is read once per child start, and that answer both checks and
  starts the child. A Dispatch faults before a child is minted when:
  - several other slots serve the definition and none is `default` (`dispatch.ambiguous`, "Definition '…' is published
    in several channels and none is 'default'. Publish it to the default channel.");
  - no slot serves it and the registry holds no workflow of that id (`dispatch.unpublished`, "Dispatch Workflow names
    definition '…', which is not published on this host. Publish it, then run the workflow again.").

  A Dispatch never loads a stored artifact through `ResolveWorkflow`, which serves turns and jobs of instances that
  exist.
- The old core refuses to publish a workflow whose Dispatch names a definition that is not published. A kernel host
  publishes it, and the Dispatch faults when it runs (the owner's choice).
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
