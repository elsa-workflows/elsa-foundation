# Elsa.Decisions

`DecisionEngine` implements bounded, pure compilation/evaluation for `elsa-dmn-table-v1`, semantics `elsa-dmn-table-v1.0`. Ordinary applications may construct it directly; it takes no database, workflow, web host or Studio dependency. Optional `DecisionsFeature` registers the two Core ports as scoped services and preserves a host's single explicit replacement.

```csharp
var engine = new DecisionEngine();
var compilation = engine.Compile(dmnXml);
if (!compilation.Success) return;
var result = engine.Evaluate(compilation.Artifact!, new Dictionary<string, DecisionValue>
{
    ["amount"] = new("number", "9007199254740993")
});
```

Only the [named profile](../../../../specs/1195-business-decisions/contracts/dmn-profile.md) is supported. Compilation checks all cells, secure XML structure and unknown semantics before returning an executable. Outputs are scalar literals. No functions, CLR member access, scripts, clock or external actions run. XML/grammar work and runtime operations are bounded; cancellation/budget failure never returns partial successful output.

Original UTF-8 source and canonical executable have distinct lowercase SHA-256 identities. Canonical bytes contain only supported executable semantics with stable decision/rule/clause IDs. Formatting, descriptions and inert diagrams stay in the original document and do not change the executable digest. Mutable design storage must retain that original XML separately. Default System.Text.Json can serialize the immutable Core artifact/results; evaluating a loaded artifact revalidates its bytes/digest/canonical form and rejects unknown semantics.

Results distinguish Success, NoMatch, InvalidInput, Unsupported, HitPolicyViolation, BudgetExceeded, Cancelled and Unavailable. Rule/cell explanations are produced by the actual matcher, including later FIRST matches; structural diagnostics do not echo business values. The host still owns access protection for source, results and retained evidence.

Core and this package are independent of workflow hosting; the optional adapters use the existing shared execution lifecycle. See [extension contracts](EXTENSION_POINTS.md).
