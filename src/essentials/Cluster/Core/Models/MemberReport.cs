namespace Elsa.Cluster.Core.Models;

/// <summary>
/// What a member says about itself, in named sections (FR-014). ADR 0078 calls this the member's self-report.
/// </summary>
/// <remarks>
/// A report the reader cannot interpret, for instance one written by a newer provider version, is
/// <see cref="Unknown"/> rather than skipped: it counts as reading nothing and fails every requirement of a counting
/// query (FR-012, FR-016).
/// </remarks>
public sealed record MemberReport
{
    /// <summary>A report with no sections.</summary>
    public static MemberReport Empty { get; } = new();

    /// <summary>The report of an entry the reader cannot interpret.</summary>
    public static MemberReport Unknown { get; } = new(readability: null, runnability: null, isUnknown: true);

    public MemberReport(ReadabilitySection? readability = null, RunnabilitySection? runnability = null)
        : this(readability, runnability, isUnknown: false)
    {
    }

    private MemberReport(ReadabilitySection? readability, RunnabilitySection? runnability, bool isUnknown)
    {
        Readability = readability;
        Runnability = runnability;
        IsUnknown = isUnknown;
    }

    /// <summary>Whether the reader could not interpret this report.</summary>
    public bool IsUnknown { get; }

    /// <summary>
    /// The readability section, or <see langword="null"/> when the member has no readability source. A member without
    /// one cannot say what it reads, so a counting query treats it like an unknown report.
    /// </summary>
    public ReadabilitySection? Readability { get; }

    /// <summary>
    /// The runnability section (spec 184, FR-008), or <see langword="null"/> when the member has no runnability source.
    /// A member without one cannot say what its runtime activates, so it meets no runnability requirement.
    /// </summary>
    public RunnabilitySection? Runnability { get; }
}
