using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Studio.Preferences.Api.Models;
using Elsa.Studio.Preferences.Api.Services;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Exceptions;
using Elsa.Studio.Preferences.Core.Models;
using Elsa.Studio.Preferences.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Elsa.Studio.Preferences.Api.Endpoints;

internal sealed class PutStudioPreference(
    IStudioPreferenceService preferences,
    StudioPreferenceScopeResolver scopes) : ElsaEndpoint<PutStudioPreferenceRequest, StudioPreferenceDocument>
{
    public override void Configure()
    {
        Put("/_elsa/studio/preferences/{namespace}");
        ConfigurePermissions(StudioPreferencesPermissions.Write);
    }

    public override async Task HandleAsync(PutStudioPreferenceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var key = await scopes.ResolveAsync(User, HttpContext.Request.Headers[StudioPreferenceScopeResolver.StudioHostIdHeader].FirstOrDefault(), request.Namespace, cancellationToken);
            var condition = StudioPreferencePreconditions.Parse(
                HttpContext.Request.Headers[HeaderNames.IfMatch].FirstOrDefault(),
                HttpContext.Request.Headers[HeaderNames.IfNoneMatch].FirstOrDefault());
            var document = await preferences.WriteAsync(key, new(request.SchemaVersion, request.Value), condition, cancellationToken);
            HttpContext.Response.Headers.ETag = $"\"{document.Revision}\"";
            await Send.OkAsync(document, cancellationToken);
        }
        catch (StudioPreferenceScopeException)
        {
            await Send.UnauthorizedAsync(cancellationToken);
        }
        catch (StudioPreferenceNamespaceNotFoundException)
        {
            await Send.NotFoundAsync(cancellationToken);
        }
        catch (StudioPreferenceHostException exception)
        {
            ThrowError(exception, StatusCodes.Status400BadRequest);
        }
        catch (StudioPreferenceQuotaExceededException exception)
        {
            ThrowError(exception, StatusCodes.Status413PayloadTooLarge);
        }
        catch (StudioPreferenceConflictException exception)
        {
            ThrowError(exception, StatusCodes.Status412PreconditionFailed);
        }
        catch (StudioPreferenceValidationException exception)
        {
            ThrowError(exception, StatusCodes.Status422UnprocessableEntity);
        }
    }

}
