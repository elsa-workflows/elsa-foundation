using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs.Workflows.Runtime.Services;

/// <summary>Reconciles Jobs-owned responsibility; transport status never acknowledges model consumption.</summary>
internal sealed class JobsResultDeliveryExecution(IJobCommands commands, IJobQueries queries, IPersistenceOperationScopeFactory scopes)
{
    internal async ValueTask<bool> ExecuteAsync(JobScope scope, JobDeliverySnapshot pending, CancellationToken cancellationToken)
    {
        var snapshot = await queries.FindAsync(scope, pending.JobId, cancellationToken);
        if (snapshot?.Request?.Association?.Continuation is not { Schema: { Alias: JobsNativeBoundary.ContinuationSchema, Version: 1 } }) return false;
        var result = JobsNativeBoundary.SelectedResult(snapshot);
        if (snapshot.Scope != scope || snapshot.JobId != pending.JobId ||
            snapshot.State != JobState.Terminal || snapshot.UnavailableDiagnostic is not null || result.ResultId != pending.ResultId ||
            result.JobId != snapshot.JobId || !JobsNativeBoundary.Equivalent(snapshot.Request.Association, pending.Association) ||
            result.PayloadSha256 != JobPayloadDigest.Compute(result.Payload)) throw JobsNativeBoundary.Invalid();
        var address = JobsNativeBoundary.ReadAddress(snapshot);
        var continuationId = JobsNativeBoundary.ContinuationId(address, result, pending.Association);
        var prior = await queries.FindContinuationAsync(scope, result.JobId, continuationId, cancellationToken);
        var previousCompletion = prior is null ? null : Completion(prior);
        var before = await InspectAsync(snapshot, address, previousCompletion, cancellationToken);
        if (before.Proof is { } terminal) return await AcknowledgeAsync(scope, result.JobId, continuationId, terminal, cancellationToken);
        if (prior is null && before.Bookmark is null) return false;
        var granted = prior is not null ? new JobContinuationResult(JobCommandStatus.Replayed, prior)
            : await commands.GrantContinuationAsync(new(scope, result.JobId, result.ResultId,
                continuationId, JobsNativeBoundary.ModelInputId(address, result)), cancellationToken);
        if (granted.Status is not (JobCommandStatus.Applied or JobCommandStatus.Replayed)) return false;
        var permission = granted.Permission ?? throw JobsNativeBoundary.Invalid();
        var retained = await queries.FindContinuationAsync(scope, result.JobId, continuationId, cancellationToken);
        if (retained is null || !JobsNativeBoundary.Equivalent(retained, permission) || permission.JobId != result.JobId ||
            permission.ResultId != result.ResultId || permission.ContinuationId != continuationId ||
            permission.ModelInputId != JobsNativeBoundary.ModelInputId(address, result) ||
            !JobsNativeBoundary.Equivalent(permission.Association, pending.Association)) throw JobsNativeBoundary.Invalid();
        var completion = Completion(permission);
        var inspection = await InspectAsync(snapshot, address, completion, cancellationToken);
        if (inspection.Proof is null && inspection.Bookmark is not null)
        {
            await using var dispatchScope = await scopes.CreateAsync(new(scope.PersistencePartition), cancellationToken);
            var response = await dispatchScope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(
                new BookmarkResumeDispatchRequest(address.WorkflowExecutionId, address.StimulusType, address.StimulusHash,
                    JsonSerializer.SerializeToElement(completion), JobsNativeBoundary.DeliveryId(address, completion), "jobs.native-delivery", null,
                    JobsNativeBoundary.CompletionType, JobsNativeBoundary.ProviderId, address.BookmarkId, null), cancellationToken: cancellationToken);
            if (response.WorkflowExecutionId != address.WorkflowExecutionId) throw JobsNativeBoundary.Invalid();
            // A new scope reads durable consumption after dispatch; Dispatched/Duplicate/NotFound are not proof.
            inspection = await InspectAsync(snapshot, address, completion, cancellationToken);
        }
        return inspection.Proof is { } proof && await AcknowledgeAsync(scope, result.JobId, continuationId, proof, cancellationToken);

        RunJobCompletion Completion(JobContinuationPermission permission)
        {
            if (permission.JobId != result.JobId || permission.ResultId != result.ResultId || permission.ContinuationId != continuationId ||
                permission.ModelInputId != JobsNativeBoundary.ModelInputId(address, result) ||
                !JobsNativeBoundary.Equivalent(permission.Association, pending.Association)) throw JobsNativeBoundary.Invalid();
            return new(1, result.JobId, snapshot.RequestSha256, result.ResultId, continuationId,
                permission.ModelInputId, JobsNativeBoundary.Write(result), JobsNativeBoundary.Write(permission));
        }
    }

    private async ValueTask<JobsNativeInspection> InspectAsync(JobSnapshot snapshot, JobNativeAddress address,
        RunJobCompletion? completion, CancellationToken cancellationToken)
    {
        await using var scope = await scopes.CreateAsync(new(snapshot.Scope.PersistencePartition), cancellationToken);
        return await scope.ServiceProvider.GetRequiredService<JobsNativeDeliveryInspector>().InspectAsync(snapshot, address, completion, cancellationToken);
    }

    private async ValueTask<bool> AcknowledgeAsync(JobScope scope, string jobId, string continuationId, JobDeliveryProof proof, CancellationToken cancellationToken)
    {
        var confirmed = await commands.AcknowledgeDeliveryAsync(new(scope, jobId, continuationId, proof), cancellationToken);
        return confirmed.Status is JobCommandStatus.Applied or JobCommandStatus.Replayed;
    }
}
