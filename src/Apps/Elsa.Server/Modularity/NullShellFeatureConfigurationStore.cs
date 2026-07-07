using System.Text.Json;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;

namespace Elsa.Server;

internal sealed class NullShellFeatureConfigurationStore : IShellFeatureConfigurationStore
{
    private static readonly ShellFeatureConfigurationSnapshot Empty = new("host", "", new Dictionary<string, JsonElement>());

    public Task<ShellFeatureConfigurationSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Empty);

    public Task<ShellFeatureConfigurationSnapshot> SaveAsync(
        string expectedRevision,
        IReadOnlyList<FeatureConfigurationChange> features,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Shell feature configuration is not available at the host level.");
}
