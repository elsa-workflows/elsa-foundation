using CShells;
using CShells.Resolution;

namespace Elsa.Studio.Hosting;

internal sealed class StudioSelectedShellFallbackResolver(StudioHostingSettings settings) : IShellResolverStrategy
{
    public Task<ShellId?> ResolveAsync(ShellResolutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ShellId?>(new ShellId(settings.ShellName));
    }
}
