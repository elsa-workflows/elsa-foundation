using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Storage;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Tasks;
using Elsa.Persistence.EFCore;
using Elsa.Tasks.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore;

/// <summary>
/// Base persistence feature that replaces the default in-memory <see cref="IOpenTelemetryStore"/> with the
/// EF Core-backed <see cref="EfCoreOpenTelemetryStore"/>.
/// </summary>
public abstract class EFCoreOpenTelemetryPersistenceFeatureBase : EFCorePersistenceShellFeatureBase<OpenTelemetryDbContext>
{
    protected EFCoreOpenTelemetryPersistenceFeatureBase()
    {
        UseCommands = false;
        UseQueries = false;
    }

    protected override Action<IServiceProvider, DbContextOptionsBuilder> DbContextOptionsBuilder { get; set; } =
        (_, builder) => builder.UseLoggerFactory(NullLoggerFactory.Instance);

    protected override void OnAfterConfigured(IServiceCollection services)
    {
        services.AddSingleton<EfCoreOpenTelemetryStore>();
        services.AddSingleton<IOpenTelemetryStore>(sp => sp.GetRequiredService<EfCoreOpenTelemetryStore>());
        services.AddScoped<IStartupTask, StartOpenTelemetryDrainingStartupTask>();
    }
}
