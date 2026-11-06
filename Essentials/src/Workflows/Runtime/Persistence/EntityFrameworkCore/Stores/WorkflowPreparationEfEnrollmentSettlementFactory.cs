using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

// The actual selected reference writer and configured issuer, never a caller-selected backend/lease.
internal sealed class WorkflowPreparationEfEnrollmentSettlementFactory(
    IWorkflowExecutableSourceReferenceWriter references) : IWorkflowPreparationEfEnrollmentSettlementFactory
{
    public IWorkflowPreparationEfEnrollmentSettlement Bind(IWorkflowPreparationOwnerIssuer configuredIssuer)
    {
        ArgumentNullException.ThrowIfNull(configuredIssuer);
        return new Bound(references as IWorkflowPreparationEfEnrollmentParticipant, configuredIssuer);
    }

    private sealed class Bound(IWorkflowPreparationEfEnrollmentParticipant? participant,
        IWorkflowPreparationOwnerIssuer issuer) : IWorkflowPreparationEfEnrollmentSettlement
    {
        public ValueTask<WorkflowPreparationEfEnrollmentResult> BindTargetAsync(
            WorkflowPreparationOwnerStamp expectedOwner, CancellationToken cancellationToken = default) =>
            InvokeAsync(expectedOwner, bindTarget: true, cancellationToken);

        public ValueTask<WorkflowPreparationEfEnrollmentResult> SettleAsync(
            WorkflowPreparationOwnerStamp expectedOwner, CancellationToken cancellationToken = default) =>
            InvokeAsync(expectedOwner, bindTarget: false, cancellationToken);

        private async ValueTask<WorkflowPreparationEfEnrollmentResult> InvokeAsync(
            WorkflowPreparationOwnerStamp expectedOwner, bool bindTarget, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(expectedOwner);
            cancellationToken.ThrowIfCancellationRequested();
            if (participant?.CanSettleEnrollment(issuer) != true)
                return WorkflowPreparationEfEnrollmentResult.Unsupported();
            WorkflowPreparationEfEnrollmentResult? result = null;
            await WorkflowPreparationOwnerInvocation.ExecuteAsync(issuer, participant, expectedOwner,
                async (owner, token) =>
                {
                    result = bindTarget
                        ? await participant.BindEnrollmentTargetAsync(owner, token)
                        : await participant.SettleEnrollmentAsync(owner, token);
                    owner.EnsureActive();
                    ValidateResult(result, expectedOwner, bindTarget);
                }, cancellationToken);
            return result ?? throw new WorkflowPreparationException("enrollment-no-owner-result", expectedOwner.Id);
        }

        private static void ValidateResult(WorkflowPreparationEfEnrollmentResult? result,
            WorkflowPreparationOwnerStamp owner, bool bindTarget)
        {
            var valid = result is not null && (result.Status switch
            {
                WorkflowPreparationEfEnrollmentStatus.Bound => bindTarget &&
                    result.Binding?.Owner == owner && result.Header is null && result.BarrierIncarnation is null,
                WorkflowPreparationEfEnrollmentStatus.Aborted => !bindTarget && result.Binding?.Owner == owner &&
                    !string.IsNullOrWhiteSpace(result.BarrierIncarnation) && result.Header is null,
                WorkflowPreparationEfEnrollmentStatus.Accepted => !bindTarget && result.Header?.Id == owner.Id &&
                    (result.Binding is null || result.Binding.Owner == owner),
                WorkflowPreparationEfEnrollmentStatus.Pending or WorkflowPreparationEfEnrollmentStatus.Conflict or
                    WorkflowPreparationEfEnrollmentStatus.Unsupported => result.Binding is null &&
                    result.Header is null && result.BarrierIncarnation is null,
                _ => false
            });
            if (!valid) throw new WorkflowPreparationException("enrollment-invalid-provider-result", owner.Id);
            if (result!.Binding is { } binding) WorkflowPreparationEnrollmentCodec.ValidateBinding(binding);
            if (result.Header is { } header)
            {
                // Shape/material checks supplement the selected native participant's commit proof.
                _ = WorkflowPreparationRetentionCodec.Create(header, RuntimeArtifactEfModule.PreparationSchemaVersion);
                WorkflowPreparationEnrollmentCodec.RequireMatchingHeader(owner, header);
            }
        }
    }
}
