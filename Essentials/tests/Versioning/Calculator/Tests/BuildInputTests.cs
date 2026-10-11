using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Calculator.Tests;

/// <summary>Spec 150 US3, FR-003, FR-004 and SC-004: inputs that sit outside every project.</summary>
public sealed class BuildInputTests : SyntheticHistory
{
    /// <summary>
    /// US3 scenario 1, SC-004: a third-party bump advances exactly the projects whose nuspec lists it — the one that
    /// references it, and the one that reaches it through that project, whose nuspec transitive pinning writes it into.
    /// </summary>
    [Fact]
    public void A_third_party_bump_advances_exactly_the_projects_whose_nuspec_lists_it()
    {
        Repo.PackageVersions["Cronos"] = "0.14.0";

        var computation = CommitAndCompute("Bump Cronos");

        Assert.Equal(["Elsa.Tasks", "Elsa.Tasks.Schedules"], computation.Affected);
        Assert.Equal(["Directory.Packages.props: Cronos (changed)"], computation["Elsa.Tasks"].Reasons);
        Assert.Equal(["Directory.Packages.props: Cronos, pinned transitively (changed)"], computation["Elsa.Tasks.Schedules"].Reasons);
    }

    /// <summary>A bump of a package only test projects reference advances no package.</summary>
    [Fact]
    public void A_bump_of_a_test_only_package_advances_nothing()
    {
        Repo.PackageVersions["xunit"] = "2.9.4";

        Assert.Empty(CommitAndCompute("Bump xunit").Affected);
    }

    /// <summary>US3 scenario 3: comments and layout in <c>Directory.Packages.props</c> advance nothing.</summary>
    [Fact]
    public void A_comment_or_layout_edit_to_central_packages_advances_nothing()
    {
        Repo.CentralPackagesComment = "A reworded explanation of why these versions are pinned.";
        Repo.Commit("Reword a comment");
        Repo.Write("Directory.Packages.props", Repo.Read("Directory.Packages.props").Replace("\n    <", "\n\n        <", StringComparison.Ordinal));

        Assert.Empty(Compute(Repo.CommitAsIs("Reindent")).Affected);
    }

    /// <summary>FR-004 for the rest of <c>Directory.Packages.props</c>: a setting there has no edge to be precise with.</summary>
    [Fact]
    public void A_central_package_management_setting_advances_every_package()
    {
        Repo.TransitivePinning = false;

        Assert.Equal(AllPackages, CommitAndCompute("Stop pinning transitively").Affected);
    }

    /// <summary>
    /// US3 scenario 2, FR-004: a repository-wide build input advances every project, including one a root file imports
    /// (<c>VersionLines.props</c>) and ones MSBuild or the SDK find by walking up (a nested <c>Directory.Build.props</c>,
    /// <c>global.json</c>).
    /// </summary>
    [Theory]
    [InlineData("Directory.Build.props", "<Project>", "<Project>\n  <PropertyGroup><Deterministic>true</Deterministic></PropertyGroup>")]
    [InlineData("NuGet.config", "</packageSources>", "  <add key=\"mirror\" value=\"https://mirror.example.invalid/v3/index.json\" />\n  </packageSources>")]
    [InlineData("VersionLines.props", "<Project>", "<Project>\n  <PropertyGroup><ElsaRepositoryWideSetting>on</ElsaRepositoryWideSetting></PropertyGroup>")]
    public void A_repository_wide_build_input_advances_every_package(string path, string anchor, string replacement)
    {
        Repo.Write(path, Repo.Read(path).Replace(anchor, replacement, StringComparison.Ordinal));

        var computation = Compute(Repo.CommitAsIs($"Edit {path}"));

        Assert.Equal(AllPackages, computation.Affected);
        Assert.All(AllPackages, id => Assert.Contains($"{path} (changed)", computation[id].Reasons));
    }

    [Theory]
    [InlineData("src/Directory.Build.props", "<Project><PropertyGroup><LangVersion>preview</LangVersion></PropertyGroup></Project>")]
    [InlineData("global.json", """{ "sdk": { "version": "10.0.100" } }""")]
    public void A_new_build_file_above_every_project_advances_every_package(string path, string content)
    {
        Repo.Write(path, content);

        var computation = CommitAndCompute($"Add {path}");

        Assert.Equal(AllPackages, computation.Affected);
        Assert.All(AllPackages, id => Assert.Contains($"{path} (added)", computation[id].Reasons));
    }

    /// <summary>A nested build file applies to its own project and the projects beneath it, and to nothing else.</summary>
    [Fact]
    public void A_nested_build_file_advances_only_the_projects_it_applies_to()
    {
        Repo.Write("src/Tasks/Directory.Build.props", "<Project><PropertyGroup><Nullable>enable</Nullable></PropertyGroup></Project>");

        Assert.Equal(["Elsa.Tasks", "Elsa.Tasks.Schedules"], CommitAndCompute().Affected);
    }

    /// <summary>
    /// The line properties reach packages through the version-line rules, not as content of the file they sit in
    /// (FR-010), so rearranging them within <c>VersionLines.props</c> advances nothing.
    /// </summary>
    [Fact]
    public void Rearranging_the_version_line_properties_advances_nothing()
    {
        Repo.Write("VersionLines.props",
            """
            <Project>
              <!-- Both lines, and Line A's members, in one group. -->
              <PropertyGroup>
                <ElsaVersion>4.0</ElsaVersion>
                <ElsaVersionLineAMembers>;Elsa.Events.Core;Elsa.Primitives;</ElsaVersionLineAMembers>
                <ElsaContractsVersion>4.0</ElsaContractsVersion>
              </PropertyGroup>
            </Project>
            """);

        Assert.Empty(Compute(Repo.CommitAsIs("Rearrange VersionLines.props")).Affected);
    }

    /// <summary>A comment in a repository-wide build file cannot change a build, so it advances nothing.</summary>
    [Fact]
    public void A_comment_in_a_repository_wide_build_file_advances_nothing()
    {
        Repo.Write("Directory.Build.props", Repo.Read("Directory.Build.props").Replace("<Project>", "<Project>\n  <!-- Shared settings. -->", StringComparison.Ordinal));

        Assert.Empty(CommitAndCompute("Comment").Affected);
    }

    /// <summary>An import the calculator cannot follow would hide what it brings in, so it refuses rather than guess.</summary>
    [Fact]
    public void An_import_that_cannot_be_resolved_is_refused()
    {
        Repo.Write("Directory.Build.props", Repo.Read("Directory.Build.props").Replace("<Project>", "<Project>\n  <Import Project=\"$(BuildRoot)Shared.props\" />", StringComparison.Ordinal));
        var commit = Repo.Commit("Import from a property");

        var exception = Assert.Throws<InvalidOperationException>(() => Compute(commit));

        Assert.Contains("$(BuildRoot)Shared.props", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A file an import names that does not exist yet is an input too: its appearing is a change.</summary>
    [Fact]
    public void A_file_imported_before_it_exists_advances_every_package_when_it_appears()
    {
        Repo.Write("Directory.Build.props", Repo.Read("Directory.Build.props").Replace("<Project>",
            "<Project>\n  <Import Project=\"$(MSBuildThisFileDirectory)Local.props\" Condition=\"Exists('$(MSBuildThisFileDirectory)Local.props')\" />", StringComparison.Ordinal));
        Publish(CommitAndCompute("Import an optional file"));
        Repo.Write("Local.props", "<Project><PropertyGroup><Optimize>false</Optimize></PropertyGroup></Project>");

        var computation = CommitAndCompute("Add the optional file");

        Assert.Equal(AllPackages, computation.Affected);
        Assert.Contains("Local.props (changed)", computation["Elsa.Tasks"].Reasons);
    }
}
