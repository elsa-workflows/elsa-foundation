using System.Net;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Kernel.Foundation.Contracts;
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
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while ((await reads.GetAsync(new ActorRef(waiting), deadline.Token)).Run is not { Run.Suspended: true })
            await Task.Delay(100, deadline.Token);
        Assert.False(await reads.ListAsync(starterDefinition, deadline.Token).AnyAsync(deadline.Token));

        var published = await host.RunAsync(publisher);
        var resumed = await host.WaitForSettledAsync(waiting);
        Assert.True(published.Workflow?.Status == WorkflowExecutionStatus.Completed, published.Describe());
        Assert.True(resumed.Workflow?.Status == WorkflowExecutionStatus.Completed, resumed.Describe());
        Assert.Contains(resumed.Activities, activity => activity.AuthoredActivityId == "wait" && activity.Status == ActivityExecutionStatus.Completed);

        string? started = null;
        while (started is null)
        {
            var candidates = await reads.ListAsync(starterDefinition, deadline.Token).ToListAsync(deadline.Token);
            if (candidates.Count != 0)
            {
                var candidate = Assert.Single(candidates);
                Assert.Equal(starter.ArtifactId, candidate.Run.Workflow);
                Assert.Equal(starter.SourceReferenceId, candidate.Run.PublishedStart?.SourceReferenceId);
                started = candidate.View.Header.Ref.Id;
            }
            else
                await Task.Delay(100, deadline.Token);
        }

        var settled = await host.WaitForSettledAsync(started);
        Assert.True(settled.Workflow?.Status == WorkflowExecutionStatus.Completed, settled.Describe());
    }
}
