using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Creates the single runtime frame shape for workflow, container, and loop-iteration activations.
/// </summary>
public sealed class VariableFrameFactory
{
    public VariableFrameState CreateRoot(
        string workflowExecutionId,
        string scopeId,
        IReadOnlyDictionary<string, ValueEnvelope> values) =>
        Create(VariableFrameKind.Root, scopeId, workflowExecutionId, parent: null, values);

    public VariableFrameState CreateContainer(
        string scopeId,
        string activityExecutionId,
        VariableFrameState parent,
        IReadOnlyDictionary<string, ValueEnvelope> values) =>
        Create(VariableFrameKind.Container, scopeId, activityExecutionId, parent, values);

    public VariableFrameState CreateIteration(
        string scopeId,
        string activityExecutionId,
        string iterationId,
        VariableFrameState parent,
        IReadOnlyDictionary<string, ValueEnvelope> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iterationId);
        return Create(
            VariableFrameKind.Iteration,
            scopeId,
            $"{activityExecutionId}:iteration:{iterationId}",
            parent,
            values);
    }

    private static VariableFrameState Create(
        VariableFrameKind kind,
        string scopeId,
        string activationId,
        VariableFrameState? parent,
        IReadOnlyDictionary<string, ValueEnvelope> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        ArgumentNullException.ThrowIfNull(values);
        if (parent?.Status == VariableFrameStatus.Closed)
            throw new InvalidOperationException($"Cannot activate a child frame beneath closed frame '{parent.FrameId}'.");

        var frameId = $"frame:{kind.ToString().ToLowerInvariant()}:{activationId}:{scopeId}";
        return new VariableFrameState(frameId, scopeId, activationId, parent?.FrameId, kind, values);
    }
}
