using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Studio.Preferences.Api.Models;
using Elsa.Studio.Preferences.Api.Services;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Exceptions;
using Elsa.Studio.Preferences.Core.Models;
using Elsa.Studio.Preferences.Core;
using Microsoft.AspNetCore.Http;

namespace Elsa.Studio.Preferences.Api.Endpoints;

internal sealed class GetStudioPreference(
    IStudioPreferenceService preferences,
    StudioPreferenceScopeResolver scopes) : ElsaEndpoint<GetStudioPreferenceRequest, StudioPreferenceDocument>
{
    public override void Configure()
    {
        Get("/_elsa/studio/preferences/{namespace}");
        ConfigurePermissions(StudioPreferencesPermissions.Read);
    }

    public override async Task HandleAsync(GetStudioPreferenceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var key = await scopes.ResolveAsync(User, HttpContext.Request.Headers[StudioPreferenceScopeResolver.StudioHostIdHeader].FirstOrDefault(), request.Namespace, cancellationToken);
            var document = await preferences.FindAsync(key, cancellationToken);
            if (document is null)
            {
                await Send.NotFoundAsync(cancellationToken);
                return;
            }

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
        catch (StudioPreferenceValidationException exception)
        {
            ThrowError(exception, StatusCodes.Status400BadRequest);
        }
    }
}
