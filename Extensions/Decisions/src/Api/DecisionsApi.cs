using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Elsa.Decisions.Core;
using Elsa.Api.AspNetCore;
using Elsa.Decisions.Design;
using Elsa.Decisions.Persistence.Core;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MinimalEndpoints;

namespace Elsa.Decisions.Api;

/// <summary>Maps Decisions operations through the host's normal endpoint catalog and authorization policies.</summary>
public static class DecisionsApi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true, MaxDepth = 64 };

    public static void MapDecisionsApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapEndpointGroup("Elsa.Decisions.Api", DecisionsJsonContext.Default, jsonContentType: "application/json");
        void Map(string method, string path, string name, string permission, Type? response, Func<HttpContext, DecisionDesignService, DecisionCaller, Task<object?>> action)
        {
            api.MapUnboundOperation(method, "/decisions" + path, name, response, StatusCodes.Status200OK, null,
                async context => await Handle(context, action), containFailures: false).DisableCookieRedirect().RequirePermission(permission);
        }
        Map("GET", "", "DecisionsList", DecisionPermissions.Read, typeof(DecisionAssetsResponse), async (ctx, service, caller) => new DecisionAssetsResponse(await service.ListAsync(caller, ctx.RequestAborted)));
        Map("POST", "", "DecisionsCreate", DecisionPermissions.Manage, typeof(DecisionAssetView), async (ctx, service, caller) => await service.SaveAsync(caller, null, await Body<SaveDecisionRequest>(ctx), ctx.RequestAborted));
        Map("GET", "/{id}", "DecisionsGet", DecisionPermissions.Read, typeof(DecisionAssetView), async (ctx, service, caller) => await service.GetAsync(caller, Id(ctx), ctx.RequestAborted));
        Map("PUT", "/{id}", "DecisionsUpdate", DecisionPermissions.Manage, typeof(DecisionAssetView), async (ctx, service, caller) => await service.SaveAsync(caller, Id(ctx), await Body<SaveDecisionRequest>(ctx), ctx.RequestAborted));
        Map("POST", "/{id}/validate", "DecisionsValidate", DecisionPermissions.Manage, typeof(DecisionValidation), async (ctx, service, caller) => await service.ValidateAsync(caller, Id(ctx), await Body<ValidateDecisionRequest>(ctx), ctx.RequestAborted));
        Map("POST", "/{id}/publish", "DecisionsPublish", DecisionPermissions.Publish, typeof(DecisionVersionView), async (ctx, service, caller) => await service.PublishAsync(caller, Id(ctx), await Body<PublishDecisionRequest>(ctx), ctx.RequestAborted));
        Map("GET", "/{id}/versions", "DecisionVersions", DecisionPermissions.Read, typeof(DecisionVersionsResponse), async (ctx, service, caller) => new DecisionVersionsResponse(await service.VersionsAsync(caller, Id(ctx), ctx.RequestAborted)));
        Map("GET", "/{id}/versions/{versionId}", "DecisionVersion", DecisionPermissions.Read, typeof(DecisionVersionView), async (ctx, service, caller) => await service.GetVersionAsync(caller, Id(ctx), Route(ctx, "versionId"), ctx.RequestAborted));
        Map("POST", "/{id}/clone-to-draft", "DecisionsClone", DecisionPermissions.Manage, typeof(DecisionAssetView), async (ctx, service, caller) => await service.CloneAsync(caller, Id(ctx), await Body<CloneDecisionRequest>(ctx), ctx.RequestAborted));
        Map("POST", "/{id}/evaluate", "DecisionsEvaluate", DecisionPermissions.Evaluate, typeof(DecisionEvaluation), async (ctx, service, caller) => await service.EvaluateAsync(caller, Id(ctx), await Body<EvaluateDecisionRequest>(ctx), ctx.RequestAborted));
        Map("GET", "/{id}/export", "DecisionsExport", DecisionPermissions.Source, null, async (ctx, service, caller) =>
        {
            var xml = await service.ExportAsync(caller, Id(ctx), ctx.Request.Query["versionId"].FirstOrDefault(), ctx.RequestAborted);
            ctx.Response.ContentType = "application/xml; charset=utf-8";
            ctx.Response.Headers.ContentDisposition = "attachment; filename=decision.dmn";
            await ctx.Response.WriteAsync(xml, ctx.RequestAborted); return null;
        });
        Map("GET", "/{id}/cases", "DecisionCases", DecisionPermissions.Test, typeof(DecisionCasesResponse), async (ctx, service, caller) => new DecisionCasesResponse(await service.CasesAsync(caller, Id(ctx), ctx.RequestAborted)));
        Map("POST", "/{id}/cases", "DecisionCaseCreate", DecisionPermissions.Test, typeof(DecisionCaseView), async (ctx, service, caller) => await service.SaveCaseAsync(caller, Id(ctx), null, await Body<SaveDecisionCaseRequest>(ctx), ctx.RequestAborted));
        Map("PUT", "/{id}/cases/{caseId}", "DecisionCaseUpdate", DecisionPermissions.Test, typeof(DecisionCaseView), async (ctx, service, caller) => await service.SaveCaseAsync(caller, Id(ctx), Route(ctx, "caseId"), await Body<SaveDecisionCaseRequest>(ctx), ctx.RequestAborted));
        Map("DELETE", "/{id}/cases/{caseId}", "DecisionCaseDelete", DecisionPermissions.Test, null, async (ctx, service, caller) =>
        { await service.DeleteCaseAsync(caller, Id(ctx), Route(ctx, "caseId"), Query(ctx, "operationId"), Query(ctx, "expectedRevision"), ctx.RequestAborted); ctx.Response.StatusCode = StatusCodes.Status204NoContent; return null; });
        Map("POST", "/{id}/test", "DecisionTest", DecisionPermissions.Test, typeof(DecisionTestReceipt), async (ctx, service, caller) => await service.TestAsync(caller, Id(ctx), await Body<TestDecisionRequest>(ctx), ctx.RequestAborted));
        Map("GET", "/{id}/runs", "DecisionRuns", DecisionPermissions.History, typeof(DecisionRunsResponse), async (ctx, service, caller) => new DecisionRunsResponse(await service.RunsAsync(caller, Id(ctx), ctx.RequestAborted)));
        Map("GET", "/{id}/runs/{runId}", "DecisionRun", DecisionPermissions.History, typeof(DecisionTestReceipt), async (ctx, service, caller) => await service.GetRunAsync(caller, Id(ctx), Route(ctx, "runId"), ctx.RequestAborted));
        Map("DELETE", "/{id}/runs/{runId}", "DecisionRunDelete", DecisionPermissions.History, null, async (ctx, service, caller) =>
        { await service.DeleteRunAsync(caller, Id(ctx), Route(ctx, "runId"), Query(ctx, "operationId"), ctx.RequestAborted); ctx.Response.StatusCode = StatusCodes.Status204NoContent; return null; });
    }

    internal static DecisionCaller? Caller(ClaimsPrincipal principal, NormalizedPrincipalValidator validator)
    {
        if (!validator.TryGetNormalizedPrincipal(principal, out var normalized)) return null;
        var tenants = normalized.FindAll(IdentityClaimTypes.TenantId).ToArray();
        var users = normalized.FindAll(IdentityClaimTypes.UserId).ToArray();
        if (tenants.Length != 1 || users.Length != 1 || string.IsNullOrWhiteSpace(tenants[0].Value) ||
            string.IsNullOrWhiteSpace(users[0].Value) || tenants[0].Value.Length > 256 || users[0].Value.Length > 128) return null;
        return new(tenants[0].Value, users[0].Value);
    }

    private static async Task Handle(HttpContext context, Func<HttpContext, DecisionDesignService, DecisionCaller, Task<object?>> action)
    {
        try
        {
            var caller = Caller(context.User, context.RequestServices.GetRequiredService<NormalizedPrincipalValidator>()) ?? throw new DecisionDesignException(context.User.Identity?.IsAuthenticated == true ? "forbidden" : "unauthenticated", "A trusted tenant identity is required.");
            var response = await action(context, context.RequestServices.GetRequiredService<DecisionDesignService>(), caller);
            if (response is not null) await context.Response.WriteAsJsonAsync(response, response.GetType(), Json, "application/json", context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (DecisionDesignException ex)
        {
            context.Response.StatusCode = ex.Code switch { "unauthenticated" => 401, "forbidden" => 403, "not-found" => 404, "conflict" => 409, "unavailable" or "corrupt" => 503, _ => 400 };
            await context.Response.WriteAsJsonAsync(new DecisionProblem(ex.Code, ex.Message), Json, context.RequestAborted);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or BadHttpRequestException)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new DecisionProblem("invalid", "The decision request is malformed or unsupported."), Json, context.RequestAborted);
        }
    }
    private static async Task<T> Body<T>(HttpContext context)
    {
        const int maximum = 2 * 1024 * 1024;
        if (context.Request.ContentLength > maximum) throw new DecisionDesignException("invalid", "The request exceeds its supported size.");
        using var stream = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
        { if (stream.Length + count > maximum) throw new DecisionDesignException("invalid", "The request exceeds its supported size."); await stream.WriteAsync(buffer.AsMemory(0, count), context.RequestAborted); }
        stream.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(stream, Json, context.RequestAborted) ?? throw new DecisionDesignException("invalid", "A request body is required.");
    }
    private static string Id(HttpContext context) => Route(context, "id");
    private static string Route(HttpContext context, string key) => context.Request.RouteValues[key]?.ToString() ?? throw new DecisionDesignException("invalid", "A resource identity is required.");
    private static string Query(HttpContext context, string key) => context.Request.Query[key].FirstOrDefault() ?? throw new DecisionDesignException("invalid", "A revision or operation identity is required.");
}

public sealed record DecisionAssetsResponse(IReadOnlyList<DecisionAssetView> Items);
public sealed record DecisionVersionsResponse(IReadOnlyList<DecisionVersionView> Items);
public sealed record DecisionCasesResponse(IReadOnlyList<DecisionCaseView> Items);
public sealed record DecisionRunsResponse(IReadOnlyList<DecisionTestReceipt> Items);
public sealed record DecisionProblem(string Code, string Message);
