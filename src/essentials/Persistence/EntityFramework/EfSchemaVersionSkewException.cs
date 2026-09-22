namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// A persisted row carries a schema version this build does not recognise.
/// </summary>
/// <remarks>
/// Distinct from the <see cref="InvalidDataException"/> the envelope-integrity checks raise, and
/// deliberately so (ADR 0077). The two describe different events and call for different responses:
/// a corrupt envelope means something damaged the row and the store needs investigating, whereas an
/// unrecognised schema version means a module version this host does not run has written here, which
/// is a deployment-sequencing question. Reporting the second as the first sends an operator looking
/// for data damage that does not exist.
/// </remarks>
public sealed class EfSchemaVersionSkewException(string module, string? found, string expected)
    : Exception(BuildMessage(module, found, expected))
{
    /// <summary>The EF module whose rows carry the unrecognised version.</summary>
    public string Module { get; } = module;

    /// <summary>The schema version found on the row, or null when the row carried none.</summary>
    public string? Found { get; } = found;

    /// <summary>The schema version this build writes and reads.</summary>
    public string Expected { get; } = expected;

    private static string BuildMessage(string module, string? found, string expected) =>
        $"Module '{module}' read a row at schema version '{found ?? "(none)"}' but this build expects " +
        $"'{expected}'. A module version this host does not run has written to this store. Check which " +
        "module versions are deployed against this database; under ADR 0077 a module whose schema " +
        "version moves cannot run alongside its predecessor.";
}
