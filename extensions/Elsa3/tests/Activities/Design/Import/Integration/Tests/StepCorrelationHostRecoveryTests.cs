using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Primitives.Activities;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

public sealed class StepCorrelationHostRecoveryTests
{
    [Fact]
    public async Task Accepted_body_identity_change_preserves_old_wait_and_resumes_explicit_new_identity_wait_after_EF_host_restart()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var collection = JsonNode.Parse(Elsa3ImportHost.Collection("script-workflow-state"))!;
        var root = collection[0]!["root"]!;
        var writer = root["activities"]![0]!.DeepClone();
        writer["script"]!["expression"]!["value"] = "setCorrelationId('new'); setWorkflowInstanceName('Changed'); return getCorrelationId();";
        var eventSource = JsonNode.Parse(Elsa3ImportHost.Collection("publish-event-waiter"))![0]!["root"]!["activities"]![0]!;
        JsonNode Wait(string id)
        {
            var node = eventSource.DeepClone();
            node["id"] = id; node["nodeId"] = id; node["name"] = id;
            return node;
        }
        var start = new JsonObject { ["id"] = "start", ["nodeId"] = "start", ["name"] = "start", ["type"] = "Elsa.WriteLine", ["version"] = 1,
            ["text"] = new JsonObject { ["typeName"] = "String", ["expression"] = new JsonObject { ["type"] = "Literal", ["value"] = "start" } } };
        var gate = Wait("change-gate");
        gate["eventName"]!["expression"]!["value"] = "change-identity";
        root["activities"] = new JsonArray(start, Wait("old-wait"), gate, writer, Wait("new-wait"));
        JsonNode Edge(string from, string to) => new JsonObject { ["source"] = new JsonObject { ["activity"] = from, ["port"] = "Done" },
            ["target"] = new JsonObject { ["activity"] = to, ["port"] = "In" } };
        root["connections"] = new JsonArray(Edge("start", "old-wait"), Edge("start", "change-gate"), Edge("change-gate", "seed"), Edge("seed", "new-wait"));
        var handle = await host.UploadCollectionJsonAsync(collection.ToJsonString());
        var (plan, _) = await host.AnalyzeAsync(handle);
        await host.SelectAsync(handle, plan, "script-workflow-state-v1");
        var applied = await host.ApplyAsync(handle, plan, "correlation-restart", "script-workflow-state-v1");
        Assert.True(applied.StatusCode == HttpStatusCode.Created, applied.Body.ToJsonString());
        var importedVersion = Elsa3ImportHost.Required(applied.Body["sources"]![0]!, "workflowVersionId");
        var converted = await host.GetJsonAsync($"design/workflows/versions/{importedVersion}");
        var state = converted["state"]!.DeepClone();
        var nodes = state["rootActivity"]!["structure"]!["payload"]!["activities"]!.AsArray();
        // Elsa 3's Event export has no mapped correlation member. Bind the existing native Event input explicitly
        // after conversion; the tested body is still the actual imported/lowered S-13 program.
        var expression = new ExpressionDefinition("JavaScript", "args.correlation", new TypeReference("String"),
            new Dictionary<string, ExpressionParameterBinding> { ["correlation"] = new WorkflowInstanceExpressionParameterBinding(WorkflowInstanceExpressionParameterBinding.Members.CorrelationId) },
            JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingReadonlyV1);
        foreach (var node in nodes.Where(node => (string?)node!["nodeId"] is "old-wait" or "new-wait"))
            node!["inputs"]!.AsArray().Add(new JsonObject { ["referenceKey"] = "CorrelationId", ["value"] = new JsonObject
            {
                ["expressionType"] = (string?)node["nodeId"] == "old-wait" ? "Literal" : "JavaScript",
                ["value"] = (string?)node["nodeId"] == "old-wait" ? JsonValue.Create("old") : JsonSerializer.SerializeToNode(expression, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            } });
        var authored = await host.PostAsync("design/workflows/definitions/submit", new JsonObject { ["name"] = "State and explicit wait correlations", ["state"] = state });
        var workflow = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(authored, "version", "id"));
        var id = await host.ExecuteAsync(workflow);
        await host.WaitForAsync(id, run => new[] { "old-wait", "change-gate" }.All(node =>
            run.Activities.Any(activity => activity.Execution.AuthoredActivityId == node && activity.Status == ActivityExecutionStatus.Suspended)));
        await host.PostAsync("runtime/workflows/stimuli", new JsonObject { ["stimulusType"] = EventStimulus.StimulusType,
            ["stimulusHash"] = EventStimulus.Hash("change-identity"), ["mode"] = "ResumeOnly", ["input"] = new JsonObject { ["eventName"] = "change-identity" } });
        var waiting = await host.WaitForAsync(id, run => new[] { "old-wait", "new-wait" }.All(node =>
            run.Activities.Any(activity => activity.Execution.AuthoredActivityId == node && activity.Status == ActivityExecutionStatus.Suspended)));
        Assert.Equal("new", waiting.Workflow!.CorrelationId);
        var before = await host.ReadAsync(services => services.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(id).AsTask());
        Assert.Equal("old", Assert.Single(before, bookmark => bookmark.ExecutableNodeId == "old-wait").Metadata[RuntimeMetadataKeys.CorrelationId]);
        Assert.Equal("new", Assert.Single(before, bookmark => bookmark.ExecutableNodeId == "new-wait").Metadata[RuntimeMetadataKeys.CorrelationId]);
        var acceptedAt = StepWorkflowStateEffects.ReadReceipts(Assert.Single(waiting.Activities, activity => activity.Execution.AuthoredActivityId == "seed").Metadata).Single().AcceptedAt;
        Assert.True(Assert.Single(before, bookmark => bookmark.ExecutableNodeId == "old-wait").CreatedAt <= acceptedAt);

        await using var restarted = await host.RestartAsync();
        var after = await restarted.ReadAsync(services => services.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(id).AsTask());
        Assert.Equal(before.OrderBy(bookmark => bookmark.BookmarkId).Select(bookmark => (bookmark.BookmarkId, bookmark.Metadata[RuntimeMetadataKeys.CorrelationId])),
            after.OrderBy(bookmark => bookmark.BookmarkId).Select(bookmark => (bookmark.BookmarkId, bookmark.Metadata[RuntimeMetadataKeys.CorrelationId])));
        var retained = await restarted.ReadRunAsync(id);
        foreach (var (node, key) in new[] { ("old-wait", "old"), ("new-wait", "new") })
            Assert.Equal(key, Assert.Single(Assert.Single(retained.Activities, activity => activity.Execution.AuthoredActivityId == node).TriggerRegistrations!).Metadata[RuntimeMetadataKeys.CorrelationId]);
        async Task<int> Route(string key) => await restarted.ReadAsync(async services =>
            (await services.GetRequiredService<IStimulusRouter>().RouteAsync(new StimulusDispatchRequest(EventStimulus.StimulusType,
                EventStimulus.Hash("order.approved"), input: JsonSerializer.SerializeToElement(new { eventName = "order.approved" }),
                correlationId: key, mode: StimulusRoutingMode.ResumeOnly, payloadType: new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(EventReceived)), schemaVersion: 1),
                providerId: PublishEvent.ActivityType) { ResumeAllMatchingBookmarks = true })).ResumedCount);
        Assert.Equal(0, await Route("unmatched"));
        Assert.Equal(1, await Route("old"));
        var oldResumed = await restarted.WaitForAsync(id, run => run.Activities.Any(activity => activity.Execution.AuthoredActivityId == "old-wait" && activity.Status == ActivityExecutionStatus.Completed));
        Assert.Equal(ActivityExecutionStatus.Suspended, Assert.Single(oldResumed.Activities, activity => activity.Execution.AuthoredActivityId == "new-wait").Status);
        Assert.Equal(1, await Route("new"));
        var completed = await restarted.WaitForSettledAsync(id);
        Assert.Equal(WorkflowExecutionStatus.Completed, completed.Workflow!.Status);
        Assert.Equal("new", completed.Workflow.CorrelationId);
    }
}
