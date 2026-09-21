using Elsa.Caching.Core;
using Microsoft.Extensions.Primitives;

namespace Elsa.Caching.Memory.Services;

/// <inheritdoc />
public sealed class ChangeTokenSignaler(IChangeTokenSignalInvoker invoker) : IChangeTokenSignaler
{
    /// <inheritdoc />
    public IChangeToken GetToken(string key)
    {
        return invoker.GetToken(key);
    }

    /// <inheritdoc />
    public ValueTask TriggerTokenAsync(string key, CancellationToken cancellationToken = default)
    {
        return invoker.TriggerTokenAsync(key, cancellationToken);
    }
}