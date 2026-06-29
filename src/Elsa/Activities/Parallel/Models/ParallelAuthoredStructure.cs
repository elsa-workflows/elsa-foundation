using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Parallel.Models;

/// <summary>
/// Authored design-time structure for a <c>Parallel</c> node: the ordered set of branches (each a stable
/// branch name plus an optional single branch activity) and an optional join threshold. Every branch is a
/// single child placed in its own named slot. A null/zero <see cref="Threshold"/> means "all branches".
/// </summary>
public sealed class ParallelAuthoredStructure
{
    [JsonConstructor]
    public ParallelAuthoredStructure(
        IReadOnlyCollection<ParallelAuthoredBranch>? branches = null,
        int? threshold = null)
    {
        Branches = branches ?? [];
        Threshold = threshold;
    }

    [JsonPropertyName("branches")]
    public IReadOnlyCollection<ParallelAuthoredBranch> Branches { get; }

    /// <summary>
    /// The number of branches that must finish before the <c>Parallel</c> joins. <c>null</c> (or a value
    /// outside <c>1..Branches.Count</c>) means all branches must finish.
    /// </summary>
    [JsonPropertyName("threshold")]
    public int? Threshold { get; }
}

/// <summary>
/// A single authored branch: its stable name (used to key its child slot) and the optional branch activity
/// to run concurrently.
/// </summary>
public sealed class ParallelAuthoredBranch
{
    [JsonConstructor]
    public ParallelAuthoredBranch(string name, ActivityNode? activity = null)
    {
        Name = name;
        Activity = activity;
    }

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("activity")]
    public ActivityNode? Activity { get; }
}
