using Microsoft.Extensions.Configuration;

namespace Elsa.Agent.Core.Extensions;

/// <summary>
/// Fallback-preserving configuration readers shared by the provider option binders (issue #414 item 4).
/// Each reader returns the current value untouched when the corresponding key is absent or unparseable,
/// so binding only overrides an option when the configuration actually supplies a usable value.
/// </summary>
public static class AgentConfigurationBinding
{
    public static string? GetString(IConfiguration section, string key, string? fallback)
        => string.IsNullOrWhiteSpace(section[key]) ? fallback : section[key];

    public static bool GetBool(IConfiguration section, string key, bool fallback)
        => bool.TryParse(section[key], out var value) ? value : fallback;

    public static int GetInt(IConfiguration section, string key, int fallback)
        => int.TryParse(section[key], out var value) ? value : fallback;
}
