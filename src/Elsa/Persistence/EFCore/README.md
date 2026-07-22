# Elsa.Persistence.EFCore

Base EF Core persistence shell. Provides `EFCorePersistenceShellFeatureBase<TDbContext>`, the two aggregating handlers (`ApplyEntitySavingHandlers`, `ApplyEntityLoadingHandlers`), bulk-upsert infrastructure, and the migrations startup task. Domain-specific persistence features (e.g. the diagnostics lanes `Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore` and `Elsa.Diagnostics.StructuredLogs.Persistence.EFCore`) inherit from this base.

See [`EXTENSION_POINTS.md`](EXTENSION_POINTS.md) for the overridable contracts (the named per-aggregate read ports over `EFCoreReadStore<>`, `IUpsertCommandGenerator`, `IElsaDbContextSchema`) and the `IEntitySavingHandler<,>` / `IEntityLoadingHandler<,>` contributor interfaces with their aggregating handlers.

## Cross-domain contributions

- **`IStartupTask`** *(Core — `Elsa.Tasks.Core`)* — `RunMigrationsStartupTask` runs EF Core database migrations at startup. Catalog: [`Elsa.Tasks/EXTENSION_POINTS.md`](../Elsa.Tasks/EXTENSION_POINTS.md)
