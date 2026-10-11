using CShells.Lifecycle;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Oidc;

/// <summary>Checks actual hosted and shell-owned handlers, collaborators and ordinary scope before serving requests.</summary>
public sealed class OidcBearerActivationGuard(
    OidcBearerRegistration registration,
    IOptionsMonitor<OidcAuthenticationOptions> options,
    IOptionsMonitor<JwtBearerOptions> bearer,
    IAuthenticationSchemeProvider schemes,
    IServiceScopeFactory scopes) : IHostedService, IShellInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configured = options.CurrentValue;
        if (!configured.NormalizeBearerClaims)
            return;
        var target = bearer.Get(registration.JwtBearerScheme);
        var scheme = await schemes.GetSchemeAsync(registration.JwtBearerScheme);
        cancellationToken.ThrowIfCancellationRequested();
        if (scheme?.HandlerType != typeof(JwtBearerHandler))
            throw Invalid();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            if (services.GetRequiredService<IPersistenceAccessContextAccessor>().Current.RequireScope() != new PersistenceScope(configured.TenantId!))
                throw Invalid();
            var trusted = services.GetRequiredService<IOptions<FoundationIdentityOptions>>().Value.NormalizedAuthenticationTypes;
            var rawType = target.TokenValidationParameters.AuthenticationType ?? "AuthenticationTypes.Federation";
            if (!trusted.Contains(OidcBearerNormalizationEvents.NormalizedAuthenticationType, StringComparer.Ordinal) ||
                trusted.Contains(registration.JwtBearerScheme, StringComparer.Ordinal) || trusted.Contains(rawType, StringComparer.Ordinal))
                throw Invalid();
            _ = services.GetRequiredService<IClaimMappingStore>();
            _ = services.GetRequiredService<IClaimsNormalizer>();
            _ = services.GetRequiredService<OidcBearerNormalizationEvents>();
            if (services.GetRequiredService<JwtBearerHandler>().GetType() != typeof(JwtBearerHandler))
                throw Invalid();
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Invalid();
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public Task StartAsync(CancellationToken cancellationToken) => InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static InvalidOperationException Invalid() => new(OidcBearerOptionsValidator.ConfigurationInvalid);
}
