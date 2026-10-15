using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Which projects are production projects is decided once for the guards (<see cref="ModuleRoots.IsProductionPath"/>)
/// and once more in the root <c>Directory.Build.props</c>, as the regular expression that gives every production project
/// the naming analyzer. MSBuild cannot call the guards' code, so the two are held together here: if they drifted, a
/// project the guards treat as production could compile without the analyzer, or the other way round.
/// </summary>
public sealed partial class ProductionPathAgreementTests
{
    private static string WiringPattern { get; } = ReadWiringPattern(File.ReadAllText(Path.Join(RepoRoot, "Directory.Build.props")));

    [Fact]
    public void The_analyzer_wiring_and_the_guards_agree_on_every_project_in_the_repository()
    {
        var directories = AllProjectDirectories(RepoRoot);
        var disagreements = directories
            .Where(directory => Regex.IsMatch(directory, WiringPattern) != ModuleRoots.IsProductionPath(directory))
            .Select(directory => $"{directory}: Directory.Build.props says {Regex.IsMatch(directory, WiringPattern)}, the guards say {ModuleRoots.IsProductionPath(directory)}")
            .ToArray();

        Assert.Contains(directories, ModuleRoots.IsProductionPath);
        Assert.Contains(directories, directory => !ModuleRoots.IsProductionPath(directory));
        Assert.True(disagreements.Length == 0, string.Join(Environment.NewLine, disagreements));
    }

    [Theory]
    [InlineData("Essentials/src/Tasks/Elsa.Tasks/", true)]
    [InlineData("Essentials/apps/Elsa.Workbench/", true)]
    [InlineData("Extensions/Agent/src/Core/", true)]
    [InlineData("Essentials/tests/Tasks/Tests/", false)]
    [InlineData("Extensions/Agent/tests/Tests/", false)]
    [InlineData("Extensions/Attention/studio/Elsa.Studio.Attention/", false)]
    [InlineData("Essentials/build/Analyzers/", false)]
    [InlineData("Essentials/srcs/Elsa.Other/", false)]
    [InlineData("tools/maps/Elsa.Maps.Generator/", false)]
    [InlineData("samples/Elsa.Samples.Nuplane.Notes/", false)]
    public void Both_definitions_classify_each_kind_of_location_the_same_way(string directory, bool production)
    {
        Assert.Equal(production, ModuleRoots.IsProductionPath(directory));
        Assert.Equal(production, Regex.IsMatch(directory, WiringPattern));
    }

    /// <summary>The pattern the root <c>Directory.Build.props</c> sets <c>ElsaIsProductionProject</c> by.</summary>
    internal static string ReadWiringPattern(string props)
    {
        var condition = XDocument.Parse(props).Descendants()
            .Where(element => element.Name.LocalName == "ElsaIsProductionProject")
            .Select(element => element.Attribute("Condition")?.Value)
            .Single(value => value is not null)!;

        return WiringCondition().Match(condition) is { Success: true } match
            ? match.Groups["pattern"].Value
            : throw new InvalidOperationException($"ElsaIsProductionProject's condition no longer has the Regex::IsMatch shape this test reads: {condition}");
    }

    /// <summary>Every project directory in the repository, repository-relative with a trailing slash, build output excluded.</summary>
    private static IReadOnlyList<string> AllProjectDirectories(string repoRoot) =>
    [
        .. Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(repoRoot, path) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repoRoot, Path.GetDirectoryName(path)!).Replace('\\', '/') + "/")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
    ];

    [GeneratedRegex(@"IsMatch\('\$\(_ElsaProjectDirectoryFromRoot\)', '(?<pattern>[^']+)'\)")]
    private static partial Regex WiringCondition();
}
