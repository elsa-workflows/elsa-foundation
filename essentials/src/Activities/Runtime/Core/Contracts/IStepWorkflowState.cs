using System.Text.Json;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>Running-workflow state available only during one inline leaf body evaluation.</summary>
public interface IStepWorkflowState
{
    /// <summary>Whether the evaluation read a protected value through this capability.</summary>
    bool HasReadProtectedValue { get; }

    /// <summary>Reads a JSON copy, distinguishing an absent output from an explicit null.</summary>
    bool TryGetOutput(string name, out JsonElement value);

    /// <summary>Stages a complete JSON output assignment for the evaluation's checkpoint.</summary>
    void SetOutput(string name, JsonElement value);

    /// <summary>The current correlation, including this evaluation's pending assignment.</summary>
    string? CorrelationId { get; set; }

    /// <summary>The current name, including this evaluation's pending assignment.</summary>
    string? InstanceName { get; set; }
}

/// <summary>An engine script activity whose analyzed body determines workflow-state use.</summary>
public interface IStepWorkflowStateConsumer;
