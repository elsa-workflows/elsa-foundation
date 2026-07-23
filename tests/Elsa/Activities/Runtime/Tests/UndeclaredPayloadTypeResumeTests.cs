using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Acceptance coverage for #1014: a stimulus published by a source that does not declare a typed payload
/// contract (the runtime stimuli REST endpoint, the in-workflow <see cref="PublishEvent"/> intent, the recurring
/// trigger pump) must still resume a mid-flow wait. The waiting activity's committed trigger registration is the
/// authority on the payload contract; an undeclared delivery adopts it instead of being silently dropped.
/// </summary>
public sealed class UndeclaredPayloadTypeResumeTests : IAsyncDisposable
{
    private const string EventName = "order-shipped";
    private const string ExecutableNodeId = "node-event";

    private readonly WorkflowExecutionHarness _harness =
        WorkflowExecutionHarness.Create().Build(WorkflowExecutionHarness.Identity, "wfexec-1", ["actexec-1"]);

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Resume_WithoutDeclaredPayloadType_CompletesTheWaitingActivity()
    {
        var bookmark = await SuspendOnEventWaitAsync();

        var result = await _harness.Services.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(
            new BookmarkResumeDispatchRequest(
                workflowExecutionId: _harness.ExecutionId,
                stimulusType: bookmark.StimulusType,
                stimulusHash: bookmark.StimulusHash,
                input: JsonSerializer.SerializeToElement(new EventReceived(EventName))));

        Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, result.Status);
        await AssertResumedToCompletionAsync();
    }

    [Fact]
    public async Task Route_WithoutDeclaredPayloadType_ResumesTheWaitingExecution()
    {
        var bookmark = await SuspendOnEventWaitAsync();

        // The host-level cross-execution index the router queries, rehydrated from the harness's own persisted
        // bookmark; resume dispatch still runs through the harness's real dispatcher and workflow actor.
        var index = new InMemoryBookmarkStateStore();
        await index.SaveAsync(bookmark);
        var router = new StimulusRouter(
            new InMemoryWorkflowTriggerBindingStore(),
            new GlobalBookmarkStimulusLookup(index),
            new UnexpectedStartDispatcher(),
            _harness.Services.GetRequiredService<IBookmarkResumeDispatcher>(),
            new InMemoryStimulusStartDeduplicator());

        var result = await router.RouteAsync(new StimulusDispatchRequest(
            EventStimulus.StimulusType,
            EventStimulus.Hash(EventName),
            input: JsonSerializer.SerializeToElement(new EventReceived(EventName)),
            mode: StimulusRoutingMode.ResumeOnly));

        Assert.Equal(1, result.ResumedCount);
        await AssertResumedToCompletionAsync();
    }

    private async Task<BookmarkState> SuspendOnEventWaitAsync()
    {
        await _harness.RunAsync(NewEventExecutable(WorkflowExecutionHarness.Identity));
        return Assert.Single(await BookmarksAsync(_harness));
    }

    private async Task AssertResumedToCompletionAsync()
    {
        Assert.Empty(await BookmarksAsync(_harness));
        Assert.Equal(ActivityExecutionStatus.Completed, await ActivityStatusAsync(_harness));
        Assert.Equal(WorkflowExecutionStatus.Completed, await WorkflowStatusAsync(_harness));
    }

    private static ValueTask<IReadOnlyCollection<BookmarkState>> BookmarksAsync(WorkflowExecutionHarness harness) =>
        harness.Services.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(harness.ExecutionId);

    private static async Task<ActivityExecutionStatus> ActivityStatusAsync(WorkflowExecutionHarness harness) =>
        Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId)).Status;

    private static async Task<WorkflowExecutionStatus?> WorkflowStatusAsync(WorkflowExecutionHarness harness) =>
        (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))?.Status;

    private static WorkflowExecutable NewEventExecutable(WorkflowExecutableIdentity identity)
    {
        var resumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(ExecutableNodeId, Event.ResumeTargetId);
        return new WorkflowExecutable(
            identity,
            EventNode(),
            new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [resumeTargetId] = new(
                    resumeTargetId,
                    ExecutableNodeId,
                    "ResumeAsync",
                    new Dictionary<string, string>(),
                    Event.ResumeTargetId)
            },
            new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string>());
    }

    private static ExecutableNode EventNode()
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new ExecutableNode(
            executableNodeId: ExecutableNodeId,
            authoredActivityId: "authored-node-event",
            activityType: Event.ActivityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(Event.EventName)] = Literal(nameof(Event.EventName), EventName),
                [nameof(Event.CanStartWorkflow)] = Literal(nameof(Event.CanStartWorkflow), false)
            },
            metadata: new Dictionary<string, string>());
    }

    private static RuntimeInputBinding Literal(string inputName, object value)
    {
        var type = new ValueTypeDescriptor(value is bool ? "Boolean" : "String");
        return new RuntimeInputBinding(
            inputName,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }

    private sealed class UnexpectedStartDispatcher : IWorkflowStartDispatcher
    {
        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A resume-only routing test must not start a workflow.");
    }
}
