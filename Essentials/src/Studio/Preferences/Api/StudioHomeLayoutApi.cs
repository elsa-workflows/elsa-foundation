using Elsa.Api.AspNetCore;
using NativeEndpoints;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Studio.Preferences.Api.Models;
using Elsa.Studio.Preferences.Api.Services;
using Elsa.Studio.Preferences.Core;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Exceptions;
using Elsa.Studio.Preferences.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Elsa.Studio.Preferences.Api;

/// <summary>
/// Per-user and admin-default Home layout surface. Persistence reuses
/// <see cref="IStudioPreferenceStore"/>; the reserved subject
/// <see cref="StudioHomeLayout.DefaultSubjectId"/> holds the tenant/host default.
/// </summary>
public static class StudioHomeLayoutApi
{
    public const string UserRoute = "/_elsa/studio/home-layout";
    public const string DefaultRoute = "/_elsa/studio/home-layout/default";

    /// <summary>
    /// Host-control keys owned by module-management. Preferences does not take a project
    /// reference on Modularity; the strings are the stable catalog keys.
    /// </summary>
    public const string HostControlRead = "module-management.read";
    public const string HostControlManage = "module-management.manage";

    public static void MapStudioHomeLayoutApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var owner = typeof(StudioPreferencesApiFeature).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The Studio Preferences API assembly has no name.");
        var api = endpoints.MapEndpointGroup(owner, StudioPreferencesJsonContext.Default, jsonContentType: "application/json");

        api.MapUnboundOperation("GET", UserRoute, "GetHomeLayout",
                typeof(HomeLayoutDocument), StatusCodes.Status200OK, null, HandleGetUserAsync,
                containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, StudioPreferencesPermissions.Read);

        api.MapUnboundOperation("PUT", UserRoute, "PutHomeLayout",
                typeof(HomeLayoutDocument), StatusCodes.Status200OK, null, HandlePutUserAsync,
                containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, StudioPreferencesPermissions.Write);

        api.MapUnboundOperation("DELETE", UserRoute, "DeleteHomeLayout",
                null, StatusCodes.Status204NoContent, null, HandleDeleteUserAsync,
                containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, StudioPreferencesPermissions.Write);

        api.MapUnboundOperation("GET", DefaultRoute, "GetHomeLayoutDefault",
                typeof(HomeLayoutDocument), StatusCodes.Status200OK, null, HandleGetDefaultAsync,
                containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, HostControlRead, HostControlManage);

        api.MapUnboundOperation("PUT", DefaultRoute, "PutHomeLayoutDefault",
                typeof(HomeLayoutDocument), StatusCodes.Status200OK, null, HandlePutDefaultAsync,
                containFailures: false)
            .RequireAnyPermission(PermissionKey.Wildcard, HostControlManage);
    }

    private static async Task HandleGetUserAsync(HttpContext httpContext)
    {
        await ExecuteGetAsync(httpContext, adminDefault: false);
    }

    private static async Task HandleGetDefaultAsync(HttpContext httpContext)
    {
        await ExecuteGetAsync(httpContext, adminDefault: true);
    }

    private static async Task HandlePutUserAsync(HttpContext httpContext)
    {
        await ExecutePutAsync(httpContext, adminDefault: false);
    }

    private static async Task HandlePutDefaultAsync(HttpContext httpContext)
    {
        await ExecutePutAsync(httpContext, adminDefault: true);
    }

    private static async Task HandleDeleteUserAsync(HttpContext httpContext)
    {
        try
        {
            var scopes = httpContext.RequestServices.GetRequiredService<StudioPreferenceScopeResolver>();
            var preferences = httpContext.RequestServices.GetRequiredService<IStudioPreferenceService>();
            var key = await scopes.ResolveAsync(
                httpContext.User,
                httpContext.Request.Headers[StudioPreferenceScopeResolver.StudioHostIdHeader].FirstOrDefault(),
                StudioPreferenceNamespaces.HomeLayout,
                httpContext.RequestAborted);
            await preferences.DeleteAsync(key, httpContext.RequestAborted);
            await Results.NoContent().ExecuteAsync(httpContext);
        }
        catch (StudioPreferenceScopeException)
        {
            await Results.StatusCode(StatusCodes.Status401Unauthorized).ExecuteAsync(httpContext);
        }
        catch (StudioPreferenceHostException exception)
        {
            await CreateProblem(httpContext, exception, StatusCodes.Status400BadRequest).ExecuteAsync(httpContext);
        }
    }

    private static async Task ExecuteGetAsync(HttpContext httpContext, bool adminDefault)
    {
        try
        {
            var scopes = httpContext.RequestServices.GetRequiredService<StudioPreferenceScopeResolver>();
            var preferences = httpContext.RequestServices.GetRequiredService<IStudioPreferenceService>();
            var hostId = httpContext.Request.Headers[StudioPreferenceScopeResolver.StudioHostIdHeader].FirstOrDefault();
            var userKey = await scopes.ResolveAsync(
                httpContext.User,
                hostId,
                StudioPreferenceNamespaces.HomeLayout,
                httpContext.RequestAborted);
            var defaultKey = userKey with { SubjectId = StudioHomeLayout.DefaultSubjectId };

            if (adminDefault)
            {
                var storedDefault = await preferences.FindAsync(defaultKey, httpContext.RequestAborted);
                var response = ToDocument(storedDefault, storedDefault is null ? "builtin" : "default");
                if (response.Revision is not null)
                    httpContext.Response.Headers.ETag = $"\"{response.Revision}\"";
                await Results.Json(response, contentType: "application/json").ExecuteAsync(httpContext);
                return;
            }

            var userDocument = await preferences.FindAsync(userKey, httpContext.RequestAborted);
            if (userDocument is not null)
            {
                httpContext.Response.Headers.ETag = $"\"{userDocument.Revision}\"";
                await Results.Json(ToDocument(userDocument, "user"), contentType: "application/json").ExecuteAsync(httpContext);
                return;
            }

            var fallback = await preferences.FindAsync(defaultKey, httpContext.RequestAborted);
            await Results.Json(
                    ToDocument(fallback, fallback is null ? "builtin" : "default"),
                    contentType: "application/json")
                .ExecuteAsync(httpContext);
        }
        catch (StudioPreferenceScopeException)
        {
            await Results.StatusCode(StatusCodes.Status401Unauthorized).ExecuteAsync(httpContext);
        }
        catch (StudioPreferenceHostException exception)
        {
            await CreateProblem(httpContext, exception, StatusCodes.Status400BadRequest).ExecuteAsync(httpContext);
        }
        catch (StudioPreferenceNamespaceNotFoundException)
        {
            await Results.NotFound().ExecuteAsync(httpContext);
        }
    }

    private static async Task ExecutePutAsync(HttpContext httpContext, bool adminDefault)
    {
        PutHomeLayoutRequest? request;
        try
        {
            request = await httpContext.Request.ReadFromJsonAsync<PutHomeLayoutRequest>(httpContext.RequestAborted);
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

        try
        {
            var scopes = httpContext.RequestServices.GetRequiredService<StudioPreferenceScopeResolver>();
            var preferences = httpContext.RequestServices.GetRequiredService<IStudioPreferenceService>();
            var hostId = httpContext.Request.Headers[StudioPreferenceScopeResolver.StudioHostIdHeader].FirstOrDefault();
            var key = adminDefault
                ? await scopes.ResolveDefaultAsync(
                    httpContext.User,
                    hostId,
                    StudioPreferenceNamespaces.HomeLayout,
                    httpContext.RequestAborted)
                : await scopes.ResolveAsync(
                    httpContext.User,
                    hostId,
                    StudioPreferenceNamespaces.HomeLayout,
                    httpContext.RequestAborted);
            var condition = StudioPreferencePreconditions.Parse(
                httpContext.Request.Headers[HeaderNames.IfMatch].FirstOrDefault(),
                httpContext.Request.Headers[HeaderNames.IfNoneMatch].FirstOrDefault());
            var value = StudioHomeLayout.Normalize(request.Value);
            var document = await preferences.WriteAsync(
                key,
                new(request.SchemaVersion, value),
                condition,
                httpContext.RequestAborted);

            httpContext.Response.Headers.ETag = $"\"{document.Revision}\"";
            await Results.Json(
                    ToDocument(document, adminDefault ? "default" : "user"),
                    contentType: "application/json")
                .ExecuteAsync(httpContext);
        }
        catch (StudioPreferenceScopeException)
        {
            await Results.StatusCode(StatusCodes.Status401Unauthorized).ExecuteAsync(httpContext);
        }
        catch (StudioPreferenceNamespaceNotFoundException)
        {
            await Results.NotFound().ExecuteAsync(httpContext);
        }
        catch (StudioPreferenceHostException exception)
        {
            await CreateProblem(httpContext, exception, StatusCodes.Status400BadRequest).ExecuteAsync(httpContext);
        }
        catch (StudioPreferenceQuotaExceededException exception)
        {
            await CreateProblem(httpContext, exception, StatusCodes.Status413PayloadTooLarge).ExecuteAsync(httpContext);
        }
        catch (StudioPreferenceConflictException exception)
        {
            await CreateProblem(httpContext, exception, StatusCodes.Status412PreconditionFailed).ExecuteAsync(httpContext);
        }
        catch (StudioPreferenceValidationException exception)
        {
            await CreateProblem(httpContext, exception, StatusCodes.Status422UnprocessableEntity).ExecuteAsync(httpContext);
        }
    }

    private static HomeLayoutDocument ToDocument(StudioPreferenceDocument? document, string source)
    {
        if (document is null)
        {
            return new HomeLayoutDocument
            {
                Source = "builtin",
                Namespace = StudioPreferenceNamespaces.HomeLayout,
                SchemaVersion = StudioHomeLayout.SchemaVersion,
                Value = StudioHomeLayout.BuiltInValue.Clone()
            };
        }

        return new HomeLayoutDocument
        {
            Source = source,
            Namespace = document.Namespace,
            SchemaVersion = document.SchemaVersion,
            Revision = document.Revision,
            Value = document.Value.Clone(),
            UpdatedAt = document.UpdatedAt
        };
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
            title: statusCode switch
            {
                StatusCodes.Status400BadRequest => "Bad Request",
                StatusCodes.Status412PreconditionFailed => "Precondition Failed",
                StatusCodes.Status413PayloadTooLarge => "Payload Too Large",
                StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
                _ => "Error"
            },
            type: statusCode switch
            {
                StatusCodes.Status400BadRequest => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.1",
                StatusCodes.Status412PreconditionFailed => "https://www.rfc-editor.org/rfc/rfc7232#section-4.2",
                StatusCodes.Status413PayloadTooLarge => "https://www.rfc-editor.org/rfc/rfc7231#section-6.5.11",
                StatusCodes.Status422UnprocessableEntity => "https://www.rfc-editor.org/rfc/rfc4918#section-11.2",
                _ => "about:blank"
            },
            extensions: extensions);
    }
}
