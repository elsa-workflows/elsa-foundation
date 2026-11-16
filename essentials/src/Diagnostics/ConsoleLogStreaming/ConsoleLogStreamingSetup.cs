using ConsoleLogStreaming.AspNetCore;
using ConsoleLogStreaming.Core.Capture;
using Microsoft.Extensions.Configuration;
using ConsoleLogOptions = ConsoleLogStreaming.Core.Options.ConsoleLogOptions;

namespace Elsa.Diagnostics.ConsoleLogStreaming;

/// <summary>
/// Host composition for the console log streaming subsystem: captures process console output and exposes recent
/// history, known sources, and a live SignalR stream.
/// </summary>
/// <remarks>
/// This is <em>not</em> a CShells shell feature. Console capture is a process-global concern (a static tee on
/// <see cref="System.Console.Out"/>) and the live stream is a long-lived SignalR connection, so the whole stack —
/// the console hook, the capture options (<see cref="ConfigureHost"/>), and the recent/sources/hub endpoints
/// (<see cref="ConfigureEndpoints"/>) — is composed once on the application root (see <c>Elsa.Workbench/Program.cs</c>),
/// never inside a shell container: a shell-hosted hub would capture the shell's <see cref="IServiceProvider"/> and
/// throw <see cref="ObjectDisposedException"/> on disconnect when that shell is recycled on feature enable. The host
/// gates that composition on a plain config switch, <see cref="EnabledConfigKey"/> (defaults to off). Because
/// composition happens once at startup, toggling the switch takes effect only after a process restart.
/// </remarks>
public static class ConsoleLogStreamingSetup
{
    /// <summary>Host config switch that enables the console log streaming subsystem. Defaults to off when absent.</summary>
    public const string EnabledConfigKey = "Elsa:Diagnostics:ConsoleLogStreaming:Enabled";

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
    /// Installs the console stream hook before the host builder is created, when the subsystem is enabled. The flag is
    /// read early from <c>appsettings[.{Environment}].json</c> + environment variables + command line (the host builder
    /// does not exist yet), mirroring the config sources <c>WebApplication.CreateBuilder</c> would apply.
    /// </summary>
    public static void InstallConsoleStreamHookIfEnabled(string[] args) =>
        InstallConsoleStreamHookIfEnabled(args, ConsoleStreamHook.Install);

    /// <summary>
    /// Installs the process-wide console stream hook unconditionally. The host calls this once it has confirmed the
    /// subsystem is enabled (see <see cref="IsEnabled(IConfiguration)"/>); installation is idempotent.
    /// </summary>
    public static void InstallConsoleStreamHook() => InstallConsoleStreamHook(ConsoleStreamHook.Install);

    /// <summary>Install seam for tests: injects the hook install action.</summary>
    public static void InstallConsoleStreamHookIfEnabled(string[] args, Action install)
    {
        if (!IsEnabled(BuildEarlyConfiguration(args)))
            return;

        InstallConsoleStreamHook(install);
    }

    /// <summary>Install seam for tests: injects the hook install action against an explicit configuration.</summary>
    public static void InstallConsoleStreamHookIfEnabled(IConfiguration configuration, Action install)
    {
        if (!IsEnabled(configuration))
            return;

        InstallConsoleStreamHook(install);
    }

    /// <summary>Resets the process-wide hook-installed flag so tests can exercise installation repeatedly.</summary>
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
    /// Indicates whether the console log streaming subsystem is enabled in the given configuration. Defaults to off
    /// when the switch is absent; the host uses this to decide whether to install the console hook and compose the
    /// root-hosted endpoints + hub.
    /// </summary>
    public static bool IsEnabled(IConfiguration configuration) =>
        bool.TryParse(configuration[EnabledConfigKey], out var enabled) && enabled;

    private static IConfiguration BuildEarlyConfiguration(string[] args)
    {
        var contentRoot = ResolveContentRootPath(args);
        var environment = ResolveEnvironmentName(args);
        return new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
    }

    private static string ResolveEnvironmentName(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--environment", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1];

            const string environmentPrefix = "--environment=";
            if (arg.StartsWith(environmentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = arg[environmentPrefix.Length..];
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Production";
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
