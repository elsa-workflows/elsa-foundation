using Microsoft.Extensions.Primitives;

namespace Elsa.Caching.Memory.Services;

/// <summary>Provides change tokens for the memory cache, so code evicts cache entries by triggering a signal.</summary>
internal sealed class ChangeTokenSignaler(ChangeTokenSignalInvoker invoker)
{
    /// <summary>Gets a change token for the specified key.</summary>
    public IChangeToken GetToken(string key)
    {
        return invoker.GetToken(key);
    }

    /// <summary>Triggers the change token for the specified key.</summary>
    public ValueTask TriggerTokenAsync(string key, CancellationToken cancellationToken = default)
    {
        return invoker.TriggerTokenAsync(key, cancellationToken);
    }
}