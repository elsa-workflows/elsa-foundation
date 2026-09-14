using System.Text.Json;

namespace Elsa.Modularity.Core.Models;

/// <param name="ShellId">The shell the features belong to.</param>
/// <param name="Revision">
/// The concurrency token an apply must present. It has to change whenever any stored value changes, secret values
/// included, yet it is returned to every catalog reader alongside the masked configuration, so a store must not derive it
/// with anything a reader could recompute from that configuration and a guessed secret, such as an unkeyed hash.
/// </param>
/// <param name="Features">The enabled features and their stored configuration, secret values unmasked.</param>
public sealed record ShellFeatureConfigurationSnapshot(
    string ShellId,
    string Revision,
    IReadOnlyDictionary<string, JsonElement> Features);
