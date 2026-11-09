# Decisions workflow design extension points

| Contract | Kind | Lifetime | Registration | Known implementation |
| --- | --- | --- | --- | --- |
| `IDecisionPublicationAuthorizer` | Single replacement | Scoped | `DecisionsWorkflowsDesignFeature` with TryAdd; exactly one required | `DecisionPublicationAuthorizer` (intra-domain default) |

Host replacements must preserve tenant boundaries and current explicit domain grants; unsupported identity fails closed. See [hosting](README.md).

## Cross-domain contributions

- IExecutableCompilationSource → DecisionPinSource (cross-domain Workflows publication).

## Events

None.
