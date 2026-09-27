using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Calculator.Tests;

/// <summary>Spec 150 FR-001, FR-002 (Line A as a whole) and FR-010 (major and minor from the line properties).</summary>
public sealed class VersionLineTests : SyntheticHistory
{
    private static readonly string[] LineA = ["Elsa.Events.Core", "Elsa.Primitives"];

    /// <summary>US1 scenario 3: a Line A change moves every Line A package to one new version and no Line B package.</summary>
    [Fact]
    public void A_line_a_change_moves_every_member_together_and_no_line_b_package()
    {
        Edit("src/Primitives/Primitive.cs");

        var computation = CommitAndCompute();

        Assert.Equal(LineA, computation.Affected);
        Assert.All(LineA, id => Assert.Equal("4.0.4", VersionOf(computation, id)));
        Assert.Equal(["Line A moves as one; changed: Elsa.Primitives"], computation["Elsa.Events.Core"].Reasons);
    }

    /// <summary>A changed member moves the line to one past its highest member, not past the member that changed.</summary>
    [Fact]
    public void Line_a_moves_to_one_past_its_highest_member()
    {
        var apart = RecordWith("Elsa.Events.Core", "4.0.6-preview");
        Edit("src/Primitives/Primitive.cs");

        var computation = Compute(Repo.Commit(), apart);

        Assert.Equal(LineA, computation.Affected);
        Assert.All(LineA, id => Assert.Equal("4.0.7", VersionOf(computation, id)));
    }

    /// <summary>
    /// Edge case, a publish that pushes some packages and not others: a Line A member whose push failed completes the
    /// line at the version the rest were pushed at, and the members already pushed are not pushed again.
    /// </summary>
    [Fact]
    public void A_partly_pushed_line_a_is_completed_at_the_same_version()
    {
        Edit("src/Primitives/Primitive.cs");
        var computation = CommitAndCompute();
        Publish(computation, "Elsa.Primitives");

        var retry = Compute(computation.Commit);

        Assert.Equal(["Elsa.Events.Core"], retry.Affected);
        Assert.Equal("4.0.4", VersionOf(retry, "Elsa.Events.Core"));
        Assert.StartsWith("completes Line A at 4.0.4", Assert.Single(retry["Elsa.Events.Core"].Reasons), StringComparison.Ordinal);
    }

    /// <summary>A member left behind whose inputs have since changed cannot complete the old version, so the line moves on.</summary>
    [Fact]
    public void A_partly_pushed_line_a_moves_on_when_a_member_behind_has_changed_since()
    {
        Edit("src/Primitives/Primitive.cs");
        Publish(CommitAndCompute(), "Elsa.Primitives");
        Edit("src/Events/Core/Event.cs");

        var retry = CommitAndCompute();

        Assert.Equal(LineA, retry.Affected);
        Assert.All(LineA, id => Assert.Equal("4.0.5", VersionOf(retry, id)));
    }

    /// <summary>Members recorded apart with nothing changed are brought together at the highest of them.</summary>
    [Fact]
    public void Members_recorded_apart_are_brought_together_at_the_highest()
    {
        var apart = RecordWith("Elsa.Events.Core", "4.0.6-preview");

        var computation = Compute(record: apart);

        Assert.Equal(["Elsa.Primitives"], computation.Affected);
        Assert.All(LineA, id => Assert.Equal("4.0.6", VersionOf(computation, id)));
    }

    /// <summary>A package joining Line A takes the line's next version, not patch 0.</summary>
    [Fact]
    public void A_new_line_a_member_joins_at_the_lines_next_version()
    {
        Repo.AddProject("src/Caching/Core/Elsa.Caching.Core.csproj", line: "A", references: ["src/Primitives/Elsa.Primitives.csproj"]);
        Repo.Write("src/Caching/Core/Cache.cs", "public interface ICache;");

        var computation = CommitAndCompute("Add Elsa.Caching.Core");

        Assert.Equal(["Elsa.Caching.Core", .. LineA], computation.Affected);
        Assert.All(["Elsa.Caching.Core", .. LineA], id => Assert.Equal("4.0.4", VersionOf(computation, id)));
    }

    /// <summary>
    /// FR-010: a Line B <c>major.minor</c> change starts every Line B package at patch 0 of the new line and leaves Line A,
    /// whose version moves only when its own contract does, where it is.
    /// </summary>
    [Fact]
    public void A_new_line_b_minor_starts_every_line_b_package_at_patch_zero()
    {
        Repo.ElsaVersion = "4.1";

        var computation = CommitAndCompute("Open 4.1");

        Assert.Equal(AllPackages.Except(LineA), computation.Affected);
        Assert.All(AllPackages.Except(LineA), id => Assert.Equal("4.1.0", VersionOf(computation, id)));
        Assert.All(LineA, id => Assert.Equal("4.0.3", VersionOf(computation, id)));
        Assert.Contains("line B is on 4.1; the last published version 4.0.7-preview is on 4.0", computation["Elsa.Tasks"].Reasons);
    }

    /// <summary>FR-010: a Line A minor starts the line at patch 0; compatible dependents keep their published ranges (FR-006a).</summary>
    [Fact]
    public void A_new_line_a_minor_moves_only_line_a()
    {
        Repo.ElsaContractsVersion = "4.1";

        var computation = CommitAndCompute("Contracts 4.1");

        Assert.Equal(LineA, computation.Affected);
        Assert.All(LineA, id => Assert.Equal("4.1.0", VersionOf(computation, id)));
    }

    /// <summary>
    /// A new Line A major falls outside every published range's upper bound (FR-007), so each package referencing a
    /// Line A member moves with it (ADR 0067: only a major change propagates through the reverse closure).
    /// </summary>
    [Fact]
    public void A_new_line_a_major_moves_every_package_that_references_the_line()
    {
        Repo.ElsaContractsVersion = "5.0";

        var computation = CommitAndCompute("Contracts 5.0");

        Assert.Equal(["Elsa.Events", .. LineA, "Elsa.Tasks"], computation.Affected);
        Assert.All(LineA, id => Assert.Equal("5.0.0", VersionOf(computation, id)));
        Assert.Equal("4.0.8", VersionOf(computation, "Elsa.Tasks"));
        Assert.Equal("4.0.3", VersionOf(computation, "Elsa.Events"));
        Assert.Contains("references Elsa.Primitives, which moves from major 4 to 5; every published range stops below the next major (FR-007)", computation["Elsa.Tasks"].Reasons);
    }

    /// <summary>
    /// FR-007 / ADR 0067 also reaches a Line B package's own major advance, not only Line A's: when a package's last
    /// published record sits on a major behind the line it is now on, its direct dependent moves with it.
    /// </summary>
    [Fact]
    public void A_stale_line_b_major_moves_every_package_that_references_it()
    {
        var stale = RecordWith("Elsa.Tasks", "3.0.7-preview");

        var computation = Compute(record: stale);

        Assert.Equal(["Elsa.Tasks", "Elsa.Tasks.Schedules"], computation.Affected);
        Assert.Equal("4.0.0", VersionOf(computation, "Elsa.Tasks"));
        Assert.Equal("4.0.2", VersionOf(computation, "Elsa.Tasks.Schedules"));
        Assert.Contains("references Elsa.Tasks, which moves from major 3 to 4; every published range stops below the next major (FR-007)", computation["Elsa.Tasks.Schedules"].Reasons);
    }

    /// <summary>A package moved between lines is marked changed and takes its new line's version.</summary>
    [Fact]
    public void A_package_moved_onto_line_a_takes_the_lines_version()
    {
        Repo.Project("Elsa.Http").Line = "A";

        var computation = CommitAndCompute("Move Elsa.Http onto Line A");

        Assert.Contains("moved from line B to line A", computation["Elsa.Http"].Reasons);
        Assert.All(["Elsa.Http", .. LineA], id => Assert.Equal("4.0.5", VersionOf(computation, id)));
    }

    /// <summary>The line properties are read statically, so anything that is not one literal is refused, not guessed.</summary>
    [Theory]
    [InlineData("$(ElsaMajor).0")]
    [InlineData("4")]
    [InlineData("4.0.1")]
    public void A_line_property_that_is_not_a_literal_major_minor_is_refused(string value)
    {
        Repo.ElsaVersion = value;
        var commit = Repo.Commit();

        Assert.Throws<InvalidOperationException>(() => Compute(commit));
    }

    /// <summary>FR-010: the version is defined in one place, so a build file that sets a line property elsewhere is refused.</summary>
    [Fact]
    public void A_line_property_set_outside_version_lines_is_refused()
    {
        Repo.Write("Directory.Build.props", Repo.Read("Directory.Build.props").Replace("</Project>", "  <PropertyGroup><ElsaVersion>4.2</ElsaVersion></PropertyGroup></Project>", StringComparison.Ordinal));
        var commit = Repo.Commit();

        var exception = Assert.Throws<InvalidOperationException>(() => Compute(commit));

        Assert.Contains("Directory.Build.props", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>A dependency map whose Line A disagrees with <c>VersionLines.props</c> is stale and refused.</summary>
    [Fact]
    public void A_dependency_map_with_a_stale_line_a_is_refused()
    {
        Repo.Project("Elsa.Http").Line = "A";
        Repo.Commit();
        Repo.Project("Elsa.Http").Line = "B";
        Repo.Write("VersionLines.props", Repo.Read("VersionLines.props").Replace("Elsa.Http;", string.Empty, StringComparison.Ordinal));
        var commit = Repo.CommitAsIs("Change Line A without regenerating the map");

        var exception = Assert.Throws<InvalidOperationException>(() => Compute(commit));

        Assert.Contains("Line A", exception.Message, StringComparison.Ordinal);
    }
}
