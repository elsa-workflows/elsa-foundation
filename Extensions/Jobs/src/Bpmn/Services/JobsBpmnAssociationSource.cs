using Elsa.Activities.Bpmn.Core;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Bpmn.Services;

internal sealed class JobsBpmnAssociationSource(IBpmnNativeWorkConsumer consumer) : IJobWorkflowAssociationSource
{
    public bool Supports(RuntimeModelNativeWorkOrigin origin) =>
        origin.Protocol == new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version);

    public async ValueTask<JobWorkflowOwner> ResolveAsync(RuntimeModelNativeWorkOrigin origin, CancellationToken cancellationToken = default)
    {
        try { return JobsBpmnBoundary.Owner(await consumer.ResolveAsync(origin, cancellationToken)); }
        catch (BpmnModelValidationException error) { throw Invalid(error); }
        catch (RuntimeSchedulerWorkUnavailableException error) { throw Unavailable(error); }
        catch (NotSupportedException error) { throw Unavailable(error); }
    }

    public async ValueTask<JobWorkflowOwnerObservation> InspectAsync(JobWorkflowOwner owner, JobNativeDeliveryEvidence? evidence,
        CancellationToken cancellationToken = default)
    {
        if (owner.Origin is not { } origin || !Supports(origin)) throw JobsBpmnBoundary.Invalid();
        try
        {
            var observation = await consumer.InspectAsync(origin, cancellationToken);
            JobsBpmnBoundary.RequireOwner(owner, observation.Address);
            if (!Enum.IsDefined(observation.Kind) || observation.Kind == BpmnNativeWorkObservationKind.RootTerminal && observation.TerminalReceiptId is null)
                throw JobsBpmnBoundary.Invalid();
            if (observation.TerminalReceiptId is { } terminalReceipt)
            {
                if (observation.Lifecycle is not (RuntimeModelLifecycle.TerminalPendingSettlement or RuntimeModelLifecycle.TerminalSettled) ||
                    observation.TerminalStatus is not ("Completed" or "Faulted" or "Cancelled") ||
                    string.IsNullOrWhiteSpace(terminalReceipt)) throw JobsBpmnBoundary.Invalid();
                return new(JobWorkflowOwnerDisposition.ProvenTerminal, terminalReceipt, evidence?.Result.ResultId, evidence?.Result.PayloadSha256);
            }
            if (evidence is null || observation.Kind == BpmnNativeWorkObservationKind.NotConsumed)
                return new(JobWorkflowOwnerDisposition.NotConsumed, null, null, null);
            if (observation.Kind != BpmnNativeWorkObservationKind.AcceptedRootInput || observation.AcceptedInput is not { } input ||
                input.InputId != origin.ResultInputId || string.IsNullOrWhiteSpace(input.ContentFingerprint) ||
                string.IsNullOrWhiteSpace(observation.ModelReceiptId) || input.AcceptedRevision > observation.ModelRevision)
                throw JobsBpmnBoundary.Invalid();
            JobsBpmnBoundary.RequireEvidence(observation.Address, evidence, input.Result);
            return new(JobWorkflowOwnerDisposition.AcceptedInput, observation.ModelReceiptId, evidence.Result.ResultId, evidence.Result.PayloadSha256);
        }
        catch (BpmnModelValidationException error) { throw Invalid(error); }
        catch (RuntimeSchedulerWorkUnavailableException error) { throw Unavailable(error); }
        catch (NotSupportedException error) { throw Unavailable(error); }
    }

    private static JobException Invalid(Exception inner) => new("jobs.bpmn.invalid", "The retained BPMN owner or result proof is invalid.", inner);
    private static JobException Unavailable(Exception inner) => new("jobs.bpmn.unavailable", "The exact BPMN owner or result proof is temporarily unavailable.", inner);
}
