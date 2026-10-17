using System.Text;
using System.Text.Json;
using Elsa.UnifiedExecution.WorkProof;

namespace Elsa.AgentCompositionProof;

internal static class AgentToolWorker
{
    public const string HandlerId = "deterministic-approved-tool";
    public const string HandlerVersion = "1";
    private const int FailAfterEffectExitCode = 74;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 10) throw new ArgumentException("Usage: --tool-worker <workDb> <sinkDb> <scopeBase64> <executionId> <invocationId> <workerId> <leaseSeconds> <failpoint|-> <wait|once>");
        try
        {
            string workDb = args[1];
            string sinkDb = args[2];
            WorkScope scope = DecodeScope(args[3]);
            string executionId = args[4];
            string invocationId = args[5];
            string workerId = args[6];
            int leaseSeconds = int.Parse(args[7]);
            string failpoint = args[8];
            bool waitForClaim = args[9] == "wait";

            var auth = new HmacWorkScopeAuthorizer(ReadKey());
            var store = new DurableWorkStore(workDb, auth);
            var sink = new IndependentSink(sinkDb, auth);
            WorkLease? lease = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(25);
            do
            {
                lease = store.TryClaimWork(scope, executionId, invocationId, workerId, HandlerId, HandlerVersion, leaseSeconds);
                if (lease is not null || !waitForClaim) break;
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("Approved tool work did not become claimable.");
                await Task.Delay(50);
            } while (true);
            if (lease is null) return 9;

            ToolInvocation invocation = ProofJson.Deserialize<ToolInvocation>(lease.InputJson);
            AgentCollaborationPolicy.CollaborationState state = ProofJson.Deserialize<AgentCollaborationPolicy.CollaborationState>(
                store.ReadExecution(scope, executionId).StateJson);
            if (state.Stage != "ToolRunning" || state.Approval is null || state.PendingToolRequest is null
                || invocation.Request != state.PendingToolRequest || invocation.Approval != state.Approval
                || invocation.Approval.RequestId != invocation.Request.RequestId
                || invocation.Approval.ArgumentsFingerprint != invocation.Request.ArgumentsFingerprint
                || invocation.Approval.RequiredCapability != invocation.Request.RequiredCapability)
                throw new UnauthorizedAccessException("Tool input does not match the persisted exact approval.");

            string effectMaterial = $"{invocation.Request.ToolName}\n{invocation.Request.ArgumentsFingerprint}\n{invocation.Request.RequiredCapability}";
            string effectFingerprint = ResourceReferenceResolver.Sha256(effectMaterial);
            string output = $"deterministic-ticket-{effectFingerprint[..12]}";
            var result = new ToolActionResult(invocation.Request.RequestId, invocation.Request.ToolName,
                invocation.Request.ArgumentsFingerprint, effectFingerprint, output);
            string resultJson = ProofJson.Serialize(result);
            bool duplicateEffect = sink.Write(scope, executionId, $"tool/{invocation.Request.IdempotencyKey}", resultJson);
            if (failpoint == "crash-after-sink")
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    outcome = "toolEffectPersistedBeforeAcknowledgement",
                    requestId = invocation.Request.RequestId,
                    duplicateEffect,
                    processId = Environment.ProcessId,
                    attempt = lease.Attempt,
                    fence = lease.Fence
                }));
                Environment.Exit(FailAfterEffectExitCode);
            }

            WorkReceipt receipt = store.CompleteWork(lease, resultJson);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                outcome = receipt.Status,
                requestId = invocation.Request.RequestId,
                duplicateEffect,
                duplicateResult = receipt.Duplicate,
                processId = Environment.ProcessId,
                attempt = lease.Attempt,
                fence = lease.Fence,
                result
            }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"APPROVED_TOOL_ERROR: {exception.GetType().Name}: {exception.Message}");
            return 3;
        }
    }

    private static WorkScope DecodeScope(string encoded) => ProofJson.Deserialize<WorkScope>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
    private static string ReadKey() => Environment.GetEnvironmentVariable("ELSA_AGENT_PROOF_KEY") ?? throw new InvalidOperationException("Parent did not provide the local proof key.");
}
