using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Calculator.Tests;

/// <summary>
/// Moves, renames, deletions and re-adds (spec 150 FR-002, SC-010, SC-013). Change detection is keyed on the package id,
/// resolved to a path through each commit's own dependency map, so a move is found at both ends and never runs a
/// version backwards, as the commit-height scheme did (ADR 0067, 2026-09-22 amendment).
/// </summary>
public sealed class MoveAndRenameTests : SyntheticHistory
{
    /// <summary>SC-010: a package moved with no content change publishes at exactly one past its last published version.</summary>
    [Fact]
    public void A_package_moved_to_a_new_directory_advances_exactly_once()
    {
        Repo.MoveDirectory("src/Tasks", "src/Scheduling/Tasks");

        var computation = CommitAndCompute("Move Tasks");

        Assert.Equal(["Elsa.Tasks", "Elsa.Tasks.Schedules"], computation.Affected);
        Assert.Equal("4.0.8", VersionOf(computation, "Elsa.Tasks"));
        Assert.Equal("4.0.2", VersionOf(computation, "Elsa.Tasks.Schedules"));
        Assert.Equal("src/Scheduling/Tasks/Elsa.Tasks.csproj", computation["Elsa.Tasks"].Path);
        Assert.Contains("src/Tasks/Scheduler.cs (removed)", computation["Elsa.Tasks"].Reasons);
        Assert.Contains("src/Scheduling/Tasks/Scheduler.cs (added)", computation["Elsa.Tasks"].Reasons);
    }

    /// <summary>
    /// The hazard that retired commit height: a move after a long stretch of unpublished history. However long the
    /// stretch, the moved package lands one past its last published version and every other package stays put.
    /// </summary>
    [Fact]
    public void A_move_after_a_long_history_never_runs_a_version_backwards()
    {
        for (var edit = 0; edit < 8; edit++)
        {
            Edit("src/Tasks/Scheduler.cs");
            Repo.Commit();
        }

        Repo.MoveDirectory("src/Tasks", "src/Scheduling/Tasks");
        Repo.Commit("Move Tasks");
        Edit("src/Scheduling/Tasks/Scheduler.cs");

        var computation = CommitAndCompute();

        Assert.Equal("4.0.8", VersionOf(computation, "Elsa.Tasks"));
        Assert.Equal(["Elsa.Tasks", "Elsa.Tasks.Schedules"], computation.Affected);
    }

    /// <summary>A moved package that is published and then left alone is unchanged at its new path.</summary>
    [Fact]
    public void A_moved_package_is_unchanged_once_its_new_home_is_published()
    {
        Repo.MoveDirectory("src/Tasks", "src/Scheduling/Tasks");
        Publish(CommitAndCompute("Move Tasks"));
        Repo.Write("README.md", "# Repository, after the move");

        Assert.Empty(CommitAndCompute().Affected);
    }

    /// <summary>Edge case: a renamed package id is a new package at patch 0; the old id's record entry is untouched.</summary>
    [Fact]
    public void A_renamed_package_id_starts_at_patch_zero()
    {
        var recorded = Record.Find("Elsa.Http");
        Repo.Project("Elsa.Http").PackageId = "Elsa.Http.Client";

        var computation = CommitAndCompute("Rename Elsa.Http");

        Assert.Equal("4.0.0", VersionOf(computation, "Elsa.Http.Client"));
        Assert.Contains("no last-published record: first publish of this package id", computation["Elsa.Http.Client"].Reasons);
        Assert.DoesNotContain(computation.Packages, package => package.PackageId == "Elsa.Http");
        Assert.Same(recorded, Record.Find("Elsa.Http"));
    }

    /// <summary>SC-013: a renamed-away package id that is later reused continues from its record rather than restarting.</summary>
    [Fact]
    public void A_reused_package_id_continues_from_its_record()
    {
        Repo.Project("Elsa.Http").PackageId = "Elsa.Http.Client";
        Publish(CommitAndCompute("Rename Elsa.Http"));
        Repo.AddProject("src/Web/Elsa.Http.csproj");
        Repo.Write("src/Web/Endpoint.cs", "public sealed class Endpoint;");

        var computation = CommitAndCompute("Reuse Elsa.Http");

        Assert.Equal("4.0.5", VersionOf(computation, "Elsa.Http"));
        Assert.Equal("4.0.0", VersionOf(computation, "Elsa.Http.Client"));
        Assert.Equal(["Elsa.Http"], computation.Affected);
    }

    /// <summary>SC-013: a deleted package re-added with changes continues from its record rather than restarting at 0.</summary>
    [Fact]
    public void A_deleted_package_readded_with_changes_continues_from_its_record()
    {
        DeleteTasks();
        var deleted = Publish(CommitAndCompute("Delete Tasks"));
        RestoreTasks("public sealed class Scheduler { public int Interval; }");

        var computation = CommitAndCompute("Restore Tasks");

        Assert.Equal("4.0.7-preview", deleted.Find("Elsa.Tasks")!.Version.ToString());
        Assert.Equal(["Elsa.Tasks"], computation.Affected);
        Assert.Equal("4.0.8", VersionOf(computation, "Elsa.Tasks"));
    }

    /// <summary>
    /// A deleted package re-added exactly as it was last published is a revert of the deletion: no change, and it
    /// keeps its version rather than restarting at 0.
    /// </summary>
    [Fact]
    public void A_deleted_package_readded_unchanged_keeps_its_version()
    {
        DeleteTasks();
        Publish(CommitAndCompute("Delete Tasks"));
        RestoreTasks("public sealed class Scheduler;");

        var computation = CommitAndCompute("Restore Tasks");

        Assert.Empty(computation.Affected);
        Assert.Equal("4.0.7", VersionOf(computation, "Elsa.Tasks"));
    }

    /// <summary>Deleting a package moves nothing else, and the deleted id simply leaves the output.</summary>
    [Fact]
    public void A_deleted_package_leaves_the_output_and_moves_nothing_else()
    {
        DeleteTasks();

        var computation = CommitAndCompute("Delete Tasks");

        Assert.Empty(computation.Affected);
        Assert.DoesNotContain(computation.Packages, package => package.PackageId == "Elsa.Tasks");
    }

    /// <summary>Deletes Tasks, the Schedules project nested in it, and the test project that references it.</summary>
    private void DeleteTasks()
    {
        Repo.Delete("src/Tasks/Schedules/Elsa.Tasks.Schedules.csproj");
        Repo.Delete("tests/Tasks");
        Repo.Delete("src/Tasks");
    }

    private void RestoreTasks(string scheduler)
    {
        Repo.AddProject("src/Tasks/Elsa.Tasks.csproj", references: ["src/Primitives/Elsa.Primitives.csproj"], packages: ["Cronos"],
            body: """  <ItemGroup><Compile Remove="Schedules/**/*" /></ItemGroup>""");
        Repo.Write("src/Tasks/Scheduler.cs", scheduler);
        Repo.Write("src/Tasks/README.md", "# Tasks");
        Repo.Write("src/Tasks/EXTENSION_POINTS.md", "# Extension points");
        Repo.Write("src/Tasks/docs/guide.md", "# Guide");
    }
}
