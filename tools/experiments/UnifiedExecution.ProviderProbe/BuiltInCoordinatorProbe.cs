using System.Diagnostics;
using System.Text.Json;
using Elsa.UnifiedExecution.WorkProof;

/// <summary>Runs the same native WorkProof policies without an Orleans provider.</summary>
internal static class BuiltInCoordinatorProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 7)
            throw new ArgumentException("Usage: builtin-run <work-db> <sink-db> <scope-base64> <execution> <policy> <input-base64>");

        string workDb = args[1];
        string sinkDb = args[2];
        WorkScope scope = JsonSerializer.Deserialize<WorkScope>(Convert.FromBase64String(args[3]))
            ?? throw new InvalidDataException("The built-in scope was empty.");
        string executionId = args[4];
        string policyName = args[5];
        _ = ExecutionGrainKey.Create(scope, executionId, policyName);
        string inputJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(args[6]));
        string proofKey = Environment.GetEnvironmentVariable("ELSA_WORK_PROOF_KEY") ?? ProviderClientCommands.ProofKey;
        var authorizer = new HmacWorkScopeAuthorizer(proofKey);
        var store = new DurableWorkStore(workDb, authorizer);
        var sink = new IndependentSink(sinkDb, authorizer);
        ICompositionPolicy policy = policyName switch
        {
            "sequence" => new SequencePolicy(),
            "flowchart" => new FlowchartPolicy(),
            _ => throw new ArgumentException($"Unsupported built-in policy '{policyName}'.")
        };
        string owner = $"builtin:{Environment.ProcessId}:{Guid.NewGuid():N}";
        var coordinator = new CompositionCoordinator(store, scope, executionId, owner, policy);
        coordinator.Start(inputJson);

        var workers = new List<(string Invocation, Process Process)>();
        IReadOnlyList<int> barrierProcessIds = [];
        try
        {
            if (policyName == "sequence")
            {
                await RunWorkerAsync(workDb, sinkDb, scope, executionId, "sequence-0", "builtin-sequence-0", "-");
                coordinator.ApplyPendingResults();
                await RunWorkerAsync(workDb, sinkDb, scope, executionId, "sequence-1", "builtin-sequence-1", "-");
                coordinator.ApplyPendingResults();
            }
            else
            {
                string barrierId = "builtin-flowchart-" + Guid.NewGuid().ToString("N");
                store.CreateBarrier(barrierId);
                workers.Add(("branch-a", StartWorker(workDb, sinkDb, scope, executionId, "branch-a", "builtin-flowchart-a", barrierId)));
                workers.Add(("branch-b", StartWorker(workDb, sinkDb, scope, executionId, "branch-b", "builtin-flowchart-b", barrierId)));
                await WaitForBarrierAsync(store, barrierId, workers);
                barrierProcessIds = store.BarrierProcessIds(barrierId);
                if (barrierProcessIds.Count != 2 || barrierProcessIds.Distinct().Count() != 2
                    || workers.Any(worker => worker.Process.HasExited))
                    throw new InvalidOperationException("Built-in Flowchart workers did not overlap at the held process barrier.");

                store.ReleaseBarrier(barrierId);
                await Task.WhenAll(workers.Select(WaitWorkerAsync));
                coordinator.ApplyPendingResults();
                coordinator.RunPendingContinuations();
            }
        }
        finally
        {
            foreach ((_, Process process) in workers)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                    await process.WaitForExitAsync();
                }
                process.Dispose();
            }
        }

        ExecutionSnapshot execution = store.ReadExecution(scope, executionId);
        CoordinatorSnapshot coordinatorState = store.ReadCoordinator(scope, executionId);
        var snapshot = new
        {
            policyId = execution.PolicyId,
            status = execution.Status,
            stateJson = execution.StateJson,
            work = store.ReadWork(scope, executionId).OrderBy(work => work.InvocationId, StringComparer.Ordinal).Select(work => new
            {
                invocationId = work.InvocationId,
                handlerId = work.HandlerId,
                handlerVersion = work.HandlerVersion,
                inputJson = work.InputJson,
                status = work.Status
            }),
            pendingContinuations = store.ReadPendingContinuations(scope, executionId).Order(StringComparer.Ordinal),
            effects = sink.CountEffects(scope, executionId),
            owner = coordinatorState.Owner,
            fence = coordinatorState.Fence,
            revision = coordinatorState.Revision,
            pid = Environment.ProcessId
        };
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            snapshot,
            workerOverlap = policyName == "flowchart" && barrierProcessIds.Count == 2,
            workerBarrierProcessIds = barrierProcessIds
        }));
        return 0;
    }

    private static async Task RunWorkerAsync(string workDb, string sinkDb, WorkScope scope,
        string executionId, string invocationId, string workerId, string barrierId)
    {
        Process process = StartWorker(workDb, sinkDb, scope, executionId, invocationId, workerId, barrierId);
        try { await WaitWorkerAsync((invocationId, process)); }
        finally { process.Dispose(); }
    }

    private static Process StartWorker(string workDb, string sinkDb, WorkScope scope, string executionId,
        string invocationId, string workerId, string barrierId)
    {
        string workerDll = Path.Combine(AppContext.BaseDirectory, "UnifiedExecution.WorkProof.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in new[]
        {
            workerDll, "--worker", workDb, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(scope))),
            executionId, invocationId, workerId, barrierId, "none", sinkDb
        }) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException($"Could not start worker for '{invocationId}'.");
    }

    private static async Task WaitForBarrierAsync(DurableWorkStore store, string barrierId,
        IReadOnlyList<(string Invocation, Process Process)> workers)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (store.BarrierEntryCount(barrierId) < 2)
        {
            if (workers.Any(worker => worker.Process.HasExited))
                throw new InvalidOperationException("A built-in Flowchart worker exited before entering the overlap barrier.");
            await Task.Delay(25, deadline.Token);
        }
    }

    private static async Task WaitWorkerAsync((string Invocation, Process Process) worker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        Task<string> stdout = worker.Process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> stderr = worker.Process.StandardError.ReadToEndAsync(timeout.Token);
        await worker.Process.WaitForExitAsync(timeout.Token);
        string output = await stdout;
        string error = await stderr;
        if (worker.Process.ExitCode != 0)
            throw new InvalidOperationException($"Built-in worker '{worker.Invocation}' exited {worker.Process.ExitCode}: {output}{error}");
    }
}
