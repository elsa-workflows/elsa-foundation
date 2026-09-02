using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>
/// Executes the recovery-scan v1.2 successor through the public recovery scanner and state stores.
/// Provider setup, schema admission, and the 2,048-row fixture are outside measured operations.
/// </summary>
public sealed class RuntimeRecoveryScanWorkload
{
    private static readonly ReproducibleWorkloadScenario Scenario = ReproducibleWorkloadScenarioCatalog.Get(WorkloadId);
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public const string WorkloadId = "recovery-scan";
    public const string ExpectedInputFingerprint = "eb4df814e208fedf12c3f8a995430b1084fac5cf7b7e67bd0464be07d0043eef";
    public const string ExpectedResultDigest = "af331fc39ac89be97b601ba9e472fd7872b45ec5e50ccc9bba6b55de53e3aba0";
    public static string ScenarioId => Scenario.ScenarioId;
    public static string Version => Scenario.Version;
    public static string Seed => Scenario.Seed;
    public static IReadOnlyList<string> NativeRouteIdentities { get; } =
    [
        "list-recovery-detected",
        "list-recovery-by-lease-expiry",
        "list-recovery-by-lease-acquisition",
        "list-recovery-by-heartbeat"
    ];
    public static int ExecutionCount => Int("executionCount");
    public static DateTimeOffset FixedNowUtc => DateTimeOffset.Parse(String("fixedNowUtc"), CultureInfo.InvariantCulture);
    public static int LiveExecutions => Int("liveExecutions");
    public static int PageSize => Int("pageSize");
    public static int RecoverableCandidates => Int("recoverableCandidates");
    // Terminal rows are explicit workflow-state rows with due recovery signals. They exercise correlation's
    // terminal exclusion while keeping the v1.2 execution and liveness cardinalities truthful.
    public static int TerminalExecutions => Int("terminalExecutions");

    public async ValueTask<RuntimeRecoveryScanResult> ExecuteAsync(
        IRuntimeRecoveryScanWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        var client = await adapter.OpenClientAsync(cancellationToken);
        RequireClient(client);

        var operations = new List<string>();
        await SeedAsync(client, adapter.PersistenceScope, cancellationToken);
        await RequireTerminalRowsAsync(client.Executions, client.Liveness, adapter.PersistenceScope, cancellationToken);
        operations.Add(scenario.OperationSequence[0]);

        var first = await ScanAllPagesAsync(client.Scanner, cancellationToken);
        RequireCandidates(first, "initial recovery scan");
        operations.Add(scenario.OperationSequence[1]);

        var reopened = await adapter.ReopenClientAsync(cancellationToken);
        RequireClient(reopened);
        if (ReferenceEquals(client.Scanner, reopened.Scanner))
            throw new InvalidOperationException("Recovery scan reopen must provide a distinct public scanner client.");
        var reopenedCandidates = await ScanAllPagesAsync(reopened.Scanner, cancellationToken);
        RequireCandidates(reopenedCandidates, "reopened recovery scan");
        if (!first.Select(Identity).SequenceEqual(reopenedCandidates.Select(Identity), StringComparer.Ordinal))
            throw new InvalidOperationException("The reopened recovery scan did not preserve candidate identity and order.");
        operations.Add(scenario.OperationSequence[2]);

        var bounded = await client.Scanner.ScanPageAsync(Request(PageSize), cancellationToken);
        RequireBoundedPage(bounded, "bounded recovery verification");
        if (bounded.Items.Any(candidate => !candidate.WorkflowExecutionId.StartsWith("recovery-candidate-", StringComparison.Ordinal)))
            throw new InvalidOperationException("The bounded recovery page exposed a live or terminal execution.");
        operations.Add(scenario.OperationSequence[3]);

        if (!operations.SequenceEqual(scenario.OperationSequence, StringComparer.Ordinal))
            throw new InvalidOperationException("The recovery-scan operation sequence no longer matches the catalog contract.");

        var actualObservations = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["candidateIdentityDigest"] = Hash(JsonSerializer.Serialize(first.Select(candidate => candidate.WorkflowExecutionId), CanonicalJsonOptions)),
            ["firstPageCount"] = bounded.Items.Count,
            ["liveExecutionResultCount"] = bounded.Items.Count(candidate => candidate.WorkflowExecutionId.StartsWith("live-", StringComparison.Ordinal)),
            ["reopenedCandidateIdentityDigest"] = Hash(JsonSerializer.Serialize(reopenedCandidates.Select(candidate => candidate.WorkflowExecutionId), CanonicalJsonOptions)),
            ["scanNowUtc"] = FixedNowUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };
        if (!ObservationsMatch(actualObservations, scenario.CreateExpectedObservations()))
            throw new InvalidOperationException("The recovery-scan observable results no longer match the catalog contract.");

        var resultDigest = ReproducibleWorkloadScenarioCatalog.Hash(ReproducibleWorkloadScenarioCatalog.Serialize(new
        {
            WorkloadId,
            scenario.ScenarioId,
            InputFingerprint = scenario.ComputeInputFingerprint(),
            Operations = operations,
            ObservableResults = actualObservations
        }));
        if (resultDigest != ExpectedResultDigest)
            throw new InvalidOperationException("The recovery-scan observable result digest no longer matches its ratified value.");

        return new RuntimeRecoveryScanResult(scenario.ComputeInputFingerprint(), resultDigest, operations, actualObservations);
    }

    /// <summary>Prepares three bounded scanner calls. All fixture writes and full correctness traversal are untimed.</summary>
    public async ValueTask<IReadOnlyList<IRuntimeRecoveryScanWorkloadOperation>> PrepareMeasuredOperationsAsync(
        IRuntimeRecoveryScanWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        if (!scenario.OperationSequence.SequenceEqual(
                ["seed-live-and-recoverable-state", "scan-recovery-candidates", "reopen-and-rescan", "verify-bounded-order-and-non-candidates"],
                StringComparer.Ordinal))
            throw new InvalidOperationException("The recovery-scan scenario operation sequence no longer matches the measured contract.");

        var client = await adapter.OpenClientAsync(cancellationToken);
        var reopened = await adapter.ReopenClientAsync(cancellationToken);
        RequireClient(client);
        RequireClient(reopened);
        if (ReferenceEquals(client.Scanner, reopened.Scanner))
            throw new InvalidOperationException("Recovery measured operations require independent scanner instances.");

        // Correctness has already seeded this exact fixture. Probe once outside timing to ensure the adapter is
        // bound to the production paging implementation and to retain the opaque continuation for the second page.
        var probe = await client.Scanner.ScanPageAsync(Request(PageSize), cancellationToken);
        RequireBoundedPage(probe, "measured recovery setup");
        var continuation = probe.NextContinuationToken
                           ?? throw new InvalidOperationException("The measured recovery setup did not return a continuation.");

        return
        [
            new RuntimeRecoveryScanWorkloadOperation(
                scenario.OperationSequence[1],
                (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var page = await client.Scanner.ScanPageAsync(Request(PageSize), token);
                    RequireBoundedPage(page, "measured recovery scan");
                }),
            new RuntimeRecoveryScanWorkloadOperation(
                scenario.OperationSequence[2],
                (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var page = await reopened.Scanner.ScanPageAsync(Request(PageSize), token);
                    RequireBoundedPage(page, "measured reopened recovery scan");
                }),
            new RuntimeRecoveryScanWorkloadOperation(
                scenario.OperationSequence[3],
                (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var page = await client.Scanner.ScanPageAsync(
                        Request(PageSize, continuation),
                        token);
                    RequireExpectedPage(page, PageSize, "measured recovery continuation");
                    if (page.Items.Any(candidate => candidate.WorkflowExecutionId.StartsWith("live-", StringComparison.Ordinal)))
                        throw new InvalidOperationException("The measured recovery continuation exposed a live execution.");
                })
        ];
    }

    private async ValueTask SeedAsync(
        RuntimeRecoveryScanClient client,
        string persistenceScope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistenceScope);
        var terminalStart = RecoverableCandidates;
        var recoverySignalCount = RecoverableCandidates + TerminalExecutions;
        for (var index = 0; index < ExecutionCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isCandidate = index < recoverySignalCount;
            var isTerminal = index >= terminalStart && index < recoverySignalCount;
            var workflowId = index < RecoverableCandidates
                ? CandidateId(index)
                : isTerminal
                    ? TerminalId(index - terminalStart)
                    : LiveId(index - recoverySignalCount);
            await client.Executions.SaveAsync(
                WorkflowState(
                    workflowId,
                    isTerminal ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running,
                    persistenceScope),
                cancellationToken);
            await client.Liveness.SaveAsync(LivenessState(index, workflowId, isCandidate), cancellationToken);

            if (index < RecoverableCandidates)
            {
                await client.Incidents.SaveAsync(new IncidentState(
                    $"incident-{index:D4}", workflowId, null, null, IncidentSeverity.Warning, IncidentStatus.Open,
                    null, "recovery-scan", "recovery scan fixture", FixedNowUtc.AddMinutes(-1), null), cancellationToken);
                await client.Scheduler.SaveAsync(new SchedulerState(workflowId, 1), cancellationToken);
                var hold = new WorkflowHold(
                    $"released-hold-{index:D4}", WorkflowHoldScope.WorkflowExecution, WorkflowHoldStatus.Released,
                    FixedNowUtc.AddMinutes(-2), "recovery-scan", "released fixture hold", workflowId,
                    releasedAt: FixedNowUtc.AddMinutes(-1), releasedBy: "recovery-scan");
                await client.Holds.SaveAsync(new WorkflowHoldState($"hold-state-{index:D4}", workflowId, releasedHolds: [hold]), cancellationToken);
            }
        }

    }

    private static async ValueTask RequireTerminalRowsAsync(
        IWorkflowExecutionStateStore executions,
        IExecutionLivenessStateStore liveness,
        string persistenceScope,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < TerminalExecutions; index++)
        {
            var state = await executions.FindAsync(TerminalId(index), cancellationToken);
            if (state is null || !state.Status.IsTerminal() || state.TenantId != persistenceScope)
                throw new InvalidOperationException("The recovery fixture did not seed every explicit terminal execution row.");
        }

        var executionRows = await executions.ListAsync(cancellationToken);
        if (executionRows.Count != ExecutionCount ||
            TerminalExecutions != executionRows.Count(state => state.WorkflowExecutionId.StartsWith("terminal-", StringComparison.Ordinal)) ||
            LiveExecutions != executionRows.Count(state => state.WorkflowExecutionId.StartsWith("live-", StringComparison.Ordinal)))
            throw new InvalidOperationException("The recovery fixture did not preserve its exact execution-state cardinalities.");

        var livenessRows = await liveness.ListAllAsync(cancellationToken);
        if (livenessRows.Count != ExecutionCount ||
            TerminalExecutions != livenessRows.Count(state => state.WorkflowExecutionId.StartsWith("terminal-", StringComparison.Ordinal)))
            throw new InvalidOperationException("The recovery fixture did not seed every terminal execution recovery signal.");
    }

    private static ExecutionLivenessState LivenessState(int index, string workflowId, bool candidate)
    {
        if (!candidate)
        {
            var owner = "live-worker";
            return new ExecutionLivenessState(
                $"operational-{index:D4}", workflowId,
                new RuntimeExecutionLease($"lease-{index:D4}", workflowId, owner, FixedNowUtc.AddMinutes(-1), FixedNowUtc.AddHours(1), 1),
                new RuntimeHeartbeat($"heartbeat-{index:D4}", workflowId, owner, $"lease-{index:D4}", FixedNowUtc), null, null);
        }

        return (index % 4) switch
        {
            0 => new ExecutionLivenessState(
                $"operational-{index:D4}", workflowId, null, null, null,
                new InterruptedExecutionState($"interruption-{index:D4}", workflowId, null, $"checkpoint-{index:D4}", RuntimeInterruptionReason.HostStopped, RuntimeInterruptionStatus.Detected, FixedNowUtc.AddMinutes(-1))),
            1 => new ExecutionLivenessState(
                $"operational-{index:D4}", workflowId,
                new RuntimeExecutionLease($"lease-{index:D4}", workflowId, "recovery-worker", FixedNowUtc.AddMinutes(-2), FixedNowUtc.AddMinutes(-1), 1), null, null, null),
            2 => new ExecutionLivenessState(
                $"operational-{index:D4}", workflowId,
                new RuntimeExecutionLease($"lease-{index:D4}", workflowId, "recovery-worker", FixedNowUtc.AddMinutes(-6), FixedNowUtc.AddMinutes(10), 1), null, null, null),
            _ => new ExecutionLivenessState(
                $"operational-{index:D4}", workflowId, null,
                new RuntimeHeartbeat($"heartbeat-{index:D4}", workflowId, "recovery-worker", null, FixedNowUtc.AddMinutes(-2)), null, null)
        };
    }

    private static WorkflowExecutionState WorkflowState(
        string workflowId,
        WorkflowExecutionStatus status,
        string persistenceScope) =>
        new(workflowId, new WorkflowExecutableIdentity("recovery-artifact", "recovery-definition", "v1", "1.0.0", "recovery-hash"),
            status, null, FixedNowUtc.AddDays(-1), FixedNowUtc.AddDays(-1), FixedNowUtc.AddMinutes(-1), null, null, null,
            persistenceScope, new Dictionary<string, string>());

    private static async ValueTask<IReadOnlyList<RuntimeRecoveryCandidate>> ScanAllPagesAsync(
        IRuntimeRecoveryScanner scanner,
        CancellationToken cancellationToken)
    {
        if (scanner is not IRuntimeRecoveryPagedScanner { SupportsPaging: true } paged)
            throw new NotSupportedException("The recovery workload requires a scanner with complete bounded paging.");

        var items = new List<RuntimeRecoveryCandidate>(RecoverableCandidates);
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        var maximumPages = (RecoverableCandidates + PageSize - 1) / PageSize + 1;
        var pageCount = 0;
        string? continuation = null;
        do
        {
            if (++pageCount > maximumPages)
                throw new InvalidOperationException("The paged recovery scan exceeded the frozen bounded page budget.");
            var page = await paged.ScanPageAsync(Request(PageSize, continuation), cancellationToken);
            if (page.Items.Count == 0 || page.Items.Count > PageSize)
                throw new InvalidOperationException("The paged recovery scan returned an invalid bounded page.");
            items.AddRange(page.Items);
            continuation = page.NextContinuationToken;
            if (continuation is not null && !seenContinuations.Add(continuation))
                throw new InvalidOperationException("The paged recovery scan returned a repeated continuation token.");
        } while (continuation is not null);
        return items;
    }

    private static void RequireCandidates(IReadOnlyList<RuntimeRecoveryCandidate> candidates, string operation)
    {
        RequireNoLiveOrTerminalCandidates(candidates, operation);
        if (candidates.Count != RecoverableCandidates ||
            !candidates.Select(Identity).SequenceEqual(Enumerable.Range(0, RecoverableCandidates).Select(index => CandidateId(index) + "/operational-" + index.ToString("D4", CultureInfo.InvariantCulture)), StringComparer.Ordinal))
            throw new InvalidOperationException($"The {operation} did not return the exact ordered recovery candidate population.");
    }

    private static void RequireBoundedPage(RuntimeRecoveryPage page, string operation)
    {
        RequireExpectedPage(page, 0, operation);
    }

    private static void RequireExpectedPage(RuntimeRecoveryPage page, int startIndex, string operation)
    {
        RequireNoLiveOrTerminalCandidates(page.Items, operation);
        var expected = Enumerable.Range(startIndex, PageSize)
            .Select(index => $"{CandidateId(index)}/operational-{index:D4}");
        if (page.Items.Count != PageSize ||
            !page.Items.Select(Identity).SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidOperationException($"The {operation} did not return the frozen bounded recovery page.");
    }

    private static void RequireNoLiveOrTerminalCandidates(
        IEnumerable<RuntimeRecoveryCandidate> candidates,
        string operation)
    {
        if (candidates.Any(candidate =>
                candidate.WorkflowExecutionId.StartsWith("live-", StringComparison.Ordinal) ||
                candidate.WorkflowExecutionId.StartsWith("terminal-", StringComparison.Ordinal)))
            throw new InvalidOperationException($"The {operation} exposed a live or terminal execution.");
    }

    private static RuntimeRecoveryScanRequest Request(int limit, string? continuation = null) =>
        new(FixedNowUtc, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), limit, continuationToken: continuation);

    private static ReproducibleWorkloadScenario ValidateScenario()
    {
        if (Scenario.Version != "1.2.0" || Scenario.ScenarioId != "runtime-recovery-scan" || Scenario.Seed != "spec094-recovery-scan-v1.2" ||
            Scenario.ComputeInputFingerprint() != ExpectedInputFingerprint || Scenario.ComputeResultDigest() != ExpectedResultDigest ||
            !ReproducibleWorkloadScenarioCatalog.GoldenVectors.TryGetValue(WorkloadId, out var golden) ||
            golden.InputFingerprint != ExpectedInputFingerprint || golden.ResultDigest != ExpectedResultDigest ||
            ExecutionCount != 2048 || RecoverableCandidates != 173 || LiveExecutions != 1867 || TerminalExecutions != 8 || PageSize != 4 ||
            FixedNowUtc != new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero))
            throw new InvalidOperationException("The recovery-scan scenario no longer matches its ratified v1.2 successor contract.");
        return Scenario;
    }

    private static void RequireClient(RuntimeRecoveryScanClient? client)
    {
        if (client is null || client.Scanner is null || client.Liveness is null || client.Executions is null ||
            client.Incidents is null || client.Scheduler is null || client.Holds is null)
            throw new InvalidOperationException("The recovery workload adapter must expose all public runtime state stores.");
    }

    private static string Identity(RuntimeRecoveryCandidate candidate) =>
        $"{candidate.WorkflowExecutionId}/{candidate.OperationalStateId}";

    private static string CandidateId(int index) => $"recovery-candidate-{index:D4}";
    private static string TerminalId(int index) => $"terminal-{index:D4}";
    private static string LiveId(int index) => $"live-{index:D4}";
    private static int Int(string name) => (int)Scenario.Parameters[name];
    private static string String(string name) => (string)Scenario.Parameters[name];
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool ObservationsMatch(IReadOnlyDictionary<string, object> actual, IReadOnlyDictionary<string, object> expected) =>
        actual.Count == expected.Count && actual.All(pair => expected.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value));
}

public interface IRuntimeRecoveryScanWorkloadAdapter
{
    string PersistenceScope { get; }
    ValueTask<RuntimeRecoveryScanClient> OpenClientAsync(CancellationToken cancellationToken = default);
    ValueTask<RuntimeRecoveryScanClient> ReopenClientAsync(CancellationToken cancellationToken = default);
}

public sealed record RuntimeRecoveryScanClient(
    IRuntimeRecoveryScanner Scanner,
    IExecutionLivenessStateStore Liveness,
    IWorkflowExecutionStateStore Executions,
    IIncidentStateStore Incidents,
    ISchedulerStateStore Scheduler,
    IWorkflowHoldStateStore Holds);

public interface IRuntimeRecoveryScanWorkloadOperation
{
    string Id { get; }
    ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default);
    ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default);
}

internal sealed class RuntimeRecoveryScanWorkloadOperation(
    string id,
    Func<long, CancellationToken, ValueTask> prepare,
    Func<long, CancellationToken, ValueTask> invoke) : IRuntimeRecoveryScanWorkloadOperation
{
    public string Id { get; } = id;
    public ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default) => prepare(invocation, cancellationToken);
    public ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default) => invoke(invocation, cancellationToken);
}

public sealed record RuntimeRecoveryScanResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, object> ObservableResults);
