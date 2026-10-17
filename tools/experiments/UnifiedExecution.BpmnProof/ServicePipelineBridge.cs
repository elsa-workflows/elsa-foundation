using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bpmn.Model;
using Bpmn.Semantics;
using Elsa.UnifiedExecution.WorkProof;
using Microsoft.Data.Sqlite;

namespace UnifiedExecution.BpmnProof;

/// <summary>Experimental adapter from a BPMN service-task handle to one recoverable WorkProof ETL execution.</summary>
internal static class ServicePipelineBridge
{
    private const string ScopeKey = "bpmn-service-pipeline-proof-key-2026";
    private const string PipelineOwner = "bpmn-service-pipeline-owner";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions PrettyJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal sealed record StageOutcome(int ProcessId, string Stage, string Scenario, string? ServiceHandle = null,
        string? ListenerHandle = null, string? PipelineStatus = null, string? BpmnStatus = null,
        int? WorkerProcesses = null, string? WorkerReceipt = null, string? FaultDisposition = null,
        int? Incidents = null, int? DestinationEffects = null, string? BusinessErrorMapping = null,
        string? CancellationMapping = null, int? PartitionConfigs = null, int? SourceRecords = null,
        int? WorkRows = null, string? Provisioning = null, string? DeliveryId = null,
        string? DeliveryStatus = null, bool? DeliveryApplied = null, int? BpmnCallbackCount = null,
        int? PendingBpmnCommands = null);

    internal sealed record WorkerOutcome(int ProcessId, string Receipt, int ReadRecords, int DestinationEffects);

    public static async Task<object> VerifyAsync(string artifactsPath)
    {
        var cases = new List<object>();
        foreach (string scenario in new[] { "success", "business-error", "fault", "cancel" })
        {
            string caseDirectory = Path.Combine(artifactsPath, $"etl-service-{scenario}");
            Directory.CreateDirectory(caseDirectory);
            string bpmnDb = Path.Combine(caseDirectory, "bpmn.db");
            string pipelineDb = Path.Combine(caseDirectory, "pipeline.db");
            string sinkDb = Path.Combine(caseDirectory, "sink.db");
            foreach (string path in new[] { bpmnDb, pipelineDb, sinkDb }) DeleteDatabase(path);

            StageOutcome started = await RunStageAsync("start", scenario, bpmnDb, pipelineDb, sinkDb);
            StageOutcome provisionCrash = await RunExpectedStageAsync("provision-crash", scenario, bpmnDb, pipelineDb, sinkDb, 73);
            StageOutcome provisioned = await RunStageAsync("ensure-pipeline", scenario, bpmnDb, pipelineDb, sinkDb);
            StageOutcome provisionReplay = await RunStageAsync("ensure-pipeline", scenario, bpmnDb, pipelineDb, sinkDb);
            StageOutcome finished;
            StageOutcome? pipelineCompleted = null;
            StageOutcome? deliveryPrepared = null;
            StageOutcome? deliveryInterrupted = null;
            StageOutcome? deliveryReplayed = null;
            int[] hostProcesses;
            if (scenario == "success")
            {
                pipelineCompleted = await RunExpectedStageAsync("complete-pipeline-crash", scenario, bpmnDb, pipelineDb, sinkDb, 75);
                deliveryPrepared = await RunStageAsync("prepare-delivery", scenario, bpmnDb, pipelineDb, sinkDb);
                deliveryInterrupted = await RunExpectedStageAsync("deliver-result-crash", scenario, bpmnDb, pipelineDb, sinkDb, 76);
                deliveryReplayed = await RunStageAsync("replay-result", scenario, bpmnDb, pipelineDb, sinkDb);
                finished = deliveryReplayed with { WorkerProcesses = pipelineCompleted.WorkerProcesses };
                hostProcesses = [started.ProcessId, provisionCrash.ProcessId, provisioned.ProcessId, provisionReplay.ProcessId,
                    pipelineCompleted.ProcessId, deliveryPrepared.ProcessId, deliveryInterrupted.ProcessId, deliveryReplayed.ProcessId];
            }
            else
            {
                finished = await RunStageAsync(scenario switch
                    {
                        "cancel" => "cancel",
                        "fault" => "fault",
                        _ => "resume"
                    },
                    scenario, bpmnDb, pipelineDb, sinkDb);
                hostProcesses = [started.ProcessId, provisionCrash.ProcessId, provisioned.ProcessId, provisionReplay.ProcessId, finished.ProcessId];
            }
            using var bpmnStore = new ProofStore(bpmnDb);
            var workStore = new DurableWorkStore(pipelineDb, Authorizer());
            var sink = new IndependentSink(sinkDb, Authorizer());
            WorkScope scope = Scope(scenario);
            string executionId = PipelineId(scenario, started.ServiceHandle
                ?? throw new InvalidDataException("BPMN service task did not persist its work handle."));
            var execution = workStore.ReadExecution(scope, executionId);
            var work = workStore.ReadWork(scope, executionId);
            ScopeRecord bpmn = bpmnStore.GetScope(BpmnScope(scenario));
            int effects = sink.CountEffects(scope, executionId);
            Require(provisionCrash.ServiceHandle == started.ServiceHandle
                && provisioned.ServiceHandle == started.ServiceHandle && provisionReplay.ServiceHandle == started.ServiceHandle,
                "Every provisioning retry must recover the exact persisted BPMN service handle used for stable pipeline identity.");

            switch (scenario)
            {
                case "success":
                    Require(execution.Status == "Completed" && bpmn.ContinuationKind == "complete" && effects == 4,
                        "Successful service task must resume the persisted ETL pipeline and complete BPMN only after its empty terminal batches.");
                    Require(provisionCrash.PartitionConfigs == 1 && provisionCrash.SourceRecords == 2
                        && provisioned.PartitionConfigs == 2 && provisioned.SourceRecords == 4 && provisioned.WorkRows == 2,
                        "Provision retry must reuse the first committed partition, create the missing partition, and create exactly one two-work initial plan.");
                    Require(provisionReplay.PartitionConfigs == 2 && provisionReplay.SourceRecords == 4 && provisionReplay.WorkRows == 2,
                        "Retrying full pipeline provisioning after its initial commit must not duplicate source rows or initial work.");
                    Require(pipelineCompleted!.PipelineStatus == "Completed" && pipelineCompleted.BpmnStatus == "defer"
                        && deliveryPrepared!.DeliveryStatus == "pending" && deliveryPrepared.BpmnStatus == "defer"
                        && deliveryInterrupted!.DeliveryStatus == "delivered" && deliveryInterrupted.DeliveryApplied == true
                        && deliveryReplayed!.DeliveryStatus == "delivered" && deliveryReplayed.DeliveryApplied == false
                        && deliveryReplayed.BpmnCallbackCount == 1 && deliveryReplayed.PendingBpmnCommands == 0,
                        "A completed ETL result must survive callback-process loss and replay from the BPMN-side receipt without applying the service callback twice.");
                    break;
                case "business-error":
                    Require(execution.Status == "Completed" && bpmn.ContinuationKind == "complete"
                        && finished.BusinessErrorMapping == "caught-at-BusinessError" && bpmnStore.GetIncidents(BpmnScope(scenario)).Count == 0,
                        "Business validation must map a successful ETL execution to its declared BPMN error boundary.");
                    break;
                case "fault":
                    Require(execution.Status == "Running" && work.All(x => x.Status == "cancelled")
                        && bpmn.ContinuationKind == "fault" && bpmnStore.GetIncidents(BpmnScope(scenario)).Count == 1,
                        "A confirmed ETL worker fault must cancel remaining work and surface a propagated BPMN incident.");
                    break;
                case "cancel":
                    Require(execution.Status == "Running" && work.All(x => x.Status == "cancelled")
                        && bpmn.ContinuationKind == "complete" && finished.WorkerReceipt == "LateRejected"
                        && effects == 0 && workStore.ReadWork(scope, executionId).Count == 2,
                        "The interrupting BPMN message must cancel the pipeline's active and owed work before accepting a late batch result.");
                    break;
            }

            var result = new
            {
                scenario,
                freshHostProcesses = hostProcesses,
                serviceHandle = started.ServiceHandle,
                messageListenerHandle = started.ListenerHandle,
                provisioningRecovery = new
                {
                    crashAfterOnePartition = new { provisionCrash.ProcessId, provisionCrash.PartitionConfigs, provisionCrash.SourceRecords },
                    resumed = new { provisioned.ProcessId, provisioned.PartitionConfigs, provisioned.SourceRecords, provisioned.WorkRows, provisioned.Provisioning },
                    afterInitialCommitRetry = new { provisionReplay.ProcessId, provisionReplay.PartitionConfigs, provisionReplay.SourceRecords, provisionReplay.WorkRows, provisionReplay.Provisioning }
                },
                workProofExecution = new { execution.PolicyId, execution.Status, execution.Revision },
                work = work.Select(item => new { item.InvocationId, item.Status, item.Attempt }).ToArray(),
                bpmnContinuation = bpmn.ContinuationKind,
                faultDisposition = finished.FaultDisposition,
                businessErrorMapping = finished.BusinessErrorMapping,
                cancellationMapping = finished.CancellationMapping,
                workerProcesses = finished.WorkerProcesses,
                lateWorkerReceipt = finished.WorkerReceipt,
                destinationEffects = effects,
                resultDelivery = scenario == "success" ? new
                {
                    pipelineCompletionProcess = pipelineCompleted!.ProcessId,
                    preparedProcess = deliveryPrepared!.ProcessId,
                    callbackCommitProcess = deliveryInterrupted!.ProcessId,
                    replayProcess = deliveryReplayed!.ProcessId,
                    deliveryId = deliveryReplayed.DeliveryId,
                    status = deliveryReplayed.DeliveryStatus,
                    firstApplication = deliveryInterrupted.DeliveryApplied,
                    replayApplication = deliveryReplayed.DeliveryApplied,
                    callbackCount = deliveryReplayed.BpmnCallbackCount,
                    pendingCommandsAfterRestore = deliveryReplayed.PendingBpmnCommands
                } : null,
                incidentCount = bpmnStore.GetIncidents(BpmnScope(scenario)).Count,
                limitation = scenario is "fault" or "cancel"
                    ? "WorkProof has per-invocation cancellation but no terminal pipeline-cancel/fail transition; the BPMN bridge records the terminal mapping while its ETL composition remains Running with cancelled work."
                    : null
            };
            WriteJson(Path.Combine(caseDirectory, "evidence.json"), result);
            cases.Add(result);
        }

        var report = new
        {
            result = "passed",
            bridge = "experimental BPMN service-task adapter to the shared WorkProof PartitionPipelinePolicy",
            cases,
            guarantees = new[]
            {
                "A fresh process starts BPMN service-task work; after a simulated crash with only one source partition committed, a new process reuses that partition and starts one stable multi-partition WorkProof execution.",
                "Pipeline completion is recorded in its own store before a durable BPMN-side delivery receipt; callback application and receipt advancement are one BPMN-store transaction, so lost acknowledgements replay without duplicate callback.",
                "Batch work is claimed and executed by fresh worker processes; the WorkProof policy advances only from accepted durable batch receipts.",
                "Success, caught business error, propagated worker fault, and BPMN interruption each have explicit adapter behavior and inspectable state."
            },
            boundaries = new[]
            {
                "This is a local SQLite proof adapter, not current Elsa integration, a broker-backed service, or a distributed exactly-once transaction.",
                "The shared PartitionPipelinePolicy cannot terminalize an execution as failed or cancelled; the bridge cancels invocation work and reports that unresolved composition state explicitly.",
                "The BPMN interpreter exposes no arbitrary token-alteration command; the existing validated modeled-message operation remains an event callback, and move-token remains unsupported."
            }
        };
        WriteJson(Path.Combine(artifactsPath, "etl-service-pipeline-bridge.json"), report);
        return report;
    }

    public static async Task<int> RunStageAsync(string[] args)
    {
        string stage = args[1], scenario = args[2], bpmnDb = args[3], pipelineDb = args[4], sinkDb = args[5];
        return stage switch
        {
            "start" => Start(scenario, bpmnDb, pipelineDb, sinkDb),
            "provision-crash" => ProvisionAndCrash(scenario, bpmnDb, pipelineDb),
            "ensure-pipeline" => EnsurePipeline(scenario, bpmnDb, pipelineDb),
            "complete-pipeline-crash" => await CompletePipelineAndCrashAsync(scenario, bpmnDb, pipelineDb, sinkDb),
            "prepare-delivery" => PrepareDelivery(scenario, bpmnDb, pipelineDb),
            "deliver-result-crash" => DeliverResultAndCrash(scenario, bpmnDb, pipelineDb),
            "replay-result" => ReplayResult(scenario, bpmnDb, pipelineDb),
            "resume" => await ResumeAsync(scenario, bpmnDb, pipelineDb, sinkDb),
            "fault" => await FaultAsync(scenario, bpmnDb, pipelineDb, sinkDb),
            "cancel" => await CancelAsync(scenario, bpmnDb, pipelineDb, sinkDb),
            _ => throw new ArgumentException($"Unknown service bridge stage '{stage}'.")
        };
    }

    public static async Task<int> RunWorkerAsync(string[] args)
    {
        string workDb = args[1], sinkDb = args[2];
        WorkScope scope = DecodeScope(args[3]);
        string executionId = args[4], invocationId = args[5], barrierId = args[6], mode = args[7];
        var authorizer = Authorizer();
        var store = new DurableWorkStore(workDb, authorizer);
        var source = new PartitionSourceStore(workDb, authorizer);
        var sink = new IndependentSink(sinkDb, authorizer);
        try
        {
            WorkSnapshot metadata = store.ReadWork(scope, executionId).Single(x => x.InvocationId == invocationId);
            WorkLease lease = store.TryClaimWork(scope, executionId, invocationId, $"etl-worker-{Environment.ProcessId}",
                metadata.HandlerId, metadata.HandlerVersion)
                ?? throw new InvalidOperationException("ETL worker could not claim the persisted batch invocation.");
            PartitionBatchInput input = JsonSerializer.Deserialize<PartitionBatchInput>(lease.InputJson)
                ?? throw new InvalidDataException("ETL worker received an empty batch snapshot.");
            IReadOnlyList<PartitionRecord> rows = source.ReadBatch(scope, executionId, invocationId, lease.Attempt, input);

            if (barrierId.Length > 0)
                await WaitAtBarrierAsync(store, barrierId, lease.Owner, invocationId);

            if (mode == "fault" && rows.Any(row => row.Payload == "injected-system-fault"))
                throw new InvalidOperationException("Injected transform failure after durable claim and source read.");

            WorkSnapshot current = store.ReadWork(scope, executionId).Single(x => x.InvocationId == invocationId);
            if (current.Status == "cancelled")
            {
                source.EndBatch(scope, executionId, invocationId, lease.Attempt, "cancelled-before-destination");
                long unchanged = input.Cursor;
                WorkReceipt late = store.CompleteWork(lease,
                    JsonSerializer.Serialize(new PartitionBatchResult(input.PartitionId, input.Cursor, unchanged, 0, true)));
                Console.WriteLine(JsonSerializer.Serialize(new WorkerOutcome(Environment.ProcessId, late.Status, rows.Count, 0), JsonOptions));
                return 0;
            }

            foreach (PartitionRecord row in rows)
                sink.Write(scope, executionId, $"{input.PartitionId}/{row.Ordinal}", row.Payload);
            source.EndBatch(scope, executionId, invocationId, lease.Attempt, "effects-delivered");
            long nextCursor = rows.Count == 0 ? input.Cursor : rows[^1].Ordinal;
            string resultJson = JsonSerializer.Serialize(new PartitionBatchResult(input.PartitionId, input.Cursor,
                nextCursor, rows.Count, rows.Count == 0));
            WorkReceipt receipt = store.CompleteWork(lease, resultJson);
            Console.WriteLine(JsonSerializer.Serialize(new WorkerOutcome(Environment.ProcessId, receipt.Status, rows.Count,
                sink.CountEffects(scope, executionId)), JsonOptions));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ETL_WORKER_FAULT: {exception.GetType().Name}: {exception.Message}");
            return 41;
        }
    }

    private static int Start(string scenario, string bpmnDb, string pipelineDb, string sinkDb)
    {
        using var host = DurableBpmnHost.Create(bpmnDb, BpmnScope(scenario), Definition(scenario));
        host.Start();
        BpmnLiveWork service = host.LiveWork.Single(work => work.BindingRef == "etl-pipeline");
        string? listener = scenario == "cancel"
            ? host.LiveWork.Single(work => host.WorkItems.Single(item => item.Handle == work.Handle).WorkKind == "message-listener").Handle
            : null;
        var result = new StageOutcome(Environment.ProcessId, "start", scenario, service.Handle, listener,
            PipelineStatus: "not-created", BpmnStatus: host.ContinuationKind);
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return 0;
    }

    private static int ProvisionAndCrash(string scenario, string bpmnDb, string pipelineDb)
    {
        using var bpmn = DurableBpmnHost.Restore(bpmnDb, BpmnScope(scenario), Definition(scenario));
        WorkRecord service = bpmn.WorkItems.Single(item => item.BindingRef == "etl-pipeline");
        string executionId = PipelineId(scenario, service.Handle);
        var source = new PartitionSourceStore(pipelineDb, Authorizer());
        var partition = PartitionDefinitions(scenario)[0];
        bool created = EnsurePartition(source, pipelineDb, Scope(scenario), executionId, partition.Id, 2, 2, partition.Rows);
        Require(created, "First provisioning stage must commit its first source partition before the simulated crash.");
        var result = new StageOutcome(Environment.ProcessId, "provision-crash", scenario, service.Handle,
            PipelineStatus: "not-created", BpmnStatus: bpmn.ContinuationKind,
            PartitionConfigs: 1, SourceRecords: partition.Rows.Length,
            Provisioning: "partition-A-committed; simulated process crash before remaining partitions and pipeline initial plan");
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return SimulateCrash(73);
    }

    private static int EnsurePipeline(string scenario, string bpmnDb, string pipelineDb)
    {
        using var bpmn = DurableBpmnHost.Restore(bpmnDb, BpmnScope(scenario), Definition(scenario));
        WorkRecord service = bpmn.WorkItems.Single(item => item.BindingRef == "etl-pipeline");
        string executionId = PipelineId(scenario, service.Handle);
        var source = new PartitionSourceStore(pipelineDb, Authorizer());
        var created = new List<string>();
        var reused = new List<string>();
        foreach (var partition in PartitionDefinitions(scenario))
        {
            bool wasCreated = EnsurePartition(source, pipelineDb, Scope(scenario), executionId, partition.Id, 2, 2, partition.Rows);
            (wasCreated ? created : reused).Add(partition.Id);
        }

        var workStore = new DurableWorkStore(pipelineDb, Authorizer());
        var coordinator = new CompositionCoordinator(workStore, Scope(scenario), executionId, PipelineOwner,
            new PartitionPipelinePolicy());
        coordinator.Start(JsonSerializer.Serialize(new PartitionPipelineInput([
            new PartitionBatchInput("A", -1, 2), new PartitionBatchInput("B", -1, 2)
        ])));
        if (scenario == "cancel") workStore.CreateBarrier(BarrierId(scenario));
        (int configs, int records) = ReadPartitionCounts(pipelineDb, Scope(scenario), executionId);
        int workRows = workStore.ReadWork(Scope(scenario), executionId).Count;
        var result = new StageOutcome(Environment.ProcessId, "ensure-pipeline", scenario, service.Handle,
            PipelineStatus: workStore.ReadExecution(Scope(scenario), executionId).Status,
            BpmnStatus: bpmn.ContinuationKind, PartitionConfigs: configs, SourceRecords: records, WorkRows: workRows,
            Provisioning: $"created=[{string.Join(',', created)}]; reused=[{string.Join(',', reused)}]; executionId=service-handle-derived-stable-key");
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return 0;
    }

    private static async Task<int> CompletePipelineAndCrashAsync(string scenario, string bpmnDb, string pipelineDb, string sinkDb)
    {
        using var bpmn = DurableBpmnHost.Restore(bpmnDb, BpmnScope(scenario), Definition(scenario));
        WorkRecord service = bpmn.WorkItems.Single(item => item.BindingRef == "etl-pipeline");
        string executionId = PipelineId(scenario, service.Handle);
        var workStore = new DurableWorkStore(pipelineDb, Authorizer());
        const string owner = "bpmn-service-result-ready-owner";
        workStore.AcquireExecution(Scope(scenario), executionId, owner);
        var coordinator = new CompositionCoordinator(workStore, Scope(scenario), executionId, owner, new PartitionPipelinePolicy());
        coordinator.Activate();
        int workers = await DrainPipelineAsync(workStore, coordinator, Scope(scenario), executionId, pipelineDb, sinkDb, mode: "fault-on-sentinel");
        var execution = workStore.ReadExecution(Scope(scenario), executionId);
        Require(execution.Status == "Completed", "ETL execution must commit completion before the BPMN callback crash point.");
        var result = new StageOutcome(Environment.ProcessId, "complete-pipeline-crash", scenario, service.Handle,
            PipelineStatus: execution.Status, BpmnStatus: bpmn.ContinuationKind, WorkerProcesses: workers,
            DestinationEffects: new IndependentSink(sinkDb, Authorizer()).CountEffects(Scope(scenario), executionId),
            Provisioning: "pipeline-completed-in-independent-store; simulated process crash before BPMN result journal or callback");
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return SimulateCrash(75);
    }

    private static int PrepareDelivery(string scenario, string bpmnDb, string pipelineDb)
    {
        using var bpmn = DurableBpmnHost.Restore(bpmnDb, BpmnScope(scenario), Definition(scenario));
        WorkRecord service = bpmn.WorkItems.Single(item => item.BindingRef == "etl-pipeline");
        string executionId = PipelineId(scenario, service.Handle);
        var execution = new DurableWorkStore(pipelineDb, Authorizer()).ReadExecution(Scope(scenario), executionId);
        Require(execution.Status == "Completed", "Only a committed successful ETL execution can prepare a service result delivery.");
        string deliveryId = ResultDeliveryId(executionId);
        string resultJson = JsonSerializer.Serialize(new ServiceResultEnvelope(executionId, execution.PolicyId, execution.Status, execution.Revision));
        BridgeDeliveryRecord delivery = bpmn.EnsureServiceResultDelivery(service.Handle, deliveryId, resultJson);
        var outcome = new StageOutcome(Environment.ProcessId, "prepare-delivery", scenario, service.Handle,
            PipelineStatus: execution.Status, BpmnStatus: bpmn.ContinuationKind, DeliveryId: delivery.DeliveryId,
            DeliveryStatus: delivery.Status, DeliveryApplied: false);
        Console.WriteLine(JsonSerializer.Serialize(outcome, JsonOptions));
        return 0;
    }

    private static int DeliverResultAndCrash(string scenario, string bpmnDb, string pipelineDb)
    {
        using var bpmn = DurableBpmnHost.Restore(bpmnDb, BpmnScope(scenario), Definition(scenario));
        WorkRecord service = bpmn.WorkItems.Single(item => item.BindingRef == "etl-pipeline");
        string executionId = PipelineId(scenario, service.Handle);
        string deliveryId = ResultDeliveryId(executionId);
        bool applied = bpmn.CompleteServiceResult(deliveryId, dispatchOwedCommands: false);
        using var store = new ProofStore(bpmnDb);
        BridgeDeliveryRecord delivery = store.GetBridgeDelivery(BpmnScope(scenario), deliveryId);
        var outcome = new StageOutcome(Environment.ProcessId, "deliver-result-crash", scenario, service.Handle,
            PipelineStatus: new DurableWorkStore(pipelineDb, Authorizer()).ReadExecution(Scope(scenario), executionId).Status,
            BpmnStatus: bpmn.ContinuationKind, DeliveryId: deliveryId, DeliveryStatus: delivery.Status,
            DeliveryApplied: applied, PendingBpmnCommands: bpmn.PendingCommandCount);
        Require(applied && delivery.Status == "delivered", "The first BPMN-side delivery application must atomically store the receipt.");
        Console.WriteLine(JsonSerializer.Serialize(outcome, JsonOptions));
        return SimulateCrash(76);
    }

    private static int ReplayResult(string scenario, string bpmnDb, string pipelineDb)
    {
        using var bpmn = DurableBpmnHost.Restore(bpmnDb, BpmnScope(scenario), Definition(scenario));
        WorkRecord service = bpmn.WorkItems.Single(item => item.BindingRef == "etl-pipeline");
        string executionId = PipelineId(scenario, service.Handle);
        string deliveryId = ResultDeliveryId(executionId);
        bool applied = bpmn.CompleteServiceResult(deliveryId);
        using var store = new ProofStore(bpmnDb);
        BridgeDeliveryRecord delivery = store.GetBridgeDelivery(BpmnScope(scenario), deliveryId);
        int callbackCount = bpmn.Trace.Count(item => item.Kind == "service-result-delivered" && item.Handle == service.Handle);
        var outcome = new StageOutcome(Environment.ProcessId, "replay-result", scenario, service.Handle,
            PipelineStatus: new DurableWorkStore(pipelineDb, Authorizer()).ReadExecution(Scope(scenario), executionId).Status,
            BpmnStatus: bpmn.ContinuationKind, DeliveryId: deliveryId, DeliveryStatus: delivery.Status,
            DeliveryApplied: applied, BpmnCallbackCount: callbackCount, PendingBpmnCommands: bpmn.PendingCommandCount);
        Console.WriteLine(JsonSerializer.Serialize(outcome, JsonOptions));
        return 0;
    }

    private static async Task<int> ResumeAsync(string scenario, string bpmnDb, string pipelineDb, string sinkDb)
    {
        using var bpmn = DurableBpmnHost.Restore(bpmnDb, BpmnScope(scenario), Definition(scenario));
        WorkRecord service = bpmn.WorkItems.Single(item => item.BindingRef == "etl-pipeline");
        string executionId = PipelineId(scenario, service.Handle);
        var workStore = new DurableWorkStore(pipelineDb, Authorizer());
        const string owner = "bpmn-service-resumed-owner";
        workStore.AcquireExecution(Scope(scenario), executionId, owner);
        var coordinator = new CompositionCoordinator(workStore, Scope(scenario), executionId, owner, new PartitionPipelinePolicy());
        coordinator.Activate();
        int workerCount = await DrainPipelineAsync(workStore, coordinator, Scope(scenario), executionId, pipelineDb, sinkDb,
            mode: "fault-on-sentinel");
        string? businessMapping = null;
        if (scenario == "business-error")
        {
            BpmnErrorDisposition disposition = bpmn.FaultWork(service.Handle, "ETL business validation failed: business-error row encountered.");
            businessMapping = disposition is BpmnErrorDisposition.Caught caught
                ? $"caught-at-{caught.CatchingElementId}"
                : disposition.GetType().Name;
            Require(businessMapping == "caught-at-BusinessError", "Business error did not reach its BPMN error boundary.");
            bpmn.CompleteWork(bpmn.LiveWork.Single(work => work.BindingRef == "business-review").Handle);
        }
        else
        {
            bpmn.CompleteWork(service.Handle);
        }
        int effects = new IndependentSink(sinkDb, Authorizer()).CountEffects(Scope(scenario), executionId);
        var outcome = new StageOutcome(Environment.ProcessId, "resume", scenario, service.Handle,
            PipelineStatus: workStore.ReadExecution(Scope(scenario), executionId).Status,
            BpmnStatus: bpmn.ContinuationKind, WorkerProcesses: workerCount, DestinationEffects: effects,
            BusinessErrorMapping: businessMapping, Incidents: bpmn.Incidents.Count);
        Console.WriteLine(JsonSerializer.Serialize(outcome, JsonOptions));
        return 0;
    }

    private static async Task<int> FaultAsync(string scenario, string bpmnDb, string pipelineDb, string sinkDb)
    {
        using var bpmn = DurableBpmnHost.Restore(bpmnDb, BpmnScope(scenario), Definition(scenario));
        WorkRecord service = bpmn.WorkItems.Single(item => item.BindingRef == "etl-pipeline");
        string executionId = PipelineId(scenario, service.Handle);
        var workStore = new DurableWorkStore(pipelineDb, Authorizer());
        const string owner = "bpmn-service-fault-owner";
        workStore.AcquireExecution(Scope(scenario), executionId, owner);
        var coordinator = new CompositionCoordinator(workStore, Scope(scenario), executionId, owner, new PartitionPipelinePolicy());
        coordinator.Activate();
        WorkSnapshot failed = workStore.ReadWork(Scope(scenario), executionId).Single(item => item.InvocationId.Contains("partition:A:", StringComparison.Ordinal));
        StageProcessResult worker = await RunWorkerAsync(pipelineDb, sinkDb, Scope(scenario), executionId, failed.InvocationId,
            barrierId: "", mode: "fault");
        Require(worker.ExitCode == 41, "Injected source transform fault did not fail the ETL worker process.");
        WorkSnapshot claimed = workStore.ReadWork(Scope(scenario), executionId).Single(item => item.InvocationId == failed.InvocationId);
        var source = new PartitionSourceStore(pipelineDb, Authorizer());
        source.ReleaseAfterConfirmedWorkerExit(Scope(scenario), executionId, failed.InvocationId, claimed.Attempt);
        CancelOwedPipelineWork(workStore, Scope(scenario), executionId, "etl-worker-fault");

        BpmnErrorDisposition disposition = bpmn.FaultWork(service.Handle, "ETL transform worker exited unsuccessfully after source read.");
        var outcome = new StageOutcome(Environment.ProcessId, "fault", scenario, service.Handle,
            PipelineStatus: workStore.ReadExecution(Scope(scenario), executionId).Status,
            BpmnStatus: bpmn.ContinuationKind, WorkerProcesses: 1, FaultDisposition: disposition.GetType().Name,
            DestinationEffects: new IndependentSink(sinkDb, Authorizer()).CountEffects(Scope(scenario), executionId),
            Incidents: bpmn.Incidents.Count);
        Console.WriteLine(JsonSerializer.Serialize(outcome, JsonOptions));
        return 0;
    }

    private static async Task<int> CancelAsync(string scenario, string bpmnDb, string pipelineDb, string sinkDb)
    {
        using var bpmn = DurableBpmnHost.Restore(bpmnDb, BpmnScope(scenario), Definition(scenario));
        WorkRecord serviceWork = bpmn.WorkItems.Single(item => item.BindingRef == "etl-pipeline");
        string executionId = PipelineId(scenario, serviceWork.Handle);
        var workStore = new DurableWorkStore(pipelineDb, Authorizer());
        const string owner = "bpmn-service-cancel-owner";
        workStore.AcquireExecution(Scope(scenario), executionId, owner);
        var coordinator = new CompositionCoordinator(workStore, Scope(scenario), executionId, owner, new PartitionPipelinePolicy());
        coordinator.Activate();
        workStore.CreateBarrier(BarrierId(scenario));
        WorkSnapshot active = workStore.ReadWork(Scope(scenario), executionId).Single(item => item.InvocationId.Contains("partition:A:", StringComparison.Ordinal));
        using Process worker = StartWorker(pipelineDb, sinkDb, Scope(scenario), executionId, active.InvocationId,
            BarrierId(scenario), mode: "cancel-aware");
        await WaitForBarrierEntryAsync(workStore, BarrierId(scenario), TimeSpan.FromSeconds(15));

        BpmnLiveWork listener = bpmn.LiveWork.Single(work => bpmn.WorkItems.Single(item => item.Handle == work.Handle).WorkKind == "message-listener");
        bpmn.DeliverModeledMessage(listener.Handle, "cancel-etl");
        WorkRecord service = bpmn.WorkItems.Single(item => item.BindingRef == "etl-pipeline");
        Require(service.Status == "cancelled", "Interrupting BPMN message did not cancel the service task work handle.");
        CancelOwedPipelineWork(workStore, Scope(scenario), executionId, "bpmn-interrupt-cancel-etl");
        workStore.ReleaseBarrier(BarrierId(scenario));
        StageProcessResult workerResult = await WaitForProcessAsync(worker, TimeSpan.FromSeconds(15));
        if (workerResult.ExitCode != 0) throw new InvalidOperationException($"Cancelled ETL worker failed: {workerResult.Error}\n{workerResult.Output}");
        WorkerOutcome late = JsonSerializer.Deserialize<WorkerOutcome>(LastNonEmptyLine(workerResult.Output), JsonOptions)
            ?? throw new InvalidDataException("Cancelled ETL worker returned no receipt.");
        var outcome = new StageOutcome(Environment.ProcessId, "cancel", scenario, service.Handle, listener.Handle,
            PipelineStatus: workStore.ReadExecution(Scope(scenario), executionId).Status,
            BpmnStatus: bpmn.ContinuationKind, WorkerProcesses: 1, WorkerReceipt: late.Receipt,
            DestinationEffects: new IndependentSink(sinkDb, Authorizer()).CountEffects(Scope(scenario), executionId),
            CancellationMapping: "interrupting-message -> BPMN CancelWorkSubtree -> cancel ETL invocation work -> late receipt rejected");
        Console.WriteLine(JsonSerializer.Serialize(outcome, JsonOptions));
        return 0;
    }

    private static async Task<int> DrainPipelineAsync(DurableWorkStore store, CompositionCoordinator coordinator,
        WorkScope scope, string executionId, string workDb, string sinkDb, string mode)
    {
        int workerCount = 0;
        for (int step = 0; step < 16 && store.ReadExecution(scope, executionId).Status == "Running"; step++)
        {
            WorkSnapshot[] owed = store.ReadWork(scope, executionId).Where(item => item.Status == "owed").ToArray();
            if (owed.Length == 0) throw new InvalidOperationException("ETL pipeline has no owed work but is not complete.");
            foreach (WorkSnapshot item in owed)
            {
                StageProcessResult worker = await RunWorkerAsync(workDb, sinkDb, scope, executionId, item.InvocationId,
                    barrierId: "", mode: mode);
                if (worker.ExitCode != 0) throw new InvalidOperationException($"ETL worker exited {worker.ExitCode}: {worker.Error}\n{worker.Output}");
                workerCount++;
                coordinator.ApplyPendingResults();
            }
        }
        if (store.ReadExecution(scope, executionId).Status != "Completed")
            throw new InvalidOperationException("ETL pipeline exceeded its bounded recovery loop without completing.");
        return workerCount;
    }

    private static void CancelOwedPipelineWork(DurableWorkStore store, WorkScope scope, string executionId, string cancellationId)
    {
        foreach (WorkSnapshot item in store.ReadWork(scope, executionId).Where(item => item.Status is "owed" or "claimed"))
            store.CancelWork(scope, executionId, item.InvocationId, $"{cancellationId}:{item.InvocationId}");
    }

    private static async Task<StageOutcome> RunStageAsync(string stage, string scenario, string bpmnDb, string pipelineDb, string sinkDb)
        => await RunExpectedStageAsync(stage, scenario, bpmnDb, pipelineDb, sinkDb, 0);

    private static async Task<StageOutcome> RunExpectedStageAsync(string stage, string scenario, string bpmnDb, string pipelineDb, string sinkDb, int expectedExitCode)
    {
        StageProcessResult child = await RunProcessAsync("service-bridge-stage", "service-stage", stage, scenario,
            bpmnDb, pipelineDb, sinkDb);
        if (child.ExitCode != expectedExitCode)
            throw new InvalidOperationException($"Service bridge stage '{stage}' exited {child.ExitCode}; expected {expectedExitCode}: {child.Error}\n{child.Output}");
        return JsonSerializer.Deserialize<StageOutcome>(LastNonEmptyLine(child.Output), JsonOptions)
            ?? throw new InvalidDataException($"Service bridge stage '{stage}' returned no outcome.");
    }

    private static async Task<StageProcessResult> RunWorkerAsync(string workDb, string sinkDb, WorkScope scope, string executionId,
        string invocationId, string barrierId, string mode) => await RunProcessAsync("etl-batch-worker", "etl-worker",
        workDb, sinkDb, EncodeScope(scope), executionId, invocationId, barrierId, mode);

    private static async Task<StageProcessResult> RunProcessAsync(string role, params string[] args)
    {
        ProcessStartInfo start = ChildStart(args);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {role} process.");
        return await WaitForProcessAsync(process, TimeSpan.FromSeconds(30));
    }

    private static Process StartWorker(string workDb, string sinkDb, WorkScope scope, string executionId,
        string invocationId, string barrierId, string mode) => Process.Start(ChildStart("etl-worker", workDb, sinkDb,
        EncodeScope(scope), executionId, invocationId, barrierId, mode))
        ?? throw new InvalidOperationException("Could not start the blocked ETL worker process.");

    private static ProcessStartInfo ChildStart(params string[] args)
    {
        string assembly = Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("Could not locate the BPMN proof assembly for a child process.");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(assembly);
        foreach (string argument in args) start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task<StageProcessResult> WaitForProcessAsync(Process process, TimeSpan timeout)
    {
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(timeout);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Process {process.Id} did not exit within {timeout}.");
        }
        return new StageProcessResult(process.Id, process.ExitCode, await stdout, await stderr);
    }

    private static async Task WaitAtBarrierAsync(DurableWorkStore store, string barrierId, string workerId, string invocationId)
    {
        store.EnterBarrier(barrierId, workerId, Environment.ProcessId, invocationId);
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!store.IsBarrierReleased(barrierId))
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException($"ETL barrier '{barrierId}' was not released.");
            await Task.Delay(20);
        }
    }

    private static async Task WaitForBarrierEntryAsync(DurableWorkStore store, string barrierId, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (store.BarrierEntryCount(barrierId) == 0)
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException($"ETL worker did not enter barrier '{barrierId}'.");
            await Task.Delay(20);
        }
    }

    private static BpmnProcessDefinition Definition(string scenario) => scenario switch
    {
        "business-error" => ProofProcesses.EtlService("proof-etl-business-error", "business-error"),
        "cancel" => ProofProcesses.EtlService("proof-etl-cancel", "cancel-message"),
        "fault" => ProofProcesses.EtlService("proof-etl-fault"),
        _ => ProofProcesses.EtlService("proof-etl-success")
    };

    private static (string Id, string[] Rows)[] PartitionDefinitions(string scenario)
    {
        string[] first = scenario switch
        {
            "business-error" => ["row-a1", "business-error"],
            "fault" => ["row-a1", "injected-system-fault"],
            _ => ["row-a1", "row-a2"]
        };
        return [("A", first), ("B", ["row-b1", "row-b2"])];
    }

    private static bool EnsurePartition(PartitionSourceStore source, string workDb, WorkScope scope,
        string executionId, string partitionId, int batchSize, int maxBatchRecords, IReadOnlyList<string> rows)
    {
        Authorizer().EnsureAuthorized(scope);
        if (PartitionExistsAndMatches(workDb, scope, executionId, partitionId, batchSize, maxBatchRecords, rows))
            return false;
        try
        {
            source.CreatePartition(scope, executionId, partitionId, batchSize, maxBatchRecords, rows);
            return true;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            if (PartitionExistsAndMatches(workDb, scope, executionId, partitionId, batchSize, maxBatchRecords, rows))
                return false;
            throw;
        }
    }

    private static bool PartitionExistsAndMatches(string workDb, WorkScope scope, string executionId,
        string partitionId, int batchSize, int maxBatchRecords, IReadOnlyList<string> rows)
    {
        using var connection = OpenPipelineDatabase(workDb);
        int? configuredBatch = null;
        int? configuredMax = null;
        using (var config = connection.CreateCommand())
        {
            config.CommandText = "SELECT batch_size,max_batch_records FROM partition_configs WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND partition_id=$partition;";
            AddScope(config, scope);
            config.Parameters.AddWithValue("$execution", executionId);
            config.Parameters.AddWithValue("$partition", partitionId);
            using var reader = config.ExecuteReader();
            if (reader.Read())
            {
                configuredBatch = reader.GetInt32(0);
                configuredMax = reader.GetInt32(1);
            }
        }

        var actualRows = new List<(int Ordinal, string Payload)>();
        using (var contents = connection.CreateCommand())
        {
            contents.CommandText = "SELECT ordinal,payload FROM partition_source WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution AND partition_id=$partition ORDER BY ordinal;";
            AddScope(contents, scope);
            contents.Parameters.AddWithValue("$execution", executionId);
            contents.Parameters.AddWithValue("$partition", partitionId);
            using var reader = contents.ExecuteReader();
            while (reader.Read()) actualRows.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        if (configuredBatch is null)
        {
            if (actualRows.Count != 0)
                throw new InvalidDataException($"Partition '{partitionId}' has source rows without a committed configuration.");
            return false;
        }
        if (configuredBatch != batchSize || configuredMax != maxBatchRecords || actualRows.Count != rows.Count)
            throw new InvalidOperationException($"Existing partition '{partitionId}' differs from the stable service-task pipeline definition.");
        for (int index = 0; index < rows.Count; index++)
            if (actualRows[index].Ordinal != index || actualRows[index].Payload != rows[index])
                throw new InvalidOperationException($"Existing partition '{partitionId}' source snapshot differs at ordinal {index}.");
        return true;
    }

    private static (int Configs, int Records) ReadPartitionCounts(string workDb, WorkScope scope, string executionId)
    {
        using var connection = OpenPipelineDatabase(workDb);
        int configs;
        int records;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM partition_configs WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution;";
            AddScope(command, scope);
            command.Parameters.AddWithValue("$execution", executionId);
            configs = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM partition_source WHERE tenant=$tenant AND persistence_scope=$scope AND authority=$authority AND execution_id=$execution;";
            AddScope(command, scope);
            command.Parameters.AddWithValue("$execution", executionId);
            records = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
        return (configs, records);
    }

    private static SqliteConnection OpenPipelineDatabase(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 20
        }.ToString());
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=20000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void AddScope(SqliteCommand command, WorkScope scope)
    {
        command.Parameters.AddWithValue("$tenant", scope.TenantId);
        command.Parameters.AddWithValue("$scope", scope.PersistenceScope);
        command.Parameters.AddWithValue("$authority", scope.Authority);
    }

    private static string BpmnScope(string scenario) => $"etl-service:{scenario}";
    private static string PipelineId(string scenario, string serviceHandle)
    {
        string handleHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serviceHandle))).ToLowerInvariant()[..16];
        return $"partition-pipeline:{scenario}:{handleHash}";
    }
    private static string BarrierId(string scenario) => $"bpmn-service-cancel:{scenario}";
    private static string ResultDeliveryId(string executionId) => $"etl-result:{executionId}";
    private static WorkScope Scope(string scenario) => Authorizer().Issue("tenant-bpmn-proof", $"etl-service:{scenario}", "bpmn-service-bridge");
    private static HmacWorkScopeAuthorizer Authorizer() => new(ScopeKey);
    private static string EncodeScope(WorkScope scope) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(scope, JsonOptions));
    private static WorkScope DecodeScope(string value) => JsonSerializer.Deserialize<WorkScope>(Convert.FromBase64String(value), JsonOptions)
        ?? throw new InvalidDataException("ETL worker received no authorized scope.");
    private static string LastNonEmptyLine(string output) => output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Trim();
    private static void DeleteDatabase(string path)
    {
        foreach (string suffix in new[] { "", "-shm", "-wal" })
            if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }
    private static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, PrettyJson));
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static int SimulateCrash(int exitCode)
    {
        Console.Out.Flush();
        Environment.Exit(exitCode);
        return exitCode;
    }

    private sealed record StageProcessResult(int ProcessId, int ExitCode, string Output, string Error);
}
