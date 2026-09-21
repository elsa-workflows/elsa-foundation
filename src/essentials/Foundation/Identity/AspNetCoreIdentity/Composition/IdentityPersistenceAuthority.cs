using CShells.Lifecycle;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Composition;

/// <summary>
/// Identifies the single feature that owns ASP.NET Core Identity persistence in a composed host.
/// </summary>
public sealed record IdentityPersistenceAuthority(string FeatureName);

/// <summary>
/// Recognizes a temporary persistence authority that cannot yet publish an
/// <see cref="IdentityPersistenceAuthority"/> marker itself.
/// </summary>
public sealed record IdentityAuthorityCompatibility(
    string FeatureName,
    Func<ServiceDescriptor, bool> Matches);

internal sealed class IdentityPersistenceAuthorityGuard(IServiceCollection services) :
    IHostedService,
    IShellInitializer,
    IConfigureOptions<IdentityOptions>
{
    private readonly List<IdentityAuthorityCompatibility> _compatibilityClassifiers = [];

    public void AddCompatibilityClassifiers(
        IEnumerable<IdentityAuthorityCompatibility> classifiers)
    {
        foreach (var classifier in classifiers)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(classifier.FeatureName);
            ArgumentNullException.ThrowIfNull(classifier.Matches);

            if (_compatibilityClassifiers.All(candidate =>
                    !string.Equals(candidate.FeatureName, classifier.FeatureName, StringComparison.Ordinal)))
                _compatibilityClassifiers.Add(classifier);
        }
    }

    public void EnsureCanSelect(string featureName)
    {
        var authorities = GetSelectedAuthorities()
            .Append(featureName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        ThrowIfConflicting(authorities);
    }

    public void Validate() => ThrowIfConflicting(GetSelectedAuthorities());

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Validate();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task InitializeAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken);

    public void Configure(IdentityOptions options) => Validate();

    private IReadOnlyList<string> GetSelectedAuthorities()
    {
        var authorities = services
            .Where(descriptor => descriptor.ServiceType == typeof(IdentityPersistenceAuthority))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IdentityPersistenceAuthority>()
            .Select(selection => selection.FeatureName)
            .ToList();

        authorities.AddRange(_compatibilityClassifiers
            .Where(classifier => services.Any(classifier.Matches))
            .Select(classifier => classifier.FeatureName));

        return authorities
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static void ThrowIfConflicting(IReadOnlyList<string> authorities)
    {
        if (authorities.Count <= 1)
            return;

        throw new InvalidOperationException(
            "ASP.NET Core Identity persistence authority conflict: " +
            $"{string.Join(" and ", authorities.Select(authority => $"'{authority}'"))} are both selected. " +
            "Select exactly one persistence authority.");
    }
}
