using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Groundwork.Kernel;
using Microsoft.Data.Sqlite;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>Official capture-plan dispatcher for the diagnostics successor contract. It invokes each
/// declared public resource route and retains the provider's own explain artifact; a checkpoint document
/// is never used as a fallback for diagnostics.</summary>
internal static class DiagnosticsNativePlanCapture
{
    private static readonly SemaphoreSlim ExplainCaptureLock = new(1, 1);

    public static Task<string> CaptureAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken = default) =>
        request.Adapter switch
        {
            DiagnosticsDurableHistoryAdapter.AdapterId => CaptureGroundworkAsync(request, connectionString, outputDirectory, observed, cancellationToken),
            EfDiagnosticsDurableHistoryAdapter.AdapterId => CaptureEfAsync(request, connectionString, outputDirectory, observed, cancellationToken),
            _ => throw new PerformanceContractException($"Diagnostics native-plan capture does not support adapter '{request.Adapter}'.")
        };

    private static async Task<string> CaptureGroundworkAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken)
    {
        await using var adapter = new DiagnosticsDurableHistoryAdapter(request, connectionString, outputDirectory);
        await adapter.PrepareAsync(cancellationToken);
        var scopes = await adapter.OpenScopedClientsAsync(cancellationToken);
        await SeedStructuredLogFixtureAsync(scopes.Primary.StructuredLogs, cancellationToken);
        await scopes.Primary.OpenTelemetry.WriteAsync(DiagnosticsDurableHistoryWorkload.NativePlanFixtureBatch(), cancellationToken);
        await adapter.FlushAsync(cancellationToken);
        adapter.CommandObserver.ClearCommands();

        await ExplainCaptureLock.WaitAsync(cancellationToken);
        try
        {
            var previousFlag = Environment.GetEnvironmentVariable("GW_EXPLAIN_ASSERT");
            var previousDirectory = Environment.GetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR");
            var explainDirectory = Path.Combine(Path.GetTempPath(), $"groundwork-diagnostics-explain-{request.Provider}-{request.MeasurementSetId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(explainDirectory);
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", "1");
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", explainDirectory);
            try
            {
                var routes = new List<NativeRouteEvidence>(DiagnosticsDurableHistoryWorkload.NativeRouteLimits.Count);
                var blockedRoutes = new List<string>();
                foreach (var (route, limit) in DiagnosticsDurableHistoryWorkload.NativeRouteLimits)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var specification = DiagnosticsNativePlanContract.For(request.Adapter, route);
                    adapter.CommandObserver.ClearCommands();
                    var before = Directory.EnumerateFiles(explainDirectory).ToHashSet(StringComparer.Ordinal);
                    var result = await InvokeRouteAsync(scopes.Primary, route, limit, cancellationToken);
                    if (result != limit)
                        throw new PerformanceContractException($"Diagnostics native route '{route}' returned {result} rows; expected {limit}.");
                    if (string.IsNullOrWhiteSpace(specification.IndexName))
                    {
                        blockedRoutes.Add(route);
                        continue;
                    }
                    var command = RequireGroundworkCommand(adapter.CommandObserver.Commands, specification);
                    var nativePath = RequireNativeArtifact(explainDirectory, before, request.Provider, specification);
                    var rawPlan = IamNativePlanParser.NormalizeForArtifact(request.Provider, File.ReadAllText(nativePath));
                    var rawReference = ArtifactStore.RawPlanName($"diagnostics.{request.Provider}.{request.MeasurementSetId}.{route}.raw.json");
                    var rawPath = Path.Combine(outputDirectory, rawReference);
                    WriteEnvelope(rawPath, new DiagnosticsNativePlanArtifact(1, request.Provider, request.Adapter, route, specification.TableName, specification.IndexName, command, rawPlan));
                    routes.Add(new NativeRouteEvidence(
                        route,
                        rawReference,
                        ArtifactStore.HashFile(rawPath),
                        "index-search",
                        specification.IndexName,
                        specification.PhysicalCardinality,
                        specification.StorageScopeRequired,
                        specification.PredicateColumn is not null,
                        limit,
                        limit));
                }
                var routeContract = blockedRoutes.Count == 0
                    ? "provider-native-routes"
                    : DiagnosticsNativePlanContract.BlockedRouteContract;
                return NativePlanEvidenceStaging.Write(
                    outputDirectory,
                    CreateDocument(request, observed, routes, routeContract, blockedRoutes));
            }
            finally
            {
                Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", previousFlag);
                Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", previousDirectory);
                try { if (Directory.Exists(explainDirectory)) Directory.Delete(explainDirectory, recursive: true); } catch { }
            }
        }
        finally
        {
            ExplainCaptureLock.Release();
        }
    }

    private static async Task<string> CaptureEfAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Provider, "sqlite", StringComparison.Ordinal))
            throw new PerformanceContractException("The temporary EF diagnostics native-plan capture only supports sqlite.");

        await using var adapter = new EfDiagnosticsDurableHistoryAdapter(request, connectionString, outputDirectory);
        await adapter.PrepareAsync(cancellationToken);
        var scopes = await adapter.OpenScopedClientsAsync(cancellationToken);
        await SeedStructuredLogFixtureAsync(scopes.Primary.StructuredLogs, cancellationToken);
        await scopes.Primary.OpenTelemetry.WriteAsync(DiagnosticsDurableHistoryWorkload.NativePlanFixtureBatch(), cancellationToken);
        await adapter.FlushAsync(cancellationToken);
        var observations = new List<DiagnosticsOracleRouteObservation>(DiagnosticsDurableHistoryWorkload.NativeRouteLimits.Count);
        foreach (var (route, limit) in DiagnosticsDurableHistoryWorkload.NativeRouteLimits)
        {
            var specification = DiagnosticsNativePlanContract.For(request.Adapter, route);
            adapter.CommandObserver.ClearCommands();
            var materialized = await InvokeRouteAsync(scopes.Primary, route, limit, cancellationToken);
            if (materialized != limit)
                throw new PerformanceContractException($"EF diagnostics route '{route}' returned {materialized} rows; expected {limit}.");
            var commands = adapter.CommandObserver.Commands
                .Where(command => command.CommandText.Contains(specification.TableName, StringComparison.OrdinalIgnoreCase) &&
                                  command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (commands.Length == 0)
                throw new PerformanceContractException($"EF diagnostics route '{route}' emitted no retained command against '{specification.TableName}'.");
            var plans = new List<string>(commands.Length);
            foreach (var command in commands)
                plans.Add(await CaptureSqlitePlanAsync(adapter.PrimaryDatabaseConnectionString, command, cancellationToken));
            observations.Add(new DiagnosticsOracleRouteObservation(
                route,
                commands.Select(command => command.CommandText).ToArray(),
                string.Join(Environment.NewLine, plans)));
        }
        return NativePlanEvidenceStaging.Write(
            outputDirectory,
            CreateDocument(
                request,
                observed,
                [],
                DiagnosticsNativePlanContract.EfCorrectnessOnlyRouteContract,
                DiagnosticsDurableHistoryWorkload.NativeRouteLimits.Keys.ToArray(),
                observations));
    }

    private static async Task<int> InvokeRouteAsync(
        DiagnosticsDurableHistoryClient client,
        string route,
        int limit,
        CancellationToken cancellationToken)
    {
        switch (route)
        {
            case "resources-by-last-seen":
                return (await client.OpenTelemetry.QueryResourcesAsync(new OpenTelemetryResourceFilter { Take = limit }, cancellationToken)).Items.Count;
            case "resources-by-status":
                return (await client.OpenTelemetry.QueryResourcesAsync(new OpenTelemetryResourceFilter { Status = TelemetryResourceStatus.Active, Take = limit }, cancellationToken)).Items.Count;
            case "resources-by-service":
                return (await client.OpenTelemetry.QueryResourcesAsync(new OpenTelemetryResourceFilter { ServiceName = DiagnosticsDurableHistoryWorkload.ServiceNameFor(0), Take = limit }, cancellationToken)).Items.Count;
            case "traces-by-last-seen":
                return (await client.OpenTelemetry.QueryTracesAsync(new OpenTelemetryTraceFilter { Take = limit }, cancellationToken)).Items.Count;
            case "trace-detail":
                return await client.OpenTelemetry.GetTraceAsync(DiagnosticsDurableHistoryWorkload.TraceIdForTesting(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream - 1), cancellationToken) is null ? 0 : 1;
            case "metrics-by-last-seen":
                return (await client.OpenTelemetry.QueryMetricsAsync(new OpenTelemetryMetricFilter { Take = limit }, cancellationToken)).Points.Count;
            case "logs-by-last-seen":
                return (await client.OpenTelemetry.QueryLogsAsync(new OpenTelemetryLogFilter { Take = limit }, cancellationToken)).Items.Count;
            case "structured-log-recent":
                return (await client.StructuredLogs.GetRecentAsync(new StructuredLogFilter { MaxCount = limit }, cancellationToken)).Count;
            case "structured-log-replay":
                return (await client.StructuredLogs.ReadAfterAsync(null, StructuredLogFilter.None, limit, cancellationToken)).Entries.Count;
            default:
                throw new PerformanceContractException($"Unsupported diagnostics route '{route}'.");
        }
    }

    private static async Task SeedStructuredLogFixtureAsync(
        Elsa.Diagnostics.StructuredLogs.Core.Contracts.IStructuredLogStore store,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream; index++)
            await store.AppendAsync(new Elsa.Diagnostics.StructuredLogs.Core.Models.StructuredLogEntry
            {
                Sequence = index + 1,
                Timestamp = DiagnosticsDurableHistoryWorkload.FixedNowUtc.AddMilliseconds(index),
                Level = Microsoft.Extensions.Logging.LogLevel.Information,
                Category = "spec094-native-plan",
                EventId = index,
                EventName = "native-plan",
                Message = $"native-plan-{index}",
                MessageTemplate = "native-plan {Index}",
                Properties = [new Elsa.Diagnostics.StructuredLogs.Core.Models.LogProperty("index", index.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                SourceId = "spec094-native-plan"
            }, cancellationToken);
    }

    private static string RequireGroundworkCommand(IReadOnlyList<ProviderCommandEvent> commands, DiagnosticsNativeRouteSpec specification)
    {
        var matches = commands.Where(command => !command.IsProbe &&
            command.Operation.EndsWith(".query", StringComparison.Ordinal) &&
            command.Kind == ProviderCommandKind.Read &&
            !string.IsNullOrWhiteSpace(command.CommandText) && command.CommandText.Contains(specification.TableName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' must emit exactly one provider query against '{specification.TableName}'; observed {matches.Length}.");
        return matches[0].CommandText!;
    }

    private static string RequireNativeArtifact(string directory, IReadOnlySet<string> before, string provider, DiagnosticsNativeRouteSpec specification)
    {
        var extension = IamNativePlanParser.RawPlanExtension(provider);
        var suffix = $"-{specification.IndexName}{extension}";
        var matches = Directory.EnumerateFiles(directory)
            .Where(path => !before.Contains(path) && Path.GetFileName(path).EndsWith(suffix, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException($"Diagnostics route '{specification.RouteIdentity}' must emit exactly one provider-native explain artifact for '{specification.IndexName}'; observed {matches.Length}.");
        return matches[0];
    }

    private static void WriteEnvelope(string path, DiagnosticsNativePlanArtifact artifact)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }));
        ArtifactStore.ValidateRawPlanFile(path);
    }

    private static NativePlanEvidenceDocument CreateDocument(
        RunRequest request,
        ProviderProbe.Result observed,
        IReadOnlyList<NativeRouteEvidence> routes,
        string routeContract = "provider-native-routes",
        IReadOnlyList<string>? blockedRoutes = null,
        IReadOnlyList<DiagnosticsOracleRouteObservation>? oracleObservations = null) =>
        new(2, request.ComparisonCohortId, request.MeasurementSetId, request.WorkloadId, request.WorkloadVersion, request.Provider, request.Adapter, request.PhysicalForm, request.Scale, request.CommitSha, request.HarnessAssemblySha256, request.CompositionFingerprint, request.HostFingerprintSha256, observed.Version, observed.Topology, observed.Configuration, request.Seed, request.InputFingerprintSha256, request.NativePlanIdentity, routes, routeContract, blockedRoutes, oracleObservations);

    internal static EfCommandSnapshot RequireEfRouteCommand(
        IReadOnlyList<EfCommandSnapshot> commands,
        DiagnosticsNativeRouteSpec specification)
    {
        var matches = commands.Where(command =>
                command.CommandText.Contains(specification.TableName, StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException(
                $"EF diagnostics route '{specification.RouteIdentity}' must retain exactly one intercepted provider query against '{specification.TableName}'; observed {matches.Length}.");
        return matches[0];
    }

    internal static async Task<string> CaptureSqlitePlanAsync(
        string connectionString,
        EfCommandSnapshot command,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var explain = connection.CreateCommand();
        explain.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;
        foreach (var parameter in command.Parameters)
        {
            var copy = explain.CreateParameter();
            copy.ParameterName = parameter.Name;
            copy.DbType = parameter.DbType;
            copy.Size = parameter.Size;
            copy.Value = parameter.Value ?? DBNull.Value;
            explain.Parameters.Add(copy);
        }
        await using var reader = await explain.ExecuteReaderAsync(cancellationToken);
        var lines = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
            lines.Add(reader.GetString(reader.GetOrdinal("detail")));
        return string.Join(Environment.NewLine, lines);
    }
}
