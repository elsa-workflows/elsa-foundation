using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using System.Collections.Concurrent;

namespace Elsa.Persistence.Groundwork.DesignConformance.Target;

/// <summary>Records the events the composed target publishes, and stages reconciliation candidates per scope.</summary>
public sealed class GroundworkTargetEventCapture
{
    private readonly ConcurrentQueue<IEvent> _events = new();
    private readonly ConcurrentQueue<DraftCreated> _publishedDraftCreatedEvents = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DraftCreated>> _publishedDraftCreatedWaiters =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyCollection<ActivityDefinitionVersion>> _candidates =
        new(StringComparer.Ordinal);

    public void Stage(string scope, IReadOnlyCollection<ActivityDefinitionVersion> candidates) =>
        _candidates[scope] = candidates.ToArray();

    public IReadOnlyCollection<ActivityDefinitionVersion> Candidates(string scope) =>
        _candidates.TryGetValue(scope, out var candidates) ? candidates : [];

    public void Clear()
    {
        _events.Clear();
        _publishedDraftCreatedEvents.Clear();
    }

    public IReadOnlyList<IEvent> Snapshot() => _events.ToArray();

    public void Record(IEvent @event) => _events.Enqueue(@event);

    public Task<DraftCreated> WaitForPublishedDraftCreatedAsync(string draftId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        var waiter = _publishedDraftCreatedWaiters
            .GetOrAdd(draftId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        var observed = _publishedDraftCreatedEvents.FirstOrDefault(@event => @event.DraftId == draftId);
        if (observed is not null)
        {
            _publishedDraftCreatedWaiters.TryRemove(draftId, out _);
            waiter.TrySetResult(observed);
        }

        return waiter.Task.WaitAsync(cancellationToken);
    }

    public void RecordPublishedDraftCreated(DraftCreated @event)
    {
        _publishedDraftCreatedEvents.Enqueue(@event);
        if (_publishedDraftCreatedWaiters.TryRemove(@event.DraftId, out var waiter))
            waiter.TrySetResult(@event);
    }
}

public sealed class GroundworkTargetDeferredEventPublisher(IEventPublisher eventPublisher, GroundworkTargetEventCapture capture)
    : IDeferredEventPublisher
{
    public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        capture.Record(@event);
        return eventPublisher.Publish(@event, EventPublishingStrategy.Background, cancellationToken);
    }
}

public sealed class GroundworkTargetCaptureHandler<TEvent>(GroundworkTargetEventCapture capture) : IEventHandler<TEvent>
    where TEvent : IEvent
{
    public Task Handle(TEvent @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        capture.Record(@event);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Observes the real, background-dispatched lifecycle-event pipeline only after the public draft store can
/// read the created draft. This is deliberately separate from the raw-document atomicity probe and its
/// fixture-local post-commit observation.
/// </summary>
public sealed class GroundworkTargetDraftCreatedCaptureHandler(
    IPersistenceAccessContextBinder accessContextBinder,
    IWorkflowDefinitionDraftStore drafts,
    GroundworkTargetEventCapture capture)
    : IEventHandler<DraftCreated>
{
    public async Task Handle(DraftCreated @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // DraftCreated intentionally carries aggregate identity rather than a persistence scope. The lifecycle
        // evidence exercises ScopeA, so the background handler binds that explicit test scope before using the
        // same public read port as an application consumer.
        accessContextBinder.Bind(PersistenceAccessContext.Scoped(new PersistenceScope(DesignPersistenceFixtureData.ScopeA)));
        if (await drafts.FindWithLayoutByIdAsync(@event.DraftId, cancellationToken) is null)
            throw new InvalidOperationException("DraftCreated reached the composed event pipeline before its draft was durable.");

        capture.RecordPublishedDraftCreated(@event);
    }
}

public sealed class GroundworkTargetReconciliationHandler(
    IPersistenceAccessContextAccessor accessContext,
    GroundworkTargetEventCapture capture)
    : IEventHandler<ActivityVersionsReconciling>
{
    public Task Handle(ActivityVersionsReconciling @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        capture.Record(@event);
        var storageScope = accessContext.Current.Scope?.Value
                           ?? throw new InvalidOperationException(
                               "Activity reconciliation requires a scope-bound persistence access context.");
        foreach (var candidate in capture.Candidates(storageScope))
            @event.Versions.Add(candidate);
        return Task.CompletedTask;
    }
}
