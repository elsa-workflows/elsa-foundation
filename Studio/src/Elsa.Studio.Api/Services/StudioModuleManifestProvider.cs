using System.Reflection;
using CShells;
using CShells.Features;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Models;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Core.Attributes;
using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Api.Services;

public sealed class StudioModuleManifestProvider(
    IEnumerable<IStudioEventHandler<OnStudioModuleManifestsCollecting>> handlers,
    IRuntimeFeatureCatalog runtimeFeatureCatalog,
    IServiceProvider serviceProvider,
    IOptions<StudioApiOptions> options) : IStudioModuleManifestProvider
{
    public async Task<StudioModulesResponse> GetModules(CancellationToken cancellationToken)
    {
        var result = await Collect(cancellationToken);
        return new StudioModulesResponse(
            result.Options.HostVersion,
            result.Options.SdkVersion,
            result.Modules,
            result.Diagnostics);
    }

    public async Task<StudioModuleRegistryResponse> GetModuleRegistry(CancellationToken cancellationToken)
    {
        var result = await Collect(cancellationToken);
        var diagnosticsByModule = result.Diagnostics
            .GroupBy(x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray() as IReadOnlyCollection<StudioModuleDiagnostic>, StringComparer.OrdinalIgnoreCase);

        var disabledIds = result.Options.DisabledModuleIds;
        var activeModules = result.Modules.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var registry = result.CollectedManifests
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(manifest =>
            {
                var disabled = disabledIds.Contains(manifest.Id) || !IsShellFeatureActive(manifest, result.ShellSettings);
                var diagnostics = diagnosticsByModule.GetValueOrDefault(manifest.Id, []);
                var status = disabled
                    ? StudioModuleDiagnosticStatuses.Disabled
                    : activeModules.ContainsKey(manifest.Id)
                        ? StudioModuleDiagnosticStatuses.Available
                        : diagnostics.FirstOrDefault()?.Status ?? "unknown";

                return new StudioModuleRegistryItem(
                    manifest.Id,
                    manifest.DisplayName,
                    GetSourceKind(manifest),
                    GetScope(manifest),
                    manifest.Version,
                    manifest.RequiredHostVersion,
                    manifest.RequiredSdkVersion,
                    GetCompatibility(manifest, result.Options),
                    status,
                    new StudioModuleRegistryManifest(manifest.Entry, manifest.Styles, manifest.Capabilities),
                    GetContributions(manifest),
                    diagnostics);
            })
            .ToArray();

        return new StudioModuleRegistryResponse(
            result.Options.HostVersion,
            result.Options.SdkVersion,
            DateTimeOffset.UtcNow,
            registry);
    }

    private async Task<CollectionResult> Collect(CancellationToken cancellationToken)
    {
        // Collect manifests from imperative handlers (backward compat).
        var collection = new OnStudioModuleManifestsCollecting();

        foreach (var handler in handlers)
            await handler.Handle(collection, cancellationToken);

        // Discover manifests from [StudioModule] attributes on feature classes.
        var handlerIds = new HashSet<string>(collection.Manifests.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var snapshot = await runtimeFeatureCatalog.GetSnapshotAsync(cancellationToken);

        foreach (var descriptor in snapshot.FeatureDescriptors)
        {
            if (descriptor.StartupType is not { } featureType)
                continue;

            var attr = featureType.GetCustomAttribute<StudioModuleAttribute>();
            if (attr is null)
                continue;

            var assemblyName = featureType.Assembly.GetName().Name ?? featureType.Assembly.FullName ?? "";
            var moduleId = assemblyName;

            // Skip if a handler already contributed a manifest with this ID.
            if (handlerIds.Contains(moduleId))
                continue;

            var entry = $"/_content/{assemblyName}/studio/modules/{attr.Slug}/module.js?v={attr.Version}";
            var styles = attr.HasStyles
                ? [$"/_content/{assemblyName}/studio/modules/{attr.Slug}/module.css?v={attr.Version}"]
                : Array.Empty<string>();

            collection.Manifests.Add(new StudioModuleManifest(
                moduleId,
                attr.DisplayName,
                attr.Version,
                entry,
                styles,
                attr.RequiredHostVersion,
                attr.RequiredSdkVersion,
                attr.Capabilities,
                descriptor.Id));

            handlerIds.Add(moduleId);
        }

        var studioOptions = options.Value;
        var shellSettings = serviceProvider.GetService<ShellSettings>();
        var modules = new List<StudioModuleManifest>();
        var diagnostics = new List<StudioModuleDiagnostic>();

        foreach (var manifest in collection.Manifests.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (studioOptions.DisabledModuleIds.Contains(manifest.Id))
            {
                diagnostics.Add(new StudioModuleDiagnostic(
                    manifest.Id,
                    StudioModuleDiagnosticStatuses.Disabled,
                    "Module disabled by host configuration."));
                continue;
            }

            if (!IsShellFeatureActive(manifest, shellSettings))
            {
                diagnostics.Add(new StudioModuleDiagnostic(
                    manifest.Id,
                    StudioModuleDiagnosticStatuses.Disabled,
                    $"Module owner feature '{manifest.ShellFeatureName}' is not enabled in the active shell."));
                continue;
            }

            modules.Add(manifest);
            diagnostics.Add(new StudioModuleDiagnostic(
                manifest.Id,
                StudioModuleDiagnosticStatuses.Available,
                "Module manifest accepted."));
        }

        return new CollectionResult(studioOptions, shellSettings, collection.Manifests.ToArray(), modules, diagnostics);
    }

    private static bool IsShellFeatureActive(StudioModuleManifest manifest, ShellSettings? shellSettings) =>
        string.IsNullOrWhiteSpace(manifest.ShellFeatureName) ||
        shellSettings is null ||
        shellSettings.EnabledFeatures.Contains(manifest.ShellFeatureName, StringComparer.OrdinalIgnoreCase);

    private static string GetSourceKind(StudioModuleManifest manifest)
    {
        if (manifest.Id.Contains(".Samples.", StringComparison.OrdinalIgnoreCase))
            return "sample";

        if (manifest.Entry.StartsWith("/_content/", StringComparison.OrdinalIgnoreCase))
            return "static-web-asset";

        return "built-in";
    }

    private static string GetScope(StudioModuleManifest manifest)
    {
        var capabilities = manifest.Capabilities;
        var hasFrontend = capabilities.Any(x => x is "navigation" or "routes" or "dashboard-widgets" or "panels" or "setting-editors" or "ai-surfaces" or "ai-context-providers" or "ai-prompt-actions" or "ai-proposal-renderers");
        var hasBackend = capabilities.Any(x => x is "http" or "signalr" or "console-stream");

        return (hasFrontend, hasBackend) switch
        {
            (true, true) => "full-stack",
            (true, false) => "frontend",
            (false, true) => "backend",
            _ => "unknown"
        };
    }

    private static string GetCompatibility(StudioModuleManifest manifest, StudioApiOptions options)
    {
        return IsMajorCompatible(manifest.RequiredHostVersion, options.HostVersion) &&
               IsMajorCompatible(manifest.RequiredSdkVersion, options.SdkVersion)
            ? "compatible"
            : "incompatible";
    }

    private static bool IsMajorCompatible(string range, string actual)
    {
        if (string.IsNullOrWhiteSpace(range) || range == "*" || range == actual)
            return true;

        return range.StartsWith('^') && GetMajor(range[1..]) == GetMajor(actual);
    }

    private static string GetMajor(string version) => version.Split('.')[0];

    private static IReadOnlyCollection<StudioModuleContributionSummary> GetContributions(StudioModuleManifest manifest)
    {
        return manifest.Capabilities
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(capability => new StudioModuleContributionSummary(
                capability,
                $"{manifest.Id}:{capability}",
                GetContributionLabel(capability),
                "active"))
            .ToArray();
    }

    private static string GetContributionLabel(string capability)
    {
        return capability switch
        {
            "dashboard-widgets" => "Dashboard widgets",
            "setting-editors" => "Setting editors",
            "console-stream" => "Console stream",
            "http" => "HTTP endpoints",
            "signalr" => "SignalR hub",
            "ai-capabilities" => "AI capabilities",
            "ai-context-providers" => "AI context providers",
            "ai-prompt-actions" => "AI prompt actions",
            "ai-proposal-renderers" => "AI proposal renderers",
            "ai-surfaces" => "AI surfaces",
            "ai-tools" => "AI tools",
            "weaver-workflows" => "Weaver workflows",
            _ => string.Join(' ', capability.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]))
        };
    }

    private sealed record CollectionResult(
        StudioApiOptions Options,
        ShellSettings? ShellSettings,
        IReadOnlyCollection<StudioModuleManifest> CollectedManifests,
        IReadOnlyCollection<StudioModuleManifest> Modules,
        IReadOnlyCollection<StudioModuleDiagnostic> Diagnostics);
}
