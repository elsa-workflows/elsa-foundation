using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Kernel.Native;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed class KernelTestRunWaitControl(KernelTestRuns tests, KernelShellRuntime runtime, KernelShellStorage storage,
    IServiceScopeFactory scopes, PersistenceScope persistence, TimeProvider time) : IWorkflowTestRunWaitControl
{
    private const int RequestMilliseconds = 5_000;

    public async ValueTask<IReadOnlyList<WorkflowTestRunActiveWait>> ReadAsync(WorkflowTestScope scope, string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        var request = new NativeTestRunWaitQuery(scope.ScopeId);
        var page = await RequestAsync<NativeTestRunWaitPage>(scope, new(workflowExecutionId), NativeTestRunWaitControl.Read,
            JsonSerializer.Serialize(request), cancellationToken);
        return page?.Waits.Select(wait => new WorkflowTestRunActiveWait(wait.WaitId, wait.FrameId, wait.NodeId, wait.Event)).ToArray() ?? [];
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
            if (receipt.StimulusDigest is not null)
            {
                await using var runtimeScope = await scopes.CreateAsyncScopeAsync(persistence);
                await runtimeScope.ServiceProvider.GetRequiredService<IWorkflowTestScopeAdmissionStore>()
                    .AssertOpenAsync(scope, time.GetUtcNow(), token);
            }
            var request = new NativeTestRunEvent(scope.ScopeId, receipt.WaitId, Guid.Parse(receipt.DeliveryId),
                receipt.ConfigurationDigest, receipt.StimulusDigest, stimulus);
            var result = await RequestAsync<NativeTestRunEventResult>(scope, new(workflowExecutionId), NativeTestRunWaitControl.Deliver,
                JsonSerializer.Serialize(request), token);
            return result?.Status ?? "Pending";
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

    private async ValueTask<T?> RequestAsync<T>(WorkflowTestScope scope, ActorRef actor, string kind, string payload,
        CancellationToken token) where T : class
    {
        if (scope.IsExpired(time.GetUtcNow()) || (scope.TenantId ?? PersistenceScope.DefaultValue) != persistence.Value)
            return null;
        var current = await tests.Reads.GetAsync(actor, token);
        if (current.Run?.Run.TestStart is not { } selected || selected.ScopeId != scope.ScopeId || selected.Tenant != scope.TenantId ||
            selected.Partition != scope.Partition.Value || selected.ExpiresAt != scope.ExpiresAt)
            return null;
        var ingress = await runtime.IngressAsync(token);
        var deadline = checked(await storage.ReadClockAsync(token) + RequestMilliseconds);
        var reply = await ingress.RequestAsync(tests.Reads.Tenant, new Target.Ref(actor), Guid.NewGuid().ToString("N"), kind,
            payload, null, new(null, deadline), token);
        return reply.Item is JsonNode body ? body.Deserialize<T>() : reply.Item as T;
    }
}
