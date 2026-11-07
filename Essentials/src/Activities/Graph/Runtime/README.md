# Graph activity runtime

The Graph module executes pinned reusable activity graph descriptors through normal activity scope/continuation services. See [extension points](EXTENSION_POINTS.md).

## Cross-domain contributions

`GraphTestScopeContractSource` contributes model-owned closed scope declarations to Workflows Runtime's `RuntimeTestScopeContractsCollecting` event through `IRuntimeTestScopeContractSource`; the Runtime domain owns the sole aggregating handler. The source validates exact placement/dependency identity, graph consumer/schema and closed marker, and qualifies ordinary JSON-storage variable/input declarations. Schema-specific or other storage profiles remain explicitly unavailable until qualified. This does not create ancestor executions or advertise a complete reusable testing product; selected execution/context/capture acceptance is owned by spec 1220.
