using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Api.AspNetCore;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Core.Permissions;
using Elsa.Connections.Options;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NativeEndpoints;

namespace Elsa.Connections.Api;

public static class ConnectionsApi
{
    public const string OAuthCallbackPath = "/_elsa/connections/oauth/callback";

    private const string ConnectionsRoute = "/connections";
    private const string ConnectionRoute = "/connections/{name}";
    private const string OwnerId = "Elsa.Connections.Api";

    // Listing connections is implied by Workflow Design's read permission, which that domain owns and contributes, or by
    // connections:manage, since whoever manages connections must see them. Connections does not reference the Workflow
    // Design API, so it names that permission here; an architecture test (EndpointSecurityTests) compares this with the
    // owner's declaration.
    private const string WorkflowDesignRead = "workflows/design:read";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static void MapConnectionsApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints.MapEndpointGroup(OwnerId, ConnectionsJsonContext.Default, jsonContentType: "application/json");

        api.MapUnboundOperation("GET", ConnectionsRoute, "ConnectionsList",
                typeof(ListConnectionsResponse), StatusCodes.Status200OK, null, HandleListAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, WorkflowDesignRead, ConnectionsPermissions.Manage);

        api.MapUnboundOperation("POST", ConnectionsRoute, "ConnectionsCreate",
                typeof(ConnectionMetadata), StatusCodes.Status201Created, null, HandleCreateAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, ConnectionsPermissions.Manage);

        api.MapUnboundOperation("GET", $"{ConnectionsRoute}/descriptors", "ConnectionsDescriptors",
                typeof(ConnectionDescriptorsResponse), StatusCodes.Status200OK, null, HandleDescriptorsAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, WorkflowDesignRead, ConnectionsPermissions.Manage);

        api.MapUnboundOperation("POST", $"{ConnectionsRoute}/picker", "ConnectionsPicker",
                typeof(ConnectionPickerResponse), StatusCodes.Status200OK, null, HandlePickerAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, WorkflowDesignRead, ConnectionsPermissions.Manage);

        api.MapUnboundOperation("GET", ConnectionRoute, "ConnectionsGet",
                typeof(ConnectionMetadata), StatusCodes.Status200OK, null, HandleGetAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, WorkflowDesignRead, ConnectionsPermissions.Manage);

        api.MapUnboundOperation("PUT", ConnectionRoute, "ConnectionsUpdate",
                typeof(ConnectionMetadata), StatusCodes.Status200OK, null, HandleUpdateAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, ConnectionsPermissions.Manage);

        api.MapUnboundOperation("DELETE", ConnectionRoute, "ConnectionsDelete",
                null, StatusCodes.Status204NoContent, null, HandleDeleteAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, ConnectionsPermissions.Manage);

        api.MapUnboundOperation("POST", $"{ConnectionRoute}/revoke", "ConnectionsRevoke",
                typeof(ConnectionMetadata), StatusCodes.Status200OK, null, HandleRevokeAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, ConnectionsPermissions.Manage);

        api.MapUnboundOperation("POST", $"{ConnectionRoute}/test", "ConnectionsTest",
                typeof(ConnectionTestResult), StatusCodes.Status200OK, null, HandleTestAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, ConnectionsPermissions.Test);

        api.MapUnboundOperation("GET", $"{ConnectionRoute}/oauth-redirect-uri", "ConnectionsOAuthRedirectUri",
                typeof(OAuthRedirectUriResponse), StatusCodes.Status200OK, null, HandleOAuthRedirectUriAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, ConnectionsPermissions.Manage);

        endpoints.MapGet(OAuthCallbackPath, HandleOAuthCallbackAsync)
            .WithName("ConnectionsOAuthCallback")
            .AllowAnonymous();
    }

    private static async Task HandleListAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        var query = new ConnectionQuery
        {
            Search = Value(context.Request.Query, "search"),
            TypeName = Value(context.Request.Query, "typeName"),
            AuthScheme = Value(context.Request.Query, "authScheme"),
            PageSize = ParsePageSize(Value(context.Request.Query, "pageSize"))
        };
        var items = await Manager(context).ListAsync(tenantId, query, context.RequestAborted);
        await Json(new ListConnectionsResponse { Items = items }).ExecuteAsync(context);
    }

    private static async Task HandleCreateAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        var request = await ReadBodyAsync<CreateConnectionRequest>(context);
        if (request is null)
            return;

        try
        {
            var result = await Manager(context).CreateAsync(tenantId, request, context.RequestAborted);
            await Json(result, StatusCodes.Status201Created).ExecuteAsync(context);
        }
        catch (Exception exception)
        {
            if (!await WriteMappedErrorAsync(context, exception))
                throw;
        }
    }

    private static async Task HandleDescriptorsAsync(HttpContext context)
    {
        var types = context.RequestServices.GetRequiredService<IConnectionTypeRegistry>().List()
            .Select(provider => provider.Descriptor).ToArray();
        var schemes = context.RequestServices.GetRequiredService<IAuthenticationSchemeRegistry>().List()
            .Select(provider => provider.Descriptor).ToArray();
        await Json(new ConnectionDescriptorsResponse { Types = types, Schemes = schemes }).ExecuteAsync(context);
    }

    private static async Task HandlePickerAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        var request = await ReadBodyAsync<ConnectionPickerRequest>(context);
        if (request is null)
            return;

        var items = await Manager(context).ListAsync(tenantId, new ConnectionQuery
        {
            Search = request.Search,
            TypeName = request.TypeName,
            PageSize = 50
        }, context.RequestAborted);
        await Json(new ConnectionPickerResponse { Items = items }).ExecuteAsync(context);
    }

    private static async Task HandleGetAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        var result = await Manager(context).FindAsync(tenantId, RouteName(context), context.RequestAborted);
        await (result is null ? Results.NotFound() : Json(result)).ExecuteAsync(context);
    }

    private static async Task HandleUpdateAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        var request = await ReadBodyAsync<UpdateConnectionRequest>(context);
        if (request is null)
            return;

        try
        {
            var result = await Manager(context).UpdateAsync(tenantId, RouteName(context), request, context.RequestAborted);
            await (result is null ? Results.NotFound() : Json(result)).ExecuteAsync(context);
        }
        catch (Exception exception)
        {
            if (!await WriteMappedErrorAsync(context, exception))
                throw;
        }
    }

    private static async Task HandleDeleteAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        try
        {
            var deleted = await Manager(context).DeleteAsync(tenantId, RouteName(context), context.RequestAborted);
            await (deleted ? Results.NoContent() : Results.NotFound()).ExecuteAsync(context);
        }
        catch (Exception exception)
        {
            if (!await WriteMappedErrorAsync(context, exception))
                throw;
        }
    }

    private static async Task HandleRevokeAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        try
        {
            var result = await Manager(context).RevokeAsync(tenantId, RouteName(context), context.RequestAborted);
            await (result is null ? Results.NotFound() : Json(result)).ExecuteAsync(context);
        }
        catch (Exception exception)
        {
            if (!await WriteMappedErrorAsync(context, exception))
                throw;
        }
    }

    private static async Task HandleTestAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        try
        {
            var result = await Manager(context).TestAsync(tenantId, RouteName(context), context.RequestAborted);
            await Json(result).ExecuteAsync(context);
        }
        catch (Exception exception)
        {
            if (!await WriteMappedErrorAsync(context, exception))
                throw;
        }
    }

    private static async Task<bool> WriteMappedErrorAsync(HttpContext context, Exception exception)
    {
        switch (exception)
        {
            case ArgumentException argument:
                await Results.Json(
                    new { error = argument.Message },
                    JsonOptions,
                    contentType: "application/json",
                    statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
                return true;
            case ConnectionConflictException conflict:
                await Results.Json(
                    new { error = conflict.Message },
                    JsonOptions,
                    contentType: "application/json",
                    statusCode: StatusCodes.Status409Conflict).ExecuteAsync(context);
                return true;
            case ConnectionCredentialWriteException credentialWrite:
                // 500, not 503: the update committed, so the caller should not blindly retry it, and the cause may be a
                // fault rather than an outage. The message names only fields; the store's failure stays in the logs.
                await Results.Json(
                    new { error = credentialWrite.Message },
                    JsonOptions,
                    contentType: "application/json",
                    statusCode: StatusCodes.Status500InternalServerError).ExecuteAsync(context);
                return true;
            case ConnectionStoreException storeFailure:
                // 500: a store fault, nothing confirmed saved. The message says whether a retry can help and names the connection only; the inner failure stays in the logs.
                await Results.Json(
                    new { error = storeFailure.Message },
                    JsonOptions,
                    contentType: "application/json",
                    statusCode: StatusCodes.Status500InternalServerError).ExecuteAsync(context);
                return true;
            case ConnectionUnavailableException unavailable when string.Equals(unavailable.Code, ConnectionFaultCodes.Forbidden, StringComparison.Ordinal):
                await Results.Json(
                    new { error = unavailable.Message },
                    JsonOptions,
                    contentType: "application/json",
                    statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
                return true;
            default:
                return false;
        }
    }

    private static async Task HandleOAuthRedirectUriAsync(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<ConnectionsOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.PublicBaseUrl))
        {
            await Results.Json(
                new { error = "Elsa:Connections:PublicBaseUrl must be configured before an OAuth redirect URI can be issued." },
                JsonOptions,
                contentType: "application/json",
                statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
            return;
        }

        var origin = options.PublicBaseUrl.TrimEnd('/');
        await Json(new OAuthRedirectUriResponse { RedirectUri = origin + OAuthCallbackPath }).ExecuteAsync(context);
    }

    private static Task HandleOAuthCallbackAsync(HttpContext context)
    {
        // Phase 2 groundwork (D2): the route exists so OAuth schemes can plug in without relocating it.
        return Results.Json(
            new
            {
                status = "unhandled",
                message = "No OAuth authentication scheme handled this callback."
            },
            JsonOptions,
            contentType: "application/json",
            statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpContext context) where T : class
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, JsonOptions, context.RequestAborted);
            if (request is not null)
                return request;
        }
        catch (JsonException)
        {
            await Results.BadRequest().ExecuteAsync(context);
            return null;
        }

        await Results.BadRequest().ExecuteAsync(context);
        return null;
    }

    private static string? Value(IQueryCollection query, string key) =>
        query.TryGetValue(key, out var values) ? values.ToString() : null;

    private static int ParsePageSize(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && size > 0
            ? size
            : 50;

    private static async ValueTask<string?> RequireTenantAsync(HttpContext context)
    {
        var tenantId = context.User.FindFirst(IdentityClaimTypes.TenantId)?.Value ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(tenantId))
            return tenantId;

        await Results.Forbid().ExecuteAsync(context);
        return null;
    }

    private static string RouteName(HttpContext context) =>
        context.Request.RouteValues.TryGetValue("name", out var value) && value is string name ? name : string.Empty;

    private static IConnectionManager Manager(HttpContext context) =>
        context.RequestServices.GetRequiredService<IConnectionManager>();

    private static IResult Json(object value, int statusCode = StatusCodes.Status200OK) =>
        Results.Json(value, JsonOptions, contentType: "application/json", statusCode: statusCode);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        options.MakeReadOnly();
        return options;
    }
}

public sealed class ListConnectionsResponse
{
    public IReadOnlyList<ConnectionMetadata> Items { get; set; } = [];
}

public sealed class ConnectionDescriptorsResponse
{
    public IReadOnlyList<ConnectionTypeDescriptor> Types { get; set; } = [];
    public IReadOnlyList<AuthSchemeDescriptor> Schemes { get; set; } = [];
}

public sealed class ConnectionPickerRequest
{
    public string? Search { get; set; }
    public string? TypeName { get; set; }
}

public sealed class ConnectionPickerResponse
{
    public IReadOnlyList<ConnectionMetadata> Items { get; set; } = [];
}

public sealed class OAuthRedirectUriResponse
{
    public string RedirectUri { get; set; } = "";
}
