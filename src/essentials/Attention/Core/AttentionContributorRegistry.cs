using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Elsa.Attention.Core;

public sealed partial class AttentionContributorRegistry : IAttentionContributorRegistry
{
    private readonly ConcurrentDictionary<string, AttentionContributorRegistration> _registrations =
        new(StringComparer.OrdinalIgnoreCase);

    public AttentionContributorRegistry(IEnumerable<IAttentionContributor> contributors)
    {
        foreach (var contributor in contributors)
            Register(contributor);
    }

    public IReadOnlyCollection<AttentionContributorRegistration> List() =>
        _registrations.Values.OrderBy(x => x.Descriptor.Id, StringComparer.Ordinal).ToArray();

    public IDisposable Register(IAttentionContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        ValidateDescriptor(contributor.Descriptor);

        var registration = new AttentionContributorRegistration(contributor, Remove);
        if (!_registrations.TryAdd(contributor.Descriptor.Id, registration))
        {
            registration.Dispose();
            throw new InvalidOperationException($"Attention contributor '{contributor.Descriptor.Id}' is already registered.");
        }

        return registration;
    }

    private void Remove(AttentionContributorRegistration registration) =>
        _registrations.TryRemove(new KeyValuePair<string, AttentionContributorRegistration>(registration.Descriptor.Id, registration));

    private static void ValidateDescriptor(AttentionContributorDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!ContributorIdPattern().IsMatch(descriptor.Id))
            throw new ArgumentException("Contributor ID must contain only lower-case letters, digits, dots, and hyphens.", nameof(descriptor));
        if (string.IsNullOrWhiteSpace(descriptor.DisplayName))
            throw new ArgumentException("Contributor display name is required.", nameof(descriptor));

        var thresholds = descriptor.Thresholds ?? [];
        if (thresholds.Any(x => string.IsNullOrWhiteSpace(x.Key)
                                || string.IsNullOrWhiteSpace(x.DisplayName)
                                || string.IsNullOrWhiteSpace(x.Description)
                                || string.IsNullOrWhiteSpace(x.DefaultValue)))
            throw new ArgumentException("Attention threshold descriptors require a key, display name, description, and default value.", nameof(descriptor));
        if (thresholds.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != thresholds.Count)
            throw new ArgumentException("Attention threshold keys must be unique within a contributor.", nameof(descriptor));
    }

    [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+)*$")]
    private static partial Regex ContributorIdPattern();
}

public sealed class AttentionContributorRegistration : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Action<AttentionContributorRegistration> _remove;
    private int _disposed;

    internal AttentionContributorRegistration(
        IAttentionContributor contributor,
        Action<AttentionContributorRegistration> remove)
    {
        Contributor = contributor;
        _remove = remove;
    }

    public IAttentionContributor Contributor { get; }

    public AttentionContributorDescriptor Descriptor => Contributor.Descriptor;

    public CancellationToken Lifetime => _lifetime.Token;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _remove(this);
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
