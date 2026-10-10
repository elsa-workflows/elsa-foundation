using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// A checkout that itself sits under a directory named <c>bin</c>, <c>obj</c> or <c>tests</c> (for example
/// <c>~/bin/elsa-foundation</c>) must not make every file in the repository look like build output or test
/// code. These tests judge paths under such a repository root and pin that the segment checks look only at
/// the path relative to the repository root.
/// </summary>
public sealed class RepoPathsTests
{
    private readonly string _repoRoot = Path.Join(Path.GetTempPath(), "bin", "obj", "tests", "repo");

    [Theory]
    [InlineData("src/Module/Feature.cs", true)]
    [InlineData("src/extensions/Ext/tests/ExtTests.cs", false)]
    [InlineData("tests/Module/ModuleTests.cs", false)]
    public void Is_not_test_file_is_judged_by_the_path_relative_to_the_repository_root(string repoRelativePath, bool expected) =>
        Assert.Equal(expected, ModuleRoots.IsNotTestFile(_repoRoot, Absolute(repoRelativePath)));

    [Theory]
    [InlineData("src/Module/obj/Generated.cs", true)]
    [InlineData("obj/TopLevel.cs", true)]
    [InlineData("src/Module/Feature.cs", false)]
    [InlineData("src/binary/Thing.cs", false)] // a "bin" substring, not a "bin" segment
    public void Is_build_output_is_judged_by_the_path_relative_to_the_repository_root(string repoRelativePath, bool expected) =>
        Assert.Equal(expected, RepoPaths.IsBuildOutput(_repoRoot, Absolute(repoRelativePath)));

    [Fact]
    public void Is_build_output_also_accepts_a_path_already_relative_and_slash_normalized()
    {
        Assert.True(RepoPaths.IsBuildOutput(_repoRoot, "src/Module/obj/Generated.cs"));
    }

    private string Absolute(string repoRelativePath) =>
        Path.Join(_repoRoot, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
}
