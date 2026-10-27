using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Services;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.InProcess;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// #3107: a Publish Event delivered by its post-commit handler resumes a waiting mid-flow Event, with or without a
/// payload and whatever the payload's shape. The delivery takes the real path: the staging buffer builds the intent,
/// <see cref="PublishStimulusExecutor"/> routes the payload as the stimulus input, and the real router resumes the
/// parked wait through the actual source-owned router/default dispatcher and queued callback. Constructed schema
/// observations qualify this native component only; no host-process or durable recovery claim is made.
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
        await using var harness = WorkflowExecutionHarness.Create().ConfigureServices(services =>
        {
            services.AddWorkflowStimulusRouter();
            services.AddSingleton<IPersistenceAccessContextAccessor, Access>();
            services.AddSingleton<ISchemaDormancyCheck>(new SchemaDormancyCheck(observed: new PublishStimulusSourcePipelineTests.Observed("finalized")));
        }).Build(identity, "wfexec-wait", ["actexec-wait"]);
        await harness.RunAsync(EventWaitExecutable.Create(identity, EventName));
        var payload = payloadJson is null ? (JsonElement?)null : JsonDocument.Parse(payloadJson).RootElement.Clone();
        using var scope = harness.Services.CreateScope(); var services = scope.ServiceProvider;
        var buffer = services.GetRequiredService<IPublishStimulusStager>();
        // A local publish targets the publishing execution, which here is the waiting one.
        buffer.StagePublishStimulus(new PublishStimulusRequest(harness.ExecutionId, "actexec-publish", EventName, payload: payload, isLocalEvent: isLocalEvent));
        var intent = Assert.Single(services.GetRequiredService<IPublishStimulusStagingAccessor>().TakePublishStimuli(harness.ExecutionId, "actexec-publish"));
        var commit = new RuntimeCheckpointCommit("publish-resume", new("publish-resume-checkpoint", RuntimeCheckpointNames.PostCommitIntentRecorded,
            harness.ExecutionId, intent.RecordedAt, ["actexec-publish"], new Dictionary<string, string>()), new(null, null, [], [], [], [], []),
            [intent], new Dictionary<string, string>());
        await services.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(commit);
        var processed = await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10, workflowExecutionId: harness.ExecutionId));
        var saved = (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync(RuntimePostCommitOutboxItems.OutboxItemId(commit.CommitId, intent)))!;
        Assert.True(processed.DeliveredCount == 1, $"processed={JsonSerializer.Serialize(processed)},source={saved.Status},failure={saved.LastFailureMessage}");
        Assert.Equal(1, saved.StimulusDeliveryProtocolVersion); Assert.Single(saved.RecipientSnapshot!.Resumes);

        var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        Assert.Equal(ActivityExecutionStatus.Completed, activity.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))?.Status);
        var result = activity.Completion!.Result.InlineValue!.Value;
        Assert.Equal(EventName, result.GetProperty("eventName").GetString());
        var delivered = result.TryGetProperty("payload", out var value) ? value : default;
        var expected = payload is { ValueKind: not JsonValueKind.Null } ? payload.Value.GetRawText() : "null";
        Assert.Equal(expected, delivered.ValueKind == JsonValueKind.Undefined ? "null" : delivered.GetRawText());
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(PersistenceScope.DefaultValue));
    }
}
