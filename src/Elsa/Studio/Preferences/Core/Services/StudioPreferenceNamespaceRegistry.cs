using Elsa.Studio.Preferences.Core.Contracts;

namespace Elsa.Studio.Preferences.Core.Services;

public sealed class StudioPreferenceNamespaceRegistry : IStudioPreferenceNamespaceRegistry
{
    private readonly IReadOnlyDictionary<string, IStudioPreferenceNamespace> _namespaces;

    public StudioPreferenceNamespaceRegistry(IEnumerable<IStudioPreferenceNamespace> namespaces)
    {
        var values = namespaces.ToArray();
        var invalid = values.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Name));
        if (invalid is not null)
            throw new InvalidOperationException("Studio preference namespaces require a non-empty name.");

        var duplicate = values.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Studio preference namespace '{duplicate.Key}' is registered more than once.");

        _namespaces = values.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IStudioPreferenceNamespace? Find(string name) => _namespaces.GetValueOrDefault(name);
    public IReadOnlyCollection<IStudioPreferenceNamespace> List() => _namespaces.Values.ToArray();
}
