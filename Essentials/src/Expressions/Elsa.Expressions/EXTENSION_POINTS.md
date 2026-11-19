# Extension points — Expressions domain

`ExpressionsFeature` builds the authoring descriptor registry and the portable evaluator. Expression
descriptors are metadata only; they do not carry runtime handler factories.

## Implementable contributor interfaces

### `IPortableExpressionHandler` *(Core — `Elsa.Expressions.Core`)*

- **Kind:** Language evaluator for one explicit capability profile.
- **Signature:** `ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)`.
- **Register:** `services.AddScoped<IPortableExpressionHandler, MyPortableHandler>()`.
- **Boundary:** The request contains portable source/options and immutable declared parameters. It
  exposes no workflow context, variable frame, service provider, delegate, or mutation callback.
  Workflow-instance metadata (correlation ID, instance ID) reaches a handler only as a declared
  `WorkflowInstance` parameter value, never as ambient state.

Shipped implementations are `PortableJavaScriptExpressionHandler` and
`PortableLiquidExpressionHandler`, both restricted to `binding-pure-v1`.

### `IExpressionDescriptorProvider` *(Core — `Elsa.Expressions.Core`)*

- **Kind:** Authoring metadata source.
- **Signature:** `IEnumerable<IExpressionDescriptor> GetDescriptors()`.
- **Consumed by:** `ExpressionDescriptorRegistry`, which creates a startup snapshot.

### `IExpressionToolingProvider` *(Core — `Elsa.Expressions.Core`)*

- **Kind:** Language-specific, metadata-only authoring assistance provider.
- **Registration:** one provider per exact expression descriptor type.
- **Boundary:** receives a revisioned, Design-filtered metadata snapshot; it must not evaluate
  source, access runtime values, or retain source. Duplicate types fail deterministic resolver
  construction. The optional tooling capability is absent when no language provider is composed.

### `IVariableTypeDescriptorProvider` *(Core — `Elsa.Expressions.Core`)*

- **Kind:** Authoring/schema type source.
- **Signature:** `IEnumerable<TypeDescriptor> GetDescriptors()`.
- **Consumed by:** `VariableTypeDescriptorCatalog` and alias-registry seeding.

### `IExpressionVariableReader` *(Core — `Elsa.Expressions.Core`)*

- **Kind:** a host-side lookup passed with one `ExpressionEvaluationRequest`, not a registered service (spec 1222
  FR-008). An evaluator reads computed and opaque variable reads through it; the host records each read.
- **Implementations:** `RuntimeVariableReadFallback` (`Elsa.Workflows.Runtime`, the recorded run-time lookup for
  expressions publish marked with a `VariableReadFallback`) and `GraphVariableReader` (`Elsa.Activities.Graph.Runtime`,
  a graph expression's own variables).
- **Consumers:** the Jint evaluator's `variables` root and `getVariable`, and the Liquid handler's `variables` root.

### `IScriptVariableReadAnalyzer` *(Core — `Elsa.Expressions.Core`)*

- **Kind:** replacement contract per language: exactly one per language, checked at shell start (see the JavaScript
  catalog). The JavaScript feature registers `JavaScriptVariableReadAnalyzer`, the Liquid feature
  `LiquidVariableReadAnalyzer`.

### `IExecutionWarningSink` *(Core — `Elsa.Expressions.Core`)*

- **Kind:** a channel, not a replacement: the runtime supplies the sink and records what is reported (spec 1228).
- **Contract:** `Report(ExecutionWarning)`, where an `ExecutionWarning` has a stable `Code`, a `Message`, a `Severity`
  (`Warning` or `Information`) and string `Details`. A warning changes and refuses nothing, and it never carries a value
  (spec 1229 FR-006): it names paths, inputs and variables only.
- **Where it comes from:** `ExpressionEvaluationRequest.Warnings` for an input expression (kept with the input snapshot,
  with the input key in `Details["input"]`), `JavaScriptScriptEvaluationRequest.Warnings` and
  `ActivityExecutionContext.Warnings` for a step (kept on the activity state). The activity execution inspection view
  lists both in `Warnings`, and Studio's run view shows them.
- **Users:** `VF-PREC-002` (a lossy number read but not written). Next: S-7's C2 visibility warning and S-4's
  `VF-READ` warnings.

## Cross-references

- JavaScript binding boundary: [`Elsa.Expressions.JavaScript/EXTENSION_POINTS.md`](../JavaScript/Elsa.Expressions.JavaScript/EXTENSION_POINTS.md).
- Liquid binding boundary: [`Elsa.Expressions.Liquid/EXTENSION_POINTS.md`](../Liquid/EXTENSION_POINTS.md).
- JS rendering declaration contributors: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../JavaScript/Rendering/Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md).
