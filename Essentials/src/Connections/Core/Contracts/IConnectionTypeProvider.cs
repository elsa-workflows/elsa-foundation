using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// A contributed kind of external-system connection. Enumerable: adding a provider never removes
/// another. Every implementation must declare supported schemes, non-secret settings, an allowed-hosts
/// policy, and a <see cref="TestAsync"/> implementation.
/// </summary>
public interface IConnectionTypeProvider
{
    ConnectionTypeDescriptor Descriptor { get; }

    /// <summary>
    /// Derives the host policy for a connection of this type from its stored settings.
    /// The default returns <see cref="Descriptor"/>.<see cref="ConnectionTypeDescriptor.Hosts"/>.
    /// </summary>
    AllowedHostsPolicy ResolveHosts(IReadOnlyDictionary<string, string> settings) => Descriptor.Hosts;

    Task<ConnectionTestResult> TestAsync(IResolvedConnection connection, CancellationToken cancellationToken = default);
}
