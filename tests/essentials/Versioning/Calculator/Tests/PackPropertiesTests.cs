using System.Xml.Linq;
using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Calculator.Tests;

/// <summary>
/// The MSBuild file that carries a computation into a pack (#2080): each package's final version and its input
/// fingerprint (FR-008, FR-018), and the commit they were computed for.
/// </summary>
/// <remarks>
/// Each test starts from a publish of the baseline with main's label assigned: assigning it changed
/// <c>VersionLines.props</c>, which every package reads, so that publish moved every package one patch on.
/// </remarks>
public sealed class PackPropertiesTests : SyntheticHistory
{
    public PackPropertiesTests()
    {
        Repo.PrereleaseLabel = "preview";
        Publish(CommitAndCompute("Label main's packages"));
    }

    /// <summary>
    /// A package being published carries its computed version with main's label; one that is not keeps the version
    /// its record names, the one on the feed, since the ranges of packages referencing it start there (SC-002).
    /// </summary>
    [Fact]
    public void On_main_a_published_package_carries_mains_label_and_the_rest_keep_their_recorded_versions()
    {
        Edit("src/Tasks/Scheduler.cs");
        var computation = CommitAndCompute();

        var packages = Rendered(computation, "main");

        Assert.Equal(["Elsa.Tasks"], computation.Affected);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Elsa.Cli"] = "4.0.6-preview",
                ["Elsa.Events"] = "4.0.3-preview",
                ["Elsa.Events.Core"] = "4.0.4-preview",
                ["Elsa.Http"] = "4.0.5-preview",
                ["Elsa.Primitives"] = "4.0.4-preview",
                ["Elsa.Tasks"] = "4.0.9-preview",
                ["Elsa.Tasks.Schedules"] = "4.0.2-preview"
            },
            packages.ToDictionary(package => package.Key, package => package.Value.Version));
        Assert.All(computation.Packages, package =>
            Assert.Equal(package.Fingerprint, packages[Path.GetFileNameWithoutExtension(package.Path)].Fingerprint));
    }

    /// <summary>US4 scenario 2: a branch build's packages carry its own label; what it does not publish stays as recorded.</summary>
    [Fact]
    public void On_a_branch_a_published_package_carries_the_branch_label()
    {
        Edit("src/Tasks/Scheduler.cs");

        var packages = Rendered(CommitAndCompute(), "refs/heads/feat/Issue_2080");

        Assert.Equal("4.0.9-branch-feat-issue-2080", packages["Elsa.Tasks"].Version);
        Assert.Equal("4.0.2-preview", packages["Elsa.Tasks.Schedules"].Version);
    }

    /// <summary>FR-008: once <c>ElsaPrereleaseLabel</c> is empty, main's packages carry no label.</summary>
    [Fact]
    public void Once_the_lines_are_released_main_packages_carry_no_label()
    {
        Repo.PrereleaseLabel = string.Empty;

        var packages = Rendered(CommitAndCompute("Release"), "main");

        Assert.Equal("4.0.9", packages["Elsa.Tasks"].Version);
        Assert.All(packages.Values, package => Assert.DoesNotContain('-', package.Version));
    }

    [Fact]
    public void The_file_names_the_commit_it_was_computed_for_and_is_the_same_every_time()
    {
        Edit("src/Tasks/Scheduler.cs");
        var commit = Repo.Commit();

        var text = PackProperties.Render(Compute(commit), "main");

        Assert.Equal(text, PackProperties.Render(Compute(commit), "main"));
        Assert.Equal(commit, XDocument.Parse(text).Root!.Element("PropertyGroup")!.Element(PackProperties.CommitProperty)!.Value);
    }

    /// <summary>Without <c>ElsaPrereleaseLabel</c> nothing says which label main's packages carry, so nothing is written.</summary>
    [Fact]
    public void A_commit_that_does_not_assign_mains_label_is_refused()
    {
        Repo.PrereleaseLabel = null;
        var computation = CommitAndCompute("Drop the label");

        var exception = Assert.Throws<InvalidOperationException>(() => PackProperties.Render(computation, "main"));

        Assert.Contains(PrereleaseLabel.PropertyName, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Main's label is read statically, like the line properties, so anything but one literal is refused, not guessed.</summary>
    [Fact]
    public void A_label_that_is_not_one_unconditional_literal_is_refused()
    {
        Repo.Write("VersionLines.props", Repo.Read("VersionLines.props").Replace(
            "<ElsaPrereleaseLabel>preview</ElsaPrereleaseLabel>",
            "<ElsaPrereleaseLabel Condition=\"'$(Release)' != 'true'\">preview</ElsaPrereleaseLabel>", StringComparison.Ordinal));
        var commit = Repo.CommitAsIs("Make the label conditional");

        var exception = Assert.Throws<InvalidOperationException>(() => Compute(commit));

        Assert.Contains(PrereleaseLabel.PropertyName, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>MSBuild selects each package's properties by project name, so two packable projects cannot share one.</summary>
    [Fact]
    public void Packable_projects_sharing_a_name_are_refused()
    {
        Repo.AddProject("src/Other/Elsa.Tasks.csproj", packageId: "Elsa.Other.Tasks");
        Repo.Write("src/Other/Task.cs", "public sealed class Task;");
        var computation = CommitAndCompute("Add a second project named Elsa.Tasks");

        var exception = Assert.Throws<InvalidOperationException>(() => PackProperties.Render(computation, "main"));

        Assert.Contains("Elsa.Tasks", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Each project's version and fingerprint, by project name, as MSBuild would select them.</summary>
    private static Dictionary<string, (string Version, string Fingerprint)> Rendered(VersionComputation computation, string branch) =>
        XDocument.Parse(PackProperties.Render(computation, branch)).Root!.Elements("PropertyGroup")
            .Where(group => group.Attribute("Condition") is not null)
            .ToDictionary(
                group => group.Attribute("Condition")!.Value.Split('\'')[3],
                group => (group.Element(PackProperties.VersionProperty)!.Value, group.Element(PackProperties.FingerprintProperty)!.Value));
}
