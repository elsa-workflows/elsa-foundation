using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>
/// Executes the catalog-owned durable diagnostics-history correctness baseline through the public
/// <see cref="IStructuredLogStore"/> and <see cref="IOpenTelemetryStore"/> contracts only.
/// </summary>
/// <remarks>
/// <para>
/// This runner is deliberately implementation-blind: it never names EF Core or Groundwork. The adapter
/// supplies whichever pair of public stores the measured target selected, so the identical operation
/// sequence and the identical asserted observations run against both stacks. That is what makes the
/// SQLite EF-vs-Groundwork comparison a differential rather than two unrelated runs.
/// </para>
/// <para>
/// Both stores queue <c>AppendAsync</c>/<c>WriteAsync</c> onto a bounded background drain, so every read
/// in this sequence is preceded by <see cref="IDiagnosticsDurableHistoryWorkloadAdapter.FlushAsync"/>.
/// Reading without flushing would measure drain scheduling instead of storage and would make the exact
/// retained counts below non-deterministic.
/// </para>
/// </remarks>
public sealed class DiagnosticsDurableHistoryWorkload
{
    private static readonly ReproducibleWorkloadScenario Scenario = ReproducibleWorkloadScenarioCatalog.Get(WorkloadId);
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public const string WorkloadId = "diagnostics-durable-history";
    public const string ExpectedInputFingerprint = "696a866f11365bfaca621328987b04d8166bf5c84a255584278669dc3909debd";
    public const string ExpectedResultDigest = "f8e8245c588a12aad79796432219c8450f26a2e90d290ceb82e06bf81c2aec77";

    public static string ScenarioId => Scenario.ScenarioId;
    public static string Version => Scenario.Version;
    public static string Seed => Scenario.Seed;

    public static int ConcurrentWriters => Int("concurrentWriters");
    public static int InstrumentCount => Int("instrumentCount");
    public static int NormalizedRecordsPerOtlpBatch => Int("normalizedRecordsPerOtlpBatch");
    public static int PayloadBytes => Int("payloadBytes");
    public static int QueryLimit => Int("queryLimit");
    public static int ResourceCount => Int("resourceCount");
    public static int RetainedRecordsPerStream => Int("retainedRecordsPerStream");
    public static int RetentionOverflowRecords => Int("retentionOverflowRecords");
    public static int AppendedRecordsPerStream => RetainedRecordsPerStream + RetentionOverflowRecords;
    public static int StructuredLogBatchSize => Int("structuredLogBatchSize");
    public static int TenantCount => Int("tenantCount");
    public static int AppendedRecordsPerOtlpBatch =>
        (RetainedRecordsPerStream + RetentionOverflowRecords + NormalizedRecordsPerOtlpBatch - 1) /
        NormalizedRecordsPerOtlpBatch;
    /// <summary>
    /// The route-specific physical cardinalities used by native-plan admission. These are observations
    /// of the frozen fixture, not optimizer claims. Routes without an explicit provider-native index are
    /// retained in the contract as blocked identities rather than being omitted from admission.
    /// </summary>
    public static IReadOnlyDictionary<string, int> NativeRouteCardinalities { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["resources-by-last-seen"] = ResourceCount,
            ["resources-by-status"] = ResourceCount,
            ["resources-by-service"] = ResourceCount,
            ["traces-by-last-seen"] = RetainedRecordsPerStream,
            ["trace-detail"] = RetainedRecordsPerStream,
            ["metrics-by-last-seen"] = RetainedRecordsPerStream,
            ["logs-by-last-seen"] = RetainedRecordsPerStream,
            ["structured-log-recent"] = AppendedRecordsPerStream,
            ["structured-log-replay"] = AppendedRecordsPerStream
        };

    public static IReadOnlyDictionary<string, int> NativeRouteLimits { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["resources-by-last-seen"] = QueryLimit,
            ["resources-by-status"] = QueryLimit,
            ["resources-by-service"] = QueryLimit,
            ["traces-by-last-seen"] = QueryLimit,
            ["trace-detail"] = 1,
            ["metrics-by-last-seen"] = QueryLimit,
            ["logs-by-last-seen"] = QueryLimit,
            ["structured-log-recent"] = QueryLimit,
            ["structured-log-replay"] = QueryLimit
        };
    public static DateTimeOffset FixedNowUtc => DateTimeOffset.Parse((string)Scenario.Parameters["fixedNowUtc"], null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

    /// <summary>Total appended per stream: the retained capacity plus the deliberate retention overflow.</summary>
    private static int AppendedPerStream => AppendedRecordsPerStream;

    private const string SeedCrossScopeDiagnosticHistory = "seed-cross-scope-diagnostic-history";
    private const string AppendStructuredLogBatches = "append-structured-log-batches";
    private const string ReadStructuredLogRecent = "read-structured-log-recent";
    private const string ResumeStructuredLogHistory = "resume-structured-log-history";
    private const string ReopenAndReadStructuredLogHighWater = "reopen-and-read-structured-log-high-water";
    private const string AppendOpenTelemetryBatches = "append-open-telemetry-batches";
    private const string QueryOpenTelemetryResources = "query-open-telemetry-resources";
    private const string QueryOpenTelemetryTraces = "query-open-telemetry-traces";
    private const string ReadOpenTelemetryTraceDetail = "read-open-telemetry-trace-detail";
    private const string QueryOpenTelemetryMetrics = "query-open-telemetry-metrics";
    private const string QueryOpenTelemetryLogs = "query-open-telemetry-logs";
    private const string InspectExactStreamCounts = "inspect-exact-stream-counts";
    private const string TrimDiagnosticStreams = "trim-diagnostic-streams";
    private const string ReopenAndVerifyDurableHistory = "reopen-and-verify-durable-history";
    private const string VerifyCrossScopeIsolation = "verify-cross-scope-isolation";

    /// <summary>
    /// The measured phase ids, in frozen catalog order. The harness stamps these onto every sample, so a
    /// divergence would silently retarget a budget row at a different operation.
    /// </summary>
    public static readonly string[] OperationIds =
    [
        SeedCrossScopeDiagnosticHistory,
        AppendStructuredLogBatches,
        ReadStructuredLogRecent,
        ResumeStructuredLogHistory,
        ReopenAndReadStructuredLogHighWater,
        AppendOpenTelemetryBatches,
        QueryOpenTelemetryResources,
        QueryOpenTelemetryTraces,
        ReadOpenTelemetryTraceDetail,
        QueryOpenTelemetryMetrics,
        QueryOpenTelemetryLogs,
        InspectExactStreamCounts,
        TrimDiagnosticStreams,
        ReopenAndVerifyDurableHistory,
        VerifyCrossScopeIsolation
    ];

    public async ValueTask<DiagnosticsDurableHistoryWorkloadResult> ExecuteAsync(
        IDiagnosticsDurableHistoryWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        var scopes = await adapter.OpenScopedClientsAsync(cancellationToken);
        RequireDistinctScopes(scopes);

        var operations = new List<string>();

        // 1. Seed the SECOND tenant scope. Every later assertion reads the primary scope only, so any
        //    leakage of these records shows up as a non-zero crossScopeResultCount at the end rather than
        //    as a silently inflated count in the middle.
        await SeedSecondaryScopeAsync(scopes.Secondary, cancellationToken);
        await adapter.FlushAsync(cancellationToken);
        operations.Add(SeedCrossScopeDiagnosticHistory);

        // 2. Append the retained capacity plus the deliberate overflow, across the frozen writer count.
        //    The overflow is what makes the later explicit trim observable: without it, TrimAsync would be
        //    a no-op and trimmedRecordsPerStream could not be distinguished from zero.
        var maximumCommittedSequence = await AppendStructuredLogsAsync(scopes.Primary, cancellationToken);
        await adapter.FlushAsync(cancellationToken);
        var highWaterAfterAppend = await scopes.Primary.StructuredLogs.GetHighWaterMarkAsync(cancellationToken);
        if (highWaterAfterAppend != maximumCommittedSequence)
            throw new InvalidOperationException(
                $"The structured-log high-water {highWaterAfterAppend} did not match the maximum committed sequence {maximumCommittedSequence}.");
        operations.Add(AppendStructuredLogBatches);

        // 3. Bounded newest window, clamped by the caller's MaxCount.
        var recent = await scopes.Primary.StructuredLogs.GetRecentAsync(
            new StructuredLogFilter { MaxCount = QueryLimit },
            cancellationToken);
        var structuredLogRecentCount = recent.Count;
        if (structuredLogRecentCount != QueryLimit)
            throw new InvalidOperationException("The structured-log recent window did not honour the frozen query limit.");
        operations.Add(ReadStructuredLogRecent);

        // 4. Durable-tail resume from the oldest retained position. NextCursor must advance across every
        //    scanned position, so a gap-free page of exactly the query limit is the contract.
        var replay = await scopes.Primary.StructuredLogs.ReadAfterAsync(
            afterCursor: null,
            filter: StructuredLogFilter.None,
            maxCount: QueryLimit,
            cancellationToken);
        var structuredLogReplayCount = replay.Entries.Count;
        if (structuredLogReplayCount != QueryLimit || replay.NextCursor is null || !replay.HasMore)
            throw new InvalidOperationException(
                "The structured-log resume page did not return a bounded, continuable oldest-first window.");
        operations.Add(ResumeStructuredLogHistory);

        // 5. A genuinely distinct client must observe the same lifetime high-water. This is the durability
        //    assertion, not a cache check, so it must not reuse the primary client.
        var reopenedForHighWater = await adapter.ReopenClientAsync(cancellationToken);
        RequireDistinctClient(reopenedForHighWater, scopes.Primary);
        var reopenedHighWater = await reopenedForHighWater.StructuredLogs.GetHighWaterMarkAsync(cancellationToken);
        if (reopenedHighWater != highWaterAfterAppend)
            throw new InvalidOperationException("A reopened client did not observe the committed structured-log high-water.");
        operations.Add(ReopenAndReadStructuredLogHighWater);

        // 6. Fan the normalized OTLP batches into the four record streams. Each stream receives exactly the
        //    retained capacity plus the overflow, so provider-side capacity retention has something to
        //    evict and the inspected counts below are exact rather than approximate.
        await AppendOpenTelemetryAsync(scopes.Primary, cancellationToken);
        await adapter.FlushAsync(cancellationToken);
        operations.Add(AppendOpenTelemetryBatches);

        // 7-11. Every declared read shape, with resource route pages strictly below their candidate
        // population so a provider cannot satisfy native admission with an unbounded full-population page.
        var resources = await scopes.Primary.OpenTelemetry.QueryResourcesAsync(
            new OpenTelemetryResourceFilter { Take = NativeRouteLimits["resources-by-last-seen"] },
            cancellationToken);
        if (resources.Items.Count != NativeRouteLimits["resources-by-last-seen"])
            throw new InvalidOperationException(
                $"The resource last-seen route exposed {resources.Items.Count} resources; the frozen bounded route requires {NativeRouteLimits["resources-by-last-seen"]}.");
        var resourcesByStatus = await scopes.Primary.OpenTelemetry.QueryResourcesAsync(
            new OpenTelemetryResourceFilter
            {
                Status = TelemetryResourceStatus.Active,
                Take = NativeRouteLimits["resources-by-status"]
            },
            cancellationToken);
        if (resourcesByStatus.Items.Count != NativeRouteLimits["resources-by-status"])
            throw new InvalidOperationException("The resource status route did not return its bounded active-resource page.");
        var resourcesByService = await scopes.Primary.OpenTelemetry.QueryResourcesAsync(
            new OpenTelemetryResourceFilter
            {
                ServiceName = ServiceNameFor(0),
                Take = NativeRouteLimits["resources-by-service"]
            },
            cancellationToken);
        if (resourcesByService.Items.Count != NativeRouteLimits["resources-by-service"])
            throw new InvalidOperationException("The resource service route did not return its bounded service page.");
        operations.Add(QueryOpenTelemetryResources);

        var traces = await scopes.Primary.OpenTelemetry.QueryTracesAsync(
            new OpenTelemetryTraceFilter { Take = QueryLimit },
            cancellationToken);
        if (traces.Items.Count == 0 || traces.Items.Count > QueryLimit)
            throw new InvalidOperationException("The grouped trace window did not return a bounded non-empty page.");
        operations.Add(QueryOpenTelemetryTraces);

        var newestTraceId = traces.Items.OrderByDescending(item => item.StartTime).ThenBy(item => item.TraceId, StringComparer.Ordinal).First().TraceId;
        var detail = await scopes.Primary.OpenTelemetry.GetTraceAsync(newestTraceId, cancellationToken);
        if (detail is null || !StringComparer.OrdinalIgnoreCase.Equals(detail.Trace.TraceId, newestTraceId) || detail.Spans.Count == 0)
            throw new InvalidOperationException("The trace detail did not expose the requested trace with its ordered spans.");
        operations.Add(ReadOpenTelemetryTraceDetail);

        var metrics = await scopes.Primary.OpenTelemetry.QueryMetricsAsync(
            new OpenTelemetryMetricFilter { Take = QueryLimit },
            cancellationToken);
        if (metrics.Points.Count != QueryLimit || metrics.Instruments.Count == 0)
            throw new InvalidOperationException("The metric window did not return the frozen bounded page with its referenced instruments.");
        operations.Add(QueryOpenTelemetryMetrics);

        var logs = await scopes.Primary.OpenTelemetry.QueryLogsAsync(
            new OpenTelemetryLogFilter { Take = QueryLimit },
            cancellationToken);
        if (logs.Items.Count != QueryLimit)
            throw new InvalidOperationException("The telemetry-log window did not honour the frozen query limit.");
        operations.Add(QueryOpenTelemetryLogs);

        // 12. Exact counts, not approximations. Provider capacity retention has already evicted the
        //     overflow from each of the four streams, so each retains exactly its configured capacity.
        var diagnostics = await scopes.Primary.OpenTelemetry.GetDiagnosticsAsync(cancellationToken);
        var openTelemetryRetainedCounts = new[]
        {
            diagnostics.TraceCount,
            diagnostics.SpanCount,
            diagnostics.MetricPointCount,
            diagnostics.LogRecordCount
        };
        if (openTelemetryRetainedCounts.Any(count => count != RetainedRecordsPerStream))
            throw new InvalidOperationException(
                $"The inspected stream counts [{string.Join(", ", openTelemetryRetainedCounts)}] do not each equal the frozen retained capacity {RetainedRecordsPerStream}.");
        var diagnosticDropCount =
            diagnostics.DroppedTraceCount +
            diagnostics.DroppedSpanCount +
            diagnostics.DroppedMetricPointCount +
            diagnostics.DroppedLogRecordCount;
        if (diagnosticDropCount != 0)
            throw new InvalidOperationException(
                $"The diagnostics capture path shed {diagnosticDropCount} records; the frozen contract admits no unexplained loss.");
        if (diagnostics.ResourceCount != ResourceCount || diagnostics.MetricInstrumentCount != InstrumentCount)
            throw new InvalidOperationException("The inspected catalog counts do not match the frozen resource/instrument contract.");
        operations.Add(InspectExactStreamCounts);

        // 13. Exact KeepNewest retention on the structured-log stream, and the lifetime high-water must
        //     survive it. A trim that rewinds the high-water would let a restart reuse a committed
        //     sequence, which is the failure this assertion exists to catch.
        await scopes.Primary.StructuredLogs.TrimAsync(RetainedRecordsPerStream, cancellationToken);
        var retainedAfterTrim = await CountRetainedAsync(scopes.Primary.StructuredLogs, cancellationToken);
        var trimmedRecordsPerStream = AppendedPerStream - retainedAfterTrim;
        if (retainedAfterTrim != RetainedRecordsPerStream || trimmedRecordsPerStream != RetentionOverflowRecords)
            throw new InvalidOperationException(
                $"Exact KeepNewest retention left {retainedAfterTrim} entries and trimmed {trimmedRecordsPerStream}; the frozen contract requires {RetainedRecordsPerStream} and {RetentionOverflowRecords}.");
        var highWaterAfterTrim = await scopes.Primary.StructuredLogs.GetHighWaterMarkAsync(cancellationToken);
        if (highWaterAfterTrim != highWaterAfterAppend)
            throw new InvalidOperationException("Retention rewound the lifetime structured-log high-water.");
        operations.Add(TrimDiagnosticStreams);

        // 14. Restart observation across a genuinely new client, after retention.
        var reopened = await adapter.ReopenClientAsync(cancellationToken);
        RequireDistinctClient(reopened, scopes.Primary);
        RequireDistinctClient(reopened, reopenedForHighWater);
        var restartHighWater = await reopened.StructuredLogs.GetHighWaterMarkAsync(cancellationToken);
        var restartRetained = await CountRetainedAsync(reopened.StructuredLogs, cancellationToken);
        var restartDiagnostics = await reopened.OpenTelemetry.GetDiagnosticsAsync(cancellationToken);
        var restartStateMatched =
            restartHighWater == highWaterAfterAppend &&
            restartRetained == RetainedRecordsPerStream &&
            restartDiagnostics.TraceCount == diagnostics.TraceCount &&
            restartDiagnostics.SpanCount == diagnostics.SpanCount &&
            restartDiagnostics.MetricPointCount == diagnostics.MetricPointCount &&
            restartDiagnostics.LogRecordCount == diagnostics.LogRecordCount &&
            restartDiagnostics.ResourceCount == diagnostics.ResourceCount &&
            restartDiagnostics.MetricInstrumentCount == diagnostics.MetricInstrumentCount;
        if (!restartStateMatched)
            throw new InvalidOperationException("The reopened client did not observe the committed durable diagnostics history.");
        operations.Add(ReopenAndVerifyDurableHistory);

        // 15. Scope isolation. The secondary scope's seeded records carry a category and service name that
        //     appear nowhere in the primary scope, so any match here is genuine leakage.
        var crossScopeResultCount = await CountCrossScopeLeakageAsync(scopes.Primary, cancellationToken);
        if (crossScopeResultCount != 0)
            throw new InvalidOperationException(
                $"{crossScopeResultCount} records from another storage scope were visible to the primary scope.");
        operations.Add(VerifyCrossScopeIsolation);

        if (!operations.SequenceEqual(scenario.OperationSequence, StringComparer.Ordinal))
            throw new InvalidOperationException("The diagnostics workload operation order no longer matches the catalog contract.");

        var actualObservations = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["crossScopeResultCount"] = crossScopeResultCount,
            ["diagnosticDropCount"] = checked((int)diagnosticDropCount),
            ["instrumentCount"] = diagnostics.MetricInstrumentCount,
            ["logWindow"] = new[] { logs.Items.First().Id, logs.Items.Last().Id },
            ["metricWindow"] = new[] { metrics.Points.First().Id, metrics.Points.Last().Id },
            ["openTelemetryRetainedCounts"] = openTelemetryRetainedCounts,
            ["resourceCount"] = diagnostics.ResourceCount,
            ["restartStateMatched"] = restartStateMatched,
            ["structuredLogHighWaterMatchedMaximumCommittedSequence"] = highWaterAfterTrim == maximumCommittedSequence,
            ["structuredLogRecentCount"] = structuredLogRecentCount,
            ["structuredLogReplayCount"] = structuredLogReplayCount,
            ["structuredLogRetainedCount"] = retainedAfterTrim,
            ["traceWindow"] = new[] { traces.Items.First().TraceId, traces.Items.Last().TraceId },
            ["trimmedRecordsPerStream"] = trimmedRecordsPerStream
        };
        if (!ObservationsMatch(actualObservations, scenario.CreateExpectedObservations()))
            throw new InvalidOperationException("The diagnostics observable results no longer match the catalog contract.");

        var resultDigest = ReproducibleWorkloadScenarioCatalog.Hash(ReproducibleWorkloadScenarioCatalog.Serialize(new
        {
            WorkloadId,
            scenario.ScenarioId,
            InputFingerprint = scenario.ComputeInputFingerprint(),
            Operations = operations,
            ObservableResults = actualObservations
        }));
        if (!StringComparer.Ordinal.Equals(resultDigest, ExpectedResultDigest))
            throw new InvalidOperationException("The diagnostics observable result digest no longer matches its ratified value.");

        return new DiagnosticsDurableHistoryWorkloadResult(
            scenario.ComputeInputFingerprint(),
            resultDigest,
            operations,
            actualObservations);
    }

    /// <summary>
    /// Prepares the fifteen catalog-owned diagnostics phases for process measurement. Correctness leaves
    /// the full retained fixture in place; reads therefore reuse that stable fixture, while each mutation
    /// gets an invocation-local reset or overflow row before the harness starts its stopwatch.
    /// </summary>
    public async ValueTask<IReadOnlyList<IDiagnosticsDurableHistoryWorkloadOperation>> PrepareMeasuredOperationsAsync(
        IDiagnosticsDurableHistoryWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        if (!scenario.OperationSequence.SequenceEqual(OperationIds, StringComparer.Ordinal))
            throw new InvalidOperationException("The diagnostics scenario operation sequence no longer matches the measured contract.");

        var scopes = await adapter.OpenScopedClientsAsync(cancellationToken);
        RequireDistinctScopes(scopes);

        // Correctness writes through asynchronous capture drains. Establish the measured fixture only
        // after those writes have become visible; this synchronization is outside every invocation.
        await adapter.FlushAsync(cancellationToken);
        await adapter.ResetReopenedClientsAsync(cancellationToken);

        var primary = scopes.Primary;
        var secondary = scopes.Secondary;
        var expectedHighWater = await primary.StructuredLogs.GetHighWaterMarkAsync(cancellationToken);
        var selectedTraceId = TraceIdFor(RetainedRecordsPerStream - 1);

        return
        [
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[0],
                async (_, token) =>
                {
                    // The secondary structured-log stream is the isolation fixture. Reset it before each
                    // sample so the seed phase never measures an already-retained/no-op append.
                    await secondary.StructuredLogs.TrimAsync(0, token);
                },
                async (invocation, token) =>
                {
                    await AppendMeasuredSecondaryLogsAsync(secondary.StructuredLogs, invocation, token);
                    await secondary.OpenTelemetry.WriteAsync(
                        MeasuredOpenTelemetryBatch(0, invocation, CrossScopeServiceName, includeCatalogs: true), token);
                    await adapter.FlushAsync(token);
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[1],
                async (_, token) =>
                {
                    // Keep the candidate population fixed at the retention boundary. The append itself
                    // remains the timed public-store mutation; this trim is preparation work.
                    await primary.StructuredLogs.TrimAsync(RetainedRecordsPerStream, token);
                },
                async (invocation, token) =>
                {
                    var maximumCommittedSequence = await AppendMeasuredStructuredLogsAsync(primary.StructuredLogs, invocation, token);
                    expectedHighWater = Math.Max(expectedHighWater, maximumCommittedSequence);
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[2],
                static (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var page = await primary.StructuredLogs.GetRecentAsync(
                        new StructuredLogFilter { MaxCount = QueryLimit }, token);
                    if (page.Count != QueryLimit)
                        throw new InvalidOperationException("The measured structured-log recent page was not bounded by the frozen query limit.");
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[3],
                static (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var page = await primary.StructuredLogs.ReadAfterAsync(
                        afterCursor: null,
                        StructuredLogFilter.None,
                        QueryLimit,
                        token);
                    if (page.Entries.Count != QueryLimit || page.NextCursor is null || !page.HasMore)
                        throw new InvalidOperationException("The measured structured-log replay page was not a bounded continuable window.");
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[4],
                async (_, token) => await adapter.ResetReopenedClientsAsync(token),
                async (_, token) =>
                {
                    var reopened = await adapter.ReopenClientAsync(token);
                    RequireDistinctClient(reopened, primary);
                    var actual = await reopened.StructuredLogs.GetHighWaterMarkAsync(token);
                    if (actual != expectedHighWater)
                        throw new InvalidOperationException("The measured reopened structured-log client did not preserve the prepared high-water.");
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[5],
                async (invocation, token) =>
                {
                    // The OpenTelemetry drain applies retention every 500 persisted records. Queue seven
                    // 64-record batches outside timing so the eighth (the measured batch) always crosses
                    // that boundary and exercises the same retention path on every sample.
                    for (var batch = 0; batch < 7; batch++)
                        await secondary.OpenTelemetry.WriteAsync(
                            MeasuredOpenTelemetryBatch(60 + batch, invocation, ServiceNameFor(0)), token);
                    await adapter.FlushAsync(token);
                },
                async (invocation, token) =>
                {
                    await secondary.OpenTelemetry.WriteAsync(
                        MeasuredOpenTelemetryBatch(6, invocation, ServiceNameFor(0)), token);
                    await adapter.FlushAsync(token);
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[6],
                static (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var result = await primary.OpenTelemetry.QueryResourcesAsync(
                        new OpenTelemetryResourceFilter { Take = NativeRouteLimits["resources-by-last-seen"] }, token);
                    if (result.Items.Count != NativeRouteLimits["resources-by-last-seen"])
                        throw new InvalidOperationException("The measured resource last-seen page was not the frozen bounded result.");
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[7],
                static (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var result = await primary.OpenTelemetry.QueryTracesAsync(
                        new OpenTelemetryTraceFilter { Take = QueryLimit }, token);
                    if (result.Items.Count == 0 || result.Items.Count > QueryLimit)
                        throw new InvalidOperationException("The measured grouped trace page was not a bounded non-empty result.");
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[8],
                static (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var detail = await primary.OpenTelemetry.GetTraceAsync(selectedTraceId, token);
                    if (detail is null || detail.Spans.Count == 0)
                        throw new InvalidOperationException("The measured trace detail did not expose the prepared trace fixture.");
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[9],
                static (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var result = await primary.OpenTelemetry.QueryMetricsAsync(
                        new OpenTelemetryMetricFilter { Take = QueryLimit }, token);
                    if (result.Points.Count != QueryLimit || result.Instruments.Count == 0)
                        throw new InvalidOperationException("The measured metric page was not the frozen bounded result.");
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[10],
                static (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var result = await primary.OpenTelemetry.QueryLogsAsync(
                        new OpenTelemetryLogFilter { Take = QueryLimit }, token);
                    if (result.Items.Count != QueryLimit)
                        throw new InvalidOperationException("The measured telemetry-log page was not the frozen bounded result.");
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[11],
                static (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var diagnostics = await primary.OpenTelemetry.GetDiagnosticsAsync(token);
                    if (diagnostics.TraceCount != RetainedRecordsPerStream ||
                        diagnostics.SpanCount != RetainedRecordsPerStream ||
                        diagnostics.MetricPointCount != RetainedRecordsPerStream ||
                        diagnostics.LogRecordCount != RetainedRecordsPerStream ||
                        diagnostics.ResourceCount != ResourceCount ||
                        diagnostics.MetricInstrumentCount != InstrumentCount ||
                        diagnostics.DroppedTraceCount + diagnostics.DroppedSpanCount +
                        diagnostics.DroppedMetricPointCount + diagnostics.DroppedLogRecordCount != 0)
                        throw new InvalidOperationException("The measured diagnostics inspection did not preserve the exact prepared stream and catalog counts.");
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[12],
                async (_, token) =>
                {
                    // Trim must delete something every time. Append one overflow row outside timing,
                    // after reducing the stream to its fixed retained boundary.
                    await primary.StructuredLogs.TrimAsync(RetainedRecordsPerStream, token);
                    var committed = await primary.StructuredLogs.AppendAsync(
                        StructuredLogEntryFor(0, PrimaryCategory, PrimarySourceId), token);
                    expectedHighWater = Math.Max(expectedHighWater, committed.Sequence);
                },
                async (_, token) =>
                {
                    await primary.StructuredLogs.TrimAsync(RetainedRecordsPerStream, token);
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[13],
                async (_, token) =>
                {
                    await adapter.ResetReopenedClientsAsync(token);
                    await adapter.FlushAsync(token);
                },
                async (_, token) =>
                {
                    var reopened = await adapter.ReopenClientAsync(token);
                    RequireDistinctClient(reopened, primary);
                    var actualHighWater = await reopened.StructuredLogs.GetHighWaterMarkAsync(token);
                    var diagnostics = await reopened.OpenTelemetry.GetDiagnosticsAsync(token);
                    if (actualHighWater != expectedHighWater || diagnostics.TraceCount == 0 || diagnostics.SpanCount == 0)
                        throw new InvalidOperationException("The measured reopened diagnostics client did not preserve the prepared history.");
                }),
            new DiagnosticsDurableHistoryWorkloadOperation(
                scenario.OperationSequence[14],
                static (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    if (await CountCrossScopeLeakageAsync(primary, token) != 0)
                        throw new InvalidOperationException("The measured cross-scope isolation query exposed another scope's records.");
                })
        ];
    }

    private static async ValueTask AppendMeasuredSecondaryLogsAsync(
        IStructuredLogStore store,
        long _,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < StructuredLogBatchSize; index++)
            await store.AppendAsync(StructuredLogEntryFor(index, CrossScopeCategory, CrossScopeSourceId), cancellationToken);
    }

    private static async ValueTask<long> AppendMeasuredStructuredLogsAsync(
        IStructuredLogStore store,
        long _,
        CancellationToken cancellationToken)
    {
        var maximumCommittedSequence = 0L;
        for (var index = 0; index < StructuredLogBatchSize; index++)
        {
            var committed = await store.AppendAsync(
                StructuredLogEntryFor(index, PrimaryCategory, PrimarySourceId), cancellationToken);
            if (committed.ReplayCursor is not { IsValid: true })
                throw new InvalidOperationException("A measured structured-log append carried no valid replay cursor.");
            maximumCommittedSequence = Math.Max(maximumCommittedSequence, committed.Sequence);
        }

        return maximumCommittedSequence;
    }

    private static OpenTelemetryBatch MeasuredOpenTelemetryBatch(
        int operation,
        long invocation,
        string serviceName,
        int recordCount = 0,
        bool includeCatalogs = false)
    {
        recordCount = recordCount <= 0 ? NormalizedRecordsPerOtlpBatch : recordCount;
        var offset = MeasuredIndex(operation, invocation);
        var resources = includeCatalogs ? [ResourceFor(0, serviceName)] : Array.Empty<TelemetryResource>();
        var instruments = includeCatalogs ? [InstrumentFor(0, serviceName)] : Array.Empty<MetricInstrument>();
        var traces = new List<TelemetryTrace>(recordCount);
        var spans = new List<TelemetrySpan>(recordCount);
        var points = new List<MetricPoint>(recordCount);
        var logs = new List<OtlpLogRecord>(recordCount);
        for (var index = 0; index < recordCount; index++)
        {
            var recordIndex = checked(offset + index);
            traces.Add(TraceFor(recordIndex, serviceName));
            spans.Add(SpanFor(recordIndex, serviceName));
            points.Add(MetricPointFor(recordIndex, serviceName));
            logs.Add(LogRecordFor(recordIndex, serviceName));
        }

        return new OpenTelemetryBatch(resources, traces, spans, instruments, points, logs);
    }

    private static int MeasuredIndex(int operation, long invocation)
    {
        var normalizedInvocation = invocation < 0 ? checked(-invocation - 1) : invocation;
        // Give every operation a non-overlapping 30M-record band and every invocation a full batch-sized
        // slice. A one-record stride would make adjacent invocations overwrite 63 of their 64 signals.
        return checked(
            operation * 30_000_000 +
            (int)(normalizedInvocation % 400_000) * NormalizedRecordsPerOtlpBatch);
    }

    private static ReproducibleWorkloadScenario ValidateScenario()
    {
        var scenarioResultDigest = Scenario.ComputeResultDigest();
        if (Scenario.Version != "1.3.0" || Scenario.ScenarioId != "diagnostics-durable-history" ||
            Scenario.Seed != "spec094-diagnostics-durable-history-v1.3" ||
            Scenario.ComputeInputFingerprint() != ExpectedInputFingerprint ||
            scenarioResultDigest != ExpectedResultDigest ||
            !ReproducibleWorkloadScenarioCatalog.GoldenVectors.TryGetValue(WorkloadId, out var golden) ||
            golden.InputFingerprint != ExpectedInputFingerprint || golden.ResultDigest != ExpectedResultDigest ||
            ConcurrentWriters != 4 || InstrumentCount != 64 || NormalizedRecordsPerOtlpBatch != 64 ||
            PayloadBytes != 512 || QueryLimit != 127 || ResourceCount != 128 ||
            RetainedRecordsPerStream != 100_000 || RetentionOverflowRecords != 1_000 ||
            StructuredLogBatchSize != 200 || TenantCount != 2 ||
            !StringComparer.Ordinal.Equals((string)Scenario.Parameters["timedSetup"], "excluded"))
            throw new InvalidOperationException(
                $"The diagnostics scenario no longer matches its frozen v1.3 successor contract. Expected result digest '{ExpectedResultDigest}', actual '{scenarioResultDigest}'.");
        if (AppendedPerStream % StructuredLogBatchSize != 0)
            throw new InvalidOperationException("The frozen append total is not an exact multiple of the structured-log batch size.");
        return Scenario;
    }

    /// <summary>
    /// Writes a small, distinctly-labelled history into the secondary scope. Volume is irrelevant — what
    /// matters is that these records exist and are findable by a predicate the primary scope will run.
    /// </summary>
    private static async Task SeedSecondaryScopeAsync(DiagnosticsDurableHistoryClient client, CancellationToken cancellationToken)
    {
        for (var index = 0; index < StructuredLogBatchSize; index++)
            await client.StructuredLogs.AppendAsync(StructuredLogEntryFor(index, CrossScopeCategory, CrossScopeSourceId), cancellationToken);

        await client.OpenTelemetry.WriteAsync(
            new OpenTelemetryBatch(
                [ResourceFor(0, CrossScopeServiceName)],
                [TraceFor(0, CrossScopeServiceName)],
                [SpanFor(0, CrossScopeServiceName)],
                [InstrumentFor(0, CrossScopeServiceName)],
                [MetricPointFor(0, CrossScopeServiceName)],
                [LogRecordFor(0, CrossScopeServiceName)]),
            cancellationToken);
    }

    private static async Task<long> AppendStructuredLogsAsync(DiagnosticsDurableHistoryClient client, CancellationToken cancellationToken)
    {
        var batches = AppendedPerStream / StructuredLogBatchSize;
        var writers = Enumerable.Range(0, ConcurrentWriters).Select(async writer =>
        {
            var maximumCommittedSequence = 0L;
            for (var batch = writer; batch < batches; batch += ConcurrentWriters)
            {
                for (var offset = 0; offset < StructuredLogBatchSize; offset++)
                {
                    var index = (batch * StructuredLogBatchSize) + offset;
                    var committed = await client.StructuredLogs.AppendAsync(
                        StructuredLogEntryFor(index, PrimaryCategory, PrimarySourceId),
                        cancellationToken);
                    if (committed.ReplayCursor is not { IsValid: true })
                        throw new InvalidOperationException("A committed structured-log entry carried no valid replay cursor.");
                    maximumCommittedSequence = Math.Max(maximumCommittedSequence, committed.Sequence);
                }
            }
            return maximumCommittedSequence;
        }).ToArray();
        return (await Task.WhenAll(writers)).Max();
    }

    private static async Task AppendOpenTelemetryAsync(DiagnosticsDurableHistoryClient client, CancellationToken cancellationToken)
    {
        foreach (var batch in OpenTelemetryBatches(AppendedPerStream, bindSignalsToLatestTrace: false))
            await client.OpenTelemetry.WriteAsync(batch, cancellationToken);
    }

    internal static IEnumerable<OpenTelemetryBatch> OpenTelemetryBatches(
        int recordCount,
        bool bindSignalsToLatestTrace,
        int batchSize = 0)
    {
        batchSize = batchSize <= 0 ? NormalizedRecordsPerOtlpBatch : batchSize;
        var remaining = recordCount;
        var index = 0;
        var resourcesWritten = 0;
        var instrumentsWritten = 0;
        var selectedTraceId = TraceIdFor(RetainedRecordsPerStream - 1);
        while (remaining > 0)
        {
            var size = Math.Min(batchSize, remaining);

            // The catalogs are mutable keyed upserts, not record streams: emit each resource and instrument
            // exactly once so their inspected counts are the frozen catalog sizes rather than the record
            // volume. Repeating them would still upsert, but it would make the intent unreadable.
            var resources = Enumerable.Range(resourcesWritten, Math.Max(0, Math.Min(size, ResourceCount - resourcesWritten)))
                .Select(ordinal => ResourceFor(ordinal, ServiceNameFor(ordinal)))
                .ToArray();
            var instruments = Enumerable.Range(instrumentsWritten, Math.Max(0, Math.Min(size, InstrumentCount - instrumentsWritten)))
                .Select(ordinal => InstrumentFor(ordinal, ServiceNameFor(ordinal)))
                .ToArray();
            resourcesWritten += resources.Length;
            instrumentsWritten += instruments.Length;

            var traces = new List<TelemetryTrace>(size);
            var spans = new List<TelemetrySpan>(size);
            var points = new List<MetricPoint>(size);
            var records = new List<OtlpLogRecord>(size);
            for (var offset = 0; offset < size; offset++, index++)
            {
                var service = ServiceNameFor(index % ResourceCount);
                traces.Add(TraceFor(index, service));
                spans.Add(SpanFor(index, service, bindSignalsToLatestTrace ? selectedTraceId : null));
                points.Add(MetricPointFor(index, service));
                records.Add(LogRecordFor(index, service, bindSignalsToLatestTrace ? selectedTraceId : null));
            }

            yield return new OpenTelemetryBatch(resources, traces, spans, instruments, points, records);
            remaining -= size;
        }

        if (index != recordCount || resourcesWritten != ResourceCount || instrumentsWritten != InstrumentCount)
            throw new InvalidOperationException("The OpenTelemetry fan-out did not emit the frozen record, resource and instrument totals.");
    }

    /// <summary>
    /// Counts retained entries by draining the durable tail. <see cref="IStructuredLogStore"/> exposes no
    /// count operation, and <c>GetRecentAsync</c> clamps to its own maximum, so the gap-free
    /// <c>ReadAfterAsync</c> traversal is the only contract-legal exact count.
    /// </summary>
    internal static async Task<int> CountRetainedAsync(IStructuredLogStore store, CancellationToken cancellationToken)
    {
        var count = 0;
        StructuredLogReplayCursor? cursor = null;

        // The adapter is frozen to QueryLimit, so its public store may clamp a larger request to that
        // value. Budget from the effective contract page size rather than from the unrelated append
        // batch size; those happen to differ specifically to catch accidental coupling.
        var maximumPages = ((AppendedPerStream + QueryLimit - 1) / QueryLimit) + 2;
        for (var page = 0; page < maximumPages; page++)
        {
            var previousCursor = cursor;
            var read = await store.ReadAfterAsync(cursor, StructuredLogFilter.None, QueryLimit, cancellationToken);
            count += read.Entries.Count;
            if (!read.HasMore)
                return count;

            cursor = read.NextCursor;
            if (cursor is null || cursor == previousCursor)
            {
                throw new InvalidOperationException(
                    "The durable structured-log tail advertised more pages without advancing its cursor.");
            }
        }

        throw new InvalidOperationException(
            "The durable structured-log tail exceeded the bounded page budget for the frozen append volume.");
    }

    private static async Task<int> CountCrossScopeLeakageAsync(DiagnosticsDurableHistoryClient client, CancellationToken cancellationToken)
    {
        var leakedEntries = await client.StructuredLogs.GetRecentAsync(
            new StructuredLogFilter { Category = CrossScopeCategory, MaxCount = QueryLimit },
            cancellationToken);
        var leakedResources = await client.OpenTelemetry.QueryResourcesAsync(
            new OpenTelemetryResourceFilter { ServiceName = CrossScopeServiceName, Take = QueryLimit },
            cancellationToken);
        var leakedTraces = await client.OpenTelemetry.QueryTracesAsync(
            new OpenTelemetryTraceFilter { ServiceName = CrossScopeServiceName, Take = QueryLimit },
            cancellationToken);
        var leakedMetrics = await client.OpenTelemetry.QueryMetricsAsync(
            new OpenTelemetryMetricFilter { ServiceName = CrossScopeServiceName, Take = QueryLimit },
            cancellationToken);
        var leakedLogs = await client.OpenTelemetry.QueryLogsAsync(
            new OpenTelemetryLogFilter { ServiceName = CrossScopeServiceName, Take = QueryLimit },
            cancellationToken);
        return leakedEntries.Count + leakedResources.Items.Count + leakedTraces.Items.Count +
               leakedMetrics.Points.Count + leakedMetrics.Instruments.Count + leakedLogs.Items.Count;
    }

    private const string PrimaryCategory = "Elsa.Benchmarks.Diagnostics.Primary";
    private const string PrimarySourceId = "spec094-primary";
    private const string CrossScopeCategory = "Elsa.Benchmarks.Diagnostics.OtherScope";
    private const string CrossScopeSourceId = "spec094-other-scope";
    private const string CrossScopeServiceName = "spec094-other-scope-service";

    // All primary resources share one service so the service-filter route has the frozen physical
    // cardinality and its finite page is strictly smaller than the candidate population. The secondary
    // scope uses CrossScopeServiceName and therefore remains an unambiguous isolation sentinel.
    internal static string ServiceNameFor(int ordinal) => "spec094-service";

    /// <summary>Setup-only fixture used by the official native-plan capture command. Its physical
    /// cardinalities match the frozen workload so every retained route fact is grounded in the same
    /// candidate populations; setup remains outside measured operations.</summary>
    internal static IEnumerable<OpenTelemetryBatch> NativePlanFixtureBatches() =>
        OpenTelemetryBatches(
            RetainedRecordsPerStream,
            bindSignalsToLatestTrace: true);

    private static StructuredLogEntry StructuredLogEntryFor(int index, string category, string sourceId) => new()
    {
        Sequence = index + 1,
        Timestamp = FixedNowUtc.AddMilliseconds(index),
        Level = LogLevel.Information,
        Category = category,
        EventId = index % 128,
        EventName = "spec094-diagnostics",
        Message = PayloadFor(index),
        MessageTemplate = "spec094 diagnostics {Index}",
        Properties = [new LogProperty("index", index.ToString(System.Globalization.CultureInfo.InvariantCulture))],
        SourceId = sourceId
    };

    /// <summary>
    /// A message padded to the frozen payload byte length, so both stacks are measured moving the same
    /// number of bytes rather than the same number of rows.
    /// </summary>
    private static string PayloadFor(int index)
    {
        var prefix = $"spec094-diagnostics-{index:D8}-";
        var padding = PayloadBytes - Encoding.UTF8.GetByteCount(prefix);
        if (padding < 0)
            throw new InvalidOperationException("The frozen diagnostics payload budget cannot hold its own record prefix.");
        var payload = prefix + new string('p', padding);
        if (Encoding.UTF8.GetByteCount(payload) != PayloadBytes)
            throw new InvalidOperationException("The diagnostics payload no longer has the frozen byte length.");
        return payload;
    }

    internal static TelemetryResource ResourceFor(int ordinal, string serviceName) => new(
        $"resource-{ordinal:D4}",
        serviceName,
        $"instance-{ordinal:D4}",
        "dotnet",
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["spec094.ordinal"] = ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        FixedNowUtc.AddMilliseconds(ordinal),
        TelemetryResourceStatus.Active);

    private static TelemetryTrace TraceFor(int index, string serviceName) => new(
        TraceIdFor(index),
        SpanIdFor(index),
        $"spec094-trace-{index:D8}",
        FixedNowUtc.AddMilliseconds(index),
        FixedNowUtc.AddMilliseconds(index + 1),
        TimeSpan.FromMilliseconds(1),
        SpanStatus.Ok,
        [$"resource-{index % ResourceCount:D4}"],
        [$"workflow-{index % ResourceCount:D4}"],
        1);

    private static TelemetrySpan SpanFor(int index, string serviceName, string? traceId = null) => new(
        $"span-row-{index:D8}",
        traceId ?? TraceIdFor(index),
        SpanIdFor(index),
        null,
        $"resource-{index % ResourceCount:D4}",
        $"spec094-span-{index:D8}",
        "Internal",
        FixedNowUtc.AddMilliseconds(index),
        FixedNowUtc.AddMilliseconds(index + 1),
        SpanStatus.Ok,
        null,
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["spec094.service"] = serviceName },
        [],
        []);

    private static MetricInstrument InstrumentFor(int ordinal, string serviceName) => new(
        $"instrument-{ordinal:D4}",
        $"resource-{ordinal % ResourceCount:D4}",
        $"spec094.instrument.{ordinal:D4}",
        "ms",
        "spec094 frozen instrument",
        MetricKind.Gauge,
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["spec094.service"] = serviceName });

    private static MetricPoint MetricPointFor(int index, string serviceName) => new(
        $"point-{index:D8}",
        $"instrument-{index % InstrumentCount:D4}",
        $"spec094.instrument.{index % InstrumentCount:D4}",
        $"resource-{index % ResourceCount:D4}",
        FixedNowUtc.AddMilliseconds(index),
        index % 1_000,
        null,
        null,
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["spec094.service"] = serviceName },
        TraceIdFor(index),
        SpanIdFor(index));

    private static OtlpLogRecord LogRecordFor(int index, string serviceName, string? traceId = null) => new(
        $"otlp-log-{index:D8}",
        $"resource-{index % ResourceCount:D4}",
        FixedNowUtc.AddMilliseconds(index),
        "Information",
        9,
        PayloadFor(index),
        traceId ?? TraceIdFor(index),
        SpanIdFor(index),
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["spec094.service"] = serviceName });

    internal static string TraceIdForTesting(int index) => TraceIdFor(index);

    private static string TraceIdFor(int index) => $"{index:x32}"[^32..];
    private static string SpanIdFor(int index) => $"{index:x16}"[^16..];

    private static void RequireDistinctScopes(DiagnosticsDurableHistoryScopes? scopes)
    {
        if (scopes is null || scopes.Primary is null || scopes.Secondary is null)
            throw new InvalidOperationException("The diagnostics workload adapter must open both storage scopes.");
        RequireDistinctClient(scopes.Secondary, scopes.Primary);
    }

    private static void RequireDistinctClient(DiagnosticsDurableHistoryClient candidate, DiagnosticsDurableHistoryClient other)
    {
        if (candidate is null ||
            ReferenceEquals(candidate, other) ||
            ReferenceEquals(candidate.StructuredLogs, other.StructuredLogs) ||
            ReferenceEquals(candidate.OpenTelemetry, other.OpenTelemetry))
            throw new InvalidOperationException("The diagnostics workload adapter must supply a genuinely distinct public-store client.");
    }

    private static int Int(string name) => (int)Scenario.Parameters[name];

    private static bool ObservationsMatch(IReadOnlyDictionary<string, object> actual, IReadOnlyDictionary<string, object> expected) =>
        actual.Count == expected.Count &&
        actual.All(pair => expected.TryGetValue(pair.Key, out var value) &&
                           JsonSerializer.Serialize(pair.Value, CanonicalJsonOptions) == JsonSerializer.Serialize(value, CanonicalJsonOptions));

}

/// <summary>
/// Creates public diagnostics-store clients over one adapter-owned backing, in two distinct storage
/// scopes, plus a genuinely reopened client for the restart assertions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Capacity obligation.</b> The runner asserts that each of the four OpenTelemetry record streams
/// retains exactly <c>retainedRecordsPerStream</c> after the deliberate overflow, and that the resource
/// and instrument catalogs hold exactly their frozen sizes. Those are properties of the
/// <c>OpenTelemetryDiagnosticsOptions</c> capacities the <i>adapter</i> configures, not of anything the
/// runner can set. An implementation must therefore size trace, span, metric-point, log-record and
/// resource capacities to the frozen parameters, or correctness fails closed at
/// <c>inspect-exact-stream-counts</c> with a count mismatch rather than with a useful message.
/// </para>
/// <para>
/// <b>Query-clamp obligation.</b> Both stores clamp a caller's requested page to their own configured
/// maximum — <c>MaxRecentQuerySize</c> on the structured-log side, <c>MaxQuerySize</c> on the
/// OpenTelemetry side. Both must be at least the frozen <c>queryLimit</c>, or the bounded reads return a
/// short page and correctness fails at <c>read-structured-log-recent</c> for a configuration reason that
/// looks like a storage defect.
/// </para>
/// <para>
/// <b>Drop obligation.</b> The runner requires a zero total drop count. The capture queues must be sized
/// so the frozen volume cannot shed; a shed batch is real evidence of loss, not a tuning artefact, so it
/// must not be worked around by relaxing the assertion.
/// </para>
/// </remarks>
public interface IDiagnosticsDurableHistoryWorkloadAdapter
{
    ValueTask<DiagnosticsDurableHistoryScopes> OpenScopedClientsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes the capture drain so everything appended is durable and queryable. Both stores enqueue
    /// onto a bounded background drain, so a read before a flush measures scheduling rather than storage.
    /// </summary>
    ValueTask FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a new client over the same durable storage, modelling a process restart.</summary>
    ValueTask<DiagnosticsDurableHistoryClient> ReopenClientAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases clients opened by earlier restart samples while retaining the two scope fixtures.
    /// The harness invokes this outside the timing window so repeated restart measurements do not
    /// accumulate provider compositions or measure their cleanup.
    /// </summary>
    ValueTask ResetReopenedClientsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Two scope-bound clients over shared backing. Isolation between them is asserted, not assumed.</summary>
public sealed record DiagnosticsDurableHistoryScopes(
    DiagnosticsDurableHistoryClient Primary,
    DiagnosticsDurableHistoryClient Secondary);

/// <summary>The public diagnostics contracts required by the durable-history correctness baseline.</summary>
public sealed record DiagnosticsDurableHistoryClient(
    IStructuredLogStore StructuredLogs,
    IOpenTelemetryStore OpenTelemetry);

/// <summary>One catalog-owned public diagnostics phase for process measurement.</summary>
public interface IDiagnosticsDurableHistoryWorkloadOperation
{
    string Id { get; }
    ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default);
    ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default);
}

internal sealed class DiagnosticsDurableHistoryWorkloadOperation(
    string id,
    Func<long, CancellationToken, ValueTask> prepare,
    Func<long, CancellationToken, ValueTask> invoke) : IDiagnosticsDurableHistoryWorkloadOperation
{
    public string Id { get; } = id;

    public ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default) =>
        prepare(invocation, cancellationToken);

    public ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default) =>
        invoke(invocation, cancellationToken);
}

public sealed record DiagnosticsDurableHistoryWorkloadResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, object> ObservableResults);
