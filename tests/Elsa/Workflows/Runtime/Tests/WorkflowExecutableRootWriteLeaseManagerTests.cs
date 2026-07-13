using System.Text.Json;
using Elsa.Workflows.Runtime.Configuration;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowExecutableRootWriteLeaseManagerTests
{
    [Fact]
    public async Task ExecuteAsync_RenewsLeaseUntilWriteCompletes()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(Executable("artifact-1"));
        var manager = new WorkflowExecutableRootWriteLeaseManager(
            store,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions
            {
                // Keep enough scheduling headroom for the full parallel solution suite while still
                // waiting beyond the original lease to prove that renewal extended it.
                RootWriteLeaseDuration = TimeSpan.FromSeconds(2)
            }),
            TimeProvider.System);
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var writeTask = manager.ExecuteAsync("artifact-1", "writer-1", async cancellationToken =>
        {
            writeStarted.SetResult();
            await finishWrite.Task.WaitAsync(cancellationToken);
        }).AsTask();

        await writeStarted.Task;
        await Task.Delay(TimeSpan.FromMilliseconds(2500));

        var guardWhileWriting = await store.TryBeginDeletionAsync(
            "artifact-1",
            "gc-1",
            DateTimeOffset.UtcNow.AddMinutes(1),
            DateTimeOffset.UtcNow);

        Assert.Null(guardWhileWriting);

        finishWrite.SetResult();
        await writeTask;

        var now = DateTimeOffset.UtcNow;
        var guardAfterWrite = await store.TryBeginDeletionAsync("artifact-1", "gc-1", now.AddMinutes(1), now);
        Assert.NotNull(guardAfterWrite);
    }

    private static WorkflowExecutable Executable(string artifactId) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "activity-root",
                activityType: "Test.Root",
                activityTypeVersion: "1.0.0",
                descriptorType: "Test",
                descriptorPayload: JsonSerializer.SerializeToElement(new { }),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
                metadata: new Dictionary<string, string>()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
}
