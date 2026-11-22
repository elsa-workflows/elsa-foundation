using ConsoleLogStreaming.Core.Capture;
using Microsoft.Extensions.Configuration;

namespace Elsa.Studio.ConsoleStream;

/// <summary>
/// Installs the console stream capture hook before the host builder is created so that, when the
/// <see cref="ConsoleStreamStudioFeature"/> is enabled in shell configuration, <em>all</em> process
/// stdout is captured from startup instead of only from the point the feature is activated.
/// </summary>
public static class ConsoleStreamHookInstaller
{
    internal const string FeatureName = "ConsoleStream";

    private static readonly object ConsoleStreamHookLock = new();
    private static bool _consoleStreamHookInstalled;

    /// <summary>
    /// Installs the console stream hook before the host builder is created when the feature is enabled in shells.json.
    /// </summary>
    public static void InstallConsoleStreamHookIfEnabled(string[] args) =>
        InstallConsoleStreamHookIfEnabled(args, ConsoleStreamHook.Install);

    /// <summary>
    /// Installs the console stream hook during startup when the feature is enabled in shell configuration.
    /// </summary>
    public static void InstallConsoleStreamHookIfEnabled(IConfiguration configuration) =>
        InstallConsoleStreamHookIfEnabled(configuration, ConsoleStreamHook.Install);

    internal static void InstallConsoleStreamHookIfEnabled(string[] args, Action install)
    {
        if (!IsFeatureEnabled(ResolveShellsJsonPath(args), args))
            return;

        InstallConsoleStreamHook(install);
    }

    internal static void InstallConsoleStreamHookIfEnabled(IConfiguration configuration, Action install)
    {
        if (!IsFeatureEnabled(configuration))
            return;

        InstallConsoleStreamHook(install);
    }

    internal static void ResetConsoleStreamHookInstallStateForTests()
    {
        lock (ConsoleStreamHookLock)
            _consoleStreamHookInstalled = false;
    }

    internal static bool IsFeatureEnabled(IConfiguration configuration)
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
