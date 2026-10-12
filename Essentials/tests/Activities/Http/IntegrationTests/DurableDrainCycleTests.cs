using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// A directly started Durable run drains in a session that serves the next activity itself instead of delivering it
/// through the outbox, so a long sequence completes under the default drain-cycle bound, which caps the cycles of one
/// drain.
/// </summary>
public sealed class DurableDrainCycleTests
{
    private const int Lines = 40;

    [Fact]
    public async Task A_directly_started_Durable_sequence_of_40_WriteLines_completes_under_the_default_drain_cycle_bound()
    {
        await using var fixture = await HttpEndpointHostFixture.StartDurableSqliteAsync(PersistenceMode.Durable, maxActivitiesPerCommit: 50);
        Assert.Equal(
            WorkflowDrainOrchestratorOptions.DefaultMaxDrainCycles,
            fixture.Services.GetRequiredService<WorkflowDrainOrchestratorOptions>().MaxDrainCycles);
        var executable = ReferenceWorkflows.WriteLineSequence("writeline-sequence-40", Lines);
        await fixture.PublishAsync(executable);

        var executionId = await fixture.StartWorkflowDirectlyAsync(executable.Identity.ArtifactId);

        var execution = await fixture.WorkflowExecutionAsync(executionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, execution.Status);
        Assert.Equal(nameof(PersistenceMode.Durable), execution.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
    }
}
