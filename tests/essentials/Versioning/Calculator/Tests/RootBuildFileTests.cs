namespace Elsa.Versioning.Calculator.Tests;

/// <summary>
/// This repository's own root build files, as the calculator reads them. Every package reads them (FR-004), so an
/// import in them the calculator cannot resolve would stop every computation, and an item it cannot resolve would be
/// taken to name every file, making every project's Markdown package-affecting (FR-002a). <c>PackageVersioning.props</c>
/// is the file most likely to grow one: its pack targets build item lists, and they must do it through task outputs
/// and metadata updates rather than an <c>Include</c> spelled with a property.
/// </summary>
public sealed class RootBuildFileTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("PackageVersioning.props")]
    [InlineData("VersionLines.props")]
    public void A_root_build_file_leaves_documentation_out_of_every_package(string file)
    {
        var parsed = MsBuildFile.Parse(File.ReadAllBytes(Path.Join(RepoRoot, file)));

        Assert.False(parsed.NamesEveryFile);
        Assert.False(parsed.Names("src/essentials/Tasks/README.md", "src/essentials/Tasks/", file));
        Assert.All(parsed.Imports, import => Assert.NotNull(RepositoryPath.Expand(import, string.Empty, string.Empty, "src/essentials/Tasks/")));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
