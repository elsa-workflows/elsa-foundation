# Contract: Runtime Value Binding

Runtime consumes compiled value bindings from `WorkflowExecutable` nodes:

```text
ExecutableNode.InputBindings[inputName] -> RuntimeInputBinding
ExecutableNode.OutputCaptures[outputName] -> RuntimeOutputCapture
```

Resolution rules:

- Literal bindings return the literal JSON value.
- Reference bindings return a reference value descriptor.
- Durable value bindings read a declared durable value by `ValueId`.
- Activity output bindings read from the active output register by exact `ActivityExecutionId` and output name.
- Expression bindings remain declarations for expression middleware and are not persisted as evaluated input state.

Diagnostics:

- Activity-output references without a concrete producer activity execution are ambiguous.
- Activity-output references crossing suspension require durable value capture.
- History/audit output snapshots are not runtime input sources in this contract.
