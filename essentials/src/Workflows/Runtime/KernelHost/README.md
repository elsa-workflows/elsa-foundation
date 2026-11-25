# Elsa.Workflows.Runtime.KernelHost

Runs workflows designed in Studio and published through the definitions API on the clean-slate kernel. It reads the
old core's compiled contracts (`Elsa.Workflows.Runtime.Core`), which layer 3 does not reference, and turns them into
the Native model's definitions.

- `NativeExecutableMapper` maps a `WorkflowExecutable` to a `Kernel.Native.Workflow`, or refuses it with a
  `NativeMappingRefusedException` (`activity-unsupported` or `binding-unsupported`) that names the node and, where it
  matters, the input.

The mapping rules are in [definitions on the kernel](../../../../../docs/architecture/definitions-on-the-kernel.md#mapping-rules).

## Limits

Each is refused with the node and input named, and the refusal says what to change.

- Delay takes a literal duration only, in the form publishing writes a time span (`00:00:05`). Publishing reads an
  authored bare number such as `5` as days and writes `5.00:00:00`, so both cores wait five days.
- HttpEndpoint takes one HTTP method.
- HttpEndpoint and Event pass `CanStartWorkflow` through: false makes them waits, also where a run begins.
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
