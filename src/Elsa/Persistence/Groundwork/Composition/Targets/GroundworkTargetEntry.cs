namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>
/// One host-configured Groundwork target: which provider opens it, what it connects to, and any
/// provider-specific tuning.
/// <para>
/// This type is deliberately provider-neutral. <see cref="Provider"/> names a leaf that some provider
/// package contributes, and <see cref="Options"/> is an opaque bag that the matching leaf binds to its own
/// strongly typed options class. Adding a provider therefore never changes this type, and the registry
/// never learns what a SQLite store cache or a MongoDB database name is.
/// </para>
/// </summary>
public sealed class GroundworkTargetEntry
{
    /// <summary>
    /// The provider leaf identity, matched case-insensitively against the leaves the host composed
    /// (for example <c>sqlite</c>, <c>postgresql</c>, <c>sqlserver</c>, <c>mongodb</c>).
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>The connection this target addresses, in the provider's own connection-string form.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Whether startup may apply safe pending schema operations for this target. Drift and destructive
    /// operations are never auto-applied regardless of this setting.
    /// </summary>
    public bool AutoApplySchemaOnStartup { get; set; } = true;

    /// <summary>
    /// Provider-specific settings for this target, bound by the provider leaf to its own options type.
    /// Keys use the leaf's own property names; unknown keys are a configuration error the leaf reports.
    /// </summary>
    public IDictionary<string, string?> Options { get; set; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The connection string, or a configuration error naming the target that is missing one.</summary>
    public string RequireConnectionString(string targetName) =>
        string.IsNullOrWhiteSpace(ConnectionString)
            ? throw new GroundworkTargetConfigurationException(
                $"Groundwork target '{targetName}' has no connection string. Every target addresses exactly " +
                "one physical store and must say which.")
            : ConnectionString;

    /// <summary>Reads an optional string option.</summary>
    public string? GetString(string key) =>
        Options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>Reads a required string option, or a configuration error naming the target and key.</summary>
    public string RequireString(string targetName, string key) =>
        GetString(key) ?? throw new GroundworkTargetConfigurationException(
            $"Groundwork target '{targetName}' is missing the required option '{key}'.");

    /// <summary>Reads an optional boolean option, failing on a value that is not a boolean.</summary>
    public bool GetBoolean(string targetName, string key, bool fallback)
    {
        var raw = GetString(key);
        if (raw is null)
            return fallback;
        if (bool.TryParse(raw, out var value))
            return value;
        throw new GroundworkTargetConfigurationException(
            $"Groundwork target '{targetName}' option '{key}' must be true or false, but was '{raw}'.");
    }

    /// <summary>Reads an optional positive integer option, failing on a value that is not one.</summary>
    public int? GetPositiveInt32(string targetName, string key)
    {
        var raw = GetString(key);
        if (raw is null)
            return null;
        if (int.TryParse(raw, out var value) && value > 0)
            return value;
        throw new GroundworkTargetConfigurationException(
            $"Groundwork target '{targetName}' option '{key}' must be a positive whole number, but was '{raw}'.");
    }

    /// <summary>
    /// Fails on any option the provider leaf does not recognize. A mistyped key would otherwise be dropped
    /// in silence, which is the exact failure mode the target registry exists to end.
    /// </summary>
    public void EnsureOnlyKnownOptions(string targetName, params string[] knownKeys)
    {
        var known = knownKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = Options.Keys
            .Where(key => !known.Contains(key))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknown.Length == 0)
            return;

        throw new GroundworkTargetConfigurationException(
            $"Groundwork target '{targetName}' sets unknown option(s) " +
            $"{string.Join(", ", unknown.Select(key => $"'{key}'"))} for provider '{Provider}'. " +
            $"Supported options are {string.Join(", ", known.Order(StringComparer.OrdinalIgnoreCase).Select(key => $"'{key}'"))}.");
    }
}

/// <summary>Raised when a host's Groundwork target configuration cannot be turned into a store.</summary>
public sealed class GroundworkTargetConfigurationException(string message) : InvalidOperationException(message);
