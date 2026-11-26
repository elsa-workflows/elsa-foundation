using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Services;

public sealed class ActivityInputOptionsProviderResolver : IActivityInputOptionsProviderResolver
{
    private readonly IReadOnlyDictionary<string, IActivityInputOptionsProvider> _providers;

    public ActivityInputOptionsProviderResolver(IEnumerable<IActivityInputOptionsProvider> providers)
    {
        var byKey = new Dictionary<string, IActivityInputOptionsProvider>(StringComparer.Ordinal);

        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Key))
                throw new InvalidOperationException($"Activity input options provider '{provider.GetType().FullName}' has a blank key.");

            if (byKey.TryGetValue(provider.Key, out var existing))
                throw new InvalidOperationException(
                    $"Activity input options provider '{provider.GetType().FullName}' contributes duplicate key '{provider.Key}' " +
                    $"(already contributed by '{existing.GetType().FullName}').");

            byKey.Add(provider.Key, provider);
        }

        _providers = byKey;
    }

    public IActivityInputOptionsProvider? Find(string key) => _providers.GetValueOrDefault(key);
}
