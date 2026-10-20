using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// #3107: Elsa 3 workflows commonly pair a mid-flow Event with a Publish Event in another workflow. Imported, the
/// waiting workflow resumes and completes when the publisher runs, with or without a payload, and the Event hands the
/// payload on as its <c>Payload</c> output (the Elsa 3 Event <c>Result</c>).
/// </summary>
public sealed class PublishEventResumeTests(ITestOutputHelper output)
{
    /// <summary>
    /// An Elsa 3 <c>Object</c> literal is the text the author typed. The import keeps it as text, so the imported
    /// Publish Event sends, and the Event receives, that JSON string unchanged.
    /// </summary>
    internal const string Elsa3ObjectLiteralPayload = "\"{\\u0022orderId\\u0022:\\u00221\\u0022}\"";

    [Theory]
    [InlineData("publish-event-publisher", null)]
    [InlineData("publish-event-publisher-payload", Elsa3ObjectLiteralPayload)]
    public async Task An_imported_Publish_Event_resumes_an_imported_Event_wait(string publisherFixture, string? expectedPayload)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync($"import-{publisherFixture}", ["publish-event-waiter", publisherFixture]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var waiter = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("publish-event-waiter-v1"), "workflowVersionId"));
        var publisher = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item($"{publisherFixture}-v1"), "workflowVersionId"));

        var waiting = await host.ExecuteAsync(waiter);
        await host.WaitForAsync(waiting, run => run.Activities.Any(state => IsEvent(state) && state.Status == ActivityExecutionStatus.Suspended));
        var published = await host.RunAsync(publisher);
        var run = await host.WaitForSettledAsync(waiting);
        output.WriteLine(run.Describe());

        Assert.True(published.Workflow?.Status == WorkflowExecutionStatus.Completed, published.Describe());
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        Assert.Equal(expectedPayload ?? "null", PayloadOf(Assert.Single(run.Activities, IsEvent)));
    }

    /// <summary>The stimulus API still resumes the wait; its input is now the event's payload.</summary>
    [Fact]
    public async Task The_stimulus_API_still_resumes_an_imported_Event_wait()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-stimulus-api", ["publish-event-waiter"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var waiter = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("publish-event-waiter-v1"), "workflowVersionId"));

        var waiting = await host.ExecuteAsync(waiter);
        await host.WaitForAsync(waiting, run => run.Activities.Any(state => IsEvent(state) && state.Status == ActivityExecutionStatus.Suspended));
        var routed = await host.PostAsync("runtime/workflows/stimuli", new JsonObject
        {
            ["stimulusType"] = EventStimulus.StimulusType,
            ["stimulusHash"] = EventStimulus.Hash("order.approved"),
            ["input"] = new JsonObject { ["eventName"] = "order.approved" },
            ["mode"] = "ResumeOnly"
        });
        Assert.Equal(1, (int?)routed["resumedCount"]);
        var run = await host.WaitForSettledAsync(waiting);

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Equal("""{"eventName":"order.approved"}""", PayloadOf(Assert.Single(run.Activities, IsEvent)));
    }

    internal static bool IsEvent(ActivityExecutionState state) =>
        state.Execution.ActivityType.EndsWith(".Event", StringComparison.Ordinal);

    /// <summary>The raw JSON of the completed Event's <c>Payload</c> output; <c>null</c> when it carried none.</summary>
    internal static string PayloadOf(ActivityExecutionState state)
    {
        var result = state.Completion?.Result.InlineValue
            ?? throw new Xunit.Sdk.XunitException($"Event '{state.Execution.ExecutableNodeId}' has no inline result ({state.Status}).");
        return result.TryGetProperty("payload", out var payload) ? payload.GetRawText() : "null";
    }
}
