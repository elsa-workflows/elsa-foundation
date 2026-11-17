using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// One background admission refresh (spec 1221 FR-018, FR-020): the snapshot it superseded, the evaluation that
/// replaced it, and why. The superseded snapshot stays in the history; the replacing one is what the worker receives
/// and is immutable from then on.
/// </summary>
public sealed record ActivityInputRefresh
{
    [JsonConstructor]
    public ActivityInputRefresh(
        ActivityInputSnapshot superseded,
        int replacedByEvaluation,
        IReadOnlyList<VariableInputChange> changes,
        DateTimeOffset refreshedAt)
    {
        ArgumentNullException.ThrowIfNull(superseded);
        ArgumentOutOfRangeException.ThrowIfLessThan(replacedByEvaluation, 2);
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
            throw new ArgumentException("A refresh names at least one changed variable.", nameof(changes));
        Superseded = superseded;
        ReplacedByEvaluation = replacedByEvaluation;
        Changes = changes;
        RefreshedAt = refreshedAt;
    }

    public ActivityInputSnapshot Superseded { get; }
    public int ReplacedByEvaluation { get; }
    public IReadOnlyList<VariableInputChange> Changes { get; }
    public DateTimeOffset RefreshedAt { get; }

    /// <summary>The reason, for example <c>count v3 → v4</c>.</summary>
    [JsonIgnore]
    public string Reason => string.Join(", ", Changes.Select(change => $"{change.VariableKey} v{change.ReadVersion} \u2192 v{change.CurrentVersion}"));
}

/// <summary>A variable an input snapshot read that has changed since: the version read and the current one.</summary>
public sealed record VariableInputChange
{
    [JsonConstructor]
    public VariableInputChange(string scopeId, string variableKey, long readVersion, long currentVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        ScopeId = scopeId;
        VariableKey = variableKey;
        ReadVersion = readVersion;
        CurrentVersion = currentVersion;
    }

    public string ScopeId { get; }
    public string VariableKey { get; }
    public long ReadVersion { get; }
    public long CurrentVersion { get; }
}
