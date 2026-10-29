using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CShells.Lifecycle;
using Elsa.Activities.Bpmn;
using Elsa.Activities.Bpmn.Core;
using Elsa.Events;
using Elsa.Tasks.Core;

// An owned SDK client, not a state seeder: ordinary Start dispatch writes the real protected queue item.
// The existing drain-policy seam leaves its processing to the standard Workbench recovery host.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
try
{
    if (args.Length is not (4 or 6) || args[0] is not ("--enqueue-start" or "--enqueue-checkpoint" or "--drain-checkpoint" or "--cancel-model") ||
        args.Length == 6 && args[4] != "--gate")
        throw new ArgumentException("Select an owned enqueue/drain/cancel operation, content root, model id, nonce and optional --gate directory.");
    var race = args[0] is "--drain-checkpoint" or "--cancel-model";
    var checkpointCut = args[0] == "--enqueue-checkpoint";
    var root = Path.GetFullPath(args[1]);
    using var marker = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "owned-bpmn-recovery.json")));
    var owner = marker.RootElement;
    if (owner.GetProperty("schemaVersion").GetInt32() != 1 || owner.GetProperty("nonce").GetString() != args[3] ||
        owner.GetProperty("modelExecutionId").GetString() != args[2] || Process.GetProcessById(owner.GetProperty("controllerProcessId").GetInt32()).HasExited)
        throw new InvalidOperationException("The caller has not identified this controller-owned journey.");
    var database = Path.Combine(root, "elsa.db");
    if (!File.Exists(database)) throw new FileNotFoundException("The owned Workbench must create its store first.");
    var services = new ServiceCollection();
    services.AddLogging(); services.AddWorkflowRuntime();
    services.AddSingleton<IPersistenceAccessContextAccessor>(new ScopedAccess());
    if (race)
    {
        new EventsFeature().ConfigureServices(services);
        new ActivitiesBpmnHostingFeature().ConfigureServices(services);
    }
    services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
    {
        Provider = "Sqlite", ConnectionString = new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString(),
        Pooling = false, CacheWorkflowExecutables = false, RecoveryContinuationSigningKey = new string('r', 32), HierarchyCursorSigningKey = new string('r', 32)
    });
    services.AddPersistenceModes();
    if (race)
    {
        if (args.Length == 6)
        {
            var gate = Path.GetFullPath(args[5]);
            if (!gate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("The observation gate must stay within the controller-owned root.");
            services.AddSingleton(new GateOptions(gate, args[0]));
            services.AddScoped<IRuntimeCheckpointCommitEnricher, AdmissionGate>();
        }
    }
    services.Replace(ServiceDescriptor.Singleton<IWorkflowSchedulerDrainPolicy>(new CaptureDrainPolicy(checkpointCut || race)));
    // The orchestration loop follows delivered continuations even when each scheduler pass has a one-item budget.
    // The existing two-cycle budget retains admission and the next queued root step, before activity invocation.
    if (args[0] == "--drain-checkpoint")
        services.Replace(ServiceDescriptor.Singleton(new WorkflowDrainOrchestratorOptions(maxTotalDrainCycles: 2)));
    await using var provider = services.BuildServiceProvider();
    if (race)
        foreach (var initializer in provider.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
    await using var scope = provider.CreateAsyncScope();
    var sdk = scope.ServiceProvider;
    if (race)
        foreach (var task in sdk.GetServices<IStartupTask>()) await task.ExecuteAsync(CancellationToken.None);
    var models = sdk.GetRequiredService<IRuntimeModelExecutionReader>();
    var model = await models.FindAsync(args[2]) ?? throw new InvalidOperationException("The actual model execution is absent.");
    if (model.Lifecycle != RuntimeModelLifecycle.Running || model.TerminalResult is not null || model.Protocol != new RuntimeExecutionProtocol("elsa.bpmn", 1))
        throw new InvalidOperationException("Only the original live BPMN invocation may enqueue its worker.");
    var origin = model.Payload.GetProperty("work").EnumerateArray().Single().GetProperty("nativeWork").Deserialize<RuntimeModelNativeWorkOrigin>(json)!;
    var source = await sdk.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(origin.SourceOutboxItemId)
        ?? throw new InvalidOperationException("The actual native start source is absent.");
    var workflows = sdk.GetRequiredService<IWorkflowExecutionStateStore>();
    var queue = sdk.GetRequiredService<IWorkflowSchedulerWorkQueue>();
    if (!origin.MatchesModel(model) || source.StimulusDeliverySourceScope != model.Scope || source.Intent.Kind != "Elsa.Bpmn.StartNativeWork.v1" ||
        source.Intent.WorkflowExecutionId != model.ExecutionId || source.Intent.IntentId != origin.SourceIntentId ||
        source.Status is RuntimePostCommitOutboxStatus.Delivered or RuntimePostCommitOutboxStatus.FailedFinal ||
        source.Intent.Payload is not { } payload || !origin.IsEquivalentTo(payload.Deserialize<RuntimeModelNativeWorkOrigin>(json)) ||
        await workflows.FindAsync(origin.WorkerExecutionId) is not null ||
        (await queue.ListAsync(new(origin.WorkerExecutionId))).Items.Count != (race ? 1 : 0))
        throw new InvalidOperationException("The actual pinned native start is not available for this queued-admission cut.");
    object Product(Assembly assembly)
    {
        var path = assembly.Location;
        return new { name = assembly.GetName().Name, informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) };
    }
    if (race)
    {
        var started = DateTimeOffset.UtcNow;
        object outcome;
        if (args[0] == "--cancel-model")
            outcome = await sdk.GetRequiredService<IBpmnModelRuntime>().CancelAsync(model.ExecutionId, "cancel-before-native-admission",
                "Retire the original native start before admission.", new("owned-sdk-race", model.TenantId));
        else
        {
            var id = "owned-native-admission:" + Guid.NewGuid().ToString("N");
            var envelope = new WorkflowExecutionCommandEnvelope(id, origin.WorkerExecutionId,
                new(id, origin.WorkerExecutionId, WorkflowExecutionCommandKind.RunSchedulerWork, started, null, new Dictionary<string, string>()),
                id, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, started, partition: new(origin.Scope.Value));
            try
            {
                outcome = await sdk.GetRequiredService<IWorkflowDrainOrchestrator>().DrainAsync(envelope,
                    new(origin.WorkerExecutionId, maxWorkItems: 1));
            }
            catch (DrainCycleLimitExceededException exception) when (exception.WorkflowExecutionId == origin.WorkerExecutionId &&
                exception.Limit == DrainCycleLimit.TotalCycles && exception.MaxCycles == 2 && exception.Cycles == 2)
            {
                outcome = new { status = "TotalCycleBudgetReached", exception.WorkflowExecutionId, exception.MaxCycles, exception.Cycles };
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { operation = args[0], startedAtUtc = started, completedAtUtc = DateTimeOffset.UtcNow,
            origin, outcome, model = await models.FindAsync(model.ExecutionId), worker = await workflows.FindAsync(origin.WorkerExecutionId),
            queuedNativeWork = (await queue.ListAsync(new(origin.WorkerExecutionId))).Items,
            products = new[] { Product(Assembly.GetExecutingAssembly()), Product(typeof(IWorkflowStartDispatcher).Assembly),
                Product(typeof(Elsa.Workflows.Runtime.Services.Executions.WorkflowStartDispatcher).Assembly), Product(typeof(RuntimeEntityFrameworkCoreOptions).Assembly),
                Product(typeof(ActivitiesBpmnHostingFeature).Assembly) } }, json));
        return 0;
    }
    var dispatched = await sdk.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(new(origin.WorkerExecutable.ArtifactId,
        model.Authority.SystemIdentity, origin.WorkerExecutionId, null, null, null, null, null, null, WorkflowRunKind.PublishedRun,
        null, WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy, null, null, model.TenantId,
        new(origin.Scope.Value), model.Authority, WorkflowExecutableStartAuthority.FromModelWork(origin)));
    var queued = await queue.ListAsync(new(origin.WorkerExecutionId));
    var after = await models.FindAsync(model.ExecutionId);
    if (dispatched.CommandDispatch.Status != WorkflowExecutionCommandDispatchStatus.Accepted || queued.Items.Count != 1 ||
        queued.Items.Single().CommandKind != (checkpointCut ? WorkflowExecutionCommandKind.Checkpoint : WorkflowExecutionCommandKind.Start) ||
        await workflows.FindAsync(origin.WorkerExecutionId) is not null ||
        !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(model, json), JsonSerializer.SerializeToElement(after, json)))
        throw new InvalidOperationException("The SDK did not retain the exact unadmitted start/checkpoint and unchanged model.");
    Console.WriteLine(JsonSerializer.Serialize(new { operation = "enqueue-model-owned-native-start", readOnly = false, model.ExecutionId, model.Revision,
        origin, sourceStatusBefore = source.Status, commandDispatch = dispatched.CommandDispatch, queuedWork = queued.Items,
        expectedRetirementCommitId = "commit:" + (checkpointCut ? queued.Items.Single().WorkItemId :
            RuntimeChainId.Derive(queued.Items.Single().WorkItemId, $"checkpoint:{RuntimeCheckpointNames.WorkflowStarted}")),
        modelUnchanged = true, nativeWorkerAdmitted = false, drainPolicy = checkpointCut ? "OneWorkItem" : "EnqueueOnly",
        products = new[] { Product(Assembly.GetExecutingAssembly()), Product(typeof(IWorkflowStartDispatcher).Assembly),
            Product(typeof(Elsa.Workflows.Runtime.Services.Executions.WorkflowStartDispatcher).Assembly), Product(typeof(RuntimeEntityFrameworkCoreOptions).Assembly) } }, json));
    return 0;
}
catch (Exception exception)
{
    Console.WriteLine(JsonSerializer.Serialize(new { operation = "enqueue-model-owned-native-start", accepted = false, failureType = exception.GetType().Name, message = exception.Message }, json));
    return 2;
}

internal sealed class ScopedAccess : IPersistenceAccessContextAccessor
{ public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(PersistenceScope.DefaultValue)); }

internal sealed class CaptureDrainPolicy(bool checkpointCut) : IWorkflowSchedulerDrainPolicy
{
    public RuntimeSchedulerDrainRequest? CreateDrainRequest(WorkflowExecutionCommandEnvelope envelope, RuntimeSchedulerWorkItem workItem) =>
        checkpointCut ? new(workItem.WorkflowExecutionId, maxWorkItems: 1) : null;
}

internal sealed record GateOptions(string Directory, string Operation);

/// <summary>Coordinates actual writers at the existing hook; it returns every proposal unchanged.</summary>
internal sealed class AdmissionGate(GateOptions options, IRuntimeModelExecutionReader models) : IRuntimeCheckpointCommitEnricher
{
    private bool observed;
    public int Order => int.MaxValue;
    public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default) => ValueTask.FromResult(commit);
    public async ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        if (observed || (options.Operation == "--drain-checkpoint" ? commit.ExpectedModelExecution is null :
            commit.ModelState is not { ExpectedRevision: 1, State.TerminalResult: not null })) return;
        observed = true;
        var execution = commit.ExpectedModelExecution?.ExecutionId ?? commit.ModelState!.State.ExecutionId;
        var before = RuntimeCheckpointCommitFingerprint.Compute(commit);
        Directory.CreateDirectory(options.Directory);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var ready = Path.Combine(options.Directory, "ready.json");
        var temporary = ready + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new { processId = Environment.ProcessId, observedAtUtc = DateTimeOffset.UtcNow,
            operation = options.Operation, observedModel = await models.FindAsync(execution, cancellationToken), proposal = commit, proposalFingerprint = before }, json), cancellationToken);
        File.Move(temporary, ready);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (!File.Exists(Path.Combine(options.Directory, "release")))
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("The owned controller did not release its observed admission/cancellation commit.");
            await Task.Delay(25, cancellationToken);
        }
        var after = RuntimeCheckpointCommitFingerprint.Compute(commit);
        if (before != after) throw new InvalidOperationException("The observation hook changed its proposed commit.");
        await File.WriteAllTextAsync(Path.Combine(options.Directory, "released.json"), JsonSerializer.Serialize(new {
            processId = Environment.ProcessId, releasedAtUtc = DateTimeOffset.UtcNow, proposalUnchanged = true, proposalFingerprint = after }, json), cancellationToken);
    }
}
