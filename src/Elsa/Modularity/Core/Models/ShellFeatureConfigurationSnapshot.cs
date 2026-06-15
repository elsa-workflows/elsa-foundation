using System.Text.Json;

namespace Elsa.Modularity.Core.Models;

public sealed record ShellFeatureConfigurationSnapshot(
    string ShellId,
    string Revision,
    IReadOnlyDictionary<string, JsonElement> Features);
