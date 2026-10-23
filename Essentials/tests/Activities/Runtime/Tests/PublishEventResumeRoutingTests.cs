using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Services;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// #3107: a Publish Event delivered by its post-commit handler resumes a waiting mid-flow Event, with or without a
/// payload and whatever the payload's shape. The delivery takes the real path: the staging buffer builds the intent,
/// <see cref="PublishStimulusExecutor"/> routes the payload as the stimulus input, and the real router resumes the
/// parked wait through the harness's resume dispatcher.
/// </summary>
public sealed class PublishEventResumeRoutingTests
{
    private const string EventName = "order.approved";

    public static TheoryData<string?, bool> Payloads => new()
    {
        { null, false },
        { "null", false },
        { """{"orderId":"1"}""", false },
        { "\"text\"", false },
        { "[1,2]", false },
        { "42", false },
        { "true", false },
        { null, true },
        { """{"orderId":"1"}""", true }
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public async Task PublishEvent_resumes_the_waiting_event(string? payloadJson, bool isLocalEvent)
    {
        var identity = new WorkflowExecutableIdentity("artifact-wait", "definition-wait", "version-wait", "1.0.0", "sha256:wait");
        await using var harness = WorkflowExecutionHarness.Create().Build(identity, "wfexec-wait", ["actexec-wait"]);
        await harness.RunAsync(EventWaitExecutable.Create(identity, EventName));
        var bookmarks = new InMemoryBookmarkStateStore();
        await bookmarks.SaveAsync(Assert.Single(await harness.Services.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(harness.ExecutionId)));
        var router = new StimulusRouter(
            new InMemoryWorkflowTriggerBindingStore(),
            new GlobalBookmarkStimulusLookup(bookmarks),
            new NoStartDispatcher(),
            harness.Services.GetRequiredService<IBookmarkResumeDispatcher>());

        var payload = payloadJson is null ? (JsonElement?)null : JsonDocument.Parse(payloadJson).RootElement.Clone();
        var buffer = new PublishStimulusStagingBuffer(TimeProvider.System);
        // A local publish targets the publishing execution, which here is the waiting one.
        buffer.StagePublishStimulus(new PublishStimulusRequest(harness.ExecutionId, "actexec-publish", EventName, payload: payload, isLocalEvent: isLocalEvent));
        await new PublishStimulusExecutor(router).HandleAsync(Assert.Single(buffer.TakePublishStimuli(harness.ExecutionId, "actexec-publish")));

        var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        Assert.Equal(ActivityExecutionStatus.Completed, activity.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))?.Status);
        var result = activity.Completion!.Result.InlineValue!.Value;
        Assert.Equal(EventName, result.GetProperty("eventName").GetString());
        var delivered = result.TryGetProperty("payload", out var value) ? value : default;
        var expected = payload is { ValueKind: not JsonValueKind.Null } ? payload.Value.GetRawText() : "null";
        Assert.Equal(expected, delivered.ValueKind == JsonValueKind.Undefined ? "null" : delivered.GetRawText());
    }

    private sealed class NoStartDispatcher : IWorkflowStartDispatcher
    {
        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No published workflow starts on this event; the router must not dispatch a start.");
    }
}
