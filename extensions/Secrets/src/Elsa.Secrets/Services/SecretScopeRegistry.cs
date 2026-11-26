using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

/// <summary>Builds a stable, immutable catalog of contributed scope suggestions.</summary>
public sealed class SecretScopeRegistry : ISecretScopeRegistry
{
    private readonly IReadOnlyCollection<SecretScopeDescriptor> _scopes;

    public SecretScopeRegistry(IEnumerable<ISecretScopeProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var contributions = new List<(SecretScopeDescriptor Descriptor, string Contributor)>();

        foreach (var provider in providers)
        {
            if (provider is null)
                throw new InvalidOperationException("A null secret scope provider was registered.");

            var contributor = provider.GetType().FullName ?? provider.GetType().Name;
            var scopes = provider.GetScopes()
                ?? throw new InvalidOperationException($"Secret scope provider '{contributor}' returned no scope collection.");

            foreach (var descriptor in scopes)
            {
                if (descriptor is null)
                    throw new InvalidOperationException($"Secret scope provider '{contributor}' returned a null scope descriptor.");

                if (string.IsNullOrWhiteSpace(descriptor.Name))
                    throw new InvalidOperationException($"Secret scope provider '{contributor}' returned a scope descriptor with an empty name.");

                if (string.IsNullOrWhiteSpace(descriptor.DisplayName))
                    throw new InvalidOperationException($"Secret scope provider '{contributor}' returned scope '{descriptor.Name}' with an empty display name.");

                contributions.Add((descriptor, contributor));
            }
        }

        var duplicate = contributions
            .GroupBy(contribution => contribution.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Skip(1).Any())
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        if (duplicate is not null)
        {
            var names = duplicate.Select(contribution => contribution.Descriptor.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal);
            var contributors = duplicate.Select(contribution => contribution.Contributor)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal);

            throw new InvalidOperationException(
                $"Secret scope name collision for [{string.Join(", ", names)}] contributed by [{string.Join(", ", contributors)}]. Scope names must be unique ignoring case.");
        }

        _scopes = Array.AsReadOnly(contributions
            .Select(contribution => contribution.Descriptor)
            .OrderBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(descriptor => descriptor.Name, StringComparer.Ordinal)
            .ToArray());
    }

    public IReadOnlyCollection<SecretScopeDescriptor> List() => _scopes;
}
