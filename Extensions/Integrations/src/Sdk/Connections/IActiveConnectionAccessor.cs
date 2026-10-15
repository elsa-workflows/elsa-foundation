namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// Stub of D1 until <c>SendHttpRequest</c> takes an <c>[ActivityConnection]</c>.
/// The named HTTP client reads the tenant's bound http connection from here.
/// Removed in the same PR that lands D1; no shim is left (ADR 0045).
/// </summary>
internal interface IActiveConnectionAccessor
{
    ConnectionRecord Current { get; }
}
