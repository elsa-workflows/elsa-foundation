using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Primitives.Services;

/// <summary>
/// Delivers a committed <see cref="PublishEvent"/> publish intent: after the activity's own checkpoint
/// commits, it routes the carried named-event stimulus through the existing <see cref="IStimulusRouter"/> in
/// <see cref="StimulusRoutingMode.StartAndResume"/> mode — starting every published message-start workflow that listens
/// on the name AND resuming every parked same-name catch, in one delivery. The router is <b>never</b> called during the
/// activity's execution; delivery is fire-and-continue, retried by the outbox per the registered retry policy.
/// </summary>
public sealed class PublishStimulusExecutor : IRuntimePostCommitDurableSourceIntentHandler
{
    private const string DeliveryFailureCode = "publish-stimulus-delivery-failed";
    private const string DeliveryFailureSummary = "The named-event stimulus could not be routed.";
    private const string ShedFailureCode = "publish-stimulus-start-shed";
    private const string CancellationFailureDataKey = "Elsa.Runtime.History.PublicationFailuresBeforeCancellation";

    private readonly IStimulusRouter _stimulusRouter;
    private IServiceProvider? _historyServices;
    private TimeProvider _clock = TimeProvider.System;

    internal static PublishStimulusExecutor Create(IServiceProvider services) => new(services.GetRequiredService<IStimulusRouter>())
        { _historyServices = services, _clock = services.GetService<TimeProvider>() ?? TimeProvider.System };

    public PublishStimulusExecutor(IStimulusRouter stimulusRouter)
    {
        ArgumentNullException.ThrowIfNull(stimulusRouter);
        _stimulusRouter = stimulusRouter;
    }

    bool IRuntimePostCommitDurableSourceIntentHandler.CanRecordHistory(IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader)
    {
        ArgumentNullException.ThrowIfNull(writer); ArgumentNullException.ThrowIfNull(reader);
        var services = _historyServices;
        if (services is null) return false;
        try
        {
            if (services.GetService<IPersistenceAccessContextAccessor>() is not { } access) return false;
            _ = access.Current.RequireScope();
            return ReferenceEquals(writer, services.GetService<IRuntimeHistoryObservationWriter>()) &&
                ReferenceEquals(reader, services.GetService<IRuntimeHistoryStore>()) && writer.CanRecordHistory(reader) &&
                ReferenceEquals(_clock, services.GetService<TimeProvider>()) && ReferenceEquals(_stimulusRouter, services.GetService<IStimulusRouter>()) &&
                services.GetService<IStimulusDeliveryStore>() is not null && services.GetService<IWorkflowExecutionStateStore>() is not null &&
                _stimulusRouter.CanRecordHistory(PublishStimulusConstants.PublishStimulusIntentKind, writer, reader);
        }
        catch (Exception failure)
        {
            throw new RuntimePostCommitSourceBoundaryException(null, PublishStimulusConstants.PublishStimulusIntentKind, failure,
                PublishStimulusHistoryObservations.IsUnavailable(failure));
        }
    }

    /// <summary>Intent-only delivery cannot select recipients; a committed physical source is required.</summary>
    public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent); cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException(new RuntimePostCommitSourceBoundaryException(null, intent.Kind,
            new NotSupportedException("Named-event delivery requires its committed physical source and saved cohort."), false));
    }

    async ValueTask IRuntimePostCommitDurableSourceIntentHandler.HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source); cancellationToken.ThrowIfCancellationRequested();
        if (source.Intent.Kind != PublishStimulusConstants.PublishStimulusIntentKind)
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind,
                new InvalidDataException("The named-event handler received another intent kind."), false);
        var observation = await PublishStimulusHistoryObservations.BeginAsync(source, _historyServices, _clock, cancellationToken);
        StimulusRoutingResult result;
        try
        {
            result = await _stimulusRouter.RouteSourceAsync(source, cancellationToken);
        }
        catch (StimulusDeliveryBoundaryException exception)
        {
            var primary = new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, exception, exception.IsUnavailable);
            await ObserveFailureAsync(source, observation, primary.IsUnavailable ? "routing-source-unavailable-observed" : "routing-source-refused-observed",
                primary, primary.IsUnavailable, cancellationToken);
            throw primary;
        }
        catch (RuntimePostCommitSourceBoundaryException primary)
        {
            await ObserveFailureAsync(source, observation, primary.IsUnavailable ? "routing-source-unavailable-observed" : "routing-source-refused-observed",
                primary, primary.IsUnavailable, cancellationToken);
            throw;
        }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || observation is null)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A checkpoint rule violation in a routed start or resume recurs on every attempt, and a retried start that is
            // now a duplicate would report the delivery as a success. Anything else may be infrastructure.
            var kind = RuntimeCheckpointCommitValidationException.IsCauseOf(exception)
                ? PostCommitFailureKind.Permanent
                : PostCommitFailureKind.Transient;
            var primary = new RuntimePostCommitDeliveryException(kind, DeliveryFailureCode, DeliveryFailureSummary, exception);
            await ObserveFailureAsync(source, observation, "routing-failure-observed", primary, kind == PostCommitFailureKind.Transient, cancellationToken);
            throw primary;
        }

        // Admission shed a start at capacity (#2548): nothing was written and its key is unconsumed, so reporting the
        // delivery as done would lose that start. Failing it as transient lets the outbox retry the whole delivery; the
        // starts and resumes that did go through answer the retry as duplicates under the same keys.
        if (result.ShedStartCount > 0)
        {
            var primary = new RuntimePostCommitDeliveryException(
                PostCommitFailureKind.Transient,
                ShedFailureCode,
                $"{result.ShedStartCount} start(s) were refused by runtime admission at capacity and will be retried.");
            await ObserveFailureAsync(source, observation, "routing-capacity-shed-observed", primary, true, cancellationToken);
            throw primary;
        }
        if (observation is not null) await observation.DispositionAsync("saved-cohort-routing-acknowledged-observed", cancellationToken);
    }

    private static async ValueTask ObserveFailureAsync(PostCommitDeliveryContext source, PublishStimulusHistoryObservations? observation,
        string reason, Exception primary, bool primaryUnavailable, CancellationToken cancellationToken)
    {
        if (observation is null) return;
        try { await observation.DispositionAsync(reason, cancellationToken); }
        catch (OperationCanceledException cancellation) when (cancellationToken.IsCancellationRequested)
        {
            // Keep shutdown cancellation intact and retain the preceding routing failure as diagnostics only.
            try
            {
                var data = cancellation.Data;
                lock (data.SyncRoot)
                {
                    var prior = data[CancellationFailureDataKey];
                    var failures = prior is IReadOnlyList<object> list ? list.ToList() : prior is null ? [] : new List<object> { prior };
                    failures.Add(primary);
                    data[CancellationFailureDataKey] = Array.AsReadOnly(failures.ToArray());
                }
            }
            catch (Exception failure) when (failure is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            { /* Supplementary diagnostics cannot replace the original requested cancellation. */ }
            throw;
        }
        catch (Exception secondary)
        {
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind,
                new AggregateException("Publication delivery and its failure observation both failed.", primary, secondary),
                primaryUnavailable && PublishStimulusHistoryObservations.IsUnavailable(secondary));
        }
    }
}
