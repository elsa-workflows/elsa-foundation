using Elsa.Workflows.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Immutable, policy-safe snapshots supplied to an incident strategy.</summary>
public sealed class IncidentStrategyContext
{
    public IncidentStrategyContext(
        IncidentStrategyIncidentSnapshot incident,
        IncidentStrategyActivitySnapshot? activity,
        IncidentStrategyWorkflowSnapshot workflow,
        IncidentStrategyExecutableSnapshot executable)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(executable);

        Incident = incident;
        Activity = activity;
        Workflow = workflow;
        Executable = executable;
    }

    public IncidentStrategyIncidentSnapshot Incident { get; }
    public IncidentStrategyActivitySnapshot? Activity { get; }
    public IncidentStrategyWorkflowSnapshot Workflow { get; }
    public IncidentStrategyExecutableSnapshot Executable { get; }
}

/// <summary>Safe incident facts supplied to policy code; it deliberately omits raw exception and message data.</summary>
public sealed class IncidentStrategyIncidentSnapshot
{
    public IncidentStrategyIncidentSnapshot(
        string incidentId,
        string workflowExecutionId,
        string? activityExecutionId,
        string? executableNodeId,
        string status,
        string severity,
        string failureType,
        DateTimeOffset createdAt,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(severity);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureType);

        IncidentId = incidentId;
        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        ExecutableNodeId = executableNodeId;
        Status = status;
        Severity = severity;
        FailureType = failureType;
        CreatedAt = createdAt;
        Metadata = IncidentStrategyMetadata.Snapshot(metadata);
    }

    public string IncidentId { get; }
    public string WorkflowExecutionId { get; }
    public string? ActivityExecutionId { get; }
    public string? ExecutableNodeId { get; }
    public string Status { get; }
    public string Severity { get; }
    public string FailureType { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>Safe activity identity and lifecycle facts supplied to policy code.</summary>
public sealed record IncidentStrategyActivitySnapshot(string ActivityExecutionId, string ActivityType, string State);

/// <summary>Safe containing-workflow identity and lifecycle facts supplied to policy code.</summary>
public sealed record IncidentStrategyWorkflowSnapshot(string WorkflowExecutionId, string State);

/// <summary>Safe identity facts for the executable whose pinned strategy is being evaluated.</summary>
public sealed record IncidentStrategyExecutableSnapshot(
    string DefinitionId,
    string DefinitionVersionId,
    string Hash,
    IncidentStrategyReference IncidentStrategy);
