# Elsa.Workflows.Runtime.KernelHost

Runs workflows designed in Studio and published through the definitions API on the clean-slate kernel. It reads the
old core's compiled contracts (`Elsa.Workflows.Runtime.Core`), which layer 3 does not reference, and turns them into
the Native model's definitions.

- `NativeExecutableMapper` maps a `WorkflowExecutable` to a `Kernel.Native.Workflow`, or refuses it with a
  `NativeMappingRefusedException` (`activity-unsupported` or `binding-unsupported`) that names the node and, where it
  matters, the input.

The mapping rules are in [definitions on the kernel](../../../../../docs/architecture/definitions-on-the-kernel.md#mapping-rules).
