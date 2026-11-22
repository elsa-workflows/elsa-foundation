namespace Elsa.Expressions.Core.Models;

/// <summary>Value-free effects found by a language parser; opaque access remains conservative.</summary>
public sealed record ScriptWorkflowStateEffects
{
    public bool AccessesState { get; init; }
    public IReadOnlyList<string> OutputNames { get; init; } = [];
    public bool WritesCorrelationId { get; init; }
    public bool WritesInstanceName { get; init; }
    public bool Conservative { get; init; }
    public static ScriptWorkflowStateEffects None { get; } = new();
}
