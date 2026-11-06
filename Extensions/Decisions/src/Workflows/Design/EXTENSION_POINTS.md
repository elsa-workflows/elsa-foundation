# Decisions workflow design extension points

| Contract | Kind | Lifetime | Registration | Known implementation |
| --- | --- | --- | --- | --- |
| `IDecisionPublicationAuthorizer` | Single replacement | Scoped | `DecisionsWorkflowsDesignFeature` with TryAdd; exactly one required | `DecisionPublicationAuthorizer` (intra-domain default) |

Host replacements must preserve tenant boundaries and current explicit domain grants; unsupported identity fails closed. See [hosting](README.md).

## Cross-domain contributions

- IExecutableCompilationSource → DecisionPinSource (cross-domain Workflows publication).
- `IActivityInputSourceConstraintProvider` → scoped `DecisionInputSourceConstraintProvider`
  *(cross-domain Workflows Design Validations Core)*, registered with `TryAddEnumerable` by
  `DecisionsWorkflowsDesignFeature`. `GetConstraintAsync(IActivityDefinitionVersion, inputKey, CancellationToken)`
  returns the literal-only fixed-selector restriction for the actual EvaluateDecision.Decision input,
  or null for other inputs/versions. It does not alter hashed catalog payloads or infer policy from UI hints.

The single Workflows-owned `CollectInputConstraints` handler aggregates this and other providers on
`InputConstraintsCollecting`; the analyzer publishes through the canonical inline Sequential pipeline
and intersects all restrictions. Decisions contributes a provider, not a competing collection handler.
`DecisionPinSource` remains the final compile/publication authority; metadata contributes no authorization.

## Events

This feature publishes no event and registers no event handler. It contributes to the Workflows-owned
`InputConstraintsCollecting` phase through the provider above; see the
[owning catalog](../../../../../Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/EXTENSION_POINTS.md).
