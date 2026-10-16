using Microsoft.AspNetCore.Authentication;

namespace Elsa.Foundation.Identity.Api.ApiKeys;

/// <summary>Options for the <see cref="ApiKeyAuthenticationDefaults.AuthenticationScheme"/> scheme.</summary>
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The tenant whose applications the scheme authenticates. Identity is host-wide in this version (ADR 0085 D6), so
    /// one tenant serves every request; it must be the tenant the persistence scope of the request is bound to, or the
    /// store refuses the lookup.
    /// </summary>
    public string TenantId { get; set; } = "default";
}
