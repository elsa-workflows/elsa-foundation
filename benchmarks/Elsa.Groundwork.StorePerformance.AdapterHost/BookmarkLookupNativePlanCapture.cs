using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Captures each bounded public bookmark-stimulus route against the production Groundwork v2 store.
/// Fixture writes and the correctness baseline run before explain capture; only the two declared
/// route calls run inside the capture scope.
/// </summary>
internal static class BookmarkLookupNativePlanCapture
{
    public static async Task<string> CaptureAsync(
        RunRequest request,
        string connectionString,
        string outputDirectory,
        ProviderProbe.Result observed,
        CancellationToken cancellationToken = default)
    {
        RuntimeNativePlanCaptureSupport.EnsureRequest(
            request,
            observed,
            RuntimeBookmarkLookupWorkload.WorkloadId,
            BookmarkLookupAdapter.PhysicalForm);

        var commandObserver = new WritePathRoundTripObserver(request.Provider, captureCommands: true);
        await using var adapter = new BookmarkLookupAdapter(
            request,
            connectionString,
            outputDirectory,
            commandObserver);
        await adapter.PrepareAsync(cancellationToken);
        await new RuntimeBookmarkLookupWorkload().ExecuteAsync(adapter, cancellationToken);
        var scopes = await adapter.OpenIsolatedScopesAsync(cancellationToken);
        var observer = RuntimeNativePlanCaptureSupport.RequireCommandObserver(adapter.RoundTripObserver);
        var routes = new List<NativeRouteEvidence>(2);

        await using var explain = await NativeExplainCaptureGate.EnterAsync(
            $"groundwork-bookmark-{request.Provider}-{request.MeasurementSetId}",
            cancellationToken);
        foreach (var specification in RuntimeNativePlanContract.ForWorkload(request.WorkloadId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            observer.ClearCommands();
            var before = Directory.EnumerateFiles(explain.Directory).ToHashSet(StringComparer.Ordinal);
            var materialized = await InvokeRouteAsync(scopes.Primary, specification.RouteIdentity, cancellationToken);
            if (materialized != specification.FiniteLimit)
                throw new PerformanceContractException(
                    $"Runtime bookmark route '{specification.RouteIdentity}' returned {materialized} rows; expected {specification.FiniteLimit}.");

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

    private static async Task<int> InvokeRouteAsync(
        RuntimeBookmarkLookupScope scope,
        string route,
        CancellationToken cancellationToken)
    {
        var query = route switch
        {
            "list-by-stimulus-and-type" => (await scope.BookmarkStimulusIndex.ListByStimulusPageAsync(
                new BookmarkStimulusPageQuery(
                    RuntimeBookmarkLookupWorkload.PrimaryStimulusType,
                    RuntimeBookmarkLookupWorkload.PrimaryStimulusHash,
                    RuntimeBookmarkLookupWorkload.PageSize),
                cancellationToken)).Items.Count,
            "list-by-stimulus-type" => (await scope.BookmarkStimulusIndex.ListByStimulusTypePageAsync(
                new BookmarkStimulusTypePageQuery(
                    RuntimeBookmarkLookupWorkload.PrimaryStimulusType,
                    RuntimeBookmarkLookupWorkload.PageSize),
                cancellationToken)).Items.Count,
            _ => throw new PerformanceContractException($"Unsupported runtime bookmark route '{route}'.")
        };
        return query;
    }
}
