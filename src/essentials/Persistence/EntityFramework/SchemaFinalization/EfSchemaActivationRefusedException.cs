namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// The finalization gate's refusal to activate an EF module (spec 181, FR-014, FR-015 and FR-017): a family of the
/// module has a finalized version, a completion version or an unresolved intent this host cannot read, so activating
/// the module could misread or rewrite rows. Thrown where a pending migration under <c>Validate</c> is, so the shell
/// does not activate and nothing touches the module's tables.
/// </summary>
/// <remarks>
/// It names the module, the family, the version and the versions this host reads, and gives the remedy. It never
/// carries a connection string or any other restored secret (spec 171, FR-061): none of its parts is taken from
/// configuration.
/// </remarks>
public sealed class EfSchemaActivationRefusedException : InvalidOperationException
{
    public EfSchemaActivationRefusedException(
        string module,
        string family,
        EfSchemaActivationRefusal refusal,
        string? version,
        IReadOnlyList<string> readableVersions)
        : base(Describe(module, family, refusal, version, readableVersions))
    {
        Module = module;
        Family = family;
        Refusal = refusal;
        Version = version;
        ReadableVersions = readableVersions;
    }

    /// <summary>The EF module that was refused.</summary>
    public string Module { get; }

    /// <summary>The schema family whose record refused it.</summary>
    public string Family { get; }

    public EfSchemaActivationRefusal Refusal { get; }

    /// <summary>The finalized, completion or intended version this host cannot read; null when no record could be read.</summary>
    public string? Version { get; }

    /// <summary>The versions of the family this host reads.</summary>
    public IReadOnlyList<string> ReadableVersions { get; }

    /// <summary>The sentence a refusal says, for a caller that reports it with its own subject, such as a feature.</summary>
    public static string Describe(string module, string family, EfSchemaActivationRefusal refusal, string? version, IReadOnlyList<string> readableVersions)
    {
        var reads = $"this host reads only [{string.Join(", ", readableVersions)}]";
        var head = $"EF module '{module}' cannot activate: schema family '{family}'";
        return refusal switch
        {
            EfSchemaActivationRefusal.FinalizedUnreadable =>
                $"{head} is finalized at '{version}' in this database, and {reads}. Rows at '{version}' may exist, and this " +
                $"host cannot read them. Run a version of the module that reads '{version}', or restore a database backup " +
                $"taken before '{version}' was finalized.",
            EfSchemaActivationRefusal.CompletionUnreadable =>
                $"{head} is complete only from '{version}' in this database, and {reads}. Rows at '{version}' may exist, " +
                $"and this host cannot read them. Run a release that still reads '{version}' until the backfill completes past it.",
            EfSchemaActivationRefusal.IntentUnresolved =>
                $"{head} has an intent to finalize '{version}' that did not resolve in time, and {reads}. Run a version of " +
                $"the module that reads '{version}', or wait for the intent to be abandoned and start the host again.",
            EfSchemaActivationRefusal.ReportNotPublished =>
                $"{head} could not be activated because this host's readability report could not be published to cluster " +
                "membership, and a host whose report the fleet cannot see must not read the finalization record. Check the " +
                "membership provider, then start the host again.",
            _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "Unknown activation refusal.")
        };
    }
}

/// <summary>Why the gate refused to activate an EF module.</summary>
public enum EfSchemaActivationRefusal
{
    /// <summary>A family's finalized version is outside this host's readable set (FR-015).</summary>
    FinalizedUnreadable,

    /// <summary>A family's completion version is outside this host's readable set (FR-015, amended; spec 186, FR-020).</summary>
    CompletionUnreadable,

    /// <summary>An intent to finalize a version this host cannot read did not resolve within the bound (FR-014).</summary>
    IntentUnresolved,

    /// <summary>This host's readability report could not be published before the record was read (FR-013).</summary>
    ReportNotPublished
}
