using Elsa.Studio.Core.Contracts;
using Elsa.Studio.Core.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Hosting;

internal sealed class StudioBackendAuthorityAccessor
{
    private readonly StudioBackendAuthority _configured;
    private readonly IStudioBackendAuthoritySource? _source;
    private readonly object _gate = new();
    private StudioBackendAuthority? _value;

    public StudioBackendAuthorityAccessor(IConfiguration configuration, IEnumerable<IStudioBackendAuthoritySource> sources)
    {
        var registrations = sources.Take(2).ToArray();
        if (registrations.Length > 1)
            throw new StudioHostingException("Only one Studio backend authority source may be registered.");
        _source = registrations.SingleOrDefault();
        var browser = configuration["Studio:BackendBaseUrl"];
        _configured = new(browser, string.IsNullOrWhiteSpace(configuration["Studio:BackendServerBaseUrl"])
            ? browser : configuration["Studio:BackendServerBaseUrl"]);
    }

    public StudioBackendAuthority Value
    {
        get
        {
            lock (_gate)
                return _value ??= _source is null ? Copy(_configured)
                    : throw new StudioHostingException("The Studio backend authority must be initialized from the selected shell before use.");
        }
    }

    public void Initialize(IReadOnlyCollection<string> enabled, IReadOnlyCollection<string> disabled)
    {
        lock (_gate)
            _value ??= Copy(_source?.Resolve(new(_configured, enabled, disabled)) ?? _configured);
    }

    private static StudioBackendAuthority Copy(StudioBackendAuthority value) => new(value.BrowserBaseUrl, value.ServerBaseUrl);

    internal static void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(provider => new StudioBackendAuthorityAccessor(configuration, provider.GetServices<IStudioBackendAuthoritySource>()));
        services.TryAddSingleton<IStudioBackendAuthorityAccessor, Accessor>();
    }

    private sealed class Accessor(StudioBackendAuthorityAccessor owner) : IStudioBackendAuthorityAccessor
    {
        public StudioBackendAuthority Value => owner.Value;
    }
}
