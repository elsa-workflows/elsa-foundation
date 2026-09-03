using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Groundwork.Diagnostics;
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
        foreach (var batch in DiagnosticsDurableHistoryWorkload.NativePlanFixtureBatches())
            await scopes.Primary.OpenTelemetry.WriteAsync(batch, cancellationToken);
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
                var traceDetailConstituents = new List<DiagnosticsTraceDetailConstituentEvidence>();
                var blockedRoutes = new List<string>();
                foreach (var (route, limit) in DiagnosticsDurableHistoryWorkload.NativeRouteLimits)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var specification = DiagnosticsNativePlanContract.For(request.Adapter, route);

                    if (route == "trace-detail")
                    {
                        adapter.CommandObserver.ClearCommands();
                        try
                        {
                            traceDetailConstituents.AddRange(await CaptureTraceDetailConstituentsAsync(
                                scopes.Primary,
                                request,
                                outputDirectory,
                                explainDirectory,
                                adapter.CommandObserver,
                                cancellationToken));
                        }
                        catch (PerformanceContractException exception) when (DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception))
                        {
                            blockedRoutes.Add(route);
                        }
                        catch (ExplainAssertionException)
                        {
                            // Groundwork's assertion mode has already retained the provider artifact;
                            // an unchosen index/scan is an honest blocked composite, never evidence that
                            // the public call used a bounded native plan.
                            blockedRoutes.Add(route);
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
                        continue;
                    }

                    adapter.CommandObserver.ClearCommands();
                    var before = Directory.EnumerateFiles(explainDirectory).ToHashSet(StringComparer.Ordinal);
                    var result = await InvokeRouteAsync(scopes.Primary, route, limit, cancellationToken);
                    if (result != limit)
                        throw new PerformanceContractException($"Diagnostics native route '{route}' returned {result} rows; expected {limit}.");
                    var physicalIndexName = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(request.Provider, specification);
                    var nativePath = RequireNativeArtifacts(
                        explainDirectory,
                        before,
                        request.Provider,
                        specification.IndexName,
                        1)[0];
                    var rawPlan = IamNativePlanParser.NormalizeForArtifact(request.Provider, File.ReadAllText(nativePath));
                    var command = string.Equals(request.Provider, "mongodb", StringComparison.Ordinal)
                        ? CaptureMongoCommand(adapter.CommandObserver.Commands, specification, rawPlan)
                        : RequireGroundworkCommand(adapter.CommandObserver.Commands, specification);
                    var rawReference = ArtifactStore.RawPlanName($"diagnostics.{request.Provider}.{request.MeasurementSetId}.{route}.raw.json");
                    var rawPath = Path.Combine(outputDirectory, rawReference);
                    var artifact = new DiagnosticsNativePlanArtifact(1, request.Provider, request.Adapter, route, specification.TableName, specification.IndexName, physicalIndexName, command, rawPlan);
                    var routeEvidence = new NativeRouteEvidence(
                        route,
                        rawReference,
                        string.Empty,
                        "index-search",
                        physicalIndexName,
                        specification.PhysicalCardinality,
                        DiagnosticsNativePlanContract.ExpectedStorageScopePredicate(request.Provider, specification),
                        specification.PredicateColumn is not null,
                        limit,
                        limit);

                    // Validate the provider-owned plan before publishing the retained envelope. A
                    // provider may still choose a sort/spill/scan for a route whose declaration has an
                    // index; that is blocked evidence, never a synthetic index-search claim.
                    var validationPath = Path.Combine(Path.GetTempPath(), $"diagnostics-native-plan-{Guid.NewGuid():N}.json");
                    try
                    {
                        WriteEnvelope(validationPath, artifact);
                        try
                        {
                            DiagnosticsNativePlanContract.ValidateEnvelope(request.Provider, request.Adapter, routeEvidence, validationPath);
                        }
                        catch (PerformanceContractException exception) when (DiagnosticsNativePlanContract.IsExpectedBlockedPlanFailure(exception))
                        {
                            blockedRoutes.Add(route);
                            continue;
                        }

                        WriteEnvelope(rawPath, artifact);
                        routes.Add(routeEvidence with { RawPlanSha256 = ArtifactStore.HashFile(rawPath) });
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(validationPath))
                                File.Delete(validationPath);
                        }
                        catch
                        {
                            // The temporary validation copy is not part of the retained artifact set.
                        }
                    }
                }
                var routeContract = blockedRoutes.Count == 0
                    ? "provider-native-routes"
                    : DiagnosticsNativePlanContract.BlockedRouteContract;
                return NativePlanEvidenceStaging.Write(
                    outputDirectory,
                    CreateDocument(request, observed, routes, routeContract, blockedRoutes, traceDetailConstituents: traceDetailConstituents));
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
        foreach (var batch in DiagnosticsDurableHistoryWorkload.NativePlanFixtureBatches())
            await scopes.Primary.OpenTelemetry.WriteAsync(batch, cancellationToken);
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

    private static async Task<IReadOnlyList<DiagnosticsTraceDetailConstituentEvidence>> CaptureTraceDetailConstituentsAsync(
        DiagnosticsDurableHistoryClient client,
        RunRequest request,
        string outputDirectory,
        string explainDirectory,
        WritePathRoundTripObserver commandObserver,
        CancellationToken cancellationToken)
    {
        var specifications = DiagnosticsNativePlanContract.TraceDetailConstituents(request.Adapter);
        var beforeTraceDetail = Directory.EnumerateFiles(explainDirectory).ToHashSet(StringComparer.Ordinal);
        var detail = await client.OpenTelemetry.GetTraceAsync(
            DiagnosticsDurableHistoryWorkload.TraceIdForTesting(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream - 1),
            cancellationToken);
        if (detail is null)
            throw new PerformanceContractException("Diagnostics trace-detail capture did not find its fixture trace.");

        var mongo = string.Equals(request.Provider, "mongodb", StringComparison.Ordinal);
        var observedReads = commandObserver.Commands
            .Where(command => !command.IsProbe && command.Kind == ProviderCommandKind.Read)
            .ToArray();
        if (mongo)
            RequireKnownMongoReadOperations(observedReads);
        var commands = observedReads
            .Where(command => !string.IsNullOrWhiteSpace(command.CommandText))
            .ToArray();
        var mongoQueryCommands = mongo
            ? commands.Where(command => command.Operation == "mongodb.query").ToArray()
            : [];
        var mongoPointCommands = mongo
            ? commands.Where(command => command.Operation == "mongodb.read").ToArray()
            : [];
        var mongoPointCommandsByRoute = ClassifyMongoPointReads(mongoPointCommands, specifications);
        var mongoQueryOffset = 0;
        var evidence = new List<DiagnosticsTraceDetailConstituentEvidence>(specifications.Count);
        foreach (var specification in specifications)
        {
            ProviderCommandEvent[] queryCommands;
            ProviderCommandEvent[] pointCommands;
            if (mongo)
            {
                queryCommands = specification.OperationKind == DiagnosticsTraceDetailOperationKind.BoundedOrderedQuery
                    ? mongoQueryCommands.Skip(mongoQueryOffset).Take(specification.MaxInvocationCount).ToArray()
                    : [];
                pointCommands = specification.OperationKind == DiagnosticsTraceDetailOperationKind.PrimaryKeyRead
                    ? mongoPointCommandsByRoute.GetValueOrDefault(specification.RouteIdentity)?.ToArray() ?? []
                    : [];
                mongoQueryOffset += queryCommands.Length;
            }
            else
            {
                var matching = commands.Where(command =>
                    command.CommandText!.Contains(specification.TableName, StringComparison.OrdinalIgnoreCase)).ToArray();
                queryCommands = matching.Where(command => command.Operation.EndsWith(".query", StringComparison.Ordinal)).ToArray();
                pointCommands = matching.Where(command => !command.Operation.EndsWith(".query", StringComparison.Ordinal)).ToArray();
            }
            var observedCount = specification.OperationKind == DiagnosticsTraceDetailOperationKind.BoundedOrderedQuery
                ? queryCommands.Length
                : pointCommands.Length;
            if (observedCount == 0 || observedCount > specification.MaxInvocationCount)
                throw new PerformanceContractException(
                    $"Diagnostics trace-detail constituent '{specification.RouteIdentity}' observed {observedCount} provider commands; expected a finite positive count no greater than {specification.MaxInvocationCount}.");

            var materialized = specification.RouteIdentity switch
            {
                "trace-detail/spans-by-trace-key-start-id" => detail.Spans.Count,
                "trace-detail/logs-by-trace-key-timestamp-id" => detail.Logs.Count,
                "trace-detail/resources-by-id" => detail.Resources.Count,
                _ => 1
            };
            if (specification.OperationKind == DiagnosticsTraceDetailOperationKind.PrimaryKeyRead)
            {
                var command = pointCommands[0].CommandText!;
                var pointEvidence = new DiagnosticsTraceDetailConstituentEvidence(
                    specification.RouteIdentity,
                    "",
                    "",
                    "primary-key-read",
                    "",
                    command,
                    specification.PhysicalCardinality,
                    DiagnosticsNativePlanContract.ExpectedStorageScopePredicate(
                        request.Provider,
                        specification.StorageScopeRequired),
                    true,
                    specification.FiniteLimit,
                    specification.PublicRowBound,
                    materialized,
                    observedCount,
                    specification.MaxInvocationCount);
                foreach (var pointCommand in pointCommands)
                    DiagnosticsNativePlanContract.ValidateTraceDetailConstituent(
                        request.Provider,
                        request.Adapter,
                        pointEvidence with { CommandText = pointCommand.CommandText! },
                        null);
                evidence.Add(pointEvidence);
                continue;
            }

            var expectedPageCount = checked((specification.PublicRowBound + specification.FiniteLimit - 1) / specification.FiniteLimit);
            if (queryCommands.Length != expectedPageCount)
                throw new PerformanceContractException(
                    $"Diagnostics trace-detail constituent '{specification.RouteIdentity}' must emit exactly {expectedPageCount} bounded page queries in the frozen fixture; observed {queryCommands.Length}.");
            var physicalIndexName = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(request.Provider, new DiagnosticsNativeRouteSpec(
                specification.RouteIdentity,
                specification.TableName,
                specification.IndexName,
                specification.Ordering[0].Column,
                specification.PredicateColumn,
                specification.PhysicalCardinality,
                specification.FiniteLimit,
                specification.StorageScopeRequired,
                false,
                specification.Ordering));
            // ExplainAssertionMode numbers and retains one artifact per provider command. Keep every
            // page, including the keyset continuation pages, so admission can reparse every command
            // and every provider-owned plan instead of treating a multi-page QueryAll as one route.
            var nativePaths = RequireNativeArtifacts(
                explainDirectory,
                beforeTraceDetail,
                request.Provider,
                specification.IndexName,
                queryCommands.Length);
            var pages = new List<DiagnosticsTraceDetailPageEvidence>(queryCommands.Length);
            for (var pageIndex = 0; pageIndex < queryCommands.Length; pageIndex++)
            {
                var rawPlan = IamNativePlanParser.NormalizeForArtifact(request.Provider, File.ReadAllText(nativePaths[pageIndex]));
                var pageCommand = mongo
                    ? MongoExplainCommandInspector.SerializeCommand(
                        MongoExplainCommandInspector.ExtractCommand(rawPlan))
                    : queryCommands[pageIndex].CommandText!;
                var pageReference = ArtifactStore.RawPlanName(
                    $"diagnostics.{request.Provider}.{request.MeasurementSetId}.{ConstituentSlug(specification.RouteIdentity)}.page-{pageIndex:D4}.raw.json");
                var pagePath = Path.Combine(outputDirectory, pageReference);
                var pageArtifact = new DiagnosticsNativePlanArtifact(
                    1,
                    request.Provider,
                    request.Adapter,
                    specification.RouteIdentity,
                    specification.TableName,
                    specification.IndexName,
                    physicalIndexName,
                    pageCommand,
                    rawPlan);
                var pageEvidence = new DiagnosticsTraceDetailConstituentEvidence(
                    specification.RouteIdentity,
                    pageReference,
                    string.Empty,
                    "index-search",
                    physicalIndexName,
                    pageCommand,
                    specification.PhysicalCardinality,
                    DiagnosticsNativePlanContract.ExpectedStorageScopePredicate(
                        request.Provider,
                        specification.StorageScopeRequired),
                    true,
                    specification.FiniteLimit,
                    specification.PublicRowBound,
                    materialized,
                    observedCount,
                    specification.MaxInvocationCount);
                var pageSha256 = ValidateAndPublishTraceDetailPage(
                    request.Provider,
                    request.Adapter,
                    pageEvidence,
                    pageArtifact,
                    pagePath);

                pages.Add(new DiagnosticsTraceDetailPageEvidence(
                    pageIndex,
                    pageReference,
                    pageSha256,
                    pageCommand));
            }

            var firstPage = pages[0];
            var constituentEvidence = new DiagnosticsTraceDetailConstituentEvidence(
                specification.RouteIdentity,
                firstPage.RawPlanReference,
                firstPage.RawPlanSha256,
                "index-search",
                physicalIndexName,
                firstPage.CommandText,
                specification.PhysicalCardinality,
                DiagnosticsNativePlanContract.ExpectedStorageScopePredicate(
                    request.Provider,
                    specification.StorageScopeRequired),
                true,
                specification.FiniteLimit,
                specification.PublicRowBound,
                materialized,
                observedCount,
                specification.MaxInvocationCount,
                pages.Skip(1).ToArray());
            evidence.Add(constituentEvidence);
        }

        if (mongo && mongoQueryOffset != mongoQueryCommands.Length)
            throw new PerformanceContractException(
                $"Diagnostics trace-detail capture observed unclassified MongoDB provider commands: " +
                $"query={mongoQueryCommands.Length - mongoQueryOffset}.");

        return evidence;
    }

    internal static void RequireKnownMongoReadOperations(IEnumerable<ProviderCommandEvent> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var reads = commands
            .Where(command => !command.IsProbe && command.Kind == ProviderCommandKind.Read)
            .ToArray();
        var unknown = reads
            .Where(command => command.Operation is not ("mongodb.query" or "mongodb.read"))
            .Select(command => command.Operation)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length != 0)
            throw new PerformanceContractException(
                $"Diagnostics trace-detail capture observed unsupported MongoDB read operations: {string.Join(", ", unknown)}.");
        if (reads.Any(command => string.IsNullOrWhiteSpace(command.CommandText)))
            throw new PerformanceContractException(
                "Diagnostics trace-detail capture observed a MongoDB read without command identity evidence.");
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<ProviderCommandEvent>> ClassifyMongoPointReads(
        IEnumerable<ProviderCommandEvent> commands,
        IReadOnlyList<DiagnosticsTraceDetailConstituentSpec> specifications)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(specifications);
        var result = new Dictionary<string, List<ProviderCommandEvent>>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            var collection = DiagnosticsNativePlanContract.RequireMongoPointCollection(command.CommandText!);
            var matches = specifications.Where(specification =>
                specification.OperationKind == DiagnosticsTraceDetailOperationKind.PrimaryKeyRead &&
                collection.StartsWith(specification.TableName + "__scope__", StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new PerformanceContractException(
                    $"Diagnostics trace-detail capture could not bind MongoDB point read collection '{collection}' to exactly one constituent.");
            if (!result.TryGetValue(matches[0].RouteIdentity, out var routeCommands))
            {
                routeCommands = [];
                result.Add(matches[0].RouteIdentity, routeCommands);
            }
            routeCommands.Add(command);
        }
        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ProviderCommandEvent>)pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    private static string ConstituentSlug(string identity) => identity.Replace('/', '-');

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

    private static string CaptureMongoCommand(
        IReadOnlyList<ProviderCommandEvent> commands,
        DiagnosticsNativeRouteSpec specification,
        string rawPlan)
    {
        var matches = commands.Where(command => !command.IsProbe &&
            command.Operation.EndsWith(".query", StringComparison.Ordinal) &&
            command.Kind == ProviderCommandKind.Read).ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException(
                $"Diagnostics route '{specification.RouteIdentity}' must emit exactly one MongoDB provider query; observed {matches.Length}.");

        // MongoDB query observers expose a descriptive label. The retained explain response is the
        // authoritative source of the physical collection, filter, ordering, and limit, so persist
        // that actual command verbatim.
        return MongoExplainCommandInspector.SerializeCommand(
            MongoExplainCommandInspector.ExtractCommand(rawPlan));
    }

    internal static IReadOnlyList<string> RequireNativeArtifacts(
        string directory,
        IReadOnlySet<string> before,
        string provider,
        string logicalIndexName,
        int expectedCount)
    {
        var extension = IamNativePlanParser.RawPlanExtension(provider);
        var suffix = $"-{logicalIndexName}{extension}";
        var matches = Directory.EnumerateFiles(directory)
            .Where(path => !before.Contains(path) &&
                           Path.GetFileName(path).EndsWith(suffix, StringComparison.Ordinal))
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        if (matches.Length != expectedCount)
            throw new PerformanceContractException(
                $"Diagnostics trace-detail query for logical index '{logicalIndexName}' must emit exactly {expectedCount} provider-native explain artifacts; observed {matches.Length}.");
        return matches;
    }

    internal static string ValidateAndPublishTraceDetailPage(
        string provider,
        string adapter,
        DiagnosticsTraceDetailConstituentEvidence evidence,
        DiagnosticsNativePlanArtifact artifact,
        string path)
    {
        var validationPath = Path.Combine(Path.GetTempPath(), $"diagnostics-trace-detail-{Guid.NewGuid():N}.json");
        try
        {
            WriteEnvelope(validationPath, artifact);
            var validatedSha256 = ArtifactStore.HashFile(validationPath);
            DiagnosticsNativePlanContract.ValidateTraceDetailConstituent(
                provider,
                adapter,
                evidence with { RawPlanSha256 = validatedSha256 },
                validationPath);

            WriteEnvelope(path, artifact);
            var retainedSha256 = ArtifactStore.HashFile(path);
            if (!string.Equals(validatedSha256, retainedSha256, StringComparison.Ordinal))
                throw new PerformanceContractException(
                    $"Diagnostics trace-detail query '{evidence.RouteIdentity}' retained a different provider-native plan than the artifact admitted for publication.");
            return retainedSha256;
        }
        finally
        {
            if (File.Exists(validationPath))
                File.Delete(validationPath);
        }
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
        IReadOnlyList<DiagnosticsOracleRouteObservation>? oracleObservations = null,
        IReadOnlyList<DiagnosticsTraceDetailConstituentEvidence>? traceDetailConstituents = null) =>
        new(2, request.ComparisonCohortId, request.MeasurementSetId, request.WorkloadId, request.WorkloadVersion, request.Provider, request.Adapter, request.PhysicalForm, request.Scale, request.CommitSha, request.HarnessAssemblySha256, request.CompositionFingerprint, request.HostFingerprintSha256, observed.Version, observed.Topology, observed.Configuration, request.Seed, request.InputFingerprintSha256, request.NativePlanIdentity, routes, routeContract, blockedRoutes, oracleObservations, traceDetailConstituents);

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
