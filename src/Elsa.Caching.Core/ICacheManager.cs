using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Caching.Core
{
    /// <summary>
    /// A thin wrapper around <see cref="IMemoryCache"/>, allowing for centralized handling of cache entries.
    /// </summary>
    public interface ICacheManager
    {
        /// <summary>
        /// Gets or sets the duration for which cache entries are stored.
        /// </summary>
        TimeSpan CacheDuration { get; }

        /// <summary>
        /// Gets a change token for the specified key.
        /// </summary>
        IChangeToken GetToken(string key);

        /// <summary>
        /// Triggers the change token for the specified key.
        /// </summary>
        ValueTask TriggerTokenAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds an item from the cache, or creates it if it doesn't exist.
        /// </summary>
        Task<TItem?> FindOrCreateAsync<TItem>(object key, Func<ICacheEntry, Task<TItem>> factory);

        /// <summary>
        /// Gets an item from the cache, or creates it if it doesn't exist.
        /// </summary>
        Task<TItem> GetOrCreateAsync<TItem>(object key, Func<ICacheEntry, Task<TItem>> factory);
    }
}
