using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// A checkout that itself sits under a directory named <c>bin</c>, <c>obj</c> or <c>tests</c> (for example
/// <c>~/bin/elsa-foundation</c>) must not make every file in the repository look like build output or test
/// code. These tests build a fake repository under such a path and pin that the segment checks look only at
/// the path relative to the repository root.
/// </summary>
public sealed class RepoPathsTests : IDisposable
{
    private readonly string _root;
    private readonly string _repoRoot;
    private readonly string _featureFile;
    private readonly string _extensionTestFile;

    public RepoPathsTests()
    {
        _root = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _repoRoot = Path.Join(_root, "bin", "obj", "tests", "repo");

        _featureFile = CreateFile("src", "Module", "Feature.cs");
        CreateFile("src", "Module", "obj", "Generated.cs");
        CreateFile("src", "Module", "bin", "Copied.cs");
        _extensionTestFile = CreateFile("src", "extensions", "Ext", "tests", "ExtTests.cs");
        CreateFile("obj", "TopLevel.cs");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Source_files_excludes_build_output_but_keeps_an_extensions_own_tests()
    {
        var files = ModuleRoots.SourceFiles(_repoRoot, "src");

        Assert.Equal(RepoRelativeSorted(_extensionTestFile, _featureFile), RepoRelativeSorted(files));
    }

    [Fact]
    public void Production_source_files_excludes_build_output_and_every_tests_directory()
    {
        var files = ModuleRoots.ProductionSourceFiles(_repoRoot);

        Assert.Equal(RepoRelativeSorted(_featureFile), RepoRelativeSorted(files));
    }

    [Theory]
    [InlineData("src/Module/obj/Generated.cs", true)]
    [InlineData("obj/TopLevel.cs", true)]
    [InlineData("src/Module/Feature.cs", false)]
    [InlineData("src/binary/Thing.cs", false)] // a "bin" substring, not a "bin" segment
    public void Is_build_output_is_judged_by_the_path_relative_to_the_repository_root(string repoRelativePath, bool expected)
    {
        var absolutePath = Path.Join(_repoRoot, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal(expected, RepoPaths.IsBuildOutput(_repoRoot, absolutePath));
    }

    [Fact]
    public void Is_build_output_also_accepts_a_path_already_relative_and_slash_normalized()
    {
        Assert.True(RepoPaths.IsBuildOutput(_repoRoot, "src/Module/obj/Generated.cs"));
    }

    private string CreateFile(params string[] segments)
    {
        var path = Path.Join([_repoRoot, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "// test fixture\n");
        return path;
    }

    private string[] RepoRelativeSorted(params string[] paths) =>
        RepoRelativeSorted((IEnumerable<string>)paths);

    private string[] RepoRelativeSorted(IEnumerable<string> paths) =>
        [.. paths
            .Select(path => Path.GetRelativePath(_repoRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)];
}
