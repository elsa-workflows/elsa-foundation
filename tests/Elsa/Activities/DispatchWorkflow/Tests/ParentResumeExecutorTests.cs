using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class ParentResumeExecutorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 13, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(BookmarkResumeDispatchStatus.Deferred)]
    [InlineData(BookmarkResumeDispatchStatus.Rejected)]
    [InlineData(BookmarkResumeDispatchStatus.Duplicate)]
    [InlineData(BookmarkResumeDispatchStatus.NotFound)]
    public async Task PresentBookmark_RemainsRetryable_AfterDispatcherOutcome(
        BookmarkResumeDispatchStatus dispatchStatus)
    {
        var fixture = await NewFixtureAsync(
            withBookmark: true,
            ActivityExecutionStatus.Suspended,
            dispatchStatus: dispatchStatus);

        var exception = await Assert.ThrowsAsync<ParentResumeDeferredException>(
            () => fixture.Executor.HandleAsync(NewIntent()).AsTask());

        Assert.Equal(Identity().DispatchId, exception.DispatchId);
        var request = Assert.Single(fixture.Dispatcher.Requests);
        Assert.Equal("parent-resume", request.WorkflowExecutionId);
        Assert.Equal(Identity().WaitStimulusHash, request.StimulusHash);
        Assert.Equal(Identity().ParentResumeIdempotencyKey, request.IdempotencyKey);
        Assert.NotNull(request.Input);
    }

    [Fact]
    public async Task MissingBookmarkWithCompletedActivity_IsIdempotentSuccess()
    {
        var fixture = await NewFixtureAsync(withBookmark: false, ActivityExecutionStatus.Completed);

        await fixture.Executor.HandleAsync(NewIntent());

        Assert.Single(fixture.Dispatcher.Requests);
    }

    [Fact]
    public async Task MissingBookmarkWithSuspendedActivity_RemainsRetryable()
    {
        var fixture = await NewFixtureAsync(withBookmark: false, ActivityExecutionStatus.Suspended);

        await Assert.ThrowsAsync<ParentResumeDeferredException>(
            () => fixture.Executor.HandleAsync(NewIntent()).AsTask());
    }

    [Theory]
    [InlineData(BookmarkResumeDispatchStatus.Deferred)]
    [InlineData(BookmarkResumeDispatchStatus.Rejected)]
    [InlineData(BookmarkResumeDispatchStatus.Duplicate)]
    [InlineData(BookmarkResumeDispatchStatus.NotFound)]
    public async Task DispatcherOutcome_IsAcknowledgedOnlyAfterAuthoritativeRecheck(
        BookmarkResumeDispatchStatus dispatchStatus)
    {
        var fixture = await NewFixtureAsync(
            withBookmark: false,
            ActivityExecutionStatus.Completed,
            dispatchStatus: dispatchStatus);

        await fixture.Executor.HandleAsync(NewIntent());

        Assert.Single(fixture.Dispatcher.Requests);
    }

    [Fact]
    public async Task CompletedParent_IsIdempotentSuccess_EvenWhenBookmarkRemains()
    {
        var fixture = await NewFixtureAsync(
            withBookmark: true,
            ActivityExecutionStatus.Suspended,
            workflowStatus: WorkflowExecutionStatus.Completed,
            dispatchStatus: BookmarkResumeDispatchStatus.Deferred);

        await fixture.Executor.HandleAsync(NewIntent());

        Assert.Single(fixture.Dispatcher.Requests);
    }

    [Fact]
    public async Task RemovedParent_IsIdempotentSuccess()
    {
        var fixture = await NewFixtureAsync(
            withBookmark: true,
            ActivityExecutionStatus.Suspended,
            workflowStatus: null,
            dispatchStatus: BookmarkResumeDispatchStatus.Rejected);

        await fixture.Executor.HandleAsync(NewIntent());

        Assert.Single(fixture.Dispatcher.Requests);
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Running)]
    [InlineData(WorkflowExecutionStatus.Suspended)]
    public async Task NonterminalParentWithMissingBookmarkAndActivity_RemainsRetryable(
        WorkflowExecutionStatus workflowStatus)
    {
        var fixture = await NewFixtureAsync(
            withBookmark: false,
            activityStatus: null,
            workflowStatus: workflowStatus,
            dispatchStatus: BookmarkResumeDispatchStatus.Duplicate);

        await Assert.ThrowsAsync<ParentResumeDeferredException>(
            () => fixture.Executor.HandleAsync(NewIntent()).AsTask());
    }

    [Fact]
    public async Task MismatchedResolvedBookmark_IsRejectedBeforeAuthoritativeRecheck()
    {
        var fixture = await NewFixtureAsync(
            withBookmark: false,
            ActivityExecutionStatus.Completed,
            dispatchStatus: BookmarkResumeDispatchStatus.Duplicate,
            resolvedBookmark: NewBookmark() with { StimulusHash = "sha256:conflicting-resolved-bookmark" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Executor.HandleAsync(NewIntent()).AsTask());

        Assert.Contains(Identity().WaitBookmarkId, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MismatchedStoredBookmark_IsRejectedDuringAuthoritativeRecheck()
    {
        var fixture = await NewFixtureAsync(
            withBookmark: true,
            ActivityExecutionStatus.Suspended,
            storedBookmark: NewBookmark() with { ResumeTargetId = "conflicting-resume-target" });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Executor.HandleAsync(NewIntent()).AsTask());

        Assert.Contains(Identity().WaitBookmarkId, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongKind_IsRejectedBeforeDispatch()
    {
        var fixture = await NewFixtureAsync(withBookmark: true, ActivityExecutionStatus.Suspended);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Executor.HandleAsync(NewIntent(kind: "Unsupported.Kind")).AsTask());

        Assert.Empty(fixture.Dispatcher.Requests);
    }

    [Fact]
    public async Task MissingOrInvalidPayload_IsRejectedBeforeDispatch()
    {
        var fixture = await NewFixtureAsync(withBookmark: true, ActivityExecutionStatus.Suspended);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Executor.HandleAsync(NewIntent(includePayload: false)).AsTask());
        await Assert.ThrowsAnyAsync<JsonException>(
            () => fixture.Executor.HandleAsync(NewIntent(payload: JsonSerializer.SerializeToElement("invalid"))).AsTask());

        Assert.Empty(fixture.Dispatcher.Requests);
    }

    [Fact]
    public async Task MismatchedDeterministicIntentIdentity_IsRejectedBeforeDispatch()
    {
        var fixture = await NewFixtureAsync(withBookmark: true, ActivityExecutionStatus.Suspended);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Executor.HandleAsync(NewIntent(intentId: "intent:conflicting")).AsTask());

        Assert.Contains("deterministic dispatch identity", exception.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Dispatcher.Requests);
    }

    private static async Task<Fixture> NewFixtureAsync(
        bool withBookmark,
        ActivityExecutionStatus? activityStatus,
        WorkflowExecutionStatus? workflowStatus = WorkflowExecutionStatus.Running,
        BookmarkResumeDispatchStatus dispatchStatus = BookmarkResumeDispatchStatus.NotFound,
        BookmarkState? resolvedBookmark = null,
        BookmarkState? storedBookmark = null)
    {
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        var activityStore = new InMemoryActivityExecutionStateStore();
        var bookmarkStore = new InMemoryBookmarkStateStore();
        if (workflowStatus is { } retainedWorkflowStatus)
        {
            await workflowStore.SaveAsync(new WorkflowExecutionState(
                "parent-resume",
                new WorkflowExecutableIdentity("artifact-parent", "definition-parent", "version-parent", "1.0.0", "sha256:parent"),
                retainedWorkflowStatus,
                null,
                Now.AddMinutes(-2),
                Now.AddMinutes(-2),
                Now,
                retainedWorkflowStatus.IsTerminal() ? Now : null,
                null,
                null,
                null,
                new Dictionary<string, string>()));
        }

        if (activityStatus is { } retainedActivityStatus)
            await activityStore.SaveAsync(NewActivityState(retainedActivityStatus, withBookmark));
        if (withBookmark)
            await bookmarkStore.SaveAsync(storedBookmark ?? NewBookmark());
        var dispatcher = new StubBookmarkResumeDispatcher(dispatchStatus, resolvedBookmark);
        return new Fixture(
            new ParentResumeExecutor(dispatcher, workflowStore, activityStore, bookmarkStore),
            dispatcher);
    }

    private static RuntimePostCommitIntent NewIntent(
        string? kind = null,
        string? intentId = null,
        JsonElement? payload = null,
        bool includePayload = true)
    {
        var identity = Identity();
        var result = new DispatchWorkflowResult(identity.ChildWorkflowExecutionId, WorkflowDispatchStatus.Completed);
        var serializedPayload = payload ?? JsonSerializer.SerializeToElement(
            new WorkflowDispatchParentResumePayload(
                identity.DispatchId,
                "parent-resume",
                "activity-resume",
                identity.ChildWorkflowExecutionId,
                identity.WaitBookmarkId,
                DispatchWorkflowConstants.WaitStimulusType,
                identity.WaitStimulusHash,
                result),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new RuntimePostCommitIntent(
            intentId ?? identity.ParentResumeIntentId,
            "parent-resume",
            kind ?? DispatchWorkflowConstants.ResumeParentIntentKind,
            Now,
            "activity-resume",
            identity.ParentResumeIdempotencyKey,
            includePayload ? serializedPayload : null);
    }

    private static ActivityExecutionState NewActivityState(ActivityExecutionStatus status, bool withBookmark)
    {
        var execution = new ActivityExecution("activity-resume", "parent-resume", "node-dispatch", "dispatch", DispatchWorkflowConstants.ActivityType, "1.0.0");
        return new ActivityExecutionState(
            execution,
            status,
            null,
            1,
            Now.AddMinutes(-2),
            Now.AddMinutes(-2),
            status == ActivityExecutionStatus.Completed ? Now : null,
            null,
            null,
            null,
            null,
            ActivitySchedulingProvenance.From("parent-resume", null, null, null, null, null, null, null),
            0,
            withBookmark ? [Identity().WaitBookmarkId] : [],
            [],
            0,
            0,
            new Dictionary<string, string>());
    }

    private static BookmarkState NewBookmark() => new(
        Identity().WaitBookmarkId,
        "parent-resume",
        "activity-resume",
        "node-dispatch",
        DispatchWorkflowConstants.CompletionResumeTargetId,
        DispatchWorkflowConstants.WaitStimulusType,
        Identity().WaitStimulusHash,
        null,
        new Dictionary<string, string>(),
        Now,
        null);

    private static WorkflowDispatchIdentity Identity() => new("parent-resume", "activity-resume");

    private sealed record Fixture(ParentResumeExecutor Executor, StubBookmarkResumeDispatcher Dispatcher);

    private sealed class StubBookmarkResumeDispatcher(
        BookmarkResumeDispatchStatus status,
        BookmarkState? resolvedBookmark) : IBookmarkResumeDispatcher
    {
        public List<BookmarkResumeDispatchRequest> Requests { get; } = [];

        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(
            BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new BookmarkResumeDispatchResult(
                status,
                request.WorkflowExecutionId,
                bookmark: resolvedBookmark,
                reason: "test dispatcher defers to authoritative state"));
        }
    }
}
