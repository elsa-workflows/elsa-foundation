using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Calculator.Tests;

/// <summary>The command line: a thin shell over <see cref="VersionCalculator"/> whose exit code separates a gate from bad input.</summary>
public sealed class CliTests : SyntheticHistory
{
    private readonly string recordFile;
    private readonly string outputFile;

    public CliTests()
    {
        // Inside .git: outside the working tree, so no commit picks them up, and removed with the repository.
        recordFile = Path.Join(Repo.Root, ".git", "published-versions.json");
        outputFile = Path.Join(Repo.Root, ".git", "computation.json");
        File.WriteAllText(recordFile, Record.Serialize());
    }

    [Fact]
    public void The_cli_writes_exactly_what_the_library_computes()
    {
        Edit("src/Tasks/Scheduler.cs");
        var commit = Repo.Commit();

        Assert.Equal(0, Run("--repo", Repo.Root, "--record", recordFile, "--commit", commit, "--output", outputFile));
        Assert.Equal(Compute(commit).ToJson(), File.ReadAllText(outputFile));
    }

    [Fact]
    public void The_cli_reads_the_record_from_a_revision_without_checking_it_out()
    {
        Repo.WriteBack(Record);

        Assert.Equal(0, Run("--repo", Repo.Root, "--record-ref", "publish-state", "--output", outputFile));
        Assert.Equal(Compute().ToJson(), File.ReadAllText(outputFile));
    }

    [Fact]
    public void The_cli_exits_1_when_a_gate_refuses()
    {
        Edit("src/Tasks/Scheduler.cs");
        Publish(CommitAndCompute());
        File.WriteAllText(recordFile, Record.Serialize());

        Assert.Equal(1, Run("--repo", Repo.Root, "--record", recordFile, "--commit", Baseline, "--output", outputFile));
        Assert.False(File.Exists(outputFile));
    }

    [Theory]
    [InlineData("--commit", "HEAD")]
    [InlineData("--record", "RECORD", "--record-ref", "publish-state")]
    [InlineData("--record", "RECORD", "--record-path", "other.json")]
    [InlineData("--record", "RECORD", "--commit", "no-such-revision")]
    [InlineData("--record", "missing.json")]
    [InlineData("--record-ref", "no-such-branch")]
    [InlineData("--record", "RECORD", "--unknown", "value")]
    [InlineData("--record", "RECORD", "--record", "RECORD")]
    [InlineData("--record")]
    public void The_cli_exits_2_on_invalid_input(params string[] arguments) =>
        Assert.Equal(2, Run(["--repo", Repo.Root, .. arguments.Select(argument => argument == "RECORD" ? recordFile : argument)]));

    private static int Run(params string[] arguments) =>
        (int)typeof(VersionCalculator).Assembly.EntryPoint!.Invoke(null, [arguments])!;
}
