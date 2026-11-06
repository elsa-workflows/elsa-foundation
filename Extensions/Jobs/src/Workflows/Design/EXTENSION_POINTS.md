# Jobs Workflows Design extension points

This optional package contributes to Publishing's existing compilation fan-in and defines no new extension interface or domain event.

| Contract and signature | Registration and implementation | Consumer and obligations |
|---|---|---|
| Cross-domain [`IExecutableCompilationSource`](../../../../../Essentials/src/Workflows/Publishing/Core/Contracts/IExecutableCompilationSource.cs), `ValueTask<ExecutableCompilationContribution> GetContributionAsync(ExecutableCompilationContext, CancellationToken = default)` in `Elsa.Workflows.Publishing.Core` | Internal scoped [`JobsHandlerPinSource`](Services/JobsHandlerPinSource.cs) in `Elsa.Jobs.Workflows.Design`, added with `TryAddEnumerable` by [`JobsWorkflowsDesignFeature`](JobsWorkflowsDesignFeature.cs). | Publishing's single `CollectExecutableCompilation` handler invokes contributions sequentially through `ExecutableCompilationCollecting`; `ExecutableNodeMetadataEnricher` merges validated claims before hashing. The contributor reads only bounded fixed literals, resolves no handler implementation and does not mutate the source tree. |

The registration is additive and idempotent. A host that replaces it explicitly removes this implementation's descriptor and supplies one deterministic contributor preserving the same pin contract. Unequal claims for `jobs.handler.v1` are conflicts, including a claim already on the compiled node. See the [Publishing engine catalog](../../../../../Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/EXTENSION_POINTS.md) for the fan-in owner, signature and conflict rules.
