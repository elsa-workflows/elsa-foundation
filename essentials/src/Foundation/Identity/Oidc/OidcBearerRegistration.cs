using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Oidc;

/// <summary>The registration-time trust boundary and captured ordinary callbacks. No token or connection values are retained.</summary>
public sealed record OidcBearerRegistration(
    bool NormalizeBearerClaims,
    string JwtBearerScheme,
    bool Enabled,
    string? Authority,
    string? Audience,
    string ProviderId,
    string? TenantId,
    bool RequireHttpsMetadata,
    IServiceCollection Services)
{
    public ConditionalWeakTable<JwtBearerOptions, OidcBearerCallbacks> CapturedCallbacks { get; } = new();
}

/// <summary>Ordinary host callbacks captured before installing the guarded events type.</summary>
public sealed record OidcBearerCallbacks(
    Func<MessageReceivedContext, Task> MessageReceived,
    Func<TokenValidatedContext, Task> TokenValidated,
    Func<AuthenticationFailedContext, Task> AuthenticationFailed);
