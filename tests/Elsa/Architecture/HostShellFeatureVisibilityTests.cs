using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// CShells feature discovery scans <c>Assembly.GetExportedTypes()</c>, so an <c>internal</c> class implementing
/// <c>IShellFeature</c> never enters the runtime feature catalog: every shell that requests it drops it with only a
/// startup log warning, and the feature silently does nothing. This guard makes that contract a build-time failure
/// for host-local features (the exact regression that kept the OTel engine tracing bridge inert).
/// </summary>
public sealed class HostShellFeatureVisibilityTests
{
    // A class declaration whose base list names IShellFeature/IWebShellFeature. Base lists in the host are
    // single-line today; if a multi-line base list is ever introduced this scan misses it, which fails safe
    // only via review — keep host feature declarations on one line.
    private static readonly Regex FeatureDeclaration = new(
        @"^(?<indent>[ \t]*)(?<modifiers>(?:\w+[ \t]+)*)class[ \t]+(?<name>\w+)[^\r\n{]*:[^\r\n{]*\bI(?:Web)?ShellFeature\b",
        RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public void Host_shell_feature_classes_are_public_so_catalog_discovery_sees_them()
    {
        var appsDirectory = Path.Combine(RepoRoot, "src", "Apps");
        var violations = Directory
            .EnumerateFiles(appsDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => FeatureDeclaration.Matches(File.ReadAllText(file))
                .Where(match => !match.Groups["modifiers"].Value.Contains("public", StringComparison.Ordinal))
                .Select(match => $"{Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/')}: class {match.Groups["name"].Value}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Host shell feature classes must be public — CShells discovery scans exported types only, so an " +
            "internal IShellFeature is silently dropped from every shell that enables it:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
