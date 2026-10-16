using System.Security.Claims;
using System.Text.Encodings.Web;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Api.ApiKeys;

/// <summary>
/// Authenticates <c>Authorization: ApiKey {key}</c> as the application the key belongs to, with the permissions of the
/// application's roles, as a trusted normalized principal.
/// </summary>
/// <remarks>
/// A key authenticates only while its credential is an active, unexpired API key of that application, the key hashes to
/// the credential's hash, and the application exists and is active. Every refusal is the same failure, so neither the
/// response nor the log says which check failed or whether the application exists. Store failures are not refusals:
/// they propagate, so an outage is never reported as a bad key.
/// </remarks>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ICredentialStore credentials,
    IApplicationStore applications,
    IRoleStore roles)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    private const string Refused = "The API key is not valid.";
    private const string AuthorizationPrefix = ApiKeyAuthenticationDefaults.AuthorizationScheme + " ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(AuthorizationPrefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var principal = await AuthenticateAsync(authorization[AuthorizationPrefix.Length..].Trim(), Context.RequestAborted);
        return principal is null
            ? AuthenticateResult.Fail(Refused)
            : AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    private async Task<ClaimsPrincipal?> AuthenticateAsync(string key, CancellationToken cancellationToken)
    {
        if (!ApplicationApiKey.TryParse(key, out var applicationId))
            return null;

        var tenantId = Options.TenantId;
        var credential = await credentials.FindAsync(tenantId, ApplicationApiKey.CredentialId(applicationId), cancellationToken);
        if (credential is null || !ApplicationApiKey.Matches(key, credential) || !IsActiveKeyOf(credential, applicationId))
            return null;

        var application = await applications.FindAsync(tenantId, applicationId, cancellationToken);
        if (application is not { Status: ApplicationStatus.Active })
            return null;

        return new ClaimsPrincipal(await CreateIdentityAsync(application, cancellationToken));
    }

    private bool IsActiveKeyOf(CredentialRecord credential, string applicationId) =>
        credential is { Kind: CredentialKind.ApiKey, SubjectType: CredentialSubjectType.Application, Status: CredentialStatus.Active } &&
        string.Equals(credential.SubjectId, applicationId, StringComparison.Ordinal) &&
        (credential.ExpiresAt is null || credential.ExpiresAt > TimeProvider.GetUtcNow());

    /// <summary>Projects the application, its roles and their permissions, with the trusted marker last.</summary>
    private async Task<ClaimsIdentity> CreateIdentityAsync(ApplicationRecord application, CancellationToken cancellationToken)
    {
        var identity = new ClaimsIdentity(Scheme.Name, ClaimTypes.Name, IdentityClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, application.Id));
        identity.AddClaim(new Claim("sub", application.Id));
        identity.AddClaim(new Claim(ClaimTypes.Name, application.DisplayName));
        identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, application.TenantId));
        identity.AddClaim(new Claim(IdentityClaimTypes.Provider, ApiKeyAuthenticationDefaults.ProviderId));

        var permissions = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var roleId in application.RoleIds.Order(StringComparer.Ordinal))
        {
            identity.AddClaim(new Claim(IdentityClaimTypes.Role, roleId));
            if (await roles.FindAsync(application.TenantId, roleId, cancellationToken) is { } role)
                permissions.UnionWith(role.Permissions);
        }

        foreach (var permission in permissions)
            identity.AddClaim(new Claim(IdentityClaimTypes.Permission, permission));

        identity.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));
        return identity;
    }
}
