using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Primitives.Services;

/// <summary>One actual handler invocation; a retry is another observation, not another business source.</summary>
internal sealed class PublishStimulusHistoryObservations(PostCommitDeliveryContext source, RuntimeHistoryCapturePolicy policy,
    IRuntimeHistoryObservationWriter writer, IPersistenceAccessContextAccessor access, PersistenceAccessContext context,
    TimeProvider clock, string receiptId)
{
    internal const string AttemptKind = "StimulusRoutingAttemptObserved";
    internal const string DispositionKind = "StimulusRoutingDispositionObserved";

    internal static async ValueTask<PublishStimulusHistoryObservations?> BeginAsync(PostCommitDeliveryContext supplied,
        IServiceProvider? services, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (supplied.Intent.HistoryCausation is null) return null;
        try
        {
            if (services is null || !ReferenceEquals(clock, services.GetService<TimeProvider>()))
                throw new NotSupportedException("The captured publication receiver or selected clock is unavailable.");
            var access = services.GetService<IPersistenceAccessContextAccessor>() ?? throw new NotSupportedException("Publication history requires explicit access.");
            var context = access.Current;
            if (context.RequireScope() != supplied.Scope) throw new InvalidDataException("Publication history changed physical scope.");
            // A self-consistent context is not proof of a physical source or a current claim.
            var source = await (services.GetService<IStimulusDeliveryStore>() ?? throw new NotSupportedException("Publication history requires its native source provider."))
                .ReadSourceAsync(supplied, cancellationToken);
            if (source.SourceOutboxItemId != supplied.SourceOutboxItemId || source.Scope != supplied.Scope || source.IntentFingerprint != supplied.IntentFingerprint ||
                source.Claim.OwnerId != supplied.Claim.OwnerId || source.Claim.FencingToken != supplied.Claim.FencingToken)
                throw new InvalidDataException("The verified publication differs from its supplied source identity.");
            EnsureContext(access, context, source.Scope);
            var owner = await (services.GetService<IWorkflowExecutionStateStore>() ?? throw new NotSupportedException("Publication history requires its native owner reader."))
                .FindAsync(source.Intent.WorkflowExecutionId, cancellationToken);
            EnsureContext(access, context, source.Scope);
            if (owner?.HistoryCapturePolicy is not { } policy || !RuntimeHistorySourceFacts.IsFinalProfile(policy))
                throw new InvalidDataException("The stamped publication lacks its original captured native owner.");
            context.EnsureTenantScope(owner.TenantId);
            var writer = services.GetService<IRuntimeHistoryObservationWriter>() ?? throw new NotSupportedException("The publication observation writer is unavailable.");
            if (services.GetService<IRuntimeHistoryStore>() is not { } reader || !writer.CanRecordHistory(reader))
                throw new NotSupportedException("Publication observations require the selected scoped writer/read pair.");
            var observations = new PublishStimulusHistoryObservations(source, policy, writer, access, context, clock, Guid.NewGuid().ToString("N"));
            await observations.RecordAsync(AttemptKind, "handler-invocation-observed", cancellationToken);
            return observations;
        }
        catch (RuntimePostCommitSourceBoundaryException) { throw; }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception failure) { throw Boundary(supplied, failure); }
    }

    internal ValueTask DispositionAsync(string reasonCode, CancellationToken cancellationToken) => RecordAsync(DispositionKind, reasonCode, cancellationToken);

    private async ValueTask RecordAsync(string name, string reasonCode, CancellationToken cancellationToken)
    {
        try
        {
            EnsureContext(access, context, source.Scope);
            var immutable = RuntimeHistorySourceFacts.PublicationObservation(policy, source, receiptId, name, clock.GetUtcNow(), reasonCode);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    EnsureContext(access, context, source.Scope);
                    var result = await writer.RecordAsync(immutable, cancellationToken);
                    EnsureContext(access, context, source.Scope);
                    if (!result.Recorded) throw new NotSupportedException("The publication's history head is unavailable.");
                    return;
                }
                catch (Exception failure) when (attempt == 0 && !cancellationToken.IsCancellationRequested && RetryableWrite(failure))
                { /* Retry the same immutable observation once; never repeat the routing effect here. */ }
            }
        }
        catch (RuntimePostCommitSourceBoundaryException) { throw; }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception failure) { throw Boundary(source, failure); }
    }

    private static void EnsureContext(IPersistenceAccessContextAccessor access, PersistenceAccessContext context, PersistenceScope scope)
    {
        if (access.Current != context || access.Current.RequireScope() != scope)
            throw new InvalidDataException("Persistence access changed during publication observation.");
    }
    internal static RuntimePostCommitSourceBoundaryException Boundary(PostCommitDeliveryContext source, Exception failure) =>
        new(source.SourceOutboxItemId, source.Intent.Kind, failure, IsUnavailable(failure));

    internal static bool IsUnavailable(Exception failure)
    {
        if (failure is RuntimePostCommitSourceBoundaryException source) return source.IsUnavailable;
        if (failure is StimulusDeliveryBoundaryException stimulus) return stimulus.IsUnavailable;
        if (failure is AggregateException aggregate) return aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(IsUnavailable);
        if (Malformed(failure)) return false;
        for (Exception? cause = failure; cause is not null; cause = cause.InnerException)
            if (cause is IOException or TimeoutException or DbException or NotSupportedException or OperationCanceledException or
                RuntimeHistoryUnavailableException or RuntimeSchedulerWorkUnavailableException) return true;
        return false;
    }
    private static bool Malformed(Exception failure) => failure is InvalidDataException or JsonException or ArgumentException or RuntimeHistoryIntegrityException ||
        RuntimeCheckpointCommitValidationException.IsCauseOf(failure) || failure.InnerException is { } cause && Malformed(cause);
    private static bool RetryableWrite(Exception failure)
    {
        if (!IsUnavailable(failure)) return false;
        for (Exception? cause = failure; cause is not null; cause = cause.InnerException)
            if (cause is IOException or TimeoutException or DbException) return true;
        return false;
    }
}
