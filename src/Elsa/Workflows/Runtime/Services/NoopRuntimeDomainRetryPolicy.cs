using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class NoopRuntimeDomainRetryPolicy : IRuntimeDomainRetryPolicy
{
    private const string PolicyMetadataKey = "runtime.domainRetry.policy";
    private const string WorkflowExecutionIdMetadataKey = "runtime.domainRetry.workflowExecutionId";
    private const string ActivityExecutionIdMetadataKey = "runtime.domainRetry.activityExecutionId";

    public RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var metadata = new Dictionary<string, string>
        {
            [PolicyMetadataKey] = nameof(NoopRuntimeDomainRetryPolicy),
            [WorkflowExecutionIdMetadataKey] = request.WorkflowExecutionId
        };

        if (!string.IsNullOrWhiteSpace(request.ActivityExecutionId))
            metadata[ActivityExecutionIdMetadataKey] = request.ActivityExecutionId;

        return new RuntimeDomainRetryDecision(
            mode: RuntimeDomainRetryMode.DoNotRetry,
            delay: null,
            reason: "Default runtime domain retry policy does not retry.",
            metadata: metadata);
    }
}
