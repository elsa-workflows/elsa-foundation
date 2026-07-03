using ConsoleLogStreaming.AspNetCore;
using ConsoleLogStreaming.Core.Capture;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ConsoleLogOptions = ConsoleLogStreaming.Core.Options.ConsoleLogOptions;

namespace Elsa.Diagnostics.ConsoleLogStreaming;

/// <summary>
/// Captures process console output and exposes recent history, known sources, and a live SignalR stream.
/// </summary>
/// <remarks>
/// Console capture is a process-global concern (a static tee on <see cref="System.Console.Out"/>), and the live
/// stream is a long-lived SignalR connection. Both are therefore hosted once on the application root (see the
/// composition in <c>Elsa.Server/Program.cs</c>) rather than inside a shell container — a shell-hosted hub would
/// capture the shell's <see cref="IServiceScopeFactory"/> and throw <see cref="ObjectDisposedException"/> on
/// disconnect when that shell is recycled on feature enable. The host owns the whole stack: the console hook, the
/// capture options (<see cref="ConfigureHost"/>), and the recent/sources/hub endpoints (<see cref="ConfigureEndpoints"/>).
/// This shell feature is purely the enable/discover surface and registers nothing in the shell container.
/// <para>
/// Two consequences of host-level composition: (1) because everything is wired once at startup, enabling the feature
/// at runtime takes effect only after a process restart; and (2) capture is a single process-wide tee, so in a
/// multi-shell host the recent/sources/hub endpoints live at one fixed root path and surface every shell's console
/// output unscoped — stdout cannot be partitioned per shell.
/// </para>
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Observability")]
[ShellFeature(
    name: "DiagnosticsConsoleLogStreaming",
    DisplayName = "Diagnostics: Console Log Streaming",
    Description = "Streams process console output over HTTP and SignalR for diagnostics views."
)]
public sealed class ConsoleLogStreamingFeature : IShellFeature
{
    /// <summary>Feature name used in shells.json feature configuration. Exposed for tests (§2.23.3).</summary>
    public const string FeatureName = "DiagnosticsConsoleLogStreaming";
    private const string DefaultEndpointPrefix = "/_elsa/server/diagnostics/console-logs";

    /// <summary>Default route for recent console log entries (host-mapped).</summary>
    public const string DefaultRecentPath = DefaultEndpointPrefix + "/recent";

    /// <summary>Default route for known console log sources (host-mapped).</summary>
    public const string DefaultSourcesPath = DefaultEndpointPrefix + "/sources";

    /// <summary>Default route for the live console log SignalR hub (host-mapped).</summary>
    public const string DefaultHubPath = DefaultEndpointPrefix + "/hub";

    private static readonly object ConsoleStreamHookLock = new();
    private static bool _consoleStreamHookInstalled;

    /// <summary>
    /// Installs the console stream hook before the host builder is created when the feature is enabled in shells.json.
    /// </summary>
    public static void InstallConsoleStreamHookIfEnabled(string[] args) =>
        InstallConsoleStreamHookIfEnabled(args, ConsoleStreamHook.Install);

    /// <summary>
    /// Installs the process-wide console stream hook unconditionally. The host calls this once it has confirmed the
    /// feature is enabled (see <see cref="IsFeatureEnabled(IConfiguration)"/>); installation is idempotent.
    /// </summary>
    public static void InstallConsoleStreamHook() => InstallConsoleStreamHook(ConsoleStreamHook.Install);

    /// <summary>Install seam for tests: injects the hook install action (§2.23.3).</summary>
    public static void InstallConsoleStreamHookIfEnabled(string[] args, Action install)
    {
        if (!IsFeatureEnabled(ResolveShellsJsonPath(args), args))
            return;

        InstallConsoleStreamHook(install);
    }

    /// <summary>Install seam for tests: injects the hook install action (§2.23.3).</summary>
    public static void InstallConsoleStreamHookIfEnabled(IConfiguration configuration, Action install)
    {
        if (!IsFeatureEnabled(configuration))
            return;

        InstallConsoleStreamHook(install);
    }

    /// <summary>Resets the process-wide hook-installed flag so tests can exercise installation repeatedly (§2.23.3).</summary>
    public static void ResetConsoleStreamHookInstallStateForTests()
    {
        lock (ConsoleStreamHookLock)
            _consoleStreamHookInstalled = false;
    }

    /// <summary>
    /// Configures the process-global console capture host. This is the single source of capture options; the host
    /// composes it once at the application root.
    /// </summary>
    public static void ConfigureHost(ConsoleLogOptions options)
    {
        options.ServiceName = "elsa-server";
        options.SourceDisplayName = "Elsa.Server";
        options.RecentCapacity = 2_000;
        options.MaxRecentQuerySize = 2_000;
        options.PreserveAnsi = true;
    }

    /// <summary>
    /// Configures the host-mapped console log endpoints (recent/sources HTTP routes and the live SignalR hub).
    /// </summary>
    public static void ConfigureEndpoints(ConsoleLogStreamingAspNetCoreOptions options)
    {
        options.RecentPath = DefaultRecentPath;
        options.SourcesPath = DefaultSourcesPath;
        options.HubPath = DefaultHubPath;
    }

    /// <summary>
    /// Indicates whether the console log streaming feature is enabled in any shell in the given configuration. The
    /// host uses this to decide whether to install the console hook and compose the root-hosted endpoints + hub.
    /// </summary>
    public static bool IsFeatureEnabled(IConfiguration configuration)
    {
        foreach (var shell in configuration.GetSection("CShells:Shells").GetChildren())
        {
            foreach (var feature in shell.GetSection("Features").GetChildren())
            {
                if (IsEnabledFeatureEntry(feature))
                    return true;
            }
        }

        return false;
    }

    private static bool IsEnabledFeatureEntry(IConfigurationSection feature)
    {
        if (string.Equals(feature.Key, FeatureName, StringComparison.OrdinalIgnoreCase))
            return !IsDisabledFeatureEntry(feature);

        if (string.Equals(feature.Value, FeatureName, StringComparison.OrdinalIgnoreCase))
            return true;

        var configuredName = GetChildValue(feature, "Name") ?? GetChildValue(feature, "Id");
        return string.Equals(configuredName, FeatureName, StringComparison.OrdinalIgnoreCase) &&
               !IsDisabledFeatureEntry(feature);
    }

    private static bool IsDisabledFeatureEntry(IConfigurationSection feature) =>
        string.Equals(feature.Value, bool.FalseString, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(GetChildValue(feature, "Enabled"), bool.FalseString, StringComparison.OrdinalIgnoreCase);

    private static string? GetChildValue(IConfigurationSection section, string key) =>
        section.GetChildren()
            .FirstOrDefault(child => string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    internal static bool IsFeatureEnabled(string shellsJsonPath) => IsFeatureEnabled(shellsJsonPath, []);

    internal static bool IsFeatureEnabled(string shellsJsonPath, string[] args)
    {
        if (!File.Exists(shellsJsonPath))
            return false;

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(shellsJsonPath, optional: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        return IsFeatureEnabled(configuration);
    }

    internal static string ResolveShellsJsonPath(string[] args)
    {
        var contentRoot = ResolveContentRootPath(args);
        return Path.Combine(contentRoot, "shells.json");
    }

    private static string ResolveContentRootPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--contentRoot", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--contentRootPath", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    return Path.GetFullPath(args[i + 1]);

                continue;
            }

            const string contentRootPrefix = "--contentRoot=";
            if (arg.StartsWith(contentRootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = arg[contentRootPrefix.Length..];
                if (!string.IsNullOrWhiteSpace(value))
                    return Path.GetFullPath(value);
            }

            const string contentRootPathPrefix = "--contentRootPath=";
            if (arg.StartsWith(contentRootPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = arg[contentRootPathPrefix.Length..];
                if (!string.IsNullOrWhiteSpace(value))
                    return Path.GetFullPath(value);
            }
        }

        return ResolveConfiguredContentRootPath() ?? Directory.GetCurrentDirectory();
    }

    private static string? ResolveConfiguredContentRootPath()
    {
        foreach (var variable in new[] { "ASPNETCORE_CONTENTROOT", "DOTNET_CONTENTROOT", "CONTENTROOT", "CONTENTROOTPATH" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
                return Path.GetFullPath(value);
        }

        return null;
    }

    // The entire stack (console hook, capture options, recent/sources endpoints, hub) is composed at the application
    // root (see Program.cs), so the feature contributes nothing to the shell container. Keeping this a no-op — rather
    // than installing the hook here — means a runtime feature-enable has no half-applied effect: nothing happens until
    // the next startup re-composes the host, which is the documented behaviour.
    public void ConfigureServices(IServiceCollection services)
    {
    }

    private static void InstallConsoleStreamHook(Action install)
    {
        lock (ConsoleStreamHookLock)
        {
            if (_consoleStreamHookInstalled)
                return;

            install();
            _consoleStreamHookInstalled = true;
        }
    }
}
