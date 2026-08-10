using CShells.Features;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Declares the physical Groundwork stores a host owns, by name. Each persistence lane then binds to one of
/// these names, which is what lets design data and runtime state live in different databases while a host
/// that names a single target behaves exactly as it always has.
/// <para>
/// This feature is provider-neutral: it names a provider per target and carries that provider's settings as
/// an opaque option bag. The provider package's own feature contributes the leaf that reads them, so adding
/// a provider never changes this feature.
/// </para>
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "GroundworkTargets",
    DisplayName = "Groundwork Targets",
    Description = "Declares the named physical Groundwork stores a host owns. Each target names a composed provider leaf and the connection it addresses; persistence lanes bind to a target by name, so a host can keep design and runtime data in separate databases or share one.")]
public class GroundworkTargetsFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Targets",
        Description = "The named physical stores this host owns, keyed by target name. 'default' is the target a lane binds to when it names none. Each entry sets Provider, ConnectionString, and any provider-specific Options.",
        Category = "Persistence",
        Secret = true)]
    public IDictionary<string, GroundworkTargetEntry>? Targets { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        if (Targets is null || Targets.Count == 0)
            return;

        foreach (var (targetName, entry) in Targets.OrderBy(target => target.Key, StringComparer.Ordinal))
        {
            if (entry is null)
            {
                throw new GroundworkTargetConfigurationException(
                    $"Groundwork target '{targetName}' has no configuration.");
            }

            services.AddGroundworkTargetConfiguration(targetName, entry);
        }
    }
}
