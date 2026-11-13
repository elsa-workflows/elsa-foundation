using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Saved ordinary waits use the existing actor mailbox and durable resume scheduler.</summary>
internal sealed class NativeIngressWaitTarget(IWorkflowExecutionActorProvider actors, IRuntimeIngressDeliveryStore ingress,
    NativeIngressWaitTargetEvidence evidence, IPersistenceAccessContextAccessor access) : IRuntimeIngressWaitTarget
{
    public ValueTask<RuntimeIngressWaitDispatchSeal> PrepareDispatchAsync(RuntimePostCommitOutboxItem source,
        RuntimeIngressWaitRecipient recipient, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            access.Current.EnsureScope(recipient.Identity.Scope);
            var capture = recipient.Expected.RegistrationCapture!;
            if ((capture.Scope.Partition?.Value ?? WorkflowExecutionPartition.DefaultValue) != recipient.Identity.Scope.Value)
                throw new TriggerOperationException("unavailable", "The ordinary resume actor cannot target the accepted physical wait scope.");
            return ValueTask.FromResult(RuntimeIngressWaitCommand.CreateSeal(source, recipient));
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw new TriggerOperationException("conflict", "The wait delivery authority is invalid.", innerException: exception);
        }
    }
    public async ValueTask<RuntimeIngressWaitReceipt?> ReconcileAsync(RuntimePostCommitOutboxItem source,
        RuntimeIngressWaitRecipient recipient, RuntimeIngressWaitDispatchSeal seal, CancellationToken cancellationToken = default)
    {
        try
        {
            var actual = await ReadAsync(source, recipient, seal, cancellationToken);
            return await evidence.ReconcileAsync(actual.Item, actual.Recipient, actual.Seal, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw new TriggerOperationException("conflict", "The wait delivery authority is invalid.", innerException: exception);
        }
    }
    public async ValueTask DispatchAsync(RuntimePostCommitOutboxItem source, RuntimeIngressWaitRecipient recipient,
        RuntimeIngressWaitDispatchSeal seal, CancellationToken cancellationToken = default)
    {
        try
        {
            var actual = await ReadAsync(source, recipient, seal, cancellationToken);
            if (await evidence.ReconcileAsync(actual.Item, actual.Recipient, actual.Seal, cancellationToken) is not null) return;
            var envelope = RuntimeIngressWaitCommand.ReadEnvelope(actual.Seal);
            var actor = await actors.GetAgentAsync(new(envelope.WorkflowExecutionId, WorkflowExecutionActorActivationReason.ResumeBookmark,
                envelope.EnqueuedAt, requestedBy: "runtime.ingress.wait", requiredCapabilities: WorkflowExecutionActorCapabilities.None,
                metadata: envelope.Metadata, partition: envelope.Partition), cancellationToken);
            // Default options deliberately carry no original HTTP request services or response sink.
            var result = await actor.EnqueueAsync(envelope, WorkflowExecutionCommandDispatchOptions.Default, cancellationToken);
            if (result.Status == WorkflowExecutionCommandDispatchStatus.Rejected || result.IsShed)
                throw new TriggerOperationException("unavailable", "The ordinary target has not accepted its committed wait resume.");
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw new TriggerOperationException("conflict", "The wait delivery authority is invalid.", innerException: exception);
        }
    }
    private async ValueTask<RuntimeCommittedIngressWait> ReadAsync(RuntimePostCommitOutboxItem source,
        RuntimeIngressWaitRecipient recipient, RuntimeIngressWaitDispatchSeal seal, CancellationToken cancellationToken)
    {
        var selection = RuntimeIngressWaitCommand.RequireSelection(source, recipient);
        var actual = await ingress.ReadCommittedWaitAsync(selection.Origin(recipient), cancellationToken);
        if (!actual.Seal.IsEquivalentTo(seal)) throw new TriggerOperationException("conflict", "The supplied wait seal differs from its physical source.");
        RuntimeIngressWaitCommand.ValidateEnvelope(actual, RuntimeIngressWaitCommand.ReadEnvelope(actual.Seal));
        return actual;
    }
}
