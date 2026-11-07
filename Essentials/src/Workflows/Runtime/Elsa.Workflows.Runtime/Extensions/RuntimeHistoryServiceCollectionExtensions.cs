using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
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
        services.TryAddScoped<IRuntimeHistorySourceContributor>(provider => provider.GetRequiredService<RuntimeHistorySourceAvailability>());
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
        try { return ResolveQualifiedPolicy(provider, options); }
        catch (Exception exception) when (exception is not (OperationCanceledException or ArgumentException or InvalidDataException or RuntimeSchedulerWorkUnavailableException) &&
            exception is IOException or InvalidOperationException or NotSupportedException)
        {
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The current runtime history provider is unavailable for admission.", exception);
        }
    }

    private static RuntimeHistoryCapturePolicy? ResolveQualifiedPolicy(IServiceProvider provider, RuntimeHistoryOptions options)
    {
        var writer = provider.GetRequiredService<IRuntimeCheckpointCommitStore>();
        var reader = provider.GetRequiredService<IRuntimeHistoryStore>();
        if (!writer.SupportsHistory || !writer.CanCommitHistory(reader))
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The selected runtime history providers cannot acknowledge one atomic checkpoint.");
        if (provider.GetService<IRuntimeActivityExecutionInspectionAccumulator>()?.SupportsCheckpointValueCapture != true)
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The selected value owner cannot supply immutable checkpoint evidence contributions.");
        var owners = provider.GetServices<RuntimeHistorySourceSupport>().ToArray();
        var observations = provider.GetService<IRuntimeHistoryObservationWriter>();
        var kinds = owners.Where(owner => owner.SourceIntentKind is null || observations is not null && observations.CanRecordHistory(reader) &&
                provider.GetService<IRuntimePostCommitIntentDispatcher>()?.CanRecordHistory(owner.SourceIntentKind, observations, reader) == true)
            .SelectMany(owner => owner.Kinds).Select(RuntimeHistorySourceFacts.EventName).ToList();
        if (observations is not null && observations.CanRecordHistory(reader))
        {
            foreach (var owner in owners.Where(owner => owner.ObservationIntentKind is { } kind &&
                         provider.GetService<IRuntimePostCommitIntentDispatcher>()?.CanRecordHistory(kind, observations, reader) == true))
                kinds.AddRange(owner.ObservationKinds);
            if (provider.GetService<IBookmarkResumeDispatcher>() is IRuntimeHistoryDeliverySource delivery && delivery.CanRecordHistory(observations, reader))
                kinds.AddRange(RuntimeHistoryDeliveryFacts.Kinds);
            if (owners.Any(owner => owner.TimerOwner) && provider.GetService<IDurableTimerStore>() is IDurableTimerHistoryStore { SupportsHistoryResponsibility: true } timers &&
                provider.GetService<IDurableTimerScheduler>() is IRuntimeHistoryTimerSource timerOwner && timerOwner.CanRecordHistory(timers))
                kinds.AddRange(DurableTimerHistoryFacts.Kinds);
        }
        return options.CreatePolicy(kinds);
    }
}
