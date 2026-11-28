using System.Net;
using System.Text;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

public sealed class ReusableBodyWaitTests
{
    [Fact]
    public async Task A_reusable_body_resumes_its_delay_and_completes()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var (apply, items) = await host.ImportAsync("import-waits", "reusable-waits");
        Assert.Equal(HttpStatusCode.Created, apply.StatusCode);
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(Assert.Single(items), "workflowVersionId"));

        var run = await host.RunAsync(wrapper);

        var delay = await AssertApprovedAsync(host, wrapper, run);
        Assert.True(delay.CompletedAt - delay.StartedAt >= TimeSpan.FromSeconds(2), $"Delay ran from {delay.StartedAt:O} to {delay.CompletedAt:O}.");
    }

    [Theory]
    [InlineData("publish-event-publisher")]
    [InlineData("publish-event-publisher-payload")]
    public async Task A_reusable_body_resumes_when_an_imported_publisher_raises_its_event(string publisherFixture)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var import = await host.ImportSelectionAsync($"import-waits-event-{publisherFixture}", ["reusable-waits-event", publisherFixture]);
        Assert.Equal(HttpStatusCode.Created, import.Apply.StatusCode);
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("waits-event-v1"), "workflowVersionId"));
        var publisher = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item($"{publisherFixture}-v1"), "workflowVersionId"));
        var waiting = await host.ExecuteAsync(wrapper);
        await host.WaitForActiveNodeAsync(waiting, await host.ResolveBodyNodeIdAsync(wrapper, "wait"));

        var published = await host.RunAsync(publisher);
        Assert.True(published.Workflow?.Status == WorkflowExecutionStatus.Completed, published.Describe());
        await AssertApprovedAsync(host, wrapper, await host.WaitForSettledAsync(waiting));
    }

    [Fact]
    public async Task A_reusable_body_waits_for_and_resumes_from_an_actual_http_request()
    {
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var (apply, items) = await host.ImportAsync("import-waits-http", "reusable-waits-http");
        Assert.Equal(HttpStatusCode.Created, apply.StatusCode);
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(Assert.Single(items), "workflowVersionId"));
        var waiting = await host.ExecuteAsync(wrapper);
        await host.WaitForActiveNodeAsync(waiting, await host.ResolveBodyNodeIdAsync(wrapper, "wait"));

        using var response = await host.Client.PostAsync("workflows/http/orders/approve", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        await AssertApprovedAsync(host, wrapper, await host.WaitForSettledAsync(waiting));
    }

    private static async Task<ActivityExecutionInspectionSummaryProjection> AssertApprovedAsync(
        Elsa3ImportHost host, PublishedWorkflow wrapper, RunState run)
    {
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        var waitId = await host.ResolveBodyNodeIdAsync(wrapper, "wait");
        var approvalId = await host.ResolveBodyNodeIdAsync(wrapper, "approved");
        var wait = Assert.Single(run.Activities, activity => activity.ExecutableNodeId == waitId);
        Assert.Equal(ActivityExecutionStatus.Completed, wait.Status);
        Assert.Contains(run.Activities, activity => activity.ExecutableNodeId == approvalId);
        return wait;
    }
}
