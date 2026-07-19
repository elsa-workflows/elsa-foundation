using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Contracts;

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
