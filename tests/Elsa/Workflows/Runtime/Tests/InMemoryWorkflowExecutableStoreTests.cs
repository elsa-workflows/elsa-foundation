using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class InMemoryWorkflowExecutableStoreTests
{
    [Fact]
    public async Task SaveBatchAsync_preserves_existing_members_and_adds_missing_members()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(Executable("artifact-existing", "1"));

        await store.SaveBatchAsync(
        [
            Executable("artifact-existing", "2"),
            Executable("artifact-new", "1")
        ]);

        Assert.Equal("1", (await store.FindAsync("artifact-existing"))!.Identity.ArtifactVersion);
        Assert.Equal("1", (await store.FindAsync("artifact-new"))!.Identity.ArtifactVersion);
    }

    [Fact]
    public async Task SaveBatchAsync_rejects_duplicate_ids_without_writing_any_member()
    {
        var store = new InMemoryWorkflowExecutableStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveBatchAsync(
        [
            Executable("artifact-a", "1"),
            Executable("artifact-b", "1"),
            Executable("artifact-a", "2")
        ]).AsTask());

        Assert.Null(await store.FindAsync("artifact-a"));
        Assert.Null(await store.FindAsync("artifact-b"));
    }

    private static WorkflowExecutable Executable(string artifactId, string artifactVersion)
    {
        using var document = JsonDocument.Parse("{\"type\":\"test\"}");
        return new WorkflowExecutable(
            new WorkflowExecutableIdentity(artifactId, "definition", "version", artifactVersion, $"sha256:{artifactId}"),
            new ExecutableNode(
                "node",
                "authored-node",
                "test/activity",
                "1.0.0",
                "test",
                document.RootElement,
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, string>(),
                outputCaptures: new Dictionary<string, RuntimeOutputCapture>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }
}
