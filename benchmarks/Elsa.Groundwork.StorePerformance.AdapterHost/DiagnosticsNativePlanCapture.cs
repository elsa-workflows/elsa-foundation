using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Groundwork.Diagnostics;
using Groundwork.Kernel;
using Npgsql;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>Official capture-plan dispatcher for the diagnostics successor contract. It invokes each
/// declared public resource route and retains provider-owned typed evidence for migrated routes or
/// explain artifacts for other routes. A checkpoint document is never a fallback for diagnostics.</summary>
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
            _ => throw new PerformanceContractException($"Diagnostics native-plan capture does not support adapter '{request.Adapter}'.")
        };

    private static async Task<string> CaptureGroundworkAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken)
    {
        await using var adapter = new DiagnosticsDurableHistoryAdapter(
            request,
            connectionString,
            outputDirectory,
            captureStructuredEvidence: true);
        await adapter.PrepareAsync(cancellationToken);
        var scopes = await adapter.OpenScopedClientsAsync(cancellationToken);
        await SeedStructuredLogFixtureAsync(scopes.Primary.StructuredLogs, cancellationToken);
        foreach (var batch in DiagnosticsDurableHistoryWorkload.NativePlanFixtureBatches())
            await scopes.Primary.OpenTelemetry.WriteAsync(batch, cancellationToken);
        await adapter.FlushAsync(cancellationToken);
        await AnalyzePostgreSqlFixtureAsync(request, connectionString, cancellationToken);
        adapter.CommandObserver.ClearCommands();

        await ExplainCaptureLock.WaitAsync(cancellationToken);
        try
        {
            var explainDirectory = Path.Combine(Path.GetTempPath(), $"groundwork-diagnostics-explain-{request.Provider}-{request.MeasurementSetId}-{Guid.NewGuid():N}");
            return await ExecuteExplainCaptureAsync(
                explainDirectory,
                outputDirectory,
                request.Provider,
                request.MeasurementSetId,
                async () =>
                {
                var routes = new List<NativeRouteEvidence>(DiagnosticsDurableHistoryWorkload.NativeRouteLimits.Count);
                var traceDetailConstituents = new List<DiagnosticsTraceDetailConstituentEvidence>();
                var blockedRoutes = new List<string>();
                var blockedRouteDiagnostics = new List<DiagnosticsBlockedRouteEvidence>();
                foreach (var (route, limit) in DiagnosticsDurableHistoryWorkload.NativeRouteLimits)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var specification = DiagnosticsNativePlanContract.For(request.Adapter, route);

                    if (route == "trace-detail")
                    {
                        adapter.CommandObserver.ClearCommands();
                        var beforeTraceDetail = Directory.EnumerateFiles(explainDirectory).ToHashSet(StringComparer.Ordinal);
                        try
                        {
                            traceDetailConstituents.AddRange(await CaptureTraceDetailConstituentsAsync(
                                adapter,
                                scopes.Primary,
                                request,
                                observed.Version,
                                cancellationToken));
                        }
                        catch (PerformanceContractException exception) when (DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception))
                        {
                            blockedRoutes.Add(route);
                            blockedRouteDiagnostics.Add(CreateBlockedRouteDiagnostic(
                                route,
                                "trace-detail-plan-validation",
                                DiagnosticsNativePlanContract.BlockedPlanReasonCode(exception),
                                PreserveBlockedExplainArtifacts(
                                    explainDirectory,
                                    beforeTraceDetail,
                                    outputDirectory,
                                    request.Provider,
                                    request.MeasurementSetId,
                                    route)));
                        }
                        catch (ExplainAssertionException exception)
                        {
                            // Groundwork's assertion mode has already retained the provider artifact;
                            // an unchosen index/scan is an honest blocked composite, never evidence that
                            // the public call used a bounded native plan.
                            blockedRoutes.Add(route);
                            blockedRouteDiagnostics.Add(CreateBlockedRouteDiagnostic(
                                route,
                                "trace-detail-plan-assertion",
                                "native-plan.assertion-mismatch",
                                PreserveBlockedExplainArtifacts(
                                    explainDirectory,
                                    beforeTraceDetail,
                                    outputDirectory,
                                    request.Provider,
                                    request.MeasurementSetId,
                                    route,
                                    exception.ArtifactPath)));
                        }
                        continue;
                    }

                    // An empty index is an explicit storage/route limitation, not an invitation to
                    // capture the public query and label its scan as native evidence. Composite
                    // trace-detail evidence is handled above; any other empty-index route remains
                    // explicitly blocked without executing its unsupported query shape.
                    if (string.IsNullOrWhiteSpace(specification.IndexName))
                    {
                        blockedRoutes.Add(route);
                        blockedRouteDiagnostics.Add(CreateBlockedRouteDiagnostic(
                            route,
                            "route-admission",
                            "native-plan.missing-index-declaration",
                            []));
                        continue;
                    }

                    var migratedStructuredRoute = DiagnosticsNativePlanContract.IsStructuredEvidenceRoute(
                        request.Provider,
                        request.Adapter,
                        route);
                    if (migratedStructuredRoute)
                    {
                        routes.Add(await CaptureStructuredEvidenceRouteAsync(
                            adapter,
                            scopes.Primary,
                            request,
                            observed,
                            route,
                            limit,
                            cancellationToken));
                        continue;
                    }

                    // Every diagnostics route on every provider is admitted from typed evidence; a route
                    // outside that table has no admission path any more (#1594).
                    throw new PerformanceContractException($"Diagnostics route '{route}' has no typed admission on provider '{request.Provider}'.");
                }
                var routeContract = blockedRoutes.Count == 0
                    ? "provider-native-routes"
                    : DiagnosticsNativePlanContract.BlockedRouteContract;
                if (blockedRouteDiagnostics.Count != 0)
                    NativePlanEvidenceStaging.WriteBlockedCapture(outputDirectory, request, blockedRouteDiagnostics);
                return NativePlanEvidenceStaging.Write(
                    outputDirectory,
                    CreateDocument(request, observed, routes, routeContract, blockedRoutes, traceDetailConstituents: traceDetailConstituents));
                });
        }
        finally
        {
            ExplainCaptureLock.Release();
        }
    }

    /// <summary>
    /// Captures a structured-log route through the same public store call and callback boundary used by
    /// native-plan capture. The route is deliberately parameterized because recent and replay are the
    /// same bounded Groundwork query family with different ordering; admission can widen per route
    /// without duplicating a request projection or falling back to raw command parsing.
    /// </summary>
    internal static async Task<NativeRouteEvidence> CaptureStructuredEvidenceRouteAsync(
        DiagnosticsDurableHistoryAdapter adapter,
        DiagnosticsDurableHistoryClient client,
        RunRequest request,
        ProviderProbe.Result observed,
        string route,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        if (!string.Equals(request.Adapter, DiagnosticsDurableHistoryAdapter.AdapterId, StringComparison.Ordinal) ||
            !DiagnosticsNativePlanContract.IsStructuredEvidenceRoute(request.Provider, request.Adapter, route))
        {
            throw new PerformanceContractException(
                $"Structured callback capture is not admitted for route '{route}' on provider '{request.Provider}'.");
        }

        var specification = DiagnosticsNativePlanContract.For(request.Adapter, route);
        if (limit != specification.FiniteLimit)
            throw new PerformanceContractException(
                $"Structured diagnostics route '{route}' must use its declared finite limit {specification.FiniteLimit}.");

        // Establish the exact one-command/one-callback window immediately before the public route call.
        // This is the producer-owned observation, not a reconstruction from RunRequest or SQL text.
        adapter.CommandObserver.ClearCommands();
        adapter.ClearStructuredEvidence();
        int result;
        try
        {
            result = await InvokeRouteAsync(client, route, limit, cancellationToken);
        }
        catch (ExplainAssertionException) when (
            DiagnosticsNativePlanContract.IsBoundedResourceRoute(request.Provider, request.Adapter, specification))
        {
            // The provider asserted the declared index and the planner declined it for the frozen 128-row
            // catalog. Repeat only the public call with the assertion suppressed; its typed plan proves the
            // bounded scan and sort, as the raw path proves it from the retained assertion artifact.
            adapter.CommandObserver.ClearCommands();
            adapter.ClearStructuredEvidence();
            result = await InvokeBoundedResourceRouteWithoutExplainAssertionAsync(client, route, limit, cancellationToken);
        }
        if (result != limit)
            throw new PerformanceContractException($"Diagnostics native route '{route}' returned {result} rows; expected {limit}.");

        var structuredEvidence = RequireStructuredEvidence(adapter, request.Provider, route, observed.Version);
        var nativeFetchLimit = structuredEvidence.BoundedQuery?.NativeLimit.Value;
        if (nativeFetchLimit != checked(limit + 1))
            throw new PerformanceContractException(
                $"Diagnostics route '{route}' emitted native fetch limit {nativeFetchLimit?.ToString() ?? "unknown"}; expected {limit + 1}.");

        var routeEvidence = new NativeRouteEvidence(
            route,
            string.Empty,
            string.Empty,
            DiagnosticsNativePlanContract.ClassifyStructuredPlan(request.Provider, request.Adapter, specification, structuredEvidence.Plan),
            DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(request.Provider, specification),
            specification.PhysicalCardinality,
            DiagnosticsNativePlanContract.ExpectedStorageScopePredicate(request.Provider, specification),
            specification.PredicateColumn is not null,
            limit,
            limit)
        {
            NativeFetchLimit = nativeFetchLimit.Value,
            StructuredEvidence = structuredEvidence
        };

        DiagnosticsNativePlanContract.ValidateStructuredEvidence(
            request.Provider,
            request.Adapter,
            routeEvidence,
            observed.Version);

        return routeEvidence;
    }

    /// <summary>The route's one bounded read, selected by operation because a route may also observe
    /// point reads (the metrics page resolves its instruments one by one).</summary>
    private static StructuredExecutionEvidence RequireStructuredEvidence(
        DiagnosticsDurableHistoryAdapter adapter,
        string provider,
        string route,
        string expectedProviderVersion)
    {
        // A route may also observe point reads (the metrics page resolves its instruments one by one) and,
        // on a provider that asserts the declared index through a separate explain statement, an
        // unsupported bounded-query observation for that statement; the route's evidence is its single
        // collected bounded read of the route's own unit.
        var unit = DiagnosticsNativePlanContract.LogicalUnitIdFor(route);
        var observations = adapter.StructuredEvidence
            .Where(observation => string.Equals(observation.Operation, "BoundedQuery", StringComparison.Ordinal) &&
                                  string.Equals(observation.Target?.LogicalUnitId, unit, StringComparison.Ordinal) &&
                                  string.Equals(observation.ShapeAvailability, "Collected", StringComparison.Ordinal))
            .ToArray();
        if (observations.Length != 1)
            throw new PerformanceContractException(
                $"Diagnostics route '{route}' emitted {observations.Length} collected structured bounded-query observations of '{unit}'; expected exactly one terminal read.");
        var evidence = observations[0];
        if (string.IsNullOrWhiteSpace(evidence.Provider))
            throw new PerformanceContractException($"Structured evidence for '{route}' on '{provider}' did not identify its provider.");
        if (!string.Equals(evidence.ProviderVersion, expectedProviderVersion, StringComparison.Ordinal))
            throw new PerformanceContractException($"Structured evidence for '{route}' did not identify the observed provider version.");
        if (evidence.BoundedQuery?.NativeLimit is null)
            throw new PerformanceContractException($"Structured evidence for '{route}' did not retain the bounded-query native limit.");
        return evidence;
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

    internal static async Task SeedStructuredLogFixtureAsync(
        Elsa.Diagnostics.StructuredLogs.Core.Contracts.IStructuredLogStore store,
        CancellationToken cancellationToken)
    {
        const int acknowledgementWindow = 1_000;
        var acknowledgements = new List<Task<Elsa.Diagnostics.StructuredLogs.Core.Models.StructuredLogEntry>>(
            acknowledgementWindow);
        for (var index = 0; index < DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream; index++)
        {
            acknowledgements.Add(store.AppendAsync(new Elsa.Diagnostics.StructuredLogs.Core.Models.StructuredLogEntry
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
            }, cancellationToken).AsTask());
            if (acknowledgements.Count != acknowledgementWindow)
                continue;

            await Task.WhenAll(acknowledgements);
            acknowledgements.Clear();
        }
        if (acknowledgements.Count != 0)
            await Task.WhenAll(acknowledgements);
    }

    private static async Task AnalyzePostgreSqlFixtureAsync(
        RunRequest request,
        string connectionString,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Provider, "postgresql", StringComparison.Ordinal))
            return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var commandText in PostgreSqlAnalyzeCommands(request.Adapter))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal static IReadOnlyList<string> PostgreSqlAnalyzeCommands(string adapter) =>
        DiagnosticsDurableHistoryWorkload.NativeRouteLimits.Keys
            .Select(route => DiagnosticsNativePlanContract.For(adapter, route).TableName)
            .Concat(DiagnosticsNativePlanContract.TraceDetailConstituents(adapter).Select(item => item.TableName))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(table => $"ANALYZE \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\"")
            .ToArray();

    internal static IReadOnlyList<string> PreserveFailedExplainArtifacts(
        string explainDirectory,
        string outputDirectory,
        string provider,
        string measurementSetId)
    {
        if (!Directory.Exists(explainDirectory))
            return [];

        Directory.CreateDirectory(outputDirectory);
        var extension = IamNativePlanParser.RawPlanExtension(provider);
        var retained = new List<string>();
        var rejected = 0;
        foreach (var source in Directory.EnumerateFiles(explainDirectory, $"*{extension}")
                     .Order(StringComparer.Ordinal).ToArray())
        {
            var reference = ArtifactStore.RawPlanName(
                $"diagnostics.{provider}.{measurementSetId}.failed-explain-{retained.Count + 1}{extension}");
            try
            {
                PreserveSafeExplainArtifact(source, Path.Combine(outputDirectory, reference), provider);
                retained.Add(reference);
            }
            catch (Exception exception) when (IsExplainRetentionFailure(exception))
            {
                rejected++;
            }
        }

        if (rejected != 0)
            Console.Error.WriteLine($"native-plan.failed-explain-retention-rejected; count={rejected}");
        return retained;
    }

    internal static async Task<T> ExecuteExplainCaptureAsync<T>(
        string explainDirectory,
        string outputDirectory,
        string provider,
        string measurementSetId,
        Func<Task<T>> capture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explainDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(measurementSetId);
        ArgumentNullException.ThrowIfNull(capture);

        var previousFlag = Environment.GetEnvironmentVariable("GW_EXPLAIN_ASSERT");
        var previousDirectory = Environment.GetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR");
        Directory.CreateDirectory(explainDirectory);
        Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", "1");
        Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", explainDirectory);
        try
        {
            return await capture();
        }
        catch (Exception exception) when (exception is ExplainAssertionException or PerformanceContractException)
        {
            try
            {
                PreserveFailedExplainArtifacts(explainDirectory, outputDirectory, provider, measurementSetId);
            }
            catch (Exception retentionFailure) when (retentionFailure is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine("native-plan.failed-explain-retention-unavailable");
            }
            throw;
        }
        finally
        {
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", previousFlag);
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", previousDirectory);
            try { if (Directory.Exists(explainDirectory)) Directory.Delete(explainDirectory, recursive: true); } catch { }
        }
    }

    internal static IReadOnlyList<DiagnosticsBlockedRawPlanEvidence> PreserveBlockedExplainArtifacts(
        string explainDirectory,
        IReadOnlySet<string> before,
        string outputDirectory,
        string provider,
        string measurementSetId,
        string route,
        string? explicitArtifactPath = null)
    {
        if (!Directory.Exists(explainDirectory))
            return [];

        var fullDirectory = Path.GetFullPath(explainDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var extension = IamNativePlanParser.RawPlanExtension(provider);
        var candidates = Directory.EnumerateFiles(explainDirectory, $"*{extension}")
            .Where(path => !before.Any(previous => string.Equals(previous, path, pathComparison)))
            .ToList();
        if (!string.IsNullOrWhiteSpace(explicitArtifactPath))
        {
            try
            {
                var fullPath = Path.GetFullPath(explicitArtifactPath);
                if (fullPath.StartsWith(fullDirectory, pathComparison) &&
                    File.Exists(fullPath) &&
                    fullPath.EndsWith(extension, StringComparison.Ordinal) &&
                    !before.Any(path => string.Equals(path, fullPath, pathComparison)))
                    candidates.Add(fullPath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Provider assertion paths are advisory; malformed paths must not hide the original
                // assertion failure or prevent other newly emitted raw plans from being retained.
            }
        }

        Directory.CreateDirectory(outputDirectory);
        var slug = route.Replace("/", "-", StringComparison.Ordinal);
        var retained = new List<DiagnosticsBlockedRawPlanEvidence>();
        var rejected = 0;
        foreach (var source in candidates.Distinct(pathComparison == StringComparison.OrdinalIgnoreCase
                     ? StringComparer.OrdinalIgnoreCase
                     : StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var reference = ArtifactStore.RawPlanName(
                $"diagnostics.{provider}.{measurementSetId}.blocked-{slug}-{retained.Count + 1}{extension}");
            var destination = Path.Combine(outputDirectory, reference);
            try
            {
                PreserveSafeExplainArtifact(source, destination, provider);
                retained.Add(new DiagnosticsBlockedRawPlanEvidence(reference, ArtifactStore.HashFile(destination)));
            }
            catch (Exception exception) when (IsExplainRetentionFailure(exception))
            {
                rejected++;
            }
        }

        if (rejected != 0)
            Console.Error.WriteLine($"native-plan.blocked-retention-rejected; count={rejected}");
        return retained;
    }

    private static bool IsExplainRetentionFailure(Exception exception) =>
        exception is PerformanceContractException or IOException or UnauthorizedAccessException;

    private static void PreserveSafeExplainArtifact(string source, string destination, string provider)
    {
        if (new FileInfo(source).Length is <= 0 or > 16 * 1024 * 1024)
            throw new PerformanceContractException("native-plan.failure-artifact-size-invalid");

        var normalized = IamNativePlanParser.NormalizeForArtifact(provider, File.ReadAllText(source));
        // Validate outside the upload directory. A failed safety check must never leave the rejected
        // contents in an artifact picked up by the workflow's always-on failure upload.
        var staging = Directory.CreateTempSubdirectory("groundwork-safe-explain-");
        try
        {
            var candidate = Path.Combine(staging.FullName, "plan" + IamNativePlanParser.RawPlanExtension(provider));
            File.WriteAllText(candidate, normalized);
            ArtifactStore.ValidateRawPlanFile(candidate);
            File.Copy(candidate, destination, overwrite: true);
        }
        finally
        {
            staging.Delete(recursive: true);
        }
    }

    private static DiagnosticsBlockedRouteEvidence CreateBlockedRouteDiagnostic(
        string route,
        string phase,
        string reasonCode,
        IReadOnlyList<DiagnosticsBlockedRawPlanEvidence> rawPlans) =>
        new(route, phase, reasonCode, rawPlans);

    /// <summary>
    /// Captures the trace-detail composite from typed Groundwork observations of the one public
    /// <c>GetTraceAsync</c> call: the summary and resource point reads and every bounded page of the
    /// span and log sequences, in observation order. No command text or native plan is read.
    /// </summary>
    private static async Task<IReadOnlyList<DiagnosticsTraceDetailConstituentEvidence>> CaptureTraceDetailConstituentsAsync(
        DiagnosticsDurableHistoryAdapter adapter,
        DiagnosticsDurableHistoryClient client,
        RunRequest request,
        string observedProviderVersion,
        CancellationToken cancellationToken)
    {
        var specifications = DiagnosticsNativePlanContract.TraceDetailConstituents(request.Adapter);
        adapter.CommandObserver.ClearCommands();
        adapter.ClearStructuredEvidence();
        var detail = await client.OpenTelemetry.GetTraceAsync(
            DiagnosticsDurableHistoryWorkload.TraceIdForTesting(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream - 1),
            cancellationToken);
        if (detail is null)
            throw new PerformanceContractException("Diagnostics trace-detail capture did not find its fixture trace.");
        var observations = adapter.StructuredEvidence;
        var evidence = new List<DiagnosticsTraceDetailConstituentEvidence>(specifications.Count);
        foreach (var specification in specifications)
        {
            var unit = DiagnosticsNativePlanContract.LogicalUnitIdForTable(specification.TableName);
            var pointRead = specification.OperationKind == DiagnosticsTraceDetailOperationKind.PrimaryKeyRead;
            var observed = observations
                .Where(observation => string.Equals(observation.Operation, pointRead ? "PointRead" : "BoundedQuery", StringComparison.Ordinal) &&
                                      string.Equals(observation.Target?.LogicalUnitId, unit, StringComparison.Ordinal))
                .ToArray();
            if (observed.Length == 0 || observed.Length > specification.MaxInvocationCount)
                throw new PerformanceContractException(
                    $"Diagnostics trace-detail constituent '{specification.RouteIdentity}' observed {observed.Length} typed reads of '{unit}'; expected a finite positive count no greater than {specification.MaxInvocationCount}.");
            var materialized = specification.RouteIdentity switch
            {
                "trace-detail/spans-by-trace-key-start-id" => detail.Spans.Count,
                "trace-detail/logs-by-trace-key-timestamp-id" => detail.Logs.Count,
                "trace-detail/resources-by-id" => detail.Resources.Count,
                _ => 1
            };
            var scopePredicate = DiagnosticsNativePlanContract.ExpectedStorageScopePredicate(request.Provider, specification.StorageScopeRequired);
            var constituent = new DiagnosticsTraceDetailConstituentEvidence(
                specification.RouteIdentity,
                string.Empty,
                string.Empty,
                pointRead ? DiagnosticsNativePlanContract.PrimaryKeyReadClassification : DiagnosticsNativePlanContract.IndexSearchPlanClassification,
                pointRead ? string.Empty : DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(request.Provider, DiagnosticsNativePlanContract.RouteSpecificationFor(specification)),
                string.Empty,
                specification.PhysicalCardinality,
                scopePredicate,
                true,
                specification.FiniteLimit,
                specification.PublicRowBound,
                materialized,
                observed.Length,
                specification.MaxInvocationCount,
                pointRead
                    ? null
                    : observed.Skip(1).Select((page, index) => new DiagnosticsTraceDetailPageEvidence(index + 1, string.Empty, string.Empty, string.Empty)
                    {
                        StructuredEvidence = page
                    }).ToArray())
            {
                StructuredEvidence = observed[0]
            };
            if (pointRead)
            {
                // Every fanned-out point read of the constituent is admitted; the constituent retains the first.
                foreach (var read in observed)
                    DiagnosticsNativePlanContract.ValidateStructuredTraceDetailConstituent(
                        request.Provider, request.Adapter, constituent with { StructuredEvidence = read }, observedProviderVersion);
            }
            else
                DiagnosticsNativePlanContract.ValidateStructuredTraceDetailConstituent(
                    request.Provider, request.Adapter, constituent, observedProviderVersion);
            evidence.Add(constituent);
        }
        return evidence;
    }

    private static async Task<int> InvokeBoundedResourceRouteWithoutExplainAssertionAsync(
        DiagnosticsDurableHistoryClient client,
        string route,
        int limit,
        CancellationToken cancellationToken)
    {
        using var suppression = ExplainAssertionMode.Suppress();
        var result = await QueryResourceRouteAsync(client, route, limit, cancellationToken);
        var diagnostics = await client.OpenTelemetry.GetDiagnosticsAsync(cancellationToken);
        ValidateBoundedResourcePage(route, result, diagnostics, limit);
        return result.Items.Count;
    }

    private static async Task<OpenTelemetryResourceResult> QueryResourceRouteAsync(
        DiagnosticsDurableHistoryClient client,
        string route,
        int? limit,
        CancellationToken cancellationToken)
    {
        var filter = route switch
        {
            "resources-by-last-seen" => new OpenTelemetryResourceFilter { Take = limit },
            "resources-by-status" => new OpenTelemetryResourceFilter
            {
                Status = TelemetryResourceStatus.Active,
                Take = limit
            },
            "resources-by-service" => new OpenTelemetryResourceFilter
            {
                ServiceName = DiagnosticsDurableHistoryWorkload.ServiceNameFor(0),
                Take = limit
            },
            _ => throw new PerformanceContractException($"Unsupported bounded resource route '{route}'.")
        };
        return await client.OpenTelemetry.QueryResourcesAsync(filter, cancellationToken);
    }

    internal static void ValidateBoundedResourcePage(
        string route,
        OpenTelemetryResourceResult result,
        OpenTelemetryStorageDiagnostics diagnostics,
        int limit)
    {
        if (diagnostics.ResourceCount != DiagnosticsDurableHistoryWorkload.ResourceCount)
            throw new PerformanceContractException(
                $"Diagnostics bounded resource route '{route}' observed {diagnostics.ResourceCount} scoped resources; expected {DiagnosticsDurableHistoryWorkload.ResourceCount}.");

        var expected = Enumerable.Range(0, DiagnosticsDurableHistoryWorkload.ResourceCount)
            .Select(ordinal => DiagnosticsDurableHistoryWorkload.ResourceFor(
                ordinal,
                DiagnosticsDurableHistoryWorkload.ServiceNameFor(ordinal)))
            .OrderByDescending(resource => resource.LastSeen)
            .ThenBy(resource => resource.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
        var actual = result.Items.ToArray();
        if (actual.Length != expected.Length ||
            !actual.Select(resource => resource.Id).SequenceEqual(
                expected.Select(resource => resource.Id),
                StringComparer.Ordinal))
            throw new PerformanceContractException(
                $"Diagnostics bounded resource route '{route}' did not return the frozen deterministic resource identity/order page.");

        for (var index = 0; index < expected.Length; index++)
        {
            var expectedResource = expected[index];
            var actualResource = actual[index];
            if (!string.Equals(actualResource.ServiceName, expectedResource.ServiceName, StringComparison.Ordinal) ||
                !string.Equals(actualResource.ServiceInstanceId, expectedResource.ServiceInstanceId, StringComparison.Ordinal) ||
                !string.Equals(actualResource.TelemetrySdkLanguage, expectedResource.TelemetrySdkLanguage, StringComparison.Ordinal) ||
                actualResource.Status != expectedResource.Status ||
                actualResource.LastSeen != expectedResource.LastSeen ||
                actualResource.Attributes.Count != expectedResource.Attributes.Count ||
                expectedResource.Attributes.Any(attribute =>
                    !actualResource.Attributes.TryGetValue(attribute.Key, out var value) ||
                    !string.Equals(value, attribute.Value, StringComparison.Ordinal)))
                throw new PerformanceContractException(
                    $"Diagnostics bounded resource route '{route}' returned resource '{actualResource.Id}' with non-canonical fixture fields.");
        }
    }

    internal static NativePlanEvidenceDocument CreateDocument(
        RunRequest request,
        ProviderProbe.Result observed,
        IReadOnlyList<NativeRouteEvidence> routes,
        string routeContract = "provider-native-routes",
        IReadOnlyList<string>? blockedRoutes = null,
        IReadOnlyList<DiagnosticsOracleRouteObservation>? oracleObservations = null,
        IReadOnlyList<DiagnosticsTraceDetailConstituentEvidence>? traceDetailConstituents = null) =>
        new(2, request.ComparisonCohortId, request.MeasurementSetId, request.WorkloadId, request.WorkloadVersion, request.Provider, request.Adapter, request.PhysicalForm, request.Scale, request.CommitSha, request.HarnessAssemblySha256, request.CompositionFingerprint, request.HostFingerprintSha256, observed.Version, observed.Topology, observed.Configuration, request.Seed, request.InputFingerprintSha256, request.NativePlanIdentity, routes, routeContract, blockedRoutes, oracleObservations, traceDetailConstituents);

}
