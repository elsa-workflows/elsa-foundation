using System.Globalization;

namespace Elsa.Primitives.Versioning;

/// <summary>
/// An immutable Semantic Versioning 2.0.0 value (<c>MAJOR.MINOR.PATCH</c> with optional
/// <c>-prerelease</c> and <c>+buildmetadata</c>).
/// </summary>
/// <remarks>
/// Precedence follows the SemVer 2.0.0 spec: numeric core segments compare numerically
/// (so <c>10.0.0 &gt; 2.0.0</c> and <c>1.0.10 &gt; 1.0.2</c>); a pre-release version has lower
/// precedence than its associated release (<c>1.0.0-alpha &lt; 1.0.0</c>); build metadata is
/// ignored for both precedence and equality (<c>1.0.0 == 1.0.0+build</c>).
/// Parsing never throws: invalid input is signalled through <see cref="TryParse"/> so callers
/// can raise a domain-scoped failure of their own.
/// </remarks>
public readonly struct SemVer : IEquatable<SemVer>, IComparable<SemVer>
{
    // Wide enough for int-range numeric identifiers; keeps the sort key lexical-order == precedence.
    private const int SegmentWidth = 11;

    private SemVer(int major, int minor, int patch, string? prerelease, string? buildMetadata)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        BuildMetadata = buildMetadata;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The pre-release portion (without the leading <c>-</c>), or <c>null</c> for a release.</summary>
    public string? Prerelease { get; }

    /// <summary>The build-metadata portion (without the leading <c>+</c>), or <c>null</c>. Ignored for precedence and equality.</summary>
    public string? BuildMetadata { get; }

    /// <summary>
    /// Attempts to parse a full SemVer 2.0.0 string. Returns <c>false</c> for any malformed input;
    /// never throws.
    /// </summary>
    public static bool TryParse(string? value, out SemVer result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var core = value;
        string? prerelease = null;
        string? build = null;

        var plusIndex = core.IndexOf('+');
        if (plusIndex >= 0)
        {
            build = core[(plusIndex + 1)..];
            core = core[..plusIndex];
        }

        var dashIndex = core.IndexOf('-');
        if (dashIndex >= 0)
        {
            prerelease = core[(dashIndex + 1)..];
            core = core[..dashIndex];
        }

        var parts = core.Split('.');
        if (parts.Length != 3)
            return false;

        if (!TryParseNumericIdentifier(parts[0], out var major) ||
            !TryParseNumericIdentifier(parts[1], out var minor) ||
            !TryParseNumericIdentifier(parts[2], out var patch))
            return false;

        if (prerelease is not null && !IsValidPrerelease(prerelease))
            return false;

        if (build is not null && !IsValidBuildMetadata(build))
            return false;

        result = new SemVer(major, minor, patch, prerelease, build);
        return true;
    }

    /// <summary>
    /// Produces a normalised, lexicographically-sortable key whose ordinal string ordering equals
    /// SemVer precedence — usable as a persisted ORDER BY column.
    /// </summary>
    public string ToSortKey()
    {
        // Core segments zero-padded so numeric magnitude survives lexical ordering.
        var core = $"{Pad(Major)}.{Pad(Minor)}.{Pad(Patch)}";

        // A release must sort ABOVE its pre-releases: '1' (release) > '0' (pre-release).
        if (Prerelease is null)
            return $"{core}.1";

        return $"{core}.0.{NormalisePrerelease(Prerelease)}";
    }

    /// <summary>
    /// Convenience: parse <paramref name="version"/> and return its sort key. Throws
    /// <see cref="ArgumentException"/> if the input is not a valid semantic version — a guard for
    /// domain-boundary callers (e.g. entity construction) that have already validated upstream.
    /// </summary>
    public static string ToSortKey(string version)
    {
        if (!TryParse(version, out var semVer))
            throw new ArgumentException($"'{version}' is not a valid semantic version.", nameof(version));

        return semVer.ToSortKey();
    }

    public int CompareTo(SemVer other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        // Build metadata is ignored. A release outranks any of its pre-releases.
        if (Prerelease is null && other.Prerelease is null) return 0;
        if (Prerelease is null) return 1;
        if (other.Prerelease is null) return -1;

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public bool Equals(SemVer other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemVer other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    public override string ToString()
    {
        var core = $"{Major}.{Minor}.{Patch}";
        if (Prerelease is not null) core += $"-{Prerelease}";
        if (BuildMetadata is not null) core += $"+{BuildMetadata}";
        return core;
    }

    public static bool operator ==(SemVer left, SemVer right) => left.Equals(right);

    public static bool operator !=(SemVer left, SemVer right) => !left.Equals(right);

    public static bool operator <(SemVer left, SemVer right) => left.CompareTo(right) < 0;

    public static bool operator >(SemVer left, SemVer right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemVer left, SemVer right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemVer left, SemVer right) => left.CompareTo(right) >= 0;

    private static int ComparePrerelease(string left, string right)
    {
        var leftIds = left.Split('.');
        var rightIds = right.Split('.');
        var shared = Math.Min(leftIds.Length, rightIds.Length);

        for (var i = 0; i < shared; i++)
        {
            var l = leftIds[i];
            var r = rightIds[i];
            var lNumeric = IsNumeric(l);
            var rNumeric = IsNumeric(r);

            if (lNumeric && rNumeric)
            {
                var cmp = ulong.Parse(l, CultureInfo.InvariantCulture)
                    .CompareTo(ulong.Parse(r, CultureInfo.InvariantCulture));
                if (cmp != 0) return cmp;
            }
            else if (lNumeric)
            {
                return -1; // numeric identifiers have lower precedence than alphanumeric.
            }
            else if (rNumeric)
            {
                return 1;
            }
            else
            {
                var cmp = string.CompareOrdinal(l, r);
                if (cmp != 0) return cmp;
            }
        }

        // All shared identifiers equal: the larger set has higher precedence.
        return leftIds.Length.CompareTo(rightIds.Length);
    }

    private static string NormalisePrerelease(string prerelease)
    {
        var ids = prerelease.Split('.');
        // Token prefixes: '0' (numeric) sorts before '1' (alphanumeric), matching precedence.
        // '.' (ASCII 46) < '0'/'1', so a shorter identifier set sorts before a longer one.
        return string.Join('.', ids.Select(id =>
            IsNumeric(id) ? "0" + id.PadLeft(SegmentWidth, '0') : "1" + id));
    }

    private static string Pad(int value) => value.ToString(CultureInfo.InvariantCulture).PadLeft(SegmentWidth, '0');

    private static bool TryParseNumericIdentifier(string value, out int number)
    {
        number = 0;
        if (!IsNumeric(value))
            return false;

        // No leading zeros (except the literal "0").
        if (value.Length > 1 && value[0] == '0')
            return false;

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static bool IsValidPrerelease(string prerelease)
    {
        var ids = prerelease.Split('.');
        foreach (var id in ids)
        {
            if (id.Length == 0)
                return false;

            if (!id.All(IsAllowedIdentifierChar))
                return false;

            // Numeric pre-release identifiers must not have leading zeros.
            if (IsNumeric(id) && id.Length > 1 && id[0] == '0')
                return false;
        }

        return true;
    }

    private static bool IsValidBuildMetadata(string build)
    {
        var ids = build.Split('.');
        return ids.All(id => id.Length > 0 && id.All(IsAllowedIdentifierChar));
    }

    private static bool IsNumeric(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);

    private static bool IsAllowedIdentifierChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '-';
}
