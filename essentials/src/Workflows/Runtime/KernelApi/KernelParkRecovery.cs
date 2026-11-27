using System.Globalization;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelParkRecovery(KernelRunReader runs, KernelRuns foundation, KernelOperatorIngress operators,
    NormalizedPrincipalValidator principals, IPermissionAuthorizationService authorization, TimeProvider clock)
{
    internal const string Route = "runtime/workflows/instances/{workflowExecutionId}/recovery";
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);
    private readonly TimeProvider _clock = clock;

    internal sealed record Selection(string? Token);
    internal sealed record RecoveryView(string Status, DateTimeOffset? ParkedAt = null, string? Token = null);

    internal static void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet(Route, static (string workflowExecutionId, HttpContext context) =>
                WithRecoveryAsync(context, (recovery, token) => recovery.ReadAsync(workflowExecutionId, context, token)))
            .WithOwner(WorkflowsRuntimeKernelApiFeature.FeatureName)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeManage)
            .WithName("ReadWorkflowRunRecovery")
            .Produces<RecoveryView>();
        routes.MapPost(Route, static (string workflowExecutionId, Selection selection, HttpContext context) =>
                WithRecoveryAsync(context, (recovery, token) => recovery.ResumeAsync(workflowExecutionId, selection, context, token)))
            .WithOwner(WorkflowsRuntimeKernelApiFeature.FeatureName)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeManage)
            .WithName("RecoverWorkflowRun")
            .Produces(StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> WithRecoveryAsync(HttpContext context,
        Func<KernelParkRecovery, CancellationToken, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.RequestServices.GetService<KernelParkRecovery>() is not { } recovery) return Results.NotFound();
        using var deadline = new CancellationTokenSource(Budget, recovery._clock);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, deadline.Token);
        try { return await action(recovery, budget.Token); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !context.RequestAborted.IsCancellationRequested)
        { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        catch (TimeoutException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
    }

    private async Task<IResult> ReadAsync(string workflowExecutionId, HttpContext context, CancellationToken token)
    {
        var view = await ReadAuthorizedAsync(workflowExecutionId, context, token);
        if (view is null) return Results.NotFound();
        if (view.Park is not { } park) return Results.Ok(new RecoveryView("NotParked"));
        var at = DateTimeOffset.FromUnixTimeMilliseconds(park.At);
        return Results.Ok(view.Header.Status == ActorStatus.Faulted && park.OpenResolution is null && view.Selection is { } selection
            ? new RecoveryView("Available", at, selection)
            : new RecoveryView("Unavailable", at));
    }

    private async Task<IResult> ResumeAsync(string workflowExecutionId, Selection selection, HttpContext context, CancellationToken token)
    {
        var view = await ReadAuthorizedAsync(workflowExecutionId, context, token);
        if (view is null) return Results.NotFound();
        if (view is not { Header.Status: ActorStatus.Faulted, Park: { OpenResolution: null }, Selection: not null } ||
            selection.Token is not { Length: > 0 and <= 1024 } selected) return Results.Conflict();

        var binding = foundation.Tenant;
        var actor = view.Header.Ref;
        var ingress = await operators.Open((scope, target, operation) =>
            scope.Tenant == binding.Tenant && target == actor && operation == Kinds.Recover, token);
        // Each submission checks the presented mark. An earlier command's receipt cannot acknowledge a new Park.
        var admission = await ingress.SendAsync(binding, new Target.Ref(actor), Guid.NewGuid().ToString("N"), Kinds.Recover,
            JsonSerializer.Serialize(new OperatorCommand(selected, ExpectedTarget: actor)), cancellationToken: token);
        if (admission.Outcome is AdmissionOutcome.Accepted or AdmissionOutcome.Duplicate) return Results.Accepted();
        if (admission.Reason is RefusalReason.RateLimited or RefusalReason.NodeBusy or RefusalReason.InboxFull)
        {
            if (admission.RetryAfter is { } retry)
                context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(retry.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return Results.StatusCode(admission.Reason == RefusalReason.RateLimited
                ? StatusCodes.Status429TooManyRequests
                : StatusCodes.Status503ServiceUnavailable);
        }
        return Results.Conflict();
    }

    private async Task<ActorView?> ReadAuthorizedAsync(string workflowExecutionId, HttpContext context, CancellationToken token)
    {
        if (KernelRunOperatorCaller.Read(context.User, principals) is not { } caller || !caller.Matches(foundation.Tenant) ||
            await runs.GetAsync(workflowExecutionId, token) is not { View: { } view, IsolatedReads: null } run) return null;
        var permission = new PermissionEvaluationContext(caller.Principal, WorkflowRuntimePermissions.WorkflowRuntimeManage, caller.Tenant, run.State);
        return (await authorization.AuthorizeAsync(permission, token)).Succeeded ? view : null;
    }
}

internal sealed class KernelParkRecoveryCapabilitySource : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>([
            new(RuntimeApiCapabilities.CapabilityId, 1,
                [new("workflow-instance-recovery", KernelParkRecovery.Route, templated: true)],
                $"{WorkflowsRuntimeKernelApiFeature.FeatureName}.Recovery")]);
}
