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
using System.Data.Common;
#if MODEL_POSTGRESQL
using Npgsql;
#else
using Microsoft.Data.Sqlite;
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

return await ModelProbe.RunAsync(args);

internal static partial class ModelProbe
{
#if MODEL_POSTGRESQL
    private const string ProfileProvider = "PostgreSql";
#else
    private const string ProfileProvider = "Sqlite";
#endif
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
            if (command.Provider != ProfileProvider) throw new InvalidDataException("The model command provider differs from this compiled package profile.");
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
                { options.Provider = command.Provider; options.ConnectionString = command.JobsConnectionString; options.Schema = command.Schema; });
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
                Provider = command.Provider, ConnectionString = command.RuntimeConnectionString, Schema = command.Schema, Pooling = false,
                CacheWorkflowExecutables = false, RecoveryContinuationSigningKey = new string('r', 32),
                HierarchyCursorSigningKey = new string('h', 32)
            }).AddEfModuleMigrations<RuntimeDbContext>(command.Provider);
            if (command.PreWaitCut is { } cut) PreWaitCut.Register(builder.Services, cut);
            SupplementalJobsControl.Register(builder.Services, command.ContinuationCut, command.GrantReadFault, command.AdmissionCut);
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

    private static object[] LoadedProducts() => new[] { typeof(ModelProbe).Assembly,
#if MODEL_POSTGRESQL
        typeof(NpgsqlConnection).Assembly,
#else
        typeof(SqliteConnection).Assembly,
#endif
        typeof(IJobCommands).Assembly, typeof(JobsFeature).Assembly,
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
            case "provider-version": return new { provider = command.Provider,
                version = await ProviderVersionAsync(command.JobsConnectionString, ct),
                runtimeVersion = await ProviderVersionAsync(command.RuntimeConnectionString, ct) };
            case "create": return await CreateAsync(services, command, ct);
            case "send-start": return await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
                .ProcessAsync(new(16, command.ExecutionId, BpmnStartKind), ct);
            case "sweep": return await services.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new(maxExecutionsPerSweep: 16), ct);
            case "inspect": return await InspectAsync(services, command, ct);
            case "retry-new": return await commands.RetryAsNewRunAsync(new(command.Scope, command.JobId,
                command.ExpectedRevision, command.ProposedJobId, command.IdempotencyKey,
                new(command.OperationId, "model-package-witness", "Retry as a linked independent run")), ct);
            case "cancel-job": return await commands.CancelAsync(new(command.Scope, command.JobId,
                new(command.OperationId, "model-package-witness", "Select actual queued Jobs cancellation")), ct);
            case "replay-source": return await SupplementalSourceCommands.ReplayAsync(services, command, ct);
            case "old-source-duplicate": return await SupplementalSourceCommands.DuplicateAsync(services, command, ct);
            case "replay-origin-fresh-key": return await SupplementalSourceCommands.ReplayOriginAsync(services, command, ct);
            case "source-status": return await SupplementalSourceCommands.ReadAsync(services, command, ct);
            case "origin":
                var admission = command.SavedAdmission ?? throw new InvalidDataException("Exact captured admission required.");
                return await jobs.FindOriginAsync(command.Scope, admission.Origin ?? throw new InvalidDataException("Exact protected origin required."), ct);
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

    private static async Task<string> ProviderVersionAsync(string connectionString, CancellationToken ct)
    {
        await using DbConnection connection =
#if MODEL_POSTGRESQL
            new NpgsqlConnection(connectionString);
#else
            new SqliteConnection(connectionString);
#endif
        await connection.OpenAsync(ct);
        await using var query = connection.CreateCommand();
        query.CommandText = ProfileProvider == "Sqlite" ? "SELECT sqlite_version()" : "SELECT version()";
        return (string)(await query.ExecuteScalarAsync(ct))!;
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
            var physical = await jobs.ListPhysicalReceiptInspectionsAsync(command.Scope, job.JobId, new(100, null), ct);
            if (attempts.ContinuationToken is not null) throw new InvalidDataException("The owned witness exceeded its finite attempt page.");
            if (physical.ContinuationToken is not null) throw new InvalidDataException("The owned witness exceeded its finite physical receipt page.");
            var delivery = await jobs.FindDeliveryAsync(command.Scope, job.JobId, ct);
            deliveries.Add(new { job.JobId, delivery, attempts = attempts.Items, physicalReceipts = physical.Items,
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
        var rootSource = command.ModelKind != "bpmn" ? null : await services.GetRequiredService<IPostCommitOutboxLookupStore>()
            .FindUndeliveredDurableAsync(command.ExecutionId, [], ct);
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
            pendingRootSource = rootSource is null ? null : new { rootSource.OutboxItemId, rootSource.Status, rootSource.Intent.Kind },
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
    JobAssociationMode AssociationMode = JobAssociationMode.Awaited, bool KeepParentActive = false,
    string Provider = "Sqlite", string? Schema = null, PreWaitCutOptions? PreWaitCut = null,
    ContinuationCutOptions? ContinuationCut = null, GrantReadFaultOptions? GrantReadFault = null,
    string JobId = "", string ProposedJobId = "", string IdempotencyKey = "", JobExecutionPolicy? Policy = null,
    AdmissionCutOptions? AdmissionCut = null, PostCommitDeliveryContext? SavedSource = null, JobAdmission? SavedAdmission = null);
