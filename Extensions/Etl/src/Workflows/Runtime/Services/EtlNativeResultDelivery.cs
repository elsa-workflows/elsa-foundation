using System.Text.Json;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Workflows.Runtime.Services;

/// <summary>Transports the ETL-owned partition into the ordinary native persistence scope.</summary>
internal sealed class EtlNativeResultDelivery(IPersistenceOperationScopeFactory scopes) : IEtlResultDelivery
{
    public async ValueTask<EtlDeliveryDisposition?> DeliverAsync(EtlDeliveryLease delivery, CancellationToken cancellationToken = default)
    {
        await using var scope = await scopes.CreateAsync(new(delivery.Scope.PersistencePartition), cancellationToken);
        return await scope.ServiceProvider.GetRequiredService<EtlNativeResultExecutor>().DeliverAsync(delivery, cancellationToken);
    }
}

internal sealed class EtlNativeResultExecutor(IEtlRuntimeStore runs, IWorkflowExecutionStateStore workflows,
    IActivityExecutionStateStore activities, IBookmarkStateStore bookmarks, IBookmarkResumeDispatcher dispatcher,
    IPersistenceAccessContextAccessor access)
{
    public async ValueTask<EtlDeliveryDisposition?> DeliverAsync(EtlDeliveryLease delivery, CancellationToken cancellationToken = default)
    {
        access.Current.EnsureScope(new(delivery.Scope.PersistencePartition));
        var run = await runs.FindRunAsync(delivery.Scope, delivery.RunId, cancellationToken) ?? throw EtlNativeBoundary.Invalid();
        var invocation = run.Invocation;
        EtlNativeBoundary.EnsureInvocation(invocation);
        if (run.TerminalResult != delivery.Result || invocation.ResultAddress != delivery.Address ||
            invocation.Scope != delivery.Scope || invocation.RunId != delivery.RunId ||
            EtlRuntimeJson.ResultEnvelopeHash(EtlRuntimeJson.Write(invocation), EtlRuntimeJson.Write(delivery.Result)) != delivery.PayloadHash)
            throw EtlNativeBoundary.Invalid();

        var before = await ProofAsync(invocation, cancellationToken);
        if (before.Disposition is not null || !before.CanDispatch)
            return before.Disposition;
        var address = invocation.ResultAddress;
        var payload = new RunEtlCompletion(EtlNativeBoundary.FormatVersion, EtlRuntimeJson.WriteElement(invocation),
            EtlRuntimeJson.WriteElement(delivery.Result));
        var dispatched = await dispatcher.DispatchAsync(new BookmarkResumeDispatchRequest(
            address.WorkflowExecutionId, address.StimulusType, address.StimulusHash,
            JsonSerializer.SerializeToElement(payload), delivery.DeliveryId, address.ProviderId, null,
            EtlNativeBoundary.CompletionType, address.ProviderId, address.BookmarkId, null), cancellationToken: cancellationToken);
        if (dispatched.WorkflowExecutionId != address.WorkflowExecutionId)
            throw EtlNativeBoundary.Invalid();
        if (dispatched.Bookmark is { } resolved)
            EtlNativeBoundary.EnsureBookmark(address, resolved);
        if (dispatched.Resolution is { } resolution)
        {
            EtlNativeBoundary.EnsureBookmark(address, resolution.Bookmark);
            if (resolution.ExecutableNode.ExecutableNodeId != address.NodeId || resolution.ResumeTarget.ResumeTargetId != address.ResumeTargetId ||
                resolution.ResumeTarget.ExecutableNodeId != address.NodeId)
                throw EtlNativeBoundary.Invalid();
        }
        // No mailbox-dispatch status, including Duplicate or NotFound, is an acknowledgement.
        var after = await ProofAsync(invocation, cancellationToken);
        return after.Disposition == EtlDeliveryDisposition.AlreadyConsumed ? EtlDeliveryDisposition.Consumed : after.Disposition;
    }

    private async ValueTask<DeliveryProof> ProofAsync(EtlInvocation invocation, CancellationToken cancellationToken)
    {
        var address = invocation.ResultAddress;
        var parent = await workflows.FindAsync(address.WorkflowExecutionId, cancellationToken);
        if (parent is null)
            return new(null, false);
        if (parent.WorkflowExecutionId != address.WorkflowExecutionId || parent.PinnedExecutable.ArtifactId != address.ExecutableId ||
            parent.PinnedExecutable.ArtifactHash != address.ExecutableHash || parent.TenantId != invocation.Scope.TenantId ||
            parent.Partition?.Value != invocation.Scope.PersistencePartition)
            throw EtlNativeBoundary.Invalid();
        var bookmark = await bookmarks.FindAsync(address.WorkflowExecutionId, address.BookmarkId, cancellationToken);
        if (bookmark is not null)
            EtlNativeBoundary.EnsureBookmark(address, bookmark);
        if (parent.Status.IsTerminal())
            return new(EtlDeliveryDisposition.ProvenParentTerminal, false);
        var activity = await activities.FindAsync(address.WorkflowExecutionId, address.ActivityExecutionId, cancellationToken);
        if (activity is null)
            return new(null, false);
        if (activity.Execution.WorkflowExecutionId != address.WorkflowExecutionId || activity.InvocationId != address.ActivityExecutionId ||
            activity.Execution.ExecutableNodeId != address.NodeId || activity.Execution.ActivityType != RunEtl.ActivityType)
            throw EtlNativeBoundary.Invalid();
        if (bookmark is null)
        {
            if (activity.TriggerDeliveries?.Any(receipt => receipt.Status == ActivityTriggerDeliveryStatus.Consumed &&
                    receipt.RegistrationId == address.RegistrationId && receipt.ProviderId == address.ProviderId &&
                    receipt.PayloadType == EtlNativeBoundary.CompletionType) == true)
                return new(EtlDeliveryDisposition.AlreadyConsumed, false);
            return activity.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled
                ? new(EtlDeliveryDisposition.ProvenParentTerminal, false)
                : new(null, false);
        }
        if (EtlRuntimeIdentity.InvocationFingerprint(EtlNativeBoundary.Invocation(activity)) != EtlRuntimeIdentity.InvocationFingerprint(invocation))
            throw EtlNativeBoundary.Invalid();
        return new(null, true);
    }

    private sealed record DeliveryProof(EtlDeliveryDisposition? Disposition, bool CanDispatch);
}
