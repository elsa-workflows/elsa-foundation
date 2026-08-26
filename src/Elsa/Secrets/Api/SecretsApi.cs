using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Api.Endpoints;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Secrets.Api.Requests;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Permissions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Elsa.Secrets.Api;

/// <summary>Maps the Secrets REST surface using ordinary ASP.NET Core endpoints.</summary>
public static class SecretsApi
{
    private const string SecretsRoute = "/secrets";
    private const string SecretRoute = "/secrets/{name}";
    private const string OwnerId = "Elsa.Secrets.Api";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>Maps all module-owned Secrets endpoints.</summary>
    public static void MapSecretsApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Every operation keeps its own tenant gate, reads, writes, and problem shapes, so the
        // surface stays on the group's raw seam; the operation names restore the historical
        // ElsaSecretsApiEndpointsSecrets* identities the owner published before the Minimal API
        // rewrite dropped endpoint names.
        var api = endpoints.MapModuleEndpoints(OwnerId, SecretsJsonContext.Default, jsonContentType: "application/json");

        api.MapUnboundOperation("GET", SecretsRoute, "SecretsList",
                typeof(ListSecretsResponse), StatusCodes.Status200OK, null, HandleListAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, SecretsPermissions.Read)
            .AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
                ConfigureLegacyOpenApiAsync(operation, context, typeof(SecretQuery), ["*/*", "application/json"], cancellationToken));

        // The legacy endpoint returns 201 at runtime but advertises 200 in OpenAPI; preserve both contracts.
        api.MapUnboundOperation("POST", SecretsRoute, "SecretsCreate",
                typeof(SecretMetadata), StatusCodes.Status201Created, StatusCodes.Status200OK, HandleCreateAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, SecretsPermissions.Write)
            .AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
                ConfigureLegacyOpenApiAsync(operation, context, typeof(CreateSecretRequest), ["application/json"], cancellationToken));

        api.MapUnboundOperation("GET", $"{SecretsRoute}/descriptors", "SecretsDescriptors",
                typeof(SecretDescriptorsResponse), StatusCodes.Status200OK, null, HandleDescriptorsAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, SecretsPermissions.Read);

        api.MapUnboundOperation("POST", $"{SecretsRoute}/picker", "SecretsPicker",
                typeof(SecretPickerResponse), StatusCodes.Status200OK, null, HandlePickerAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, SecretsPermissions.Read)
            .AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
                ConfigureLegacyOpenApiAsync(operation, context, typeof(SecretPickerRequest), ["application/json"], cancellationToken));

        api.MapUnboundOperation("GET", SecretRoute, "SecretsGet",
                typeof(SecretMetadata), StatusCodes.Status200OK, null, HandleGetAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, SecretsPermissions.Read)
            .AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
                ConfigureLegacyOpenApiAsync(operation, context, typeof(GetSecretRequest), ["*/*", "application/json"], cancellationToken));

        api.MapUnboundOperation("PUT", SecretRoute, "SecretsUpdate",
                typeof(SecretMetadata), StatusCodes.Status200OK, null, HandleUpdateAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, SecretsPermissions.Write)
            .AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
                ConfigureLegacyOpenApiAsync(operation, context, typeof(UpdateSecretApiRequest), ["application/json"], cancellationToken));

        api.MapUnboundOperation("DELETE", SecretRoute, "SecretsDelete",
                null, StatusCodes.Status204NoContent, null, HandleDeleteAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, SecretsPermissions.Delete)
            .AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
                ConfigureLegacyOpenApiAsync(operation, context, typeof(DeleteSecretRequest), ["*/*", "application/json"], cancellationToken));

        api.MapUnboundOperation("POST", $"{SecretRoute}/revoke", "SecretsRevoke",
                typeof(SecretMetadata), StatusCodes.Status200OK, null, HandleRevokeAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, SecretsPermissions.Delete)
            .AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
                ConfigureLegacyOpenApiAsync(operation, context, typeof(RevokeSecretRequest), ["application/json"], cancellationToken));

        api.MapUnboundOperation("POST", $"{SecretRoute}/rotate", "SecretsRotate",
                typeof(SecretMetadata), StatusCodes.Status200OK, null, HandleRotateAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, SecretsPermissions.UpdateValue)
            .AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
                ConfigureLegacyOpenApiAsync(operation, context, typeof(RotateSecretApiRequest), ["application/json"], cancellationToken));

        api.MapUnboundOperation("POST", $"{SecretRoute}/test", "SecretsTest",
                typeof(SecretTestResult), StatusCodes.Status200OK, null, HandleTestAsync, containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, SecretsPermissions.Test)
            .AddOpenApiOperationTransformer((operation, context, cancellationToken) =>
                ConfigureLegacyOpenApiAsync(operation, context, typeof(TestSecretRequest), ["application/json"], cancellationToken));
    }

    private static async Task HandleListAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        if (!TryBindQuery(context.Request.Query, out var query, out var bindingError))
        {
            await QueryBindingProblem(bindingError).ExecuteAsync(context);
            return;
        }

        var page = await Manager(context).ListAsync(tenantId, query, context.RequestAborted);
        await Json(ListSecretsResponse.FromPage(page)).ExecuteAsync(context);
    }

    private static async Task HandleCreateAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        var request = await ReadBodyAsync<CreateSecretRequest>(context);
        if (request is null)
            return;

        var result = await Manager(context).CreateAsync(tenantId, request, context.RequestAborted);
        await Json(result, StatusCodes.Status201Created).ExecuteAsync(context);
    }

    private static async Task HandleDescriptorsAsync(HttpContext context)
    {
        var types = context.RequestServices.GetRequiredService<ISecretTypeRegistry>().List().ToArray();
        var stores = context.RequestServices.GetRequiredService<ISecretStoreRegistry>().List()
            .Select(store => store.Descriptor).ToArray();
        await Json(new SecretDescriptorsResponse { Types = types, Stores = stores }).ExecuteAsync(context);
    }

    private static async Task HandlePickerAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        var request = await ReadBodyAsync<SecretPickerRequest>(context);
        if (request is null)
            return;

        var page = await Manager(context).ListAsync(tenantId, new SecretQuery
        {
            Search = request.Search,
            TypeNames = request.TypeNames,
            StoreNames = request.StoreNames,
            Scope = request.Scope,
            ActiveOnly = request.ActiveOnly,
            PageSize = 50
        }, context.RequestAborted);
        await Json(new SecretPickerResponse { Items = page.Items, CanCreateInline = true }).ExecuteAsync(context);
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

        var request = await ReadBodyAsync<UpdateSecretApiRequest>(context);
        if (request is null)
            return;

        var result = await Manager(context).UpdateAsync(tenantId, RouteName(context), new UpdateSecretMetadataRequest
        {
            DisplayName = request.DisplayName,
            Description = request.Description
        }, context.RequestAborted);
        await Json(result).ExecuteAsync(context);
    }

    private static async Task HandleDeleteAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        var deleted = await Manager(context).DeleteAsync(tenantId, RouteName(context), context.RequestAborted);
        await (deleted ? Results.NoContent() : Results.NotFound()).ExecuteAsync(context);
    }

    private static async Task HandleRevokeAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        var result = await Manager(context).RevokeAsync(tenantId, RouteName(context), context.RequestAborted);
        await (result is null ? Results.NotFound() : Json(result)).ExecuteAsync(context);
    }

    private static async Task HandleRotateAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        var request = await ReadBodyAsync<RotateSecretApiRequest>(context);
        if (request is null)
            return;

        var result = await Manager(context).RotateAsync(tenantId, RouteName(context), new RotateSecretRequest
        {
            Value = request.Value,
            ConfigurationKey = request.ConfigurationKey,
            ExpiresAt = request.ExpiresAt,
            Metadata = request.Metadata
        }, context.RequestAborted);
        await Json(result).ExecuteAsync(context);
    }

    private static async Task HandleTestAsync(HttpContext context)
    {
        var tenantId = await RequireTenantAsync(context);
        if (tenantId is null)
            return;

        var result = await Manager(context).TestAsync(tenantId, RouteName(context), context.RequestAborted);
        await Json(result).ExecuteAsync(context);
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpContext context) where T : class
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, JsonOptions, context.RequestAborted);
            if (request is not null)
                return request;
        }
        catch (JsonException exception)
        {
            await BindingProblem(context, exception).ExecuteAsync(context);
            return null;
        }

        await Results.BadRequest().ExecuteAsync(context);
        return null;
    }

    private static IResult BindingProblem(HttpContext context, JsonException exception)
    {
        var detail = exception.Path is null
            ? exception.Message
            : exception.Message.Replace($" Path: {exception.Path} |", "", StringComparison.Ordinal);
        var name = exception.Path?.TrimStart('$', '.').Split(['.', '['], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "generalErrors";
        return Results.Problem(
            detail: detail,
            instance: context.Request.Path,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            type: "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1",
            extensions: new Dictionary<string, object?>
            {
                ["errors"] = new[] { new { name, reason = detail } },
                ["traceId"] = context.TraceIdentifier
            });
    }

    private static bool TryBindQuery(
        IQueryCollection values,
        out SecretQuery query,
        out QueryBindingError error)
    {
        var statusValue = Value(values, "status");
        if (statusValue is not null && !Enum.TryParse<SecretStatus>(statusValue, true, out _))
            return Invalid("status", statusValue, nameof(SecretStatus), out query, out error);

        var activeOnlyValue = Value(values, "activeOnly");
        if (activeOnlyValue is not null && !bool.TryParse(activeOnlyValue, out _))
            return Invalid("activeOnly", activeOnlyValue, nameof(Boolean), out query, out error);

        var pageValue = Value(values, "page");
        if (pageValue is not null && !int.TryParse(pageValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return Invalid("page", pageValue, nameof(Int32), out query, out error);

        var pageSizeValue = Value(values, "pageSize");
        if (pageSizeValue is not null && !int.TryParse(pageSizeValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return Invalid("pageSize", pageSizeValue, nameof(Int32), out query, out error);

        query = new SecretQuery
        {
            Search = Value(values, "search"),
            TypeName = Value(values, "typeName"),
            TypeNames = Values(values, "typeNames"),
            StoreName = Value(values, "storeName"),
            StoreNames = Values(values, "storeNames"),
            Scope = Value(values, "scope"),
            Status = statusValue is null ? null : Enum.Parse<SecretStatus>(statusValue, true),
            ActiveOnly = activeOnlyValue is not null && bool.Parse(activeOnlyValue),
            Page = pageValue is null ? null : int.Parse(pageValue, NumberStyles.Integer, CultureInfo.InvariantCulture),
            PageSize = pageSizeValue is null ? null : int.Parse(pageSizeValue, NumberStyles.Integer, CultureInfo.InvariantCulture)
        };
        error = default;
        return true;
    }

    private static bool Invalid(
        string name,
        string value,
        string typeName,
        out SecretQuery query,
        out QueryBindingError error)
    {
        query = new SecretQuery();
        error = new QueryBindingError(name, $"Value [{value}] is not valid for a [{typeName}] property!");
        return false;
    }

    private static IResult QueryBindingProblem(QueryBindingError error) =>
        Results.Json(
            new
            {
                statusCode = StatusCodes.Status400BadRequest,
                message = "One or more errors occurred!",
                errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [error.Name] = [error.Reason]
                }
            },
            JsonOptions,
            contentType: "application/problem+json",
            statusCode: StatusCodes.Status400BadRequest);

    private static string? Value(IQueryCollection query, string key) =>
        query.TryGetValue(key, out var values) ? values.ToString() : null;

    private static ICollection<string> Values(IQueryCollection query, string key) =>
        query.TryGetValue(key, out var values) ? values.Where(value => value is not null).Select(value => value!).ToArray() : [];

    private static bool TryGetTenant(ClaimsPrincipal principal, out string tenantId)
    {
        tenantId = principal.FindFirst(IdentityClaimTypes.TenantId)?.Value ?? string.Empty;
        return !string.IsNullOrWhiteSpace(tenantId);
    }

    private static async ValueTask<string?> RequireTenantAsync(HttpContext context)
    {
        if (TryGetTenant(context.User, out var tenantId))
            return tenantId;

        await Results.Forbid().ExecuteAsync(context);
        return null;
    }

    private static string RouteName(HttpContext context) =>
        context.Request.RouteValues.TryGetValue("name", out var value) && value is string name ? name : string.Empty;

    private static ISecretManager Manager(HttpContext context) =>
        context.RequestServices.GetRequiredService<ISecretManager>();

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
        return options;
    }

    private readonly record struct QueryBindingError(string Name, string Reason);

    private static async Task ConfigureLegacyOpenApiAsync(
        OpenApiOperation operation,
        Microsoft.AspNetCore.OpenApi.OpenApiOperationTransformerContext context,
        Type requestType,
        string[] contentTypes,
        CancellationToken cancellationToken)
    {
        // The consumed legacy document represents requests, including GET/DELETE, as bodies and omits
        // route/query parameters. Keep that non-standard shape until a separately approved contract change.
        operation.Parameters?.Clear();
        var schema = await context.GetOrCreateSchemaAsync(requestType, parameterDescription: null, cancellationToken);
        var document = context.Document ?? throw new InvalidOperationException("The OpenAPI document is unavailable.");
        var components = document.Components ??= new OpenApiComponents();
        var schemas = components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        if (requestType == typeof(SecretQuery) && schema.Properties is not null)
        {
            schema.Properties["status"] = new OpenApiSchema
            {
                OneOf =
                [
                    new OpenApiSchema { Type = JsonSchemaType.Null },
                    new OpenApiSchemaReference(nameof(SecretStatus), document)
                ]
            };
        }
        schemas[requestType.Name] = schema;
        var schemaReference = new OpenApiSchemaReference(requestType.Name, document);
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = contentTypes.ToDictionary(
                contentType => contentType,
                _ => new OpenApiMediaType { Schema = schemaReference },
                StringComparer.Ordinal)
        };
    }
}
