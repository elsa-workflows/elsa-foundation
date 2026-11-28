using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Hosting;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed class KernelTestRunWaitControl(KernelTestRuns tests, KernelShellRuntime runtime,
    IServiceScopeFactory scopes, PersistenceScope persistence, TimeProvider time) : IWorkflowTestRunWaitControl
{
    private const int RequestMilliseconds = 5_000;

    public async ValueTask<IReadOnlyList<WorkflowTestRunActiveWait>> ReadAsync(WorkflowTestScope scope, string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSnapshotAsync(scope, new(workflowExecutionId), cancellationToken);
        if (snapshot is not { Status: ActorStatus.Running } active) return [];
        return NativeTestRunWaitControl.ReadActive(active.State)
            .Select(wait => new WorkflowTestRunActiveWait(wait.WaitId, wait.FrameId, wait.NodeId, wait.Event)).ToArray();
    }

    public async ValueTask<string> DeliverAsync(WorkflowTestScope scope, string workflowExecutionId, TestRunDeliveryReceipt receipt,
        JsonElement? stimulus, CancellationToken cancellationToken = default)
    {
        if (receipt.TestRunId != scope.ScopeId || receipt.StimulusDigest is { } digest && TestRunDeliveryReceipt.Digest(stimulus) != digest)
            return "Rejected";
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(RequestMilliseconds), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        var token = linked.Token;
        try
        {
            var actor = new ActorRef(workflowExecutionId);
            var deliveryId = Guid.Parse(receipt.DeliveryId);
            var snapshot = await ReadSnapshotAsync(scope, actor, token);
            var previous = Lookup(snapshot, receipt, deliveryId, delivered: false);
            if (previous != "Unseen" || receipt.StimulusDigest is null) return previous;

            var request = new NativeTestRunEvent(scope.ScopeId, receipt.WaitId, deliveryId, receipt.ConfigurationDigest,
                receipt.StimulusDigest, stimulus);
            var payload = JsonSerializer.Serialize(request);
            var ingress = await runtime.IngressAsync(token);
            await using (var runtimeScope = await scopes.CreateAsyncScopeAsync(persistence))
                await runtimeScope.ServiceProvider.GetRequiredService<IWorkflowTestScopeAdmissionStore>()
                    .AdmitIsolatedActorAsync(scope, actor.Id, time.GetUtcNow, token);
            // The scope CAS authorized this attempt. If Stop has since committed a terminal actor checkpoint,
            // the Foundation will settle this as ended input without invoking the workflow.
            var admission = await ingress.SendAsync(tests.Reads.Tenant, new Target.Ref(actor), Guid.NewGuid().ToString("N"),
                NativeTestRunWaitControl.Deliver, payload, cancellationToken: token);
            if (admission.Outcome == AdmissionOutcome.Refused)
                return admission.Reason is RefusalReason.NodeBusy or RefusalReason.InboxFull or RefusalReason.RateLimited
                    ? "Pending" : "NoLongerWaiting";
            while (true)
            {
                var observed = Lookup(await ReadSnapshotAsync(scope, actor, token), receipt, deliveryId, delivered: true);
                if (observed != "Unseen") return observed;
                await Task.Delay(TimeSpan.FromMilliseconds(100), time, token);
            }
        }
        catch (TestScopeAdmissionException)
        {
            return "Rejected";
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return "Pending";
        }
    }

    private sealed record Snapshot(NativeState State, ActorStatus Status);

    private static string Lookup(Snapshot? snapshot, TestRunDeliveryReceipt receipt, Guid deliveryId, bool delivered)
    {
        if (snapshot?.State.TestRunEvents?.GetValueOrDefault(deliveryId) is { } prior)
            return prior.WaitId != receipt.WaitId || prior.ConfigurationDigest != receipt.ConfigurationDigest
                ? "Conflict" : delivered ? "Delivered" : "AlreadyDelivered";
        if (snapshot?.State.TestRunEvents?.Count >= NativeTestRunWaitControl.ReceiptLimit) return "Limit";
        if (snapshot is not { Status: ActorStatus.Running } active ||
            !NativeTestRunWaitControl.ReadActive(active.State).Any(wait => wait.WaitId == receipt.WaitId))
            return "NoLongerWaiting";
        return "Unseen";
    }

    private async ValueTask<Snapshot?> ReadSnapshotAsync(WorkflowTestScope scope, ActorRef actor, CancellationToken token)
    {
        if (scope.IsExpired(time.GetUtcNow()) || (scope.TenantId ?? PersistenceScope.DefaultValue) != persistence.Value)
            return null;
        if (await runtime.ReadAsync(token) is not ICommittedRootStateReads reads ||
            await reads.ReadCommittedStateAsync(tests.Reads.Tenant, actor, token) is not { State: { } state } checkpoint ||
            WorkflowActor.DescribeStart(WorkflowActor.ActorType, checkpoint.TypeData) is not { Model: NativeExecutionModel.ModelKey, TestStart: { } selected } ||
            selected.ScopeId != scope.ScopeId || selected.Tenant != scope.TenantId ||
            selected.Partition != scope.Partition.Value || selected.ExpiresAt != scope.ExpiresAt)
            return null;
        return JsonSerializer.Deserialize<NativeState>(state) is { } native ? new(native, checkpoint.Status) : null;
    }
}
