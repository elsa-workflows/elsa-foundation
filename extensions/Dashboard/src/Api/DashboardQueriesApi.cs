using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Exceptions;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MinimalEndpoints;

namespace Elsa.Studio.Dashboard.Api;

internal static class DashboardQueriesApi
{
    private const string Root = "/_elsa/studio/";

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapEndpointGroup("Elsa.Studio.Dashboard.Api", DashboardQueriesJsonContext.Default,
            jsonContentType: "application/json");
        group.MapUnboundOperation("GET", Root + "dashboard-datasets", "DiscoverDashboardDatasets",
            typeof(DashboardDatasetCatalog), 200, null, Discover).RequirePermission("studio/questions:read");
        group.MapUnboundOperation("POST", Root + "dashboard-queries/validate", "ValidateDashboardQuestion",
            typeof(DashboardQueryValidation), 200, null, Validate).RequirePermission("studio/questions:read");
        group.MapUnboundOperation("POST", Root + "dashboard-queries/preview", "PreviewDashboardQuestion",
            typeof(DashboardQueryResult), 200, null, Preview).RequirePermission("studio/questions:read");
        group.MapUnboundOperation("POST", Root + "dashboard-queries/drill", "DrillDashboardQuestion",
            typeof(DashboardQueryResult), 200, null, Drill).RequirePermission("studio/questions:read");
        group.MapUnboundOperation("POST", Root + "questions/{id}/execute", "ExecuteSavedDashboardQuestion",
            typeof(DashboardQueryResult), 200, null, ExecuteSaved).RequirePermission("studio/questions:read");
        group.MapUnboundOperation("POST", Root + "questions/{id}/preview", "PreviewSavedDashboardQuestionDraft",
            typeof(DashboardQueryResult), 200, null, PreviewSavedDraft).RequirePermission("studio/questions:read");
        group.MapUnboundOperation("POST", Root + "questions/{id}/drill", "DrillSavedDashboardQuestion",
            typeof(DashboardQueryResult), 200, null, DrillSaved).RequirePermission("studio/questions:read");
        group.MapUnboundOperation("POST", Root + "dashboards/{dashboardId}/tiles/{tileId}/execute", "ExecuteDashboardTile",
            typeof(DashboardQueryResult), 200, null, ExecuteTile).RequirePermission("studio/dashboards:read");
        group.MapUnboundOperation("POST", Root + "dashboards/{dashboardId}/tiles/{tileId}/drill", "DrillDashboardTile",
            typeof(DashboardQueryResult), 200, null, DrillTile).RequirePermission("studio/dashboards:read");
    }

    private static Task Discover(HttpContext http) => Boundary(http, async actor =>
    {
        var items = await Queries(http).DiscoverAsync(Scope(http, actor), http.RequestAborted);
        await Results.Json(new DashboardDatasetCatalog(items), DashboardQueriesJsonContext.Default.DashboardDatasetCatalog).ExecuteAsync(http);
    });

    private static Task Validate(HttpContext http) => Boundary(http, async actor =>
    {
        var request = await Read(http, DashboardQueriesJsonContext.Default.DashboardQueryRequest);
        var result = await Queries(http).ValidateAsync(Scope(http, actor), request, http.RequestAborted);
        await Results.Json(result, DashboardQueriesJsonContext.Default.DashboardQueryValidation).ExecuteAsync(http);
    });

    private static Task Preview(HttpContext http) => Boundary(http, async actor =>
    {
        var request = await Read(http, DashboardQueriesJsonContext.Default.DashboardQueryRequest);
        await Write(http, await Queries(http).ExecuteAsync(Scope(http, actor), request, http.RequestAborted));
    });

    private static Task Drill(HttpContext http) => Boundary(http, async actor =>
    {
        var request = await Read(http, DashboardQueriesJsonContext.Default.DashboardDrillRequest);
        // Native draft exploration is separate from a saved-resource drill; callers binding a saved
        // question must use the saved endpoint so a revoked definition is checked again.
        await Write(http, await Queries(http).DrillAsync(Scope(http, actor), request, http.RequestAborted));
    });

    private static Task ExecuteSaved(HttpContext http) => Boundary(http, async actor =>
    {
        var id = RequiredRoute(http, "id");
        var question = await Resources(http).GetAuthorizedQuestionAsync(actor, id, http.RequestAborted);
        RequireRevision(http, question.Revision);
        var input = await ReadOptional(http);
        var definition = question.Content.Definition
            ?? throw new DashboardResourceException("unavailable", "The saved question is unavailable.");
        var request = Merge(definition, null, input.ParameterValues);
        http.Response.Headers.ETag = Quote(question.Revision);
        await Write(http, await Queries(http).ExecuteAsync(Scope(http, actor), request, http.RequestAborted));
    });

    private static Task PreviewSavedDraft(HttpContext http) => Boundary(http, async actor =>
    {
        // Bind to current resource authority/revision without disclosing the unavailable original.
        // An editor can explicitly repair it using another currently authorized source. Read-only
        // viewers retain the original source-disclosure requirement for bound draft previews.
        var question = await Resources(http).GetAsync(actor, DashboardResourceKinds.Question, RequiredRoute(http, "id"), http.RequestAborted);
        RequireRevision(http, question.Revision);
        if (question.Definition is null && !question.Capabilities.CanEdit)
            throw new DashboardResourceException("denied", "Editing access is required to preview a replacement for an unavailable question.");
        var draft = await Read(http, DashboardQueriesJsonContext.Default.DashboardQueryRequest);
        // The ordinary native query service independently re-evaluates the replacement's source
        // authority and grammar; a preview does not apply or save the replacement.
        http.Response.Headers.ETag = Quote(question.Revision);
        await Write(http, await Queries(http).ExecuteAsync(Scope(http, actor), draft, http.RequestAborted));
    });

    private static Task ExecuteTile(HttpContext http) => Boundary(http, async actor =>
    {
        var dashboard = await Resources(http).GetAsync(actor, DashboardResourceKinds.Dashboard,
            RequiredRoute(http, "dashboardId"), http.RequestAborted);
        RequireRevision(http, dashboard.Revision);
        var tileId = RequiredRoute(http, "tileId");
        var tile = dashboard.Tiles.SingleOrDefault(x => StringComparer.Ordinal.Equals(x.Id, tileId));
        if (tile?.QuestionId is null || tile.ReferenceState != "available")
            throw new DashboardResourceException("notFound", "The dashboard question is unavailable.");
        var question = await Resources(http).GetAuthorizedQuestionAsync(actor, tile.QuestionId, http.RequestAborted);
        var input = await ReadOptional(http);
        if (input.QuestionRevision is { } observed && !StringComparer.Ordinal.Equals(observed, question.Revision))
            throw new DashboardResourceException("conflict", "The question changed. Reload and preview its current definition.");
        var definition = question.Content.Definition
            ?? throw new DashboardResourceException("unavailable", "The saved question is unavailable.");
        var request = Merge(definition, tile, input.ParameterValues);
        http.Response.Headers.ETag = Quote(question.Revision);
        http.Response.Headers["X-Elsa-Dashboard-Revision"] = dashboard.Revision;
        await Write(http, await Queries(http).ExecuteAsync(Scope(http, actor), request, http.RequestAborted));
    });

    private static DashboardQueryRequest Merge(DashboardQueryRequest question, DashboardTileView? tile,
        IReadOnlyDictionary<string, DashboardValue>? supplied)
    {
        var values = new Dictionary<string, DashboardValue>(question.ParameterValues, StringComparer.Ordinal);
        if (tile?.Parameters is { } overrides)
            foreach (var pair in overrides) values[pair.Key] = pair.Value;
        if (supplied is not null)
            foreach (var pair in supplied) values[pair.Key] = pair.Value;
        return question with { Visualization = tile?.Presentation ?? question.Visualization, ParameterValues = values };
    }

    private static Task DrillSaved(HttpContext http) => Boundary(http, async actor =>
    {
        var question = await Resources(http).GetAuthorizedQuestionAsync(actor, RequiredRoute(http, "id"), http.RequestAborted);
        RequireRevision(http, question.Revision);
        var input = await Read(http, DashboardQueriesJsonContext.Default.DashboardSavedQueryDrillInput);
        var definition = question.Content.Definition
            ?? throw new DashboardResourceException("unavailable", "The saved question is unavailable.");
        var request = new DashboardDrillRequest(Merge(definition, null, input.ParameterValues),
            input.Descriptor ?? throw new DashboardResourceException("validation", "Select an available result group."),
            input.PageSize, input.Cursor);
        http.Response.Headers.ETag = Quote(question.Revision);
        await Write(http, await Queries(http).DrillAsync(Scope(http, actor), request, http.RequestAborted));
    });

    private static Task DrillTile(HttpContext http) => Boundary(http, async actor =>
    {
        var dashboard = await Resources(http).GetAsync(actor, DashboardResourceKinds.Dashboard,
            RequiredRoute(http, "dashboardId"), http.RequestAborted);
        RequireRevision(http, dashboard.Revision);
        var tile = dashboard.Tiles.SingleOrDefault(x => StringComparer.Ordinal.Equals(x.Id, RequiredRoute(http, "tileId")));
        if (tile?.QuestionId is null || tile.ReferenceState != "available")
            throw new DashboardResourceException("notFound", "The dashboard question is unavailable.");
        var question = await Resources(http).GetAuthorizedQuestionAsync(actor, tile.QuestionId, http.RequestAborted);
        var input = await Read(http, DashboardQueriesJsonContext.Default.DashboardSavedQueryDrillInput);
        if (input.QuestionRevision is { } observed && !StringComparer.Ordinal.Equals(observed, question.Revision))
            throw new DashboardResourceException("conflict", "The question changed. Reload before drilling into its results.");
        var definition = question.Content.Definition
            ?? throw new DashboardResourceException("unavailable", "The saved question is unavailable.");
        var request = new DashboardDrillRequest(Merge(definition, tile, input.ParameterValues),
            input.Descriptor ?? throw new DashboardResourceException("validation", "Select an available result group."),
            input.PageSize, input.Cursor);
        http.Response.Headers.ETag = Quote(question.Revision);
        http.Response.Headers["X-Elsa-Dashboard-Revision"] = dashboard.Revision;
        await Write(http, await Queries(http).DrillAsync(Scope(http, actor), request, http.RequestAborted));
    });

    private static async Task Boundary(HttpContext http, Func<DashboardActor, Task> action)
    {
        try
        {
            var actor = await DashboardApiActor.CurrentAsync(http);
            http.Response.Headers.CacheControl = "no-store";
            await action(actor);
        }
        catch (DashboardResourceException error)
        {
            var status = error.Code switch
            {
                "notFound" => 404, "forbidden" or "denied" => 403, "unauthenticated" => 401,
                "conflict" => 412, "preconditionRequired" => 428, "tooLarge" => 413, "validation" => 422,
                "unavailable" or "schemaIncompatible" => 503, _ => 400
            };
            await Problem(http, status, error.Code, error.Message);
        }
        catch (DashboardQueryException error)
        {
            var status = error.Code switch
            {
                "DATASET_UNAVAILABLE" => 404,
                "QUERY_SOURCE_FAILED" or "DATASET_OWNER_CONFLICT" or "INVALID_DATASET_DECLARATION" or "INVALID_SOURCE_RESULT" or
                    "INVALID_QUERY_LIMITS" or "unavailable" or "ownerUnavailable" => 503,
                "QUERY_TIMEOUT" => 504,
                "QUERY_CONCURRENCY_LIMIT" => 429,
                "QUERY_SCOPE_REQUIRED" => 403,
                _ => 422
            };
            await Problem(http, status, error.Code, error.Message);
        }
        catch (JsonException)
        {
            await Problem(http, 400, "invalidBody", "Supply a supported Dashboard query definition.");
        }
    }

    private static IDashboardQueryService Queries(HttpContext http) => http.RequestServices.GetRequiredService<IDashboardQueryService>();
    private static IDashboardResourceService Resources(HttpContext http) => http.RequestServices.GetRequiredService<IDashboardResourceService>();
    private static DashboardRequestScope Scope(HttpContext http, DashboardActor actor) =>
        new(actor.TenantId, actor.UserId, http.RequestServices.GetService<IHostEnvironment>()?.ApplicationName ?? "elsa");
    private static string RequiredRoute(HttpContext http, string key) => http.Request.RouteValues[key]?.ToString()
        ?? throw new DashboardResourceException("notFound", "The resource is unavailable.");
    private static string Quote(string revision) => "\"" + revision + "\"";

    private static void RequireRevision(HttpContext http, string revision)
    {
        var observed = http.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(observed))
            throw new DashboardResourceException("preconditionRequired", "Reload the resource before executing it.");
        if (!StringComparer.Ordinal.Equals(observed, Quote(revision)))
            throw new DashboardResourceException("conflict", "The definition changed. Reload before refreshing.");
    }

    private static async ValueTask<T> Read<T>(HttpContext http, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        var bytes = await ReadBytes(http);
        return JsonSerializer.Deserialize(bytes, type)
            ?? throw new DashboardResourceException("invalidBody", "A query request is required.");
    }

    private static async ValueTask<DashboardSavedQueryExecutionInput> ReadOptional(HttpContext http)
    {
        var bytes = await ReadBytes(http);
        return bytes.Length == 0 ? new() : JsonSerializer.Deserialize(bytes, DashboardQueriesJsonContext.Default.DashboardSavedQueryExecutionInput)
            ?? throw new DashboardResourceException("invalidBody", "Supply valid typed parameter overrides.");
    }

    private static async ValueTask<byte[]> ReadBytes(HttpContext http)
    {
        const int maximum = 65536;
        if (http.Request.ContentLength > maximum)
            throw new DashboardResourceException("tooLarge", "Narrow the query definition before previewing.");
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await http.Request.Body.ReadAsync(buffer, http.RequestAborted)) > 0)
        {
            if (output.Length + read > maximum)
                throw new DashboardResourceException("tooLarge", "Narrow the query definition before previewing.");
            await output.WriteAsync(buffer.AsMemory(0, read), http.RequestAborted);
        }
        return output.ToArray();
    }

    private static Task Write(HttpContext http, DashboardQueryResult result) =>
        Results.Json(result, DashboardQueriesJsonContext.Default.DashboardQueryResult).ExecuteAsync(http);
    private static Task Problem(HttpContext http, int status, string code, string message) =>
        Results.Json(new DashboardQueryApiProblem(code, message), DashboardQueriesJsonContext.Default.DashboardQueryApiProblem,
            statusCode: status).ExecuteAsync(http);
}

internal sealed record DashboardDatasetCatalog(IReadOnlyList<DashboardDatasetDeclaration> Items);
internal sealed record DashboardQueryApiProblem(string Code, string Message);
internal sealed record DashboardSavedQueryExecutionInput
{
    public string? QuestionRevision { get; init; }
    public IReadOnlyDictionary<string, DashboardValue>? ParameterValues { get; init; }
}
internal sealed record DashboardSavedQueryDrillInput
{
    public string? QuestionRevision { get; init; }
    public DashboardDrillDescriptor? Descriptor { get; init; }
    public int PageSize { get; init; } = 25;
    public string? Cursor { get; init; }
    public IReadOnlyDictionary<string, DashboardValue>? ParameterValues { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DashboardDatasetCatalog))]
[JsonSerializable(typeof(DashboardQueryRequest))]
[JsonSerializable(typeof(DashboardQueryValidation))]
[JsonSerializable(typeof(DashboardQueryResult))]
[JsonSerializable(typeof(DashboardDrillRequest))]
[JsonSerializable(typeof(DashboardSavedQueryExecutionInput))]
[JsonSerializable(typeof(DashboardSavedQueryDrillInput))]
[JsonSerializable(typeof(DashboardQueryApiProblem))]
internal partial class DashboardQueriesJsonContext : JsonSerializerContext;
