namespace Elsa.Foundation.Identity.OpenIddict;

/// <summary>
/// Describes the OpenIddict token store a persistence adapter configured, so the provider-neutral token service can
/// adapt to it without depending on a concrete persistence technology. When no adapter registers one, the store is
/// treated as durable.
/// </summary>
public interface IOpenIddictTokenStoreInfo
{
    /// <summary>
    /// Whether the store is held in memory and discarded when the host stops.
    /// </summary>
    bool IsInMemory { get; }
}
