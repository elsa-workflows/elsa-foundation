using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class DefaultRuntimeVolatileWaitPolicy : IRuntimeVolatileWaitPolicy
{
    private const string PolicyMetadataKey = "runtime.volatileWait.policy";
    private const string WorkflowExecutionIdMetadataKey = "runtime.volatileWait.workflowExecutionId";
    private const string ActivityExecutionIdMetadataKey = "runtime.volatileWait.activityExecutionId";
    private const string AwaitableKindMetadataKey = "runtime.volatileWait.awaitableKind";

    public RuntimeVolatileWaitPolicyDecision Decide(RuntimeVolatileWaitPolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isAllowed = request.HostSupportsInMemoryContinuation;

        return new RuntimeVolatileWaitPolicyDecision(
            isAllowed: isAllowed,
            hostShutdownBehavior: request.RequestedHostShutdownBehavior,
            cancellationBehavior: request.RequestedCancellationBehavior,
            durableFallbackPolicy: request.DurableFallbackPolicy,
            maximumDuration: request.RequestedDuration,
            reason: isAllowed ? null : "Host does not support in-memory volatile wait continuation.",
            metadata: new Dictionary<string, string>
            {
                [PolicyMetadataKey] = nameof(DefaultRuntimeVolatileWaitPolicy),
                [WorkflowExecutionIdMetadataKey] = request.WorkflowExecutionId,
                [ActivityExecutionIdMetadataKey] = request.ActivityExecutionId,
                [AwaitableKindMetadataKey] = request.AwaitableKind
            });
    }
}
