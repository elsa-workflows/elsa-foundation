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
    public static async Task<string> CaptureDueTimerAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken)
    {
        RuntimeNativePlanCaptureSupport.EnsureRequest(
            request,
            observed,
            RuntimeDueTimerSelectionWorkload.WorkloadId,
            DueTimerSelectionAdapter.PhysicalForm);
        var commandObserver = new WritePathRoundTripObserver(request.Provider, captureCommands: true);
        await using var adapter = new DueTimerSelectionAdapter(
            request,
            connectionString,
            outputDirectory,
            commandObserver);
        await adapter.PrepareAsync(cancellationToken);
        await new RuntimeDueTimerSelectionWorkload().ExecuteAsync(adapter, cancellationToken);
        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        return await CaptureRoutesAsync(
            request,
            outputDirectory,
            observed,
            RuntimeNativePlanCaptureSupport.RequireCommandObserver(adapter.RoundTripObserver),
            async specification =>
            {
                var page = await clients.Primary.ListDueAsync(
                    RuntimeDueTimerSelectionWorkload.FixedNowUtc,
                    specification.FiniteLimit,
                    cancellationToken);
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
        RuntimeNativePlanCaptureSupport.EnsureRequest(
            request,
            observed,
            RuntimeRecurringScheduleSelectionWorkload.WorkloadId,
            RecurringScheduleSelectionAdapter.PhysicalForm);
        var commandObserver = new WritePathRoundTripObserver(request.Provider, captureCommands: true);
        await using var adapter = new RecurringScheduleSelectionAdapter(
            request,
            connectionString,
            outputDirectory,
            commandObserver);
        await adapter.PrepareAsync(cancellationToken);
        await new RuntimeRecurringScheduleSelectionWorkload().ExecuteAsync(adapter, cancellationToken);
        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        return await CaptureRoutesAsync(
            request,
            outputDirectory,
            observed,
            RuntimeNativePlanCaptureSupport.RequireCommandObserver(adapter.RoundTripObserver),
            async specification =>
            {
                if (specification.RouteIdentity == "list-due")
                    return (await clients.Primary.ListDueAsync(
                        RuntimeRecurringScheduleSelectionWorkload.FixedNowUtc,
                        specification.FiniteLimit,
                        cancellationToken)).Count;

                if (specification.RouteIdentity != "page-by-publication")
                    throw new PerformanceContractException(
                        $"Unsupported recurring-schedule native route '{specification.RouteIdentity}'.");
                var projection = await clients.Primary.ListByActivationPageAsync(
                    new Elsa.Workflows.Runtime.Core.Models.RecurringTriggerScheduleActivationPageQuery(
                        "publication-0000",
                        specification.FiniteLimit),
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(projection.NextContinuationToken))
                    throw new PerformanceContractException(
                        $"Runtime schedule route '{specification.RouteIdentity}' did not retain its continuation boundary.");
                return projection.Items.Count;
            },
            cancellationToken);
    }

    private static async Task<string> CaptureRoutesAsync(
        RunRequest request,
        string outputDirectory,
        ProviderProbe.Result observed,
        WritePathRoundTripObserver observer,
        Func<RuntimeNativeRouteSpec, Task<int>> invokeRoute,
        CancellationToken cancellationToken)
    {
        var routes = new List<NativeRouteEvidence>();
        await using var explain = await NativeExplainCaptureGate.EnterAsync(
            $"groundwork-schedule-{request.Provider}-{request.MeasurementSetId}",
            cancellationToken);
        foreach (var specification in RuntimeNativePlanContract.ForWorkload(request.WorkloadId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            observer.ClearCommands();
            var before = Directory.EnumerateFiles(explain.Directory).ToHashSet(StringComparer.Ordinal);
            var materialized = await invokeRoute(specification);
            if (materialized != specification.FiniteLimit)
                throw new PerformanceContractException(
                    $"Runtime schedule route '{specification.RouteIdentity}' returned {materialized} rows; expected {specification.FiniteLimit}.");

            var command = RuntimeNativePlanCaptureSupport.RequireRouteCommand(observer.Commands, specification);
            var nativePath = RuntimeNativePlanCaptureSupport.RequireNativeArtifact(
                explain.Directory,
                before,
                request.Provider,
                specification);
            var plan = RuntimeNativePlanCaptureSupport.ParsePlan(request.Provider, File.ReadAllText(nativePath));
            var rawReference = RuntimeNativePlanCaptureSupport.RawReference(
                request,
                specification.RouteIdentity,
                request.Provider);
            var rawPath = RuntimeNativePlanCaptureSupport.WriteArtifact(
                outputDirectory,
                request,
                specification,
                command,
                plan,
                rawReference);
            var route = new NativeRouteEvidence(
                specification.RouteIdentity,
                rawReference,
                ArtifactStore.HashFile(rawPath),
                plan.PlanClassification,
                plan.PhysicalIndexName,
                specification.PhysicalCardinality,
                specification.StorageScopeRequired,
                specification.PredicateColumn is not null,
                specification.FiniteLimit,
                materialized);
            RuntimeNativePlanContract.ValidateEnvelope(
                request.WorkloadId,
                request.Provider,
                request.Adapter,
                route,
                rawPath);
            routes.Add(route);
        }

        return NativePlanEvidenceStaging.Write(
            outputDirectory,
            RuntimeNativePlanCaptureSupport.CreateDocument(request, observed, routes));
    }
}
