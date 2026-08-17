using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Studio.Preferences.Api.Models;
using Elsa.Studio.Preferences.Api.Services;
using Elsa.Studio.Preferences.Core;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Exceptions;
using Elsa.Studio.Preferences.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;

namespace Elsa.Studio.Preferences.Api;

/// <summary>Maps the Studio Preferences HTTP surface using ordinary ASP.NET Core endpoints.</summary>
public static class StudioPreferencesApi
{
    private const string PreferencesRoute = "/_elsa/studio/preferences/{namespace}";

    public static void MapStudioPreferencesApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var owner = typeof(StudioPreferencesApiFeature).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The Studio Preferences API assembly has no name.");
        RequestDelegate getHandler = HandleGetRequestAsync;
        RequestDelegate putHandler = HandlePutRequestAsync;
        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        endpoints.MapGet(PreferencesRoute, getHandler)
            .WithOwner(owner)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequireAnyPermission(PermissionKey.Wildcard, StudioPreferencesPermissions.Read)
            .WithMetadata(
                descriptionMethod,
                new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(StudioPreferenceDocument), ["application/json"]),
                new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []),
                new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void), []))
            .AddOpenApiOperationTransformer(ConfigureGetOpenApiAsync);

        endpoints.MapPut(PreferencesRoute, putHandler)
            .WithOwner(owner)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequireAnyPermission(PermissionKey.Wildcard, StudioPreferencesPermissions.Write)
            .WithMetadata(
                descriptionMethod,
                new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(StudioPreferenceDocument), ["application/json"]),
                new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []),
                new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void), []))
            .AddOpenApiOperationTransformer(ConfigurePutOpenApiAsync);
    }

    private static async Task HandleGetRequestAsync(HttpContext httpContext)
    {
        var result = await HandleGetAsync(
            GetRouteNamespace(httpContext),
            httpContext,
            httpContext.RequestServices.GetRequiredService<IStudioPreferenceService>(),
            httpContext.RequestServices.GetRequiredService<StudioPreferenceScopeResolver>(),
            httpContext.RequestAborted);
        await result.ExecuteAsync(httpContext);
    }

    private static async Task HandlePutRequestAsync(HttpContext httpContext)
    {
        PutStudioPreferenceRequest? request;
        try
        {
            request = await httpContext.Request.ReadFromJsonAsync<PutStudioPreferenceRequest>(httpContext.RequestAborted);
        }
        catch (System.Text.Json.JsonException)
        {
            await Results.BadRequest().ExecuteAsync(httpContext);
            return;
        }

        if (request is null)
        {
            await Results.BadRequest().ExecuteAsync(httpContext);
            return;
        }

        var result = await HandlePutAsync(
            GetRouteNamespace(httpContext),
            request,
            httpContext,
            httpContext.RequestServices.GetRequiredService<IStudioPreferenceService>(),
            httpContext.RequestServices.GetRequiredService<StudioPreferenceScopeResolver>(),
            httpContext.RequestAborted);
        await result.ExecuteAsync(httpContext);
    }

    private static string GetRouteNamespace(HttpContext httpContext) =>
        httpContext.Request.RouteValues.TryGetValue("namespace", out var value) && value is string @namespace
            ? @namespace
            : string.Empty;

    private static async Task<IResult> HandleGetAsync(
        string @namespace,
        HttpContext httpContext,
        [FromServices] IStudioPreferenceService preferences,
        [FromServices] StudioPreferenceScopeResolver scopes,
        CancellationToken cancellationToken)
    {
        try
        {
            var key = await scopes.ResolveAsync(
                httpContext.User,
                httpContext.Request.Headers[StudioPreferenceScopeResolver.StudioHostIdHeader].FirstOrDefault(),
                @namespace,
                cancellationToken);
            var document = await preferences.FindAsync(key, cancellationToken);
            if (document is null)
                return Results.NotFound();

            httpContext.Response.Headers.ETag = $"\"{document.Revision}\"";
            return Results.Json(document, contentType: "application/json");
        }
        catch (StudioPreferenceScopeException)
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }
        catch (StudioPreferenceNamespaceNotFoundException)
        {
            return Results.NotFound();
        }
        catch (StudioPreferenceHostException exception)
        {
            return CreateProblem(httpContext, exception, StatusCodes.Status400BadRequest);
        }
        catch (StudioPreferenceValidationException exception)
        {
            return CreateProblem(httpContext, exception, StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> HandlePutAsync(
        string @namespace,
        PutStudioPreferenceRequest request,
        HttpContext httpContext,
        [FromServices] IStudioPreferenceService preferences,
        [FromServices] StudioPreferenceScopeResolver scopes,
        CancellationToken cancellationToken)
    {
        try
        {
            var key = await scopes.ResolveAsync(
                httpContext.User,
                httpContext.Request.Headers[StudioPreferenceScopeResolver.StudioHostIdHeader].FirstOrDefault(),
                @namespace,
                cancellationToken);
            var condition = StudioPreferencePreconditions.Parse(
                httpContext.Request.Headers[HeaderNames.IfMatch].FirstOrDefault(),
                httpContext.Request.Headers[HeaderNames.IfNoneMatch].FirstOrDefault());
            var document = await preferences.WriteAsync(
                key,
                new(request.SchemaVersion, request.Value),
                condition,
                cancellationToken);

            httpContext.Response.Headers.ETag = $"\"{document.Revision}\"";
            return Results.Json(document, contentType: "application/json");
        }
        catch (StudioPreferenceScopeException)
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }
        catch (StudioPreferenceNamespaceNotFoundException)
        {
            return Results.NotFound();
        }
        catch (StudioPreferenceHostException exception)
        {
            return CreateProblem(httpContext, exception, StatusCodes.Status400BadRequest);
        }
        catch (StudioPreferenceQuotaExceededException exception)
        {
            return CreateProblem(httpContext, exception, StatusCodes.Status413PayloadTooLarge);
        }
        catch (StudioPreferenceConflictException exception)
        {
            return CreateProblem(httpContext, exception, StatusCodes.Status412PreconditionFailed);
        }
        catch (StudioPreferenceValidationException exception)
        {
            return CreateProblem(httpContext, exception, StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static IResult CreateProblem(HttpContext httpContext, Exception exception, int statusCode)
    {
        var detail = exception.Message;
        var extensions = new Dictionary<string, object?>
        {
            ["errors"] = new[] { new { name = "generalErrors", reason = detail } },
            ["traceId"] = httpContext.TraceIdentifier
        };

        return Results.Problem(
            detail: detail,
            instance: httpContext.Request.Path,
            statusCode: statusCode,
            title: GetTitle(statusCode),
            type: GetTypeUri(statusCode),
            extensions: extensions);
    }

    private static string GetTitle(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status412PreconditionFailed => "Precondition Failed",
        StatusCodes.Status413PayloadTooLarge => "Payload Too Large",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
        _ => "Error"
    };

    private static string GetTypeUri(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1",
        StatusCodes.Status412PreconditionFailed => "https://www.rfc-editor.org/rfc/rfc7232#section-4.2",
        StatusCodes.Status413PayloadTooLarge => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.11",
        StatusCodes.Status422UnprocessableEntity => "https://www.rfc-editor.org/rfc/rfc4918#section-11.2",
        _ => "about:blank"
    };

    private static Task ConfigureGetOpenApiAsync(
        Microsoft.OpenApi.OpenApiOperation operation,
        Microsoft.AspNetCore.OpenApi.OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken) =>
        ConfigureLegacyOpenApiAsync(
            operation,
            context,
            typeof(GetStudioPreferenceRequest),
            ["*/*", "application/json"],
            cancellationToken);

    private static Task ConfigurePutOpenApiAsync(
        Microsoft.OpenApi.OpenApiOperation operation,
        Microsoft.AspNetCore.OpenApi.OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken) =>
        ConfigureLegacyOpenApiAsync(
            operation,
            context,
            typeof(PutStudioPreferenceRequest),
            ["application/json"],
            cancellationToken);

    private static async Task ConfigureLegacyOpenApiAsync(
        Microsoft.OpenApi.OpenApiOperation operation,
        Microsoft.AspNetCore.OpenApi.OpenApiOperationTransformerContext context,
        Type requestType,
        string[] contentTypes,
        CancellationToken cancellationToken)
    {
        operation.Parameters?.Clear();
        var schema = await context.GetOrCreateSchemaAsync(requestType, parameterDescription: null, cancellationToken);
        var document = context.Document ?? throw new InvalidOperationException("The OpenAPI document is unavailable.");
        var components = document.Components ??= new OpenApiComponents();
        var schemas = components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        if (requestType == typeof(PutStudioPreferenceRequest) && schema.Properties is not null)
            schema.Properties["value"] = new OpenApiSchemaReference("JsonElement", document);
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
