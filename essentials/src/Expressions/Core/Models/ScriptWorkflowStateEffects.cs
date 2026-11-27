namespace Elsa.Expressions.Core.Models;

/// <summary>Value-free effects found by a language parser; opaque access remains conservative.</summary>
public sealed record ScriptWorkflowStateEffects
{
    public bool AccessesState { get; init; }
    public IReadOnlyList<string> OutputNames { get; init; } = [];
    public bool WritesCorrelationId { get; init; }
    public bool WritesInstanceName { get; init; }
    public bool Conservative { get; init; }
    /// <summary>A direct unshadowed call has provably invalid literal name, identity kind, arity or guaranteed-undefined payload.</summary>
    public bool HasInvalidLiteralCall { get; init; }

    /// <summary>
    /// The script references the unshadowed <c>workflowState</c> capability by name. Only a script body is given the
    /// capability; an input expression evaluates without it, so a runtime key, <c>eval</c> or <c>this</c> there cannot
    /// reach it. Input-expression checks refuse on this fact rather than on conservative <see cref="AccessesState"/> (#5686).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ReferencesCapability { get; init; }

    public static ScriptWorkflowStateEffects None { get; } = new();
}
