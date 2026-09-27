using Elsa.Versioning.Calculator;
using Elsa.Versioning.Publisher.Tests.Support;

namespace Elsa.Versioning.Publisher.Tests;

/// <summary>The command line the workflow runs: a thin shell whose exit code separates a refusal from bad input.</summary>
public sealed class CliTests : PublishingHistory
{
    private string Output => Path.Join(Repo.Root, ".git", "cli");

    [Fact]
    public void Plan_writes_what_the_library_plans()
    {
        Assert.Equal(0, Run("plan", "--repo", Repo.Root, "--ref", PublishPlan.MainRef, "--output", Output, "--force-advance", "", "--force-reason", ""));

        var plan = PublishPlan.Load(Path.Join(Output, PlanFiles.Plan));
        Assert.Equal((PublishMode.DryRun, Repo.Head), (plan.Mode, plan.Commit));
        Assert.Null(plan.Forced);
        Assert.Equal(PlanAndPack().Computation.ToJson(), File.ReadAllText(Path.Join(Output, PlanFiles.Computation)));
    }

    /// <summary>Refusals exit 1: the bootstrap once the record exists, and the forward-only gate.</summary>
    [Fact]
    public async Task Plan_exits_1_when_it_refuses()
    {
        var bootstrap = (await BootstrapAsync()).Plan.Commit;
        Edit("src/Tasks/Scheduler.cs");
        CommitAndPush();
        await RunAsync();

        Assert.Equal(1, Run("plan", "--repo", Repo.Root, "--ref", PublishPlan.MainRef, "--output", Output, "--bootstrap", "true"));
        Assert.Equal(1, Run("plan", "--repo", Repo.Root, "--ref", PublishPlan.MainRef, "--output", Output, "--commit", bootstrap));
    }

    /// <summary>Publishing a plan that pushes nothing exits 1 before it needs a feed.</summary>
    [Fact]
    public void Publish_exits_1_for_a_plan_that_pushes_nothing()
    {
        var run = PlanAndPack();
        Environment.SetEnvironmentVariable("ELSA_PUBLISHER_TEST_KEY", "key");

        Assert.Equal(1, Run("publish", "--repo", Repo.Root, "--ref", PublishPlan.MainRef, "--plan-dir", run.Directory,
            "--feed", "https://feed.invalid/index.json", "--api-key-env", "ELSA_PUBLISHER_TEST_KEY"));
    }

    [Theory]
    [InlineData]
    [InlineData("pack")]
    [InlineData("plan")]
    [InlineData("plan", "--ref", "refs/heads/main")]
    [InlineData("plan", "--ref", "refs/heads/main", "--output", "OUT", "--bootstrap", "yes")]
    [InlineData("plan", "--ref", "refs/heads/main", "--output", "OUT", "--force-advance", "Elsa.Tasks", "--force-reason", "")]
    [InlineData("plan", "--ref", "refs/heads/main", "--output", "OUT", "--force-advance", "", "--force-reason", "a reason")]
    [InlineData("plan", "--ref", "refs/tags/4.0.0", "--output", "OUT")]
    [InlineData("plan", "--ref", "refs/heads/main", "--output", "OUT", "--feed", "https://feed.invalid/index.json")]
    [InlineData("publish", "--ref", "refs/heads/main", "--plan-dir", "OUT", "--feed", "https://feed.invalid/index.json", "--api-key-env", "ELSA_PUBLISHER_UNSET_KEY")]
    [InlineData("publish", "--ref", "refs/heads/main", "--plan-dir", "OUT", "--api-key-env", "ELSA_PUBLISHER_UNSET_KEY", "--output", "OUT")]
    public void Invalid_input_exits_2(params string[] arguments) =>
        Assert.Equal(2, Run([.. arguments.Select(argument => argument == "OUT" ? Output : argument), .. arguments.Length > 0 ? ["--repo", Repo.Root] : Array.Empty<string>()]));

    private static int Run(params string[] arguments) =>
        typeof(PlanCommand).Assembly.EntryPoint!.Invoke(null, [arguments]) switch
        {
            int code => code,
            Task<int> task => task.GetAwaiter().GetResult(),
            var other => throw new InvalidOperationException($"The entry point returned {other}.")
        };
}
