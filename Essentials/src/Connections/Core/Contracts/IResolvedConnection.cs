using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// The short-lived runtime object an activity receives: typed, disposable, with redacting
/// <see cref="object.ToString"/>. Preferred access is through a type-specific authenticator;
/// <see cref="GetSensitiveAsync"/> is the greppable escape hatch.
/// </summary>
public interface IResolvedConnection : IAsyncDisposable
{
    string TenantId { get; }
    string Name { get; }
    string TypeName { get; }
    string AuthScheme { get; }
    string Owner { get; }
    string Status { get; }
    /// <summary>
    /// The metadata revision resolved with this settings snapshot. Zero means the implementation
    /// does not expose a revision; consumers requiring revision-bound access must refuse that profile.
    /// </summary>
    int Version => 0;
    /// <summary>Opaque creation identity. Null means unknown; version-bound consumers must refuse that profile.</summary>
    string? IncarnationId => null;
    IReadOnlyDictionary<string, string> Settings { get; }
    AllowedHostsPolicy Hosts { get; }

    ValueTask<SensitiveValue> GetSensitiveAsync(string fieldName, CancellationToken cancellationToken = default);
}
