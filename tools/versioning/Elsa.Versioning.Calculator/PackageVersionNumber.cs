using System.Globalization;
using System.Text.RegularExpressions;

namespace Elsa.Versioning.Calculator;

/// <summary>A <c>major.minor</c> pair: what a version line's MSBuild property supplies (spec 150 FR-010).</summary>
public readonly partial record struct LineVersion(int Major, int Minor)
{
    public static LineVersion Parse(string text, string source)
    {
        var match = Pattern().Match(text);
        if (!match.Success)
            throw new InvalidOperationException($"{source}: '{text}' is not a literal major.minor such as 4.0.");

        return new LineVersion(Number(match.Groups[1].Value), Number(match.Groups[2].Value));
    }

    public override string ToString() => $"{Major}.{Minor}";

    internal static int Number(string digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

/// <summary>
/// A package version as the last-published record holds it: <c>major.minor.patch</c> with an optional prerelease
/// label. The calculator reads only the numeric part; the label is applied when packing (spec 150 FR-008).
/// </summary>
public readonly partial record struct PackageVersionNumber(int Major, int Minor, int Patch, string? Label)
{
    public static PackageVersionNumber Parse(string text, string source)
    {
        var match = Pattern().Match(text);
        if (!match.Success)
            throw new InvalidOperationException($"{source}: '{text}' is not a version of the form major.minor.patch[-label].");

        return new PackageVersionNumber(
            LineVersion.Number(match.Groups[1].Value),
            LineVersion.Number(match.Groups[2].Value),
            LineVersion.Number(match.Groups[3].Value),
            match.Groups[4].Success ? match.Groups[4].Value : null);
    }

    /// <summary>The line this version belongs to.</summary>
    public LineVersion Line => new(Major, Minor);

    /// <summary><c>major.minor.patch</c>, without the label.</summary>
    public string Numeric => $"{Major}.{Minor}.{Patch}";

    /// <summary>Orders by major, minor and patch; the label takes no part.</summary>
    public int CompareNumeric(PackageVersionNumber other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    public override string ToString() => Label is null ? Numeric : $"{Numeric}-{Label}";

    [GeneratedRegex(@"^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
