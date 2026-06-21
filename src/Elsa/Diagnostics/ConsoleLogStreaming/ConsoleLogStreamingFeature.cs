using ConsoleLogStreaming.AspNetCore.DependencyInjection;
using ConsoleLogStreaming.Core.Capture;
using ConsoleLogStreaming.Core.DependencyInjection;
using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace Elsa.Diagnostics.ConsoleLogStreaming;

/// <summary>
/// Captures process console output and exposes recent history, known sources, and a live SignalR stream.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Observability")]
[ShellFeature(
    name: "DiagnosticsConsoleLogStreaming",
    DisplayName = "Diagnostics: Console Log Streaming",
    Description = "Streams process console output over HTTP and SignalR for diagnostics views."
)]
public sealed class ConsoleLogStreamingFeature : IWebShellFeature
{
    internal const string FeatureName = "DiagnosticsConsoleLogStreaming";
    private const string DefaultEndpointPrefix = "/_elsa/server/diagnostics/console-logs";
    private static readonly object ConsoleStreamHookLock = new();
    private static bool _consoleStreamHookInstalled;

    [ManifestSetting(DisplayName = "Service name", Description = "Identifies the local console log source.", Category = "Diagnostics", DefaultValue = "elsa-server")]
    public string ServiceName { get; set; } = "elsa-server";

    [ManifestSetting(DisplayName = "Source display name", Description = "UI label for the local console log source.", Category = "Diagnostics", DefaultValue = "Elsa.Server")]
    public string SourceDisplayName { get; set; } = "Elsa.Server";

    [ManifestSetting(DisplayName = "Recent capacity", Description = "Maximum number of recent console log entries retained in memory.", Category = "Diagnostics", DefaultValue = "2000")]
    public int RecentCapacity { get; set; } = 2_000;

    [ManifestSetting(DisplayName = "Max recent query size", Description = "Upper clamp applied to a recent-history query result size.", Category = "Diagnostics", DefaultValue = "2000")]
    public int MaxRecentQuerySize { get; set; } = 2_000;

    [ManifestSetting(DisplayName = "Preserve ANSI", Description = "Preserve ANSI escape sequences in captured console output.", Category = "Diagnostics", DefaultValue = "true")]
    public bool PreserveAnsi { get; set; } = true;

    [ManifestSetting(DisplayName = "Endpoint prefix", Description = "Base route used when explicit endpoint paths are not configured.", Category = "Diagnostics", DefaultValue = DefaultEndpointPrefix)]
    public string EndpointPrefix { get; set; } = DefaultEndpointPrefix;

    [ManifestSetting(DisplayName = "Recent path", Description = "Optional explicit route for recent console log entries.", Category = "Diagnostics")]
    public string? RecentPath { get; set; }

    [ManifestSetting(DisplayName = "Sources path", Description = "Optional explicit route for known console log sources.", Category = "Diagnostics")]
    public string? SourcesPath { get; set; }

    [ManifestSetting(DisplayName = "Hub path", Description = "Optional explicit route for the live console log SignalR hub.", Category = "Diagnostics")]
    public string? HubPath { get; set; }

    /// <summary>
    /// Installs the console stream hook before the host builder is created when the feature is enabled in shells.json.
    /// </summary>
    public static void InstallConsoleStreamHookIfEnabled(string? shellsJsonPath = null) =>
        InstallConsoleStreamHookIfEnabled(shellsJsonPath, ConsoleStreamHook.Install);

    internal static void InstallConsoleStreamHookIfEnabled(string? shellsJsonPath, Action install)
    {
        if (!IsFeatureEnabled(shellsJsonPath ?? "shells.json"))
            return;

        lock (ConsoleStreamHookLock)
        {
            if (_consoleStreamHookInstalled)
                return;

            install();
            _consoleStreamHookInstalled = true;
        }
    }

    internal static void ResetConsoleStreamHookInstallStateForTests()
    {
        lock (ConsoleStreamHookLock)
            _consoleStreamHookInstalled = false;
    }

    internal static bool IsFeatureEnabled(string shellsJsonPath)
    {
        if (!File.Exists(shellsJsonPath))
            return false;

        using var document = JsonDocument.Parse(File.ReadAllBytes(shellsJsonPath));
        if (!TryGetProperty(document.RootElement, "CShells", out var cShells) ||
            !TryGetProperty(cShells, "Shells", out var shells))
            return false;

        foreach (var shell in shells.EnumerateObject())
        {
            if (TryGetProperty(shell.Value, "Features", out var features) &&
                TryGetProperty(features, FeatureName, out _))
                return true;
        }

        return false;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddConsoleLogStreamingHost(options =>
        {
            options.ServiceName = ServiceName;
            options.SourceDisplayName = SourceDisplayName;
            options.RecentCapacity = RecentCapacity;
            options.MaxRecentQuerySize = MaxRecentQuerySize;
            options.PreserveAnsi = PreserveAnsi;
        });

        services.AddConsoleLogStreamingAspNetCore(options =>
        {
            options.RecentPath = ResolvePath(RecentPath, "recent");
            options.SourcesPath = ResolvePath(SourcesPath, "sources");
            options.HubPath = ResolvePath(HubPath, "hub");
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => endpoints.MapConsoleLogStreaming();

    private string ResolvePath(string? configuredPath, string segment)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return EnsureLeadingSlash(configuredPath.Trim());

        var prefix = NormalizeEndpointPrefix(EndpointPrefix);
        return prefix.Length == 0 ? $"/{segment}" : $"{prefix}/{segment}";
    }

    private static string NormalizeEndpointPrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return string.Empty;

        return EnsureLeadingSlash(prefix.Trim()).TrimEnd('/');
    }

    private static string EnsureLeadingSlash(string path) => path.StartsWith('/') ? path : $"/{path}";

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
            return true;

        value = default;
        return false;
    }
}
