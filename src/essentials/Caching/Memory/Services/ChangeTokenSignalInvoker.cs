using Elsa.Caching.Core;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;

namespace Elsa.Caching.Memory.Services;

/// <inheritdoc />
/// <remarks>
/// Concurrency notes (issue #396):
/// <list type="bullet">
/// <item>Dictionary values are <see cref="Lazy{T}"/>-wrapped so the <see cref="CancellationTokenSource"/>
/// is constructed exactly once per published entry — a losing <c>GetOrAdd</c> racer never creates (and
/// therefore never leaks) a token source.</item>
/// <item>An invalidation that arrives before any token is registered for the key is remembered in
/// <see cref="_pendingTriggers"/> and consumed by the next <see cref="GetToken"/>, which hands out an
/// already-signalled token. This closes the TOCTOU where a reader populates a cache entry from stale
/// data and registers its token just after the invalidation fired — previously that invalidation was
/// silently lost and the stale entry lived forever.</item>
/// <item>Growth is bounded by live keys: <see cref="_changeTokens"/> holds one entry per key with an
/// active (uninvalidated) token, and <see cref="_pendingTriggers"/> one marker per key invalidated
/// before/without a listener, consumed on the next <see cref="GetToken"/>.</item>
/// </list>
/// </remarks>
public sealed class ChangeTokenSignalInvoker : IChangeTokenSignalInvoker
{
    private readonly ConcurrentDictionary<string, Lazy<ChangeTokenInfo>> _changeTokens = new();
    private readonly ConcurrentDictionary<string, byte> _pendingTriggers = new();

    /// <inheritdoc />
    public IChangeToken GetToken(string key)
    {
        var entry = _changeTokens.GetOrAdd(key, _ => new Lazy<ChangeTokenInfo>(static () =>
        {
            var cancellationTokenSource = new CancellationTokenSource();
            var changeToken = new CancellationChangeToken(cancellationTokenSource.Token);
            return new ChangeTokenInfo(changeToken, cancellationTokenSource);
        }));

        if (_pendingTriggers.TryRemove(key, out _))
        {
            // An invalidation raced ahead of this registration: signal the entry we just created (or
            // found) so the caller's freshly-populated cache entry is evicted immediately. If another
            // thread already removed this exact entry, it also cancelled it — either way the returned
            // token reflects the invalidation.
            if (_changeTokens.TryRemove(new KeyValuePair<string, Lazy<ChangeTokenInfo>>(key, entry)))
                CancelAndDispose(entry);

            return entry.Value.ChangeToken;
        }

        return entry.Value.ChangeToken;
    }

    /// <inheritdoc />
    public ValueTask TriggerTokenAsync(string key, CancellationToken cancellationToken = default)
    {
        // Mark first, so a GetToken racing this call observes the invalidation even when the entry is
        // not registered yet (the TryRemove below would be a silent no-op in that interleaving).
        _pendingTriggers[key] = 1;

        if (_changeTokens.TryRemove(key, out var entry))
        {
            // Delivered to a registered token — the pending marker is no longer needed.
            _pendingTriggers.TryRemove(key, out _);
            CancelAndDispose(entry);
        }

        return default;
    }

    private static void CancelAndDispose(Lazy<ChangeTokenInfo> entry)
    {
        var info = entry.Value;
        info.TokenSource.Cancel();
        info.TokenSource.Dispose();
    }

    private readonly struct ChangeTokenInfo(IChangeToken changeToken, CancellationTokenSource tokenSource)
    {
        public IChangeToken ChangeToken { get; } = changeToken;
        public CancellationTokenSource TokenSource { get; } = tokenSource;
    }
}
