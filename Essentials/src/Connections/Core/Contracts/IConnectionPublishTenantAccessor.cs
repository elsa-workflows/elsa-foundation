namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// The tenant used by the publish-time connection gate. Hosts replace this; the API sets it from the
/// request principal. Owner/run-as stays reserved (D5).
/// </summary>
public interface IConnectionPublishTenantAccessor
{
    string? TenantId { get; }
}
