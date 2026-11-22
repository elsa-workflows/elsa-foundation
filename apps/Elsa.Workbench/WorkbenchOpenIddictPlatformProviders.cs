using System.Reflection;
using CShells;
using CShells.Configuration;
using CShells.Features;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Elsa.Persistence.EntityFramework.Tooling;
using Microsoft.Extensions.Configuration;

namespace Elsa.Workbench;

/// <summary>
/// The persistence providers the platform gives the default shell's EF consumers, resolved as the platform resolves them, so the
/// OpenIddict token store can be compared with them. The resolution is the platform's own (<see cref="EfPersistencePreparation"/>,
/// which the live host and <c>dotnet elsa persistence</c> run), over the shell composed from the host's defaults and its
/// configuration, with the features the loaded assemblies declare, so it covers every way a provider is selected: the root
/// <c>Elsa:Persistence:DefaultResource</c>, the shell's own default resource, a feature's <c>Bindings</c> entry, and a feature's
/// own <c>Provider</c> setting.
/// </summary>
public static class WorkbenchOpenIddictPlatformProviders
{
    /// <summary>
    /// The distinct providers, as configured, of the enabled EF consumers of <paramref name="shell"/>. A consumer selected by a
    /// resource has the resource's; any other has the <c>Provider</c> of its own settings, if it declares one.
    /// </summary>
    public static IReadOnlyCollection<string> Resolve(
        IConfiguration configuration,
        IEfToolingShellDefaults hostDefaults,
        IEnumerable<Assembly> assemblies,
        string shell = "default")
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(hostDefaults);
        ArgumentNullException.ThrowIfNull(assemblies);

        var loaded = assemblies.Where(assembly => !assembly.IsDynamic).Distinct().ToArray();
        var descriptors = FeatureDiscovery.DiscoverFeatures(loaded).ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);
        var builder = new ShellBuilder(shell);
        hostDefaults.Configure(builder, configuration);
        builder.FromConfiguration(configuration.GetSection($"CShells:Shells:{shell}"));
        var settings = builder.Build();
        var prepared = EfPersistencePreparation.Prepare(settings, descriptors, configuration, loaded, verifyConnectionValues: false);

        string? Configured(IEnumerable<KeyValuePair<string, object>> data, string feature) =>
            data.FirstOrDefault(entry => StringComparer.OrdinalIgnoreCase.Equals(entry.Key, $"{feature}:Provider")).Value?.ToString();

        var patch = prepared.Patch.ConfigurationData.ToDictionary(entry => entry.Key, entry => (object)(entry.Value ?? ""), StringComparer.OrdinalIgnoreCase);
        return prepared.Participants
            .Select(participant => participant.SelectionKind == "Legacy"
                ? Configured(settings.ConfigurationData, participant.FeatureId)
                : Configured(patch, participant.FeatureId))
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Select(provider => provider!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
