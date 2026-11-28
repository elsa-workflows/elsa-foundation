using CShells.Features;
using Elsa.Tasks.Core;
using Elsa.Tasks.Schedules;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Scheduling;

[ShellFeature(
    name: "WorkflowsRuntimePreparedStandaloneSourcePump",
    DisplayName = "Workflows Runtime Prepared Standalone Source Pump",
    Description = "Drains bounded pages of protected standalone source deliveries to Actor Foundation.",
    DependsOn = new object[] { "WorkflowsRuntimePreparedStandaloneEntityFrameworkCorePersistence", "Tasks" })]
public sealed class WorkflowsRuntimePreparedStandaloneSourcePumpFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecurringTask, PreparedStandaloneSourcePump>());
}

internal sealed class PreparedStandaloneSourcePump(IPersistenceScopeRunner scopes, ILogger<PreparedStandaloneSourcePump> logger)
    : BackoffSweepWorkerTask(logger)
{
    private const int BatchLimit = 32;

    protected override TimeSpan SweepInterval => TimeSpan.FromSeconds(1);
    protected override TimeSpan MaxBackoffInterval => TimeSpan.FromMinutes(1);

    protected override async Task SweepAsync(CancellationToken cancellationToken) =>
        await scopes.RunAsync(async (_, operation, token) =>
        {
            var processor = operation.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
            const string kind = RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind;
            if (processor.SupportsSourceKind(RuntimeDeliverySourceKind.WorkflowCheckpoint) ||
                !processor.SupportsSubscriptionDelivery(RuntimeDeliverySourceKind.StandaloneIngress, kind, RuntimeSubscriptionDelivery.CurrentVersion))
                throw new InvalidOperationException("The standalone sweep requires the selected prepared-only Runtime outbox.");
            await processor.ProcessAsync(new(BatchLimit, intentKind: kind), token);
        }, cancellationToken);

    protected override bool ShouldRethrowCancellation(OperationCanceledException exception, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested;

    protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval) =>
        Logger.LogError(exception, "Prepared standalone source sweep failed ({ConsecutiveFailures} consecutive) and will retry after {Interval}.",
            consecutiveFailures, backoffInterval);
}
