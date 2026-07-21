namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Canonical partitioning for membership (<c>IN</c>) reads. Values are deduplicated and
/// ordinal-sorted so the same logical id set always produces the same bounded batches regardless
/// of caller order, and every batch stays within the declared maximum membership cardinality.
/// Because each batch's values are strictly ordinal-ordered across batches, concatenating
/// per-batch results preserves any declared route order whose leading sort field is the
/// membership field.
/// </summary>
public static class GroundworkMembershipBatches
{
    /// <summary>Partitions <paramref name="values"/> into deterministic bounded batches.</summary>
    public static IReadOnlyList<string[]> Create(
        IEnumerable<string> values,
        int maximumBatchSize = ElsaGroundworkQueryRoutes.MaximumMembershipCount)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumBatchSize, 0);
        var distinct = values
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return distinct.Length == 0 ? [] : distinct.Chunk(maximumBatchSize).ToArray();
    }
}
