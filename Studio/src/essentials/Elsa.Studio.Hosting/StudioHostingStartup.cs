using CShells.Lifecycle;
using CShells.Features;

namespace Elsa.Studio.Hosting;

internal sealed class StudioHostingStartup(IShellRegistry registry, StudioHostingSettings settings, StudioBackendAuthorityAccessor authority, StudioStartupFeatureAssemblies declarations, ILogger<StudioHostingStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var provided = await registry.GetBlueprintAsync(settings.ShellName, cancellationToken)
                ?? throw new StudioHostingException($"The selected Studio shell '{settings.ShellName}' has no blueprint.");
            var shell = await provided.Blueprint.ComposeAsync(cancellationToken);
            if (!string.Equals(shell.Id.Name, settings.ShellName, StringComparison.OrdinalIgnoreCase)
                || !shell.EnabledFeatures.Contains("StudioApi", StringComparer.OrdinalIgnoreCase)
                || shell.DisabledFeatures.Contains("StudioApi", StringComparer.OrdinalIgnoreCase))
                throw new StudioHostingException($"The selected Studio shell '{settings.ShellName}' must explicitly enable StudioApi.");
            var features = FeatureDiscovery.DiscoverFeatures(declarations.Assemblies).ToArray();
            var selection = StudioFeatureSelection.Resolve(shell, features, logger);
            authority.Initialize(selection.Enabled, selection.Disabled);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not (StudioHostingException or OutOfMemoryException))
        {
            throw new StudioHostingException($"Could not validate the selected Studio shell '{settings.ShellName}'.", error);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
