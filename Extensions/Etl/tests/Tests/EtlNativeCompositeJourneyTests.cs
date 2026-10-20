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
using Elsa.Etl.Workflows.Runtime.Models;
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
    public Task Published_RunEtl_resumes_its_composite_before_the_following_sibling(CompositeKind compositeKind) =>
        WithJourneyAsync(compositeKind, NativeOutcome.Succeeded, async journey =>
        {
            var invocation = await journey.StartSuspendedAsync();

            await journey.StartWorkerAsync();
            await journey.WaitForNativeResultAsync(invocation, ActivityExecutionStatus.Completed, WorkflowExecutionStatus.Completed);

            var completed = await journey.ReadNativeStateAsync();
            Assert.Equal(WorkflowExecutionStatus.Completed, completed.Workflow.Status);
            var resumedEtl = Assert.Single(completed.Activities, state => state.Execution.ExecutableNodeId == "run-etl");
            Assert.Equal(ActivityExecutionStatus.Completed, resumedEtl.Status);
            await AssertConsumedDeliveryAsync(journey, invocation, resumedEtl);
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
            AssertCompletionResult(resumedEtl, run);
            var manifest = await journey.ReadManifestAsync(invocation.Scope, invocation.RunId);
            var chunk = Assert.Single(manifest, entry => entry.Effect.Route == EtlOutputRoute.Main);
            var outputPath = Path.Combine(journey.OutputRoot, chunk.Effect.Chunk.Key);
            Assert.Equal("{\"name\":\"Ada\"}\n", await File.ReadAllTextAsync(outputPath, Encoding.UTF8));
        });

    [Theory]
    [InlineData(CompositeKind.Sequence, NativeOutcome.BusinessError)]
    [InlineData(CompositeKind.Flowchart, NativeOutcome.BusinessError)]
    [InlineData(CompositeKind.CompensationScope, NativeOutcome.BusinessError)]
    [InlineData(CompositeKind.Sequence, NativeOutcome.Faulted)]
    [InlineData(CompositeKind.Flowchart, NativeOutcome.Faulted)]
    [InlineData(CompositeKind.CompensationScope, NativeOutcome.Faulted)]
    [InlineData(CompositeKind.Sequence, NativeOutcome.Cancelled)]
    [InlineData(CompositeKind.Flowchart, NativeOutcome.Cancelled)]
    [InlineData(CompositeKind.CompensationScope, NativeOutcome.Cancelled)]
    public Task Published_ETL_terminal_outcome_obeys_the_surrounding_composite(CompositeKind compositeKind, NativeOutcome outcome) =>
        WithJourneyAsync(compositeKind, outcome, async journey =>
        {
            var invocation = await journey.StartSuspendedAsync();
            if (outcome == NativeOutcome.Cancelled)
                await journey.CancelEtlAsync(invocation);

            var childStatus = outcome switch
            {
                NativeOutcome.BusinessError => ActivityExecutionStatus.Completed,
                NativeOutcome.Faulted => ActivityExecutionStatus.Faulted,
                _ => ActivityExecutionStatus.Cancelled
            };
            var parentStatus = outcome switch
            {
                NativeOutcome.BusinessError => WorkflowExecutionStatus.Completed,
                NativeOutcome.Faulted when compositeKind == CompositeKind.CompensationScope => WorkflowExecutionStatus.Completed,
                NativeOutcome.Faulted => WorkflowExecutionStatus.Faulted,
                _ => WorkflowExecutionStatus.Cancelled
            };
            await journey.StartWorkerAsync();
            await journey.WaitForNativeResultAsync(invocation, childStatus, parentStatus);

            var native = await journey.ReadNativeStateAsync();
            Assert.Equal(parentStatus, native.Workflow.Status);
            var child = Assert.Single(native.Activities, state => state.Execution.ExecutableNodeId == "run-etl");
            Assert.Equal(childStatus, child.Status);
            await AssertConsumedDeliveryAsync(journey, invocation, child);
            var run = Assert.IsType<EtlRunView>(await journey.ReadRunAsync(invocation.Scope, invocation.RunId));
            var expectedOutcome = outcome switch
            {
                NativeOutcome.BusinessError => EtlTerminalOutcome.BusinessError,
                NativeOutcome.Faulted => EtlTerminalOutcome.Faulted,
                _ => EtlTerminalOutcome.Cancelled
            };
            Assert.Equal(expectedOutcome, run.TerminalResult!.Outcome);
            Assert.Equal(outcome switch
            {
                NativeOutcome.BusinessError => EtlRunStatus.BusinessError,
                NativeOutcome.Faulted => EtlRunStatus.Faulted,
                _ => EtlRunStatus.Cancelled
            }, run.Status);
            var composite = Assert.Single(native.Activities, state => state.Execution.ExecutableNodeId == journey.CompositeNodeId);
            if (outcome == NativeOutcome.BusinessError)
            {
                Assert.Equal(["BusinessError"], WorkflowExecutionRun.CompletionOutcomes(child));
                AssertCompletionResult(child, run);
                Assert.Equal(0, run.TerminalResult.AcceptedRecords);
                Assert.Equal(1, run.TerminalResult.RejectedRecords);
                Assert.Equal(ActivityExecutionStatus.Completed,
                    Assert.Single(native.Activities, state => state.Execution.ExecutableNodeId == "after-etl").Status);
                Assert.Equal(ActivityExecutionStatus.Completed, composite.Status);
                var errors = await journey.ReadManifestAsync(invocation.Scope, invocation.RunId);
                Assert.DoesNotContain(errors, entry => entry.Effect.Route == EtlOutputRoute.Main);
                Assert.Single(errors, entry => entry.Effect.Route == EtlOutputRoute.Error);
            }
            else
            {
                Assert.DoesNotContain(native.Activities, state => state.Execution.ExecutableNodeId == "after-etl");
                Assert.Empty(await journey.ReadManifestAsync(invocation.Scope, invocation.RunId));
                if (outcome == NativeOutcome.Faulted)
                {
                    Assert.Null(child.Completion);
                    Assert.Equal("etl.output-write-failed", run.TerminalResult.Diagnostic?.Code);
                    Assert.Equal("etl.output-write-failed", child.Fault?.Code);
                    if (compositeKind == CompositeKind.CompensationScope)
                        Assert.Equal([CompensationActivity.CompensatedOutcome], WorkflowExecutionRun.CompletionOutcomes(composite));
                    else if (compositeKind == CompositeKind.Flowchart)
                    {
                        Assert.Equal(ActivityExecutionStatus.Faulted, composite.Status);
                        Assert.Equal("flowchart.child.faulted", composite.Fault?.Code);
                    }
                }
                else
                {
                    Assert.Null(child.Completion);
                    Assert.Equal(ActivityExecutionStatus.Cancelled, composite.Status);
                }
            }
        });

    private static async Task WithJourneyAsync(CompositeKind kind, NativeOutcome outcome, Func<NativeEtlJourney, Task> test)
    {
        await using var journey = await NativeEtlJourney.CreateAsync(kind, outcome);
        try
        {
            await test(journey);
        }
        catch (Exception exception)
        {
            await journey.PreserveFailureAsync(exception);
            throw;
        }
    }

    private static void AssertCompletionResult(ActivityExecutionState child, EtlRunView run)
    {
        var terminal = Assert.IsType<EtlTerminalResult>(run.TerminalResult);
        var completed = Assert.IsType<ActivityCompletion>(child.Completion);
        var value = Assert.IsType<JsonElement>(completed.Result.InlineValue);
        Assert.Equal(new RunEtlResult(run.Invocation.RunId, terminal.Outcome, terminal.AcceptedRecords,
            terminal.RejectedRecords, terminal.FilteredRecords, terminal.Diagnostic?.Code),
            value.Deserialize<RunEtlResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static async Task AssertConsumedDeliveryAsync(NativeEtlJourney journey, EtlInvocation invocation, ActivityExecutionState child)
    {
        var receipt = Assert.Single(child.TriggerDeliveries ?? []);
        Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, receipt.Status);
        Assert.Equal(invocation.ResultAddress.RegistrationId, receipt.RegistrationId);
        Assert.Equal(invocation.ResultAddress.ProviderId, receipt.ProviderId);
        Assert.Equal(EtlNativeBoundary.CompletionType, receipt.PayloadType);
        Assert.Null(await journey.ReadBookmarkAsync(invocation.ResultAddress.BookmarkId));
        var delivery = await journey.ReadOutboxAsync(invocation.Scope, invocation.RunId);
        // Delivery may be acknowledged after the scheduler terminates the parent; the exact receipt independently
        // proves that this invocation's typed result really resumed the child.
        Assert.Contains(delivery.Disposition, new int?[]
        {
            (int)EtlDeliveryDisposition.Consumed,
            (int)EtlDeliveryDisposition.AlreadyConsumed,
            (int)EtlDeliveryDisposition.ProvenParentTerminal
        });
        Assert.NotNull(delivery.AcknowledgedAtUtcTicks);
    }

    public enum NativeOutcome { Succeeded, BusinessError, Faulted, Cancelled }

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
            NativeOutcome outcome,
            string workflowExecutionId,
            EtlScope scope)
        {
            _directory = directory;
            _lifetimeHost = lifetimeHost;
            Harness = harness;
            _worker = worker;
            CompositeKind = compositeKind;
            Outcome = outcome;
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
        private NativeOutcome Outcome { get; }
        public string WorkflowExecutionId { get; }
        public string CompositeNodeId { get; }
        public EtlScope Scope { get; }
        public string OutputRoot { get; }

        public static async Task<NativeEtlJourney> CreateAsync(CompositeKind compositeKind, NativeOutcome outcome = NativeOutcome.Succeeded)
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

                await File.WriteAllTextAsync(sourcePath,
                    outcome == NativeOutcome.BusinessError ? "id,name\nnot-an-int,Ada\n" : "id,name\n1,Ada\n", new UTF8Encoding(false));
                if (outcome == NativeOutcome.Faulted)
                    await File.WriteAllTextAsync(snapshotRoot, "Host snapshot directory is unavailable.");
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
                return new NativeEtlJourney(directory, lifetimeHost, harness, worker, compositeKind, outcome, workflowExecutionId, scope);
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

        public async Task<EtlInvocation> StartSuspendedAsync()
        {
            var reference = await PublishWorkflowAsync();
            var started = await Harness.StartPublishedAsync(reference, WorkflowExecutionId);
            Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, started.CommandDispatch.Status);
            EtlInvocation? invocation = null;
            await WaitForAsync(async () =>
            {
                var native = await ReadNativeStateAsync();
                var child = native.Activities.SingleOrDefault(state => state.Execution.ExecutableNodeId == "run-etl");
                if (native.Workflow.Status != WorkflowExecutionStatus.Running || child?.Status != ActivityExecutionStatus.Suspended)
                    return false;
                invocation = EtlNativeBoundary.Invocation(child);
                var bookmark = await ReadBookmarkAsync(invocation.ResultAddress.BookmarkId);
                var admitted = await ReadRunAsync(invocation.Scope, invocation.RunId);
                if (bookmark is null || admitted is null)
                    return false;
                EtlNativeBoundary.EnsureBookmark(invocation.ResultAddress, bookmark);
                Assert.Null(admitted.TerminalResult);
                Assert.DoesNotContain(native.Activities, state => state.Execution.ExecutableNodeId == "after-etl");
                return true;
            });
            return Assert.IsType<EtlInvocation>(invocation);
        }

        public async Task CancelEtlAsync(EtlInvocation invocation)
        {
            await using var operation = await OpenPartitionScopeAsync();
            var cancelled = await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>().CancelAsync(invocation);
            Assert.Equal(EtlStoreStatus.Applied, cancelled.Status);
        }

        public async Task StartWorkerAsync()
        {
            Assert.False(_workerStarted);
            await _worker.StartAsync(CancellationToken.None);
            _workerStarted = true;
        }

        public Task WaitForNativeResultAsync(EtlInvocation invocation, ActivityExecutionStatus childStatus, WorkflowExecutionStatus parentStatus) =>
            WaitForAsync(async () =>
            {
                var native = await ReadNativeStateAsync();
                if (native.Workflow.Status != parentStatus || !native.Activities.Any(state =>
                        state.Execution.ExecutableNodeId == "run-etl" && state.Status == childStatus))
                    return false;
                return (await ReadOutboxAsync(invocation.Scope, invocation.RunId)).AcknowledgedAtUtcTicks is not null;
            });

        private async Task WaitForAsync(Func<Task<bool>> completed)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await DriveAsync();
                if (await completed())
                    return;
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
            }
        }

        public async Task PreserveFailureAsync(Exception exception)
        {
            _retainFailureEvidence = true;
            try
            {
                var native = await ReadNativeStateAsync();
                var child = native.Activities.FirstOrDefault(state => state.Execution.ExecutableNodeId == "run-etl");
                var runId = child is null ? null : EtlNativeBoundary.RunId(Scope, WorkflowExecutionId, child.InvocationId);
                var run = runId is null ? null : await ReadRunAsync(Scope, runId);
                var outbox = run?.TerminalResult is null ? null : await ReadOutboxAsync(Scope, runId!);
                await using var operation = await OpenPartitionScopeAsync();
                var queued = await operation.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                    .ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId, limit: 100));
                var diagnostic = JsonSerializer.Serialize(new
                {
                    Error = exception.ToString(), Queue = queued.Items,
                    Workflow = native.Workflow, native.Activities, Run = run, Outbox = outbox
                }, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(_directory, "failure-state.json"), diagnostic);
            }
            catch (Exception diagnosticFailure)
            {
                Console.WriteLine($"Could not capture native ETL diagnostic JSON: {diagnosticFailure.GetType().Name}.");
            }
            Console.WriteLine($"Native ETL failure artifacts retained at '{_directory}'.");
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

        private static ExecutableNode Composite(ExecutableNode runEtl, ExecutableNode sibling, CompositeKind compositeKind, NativeOutcome outcome)
        {
            return compositeKind switch
            {
                CompositeKind.Sequence => Sequence(runEtl, sibling),
                CompositeKind.Flowchart => Flowchart(runEtl, sibling, outcome == NativeOutcome.BusinessError ? "BusinessError" : ActivityOutcomes.Done),
                CompositeKind.CompensationScope => Compensation(runEtl, sibling),
                _ => Sequence(Compensation(runEtl, sibling, faultAfterEtl: true),
                    WorkflowExecutionHarness.NewProbeNode("after-composite"), "outer-sequence")
            };
        }

        private ExecutableNode Composite(ExecutableNode runEtl, ExecutableNode sibling) =>
            Composite(runEtl, sibling, CompositeKind, Outcome);

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

        private static ExecutableNode Flowchart(ExecutableNode runEtl, ExecutableNode sibling, string outcome)
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
                        [new FlowchartConnection(new FlowchartEndpoint(runEtl.ExecutableNodeId, outcome), new FlowchartEndpoint(sibling.ExecutableNodeId))],
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
            var runtime = operation.ServiceProvider.GetRequiredService<IRuntimeResumptionService>();
            // An actor/result worker may own queued work concurrently. The caller waits on durable domain state
            // with a deadline; a nonempty queue after a sweep is not proof of a stuck execution.
            await runtime.SweepAsync(new RuntimeResumptionSweepRequest(
                backlogBatchSize: 100, recoveryScanBatchSize: 100, maxExecutionsPerSweep: 100));
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
