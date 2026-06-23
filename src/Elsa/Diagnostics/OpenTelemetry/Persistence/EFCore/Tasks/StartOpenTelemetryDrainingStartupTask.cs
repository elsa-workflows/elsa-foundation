using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Storage;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using JetBrains.Annotations;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Tasks;

/// <summary>
/// Starts the EFCore telemetry store drain loop once migrations have had a chance to create the schema.
/// </summary>
[UsedImplicitly]
[Order(0)]
public sealed class StartOpenTelemetryDrainingStartupTask(EfCoreOpenTelemetryStore store) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        store.StartDraining();
        return Task.CompletedTask;
    }
}
