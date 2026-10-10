using Elsa.Cli.Worker;
using Microsoft.Extensions.Configuration;

namespace Elsa.Cli;

/// <summary>
/// The host's shell feature configuration, read the way the host itself reads it (FR-035, FR-038):
/// <c>shells.json</c> in the <c>--host</c> directory, then <c>shells.&lt;environment&gt;.json</c> layered on
/// top when that file exists, through the same JSON configuration providers a host composes at startup.
/// </summary>
/// <remarks>
/// <para>
/// Only <c>Features</c> is read, and of each feature only its name and its <c>Provider</c> setting. That is
/// deliberate and is the whole defense here: shell configuration is where a host's connection strings live,
/// so nothing that could be one is ever lifted out of the file — not into the request that travels to the
/// worker, not into a message, not into the manifest. Every refusal below names a file, a shell, or a
/// feature, never a configured value other than <c>Provider</c>.
/// </para>
/// <para>
/// The environment is the <c>--environment</c> value, never this process's own
/// <c>ASPNETCORE_ENVIRONMENT</c>/<c>DOTNET_ENVIRONMENT</c>: the tool's environment is not the host's, and
/// the overlay a host would read is decided by the host's environment alone (FR-038).
/// </para>
/// <para>
/// The two <c>Features</c> shapes CShells accepts are both supported — an object map keyed by feature name,
/// and an array of names or of <c>{ Name, ... }</c> entries — because a host that authored the shape this
/// tool did not read would silently present as "no feature enabled", which is the one failure mode a check
/// like this must not have. A section mixing both shapes is refused here exactly as CShells refuses it.
/// </para>
/// </remarks>
public sealed class ShellConfiguration
{
    public const string BaseFileName = "shells.json";
    private const string ShellsSection = "CShells:Shells";
    private const string FeaturesSection = "Features";
    private const string NameKey = "Name";
    private const string SettingsKey = "Settings";
    private const string ProviderKey = "Provider";

    private readonly IReadOnlyList<WorkerShellFeature> enabled;

    private ShellConfiguration(IReadOnlyList<string> shellNames, IReadOnlyList<WorkerShellFeature> enabled)
    {
        ShellNames = shellNames;
        this.enabled = enabled;
    }

    /// <summary>The <c>shells.&lt;environment&gt;.json</c> overlay's file name for <paramref name="environment"/>.</summary>
    public static string OverlayFileName(string environment) => $"shells.{environment}.json";

    /// <summary>Every shell the configuration declares, in the order the configuration lists them.</summary>
    public IReadOnlyList<string> ShellNames { get; }

    /// <summary>
    /// Reads the host's shell configuration, or <c>null</c> when neither the base file nor the overlay is
    /// present — the one case FR-039 records as <c>providerAgreement: not-checked</c>.
    /// </summary>
    public static ShellConfiguration? Read(string hostDirectory, string environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);

        var basePath = Path.Join(hostDirectory, BaseFileName);
        var overlayPath = Path.Join(hostDirectory, OverlayFileName(environment));
        if (!File.Exists(basePath) && !File.Exists(overlayPath))
            return null;

        IConfigurationRoot configuration;
        try
        {
            configuration = new ConfigurationBuilder()
                .AddJsonFile(basePath, optional: true, reloadOnChange: false)
                .AddJsonFile(overlayPath, optional: true, reloadOnChange: false)
                .Build();
        }
        // The provider reports a malformed document without quoting its contents, and a duplicated key by
        // key path alone, so neither message can carry a configured value.
        catch (Exception failure) when (failure is FormatException or InvalidDataException or IOException)
        {
            throw CliRefusal.Resolution(
                "shells-configuration-unreadable",
                $"The host's shell configuration beside '{hostDirectory}' could not be read: {failure.Message}");
        }

        var shells = configuration.GetSection(ShellsSection).GetChildren().ToArray();
        return new(
            [.. shells.Select(shell => shell.Key)],
            [.. shells.SelectMany(shell => EnabledIn(shell))]);
    }

    /// <summary>
    /// The enabled features to compare, narrowed to <paramref name="shell"/> when one was named. A named
    /// shell the configuration does not declare is refused rather than silently matching nothing, which
    /// would report a check as run against a shell that does not exist.
    /// </summary>
    public IReadOnlyList<WorkerShellFeature> EnabledFeatures(string? shell)
    {
        if (string.IsNullOrWhiteSpace(shell))
            return enabled;

        if (!ShellNames.Contains(shell, StringComparer.OrdinalIgnoreCase))
        {
            throw CliRefusal.Resolution(
                "unknown-shell",
                $"--shell '{shell}' is not a shell this host configures.",
                [.. ShellNames.Select(name => $"'{name}' is configured.").DefaultIfEmpty("This host configures no shell at all.")]);
        }

        return [.. enabled.Where(feature => string.Equals(feature.Shell, shell, StringComparison.OrdinalIgnoreCase))];
    }

    private static IEnumerable<WorkerShellFeature> EnabledIn(IConfigurationSection shell)
    {
        var features = shell.GetSection(FeaturesSection).GetChildren().ToArray();
        if (features.Length == 0)
            return [];

        var numeric = features.Count(feature => int.TryParse(feature.Key, out _));
        if (numeric > 0 && numeric < features.Length)
        {
            throw CliRefusal.Resolution(
                "shells-configuration-invalid",
                $"Shell '{shell.Key}' has a 'Features' section that mixes array and object-map children, which CShells itself refuses. " +
                "Use either array syntax or object-map syntax, not both.");
        }

        IEnumerable<WorkerShellFeature?> entries = numeric == features.Length
            ? features.Select(feature => (WorkerShellFeature?)FromArrayEntry(shell.Key, feature))
            : features.Select(feature => FromObjectMapEntry(shell.Key, feature));
        return Deduplicate(shell.Key, [.. entries.OfType<WorkerShellFeature>()]);
    }

    /// <summary>
    /// The object-map shape: <c>"Feature": {}</c>, <c>"Feature": { "Provider": "…" }</c>, or a
    /// <c>true</c>/<c>false</c> scalar. <c>false</c> disables the feature, which is why the comparison never
    /// sees it; any other scalar is what CShells refuses, and so is refused here rather than read as enabled.
    /// </summary>
    private static WorkerShellFeature? FromObjectMapEntry(string shell, IConfigurationSection feature)
    {
        if (feature.Value is not null)
        {
            if (bool.TryParse(feature.Value, out var enabled))
                return enabled ? new() { Shell = shell, Feature = Named(shell, feature.Key) } : null;

            throw CliRefusal.Resolution(
                "shells-configuration-invalid",
                $"Feature '{feature.Key}' in shell '{shell}' must be true, false, a 'true'/'false' string, or an object.");
        }

        return new() { Shell = shell, Feature = Named(shell, feature.Key), Provider = feature[ProviderKey] };
    }

    /// <summary>
    /// The array shape: a bare feature name, or an object carrying <c>Name</c> plus either direct settings
    /// or a <c>Settings</c> wrapper. CShells has no disabled form here, so every entry is enabled.
    /// </summary>
    private static WorkerShellFeature FromArrayEntry(string shell, IConfigurationSection feature)
    {
        if (feature.Value is { } bare)
            return new() { Shell = shell, Feature = Named(shell, bare) };

        var name = feature[NameKey];
        if (string.IsNullOrWhiteSpace(name))
        {
            throw CliRefusal.Resolution(
                "shells-configuration-invalid",
                $"Feature array entry '{feature.Key}' in shell '{shell}' must define a non-empty 'Name' property.");
        }

        var wrapper = feature.GetSection(SettingsKey);
        return new()
        {
            Shell = shell,
            Feature = name.Trim(),
            Provider = wrapper.GetChildren().Any() ? wrapper[ProviderKey] : feature[ProviderKey]
        };
    }

    private static string Named(string shell, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw CliRefusal.Resolution("shells-configuration-invalid", $"Shell '{shell}' declares a feature with no name.");
        return name.Trim();
    }

    private static IReadOnlyList<WorkerShellFeature> Deduplicate(string shell, IReadOnlyList<WorkerShellFeature> features)
    {
        var duplicates = features
            .GroupBy(feature => feature.Feature, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"'{group.Key}' is configured {group.Count()} times.")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw CliRefusal.Resolution(
                "shells-configuration-invalid",
                $"Shell '{shell}' configures a feature more than once, which CShells itself refuses.",
                duplicates);
        }

        return features;
    }
}
