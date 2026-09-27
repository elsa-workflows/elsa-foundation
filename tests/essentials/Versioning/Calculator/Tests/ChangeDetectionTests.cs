using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Calculator.Tests;

/// <summary>Spec 150 FR-002, FR-006a, FR-009, SC-001, SC-003 and SC-008 over synthetic histories.</summary>
public sealed class ChangeDetectionTests : SyntheticHistory
{
    [Fact]
    public void The_published_baseline_is_unchanged_against_its_own_record()
    {
        var computation = Compute();

        Assert.Empty(computation.Affected);
        Assert.All(computation.Packages, package => Assert.Equal(Record.Find(package.PackageId)!.Version.Numeric, package.Version.Numeric));
        Assert.All(computation.Packages, package => Assert.Empty(package.Reasons));
    }

    /// <summary>US1 scenario 1, SC-001, FR-006a: <c>Elsa.Tasks.Schedules</c> references Tasks but is not repacked.</summary>
    [Fact]
    public void A_commit_touching_one_project_advances_exactly_that_package()
    {
        Edit("src/Tasks/Scheduler.cs");

        var computation = CommitAndCompute();

        Assert.Equal(["Elsa.Tasks"], computation.Affected);
        Assert.Equal("4.0.8", VersionOf(computation, "Elsa.Tasks"));
        Assert.Equal(["src/Tasks/Scheduler.cs (changed)"], computation["Elsa.Tasks"].Reasons);
        Assert.Equal("4.0.1", VersionOf(computation, "Elsa.Tasks.Schedules"));
    }

    /// <summary>Edge cases: a commit touching only files owned by no project, including the dependency map's own home.</summary>
    [Fact]
    public void Files_owned_by_no_project_advance_nothing()
    {
        Repo.Write("README.md", "# Repository, revised");
        Repo.Write("docs/guide.md", "# Guide, revised");
        Repo.Write("tools/script.sh", "echo revised");
        Repo.Write("tests/Tasks/SchedulerTests.cs", "public sealed class SchedulerTests { }");

        Assert.Empty(CommitAndCompute().Affected);
    }

    /// <summary>Spec 149 FR-006: the deeper project owns its subtree, so the outer one does not move.</summary>
    [Fact]
    public void A_nested_project_owns_its_own_subtree()
    {
        Edit("src/Tasks/Schedules/Schedule.cs");

        var computation = CommitAndCompute();

        Assert.Equal(["Elsa.Tasks.Schedules"], computation.Affected);
        Assert.Equal("4.0.2", VersionOf(computation, "Elsa.Tasks.Schedules"));
    }

    /// <summary>Edge case: a revert is a change against the last published state, so it advances again.</summary>
    [Fact]
    public void A_revert_after_a_publish_advances_the_patch_again()
    {
        Edit("src/Tasks/Scheduler.cs");
        Publish(CommitAndCompute());
        Repo.Write("src/Tasks/Scheduler.cs", "public sealed class Scheduler;");

        var computation = CommitAndCompute("Revert");

        Assert.Equal(["Elsa.Tasks"], computation.Affected);
        Assert.Equal("4.0.9", VersionOf(computation, "Elsa.Tasks"));
    }

    /// <summary>Edge case: a revert restoring exactly the last published state, before anything published since, is no change.</summary>
    [Fact]
    public void A_revert_before_any_publish_is_not_a_change()
    {
        Edit("src/Tasks/Scheduler.cs");
        Repo.Commit();
        Repo.Write("src/Tasks/Scheduler.cs", "public sealed class Scheduler;");

        var computation = CommitAndCompute("Revert");

        Assert.Empty(computation.Affected);
        Assert.Equal("4.0.7", VersionOf(computation, "Elsa.Tasks"));
    }

    /// <summary>
    /// US2: however many commits land between publishes, a package advances once, to one past its last published
    /// version; the old commit-height scheme counted them.
    /// </summary>
    [Fact]
    public void Commits_between_publishes_advance_a_package_once()
    {
        for (var edit = 0; edit < 5; edit++)
        {
            Edit("src/Tasks/Scheduler.cs");
            Repo.Commit();
        }

        Assert.Equal("4.0.8", VersionOf(Compute(), "Elsa.Tasks"));
    }

    /// <summary>
    /// US2 scenario 1, FR-009, SC-003, SC-008: the output is a function of the commit and the record revision alone, not
    /// of the working tree, the directory it is run from, or the run.
    /// </summary>
    [Fact]
    public void The_same_commit_and_record_give_byte_identical_output_whatever_the_working_tree_holds()
    {
        Edit("src/Tasks/Scheduler.cs");
        var commit = Repo.Commit();
        var first = Compute(commit).ToJson();

        Repo.Write("src/Http/Http.cs", "uncommitted edit");
        Repo.Write("src/Http/Untracked.cs", "untracked file");
        Repo.Delete("src/Primitives/Primitive.cs");
        var fromSubdirectory = VersionCalculator.Compute(new GitRepository(Path.Join(Repo.Root, "src", "Tasks")), commit, Record).ToJson();

        Assert.Equal(first, Compute(commit).ToJson());
        Assert.Equal(first, fromSubdirectory);
    }

    /// <summary>
    /// FR-006, FR-013, SC-008: the affected set is derived from the repository alone. The calculator references no HTTP
    /// or NuGet client, and the only git commands it can run read local objects.
    /// </summary>
    [Fact]
    public void The_calculator_can_reach_no_feed()
    {
        var referenced = typeof(VersionCalculator).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!).ToArray();

        Assert.DoesNotContain(referenced, name => name.StartsWith("System.Net", StringComparison.Ordinal) || name.StartsWith("NuGet", StringComparison.Ordinal));
        Assert.Equal(["cat-file", "ls-tree", "merge-base", "rev-parse"], GitRepository.ReadOnlyCommands.Order(StringComparer.Ordinal));
    }

    /// <summary>US4 scenario 3: of two consecutive builds where one package changed, only that package moves.</summary>
    [Fact]
    public void Consecutive_publishes_move_only_what_changed_since_each()
    {
        Edit("src/Http/Http.cs");
        var first = Publish(CommitAndCompute());
        Edit("src/Tasks/Scheduler.cs");

        var second = CommitAndCompute();

        Assert.Equal("4.0.5", first.Find("Elsa.Http")!.Version.Numeric);
        Assert.Equal(["Elsa.Tasks"], second.Affected);
        Assert.Equal("4.0.5", VersionOf(second, "Elsa.Http"));
    }
}
