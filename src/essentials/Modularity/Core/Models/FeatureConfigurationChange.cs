using System.Text.Json;

namespace Elsa.Modularity.Core.Models;

public sealed record FeatureConfigurationChange(
    string Id,
    bool Enabled,
    JsonElement Configuration);
