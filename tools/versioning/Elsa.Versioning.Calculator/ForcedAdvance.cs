namespace Elsa.Versioning.Calculator;

/// <summary>
/// Packages to advance although none of their inputs changed, and why: spec 150 FR-003's force-advance, the escape
/// hatch at publish time.
/// </summary>
/// <remarks>
/// A forced package advances exactly as a changed one does: one past its recorded patch, Line A moving as one, and a
/// tool that carries it moving with it. The monotonicity gate (FR-012) holds for it as for any other package. Its
/// reason is one of the package's reasons in the output, so what moved it is on record beside the version.
/// </remarks>
public sealed class ForcedAdvance
{
    /// <summary>Leads the reason in a forced package's reasons.</summary>
    public const string ReasonPrefix = "force-advanced: ";

    /// <param name="packageIds">The packages to advance; package ids compare case-insensitively, as a feed compares them.</param>
    /// <param name="reason">Why, as it is to appear in the output.</param>
    /// <exception cref="ArgumentException">No package is named, one is named twice, or the reason is blank.</exception>
    public ForcedAdvance(IEnumerable<string> packageIds, string reason)
    {
        PackageIds = packageIds.ToArray();
        if (PackageIds.Count == 0)
            throw new ArgumentException("A force-advance names at least one package.");
        if (PackageIds.GroupBy(id => id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1) is { } twice)
            throw new ArgumentException($"A force-advance names {twice.Key} more than once.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A force-advance states its reason; the output records it on every package it moves.");

        Reason = reason.Trim();
    }

    public IReadOnlyList<string> PackageIds { get; }

    public string Reason { get; }

    /// <summary>Reads package ids separated by commas or whitespace, as the command line takes them.</summary>
    public static ForcedAdvance Parse(string packageIds, string reason) =>
        new(packageIds.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries), reason);
}
