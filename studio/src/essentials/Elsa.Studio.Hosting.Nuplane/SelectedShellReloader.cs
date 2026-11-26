using CShells.Lifecycle;
using Elsa.Modularity.Core.Contracts;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Hosting.Nuplane;

internal sealed class SelectedShellReloader(IShellRegistry registry, IOptions<StudioHostingOptions> options, ILogger<SelectedShellReloader> logger) : IShellReloader
{
    public async Task<int> ReloadAsync(CancellationToken cancellationToken = default)
    {
        var result = await registry.ReloadAsync(options.Value.ShellName, cancellationToken);
        if (result.Error is not null)
        {
            logger.LogWarning(result.Error, "Studio shell {ShellName} replacement failed after feature configuration was saved.", options.Value.ShellName);
            throw new StudioHostingException($"Studio shell '{options.Value.ShellName}' configuration was saved, but its replacement failed; the previous generation remains active.", result.Error);
        }
        return 1;
    }
}
