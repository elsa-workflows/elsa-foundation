using CShells.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// A feature class is public, so CShells can discover it and an inheriting feature can extend it, and it is not sealed,
/// so a feature in another package can derive from it and override <c>ConfigureServices</c>: feature inheritance is how
/// one feature builds on another.
/// </summary>
/// <remarks>
/// The rule is activated domain by domain, as each domain's internal-by-default change unseals its features, because
/// feature classes across the tree are still sealed today. A domain is named by its <see cref="ElsaDomains"/> key.
/// </remarks>
public sealed class FeatureClassShapeGuardTests
{
    /// <summary>The domains whose feature classes are held to the rule. A domain's change adds its key here.</summary>
    private static readonly HashSet<string> ActivatedDomains = new(StringComparer.Ordinal) { "Caching", "Events", "Locking", "Mediator", "Primitives", "Serialization", "Tasks" };

    private static IReadOnlyList<(Type Feature, string? Domain)> Features { get; } = LoadFeatures();

    [Fact]
    public void Every_feature_class_in_an_activated_domain_is_public_and_not_sealed()
    {
        var violations = ShapeViolations(Features.Where(feature => feature.Domain is not null && ActivatedDomains.Contains(feature.Domain)).Select(feature => feature.Feature));

        Assert.True(violations.Count == 0,
            "Feature classes must be public and not sealed:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>A key that matches no domain with a feature would activate nothing while reading as activated.</summary>
    [Fact]
    public void Every_activated_domain_has_feature_classes()
    {
        var empty = ActivatedDomains.Where(domain => !Features.Any(feature => feature.Domain == domain)).Order(StringComparer.Ordinal).ToArray();

        Assert.True(empty.Length == 0, $"These activated domains hold no feature class, so the rule checks nothing there: {string.Join(", ", empty)}.");
    }

    /// <summary>Pins that activating a domain has something to check: features are found, and nearly all map to a domain.</summary>
    [Fact]
    public void The_sweep_finds_feature_classes_and_maps_them_to_domains()
    {
        Assert.True(Features.Count >= 100, $"Only {Features.Count} feature classes were found; discovery has stopped finding them.");
        Assert.True(Features.Count(feature => feature.Domain is null) * 10 < Features.Count,
            "More than a tenth of the feature classes map to no domain; the domain mapping no longer matches the tree.");
        Assert.Contains(Features, feature => feature is { Feature.Name: "EventsFeature", Domain: "Events" });
    }

    /// <summary>The detector itself, on feature classes of each shape.</summary>
    [Fact]
    public void A_sealed_or_non_public_feature_class_is_flagged_and_an_open_public_one_is_not()
    {
        Assert.Equal(
            [
                $"{typeof(HiddenFeature).FullName}: not public",
                $"{typeof(SealedFeature).FullName}: sealed",
            ],
            ShapeViolations([typeof(OpenFeature), typeof(SealedFeature), typeof(HiddenFeature)]));
    }

    private static IReadOnlyList<string> ShapeViolations(IEnumerable<Type> features) =>
    [
        .. features.SelectMany(feature => new[]
            {
                feature.IsVisible ? null : $"{feature.FullName}: not public",
                feature.IsSealed ? $"{feature.FullName}: sealed" : null,
            })
            .OfType<string>()
            .Order(StringComparer.Ordinal)
    ];

    private static IReadOnlyList<(Type Feature, string? Domain)> LoadFeatures()
    {
        var domainsByAssembly = RuntimeLibraries.Packable
            .Where(library => library.Assembly is not null)
            .ToDictionary(library => library.Assembly!, library => ElsaDomains.Of(library.ProjectPath));

        return [.. RuntimeLibraries.FeatureTypes.Select(feature => (feature, domainsByAssembly.GetValueOrDefault(feature.Assembly)))];
    }

    public class OpenFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) { }
    }

    public sealed class SealedFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) { }
    }

    internal class HiddenFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) { }
    }
}
