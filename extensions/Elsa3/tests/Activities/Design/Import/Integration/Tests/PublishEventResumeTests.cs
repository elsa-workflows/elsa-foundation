using System.Net;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

public sealed class PublishEventResumeTests
{
    [Fact]
    public async Task Imported_Publish_Event_starts_the_published_workflow_and_resumes_an_existing_wait()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        const string starterDefinition = "publish-event-starter";
        var import = await host.ImportSelectionAsync("import-start-and-resume",
            ["publish-event-waiter", starterDefinition, "publish-event-publisher-payload"]);
        Assert.True(import.Apply.StatusCode == HttpStatusCode.Created, import.Apply.Body.ToJsonString());
        var waiter = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("publish-event-waiter-v1"), "workflowVersionId"));
        var starter = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("publish-event-starter-v1"), "workflowVersionId"));
        var publisher = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(import.Item("publish-event-publisher-payload-v1"), "workflowVersionId"));

        var waiting = await host.ExecuteAsync(waiter);
        var reads = host.Shell.ServiceProvider.GetRequiredService<KernelRuns>();
        await host.WaitForActiveNodeAsync(waiting, "wait");
        Assert.False(await reads.ListAsync(starterDefinition).AnyAsync());

        var published = await host.RunAsync(publisher);
        var resumed = await host.WaitForSettledAsync(waiting);
        Assert.True(published.Workflow?.Status == WorkflowExecutionStatus.Completed, published.Describe());
        Assert.True(resumed.Workflow?.Status == WorkflowExecutionStatus.Completed, resumed.Describe());
        Assert.Contains(resumed.Activities, activity => activity.AuthoredActivityId == "wait" && activity.Status == ActivityExecutionStatus.Completed);

        var started = await host.WaitForActorAsync(starterDefinition);
        Assert.Equal(starter.ArtifactId, started.Run.Workflow);
        Assert.Equal(starter.SourceReferenceId, started.Run.PublishedStart?.SourceReferenceId);
        var settled = await host.WaitForSettledAsync(started.View.Header.Ref.Id);
        Assert.True(settled.Workflow?.Status == WorkflowExecutionStatus.Completed, settled.Describe());
    }
}
