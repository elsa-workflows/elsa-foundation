using System.Text.Json;

namespace Elsa.Modularity.Core.Models;

public sealed record ShellFeatureConfigurationSnapshot(
    string ShellName,
    string Revision,
    IReadOnlyDictionary<string, JsonElement> Features);
