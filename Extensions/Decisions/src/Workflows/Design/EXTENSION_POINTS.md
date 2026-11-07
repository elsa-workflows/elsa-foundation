# Decisions workflow design extension points

| Contract | Kind | Lifetime | Registration | Known implementation |
| --- | --- | --- | --- | --- |
| `IDecisionPublicationAuthorizer` | Single replacement | Scoped | `DecisionsWorkflowsDesignFeature` with TryAdd; exactly one required | `DecisionPublicationAuthorizer` (intra-domain default) |

Host replacements must preserve tenant boundaries and current explicit domain grants; unsupported identity fails closed. See [hosting](README.md).

## Cross-domain contributions

- `IExecutableCompilationSource` → `DecisionPinSource` (cross-domain Workflows publication).
- `IActivityInputSourceConstraintProvider` → `DecisionInputSourceConstraintProvider` (shared analysis/admission). Restricts Evaluate Decision's `Decision` input to `Literal` with reason `decisions.selector-fixed-at-publication`. Identifies the activity from the loaded definition when present, otherwise from the CLR descriptor `typeAlias`, so catalog `FindVersion` rows without a `Definition` navigation still admit. Does not invent `[Output]` projections or change Done/NoMatch/Refused outcomes.

## Events

None.
