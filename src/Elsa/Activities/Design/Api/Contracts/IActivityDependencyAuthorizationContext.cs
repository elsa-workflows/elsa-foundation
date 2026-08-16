using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Contracts;

/// <summary>
/// Stable resource passed to Foundation Identity for provider-specific authoring decisions.
/// The provider key is opaque to the shared authorization service; feature-owned resource
/// handlers may use it to grant or veto access without changing the endpoint framework.
/// </summary>
public sealed record ActivityProviderAuthorizationResource(string ProviderKey, string? TenantId);

/// <summary>
/// Required scoped host adapter for dependency-query visibility. The stable authorization profile
/// must change whenever permissions that affect structural results change, so cursors cannot cross
/// authorization contexts.
/// </summary>
public interface IActivityDependencyAuthorizationContext
{
    string? TenantId { get; }

    string AuthorizationProfile { get; }

    bool CanRead(ActivityDefinitionReference reference);
}

/// <summary>Asynchronous replacement seam for request-scoped dependency authorization.</summary>
public interface IActivityDependencyAuthorizationContextAsync
{
    string? TenantId { get; }

    ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default);

    ValueTask<bool> CanReadAsync(ActivityDefinitionReference reference, CancellationToken cancellationToken = default);
}
