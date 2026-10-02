using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// One host, two partitions, drained by the background scheduler path (no ambient services): a run started under a
/// partition other than the host's default persistence scope must execute in that partition.
/// </summary>
public sealed class SchedulerWorkHandlerPartitionScopeTests : IDisposable
{
    private const string TenantPartition = "tenant-alpha";
    private const string ExecutionId = "wfexec-partition-scope";
    private const string NodeId = "node-leaf";
    private const string Activation = "activation";
    private const string ActivityServices = "activity services";
    private const string RecoverySigningKey = "scheduler-partition-scope-recovery-signing-key-32";
    private const string HierarchySigningKey = "scheduler-partition-scope-hierarchy-signing-key-32";
    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"elsa-runtime-partition-scope-{Guid.NewGuid():N}.db");

    [Theory]
    [InlineData(PersistenceScope.DefaultValue)]
    [InlineData(TenantPartition)]
    public async Task A_run_completes_in_the_partition_it_was_started_under_on_the_ef_stores(string partition)
    {
        await using var harness = await StartSqliteHostAsync();
        var executable = WorkflowExecutionHarness.NewExecutable(WorkflowExecutionHarness.NewProbeNode(NodeId));
        var reference = await harness.PublishAsync(executable, "source-partition-scope");
        await CopyPublicationIntoAsync(harness, partition, reference);

        await StartAsync(harness, reference, ExecutionId, partition);

        await using var scope = await harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new PersistenceScope(partition));
        var state = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(ExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, state?.Status);
    }

    [Theory]
    [InlineData(PersistenceScope.DefaultValue)]
    [InlineData(TenantPartition)]
    public async Task An_activity_observes_the_partition_its_run_was_started_under_on_the_in_memory_stores(string partition)
    {
        var recorder = new PartitionRecorder();
        await using var harness = WorkflowExecutionHarness.Create()
            .ConfigureServices(services => services
                .AddSingleton(recorder)
                .AddScoped<ActivityActivator>()
                .Replace(ServiceDescriptor.Scoped<IActivityActivator, PartitionRecordingActivator>()))
            .Build("actexec-partition-scope");
        var reference = await harness.PublishAsync(
            WorkflowExecutionHarness.NewExecutable(ClrNode(typeof(PartitionRecordingActivity))),
            "source-partition-scope");

        await StartAsync(harness, reference, ExecutionId, partition);

        Assert.Equal([(Activation, partition), (ActivityServices, partition)], recorder.Observed);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            File.Delete(file);
    }

    private async Task<WorkflowExecutionHarness> StartSqliteHostAsync()
    {
        var harness = WorkflowExecutionHarness.Create()
            .ConfigureServices(services => services
                .AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = $"Data Source={_databasePath};Pooling=False",
                    RecoveryContinuationSigningKey = RecoverySigningKey,
                    HierarchyCursorSigningKey = HierarchySigningKey
                })
                .AddEfModuleMigrations<RuntimeDbContext>("Sqlite"))
            .Build("actexec-partition-scope");

        foreach (var initializer in harness.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        harness.InitializeActivityTypes();
        return harness;
    }

    /// <summary>Copies into <paramref name="partition"/> what <see cref="WorkflowExecutionHarness.PublishAsync"/> published into the host's default scope.</summary>
    private static async Task CopyPublicationIntoAsync(WorkflowExecutionHarness harness, string partition, WorkflowExecutableSourceReference reference)
    {
        if (partition == PersistenceScope.DefaultValue)
            return;

        var scopes = harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>();
        WorkflowExecutable? published;
        await using (var source = await scopes.CreateAsync(new PersistenceScope(PersistenceScope.DefaultValue)))
            published = await source.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindAsync(reference.ArtifactId);

        await using var target = await scopes.CreateAsync(new PersistenceScope(partition));
        await target.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(Assert.IsType<WorkflowExecutable>(published));
        await target.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference);
    }

    /// <summary>
    /// Starts through the production start dispatcher with no ambient services, so the in-process actor drains on the
    /// background path, and requires the drain to have run without a fault.
    /// </summary>
    private static async Task StartAsync(
        WorkflowExecutionHarness harness,
        WorkflowExecutableSourceReference reference,
        string workflowExecutionId,
        string partition)
    {
        const string requestedBy = "scheduler-partition-scope-tests";
        await using var scope = await harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new PersistenceScope(partition));
        var start = await scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(
                artifactId: reference.ArtifactId,
                requestedBy: requestedBy,
                workflowExecutionId: workflowExecutionId,
                idempotencyKey: $"start:{workflowExecutionId}",
                metadata: null,
                variables: null,
                inputs: null,
                stimulusInput: null,
                triggerNodeId: null,
                runKind: WorkflowRunKind.PublishedRun,
                sourceSelection: new WorkflowExecutableSourceSelection(reference.SourceReferenceId),
                provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                parentWorkflowExecutionId: null,
                correlationId: null,
                tenantId: partition,
                partition: new WorkflowExecutionPartition(partition),
                authority: new WorkflowExecutionAuthoritySnapshot(systemIdentity: requestedBy, rootInitiator: requestedBy)));

        Assert.True(start.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Accepted, $"{start.CommandDispatch.Status}: {start.CommandDispatch.Reason}");
    }

    private static ExecutableNode ClrNode(Type activityType) =>
        new(
            executableNodeId: NodeId,
            authoredActivityId: $"authored-{NodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(ClrActivityDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(activityType))),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

    /// <summary>Where a partition was read, and which one: at activation (the work handler's services) and in the activity's own services.</summary>
    public sealed class PartitionRecorder
    {
        private readonly List<(string Where, string Partition)> _observed = [];

        public IReadOnlyList<(string Where, string Partition)> Observed
        {
            get
            {
                lock (_observed)
                    return _observed.ToArray();
            }
        }

        public void Add(string where, string partition)
        {
            lock (_observed)
                _observed.Add((where, partition));
        }
    }

    /// <summary>Records the partition the activating services read, which is the one activation resolves secret-bound inputs under.</summary>
    public sealed class PartitionRecordingActivator(
        ActivityActivator inner,
        PartitionRecorder recorder,
        IWorkflowExecutionPartitionAccessor partitionAccessor) : IActivityActivator
    {
        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationRequest request, CancellationToken cancellationToken = default)
        {
            recorder.Add(Activation, partitionAccessor.Current.Value);
            return inner.ActivateAsync(request, cancellationToken);
        }
    }

    /// <summary>A leaf that records the partition of the services it was constructed from, then completes.</summary>
    public sealed class PartitionRecordingActivity(PartitionRecorder recorder, IWorkflowExecutionPartitionAccessor partitionAccessor) : Activity<ActivityUnit>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            recorder.Add(ActivityServices, partitionAccessor.Current.Value);
            return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Done));
        }
    }
}
