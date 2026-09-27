using System.Xml.Linq;

namespace Elsa.Versioning.Calculator;

/// <summary>
/// Each version line's <c>major.minor</c>, read from <c>VersionLines.props</c> in the commit being built: Line A's from
/// <c>ElsaContractsVersion</c>, Line B's from <c>ElsaVersion</c> (spec 150 FR-001, FR-010). The last-published record
/// supplies only the patch.
/// </summary>
/// <param name="LineA">Line A's <c>major.minor</c>.</param>
/// <param name="LineB">Line B's <c>major.minor</c>.</param>
/// <param name="LineAMembers">The project names <c>ElsaVersionLineAMembers</c> lists.</param>
public sealed record VersionLineSettings(LineVersion LineA, LineVersion LineB, IReadOnlySet<string> LineAMembers)
{
    public const string RelativePath = "VersionLines.props";

    public LineVersion For(string line) => line == "A" ? LineA : LineB;

    /// <summary>
    /// Reads the three properties, each of which must be assigned exactly once, unconditionally, to a literal: this is a
    /// static read standing in for MSBuild's evaluation, so anything it could misread is refused.
    /// </summary>
    internal static VersionLineSettings Parse(byte[]? content, string source)
    {
        if (content is null)
            throw new InvalidOperationException($"{source} does not exist; it holds each version line's major.minor.");

        var root = MsBuildFile.LoadRoot(content, source);

        string Literal(string name)
        {
            var assignments = root.Descendants().Where(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (assignments.Length != 1 || assignments[0].Parent?.Name.LocalName != "PropertyGroup" ||
                assignments[0].Attribute("Condition") is not null || assignments[0].Parent!.Attribute("Condition") is not null ||
                assignments[0].Value.Contains("$(", StringComparison.Ordinal))
                throw new InvalidOperationException($"{source} must assign <{name}> exactly once, unconditionally, to a literal.");

            return assignments[0].Value.Trim();
        }

        return new VersionLineSettings(
            LineVersion.Parse(Literal("ElsaContractsVersion"), $"{source} <ElsaContractsVersion>"),
            LineVersion.Parse(Literal("ElsaVersion"), $"{source} <ElsaVersion>"),
            Literal("ElsaVersionLineAMembers").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal));
    }
}
