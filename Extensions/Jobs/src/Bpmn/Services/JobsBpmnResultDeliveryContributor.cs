using Elsa.Activities.Bpmn.Core;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Bpmn.Services;

/// <summary>Settles Jobs before the existing BPMN result source can release its native history.</summary>
internal sealed class JobsBpmnResultDeliveryContributor(IBpmnNativeWorkConsumer consumer, IJobNativeDeliveryInspector inspector,
    IJobNativeResultSettlement settlement, IJobNativeSubmissionInspector submissions) : IBpmnNativeWorkResultDeliveryContributor
{
    public async ValueTask CompleteAsync(BpmnNativeWorkResultDelivery context, CancellationToken cancellationToken = default)
    {
        try
        {
            var submission = await submissions.InspectSubmissionWorkerAsync(context.Worker, cancellationToken);
            if (submission is not null)
            {
                var address = await consumer.ResolveAsync(context.Origin, cancellationToken);
                JobsBpmnBoundary.RequireSubmissionEvidence(address, submission, context.Result, terminalCleanup: RootTerminal(context));
                if (context.InputReceipt.InputId != context.Origin.ResultInputId || !Enum.IsDefined(context.Disposition)) throw JobsBpmnBoundary.Invalid();
                if (context.Disposition != BpmnNativeWorkInputDisposition.Accepted && !RootTerminal(context)) throw Pending();
                await settlement.SettleWorkerAsync(context.Worker, cancellationToken);
                return;
            }
            var evidence = await inspector.InspectWorkerAsync(context.Worker, cancellationToken);
            if (evidence is not null)
            {
                var address = await consumer.ResolveAsync(context.Origin, cancellationToken);
                JobsBpmnBoundary.RequireEvidence(address, evidence, context.Result);
                if (context.InputReceipt.InputId != context.Origin.ResultInputId ||
                    context.InputReceipt.InputId != evidence.Completion.ModelInputId || !Enum.IsDefined(context.Disposition))
                    throw JobsBpmnBoundary.Invalid();
                if (context.Disposition != BpmnNativeWorkInputDisposition.Accepted && !RootTerminal(context))
                    throw Pending();
            }
            // The native seam authenticates unowned/pre-admission/cancellation phases as well as an exact
            // already-ACKed selected result. Missing or retired-only origin state never releases this source.
            await settlement.SettleWorkerAsync(context.Worker, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (RuntimePostCommitDeliveryException) { throw; }
        catch (Exception error) when (error is JobException or BpmnModelValidationException or RuntimeSchedulerWorkUnavailableException or NotSupportedException)
        { throw Pending(error); }
    }

    private static bool RootTerminal(BpmnNativeWorkResultDelivery context) =>
        (context.Lifecycle is RuntimeModelLifecycle.TerminalPendingSettlement or RuntimeModelLifecycle.TerminalSettled) &&
        (context.TerminalStatus is "Completed" or "Faulted" or "Cancelled") && !string.IsNullOrWhiteSpace(context.TerminalReceiptId);

    private static RuntimePostCommitDeliveryException Pending(Exception? inner = null) =>
        new(PostCommitFailureKind.Transient, "jobs.bpmn.settlement-pending", "The exact Jobs result settlement remains owed.", inner);
}
