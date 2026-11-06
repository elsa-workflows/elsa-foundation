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
        if (snapshot?.Request is null || snapshot.UnavailableDiagnostic is not null || snapshot.Result is null) throw Unavailable();
        var result = snapshot.Result;
        if (snapshot.Scope != admission.Address.Scope || snapshot.JobId != completion.JobId || snapshot.State != JobState.Terminal ||
            snapshot.RequestSha256 != admission.RequestSha256 || JobRequestDigest.Compute(snapshot.Request) != admission.RequestSha256 ||
            !JobsNativeBoundary.Equivalent(snapshot.Request, admission.Request) || result.JobId != completion.JobId || result.ResultId != completion.ResultId ||
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
