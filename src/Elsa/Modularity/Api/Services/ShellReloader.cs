using CShells.Lifecycle;
using Elsa.Modularity.Api.Options;
using Elsa.Modularity.Core.Contracts;
using Microsoft.Extensions.Options;

namespace Elsa.Modularity.Api.Services;

public sealed class ShellReloader(IShellRegistry shellRegistry, IOptions<FeatureManagementOptions> options) : IShellReloader
{
    public async Task<int> ReloadAsync(CancellationToken cancellationToken = default)
    {
        var result = await shellRegistry.ReloadAsync(options.Value.ShellName, cancellationToken);
        return result.Error is null ? 1 : 0;
    }
}
