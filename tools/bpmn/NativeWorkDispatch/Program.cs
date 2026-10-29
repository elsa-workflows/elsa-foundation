using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// An owned SDK client, not a state seeder: ordinary Start dispatch writes the real protected queue item.
// The existing drain-policy seam leaves its processing to the standard Workbench recovery host.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
try
{
    if (args.Length != 4 || args[0] is not ("--enqueue-start" or "--enqueue-checkpoint"))
        throw new ArgumentException("Use --enqueue-start or --enqueue-checkpoint <owned-content-root> <model-id> <controller-nonce>.");
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
    services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
    {
        Provider = "Sqlite", ConnectionString = new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString(),
        Pooling = false, CacheWorkflowExecutables = false, RecoveryContinuationSigningKey = new string('r', 32), HierarchyCursorSigningKey = new string('r', 32)
    });
    services.Replace(ServiceDescriptor.Singleton<IWorkflowSchedulerDrainPolicy>(new CaptureDrainPolicy(checkpointCut)));
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var sdk = scope.ServiceProvider;
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
        await workflows.FindAsync(origin.WorkerExecutionId) is not null || (await queue.ListAsync(new(origin.WorkerExecutionId))).Items.Count != 0)
        throw new InvalidOperationException("The actual pinned native start is not available for this queued-admission cut.");
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
    object Product(Assembly assembly)
    {
        var path = assembly.Location;
        return new { name = assembly.GetName().Name, informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) };
    }
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
