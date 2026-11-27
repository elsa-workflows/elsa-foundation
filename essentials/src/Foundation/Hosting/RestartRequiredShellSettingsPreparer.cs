using CShells.Lifecycle;
using Elsa.Modularity.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Foundation.Hosting;

/// <summary>Checks the outgoing generation before CShells configures any of its replacement's features.</summary>
internal sealed class RestartRequiredShellSettingsPreparer(IServiceProvider host,
    ILogger<RestartRequiredShellSettingsPreparer> logger) : IShellSettingsPreparer
{
    public Task<ShellSettingsPreparationResult> PrepareAsync(ShellSettingsPreparationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registry = host.GetRequiredService<IShellRegistry>();
        if (registry.GetActive(context.ShellId.Name) is { } active &&
            active.ServiceProvider.GetService<ShellRestartRequirement>() is { } requirement)
        {
            logger.LogWarning("{Feature} requires a host restart to replace the active shell {ShellName}.",
                requirement.FeatureName, context.ShellId.Name);
            throw new InvalidOperationException("The active shell requires a host restart before replacement.");
        }

        return Task.FromResult(ShellSettingsPreparationResult.Unchanged(context));
    }
}
