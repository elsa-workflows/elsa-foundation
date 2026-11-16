using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class TestRunResumeFenceTests
{
    [Theory]
    [InlineData("open")]
    [InlineData("closed")]
    [InlineData("expired")]
    public async Task ReceiptAndBookmarkConsumption_ShareScopeFence(string scopeState)
    {
        var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var shared = new InMemoryRuntimeCheckpointStoreState();
        var scopes = new InMemoryWorkflowTestScopeStore(shared);
        var executions = new InMemoryWorkflowExecutionStateStore();
        var bookmarks = new InMemoryBookmarkStateStore();
        var testScope = new WorkflowTestScope("test-run", now.AddMinutes(30), null, new("default"));
        await scopes.CreateAsync(testScope, now);
        var execution = new WorkflowExecutionState("execution", new("artifact", "definition", "draft", "draft", "hash"),
            WorkflowExecutionStatus.Suspended, null, now, now, now, null, null, null, null, new Dictionary<string, string>())
            { RunKind = WorkflowRunKind.TestRun, TestScope = testScope };
        await executions.SaveAsync(execution);
        var wait = new BookmarkState("wait", "execution", "activity", "node", "resume", "Event", "hash", null, new Dictionary<string, string>(), now, null);
        await bookmarks.SaveAsync(wait);
        var receipt = new TestRunDeliveryReceipt("test-run", Guid.NewGuid().ToString("D"), "wait", TestRunDeliveryReceipt.Digest(null));
        var metadata = new Dictionary<string, string> { [TestRunDeliveryReceipt.MetadataKey] = receipt.DeliveryId };
        var commit = new RuntimeCheckpointCommit("commit", new("checkpoint", RuntimeCheckpointNames.BookmarkConsumed, "execution", now, ["activity"], metadata),
            new(new RuntimeStateChange<WorkflowExecutionState>("execution", RuntimeStateChangeOperation.Upsert, receipt.Record(execution), metadata), null, [],
                [new RuntimeStateChange<BookmarkState>("wait", RuntimeStateChangeOperation.Delete, wait, metadata)], [], [], []), [], metadata);
        var writer = new InMemoryRuntimeCheckpointCommitStore(executions, bookmarkStateStore: bookmarks,
            rootWriteLeaseManager: new Lease(), state: shared, timeProvider: clock);
        if (scopeState == "closed")
            await scopes.CloseAsync(new("test-run", WorkflowTestScopeCloseReason.ExplicitTeardown, now));
        if (scopeState == "expired")
            clock.Advance(TimeSpan.FromMinutes(30));

        if (scopeState == "open")
        {
            await writer.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Null(await bookmarks.FindAsync("execution", "wait"));
            Assert.True(receipt.MatchesExisting((await executions.FindAsync("execution"))!));
        }
        else
        {
            await Assert.ThrowsAsync<TestScopeAdmissionException>(() => writer.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
            Assert.NotNull(await bookmarks.FindAsync("execution", "wait"));
            Assert.Null(receipt.MatchesExisting((await executions.FindAsync("execution"))!));
        }
    }

    [Fact]
    public async Task ExactWaitSelector_DistinguishesTwoIdenticalEventWaits()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new InMemoryBookmarkStateStore();
        foreach (var id in new[] { "wait-1", "wait-2" })
            await store.SaveAsync(new(id, "execution", "activity-" + id, "node", "resume", "Event", "hash", null, new Dictionary<string, string>(), now, null));
        var lookup = new BookmarkStimulusLookup(store);
        Assert.Equal(BookmarkStimulusLookupStatus.Ambiguous, (await lookup.FindAsync(new("execution", "Event", "hash", now))).Status);
        Assert.Equal("wait-2", (await lookup.FindAsync(new("execution", "Event", "hash", now, "wait-2"))).Bookmark!.BookmarkId);
        Assert.Equal(BookmarkStimulusLookupStatus.NotFound, (await lookup.FindAsync(new("execution", "Event", "hash", now, "missing"))).Status);
    }

    private sealed class Lease : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) => write(cancellationToken);
    }
}
