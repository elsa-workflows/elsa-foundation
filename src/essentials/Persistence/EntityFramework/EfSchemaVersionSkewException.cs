namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// A persisted row carries a schema version this build cannot read: one outside its family's readable set (spec 180,
/// FR-007), whether newer than the build's current version, older than its upcaster chain reaches, below a gap in the
/// chain, or missing altogether.
/// </summary>
/// <remarks>
/// Distinct from the <see cref="InvalidDataException"/> the integrity checks raise, and deliberately so (ADR 0077). The
/// two describe different events and call for different responses: a corrupt row means something damaged it and the
/// store needs investigating, whereas an unreadable schema version means a build this host does not run has written
/// here, which is a deployment-sequencing question. Reporting the second as the first sends an operator looking for data
/// damage that does not exist. It stays unassignable to every type the stores' catch filters turn into corruption.
/// </remarks>
public sealed class EfSchemaVersionSkewException(string family, string? found, string expected, IReadOnlyList<string> readableVersions)
    : Exception(BuildMessage(family, found, expected, readableVersions))
{
    /// <summary>The schema family whose row carries the unreadable version.</summary>
    public string Family { get; } = family;

    /// <summary>The schema version found on the row, or null when the row carried none.</summary>
    public string? Found { get; } = found;

    /// <summary>The schema version this build writes: the last of <see cref="ReadableVersions"/>.</summary>
    public string Expected { get; } = expected;

    /// <summary>Every version this build reads for the family, in chain order ending at <see cref="Expected"/>.</summary>
    public IReadOnlyList<string> ReadableVersions { get; } = readableVersions;

    private static string BuildMessage(string family, string? found, string expected, IReadOnlyList<string> readable)
    {
        var readableText = string.Join(", ", readable.Select(version => $"'{version}'"));
        var cause = found is null
            ? "A row with no stamp is never read as any version."
            : "A build writes a schema version only once it is finalized for the family, which waits until every host " +
              "sharing this database can read it, so either a build that finalized a version this host cannot read " +
              "wrote the row, or the row predates every version this build's upcaster chain still reaches.";
        return $"Schema family '{family}' read a row at schema version '{found ?? "(none)"}', which this build cannot read: " +
               $"it reads {readableText} and writes '{expected}'. {cause} Run a build whose readable versions include the " +
               "row's version against this database. This is a deployment-sequencing question, not damaged data.";
    }
}
