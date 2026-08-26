using Elsa.Api.AspNetCore;
using Elsa.Api.Endpoints;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Api.Constants;
using Elsa.Foundation.Identity.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Foundation.Identity.Api;

/// <summary>Maps the provider-neutral Foundation Identity protocol using ordinary ASP.NET Core endpoints.</summary>
public static class FoundationIdentityApi
{
    private const string PublicCategory = "identity-protocol";
    private const string PublicReason = "Provider-neutral identity protocol entry point.";

    public static void MapFoundationIdentityApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var owner = typeof(FoundationIdentityApiFeature).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The Foundation Identity API assembly has no name.");

        // The published operation ids predate the naming scheme, the tag is the literal "Identity",
        // the public entry points document no auth responses, and the flows own challenges,
        // sign-outs, and bare failure statuses — so the surface stays on the group's raw seam.
        var api = endpoints.MapModuleEndpoints(owner, FoundationIdentityApiJsonContext.Default, tag: "Identity");

        api.MapUnboundOperation("GET", Route("bootstrap"), "Bootstrap",
                typeof(IdentityBootstrapResponse), StatusCodes.Status200OK, null, HandleBootstrapAsync,
                name: "FoundationIdentityBootstrap", documentAuthResponses: false, containFailures: false)
            .AllowPublic(PublicCategory, PublicReason);

        api.MapUnboundOperation("GET", Route("capabilities"), "Capabilities",
                typeof(IdentityCapabilitiesResponse), StatusCodes.Status200OK, null, HandleCapabilitiesAsync,
                name: "FoundationIdentityCapabilities", containFailures: false)
            .RequirePermission(DefaultIdentityPermissionKeys.IdentityProvidersRead);

        api.MapUnboundOperation("GET", Route("session"), "Session",
                typeof(AuthSession), StatusCodes.Status200OK, null, HandleSessionAsync,
                name: "FoundationIdentitySession", documentAuthResponses: false, containFailures: false)
            .AllowPublic(PublicCategory, PublicReason);

        api.MapUnboundOperation("GET", Route("token"), "Token",
                typeof(AccessTokenResponse), StatusCodes.Status200OK, null, HandleTokenAsync,
                name: "FoundationIdentityToken", documentAuthResponses: false, containFailures: false)
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []))
            .AllowPublic(PublicCategory, PublicReason);

        api.MapUnboundOperation("GET", Route("challenge/{provider}"), "Challenge",
                null, StatusCodes.Status200OK, null, HandleChallengeAsync,
                name: "FoundationIdentityChallenge", documentAuthResponses: false, containFailures: false)
            .AllowPublic(PublicCategory, PublicReason);

        api.MapUnboundOperation("POST", Route("logout/{provider}"), "Logout",
                null, StatusCodes.Status204NoContent, null, HandleLogoutAsync,
                name: "FoundationIdentityLogout", documentAuthResponses: false, containFailures: false)
            .AllowPublic(PublicCategory, PublicReason);

        api.MapUnboundOperation("POST", Route("refresh"), "Refresh",
                typeof(TokenRefreshResult), StatusCodes.Status200OK, null, HandleRefreshAsync,
                name: "FoundationIdentityRefresh", documentAuthResponses: false, containFailures: false)
            .WithMetadata(
                new AcceptsMetadata(["application/json"], typeof(RefreshTokenRequest), false),
                new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(void), []),
                new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []))
            .AllowPublic(PublicCategory, PublicReason);
    }

    private static async Task HandleBootstrapAsync(HttpContext context)
    {
        var ownership = await context.RequestServices.GetRequiredService<IOwnershipModeProvider>()
            .GetAsync(cancellationToken: context.RequestAborted);
        var providers = await context.RequestServices.GetRequiredService<IAuthenticationProviderResolver>()
            .ListAsync(context.RequestAborted);
        var response = new IdentityBootstrapResponse(
            ownership.Mode,
            providers.Select(x => new IdentityProviderResponse(
                x.Id, x.Kind, x.DisplayName, x.IsDefault, x.Enabled, x.Challenge)).ToList());

        await Results.Json(response, FoundationIdentityApiJsonContext.Default.IdentityBootstrapResponse).ExecuteAsync(context);
    }

    private static async Task HandleCapabilitiesAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var ownership = await services.GetRequiredService<IOwnershipModeProvider>()
            .GetAsync(cancellationToken: context.RequestAborted);
        var providers = await services.GetRequiredService<IAuthenticationProviderResolver>()
            .ListAsync(context.RequestAborted);
        var capabilitiesResolver = services.GetRequiredService<IEffectiveCapabilitiesResolver>();
        var permissions = services.GetRequiredService<IPermissionCatalog>();
        var response = new IdentityCapabilitiesResponse(
            ownership.Mode,
            providers.Select(x => new IdentityProviderCapabilitiesResponse(
                x.Id,
                x.Kind,
                x.DisplayName,
                x.IsDefault,
                x.Enabled,
                capabilitiesResolver.Resolve(ownership, x.Capabilities))).ToList(),
            permissions.List().Select(x => new IdentityPermissionResponse(
                x.Key,
                x.DisplayName,
                x.Category,
                x.Description,
                x.Implies?.ToArray() ?? [])).ToList());

        await Results.Json(response, FoundationIdentityApiJsonContext.Default.IdentityCapabilitiesResponse).ExecuteAsync(context);
    }

    private static async Task HandleSessionAsync(HttpContext context)
    {
        var session = await context.RequestServices.GetRequiredService<IAuthSessionService>()
            .GetAsync(context.User, context.RequestAborted);
        await Results.Json(session, FoundationIdentityApiJsonContext.Default.AuthSession).ExecuteAsync(context);
    }

    private static async Task HandleTokenAsync(HttpContext context)
    {
        if (await IsFirstPartyBearerAsync(context))
        {
            await Results.Unauthorized().ExecuteAsync(context);
            return;
        }

        var principal = await AuthenticateInteractivePrincipalAsync(context);
        if (principal is null || !principal.Identities.Any(identity => identity.IsAuthenticated))
        {
            await Results.Unauthorized().ExecuteAsync(context);
            return;
        }

        // Never exchange an already-authenticated bearer (or any other ambient
        // principal) for a fresh token. The exchange is exclusively backed by
        // one of the explicitly configured interactive schemes above.
        context.User = principal;

        var normalizedValidator = context.RequestServices.GetRequiredService<NormalizedPrincipalValidator>();
        if (!normalizedValidator.TryGetNormalizedPrincipal(context.User, out var normalizedPrincipal))
        {
            await Results.Unauthorized().ExecuteAsync(context);
            return;
        }

        var subject = normalizedPrincipal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? normalizedPrincipal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
        {
            await Results.Unauthorized().ExecuteAsync(context);
            return;
        }

        var tenantId = normalizedPrincipal.FindFirstValue(IdentityClaimTypes.TenantId) ?? "default";
        var permissions = normalizedPrincipal.FindAll(IdentityClaimTypes.Permission)
            .Select(x => x.Value)
            .ToArray();
        var result = await context.RequestServices.GetRequiredService<ITokenService>()
            .IssueAsync(new TokenIssueRequest(subject, tenantId, permissions), context.RequestAborted);

        await Results.Json(new AccessTokenResponse(result.AccessToken, result.ExpiresAt), FoundationIdentityApiJsonContext.Default.AccessTokenResponse).ExecuteAsync(context);
    }

    private static async Task<bool> IsFirstPartyBearerAsync(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var token = authorization[prefix.Length..].Trim();
        if (token.Length == 0)
            return false;

        var validation = await context.RequestServices.GetRequiredService<ITokenService>()
            .ValidateAsync(new TokenValidationRequest(token), context.RequestAborted);
        return validation.Succeeded;
    }

    private static async Task HandleChallengeAsync(HttpContext context)
    {
        var provider = RouteValue(context, "provider");
        var descriptor = await context.RequestServices.GetRequiredService<IAuthenticationProviderResolver>()
            .FindAsync(provider, allowGlobalFallback: true, cancellationToken: context.RequestAborted);
        if (descriptor?.Challenge is null)
        {
            await Results.NotFound().ExecuteAsync(context);
            return;
        }

        await context.ChallengeAsync(
            descriptor.Challenge.Scheme ?? descriptor.Id,
            new AuthenticationProperties
            {
                RedirectUri = context.Request.Query["returnUrl"].FirstOrDefault() ?? "/"
            });
    }

    private static async Task HandleLogoutAsync(HttpContext context)
    {
        var provider = RouteValue(context, "provider");
        var services = context.RequestServices;
        var resolver = services.GetRequiredService<IAuthenticationProviderResolver>();
        var descriptor = await resolver.FindAsync(provider, allowGlobalFallback: true, cancellationToken: context.RequestAborted);
        var schemeName = descriptor?.Challenge?.Scheme ?? provider;

        var invalidator = descriptor is null
            ? null
            : services.GetServices<IAuthenticationSessionInvalidator>().SingleOrDefault(candidate =>
                string.Equals(candidate.ProviderId, descriptor.Id, StringComparison.OrdinalIgnoreCase));
        if (invalidator is not null)
            await invalidator.InvalidateAsync(new AuthenticationSessionInvalidationContext(context.User), context.RequestAborted);

        if (!string.IsNullOrEmpty(schemeName) &&
            await services.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(schemeName) is not null)
        {
            var handler = await services.GetRequiredService<IAuthenticationHandlerProvider>()
                .GetHandlerAsync(context, schemeName);
            if (handler is IAuthenticationSignOutHandler)
                await context.SignOutAsync(schemeName);
        }

        await Results.NoContent().ExecuteAsync(context);
    }

    private static async Task HandleRefreshAsync(HttpContext context)
    {
        var (request, malformed) = await ReadJsonAsync(context, FoundationIdentityApiJsonContext.Default.RefreshTokenRequest);
        if (malformed)
            return;

        if (request is null || string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await Results.Unauthorized().ExecuteAsync(context);
            return;
        }

        TokenRefreshResult result;
        try
        {
            result = await context.RequestServices.GetRequiredService<ITokenService>()
                .RefreshAsync(new TokenRefreshRequest(request.RefreshToken), context.RequestAborted);
        }
        catch (InvalidOperationException)
        {
            await Results.Unauthorized().ExecuteAsync(context);
            return;
        }

        await Results.Json(result, FoundationIdentityApiJsonContext.Default.TokenRefreshResult).ExecuteAsync(context);
    }

    private static async Task<(T? Value, bool Malformed)> ReadJsonAsync<T>(HttpContext context, JsonTypeInfo<T> typeInfo) where T : class
    {
        try
        {
            return (await context.Request.ReadFromJsonAsync(typeInfo, context.RequestAborted), false);
        }
        catch (JsonException)
        {
            await Results.BadRequest().ExecuteAsync(context);
            return (null, true);
        }
        catch (InvalidDataException)
        {
            await Results.BadRequest().ExecuteAsync(context);
            return (null, true);
        }
        catch (NotSupportedException)
        {
            await Results.BadRequest().ExecuteAsync(context);
            return (null, true);
        }
        catch (InvalidOperationException)
        {
            await Results.BadRequest().ExecuteAsync(context);
            return (null, true);
        }
    }

    private static async Task<ClaimsPrincipal?> AuthenticateInteractivePrincipalAsync(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<FoundationIdentityApiOptions>>().Value;
        var schemeProvider = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        foreach (var scheme in options.InteractiveAuthSchemes)
        {
            if (await schemeProvider.GetSchemeAsync(scheme) is null)
                continue;

            var result = await context.AuthenticateAsync(scheme);
            if (!result.Succeeded || result.Principal is null)
                continue;

            return result.Principal;
        }

        return null;
    }

    private static string RouteValue(HttpContext context, string name) =>
        context.Request.RouteValues.TryGetValue(name, out var value) ? Convert.ToString(value) ?? string.Empty : string.Empty;

    private static string Route(string path) => "/" + IdentityRouteConstants.GetRoute(path);

}
