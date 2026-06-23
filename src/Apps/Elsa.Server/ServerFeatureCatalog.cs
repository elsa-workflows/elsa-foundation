using System.Text.Json;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;

namespace Elsa.Server;

internal interface IServerFeatureCatalog
{
    Task<IReadOnlyList<FeatureCatalogItem>> ListFeaturesAsync(CancellationToken cancellationToken = default);
}

internal sealed class ServerFeatureCatalog(IEnumerable<IFeatureCatalogContributor> contributors) : IServerFeatureCatalog
{
    public async Task<IReadOnlyList<FeatureCatalogItem>> ListFeaturesAsync(CancellationToken cancellationToken = default)
    {
        var shell = new ShellFeatureConfigurationSnapshot("server", "runtime", new Dictionary<string, JsonElement>());
        var context = new FeatureCatalogContributionContext(shell);
        foreach (var contributor in contributors)
            await contributor.ContributeAsync(context, cancellationToken);

        return context.Items.Values
            .Select(x => x.ToItem())
            .OrderByDescending(x => x.Enabled)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
