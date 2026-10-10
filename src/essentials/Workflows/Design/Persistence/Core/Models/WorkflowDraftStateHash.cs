using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Design.Persistence.Core.Models;

/// <summary>
/// The content hash of a workflow draft: the SHA-256 of its stored <c>StateSource</c> (of an empty string when it is
/// null), as lowercase hexadecimal. Promotion compares it to the hash of the draft the caller read, so a promote writes
/// exactly the content its caller saw (spec 188, research R7). The draft row carries no revision or concurrency token, so
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
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stateSource ?? string.Empty)));
}
