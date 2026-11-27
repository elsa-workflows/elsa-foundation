using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Workflows.Runtime.KernelApi;

internal static class KernelParkInspection
{
    internal const string Route = "runtime/workflows/instances/{workflowExecutionId}/park";
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    internal sealed record Delivery(string Handle, string Kind, string State, DateTimeOffset? DueAt, int Attempts);
    internal sealed record ParkEvidence(DateTimeOffset ParkedAt, string FaultSummary, IReadOnlyList<Delivery> Deliveries, string? Next);

    internal static void Map(IEndpointRouteBuilder routes) =>
        routes.MapGet(Route, ReadAsync)
            .WithOwner(WorkflowsRuntimeKernelApiFeature.FeatureName)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeRead)
            .WithName("ReadWorkflowRunPark")
            .Produces<ParkEvidence>()
            .Produces(StatusCodes.Status409Conflict);

    private static async Task<IResult> ReadAsync(string workflowExecutionId, string? cursor, HttpContext context,
        KernelRunReader reader, KernelRuns runs, IActivityInspectionContextAsync authorization, TimeProvider clock)
    {
        context.Response.Headers.CacheControl = "no-store";
        using var deadline = new CancellationTokenSource(Budget, clock);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, deadline.Token);
        var token = budget.Token;
        try
        {
            if (await reader.GetAsync(workflowExecutionId, token) is not { View: { } view } run ||
                !await authorization.CanInspectStructureAsync(run.State, token) || !await runs.SupportsParkInboxAsync(token))
                return Results.NotFound();
            if (view is not { Header.Status: ActorStatus.Faulted, Park: { } park, Selection: { } selection })
                return cursor is null ? Results.NotFound() : Results.Conflict();

            var page = await runs.ReadParkInboxAsync(view.Header.Ref, selection, cursor is null ? null : new(cursor), token);
            if (page is null) return Results.Conflict();

            var fault = "The workflow stopped before its turn committed.";
            if (view.Header.Fault is { Length: > 0 } description && await authorization.CanInspectSensitiveValuesAsync(run.State, token))
                fault = description;
            var deliveries = page.Items.Select(item => new Delivery(item.Handle, item.Kind, item.State.ToString(),
                item.Due is { } due ? DateTimeOffset.FromUnixTimeMilliseconds(due) : null, item.Attempts)).ToArray();
            return Results.Ok(new ParkEvidence(DateTimeOffset.FromUnixTimeMilliseconds(park.At), fault, deliveries, page.Next?.Value));
        }
        catch (ArgumentException error) when (error.ParamName == "from")
        { return Results.BadRequest(); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !context.RequestAborted.IsCancellationRequested)
        { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        catch (TimeoutException)
        { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
    }
}

internal sealed class KernelParkInspectionCapabilitySource(KernelRuns runs) : IApiCapabilitySource
{
    public async ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        await runs.SupportsParkInboxAsync(cancellationToken)
            ? [new(RuntimeApiCapabilities.CapabilityId, 1,
                [new("workflow-instance-park", KernelParkInspection.Route, templated: true)],
                $"{WorkflowsRuntimeKernelApiFeature.FeatureName}.Park")]
            : [];
}
