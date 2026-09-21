using System.Text.Json;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Tests;

/// <summary>Shared factory for an empty <see cref="FeatureCatalogContributionContext"/> used across the catalog contributor tests.</summary>
internal static class FeatureCatalogTestContext
{
    public static FeatureCatalogContributionContext Create() =>
        new(new ShellFeatureConfigurationSnapshot("default", "revision", new Dictionary<string, JsonElement>()));
}
