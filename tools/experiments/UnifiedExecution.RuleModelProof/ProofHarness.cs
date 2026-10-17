using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.UnifiedExecution.FiniteRules;
using Elsa.UnifiedExecution.ModelContracts;
using Elsa.UnifiedExecution.WorkProof;

namespace Elsa.UnifiedExecution.RuleModelProof;

internal static class ProofHarness
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly List<ProofAssertion> Assertions = [];
    private static readonly List<ChildEvidence> Children = [];

    public static async Task<int> RunAsync()
    {
        Assertions.Clear();
        Children.Clear();
        string manifestPath = Path.Combine(AppContext.BaseDirectory, "Assets", "model-manifest.json");
        string scratch = Path.Combine(Path.GetTempPath(), $"elsa-rule-model-proof-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        string workDb = Path.Combine(scratch, "work.db");
        string sinkDb = Path.Combine(scratch, "sink.db");
        var authorizer = ChildCommands.CreateAuthorizer();
        WorkScope scope = authorizer.Issue("tenant-proof", "rule-model-local", "positive-rules");
        var store = new DurableWorkStore(workDb, authorizer);
        var sink = new IndependentSink(sinkDb, authorizer);

        await ProveIncompatibleHostPreflightAsync(manifestPath, workDb, sink, scope);
        ResolvedModel model = ModelRegistry.Resolve(manifestPath, HostCapabilities.Complete);
        const string executionId = "positive-rules-main";
        const string owner = "rule-model-host";
        ChildResult startedProcess = await RunChildAsync("fresh-host-start", ChildCommands.Start("--start", workDb, sinkDb,
            ChildCommands.EncodeScope(scope), executionId, owner, manifestPath));
        ChildCommands.StartOutcome started = DeserializeChild<ChildCommands.StartOutcome>(startedProcess);
        Assert(startedProcess.ExitCode == 0 && started.Status == "Running" && started.WorkCount == 1
            && store.ReadExecution(scope, executionId).PolicyId == model.Policy.Id,
            "start.pins-one-rule-round", $"fresh host process {started.ProcessId} resolved the pinned model and durably dispatched its first inference round");

        PackageSwapEvidence packageSwap = await ProvePackageSwapRejectedAsync(
            manifestPath, scratch, workDb, scope, executionId, owner, model);

        int roundCount = 0;
        while (store.ReadExecution(scope, executionId).Status != "Completed")
        {
            if (++roundCount > 8) throw new InvalidOperationException("Finite-rule execution exceeded its proof round bound.");
            ExecutionSnapshot before = store.ReadExecution(scope, executionId);
            ResolvedModel pinned = ModelRegistry.ResolveExecution(manifestPath, HostCapabilities.Complete, before);
            WorkSnapshot pending = store.ReadWork(scope, executionId).Single(x => x.Status == "owed");
            ChildResult worker = await RunChildAsync("rule-round-worker", ChildCommands.Start("--rule-worker", workDb, sinkDb,
                ChildCommands.EncodeScope(scope), executionId, pending.InvocationId, $"rule-worker-{roundCount}", manifestPath));
            Assert(worker.ExitCode == 0, $"worker.round-{roundCount}.completed", $"fresh worker process {worker.ProcessId} committed one deterministic rule result");

            WorkSnapshot completed = store.ReadWork(scope, executionId).Single(x => x.InvocationId == pending.InvocationId);
            var originalLease = new WorkLease(scope, executionId, completed.InvocationId, completed.Owner!, completed.Fence,
                completed.Attempt, completed.HandlerId, completed.HandlerVersion, completed.InputJson);
            WorkReceipt duplicate = store.CompleteWork(originalLease, completed.ResultJson!);
            Assert(duplicate.Status == "Succeeded" && duplicate.Duplicate,
                $"result.round-{roundCount}.duplicate-idempotent", "replaying the identical completed result returns its existing durable receipt");

            // The child is a fresh coordinator process. The parent deliberately does not apply the result first.
            ChildResult resumed = await RunChildAsync("fresh-host-resume", ChildCommands.Start("--resume", workDb,
                ChildCommands.EncodeScope(scope), executionId, owner, manifestPath));
            Assert(resumed.ExitCode == 0 && resumed.ProcessId != worker.ProcessId,
                $"restart.round-{roundCount}.fresh-process", $"worker {worker.ProcessId} and rehydrated host {resumed.ProcessId} are distinct OS processes");

            ExecutionSnapshot after = store.ReadExecution(scope, executionId);
            ModelInspection inspection = pinned.Package.Inspect(pinned.Artifact, after.StateJson);
            RuleInspection progress = JsonSerializer.Deserialize<RuleInspection>(inspection.DetailJson, WebJson)!;
            Assert(progress.Round == roundCount && inspection.ArtifactFingerprint == model.Artifact.Fingerprint,
                $"inspect.round-{roundCount}.pinned-state", "inspection resolved the same artifact fingerprint and persisted rule-state schema after restart");
        }

        ExecutionSnapshot finished = store.ReadExecution(scope, executionId);
        ModelInspection final = model.Package.Inspect(model.Artifact, finished.StateJson);
        RuleInspection detail = JsonSerializer.Deserialize<RuleInspection>(final.DetailJson, WebJson)
            ?? throw new InvalidDataException("Final rule inspection could not be parsed.");
        string[] expectedFacts = ["final", "joined", "left", "right", "seed"];
        Assert(finished.Status == "Completed" && final.Status == "Completed" && detail.LeastFixedPointReached,
            "closure.completed-only-at-empty-delta", "semantic completion is committed only after a full positive-rule round adds no facts");
        Assert(detail.Facts.SequenceEqual(expectedFacts, StringComparer.Ordinal) && detail.Round == 4,
            "closure.least-fixed-point-facts", "four monotone rounds derive the least closure {seed,left,right,joined,final}; unreachable stays absent");
        Assert(detail.Trace.Length == 4
            && detail.Trace[0].AddedFacts.SequenceEqual(new[] { "left", "right" }, StringComparer.Ordinal)
            && detail.Trace[1].AddedFacts.SequenceEqual(new[] { "joined" }, StringComparer.Ordinal)
            && detail.Trace[2].AddedFacts.SequenceEqual(new[] { "final" }, StringComparer.Ordinal)
            && detail.Trace[3].AddedFacts.Length == 0,
            "closure.trace-proves-rounds", "inspection exposes the rule firings and final empty-delta round that establishes the fixed point");
        Assert(store.CountWorkRows(scope, executionId) == 4 && sink.CountEffects(scope, executionId) == 0,
            "closure.durable-work-is-round-scoped", "four persisted round invocations produce no per-fact workflow executions or external effects");

        CancellationEvidence cancellation = await ProveCancellationAsync(store, workDb, sinkDb, scope, sink, manifestPath);
        var report = new ProofReport(
            "source-pinned experimental model extension witness",
            model.Manifest.ModelId,
            model.Manifest.ModelVersion,
            model.Manifest.StateSchema,
            model.Artifact.Fingerprint,
            model.Package.PackageId,
            model.Package.PackageVersion,
            model.Manifest.PackageSha256,
            model.Policy.Id,
            model.ExecutionPinFingerprint,
            executionId,
            final,
            detail,
            cancellation,
            packageSwap,
            Assertions.ToArray(),
            Children.ToArray());
        string evidencePath = Path.Combine(scratch, "evidence.json");
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(report, new JsonSerializerOptions(WebJson) { WriteIndented = true }));
        Console.WriteLine($"Evidence: {evidencePath}");
        Console.WriteLine(JsonSerializer.Serialize(report, WebJson));
        return Assertions.All(x => x.Passed) ? 0 : 1;
    }

    private static async Task<PackageSwapEvidence> ProvePackageSwapRejectedAsync(
        string manifestPath, string scratch, string workDb, WorkScope scope, string executionId, string owner,
        ResolvedModel original)
    {
        string swapPackage = Environment.GetEnvironmentVariable("ELSA_RULE_MODEL_SWAP_PACKAGE")
            ?? throw new InvalidOperationException("Run this witness through run-proof.sh to provide the alternate package build.");
        if (!File.Exists(swapPackage)) throw new FileNotFoundException("Alternate package build is missing.", swapPackage);

        string swappedDirectory = Path.Combine(scratch, "swapped-package");
        string assetsDirectory = Path.Combine(swappedDirectory, "Assets");
        Directory.CreateDirectory(assetsDirectory);
        File.Copy(Path.Combine(Path.GetDirectoryName(manifestPath)!, original.Manifest.ArtifactFile),
            Path.Combine(assetsDirectory, original.Manifest.ArtifactFile));
        string swappedAssembly = Path.Combine(swappedDirectory, Path.GetFileName(swapPackage));
        File.Copy(swapPackage, swappedAssembly);
        string swappedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(swappedAssembly))).ToLowerInvariant();
        ModelManifest swappedManifest = original.Manifest with
        {
            PackageAssembly = $"../{Path.GetFileName(swappedAssembly)}",
            PackageSha256 = swappedHash
        };
        string swappedManifestPath = Path.Combine(assetsDirectory, "model-manifest.json");
        await File.WriteAllTextAsync(swappedManifestPath,
            JsonSerializer.Serialize(swappedManifest, new JsonSerializerOptions(WebJson) { WriteIndented = true }));

        ChildResult resume = await RunChildAsync("fresh-host-rejects-package-swap",
            ChildCommands.Start("--resume", workDb, ChildCommands.EncodeScope(scope), executionId, owner, swappedManifestPath));
        bool rejectedForChangedPin = resume.ExitCode != 0
            && resume.Output.Contains("Persisted execution references a different model policy", StringComparison.Ordinal);
        Assert(rejectedForChangedPin, "resume.rejects-swapped-package-pin",
            $"fresh host rejected the alternate package (exit {resume.ExitCode}); output: {resume.Output.ReplaceLineEndings(" | ")}");
        var store = new DurableWorkStore(workDb, ChildCommands.CreateAuthorizer());
        WorkSnapshot pending = store.ReadWork(scope, executionId).Single();
        ChildResult worker = await RunChildAsync("fresh-worker-rejects-package-swap",
            ChildCommands.Start("--rule-worker", workDb, Path.Combine(scratch, "sink.db"), ChildCommands.EncodeScope(scope),
                executionId, pending.InvocationId, "swapped-worker", swappedManifestPath));
        WorkSnapshot unchanged = store.ReadWork(scope, executionId).Single();
        Assert(worker.ExitCode != 0 && worker.Output.Contains("Persisted execution references a different model policy", StringComparison.Ordinal)
            && unchanged.Attempt == pending.Attempt && unchanged.Fence == pending.Fence && unchanged.Status == "owed",
            "worker.rejects-swapped-package-before-claim", "a different package cannot claim work using the original handler name/version");
        return new PackageSwapEvidence(original.Manifest.PackageSha256, swappedHash, original.ExecutionPinFingerprint,
            rejectedForChangedPin, Marker(resume.Output));
    }

    private static Task ProveIncompatibleHostPreflightAsync(string manifestPath, string workDb, IndependentSink sink, WorkScope scope)
    {
        string incompatibleId = "incompatible-host-must-not-start";
        string required = "composition.atomic-semantic-commit.v1";
        var deficient = new HostCapabilities(HostCapabilities.Complete.Values.Where(x => x != required));
        bool rejected = Throws<NotSupportedException>(() => ModelRegistry.Resolve(manifestPath, deficient));
        var store = new DurableWorkStore(workDb, ChildCommands.CreateAuthorizer());
        bool noExecution = Throws<UnauthorizedAccessException>(() => store.ReadExecution(scope, incompatibleId));
        Assert(rejected && noExecution && sink.CountEffects(scope, incompatibleId) == 0,
            "compatibility.rejects-before-effects", "missing host capability is rejected before start, durable execution creation, work dispatch, or sink effects");
        return Task.CompletedTask;
    }

    private static async Task<CancellationEvidence> ProveCancellationAsync(
        DurableWorkStore store, string workDb, string sinkDb, WorkScope scope, IndependentSink sink, string manifestPath)
    {
        const string executionId = "positive-rules-cancelled";
        const string owner = "rule-model-cancel-host";
        ResolvedModel model = ModelRegistry.Resolve(manifestPath, HostCapabilities.Complete);
        ChildResult started = await RunChildAsync("cancel-host-start", ChildCommands.Start("--start",
            workDb, sinkDb,
            ChildCommands.EncodeScope(scope), executionId, owner, manifestPath));
        ChildCommands.StartOutcome start = DeserializeChild<ChildCommands.StartOutcome>(started);
        Assert(started.ExitCode == 0 && start.Status == "Running" && start.WorkCount == 1,
            "cancellation.host-starts-in-fresh-process", $"host process {start.ProcessId} created one cancellable round");
        WorkSnapshot pending = store.ReadWork(scope, executionId).Single();
        ModelWorkHandler handler = model.Manifest.WorkHandlers.Single();
        WorkLease lease = store.TryClaimWork(scope, executionId, pending.InvocationId, "claim-before-cancel",
            handler.Id, handler.Version)!;
        bool cancelled = store.CancelWork(scope, executionId, pending.InvocationId, "operator-cancel-round-0");
        WorkReceipt late = store.CompleteWork(lease, "{}");
        WorkLease? afterCancel = store.TryClaimWork(scope, executionId, pending.InvocationId, "worker-after-cancel",
            handler.Id, handler.Version);
        ChildResult resumed = await RunChildAsync("cancel-host-restart", ChildCommands.Start("--resume", workDb,
            ChildCommands.EncodeScope(scope), executionId, owner, manifestPath));
        ChildCommands.ResumeOutcome resume = DeserializeChild<ChildCommands.ResumeOutcome>(resumed);
        int applied = resume.Applied;
        ExecutionSnapshot snapshot = store.ReadExecution(scope, executionId);
        ModelInspection inspection = model.Package.Inspect(model.Artifact, snapshot.StateJson);
        RuleInspection detail = JsonSerializer.Deserialize<RuleInspection>(inspection.DetailJson, WebJson)!;
        bool preserved = cancelled && late.Status == "LateRejected" && afterCancel is null && applied == 0
            && detail.Facts.SequenceEqual(new[] { "seed" }, StringComparer.Ordinal) && detail.Round == 0
            && sink.CountEffects(scope, executionId) == 0;
        Assert(preserved, "cancellation.late-round-does-not-advance", "cancelled round is unclaimable; its late result is rejected and model facts remain at the base set");
        return new CancellationEvidence(executionId, store.ReadWork(scope, executionId).Single().Status,
            late.Status, afterCancel is null, applied, inspection);
    }

    private static T DeserializeChild<T>(ChildResult result)
        => JsonSerializer.Deserialize<T>(result.Output, WebJson)
           ?? throw new InvalidDataException($"Child process {result.ProcessId} returned an empty result.");

    private static async Task<ChildResult> RunChildAsync(string role, ProcessStartInfo start)
    {
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start proof child process.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Proof child {process.Id} ({role}) did not finish in the bounded wait.");
        }
        string output = (await stdout) + (await stderr);
        var result = new ChildResult(process.Id, process.ExitCode, output);
        Children.Add(new ChildEvidence(role, result.ProcessId, result.ExitCode, Marker(result.Output)));
        return result;
    }

    private static string Marker(string output)
        => output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).LastOrDefault()
           ?? "no-output";

    private static void Assert(bool passed, string id, string observed)
    {
        Assertions.Add(new ProofAssertion(id, passed, observed));
        if (!passed) throw new InvalidOperationException($"Assertion '{id}' failed: {observed}");
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try { action(); return false; }
        catch (TException) { return true; }
    }

    internal sealed record ChildResult(int ProcessId, int ExitCode, string Output);
    private sealed record ProofAssertion(string Id, bool Passed, string Observed);
    private sealed record ChildEvidence(string Role, int ProcessId, int ExitCode, string Marker);
    private sealed record RuleInspection(string[] Facts, int Round, RuleTraceRecord[] Trace, bool LeastFixedPointReached);
    private sealed record RuleTraceRecord(int Round, string[] FiredRuleIds, string[] AddedFacts);
    private sealed record CancellationEvidence(string ExecutionId, string WorkStatus, string LateResultStatus,
        bool RemainsUnclaimable, int AppliedResults, ModelInspection Inspection);
    private sealed record PackageSwapEvidence(string OriginalPackageSha256, string AlternatePackageSha256,
        string PersistedExecutionPinFingerprint, bool RejectedAtResume, string Rejection);
    private sealed record ProofReport(string Kind, string ModelId, string ModelVersion, string StateSchema,
        string ArtifactSha256, string PackageId, string PackageVersion, string PackageSha256, string PolicyId,
        string ExecutionPinFingerprint, string ExecutionId, ModelInspection FinalInspection, RuleInspection RuleTrace,
        CancellationEvidence Cancellation, PackageSwapEvidence PackageSwap,
        IReadOnlyList<ProofAssertion> Assertions, IReadOnlyList<ChildEvidence> ChildProcesses);
}
