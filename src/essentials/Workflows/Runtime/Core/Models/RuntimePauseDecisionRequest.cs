namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimePauseDecisionRequest
{
    public RuntimePauseDecisionRequest(
        RuntimePauseBoundary boundary,
        DateTimeOffset evaluatedAt,
        string? workflowExecutionId = null,
        string? activityExecutionId = null,
        string? generatorId = null,
        string? ingressSourceId = null,
        string? workerId = null,
        string? hostId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ValidateOptionalId(workflowExecutionId, nameof(workflowExecutionId), "Workflow execution ID");
        ValidateOptionalId(activityExecutionId, nameof(activityExecutionId), "Activity execution ID");
        ValidateOptionalId(generatorId, nameof(generatorId), "Generator ID");
        ValidateOptionalId(ingressSourceId, nameof(ingressSourceId), "Ingress source ID");
        ValidateOptionalId(workerId, nameof(workerId), "Worker ID");
        ValidateOptionalId(hostId, nameof(hostId), "Host ID");

        if (activityExecutionId is not null && workflowExecutionId is null)
            throw new ArgumentException("Activity execution pause decisions require a workflow execution ID.", nameof(workflowExecutionId));

        if (generatorId is not null && workflowExecutionId is null)
            throw new ArgumentException("Generator pause decisions require a workflow execution ID.", nameof(workflowExecutionId));

        Boundary = boundary;
        EvaluatedAt = evaluatedAt;
        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        GeneratorId = generatorId;
        IngressSourceId = ingressSourceId;
        WorkerId = workerId;
        HostId = hostId;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public RuntimePauseBoundary Boundary { get; }
    public DateTimeOffset EvaluatedAt { get; }
    public string? WorkflowExecutionId { get; }
    public string? ActivityExecutionId { get; }
    public string? GeneratorId { get; }
    public string? IngressSourceId { get; }
    public string? WorkerId { get; }
    public string? HostId { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static void ValidateOptionalId(string? value, string parameterName, string label)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{label} cannot be blank when provided.", parameterName);
    }
}
