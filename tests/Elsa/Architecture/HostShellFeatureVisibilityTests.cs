using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// CShells feature discovery scans <c>Assembly.GetExportedTypes()</c>, so an <c>internal</c> class implementing
/// <c>IShellFeature</c> never enters the runtime feature catalog: every shell that requests it drops it with only a
/// startup log warning, and the feature silently does nothing. This guard makes that contract a build-time failure
/// (the exact regression that kept the OTel engine tracing bridge inert).
///
/// The scan covers all of <c>src</c>, not just <c>src/Apps</c>. The hazard is identical wherever the feature class
/// lives — the great majority of feature classes are under <c>src/Elsa</c> — and narrowing one there fails silently
/// at runtime rather than at compile time, so the compiler cannot be the oracle. Constitution §2.23.3 independently
/// requires feature classes to be public and non-sealed; this test is the mechanical half of that rule.
/// </summary>
public sealed class HostShellFeatureVisibilityTests
{
    // A class declaration whose base list names IShellFeature/IWebShellFeature, or a feature base class
    // (FastEndpointsFeatureBase, EFCore*FeatureBase, Groundwork*ShellFeatureBase, ...) — derived features
    // implement the interface only through the base, so matching the interface alone lets them silently
    // fall out of this guard. Base lists are single-line today; if a multi-line base list is ever
    // introduced this scan misses it, which fails safe only via review — keep feature declarations on one
    // line.
    private static readonly Regex FeatureDeclaration = new(
        @"^(?<indent>[ \t]*)(?<modifiers>(?:\w+[ \t]+)*)class[ \t]+(?<name>\w+)[^\r\n{]*:[^\r\n{]*\b(?:I(?:Web)?ShellFeature|\w+FeatureBase)\b",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // A text scan that matches nothing passes vacuously. This floor is well below the ~80 declarations
    // present today, so it tolerates deleting features but catches the regex silently going dead.
    private const int MinimumExpectedFeatureDeclarations = 40;

    [Fact]
    public void Shell_feature_classes_are_public_so_catalog_discovery_sees_them()
    {
        var sourceDirectory = Path.Combine(RepoRoot, "src");
        var declarations = Directory
            .EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsGeneratedOutput(file))
            .SelectMany(file => FeatureDeclaration.Matches(File.ReadAllText(file))
                .Select(match => (
                    IsPublic: match.Groups["modifiers"].Value.Contains("public", StringComparison.Ordinal),
                    Label: $"{Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/')}: class {match.Groups["name"].Value}")))
            .ToArray();

        Assert.True(
            declarations.Length >= MinimumExpectedFeatureDeclarations,
            $"Only {declarations.Length} IShellFeature declarations were found under src, below the expected " +
            $"floor of {MinimumExpectedFeatureDeclarations}. The scan has probably stopped matching (for example " +
            "a multi-line base list), which would make this guard pass without checking anything.");

        var violations = declarations
            .Where(declaration => !declaration.IsPublic)
            .Select(declaration => declaration.Label)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Shell feature classes must be public — CShells discovery scans exported types only, so an " +
            "internal IShellFeature is silently dropped from every shell that enables it:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static bool IsGeneratedOutput(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
