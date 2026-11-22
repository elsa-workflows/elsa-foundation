namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The one reduction every stimulus-hash projection returns (#2190): each hash once, compared ordinally, as stimulus
/// identities are everywhere else.
/// </summary>
public static class StimulusHashes
{
    public static IReadOnlyCollection<string> DistinctOrdinal(IEnumerable<string> stimulusHashes)
    {
        ArgumentNullException.ThrowIfNull(stimulusHashes);
        return stimulusHashes.Distinct(StringComparer.Ordinal).ToArray();
    }
}
