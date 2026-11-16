using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Primitives.Activities;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
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
    [Fact]
    public async Task Import_host_observes_finalized_outbox_schema_for_publication()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        using var scope = host.Shell.ServiceProvider.CreateScope();
        var dormancy = scope.ServiceProvider.GetService<ISchemaDormancyCheck>();
        Assert.NotNull(dormancy);
        var availability = await dormancy.EvaluateAsync([new SchemaVersionRequirement("RuntimePostCommitOutbox", "2.0.0")]);
        Assert.True(availability.IsAvailable, $"{availability.Reason}: {JsonSerializer.Serialize(dormancy.Observe())}");
    }

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

    /// <summary>
    /// One Publish Event both starts a workflow whose start trigger is the Event and resumes a parked Event wait. Both
    /// Events receive the same payload.
    /// </summary>
    [Fact]
    public async Task One_Publish_Event_starts_a_workflow_and_resumes_a_wait_with_the_same_payload()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync("import-start-and-resume", ["publish-event-waiter", "publish-event-starter", "publish-event-publisher-payload"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var waiter = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("publish-event-waiter-v1"), "workflowVersionId"));
        var starter = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("publish-event-starter-v1"), "workflowVersionId"));
        var publisher = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("publish-event-publisher-payload-v1"), "workflowVersionId"));

        var waiting = await host.ExecuteAsync(waiter);
        await host.WaitForAsync(waiting, run => run.Activities.Any(state => IsEvent(state) && state.Status == ActivityExecutionStatus.Suspended));
        var published = await host.RunAsync(publisher);
        var resumed = await host.WaitForSettledAsync(waiting);
        var started = await host.WaitForSettledAsync(await ExecutionOfAsync(host, starter));
        output.WriteLine($"resumed: {resumed.Describe()}; started: {started.Describe()}");

        Assert.True(published.Workflow?.Status == WorkflowExecutionStatus.Completed, published.Describe());
        Assert.True(resumed.Workflow?.Status == WorkflowExecutionStatus.Completed, resumed.Describe());
        Assert.True(started.Workflow?.Status == WorkflowExecutionStatus.Completed, started.Describe());
        Assert.Equal(Elsa3ObjectLiteralPayload, PayloadOf(Assert.Single(resumed.Activities, IsEvent)));
        Assert.Equal(Elsa3ObjectLiteralPayload, PayloadOf(Assert.Single(started.Activities, IsEvent)));
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

    /// <summary>The execution started from <paramref name="workflow"/>'s artifact; the routed start may land after the publisher settles.</summary>
    private static async Task<string> ExecutionOfAsync(Elsa3ImportHost host, PublishedWorkflow workflow)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            var executions = await host.ReadAsync(services => services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync().AsTask());
            if (executions.SingleOrDefault(execution => execution.PinnedExecutable.ArtifactId == workflow.ArtifactId) is { } execution)
                return execution.WorkflowExecutionId;
            Assert.True(deadline.Elapsed < Elsa3ImportHost.SettleTimeout, $"No execution of '{workflow.ArtifactId}' was started.");
            await Task.Delay(100);
        }
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
