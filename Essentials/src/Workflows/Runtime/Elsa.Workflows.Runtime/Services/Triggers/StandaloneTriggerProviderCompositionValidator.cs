using CShells.Lifecycle;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed record StandaloneTriggerCompositionServices(IServiceCollection Services);

/// <summary>Checks final replacement ownership before shell migrators or source intake can run.</summary>
internal sealed class StandaloneTriggerProviderCompositionValidator(StandaloneTriggerCompositionServices composition) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var services = composition.Services;
        foreach (var port in new[] { typeof(IStandaloneTriggerStore), typeof(IRuntimeIngressDeliveryStore),
                     typeof(ITriggerSourceResourceCommitStore), typeof(IRuntimeIngressReservationReader),
                     typeof(IRuntimeIngressWaitTarget), typeof(IStandaloneTriggerTargetDiscovery),
                     typeof(ITriggerInputMapper), typeof(ITriggerManagementCommitter), typeof(IAuthoredModelQueue) })
            if (services.Count(descriptor => descriptor.ServiceType == port) > 1)
                throw new InvalidOperationException($"Standalone trigger replacement '{port.Name}' has multiple registrations.");
        var owners = services.Select(descriptor => descriptor.ImplementationInstance).OfType<StandaloneTriggerProviderRegistration>().ToArray();
        if (owners.Length > 1) throw new InvalidOperationException("Standalone trigger persistence has multiple provider owners.");
        owners.SingleOrDefault()?.EnsureOwned(services);
        return Task.CompletedTask;
    }

    internal static void Register(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(StandaloneTriggerCompositionServices))) return;
        services.AddSingleton(new StandaloneTriggerCompositionServices(services));
        services.AddShellInitializer<StandaloneTriggerProviderCompositionValidator>(LifecyclePhase.Prepare, -49);
    }
}
