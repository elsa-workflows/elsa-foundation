using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

/// <summary>Dispatches committed intents to the single contributed handler for their ordinal kind.</summary>
public sealed class RuntimePostCommitIntentDispatcher : IRuntimePostCommitCompletionAwareDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<string, Type> _handlerTypes;
    private readonly HashSet<string> _requiredSourceKinds;
    private readonly HashSet<string> _requiredFinalizationKinds;

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
        _requiredFinalizationKinds = materialized.Where(item => item.RequiresSuccessfulFinalization)
            .Select(item => item.IntentKind).ToHashSet(StringComparer.Ordinal);
    }

    public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        cancellationToken.ThrowIfCancellationRequested();

        if (_requiredFinalizationKinds.Contains(intent.Kind))
            throw FinalizationRequired(intent.Kind, null);
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

    public ValueTask DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (_requiredFinalizationKinds.Contains(source.Intent.Kind))
            throw FinalizationRequired(source.Intent.Kind, source.SourceOutboxItemId);
        if (!RequiresDurableSource(source.Intent.Kind))
            return DispatchAsync(source.Intent, cancellationToken);
        var handler = (IRuntimePostCommitDurableSourceIntentHandler)_serviceProvider
            .GetRequiredService(_handlerTypes[source.Intent.Kind]);
        return handler.HandleAsync(source, cancellationToken);
    }

    /// <summary>Prepares required finalization without persisting it; ordinary kinds retain their existing handlers.</summary>
    public async ValueTask<RuntimePostCommitDispatchPreparation> PrepareDispatchAsync(PostCommitDeliveryContext source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_requiredFinalizationKinds.Contains(source.Intent.Kind))
        {
            await DispatchAsync(source, cancellationToken);
            return RuntimePostCommitDispatchPreparation.LegacyHandled;
        }

        var handler = (IRuntimePostCommitCompletionAwareIntentHandler)_serviceProvider
            .GetRequiredService(_handlerTypes[source.Intent.Kind]);
        var finalization = await handler.PrepareCompletionAsync(source, cancellationToken);
        if (finalization is null)
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind,
                new InvalidOperationException("A completion-aware handler returned no finalization plan."), false);
        return RuntimePostCommitDispatchPreparation.FromFinalization(finalization);
    }

    private static RuntimePostCommitSourceBoundaryException FinalizationRequired(string kind, string? sourceId) =>
        new(sourceId, kind, new NotSupportedException("This kind requires typed preparation and atomic claim completion."), false);

    private static IReadOnlyDictionary<string, Type> BuildHandlerMap(
        IEnumerable<RuntimePostCommitIntentHandlerContribution> contributions)
    {
        var handlerTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var group in contributions
                     .Select(contribution => contribution ?? throw new InvalidOperationException("Runtime post-commit intent handler contributions cannot contain null entries."))
                     .GroupBy(contribution => contribution.IntentKind, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            if (group.Select(item => (item.RequiresDurableSource, item.StimulusDeliveryProtocolVersion,
                    item.RequiresSuccessfulFinalization)).Distinct().Count() != 1)
                throw new InvalidOperationException("One intent kind cannot have competing source or finalization requirements.");
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
