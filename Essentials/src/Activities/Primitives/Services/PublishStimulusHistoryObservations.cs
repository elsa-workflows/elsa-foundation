using System.Globalization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Primitives.Services;

/// <summary>One actual source-handler invocation. A retry is another observation, not another business source.</summary>
internal sealed class PublishStimulusHistoryObservations(PostCommitDeliveryContext source, RuntimeHistoryCapturePolicy policy,
    IRuntimeHistoryObservationWriter writer, TimeProvider clock, string receiptId, string? relatedRun)
{
    internal const string AttemptKind = "StimulusRoutingAttemptObserved";
    internal const string DispositionKind = "StimulusRoutingDispositionObserved";
    internal static async ValueTask<PublishStimulusHistoryObservations?> BeginAsync(PostCommitDeliveryContext source, IServiceProvider? services,
        TimeProvider clock, string? relatedRun, CancellationToken cancellationToken)
    {
        if (source.Intent.HistoryCausation is not { } cause) return null;
        try
        {
            if (services is null || !ReferenceEquals(clock, services.GetService<TimeProvider>()))
                throw new NotSupportedException("The captured source receiver or its configured clock is unavailable.");
            if (cause.WorkflowExecutionId != source.Intent.WorkflowExecutionId || cause.DeliveryId != source.SourceOutboxItemId || cause.HistoryEventId is null)
                throw new InvalidDataException("The observed delivery requires its exact stamped source owner.");
            var access = (services.GetService<IPersistenceAccessContextAccessor>() ??
                throw new NotSupportedException("The captured source requires explicit persistence access.")).Current;
            if (access.RequireScope() != source.Scope) throw new InvalidDataException("The observed source changed physical scope.");
            // Read through the actual provider protocol before recording even an attempt. A context alone is
            // self-consistent data, not proof of a physically committed source or its current claim.
            source = await (services.GetService<IStimulusDeliveryStore>() ??
                throw new NotSupportedException("The captured publication requires its physical source provider."))
                .ReadSourceAsync(source, cancellationToken);
            var owner = await (services.GetService<IWorkflowExecutionStateStore>() ??
                throw new NotSupportedException("The captured source requires its native owner reader."))
                .FindAsync(source.Intent.WorkflowExecutionId, cancellationToken);
            if (owner?.HistoryCapturePolicy is not { } policy || !RuntimeHistorySourceFacts.IsFinalProfile(policy))
                throw new InvalidDataException("The stamped source lacks its original captured native owner.");
            access.EnsureTenantScope(owner.TenantId);
            if (!policy.SupportedKinds.Contains(AttemptKind, StringComparer.Ordinal) || !policy.SupportedKinds.Contains(DispositionKind, StringComparer.Ordinal))
                throw new InvalidDataException("The stamped source was not admitted with its required observation kinds.");
            var writer = services.GetService<IRuntimeHistoryObservationWriter>() ??
                throw new NotSupportedException("The captured source observation writer is unavailable.");
            if (services.GetService<IRuntimeHistoryStore>() is not { } reader || !writer.CanRecordHistory(reader))
                throw new NotSupportedException("The selected source observation writer does not own this scoped history.");
            var observation = new PublishStimulusHistoryObservations(source, policy, writer, clock, Guid.NewGuid().ToString("N"), relatedRun);
            await observation.RecordAsync(AttemptKind, "handler-invocation-observed", cancellationToken);
            return observation;
        }
        catch (RuntimePostCommitSourceBoundaryException) { throw; }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (StimulusDeliveryBoundaryException exception)
        { throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, exception, exception.IsUnavailable); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException)
        { throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, exception, false); }
        catch (Exception exception) { throw Unavailable(source, exception); }
    }
    internal ValueTask DispositionAsync(string reason, CancellationToken cancellationToken) => RecordAsync(DispositionKind, reason, cancellationToken);
    private async ValueTask RecordAsync(string name, string reason, CancellationToken cancellationToken)
    {
        var attemptId = "outbox-attempt:" + ((long)source.Claim.Item.DeliveryAttemptCount + 1).ToString(CultureInfo.InvariantCulture) +
            ":fence:" + source.Claim.FencingToken.ToString(CultureInfo.InvariantCulture);
        var immutable = RuntimeHistoryDeliveryFacts.CreateOwnerObservation(policy, source.Intent.WorkflowExecutionId, receiptId,
            name, clock.GetUtcNow(), source.Intent.HistoryCausation!, source.Intent.ActivityExecutionId, relatedRun, reason, attemptId);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (!(await writer.RecordAsync(immutable, cancellationToken)).Recorded)
                    throw new NotSupportedException("The captured source's history head is unavailable.");
                return;
            }
            catch (Exception exception) when (attempt == 0 && (exception is IOException or TimeoutException) && exception is not InvalidDataException)
            { /* An ambiguous write retries the SAME immutable event, without repeating a delivery effect. */ }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is RuntimeHistoryIntegrityException or InvalidDataException or InvalidOperationException or ArgumentException)
            { throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, exception, false); }
            catch (Exception exception) { throw Unavailable(source, exception); }
        }
    }
    private static RuntimePostCommitSourceBoundaryException Unavailable(PostCommitDeliveryContext source, Exception exception) =>
        new(source.SourceOutboxItemId, source.Intent.Kind, exception, true);
}
