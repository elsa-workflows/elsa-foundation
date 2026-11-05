using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Extensions;

public static class RuntimeHistoryServiceCollectionExtensions
{
    /// <summary>Enables production metadata history independently of QA capture sessions.</summary>
    public static IServiceCollection AddRuntimeHistory(this IServiceCollection services, Action<RuntimeHistoryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new RuntimeHistoryOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddScoped<IRuntimeHistoryStore, InMemoryRuntimeHistoryStore>();
        services.TryAddScoped<RuntimeHistorySourceAvailability>();
        services.TryAddScoped<IRuntimeHistoryObservationWriter, InMemoryRuntimeHistoryWriter>();
        services.TryAddScoped<IRuntimeHistoryRetentionStore, InMemoryRuntimeHistoryWriter>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, RuntimeHistoryCapture>());
        return services;
    }
}

internal static class RuntimeHistoryRegistration
{
    internal static RuntimeHistoryCapturePolicy? ResolvePolicy(IServiceProvider provider)
    {
        var options = provider.GetService<RuntimeHistoryOptions>();
        if (options?.Enabled != true) return null;
        var writer = provider.GetRequiredService<IRuntimeCheckpointCommitStore>();
        var reader = provider.GetRequiredService<IRuntimeHistoryStore>();
        if (!writer.SupportsHistory || !writer.CanCommitHistory(reader))
            throw new NotSupportedException("The selected checkpoint writer and history reader do not share one qualified atomic history boundary.");
        var owners = provider.GetServices<RuntimeHistorySourceSupport>().ToArray();
        var kinds = owners.SelectMany(owner => owner.Kinds).Select(kind => kind.ToString()).ToList();
        if (provider.GetService<IRuntimeHistoryObservationWriter>() is { } observations && observations.CanRecordHistory(reader))
        {
            if (provider.GetService<IBookmarkResumeDispatcher>() is IRuntimeHistoryDeliverySource delivery && delivery.CanRecordHistory(observations, reader))
                kinds.AddRange(RuntimeHistoryDeliveryFacts.Kinds);
            if (owners.Any(owner => owner.TimerOwner) && provider.GetService<IDurableTimerStore>() is IDurableTimerHistoryStore { SupportsHistoryResponsibility: true } timers &&
                provider.GetService<IDurableTimerScheduler>() is IRuntimeHistoryTimerSource timerOwner && timerOwner.CanRecordHistory(timers))
                kinds.AddRange(DurableTimerHistoryFacts.Kinds);
        }
        return options.CreatePolicy(kinds);
    }
}
