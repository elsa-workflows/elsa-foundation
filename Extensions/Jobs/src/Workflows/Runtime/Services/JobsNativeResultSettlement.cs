using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs.Workflows.Runtime.Services;

/// <summary>Confirms canonical Jobs settlement before the native/model source may retire its retained history.</summary>
internal sealed class JobsNativeResultSettlement(IJobQueries queries, JobsResultDeliveryExecution execution, IPersistenceOperationScopeFactory scopes)
    : IJobNativeResultSettlement
{
    public async ValueTask SettleWorkerAsync(WorkflowExecutionState worker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worker);
        try
        {
            await using var scope = await scopes.CreateAsync(new(worker.Partition?.Value ?? throw JobsNativeBoundary.Invalid()), cancellationToken);
            var inspector = scope.ServiceProvider.GetRequiredService<JobsNativeDeliveryInspector>();
            var root = await inspector.FindRootInScopeAsync(worker, cancellationToken);
            if (root is null) return; // A genuine non-RunJob worker contributes no Jobs responsibility.
            if (!root.Metadata.ContainsKey(JobsNativeBoundary.AdmissionMetadataKey))
            {
                if (JobsNativeDeliveryInspector.PreAdmissionFailure(worker, root)) return;
                throw JobsNativeBoundary.Invalid();
            }
            var admission = JobsNativeBoundary.ReadAdmission(root);
            JobsNativeDeliveryInspector.MatchParent(worker, admission.Address);
            await scope.ServiceProvider.GetRequiredService<JobsNativeResultSettlement>().SettleAdmissionAsync(admission, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JobException) { throw; }
        catch (Exception error) { throw Unavailable(error); }
    }

    internal async ValueTask SettleAdmissionAsync(JobNativeAdmission admission, CancellationToken cancellationToken)
    {
        var origin = await queries.FindOriginAsync(admission.Address.Scope, admission.Origin, cancellationToken) ?? throw Unavailable();
        JobsNativeBoundary.EnsureOrigin(admission, origin);
        if (origin.Acceptance is null)
        {
            if (origin.Cancelled && !origin.Retired) return;
            throw Unavailable(); // Retirement or absence is not a never-admitted proof.
        }
        var snapshot = await queries.FindAsync(admission.Address.Scope, origin.Acceptance.JobId, cancellationToken) ?? throw Unavailable();
        if (snapshot.UnavailableDiagnostic is not null || snapshot.Request is null) throw Unavailable();
        if (snapshot.Scope != admission.Address.Scope || snapshot.JobId != origin.Acceptance.JobId ||
            snapshot.RequestSha256 != admission.RequestSha256 || !JobsNativeBoundary.Equivalent(snapshot.Request, admission.Request) ||
            !JobsNativeBoundary.Equivalent(JobsNativeBoundary.ReadAddress(snapshot), admission.Address)) throw JobsNativeBoundary.Invalid();
        if (snapshot.State != JobState.Terminal || snapshot.Result is null) throw Unavailable();
        _ = JobsNativeBoundary.SelectedResult(snapshot);
        var delivery = await queries.FindDeliveryAsync(snapshot.Scope, snapshot.JobId, cancellationToken) ?? throw Unavailable();
        if (Confirmed(admission, snapshot, delivery)) return;
        try
        {
            await execution.ExecuteAsync(snapshot.Scope, new(delivery.JobId, delivery.ResultId, delivery.Association, delivery.Permission), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // A commit followed by acknowledgement loss leaves this same required source owed.
            // An exact read can confirm that committed ACK without repeating a model decision.
            var retained = await queries.FindDeliveryAsync(snapshot.Scope, snapshot.JobId, cancellationToken);
            if (retained is not null && Confirmed(admission, snapshot, retained)) return;
            throw Unavailable(error);
        }
        var confirmed = await queries.FindDeliveryAsync(snapshot.Scope, snapshot.JobId, cancellationToken);
        if (confirmed is null || !Confirmed(admission, snapshot, confirmed)) throw Unavailable();
    }

    internal static bool Confirmed(JobNativeAdmission admission, JobSnapshot snapshot, JobDeliveryStatusSnapshot delivery)
    {
        var result = snapshot.Result ?? throw JobsNativeBoundary.Invalid();
        var association = snapshot.Request?.Association ?? throw JobsNativeBoundary.Invalid();
        var continuationId = JobsNativeBoundary.ContinuationId(admission.Address, result, association);
        if (delivery.JobId != snapshot.JobId || delivery.ResultId != result.ResultId || !JobsNativeBoundary.Equivalent(delivery.Association, association))
            throw JobsNativeBoundary.Invalid();
        if (delivery.Permission is { } permission && (permission.JobId != snapshot.JobId || permission.ResultId != result.ResultId ||
            permission.ContinuationId != continuationId || permission.ModelInputId != JobsNativeBoundary.ModelInputId(admission.Address, result) ||
            !JobsNativeBoundary.Equivalent(permission.Association, association))) throw JobsNativeBoundary.Invalid();
        if (delivery.Acknowledgement is not { } acknowledgement) return false;
        if (acknowledgement.Proof is not { } proof || acknowledgement.ContinuationId != continuationId ||
            !Enum.IsDefined(proof.Kind) || proof.ResultSha256 != result.PayloadSha256 ||
            proof.Kind != JobDeliveryProofKind.ParentProvenTerminal && delivery.Permission is null) throw JobsNativeBoundary.Invalid();
        JobsNativeBoundary.Name(proof.ModelReceiptId);
        return true;
    }

    private static JobException Unavailable(Exception? inner = null) => inner is null
        ? new("jobs.native.settlement-unavailable", "The exact Jobs result settlement remains owed.")
        : new("jobs.native.settlement-unavailable", "The exact Jobs result settlement remains owed.", inner);
}
