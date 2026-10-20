using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Compensation;
using Elsa.Activities.Compensation.Models;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Testing;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Workflows.Design;
using Elsa.Etl.Workflows.Runtime;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Services;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using CompensationActivity = Elsa.Activities.Compensation.Activities.CompensationScope;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Etl.Tests;

/// <summary>
/// Exercises the real native ETL suspension, durable bookmark, host worker, result dispatch and composite continuation
/// over SQLite. The harness is not itself a Generic Host, so the test starts its registered IBackgroundTask through
/// its production lifecycle contract after running the registered migration initializers.
/// </summary>
public sealed class EtlNativeCompositeJourneyTests
{
    [Theory]
    [InlineData(CompositeKind.Sequence)]
    [InlineData(CompositeKind.Flowchart)]
    [InlineData(CompositeKind.CompensationScope)]
    [InlineData(CompositeKind.CompensationAfterFault)]
    public async Task Published_RunEtl_resumes_its_composite_before_the_following_sibling(CompositeKind compositeKind)
    {
        await using var journey = await NativeEtlJourney.CreateAsync(compositeKind);

        var reference = await journey.PublishWorkflowAsync();
        var started = await journey.Harness.StartPublishedAsync(reference, journey.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, started.CommandDispatch.Status);

        await journey.DriveAsync();
        var suspended = await journey.ReadNativeStateAsync();
        Assert.Equal(WorkflowExecutionStatus.Running, suspended.Workflow.Status);
        var etlActivity = Assert.Single(suspended.Activities, state => state.Execution.ExecutableNodeId == "run-etl");
        Assert.Equal(ActivityExecutionStatus.Suspended, etlActivity.Status);
        Assert.DoesNotContain(suspended.Activities, state => state.Execution.ExecutableNodeId == "after-etl");

        var invocation = EtlNativeBoundary.Invocation(etlActivity);
        var bookmark = await journey.ReadBookmarkAsync(invocation.ResultAddress.BookmarkId);
        Assert.NotNull(bookmark);
        EtlNativeBoundary.EnsureBookmark(invocation.ResultAddress, bookmark!);

        var admitted = await journey.ReadRunAsync(invocation.Scope, invocation.RunId);
        Assert.NotNull(admitted);
        Assert.Null(admitted!.TerminalResult);

        await journey.StartWorkerAsync();
        await journey.WaitForCompositeCompletionAsync(invocation);

        var completed = await journey.ReadNativeStateAsync();
        Assert.Equal(WorkflowExecutionStatus.Completed, completed.Workflow.Status);
        var resumedEtl = Assert.Single(completed.Activities, state => state.Execution.ExecutableNodeId == "run-etl");
        Assert.Equal(ActivityExecutionStatus.Completed, resumedEtl.Status);
        var receipt = Assert.Single(resumedEtl.TriggerDeliveries ?? []);
        Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, receipt.Status);
        Assert.Equal(invocation.ResultAddress.RegistrationId, receipt.RegistrationId);
        Assert.Equal(invocation.ResultAddress.ProviderId, receipt.ProviderId);
        Assert.Equal(EtlNativeBoundary.CompletionType, receipt.PayloadType);
        Assert.Null(await journey.ReadBookmarkAsync(invocation.ResultAddress.BookmarkId));
        Assert.Equal(ActivityExecutionStatus.Completed,
            Assert.Single(completed.Activities, state => state.Execution.ExecutableNodeId == "after-etl").Status);
        var composite = Assert.Single(completed.Activities, state => state.Execution.ExecutableNodeId == journey.CompositeNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, composite.Status);
        if (compositeKind == CompositeKind.CompensationAfterFault)
        {
            Assert.Equal([CompensationActivity.CompensatedOutcome], WorkflowExecutionRun.CompletionOutcomes(composite));
            Assert.Equal(ActivityExecutionStatus.Faulted,
                Assert.Single(completed.Activities, state => state.Execution.ExecutableNodeId == "fail-after-etl").Status);
            Assert.Equal(ActivityExecutionStatus.Completed,
                Assert.Single(completed.Activities, state => state.Execution.ExecutableNodeId == "after-composite").Status);
        }

        var run = await journey.ReadRunAsync(invocation.Scope, invocation.RunId);
        Assert.Equal(EtlRunStatus.Succeeded, run!.Status);
        var manifest = await journey.ReadManifestAsync(invocation.Scope, invocation.RunId);
        var chunk = Assert.Single(manifest, entry => entry.Effect.Route == EtlOutputRoute.Main);
        var outputPath = Path.Combine(journey.OutputRoot, chunk.Effect.Chunk.Key);
        Assert.Equal("{\"name\":\"Ada\"}\n", await File.ReadAllTextAsync(outputPath, Encoding.UTF8));

        var delivery = await journey.ReadOutboxAsync(invocation.Scope, invocation.RunId);
        // The worker may acknowledge after the native scheduler has completed the parent. Both proofs are valid;
        // the matching durable receipt above independently establishes that this result resumed the child.
        Assert.Contains(delivery.Disposition, new int?[]
        {
            (int)EtlDeliveryDisposition.Consumed,
            (int)EtlDeliveryDisposition.AlreadyConsumed,
            (int)EtlDeliveryDisposition.ProvenParentTerminal
        });
        Assert.NotNull(delivery.AcknowledgedAtUtcTicks);
    }

    public enum CompositeKind
    {
        Sequence,
        Flowchart,
        CompensationScope,
        CompensationAfterFault
    }

    private sealed class NativeEtlJourney : IAsyncDisposable
    {
        private const string WorkflowRecoverySigningKey = "etl-native-composite-recovery-key-32";
        private const string WorkflowHierarchySigningKey = "etl-native-composite-hierarchy-key-32";

        private readonly string _directory;
        private readonly IHost _lifetimeHost;
        private readonly IBackgroundTask _worker;
        private bool _workerStarted;
        private bool _retainFailureEvidence;

        private NativeEtlJourney(
            string directory,
            IHost lifetimeHost,
            WorkflowExecutionHarness harness,
            IBackgroundTask worker,
            CompositeKind compositeKind,
            string workflowExecutionId,
            EtlScope scope)
        {
            _directory = directory;
            _lifetimeHost = lifetimeHost;
            Harness = harness;
            _worker = worker;
            CompositeKind = compositeKind;
            WorkflowExecutionId = workflowExecutionId;
            Scope = scope;
            CompositeNodeId = compositeKind switch
            {
                CompositeKind.Sequence => "sequence-root",
                CompositeKind.Flowchart => "flowchart-root",
                _ => "compensation-root"
            };
            OutputRoot = Path.Combine(directory, "output");
        }

        public WorkflowExecutionHarness Harness { get; }
        public CompositeKind CompositeKind { get; }
        public string WorkflowExecutionId { get; }
        public string CompositeNodeId { get; }
        public EtlScope Scope { get; }
        public string OutputRoot { get; }

        public static async Task<NativeEtlJourney> CreateAsync(CompositeKind compositeKind)
        {
            var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.NativeCompositeJourney", Guid.NewGuid().ToString("N"));
            IHost? lifetimeHost = null;
            WorkflowExecutionHarness? harness = null;
            try
            {
                Directory.CreateDirectory(directory);

                var workflowExecutionId = "etl-native-composite-" + Guid.NewGuid().ToString("N");
                var scope = new EtlScope(null, WorkflowExecutionHarness.Partition.Value);
                var sourcePath = Path.Combine(directory, "source.csv");
                var lookupPath = Path.Combine(directory, "lookup.csv");
                var workflowDatabase = Path.Combine(directory, "workflow.db");
                var etlRuntimeDatabase = Path.Combine(directory, "etl-runtime.db");
                var etlDesignDatabase = Path.Combine(directory, "etl-design.db");
                var snapshotRoot = Path.Combine(directory, "snapshots");

                await File.WriteAllTextAsync(sourcePath, "id,name\n1,Ada\n", new UTF8Encoding(false));
                await File.WriteAllTextAsync(lookupPath, "id,category\n1,one\n", new UTF8Encoding(false));

                var bindings = new[]
                {
                    Binding(scope, "source", "Source", "source-v1", sourcePath, true, false),
                    Binding(scope, "lookup", "Lookup", "lookup-v1", lookupPath, true, false),
                    Binding(scope, "output", "MainOutput", "output-v1", Path.Combine(directory, "output"), false, true),
                    Binding(scope, "errors", "ErrorOutput", "errors-v1", Path.Combine(directory, "errors"), false, true)
                };

                // RunEtl's production host worker requires the normal host lifetime service even though this harness starts
                // it through IBackgroundTask. The same real Generic Host lifetime lets us exercise that production path.
                lifetimeHost = Host.CreateApplicationBuilder().Build();
                await lifetimeHost.StartAsync();

                var builder = WorkflowExecutionHarness.Create()
                    .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
                    .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
                    .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
                    .WithFeature(services => new ActivitiesCompensationFeature().ConfigureServices(services))
                    .WithFeature(services => new EtlDesignFeature().ConfigureServices(services))
                    .WithFeature(services => new EtlDesignEntityFrameworkCoreFeature
                    {
                        ConnectionString = $"Data Source={etlDesignDatabase};Pooling=False"
                    }.ConfigureServices(services))
                    .WithFeature(services => new EtlRuntimeFeature
                    {
                        SnapshotRoot = snapshotRoot,
                        FileBindings = bindings.ToList()
                    }.ConfigureServices(services))
                    .WithFeature(services => new EtlRuntimeEntityFrameworkCoreFeature
                    {
                        ConnectionString = $"Data Source={etlRuntimeDatabase};Pooling=False"
                    }.ConfigureServices(services))
                    .WithFeature(services => new EtlWorkflowsRuntimeFeature().ConfigureServices(services))
                    .WithFeature(services => new EtlWorkflowsDesignFeature().ConfigureServices(services))
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddSingleton(lifetimeHost.Services.GetRequiredService<IHostApplicationLifetime>());
                        services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                        {
                            Provider = "Sqlite",
                            ConnectionString = $"Data Source={workflowDatabase};Pooling=False",
                            RecoveryContinuationSigningKey = WorkflowRecoverySigningKey,
                            HierarchyCursorSigningKey = WorkflowHierarchySigningKey
                        })
                        .AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                        services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);
                    })
                    .WithPersistenceMode(PersistenceMode.Durable);

                harness = builder.Build(
                    WorkflowExecutionHarness.Identity,
                    workflowExecutionId,
                    Enumerable.Range(1, 32).Select(index => $"{workflowExecutionId}-activity-{index}"));

                foreach (var initializer in harness.Services.GetServices<IShellInitializer>())
                    await initializer.InitializeAsync();
                harness.InitializeActivityTypes();
                var worker = Assert.Single(harness.Services.GetServices<IBackgroundTask>().OfType<EtlRuntimeWorker>());
                return new NativeEtlJourney(directory, lifetimeHost, harness, worker, compositeKind, workflowExecutionId, scope);
            }
            catch
            {
                if (harness is not null)
                    await harness.DisposeAsync();
                if (lifetimeHost is not null)
                {
                    await lifetimeHost.StopAsync();
                    lifetimeHost.Dispose();
                }
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
                throw;
            }
        }

        public async Task<WorkflowExecutableSourceReference> PublishWorkflowAsync()
        {
            EtlArtifact artifact;
            await using (var operation = await OpenPartitionScopeAsync())
            {
                var compiler = operation.ServiceProvider.GetRequiredService<IEtlArtifactCompiler>();
                artifact = Assert.IsType<EtlArtifact>(compiler.Compile("orders", 1, EtlTestPipelines.Create()).Artifact);
                var definitions = operation.ServiceProvider.GetRequiredService<IEtlDefinitionStore>();
                Assert.Equal(EtlDefinitionWriteStatus.Created,
                    (await definitions.CreateDraftAsync(Scope, "orders", EtlTestPipelines.Create())).Status);
                Assert.Equal(EtlPublicationStatus.Published,
                    (await definitions.PublishAsync(Scope, "orders", 1, artifact)).Status);

                var token = new EtlPublishedSelection(artifact.DefinitionId, artifact.DefinitionVersion, artifact.ContentHash).ToToken();
                var rootWithoutPin = Composite(RunEtlNode(token), WorkflowExecutionHarness.NewProbeNode("after-etl"));
                var request = new WorkflowExecutableCompileRequest(
                    "parent-workflow-version",
                    WorkflowExecutableReferenceScope.Published,
                    WorkflowExecutionHarness.Timestamp,
                    WorkflowExecutionHarness.Timestamp,
                    null,
                    "parent-workflow-artifact",
                    TenantId: Scope.TenantId);
                var source = new WorkflowExecutableCompileSource(
                    "parent-workflow-definition",
                    "parent-workflow-version",
                    "1.0.0",
                    WorkflowDefinitionState.Empty,
                    "WorkflowDefinitionVersion",
                    "parent-workflow-version",
                    "1.0.0");
                var context = new ExecutableCompilationContext(request, source, rootWithoutPin);
                var compilationSource = Assert.Single(operation.ServiceProvider.GetServices<IExecutableCompilationSource>()
                    .OfType<Elsa.Etl.Workflows.Design.Services.EtlPublishedArtifactPinSource>());
                var contribution = await compilationSource.GetContributionAsync(context);
                var pin = Assert.Single(contribution.NodeMetadata);
                Assert.Equal("run-etl", pin.ExecutableNodeId);
                Assert.Equal(EtlNativeBoundary.PinKey, pin.Key);

                var pinnedRoot = Composite(RunEtlNode(token, new Dictionary<string, string> { [pin.Key] = pin.Value }),
                    WorkflowExecutionHarness.NewProbeNode("after-etl"));
                var targets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal);
                var targetId = WorkflowExecutableResumeTarget.ComposeScopedId("run-etl", EtlNativeBoundary.ResumeTargetKey);
                targets.Add(targetId, new WorkflowExecutableResumeTarget(
                    targetId, "run-etl", "ResumeAsync", new Dictionary<string, string>(), EtlNativeBoundary.ResumeTargetKey));
                var executable = new WorkflowExecutable(
                    WorkflowExecutionHarness.Identity,
                    pinnedRoot,
                    targets,
                    WorkflowExecutionHarness.Timestamp,
                    new Dictionary<string, string>(),
                    IncidentStrategyBuiltIns.FaultReference);

                var reference = await Harness.PublishAsync(executable, "etl-native-composite-publication");
                await CopyPublicationIntoPartitionAsync(reference);
                return reference;
            }
        }

        public async Task StartWorkerAsync()
        {
            Assert.False(_workerStarted);
            await _worker.StartAsync(CancellationToken.None);
            _workerStarted = true;
        }

        public async Task WaitForCompositeCompletionAsync(EtlInvocation invocation)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                while (true)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    await DrivePartitionAsync(Harness, WorkflowExecutionId);

                    await using var operation = await OpenPartitionScopeAsync(timeout.Token);
                    var workflow = await operation.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>()
                        .FindAsync(WorkflowExecutionId, timeout.Token);
                    if (workflow?.Status == WorkflowExecutionStatus.Completed)
                    {
                        var delivery = await ReadOutboxAsync(invocation.Scope, invocation.RunId);
                        if (delivery.AcknowledgedAtUtcTicks is not null)
                            return;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                _retainFailureEvidence = true;
                var native = await ReadNativeStateAsync();
                var run = await ReadRunAsync(invocation.Scope, invocation.RunId);
                var outbox = await ReadOutboxAsync(invocation.Scope, invocation.RunId);
                var diagnostic = JsonSerializer.Serialize(new
                {
                    Workflow = native.Workflow,
                    native.Activities,
                    Run = run,
                    Outbox = outbox
                }, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(_directory, "failure-state.json"), diagnostic);
                throw new TimeoutException($"Native ETL composite did not finish; retained SQLite files and state at '{_directory}'. " +
                    $"Workflow={native.Workflow.Status}; activities={string.Join(",", native.Activities.Select(state => state.Execution.ExecutableNodeId + ":" + state.Status))}; " +
                    $"ETL={run?.Status}; delivery={outbox.Disposition}.");
            }
        }

        public async Task<(WorkflowExecutionState Workflow, IReadOnlyCollection<ActivityExecutionState> Activities)> ReadNativeStateAsync()
        {
            await using var operation = await OpenPartitionScopeAsync();
            var workflow = await operation.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>()
                .FindAsync(WorkflowExecutionId);
            var activities = await operation.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>()
                .ListAllAsync(WorkflowExecutionId);
            return (Assert.IsType<WorkflowExecutionState>(workflow), activities);
        }

        public async Task<BookmarkState?> ReadBookmarkAsync(string bookmarkId)
        {
            await using var operation = await OpenPartitionScopeAsync();
            return await operation.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
                .FindAsync(WorkflowExecutionId, bookmarkId);
        }

        public async Task<EtlRunView?> ReadRunAsync(EtlScope scope, string runId)
        {
            await using var operation = await OpenPartitionScopeAsync();
            return await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>()
                .FindRunAsync(scope, runId);
        }

        public async Task<IReadOnlyList<EtlManifestEntry>> ReadManifestAsync(EtlScope scope, string runId)
        {
            await using var operation = await OpenPartitionScopeAsync();
            return await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>()
                .ListManifestAsync(scope, runId, null, 20);
        }

        public async Task<EtlRuntimeOutboxRecord> ReadOutboxAsync(EtlScope scope, string runId)
        {
            await using var operation = await OpenPartitionScopeAsync();
            var scopeKey = EtlRuntimeIdentity.ScopeKey(scope);
            var runKey = EtlRuntimeRecordCodec.EncodeIdentity(runId, "run id");
            return await operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().Outbox
                .AsNoTracking()
                .SingleAsync(row => row.ScopeKey == scopeKey && row.RunKey == runKey);
        }

        private static EtlHostFileBindingOptions Binding(
            EtlScope scope,
            string alias,
            string role,
            string revision,
            string path,
            bool immutableInput,
            bool atomicPublication) => new()
        {
            TenantId = scope.TenantId,
            PersistencePartition = scope.PersistencePartition,
            Alias = alias,
            Role = role,
            Revision = revision,
            Path = path,
            IsCurrent = true,
            ExpectedSha256 = immutableInput
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
                : null,
            ImmutableInput = immutableInput,
            AtomicPublication = atomicPublication
        };

        private static ExecutableNode Composite(ExecutableNode runEtl, ExecutableNode sibling, CompositeKind compositeKind)
        {
            return compositeKind switch
            {
                CompositeKind.Sequence => Sequence(runEtl, sibling),
                CompositeKind.Flowchart => Flowchart(runEtl, sibling),
                CompositeKind.CompensationScope => Compensation(runEtl, sibling),
                _ => Sequence(Compensation(runEtl, sibling, faultAfterEtl: true),
                    WorkflowExecutionHarness.NewProbeNode("after-composite"), "outer-sequence")
            };
        }

        private ExecutableNode Composite(ExecutableNode runEtl, ExecutableNode sibling) =>
            Composite(runEtl, sibling, CompositeKind);

        private static ExecutableNode Sequence(ExecutableNode first, ExecutableNode second, string nodeId = "sequence-root")
        {
            return new ExecutableNode(
                nodeId,
                $"authored-{nodeId}",
                typeof(SequenceActivity).FullName!,
                "1.0.0",
                new RuntimeActivityDescriptor("test.clr-activity", RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(new { type = typeof(SequenceActivity).FullName })),
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, string>(),
                childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [first, second])],
                structure: new ExecutableActivityStructure(
                    SequenceActivity.StructureKind,
                    SequenceActivity.StructureSchemaVersion,
                    JsonSerializer.SerializeToElement(new SequenceExecutableStructure([first.ExecutableNodeId, second.ExecutableNodeId]))));
        }

        private static ExecutableNode Flowchart(ExecutableNode runEtl, ExecutableNode sibling)
        {
            var nodeId = "flowchart-root";
            return new ExecutableNode(
                nodeId,
                "authored-flowchart-root",
                typeof(FlowchartActivity).FullName!,
                "1.0.0",
                new RuntimeActivityDescriptor("test.clr-activity", RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(new { type = typeof(FlowchartActivity).FullName })),
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, string>(),
                childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [runEtl, sibling])],
                structure: new ExecutableActivityStructure(
                    FlowchartActivity.StructureKind,
                    FlowchartActivity.StructureSchemaVersion,
                    JsonSerializer.SerializeToElement(new FlowchartStructure(
                        [new FlowchartConnection(new FlowchartEndpoint(runEtl.ExecutableNodeId), new FlowchartEndpoint(sibling.ExecutableNodeId))],
                        runEtl.ExecutableNodeId))));
        }

        private static ExecutableNode Compensation(ExecutableNode runEtl, ExecutableNode sibling, bool faultAfterEtl = false)
        {
            var children = new List<(string Name, ExecutableNode Node)>
            {
                ("Extract", runEtl),
                ("Continue", sibling)
            };
            if (faultAfterEtl)
                children.Add(("Fail later", WorkflowExecutionHarness.NewFaultingNode("fail-after-etl")));
            var steps = children.Select(child => new CompensationScopeExecutableStep(child.Name, child.Node.ExecutableNodeId)).ToArray();
            return new ExecutableNode(
                "compensation-root", "authored-compensation-root", typeof(CompensationActivity).FullName!, "1.0.0",
                new RuntimeActivityDescriptor("test.clr-activity", RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(new { type = typeof(CompensationActivity).FullName })),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
                childSlots: children.Select(child => new ExecutableChildSlot(CompensationActivity.StepSlotName(child.Name), [child.Node])).ToArray(),
                structure: new ExecutableActivityStructure(
                    CompensationActivity.StructureKind, CompensationActivity.StructureSchemaVersion,
                    JsonSerializer.SerializeToElement(new CompensationScopeExecutableStructure(steps),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        }

        private static ExecutableNode RunEtlNode(string token, IReadOnlyDictionary<string, string>? metadata = null)
        {
            var type = new ValueTypeDescriptor("String");
            var binding = new RuntimeInputBinding(
                nameof(RunEtl.Pipeline),
                type,
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(token), ValueProtectionPolicy.InstanceInline));
            return new ExecutableNode(
                "run-etl",
                "authored-run-etl",
                RunEtl.ActivityType,
                "1.0.0",
                new RuntimeActivityDescriptor(
                    "test.clr-activity",
                    RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(new { type = RunEtl.ActivityType })),
                new Dictionary<string, RuntimeInputBinding> { [nameof(RunEtl.Pipeline)] = binding },
                metadata ?? new Dictionary<string, string>());
        }

        private async ValueTask<PersistenceOperationScope> OpenPartitionScopeAsync(CancellationToken cancellationToken = default) =>
            await Harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>()
                .CreateAsync(new PersistenceScope(Scope.PersistencePartition), cancellationToken);

        private async Task CopyPublicationIntoPartitionAsync(WorkflowExecutableSourceReference reference)
        {
            WorkflowExecutable executable;
            await using (var source = await Harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>()
                             .CreateAsync(new PersistenceScope(PersistenceScope.DefaultValue)))
                executable = Assert.IsType<WorkflowExecutable>(await source.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>()
                    .FindAsync(reference.ArtifactId));

            await using var target = await OpenPartitionScopeAsync();
            await target.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
            await target.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference);
        }

        public Task DriveAsync() => DrivePartitionAsync(Harness, WorkflowExecutionId);

        private static async Task DrivePartitionAsync(WorkflowExecutionHarness harness, string executionId)
        {
            await using var operation = await harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>()
                .CreateAsync(new PersistenceScope(WorkflowExecutionHarness.Partition.Value));
            var queue = operation.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
            var runtime = operation.ServiceProvider.GetRequiredService<IRuntimeResumptionService>();
            for (var pass = 0; pass < 8; pass++)
            {
                await runtime.SweepAsync(new RuntimeResumptionSweepRequest(
                    backlogBatchSize: 100,
                    recoveryScanBatchSize: 100,
                    maxExecutionsPerSweep: 100));
                if ((await queue.ListAsync(new RuntimeSchedulerWorkQuery(executionId, limit: 100))).Items.Count == 0)
                    return;
            }

            throw new InvalidOperationException($"Scheduler work for '{executionId}' did not drain after eight bounded sweeps.");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_workerStarted)
                    await _worker.StopAsync(CancellationToken.None);
            }
            finally
            {
                try
                {
                    await Harness.DisposeAsync();
                }
                finally
                {
                    try
                    {
                        await _lifetimeHost.StopAsync();
                    }
                    finally
                    {
                        _lifetimeHost.Dispose();
                        if (!_retainFailureEvidence && Directory.Exists(_directory))
                            Directory.Delete(_directory, recursive: true);
                    }
                }
            }
        }
    }
}
