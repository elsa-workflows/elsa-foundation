using System.Text.RegularExpressions;

namespace Elsa.Versioning.Calculator;

/// <summary>
/// The prerelease label a packed package carries (spec 150 FR-008). A build from <c>main</c> carries
/// <c>ElsaPrereleaseLabel</c> from <c>VersionLines.props</c> at the commit being built: <c>preview</c>, with no counter,
/// while the lines are unreleased, and no label once that property is empty. A build from any other branch carries
/// <c>branch-&lt;name&gt;</c> instead, which no build from <c>main</c> can produce.
/// </summary>
/// <remarks>
/// <para>The branch's name is made into a label in these steps, in order:</para>
/// <list type="number">
/// <item>a leading <c>refs/heads/</c> is removed, so a full ref and a short name give the same label;</item>
/// <item>the name <c>main</c> takes <c>main</c>'s label, and every other name continues;</item>
/// <item>it is lowercased, since NuGet compares labels without regard to case;</item>
/// <item>each run of characters other than <c>a-z</c> and <c>0-9</c> becomes one <c>-</c>, and <c>-</c> is trimmed from both
/// ends;</item>
/// <item>it is cut to its first <see cref="MaxBranchLength"/> characters and any <c>-</c> left at the end is trimmed, which
/// keeps a whole version within the 64 characters nuget.org accepts;</item>
/// <item>and it is prefixed with <see cref="BranchPrefix"/>.</item>
/// </list>
/// <para>
/// The result is one SemVer 2.0.0 prerelease identifier, never numeric, so no leading-zero rule can reject it. For
/// example, <c>feat/Issue_2080</c> becomes <c>branch-feat-issue-2080</c>. Its prefix sorts below <c>preview</c>, so a
/// branch build never outranks the <c>main</c> build of the same number. Two branches whose names differ only in the
/// characters these steps fold together share a label; the label scopes a build to its branch, not to its commit.
/// </para>
/// </remarks>
public static partial class PrereleaseLabel
{
    /// <summary>The property in <c>VersionLines.props</c> that holds <c>main</c>'s label.</summary>
    public const string PropertyName = "ElsaPrereleaseLabel";

    /// <summary>The one branch whose builds carry <c>main</c>'s label: the branch publishes are built from (FR-014).</summary>
    public const string MainBranch = "main";

    /// <summary>Leads every branch-scoped label, and so marks it as one.</summary>
    public const string BranchPrefix = "branch-";

    /// <summary>The most characters of a branch's name a label keeps.</summary>
    public const int MaxBranchLength = 40;

    private const string FullRefPrefix = "refs/heads/";

    /// <summary>The label a package packed from <paramref name="branch"/> carries, or null for none.</summary>
    /// <param name="branch">The branch being built, as a short name or a full <c>refs/heads/</c> ref.</param>
    /// <param name="mainLabel"><c>main</c>'s label, <c>ElsaPrereleaseLabel</c>: empty once the lines are released.</param>
    public static string? For(string branch, string mainLabel)
    {
        if (mainLabel.Length > 0 && !Pattern().IsMatch(mainLabel))
            throw new InvalidOperationException($"{PropertyName} '{mainLabel}' is not a prerelease label: dot-separated identifiers of 0-9, A-Z, a-z and '-'.");

        if (mainLabel.StartsWith(BranchPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{PropertyName} '{mainLabel}' starts with '{BranchPrefix}', which marks a branch build's label; main's must not.");

        var name = branch.StartsWith(FullRefPrefix, StringComparison.Ordinal) ? branch[FullRefPrefix.Length..] : branch;
        if (name == MainBranch)
            return mainLabel.Length > 0 ? mainLabel : null;

        var folded = NonAlphanumeric().Replace(name.ToLowerInvariant(), "-").Trim('-');
        var kept = folded[..Math.Min(folded.Length, MaxBranchLength)].TrimEnd('-');
        if (kept.Length == 0)
            throw new InvalidOperationException($"The branch '{branch}' has no letter or digit to make a prerelease label from.");

        return BranchPrefix + kept;
    }

    [GeneratedRegex("^[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphanumeric();
}
