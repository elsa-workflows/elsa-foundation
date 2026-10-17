using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Elsa.UnifiedExecution.WorkProof;

internal static class WorkerCommands
{
    public const string ProofKey = "unified-work-proof-local-key-2026";
    private const int FailureExitCode = 73;

    public static RunningChild Start(params string[] arguments)
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
        start.Environment["ELSA_WORK_PROOF_KEY"] = ProofKey;
        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start proof child process.");
        return new RunningChild(process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
    }

    public static async Task<ChildResult> WaitAsync(RunningChild child, TimeSpan? timeout = null)
    {
        Process process = child.Process;
        using var limit = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(25));
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Proof process {process.Id} did not finish within the bounded wait.");
        }
        string stdout = await child.Stdout;
        string stderr = await child.Stderr;
        return new ChildResult(process.Id, process.ExitCode, stdout + stderr);
    }

    public static async Task<int> RunWorkerAsync(string[] args)
    {
        try
        {
            // --worker <work-db> <scope-base64> <execution> <invocation> <worker> <barrier|-> <failpoint> [sink-db]
            string workDb = args[1];
            WorkScope scope = DecodeScope(args[2]);
            string executionId = args[3];
            string invocationId = args[4];
            string workerId = args[5];
            string barrierId = args[6];
            string failpoint = args[7];
            string sinkDb = args[8];
            string key = Environment.GetEnvironmentVariable("ELSA_WORK_PROOF_KEY") ?? throw new InvalidOperationException("Proof scope key was not inherited by the worker.");
            var authorizer = new HmacWorkScopeAuthorizer(key);
            var store = new DurableWorkStore(workDb, authorizer);
            var sink = new IndependentSink(sinkDb, authorizer);
            WorkSnapshot metadata = store.ReadWork(scope, executionId).Single(x => x.InvocationId == invocationId);
            string supportedHandler = metadata.HandlerId == "partition-batch" ? "partition-batch" : "snapshot-echo";
            string supportedVersion = "1";
            WorkLease? lease = store.TryClaimWork(scope, executionId, invocationId, workerId, supportedHandler, supportedVersion);
            if (lease is null)
            {
                Console.WriteLine(metadata.UnsupportedReason ?? "WORK_NOT_CLAIMED");
                return 9;
            }
            if (metadata.HandlerId == "partition-batch")
            {
                var source = new PartitionSourceStore(workDb, authorizer);
                PartitionBatchInput input = JsonSupport.Deserialize<PartitionBatchInput>(lease.InputJson);
                IReadOnlyList<PartitionRecord> records = source.ReadBatch(scope, executionId, invocationId, lease.Attempt, input);
                await WaitAtBarrier(store, barrierId, workerId, invocationId);
                foreach (PartitionRecord record in records)
                    sink.Write(scope, executionId, $"partition/{input.PartitionId}/{record.Ordinal}", record.Payload);
                source.EndBatch(scope, executionId, invocationId, lease.Attempt, "effects-delivered");
                if (failpoint == "crash-after-sink")
                {
                    Console.WriteLine("FAILPOINT: destination effect persisted; result receipt not committed.");
                    Environment.Exit(FailureExitCode);
                }
                long nextCursor = records.Count == 0 ? input.Cursor : records[^1].Ordinal;
                string resultJson = JsonSupport.Serialize(new PartitionBatchResult(input.PartitionId, input.Cursor, nextCursor, records.Count, records.Count == 0));
                WorkReceipt receipt = store.CompleteWork(lease, resultJson);
                Console.WriteLine(JsonSupport.Serialize(receipt));
                return 0;
            }

            if (metadata.HandlerId != "snapshot-echo") throw new InvalidOperationException($"Unsupported handler '{metadata.HandlerId}'.");
            await WaitAtBarrier(store, barrierId, workerId, invocationId);
            // The handler consumes only the serialized input pinned in the durable dispatch row.
            sink.Write(scope, executionId, $"activity/{invocationId}", lease.InputJson);
            if (failpoint == "crash-after-sink")
            {
                Console.WriteLine("FAILPOINT: destination effect persisted; result receipt not committed.");
                Environment.Exit(FailureExitCode);
            }
            WorkReceipt result = store.CompleteWork(lease, lease.InputJson);
            Console.WriteLine(JsonSupport.Serialize(result));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"WORKER_ERROR: {exception.GetType().Name}: {exception.Message}");
            return 3;
        }
    }

    public static async Task<int> RunSeedDispatchCrashAsync(string[] args)
    {
        string workDb = args[1];
        WorkScope scope = DecodeScope(args[2]);
        string executionId = args[3];
        string key = Environment.GetEnvironmentVariable("ELSA_WORK_PROOF_KEY")!;
        var store = new DurableWorkStore(workDb, new HmacWorkScopeAuthorizer(key));
        var plan = new CompositionPlan("{\"phase\":\"waiting\"}", [new WorkDefinition("after-crash", "snapshot-echo", "1", "{\"value\":\"dispatch-survived\"}")], [], false);
        store.CreateAndCommitInitialPlan(scope, executionId, "crash-dispatch-witness", "coordinator-before-exit", $"start:{executionId}", plan);
        Console.WriteLine("COMMIT_DONE_NO_NOTIFICATION_SENT");
        await Task.Yield();
        Environment.Exit(FailureExitCode);
        return FailureExitCode;
    }

    public static int RunBeforeDispatchCrash(string[] args)
    {
        Console.WriteLine("EXIT_BEFORE_ATOMIC_DISPATCH");
        Environment.Exit(72);
        return 72;
    }

    public static int RunLostCommitReplyAsync(string[] args)
    {
        string workDb = args[1];
        WorkScope scope = DecodeScope(args[2]);
        string executionId = args[3];
        string owner = args[4];
        long fence = long.Parse(args[5]);
        long revision = long.Parse(args[6]);
        string commitId = args[7];
        string key = Environment.GetEnvironmentVariable("ELSA_WORK_PROOF_KEY")!;
        var store = new DurableWorkStore(workDb, new HmacWorkScopeAuthorizer(key));
        var plan = new CompositionPlan("{\"counter\":1}", [new WorkDefinition("lost-response-work", "snapshot-echo", "1", "{\"value\":\"once\"}")], [], false);
        store.CommitPlan(scope, executionId, owner, fence, revision, commitId, plan);
        Console.WriteLine("COMMIT_DONE_RESPONSE_NOT_DELIVERED");
        Environment.Exit(FailureExitCode);
        return FailureExitCode;
    }

    private static async Task WaitAtBarrier(DurableWorkStore store, string barrierId, string workerId, string invocationId)
    {
        if (barrierId == "-") return;
        store.EnterBarrier(barrierId, workerId, Environment.ProcessId, invocationId);
        DateTime deadline = DateTime.UtcNow.AddSeconds(25);
        while (!store.IsBarrierReleased(barrierId))
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException($"Barrier '{barrierId}' was not released.");
            await Task.Delay(10);
        }
    }

    internal static WorkScope DecodeScope(string encoded)
        => JsonSupport.Deserialize<WorkScope>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));

    internal static string EncodeScope(WorkScope scope)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSupport.Serialize(scope)));
}

internal sealed record ChildResult(int ProcessId, int ExitCode, string Output);
internal sealed record RunningChild(Process Process, Task<string> Stdout, Task<string> Stderr);
