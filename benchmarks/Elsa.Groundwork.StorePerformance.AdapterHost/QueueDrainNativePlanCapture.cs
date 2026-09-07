using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Captures the two frozen public scheduler-queue reads after the correctness baseline has settled.
/// The queue's latest-per-workflow route is intentionally captured against the post-baseline state;
/// its physical cardinality therefore describes the rows the route actually searched.
/// </summary>
internal static class QueueDrainNativePlanCapture
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
            RuntimeQueueDrainWorkload.WorkloadId,
            QueueDrainAdapter.PhysicalForm);

        var commandObserver = new WritePathRoundTripObserver(request.Provider, captureCommands: true);
        await using var adapter = new QueueDrainAdapter(
            request,
            connectionString,
            outputDirectory,
            commandObserver);
        await adapter.PrepareAsync(cancellationToken);
        await new RuntimeQueueDrainWorkload().ExecuteAsync(adapter, cancellationToken);
        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        var observer = RuntimeNativePlanCaptureSupport.RequireCommandObserver(adapter.RoundTripObserver);
        var routes = new List<NativeRouteEvidence>(2);

        await using var explain = await NativeExplainCaptureGate.EnterAsync(
            $"groundwork-queue-{request.Provider}-{request.MeasurementSetId}",
            cancellationToken);
        foreach (var specification in RuntimeNativePlanContract.ForWorkload(request.WorkloadId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            observer.ClearCommands();
            var before = Directory.EnumerateFiles(explain.Directory).ToHashSet(StringComparer.Ordinal);
            var materialized = await InvokeRouteAsync(clients.Primary, specification.RouteIdentity, cancellationToken);
            if (materialized != specification.FiniteLimit)
                throw new PerformanceContractException(
                    $"Runtime queue route '{specification.RouteIdentity}' returned {materialized} rows; expected {specification.FiniteLimit}.");

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
                materialized,
                specification.ResultShape,
                null,
                specification.UsesLatestPerKey)
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
        RuntimeQueueDrainClient client,
        string route,
        CancellationToken cancellationToken)
    {
        return route switch
        {
            "list-pending-scheduler-workflow-executions" =>
                (await client.Queue.ListPendingWorkflowExecutionIdsAsync(
                    RuntimeQueueDrainWorkload.BatchSize,
                    cancellationToken)).Count,
            "list-by-workflow-execution" =>
                (await client.Queue.ListAsync(
                    new RuntimeSchedulerWorkQuery(
                        $"scheduler-workflow-{RuntimeQueueDrainWorkload.WorkflowCount - 1:D4}",
                        RuntimeQueueDrainWorkload.WorkItemsPerWorkflow),
                    cancellationToken)).Items.Count,
            _ => throw new PerformanceContractException($"Unsupported runtime queue route '{route}'.")
        };
    }
}
