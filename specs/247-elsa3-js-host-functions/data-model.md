# Data model: spec 247

## Elsa 4 (Core and Runtime)

### `WorkflowInstanceExpressionParameterBinding` (new, `Elsa.Expressions.Core`)

| Member | Type | Rule |
|---|---|---|
| `Member` | `string` | `"correlationId"` or `"instanceId"`; anything else throws `ArgumentException` |

It is a sealed record deriving from `ExpressionParameterBinding`, with discriminator `"WorkflowInstance"`. The member
names are exposed as constants (`WorkflowInstanceExpressionParameterBinding.Members.CorrelationId` and
`.InstanceId`).

### `RuntimeInputBindingResolutionContext` (changed, `Elsa.Workflows.Runtime.Core`)

| Member | Type | Source |
|---|---|---|
| `CorrelationId` (new) | `string?` | The identity projection over the committed durable values the context is built from |

The two places that build the context fill it: `RuntimeActivityInputSnapshotMaterializer` and
`WorkflowIntrinsicExecutor`. The instance ID needs no new field because it is `WorkflowExecutionId`.

### Identity seed (changed, `RuntimeWorkflowStateSeed`)

At start, `BuildSeedChanges` writes the durable value `identity:correlationId` (tagged `RuntimeMetadataKeys.IdentityName`)
whenever the dispatch carries a correlation ID. Today that value exists only after a `SetCorrelationId` runs.

## Elsa 3 import (`Extensions/Elsa3`)

### `Elsa3AmbientReferenceKind.GeneratedAccessor` (new enum member)

`Elsa3AmbientReference(GeneratedAccessor, Name)` holds `Name` = the callee without its `get` prefix, exactly as written.
The lowerer resolves it in this order:

1. **Output accessor.** If a named activity's output yields `get{Pascalize(output)}From{Pascalize(activityName)}`
   equal to the callee, resolution fails: the accessor is refused with `VF-IMP-005`.
2. **Workflow input** (standalone conversion). An input whose name is exactly `Name` resolves to
   `WorkflowRequestExpressionParameterBinding`.
3. **Variable.** A variable with an all-letters-and-digits name for which `Pascalize(name) == Name` resolves through
   the existing variable path, including loop variables. In a reusable conversion, workflow inputs are candidates at
   this step too, by `Pascalize(inputName)`, because Elsa 3 declares them as variables there; an input match resolves
   to the binding `getInput` produces (R-2).
4. **None.** Resolution fails with the "unknown or unapproved host function" refusal (`VF-IMP-005`).

If two candidates match at the same step, the result is `VF-IMP-002`.

### `Elsa3JavaScriptNames` (new, internal static)

- `Pascalize(string)`: Humanizer 2.14.1 semantics, reduced to the names Elsa 3 accepts (all letters and digits): it
  upper-cases the first character with the invariant culture.
- `IsAccessorName(string)`: Elsa 3's `IsValidVariableName`. True when the name is not empty and every character is a
  letter or a digit.
- `BuiltIns`: the names of Elsa 3's built-in functions, used to detect collisions with generated accessors.

### Refusal table (internal, in the rewriter)

A map from callee name to `(code, message, guidance)`. The rows are the ones in
[contracts/javascript-conversion.md](contracts/javascript-conversion.md).

## State transitions

None. The conversion is a pure function of the definition, as it is in spec 236.
