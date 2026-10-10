using System.Xml.Linq;

namespace Elsa.Architecture.Tests;

/// <summary>The repository's one reviewed Line A list (ADR 0067), as the guards read it.</summary>
internal static class VersionLines
{
    /// <summary>
    /// The members of <c>VersionLines.props</c>, in the order it lists them: <c>ElsaVersionLineAMembers</c>'s
    /// semicolon-delimited text, the same property Directory.Build.props reads to compute <c>$(ElsaVersionLine)</c>.
    /// </summary>
    internal static IReadOnlyList<string> LineAMembers(string repoRoot) =>
        [
            .. XDocument.Load(Path.Join(repoRoot, "VersionLines.props"))
                .Descendants("ElsaVersionLineAMembers")
                .SelectMany(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        ];
}
