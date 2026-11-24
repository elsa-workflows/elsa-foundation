using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using WorkflowActor = Kernel.WorkflowActor.WorkflowActor;

namespace Elsa.Workbench.CorePreview;

internal sealed class WorkbenchCorePreviewReads(Func<KernelDb> open, BoundScope tenant)
{
    internal const string ListPath = "/runtime/core-preview/workflows/instances";
    private const int PageSize = 25;

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        if (endpoints.ServiceProvider.GetService<WorkbenchCorePreviewReads>() is not { } reads) return;

        endpoints.MapGet(ListPath, reads.ListAsync)
            .WithHostOwner("Elsa.Workbench")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequireAllPermissions(WorkflowRuntimePermissions.WorkflowRuntimeRead,
                HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission);
    }

    private async Task<IResult> ListAsync(HttpContext context, string? definition, string? status, string? cursor)
    {
        if (!CanRead(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);

        ActorStatus? filter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<ActorStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return Results.BadRequest("Use a valid workflow status.");
            filter = parsed;
        }

        if (definition is { Length: > 1_024 } || cursor is { Length: 0 or > 1_024 })
            return Results.BadRequest("The definition or continuation is invalid.");

        var token = context.RequestAborted;
        var store = new EfStore(open, postgres: true);
        var reads = await store.OpenPreviewRootReadsAsync(PageSize, token);
        var query = new ActorQuery(WorkflowActor.ActorType, filter,
            definition is null ? null : new Dictionary<string, string> { ["definition"] = definition });
        Page<ActorView> page;
        try { page = await reads.ListAsync(tenant, query, cursor is null ? null : new(cursor), token); }
        catch (ArgumentException error) when (error.ParamName == "from") { return Results.BadRequest("The continuation is invalid for this list."); }

        return Results.Ok(new Snapshot(page.Items.Select(view => new Row(view.Header.Ref.Id, view.Header.Status.ToString(),
            view.Header.Labels.GetValueOrDefault("definition"), view.Header.Labels.GetValueOrDefault("version"), view.Header.EndedAt)).ToArray(),
            page.Next?.Value, page.AsOf));
    }

    private bool CanRead(HttpContext context)
    {
        var services = context.RequestServices;
        var access = services.GetService<IPersistenceAccessContextAccessor>()?.Current;
        if (access is not { AccessPolicy: PersistenceAccessPolicy.Ordinary, AcrossScopes: false, Scope.Value: { } scope } ||
            !string.Equals(scope, tenant.Tenant, StringComparison.Ordinal) ||
            services.GetService<NormalizedPrincipalValidator>() is not { } validator ||
            !validator.TryGetNormalizedPrincipal(context.User, out var principal))
            return false;

        var claims = principal.FindAll(IdentityClaimTypes.TenantId).Take(2).ToArray();
        return claims is [var claim] && string.Equals(claim.Value, tenant.Tenant, StringComparison.Ordinal);
    }

    private sealed record Row(string Ref, string Status, string? Definition, string? Version, long? EndedAt);
    private sealed record Snapshot(IReadOnlyList<Row> Items, string? Next, long AsOf);
}
