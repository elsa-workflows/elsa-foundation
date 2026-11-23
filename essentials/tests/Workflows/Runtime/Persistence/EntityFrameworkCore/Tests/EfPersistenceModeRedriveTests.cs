using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// A run started through the start dispatcher on the EF stores, whose first pass ends before anything is committed, is
/// driven again by a resumption sweep on a host whose default is another mode. The workflow chooses no mode, so the
/// mode the dispatcher wrote onto the queued start is all that is left of the run's choice, and the sweep's drain has
/// to find it there. Both hosts keep no completion record for an Ephemeral run, so a drain that ran the wrong mode shows
/// in what the run stores.
/// </summary>
public sealed class EfPersistenceModeRedriveTests : IDisposable
{
    private const string WorkflowExecutionId = "wfexec-ef-mode-redrive";
    private const string NodeId = "node-ef-mode-redrive";
    private static readonly WorkflowExecutionPartition DefaultPartition = new(PersistenceScope.DefaultValue);
    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"elsa-ef-mode-redrive-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            File.Delete(file);
    }

    [Theory]
    [InlineData(PersistenceMode.Durable, PersistenceMode.Ephemeral)]
    [InlineData(PersistenceMode.Balanced, PersistenceMode.Ephemeral)]
    [InlineData(PersistenceMode.Ephemeral, PersistenceMode.Balanced)]
    public async Task A_stamped_start_driven_again_by_a_sweep_under_another_host_default_completes_in_the_stamped_mode(
        PersistenceMode stamped,
        PersistenceMode laterHostDefault)
    {
        await using (var first = await StartNodeAsync(stamped, services => InvokeHandlerInterruptions.StopHostOnFirstInvokeOf(services, NodeId)))
        {
            var reference = await first.PublishAsync(
                WorkflowExecutionHarness.NewExecutable(WorkflowExecutionHarness.NewReplaySafeProbeNode(NodeId)),
                "source-ef-mode-redrive");
            await Assert.ThrowsAnyAsync<Exception>(() => first.StartPublishedAsync(reference, WorkflowExecutionId, partition: DefaultPartition));

            // No mode commits anything before a ReplaySafe activity runs, so the run has no record. The queue holds its start.
            Assert.Null((await first.ReadRunAsync(WorkflowExecutionId)).WorkflowState);
            var start = Assert.Single(await QueuedWorkAsync(first));
            Assert.Equal(WorkflowExecutionCommandKind.Start, start.CommandKind);
            Assert.Equal(stamped.ToString(), start.CommandMetadata[RuntimeMetadataKeys.PersistenceMode]);
        }

        await using var later = await StartNodeAsync(laterHostDefault);
        var sweep = await later.SweepAsync();

        Assert.Contains(sweep.Dispatches, dispatch =>
            dispatch.WorkflowExecutionId == WorkflowExecutionId && dispatch.Outcome == RuntimeResumptionDispatchOutcome.Accepted);
        Assert.Empty(await QueuedWorkAsync(later));
        var state = (await later.ReadRunAsync(WorkflowExecutionId)).WorkflowState;
        if (stamped == PersistenceMode.Ephemeral)
        {
            // The drain consumed the start and left nothing: only an Ephemeral run that completed does that here. A fault,
            // or a completion in either other mode, leaves a record.
            Assert.Null(state);
        }
        else
        {
            Assert.Equal(WorkflowExecutionStatus.Completed, state?.Status);
            Assert.Equal(stamped.ToString(), state!.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
        }
    }

    private Task<WorkflowExecutionHarness> StartNodeAsync(PersistenceMode hostDefault, Action<IServiceCollection>? configure = null) =>
        KeyedStartNodes.StartAsync("Sqlite", $"Data Source={_databasePath};Pooling=False", services =>
        {
            new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
            services.AddPersistenceModes(options =>
            {
                options.Mode = hostDefault;
                options.RecordCompletion = false;
            });
            configure?.Invoke(services);
        });

    private static async Task<IReadOnlyCollection<RuntimeSchedulerWorkItem>> QueuedWorkAsync(WorkflowExecutionHarness node)
    {
        await using var scope = node.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(WorkflowExecutionId);
    }
}
