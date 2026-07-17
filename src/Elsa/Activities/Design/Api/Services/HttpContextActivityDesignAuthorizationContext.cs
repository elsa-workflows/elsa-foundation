using System.Security.Cryptography;
using System.Text;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.FastEndpoints.Constants;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>
/// Default HTTP request adapter for reusable-activity authoring and dependency visibility.
/// Hosts can replace either interface registration when their authorization model is more specific.
/// </summary>
public sealed class HttpContextActivityDesignAuthorizationContext(IHttpContextAccessor httpContextAccessor)
    : IActivityAuthoringContext, IActivityDependencyAuthorizationContext
{
    public const string AuthorPermission = "activities.design.author";
    public const string ProviderPayloadReadPermission = "activities.design.provider-payload.read";
    private const string ElsaTenantClaim = "elsa.identity.tenant_id";
    private const string ConventionalTenantClaim = "tenant_id";
    private const string PermissionClaim = "elsa.identity.permission";

    private HttpContext? HttpContext => httpContextAccessor.HttpContext;

    public string? TenantId =>
        HttpContext?.User.FindFirst(ElsaTenantClaim)?.Value
        ?? HttpContext?.User.FindFirst(ConventionalTenantClaim)?.Value;

    public string AuthorizationProfile
    {
        get
        {
            var permissions = PermissionValues();
            var material = $"tenant:{TenantId ?? "global"}|permissions:{string.Join(',', permissions)}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        }
    }

    public bool CanAuthorProvider(string providerKey) =>
        !string.IsNullOrWhiteSpace(providerKey) && HasPermission(AuthorPermission);

    public bool CanReadProviderPayload(string providerKey) =>
        !string.IsNullOrWhiteSpace(providerKey) && HasPermission(ProviderPayloadReadPermission);

    public bool CanManageActivityDefinitions => HasPermission(PermissionNames.ActivityDesignManage);

    public bool CanRead(ActivityDefinitionReference reference) =>
        reference.TenantId is null || StringComparer.Ordinal.Equals(reference.TenantId, TenantId);

    private bool HasPermission(string permission)
    {
        var values = PermissionValues();
        return values.Contains("*", StringComparer.Ordinal) || values.Contains(permission, StringComparer.Ordinal);
    }

    private string[] PermissionValues() =>
        HttpContext?.User.FindAll(PermissionClaim)
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray() ?? [];
}
