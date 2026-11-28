using System.Net;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// Spec 1242 FR-016 (Q2): a bulk dispatch whose target is not in the import but is already published in Elsa 4 converts,
/// because the host's materializer reads the published target's declared inputs. The import runs through the host's own
/// composition, so this also proves the materializer is built with the runtime stores.
/// </summary>
public sealed class BulkDispatchPublishedTargetTests
{
    [Fact]
    public async Task A_bulk_dispatch_of_a_workflow_published_earlier_converts_publishes_and_starts_one_child_per_item()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var targetImport = await host.ImportSelectionAsync("import-bulk-target", ["output-target"]);
        Assert.True(targetImport.Apply.StatusCode == HttpStatusCode.Created, targetImport.Apply.Body.ToJsonString());
        var target = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(targetImport.Item("output-target-v1"), "workflowVersionId"));

        // The caller is imported on its own: its target is found only as a published Elsa 4 workflow.
        var callerImport = await host.ImportSelectionAsync("import-bulk-caller", ["bulk-dispatch-caller"]);
        Assert.True(callerImport.Apply.StatusCode == HttpStatusCode.Created, callerImport.Apply.Body.ToJsonString());
        Assert.DoesNotContain("BULK-DISPATCH-TARGET-UNKNOWN", callerImport.Apply.Body.ToJsonString(), StringComparison.Ordinal);
        var caller = await host.PublishWorkflowAsync(Elsa3ImportHost.Required(callerImport.Item("bulk-dispatch-caller-v1"), "workflowVersionId"));

        var run = await host.RunAsync(caller);

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Contains(run.Activities, state => state.Execution.ExecutableNodeId == "after" && state.Status == ActivityExecutionStatus.Completed);
        var children = await host.ReadAsync(services => services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync().AsTask());
        Assert.Equal(3, children.Count(execution => execution.PinnedExecutable.ArtifactId == target.ArtifactId &&
                                                    execution.Status == WorkflowExecutionStatus.Completed));
    }
}
