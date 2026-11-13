using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

/// <summary>Dispatches committed intents to the single contributed handler for their ordinal kind.</summary>
public sealed class RuntimePostCommitIntentDispatcher : IRuntimePostCommitIntentDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<string, Type> _handlerTypes;
    private readonly HashSet<string> _requiredSourceKinds;
    private readonly IReadOnlyDictionary<string, RuntimeDeliverySourceKind> _sourceKinds;
    private readonly IReadOnlyDictionary<string, int?> _subscriptionProtocols;

    public RuntimePostCommitIntentDispatcher(
        IEnumerable<RuntimePostCommitIntentHandlerContribution> contributions,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _serviceProvider = serviceProvider;
        var materialized = contributions.ToArray();
        _handlerTypes = BuildHandlerMap(materialized);
        _requiredSourceKinds = RuntimePostCommitOutboxItems.RequiredSourceKinds(materialized);
        _sourceKinds = materialized.GroupBy(item => item.IntentKind, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().SourceKind, StringComparer.Ordinal);
        _subscriptionProtocols = materialized.GroupBy(item => item.IntentKind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().SubscriptionDeliveryProtocolVersion, StringComparer.Ordinal);
    }

    public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        cancellationToken.ThrowIfCancellationRequested();

        if (RequiresDurableSource(intent.Kind))
            throw new RuntimePostCommitSourceBoundaryException(null, intent.Kind,
                new InvalidOperationException("The contributed handler requires durable source context."), false);

        if (!_handlerTypes.TryGetValue(intent.Kind, out var handlerType))
        {
            throw new InvalidOperationException(
                $"Runtime post-commit intent '{intent.IntentId}' has unsupported kind '{intent.Kind}': no runtime post-commit intent handler is registered for that exact ordinal kind.");
        }

        var handler = (IRuntimePostCommitIntentHandler)_serviceProvider.GetRequiredService(handlerType);
        return handler.HandleAsync(intent, cancellationToken);
    }

    public bool RequiresDurableSource(string intentKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentKind);
        return _requiredSourceKinds.Contains(intentKind);
    }

    public bool SupportsSourceKind(RuntimeDeliverySourceKind sourceKind) => sourceKind == RuntimeDeliverySourceKind.WorkflowCheckpoint ||
        sourceKind == RuntimeDeliverySourceKind.StandaloneIngress && _sourceKinds.Values.Contains(sourceKind);

    public bool SupportsSubscriptionDelivery(RuntimeDeliverySourceKind sourceKind, string deliveryKind, int protocolVersion) =>
        protocolVersion == RuntimeSubscriptionDelivery.CurrentVersion && SupportsSourceKind(sourceKind) &&
        _sourceKinds.TryGetValue(deliveryKind, out var actualKind) && actualKind == sourceKind &&
        _subscriptionProtocols.TryGetValue(deliveryKind, out var actualProtocol) && actualProtocol == protocolVersion &&
        _requiredSourceKinds.Contains(deliveryKind);

    public ValueTask DispatchAsync(RuntimeDeliverySourceContext source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); cancellationToken.ThrowIfCancellationRequested();
        if (!_handlerTypes.TryGetValue(source.DeliveryKind, out var handlerType) ||
            !_sourceKinds.TryGetValue(source.DeliveryKind, out var expectedKind) || expectedKind != source.SourceKind)
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.DeliveryKind,
                new NotSupportedException("No matching contributed handler owns this actual source family."), false);
        if (source.SourceKind == RuntimeDeliverySourceKind.WorkflowCheckpoint)
            return DispatchAsync(new PostCommitDeliveryContext(source.Claim, source.Scope), cancellationToken);
        return ((IRuntimeDeliverySourceExecutor)_serviceProvider.GetRequiredService(handlerType)).HandleAsync(source, cancellationToken);
    }

    public ValueTask DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (_sourceKinds.TryGetValue(source.Intent.Kind, out var expectedKind) && expectedKind != RuntimeDeliverySourceKind.WorkflowCheckpoint)
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind,
                new InvalidDataException("A workflow source cannot impersonate a standalone ingress source."), false);
        if (!RequiresDurableSource(source.Intent.Kind))
            return DispatchAsync(source.Intent, cancellationToken);
        var handler = (IRuntimePostCommitDurableSourceIntentHandler)_serviceProvider
            .GetRequiredService(_handlerTypes[source.Intent.Kind]);
        return handler.HandleAsync(source, cancellationToken);
    }

    private static IReadOnlyDictionary<string, Type> BuildHandlerMap(
        IEnumerable<RuntimePostCommitIntentHandlerContribution> contributions)
    {
        var handlerTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var group in contributions
                     .Select(contribution => contribution ?? throw new InvalidOperationException("Runtime post-commit intent handler contributions cannot contain null entries."))
                     .GroupBy(contribution => contribution.IntentKind, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            if (group.Select(item => (item.RequiresDurableSource, item.StimulusDeliveryProtocolVersion, item.SourceKind, item.SubscriptionDeliveryProtocolVersion)).Distinct().Count() != 1)
                throw new InvalidOperationException("One intent kind cannot have competing durable-source requirements.");
            var distinctTypes = group
                .Select(contribution => contribution.HandlerType)
                .Distinct()
                .OrderBy(HandlerIdentity, StringComparer.Ordinal)
                .ToArray();
            if (distinctTypes.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Runtime post-commit intent kind '{group.Key}' has conflicting handler contributions: " +
                    $"{string.Join(", ", distinctTypes.Select(HandlerIdentity))}.");
            }

            handlerTypes.Add(group.Key, distinctTypes[0]);
        }

        return handlerTypes;
    }

    private static string HandlerIdentity(Type handlerType) => handlerType.FullName ?? handlerType.Name;
}
