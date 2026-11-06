using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Services;

/// <summary>A direct-native required source; model workers settle through their existing model-result source instead.</summary>
internal sealed class JobsNativeSettlementIntentHandler(JobsNativeResultSettlement settlement, IPersistenceAccessContextAccessor access,
    JobsNativeSourceVerifier sourceVerifier) : IRuntimePostCommitDurableSourceIntentHandler
{
    internal static JobNativeAdmission Read(RuntimePostCommitIntent intent)
    {
        if (intent.Kind != JobsNativeBoundary.SettlementKind || intent.Payload is not { } payload) throw JobsNativeBoundary.Invalid();
        var native = JobsNativeBoundary.Read<JobNativeIntent>(payload);
        var admission = native.Admission;
        if (native.FormatVersion != 1 || admission is null || admission.Invocation is null || admission.Address is null || admission.Address.Owner is null ||
            admission.Address.Owner.Origin is not null ||
            !JobsNativeBoundary.Equivalent(admission, JobsNativeBoundary.Admission(admission.Invocation, admission.Address, admission.CommittedAt)) ||
            !intent.IsEquivalentTo(JobsNativeBoundary.SettlementIntent(admission))) throw JobsNativeBoundary.Invalid();
        return admission;
    }

    public async ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        await sourceVerifier.VerifyAsync(source, cancellationToken);
        await HandleIntentAsync(source.Intent, cancellationToken);
    }

    internal async ValueTask HandleIntentAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        JobNativeAdmission admission;
        try { admission = Read(intent); }
        catch (Exception error) when (error is JobException or ArgumentException)
        {
            throw new RuntimePostCommitDeliveryException(PostCommitFailureKind.Permanent, "jobs.native.intent-invalid",
                "The retained Jobs settlement source is invalid.", error);
        }
        try
        {
            if (access.Current.RequireScope().Value != admission.Address.Scope.PersistencePartition) throw JobsNativeBoundary.Invalid();
            await settlement.SettleAdmissionAsync(admission, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // Durable responsibility is retained even when validation cannot currently prove it.
            throw new RuntimePostCommitDeliveryException(PostCommitFailureKind.Transient, "jobs.native.settlement-unavailable",
                "The exact Jobs result settlement remains owed.", error);
        }
    }
}
