using System.Text.Json.Nodes;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// A reusable body that waits (spec 246 guidance review, G6, R-19): Elsa 3 suspends inside a workflow-as-activity and
/// resumes there. An imported reusable activity does the same when its graph body suspends on a Delay, a mid-flow Event or
/// a mid-flow HTTP Endpoint, so the import does not refuse waits. The readiness guide (docs/reference/elsa3-migration-readiness.md) relies on this.
/// </summary>
public sealed class ReusableBodyWaitTests(ITestOutputHelper output)
{
    [Fact]
    public async Task A_reusable_body_that_waits_resumes_and_completes()
    {
        await using var host = await Elsa3ImportHost.StartAsync(withScheduling: true);
        var (apply, items) = await host.ImportAsync("import-waits", "reusable-waits");
        Assert.True((int)apply.StatusCode < 300, apply.Body.ToJsonString());
        var reusable = items.Single(item => (bool?)item["isReusable"] == true);
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(reusable, "workflowVersionId"));

        var run = await host.RunAsync(wrapper, new JsonObject());
        foreach (var state in run.Activities)
            output.WriteLine($"{state.Execution.ActivityType} {state.Status} {state.StartedAt:O} -> {state.CompletedAt:O}");

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        var activity = Assert.Single(run.Activities, state => state.Execution.ActivityType == Elsa3ImportHost.Required(reusable, "activityTypeKey"));
        Assert.Equal("Approved", activity.Completion?.OutcomeKey);
        // The Delay suspended and resumed: it completed, and no earlier than its two seconds after it started.
        var delay = Assert.Single(run.Activities, state => state.Execution.ActivityType.EndsWith(".Delay", StringComparison.Ordinal));
        Assert.Equal(ActivityExecutionStatus.Completed, delay.Status);
        Assert.True(delay.CompletedAt - delay.StartedAt >= TimeSpan.FromSeconds(2), $"Delay ran from {delay.StartedAt:O} to {delay.CompletedAt:O}.");
    }

    /// <summary>The event is raised the way Elsa 3 workflows raise it: by an imported Publish Event, with or without a payload (#3107).</summary>
    [Theory]
    [InlineData("publish-event-publisher", null)]
    [InlineData("publish-event-publisher-payload", PublishEventResumeTests.Elsa3ObjectLiteralPayload)]
    public async Task A_reusable_body_that_waits_for_an_event_resumes_when_the_event_arrives(string publisherFixture, string? expectedPayload)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync($"import-waits-event-{publisherFixture}", ["reusable-waits-event", publisherFixture]);
        Assert.True((int)import.Apply.StatusCode < 300, import.Apply.Body.ToJsonString());
        var reusable = import.Item("waits-event-v1");
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(reusable, "workflowVersionId"));
        var publisher = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item($"{publisherFixture}-v1"), "workflowVersionId"));

        var waiting = await host.ExecuteAsync(wrapper, new JsonObject());
        await host.WaitForAsync(waiting, run => run.Activities.Any(state =>
            PublishEventResumeTests.IsEvent(state) && state.Status == ActivityExecutionStatus.Suspended));
        var published = await host.RunAsync(publisher);
        var run = await host.WaitForSettledAsync(waiting);
        output.WriteLine(run.Describe());

        Assert.True(published.Workflow?.Status == WorkflowExecutionStatus.Completed, published.Describe());
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        var activity = Assert.Single(run.Activities, state => state.Execution.ActivityType == Elsa3ImportHost.Required(reusable, "activityTypeKey"));
        Assert.Equal("Approved", activity.Completion?.OutcomeKey);
        Assert.Equal(expectedPayload ?? "null", PublishEventResumeTests.PayloadOf(Assert.Single(run.Activities, PublishEventResumeTests.IsEvent)));
    }

    [Fact]
    public async Task A_reusable_body_that_waits_for_an_http_request_resumes_when_it_arrives()
    {
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var (apply, items) = await host.ImportAsync("import-waits-http", "reusable-waits-http");
        Assert.True((int)apply.StatusCode < 300, apply.Body.ToJsonString());
        var reusable = Assert.Single(items);
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(reusable, "workflowVersionId"));

        var waiting = await host.ExecuteAsync(wrapper, new JsonObject());
        await host.WaitForAsync(waiting, run => run.Activities.Any(state =>
            state.Execution.ActivityType.EndsWith(".HttpEndpoint", StringComparison.Ordinal) && state.Status == ActivityExecutionStatus.Suspended));
        // The request resumes the waiting endpoint (202: the reusable body authors no HTTP response).
        using var response = await host.Client.PostAsync("workflows/http/orders/approve", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Accepted, $"{(int)response.StatusCode}: {body}");
        Assert.Contains(waiting, body, StringComparison.Ordinal);
        var run = await host.WaitForSettledAsync(waiting);
        output.WriteLine(run.Describe());

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        var activity = Assert.Single(run.Activities, state => state.Execution.ActivityType == Elsa3ImportHost.Required(reusable, "activityTypeKey"));
        Assert.Equal("Approved", activity.Completion?.OutcomeKey);
    }
}
