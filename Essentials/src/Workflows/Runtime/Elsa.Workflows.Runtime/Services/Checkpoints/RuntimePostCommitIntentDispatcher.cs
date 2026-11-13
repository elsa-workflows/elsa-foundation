using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

/// <summary>Dispatches committed intents to the single contributed handler for their ordinal kind.</summary>
public sealed class RuntimePostCommitIntentDispatcher : IRuntimePostCommitIntentDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyDictionary<string, Type> _handlerTypes;
    private readonly HashSet<string> _requiredSourceKinds;

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
    }

    public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        cancellationToken.ThrowIfCancellationRequested();

        if (intent.HistoryCausation is not null || RequiresDurableSource(intent.Kind))
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

    public bool CanRecordHistory(string intentKind, IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentKind);
        ArgumentNullException.ThrowIfNull(writer); ArgumentNullException.ThrowIfNull(reader);
        return IsSelectedPair(writer, reader) &&
            _handlerTypes.TryGetValue(intentKind, out var type) &&
            _serviceProvider.GetService(type) is IRuntimePostCommitDurableSourceIntentHandler owner && owner.CanRecordHistory(writer, reader);
    }

    public ValueTask DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Intent.HistoryCausation is not null)
            return RequireHistorySource(source, cancellationToken).HandleAsync(source, cancellationToken);
        if (!RequiresDurableSource(source.Intent.Kind))
            return DispatchAsync(source.Intent, cancellationToken);
        var handler = (IRuntimePostCommitDurableSourceIntentHandler)_serviceProvider
            .GetRequiredService(_handlerTypes[source.Intent.Kind]);
        return handler.HandleAsync(source, cancellationToken);
    }

    private bool IsSelectedPair(IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader) =>
        ReferenceEquals(writer, _serviceProvider.GetService<IRuntimeHistoryObservationWriter>()) &&
        ReferenceEquals(reader, _serviceProvider.GetService<IRuntimeHistoryStore>()) && writer.CanRecordHistory(reader);

    private IRuntimePostCommitDurableSourceIntentHandler RequireHistorySource(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        try
        {
            var cause = source.Intent.HistoryCausation!;
            if (cause.WorkflowExecutionId != source.Intent.WorkflowExecutionId || cause.DeliveryId != source.SourceOutboxItemId ||
                source.Claim.Item.StimulusDeliverySourceScope != source.Scope || string.IsNullOrWhiteSpace(cause.HistoryEventId) ||
                new[] { cause.HistoryEventId, cause.WorkflowExecutionId, cause.DeliveryId, cause.CommandId, cause.SchedulerWorkItemId }
                    .Any(value => value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > 1024)))
                throw new InvalidDataException("The history origin differs from the exact committed source identity or scope.");
            var writer = _serviceProvider.GetService<IRuntimeHistoryObservationWriter>();
            var reader = _serviceProvider.GetService<IRuntimeHistoryStore>();
            if (writer is null || reader is null || !IsSelectedPair(writer, reader) ||
                !_handlerTypes.TryGetValue(source.Intent.Kind, out var type) ||
                _serviceProvider.GetService(type) is not IRuntimePostCommitDurableSourceIntentHandler owner || !owner.CanRecordHistory(writer, reader))
                throw new NotSupportedException("The selected source owner cannot preserve its retained history responsibility.");
            return owner;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (RuntimePostCommitSourceBoundaryException) { throw; }
        catch (Exception failure)
        {
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, failure,
                RuntimeHistoryReadFailures.IsUnavailable(failure));
        }
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
            if (group.Select(item => (item.RequiresDurableSource, item.StimulusDeliveryProtocolVersion)).Distinct().Count() != 1)
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
