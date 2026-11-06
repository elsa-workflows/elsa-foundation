using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Jobs.Workflows.Runtime.Services;

/// <summary>Reads canonical accepted content and an existing grant; it never creates a permission during native validation.</summary>
internal sealed class JobsNativeResultValidator(IJobQueries jobs)
{
    internal async ValueTask<JobResult> ValidateAsync(JobNativeAdmission admission, RunJobCompletion completion, CancellationToken cancellationToken)
    {
        if (completion is null || completion.FormatVersion != 1 || completion.RequestSha256 != admission.RequestSha256) throw JobsNativeBoundary.Invalid();
        foreach (var value in new[] { completion.JobId, completion.ResultId, completion.ContinuationId, completion.ModelInputId }) JobsNativeBoundary.Name(value);
        var suppliedResult = JobsNativeBoundary.Read<JobResult>(completion.Result);
        var suppliedPermission = JobsNativeBoundary.Read<JobContinuationPermission>(completion.Permission);
        var snapshot = await ReadAsync(() => jobs.FindAsync(admission.Address.Scope, completion.JobId, cancellationToken), cancellationToken);
        if (snapshot is null || snapshot.UnavailableDiagnostic is not null) throw Unavailable();
        var metadata = JobsNativeBoundary.RetainedRequest(snapshot);
        var retained = JobsNativeBoundary.RetainedResult(snapshot);
        if (snapshot.InputPayloadState == JobPayloadState.Expired || snapshot.ResultPayloadState == JobPayloadState.Expired)
        {
            var settled = await ReadAsync(() => jobs.FindDeliveryAsync(admission.Address.Scope, completion.JobId, cancellationToken), cancellationToken);
            if (settled?.Acknowledgement is not { } ack || settled.ResultId != retained.ResultId ||
                ack.ContinuationId != completion.ContinuationId || ack.Proof.ResultSha256 != retained.PayloadSha256 ||
                !Enum.IsDefined(ack.Proof.Kind) || string.IsNullOrWhiteSpace(ack.Proof.ModelReceiptId)) throw Unavailable();
        }
        // An already-settled native source retains its own original completion bytes. Compact canonical metadata
        // authenticates that existing source after store expiry; no successful null payload is reconstructed.
        var result = snapshot.Result ?? suppliedResult;
        if (snapshot.Scope != admission.Address.Scope || snapshot.JobId != completion.JobId || snapshot.State != JobState.Terminal ||
            snapshot.RequestSha256 != admission.RequestSha256 || !JobsNativeBoundary.Equivalent(metadata, JobRequestMetadata.From(admission.Request)) ||
            !JobsNativeBoundary.Equivalent(retained, JobResultMetadata.From(result)) || result.JobId != completion.JobId || result.ResultId != completion.ResultId ||
            !JobsNativeBoundary.Equivalent(result, suppliedResult) || result.PayloadSha256 != JobPayloadDigest.Compute(result.Payload) ||
            !Enum.IsDefined(result.Outcome) || result.Outcome == JobOutcome.Succeeded && (result.Payload is null || result.Payload.Schema != admission.Request.ResultSchema) ||
            completion.ModelInputId != JobsNativeBoundary.ModelInputId(admission.Address, result) ||
            completion.ContinuationId != JobsNativeBoundary.ContinuationId(admission.Address, result, admission.Request.Association!)) throw JobsNativeBoundary.Invalid();
        var permission = await ReadAsync(() => jobs.FindContinuationAsync(admission.Address.Scope, completion.JobId,
            completion.ContinuationId, cancellationToken), cancellationToken) ?? throw Unavailable();
        if (permission.JobId != completion.JobId || permission.ResultId != completion.ResultId || permission.ContinuationId != completion.ContinuationId ||
            permission.ModelInputId != completion.ModelInputId || !JobsNativeBoundary.Equivalent(permission.Association, admission.Request.Association) ||
            !JobsNativeBoundary.Equivalent(permission, suppliedPermission)) throw JobsNativeBoundary.Invalid();
        return result;
    }

    private static async ValueTask<T> ReadAsync<T>(Func<ValueTask<T>> read, CancellationToken cancellationToken)
    {
        try { return await read(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { throw Unavailable(error); }
    }

    private static RuntimeSchedulerWorkUnavailableException Unavailable(Exception? error = null) =>
        new("jobs.native.result-unavailable", "The canonical Jobs result and existing continuation grant are not currently available.", error);
}
