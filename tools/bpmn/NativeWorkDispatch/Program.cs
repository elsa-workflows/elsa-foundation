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
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Runtime;
using Elsa.Activities.Primitives;
using Elsa.Activities.Sequence;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Compensation;
using Elsa.Serialization.SystemText;
using Elsa.Events;
using Elsa.Tasks.Core;
using Elsa.Etl.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Workflows.Runtime;

// An owned SDK client, not a state seeder: ordinary Start dispatch writes the real protected queue item.
// The existing drain-policy seam leaves its processing to the standard Workbench recovery host.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
try
{
    if (args.Length is not (4 or 6) || args[0] is not ("--enqueue-start" or "--enqueue-checkpoint" or "--drain-checkpoint" or "--cancel-model" or "--consume-native-result" or "--consume-native-success") ||
        args.Length == 6 && args[4] != (args[0] == "--consume-native-success" ? "--parent-return-stage" : "--gate"))
        throw new ArgumentException("Select an owned enqueue/drain/cancel operation, content root, model id, nonce and optional --gate directory.");
    var race = args[0] is "--drain-checkpoint" or "--cancel-model";
    var success = args[0] == "--consume-native-success";
    var consume = success || args[0] == "--consume-native-result";
    if (consume && !success && args.Length != 4) throw new ArgumentException("Consumption uses the original production delivery path without a validation gate.");
    var parentReturnStage = success && args.Length == 6 ? args[5] : null;
    if (success && parentReturnStage is not ("pending" or "consumed" or "settled"))
        throw new ArgumentException("Successful native-parent capture requires --parent-return-stage pending|consumed|settled.");
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
    if (race || consume)
    {
        new EventsFeature().ConfigureServices(services);
        new ActivitiesBpmnHostingFeature().ConfigureServices(services);
    }
    if (consume)
    {
        using var configuration = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "shells.json")));
        var feature = configuration.RootElement.GetProperty("CShells").GetProperty("Shells").GetProperty("default").GetProperty("Features");
        var etl = feature.GetProperty("EtlRuntime").Deserialize<EtlRuntimeFeature>(json)!;
        if (!Path.GetFullPath(etl.SnapshotRoot).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            etl.FileBindings.Any(binding => !Path.GetFullPath(binding.Path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new InvalidOperationException("ETL file bindings must stay in this controller's owned root.");
        etl.ConfigureServices(services);
        var etlDatabase = Path.Combine(root, "etl-runtime.db");
        if (!File.Exists(etlDatabase)) throw new FileNotFoundException("The actual owned ETL runtime store is absent.");
        new EtlRuntimeEntityFrameworkCoreFeature { ConnectionString = new SqliteConnectionStringBuilder
            { DataSource = etlDatabase, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString() }.ConfigureServices(services);
        new EtlWorkflowsRuntimeFeature().ConfigureServices(services);
    }
    if (success)
    {
        // These are the existing Workbench consumers for this capture's real CLR native parents.
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new ActivitiesPrimitivesFeature().ConfigureServices(services);
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new ActivitiesFlowchartFeature().ConfigureServices(services);
        new ActivitiesCompensationFeature().ConfigureServices(services);
        new SerializationFeature().ConfigureServices(services);
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
    services.Replace(ServiceDescriptor.Singleton<IWorkflowSchedulerDrainPolicy>(new CaptureDrainPolicy(checkpointCut || race || consume)));
    // The orchestration loop follows delivered continuations even when each scheduler pass has a one-item budget.
    // The existing two-cycle budget retains admission and the next queued root step, before activity invocation.
    if (args[0] == "--drain-checkpoint")
        services.Replace(ServiceDescriptor.Singleton(new WorkflowDrainOrchestratorOptions(maxTotalDrainCycles: 2)));
    await using var provider = services.BuildServiceProvider();
    if (race || consume)
        foreach (var initializer in provider.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
    await using var scope = provider.CreateAsyncScope();
    var sdk = scope.ServiceProvider;
    if (race || consume)
        foreach (var task in sdk.GetServices<IStartupTask>()) await task.ExecuteAsync(CancellationToken.None);
    var models = sdk.GetRequiredService<IRuntimeModelExecutionReader>();
    var model = await models.FindAsync(args[2]) ?? throw new InvalidOperationException("The actual model execution is absent.");
    if ((consume ? model.Lifecycle is not (RuntimeModelLifecycle.Running or RuntimeModelLifecycle.TerminalPendingSettlement) :
            model.Lifecycle != RuntimeModelLifecycle.Running || model.TerminalResult is not null) || model.Protocol != new RuntimeExecutionProtocol("elsa.bpmn", 1))
        throw new InvalidOperationException("Only the original live BPMN invocation may enqueue its worker.");
    var originElement = consume && model.Lifecycle == RuntimeModelLifecycle.TerminalPendingSettlement
        ? model.Payload.GetProperty("cancellations").EnumerateArray().Single().GetProperty("origin")
        : model.Payload.GetProperty("work").EnumerateArray().Single().GetProperty("nativeWork");
    var origin = originElement.Deserialize<RuntimeModelNativeWorkOrigin>(json)!;
    var source = await sdk.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(origin.SourceOutboxItemId)
        ?? throw new InvalidOperationException("The actual native start source is absent.");
    var workflows = sdk.GetRequiredService<IWorkflowExecutionStateStore>();
    var queue = sdk.GetRequiredService<IWorkflowSchedulerWorkQueue>();
    if (!origin.MatchesModel(model) || source.StimulusDeliverySourceScope != model.Scope || source.Intent.Kind != "Elsa.Bpmn.StartNativeWork.v1" ||
        source.Intent.WorkflowExecutionId != model.ExecutionId || source.Intent.IntentId != origin.SourceIntentId ||
        (consume ? source.Status != RuntimePostCommitOutboxStatus.Delivered : source.Status is RuntimePostCommitOutboxStatus.Delivered or RuntimePostCommitOutboxStatus.FailedFinal) ||
        source.Intent.Payload is not { } payload || !origin.IsEquivalentTo(payload.Deserialize<RuntimeModelNativeWorkOrigin>(json)) ||
        !consume && (await workflows.FindAsync(origin.WorkerExecutionId) is not null ||
            (await queue.ListAsync(new(origin.WorkerExecutionId))).Items.Count != (race ? 1 : 0)))
        throw new InvalidOperationException("The actual pinned native start is not available for this queued-admission cut.");
    object Product(Assembly assembly)
    {
        var path = assembly.Location;
        return new { name = assembly.GetName().Name, informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) };
    }
    if (consume)
    {
        var started = DateTimeOffset.UtcNow;
        var steps = new List<object>();
        var processor = sdk.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        var lookup = sdk.GetRequiredService<IPostCommitOutboxLookupStore>();
        async Task<object> Drain(string execution)
        {
            var id = "owned-consumption:" + Guid.NewGuid().ToString("N");
            var envelope = new WorkflowExecutionCommandEnvelope(id, execution,
                new(id, execution, WorkflowExecutionCommandKind.RunSchedulerWork, started, null, new Dictionary<string, string>()),
                id, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, started, partition: new(origin.Scope.Value));
            var actor = await sdk.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(execution,
                WorkflowExecutionActorActivationReason.Recovery, started, "owned-consumption", WorkflowExecutionActorCapabilities.InProcessMailbox,
                partition: new(origin.Scope.Value)));
            return await actor.EnqueueAsync(envelope);
        }
        string? embeddedParentId = null;
        string? embeddedParentInvocationId = null;
        if (model.Payload.TryGetProperty("nativeParent", out var embeddedParent) && embeddedParent.ValueKind != JsonValueKind.Null)
        {
            embeddedParentId = owner.GetProperty("nativeParentExecutionId").GetString()!;
            var actualParent = await workflows.FindAsync(embeddedParentId);
            var text = embeddedParent.GetProperty("invocation").GetProperty("nativeExecutionId").GetString()!;
            var bytes = new byte[embeddedParentId.Length * 2];
            for (var index = 0; index < embeddedParentId.Length; index++)
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2, 2), embeddedParentId[index]);
            if (text != "u16:" + Convert.ToBase64String(bytes) || actualParent?.Status != (success ? WorkflowExecutionStatus.Running : WorkflowExecutionStatus.Cancelled) ||
                model.Lifecycle != RuntimeModelLifecycle.Running)
                throw new InvalidOperationException("This embedded cut requires the original native parent in the selected actual state and its still-running model.");
            if (success)
            {
                embeddedParentInvocationId = owner.GetProperty("nativeParentInvocationId").GetString()!;
                var activity = await sdk.GetRequiredService<IActivityExecutionStateStore>().FindAsync(embeddedParentId, embeddedParentInvocationId);
                var saved = activity?.PrivateState?.Value.InlineValue?.Deserialize<RunBpmnState>(json);
                if (activity?.Status != ActivityExecutionStatus.Suspended || activity.Execution.ActivityType != "Elsa.Activities.Bpmn.Activities.RunBpmn" ||
                    saved is not { FormatVersion: 1 } || !JsonElement.DeepEquals(saved.Invocation, embeddedParent.GetProperty("invocation")))
                    throw new InvalidOperationException("The live controller has not identified this model's original actual RunBpmn wait.");
            }
            if (!success)
            {
                var delivery = await processor.ProcessAsync(new(1, embeddedParentId, "Elsa.Bpmn.CancelEmbedded.v1"));
                steps.Add(new { operation = "deliver-embedded-cancel", delivery });
                if (delivery.AttemptedCount != 1 || delivery.DeliveredCount != 0)
                    throw new InvalidOperationException("The original native parent's cancellation was not queued without acknowledgment.");
                var drain = await Drain(model.ExecutionId); steps.Add(new { operation = "consume-embedded-cancel", drain });
                model = await models.FindAsync(model.ExecutionId) ?? throw new InvalidOperationException("The embedded model disappeared.");
                if (model.Lifecycle != RuntimeModelLifecycle.TerminalPendingSettlement || model.Revision != 2 ||
                    model.TerminalResult?.GetProperty("status").GetString() != "Cancelled")
                    throw new InvalidOperationException("Actual embedded cancellation did not retain its pending worker responsibility.");
            }
        }
        if (success && embeddedParentId is null) throw new InvalidOperationException("Successful parent-return capture requires its actual RunBpmn parent.");
        var worker = await workflows.FindAsync(origin.WorkerExecutionId) ?? throw new InvalidOperationException("The real native worker is absent.");
        if (!origin.IsEquivalentTo(worker.ModelWorkOrigin) || (await queue.ListAsync(new(origin.WorkerExecutionId))).Items.Count != 0 ||
            (await queue.ListAsync(new(model.ExecutionId))).Items.Count != 0 || await models.FindInputAsync(model.ExecutionId, origin.ResultInputId) is not null)
            throw new InvalidOperationException("This consumption cut requires the original worker with no queued/consumed native result.");
        if (worker.Status == WorkflowExecutionStatus.Running && model.Lifecycle == RuntimeModelLifecycle.TerminalPendingSettlement)
        {
            var delivery = await processor.ProcessAsync(new(1, model.ExecutionId, "Elsa.Bpmn.CancelNativeWork.v1"));
            steps.Add(new { operation = "deliver-model-cancel", delivery });
            if (delivery.AttemptedCount != 1 || delivery.DeliveredCount != 0 ||
                (await queue.ListAsync(new(origin.WorkerExecutionId))).Items.Single().CommandKind != WorkflowExecutionCommandKind.Cancel)
                throw new InvalidOperationException("The original model cancellation did not retain its real native Cancel command.");
            var drain = await Drain(origin.WorkerExecutionId); steps.Add(new { operation = "drain-native-cancel", drain });
            worker = await workflows.FindAsync(origin.WorkerExecutionId) ?? throw new InvalidOperationException("The native worker disappeared.");
        }
        if (worker.Status != (success ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Cancelled))
            throw new InvalidOperationException("Consumption must retain the selected actual native terminal result.");
        if (!success)
        {
            var etl = await processor.ProcessAsync(new(1, origin.WorkerExecutionId, "Elsa.Etl.Cancel.v1"));
            steps.Add(new { operation = "deliver-etl-cancel", delivery = etl });
            if (etl.AttemptedCount != 1 || etl.DeliveredCount != 1)
                throw new InvalidOperationException("The original ETL cancellation remains owed.");
        }
        var result = await processor.ProcessAsync(new(1, origin.WorkerExecutionId, "Elsa.Bpmn.NativeWorkResult.v1"));
        steps.Add(new { operation = "queue-native-result", delivery = result });
        var queuedModel = (await queue.ListAsync(new(model.ExecutionId))).Items;
        if (result.AttemptedCount != 1 || result.DeliveredCount != 0 || queuedModel.Count != 1 || queuedModel.Single().CommandKind != WorkflowExecutionCommandKind.ExecuteModel)
            throw new InvalidOperationException("The original native result was not queued without acknowledgment.");
        var consumed = await Drain(model.ExecutionId); steps.Add(new { operation = "consume-native-result", drain = consumed });
        var consumedModel = await models.FindAsync(model.ExecutionId) ?? throw new InvalidOperationException("The consumed model disappeared.");
        var receipt = await models.FindInputAsync(model.ExecutionId, origin.ResultInputId);
        var resultAddress = consumedModel.Payload.GetProperty("nativeResults").EnumerateArray().Single().GetProperty("sourceOutboxItemId").GetString()!;
        var resultSource = await lookup.FindDurableAsync(resultAddress);
        if (consumedModel.Lifecycle != (embeddedParentId is null ? RuntimeModelLifecycle.TerminalSettled : RuntimeModelLifecycle.TerminalPendingSettlement) || consumedModel.Revision != model.Revision + 1 || receipt is null ||
            receipt.Result.GetProperty("status").GetString() != "Accepted" || resultSource is null || resultSource.Status != RuntimePostCommitOutboxStatus.FailedRetryable ||
            (await queue.ListAsync(new(model.ExecutionId))).Items.Count != 0)
            throw new InvalidOperationException("The actual native result lacks durable model consumption with source acknowledgment still owed.");
        var nativeResultConsumedModel = consumedModel;
        RuntimePostCommitOutboxItem? parentResultSource = null;
        RuntimeModelInputReceipt? parentSettlementReceipt = null;
        RuntimeModelExecutionCommand? parentSettlementCommand = null;
        if (success)
        {
            if (consumedModel.Revision != 2 || consumedModel.TerminalResult?.GetProperty("status").GetString() != "Completed")
                throw new InvalidOperationException("The actual successful native result did not leave its parent return pending.");
            parentResultSource = (await sdk.GetRequiredService<IRuntimePostCommitOutboxStore>()
                .GetDeliverableAsync(new(DateTimeOffset.UtcNow, 2, model.ExecutionId, "Elsa.Bpmn.ResumeNativeParent.v1"))).Single();
            if (parentResultSource.Status != RuntimePostCommitOutboxStatus.Pending)
                throw new InvalidOperationException("The original native-parent result was not still pending.");
            if (parentReturnStage is "consumed" or "settled")
            {
                var delivery = await processor.ProcessAsync(new(1, model.ExecutionId, "Elsa.Bpmn.ResumeNativeParent.v1"));
                steps.Add(new { operation = "deliver-parent-result", delivery });
                if (delivery.AttemptedCount != 1 || delivery.DeliveredCount != 0)
                    throw new InvalidOperationException("The actual native parent result was acknowledged before its model settlement.");
                var activity = await sdk.GetRequiredService<IActivityExecutionStateStore>().FindAsync(embeddedParentId!, embeddedParentInvocationId!);
                if (activity?.Status != ActivityExecutionStatus.Completed)
                {
                    var drain = await Drain(embeddedParentId!); steps.Add(new { operation = "consume-parent-result", drain });
                }
                activity = await sdk.GetRequiredService<IActivityExecutionStateStore>().FindAsync(embeddedParentId!, embeddedParentInvocationId!);
                if (activity?.Status != ActivityExecutionStatus.Completed || activity.TriggerDeliveries?.Any(receipt =>
                        receipt.Status == ActivityTriggerDeliveryStatus.Consumed && receipt.ProviderId == "elsa.bpmn.native-parent.v1" &&
                        receipt.PayloadType.Alias == typeof(RunBpmnCompletion).FullName) != true)
                    throw new InvalidOperationException("The actual native parent did not retain typed consumption of its original RunBpmn invocation.");
                if (parentReturnStage == "settled")
                {
                    if ((await queue.ListAsync(new(model.ExecutionId))).Items.Count == 0)
                    {
                        parentResultSource = await lookup.FindDurableAsync(parentResultSource.OutboxItemId) ?? throw new InvalidOperationException("The parent result disappeared.");
                        var delay = (parentResultSource.AvailableAt ?? throw new InvalidOperationException("The pending parent source has no retained retry deadline.")) - DateTimeOffset.UtcNow;
                        if (delay > TimeSpan.FromSeconds(30)) throw new InvalidOperationException("The original parent source is not due within this owned capture's bound.");
                        if (delay > TimeSpan.Zero) await Task.Delay(delay);
                        var settle = await processor.ProcessAsync(new(1, model.ExecutionId, "Elsa.Bpmn.ResumeNativeParent.v1"));
                        steps.Add(new { operation = "queue-parent-settlement", delivery = settle });
                        if (settle.AttemptedCount != 1 || settle.DeliveredCount != 0)
                            throw new InvalidOperationException("The parent source did not queue its exact model settlement without acknowledgment.");
                    }
                    var work = (await queue.ListAsync(new(model.ExecutionId))).Items.Single();
                    parentSettlementCommand = work.Payload?.Deserialize<RuntimeModelExecutionCommand>(json)
                        ?? throw new InvalidOperationException("The real model settlement command is absent.");
                    if (work.CommandKind != WorkflowExecutionCommandKind.ExecuteModel || parentSettlementCommand.InputKind != "native-parent-settled")
                        throw new InvalidOperationException("The real queued input is not native-parent settlement.");
                    var drain = await Drain(model.ExecutionId); steps.Add(new { operation = "consume-parent-settlement", drain });
                }
            }
            consumedModel = await models.FindAsync(model.ExecutionId) ?? throw new InvalidOperationException("The final model disappeared.");
            parentResultSource = await lookup.FindDurableAsync(parentResultSource.OutboxItemId) ?? throw new InvalidOperationException("The original parent result disappeared.");
            parentSettlementReceipt = parentSettlementCommand is null ? null : await models.FindInputAsync(model.ExecutionId, parentSettlementCommand.InputId);
            if (consumedModel.Revision != (parentReturnStage == "settled" ? 3 : 2) ||
                consumedModel.Lifecycle != (parentReturnStage == "settled" ? RuntimeModelLifecycle.TerminalSettled : RuntimeModelLifecycle.TerminalPendingSettlement) ||
                (parentSettlementReceipt is not null) != (parentReturnStage == "settled") || parentResultSource.DeliveredAt is not null ||
                parentResultSource.Status != (parentReturnStage == "pending" ? RuntimePostCommitOutboxStatus.Pending : RuntimePostCommitOutboxStatus.FailedRetryable))
                throw new InvalidOperationException("The actual native-parent consumption/settlement/ACK boundary differs from the selected stage.");
        }
        var products = new List<object> { Product(Assembly.GetExecutingAssembly()), Product(typeof(IWorkflowStartDispatcher).Assembly),
            Product(typeof(Elsa.Workflows.Runtime.Services.Executions.WorkflowStartDispatcher).Assembly), Product(typeof(RuntimeEntityFrameworkCoreOptions).Assembly),
            Product(typeof(ActivitiesBpmnHostingFeature).Assembly), Product(typeof(EtlRuntimeFeature).Assembly), Product(typeof(EtlRuntimeEntityFrameworkCoreFeature).Assembly),
            Product(typeof(EtlWorkflowsRuntimeFeature).Assembly) };
        if (success) products.AddRange([Product(typeof(ActivitiesRuntimeFeature).Assembly), Product(typeof(ActivitiesPrimitivesFeature).Assembly),
            Product(typeof(ActivitiesSequenceFeature).Assembly), Product(typeof(ActivitiesFlowchartFeature).Assembly), Product(typeof(ActivitiesCompensationFeature).Assembly),
            Product(typeof(SerializationFeature).Assembly)]);
        Console.WriteLine(JsonSerializer.Serialize(new { operation = args[0], startedAtUtc = started, completedAtUtc = DateTimeOffset.UtcNow,
            origin, steps, embeddedParentExecutionId = embeddedParentId, model = consumedModel, worker = await workflows.FindAsync(origin.WorkerExecutionId), receipt, resultSource,
            nativeResultConsumedModel, parentReturnStage, parentResultSource, parentSettlementReceipt, parentSettlementCommand,
            parentActivity = success ? await sdk.GetRequiredService<IActivityExecutionStateStore>().FindAsync(embeddedParentId!, embeddedParentInvocationId!) : null,
            products }, json));
        return 0;
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
