namespace Elsa.Workflows.Design.Persistence.Core.Models;

/// <summary>
/// The content hash of a workflow draft: SHA-256 of the canonical JSON value in <c>StateSource</c>, as lowercase
/// hexadecimal. Null or empty source uses the hash of an empty binary frame. Promotion compares JSON content with
/// the draft the caller read, preserving equivalent escaping, member order and number spellings.
/// The draft row carries no revision or concurrency token, so
/// its content is the only thing such a compare can rest on.
/// </summary>
public static class WorkflowDraftStateHash
{
    /// <summary>
    /// The hash a caller passes when it found no draft to read and so checked no content: the hash of empty content.
    /// Promotion still answers a replay of a promotion that already succeeded (the original version) and a first
    /// promotion of a draft that does not exist (not found). The only drafts that match it are those whose stored state
    /// source is null or empty, which hold no state to promote; any other draft differs and is refused as changed.
    /// </summary>
    public static string Absent { get; } = Compute(null);

    /// <summary>The hash of a draft whose stored state source is <paramref name="stateSource"/>.</summary>
    public static string Compute(string? stateSource) =>
        string.IsNullOrEmpty(stateSource)
            ? Elsa.Serialization.Core.JsonHashInput.HashFramed(ReadOnlySpan<byte>.Empty)
            : Elsa.Serialization.Core.JsonHashInput.HashJson(stateSource);
}
