using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Calculator.Tests;

/// <summary>
/// Spec 150 FR-002a and SC-009: a file that does not contribute to the package does not advance it, and the exclusion
/// is by effect — a document a build file ships is package-affecting, and a file only a build convention reads is
/// never mistaken for documentation.
/// </summary>
public sealed class DocumentationTests : SyntheticHistory
{
    /// <summary>SC-009, and the edge case of documentation inside a project directory.</summary>
    [Fact]
    public void A_documentation_only_commit_advances_nothing()
    {
        Repo.Write("src/Tasks/README.md", "# Tasks, revised");
        Repo.Write("src/Tasks/EXTENSION_POINTS.md", "# Extension points, revised");
        Repo.Write("src/Tasks/docs/guide.md", "# Guide, revised");
        Repo.Write("src/Events/README.md", "# Events, revised");
        Repo.Write("src/Http/CHANGELOG.md", "# New document");
        Repo.Write("docs/guide.md", "# Guide, revised");

        Assert.Empty(CommitAndCompute("Docs").Affected);
    }

    /// <summary>
    /// The spec's worked example and its kin: once a build file ships or compiles a document, editing it advances the
    /// package. Each shape is declared, published, and then only the document is edited.
    /// </summary>
    [Theory]
    [InlineData("""  <PropertyGroup><PackageReadmeFile>README.md</PackageReadmeFile></PropertyGroup><ItemGroup><None Include="README.md" Pack="true" PackagePath="\" /></ItemGroup>""")]
    [InlineData("""  <ItemGroup><None Update="README.md" Pack="true" PackagePath="\" /></ItemGroup>""")]
    [InlineData("""  <ItemGroup><AdditionalFiles Include="README.md" /></ItemGroup>""")]
    [InlineData("""  <ItemGroup><EmbeddedResource Include="**\*.md" /></ItemGroup>""")]
    [InlineData("""  <ItemGroup><Content Include="$(DocumentationRoot)*.md" /></ItemGroup>""")]
    public void A_document_a_build_file_names_is_package_affecting(string body)
    {
        Repo.Project("Elsa.Tasks").Body += "\n" + body;
        Publish(CommitAndCompute("Ship the README"));
        Repo.Write("src/Tasks/README.md", "# Tasks, revised");

        var computation = CommitAndCompute("Docs");

        Assert.Equal(["Elsa.Tasks"], computation.Affected);
        Assert.Equal(["src/Tasks/README.md (changed)"], computation["Elsa.Tasks"].Reasons);
    }

    /// <summary>A repository-wide build file that ships documents makes every project's documents package-affecting.</summary>
    [Fact]
    public void A_document_an_ambient_build_file_ships_is_package_affecting()
    {
        Repo.Write("Directory.Build.props", Repo.Read("Directory.Build.props").Replace("</Project>",
            """  <ItemGroup><None Include="README.md" Pack="true" PackagePath="\" /></ItemGroup></Project>""", StringComparison.Ordinal));
        Publish(CommitAndCompute("Ship every README"));
        Repo.Write("src/Events/README.md", "# Events, revised");

        Assert.Equal(["Elsa.Events"], CommitAndCompute("Docs").Affected);
    }

    /// <summary>
    /// The direction that would look like success: a file nothing names but a build convention reads —
    /// the package-manifest generator picks up <c>elsa-package.overrides.json</c> by name alone — always counts.
    /// </summary>
    [Fact]
    public void An_owned_file_that_is_not_documentation_counts_even_when_nothing_names_it()
    {
        Repo.Write("src/Tasks/elsa-package.overrides.json", """{ "description": "overridden" }""");

        var computation = CommitAndCompute();

        Assert.Equal(["Elsa.Tasks"], computation.Affected);
        Assert.Equal(["src/Tasks/elsa-package.overrides.json (added)"], computation["Elsa.Tasks"].Reasons);
    }
}
