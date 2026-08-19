using System.Text.Json.Serialization;

namespace Elsa.Activities.Parallel.Models;

/// <summary>
/// Compiled executable structure for a <c>Parallel</c> node: the ordered branches (each a stable name plus
/// the executable node id of its optional branch activity) and an optional join threshold. The branch
/// activities themselves are carried in named child slots; this structure records which slot child belongs
/// to which branch so the runtime can fork all of them (each under a distinct engine <c>BranchId</c>) and
/// join on their completions without re-reading the design document.
/// </summary>
public sealed class ParallelExecutableStructure
{
    [JsonConstructor]
    public ParallelExecutableStructure(
        IReadOnlyCollection<ParallelExecutableBranch>? branches = null,
        int? threshold = null)
    {
        Branches = branches ?? [];
        Threshold = threshold;
    }

    [JsonPropertyName("branches")]
    public IReadOnlyCollection<ParallelExecutableBranch> Branches { get; }

    /// <summary>
    /// The number of branches that must finish before the <c>Parallel</c> joins. <c>null</c> (or a value
    /// outside <c>1..Branches.Count</c>) means all branches must finish.
    /// </summary>
    [JsonPropertyName("threshold")]
    public int? Threshold { get; }
}

/// <summary>
/// A single compiled branch: its stable name and the executable node id of its optional branch activity
/// (<c>null</c> when the branch is empty).
/// </summary>
public sealed class ParallelExecutableBranch
{
    [JsonConstructor]
    public ParallelExecutableBranch(string name, string? activity = null)
    {
        Name = name;
        Activity = activity;
    }

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("activity")]
    public string? Activity { get; }
}
