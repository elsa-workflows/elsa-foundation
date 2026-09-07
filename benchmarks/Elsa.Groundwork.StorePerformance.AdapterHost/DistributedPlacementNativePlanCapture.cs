using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Groundwork.Kernel;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Captures the current public placement-owner route from a freshly executed Groundwork v2 fixture.
/// The route is deliberately invoked through <see cref="IExecutionPlacementStore"/> so the retained
/// command and provider plan describe production behavior rather than a direct provider query.
/// </summary>
internal static class DistributedPlacementNativePlanCapture
{
    private const string InitialOwner = "worker-alpha";

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
            DistributedPlacementTakeoverWorkload.WorkloadId,
            DistributedPlacementTakeoverAdapter.PhysicalForm);

        await using var adapter = new DistributedPlacementTakeoverAdapter(
            request,
            connectionString,
            outputDirectory,
            captureCommands: true);
        await adapter.PrepareAsync(cancellationToken);
        await new DistributedPlacementTakeoverWorkload().ExecuteAsync(adapter, cancellationToken);
        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        var observer = adapter.CommandObserver;
        var specification = RuntimeNativePlanContract.For(
            request.WorkloadId,
            "list-owned-live-placements");

        await using var explain = await NativeExplainCaptureGate.EnterAsync(
            $"groundwork-placement-{request.Provider}-{request.MeasurementSetId}",
            cancellationToken);
        observer.ClearCommands();
        var before = Directory.EnumerateFiles(explain.Directory).ToHashSet(StringComparer.Ordinal);
        var leases = await clients.Primary.ListOwnedAsync(
            new ExecutionPlacementLeaseListRequest(
                InitialOwner,
                DistributedPlacementTakeoverWorkload.FixedNowUtc,
                DistributedPlacementTakeoverWorkload.TakeoverCandidates),
            cancellationToken);
        if (leases.Count != specification.FiniteLimit)
            throw new PerformanceContractException(
                $"Runtime placement route '{specification.RouteIdentity}' returned {leases.Count} rows; expected {specification.FiniteLimit}.");

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
            leases.Count)
        {
            NativeFetchLimit = specification.NativeFetchLimit
        };
        RuntimeNativePlanContract.ValidateEnvelope(
            request.WorkloadId,
            request.Provider,
            request.Adapter,
            route,
            rawPath);

        return NativePlanEvidenceStaging.Write(
            outputDirectory,
            RuntimeNativePlanCaptureSupport.CreateDocument(request, observed, [route]));
    }
}
