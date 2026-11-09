using Elsa.Etl.Core.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Etl.Workflows.Runtime.Services;

internal sealed class EtlIntentHandler(IEtlRunAdmissionService admission, IEtlRuntimeStore runs,
    IWorkflowExecutionStateStore workflows, IActivityExecutionStateStore activities,
    IBookmarkStateStore bookmarks, IPersistenceAccessContextAccessor access) : IRuntimePostCommitIntentHandler
{
    public async ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        if (intent.Kind is not (EtlNativeBoundary.StartKind or EtlNativeBoundary.CancelKind) || intent.Payload is not { } payload)
            throw EtlNativeBoundary.Invalid();
        var invocation = EtlRuntimeJson.Read<EtlInvocation>(payload, EtlNativeBoundary.MaximumRequestBytes);
        EtlNativeBoundary.EnsureInvocation(invocation);
        access.Current.EnsureScope(new(invocation.Scope.PersistencePartition));
        var address = invocation.ResultAddress;
        if (intent.WorkflowExecutionId != address.WorkflowExecutionId || intent.ActivityExecutionId != address.ActivityExecutionId ||
            !intent.IsEquivalentTo(EtlNativeBoundary.Intent(invocation, intent.Kind == EtlNativeBoundary.CancelKind, intent.RecordedAt)))
            throw EtlNativeBoundary.Invalid();

        if (intent.Kind == EtlNativeBoundary.CancelKind)
        {
            EnsureSettled(await runs.CancelAsync(invocation, cancellationToken));
            return;
        }

        // The admission acknowledgement can be lost while the run and native continuation advance.
        // Its immutable ETL record proves this exact responsibility was accepted, even after wait/history cleanup.
        var admitted = await runs.FindRunAsync(invocation.Scope, invocation.RunId, cancellationToken);
        if (admitted is not null)
        {
            if (EtlRuntimeIdentity.InvocationFingerprint(admitted.Invocation) != EtlRuntimeIdentity.InvocationFingerprint(invocation))
                throw EtlNativeBoundary.Invalid();
            return;
        }

        var parent = await workflows.FindAsync(address.WorkflowExecutionId, cancellationToken)
            ?? throw new InvalidOperationException("ETL admission awaits its durable parent state.");
        var activity = await activities.FindAsync(address.WorkflowExecutionId, address.ActivityExecutionId, cancellationToken)
            ?? throw new InvalidOperationException("ETL admission awaits its durable activity state.");
        if (parent.PinnedExecutable.ArtifactId != address.ExecutableId || parent.PinnedExecutable.ArtifactHash != address.ExecutableHash ||
            parent.TenantId != invocation.Scope.TenantId || parent.Partition?.Value != invocation.Scope.PersistencePartition ||
            EtlRuntimeIdentity.InvocationFingerprint(EtlNativeBoundary.Invocation(activity)) != EtlRuntimeIdentity.InvocationFingerprint(invocation))
            throw EtlNativeBoundary.Invalid();
        if (parent.Status.IsTerminal() || activity.Status == ActivityExecutionStatus.Cancelled)
        {
            EnsureSettled(await runs.CancelAsync(invocation, cancellationToken));
            return;
        }
        var bookmark = await bookmarks.FindAsync(address.WorkflowExecutionId, address.BookmarkId, cancellationToken);
        if (bookmark is null)
        {
            throw new InvalidOperationException("ETL admission awaits its exact durable bookmark.");
        }
        EtlNativeBoundary.EnsureBookmark(address, bookmark);
        var result = await admission.AdmitAsync(invocation, cancellationToken);
        if (result.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay or EtlStoreStatus.Terminal))
            throw new InvalidOperationException("The ETL invocation could not be durably admitted.");
    }

    private static void EnsureSettled(EtlTerminalSettlementResult result)
    {
        if (result.Status is not (EtlStoreStatus.Applied or EtlStoreStatus.Replay or EtlStoreStatus.Terminal))
            throw new InvalidOperationException("The ETL cancellation could not be durably settled.");
    }
}
