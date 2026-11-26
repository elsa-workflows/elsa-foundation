using System.Collections.Concurrent;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;

namespace Elsa.Activities.Design.Core.Stores;

public sealed class InMemoryActivityAvailabilitySettingsStore : IActivityAvailabilitySettingsStore
{
    private readonly ConcurrentDictionary<string, ActivityAvailabilitySettings> _settings = new(StringComparer.Ordinal);

    public Task<ActivityAvailabilitySettings?> LoadAsync(string scope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        return Task.FromResult(_settings.TryGetValue(scope, out var settings) ? Clone(settings) : null);
    }

    public Task SaveAsync(ActivityAvailabilitySettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Scope);

        _settings[settings.Scope] = Clone(settings);
        return Task.CompletedTask;
    }

    private static ActivityAvailabilitySettings Clone(ActivityAvailabilitySettings settings) =>
        new()
        {
            Scope = settings.Scope,
            Mode = settings.Mode,
            Rules = Clone(settings.Rules)
        };

    private static ActivityAvailabilityRuleSet Clone(ActivityAvailabilityRuleSet? rules) =>
        new()
        {
            ActivityTypes = rules?.ActivityTypes.ToArray() ?? [],
            Sets = rules?.Sets.ToArray() ?? []
        };
}
