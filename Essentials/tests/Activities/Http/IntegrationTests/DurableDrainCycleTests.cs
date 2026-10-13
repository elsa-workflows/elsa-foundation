using System.Net;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// A Durable run drains in a session that serves the next activity itself instead of delivering it through the outbox,
/// so a long sequence completes under the default drain-cycle bound, which caps the cycles of one drain. A drain that
/// delivered each next activity through the outbox would spend at least one cycle per activity, and these sequences are
/// several times longer than the bound. The bound applies to every drain whatever started the run, so a run started by
/// a trigger is held to it as a directly started one is.
/// </summary>
public sealed class DurableDrainCycleTests
{
    private const int Lines = 200;
    private const string TriggerPath = "drain-cycle/writelines";

    [Fact]
    public async Task A_directly_started_Durable_sequence_of_200_WriteLines_completes_under_the_default_drain_cycle_bound()
    {
        await using var fixture = await StartDurableHostAsync();
        var executable = ReferenceWorkflows.WriteLineSequence("writeline-sequence-200", Lines);
        await fixture.PublishAsync(executable);

        var executionId = await fixture.StartWorkflowDirectlyAsync(executable.Identity.ArtifactId);

        AssertCompletedDurable(await fixture.WorkflowExecutionAsync(executionId));
    }

    [Fact]
    public async Task An_HTTP_triggered_Durable_sequence_of_200_WriteLines_completes_under_the_default_drain_cycle_bound()
    {
        await using var fixture = await StartDurableHostAsync();
        await fixture.PublishAsync(ReferenceWorkflows.HttpTriggeredWriteLineSequence("http-writeline-sequence-200", TriggerPath, Lines));

        using var response = await fixture.Client.PostAsync($"/workflows/http/{TriggerPath}", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("done", await response.Content.ReadAsStringAsync());
        AssertCompletedDurable(await fixture.SingleWorkflowExecutionAsync());
    }

    private static async Task<HttpEndpointHostFixture> StartDurableHostAsync()
    {
        var fixture = await HttpEndpointHostFixture.StartDurableSqliteAsync(PersistenceMode.Durable, maxActivitiesPerCommit: 50);
        Assert.Equal(
            WorkflowDrainOrchestratorOptions.DefaultMaxDrainCycles,
            fixture.Services.GetRequiredService<WorkflowDrainOrchestratorOptions>().MaxDrainCycles);
        return fixture;
    }

    private static void AssertCompletedDurable(WorkflowExecutionState execution)
    {
        Assert.Equal(WorkflowExecutionStatus.Completed, execution.Status);
        Assert.Equal(nameof(PersistenceMode.Durable), execution.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
    }
}
