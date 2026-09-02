using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Groundwork.Kernel;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>Captures the bounded due-timer route from the public durable-timer store.</summary>
internal static class DueTimerNativePlanCapture
{
    public static Task<string> CaptureAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken = default) =>
        RuntimeScheduleNativePlanCapture.CaptureDueTimerAsync(
            request, connectionString, outputDirectory, observed, cancellationToken);
}

/// <summary>Captures the bounded due and publication-page routes from the public recurring-schedule store.</summary>
internal static class RecurringScheduleNativePlanCapture
{
    public static Task<string> CaptureAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken = default) =>
        RuntimeScheduleNativePlanCapture.CaptureRecurringScheduleAsync(
            request, connectionString, outputDirectory, observed, cancellationToken);
}

internal static class RuntimeScheduleNativePlanCapture
{
    // The current schedule stores do not pass a selected index to Groundwork.Query, so their public
    // routes do not produce a diagnostics artifact. The route capture remains implemented for the
    // selected-index provider contract and deliberately fails closed until that upstream seam exists.
    private const string RouteContract = "provider-native-routes";
    private static readonly SemaphoreSlim ExplainEnvironmentGate = new(1, 1);

    public static async Task<string> CaptureDueTimerAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken)
    {
        EnsureRequest(request, observed, RuntimeDueTimerSelectionWorkload.WorkloadId, DueTimerSelectionAdapter.PhysicalForm);
        var commandObserver = new WritePathRoundTripObserver(request.Provider, captureCommands: true);
        await using var adapter = new DueTimerSelectionAdapter(request, connectionString, outputDirectory, commandObserver);
        await adapter.PrepareAsync(cancellationToken);
        await new RuntimeDueTimerSelectionWorkload().ExecuteAsync(adapter, cancellationToken);
        var observer = RequireObserver(adapter.RoundTripObserver);
        return await CaptureRoutesAsync(
            request,
            outputDirectory,
            observed,
            observer,
            [RuntimeScheduleNativePlan.Definition(request.WorkloadId, "list-due")],
            async definition =>
            {
                var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
                var page = await clients.Primary.ListDueAsync(RuntimeDueTimerSelectionWorkload.FixedNowUtc, definition.FiniteLimit, cancellationToken);
                if (page.Count != definition.MaterializedCandidateCount)
                    throw new PerformanceContractException(
                        $"Schedule native route '{definition.RouteIdentity}' materialized {page.Count} rows; expected exactly {definition.MaterializedCandidateCount}.");
                return page.Count;
            },
            cancellationToken);
    }

    public static async Task<string> CaptureRecurringScheduleAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken)
    {
        EnsureRequest(request, observed, RuntimeRecurringScheduleSelectionWorkload.WorkloadId, RecurringScheduleSelectionAdapter.PhysicalForm);
        var commandObserver = new WritePathRoundTripObserver(request.Provider, captureCommands: true);
        await using var adapter = new RecurringScheduleSelectionAdapter(request, connectionString, outputDirectory, commandObserver);
        await adapter.PrepareAsync(cancellationToken);
        await new RuntimeRecurringScheduleSelectionWorkload().ExecuteAsync(adapter, cancellationToken);
        var observer = RequireObserver(adapter.RoundTripObserver);
        return await CaptureRoutesAsync(
            request,
            outputDirectory,
            observed,
            observer,
            [
                RuntimeScheduleNativePlan.Definition(request.WorkloadId, "list-due"),
                RuntimeScheduleNativePlan.Definition(request.WorkloadId, "page-by-publication")
            ],
            async definition =>
            {
                var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
                if (definition.RouteIdentity == "list-due")
                {
                    var page = await clients.Primary.ListDueAsync(RuntimeRecurringScheduleSelectionWorkload.FixedNowUtc, definition.FiniteLimit, cancellationToken);
                    if (page.Count != definition.MaterializedCandidateCount)
                        throw new PerformanceContractException(
                            $"Schedule native route '{definition.RouteIdentity}' materialized {page.Count} rows; expected exactly {definition.MaterializedCandidateCount}.");
                    return page.Count;
                }

                var projection = await clients.Primary.ListByActivationPageAsync(
                    new Elsa.Workflows.Runtime.Core.Models.RecurringTriggerScheduleActivationPageQuery(
                        "publication-0000",
                        definition.FiniteLimit),
                    cancellationToken);
                if (projection.Items.Count != definition.MaterializedCandidateCount ||
                    string.IsNullOrWhiteSpace(projection.NextContinuationToken))
                    throw new PerformanceContractException(
                        $"Schedule native route '{definition.RouteIdentity}' did not materialize the exact finite page and continuation boundary.");
                return projection.Items.Count;
            },
            cancellationToken);
    }

    private static async Task<string> CaptureRoutesAsync(
        RunRequest request,
        string outputDirectory,
        ProviderProbe.Result observed,
        WritePathRoundTripObserver observer,
        IReadOnlyList<RuntimeScheduleNativePlan.RouteDefinition> definitions,
        Func<RuntimeScheduleNativePlan.RouteDefinition, Task<int>> invokeRoute,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var explainDirectory = Path.Combine(
            Path.GetTempPath(),
            $"groundwork-schedule-explain-{request.Provider}-{request.MeasurementSetId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(explainDirectory);
        await ExplainEnvironmentGate.WaitAsync(cancellationToken);
        var previousFlag = Environment.GetEnvironmentVariable("GW_EXPLAIN_ASSERT");
        var previousDirectory = Environment.GetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR");
        try
        {
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", "1");
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", explainDirectory);
            var routes = new List<NativeRouteEvidence>(definitions.Count);
            foreach (var definition in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                observer.ClearCommands();
                var artifactsBefore = Directory.EnumerateFiles(explainDirectory).ToHashSet(StringComparer.Ordinal);
                var materialized = await invokeRoute(definition);
                if (materialized != definition.MaterializedCandidateCount)
                    throw new PerformanceContractException(
                        $"Schedule native route '{definition.RouteIdentity}' materialized {materialized} candidates; expected {definition.MaterializedCandidateCount}.");

                var command = RequireRouteCommand(observer.Commands, request.Provider, definition);
                var nativePlanPath = RequireNativePlanArtifact(
                    explainDirectory,
                    artifactsBefore,
                    request.Provider,
                    definition);
                var nativePlan = IamNativePlanParser.Parse(request.Provider, File.ReadAllText(nativePlanPath));
                var normalizedPlan = IamNativePlanParser.NormalizeForArtifact(request.Provider, nativePlan.Content);
                nativePlan = IamNativePlanParser.Parse(request.Provider, normalizedPlan);
                var route = new NativeRouteEvidence(
                    definition.RouteIdentity,
                    ArtifactStore.RawPlanName(
                        $"schedule.{request.WorkloadId}.{request.Provider}.{request.MeasurementSetId}.{definition.RouteIdentity}.raw{IamNativePlanParser.RawPlanExtension(request.Provider)}"),
                    new string('0', 64),
                    nativePlan.PlanClassification,
                    nativePlan.PhysicalIndexName,
                    definition.PhysicalCardinality,
                    command.CommandText?.Contains("__groundwork_scope", StringComparison.Ordinal) == true,
                    definition.PredicateFields.All(field => command.CommandText?.Contains(field, StringComparison.Ordinal) == true),
                    definition.FiniteLimit,
                    materialized);
                var retained = RuntimeScheduleNativePlan.Create(
                    request.Provider,
                    request.WorkloadId,
                    definition.RouteIdentity,
                    command.CommandText ?? throw new PerformanceContractException(
                        $"Schedule native route '{definition.RouteIdentity}' did not expose provider command text."),
                    normalizedPlan);
                RuntimeScheduleNativePlan.Validate(request.Provider, route, retained);

                var rawPlanPath = Path.Combine(outputDirectory, route.RawPlanReference);
                File.WriteAllText(rawPlanPath, retained);
                ArtifactStore.ValidateRawPlanFile(rawPlanPath);
                routes.Add(route with { RawPlanSha256 = NativePlanEvidenceStaging.Sha256(rawPlanPath) });
            }

            return NativePlanEvidenceStaging.Write(
                outputDirectory,
                new NativePlanEvidenceDocument(
                    2,
                    request.ComparisonCohortId,
                    request.MeasurementSetId,
                    request.WorkloadId,
                    request.WorkloadVersion,
                    request.Provider,
                    request.Adapter,
                    request.PhysicalForm,
                    request.Scale,
                    request.CommitSha,
                    request.HarnessAssemblySha256,
                    request.CompositionFingerprint,
                    request.HostFingerprintSha256,
                    observed.Version,
                    observed.Topology,
                    observed.Configuration,
                    request.Seed,
                    request.InputFingerprintSha256,
                    request.NativePlanIdentity,
                    routes,
                    RouteContract));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ASSERT", previousFlag);
            Environment.SetEnvironmentVariable("GW_EXPLAIN_ARTIFACT_DIR", previousDirectory);
            ExplainEnvironmentGate.Release();
            try
            {
                if (Directory.Exists(explainDirectory))
                    Directory.Delete(explainDirectory, recursive: true);
            }
            catch
            {
                // Retained artifacts are complete; temporary diagnostics cleanup must not mask capture.
            }
        }
    }

    private static void EnsureRequest(
        RunRequest request,
        ProviderProbe.Result observed,
        string workloadId,
        string physicalForm)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observed);
        if (!string.Equals(request.WorkloadId, workloadId, StringComparison.Ordinal) ||
            !string.Equals(request.WorkloadVersion, "1.1.0", StringComparison.Ordinal) ||
            !string.Equals(request.PhysicalForm, physicalForm, StringComparison.Ordinal) ||
            !string.Equals(
                request.NativePlanEvidenceReference,
                NativePlanEvidenceStaging.ReferenceFor(request.WorkloadId, request.Provider, request.MeasurementSetId),
                StringComparison.Ordinal) ||
            observed.Provider != request.Provider ||
            observed.Version != request.ProviderVersion ||
            observed.Topology != request.ProviderTopology ||
            !observed.Configuration.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(request.ProviderConfiguration.OrderBy(pair => pair.Key, StringComparer.Ordinal)))
            throw new PerformanceContractException(
                "Schedule native-plan capture request does not match the live provider and frozen route contract.");
    }

    private static WritePathRoundTripObserver RequireObserver(IProviderRoundTripObserver? observer) =>
        observer as WritePathRoundTripObserver
        ?? throw new PerformanceContractException(
            "Schedule native-plan capture requires the exact Groundwork provider-command observer.");

    private static ProviderCommandEvent RequireRouteCommand(
        IReadOnlyList<ProviderCommandEvent> commands,
        string provider,
        RuntimeScheduleNativePlan.RouteDefinition definition)
    {
        var matches = commands.Where(command =>
                !command.IsProbe &&
                command.Kind == ProviderCommandKind.Read &&
                string.Equals(command.Operation, provider + ".query", StringComparison.Ordinal) &&
                command.CommandText?.Contains(definition.TableName, StringComparison.Ordinal) == true &&
                definition.PredicateFields.All(field => command.CommandText.Contains(field, StringComparison.Ordinal)))
            .ToArray();
        if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].CommandText))
            throw new PerformanceContractException(
                $"Schedule native route '{definition.RouteIdentity}' must emit exactly one observable provider query against '{definition.TableName}'; observed {matches.Length}. Commands: {string.Join(" || ", commands.Select(command => $"{command.Operation}/{command.Kind}/{command.IsProbe}: {command.CommandText}"))}");
        return matches[0];
    }

    private static string RequireNativePlanArtifact(
        string directory,
        IReadOnlySet<string> artifactsBefore,
        string provider,
        RuntimeScheduleNativePlan.RouteDefinition definition)
    {
        var suffix = $"-{definition.IndexName}{IamNativePlanParser.RawPlanExtension(provider)}";
        var matches = Directory.EnumerateFiles(directory)
            .Where(path => !artifactsBefore.Contains(path))
            .Where(path => Path.GetFileName(path).EndsWith(suffix, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (matches.Length != 1)
            throw new PerformanceContractException(
                $"Schedule native route '{definition.RouteIdentity}' must emit exactly one provider-native explain artifact for logical index '{definition.IndexName}'; observed {matches.Length}.");
        return matches[0];
    }
}
