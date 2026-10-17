using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Elsa.UnifiedExecution.ModelContracts;
using Elsa.UnifiedExecution.WorkProof;

namespace Elsa.UnifiedExecution.RuleModelProof;

internal static class ChildCommands
{
    internal const string ProofKey = "unified-rule-model-proof-local-key-2026";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunWorkerAsync(string[] args)
    {
        try
        {
            string workDb = args[1], sinkDb = args[2];
            WorkScope scope = DecodeScope(args[3]);
            string executionId = args[4], invocationId = args[5], workerId = args[6], manifestPath = args[7];
            var authorizer = CreateAuthorizer();
            var store = new DurableWorkStore(workDb, authorizer);
            var sink = new IndependentSink(sinkDb, authorizer);
            ResolvedModel model = ModelRegistry.ResolveExecution(manifestPath, HostCapabilities.Complete, store.ReadExecution(scope, executionId));
            WorkSnapshot metadata = store.ReadWork(scope, executionId).Single(x => x.InvocationId == invocationId);
            if (!model.Manifest.WorkHandlers.Contains(new ModelWorkHandler(metadata.HandlerId, metadata.HandlerVersion)))
            {
                Console.WriteLine("MODEL_HANDLER_NOT_DECLARED");
                return 9;
            }
            WorkLease? lease = store.TryClaimWork(scope, executionId, invocationId, workerId,
                metadata.HandlerId, metadata.HandlerVersion);
            if (lease is null)
            {
                Console.WriteLine("WORK_NOT_CLAIMED");
                return 9;
            }

            // The sink is intentionally only observed, not written: rule inference is pure and
            // its durable side effect is the host's semantic state commit after the receipt.
            _ = sink.CountEffects(scope, executionId);
            string resultJson = model.Package.ExecuteWork(model.Artifact, lease.HandlerId, lease.HandlerVersion, lease.InputJson);
            WorkReceipt receipt = store.CompleteWork(lease, resultJson);
            Console.WriteLine(JsonSerializer.Serialize(new WorkerOutcome(
                Environment.ProcessId, metadata.HandlerId, receipt.Status, receipt.Fingerprint), WebJson));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"RULE_WORKER_ERROR: {exception.GetType().Name}: {exception.Message}");
            return 3;
        }
    }

    public static Task<int> RunStartAsync(string[] args)
    {
        try
        {
            string workDb = args[1], sinkDb = args[2];
            WorkScope scope = DecodeScope(args[3]);
            string executionId = args[4], owner = args[5], manifestPath = args[6];
            var authorizer = CreateAuthorizer();
            ResolvedModel model = ModelRegistry.Resolve(manifestPath, HostCapabilities.Complete);
            var store = new DurableWorkStore(workDb, authorizer);
            _ = new IndependentSink(sinkDb, authorizer);
            var coordinator = new CompositionCoordinator(store, scope, executionId, owner, model.Policy);
            CommitReceipt receipt = coordinator.Start("{}");
            Console.WriteLine(JsonSerializer.Serialize(new StartOutcome(Environment.ProcessId, receipt.Status,
                model.Policy.Id, receipt.WorkIds.Count), WebJson));
            return Task.FromResult(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"RULE_START_ERROR: {exception.GetType().Name}: {exception.Message}");
            return Task.FromResult(3);
        }
    }

    public static Task<int> RunResumeAsync(string[] args)
    {
        try
        {
            string workDb = args[1];
            WorkScope scope = DecodeScope(args[2]);
            string executionId = args[3], owner = args[4], manifestPath = args[5];
            var authorizer = CreateAuthorizer();
            var store = new DurableWorkStore(workDb, authorizer);
            ExecutionSnapshot before = store.ReadExecution(scope, executionId);
            ResolvedModel model = ModelRegistry.ResolveExecution(manifestPath, HostCapabilities.Complete, before);
            var coordinator = new CompositionCoordinator(store, scope, executionId, owner, model.Policy);
            coordinator.Activate();
            int applied = coordinator.ApplyPendingResults();
            ExecutionSnapshot after = store.ReadExecution(scope, executionId);
            ModelInspection inspection = model.Package.Inspect(model.Artifact, after.StateJson);
            Console.WriteLine(JsonSerializer.Serialize(new ResumeOutcome(
                Environment.ProcessId, before.Revision, after.Revision, applied, after.Status, inspection), WebJson));
            return Task.FromResult(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"RULE_RESUME_ERROR: {exception.GetType().Name}: {exception.Message}");
            return Task.FromResult(3);
        }
    }

    public static ProcessStartInfo Start(params string[] arguments)
    {
        string assembly = Assembly.GetExecutingAssembly().Location;
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(assembly);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["ELSA_RULE_MODEL_PROOF_KEY"] = ProofKey;
        return start;
    }

    public static WorkScope DecodeScope(string encoded)
        => JsonSerializer.Deserialize<WorkScope>(Convert.FromBase64String(encoded), WebJson)
           ?? throw new InvalidDataException("Child process received an empty work scope.");

    public static string EncodeScope(WorkScope scope)
        => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(scope, WebJson));

    public static HmacWorkScopeAuthorizer CreateAuthorizer()
    {
        string key = Environment.GetEnvironmentVariable("ELSA_RULE_MODEL_PROOF_KEY") ?? ProofKey;
        return new HmacWorkScopeAuthorizer(key);
    }

    internal sealed record WorkerOutcome(int ProcessId, string HandlerId, string ReceiptStatus, string Fingerprint);
    internal sealed record StartOutcome(int ProcessId, string Status, string PolicyId, int WorkCount);
    internal sealed record ResumeOutcome(int ProcessId, long BeforeRevision, long AfterRevision, int Applied, string ExecutionStatus,
        ModelInspection Inspection);
}
