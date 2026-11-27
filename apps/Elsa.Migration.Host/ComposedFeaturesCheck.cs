using CShells;
using CShells.Features;
using CShells.Lifecycle;

namespace Elsa.Migration.Host;

/// <summary>
/// Refuses a shell's activation when its settings name a feature no assembly the host references provides. CShells itself
/// logs such a feature and activates with the rest, but a migrated workflow would then run without the activities it needs.
/// Registered on the host container, so CShells runs it in every shell as it activates, before anything is prepared.
/// </summary>
/// <remarks>Its services are resolved when it runs: the host container, which validates its registrations in Development,
/// has no <see cref="ShellSettings"/>.</remarks>
internal sealed class ComposedFeaturesCheck(IServiceProvider services) : IShellInitializer
{
    /// <summary>Ahead of the shell's own startup validation (-1000), so the missing feature is the reason the log gives.</summary>
    internal const int PrepareOrder = -2000;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var shell = services.GetRequiredService<ShellSettings>();
        // The composition as configured: CShells leaves a feature it can't find out of the shell's settings.
        var configured = services.GetRequiredService<RootConfiguration>().Configuration
            .GetSection($"CShells:Shells:{shell.Id}:Features").GetChildren().Select(feature => feature.Key);
        var snapshot = await services.GetRequiredService<IRuntimeFeatureCatalog>().GetSnapshotAsync(cancellationToken);
        var available = snapshot.FeatureDescriptors.Select(feature => feature.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = configured.Where(feature => !available.Contains(feature)).Order(StringComparer.Ordinal).ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Shell '{shell.Id}' lists feature(s) no project the Elsa 3 migration host references provides: {string.Join(", ", missing)}. "
                + "Add a project reference to the feature's project in Elsa.Migration.Host.csproj, or remove it from the composition.");
    }
}

/// <summary>The host's own configuration, shared into the shells as one instance, so a shell reads the composition as configured.</summary>
internal sealed record RootConfiguration(IConfiguration Configuration);
