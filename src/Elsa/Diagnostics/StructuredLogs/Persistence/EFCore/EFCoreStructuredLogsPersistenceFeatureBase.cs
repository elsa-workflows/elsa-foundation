using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Storage;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Tasks;
using Elsa.Persistence.EFCore;
using Elsa.Tasks.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EFCore;

/// <summary>
/// Base persistence feature that swaps the default in-memory <see cref="IStructuredLogStore"/> for the
/// EF Core-backed <see cref="EfCoreStructuredLogStore"/>. Command/query machinery of the persistence base
/// is disabled (this is a single append-only table with a bespoke store, not a read-model domain).
/// </summary>
public abstract class EFCoreStructuredLogsPersistenceFeatureBase : EFCorePersistenceShellFeatureBase<StructuredLogsDbContext>
{
    protected EFCoreStructuredLogsPersistenceFeatureBase()
    {
        UseCommands = false;
        UseQueries = false;
    }

    /// <summary>
    /// Routes this context's EF Core logging to <see cref="NullLoggerFactory"/>. Without this, every
    /// "Executed DbCommand" the store emits would be captured by the diagnostics <c>ILoggerProvider</c>,
    /// re-persisted, and emit further commands — an unbounded feedback loop. Other DbContexts keep their
    /// normal logging.
    /// </summary>
    protected override Action<IServiceProvider, DbContextOptionsBuilder> DbContextOptionsBuilder { get; set; } =
        (_, builder) => builder.UseLoggerFactory(NullLoggerFactory.Instance);

    protected override void OnAfterConfigured(IServiceCollection services)
    {
        // Single instance shared by the store contract and the draining startup task. Registered with
        // AddSingleton (not TryAdd): the StructuredLogs feature registers its in-memory store with
        // TryAddSingleton, so this override wins regardless of feature ordering.
        services.AddSingleton<EfCoreStructuredLogStore>();
        services.AddSingleton<IStructuredLogStore>(sp => sp.GetRequiredService<EfCoreStructuredLogStore>());
        services.AddScoped<IStartupTask, StartStructuredLogDrainingStartupTask>();
    }
}
