namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// Stub of D1 until <c>SendHttpRequest</c> takes an <c>[ActivityConnection]</c>.
/// The named HTTP client reads the tenant's bound http connection from here.
/// </summary>
public interface IActiveConnectionAccessor
{
    ConnectionRecord Current { get; }
}
