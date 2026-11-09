# Activity graph Design provider

The provider validates and compiles authored reusable activity graphs. Its registration inventory and
cross-domain contributions are documented in [EXTENSION_POINTS.md](EXTENSION_POINTS.md).

## Cross-domain contributions

For activity-draft expression authoring, the graph provider registers a scoped keyed strategy for its authorized
in-memory body projection through `Elsa.Workflows.Design.Core.Contracts.IActivityDraftExpressionGraphProjection`.
Its `ProviderKey` is `elsa.activity-graph`; the registry selects only the persisted provider key and rejects
duplicate keys at startup. The activity API owns source authorization, and the Workflows Design context source
owns shared scope/order and language tooling.
The provider does not persist a workflow or expose a placed graph's hidden body to its caller.
