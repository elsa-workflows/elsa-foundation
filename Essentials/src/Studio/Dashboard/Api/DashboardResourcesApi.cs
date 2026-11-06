using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Studio.Dashboard.Core.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MinimalEndpoints;

namespace Elsa.Studio.Dashboard.Api;

public static class DashboardResourcesApi
{
    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false, MaxDepth = 32 };

    public static void MapDashboardResourcesApi(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapEndpointGroup(typeof(DashboardResourcesApi).Assembly.GetName().Name!, DashboardResourcesJsonContext.Default);
        foreach (var (plural, kind) in new[] { ("questions", DashboardResourceKinds.Question), ("dashboards", DashboardResourceKinds.Dashboard) })
        {
            var route = $"/_elsa/studio/{plural}";
            Map("GET", route, $"List{plural}", "read", async (http, actor, service) =>
                await service.ListAsync(actor, kind, http.Request.Query["ownership"].FirstOrDefault(), http.Request.Query["search"].FirstOrDefault(), http.RequestAborted,
                    http.Request.Query["continuationToken"].FirstOrDefault()));
            Map("POST", route, $"Create{plural}", "create", async (http, actor, service) =>
                await service.CreateAsync(actor, kind, await ReadAsync<DashboardResourceInput>(http), http.RequestAborted));
            Map("GET", route + "/{id}", $"Get{plural}", "read", async (http, actor, service) =>
                await service.GetAsync(actor, kind, Id(http), http.RequestAborted));
            Map("PUT", route + "/{id}", $"Update{plural}", "edit", async (http, actor, service) =>
                await service.UpdateAsync(actor, kind, Id(http), Expected(http), await ReadAsync<DashboardResourceInput>(http), http.RequestAborted));
            Map("DELETE", route + "/{id}", $"Delete{plural}", "delete", async (http, actor, service) => {
                await service.DeleteAsync(actor, kind, Id(http), Expected(http), http.RequestAborted); return null; });
            Map("GET", route + "/{id}/audience", $"Get{plural}Audience", "share", async (http, actor, service) =>
                await service.GetAudienceAsync(actor, kind, Id(http), http.RequestAborted));
            Map("PUT", route + "/{id}/audience", $"Set{plural}Audience", "share", async (http, actor, service) =>
                await service.SetAudienceAsync(actor, kind, Id(http), Expected(http), (await ReadAsync<DashboardAudiencePage>(http)).Items, http.RequestAborted));
            Map("POST", route + "/{id}/copies", $"Copy{plural}", "create", async (http, actor, service) =>
                await service.CopyAsync(actor, kind, Id(http), Expected(http), await ReadAsync<DashboardCopyInput>(http), http.RequestAborted));
            Map("GET", route + "/{id}/revisions", $"List{plural}Revisions", "read", async (http, actor, service) =>
                await service.HistoryAsync(actor, kind, Id(http), http.RequestAborted));
            Map("GET", route + "/{id}/revisions/{revision}", $"Get{plural}Revision", "read", async (http, actor, service) =>
                await service.GetRevisionAsync(actor, kind, Id(http), http.Request.RouteValues["revision"]!.ToString()!, http.RequestAborted));
            Map("POST", route + "/{id}/restorations", $"Restore{plural}", "edit", async (http, actor, service) =>
                await service.RestoreAsync(actor, kind, Id(http), Expected(http), (await ReadAsync<DashboardRestoreInput>(http)).Revision, http.RequestAborted));

            void Map(string method, string path, string operation, string verb,
                Func<HttpContext, DashboardActor, IDashboardResourceService, ValueTask<object?>> action)
            {
                group.MapUnboundOperation(method, path, operation, typeof(object), method == "DELETE" ? 204 : 200, null,
                    http => ExecuteAsync(http, async actor => await action(http, actor, http.RequestServices.GetRequiredService<IDashboardResourceService>())), containFailures: false)
                    .RequireAnyPermission("*", $"studio/{plural}:{verb}");
            }
        }
        group.MapUnboundOperation("GET", "/_elsa/studio/dashboard-audience-candidates", "DashboardAudienceCandidates", typeof(DashboardAudienceCandidatePage), 200, null,
            http => ExecuteAsync(http, async actor => await http.RequestServices.GetRequiredService<IDashboardAudienceDirectory>()
                .SearchAsync(actor, http.Request.Query["kind"].FirstOrDefault() ?? "user", http.Request.Query["search"].FirstOrDefault(),
                    http.Request.Query["continuationToken"].FirstOrDefault(), http.RequestAborted)), containFailures: false)
            .RequireAnyPermission("*", "studio/questions:share", "studio/dashboards:share");
    }

    internal static async Task ExecuteAsync(HttpContext context, Func<DashboardActor, ValueTask<object?>> action)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var result = await action(await DashboardApiActor.CurrentAsync(context));
            if (result is null) { context.Response.StatusCode = 204; return; }
            if (result is DashboardResourceView resource) context.Response.Headers.ETag = $"\"{resource.Revision}\"";
            await context.Response.WriteAsJsonAsync(result, Json, context.RequestAborted);
        }
        catch (DashboardResourceException error)
        {
            var status = error.Code switch { "unauthenticated" => 401, "denied" => 403, "notFound" => 404, "conflict" => 412,
                "preconditionRequired" => 428, "validation" => 422, _ => 503 };
            await Results.Problem(statusCode: status, title: error.Message, extensions: new Dictionary<string, object?> { ["code"] = error.Code }).ExecuteAsync(context);
        }
        catch (JsonException) { await Results.Problem(statusCode: 400, title: "The request contains malformed or unsupported fields.").ExecuteAsync(context); }
    }

    internal static async ValueTask<T> ReadAsync<T>(HttpContext context)
    {
        const int maximum = 131_072;
        if (context.Request.ContentLength > maximum) throw new DashboardResourceException("validation", "The request exceeds 128 KiB.");
        using var buffer = new MemoryStream(); var chunk = new byte[16_384];
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, maximum + 1 - (int)buffer.Length)), context.RequestAborted);
            if (read == 0) break;
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maximum) throw new DashboardResourceException("validation", "The request exceeds 128 KiB.");
        }
        return JsonSerializer.Deserialize<T>(buffer.ToArray(), Json) ?? throw new JsonException();
    }

    private static string Id(HttpContext context) => context.Request.RouteValues["id"]?.ToString() ?? "";
    internal static string Expected(HttpContext context)
    {
        var values = context.Request.Headers.IfMatch;
        if (values.Count != 1 || values[0] is not { } value || value.Length < 3 || value[0] != '"' || value[^1] != '"' || value.Contains(','))
            throw new DashboardResourceException("preconditionRequired", "Supply one quoted resource revision with If-Match.");
        return value[1..^1];
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DashboardResourceView))]
[JsonSerializable(typeof(DashboardResourcePage))]
[JsonSerializable(typeof(DashboardResourceInput))]
[JsonSerializable(typeof(DashboardAudiencePage))]
[JsonSerializable(typeof(DashboardAudienceCandidatePage))]
[JsonSerializable(typeof(DashboardHistoryPage))]
[JsonSerializable(typeof(object))]
internal partial class DashboardResourcesJsonContext : JsonSerializerContext;
