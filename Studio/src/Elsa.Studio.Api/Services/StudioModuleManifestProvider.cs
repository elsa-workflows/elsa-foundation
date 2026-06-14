using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Models;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Api.Services;

public sealed class StudioModuleManifestProvider(
    IEnumerable<IStudioEventHandler<OnStudioModuleManifestsCollecting>> handlers,
    IBackendCapabilityProvider backendCapabilityProvider,
    IOptions<StudioApiOptions> options) : IStudioModuleManifestProvider
{
    public async Task<StudioModulesResponse> GetModules(CancellationToken cancellationToken)
    {
        var collection = new OnStudioModuleManifestsCollecting();

        foreach (var handler in handlers)
            await handler.Handle(collection, cancellationToken);

        var studioOptions = options.Value;
        var backendCapabilityIds = await backendCapabilityProvider.GetCapabilityIdsAsync(cancellationToken);
        var modules = new List<StudioModuleManifest>();
        var diagnostics = new List<StudioModuleDiagnostic>();

        foreach (var manifest in collection.Manifests.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            var moduleMode = GetModuleMode(studioOptions, manifest.Id);

            if (moduleMode == StudioModuleMode.Disabled)
            {
                diagnostics.Add(new StudioModuleDiagnostic(
                    manifest.Id,
                    StudioModuleDiagnosticStatuses.Disabled,
                    "Module disabled by host configuration."));
                continue;
            }

            var missingBackendCapabilities = manifest.RequiredBackendCapabilities
                .Where(x => !backendCapabilityIds.Contains(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (missingBackendCapabilities.Length > 0)
            {
                var reason = $"Missing backend capabilities: {string.Join(", ", missingBackendCapabilities)}.";
                diagnostics.Add(new StudioModuleDiagnostic(
                    manifest.Id,
                    moduleMode == StudioModuleMode.Enabled
                        ? StudioModuleDiagnosticStatuses.Incompatible
                        : StudioModuleDiagnosticStatuses.MissingBackendCapability,
                    reason));
                continue;
            }

            modules.Add(manifest);
            diagnostics.Add(new StudioModuleDiagnostic(
                manifest.Id,
                StudioModuleDiagnosticStatuses.Available,
                "Module manifest accepted."));
        }

        return new StudioModulesResponse(studioOptions.HostVersion, studioOptions.SdkVersion, modules, diagnostics);
    }

    private static StudioModuleMode GetModuleMode(StudioApiOptions options, string moduleId)
    {
        if (options.DisabledModuleIds.Contains(moduleId))
            return StudioModuleMode.Disabled;

        if (!options.Modules.TryGetValue(moduleId, out var configuredMode))
            return StudioModuleMode.Auto;

        return configuredMode.Trim().ToLowerInvariant() switch
        {
            "true" or "enabled" or "enable" or "on" => StudioModuleMode.Enabled,
            "false" or "disabled" or "disable" or "off" => StudioModuleMode.Disabled,
            _ => StudioModuleMode.Auto
        };
    }

    private enum StudioModuleMode
    {
        Auto,
        Enabled,
        Disabled
    }
}
