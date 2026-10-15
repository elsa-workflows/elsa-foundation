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
    IReadOnlyDictionary<string, string> Settings { get; }
    AllowedHostsPolicy Hosts { get; }

    ValueTask<SensitiveValue> GetSensitiveAsync(string fieldName, CancellationToken cancellationToken = default);
}
