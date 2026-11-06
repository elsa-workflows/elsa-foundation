using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Security.Cryptography;
using CShells.Lifecycle;
using Elsa.Activities.Bpmn;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Primitives;
using Elsa.Activities.Sequence;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Runtime;
using Elsa.Events;
using Elsa.Jobs;
using Elsa.Jobs.Bpmn;
using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Elsa.Jobs.Workflows.Runtime;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Activities;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

return await ModelProbe.RunAsync(args);

internal static partial class ModelProbe
{
    internal static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    internal const string BpmnStartKind = "Elsa.Bpmn.StartNativeWork.v1";
    internal const string BpmnResultKind = "Elsa.Bpmn.NativeWorkResult.v1";

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass one controller-owned JSON command file.");
        var command = JsonSerializer.Deserialize<ModelCommand>(await File.ReadAllTextAsync(args[0]), Probe.Json)
            ?? throw new InvalidDataException("The model probe command is missing.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(command.TimeoutSeconds));
        try
        {
            using var faults = command.AckFault is null ? null : new AckReplyFault(command.AckFault);
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                { Args = [], ContentRootPath = AppContext.BaseDirectory, DisableDefaults = true });
            builder.Logging.AddSimpleConsole(options => options.SingleLine = true).SetMinimumLevel(LogLevel.Warning);
            builder.Services.Configure<EfMigrateOptions>(options => options.Policy = command.Mode == "initialize"
                ? EfMigratePolicy.AutoMigrate : EfMigratePolicy.Validate);
            builder.Services.AddElsaJobs(options =>
            {
                options.EnableWorker = command.Mode == "worker";
                options.MaximumConcurrentAttempts = 2;
                options.WorkerScopes.Add(command.Scope);
                options.PollInterval = TimeSpan.FromMilliseconds(50);
                options.LeaseDuration = TimeSpan.FromSeconds(5);
                options.RenewalInterval = TimeSpan.FromMilliseconds(500);
                options.ShutdownGracePeriod = TimeSpan.FromSeconds(2);
            });
            builder.Services.AddJobsEntityFrameworkCore(options =>
                { options.Provider = "Sqlite"; options.ConnectionString = command.JobsConnectionString; });
            if (command.Mode == "worker") builder.Services.AddJobHandler<ControlledReportHandler, ControlledReportInput, ControlledReportResult>(Probe.Support);
            new EventsFeature().ConfigureServices(builder.Services);
            new SerializationFeature().ConfigureServices(builder.Services);
            new ActivitiesRuntimeFeature().ConfigureServices(builder.Services);
            new ActivitiesPrimitivesFeature().ConfigureServices(builder.Services);
            new ActivitiesSequenceFeature().ConfigureServices(builder.Services);
            new ActivitiesFlowchartFeature().ConfigureServices(builder.Services);
            builder.Services.AddWorkflowRuntime();
            builder.Services.AddPersistenceModes();
            new WorkflowsRuntimeResumptionFeature().ConfigureServices(builder.Services);
            new ActivitiesBpmnHostingFeature { AdmitNewExecutions = true }.ConfigureServices(builder.Services);
            builder.Services.AddJobsWorkflowsRuntime(options =>
            {
                options.EnableResultDelivery = command.Mode is "coordinator" or "deliver-only";
                if (options.EnableResultDelivery) options.DeliveryScopes.Add(command.Scope);
                options.PollInterval = TimeSpan.FromMilliseconds(50);
                options.OperationTimeout = TimeSpan.FromSeconds(20);
            });
            new JobsBpmnFeature().ConfigureServices(builder.Services);
            builder.Services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
            {
                Provider = "Sqlite", ConnectionString = command.RuntimeConnectionString, Pooling = false,
                CacheWorkflowExecutables = false, RecoveryContinuationSigningKey = new string('r', 32),
                HierarchyCursorSigningKey = new string('h', 32)
            }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
            // Force the actual CLR activity assembly into the ordinary startup registration scan.
            _ = typeof(Elsa.Jobs.Workflows.Runtime.Activities.RunJob).Assembly.GetName();
            using var host = builder.Build();
            foreach (var initializer in host.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync(timeout.Token);
            await using (var startup = host.Services.CreateAsyncScope())
                foreach (var task in startup.ServiceProvider.GetServices<IStartupTask>()) await task.ExecuteAsync(timeout.Token);
            await host.StartAsync(timeout.Token);
            object? value;
            if (command.Mode is "worker" or "coordinator" or "deliver-only")
            {
                await Probe.WriteAsync(command.ReadyFile, new { ready = true, processId = Environment.ProcessId, command.Mode, loadedProducts = LoadedProducts() });
                while (!File.Exists(command.StopFile))
                {
                    if (command.Mode == "coordinator")
                    {
                        await using var operation = await ScopeAsync(host.Services, command, timeout.Token);
                        await operation.ServiceProvider.GetRequiredService<IRuntimeResumptionService>()
                            .SweepAsync(new(maxExecutionsPerSweep: 16), timeout.Token);
                    }
                    await Task.Delay(50, timeout.Token);
                }
                value = new { stopped = true };
            }
            else
            {
                await using var operation = await ScopeAsync(host.Services, command, timeout.Token);
                value = await ExecuteAsync(operation.ServiceProvider, command, timeout.Token);
            }
            await host.StopAsync(timeout.Token);
            await Probe.WriteAsync(command.OutputFile, new { ok = true, processId = Environment.ProcessId, value, loadedProducts = LoadedProducts() });
            return 0;
        }
        catch (Exception error)
        {
            await Probe.WriteAsync(command.OutputFile, new { ok = false, processId = Environment.ProcessId,
                code = error is JobException job ? job.Code : error.GetType().Name, error.Message });
            return 1;
        }
    }

    private static ValueTask<PersistenceOperationScope> ScopeAsync(IServiceProvider services, ModelCommand command, CancellationToken ct) =>
        services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new(command.Scope.PersistencePartition), ct);

    private static object[] LoadedProducts() => new[] { typeof(ModelProbe).Assembly, typeof(IJobCommands).Assembly, typeof(JobsFeature).Assembly,
        typeof(JobsEntityFrameworkCoreFeature).Assembly, typeof(Elsa.Jobs.Workflows.Runtime.Activities.RunJob).Assembly,
        typeof(JobsBpmnFeature).Assembly, typeof(ActivitiesBpmnHostingFeature).Assembly, typeof(ActivitiesSequenceFeature).Assembly,
        typeof(ActivitiesFlowchartFeature).Assembly, typeof(IWorkflowStartDispatcher).Assembly,
        typeof(Elsa.Workflows.Runtime.Services.Executions.WorkflowStartDispatcher).Assembly, typeof(RuntimeEntityFrameworkCoreOptions).Assembly }
        .Distinct().Select(assembly => (object)new { name = assembly.GetName().Name, path = assembly.Location,
            informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))) }).ToArray();

    private static async Task<object?> ExecuteAsync(IServiceProvider services, ModelCommand command, CancellationToken ct)
    {
        var jobs = services.GetRequiredService<IJobQueries>();
        var commands = services.GetRequiredService<IJobCommands>();
        switch (command.Mode)
        {
            case "initialize": return new { initialized = true };
            case "provider-version":
                await using (var connection = new SqliteConnection(command.JobsConnectionString))
                {
                    await connection.OpenAsync(ct); await using var query = connection.CreateCommand();
                    query.CommandText = "SELECT sqlite_version()";
                    return new { provider = "Sqlite", version = await query.ExecuteScalarAsync(ct) };
                }
            case "create": return await CreateAsync(services, command, ct);
            case "send-start": return await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
                .ProcessAsync(new(16, command.ExecutionId, BpmnStartKind), ct);
            case "sweep": return await services.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new(maxExecutionsPerSweep: 16), ct);
            case "inspect": return await InspectAsync(services, command, ct);
            case "pause": return await commands.PauseAsync(new(command.Scope, command.Gate ?? throw new InvalidDataException("Gate required."),
                command.HoldId, new(command.OperationId, "model-package-witness", "Hold completed result continuation")), ct);
            case "holds": return await jobs.ListHoldsAsync(command.Scope, command.Gate, new(100, null), ct);
            case "release": return await commands.ReleaseHoldAsync(new(command.Scope, command.Gate ?? throw new InvalidDataException("Gate required."),
                command.HoldId, command.ExpectedRevision, new(command.OperationId, "model-package-witness", "Release exact completed-result hold")), ct);
            case "cancel":
                if (command.ModelKind == "bpmn") return await services.GetRequiredService<IBpmnModelRuntime>().CancelAsync(
                    command.ExecutionId, command.OperationId, "Exact model-owner cancellation witness", new("model-package-witness", null), ct);
                var now = DateTimeOffset.UtcNow;
                var envelope = new WorkflowExecutionCommandEnvelope(command.OperationId, command.ExecutionId,
                    new(command.OperationId, command.ExecutionId, WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>()),
                    command.OperationId, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now, partition: new(command.Scope.PersistencePartition));
                return await (await services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(
                    new(command.ExecutionId, WorkflowExecutionActorActivationReason.ControlPlaneCommand, now, "model-package-witness",
                        WorkflowExecutionActorCapabilities.InProcessMailbox, partition: new(command.Scope.PersistencePartition)), ct)).EnqueueAsync(envelope, ct);
            case "delete-worker":
                var deletion = services.GetRequiredService<IWorkflowExecutionDeletionStore>();
                var candidate = await deletion.ReadAsync(command.WorkerExecutionId ?? command.ExecutionId, ct);
                return new { supported = deletion.IsSupported, result = candidate is null ? WorkflowExecutionDeletionResult.NotFound :
                    await deletion.TryDeleteTerminalAsync(candidate, ct) };
            default: throw new InvalidDataException("Unknown model witness command.");
        }
    }

    private static async Task<object> InspectAsync(IServiceProvider services, ModelCommand command, CancellationToken ct)
    {
        var jobs = services.GetRequiredService<IJobQueries>();
        var page = await jobs.ListJobsAsync(command.Scope, new(new(100, null), null, null, null), ct);
        if (page.ContinuationToken is not null) throw new InvalidDataException("The owned witness exceeded its finite Jobs page.");
        var deliveries = new List<object>();
        foreach (var job in page.Items)
        {
            var attempts = await jobs.ListAttemptsAsync(command.Scope, job.JobId, new(100, null), ct);
            if (attempts.ContinuationToken is not null) throw new InvalidDataException("The owned witness exceeded its finite attempt page.");
            var delivery = await jobs.FindDeliveryAsync(command.Scope, job.JobId, ct);
            deliveries.Add(new { job.JobId, delivery, attempts = attempts.Items,
                retainedGrant = delivery?.Permission is { } grant ? await jobs.FindContinuationAsync(command.Scope, job.JobId, grant.ContinuationId, ct) : null });
        }
        var workerId = command.WorkerExecutionId ?? command.ExecutionId;
        var worker = await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(workerId, ct);
        var activityPage = await services.GetRequiredService<IActivityExecutionStateStore>().ListPageAsync(new(workerId), ct);
        if (activityPage.NextContinuationToken is not null) throw new InvalidDataException("The owned finite model exceeded its activity page.");
        var jobActivities = activityPage.Items.Where(activity => activity.Execution.ActivityType == RunJob.ActivityType).Select(activity => new
        {
            activity.InvocationId, activity.Execution.ExecutableNodeId, activity.ParentActivityExecutionId, activity.ExecutionScopeId,
            activity.EnclosingExecutionScopeId, activity.Status, activity.ContractIdentity, activity.BookmarkIds,
            activity.Attempts, activity.TriggerDeliveries, activity.Completion,
            selectedResult = activity.Status == ActivityExecutionStatus.Completed && activity.Completion?.Result.InlineValue is { } value
                ? value.Deserialize<RunJobResult>(Web) ?? throw new InvalidDataException("The actual RunJob typed result is missing.") : null
        }).ToArray();
        var submissionActivities = activityPage.Items.Where(activity => activity.Execution.ActivityType == SubmitJob.ActivityType).Select(activity => new
        {
            activity.InvocationId, activity.Execution.ExecutableNodeId, activity.ParentActivityExecutionId, activity.Status,
            activity.ContractIdentity, activity.BookmarkIds, activity.Attempts, activity.TriggerRegistrations, activity.TriggerDeliveries,
            activity.Completion,
            submitted = activity.Status == ActivityExecutionStatus.Completed && activity.Completion?.Result.InlineValue is { } value
                ? value.Deserialize<SubmitJobResult>(Web) ?? throw new InvalidDataException("The actual SubmitJob typed result is missing.") : null
        }).ToArray();
        BpmnNativeWorkObservation? owner = null;
        object? model = null;
        if (command.ModelKind == "bpmn")
        {
            var inspection = await services.GetRequiredService<IBpmnModelRuntime>().InspectAsync(command.ExecutionId, new("model-package-witness", null), ct);
            var state = inspection.State;
            model = new { state.ExecutionId, state.Protocol, state.Executable, state.MaterialIncarnationId, state.Revision,
                state.Lifecycle, state.TerminalResult, inspection.Work, inspection.Attempts, inspection.Settlements };
            if (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(command.ExecutionId, ct) is not null)
                throw new InvalidDataException("A genuine BPMN root must have no native workflow header.");
            if (worker?.ModelWorkOrigin is { } origin) owner = await services.GetRequiredService<IBpmnNativeWorkConsumer>().InspectAsync(origin, ct);
        }
        var source = worker is null ? null : await services.GetRequiredService<IPostCommitOutboxLookupStore>()
            .FindUndeliveredDurableAsync(workerId, [], ct);
        var evidence = worker is not null && worker.Status.IsTerminal()
            ? await services.GetRequiredService<IJobNativeDeliveryInspector>().InspectWorkerAsync(worker, ct) : null;
        JobNativeSubmissionEvidence? submissionEvidence = null;
        string? submissionUnavailable = null;
        if (worker is not null && worker.Status.IsTerminal())
        {
            try { submissionEvidence = await services.GetRequiredService<IJobNativeSubmissionInspector>().InspectSubmissionWorkerAsync(worker, ct); }
            catch (JobException error) when (error.Code == "jobs.native.submission-unavailable") { submissionUnavailable = error.Code; }
        }
        return new { jobs = page.Items, deliveries, worker, activities = activityPage.Items, jobActivities, submissionActivities, model, owner,
            nativeEvidence = evidence,
            submissionEvidence, submissionUnavailable,
            pendingSource = source is null ? null : new { source.OutboxItemId, source.Status, source.Intent.Kind,
                source.StimulusDeliverySourceScope, source.StimulusDeliveryProtocolVersion,
                retainsPublisher = source.RetainsPublisherForRequiredDelivery(workerId) } };
    }
}

internal sealed record ModelCommand(string Mode, string JobsConnectionString, string RuntimeConnectionString, JobScope Scope,
    string OutputFile, string ModelKind = "native", string ExecutionId = "", string? WorkerExecutionId = null,
    string DefinitionId = "", string DefinitionVersion = "1", JsonElement? Report = null, string? InvalidInput = null,
    bool SendBpmnStart = true, string ReadyFile = "", string StopFile = "", JobGateKey? Gate = null,
    string HoldId = "", string OperationId = "", long ExpectedRevision = 0, int TimeoutSeconds = 150,
    AckFaultOptions? AckFault = null, string NativeStructure = "root",
    JobAssociationMode AssociationMode = JobAssociationMode.Awaited, bool KeepParentActive = false);
