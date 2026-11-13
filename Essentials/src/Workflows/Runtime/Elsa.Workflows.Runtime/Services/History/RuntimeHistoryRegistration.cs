using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Internal composition boundary; the production feature/host is installed after owner/provider qualification.</summary>
internal static class RuntimeHistoryRegistration
{
    private static readonly RuntimeHistorySourceSupport IncidentSources = new([
        RuntimeHistorySourceFactKind.IncidentCreated, RuntimeHistorySourceFactKind.IncidentResolved,
        RuntimeHistorySourceFactKind.IncidentStrategyEvaluated, RuntimeHistorySourceFactKind.IncidentProjectionReconciled,
        RuntimeHistorySourceFactKind.IncidentSuppressed, RuntimeHistorySourceFactKind.IncidentDecisionApplied]);

    internal static void AddServices(IServiceCollection services, RuntimeHistoryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(options ?? new RuntimeHistoryOptions());
        services.TryAddScoped<IRuntimeHistoryStore, InMemoryRuntimeHistoryStore>();
        services.TryAddScoped<IRuntimeHistorySourceExtension, DefaultRuntimeHistorySourceExtension>();
        services.TryAddScoped<RuntimeHistoryCaptureCapabilities>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, RuntimeHistoryCapture>());
        if (!services.Any(descriptor => ReferenceEquals(descriptor.ImplementationInstance, IncidentSources))) services.AddSingleton(IncidentSources);
    }

    internal static (RuntimeHistoryCapturePolicy? Policy, bool? Enabled) ResolveAdmission(IServiceProvider provider, string workflowExecutionId)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        var options = provider.GetService<RuntimeHistoryOptions>();
        bool? enabled = options is null ? null : options.Enabled;
        return (enabled == true ? ResolveQualifiedPolicy(provider, workflowExecutionId) : null, enabled);
    }

    internal static RuntimeHistoryCapturePolicy? ResolvePolicy(IServiceProvider provider, string workflowExecutionId) =>
        ResolveAdmission(provider, workflowExecutionId).Policy;

    private static RuntimeHistoryCapturePolicy ResolveQualifiedPolicy(IServiceProvider provider, string workflowExecutionId)
    {
        try
        {
            var writer = provider.GetRequiredService<IRuntimeCheckpointCommitStore>();
            var reader = provider.GetRequiredService<IRuntimeHistoryStore>();
            if (!writer.SupportsHistory || !writer.CanCommitHistory(reader))
                throw Unavailable("The selected history providers cannot acknowledge one scoped native checkpoint.");
            var kinds = provider.GetServices<RuntimeHistorySourceSupport>().Where(owner => owner.SourceIntentKind is null)
                .SelectMany(owner => owner.Kinds).Select(RuntimeHistorySourceFacts.EventName);
            var policy = new RuntimeHistoryCapturePolicy("runtime-history-v3", 3, true,
                RuntimeHistoryCapture.Kinds.Concat(RuntimeHistoryCapture.Version2Kinds).Concat(kinds).ToArray());
            if (!provider.GetServices<IRuntimeCheckpointCommitEnricher>().OfType<IRuntimeHistoryCaptureSupport>().Any(capture => capture.CanCapture(policy)))
                throw Unavailable("The selected actual source/value/capture chain cannot supply this admission profile.");
            return policy;
        }
        catch (Exception exception) when (RuntimeHistoryReadFailures.IsMalformed(exception))
        {
            throw new RuntimeHistoryIntegrityException(workflowExecutionId, "the selected admission history provider could not interpret its retained data", exception);
        }
        catch (Exception exception) when (exception is not RuntimeSchedulerWorkUnavailableException && !IsHardOrCancelled(exception) &&
            !RuntimeHistoryReadFailures.IsMalformed(exception) &&
            (RuntimeHistoryReadFailures.IsUnavailable(exception) || exception is InvalidOperationException))
        {
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The history provider is unavailable for new admission.", exception);
        }
    }

    private static bool IsHardOrCancelled(Exception exception) =>
        exception is ArgumentException or OperationCanceledException || RuntimeCheckpointCommitValidationException.IsCauseOf(exception) ||
        exception.InnerException is { } inner && IsHardOrCancelled(inner);

    private static RuntimeSchedulerWorkUnavailableException Unavailable(string message) => new("runtime.history.capture-unavailable", message);
}
