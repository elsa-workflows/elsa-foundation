using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Calculator.Tests;

/// <summary>
/// Spec 150 FR-003: a pin a package reaches only transitively is in its nuspec all the same, so a change to it, or to
/// which pins a package reaches, advances exactly the packages whose nuspec changes (spec 149's pinned-transitive edges).
/// </summary>
public sealed class PinnedTransitiveTests : SyntheticHistory
{
    private const string OpenApiReason = "Directory.Packages.props: Microsoft.OpenApi, pinned transitively (changed)";

    /// <summary>
    /// The direction that looked like success before: a pin no project references, bumped for a security fix, advanced
    /// nothing, and no published nuspec carried the fixed floor.
    /// </summary>
    [Fact]
    public void A_transitive_pin_bump_advances_exactly_the_packages_reaching_it()
    {
        Repo.PackageVersions["Microsoft.OpenApi"] = "2.1.1";

        var computation = CommitAndCompute("Bump a transitive pin");

        Assert.Equal(["Elsa.Http", "dotnet-elsa"], computation.Affected);
        Assert.Equal([OpenApiReason], computation["Elsa.Http"].Reasons);
        Assert.Equal([OpenApiReason], computation["dotnet-elsa"].Reasons);
        Assert.Equal("4.0.5", VersionOf(computation, "Elsa.Http"));
    }

    /// <summary>A pin that only a test project reaches is in no package's nuspec.</summary>
    [Fact]
    public void A_pin_reached_by_no_package_advances_nothing()
    {
        Repo.PackageVersions["SQLitePCLRaw.bundle_e_sqlite3"] = "3.0.4";

        Assert.Empty(CommitAndCompute("Bump a pin no package reaches").Affected);
    }

    /// <summary>A Line A member whose nuspec a pin reaches moves, and Line A moves as one with it (FR-002).</summary>
    [Fact]
    public void A_pin_reaching_a_line_a_member_moves_the_whole_line()
    {
        Repo.PackageVersions["Microsoft.Extensions.Primitives"] = "10.0.11";

        var computation = CommitAndCompute("Bump a pin Line A reaches");

        Assert.Equal(["Elsa.Events", "Elsa.Events.Core", "Elsa.Primitives"], computation.Affected);
        Assert.Equal(["Directory.Packages.props: Microsoft.Extensions.Primitives, pinned transitively (changed)"], computation["Elsa.Events.Core"].Reasons);
        Assert.Equal(["Line A moves as one; changed: Elsa.Events.Core"], computation["Elsa.Primitives"].Reasons);
        Assert.Equal("4.0.4", VersionOf(computation, "Elsa.Primitives"));
    }

    /// <summary>
    /// A package whose own files did not change still advances when the pins its nuspec lists do: here a package it
    /// references starts, and then stops, referencing a pinned package.
    /// </summary>
    [Fact]
    public void A_package_advances_when_the_set_of_pins_it_reaches_changes()
    {
        Repo.Project("Elsa.Tasks").Packages.Add("Microsoft.Extensions.Primitives");
        Repo.Project("Elsa.Tasks.Schedules").PinnedTransitive.Add("Microsoft.Extensions.Primitives");

        var added = CommitAndCompute("Reference a pinned package from Tasks");
        Publish(added);
        Repo.Project("Elsa.Tasks").Packages.Remove("Microsoft.Extensions.Primitives");
        Repo.Project("Elsa.Tasks.Schedules").PinnedTransitive.Remove("Microsoft.Extensions.Primitives");
        var removed = CommitAndCompute("Stop referencing it");

        Assert.Equal(["Elsa.Tasks", "Elsa.Tasks.Schedules"], added.Affected);
        Assert.Equal(["Directory.Packages.props: Microsoft.Extensions.Primitives, pinned transitively (added)"], added["Elsa.Tasks.Schedules"].Reasons);
        Assert.Equal(["Elsa.Tasks", "Elsa.Tasks.Schedules"], removed.Affected);
        Assert.Equal(["Directory.Packages.props: Microsoft.Extensions.Primitives, pinned transitively (removed)"], removed["Elsa.Tasks.Schedules"].Reasons);
        Assert.Equal("4.0.3", VersionOf(removed, "Elsa.Tasks.Schedules"));
    }

    /// <summary>
    /// A pinned-transitive edge is in the fingerprint as it is in change detection (FR-018): the two are one input set,
    /// so a pin bump changes the fingerprint of exactly the packages it advances.
    /// </summary>
    [Fact]
    public void A_transitive_pin_bump_changes_the_fingerprint_of_exactly_the_packages_it_advances()
    {
        var before = Compute();
        Repo.PackageVersions["Microsoft.OpenApi"] = "2.1.1";

        var after = CommitAndCompute("Bump a transitive pin");

        Assert.NotEmpty(after.Affected);
        Assert.Equal(
            after.Affected,
            after.Packages.Where(package => package.Fingerprint != before[package.PackageId].Fingerprint).Select(package => package.PackageId));
    }
}
