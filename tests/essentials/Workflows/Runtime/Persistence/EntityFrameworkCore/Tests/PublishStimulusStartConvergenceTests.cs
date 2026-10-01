using CShells.Lifecycle;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Services;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// A committed PublishStimulus intent is delivered at least once: a peer re-claims it when the first deliverer's lease
/// lapses, and a crash between dispatch and completion redelivers it after a restart. Each node, every generation here,
/// is a fresh service provider over one SQLite file, so it shares nothing with the node before it but the database.
/// Delivering the same intent again must not start the message-start workflow a second time (#2195).
/// </summary>
public sealed class PublishStimulusStartConvergenceTests : IAsyncDisposable
{
    private const string EventName = "order-placed";
    private const string RecoverySigningKey = "ef-runtime-publish-stimulus-recovery-signing-key-32";
    private const string HierarchySigningKey = "ef-runtime-publish-stimulus-hierarchy-signing-key-32";

    private readonly TemporarySqliteDatabase _database = new("publish-stimulus");
    private readonly RuntimePostCommitIntent _intent = PublishIntent();

    [Fact]
    public async Task A_PublishStimulus_delivered_again_on_another_node_starts_its_workflow_once()
    {
        await using (var first = await StartNodeAsync())
        {
            await PublishMessageStartWorkflowAsync(first);

            Assert.Equal(1, (await DeliverAsync(first)).StartedCount);
        }

        await using var second = await StartNodeAsync();
        var redelivery = await DeliverAsync(second);

        Assert.Equal(0, redelivery.StartedCount);
        Assert.Equal(1, redelivery.SkippedStartCount);
        var execution = Assert.Single(await ExecutionsAsync(second));
        Assert.Equal(WorkflowExecutionStatus.Completed, execution.Status);
        Assert.Single(await ActivityExecutionsAsync(second, execution.WorkflowExecutionId));
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    private async Task<WorkflowExecutionHarness> StartNodeAsync()
    {
        var node = WorkflowExecutionHarness.Create()
            .ConfigureServices(services =>
            {
                services
                    .AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                    {
                        Provider = "Sqlite",
                        ConnectionString = $"{_database.ConnectionString};Pooling=False",
                        RecoveryContinuationSigningKey = RecoverySigningKey,
                        HierarchyCursorSigningKey = HierarchySigningKey
                    })
                    .AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                new WorkflowsRuntimeTriggersFeature().ConfigureServices(services);
                // Real ids: the harness default names every execution alike, which would hide a second start.
                services.Replace(ServiceDescriptor.Singleton<IRuntimeExecutionIdGenerator>(
                    _ => new ShortRuntimeExecutionIdGenerator(TimeProvider.System)));
            })
            .Build();

        foreach (var initializer in node.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        node.InitializeActivityTypes();
        return node;
    }

    private static async Task PublishMessageStartWorkflowAsync(WorkflowExecutionHarness node)
    {
        var executable = WorkflowExecutionHarness.NewExecutable(WorkflowExecutionHarness.NewProbeNode("node-probe"));
        var reference = await node.PublishAsync(executable, "ref-message-start");
        await using var scope = node.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>().SaveAsync(new WorkflowTriggerBinding(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(executable.Identity.ArtifactId, "node-probe", EventStimulus.Hash(EventName)),
            ArtifactId: executable.Identity.ArtifactId,
            DefinitionId: executable.Identity.DefinitionId,
            ArtifactVersion: executable.Identity.ArtifactVersion,
            ArtifactHash: executable.Identity.ArtifactHash,
            ExecutableNodeId: "node-probe",
            StimulusType: EventStimulus.StimulusType,
            StimulusHash: EventStimulus.Hash(EventName),
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: WorkflowExecutionHarness.Timestamp,
            ActivationId: reference.ActivationId,
            SlotId: reference.SlotId));
    }

    private async Task<StimulusRoutingResult> DeliverAsync(WorkflowExecutionHarness node)
    {
        var router = new RecordingStimulusRouter(node);
        await new PublishStimulusExecutor(router).HandleAsync(_intent);
        return router.LastResult!;
    }

    private static async Task<IReadOnlyCollection<WorkflowExecutionState>> ExecutionsAsync(WorkflowExecutionHarness node)
    {
        await using var scope = node.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync();
    }

    private static async Task<IReadOnlyCollection<ActivityExecutionState>> ActivityExecutionsAsync(WorkflowExecutionHarness node, string workflowExecutionId)
    {
        await using var scope = node.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(workflowExecutionId);
    }

    // The intent a committed PublishEvent leaves in the outbox: its idempotency key is fixed by the publishing activity
    // execution, so every delivery of it carries the same key.
    private static RuntimePostCommitIntent PublishIntent()
    {
        var buffer = new PublishStimulusStagingBuffer();
        buffer.StagePublishStimulus(new PublishStimulusRequest("wfexec-publisher", "actexec-publish", EventName));
        return Assert.Single(buffer.TakePublishStimuli("wfexec-publisher", "actexec-publish"));
    }

    /// <summary>Routes through the node's real router in a fresh scope, as the outbox's handler scope does.</summary>
    private sealed class RecordingStimulusRouter(WorkflowExecutionHarness node) : IStimulusRouter
    {
        public StimulusRoutingResult? LastResult { get; private set; }

        public async ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
        {
            await using var scope = node.Services.CreateAsyncScope();
            return LastResult = await scope.ServiceProvider.GetRequiredService<IStimulusRouter>().RouteAsync(request, cancellationToken);
        }
    }
}
