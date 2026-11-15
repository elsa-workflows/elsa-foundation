using System.Security.Claims;

namespace Elsa.Weaver.Core;

/// <summary>Optional scoped replacement contract for the genuine authenticated principal of the current operation.</summary>
/// <remarks>
/// Hosts supply at most one scoped registration. New owning tools and context sources require exactly one on each call
/// and fail closed otherwise, including when called directly. A principal grants no permission by itself and must never
/// be reconstructed from actor strings, serialized into Weaver data, or retained across requests. Approval does not call this source.
/// </remarks>
public interface IWeaverPrincipalSource
{
    /// <summary>Returns the current principal only for the exact actor and tenant, or null when it is unavailable.</summary>
    ClaimsPrincipal? GetCurrent(string actorId, string tenantId);
}
