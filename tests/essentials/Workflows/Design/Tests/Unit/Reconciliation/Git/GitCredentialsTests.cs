using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// Token credentials (FR-013, #2197): the token reaches git through the environment of the commands that talk to the
/// remote, never through their arguments or a file, and the helper that reads it answers for the remote's host alone.
/// </summary>
public sealed class GitCredentialsTests : GitExportTest
{
    private const string Token = "s3cr3t-token-2197";
    private const string MachineHelper = "!f() { echo username=machine; echo password=machine-secret; }; f";

    [Fact]
    public async Task A_token_reaches_git_through_the_environment_and_never_the_command_line_or_the_disk()
    {
        Publish("wf-t", "T", "1.0.0");
        var slotsRoot = NewSlotsRoot();
        var node = Writer(GitPushMode.Immediate, slotsRoot: slotsRoot, configure: options =>
        {
            options.CredentialsMode = GitCredentialsMode.Token;
            options.Token = Token;
        });

        await node.Exporter.ExportAsync(CancellationToken.None);
        node.Slot.Dispose(); // so the scan below can read the lock file too

        Assert.Contains("Publish T v1.0.0 (wf-t)", RemoteSubjects());
        var remoteRuns = node.Git.Runs.Where(run => run.Command is "clone" or "fetch" or "push").ToList();
        Assert.Contains(remoteRuns, run => run.Command == "push");
        Assert.All(remoteRuns, run => Assert.Equal(Token, run.Environment[GitCredentials.TokenVariable]));
        Assert.All(node.Git.Runs.Except(remoteRuns), run => Assert.Empty(run.Environment));
        Assert.DoesNotContain(node.Git.Runs, run => run.Arguments.Any(argument => argument.Contains(Token)));
        Assert.DoesNotContain(Directory.EnumerateFiles(slotsRoot, "*", SearchOption.AllDirectories), file => File.ReadAllText(file).Contains(Token));
    }

    [Fact]
    public async Task The_token_helper_answers_for_the_remotes_host_alone_in_place_of_the_machines_helpers()
    {
        var credentials = GitCredentials.For(new GitReconciliationOptions
        {
            RemoteUrl = "https://git.example.test/acme/workflows.git", CredentialsMode = GitCredentialsMode.Token, Token = Token,
        });

        var remoteHost = await GitTestSupport.CredentialFillAsync(credentials, "git.example.test", MachineHelper);
        var otherHost = await GitTestSupport.CredentialFillAsync(credentials, "other.example.test", MachineHelper);

        Assert.Contains("username=x-access-token", remoteHost);
        Assert.Contains($"password={Token}", remoteHost);
        Assert.DoesNotContain("machine-secret", remoteHost);
        Assert.DoesNotContain(Token, otherHost);
        Assert.Contains("password=machine-secret", otherHost);
    }
}
