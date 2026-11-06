# Publishing EF Core persistence

See the [Publishing catalog](../../Elsa.Workflows.Publishing/EXTENSION_POINTS.md) for composition and persistence contracts.

## Cross-domain contributions

`RebuildActivityUsageStartupTask` contributes to [Tasks' `IStartupTask`](../../../../Tasks/Elsa.Tasks/EXTENSION_POINTS.md#istartuptask--itask-core--elsataskscore).
It refreshes derived usage after reconciliation with an isolated privileged scope across tenants and
a per-shell maintenance lock. Failures preserve existing evidence and allow startup; shutdown cancellation propagates.
