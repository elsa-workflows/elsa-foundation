using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Captures the three current public trigger/source-reference routes from the production Groundwork
/// v2 stores. Fixture writes and correctness reads are public workload operations; only the three
/// declared bounded route calls execute while explain capture is enabled.
/// </summary>
internal static class TriggerBindingNativePlanCapture
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
            RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId,
            TriggerBindingStimulusLookupAdapter.PhysicalForm);

        await using var adapter = new TriggerBindingStimulusLookupAdapter(
            request,
            connectionString,
            outputDirectory,
            captureCommands: true);
        await adapter.PrepareAsync(cancellationToken);
        await new RuntimeTriggerBindingStimulusLookupWorkload().ExecuteAsync(adapter, cancellationToken);
        var scopes = await adapter.OpenIsolatedScopesAsync(cancellationToken);
        var observer = adapter.CommandObserver;
        var routes = new List<NativeRouteEvidence>(3);

        await using var explain = await NativeExplainCaptureGate.EnterAsync(
            $"groundwork-trigger-{request.Provider}-{request.MeasurementSetId}",
            cancellationToken);
        foreach (var specification in RuntimeNativePlanContract.ForWorkload(request.WorkloadId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            observer.ClearCommands();
            var before = Directory.EnumerateFiles(explain.Directory).ToHashSet(StringComparer.Ordinal);
            var materialized = await InvokeRouteAsync(scopes.Primary, specification.RouteIdentity, cancellationToken);
            if (materialized != specification.FiniteLimit)
                throw new PerformanceContractException(
                    $"Runtime trigger route '{specification.RouteIdentity}' returned {materialized} rows; expected {specification.FiniteLimit}.");

            var command = RuntimeNativePlanCaptureSupport.RequireRouteCommand(observer.Commands, specification);
            var nativePath = RuntimeNativePlanCaptureSupport.RequireNativeArtifact(
                explain.Directory,
                before,
                request.Provider,
                specification);
            var plan = RuntimeNativePlanCaptureSupport.ParsePlan(request.Provider, File.ReadAllText(nativePath));
            var rawReference = RuntimeNativePlanCaptureSupport.RawReference(request, specification.RouteIdentity, request.Provider);
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
                materialized)
                {
                    NativeFetchLimit = specification.NativeFetchLimit
                };
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
        RuntimeTriggerBindingStimulusLookupScope scope,
        string route,
        CancellationToken cancellationToken)
    {
        return route switch
        {
            "list-by-stimulus-and-type" => (await scope.TriggerBindings.ListByStimulusAsync(
                new WorkflowTriggerBindingPageQuery(
                    "runtime-trigger-stimulus",
                    "sha256:trigger-binding-primary",
                    RuntimeTriggerBindingStimulusLookupWorkload.PageSize),
                cancellationToken)).Items.Count,
            "list-by-stimulus-type" => (await scope.TriggerBindings.ListByStimulusTypeAsync(
                new WorkflowTriggerBindingTypePageQuery(
                    "runtime-trigger-stimulus",
                    RuntimeTriggerBindingStimulusLookupWorkload.PageSize),
                cancellationToken)).Items.Count,
            "page-live-by-scope" => (await scope.SourceReferences.ListPageAsync(
                new WorkflowExecutableSourceReferencePageQuery(
                    WorkflowExecutableReferenceScope.Published,
                    liveOnly: true,
                    now: new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero),
                    limit: RuntimeTriggerBindingStimulusLookupWorkload.PageSize),
                cancellationToken)).Items.Count,
            _ => throw new PerformanceContractException($"Unsupported runtime trigger route '{route}'.")
        };
    }
}
