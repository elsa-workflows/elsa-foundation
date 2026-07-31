using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Acceptance coverage for #1001: an authored Event wait's correlation becomes durable bookmark metadata and
/// scopes the real router's fan-in resume path without changing the event's name-based stimulus identity.
/// </summary>
public sealed class EventCorrelationRoutingTests
{
    private const string EventName = "order-shipped";

    [Fact]
    public async Task Route_ResumesOnlyTheWaitingEventWithTheMatchingCorrelation()
    {
        var matchingIdentity = Identity("matching");
        var nonMatchingIdentity = Identity("non-matching");
        await using var matchingHarness = NewHarness(matchingIdentity, "wfexec-matching", "actexec-matching");
        await using var nonMatchingHarness = NewHarness(nonMatchingIdentity, "wfexec-non-matching", "actexec-non-matching");

        await matchingHarness.RunAsync(NewEventExecutable(matchingIdentity, "order-7"));
        await nonMatchingHarness.RunAsync(NewEventExecutable(nonMatchingIdentity, "order-8"));

        var matchingBookmark = await ReadBookmarkAsync(matchingHarness);
        var nonMatchingBookmark = await ReadBookmarkAsync(nonMatchingHarness);
        Assert.Equal("order-7", matchingBookmark.Metadata[RuntimeMetadataKeys.CorrelationId]);
        Assert.Equal("order-8", nonMatchingBookmark.Metadata[RuntimeMetadataKeys.CorrelationId]);

        // The two real harnesses own isolated in-memory persistence. Rehydrate the two actual persisted bookmark
        // records into the one cross-execution index the host-level router queries; resume dispatch still delegates
        // to each harness's real BookmarkResumeDispatcher and workflow actor.
        var sharedBookmarks = new InMemoryBookmarkStateStore();
        await sharedBookmarks.SaveAsync(matchingBookmark);
        await sharedBookmarks.SaveAsync(nonMatchingBookmark);
        var resumeDispatcher = new HarnessBookmarkResumeDispatcher(new Dictionary<string, IBookmarkResumeDispatcher>
        {
            [matchingHarness.ExecutionId] = matchingHarness.Services.GetRequiredService<IBookmarkResumeDispatcher>(),
            [nonMatchingHarness.ExecutionId] = nonMatchingHarness.Services.GetRequiredService<IBookmarkResumeDispatcher>()
        });
        var router = new StimulusRouter(
            new InMemoryWorkflowTriggerBindingStore(),
            new GlobalBookmarkStimulusLookup(sharedBookmarks),
            new UnexpectedStartDispatcher(),
            resumeDispatcher,
            new InMemoryStimulusStartDeduplicator());

        var result = await router.RouteAsync(new StimulusDispatchRequest(
            EventStimulus.StimulusType,
            EventStimulus.Hash(EventName),
            input: JsonSerializer.SerializeToElement(new EventReceived(EventName)),
            mode: StimulusRoutingMode.ResumeOnly,
            correlationId: "order-7",
            payloadType: new ValueTypeDescriptor(
                TypeAliasConvention.CanonicalAlias(typeof(EventReceived)),
                schemaVersion: 1),
            providerId: PublishEvent.ActivityType));

        Assert.Equal(1, result.ResumedCount);
        Assert.Equal(matchingHarness.ExecutionId, Assert.Single(result.Resumes).WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, resumeDispatcher.LastResult?.CommandDispatch?.Status);
        Assert.Empty(await BookmarksAsync(matchingHarness));
        Assert.Single(await BookmarksAsync(nonMatchingHarness));
        Assert.Equal(ActivityExecutionStatus.Completed, await ActivityStatusAsync(matchingHarness));
        Assert.Equal(WorkflowExecutionStatus.Completed, await WorkflowStatusAsync(matchingHarness));
        Assert.Equal(WorkflowExecutionStatus.Running, await WorkflowStatusAsync(nonMatchingHarness));
    }

    /// <summary>
    /// A local publish (<c>PublishEvent.IsLocalEvent</c>, #1117) delivers only inside the publishing execution.
    /// The two waits here carry no correlation at all — under a broadcast publish both would resume — so the
    /// single resume proves the target filter, not correlation, did the scoping.
    /// </summary>
    [Fact]
    public async Task Route_TargetedAtOneExecution_ResumesOnlyThatExecutionsWait()
    {
        var localIdentity = Identity("local");
        var siblingIdentity = Identity("sibling");
        await using var localHarness = NewHarness(localIdentity, "wfexec-local", "actexec-local");
        await using var siblingHarness = NewHarness(siblingIdentity, "wfexec-sibling", "actexec-sibling");

        await localHarness.RunAsync(NewEventExecutable(localIdentity, correlationId: null));
        await siblingHarness.RunAsync(NewEventExecutable(siblingIdentity, correlationId: null));

        var sharedBookmarks = new InMemoryBookmarkStateStore();
        await sharedBookmarks.SaveAsync(await ReadBookmarkAsync(localHarness));
        await sharedBookmarks.SaveAsync(await ReadBookmarkAsync(siblingHarness));
        var resumeDispatcher = new HarnessBookmarkResumeDispatcher(new Dictionary<string, IBookmarkResumeDispatcher>
        {
            [localHarness.ExecutionId] = localHarness.Services.GetRequiredService<IBookmarkResumeDispatcher>(),
            [siblingHarness.ExecutionId] = siblingHarness.Services.GetRequiredService<IBookmarkResumeDispatcher>()
        });
        var router = new StimulusRouter(
            new InMemoryWorkflowTriggerBindingStore(),
            new GlobalBookmarkStimulusLookup(sharedBookmarks),
            new UnexpectedStartDispatcher(),
            resumeDispatcher,
            new InMemoryStimulusStartDeduplicator());

        var result = await router.RouteAsync(new StimulusDispatchRequest(
            EventStimulus.StimulusType,
            EventStimulus.Hash(EventName),
            input: JsonSerializer.SerializeToElement(new EventReceived(EventName)),
            mode: StimulusRoutingMode.ResumeOnly,
            payloadType: new ValueTypeDescriptor(
                TypeAliasConvention.CanonicalAlias(typeof(EventReceived)),
                schemaVersion: 1),
            providerId: PublishEvent.ActivityType,
            targetWorkflowExecutionId: localHarness.ExecutionId));

        Assert.Equal(1, result.ResumedCount);
        Assert.Equal(localHarness.ExecutionId, Assert.Single(result.Resumes).WorkflowExecutionId);
        Assert.Empty(await BookmarksAsync(localHarness));
        Assert.Single(await BookmarksAsync(siblingHarness));
        Assert.Equal(WorkflowExecutionStatus.Completed, await WorkflowStatusAsync(localHarness));
        Assert.Equal(WorkflowExecutionStatus.Running, await WorkflowStatusAsync(siblingHarness));
    }

    /// <summary>
    /// Targeting one execution requires ResumeOnly. Enforcing it at construction is what lets the router keep a
    /// single start-suppression rule (the mode) instead of a second one keyed on the target.
    /// </summary>
    [Theory]
    [InlineData(StimulusRoutingMode.StartOnly)]
    [InlineData(StimulusRoutingMode.StartAndResume)]
    public void TargetedRequest_RequiresResumeOnlyRouting(StimulusRoutingMode mode) =>
        Assert.Throws<ArgumentException>(() => new StimulusDispatchRequest(
            EventStimulus.StimulusType,
            EventStimulus.Hash(EventName),
            mode: mode,
            targetWorkflowExecutionId: "wfexec-local"));

    private static WorkflowExecutionHarness NewHarness(
        WorkflowExecutableIdentity identity,
        string workflowExecutionId,
        string activityExecutionId) =>
        WorkflowExecutionHarness.Create().Build(identity, workflowExecutionId, [activityExecutionId]);

    private static WorkflowExecutableIdentity Identity(string suffix) =>
        new($"artifact-{suffix}", $"definition-{suffix}", $"version-{suffix}", "1.0.0", $"sha256:{suffix}");

    private static WorkflowExecutable NewEventExecutable(WorkflowExecutableIdentity identity, string? correlationId)
    {
        const string executableNodeId = "node-event";
        var resumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(executableNodeId, Event.ResumeTargetId);
        return new WorkflowExecutable(
            identity,
            EventNode(correlationId),
            new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [resumeTargetId] = new(
                    resumeTargetId,
                    executableNodeId,
                    "ResumeAsync",
                    new Dictionary<string, string>(),
                    Event.ResumeTargetId)
            },
            new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    private static async Task<BookmarkState> ReadBookmarkAsync(WorkflowExecutionHarness harness)
    {
        var bookmarks = await BookmarksAsync(harness);
        return Assert.Single(bookmarks);
    }

    private static ValueTask<IReadOnlyCollection<BookmarkState>> BookmarksAsync(WorkflowExecutionHarness harness) =>
        harness.Services.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(harness.ExecutionId);

    private static async Task<ActivityExecutionStatus> ActivityStatusAsync(WorkflowExecutionHarness harness) =>
        Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId)).Status;

    private static async Task<WorkflowExecutionStatus?> WorkflowStatusAsync(WorkflowExecutionHarness harness) =>
        (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))?.Status;

    private static ExecutableNode EventNode(string? correlationId)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            [nameof(Event.EventName)] = Literal(nameof(Event.EventName), EventName),
            [nameof(Event.CanStartWorkflow)] = Literal(nameof(Event.CanStartWorkflow), false)
        };
        if (correlationId is not null)
            inputBindings[nameof(Event.CorrelationId)] = Literal(nameof(Event.CorrelationId), correlationId);

        return new ExecutableNode(
            executableNodeId: "node-event",
            authoredActivityId: "authored-node-event",
            activityType: Event.ActivityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: inputBindings,
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

    private sealed class HarnessBookmarkResumeDispatcher(
        IReadOnlyDictionary<string, IBookmarkResumeDispatcher> dispatchers) : IBookmarkResumeDispatcher
    {
        public BookmarkResumeDispatchResult? LastResult { get; private set; }

        public async ValueTask<BookmarkResumeDispatchResult> DispatchAsync(
            BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            LastResult = dispatchers.TryGetValue(request.WorkflowExecutionId, out var dispatcher)
                ? await dispatcher.DispatchAsync(request, dispatchOptions, cancellationToken)
                : throw new InvalidOperationException($"No harness is registered for workflow execution '{request.WorkflowExecutionId}'.");
            return LastResult;
        }
    }

    private sealed class UnexpectedStartDispatcher : IWorkflowStartDispatcher
    {
        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A resume-only correlation routing test must not start a workflow.");
    }
}
