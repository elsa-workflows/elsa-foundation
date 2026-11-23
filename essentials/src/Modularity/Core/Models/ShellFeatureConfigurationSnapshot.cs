using System.Text.Json;

namespace Elsa.Modularity.Core.Models;

/// <param name="ShellId">The shell the features belong to.</param>
/// <param name="Revision">
/// The concurrency token an apply must present. It has to change whenever any stored value changes, secret values
/// included, yet it is returned to every catalog reader alongside the masked configuration, so a store must not derive it
/// with anything a reader could recompute from that configuration and a guessed secret, such as an unkeyed hash.
/// </param>
/// <param name="Features">The enabled features and their stored configuration, secret values unmasked.</param>
/// <param name="Configuration">
/// The shell's own <c>Configuration</c> node, as CShells layers it over the host's configuration for every
/// key a shell has not left unset — not the shell's feature settings, which <paramref name="Features"/>
/// already carries. Left as the default <see cref="JsonElement"/> (not a JSON object) by a store that has
/// no such node for this shell, or does not track one at all.
/// </param>
public sealed record ShellFeatureConfigurationSnapshot(
    string ShellId,
    string Revision,
    IReadOnlyDictionary<string, JsonElement> Features,
    JsonElement Configuration = default);
