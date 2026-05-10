using Elsa.Caching.Core;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;

namespace Elsa.Caching.Memory.Services
{
    /// <inheritdoc />
    public sealed class ChangeTokenSignalInvoker : IChangeTokenSignalInvoker
    {
        private readonly ConcurrentDictionary<string, ChangeTokenInfo> _changeTokens = new();

        /// <inheritdoc />
        public IChangeToken GetToken(string key)
        {
            return _changeTokens.GetOrAdd(
                key,
                _ =>
                {
                    var cancellationTokenSource = new CancellationTokenSource();
                    var changeToken = new CancellationChangeToken(cancellationTokenSource.Token);
                    return new ChangeTokenInfo(changeToken, cancellationTokenSource);
                }).ChangeToken;
        }

        /// <inheritdoc />
        public ValueTask TriggerTokenAsync(string key, CancellationToken cancellationToken = default)
        {
            if (_changeTokens.TryRemove(key, out var changeTokenInfo))
                changeTokenInfo.TokenSource.Cancel();

            return default;
        }

        private readonly struct ChangeTokenInfo(IChangeToken changeToken, CancellationTokenSource tokenSource)
        {
            public IChangeToken ChangeToken { get; } = changeToken;
            public CancellationTokenSource TokenSource { get; } = tokenSource;
        }
    }
}
