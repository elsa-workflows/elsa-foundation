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
the two compile paths cannot disagree.

## Pinned contract (`Elsa.Activities.Runtime.Core.Models.ActivityInputContract`)

The pinned-contract path (`RuntimeInputBindingCompiler.CompileAll(nodeId, IEnumerable<ActivityInputContract>, ...)`,
used by `ActivityTemplatePlacer`) has no `InputDefinition`. `ActivityInputContract` therefore gains an explicit
`IsCredential` flag, set by `ExecutableNodeCompiler.BuildActivityContract` from `InputDefinition.IsCredential`. The
credential rule reads that flag and nothing else. It never infers "credential" from `RequiresEncryption`, which is
also true for non-credential inputs (any `Secret` binding, and any pinned policy that asks for encryption);
inferring it would refuse literals the spec allows on non-credential inputs. Undeclared inputs keep their existing
contract schema fingerprint, pinned by a golden fingerprint test.

## Encryption-required inputs (FR-010)

Independently of the credential rule, publish refuses a literal, object, default or expression binding on any input
whose effective policy requires encryption, with `VF-ACT-011` (research R8). Only a `Secret` reference or no
binding is accepted. An unbound input whose pinned contract declares a default counts as a literal. For a credential
input the credential rule runs first, so it reports `Inputs/CredentialLiteral`.

## Not in this contract

No built-in activity is annotated in phase 0 (see research R5). Workflow-level inputs, variables and outputs have no
declaration surface in this work.
