using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Oidc;

/// <summary>Admits normalized principals only after real token validation and owned per-request mapping.</summary>
public sealed class OidcBearerNormalizationEvents(
    OidcBearerRegistration registration,
    IOptionsMonitor<OidcAuthenticationOptions> options,
    IClaimMappingStore mappings,
    IClaimsNormalizer normalizer,
    IPersistenceAccessContextAccessor access) : JwtBearerEvents
{
    public const string NormalizedAuthenticationType = "Elsa.Foundation.Identity.Oidc.Bearer.Normalized";

    private static readonly string[] InternalClaimTypes =
    [IdentityClaimTypes.Normalized, IdentityClaimTypes.TenantId, IdentityClaimTypes.Provider, IdentityClaimTypes.Role, IdentityClaimTypes.Permission];

    public override async Task MessageReceived(MessageReceivedContext context)
    {
        context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
        if (!TryCallbacks(context.Options, context.Scheme.Name, out var callbacks))
        {
            context.Fail("oidc-normalization-configuration-invalid");
            return;
        }
        try
        {
            await callbacks.MessageReceived(context);
        }
        catch (Exception)
        {
            context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
            context.Fail("oidc-normalization-events-failed");
            return;
        }
        context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
        GuardResult(context.Result, context.Fail);
    }

    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var cancellationToken = context.HttpContext.RequestAborted;
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCallbacks(context.Options, context.Scheme.Name, out var callbacks))
        {
            context.Fail("oidc-normalization-configuration-invalid");
            return;
        }
        try
        {
            await callbacks.TokenValidated(context);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Fail("oidc-normalization-events-failed");
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Result is not null)
        {
            GuardResult(context.Result, context.Fail);
            return;
        }
        if (context.Principal is null || context.SecurityToken is null)
        {
            context.Fail("oidc-normalization-result-invalid");
            return;
        }
        // CurrentValue invokes final option validation on reload; trust changes require fresh activation.
        OidcAuthenticationOptions configured;
        try
        {
            configured = options.CurrentValue;
            if (!configured.NormalizeBearerClaims || configured.ProviderId != registration.ProviderId || configured.TenantId != registration.TenantId)
                throw new InvalidOperationException();
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Fail("oidc-normalization-configuration-invalid");
            return;
        }
        try
        {
            if (access.Current?.RequireScope() != new PersistenceScope(configured.TenantId!))
                throw new InvalidOperationException();
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Fail("oidc-normalization-persistence-scope-invalid");
            return;
        }
        var filtered = new ClaimsPrincipal(context.Principal.Identities.Select(identity =>
            new ClaimsIdentity(identity.Claims.Where(claim => !InternalClaimTypes.Contains(claim.Type, StringComparer.OrdinalIgnoreCase))
                .Select(claim => claim.Clone()), identity.AuthenticationType, identity.NameClaimType, identity.RoleClaimType)));

        IReadOnlyList<ClaimMappingRule> rules;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            rules = await mappings.ListForProviderAsync(configured.TenantId!, configured.ProviderId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Fail("oidc-normalization-mapping-unavailable");
            return;
        }
        ClaimsNormalizationResult result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = await normalizer.NormalizeAsync(new ClaimsNormalizationContext(
                filtered, configured.TenantId!, configured.ProviderId, rules, NormalizedAuthenticationType), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Fail("oidc-normalization-failed");
            return;
        }
        try
        {
            if (!Admits(result, configured))
            {
                context.Fail("oidc-normalization-result-invalid");
                return;
            }
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Fail("oidc-normalization-result-invalid");
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        context.Principal = result.Principal;
        // The actual handler publishes its ticket after this final guarded principal assignment.
    }

    public override async Task AuthenticationFailed(AuthenticationFailedContext context)
    {
        var cancellationToken = context.HttpContext.RequestAborted;
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCallbacks(context.Options, context.Scheme.Name, out var callbacks))
        {
            context.Fail("oidc-normalization-configuration-invalid");
            return;
        }
        try
        {
            await callbacks.AuthenticationFailed(context);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Fail("oidc-normalization-events-failed");
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Result is not null)
            GuardResult(context.Result, context.Fail);
        else
            context.Fail("oidc-normalization-events-failed");
    }

    public override Task Challenge(JwtBearerChallengeContext context)
    {
        context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    public override Task Forbidden(ForbiddenContext context)
    {
        context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private bool TryCallbacks(JwtBearerOptions target, string scheme, out OidcBearerCallbacks callbacks)
    {
        callbacks = null!;
        return registration.NormalizeBearerClaims && string.Equals(scheme, registration.JwtBearerScheme, StringComparison.Ordinal) &&
               registration.CapturedCallbacks.TryGetValue(target, out callbacks!);
    }

    private static void GuardResult(AuthenticateResult? result, Action<string> fail)
    {
        if (result?.Succeeded == true)
            fail("oidc-normalization-short-circuit-refused");
        else if (result?.Failure is not null)
            fail("oidc-normalization-events-failed");
        // A prior NoResult remains unchanged.
    }

    private static bool Admits(ClaimsNormalizationResult? result, OidcAuthenticationOptions configured)
    {
        if (result?.Principal is null)
            return false;
        var identities = result.Principal.Identities.ToArray();
        if (identities.Length != 1 || !identities[0].IsAuthenticated ||
            !string.Equals(identities[0].AuthenticationType, NormalizedAuthenticationType, StringComparison.Ordinal))
            return false;
        return ExactClaim(identities[0], IdentityClaimTypes.Normalized, "v1") &&
               ExactClaim(identities[0], IdentityClaimTypes.TenantId, configured.TenantId!) &&
               ExactClaim(identities[0], IdentityClaimTypes.Provider, configured.ProviderId);
    }

    private static bool ExactClaim(ClaimsIdentity identity, string type, string value)
    {
        var claims = identity.Claims.Where(claim => string.Equals(claim.Type, type, StringComparison.OrdinalIgnoreCase)).ToArray();
        return claims.Length == 1 && string.Equals(claims[0].Type, type, StringComparison.Ordinal) &&
               string.Equals(claims[0].Value, value, StringComparison.Ordinal);
    }
}
