namespace Elsa.Workbench.Boot;

/// <summary>
/// Opt-in eager shell-activation configuration (spec 132, First-Request/Cold-Start Readiness, program unit 4).
///
/// CShells activates each shell lazily inside the first matching request (see <c>ShellMiddleware</c>), so the
/// first user request pays the full activation wall — and every request that arrives mid-activation queues behind
/// it. This switch moves that wall off the request path: when enabled, a host-level hosted service activates the
/// configured shell(s) at boot through the same <c>IShellRegistry.GetOrActivateAsync</c> path the middleware uses,
/// producing byte-identical shell state, just earlier.
///
/// Default is OFF. A many-shell host pays every activation at boot when it opts in — that is the documented trade,
/// and the reason the whole feature is opt-in.
/// </summary>
public sealed record EagerShellActivationOptions
{
    /// <summary>Root configuration section for the switch, e.g. <c>Elsa:Boot:EagerShellActivation</c>.</summary>
    public const string ConfigurationSection = "Elsa:Boot:EagerShellActivation";

    /// <summary>Host configuration switch that turns eager activation on. Absent or false means fully inert.</summary>
    public const string EnabledConfigurationKey = ConfigurationSection + ":Enabled";

    /// <summary>Configuration key for the optional named-shells list.</summary>
    public const string ShellsConfigurationKey = ConfigurationSection + ":Shells";

    /// <summary>
    /// Sentinel entry in <see cref="Shells"/> meaning "every configured shell". An empty <see cref="Shells"/>
    /// list means the same thing, so the demo single-shell host activates its one shell by default.
    /// </summary>
    public const string AllShellsMarker = "*";

    /// <summary>CShells configuration section whose child keys are the configured shell names (the composition).</summary>
    public const string ConfiguredShellsSection = "CShells:Shells";

    /// <summary>Whether eager activation is enabled. Default false.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The shells to activate at boot. Empty (the default) or a list containing <see cref="AllShellsMarker"/>
    /// means "all configured shells" — which on the reference single-shell host is exactly the demo shell.
    /// Otherwise the named shells are activated in the order given.
    /// </summary>
    public IReadOnlyList<string> Shells { get; init; } = [];

    /// <summary>Reads the opt-in switch without binding the whole options object. Parses "true"/"1" loosely.</summary>
    public static bool IsEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration[EnabledConfigurationKey];
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (bool.TryParse(value, out var parsed))
            return parsed;
        return value.Trim() == "1";
    }

    /// <summary>Binds the options from configuration (enabled flag + optional named-shells list).</summary>
    public static EagerShellActivationOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var shells = configuration
            .GetSection(ShellsConfigurationKey)
            .GetChildren()
            .Select(child => child.Value?.Trim())
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(value => value!)
            .ToArray();

        return new EagerShellActivationOptions
        {
            Enabled = IsEnabled(configuration),
            Shells = shells
        };
    }

    /// <summary>
    /// The configured shell names from the CShells composition (the child keys under <c>CShells:Shells</c>).
    /// This is the same source CShells' own <c>ConfigurationShellBlueprintProvider</c> reads, so it matches the
    /// composition in <c>shells.json</c> exactly without touching the registry.
    /// </summary>
    public static IReadOnlyList<string> ReadConfiguredShellNames(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration
            .GetSection(ConfiguredShellsSection)
            .GetChildren()
            .Select(child => child.Key.Trim())
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Resolves the concrete list of shells to activate. An empty list, or a list containing the
    /// <see cref="AllShellsMarker"/>, expands to every configured shell; otherwise the explicit names are used
    /// (de-duplicated, order preserved). Names are returned as given — activation itself surfaces an unknown name.
    /// </summary>
    public IReadOnlyList<string> ResolveTargetShellNames(IReadOnlyList<string> configuredShellNames)
    {
        ArgumentNullException.ThrowIfNull(configuredShellNames);

        var wantsAll = Shells.Count == 0 || Shells.Any(name => name == AllShellsMarker);
        if (wantsAll)
            return configuredShellNames;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(Shells.Count);
        foreach (var name in Shells)
        {
            if (seen.Add(name))
                result.Add(name);
        }

        return result;
    }
}
