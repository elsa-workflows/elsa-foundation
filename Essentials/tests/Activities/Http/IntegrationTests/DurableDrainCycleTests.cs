using System.Net;
using Elsa.Activities.Http.Options;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// A long Durable sequence completes under the default drain cycle limits, whatever started the run. The run drains in a
/// session that serves the next activity itself instead of delivering it through the outbox, so the drain takes few
/// cycles. Since #2661 the limit counts only cycles without progress, so a drain that spends a cycle per activity
/// completes too; <see cref="DrainCyclesWithoutProgressTests"/> covers those drains in every mode.
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
        // This tests drain-cycle progress through 200 activities, not the default HTTP deadline. Match
        // the client's existing 100-second bound rather than introducing a stricter server deadline.
        var fixture = await HttpEndpointHostFixture.StartDurableSqliteAsync(
            PersistenceMode.Durable,
            maxActivitiesPerCommit: 50,
            services => services.Configure<HttpEndpointOptions>(options => options.SyncRequestTimeout = TimeSpan.FromSeconds(100)));
        Assert.Equal(
            WorkflowDrainOrchestratorOptions.DefaultMaxCyclesWithoutProgress,
            fixture.Services.GetRequiredService<WorkflowDrainOrchestratorOptions>().MaxCyclesWithoutProgress);
        return fixture;
    }

    private static void AssertCompletedDurable(WorkflowExecutionState execution)
    {
        Assert.Equal(WorkflowExecutionStatus.Completed, execution.Status);
        Assert.Equal(nameof(PersistenceMode.Durable), execution.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
    }
}
