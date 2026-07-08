using Elsa.Git;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

/// <summary>
/// Edge behaviour of the working-copy manager: an unreachable remote fails fast (no interactive hang,
/// GIT_TERMINAL_PROMPT=0) and leaves no partial clone the caller would mistake for ready.
/// </summary>
public sealed class GitWorkspaceTests : GitIntegrationTest
{
    [Fact]
    public async Task Unreachable_remote_fails_fast()
    {
        var cache = GitTestSupport.NewCachePath();
        _tempPaths.Add(cache);
        var options = new GitReconciliationOptions
        {
            RemoteUrl = Path.Combine(Path.GetTempPath(), "elsa-gitops-tests", "does-not-exist-" + Guid.NewGuid().ToString("N")),
            Branch = "main", LocalCachePath = cache, Role = GitReconciliationRole.Consumer,
        };
        var workspace = new GitWorkspace(_git, GitTestSupport.Options(options), NullLogger<GitWorkspace>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.EnsureReadyAsync(CancellationToken.None));
    }
}
