using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Primitives.Activities;
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
/// One host serving two partitions on the background drain path, where the drain hands the work handlers no ambient
/// services: a run executes in the partition it was started under, not in the host's own persistence scope (#2341).
/// Each test exercises one place that creates a scope of its own for a work item.
/// </summary>
public sealed class SchedulerWorkHandlerPartitionScopeTests : IDisposable
{
    private const string TenantPartition = "tenant-alpha";
    private const string ExecutionId = "wfexec-partition-scope";
    private const string SourceReferenceId = "source-partition-scope";
    private const string ActivityExecutionId = "actexec-partition-scope";
    private const string NodeId = "node-leaf";
    private const string Activation = "activation";
    private const string ActivityServices = "activity services";
    private const string RecoverySigningKey = "scheduler-partition-scope-recovery-signing-key-32";
    private const string HierarchySigningKey = "scheduler-partition-scope-hierarchy-signing-key-32";
    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"elsa-runtime-partition-scope-{Guid.NewGuid():N}.db");

    /// <summary>The host's own scope, as the control, and a partition that is not the host's.</summary>
    public static TheoryData<string> Partitions => [PersistenceScope.DefaultValue, TenantPartition];

    /// <summary>The invoke handler reads the activity execution the start-activity handler wrote (<c>RuntimeSchedulerWorkHandlerBase</c>).</summary>
    [Theory]
    [MemberData(nameof(Partitions))]
    public async Task A_clr_activity_completes_in_the_partition_its_run_was_started_under(string partition)
    {
        await using var harness = await StartOnSqliteAsync(partition, WorkflowExecutionHarness.NewExecutable(WorkflowExecutionHarness.NewProbeNode(NodeId)));

        Assert.Equal(WorkflowExecutionStatus.Completed, await StatusAsync(harness, partition));
    }

    /// <summary>The intrinsic executor is resolved from the scope the start-activity handler creates.</summary>
    [Theory]
    [MemberData(nameof(Partitions))]
    public async Task An_intrinsic_node_completes_in_the_partition_its_run_was_started_under(string partition)
    {
        await using var harness = await StartOnSqliteAsync(partition, WorkflowExecutionHarness.NewExecutable(SetOutputNode()));

        Assert.Equal(WorkflowExecutionStatus.Completed, await StatusAsync(harness, partition));
    }

    /// <summary>The resume handler reads the bookmark and the suspended activity from the scope it creates.</summary>
    [Theory]
    [MemberData(nameof(Partitions))]
    public async Task A_bookmark_resumes_in_the_partition_its_run_was_started_under(string partition)
    {
        await using var harness = await StartOnSqliteAsync(partition, RuntimeEntityFrameworkCoreEndToEndTests.NewEventExecutable());
        Assert.Equal(WorkflowExecutionStatus.Running, await StatusAsync(harness, partition));

        await using (var scope = await CreateScopeAsync(harness, partition))
        {
            var bookmark = Assert.Single(await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(ExecutionId));
            var resumed = await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(
                new BookmarkResumeDispatchRequest(
                    workflowExecutionId: ExecutionId,
                    stimulusType: bookmark.StimulusType,
                    stimulusHash: bookmark.StimulusHash,
                    input: JsonSerializer.SerializeToElement(new EventReceived(RuntimeEntityFrameworkCoreEndToEndTests.EventName))));
            Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, resumed.Status);
        }

        Assert.Equal(WorkflowExecutionStatus.Completed, await StatusAsync(harness, partition));
    }

    /// <summary>
    /// The partition activation reads, which is the one a secret-bound input is resolved under, and the partition the
    /// services injected into the activity read (<c>ClrActivityActivator</c>). On the in-memory stores, which do not
    /// keep partitions apart, so the run completes either way and only the partition read can differ.
    /// </summary>
    [Theory]
    [MemberData(nameof(Partitions))]
    public async Task Activation_and_the_activity_read_the_partition_the_run_was_started_under(string partition)
    {
        var recorder = new PartitionRecorder();
        await using var harness = WorkflowExecutionHarness.Create()
            .ConfigureServices(services => services
                .AddSingleton(recorder)
                .AddScoped<ActivityActivator>()
                .Replace(ServiceDescriptor.Scoped<IActivityActivator, PartitionRecordingActivator>()))
            .Build(ActivityExecutionId);
        var reference = await harness.PublishAsync(
            WorkflowExecutionHarness.NewExecutable(ClrNode(typeof(PartitionRecordingActivity))),
            SourceReferenceId);

        await StartAsync(harness, reference, partition);

        Assert.Equal([(Activation, partition), (ActivityServices, partition)], recorder.Observed);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            File.Delete(file);
    }

    /// <summary>
    /// Builds a host on the EF stores over one SQLite file, whose own persistence scope is the default one, publishes
    /// <paramref name="executable"/> into <paramref name="partition"/> and starts it there.
    /// </summary>
    private async Task<WorkflowExecutionHarness> StartOnSqliteAsync(string partition, WorkflowExecutable executable)
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
            .Build(ActivityExecutionId);

        foreach (var initializer in harness.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        harness.InitializeActivityTypes();

        var reference = await harness.PublishAsync(executable, SourceReferenceId);
        await CopyPublicationIntoAsync(harness, partition, reference);
        await StartAsync(harness, reference, partition);
        return harness;
    }

    private static ValueTask<PersistenceOperationScope> CreateScopeAsync(WorkflowExecutionHarness harness, string partition) =>
        harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new PersistenceScope(partition));

    private static async Task<WorkflowExecutionStatus?> StatusAsync(WorkflowExecutionHarness harness, string partition)
    {
        await using var scope = await CreateScopeAsync(harness, partition);
        return (await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(ExecutionId))?.Status;
    }

    /// <summary>Copies into <paramref name="partition"/> what <see cref="WorkflowExecutionHarness.PublishAsync"/> published into the host's default scope.</summary>
    private static async Task CopyPublicationIntoAsync(WorkflowExecutionHarness harness, string partition, WorkflowExecutableSourceReference reference)
    {
        if (partition == PersistenceScope.DefaultValue)
            return;

        WorkflowExecutable? published;
        await using (var source = await CreateScopeAsync(harness, PersistenceScope.DefaultValue))
            published = await source.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindAsync(reference.ArtifactId);

        await using var target = await CreateScopeAsync(harness, partition);
        await target.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(Assert.IsType<WorkflowExecutable>(published));
        await target.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference);
    }

    /// <summary>
    /// Starts through the production start dispatcher with no ambient services, so the in-process actor drains on the
    /// background path, and requires the drain to have run without a fault.
    /// </summary>
    private static async Task StartAsync(WorkflowExecutionHarness harness, WorkflowExecutableSourceReference reference, string partition)
    {
        const string requestedBy = "scheduler-partition-scope-tests";
        await using var scope = await CreateScopeAsync(harness, partition);
        var start = await scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(
                artifactId: reference.ArtifactId,
                requestedBy: requestedBy,
                workflowExecutionId: ExecutionId,
                idempotencyKey: $"start:{ExecutionId}",
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

    /// <summary>A root the runtime executes itself rather than activating a CLR activity for.</summary>
    private static ExecutableNode SetOutputNode() =>
        new(
            executableNodeId: NodeId,
            authoredActivityId: $"authored-{NodeId}",
            activityType: "elsa.intrinsic.set-output",
            activityTypeVersion: "1.0.0",
            descriptorType: "intrinsic",
            descriptorPayload: JsonSerializer.SerializeToElement(new { kind = "SetOutput", schemaVersion = "1.0.0" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Name] = RuntimeEntityFrameworkCoreEndToEndTests.Literal(WorkflowIntrinsicInputKeys.Name, "String", "result"),
                [WorkflowIntrinsicInputKeys.Value] = RuntimeEntityFrameworkCoreEndToEndTests.Literal(WorkflowIntrinsicInputKeys.Value, "String", "done")
            },
            metadata: new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.SetOutput);

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
