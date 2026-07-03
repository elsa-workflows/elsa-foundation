using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// RT-4 guard: the runtime execution spine is composed by the host-agnostic <see cref="RuntimeCoreServiceCollectionExtensions.AddWorkflowRuntimeCore"/>
/// composition root, so a non-HTTP host (worker, test harness, another module) can resolve and drive the runtime without
/// the FastEndpoints <c>WorkflowsRuntimeApiFeature</c>. Mirrors the failure class the review flagged: the runtime must not
/// be reachable only through the API feature.
/// </summary>
public sealed class RuntimeCoreCompositionRootTests : RuntimePipelineTestSupport
{
    [Fact]
    public void AddWorkflowRuntimeCore_ResolvesTheExecutionSpine_WithoutTheApiFeature()
    {
        using var provider = new ServiceCollection().AddWorkflowRuntimeCore().BuildServiceProvider();

        // The whole dispatch graph must resolve from the Core composition root alone.
        Assert.NotNull(provider.GetService<IWorkflowSchedulerDrainer>());
        Assert.NotNull(provider.GetService<IWorkflowExecutionCommandProcessor>());
        Assert.NotNull(provider.GetService<IWorkflowExecutionDrainCoordinator>());
        Assert.NotNull(provider.GetService<IRuntimeExecutionPipelineDispatcher>());
        Assert.NotNull(provider.GetService<IRuntimeWorkflowExecutionPipeline>());
        Assert.NotNull(provider.GetService<IRuntimeActivityExecutionPipeline>());
        Assert.NotNull(provider.GetService<IWorkflowExecutionAgentProvider>());
        Assert.NotNull(provider.GetService<IWorkflowExecutionStartDispatcher>());
        Assert.NotEmpty(provider.GetServices<IWorkflowSchedulerWorkHandler>());
    }

    [Fact]
    public async Task AddWorkflowRuntimeCore_DrivesADrainEndToEnd_WithoutTheApiFeature()
    {
        using var provider = new ServiceCollection().AddWorkflowRuntimeCore().BuildServiceProvider();
        await provider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(NewWorkflowState(WorkflowExecutionStatus.Running));
        await provider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(NewActivityStateForStatus(ActivityExecutionStatus.Running));
        await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(NewCancelWorkItem());

        var result = await provider.GetRequiredService<IWorkflowSchedulerDrainer>()
            .DrainAsync(new RuntimeSchedulerDrainRequest("wf-1"));

        // The Cancel work item ran through the composed workflow pipeline (Invoke slot -> Checkpoint slot -> committer).
        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status);
        var committed = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivityCancelled, committed.Commit.Checkpoint.Name);
        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wf-1");
        Assert.Equal(WorkflowExecutionStatus.Cancelled, workflowState!.Status);
    }
}
