# Decisions workflow runtime extension points

| Contract | Kind | Lifetime | Registration | Known implementation |
| --- | --- | --- | --- | --- |
| `IDecisionWorkflowAuthorizer` | Single replacement | Scoped | `DecisionsWorkflowsRuntimeFeature` with TryAdd; exactly one required | `DecisionWorkflowAuthorizer` (intra-domain default) |

Host replacements must preserve tenant boundaries and current explicit domain grants; unsupported identity fails closed. See [hosting](README.md).

## Cross-domain contributions

- Ordinary activity discovery → EvaluateDecision (cross-domain Activities/Workflows runtime).

## Events

None.
