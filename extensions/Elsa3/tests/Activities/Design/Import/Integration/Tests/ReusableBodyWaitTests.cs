using System.Net;
using System.Text;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
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

        AssertApproved(run);
        var delay = Assert.Single(run.Activities, state => state.AuthoredActivityId == "wait");
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
        await host.WaitForActiveNodeAsync(waiting, "wait");

        var published = await host.RunAsync(publisher);
        Assert.True(published.Workflow?.Status == WorkflowExecutionStatus.Completed, published.Describe());
        AssertApproved(await host.WaitForSettledAsync(waiting));
    }

    [Fact]
    public async Task A_reusable_body_waits_for_and_resumes_from_an_actual_http_request()
    {
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true);
        var (apply, items) = await host.ImportAsync("import-waits-http", "reusable-waits-http");
        Assert.Equal(HttpStatusCode.Created, apply.StatusCode);
        var wrapper = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(Assert.Single(items), "workflowVersionId"));
        var waiting = await host.ExecuteAsync(wrapper);
        await host.WaitForActiveNodeAsync(waiting, "wait");

        using var response = await host.Client.PostAsync("workflows/http/orders/approve", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.Equal(waiting, Assert.Single(response.Headers.GetValues(KernelHttpEdge.WorkflowInstanceIdHeader)));
        AssertApproved(await host.WaitForSettledAsync(waiting));
    }

    private static void AssertApproved(RunState run)
    {
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Contains(run.Activities, activity => activity.AuthoredActivityId == "wait" && activity.Status == ActivityExecutionStatus.Completed);
        Assert.Contains("approved", run.ExecutedNodeIds);
    }
}
