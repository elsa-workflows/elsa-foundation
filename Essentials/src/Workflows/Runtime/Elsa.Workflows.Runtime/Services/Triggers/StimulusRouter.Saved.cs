using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

public sealed partial class StimulusRouter
{
    async ValueTask<StimulusRoutingResult> IStimulusRouter.RouteSourceAsync(PostCommitDeliveryContext supplied, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplied); cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var delivery = _delivery ?? throw new NotSupportedException("This router was constructed without canonical source support.");
            delivery.RequireBoundary(supplied);
            if (!_startDispatcher.CanDispatchSavedStimulus(delivery.Sources, delivery.Material, delivery.Executions) ||
                !_resumeDispatcher.CanDispatchSavedStimulus(delivery.Sources, delivery.Material, delivery.Executions))
                throw new NotSupportedException("Both selected dispatchers must support this actual source/material/execution boundary.");
            var source = await delivery.Sources.ReadSourceAsync(supplied, cancellationToken);
            var request = delivery.Request(source);
            if (source.Claim.Item.RecipientSnapshot is null)
            {
                var candidate = await delivery.SelectAsync(source, request, _globalBookmarkStimulusLookup, _triggerBindingStore, cancellationToken);
                _ = await delivery.Sources.InstallAsync(source, candidate, cancellationToken);
                // Read the actual winner, including a competing proposal; an install return value alone cannot
                // authorize effects or substitute a loser when a provider acknowledgment was ambiguous.
                source = await delivery.Sources.ReadSourceAsync(source, cancellationToken);
            }
            StimulusDeliveryServices.ValidateWinner(source, request);
            var winner = source.Claim.Item.RecipientSnapshot!;
            var metadata = request.BuildDispatchMetadata(); var starts = new List<StimulusStartOutcome>();
            foreach (var recipient in winner.Starts)
            {
                source = await delivery.Sources.ReadSourceAsync(source, cancellationToken);
                StimulusDeliveryServices.ValidateWinner(source, request);
                var binding = recipient.Binding; var key = KeyedWorkflowStartIdentity.For(request.IdempotencyKey!, recipient.Executable.ArtifactId);
                var start = new WorkflowExecutionStartDispatchRequest(recipient.Executable.ArtifactId, request.RequestedBy,
                    key.WorkflowExecutionId, key.StartKey, metadata, null, null, request.Input, binding.ExecutableNodeId,
                    WorkflowRunKind.PublishedRun, null, WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy,
                    null, null, winner.Scope.TenantId, winner.Scope.Partition, null, WorkflowExecutableStartAuthority.FromStimulusDelivery(source, binding.TriggerBindingId),
                    triggerMetadata: binding.Metadata);
                var result = await _startDispatcher.DispatchAsync(start, cancellationToken: cancellationToken);
                if (!result.CommandDispatch.IsShed && result.CommandDispatch.Status is not
                    (WorkflowExecutionCommandDispatchStatus.Accepted or WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted or WorkflowExecutionCommandDispatchStatus.Duplicate))
                    throw new IOException("A saved start did not accept its queued responsibility.");
                starts.Add(result.CommandDispatch.IsShed
                    ? StimulusStartOutcome.Shed(binding.TriggerBindingId, binding.ArtifactId, result.CommandDispatch.ShedRetryAfter)
                    : result.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Duplicate
                        ? StimulusStartOutcome.SkippedDuplicate(binding.TriggerBindingId, binding.ArtifactId)
                        : StimulusStartOutcome.Started(binding.TriggerBindingId, binding.ArtifactId, result.WorkflowExecutionId));
            }
            var resumes = new List<StimulusResumeOutcome>();
            foreach (var recipient in winner.Resumes)
            {
                source = await delivery.Sources.ReadSourceAsync(source, cancellationToken);
                StimulusDeliveryServices.ValidateWinner(source, request);
                var result = await _resumeDispatcher.DispatchAsync(source, recipient.WorkflowExecutionId, recipient.BookmarkId, cancellationToken);
                if (result.Status is not (BookmarkResumeDispatchStatus.Dispatched or BookmarkResumeDispatchStatus.Duplicate or BookmarkResumeDispatchStatus.NotFound))
                    throw new IOException("A saved resume did not accept its queued responsibility.");
                resumes.Add(new(recipient.WorkflowExecutionId, result.Status, result.Reason));
            }
            return new(starts, resumes);
        }
        catch (StimulusDeliveryBoundaryException) { throw; }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            throw new StimulusDeliveryBoundaryException(supplied.SourceOutboxItemId, exception, StimulusDeliveryFailures.IsUnavailable(exception));
        }
    }
}
