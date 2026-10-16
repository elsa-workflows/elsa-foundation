using Elsa.Foundation.Identity.Api.Applications;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NativeEndpoints;

namespace Elsa.Foundation.Identity.Api.Endpoints.Applications;

internal sealed record ListApplications(int? Skip = null, int? Take = null);

internal sealed record ApplicationRoute(string ApplicationId);

internal sealed record CreateApplication(string? DisplayName, IReadOnlyList<string>? RoleIds);

internal sealed record UpdateApplication(string ApplicationId, string? DisplayName, IReadOnlyList<string>? RoleIds, bool? Enabled);

/// <summary>
/// An application as the API shows it. It carries the status of the API key, never the key or its hash: the key is in
/// the one <see cref="IssuedApplicationResponse"/> or <see cref="ApiKeyResponse"/> that issued it.
/// </summary>
internal sealed record ApplicationResponse(
    string Id,
    string ClientId,
    string DisplayName,
    IReadOnlyList<string> RoleIds,
    bool Enabled,
    string ApiKeyStatus)
{
    public static ApplicationResponse From(ApplicationState state) => new(
        state.Application.Id,
        state.Application.ClientId,
        state.Application.DisplayName,
        state.Application.RoleIds.Order(StringComparer.Ordinal).ToArray(),
        state.Application.Status == ApplicationStatus.Active,
        state.ApiKeyStatus switch
        {
            null => "none",
            CredentialStatus.Active => "active",
            CredentialStatus.Rotating => "rotating",
            _ => "revoked"
        });
}

internal sealed record ApplicationPageResponse(IReadOnlyList<ApplicationResponse> Items, long TotalCount)
{
    public static ApplicationPageResponse From(IamPage<ApplicationState> page) =>
        new(page.Items.Select(ApplicationResponse.From).ToArray(), page.TotalCount);
}

internal sealed record IssuedApplicationResponse(ApplicationResponse Application, string ApiKey);

internal sealed record ApiKeyResponse(string ApiKey);

internal static class ApplicationRequests
{
    /// <summary>The tenant of the caller's trusted normalized principal, which is the tenant whose applications it manages.</summary>
    public static string CallerTenant(HttpContext context) =>
        context.RequestServices.GetRequiredService<NormalizedPrincipalValidator>().TryGetNormalizedPrincipal(context.User, out var principal) &&
        principal.FindFirst(IdentityClaimTypes.TenantId)?.Value is { Length: > 0 } tenantId
            ? tenantId
            : throw new InvalidOperationException("The authorized principal carries no tenant.");

    /// <summary>A response that shows an API key must not be stored by a cache on the way.</summary>
    public static void DoNotStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";
}

internal sealed class ApplicationExceptionTranslator : IEndpointExceptionTranslator
{
    public EndpointProblem? Translate(Exception exception) => exception switch
    {
        ApplicationNotFoundException => EndpointProblem.General(StatusCodes.Status404NotFound, exception.Message),
        ApplicationConflictException => EndpointProblem.General(StatusCodes.Status409Conflict, exception.Message),
        ArgumentException => EndpointProblem.General(StatusCodes.Status400BadRequest, exception.Message),
        _ => null
    };
}
