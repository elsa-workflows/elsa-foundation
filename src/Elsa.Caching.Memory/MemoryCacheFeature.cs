using CShells.Features;
using Elsa.Caching.Core;
using Elsa.Caching.Memory.Options;
using Elsa.Caching.Memory.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Caching.Memory
{
    /// <summary>
    /// Configures the MemoryCache.
    /// </summary>
    [ShellFeature(
        name:  "MemoryCache",
        DisplayName = "Memory Cache",
        Description = "Provides in-memory caching capabilities."
    )]
    public class MemoryCacheFeature : IShellFeature
    {
        public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(1);

        public void ConfigureServices(IServiceCollection services)
        {
            services
                .AddMemoryCache()
                .Configure<CachingOptions>(o => o.CacheDuration = CacheDuration)
                .AddSingleton<ICacheManager, CacheManager>()
                .AddSingleton<IChangeTokenSignalInvoker, ChangeTokenSignalInvoker>()
                .AddSingleton<IChangeTokenSignaler, ChangeTokenSignaler>();
        }
    }
}
