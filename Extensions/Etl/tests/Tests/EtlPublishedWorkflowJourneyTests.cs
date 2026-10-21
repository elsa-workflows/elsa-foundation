using System.Text;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
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
using Elsa.Etl.Workflows.Runtime;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Etl.Workflows.Runtime.Services;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Hosting;
using Elsa.Primitives.Models;
using Elsa.Tasks.Core;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Etl.Tests;

/// <summary>Exercises persisted native publication followed by execution in a separate runtime-only provider.</summary>
public sealed class EtlPublishedWorkflowJourneyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Published_parent_keeps_its_ETL_version_after_edit_and_runs_without_design_services(bool flowchart)
    {
        await using var published = await EtlPublishedWorkflowFixture.CreateAsync(flowchart);
        // All authoring scopes and connections are disposed before taking the design database offline.
        var offlineDesignDatabase = published.EtlDesignDatabase + ".offline";
        File.Move(published.EtlDesignDatabase, offlineDesignDatabase);
        await using var runtime = await RuntimeHost.CreateAsync(published);
        try
        {
            Assert.NotEqual(published.OriginalArtifact.DefinitionVersion, published.UpdatedArtifact.DefinitionVersion);
            Assert.NotEqual(published.OriginalArtifact.ContentHash, published.UpdatedArtifact.ContentHash);
            await runtime.AssertRuntimeOnlyAsync();
            await runtime.StartAsync();

            // Observe the actual frozen bookmark and invocation before starting the independent ETL worker.
            var invocation = await runtime.WaitForSuspensionAsync();
            Assert.Equal(published.OriginalArtifact.ArtifactId, invocation.Artifact.ArtifactId);
            Assert.Equal(published.OriginalArtifact.ContentHash, invocation.Artifact.ContentHash);
            Assert.Equal(published.Executable.Identity.ArtifactId, invocation.ResultAddress.ExecutableId);
            Assert.Equal(published.Executable.Identity.ArtifactHash, invocation.ResultAddress.ExecutableHash);

            await runtime.StartWorkerAsync();
            await runtime.WaitForCompletionAsync(invocation);
            var native = await runtime.ReadNativeAsync();
            Assert.Equal(WorkflowExecutionStatus.Completed, native.Workflow.Status);
            foreach (var node in published.Executable.RootActivity.DescendantsAndSelf())
                Assert.Equal(ActivityExecutionStatus.Completed,
                    Assert.Single(native.Activities, activity => activity.Execution.ExecutableNodeId == node.ExecutableNodeId).Status);

            var child = Assert.Single(native.Activities, activity => activity.Execution.ActivityType == RunEtl.ActivityType);
            var receipt = Assert.Single(child.TriggerDeliveries ?? []);
            Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, receipt.Status);
            Assert.Equal(invocation.ResultAddress.RegistrationId, receipt.RegistrationId);
            Assert.Equal(invocation.ResultAddress.ProviderId, receipt.ProviderId);
            Assert.Equal(EtlNativeBoundary.CompletionType, receipt.PayloadType);
            Assert.Equal(ValuePresence.Absent, receipt.Payload.Presence);
            Assert.Null(child.PrivateState);

            await using var operation = await runtime.OpenScopeAsync();
            var services = operation.ServiceProvider;
            var store = services.GetRequiredService<IEtlRuntimeStore>();
            var run = Assert.IsType<EtlRunView>(await store.FindRunAsync(published.Scope, invocation.RunId));
            Assert.Equal(EtlRunStatus.Succeeded, run.Status);
            var result = Assert.IsType<EtlTerminalResult>(run.TerminalResult);
            Assert.Equal(1, result.AcceptedRecords);
            Assert.Equal(0, result.RejectedRecords);
            var completed = Assert.IsType<ActivityCompletion>(child.Completion);
            var typed = Assert.IsType<JsonElement>(completed.Result.InlineValue)
                .Deserialize<RunEtlResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(new RunEtlResult(invocation.RunId, result.Outcome, 1, 0, 0, null), typed);
            Assert.Null(await services.GetRequiredService<IBookmarkStateStore>()
                .FindAsync(runtime.ExecutionId, invocation.ResultAddress.BookmarkId));
            var manifest = await store.ListManifestAsync(published.Scope, invocation.RunId, null, 20);
            var output = Assert.Single(manifest);
            Assert.Equal(EtlOutputRoute.Main, output.Effect.Route);
            Assert.Equal("{\"name\":\"Ada\"}\n",
                await File.ReadAllTextAsync(Path.Combine(published.OutputRoot, output.Effect.Chunk.Key), Encoding.UTF8));
            Assert.False(File.Exists(published.EtlDesignDatabase));
            Assert.True(File.Exists(offlineDesignDatabase));
        }
        catch (Exception exception)
        {
            await runtime.PreserveFailureAsync(exception);
            throw;
        }
    }

    private sealed class RuntimeHost(
        EtlPublishedWorkflowFixture published, IHost lifetime, WorkflowExecutionHarness harness, IBackgroundTask worker,
        string executionId) : IAsyncDisposable
    {
        public WorkflowExecutionHarness Harness { get; } = harness;
        public string ExecutionId { get; } = executionId;
        private bool _workerStarted;

        public static async Task<RuntimeHost> CreateAsync(EtlPublishedWorkflowFixture published)
        {
            var lifetime = Host.CreateApplicationBuilder().Build();
            WorkflowExecutionHarness? harness = null;
            try
            {
                await lifetime.StartAsync();
                var executionId = "etl-published-parent-" + Guid.NewGuid().ToString("N");
                harness = WorkflowExecutionHarness.Create()
                    .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
                    .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
                    .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
                    .WithFeature(services => new PrimitivesFeature().ConfigureServices(services))
                    .WithFeature(services => new ExpressionsFeature().ConfigureServices(services))
                    .WithFeature(services => new EventsFeature().ConfigureServices(services))
                    .WithFeature(services => new EtlRuntimeFeature
                    {
                        SnapshotRoot = published.SnapshotRoot, FileBindings = published.Bindings.ToList()
                    }.ConfigureServices(services))
                    .WithFeature(services => new EtlRuntimeEntityFrameworkCoreFeature
                    {
                        ConnectionString = $"Data Source={published.EtlRuntimeDatabase};Pooling=False"
                    }.ConfigureServices(services))
                    .WithFeature(services => new EtlWorkflowsRuntimeFeature().ConfigureServices(services))
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddSingleton(lifetime.Services.GetRequiredService<IHostApplicationLifetime>());
                        services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                        {
                            Provider = "Sqlite", ConnectionString = $"Data Source={published.WorkflowDatabase};Pooling=False",
                            RecoveryContinuationSigningKey = "etl-published-parent-recovery-key-32",
                            HierarchyCursorSigningKey = "etl-published-parent-hierarchy-key-32"
                        }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                        services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);
                    })
                    .WithPersistenceMode(PersistenceMode.Durable)
                    .Build(published.Executable.Identity, executionId,
                        Enumerable.Range(1, 32).Select(index => $"{executionId}-activity-{index}"));
                foreach (var initializer in harness.Services.GetServices<IShellInitializer>())
                    await initializer.InitializeAsync();
                harness.InitializeActivityTypes();
                return new RuntimeHost(published, lifetime, harness,
                    Assert.Single(harness.Services.GetServices<IBackgroundTask>().OfType<EtlRuntimeWorker>()), executionId);
            }
            catch
            {
                if (harness is not null)
                    await harness.DisposeAsync();
                await lifetime.StopAsync();
                lifetime.Dispose();
                throw;
            }
        }

        public async Task AssertRuntimeOnlyAsync()
        {
            await using var operation = await OpenScopeAsync();
            Assert.Null(operation.ServiceProvider.GetService<IEtlDefinitionStore>());
            Assert.Null(operation.ServiceProvider.GetService<IEtlArtifactCompiler>());
            Assert.Null(operation.ServiceProvider.GetService<EtlDesignDbContext>());
            Assert.Empty(operation.ServiceProvider.GetServices<IExecutableCompilationSource>());
            Assert.Null(operation.ServiceProvider.GetService<IWorkflowExecutableCompiler>());
            var stored = Assert.IsType<WorkflowExecutable>(await operation.ServiceProvider
                .GetRequiredService<IWorkflowExecutableStore>().FindAsync(published.Reference.ArtifactId));
            Assert.Equal(published.Executable.Identity, stored.Identity);
            Assert.Empty(stored.Dependencies);
            var node = Assert.Single(stored.Nodes, node => node.ActivityType == RunEtl.ActivityType);
            Assert.True(node.Metadata.TryGetValue(EtlNativeBoundary.PinKey, out var envelope),
                "The normal publication path must persist the ETL compilation contribution.");
            var selection = node.InputBindings[nameof(RunEtl.Pipeline)].LiteralValue!.Value.GetString()!;
            Assert.Equal(published.OriginalArtifact.ArtifactId, EtlNativeBoundary.ReadPin(envelope, selection).ArtifactId);
        }

        public async Task StartAsync()
        {
            // A transport binds the trusted persistence scope before dispatch. The generic harness start helper
            // creates an unbound DI scope, which cannot read this genuinely partition-owned publication.
            await using var operation = await OpenScopeAsync();
            var start = await operation.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
                new WorkflowExecutionStartDispatchRequest(
                    artifactId: published.Reference.ArtifactId,
                    requestedBy: "etl-published-parent-test",
                    workflowExecutionId: ExecutionId,
                    idempotencyKey: $"start:{ExecutionId}",
                    metadata: null, variables: null, inputs: null, stimulusInput: null, triggerNodeId: null,
                    runKind: WorkflowRunKind.PublishedRun,
                    sourceSelection: new WorkflowExecutableSourceSelection(published.Reference.SourceReferenceId),
                    provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                    parentWorkflowExecutionId: null, correlationId: null,
                    tenantId: published.Scope.TenantId,
                    partition: new WorkflowExecutionPartition(published.Scope.PersistencePartition),
                    authority: new WorkflowExecutionAuthoritySnapshot("etl-published-parent-test", "etl-published-parent-test")));
            Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, start.CommandDispatch.Status);
        }

        public async Task<EtlInvocation> WaitForSuspensionAsync()
        {
            EtlInvocation? invocation = null;
            await WaitForAsync(async () =>
            {
                var native = await ReadNativeAsync();
                var child = native.Activities.SingleOrDefault(activity => activity.Execution.ActivityType == RunEtl.ActivityType);
                if (child?.Status != ActivityExecutionStatus.Suspended)
                    return false;
                invocation = EtlNativeBoundary.Invocation(child);
                await using var operation = await OpenScopeAsync();
                var bookmark = await operation.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
                    .FindAsync(ExecutionId, invocation.ResultAddress.BookmarkId);
                var run = await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>()
                    .FindRunAsync(published.Scope, invocation.RunId);
                if (bookmark is null || run is null)
                    return false;
                EtlNativeBoundary.EnsureBookmark(invocation.ResultAddress, bookmark);
                Assert.Null(run.TerminalResult);
                var followingNode = Assert.Single(published.Executable.Nodes,
                    node => node.ActivityType == typeof(Elsa.Activities.Primitives.Activities.WriteLine).FullName);
                Assert.DoesNotContain(native.Activities, activity => activity.Execution.ExecutableNodeId == followingNode.ExecutableNodeId);
                return true;
            });
            return Assert.IsType<EtlInvocation>(invocation);
        }

        public async Task StartWorkerAsync()
        {
            await worker.StartAsync(CancellationToken.None);
            _workerStarted = true;
        }

        public Task WaitForCompletionAsync(EtlInvocation invocation) => WaitForAsync(async () =>
            (await ReadNativeAsync()).Workflow.Status == WorkflowExecutionStatus.Completed &&
            (await ReadOutboxAsync(invocation)).AcknowledgedAtUtcTicks is not null);

        private async Task<EtlRuntimeOutboxRecord> ReadOutboxAsync(EtlInvocation invocation)
        {
            await using var operation = await OpenScopeAsync();
            var scopeKey = EtlRuntimeIdentity.ScopeKey(published.Scope);
            var runKey = EtlRuntimeRecordCodec.EncodeIdentity(invocation.RunId, "run id");
            return await operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().Outbox.AsNoTracking()
                .SingleAsync(row => row.ScopeKey == scopeKey && row.RunKey == runKey);
        }

        public async Task<(WorkflowExecutionState Workflow, IReadOnlyCollection<ActivityExecutionState> Activities)> ReadNativeAsync()
        {
            await using var operation = await OpenScopeAsync();
            var services = operation.ServiceProvider;
            return (Assert.IsType<WorkflowExecutionState>(await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(ExecutionId)),
                await services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(ExecutionId));
        }

        public ValueTask<PersistenceOperationScope> OpenScopeAsync() => Harness.Services
            .GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new PersistenceScope(published.Scope.PersistencePartition));

        private async Task WaitForAsync(Func<Task<bool>> completed)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await using (var operation = await OpenScopeAsync())
                    await operation.ServiceProvider.GetRequiredService<IRuntimeResumptionService>()
                        .SweepAsync(new RuntimeResumptionSweepRequest(backlogBatchSize: 100, recoveryScanBatchSize: 100, maxExecutionsPerSweep: 100));
                if (await completed())
                    return;
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
            }
        }

        public async Task PreserveFailureAsync(Exception exception)
        {
            // Keep a separate diagnostic copy before fixture cleanup, without retaining a running worker.
            var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.PublicationFailure", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var native = await ReadNativeAsync();
                await using var operation = await OpenScopeAsync();
                var runs = await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>()
                    .ListRunsAsync(published.Scope, null, 20);
                var queue = await operation.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                    .ListAsync(new RuntimeSchedulerWorkQuery(ExecutionId, limit: 100));
                await File.WriteAllTextAsync(Path.Combine(directory, "native-state.json"),
                    JsonSerializer.Serialize(new
                    {
                        Error = exception.ToString(), published.Executable.Identity, published.Reference,
                        native.Workflow, native.Activities, Runs = runs, Queue = queue.Items
                    },
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception diagnosticFailure)
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "diagnostic-error.txt"),
                    exception + Environment.NewLine + diagnosticFailure);
            }
            Console.WriteLine($"ETL persisted-publication diagnostic retained at '{directory}'.");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_workerStarted)
                    await worker.StopAsync(CancellationToken.None);
            }
            finally
            {
                try { await Harness.DisposeAsync(); }
                finally
                {
                    try { await lifetime.StopAsync(); }
                    finally { lifetime.Dispose(); }
                }
            }
        }
    }
}
