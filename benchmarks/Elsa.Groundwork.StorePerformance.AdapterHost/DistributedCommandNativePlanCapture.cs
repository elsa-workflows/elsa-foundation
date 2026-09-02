using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Groundwork.Kernel;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Captures every frozen public command-transport read route after correctness has settled. Leasing
/// changes the stream-head summary, but the frozen fixture retains a visible tail after the bounded
/// lease so the subsequent list and count routes keep their declared cardinalities.
/// </summary>
internal static class DistributedCommandNativePlanCapture
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
            DistributedCommandSendLeaseAckWorkload.WorkloadId,
            DistributedCommandSendLeaseAckAdapter.PhysicalForm);

        var commandObserver = new WritePathRoundTripObserver(request.Provider, captureCommands: true);
        await using var adapter = new DistributedCommandSendLeaseAckAdapter(
            request,
            connectionString,
            outputDirectory,
            commandObserver);
        await adapter.PrepareAsync(cancellationToken);
        await new DistributedCommandSendLeaseAckWorkload().ExecuteAsync(adapter, cancellationToken);
        var clients = await adapter.OpenIndependentClientsAsync(cancellationToken);
        var observer = RuntimeNativePlanCaptureSupport.RequireCommandObserver(adapter.RoundTripObserver);
        var routes = new Dictionary<string, NativeRouteEvidence>(StringComparer.Ordinal);
        var specifications = RuntimeNativePlanContract.ForWorkload(request.WorkloadId)
            .OrderBy(specification => specification.ResultShape == RuntimeNativeResultShape.Page && specification.UsesLatestPerKey ? 0 :
                specification.RouteIdentity == "lease-visible-commands-by-execution" ? 1 : 2)
            .ToArray();

        await using var explain = await NativeExplainCaptureGate.EnterAsync(
            $"groundwork-command-{request.Provider}-{request.MeasurementSetId}",
            cancellationToken);
        foreach (var specification in specifications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observer.ClearCommands();
            var before = Directory.EnumerateFiles(explain.Directory).ToHashSet(StringComparer.Ordinal);
            var result = await InvokeRouteAsync(clients.Primary, specification.RouteIdentity, cancellationToken);
            if (specification.ResultShape == RuntimeNativeResultShape.Page)
            {
                if (result.PageCount != specification.FiniteLimit)
                    throw new PerformanceContractException(
                        $"Runtime command route '{specification.RouteIdentity}' returned {result.PageCount} rows; expected {specification.FiniteLimit}.");
            }
            else if (result.ScalarCount != specification.ScalarResultCount)
            {
                throw new PerformanceContractException(
                    $"Runtime command route '{specification.RouteIdentity}' returned scalar count {result.ScalarCount}; expected {specification.ScalarResultCount}.");
            }

            var command = RuntimeNativePlanCaptureSupport.RequireRouteCommand(
                observer.Commands,
                specification,
                allowAuxiliaryQueries: specification.RouteIdentity == "lease-visible-commands-by-execution");
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
                specification.ResultShape == RuntimeNativeResultShape.Page ? result.PageCount!.Value : 0,
                specification.ResultShape,
                specification.ResultShape == RuntimeNativeResultShape.ScalarCount ? result.ScalarCount : null,
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
            routes.Add(route.RouteIdentity, route);
        }

        var orderedRoutes = RuntimeNativePlanContract.ForWorkload(request.WorkloadId)
            .Select(specification => routes[specification.RouteIdentity])
            .ToArray();
        return NativePlanEvidenceStaging.Write(
            outputDirectory,
            RuntimeNativePlanCaptureSupport.CreateDocument(request, observed, orderedRoutes));
    }

    private static async Task<CommandRouteResult> InvokeRouteAsync(
        IExecutionCommandTransport client,
        string route,
        CancellationToken cancellationToken)
    {
        return route switch
        {
            "list-visible-command-executions" => new(
                PageCount: (await client.ListPendingExecutionIdsAsync(
                    DistributedCommandSendLeaseAckWorkload.FixedNowUtc,
                    DistributedCommandSendLeaseAckWorkload.WorkflowCount,
                    cancellationToken)).Count,
                ScalarCount: null),
            "lease-visible-commands-by-execution" => new(
                PageCount: (await client.LeaseAsync(
                    "command-workflow-0001",
                    "capture-lease",
                    DistributedCommandSendLeaseAckWorkload.FixedNowUtc,
                    TimeSpan.FromSeconds(30),
                    DistributedCommandSendLeaseAckWorkload.BatchSize / DistributedCommandSendLeaseAckWorkload.ConcurrentLeasers,
                    cancellationToken)).Count,
                ScalarCount: null),
            "count-pending-commands-by-execution" => new(
                PageCount: null,
                ScalarCount: await client.CountPendingAsync("command-workflow-0002", cancellationToken)),
            _ => throw new PerformanceContractException($"Unsupported distributed command route '{route}'.")
        };
    }

    private sealed record CommandRouteResult(int? PageCount, int? ScalarCount);
}
