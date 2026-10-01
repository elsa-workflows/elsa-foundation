# Contract: input sensitivity declaration and effective policy

Proposed shapes for FR-006 and FR-007. Decisions are in [research R5 and R6](../research.md).

## Authoring surface (`Elsa.Activities.Runtime.Core`)

```csharp
public sealed class ActivityInputAttribute : Attribute
{
    // existing members unchanged
    /// The input carries data that must not appear in logs, evidence or inspection output.
    public bool IsSensitive { get; init; }
    /// The input carries a credential: it accepts only a secret reference or no binding. Implies IsSensitive.
    public bool IsCredential { get; init; }
}
```

Example:

```csharp
[ActivityInput(IsCredential = true, DisplayName = "API key")]
public string? ApiKey { get; set; }
```

## Catalog (`Elsa.Activities.Design.Core.Models.InputDefinition`)

| Member | Type | Rule |
|---|---|---|
| `IsSensitive` | `bool?`, default `null` | `true` when the attribute declares sensitive or credential; otherwise `null`. |
| `IsCredential` | `bool?`, default `null` | `true` when the attribute declares credential; otherwise `null`. |

Null rather than `false` keeps `DefaultActivityDefinitionHasher` output byte-identical for every activity that
declares nothing. `ClrAssemblyScanner` refuses (reconciliation error naming the activity type and input) a credential
input that also declares `DefaultValue`.

## Wire (`Elsa.Activities.Design.Api.Models.ActivityInputDescriptorView`)

Adds `bool IsSensitive` and `bool IsCredential`, always present (`false` when the catalog holds null). Intrinsic
descriptors (`IntrinsicAuthoringDescriptorProvider`) report `false` for both.

```json
{ "referenceKey": "apiKey", "name": "ApiKey", "type": "string", "isSensitive": true, "isCredential": true, "uiHint": null }
```

## Effective policy (FR-007)

| Declaration | Authored `ArgumentState.IsSensitive` | Effective `IsSensitive` | Effective `RequiresEncryption` |
|---|---|---|---|
| none | `null` or `false` | false | false |
| none | `true` | true | false |
| sensitive | `null` or `true` | true | false |
| sensitive | `false` | refused, `VF-ACT-005` | |
| credential | `null` or `true` | true | true |
| credential | `false` | refused, `VF-ACT-005` | |
| any | any, with binding `Secret` | true | true |

One helper on `ValuePolicyCombiner` computes the owner policy from the declaration and combines the authored
minimum. `ExecutableNodeCompiler.CompileActivityPolicy` and `RuntimeInputBindingCompiler.Compile` both call it, so
the two compile paths cannot disagree. The pinned-contract path
(`RuntimeInputBindingCompiler.CompileAll(nodeId, IEnumerable<ActivityInputContract>, ...)`) reads the declaration
from the pinned contract's policy. In phase 0, `RequiresEncryption` on an input policy is set only by a credential
declaration or a `Secret` binding; a test pins that equivalence so a future second producer fails it.

## Not in this contract

No built-in activity is annotated in phase 0 (see research R5). Workflow-level inputs, variables and outputs have no
declaration surface in this work.
