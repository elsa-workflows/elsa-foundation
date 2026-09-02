using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Captures the frozen canonical unfiltered claimable route through the public fenced outbox store.
/// The correctness run establishes the exact 1,024-row fixture; its untouched due tail supplies a
/// full public page without inventing rows solely for explain capture.
/// </summary>
internal static class OutboxDrainNativePlanCapture
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
            RuntimeOutboxDrainWorkload.WorkloadId,
            OutboxDrainAdapter.PhysicalForm);

        await using var adapter = new OutboxDrainAdapter(
            request,
            connectionString,
            outputDirectory,
            captureCommands: true);
        await adapter.PrepareAsync(cancellationToken);
        await new RuntimeOutboxDrainWorkload().ExecuteAsync(adapter, cancellationToken);
        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        var observer = adapter.CommandObserver;
        var specification = RuntimeNativePlanContract.For(request.WorkloadId, "list-claimable");

        await using var explain = await NativeExplainCaptureGate.EnterAsync(
            $"groundwork-outbox-{request.Provider}-{request.MeasurementSetId}",
            cancellationToken);
        observer.ClearCommands();
        var before = Directory.EnumerateFiles(explain.Directory).ToHashSet(StringComparer.Ordinal);
        var claims = await clients.Primary.Claims.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest(
                "native-plan-capture",
                RuntimeOutboxDrainWorkload.FixedNowUtc,
                TimeSpan.FromMinutes(1),
                specification.FiniteLimit),
            cancellationToken);
        if (claims.Count != specification.FiniteLimit)
            throw new PerformanceContractException(
                $"Runtime outbox route '{specification.RouteIdentity}' returned {claims.Count} claims; expected {specification.FiniteLimit}.");

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
            claims.Count)
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
